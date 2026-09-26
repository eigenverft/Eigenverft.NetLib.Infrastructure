using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Parsing;

namespace Eigenverft.NetLib.SerilogRelay.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class SerilogRelayReliabilityTests
    {
        [TestMethod]
        public void OptionsOverloadKeepsSimpleDurableUsage()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "options.db");

            try
            {
                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 2;
                options.Delivery.MaximumBatchEvents = 10;
                options.Delivery.PollInterval = TimeSpan.FromMilliseconds(50);
                options.Delivery.MaximumBatchWait = TimeSpan.FromMilliseconds(100);

                using (Logger logger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(
                        endpoint: null,
                        options: options,
                        spoolDirectory: directory,
                        spoolFileName: "options.db",
                        applicationId: "Options.Api.App")
                    .CreateLogger())
                {
                    logger.Information("options overload");
                }

                using var connection = new SqliteConnection($"Data Source={databasePath}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT COUNT(*) FROM SerilogRelayEvents WHERE Sent = 0 AND ApplicationId = 'Options.Api.App' AND RenderMessage = 'options overload';";
                Assert.AreEqual(
                    1L,
                    Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public void DefaultUnsentBacklogSurvivesRestartRegardlessOfAge()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");

            try
            {
                using (Logger logger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(
                        endpoint: null,
                        spoolDirectory: directory,
                        spoolFileName: "relay.db")
                    .CreateLogger())
                {
                    logger.Information("old unsent");
                }

                using (var connection = new SqliteConnection($"Data Source={databasePath}"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "UPDATE SerilogRelayEvents SET CreatedAt = datetime('now', '-30 days') WHERE Sent = 0;";
                    Assert.AreEqual(1, command.ExecuteNonQuery());
                }

                using (Logger logger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(
                        endpoint: null,
                        spoolDirectory: directory,
                        spoolFileName: "relay.db")
                    .CreateLogger())
                {
                }

                Assert.AreEqual(1L, GetUnsentCount($"Data Source={databasePath}"));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task LowVolumeBacklogSendsAfterMaximumBatchWait()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);

            try
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<string> requestTask = ReceiveSingleRequestAsync(listener, HttpStatusCode.OK);

                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 20;
                options.Delivery.MaximumBatchEvents = 100;
                options.Delivery.PollInterval = TimeSpan.FromSeconds(1);
                options.Delivery.MaximumBatchWait = TimeSpan.FromMilliseconds(200);
                options.Retry.JitterRatio = 0d;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{port}/logs",
                    options);

                sink.Emit(CreateLogEvent("low volume"));

                await Task.Delay(50);
                Assert.IsFalse(requestTask.IsCompleted);

                string body = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
                using JsonDocument document = JsonDocument.Parse(body);
                Assert.AreEqual(
                    "low volume",
                    document.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString());

                await WaitUntilAsync(
                    () => GetUnsentCount(connectionString) == 0,
                    TimeSpan.FromSeconds(5));
            }
            finally
            {
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task StartupBacklogGetsDeliveryChanceBeforeExplicitUnsentExpiry()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);

            try
            {
                await using (var writer = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    minBatchItems: 20,
                    maxBatchItems: 100,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromDays(1),
                    unsentRetention: null))
                {
                    writer.Emit(CreateLogEvent("startup backlog"));
                }

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "UPDATE SerilogRelayEvents SET CreatedAt = datetime('now', '-10 days') WHERE Sent = 0;";
                    Assert.AreEqual(1, command.ExecuteNonQuery());
                }

                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<string> requestTask = ReceiveSingleRequestAsync(listener, HttpStatusCode.OK);

                await using var reader = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{port}/logs",
                    minBatchItems: 20,
                    maxBatchItems: 100,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(1),
                    maximumBatchWait: TimeSpan.FromSeconds(10));

                string body = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
                using JsonDocument document = JsonDocument.Parse(body);
                Assert.AreEqual(
                    "startup backlog",
                    document.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString());
            }
            finally
            {
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task EndpointFailureKeepsHealthySpoolOutOfEmergencyPath()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            int closedPort = ReserveAndReleasePort();

            try
            {
                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 1;
                options.Delivery.MaximumBatchEvents = 10;
                options.Delivery.PollInterval = TimeSpan.FromMilliseconds(20);
                options.Delivery.MaximumBatchWait = TimeSpan.FromMilliseconds(20);
                options.Retry.InitialDelay = TimeSpan.FromMilliseconds(100);
                options.Retry.MaximumDelay = TimeSpan.FromMilliseconds(100);
                options.Retry.Multiplier = 1d;
                options.Retry.JitterRatio = 0d;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{closedPort}/logs",
                    options);

                sink.Emit(CreateLogEvent("endpoint down"));

                RetryGate gate = GetPrivateField<RetryGate>(sink, "_retryGate");
                await WaitUntilAsync(
                    () => gate.ConsecutiveFailures > 0,
                    TimeSpan.FromSeconds(5));

                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
                Assert.AreEqual(0, GetPrivateField<int>(sink, "_spoolUnavailable"));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task EmergencyPayloadBudgetDropsEventThatDoesNotFit()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var selfLog = new StringWriter(CultureInfo.InvariantCulture);
            SelfLog.Enable(selfLog);

            try
            {
                var options = new SerilogRelayOptions();
                options.Emergency.MaxBufferedEvents = 10;
                options.Emergency.MaxBufferedPayloadBytes = 1;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
CREATE TRIGGER fail_serilog_relay_insert
BEFORE INSERT ON SerilogRelayEvents
BEGIN
    SELECT RAISE(ABORT, 'simulated write failure');
END;";
                    command.ExecuteNonQuery();
                }

                sink.Emit(CreateLogEvent("too large for emergency"));

                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_emergencyDroppedCount"));
                StringAssert.Contains(
                    selfLog.ToString(),
                    "emergency buffer limit reached (10 events / 1 payload bytes)");
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SpoolByteBudgetEvictsOldestUnsentAndPreservesNewerEntries()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            using var selfLog = new StringWriter(CultureInfo.InvariantCulture);
            SelfLog.Enable(selfLog);

            try
            {
                var options = new SerilogRelayOptions();
                options.Spool.MaxBytes = 128L * 1024L;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);

                string largeSuffix = new string('x', 4096);
                const int emitted = 80;
                for (int index = 0; index < emitted; index++)
                    sink.Emit(CreateLogEvent($"spool {index:D2} {largeSuffix}"));

                long remaining = GetUnsentCount(connectionString);
                Assert.IsGreaterThan(0L, remaining);
                Assert.IsLessThan(emitted, remaining);
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
SELECT
    SUM(CASE WHEN RenderMessage LIKE 'spool 00 %' THEN 1 ELSE 0 END),
    SUM(CASE WHEN RenderMessage LIKE 'spool 79 %' THEN 1 ELSE 0 END)
FROM SerilogRelayEvents
WHERE Sent = 0;";
                    using SqliteDataReader reader = command.ExecuteReader();
                    Assert.IsTrue(reader.Read());
                    Assert.AreEqual(0L, reader.GetInt64(0));
                    Assert.AreEqual(1L, reader.GetInt64(1));
                }

                StringAssert.Contains(
                    selfLog.ToString(),
                    "spool reached its 131072-byte budget");
                Assert.IsLessThanOrEqualTo(
                    options.Spool.MaxBytes,
                    new FileInfo(databasePath).Length);
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ShutdownCancelsInFlightFlushAtDeadline()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            using var serverCancellation = new CancellationTokenSource();

            try
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task serverTask = AcceptAndHoldAsync(listener, serverCancellation.Token);

                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 20;
                options.Delivery.MaximumBatchEvents = 100;
                options.Delivery.PollInterval = TimeSpan.FromSeconds(5);
                options.Delivery.MaximumBatchWait = TimeSpan.FromSeconds(10);
                options.Retry.JitterRatio = 0d;

                var sink = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{port}/logs",
                    options);
                GetPrivateField<HttpClient>(sink, "_httpClient").Timeout = TimeSpan.FromSeconds(10);

                sink.Emit(CreateLogEvent("shutdown deadline"));

                Stopwatch stopwatch = Stopwatch.StartNew();
                await sink.DisposeAsync();
                stopwatch.Stop();

                Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(2.5), stopwatch.Elapsed);
                Assert.IsLessThan(TimeSpan.FromSeconds(4.5), stopwatch.Elapsed);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));

                serverCancellation.Cancel();
                await serverTask;
            }
            finally
            {
                serverCancellation.Cancel();
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task CompatibilityConstructorCopiesEmergencyOptions()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var emergency = new EmergencyOptions
                {
                    MaxBufferedEvents = 7,
                    MaxBufferedPayloadBytes = 1234,
                };

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    unsentRetention: null,
                    emergencyOptions: emergency);

                Assert.AreEqual(7, GetPrivateField<int>(sink, "_emergencyBufferCapacity"));
                Assert.AreEqual(1234L, GetPrivateField<long>(sink, "_maxEmergencyBufferedPayloadBytes"));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SpoolCapacityRejectDoesNotEnterEmergency()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var selfLog = new StringWriter(CultureInfo.InvariantCulture);
            SelfLog.Enable(selfLog);

            try
            {
                var options = new SerilogRelayOptions();
                options.Spool.MaxBytes = 64L * 1024L;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);

                string oversized = new string('x', 256 * 1024);
                sink.Emit(CreateLogEvent(oversized));
                sink.Emit(CreateLogEvent(oversized));

                Assert.AreEqual(0L, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
                Assert.AreEqual(0, GetPrivateField<int>(sink, "_spoolUnavailable"));
                Assert.AreEqual(2L, GetPrivateField<long>(sink, "_spoolDroppedCount"));
                StringAssert.Contains(
                    selfLog.ToString(),
                    "incoming events are being dropped because no retained rows can be reclaimed");
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SpoolReclaimPrefersSentRowsBeforeUnsentRows()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                sink.Emit(CreateLogEvent("sent candidate"));
                sink.Emit(CreateLogEvent("unsent candidate"));

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
UPDATE SerilogRelayEvents
   SET Sent = 1
 WHERE Id = (SELECT MIN(Id) FROM SerilogRelayEvents);";
                    Assert.AreEqual(1, command.ExecuteNonQuery());
                }

                bool reclaimed = InvokePrivateMethod<bool>(sink, "TryReclaimSpoolSpaceCore");

                Assert.IsTrue(reclaimed);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_spoolDroppedCount"));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SenderDelayHandlesOverduePartialBatchAndCatchUp()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 20;
                options.Delivery.MaximumBatchEvents = 20;
                options.Delivery.PollInterval = TimeSpan.FromSeconds(5);
                options.Delivery.MaximumBatchWait = TimeSpan.FromSeconds(1);

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);

                DateTimeOffset now = DateTimeOffset.UtcNow;
                SetPrivateField(sink, "_pendingSinceUtc", (DateTimeOffset?)now.AddSeconds(-2));

                Assert.AreEqual(
                    TimeSpan.FromMilliseconds(1),
                    InvokePrivateMethod<TimeSpan>(
                        sink,
                        "GetSenderDelay",
                        1L,
                        false,
                        now));

                Assert.AreEqual(
                    TimeSpan.FromSeconds(1),
                    InvokePrivateMethod<TimeSpan>(
                        sink,
                        "GetSenderDelay",
                        101L,
                        true,
                        now));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task EmergencyRecoveryDropsCapacityRejectedEventWithoutLeavingSpoolUnavailable()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var options = new SerilogRelayOptions();
                options.Spool.MaxBytes = 64L * 1024L;
                options.Emergency.MaxBufferedEvents = 10;
                options.Emergency.MaxBufferedPayloadBytes = 1024L * 1024L;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
CREATE TRIGGER fail_serilog_relay_insert
BEFORE INSERT ON SerilogRelayEvents
BEGIN
    SELECT RAISE(ABORT, 'simulated write failure');
END;";
                    command.ExecuteNonQuery();
                }

                sink.Emit(CreateLogEvent(new string('x', 256 * 1024)));

                await WaitUntilAsync(
                    () => GetPrivateField<long>(sink, "_emergencyBufferedCount") == 1,
                    TimeSpan.FromSeconds(5));

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "DROP TRIGGER fail_serilog_relay_insert;";
                    command.ExecuteNonQuery();
                }

                await WaitUntilAsync(
                    () => GetPrivateField<long>(sink, "_emergencyBufferedCount") == 0,
                    TimeSpan.FromSeconds(5));

                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_spoolDroppedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
                Assert.AreEqual(0, GetPrivateField<int>(sink, "_spoolUnavailable"));
                Assert.AreEqual(0L, GetUnsentCount(connectionString));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ExistingSpoolAboveNewBudgetIsNotPurgedAtStartup()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var initialOptions = new SerilogRelayOptions();
                initialOptions.Spool.MaxBytes = 2L * 1024L * 1024L;

                await using (var writer = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    initialOptions))
                {
                    string suffix = new string('x', 4096);
                    for (int index = 0; index < 100; index++)
                        writer.Emit(CreateLogEvent($"grandfather {index:D3} {suffix}"));
                }

                long before = GetUnsentCount(connectionString);
                Assert.AreEqual(100L, before);

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var pageSizeCommand = connection.CreateCommand();
                    pageSizeCommand.CommandText = "PRAGMA page_size;";
                    long pageSize = Convert.ToInt64(pageSizeCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
                    using var pageCountCommand = connection.CreateCommand();
                    pageCountCommand.CommandText = "PRAGMA page_count;";
                    long pageCount = Convert.ToInt64(pageCountCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
                    Assert.IsGreaterThan(64L * 1024L, pageSize * pageCount);
                }

                var reducedOptions = new SerilogRelayOptions();
                reducedOptions.Spool.MaxBytes = 64L * 1024L;

                await using (var reader = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    reducedOptions))
                {
                    Assert.AreEqual(before, GetUnsentCount(connectionString));
                }

                Assert.AreEqual(before, GetUnsentCount(connectionString));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        private static LogEvent CreateLogEvent(string message)
        {
            MessageTemplate template = new MessageTemplateParser().Parse(message);
            return new LogEvent(
                DateTimeOffset.UtcNow,
                LogEventLevel.Information,
                exception: null,
                template,
                Array.Empty<LogEventProperty>());
        }

        private static long GetUnsentCount(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM SerilogRelayEvents WHERE Sent = 0;";
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static T GetPrivateField<T>(SerilogRelaySink sink, string name)
        {
            FieldInfo field = typeof(SerilogRelaySink).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(SerilogRelaySink).FullName, name);
            return (T)field.GetValue(sink)!;
        }

        private static T InvokePrivateMethod<T>(
            SerilogRelaySink sink,
            string name,
            params object?[] arguments)
        {
            MethodInfo method = typeof(SerilogRelaySink).GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(SerilogRelaySink).FullName, name);
            return (T)method.Invoke(sink, arguments)!;
        }

        private static void SetPrivateField<T>(SerilogRelaySink sink, string name, T value)
        {
            FieldInfo field = typeof(SerilogRelaySink).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(SerilogRelaySink).FullName, name);
            field.SetValue(sink, value);
        }

        private static int ReserveAndReleasePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task<string> ReceiveSingleRequestAsync(
            TcpListener listener,
            HttpStatusCode statusCode)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync();
            using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            int contentLength = 0;
            while (true)
            {
                string? line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                    break;

                const string prefix = "Content-Length:";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(
                        line.Substring(prefix.Length).Trim(),
                        CultureInfo.InvariantCulture);
                }
            }

            char[] bodyBuffer = new char[contentLength];
            int totalRead = 0;
            while (totalRead < contentLength)
            {
                int read = await reader.ReadAsync(
                    bodyBuffer.AsMemory(totalRead, contentLength - totalRead));
                if (read == 0)
                    break;

                totalRead += read;
            }

            string body = new string(bodyBuffer, 0, totalRead);
            string reason = statusCode == HttpStatusCode.OK ? "OK" : "Error";
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)statusCode} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.FlushAsync();
            return body;
        }

        private static async Task AcceptAndHoldAsync(
            TcpListener listener,
            CancellationToken cancellationToken)
        {
            try
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (predicate())
                    return;

                await Task.Delay(25);
            }

            Assert.Fail("Timed out waiting for the expected condition.");
        }

        private static string CreateTemporaryDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "Eigenverft.NetLib.SerilogRelay.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void DeleteTemporaryDirectory(string directory)
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
