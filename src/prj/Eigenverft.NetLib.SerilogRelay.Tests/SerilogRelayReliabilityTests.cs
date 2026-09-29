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
        public async Task RunningSinkPeriodicallyExpiresUnsentEventsWithoutRestart()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var options = new SerilogRelayOptions();
                options.ApplicationSpool.UnsentEventMaxAge = TimeSpan.FromMilliseconds(100);
                options.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);

                sink.Emit(CreateLogEvent("periodic unsent expiry"));
                Assert.AreEqual(1L, GetUnsentCount(connectionString));

                await WaitUntilAsync(
                    () => GetUnsentCount(connectionString) == 0,
                    TimeSpan.FromSeconds(5));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task UnsentAgeCleanupDefersActivelyClaimedRowUntilClaimRelease()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var claimantOptions = new SerilogRelayOptions();
                claimantOptions.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);

                await using var claimant = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    claimantOptions);
                claimant.Emit(CreateLogEvent("claimed expiry"));

                ClaimedLogBatch claimed = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    claimant,
                    "ClaimPendingAsync",
                    1,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);
                Assert.AreEqual(1, claimed.Entries.Count);

                var cleanerOptions = new SerilogRelayOptions();
                cleanerOptions.ApplicationSpool.UnsentEventMaxAge = TimeSpan.Zero;
                cleanerOptions.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);

                await using var cleaner = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    cleanerOptions);

                await Task.Delay(150);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));

                await InvokePrivateTaskMethod(
                    claimant,
                    "ReleaseClaimAsync",
                    claimed,
                    CancellationToken.None);

                await WaitUntilAsync(
                    () => GetUnsentCount(connectionString) == 0,
                    TimeSpan.FromSeconds(5));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task CapacityReclamationDoesNotDeleteActivelyClaimedUnsentRow()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var options = new SerilogRelayOptions();
                options.ApplicationSpool.MaxPhysicalBytes = 128L * 1024L;
                options.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);

                string largeSuffix = new string('x', 4096);
                sink.Emit(CreateLogEvent($"protected {largeSuffix}"));

                ClaimedLogBatch claimed = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    sink,
                    "ClaimPendingAsync",
                    1,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);
                Assert.AreEqual(1, claimed.Entries.Count);
                string protectedEventId = claimed.Entries[0].EventId;

                for (int index = 0; index < 80; index++)
                    sink.Emit(CreateLogEvent($"pressure {index:D2} {largeSuffix}"));

                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT COUNT(*) FROM SerilogRelayEvents WHERE EventId = $eventId AND Sent = 0;";
                command.Parameters.AddWithValue("$eventId", protectedEventId);

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
        public async Task UnsentMaxAgeContinuesDuringEndpointRetryBackoff()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            int closedPort = ReserveAndReleasePort();

            try
            {
                var options = new SerilogRelayOptions();
                options.ApplicationSpool.UnsentEventMaxAge =
                    TimeSpan.FromHours(1);
                options.Delivery.MinimumBatchEvents = 1;
                options.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);
                options.Delivery.MaximumBatchWait = TimeSpan.FromMilliseconds(25);
                options.EndpointRetry.InitialDelay = TimeSpan.FromSeconds(30);
                options.EndpointRetry.MaximumDelay = TimeSpan.FromSeconds(30);
                options.EndpointRetry.Multiplier = 1d;
                options.EndpointRetry.JitterRatio = 0d;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{closedPort}/logs",
                    options);

                sink.Emit(CreateLogEvent("expire during retry backoff"));

                RetryGate retryGate =
                    GetPrivateField<RetryGate>(sink, "_retryGate");
                await WaitUntilAsync(
                    () => retryGate.ConsecutiveFailures > 0,
                    TimeSpan.FromSeconds(5));

                Assert.IsTrue(retryGate.NextAttemptAt.HasValue);
                Assert.IsGreaterThan(
                    TimeSpan.FromSeconds(20),
                    retryGate.NextAttemptAt.Value - DateTimeOffset.UtcNow);

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "UPDATE SerilogRelayEvents SET CreatedAt = datetime('now', '-2 hours') WHERE Sent = 0;";
                    Assert.AreEqual(1, command.ExecuteNonQuery());
                }

                await WaitUntilAsync(
                    () => GetUnsentCount(connectionString) == 0,
                    TimeSpan.FromSeconds(5));

                Assert.IsTrue(retryGate.NextAttemptAt.Value > DateTimeOffset.UtcNow);
            }
            finally
            {
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
        public async Task SeparateProcessesAllowNewEndpointAndTokenToTakeOverFailedOldDelivery()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            string stopFile = Path.Combine(directory, "stop-old-sender");
            string oldReadyFile = Path.Combine(directory, "old-ready");
            string newReadyFile = Path.Combine(directory, "new-ready");
            using var oldListener = new TcpListener(IPAddress.Loopback, 0);
            using var newListener = new TcpListener(IPAddress.Loopback, 0);
            Process? oldProcess = null;
            Process? newProcess = null;

            oldListener.Start();
            newListener.Start();

            try
            {
                string oldEndpoint =
                    $"http://127.0.0.1:{((IPEndPoint)oldListener.LocalEndpoint).Port}/logs";
                string newEndpoint =
                    $"http://127.0.0.1:{((IPEndPoint)newListener.LocalEndpoint).Port}/logs";

                Task<(string? Authorization, string Body)> oldRequest =
                    ReceiveRequestAsync(oldListener, HttpStatusCode.Unauthorized);

                oldProcess = StartSeparateProcessSender(
                    role: "old",
                    connectionString,
                    oldEndpoint,
                    bearerToken: "old-token",
                    stopFile,
                    readyFile: oldReadyFile);

                await WaitForProcessReadyAsync(
                    oldProcess,
                    oldReadyFile,
                    TimeSpan.FromSeconds(20));

                (string? oldAuthorization, string oldBody) =
                    await oldRequest.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.AreEqual("Bearer old-token", oldAuthorization);

                string oldEventId = GetSingleRequestEventId(oldBody);

                await WaitUntilAsync(
                    () => GetUnsentCount(connectionString) == 1
                        && GetClaimOwnerId(connectionString) is null,
                    TimeSpan.FromSeconds(10));

                Assert.IsFalse(oldProcess.HasExited);

                Task<(string? Authorization, string Body)> newRequest =
                    ReceiveRequestAsync(newListener, HttpStatusCode.OK);

                newProcess = StartSeparateProcessSender(
                    role: "new",
                    connectionString,
                    newEndpoint,
                    bearerToken: "new-token",
                    stopFile: null,
                    readyFile: newReadyFile);

                await WaitForProcessReadyAsync(
                    newProcess,
                    newReadyFile,
                    TimeSpan.FromSeconds(20));

                (string? newAuthorization, string newBody) =
                    await newRequest.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.AreEqual("Bearer new-token", newAuthorization);
                Assert.AreEqual(oldEventId, GetSingleRequestEventId(newBody));

                await WaitForProcessExitAsync(newProcess, TimeSpan.FromSeconds(15));
                Assert.AreEqual(0, newProcess.ExitCode);
                Assert.AreEqual(0L, GetUnsentCount(connectionString));

                File.WriteAllText(stopFile, "stop");
                await WaitForProcessExitAsync(oldProcess, TimeSpan.FromSeconds(15));
                Assert.AreEqual(0, oldProcess.ExitCode);
            }
            finally
            {
                StopProcessIfRunning(newProcess);
                StopProcessIfRunning(oldProcess);
                oldListener.Stop();
                newListener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SeparateProcessSenderRole()
        {
            string? role = Environment.GetEnvironmentVariable(
                "SERILOG_RELAY_PROCESS_TEST_ROLE");
            if (string.IsNullOrEmpty(role))
                return;

            string connectionString = GetRequiredProcessTestEnvironment(
                "SERILOG_RELAY_PROCESS_TEST_CONNECTION");
            string endpoint = GetRequiredProcessTestEnvironment(
                "SERILOG_RELAY_PROCESS_TEST_ENDPOINT");
            string bearerToken = GetRequiredProcessTestEnvironment(
                "SERILOG_RELAY_PROCESS_TEST_TOKEN");

            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = 1;
            options.Delivery.MaximumBatchEvents = 10;
            options.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);
            options.Delivery.MaximumBatchWait = TimeSpan.FromMilliseconds(25);
            options.EndpointRetry.InitialDelay = TimeSpan.FromSeconds(30);
            options.EndpointRetry.MaximumDelay = TimeSpan.FromSeconds(30);
            options.EndpointRetry.Multiplier = 1d;
            options.EndpointRetry.JitterRatio = 0d;

            await using var sink = new SerilogRelaySink(
                connectionString,
                endpoint,
                options,
                applicationId: "ProcessSmoke.App",
                bearerToken: bearerToken);

            string readyFile = GetRequiredProcessTestEnvironment(
                "SERILOG_RELAY_PROCESS_TEST_READY_FILE");
            File.WriteAllText(readyFile, role);

            if (string.Equals(role, "old", StringComparison.Ordinal))
            {
                sink.Emit(CreateLogEvent("separate process takeover"));

                RetryGate retryGate = GetPrivateField<RetryGate>(sink, "_retryGate");
                await WaitUntilAsync(
                    () => retryGate.ConsecutiveFailures > 0
                        && GetClaimOwnerId(connectionString) is null,
                    TimeSpan.FromSeconds(10));

                string stopFile = GetRequiredProcessTestEnvironment(
                    "SERILOG_RELAY_PROCESS_TEST_STOP_FILE");
                await WaitUntilAsync(
                    () => File.Exists(stopFile),
                    TimeSpan.FromSeconds(20));
                return;
            }

            if (string.Equals(role, "new", StringComparison.Ordinal))
            {
                await WaitUntilAsync(
                    () => GetUnsentCount(connectionString) == 0,
                    TimeSpan.FromSeconds(10));
                return;
            }

            Assert.Fail($"Unknown separate-process role '{role}'.");
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
        public async Task FailedSendWithBackoffLongerThanLeaseReleasesClaimImmediately()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            int port = ReserveAndReleasePort();
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            try
            {
                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 1;
                options.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);
                options.Delivery.MaximumBatchWait = TimeSpan.FromMilliseconds(25);
                options.EndpointRetry.InitialDelay = TimeSpan.FromSeconds(40);
                options.EndpointRetry.MaximumDelay = TimeSpan.FromMinutes(5);
                options.EndpointRetry.JitterRatio = 0d;

                await using var first = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{port}/logs",
                    options);
                await using var second = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                Task<string> failedRequest =
                    ReceiveSingleRequestAsync(listener, HttpStatusCode.InternalServerError);
                first.Emit(CreateLogEvent("long process-local backoff"));
                await failedRequest.WaitAsync(TimeSpan.FromSeconds(5));

                await WaitUntilAsync(
                    () => GetClaimOwnerId(connectionString) is null,
                    TimeSpan.FromSeconds(5));

                RetryGate gate = GetPrivateField<RetryGate>(first, "_retryGate");
                Assert.IsTrue(gate.NextAttemptAt.HasValue);
                Assert.IsGreaterThan(
                    TimeSpan.FromSeconds(30),
                    gate.NextAttemptAt.Value - DateTimeOffset.UtcNow);

                ClaimedLogBatch takeover = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    1,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);
                Assert.AreEqual(1, takeover.Entries.Count);
            }
            finally
            {
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task RetryAfterLongerThanMaximumBackoffDoesNotReserveSharedClaim()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            int port = ReserveAndReleasePort();
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            try
            {
                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 1;
                options.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);
                options.Delivery.MaximumBatchWait = TimeSpan.FromMilliseconds(25);
                options.EndpointRetry.JitterRatio = 0d;
                options.EndpointRetry.MaximumDelay = TimeSpan.FromMinutes(5);
                options.EndpointRetry.RespectRetryAfter = true;

                await using var first = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{port}/logs",
                    options);
                await using var second = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    new SerilogRelayOptions());

                Task<string> throttledRequest = ReceiveSingleRequestAsync(
                    listener,
                    HttpStatusCode.TooManyRequests,
                    "Retry-After: 600\r\n");
                first.Emit(CreateLogEvent("ten minute retry after"));
                await throttledRequest.WaitAsync(TimeSpan.FromSeconds(5));

                await WaitUntilAsync(
                    () => GetClaimOwnerId(connectionString) is null,
                    TimeSpan.FromSeconds(5));

                RetryGate gate = GetPrivateField<RetryGate>(first, "_retryGate");
                Assert.IsTrue(gate.NextAttemptAt.HasValue);
                Assert.IsGreaterThan(
                    TimeSpan.FromMinutes(9),
                    gate.NextAttemptAt.Value - DateTimeOffset.UtcNow);

                ClaimedLogBatch takeover = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    1,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);
                Assert.AreEqual(1, takeover.Entries.Count);
            }
            finally
            {
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task RetryGateAlreadyInFlightDoesNotLeaveClaimOwned()
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

                sink.Emit(CreateLogEvent("retry gate already in flight"));

                RetryGate gate = GetPrivateField<RetryGate>(sink, "_retryGate");
                Assert.IsTrue(gate.TryAcquire(DateTimeOffset.UtcNow));

                bool didWork = await InvokePrivateTaskMethod<bool>(
                    sink,
                    "ProcessPendingAsync",
                    true,
                    CancellationToken.None);

                Assert.IsFalse(didWork);
                Assert.IsNull(GetClaimOwnerId(connectionString));
                gate.CancelAttempt();
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task SenderLoopCompletesShortRetryDelayAndRetriesAfterReleasingClaim()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            int port = ReserveAndReleasePort();
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            try
            {
                var options = new SerilogRelayOptions();
                options.Delivery.MinimumBatchEvents = 1;
                options.Delivery.PollInterval = TimeSpan.FromMilliseconds(10);
                options.Delivery.MaximumBatchWait = TimeSpan.FromMilliseconds(10);
                options.EndpointRetry.InitialDelay = TimeSpan.FromMilliseconds(100);
                options.EndpointRetry.MaximumDelay = TimeSpan.FromMilliseconds(100);
                options.EndpointRetry.JitterRatio = 0d;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{port}/logs",
                    options);

                Task<string> failedRequest =
                    ReceiveSingleRequestAsync(listener, HttpStatusCode.InternalServerError);

                sink.Emit(CreateLogEvent("short retry completes"));

                await failedRequest.WaitAsync(TimeSpan.FromSeconds(5));
                await WaitUntilAsync(
                    () => GetClaimOwnerId(connectionString) is null,
                    TimeSpan.FromSeconds(2));

                Task<string> successRequest =
                    ReceiveSingleRequestAsync(listener, HttpStatusCode.OK);
                string successBody = await successRequest.WaitAsync(TimeSpan.FromSeconds(5));
                StringAssert.Contains(successBody, "short retry completes");

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

                // This test drives ProcessPendingAsync directly. Stop the autonomous sender first
                // so it cannot reclaim the row between the explicit release and verification.
                CancellationTokenSource senderCancellation =
                    GetPrivateField<CancellationTokenSource>(sink, "_cts");
                senderCancellation.Cancel();
                try
                {
                    await GetPrivateField<Task>(sink, "_senderTask")
                        .WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (senderCancellation.IsCancellationRequested)
                {
                }

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
        public async Task PublicBearerTokenParameterSendsAuthorizationHeader()
        {
            string directory = CreateTemporaryDirectory();
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            try
            {
                Task<string?> authorizationTask =
                    ReceiveAuthorizationHeaderAsync(listener, HttpStatusCode.OK);

                using Logger logger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(
                        endpoint: $"http://127.0.0.1:{port}/logs",
                        spoolDirectory: directory,
                        minimumBatchSize: 1,
                        baseInterval: TimeSpan.FromMilliseconds(10),
                        bearerToken: "relay-secret-token")
                    .CreateLogger();

                logger.Information("bearer token test");

                string? authorization =
                    await authorizationTask.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual("Bearer relay-secret-token", authorization);
            }
            finally
            {
                listener.Stop();
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

        private static string? GetClaimOwnerId(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT ClaimOwnerId
  FROM SerilogRelayEvents
 WHERE Sent = 0
 ORDER BY Id
 LIMIT 1;";
            object? value = command.ExecuteScalar();
            return value is null || value is DBNull
                ? null
                : Convert.ToString(value, CultureInfo.InvariantCulture);
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

        private static Process StartSeparateProcessSender(
            string role,
            string connectionString,
            string endpoint,
            string bearerToken,
            string? stopFile,
            string readyFile)
        {
            string testAssemblyPath = typeof(SerilogRelayReliabilityTests).Assembly.Location;

            var startInfo = new ProcessStartInfo(GetCurrentDotNetHostPath())
            {
                WorkingDirectory = Path.GetDirectoryName(testAssemblyPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // Execute the already-built test assembly directly. Using `dotnet test <csproj>`
            // would still evaluate the project and share its obj directory with the parent
            // test run, which can race with generated MSBuild props on hosted runners.
            startInfo.ArgumentList.Add("vstest");
            startInfo.ArgumentList.Add(testAssemblyPath);
            startInfo.ArgumentList.Add("--Tests:SeparateProcessSenderRole");

            startInfo.Environment["SERILOG_RELAY_PROCESS_TEST_ROLE"] = role;
            startInfo.Environment["SERILOG_RELAY_PROCESS_TEST_CONNECTION"] = connectionString;
            startInfo.Environment["SERILOG_RELAY_PROCESS_TEST_ENDPOINT"] = endpoint;
            startInfo.Environment["SERILOG_RELAY_PROCESS_TEST_TOKEN"] = bearerToken;
            startInfo.Environment["SERILOG_RELAY_PROCESS_TEST_READY_FILE"] = readyFile;
            if (stopFile is not null)
            {
                startInfo.Environment["SERILOG_RELAY_PROCESS_TEST_STOP_FILE"] =
                    stopFile;
            }

            return Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start separate SerilogRelay process test host.");
        }

        private static string GetCurrentDotNetHostPath()
        {
            string runtimeDirectory =
                Path.GetDirectoryName(typeof(object).Assembly.Location)
                ?? throw new InvalidOperationException(
                    "Could not resolve the current .NET runtime directory.");

            string hostPath = Path.GetFullPath(
                Path.Combine(
                    runtimeDirectory,
                    "..",
                    "..",
                    "..",
                    OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

            return File.Exists(hostPath)
                ? hostPath
                : throw new FileNotFoundException(
                    "Could not resolve the current .NET host.",
                    hostPath);
        }


        private static string GetRequiredProcessTestEnvironment(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value)
                ? throw new InvalidOperationException(
                    $"Required process-test environment variable '{name}' is missing.")
                : value;
        }

        private static async Task WaitForProcessReadyAsync(
            Process process,
            string readyFile,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(readyFile))
                    return;

                if (process.HasExited)
                {
                    string stdout = await process.StandardOutput.ReadToEndAsync();
                    string stderr = await process.StandardError.ReadToEndAsync();
                    Assert.Fail(
                        $"Separate SerilogRelay process exited before readiness. ExitCode={process.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
                }

                await Task.Delay(50);
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            string timedOutStdout =
                await process.StandardOutput.ReadToEndAsync();
            string timedOutStderr =
                await process.StandardError.ReadToEndAsync();
            Assert.Fail(
                $"Separate SerilogRelay process did not signal readiness within {timeout}.{Environment.NewLine}STDOUT:{Environment.NewLine}{timedOutStdout}{Environment.NewLine}STDERR:{Environment.NewLine}{timedOutStderr}");
        }

        private static async Task WaitForProcessExitAsync(
            Process process,
            TimeSpan timeout)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                StopProcessIfRunning(process);
                Assert.Fail(
                    $"Separate SerilogRelay process {process.Id} did not exit within {timeout}.");
            }
        }

        private static void StopProcessIfRunning(Process? process)
        {
            if (process is null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        private static async Task<(string? Authorization, string Body)> ReceiveRequestAsync(
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

            string? authorization = null;
            int contentLength = 0;
            while (true)
            {
                string? line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                    break;

                const string authorizationPrefix = "Authorization:";
                if (line.StartsWith(
                        authorizationPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    authorization =
                        line.Substring(authorizationPrefix.Length).Trim();
                }

                const string contentLengthPrefix = "Content-Length:";
                if (line.StartsWith(
                        contentLengthPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(
                        line.Substring(contentLengthPrefix.Length).Trim(),
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
            string reason =
                statusCode == HttpStatusCode.OK ? "OK" : "Error";
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)statusCode} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.FlushAsync();

            return (authorization, body);
        }

        private static string GetSingleRequestEventId(string body)
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement
                .GetProperty("logs")[0]
                .GetProperty("eventId")
                .GetString()
                ?? throw new InvalidOperationException(
                    "The SerilogRelay request did not contain an eventId.");
        }

        private static int ReserveAndReleasePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task<string?> ReceiveAuthorizationHeaderAsync(
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

            string? authorization = null;
            int contentLength = 0;
            while (true)
            {
                string? line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                    break;

                const string authorizationPrefix = "Authorization:";
                if (line.StartsWith(authorizationPrefix, StringComparison.OrdinalIgnoreCase))
                    authorization = line.Substring(authorizationPrefix.Length).Trim();

                const string contentLengthPrefix = "Content-Length:";
                if (line.StartsWith(contentLengthPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(
                        line.Substring(contentLengthPrefix.Length).Trim(),
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

            string reason = statusCode == HttpStatusCode.OK ? "OK" : "Error";
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)statusCode} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.FlushAsync();
            return authorization;
        }

        private static async Task<string> ReceiveSingleRequestAsync(
            TcpListener listener,
            HttpStatusCode statusCode,
            string? extraHeaders = null)
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
                $"HTTP/1.1 {(int)statusCode} {reason}\r\n{extraHeaders ?? string.Empty}Content-Length: 0\r\nConnection: close\r\n\r\n");
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
