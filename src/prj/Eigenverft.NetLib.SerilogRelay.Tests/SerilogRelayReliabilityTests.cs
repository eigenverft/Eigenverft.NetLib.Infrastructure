using System;
using System.Collections.Generic;
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
                options.EndpointRetry.JitterRatio = 0d;

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
                options.EndpointRetry.InitialDelay = TimeSpan.FromMilliseconds(100);
                options.EndpointRetry.MaximumDelay = TimeSpan.FromMilliseconds(100);
                options.EndpointRetry.Multiplier = 1d;
                options.EndpointRetry.JitterRatio = 0d;

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
                options.EmergencyMemoryBuffer.MaxBufferedEvents = 10;
                options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes = 1;

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
                options.ApplicationSpool.MaxPhysicalBytes = 128L * 1024L;

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
                    options.ApplicationSpool.MaxPhysicalBytes,
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
                options.EndpointRetry.JitterRatio = 0d;

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
        public async Task CompatibilityConstructorCopiesEmergencyMemoryBufferOptions()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var emergency = new EmergencyMemoryBufferOptions
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
                options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;

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
                Assert.AreEqual(2L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
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
        public async Task OversizedEventDoesNotEvictExistingUnsentBacklog()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var options = new SerilogRelayOptions();
                options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);

                sink.Emit(CreateLogEvent("backlog 0"));
                sink.Emit(CreateLogEvent("backlog 1"));
                sink.Emit(CreateLogEvent("backlog 2"));

                Assert.AreEqual(3L, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));

                sink.Emit(CreateLogEvent(new string('x', 256 * 1024)));

                Assert.AreEqual(3L, GetUnsentCount(connectionString));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));

                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT COUNT(*)
  FROM SerilogRelayEvents
 WHERE Sent = 0
   AND RenderMessage LIKE 'backlog %';";
                Assert.AreEqual(
                    3L,
                    Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SpoolCapacityRejectsWhenBudgetIsConsumedWithoutRelayRows()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var options = new SerilogRelayOptions();
                options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    int sequence = 0;

                    foreach (int payloadSize in new[] { 4096, 512, 64, 1 })
                    {
                        while (true)
                        {
                            try
                            {
                                using var fill = connection.CreateCommand();
                                fill.CommandText = @"
INSERT INTO SerilogRelayEvents
    (EventId, ApplicationId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate, Sent)
VALUES
    ($eventId, 'capacity-filler', 0, '2026-01-01T00:00:00.0000000Z', 'Information', $payload, $payload, 2);";
                                fill.Parameters.AddWithValue("$eventId", $"capacity-filler-{sequence++}");
                                fill.Parameters.AddWithValue("$payload", new string('f', payloadSize));
                                fill.ExecuteNonQuery();
                            }
                            catch (SqliteException ex) when (ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_FULL)
                            {
                                break;
                            }
                        }
                    }
                }

                sink.Emit(CreateLogEvent("small event still fits an empty spool"));

                Assert.AreEqual(0L, GetUnsentCount(connectionString));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0, GetPrivateField<int>(sink, "_spoolUnavailable"));
            }
            finally
            {
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

                bool reclaimed = InvokePrivateMethod<bool>(sink, "TryReclaimApplicationSpoolSpaceCore");

                Assert.IsTrue(reclaimed);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
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
                options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;
                options.EmergencyMemoryBuffer.MaxBufferedEvents = 10;
                options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes = 1024L * 1024L;

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

                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
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
        public async Task FileBackedApplicationSpoolAllowsMultipleActiveSinks()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                await using var first = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());
                await using var second = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                first.Emit(CreateLogEvent("from first process"));
                second.Emit(CreateLogEvent("from second process"));

                Assert.AreEqual(2L, GetUnsentCount(connectionString));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ConcurrentSendersClaimDisjointRowsFromSharedSpool()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                await using var first = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());
                await using var second = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                for (int index = 0; index < 10; index++)
                    first.Emit(CreateLogEvent($"claim {index:D2}"));

                DateTimeOffset now = DateTimeOffset.UtcNow;
                Task<ClaimedLogBatch> firstClaimTask = InvokePrivateTaskMethod<ClaimedLogBatch>(
                    first,
                    "ClaimPendingAsync",
                    5,
                    now,
                    CancellationToken.None);
                Task<ClaimedLogBatch> secondClaimTask = InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    5,
                    now,
                    CancellationToken.None);

                await Task.WhenAll(firstClaimTask, secondClaimTask);

                ClaimedLogBatch firstClaim = await firstClaimTask;
                ClaimedLogBatch secondClaim = await secondClaimTask;
                Assert.AreEqual(5, firstClaim.Entries.Count);
                Assert.AreEqual(5, secondClaim.Entries.Count);

                var eventIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (LogEntry entry in firstClaim.Entries)
                    Assert.IsTrue(eventIds.Add(entry.EventId));
                foreach (LogEntry entry in secondClaim.Entries)
                    Assert.IsTrue(eventIds.Add(entry.EventId));

                Assert.AreEqual(10, eventIds.Count);
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ExpiredClaimCanBeTakenOverByAnotherSender()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                await using var first = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());
                await using var second = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                first.Emit(CreateLogEvent("claim takeover"));
                DateTimeOffset now = DateTimeOffset.UtcNow;

                ClaimedLogBatch original = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    first,
                    "ClaimPendingAsync",
                    1,
                    now,
                    CancellationToken.None);
                Assert.AreEqual(1, original.Entries.Count);

                ClaimedLogBatch blocked = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    1,
                    now.AddSeconds(1),
                    CancellationToken.None);
                Assert.AreEqual(0, blocked.Entries.Count);

                ClaimedLogBatch takeover = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    1,
                    now.AddSeconds(31),
                    CancellationToken.None);
                Assert.AreEqual(1, takeover.Entries.Count);
                Assert.AreEqual(original.Entries[0].EventId, takeover.Entries[0].EventId);

                await InvokePrivateTaskMethod(
                    first,
                    "MarkClaimedAsSentAsync",
                    original,
                    CancellationToken.None);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));

                await InvokePrivateTaskMethod(
                    second,
                    "MarkClaimedAsSentAsync",
                    takeover,
                    CancellationToken.None);
                Assert.AreEqual(0L, GetUnsentCount(connectionString));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SameSenderCanRefreshItsOwnUnexpiredClaim()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                sink.Emit(CreateLogEvent("claim retry"));
                DateTimeOffset now = DateTimeOffset.UtcNow;

                ClaimedLogBatch first = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    sink,
                    "ClaimPendingAsync",
                    1,
                    now,
                    CancellationToken.None);
                ClaimedLogBatch refreshed = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    sink,
                    "ClaimPendingAsync",
                    1,
                    now.AddSeconds(1),
                    CancellationToken.None);

                Assert.AreEqual(1, first.Entries.Count);
                Assert.AreEqual(1, refreshed.Entries.Count);
                Assert.AreEqual(first.Entries[0].EventId, refreshed.Entries[0].EventId);
                Assert.AreNotEqual(first.ClaimBatchId, refreshed.ClaimBatchId);
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task GracefulDisposeReleasesOwnedClaimsImmediately()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            SerilogRelaySink? first = null;

            try
            {
                first = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());
                await using var second = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                first.Emit(CreateLogEvent("release on dispose"));
                DateTimeOffset now = DateTimeOffset.UtcNow;
                ClaimedLogBatch claimed = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    first,
                    "ClaimPendingAsync",
                    1,
                    now,
                    CancellationToken.None);
                Assert.AreEqual(1, claimed.Entries.Count);

                await first.DisposeAsync();
                first = null;

                ClaimedLogBatch takeover = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    1,
                    now.AddSeconds(1),
                    CancellationToken.None);
                Assert.AreEqual(1, takeover.Entries.Count);
                Assert.AreEqual(claimed.Entries[0].EventId, takeover.Entries[0].EventId);
            }
            finally
            {
                if (first is not null)
                    await first.DisposeAsync();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task RunningSenderDiscoversAndDeliversRowsWrittenByAnotherSink()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            int port = ReserveAndReleasePort();
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            try
            {
                var senderOptions = new SerilogRelayOptions();
                senderOptions.Delivery.MinimumBatchEvents = 20;
                senderOptions.Delivery.MaximumBatchEvents = 100;
                senderOptions.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);
                senderOptions.Delivery.MaximumBatchWait = TimeSpan.FromMilliseconds(50);

                await using var sender = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{port}/logs",
                    senderOptions);
                await using var producer = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                Task<string> received = ReceiveSingleRequestAsync(listener, HttpStatusCode.OK);
                producer.Emit(CreateLogEvent("cross process discovery"));

                string body = await received.WaitAsync(TimeSpan.FromSeconds(5));
                StringAssert.Contains(body, "cross process discovery");

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
        public async Task ExistingSpoolSchemaIsUpgradedWithClaimColumns()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";

            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var create = connection.CreateCommand();
                    create.CommandText = @"
CREATE TABLE SerilogRelayEvents (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId         TEXT    NOT NULL UNIQUE,
    ApplicationId   TEXT    NOT NULL,
    MachineId       TEXT,
    ProcessId       INTEGER NOT NULL,
    Timestamp       TEXT    NOT NULL,
    Level           TEXT    NOT NULL,
    RenderMessage   TEXT    NOT NULL,
    MessageTemplate TEXT    NOT NULL,
    TraceId         TEXT,
    SpanId          TEXT,
    Exception       TEXT,
    Properties      TEXT,
    Sent            INTEGER NOT NULL DEFAULT 0,
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
);";
                    create.ExecuteNonQuery();
                }

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                using var verify = new SqliteConnection(connectionString);
                verify.Open();
                using var info = verify.CreateCommand();
                info.CommandText = "PRAGMA table_info(SerilogRelayEvents);";
                using var reader = info.ExecuteReader();
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                    columns.Add(reader.GetString(1));

                Assert.IsTrue(columns.Contains("ClaimOwnerId"));
                Assert.IsTrue(columns.Contains("ClaimBatchId"));
                Assert.IsTrue(columns.Contains("ClaimUntilUnixMs"));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task MaximumBatchWaitInitializesWhenPendingAgeIsUnknown()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                DateTimeOffset now = DateTimeOffset.UtcNow;
                SetPrivateField<DateTimeOffset?>(sink, "_pendingSinceUtc", null);

                Assert.IsFalse(InvokePrivateMethod<bool>(
                    sink,
                    "IsMaximumBatchWaitElapsed",
                    now));
                Assert.AreEqual(
                    now,
                    GetPrivateField<DateTimeOffset?>(sink, "_pendingSinceUtc"));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SenderHandlesClaimRaceThatLeavesNoRows()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 1;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    "http://127.0.0.1:1/logs",
                    options);

                sink.Emit(CreateLogEvent("claim race empty"));

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var trigger = connection.CreateCommand();
                    trigger.CommandText = @"
CREATE TRIGGER ignore_all_claims
BEFORE UPDATE OF ClaimOwnerId ON SerilogRelayEvents
WHEN NEW.ClaimOwnerId IS NOT NULL
BEGIN
    SELECT RAISE(IGNORE);
END;";
                    trigger.ExecuteNonQuery();
                }

                bool didWork = await InvokePrivateTaskMethod<bool>(
                    sink,
                    "ProcessPendingAsync",
                    true,
                    CancellationToken.None);

                Assert.IsFalse(didWork);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SenderReleasesClaimWhenConcurrentRaceLeavesUndersizedBatch()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 2;
                options.Delivery.MaximumBatchEvents = 10;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    "http://127.0.0.1:1/logs",
                    options);

                sink.Emit(CreateLogEvent("claim race first"));
                sink.Emit(CreateLogEvent("claim race second"));

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var trigger = connection.CreateCommand();
                    trigger.CommandText = @"
CREATE TRIGGER ignore_even_claims
BEFORE UPDATE OF ClaimOwnerId ON SerilogRelayEvents
WHEN NEW.ClaimOwnerId IS NOT NULL AND (OLD.Id % 2) = 0
BEGIN
    SELECT RAISE(IGNORE);
END;";
                    trigger.ExecuteNonQuery();
                }

                bool didWork = await InvokePrivateTaskMethod<bool>(
                    sink,
                    "ProcessPendingAsync",
                    false,
                    CancellationToken.None);

                Assert.IsFalse(didWork);

                using var verify = new SqliteConnection(connectionString);
                verify.Open();
                using var command = verify.CreateCommand();
                command.CommandText = @"
SELECT COUNT(*)
  FROM SerilogRelayEvents
 WHERE Sent = 0
   AND ClaimOwnerId IS NOT NULL;";
                Assert.AreEqual(
                    0L,
                    Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task EmptyClaimBatchCanBeMarkedSentWithoutDatabaseWork()
        {
            await using var sink = new SerilogRelaySink(
                "Data Source=:memory:",
                endpoint: null,
                new SerilogRelayOptions());

            var empty = new ClaimedLogBatch("empty", new List<LogEntry>());
            await InvokePrivateTaskMethod(
                sink,
                "MarkClaimedAsSentAsync",
                empty,
                CancellationToken.None);
        }

        [TestMethod]
        public async Task RecoveryLockSupportsNonFileSpoolsAndTimesOutWhenHeld()
        {
            await using (var memorySink = new SerilogRelaySink(
                "Data Source=:memory:",
                endpoint: null,
                new SerilogRelayOptions()))
            {
                Assert.IsNull(InvokePrivateMethod<FileStream?>(
                    memorySink,
                    "TryAcquireRecoveryLock",
                    TimeSpan.Zero));
            }

            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";

            try
            {
                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                string lockPath = databasePath + ".recovery.lock";
                using var held = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

                FileStream? acquired = InvokePrivateMethod<FileStream?>(
                    sink,
                    "TryAcquireRecoveryLock",
                    TimeSpan.FromMilliseconds(75));
                Assert.IsNull(acquired);
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task HealthySpoolShortCircuitsRedundantCorruptionRecovery()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                Assert.IsTrue(InvokePrivateMethod<bool>(
                    sink,
                    "IsCurrentSpoolHealthyCore"));
                Assert.IsTrue(InvokePrivateMethod<bool>(
                    sink,
                    "TryRecoverCorruptedSpool",
                    new SqliteException("stale corruption observation", SQLitePCL.raw.SQLITE_CORRUPT)));
                Assert.AreEqual(0L, GetUnsentCount(connectionString));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task CorruptionRecoveryReturnsFalseWhenAnotherProcessHoldsRecoveryLock()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            using var selfLog = new StringWriter(CultureInfo.InvariantCulture);
            SelfLog.Enable(selfLog);

            try
            {
                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                string lockPath = databasePath + ".recovery.lock";
                using var held = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

                Assert.IsFalse(InvokePrivateMethod<bool>(
                    sink,
                    "TryRecoverCorruptedSpool",
                    new SqliteException("corrupt", SQLitePCL.raw.SQLITE_CORRUPT)));
                StringAssert.Contains(
                    selfLog.ToString(),
                    "could not acquire cross-process corruption-recovery coordination");
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ShutdownClaimReleaseFailureIsDiagnosed()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var selfLog = new StringWriter(CultureInfo.InvariantCulture);
            SelfLog.Enable(selfLog);

            try
            {
                var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                GetPrivateField<SemaphoreSlim>(sink, "_databaseGate").Dispose();
                await sink.DisposeAsync();

                StringAssert.Contains(
                    selfLog.ToString(),
                    "could not release owned delivery claims during shutdown");
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ConcurrentStartupCanUpgradeOneLegacySpool()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var create = connection.CreateCommand();
                    create.CommandText = @"
CREATE TABLE SerilogRelayEvents (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId         TEXT    NOT NULL UNIQUE,
    ApplicationId   TEXT    NOT NULL,
    MachineId       TEXT,
    ProcessId       INTEGER NOT NULL,
    Timestamp       TEXT    NOT NULL,
    Level           TEXT    NOT NULL,
    RenderMessage   TEXT    NOT NULL,
    MessageTemplate TEXT    NOT NULL,
    TraceId         TEXT,
    SpanId          TEXT,
    Exception       TEXT,
    Properties      TEXT,
    Sent            INTEGER NOT NULL DEFAULT 0,
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
);";
                    create.ExecuteNonQuery();
                }

                Task<SerilogRelaySink> firstTask = Task.Run(() =>
                    new SerilogRelaySink(
                        connectionString,
                        endpoint: null,
                        new SerilogRelayOptions()));
                Task<SerilogRelaySink> secondTask = Task.Run(() =>
                    new SerilogRelaySink(
                        connectionString,
                        endpoint: null,
                        new SerilogRelayOptions()));

                SerilogRelaySink[] sinks = await Task.WhenAll(firstTask, secondTask);
                try
                {
                    sinks[0].Emit(CreateLogEvent("parallel migration first"));
                    sinks[1].Emit(CreateLogEvent("parallel migration second"));
                    Assert.AreEqual(2L, GetUnsentCount(connectionString));
                }
                finally
                {
                    await sinks[0].DisposeAsync();
                    await sinks[1].DisposeAsync();
                }
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ShutdownFinallyRunsWhenCancellationSourceIsAlreadyDisposed()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                GetPrivateField<CancellationTokenSource>(sink, "_cts").Dispose();

                await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
                    async () => await sink.DisposeAsync());
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
                initialOptions.ApplicationSpool.MaxPhysicalBytes = 2L * 1024L * 1024L;

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
                reducedOptions.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;

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

        private static async Task<T> InvokePrivateTaskMethod<T>(
            SerilogRelaySink sink,
            string name,
            params object?[] arguments)
        {
            MethodInfo method = typeof(SerilogRelaySink).GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(SerilogRelaySink).FullName, name);
            var task = (Task<T>)method.Invoke(sink, arguments)!;
            return await task.ConfigureAwait(false);
        }

        private static async Task InvokePrivateTaskMethod(
            SerilogRelaySink sink,
            string name,
            params object?[] arguments)
        {
            MethodInfo method = typeof(SerilogRelaySink).GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(SerilogRelaySink).FullName, name);
            var task = (Task)method.Invoke(sink, arguments)!;
            await task.ConfigureAwait(false);
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
