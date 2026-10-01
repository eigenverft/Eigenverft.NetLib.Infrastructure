using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SerilogRelay.Tests
{
    public sealed partial class SerilogRelayReliabilityTests
    {
        [TestMethod]
        public async Task ResponseDiscardStreamHonorsItsWriteOnlyContractAndExactByteLimit()
        {
            using Stream stream = CreateResponseDiscardStream(14, CancellationToken.None);
            Assert.IsFalse(stream.CanRead);
            Assert.IsFalse(stream.CanSeek);
            Assert.IsTrue(stream.CanWrite);
            Assert.ThrowsExactly<NotSupportedException>(() => _ = stream.Length);
            Assert.ThrowsExactly<NotSupportedException>(() => _ = stream.Position);
            Assert.ThrowsExactly<NotSupportedException>(() => stream.Position = 0);
            Assert.ThrowsExactly<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
            Assert.ThrowsExactly<NotSupportedException>(() => stream.SetLength(0));
            Assert.ThrowsExactly<NotSupportedException>(() => stream.Read(new byte[1], 0, 1));

            byte[] bytes = new byte[8];
            stream.Write(bytes, 1, 2);
            stream.Write(bytes.AsSpan(0, 3));
            await stream.WriteAsync(bytes, 0, 4, CancellationToken.None);
            await stream.WriteAsync(bytes.AsMemory(0, 5), CancellationToken.None);
            stream.Write(ReadOnlySpan<byte>.Empty);
            stream.Flush();
            Assert.ThrowsExactly<HttpRequestException>(() => stream.WriteByte(1));
        }

        [TestMethod]
        public async Task ResponseDiscardStreamHonorsBothItsLifetimeAndEachWriteCancellation()
        {
            using var lifetime = new CancellationTokenSource();
            using var writeCancellation = new CancellationTokenSource();
            using Stream stream = CreateResponseDiscardStream(10, lifetime.Token);
            byte[] bytes = new byte[1];
            writeCancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                async () => await stream.WriteAsync(bytes, 0, 1, writeCancellation.Token));
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                async () => await stream.WriteAsync(bytes.AsMemory(), writeCancellation.Token));
            lifetime.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(() => stream.Flush());
            Assert.ThrowsExactly<OperationCanceledException>(() => stream.Write(bytes, 0, 1));
        }

        [TestMethod]
        [DataRow(true, false)]
        [DataRow(false, false)]
        [DataRow(true, true)]
        [DataRow(false, true)]
        public async Task ResponseSizeLimitAcknowledgesOnlyCompleteBodiesWithinTheLimit(bool knownLength, bool oversized)
        {
            int length = oversized ? 9 : 8;
            int copiedBytes = 0;
            bool bodyCompleted = false;
            using var content = new StreamingResponseContent(async (stream, token) =>
            {
                await stream.WriteAsync(new byte[4].AsMemory(), token);
                copiedBytes += 4;
                await stream.WriteAsync(new byte[length - 4].AsMemory(), token);
                copiedBytes += length - 4;
                bodyCompleted = true;
            }, knownLength ? length : null);
            using var client = new HttpClient(new RegressionHttpHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })))
            {
                MaxResponseContentBufferSize = 8,
            };

            await RunResponseScenarioAsync(client, new SerilogRelayOptions(), async sink =>
            {
                bool delivered = await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, false);
                Assert.AreEqual(!oversized, delivered);
                Assert.AreEqual(!oversized, bodyCompleted);
                Assert.AreEqual(oversized ? (knownLength ? 0 : 4) : 8, copiedBytes);
                Assert.AreEqual(oversized ? 1L : 0L, GetUnsentCount(GetPrivateField<string>(sink, "_connectionString")));
                Assert.AreEqual(oversized ? 1 : 0, GetPrivateField<RetryGate>(sink, "_retryGate").ConsecutiveFailures);
                Assert.IsNull(GetClaimOwnerId(GetPrivateField<string>(sink, "_connectionString")));
            });
        }

        [TestMethod]
        public async Task ResponseHeadersDoNotAcknowledgeAnUnfinishedBody()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var content = new StreamingResponseContent(async (stream, token) =>
            {
                await stream.WriteAsync(new byte[4].AsMemory(), token);
                started.SetResult(true);
                await finish.Task.WaitAsync(token);
                await stream.WriteAsync(new byte[4].AsMemory(), token);
            });
            using var client = new HttpClient(new RegressionHttpHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })))
            {
                Timeout = Timeout.InfiniteTimeSpan,
                MaxResponseContentBufferSize = 8,
            };
            try
            {
                await RunResponseScenarioAsync(client, new SerilogRelayOptions(), async sink =>
                {
                    Task<bool> delivery = InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, false);
                    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.IsFalse(delivery.IsCompleted);
                    Assert.AreEqual(1L, GetUnsentCount(GetPrivateField<string>(sink, "_connectionString")));
                    Assert.AreEqual(0L, GetPrivateField<long>(sink, "_lastHttpSuccessUnixMs"));
                    finish.SetResult(true);
                    Assert.IsTrue(await delivery.WaitAsync(TimeSpan.FromSeconds(5)));
                    Assert.AreEqual(0L, GetUnsentCount(GetPrivateField<string>(sink, "_connectionString")));
                    Assert.IsGreaterThan(0L, GetPrivateField<long>(sink, "_lastHttpSuccessUnixMs"));
                });
            }
            finally { finish.TrySetResult(true); }
        }

        [TestMethod]
        [DataRow("client")]
        [DataRow("relay")]
        [DataRow("shutdown")]
        [DataRow("caller")]
        public async Task ResponseBodyRemainsBoundByEveryApplicableCancellation(string limit)
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var content = new StreamingResponseContent(async (stream, token) =>
            {
                await stream.WriteAsync(new byte[1].AsMemory(), token);
                started.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
            using var client = new HttpClient(new RegressionHttpHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })))
            {
                Timeout = limit == "client" ? TimeSpan.FromMilliseconds(250) : Timeout.InfiniteTimeSpan,
            };
            var options = new SerilogRelayOptions();
            if (limit == "relay")
                options.Delivery.RequestTimeout = TimeSpan.FromMilliseconds(250);
            options.Delivery.ShutdownRequestTimeout = TimeSpan.FromMilliseconds(250);
            using var caller = new CancellationTokenSource();
            await RunResponseScenarioAsync(client, options, async sink =>
            {
                Task<bool> delivery = InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, caller.Token, false);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (limit == "shutdown")
                    GetPrivateField<CancellationTokenSource>(sink, "_shutdownSignal").Cancel();
                if (limit == "caller")
                {
                    caller.Cancel();
                    await Assert.ThrowsAsync<OperationCanceledException>(async () => await delivery.WaitAsync(TimeSpan.FromSeconds(5)));
                }
                else
                {
                    Assert.IsFalse(await delivery.WaitAsync(TimeSpan.FromSeconds(5)));
                }
                Assert.AreEqual(1L, GetUnsentCount(GetPrivateField<string>(sink, "_connectionString")));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_lastHttpSuccessUnixMs"));
                Assert.AreEqual(limit == "caller" ? 0 : 1, GetPrivateField<RetryGate>(sink, "_retryGate").ConsecutiveFailures);
            });
        }

        [TestMethod]
        public async Task LargeGeneratedResponseDoesNotAllocateAWholeBodyBuffer()
        {
            const int bodyBytes = 64 * 1024 * 1024;
            byte[] chunk = new byte[4096];
            long allocatedBytes = 0;
            int copiedBytes = 0;
            using var content = new StreamingResponseContent(async (stream, token) =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                while (copiedBytes < bodyBytes)
                {
                    await stream.WriteAsync(chunk.AsMemory(), token);
                    copiedBytes += chunk.Length;
                }
                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            });
            using var client = new HttpClient(new RegressionHttpHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })))
            {
                MaxResponseContentBufferSize = bodyBytes,
            };
            await RunResponseScenarioAsync(client, new SerilogRelayOptions(), async sink =>
            {
                Assert.IsTrue(await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, false));
                Assert.AreEqual(bodyBytes, copiedBytes);
                Assert.IsLessThan(1024L * 1024L, allocatedBytes, "Discarding a 64 MiB body must not allocate a growing body buffer.");
            });
        }

        private static Stream CreateResponseDiscardStream(long maximumBytes, CancellationToken token)
        {
            Type type = typeof(SerilogRelaySink).GetNestedType("ResponseDiscardStream", BindingFlags.NonPublic)!;
            return (Stream)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: new object[] { maximumBytes, token }, culture: null)!;
        }

        private static async Task RunResponseScenarioAsync(HttpClient client, SerilogRelayOptions options, Func<SerilogRelaySink, Task> verify)
        {
            string directory = CreateTemporaryDirectory();
            SerilogRelaySink? sink = null;
            try
            {
                options.HttpClient = client;
                options.Delivery.MinimumBatchEvents = 1;
                sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "response.db")}", null, options);
                await StopRelayWorkers(sink);
                sink.Emit(CreateLogEvent("event awaiting a complete response"));
                SetPrivateField(sink, "_endpoint", "http://localhost/relay");
                await verify(sink);
            }
            finally
            {
                if (sink is not null)
                {
                    SetPrivateField(sink, "_endpoint", (string?)null);
                    await DisposeAndWaitForCleanupAsync(sink);
                }
                DeleteTemporaryDirectory(directory);
            }
        }

        private sealed class StreamingResponseContent : HttpContent
        {
            private readonly Func<Stream, CancellationToken, Task> _write;
            private readonly long? _length;

            internal StreamingResponseContent(Func<Stream, CancellationToken, Task> write, long? length = null)
            {
                _write = write;
                _length = length;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
                => _write(stream, CancellationToken.None);

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
                => _write(stream, cancellationToken);

            protected override bool TryComputeLength(out long length)
            {
                length = _length ?? 0;
                return _length.HasValue;
            }

            protected override Task<Stream> CreateContentReadStreamAsync()
                => throw new AssertFailedException("The response must be copied directly without materializing a read stream.");

            protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
                => throw new AssertFailedException("The response must be copied directly without materializing a read stream.");
        }
    }
}
