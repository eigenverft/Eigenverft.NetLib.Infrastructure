using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SerilogRelay.Tests
{
    [TestClass]
    public sealed class SerilogRelayHappyEyeballsTests
    {
        private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);

        [TestMethod]
        [DataRow("", "")]
        [DataRow("127.0.0.1,127.0.0.2", "127.0.0.1,127.0.0.2")]
        [DataRow("::1,::2,::3", "::1,::2,::3")]
        [DataRow("::1,::2,127.0.0.1,127.0.0.2", "::1,127.0.0.1,::2,127.0.0.2")]
        [DataRow("127.0.0.1,127.0.0.2,::1,::2,::3", "127.0.0.1,::1,127.0.0.2,::2,::3")]
        [DataRow("::1,::1,127.0.0.1,127.0.0.1", "::1,127.0.0.1")]
        [DataRow("::ffff:127.0.0.1,::1,127.0.0.1,127.0.0.2", "127.0.0.1,::1,127.0.0.2")]
        public void AddressesAlternateFamiliesInResolverOrderWithoutDuplicates(string input, string expected)
        {
            CollectionAssert.AreEqual(ParseAddresses(expected), HappyEyeballsConnector.Interleave(ParseAddresses(input)));
        }

        [TestMethod]
        public async Task PreferredConnectionWinsWithoutStartingAnAlternative()
        {
            var network = new ControlledNetwork("::1,127.0.0.1");
            Task<Stream> connection = network.Begin();
            Assert.AreEqual(1, network.Attempts.Count);
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), network.Delays[0].Duration);
            using var stream = new TrackedStream();
            network.Attempts[0].Completion.SetResult(stream);
            Assert.AreSame(stream, await connection.WaitAsync(_testTimeout));
            Assert.AreEqual(1, network.Attempts.Count);
            Assert.IsFalse(stream.IsDisposed);
            Assert.IsTrue(network.Attempts[0].Token.IsCancellationRequested);
        }

        [TestMethod]
        [DataRow("::1,127.0.0.1")]
        [DataRow("127.0.0.1,::1")]
        public async Task HangingPreferredFamilyAllowsOtherFamilyAfterDelay(string addresses)
        {
            var network = new ControlledNetwork(addresses);
            Task<Stream> connection = network.Begin();
            Assert.AreEqual(1, network.Attempts.Count);
            network.Delays[0].Completion.SetResult();
            await network.WaitForAttemptsAsync(2);
            Assert.AreNotEqual(network.Attempts[0].Address.AddressFamily, network.Attempts[1].Address.AddressFamily);
            Assert.IsFalse(network.Attempts[0].Token.IsCancellationRequested);
            using var winner = new TrackedStream();
            network.Attempts[1].Completion.SetResult(winner);
            Assert.AreSame(winner, await connection.WaitAsync(_testTimeout));
            Assert.IsTrue(network.Attempts[0].Token.IsCancellationRequested);
            using var late = new TrackedStream();
            network.Attempts[0].Completion.SetResult(late);
            await late.Disposed.Task.WaitAsync(_testTimeout);
            Assert.IsFalse(winner.IsDisposed);
        }

        [TestMethod]
        public async Task ImmediateFailureStartsNextAddressWithoutWaitingForDelay()
        {
            var network = new ControlledNetwork("::1,127.0.0.1");
            Task<Stream> connection = network.Begin();
            network.Attempts[0].Completion.SetException(new SocketException((int)SocketError.ConnectionRefused));
            await network.WaitForAttemptsAsync(2);
            Assert.IsFalse(network.Delays[0].Completion.Task.IsCompleted);
            using var winner = new TrackedStream();
            network.Attempts[1].Completion.SetResult(winner);
            Assert.AreSame(winner, await connection.WaitAsync(_testTimeout));
        }

        [TestMethod]
        public async Task EarlierPendingAttemptCanWinAfterLaterAddressesFail()
        {
            var network = new ControlledNetwork("::1,::2,127.0.0.1");
            Task<Stream> connection = network.Begin();
            network.Delays[0].Completion.SetResult();
            await network.WaitForAttemptsAsync(2);
            network.Attempts[1].Completion.SetException(new IOException("refused"));
            await network.WaitForAttemptsAsync(3);
            network.Attempts[2].Completion.SetException(new IOException("also refused"));
            using var winner = new TrackedStream();
            network.Attempts[0].Completion.SetResult(winner);
            Assert.AreSame(winner, await connection.WaitAsync(_testTimeout));
        }

        [TestMethod]
        public async Task AdditionalAddressesAreStaggeredAndEverySuccessfulLoserIsDisposed()
        {
            var network = new ControlledNetwork("::1,::2,127.0.0.1,127.0.0.2");
            Task<Stream> connection = network.Begin();
            network.Delays[0].Completion.SetResult();
            await network.WaitForAttemptsAsync(2);
            network.Delays[1].Completion.SetResult();
            await network.WaitForAttemptsAsync(3);
            using var winner = new TrackedStream();
            network.Attempts[0].Completion.SetResult(winner);
            Assert.AreSame(winner, await connection.WaitAsync(_testTimeout));
            Assert.AreEqual(3, network.Attempts.Count);
            using var loser = new TrackedStream();
            network.Attempts[1].Completion.SetResult(loser);
            network.Attempts[2].Completion.SetCanceled();
            await loser.Disposed.Task.WaitAsync(_testTimeout);
            Assert.IsTrue(network.Attempts[2].Token.IsCancellationRequested);
        }

        [TestMethod]
        public async Task SimultaneousSuccessKeepsExactlyOneConnection()
        {
            var network = new ControlledNetwork("::1,127.0.0.1");
            Task<Stream> connection = network.Begin();
            network.Delays[0].Completion.SetResult();
            await network.WaitForAttemptsAsync(2);
            using var first = new TrackedStream();
            using var second = new TrackedStream();
            network.Attempts[0].Completion.SetResult(first);
            network.Attempts[1].Completion.SetResult(second);
            Stream winner = await connection.WaitAsync(_testTimeout);
            TrackedStream loser = ReferenceEquals(winner, first) ? second : first;
            await loser.Disposed.Task.WaitAsync(_testTimeout);
            Assert.IsFalse(((TrackedStream)winner).IsDisposed);
        }

        [TestMethod]
        public async Task AllFailedAddressesSurfaceTheLastFailure()
        {
            var network = new ControlledNetwork("::1,127.0.0.1");
            Task<Stream> connection = network.Begin();
            network.Attempts[0].Completion.SetException(new IOException("first"));
            await network.WaitForAttemptsAsync(2);
            var last = new SocketException((int)SocketError.ConnectionRefused);
            network.Attempts[1].Completion.SetException(last);
            Assert.AreSame(last, await Assert.ThrowsExactlyAsync<SocketException>(() => connection.WaitAsync(_testTimeout)));
        }

        [TestMethod]
        public async Task EmptyResolutionAndDnsFailureDoNotStartSockets()
        {
            var network = new ControlledNetwork("");
            var empty = await Assert.ThrowsExactlyAsync<SocketException>(() => network.Begin());
            Assert.AreEqual(SocketError.HostNotFound, empty.SocketErrorCode);
            Assert.AreEqual(0, network.Attempts.Count);
            var failure = new SocketException((int)SocketError.TryAgain);
            var connector = new HappyEyeballsConnector((_, _) => Task.FromException<IPAddress[]>(failure),
                (_, _, _) => throw new AssertFailedException("DNS failed; no socket should be started."), Task.Delay);
            Assert.AreSame(failure, await Assert.ThrowsExactlyAsync<SocketException>(() =>
                connector.ConnectAsync(new DnsEndPoint("relay.test", 443), CancellationToken.None)));
        }

        [TestMethod]
        public async Task CancellationStopsWaitingAndDisposesALateConnection()
        {
            var network = new ControlledNetwork("::1,127.0.0.1");
            using var cancellation = new CancellationTokenSource();
            Task<Stream> connection = network.Begin(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => connection.WaitAsync(_testTimeout));
            Assert.AreEqual(1, network.Attempts.Count);
            Assert.IsTrue(network.Attempts[0].Token.IsCancellationRequested);
            using var late = new TrackedStream();
            network.Attempts[0].Completion.SetResult(late);
            await late.Disposed.Task.WaitAsync(_testTimeout);
        }

        [TestMethod]
        public async Task CancellationAfterResolutionPreventsFirstConnect()
        {
            var network = new ControlledNetwork("::1");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => network.Begin(cancellation.Token));
            Assert.AreEqual(0, network.Attempts.Count);
        }

        [TestMethod]
        [DataRow("127.0.0.1")]
        [DataRow("::1")]
        [DataRow("::ffff:127.0.0.1")]
        public async Task RealSocketsUseTheResolvedFamilyAndTransferBytes(string host)
        {
            using var deadline = new CancellationTokenSource(_testTimeout);
            IPAddress address = IPAddress.Parse(host);
            using var listener = new TcpListener(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<Socket> accepted = listener.AcceptSocketAsync(deadline.Token).AsTask();
            using Stream stream = await new HappyEyeballsConnector().ConnectAsync(new DnsEndPoint(host, port), deadline.Token);
            using Socket server = await accepted;
            Assert.AreEqual(listener.Server.AddressFamily, server.AddressFamily);
            Assert.IsTrue(((NetworkStream)stream).Socket.NoDelay);
            await stream.WriteAsync(new byte[] { 42 }, deadline.Token);
            var buffer = new byte[1];
            Assert.AreEqual(1, await server.ReceiveAsync(buffer, SocketFlags.None, deadline.Token));
            Assert.AreEqual(42, buffer[0]);
        }

        [TestMethod]
        public async Task RealConnectionRefusalAndCanceledDnsFinishWithoutLeakingSockets()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            await Assert.ThrowsExactlyAsync<SocketException>(() =>
                HappyEyeballsConnector.ConnectSocketAsync(IPAddress.Loopback, port, CancellationToken.None));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                HappyEyeballsConnector.ResolveAsync("localhost", cancellation.Token));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task BuiltInTransportPreservesTlsValidationAndHostname(bool dangerous)
        {
            using var deadline = new CancellationTokenSource(_testTimeout);
            using RSA key = RSA.Create(2048);
            var certificateRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            certificateRequest.CertificateExtensions.Add(names.Build());
            using X509Certificate2 generated = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            // Schannel needs an imported private key for the local TLS server.
#if NET8_0
            using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.DefaultKeySet);
#else
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.DefaultKeySet);
#endif
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<string?> served = ServeHttpsAsync(listener, certificate, dangerous, deadline.Token);
            using var client = new HttpClient(SerilogRelaySink.GetHttpClientHandler(dangerous), disposeHandler: false);
            string url = $"https://localhost:{port}/logs";
            try
            {
                if (dangerous)
                {
                    using HttpResponseMessage response = await client.GetAsync(url, deadline.Token);
                    Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
                }
                else
                {
                    HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url, deadline.Token));
                    Assert.IsInstanceOfType<AuthenticationException>(error.InnerException);
                }
            }
            finally
            {
                Assert.AreEqual("localhost", await served.WaitAsync(_testTimeout));
            }
        }

        private static async Task<string?> ServeHttpsAsync(TcpListener listener, X509Certificate2 certificate, bool dangerous, CancellationToken token)
        {
            using TcpClient peer = await listener.AcceptTcpClientAsync(token);
            using var ssl = new SslStream(peer.GetStream());
            string? serverName = null;
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificateSelectionCallback = (_, name) => { serverName = name; return certificate; },
                }, token);
                var buffer = new byte[8192];
                int received = await ssl.ReadAsync(buffer, token);
                Assert.IsTrue(Encoding.ASCII.GetString(buffer, 0, received).StartsWith("GET /logs HTTP/", StringComparison.Ordinal));
                await ssl.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"), token);
            }
            catch (AuthenticationException) when (!dangerous) { }
            catch (IOException) when (!dangerous) { }
            return serverName;
        }

        private static IPAddress[] ParseAddresses(string addresses)
            => addresses.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(IPAddress.Parse).ToArray();

        private sealed class ControlledNetwork(string addresses)
        {
            internal readonly List<Attempt> Attempts = [];
            internal readonly List<Delay> Delays = [];
            private readonly SemaphoreSlim _started = new(0);

            internal Task<Stream> Begin(CancellationToken token = default)
            {
                var connector = new HappyEyeballsConnector((_, _) => Task.FromResult(ParseAddresses(addresses)),
                    (address, port, cancellation) =>
                    {
                        Assert.AreEqual(443, port);
                        var attempt = new Attempt(address, cancellation);
                        lock (Attempts) { Attempts.Add(attempt); }
                        _started.Release();
                        return attempt.Completion.Task;
                    },
                    (duration, _) =>
                    {
                        var delay = new Delay(duration);
                        lock (Attempts) { Delays.Add(delay); }
                        _started.Release();
                        return delay.Completion.Task;
                    });
                return connector.ConnectAsync(new DnsEndPoint("relay.test", 443), token);
            }

            internal async Task WaitForAttemptsAsync(int count)
            {
                while (true)
                {
                    lock (Attempts)
                    {
                        if (Attempts.Count >= count && Delays.Count >= Math.Min(count, ParseAddresses(addresses).Length - 1))
                            return;
                    }
                    Assert.IsTrue(await _started.WaitAsync(_testTimeout), "Expected connection attempt did not start.");
                }
            }
        }

        private sealed class Attempt(IPAddress address, CancellationToken token)
        {
            internal IPAddress Address { get; } = address;
            internal CancellationToken Token { get; } = token;
            internal TaskCompletionSource<Stream> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class Delay(TimeSpan duration)
        {
            internal TimeSpan Duration { get; } = duration;
            internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class TrackedStream : MemoryStream
        {
            internal bool IsDisposed { get; private set; }
            internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
                Disposed.TrySetResult();
            }
        }
    }
}
