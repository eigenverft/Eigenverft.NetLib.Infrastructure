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
    public sealed partial class SerilogRelayReliabilityTests
    {
        [TestMethod]
        public void GroupedOptionsKeepSimpleDurableUsage()
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
                    logger.Information("grouped options");
                }

                using var connection = new SqliteConnection($"Data Source={databasePath}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT COUNT(*) FROM SerilogRelayEvents WHERE Sent = 0 AND ApplicationId = 'Options.Api.App' AND RenderMessage = 'grouped options';";
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
                Task<string> requestTask = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent);

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
                Task<string> requestTask = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent);

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
                options.Delivery.RequestTimeout = TimeSpan.FromSeconds(10);
                options.Delivery.ShutdownRequestTimeout = TimeSpan.FromSeconds(10);
                options.EndpointRetry.JitterRatio = 0d;

                var sink = new SerilogRelaySink(
                    connectionString,
                    $"http://127.0.0.1:{port}/logs",
                    options);
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
        public async Task SpoolCapacityRejectUsesEmergencyMemory()
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
                Assert.AreEqual(2L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.IsGreaterThan(0L, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
                Assert.AreEqual(0, GetPrivateField<int>(sink, "_spoolUnavailable"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
                await DisposeAndWaitForCleanupAsync(sink);
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DataRow(22500)]
        [DataRow(256 * 1024)]
        public async Task OversizedEventDoesNotEvictExistingUnsentBacklog(int messageLength)
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

                sink.Emit(CreateLogEvent(new string('x', messageLength)));

                Assert.AreEqual(3L, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));

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
                await DisposeAndWaitForCleanupAsync(sink);
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

                FillSpoolWithNonReclaimableRows(connectionString);

                sink.Emit(CreateLogEvent("small event still fits an empty spool"));

                Assert.AreEqual(0L, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0, GetPrivateField<int>(sink, "_spoolUnavailable"));
                await DisposeAndWaitForCleanupAsync(sink);
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

                LogEntry replacement = InvokePrivateMethod<LogEntry>(sink, "CreateLogEntry", CreateLogEvent("replacement"), Guid.NewGuid().ToString("D"), false);
                bool reclaimed = InvokePrivateMethod<bool>(sink, "TryReclaimAndPersistApplicationSpoolCore", replacement);

                Assert.IsTrue(reclaimed);
                Assert.AreEqual(2L, GetUnsentCount(connectionString));
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
        public async Task EmergencyCapacityRejectionDoesNotReportRecoveryUntilAnEventIsStored()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";

            try
            {
                var options = new SerilogRelayOptions();
                options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;
                options.EmergencyMemoryBuffer.MaxBufferedEvents = 10;
                options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes = 2L * 1024L * 1024L;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    endpoint: null,
                    options);
                await StopRelayWorkers(sink);

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

                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(1, GetPrivateField<int>(sink, "_spoolUnavailable"));

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "DROP TRIGGER fail_serilog_relay_insert;";
                    command.ExecuteNonQuery();
                }

                sink.Emit(CreateLogEvent(new string('y', 256 * 1024)));
                Assert.AreEqual(1, GetPrivateField<int>(sink, "_spoolUnavailable"),
                    "An Emit rejected by capacity must not establish write recovery.");
                object batch = InvokePrivateMethod<object>(sink, "TakeEmergencyBatch");
                Assert.IsFalse(await InvokePrivateTaskMethod<bool>(sink, "TryProcessEmergencyBatchAsync", batch, CancellationToken.None));

                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyDroppedCount"));
                Assert.AreEqual(2L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.IsGreaterThan(0L, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
                Assert.AreEqual(1, GetPrivateField<int>(sink, "_spoolUnavailable"),
                    "Rejected emergency persistence must not establish write recovery either.");
                Assert.AreEqual(0L, GetUnsentCount(connectionString));
                InvokePrivateMethod<object?>(sink, "ReturnEmergencyEntriesToRetry", batch);
                InvokePrivateMethod<object?>(sink, "ReturnEmergencyEntriesToRetry", batch);
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyInFlightPayloadBytes"));

                sink.Emit(CreateLogEvent("successful write after capacity rejection"));
                Assert.AreEqual(0, GetPrivateField<int>(sink, "_spoolUnavailable"));
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.AreEqual(2L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                await DisposeAndWaitForCleanupAsync(sink);
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
                    ReceiveRequestAsync(newListener, HttpStatusCode.NoContent);

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
                    CancellationToken.None);
                Task<ClaimedLogBatch> secondClaimTask = InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    5,
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
                var clock = new ManualRelayTimeProvider();
                SetPrivateField(second, "_timeProvider", clock);

                ClaimedLogBatch original = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    first,
                    "ClaimPendingAsync",
                    1,
                    CancellationToken.None);
                Assert.AreEqual(1, original.Entries.Count);

                ClaimedLogBatch blocked = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    1,
                    CancellationToken.None);
                Assert.AreEqual(0, blocked.Entries.Count);

                clock.Advance(TimeSpan.FromSeconds(31));
                ClaimedLogBatch takeover = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    1,
                    CancellationToken.None);
                Assert.AreEqual(1, takeover.Entries.Count);
                Assert.AreEqual(original.Entries[0].EventId, takeover.Entries[0].EventId);

                await InvokePrivateTaskMethod(
                    first,
                    "AcknowledgeClaimAsync",
                    original,
                    CancellationToken.None);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));

                await InvokePrivateTaskMethod(
                    second,
                    "AcknowledgeClaimAsync",
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
                    CancellationToken.None);
                ClaimedLogBatch refreshed = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    sink,
                    "ClaimPendingAsync",
                    1,
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
        public async Task CappedRetryAfterDoesNotReserveSharedClaim()
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
                TimeSpan remainingDelay = gate.NextAttemptAt.Value - DateTimeOffset.UtcNow;
                Assert.IsTrue(remainingDelay > TimeSpan.FromMinutes(4)
                    && remainingDelay <= TimeSpan.FromMinutes(5));

                ClaimedLogBatch takeover = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    1,
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

                // This test drives ProcessPendingAsync directly. Stop the autonomous sender first
                // so it cannot claim the same row while the retry gate is held by the test.
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

                sink.Emit(CreateLogEvent("retry gate already in flight"));

                RetryGate gate = GetPrivateField<RetryGate>(sink, "_retryGate");
                Assert.IsTrue(gate.TryAcquire(DateTimeOffset.UtcNow));

                bool didWork = await InvokePrivateTaskMethod<bool>(
                    sink,
                    "ProcessPendingAsync",
                    true,
                    CancellationToken.None, false);

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
                    ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent);
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
                    CancellationToken.None);
                Assert.AreEqual(1, claimed.Entries.Count);

                await first.DisposeAsync();
                first = null;

                ClaimedLogBatch takeover = await InvokePrivateTaskMethod<ClaimedLogBatch>(
                    second,
                    "ClaimPendingAsync",
                    1,
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

                Task<string> received = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent);
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
                    CancellationToken.None, false);

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
                    CancellationToken.None, false);

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
                "AcknowledgeClaimAsync",
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
        public async Task CorruptionCreatesOnlyOneSuccessorAcrossInstances()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            try
            {
                await using var first = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                await using var second = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                var corruption = new SqliteException("corruption observation", SQLitePCL.raw.SQLITE_CORRUPT);
                Assert.IsTrue(InvokePrivateMethod<bool>(first, "TryRecoverCorruptedSpool", corruption));
                Assert.IsTrue(InvokePrivateMethod<bool>(second, "TryRecoverCorruptedSpool", corruption));
                Assert.IsTrue(GetPrivateField<bool>(first, "_spoolDisabledForLifetime"));
                Assert.IsTrue(GetPrivateField<bool>(second, "_spoolDisabledForLifetime"));
                Assert.IsTrue(File.Exists(Path.Combine(directory, "relay.g0001.db")));
                Assert.IsFalse(File.Exists(Path.Combine(directory, "relay.g0002.db")));
                await using var successor = new SerilogRelaySink(connectionString, null, new SerilogRelayOptions());
                Assert.AreEqual(Path.Combine(directory, "relay.g0001.db"), GetPrivateField<string>(successor, "_databasePath"));
                Assert.AreEqual(0L, GetUnsentCount($"Data Source={Path.Combine(directory, "relay.g0001.db")}"));
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
                    ReceiveAuthorizationHeaderAsync(listener, HttpStatusCode.NoContent);

                using Logger logger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(
                        endpoint: $"http://127.0.0.1:{port}/logs",
                        spoolDirectory: directory,
                        bearerToken: "relay-secret-token",
                        options: new SerilogRelayOptions
                        {
                            Delivery =
                            {
                                MinimumBatchEvents = 1,
                                PollInterval = TimeSpan.FromMilliseconds(10),
                            },
                        })
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


        [TestMethod]
        public async Task DefaultDeliveryDeletesAcknowledgedRows()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = 1;
            options.Delivery.PollInterval = TimeSpan.FromMilliseconds(25);
            var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);

            try
            {
                Task<string> received = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent);
                sink.Emit(CreateLogEvent("delete after acknowledgment"));
                await received.WaitAsync(TimeSpan.FromSeconds(5));
                await WaitUntilAsync(() => GetTotalRowCount(connectionString) == 0, TimeSpan.FromSeconds(5));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DataRow(2, 4 * 1024 * 1024, 1)]
        [DataRow(1, 4 * 1024 * 1024, 2)]
        [DataRow(2, 1, 2)]
        [DataRow(2, 650, 2)]
        public async Task ShutdownSendsVolatileEventsThenPartialSpoolWithoutNormalWaits(
            int emergencyMaximumBatchEvents,
            int emergencyTargetBatchPayloadBytes,
            int expectedEmergencyRequests)
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.Delivery.EmergencyMaximumBatchEvents = emergencyMaximumBatchEvents;
            options.Delivery.EmergencyTargetBatchPayloadBytes = emergencyTargetBatchPayloadBytes;
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            options.Delivery.MaximumBatchWait = TimeSpan.FromMinutes(1);
            var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);

            try
            {
                GetPrivateField<RetryGate>(sink, "_retryGate").RecordFailure(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
                sink.Emit(CreateLogEvent("durable one"));
                sink.Emit(CreateLogEvent("durable two"));

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = """
CREATE TRIGGER reject_volatile
BEFORE INSERT ON SerilogRelayEvents
WHEN NEW.RenderMessage LIKE 'volatile%'
BEGIN
    SELECT RAISE(ABORT, 'temporary spool failure');
END;
""";
                    command.ExecuteNonQuery();
                }

                sink.Emit(CreateLogEvent("volatile one"));
                await WaitUntilAsync(() => GetPrivateField<System.Collections.ICollection>(sink, "_emergencyRetryEntries").Count > 0, TimeSpan.FromSeconds(5));
                sink.Emit(CreateLogEvent("volatile two"));
                Assert.AreEqual(2L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Task<List<string>> received = expectedEmergencyRequests == 1
                    ? ReceiveRequestsAsync(listener, HttpStatusCode.NoContent, HttpStatusCode.NoContent)
                    : ReceiveRequestsAsync(listener, HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent);

                Stopwatch stopwatch = Stopwatch.StartNew();
                await sink.DisposeAsync();
                stopwatch.Stop();
                List<string> bodies = await received.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsLessThan(TimeSpan.FromSeconds(2), stopwatch.Elapsed);
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetTotalRowCount(connectionString));

                var volatileMessages = new List<string>();
                for (int index = 0; index < expectedEmergencyRequests; index++)
                {
                    using JsonDocument emergencyBatch = JsonDocument.Parse(bodies[index]);
                    JsonElement logs = emergencyBatch.RootElement.GetProperty("logs");
                    Assert.AreEqual(expectedEmergencyRequests == 1 ? 2 : 1, logs.GetArrayLength());
                    foreach (JsonElement log in logs.EnumerateArray())
                        volatileMessages.Add(log.GetProperty("renderMessage").GetString()!);
                }
                CollectionAssert.AreEqual(new[] { "volatile one", "volatile two" }, volatileMessages);

                using JsonDocument spoolBatch = JsonDocument.Parse(bodies[expectedEmergencyRequests]);
                JsonElement spoolLogs = spoolBatch.RootElement.GetProperty("logs");
                Assert.AreEqual(2, spoolLogs.GetArrayLength());
                Assert.AreEqual("durable one", spoolLogs[0].GetProperty("renderMessage").GetString());
                Assert.AreEqual("durable two", spoolLogs[1].GetProperty("renderMessage").GetString());
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ShutdownRetriesFailedBatchWithinConfiguredBudget()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            options.Delivery.MaximumBatchWait = TimeSpan.FromMinutes(1);
            options.Delivery.ShutdownTimeout = TimeSpan.FromSeconds(1);
            options.Delivery.ShutdownRetryInterval = TimeSpan.FromMilliseconds(50);
            var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);

            try
            {
                Task<List<string>> received = ReceiveRequestsAsync(listener, HttpStatusCode.ServiceUnavailable, HttpStatusCode.NoContent);
                sink.Emit(CreateLogEvent("shutdown retry"));
                Stopwatch stopwatch = Stopwatch.StartNew();
                await sink.DisposeAsync();
                stopwatch.Stop();

                List<string> bodies = await received.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsLessThan(options.Delivery.ShutdownTimeout, stopwatch.Elapsed);
                Assert.IsGreaterThanOrEqualTo(options.Delivery.ShutdownRetryInterval, stopwatch.Elapsed);
                Assert.AreEqual(0L, GetTotalRowCount(connectionString));
                using JsonDocument first = JsonDocument.Parse(bodies[0]);
                using JsonDocument second = JsonDocument.Parse(bodies[1]);
                Assert.AreEqual(first.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString(),
                    second.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString());
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DataRow(3, 10 * 1024 * 1024, 2)]
        [DataRow(10, 1600 * 1024, 2)]
        [DataRow(1, 10 * 1024 * 1024, 4)]
        [DataRow(10, 600 * 1024, 4)]
        public async Task EmergencyCapacityPreservesActiveRequestAndNewestQueuedEvents(int eventLimit, int byteLimit, int expectedDropped)
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;
            options.EmergencyMemoryBuffer.MaxBufferedEvents = eventLimit;
            options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes = byteLimit;
            options.Delivery.EmergencyMaximumBatchEvents = 1;
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            options.Delivery.RequestTimeout = TimeSpan.FromSeconds(10);
            var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);

            try
            {
                var responseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<string> activeRequest = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent, responseGate: responseGate.Task, onReceived: () => requestStarted.SetResult(true));
                string suffix = new string('x', 256 * 1024);
                sink.Emit(CreateLogEvent($"ram0 {suffix}"));
                await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                for (int index = 1; index <= 4; index++)
                    sink.Emit(CreateLogEvent($"ram{index} {suffix}"));

                Assert.AreEqual((long)expectedDropped, GetPrivateField<long>(sink, "_emergencyDroppedCount"));
                Assert.AreEqual(5L - expectedDropped, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.IsLessThanOrEqualTo((long)byteLimit, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
                HttpStatusCode[] responses = new HttpStatusCode[4 - expectedDropped];
                Array.Fill(responses, HttpStatusCode.NoContent);
                Task<List<string>> received = ReceiveRequestsAsync(listener, responses);
                responseGate.SetResult(true);
                await sink.DisposeAsync();
                List<string> bodies = await received.WaitAsync(TimeSpan.FromSeconds(5));
                bodies.Insert(0, await activeRequest.WaitAsync(TimeSpan.FromSeconds(5)));

                var messages = new List<string>();
                foreach (string body in bodies)
                {
                    using JsonDocument document = JsonDocument.Parse(body);
                    string message = document.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString()!;
                    messages.Add(message[..4]);
                }

                CollectionAssert.AreEqual(expectedDropped == 2 ? new[] { "ram0", "ram3", "ram4" } : new[] { "ram0" }, messages.ToArray());
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DataRow(3, 10 * 1024 * 1024, 2)]
        [DataRow(10, 1600 * 1024, 2)]
        [DataRow(1, 10 * 1024 * 1024, 4)]
        [DataRow(10, 600 * 1024, 4)]
        public async Task EmergencyCapacityEvictsRetryWaitingEventsBeforeNewerQueuedEvents(int eventLimit, int byteLimit, int expectedDropped)
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;
            options.EmergencyMemoryBuffer.MaxBufferedEvents = eventLimit;
            options.EmergencyMemoryBuffer.MaxBufferedPayloadBytes = byteLimit;
            options.Delivery.EmergencyMaximumBatchEvents = 1;
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);

            try
            {
                GetPrivateField<RetryGate>(sink, "_retryGate").RecordFailure(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
                string suffix = new string('x', 256 * 1024);
                sink.Emit(CreateLogEvent($"ram0 {suffix}"));
                await WaitUntilAsync(() => GetPrivateField<System.Collections.ICollection>(sink, "_emergencyRetryEntries").Count > 0, TimeSpan.FromSeconds(5));

                lock (GetPrivateField<object>(sink, "_emergencyBufferLock"))
                {
                    for (int index = 1; index <= 4; index++)
                        sink.Emit(CreateLogEvent($"ram{index} {suffix}"));
                }

                Assert.AreEqual((long)expectedDropped, GetPrivateField<long>(sink, "_emergencyDroppedCount"));
                Assert.AreEqual(5L - expectedDropped, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.IsLessThanOrEqualTo((long)byteLimit, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
                HttpStatusCode[] responses = new HttpStatusCode[5 - expectedDropped];
                Array.Fill(responses, HttpStatusCode.NoContent);
                Task<List<string>> received = ReceiveRequestsAsync(listener, responses);
                await sink.DisposeAsync();
                List<string> bodies = await received.WaitAsync(TimeSpan.FromSeconds(5));

                var messages = new List<string>();
                foreach (string body in bodies)
                {
                    using JsonDocument document = JsonDocument.Parse(body);
                    string message = document.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString()!;
                    messages.Add(message[..4]);
                }

                CollectionAssert.AreEqual(expectedDropped == 2 ? new[] { "ram2", "ram3", "ram4" } : new[] { "ram4" }, messages.ToArray());
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DataRow(3, 8192)]
        [DataRow(800, 70000)]
        public async Task FailedReplacementPreservesBacklogWhenOtherRowsConsumeCapacity(int backlogRows, int messageLength)
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            var options = new SerilogRelayOptions();
            options.ApplicationSpool.MaxPhysicalBytes = backlogRows == 3 ? 64L * 1024L : 256L * 1024L;
            options.Delivery.ShutdownTimeout = TimeSpan.FromMilliseconds(100);
            var sink = new SerilogRelaySink(connectionString, endpoint: null, options);

            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var transaction = connection.BeginTransaction();
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
INSERT INTO SerilogRelayEvents
    (EventId, ApplicationId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate)
VALUES
    ($eventId, 'backlog', 1, 'x', 'Information', 'tiny', 'tiny');
""";
                    command.Parameters.AddWithValue("$eventId", string.Empty);
                    for (int index = 0; index < backlogRows; index++)
                    {
                        command.Parameters["$eventId"].Value = Guid.NewGuid().ToString("D");
                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }

                FillSpoolWithNonReclaimableRows(connectionString);
                sink.Emit(CreateLogEvent(new string('x', messageLength)));
                Assert.AreEqual((long)backlogRows, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ShutdownReturnsAtConfiguredDeadlineWhileSqliteIsLocked()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            var options = new SerilogRelayOptions();
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            options.Delivery.MaximumBatchWait = TimeSpan.FromMinutes(1);
            options.Delivery.ShutdownTimeout = TimeSpan.FromMilliseconds(200);
            var sink = new SerilogRelaySink(connectionString, "http://127.0.0.1:1/logs", options);

            try
            {
                sink.Emit(CreateLogEvent("locked shutdown"));
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE SerilogRelayEvents SET RenderMessage = 'locked';";
                    command.ExecuteNonQuery();

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    await sink.DisposeAsync();
                    stopwatch.Stop();
                    Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100), stopwatch.Elapsed);
                    Assert.IsLessThan(TimeSpan.FromSeconds(1), stopwatch.Elapsed);
                    transaction.Rollback();
                }

                await GetPrivateField<Task>(sink, "_shutdownCleanupTask").WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task EmptyShutdownReturnsWithoutWaitingForItsBudget()
        {
            string directory = CreateTemporaryDirectory();
            var sink = new SerilogRelaySink($"Data Source={Path.Combine(directory, "relay.db")}", endpoint: null, new SerilogRelayOptions());
            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                await sink.DisposeAsync();
                stopwatch.Stop();
                Assert.IsLessThan(TimeSpan.FromMilliseconds(500), stopwatch.Elapsed);
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                DeleteTemporaryDirectory(directory);
            }
        }


        [TestMethod]
        public async Task EventAlreadyInProgressDuringShutdownDoesNotLeakEmergencyReservation()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            var options = new SerilogRelayOptions();
            options.Delivery.ShutdownTimeout = TimeSpan.FromSeconds(1);
            var sink = new SerilogRelaySink(connectionString, endpoint: null, options);
            SemaphoreSlim gate = GetPrivateField<SemaphoreSlim>(sink, "_databaseGate");
            bool gateHeld = false;

            try
            {
                gate.Wait();
                gateHeld = true;
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "DROP TABLE SerilogRelayEvents;";
                    command.ExecuteNonQuery();
                }

                var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task emit = Task.Run(() =>
                {
                    started.SetResult(true);
                    sink.Emit(CreateLogEvent("event already in progress"));
                });
                await started.Task;
                await Task.Delay(50);
                Assert.IsFalse(emit.IsCompleted);

                Task shutdown = sink.DisposeAsync().AsTask();
                await WaitUntilAsync(() => GetPrivateField<object?>(sink, "_shutdownCleanupTask") is not null, TimeSpan.FromSeconds(5));
                gate.Release();
                gateHeld = false;
                await emit.WaitAsync(TimeSpan.FromSeconds(5));
                await shutdown;

                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedPayloadBytes"));
                Assert.AreEqual(1L, GetPrivateField<long>(sink, "_emergencyDroppedCount"));
            }
            finally
            {
                if (gateHeld)
                    gate.Release();
                await DisposeAndWaitForCleanupAsync(sink);
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ShutdownReleasesResourcesWhenDiagnosticOutputFails()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            var sink = new SerilogRelaySink(connectionString, "http://127.0.0.1:1/logs", new SerilogRelayOptions());
            CancellationTokenSource cancellation = GetPrivateField<CancellationTokenSource>(sink, "_cts");

            try
            {
                cancellation.Cancel();
                try
                {
                    await Task.WhenAll(GetPrivateField<Task>(sink, "_senderTask"),
                        GetPrivateField<Task>(sink, "_emergencyTask"), GetPrivateField<Task>(sink, "_applicationSpoolMaintenanceTask"));
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "DROP TABLE SerilogRelayEvents;";
                    command.ExecuteNonQuery();
                }

                int diagnostics = 0;
                SelfLog.Enable(_ =>
                {
                    Interlocked.Increment(ref diagnostics);
                    throw new IOException("diagnostic output unavailable");
                });

                await Assert.ThrowsExactlyAsync<IOException>(async () => await sink.DisposeAsync());
                Assert.IsGreaterThan(0, diagnostics);
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = cancellation.Token);
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(directory);
            }
        }


        [TestMethod]
        public async Task ZeroShutdownBudgetLeavesPendingEventsForNextRun()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            var options = new SerilogRelayOptions();
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            options.Delivery.MaximumBatchWait = TimeSpan.FromMinutes(1);
            options.Delivery.ShutdownTimeout = TimeSpan.Zero;
            var sink = new SerilogRelaySink(connectionString, "http://127.0.0.1:1/logs", options);

            try
            {
                sink.Emit(CreateLogEvent("no shutdown wait"));
                Stopwatch stopwatch = Stopwatch.StartNew();
                await DisposeAndWaitForCleanupAsync(sink);
                stopwatch.Stop();
                Assert.IsLessThan(TimeSpan.FromMilliseconds(500), stopwatch.Elapsed);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.AreEqual(0, GetPrivateField<RetryGate>(sink, "_retryGate").ConsecutiveFailures);
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task PayloadTargetSplitsEscapedJsonAndDrainsEveryEvent(bool shutdown)
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.Delivery.TargetBatchPayloadBytes = 4096;
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            options.Delivery.MaximumBatchWait = TimeSpan.FromMinutes(1);
            string suffix = new string('ä', 100) + "\"\\\n";
            var expected = new List<string>();

            try
            {
                using (var seed = new SerilogRelaySink(connectionString, endpoint: null, options))
                {
                    for (int index = 0; index < 12; index++)
                    {
                        string message = $"event {index} {suffix}";
                        expected.Add(message);
                        seed.Emit(CreateLogEvent(message));
                    }
                }

                Task<List<string>> requests = ReceiveRequestsAsync(listener,
                    HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent,
                    HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent);
                var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);
                try
                {
                    if (shutdown)
                        await sink.DisposeAsync();
                    List<string> bodies = await requests.WaitAsync(TimeSpan.FromSeconds(10));
                    var received = new List<string>();
                    foreach (string body in bodies)
                    {
                        Assert.IsLessThanOrEqualTo(options.Delivery.TargetBatchPayloadBytes, Encoding.UTF8.GetByteCount(body));
                        using JsonDocument document = JsonDocument.Parse(body);
                        JsonElement logs = document.RootElement.GetProperty("logs");
                        Assert.AreEqual(2, logs.GetArrayLength());
                        Assert.AreEqual(logs.GetArrayLength(), document.RootElement.GetProperty("count").GetInt32());
                        foreach (JsonElement entry in logs.EnumerateArray())
                            received.Add(entry.GetProperty("renderMessage").GetString()!);
                    }
                    CollectionAssert.AreEqual(expected, received);
                    await WaitUntilAsync(() => GetTotalRowCount(connectionString) == 0, TimeSpan.FromSeconds(5));
                }
                finally
                {
                    await DisposeAndWaitForCleanupAsync(sink);
                }
            }
            finally
            {
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task SingleOversizedEventIsSentAloneAndDoesNotStrandFollowingEvents(bool emergency)
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            if (emergency)
            {
                options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;
                options.Delivery.EmergencyTargetBatchPayloadBytes = 1024 * 1024;
            }
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            options.Delivery.MaximumBatchWait = TimeSpan.FromMinutes(1);
            Task<List<string>> requests = ReceiveRequestsAsync(listener, HttpStatusCode.NoContent, HttpStatusCode.NoContent);
            var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);
            string oversized = new string('x', 2 * 1024 * 1024);

            try
            {
                sink.Emit(CreateLogEvent(oversized));
                sink.Emit(CreateLogEvent("following event"));
                await sink.DisposeAsync();
                List<string> bodies = await requests.WaitAsync(TimeSpan.FromSeconds(10));
                using JsonDocument first = JsonDocument.Parse(bodies[0]);
                int targetBytes = emergency
                    ? options.Delivery.EmergencyTargetBatchPayloadBytes
                    : options.Delivery.TargetBatchPayloadBytes;
                Assert.IsGreaterThan(targetBytes, Encoding.UTF8.GetByteCount(bodies[0]));
                Assert.AreEqual(1, first.RootElement.GetProperty("count").GetInt32());
                Assert.AreEqual(oversized, first.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString());
                using JsonDocument second = JsonDocument.Parse(bodies[1]);
                Assert.AreEqual("following event", second.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString());
                Assert.AreEqual(0L, GetTotalRowCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DataRow(HttpStatusCode.NoContent)]
        [DataRow(HttpStatusCode.ServiceUnavailable)]
        public async Task ByteFilledBatchSendsBelowMinimumAndReleasesTheUnsentSuffix(HttpStatusCode status)
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = 2;
            options.Delivery.TargetBatchPayloadBytes = 1024;
            options.Delivery.ShutdownTimeout = TimeSpan.Zero;
            var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);

            try
            {
                CancellationTokenSource cancellation = GetPrivateField<CancellationTokenSource>(sink, "_cts");
                cancellation.Cancel();
                try
                {
                    await GetPrivateField<Task>(sink, "_senderTask").WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }

                sink.Emit(CreateLogEvent("first " + new string('x', 100)));
                sink.Emit(CreateLogEvent("second " + new string('x', 100)));
                Task<string> request = ReceiveSingleRequestAsync(listener, status);
                bool delivered = await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", false, CancellationToken.None, false);
                Assert.AreEqual(status == HttpStatusCode.NoContent, delivered);
                string body = await request.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsLessThanOrEqualTo(options.Delivery.TargetBatchPayloadBytes, Encoding.UTF8.GetByteCount(body));
                using JsonDocument document = JsonDocument.Parse(body);
                Assert.AreEqual(1, document.RootElement.GetProperty("count").GetInt32());
                Assert.AreEqual(status == HttpStatusCode.NoContent ? 1L : 2L, GetUnsentCount(connectionString));
                Assert.IsNull(GetClaimOwnerId(connectionString));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task CapacityLimitedSpoolDrainsWithoutFurtherIncomingEvents()
        {
            string connectionString = $"Data Source=reserve-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
            using var keeper = new SqliteConnection(connectionString);
            keeper.Open();
            var options = new SerilogRelayOptions();
            options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;
            options.Delivery.ShutdownTimeout = TimeSpan.Zero;
            var sink = new SerilogRelaySink(connectionString, endpoint: null, options);

            try
            {
                for (int i = 0; i < 300; i++)
                    sink.Emit(CreateLogEvent($"outage event {i}"));

                long evictedBeforeDelivery = GetPrivateField<long>(sink, "_applicationSpoolDroppedCount");
                long pending = GetUnsentCount(connectionString);
                Assert.IsGreaterThan(0L, evictedBeforeDelivery);
                Assert.IsGreaterThan(0L, pending);
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));

                using var receiver = new RecordingReceiverHandler();
                GetPrivateField<HttpClient>(sink, "_httpClient").Dispose();
                SetPrivateField(sink, "_httpClient", new HttpClient(receiver));
                SetPrivateField(sink, "_endpoint", "http://receiver.test/logs");
                Assert.IsTrue(await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, false));

                Assert.AreEqual(pending, (long)receiver.EventIds.Count);
                Assert.AreEqual(0L, GetUnsentCount(connectionString));
                Assert.AreEqual(evictedBeforeDelivery, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
            }
        }

        [TestMethod]
        [DataRow(64 * 1024, 16, false, false)]
        [DataRow(1024 * 1024, 16, false, false)]
        [DataRow(64 * 1024, 1900, true, false)]
        [DataRow(64 * 1024, 1900, false, true)]
        public async Task LegacyFullSpoolMakesDeliveryProgressWithoutNewEvents(int budgetBytes, int messageLength, bool needsReclamation, bool retainedSentEvent)
        {
            string connectionString = $"Data Source=legacy-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
            using var keeper = new SqliteConnection(connectionString);
            keeper.Open();
            var options = new SerilogRelayOptions();
            options.ApplicationSpool.MaxPhysicalBytes = budgetBytes;
            options.Delivery.MinimumBatchEvents = 2;
            options.Delivery.ShutdownTimeout = TimeSpan.Zero;
            var sink = new SerilogRelaySink(connectionString, endpoint: null, options);

            try
            {
                // Raw inserts reproduce a spool written before claim headroom was reserved.
                if (retainedSentEvent)
                {
                    FillLegacySpool(keeper, budgetBytes, messageLength, eventLimit: 1);
                    using var delivered = keeper.CreateCommand();
                    delivered.CommandText = "UPDATE SerilogRelayEvents SET Sent = 1;";
                    delivered.ExecuteNonQuery();
                }
                int originalCount = FillLegacySpool(keeper, budgetBytes, messageLength);
                using var receiver = new RecordingReceiverHandler();
                GetPrivateField<HttpClient>(sink, "_httpClient").Dispose();
                SetPrivateField(sink, "_httpClient", new HttpClient(receiver));
                SetPrivateField(sink, "_endpoint", "http://receiver.test/logs");

                Assert.IsTrue(await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", false, CancellationToken.None, false));
                long evicted = GetPrivateField<long>(sink, "_applicationSpoolDroppedCount");
                Assert.IsGreaterThan(0, receiver.EventIds.Count);
                Assert.IsTrue(receiver.BatchSizes.Contains(1), "A reduced claim must be deliverable below MinimumBatchEvents.");
                Assert.AreEqual(needsReclamation, evicted > 0);
                Assert.AreEqual((long)originalCount, GetUnsentCount(connectionString) + receiver.EventIds.Count + evicted);
                Assert.AreEqual(receiver.EventIds.Count, new HashSet<string>(receiver.EventIds).Count);
                Assert.IsNull(GetClaimOwnerId(connectionString));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
            }
        }

        [TestMethod]
        [DataRow(3)]
        [DataRow(600)]
        public async Task ClaimCapacityFailureRollsBackAllReclamation(int eventCount)
        {
            string connectionString = $"Data Source=claim-failure-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
            using var keeper = new SqliteConnection(connectionString);
            keeper.Open();
            var options = new SerilogRelayOptions();
            options.ApplicationSpool.MaxPhysicalBytes = 1024L * 1024L;
            options.Delivery.ShutdownTimeout = TimeSpan.Zero;
            var sink = new SerilogRelaySink(connectionString, endpoint: null, options);

            try
            {
                FillLegacySpool(keeper, (int)options.ApplicationSpool.MaxPhysicalBytes, 16, eventCount);
                using (var command = keeper.CreateCommand())
                {
                    command.CommandText = """
CREATE TABLE claim_growth(payload BLOB);
CREATE TRIGGER exhaust_claim_capacity
BEFORE UPDATE OF ClaimOwnerId ON SerilogRelayEvents
WHEN NEW.ClaimOwnerId IS NOT NULL
BEGIN
    INSERT INTO claim_growth VALUES (zeroblob(2097152));
END;
""";
                    command.ExecuteNonQuery();
                }

                SqliteException exception = await Assert.ThrowsExactlyAsync<SqliteException>(
                    () => InvokePrivateTaskMethod<ClaimedLogBatch>(sink, "ClaimPendingAsync", 100, CancellationToken.None));
                Assert.AreEqual(SQLitePCL.raw.SQLITE_FULL, exception.SqliteErrorCode);
                Assert.AreEqual((long)eventCount, GetUnsentCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_applicationSpoolDroppedCount"));
                Assert.IsNull(GetClaimOwnerId(connectionString));
                using var count = keeper.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM claim_growth;";
                Assert.AreEqual(0L, Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
            }
        }

        [TestMethod]
        public async Task ShutdownYieldsAnActiveNormalBatchToVolatileDelivery()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;
            options.Delivery.MinimumBatchEvents = 1;
            options.Delivery.MaximumBatchEvents = 1;
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            options.Delivery.MaximumBatchWait = TimeSpan.FromMinutes(1);
            var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);
            var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var responseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            string volatileMessage = "volatile " + new string('v', 128 * 1024);

            try
            {
                Task<string> activeRequest = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent, responseGate: responseGate.Task, onReceived: () => requestStarted.TrySetResult(true));
                sink.Emit(CreateLogEvent("already sending"));
                await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                sink.Emit(CreateLogEvent("durable next"));
                sink.Emit(CreateLogEvent("durable last"));
                sink.Emit(CreateLogEvent(volatileMessage));
                await WaitUntilAsync(() => GetPrivateField<long>(sink, "_emergencyBufferedCount") == 1, TimeSpan.FromSeconds(5));

                Task<List<string>> followingRequests = ReceiveRequestsAsync(listener, HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent);
                Task shutdown = sink.DisposeAsync().AsTask();
                responseGate.TrySetResult(true);
                await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
                await activeRequest.WaitAsync(TimeSpan.FromSeconds(5));
                List<string> bodies = await followingRequests.WaitAsync(TimeSpan.FromSeconds(5));
                using JsonDocument first = JsonDocument.Parse(bodies[0]);
                using JsonDocument second = JsonDocument.Parse(bodies[1]);
                using JsonDocument third = JsonDocument.Parse(bodies[2]);
                Assert.AreEqual(volatileMessage, first.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString());
                Assert.AreEqual("durable next", second.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString());
                Assert.AreEqual("durable last", third.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString());
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                Assert.AreEqual(0L, GetUnsentCount(connectionString));
            }
            finally
            {
                responseGate.TrySetResult(true);
                await DisposeAndWaitForCleanupAsync(sink);
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task NormalSenderLeavesUnstartedBatchesForShutdownDrain()
        {
            string connectionString = $"Data Source=handover-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
            using var keeper = new SqliteConnection(connectionString);
            keeper.Open();
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = 1;
            options.Delivery.ShutdownTimeout = TimeSpan.Zero;
            var sink = new SerilogRelaySink(connectionString, endpoint: null, options);

            try
            {
                sink.Emit(CreateLogEvent("awaiting shutdown drain"));
                using var receiver = new RecordingReceiverHandler();
                GetPrivateField<HttpClient>(sink, "_httpClient").Dispose();
                SetPrivateField(sink, "_httpClient", new HttpClient(receiver));
                SetPrivateField(sink, "_endpoint", "http://receiver.test/logs");
                SetPrivateField(sink, "_disposeStarted", 1);

                Assert.IsFalse(await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, false));
                Assert.AreEqual(0, receiver.EventIds.Count);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.IsNull(GetClaimOwnerId(connectionString));

                Assert.IsTrue(await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, true));
                Assert.AreEqual(1, receiver.EventIds.Count);
                Assert.AreEqual(0L, GetUnsentCount(connectionString));
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
            }
        }

        private static int FillLegacySpool(SqliteConnection connection, int budgetBytes, int messageLength, int? eventLimit = null)
        {
            using var budget = connection.CreateCommand();
            budget.CommandText = $"PRAGMA max_page_count = {budgetBytes / 4096};";
            budget.ExecuteNonQuery();
            using var insert = connection.CreateCommand();
            insert.CommandText = """
INSERT INTO SerilogRelayEvents
    (EventId, ApplicationId, MachineId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties)
VALUES
    ($eventId, 'Relay.Test', $machineId, 1234, '2026-09-30T00:00:00.0000000Z', 'Information', $message, $message, '', '', '', '{}');
""";
            insert.Parameters.AddWithValue("$eventId", "");
            insert.Parameters.AddWithValue("$machineId", new string('a', 64));
            insert.Parameters.AddWithValue("$message", new string('x', messageLength));
            insert.Prepare();
            int inserted = 0;
            while (!eventLimit.HasValue || inserted < eventLimit.Value)
            {
                insert.Parameters["$eventId"].Value = Guid.NewGuid().ToString("D");
                try
                {
                    insert.ExecuteNonQuery();
                    inserted++;
                }
                catch (SqliteException ex) when (!eventLimit.HasValue && ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_FULL)
                {
                    return inserted;
                }
            }
            return inserted;
        }

        private sealed class RecordingReceiverHandler : HttpMessageHandler
        {
            internal List<string> EventIds { get; } = new List<string>();
            internal List<int> BatchSizes { get; } = new List<int>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement logs = document.RootElement.GetProperty("logs");
                BatchSizes.Add(logs.GetArrayLength());
                foreach (JsonElement entry in logs.EnumerateArray())
                    EventIds.Add(entry.GetProperty("eventId").GetString()!);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task DelayedHttpResponseTimesOutReleasesClaimAndRetriesStableEvent(bool waitAfterResponseHeaders)
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = 1;
            options.Delivery.ShutdownTimeout = TimeSpan.Zero;
            options.EndpointRetry.JitterRatio = 0d;
            var sink = new SerilogRelaySink(connectionString, endpoint: null, options);
            var responseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var receivedBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string>? heldRequest = null;

            try
            {
                Assert.AreEqual(TimeSpan.FromSeconds(2), options.Delivery.RequestTimeout);
                sink.Emit(CreateLogEvent("receiver still working"));
                SetPrivateField(sink, "_endpoint", $"http://127.0.0.1:{port}/logs");
                // HTTP 204 has no body; use an unacknowledged 200 response for the stalled-body case.
                heldRequest = ReceiveSingleRequestAsync(listener,
                    waitAfterResponseHeaders ? HttpStatusCode.OK : HttpStatusCode.NoContent, responseGate: responseGate.Task,
                    onBodyReceived: body => receivedBody.TrySetResult(body), waitAfterResponseHeaders: waitAfterResponseHeaders);

                Stopwatch stopwatch = Stopwatch.StartNew();
                Task<bool> firstAttempt = InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, false);
                string originalBody = await receivedBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsNotNull(GetClaimOwnerId(connectionString));
                Assert.IsFalse(await firstAttempt.WaitAsync(TimeSpan.FromSeconds(6)));
                stopwatch.Stop();
                Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.5), stopwatch.Elapsed);
                Assert.IsLessThan(TimeSpan.FromSeconds(5), stopwatch.Elapsed);
                Assert.IsFalse(heldRequest.IsCompleted);
                Assert.AreEqual(1L, GetUnsentCount(connectionString));
                Assert.IsNull(GetClaimOwnerId(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));

                RetryGate retry = GetPrivateField<RetryGate>(sink, "_retryGate");
                Assert.AreEqual(1, retry.ConsecutiveFailures);
                Assert.IsNotNull(retry.NextAttemptAt);
                Assert.IsGreaterThan(DateTimeOffset.UtcNow, retry.NextAttemptAt.Value);
                Assert.IsFalse(await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, false));
                Assert.IsFalse(listener.Pending());

                // Check that timeout released the HTTP gate, then advance the test past backoff.
                Assert.IsTrue(retry.TryAcquire(retry.NextAttemptAt.Value));
                retry.RecordSuccess();
                Task<string> secondRequest = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent);
                Assert.IsTrue(await InvokePrivateTaskMethod<bool>(sink, "ProcessPendingAsync", true, CancellationToken.None, false));
                string retryBody = await secondRequest.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsFalse(heldRequest.IsCompleted, "The receiver may still be processing the timed-out first attempt.");
                Assert.AreEqual(0L, GetTotalRowCount(connectionString));
                Assert.AreEqual(0, retry.ConsecutiveFailures);
                using JsonDocument original = JsonDocument.Parse(originalBody);
                using JsonDocument repeated = JsonDocument.Parse(retryBody);
                Assert.AreEqual(original.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString(),
                    repeated.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString());
                Assert.AreNotEqual(original.RootElement.GetProperty("batchId").GetString(), repeated.RootElement.GetProperty("batchId").GetString());

                responseGate.TrySetResult(true);
                try
                {
                    await heldRequest.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (IOException)
                {
                    // A late response can fail to write because HttpClient canceled the request.
                }
                Assert.AreEqual(0L, GetTotalRowCount(connectionString));
            }
            finally
            {
                responseGate.TrySetResult(true);
                listener.Stop();
                if (heldRequest is not null)
                {
                    try
                    {
                        await heldRequest.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception exception) when (exception is IOException or SocketException)
                    {
                    }
                }
                await DisposeAndWaitForCleanupAsync(sink);
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ShutdownRetriesHangingResponsesAtOneSecondIntervals(bool emergency)
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            if (emergency)
                options.ApplicationSpool.MaxPhysicalBytes = 64L * 1024L;
            var sink = new SerilogRelaySink(connectionString, endpoint: null, options);
            var responseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string>? firstRequest = null;
            Task<string>? secondRequest = null;

            try
            {
                // Keep normal delivery out of the way; exercise the public shutdown path.
                GetPrivateField<RetryGate>(sink, "_retryGate").RecordFailure(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1));
                sink.Emit(CreateLogEvent(emergency ? new string('v', 128 * 1024) : "durable shutdown timeout"));
                Assert.AreEqual(emergency ? 1L : 0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                SetPrivateField(sink, "_endpoint", $"http://127.0.0.1:{port}/logs");
                firstRequest = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent, responseGate: responseGate.Task,
                    onBodyReceived: body => firstBody.TrySetResult(body));
                Stopwatch stopwatch = Stopwatch.StartNew();
                Task shutdown = sink.DisposeAsync().AsTask();
                string first = await firstBody.Task.WaitAsync(TimeSpan.FromSeconds(2));
                TimeSpan firstStarted = stopwatch.Elapsed;

                secondRequest = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent, responseGate: responseGate.Task,
                    onBodyReceived: body => secondBody.TrySetResult(body));
                string second = await secondBody.Task.WaitAsync(TimeSpan.FromSeconds(2));
                TimeSpan secondStarted = stopwatch.Elapsed;
                Task<string> thirdRequest = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent);
                string third = await thirdRequest.WaitAsync(TimeSpan.FromSeconds(2));
                TimeSpan thirdStarted = stopwatch.Elapsed;
                await shutdown.WaitAsync(TimeSpan.FromSeconds(4));

                Assert.IsFalse(firstRequest.IsCompleted);
                Assert.IsFalse(secondRequest.IsCompleted);
                Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(800), secondStarted - firstStarted);
                Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(800), thirdStarted - secondStarted);
                Assert.IsLessThan(options.Delivery.ShutdownTimeout, thirdStarted);
                Assert.AreEqual(0L, GetTotalRowCount(connectionString));
                Assert.AreEqual(0L, GetPrivateField<long>(sink, "_emergencyBufferedCount"));
                using JsonDocument firstDocument = JsonDocument.Parse(first);
                using JsonDocument secondDocument = JsonDocument.Parse(second);
                using JsonDocument thirdDocument = JsonDocument.Parse(third);
                string? eventId = firstDocument.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString();
                Assert.AreEqual(eventId, secondDocument.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString());
                Assert.AreEqual(eventId, thirdDocument.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString());
            }
            finally
            {
                responseGate.TrySetResult(true);
                listener.Stop();
                foreach (Task<string>? request in new[] { firstRequest, secondRequest })
                {
                    if (request is null)
                        continue;
                    try
                    {
                        await request.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception exception) when (exception is IOException or SocketException)
                    {
                    }
                }
                await DisposeAndWaitForCleanupAsync(sink);
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ShutdownShortensAnAlreadyRunningNormalHttpRequest()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = 1;
            options.Delivery.PollInterval = TimeSpan.FromMinutes(1);
            options.Delivery.ShutdownRequestTimeout = TimeSpan.FromMilliseconds(200);
            options.Delivery.ShutdownRetryInterval = TimeSpan.FromMilliseconds(200);
            var sink = new SerilogRelaySink(connectionString, $"http://127.0.0.1:{port}/logs", options);
            var responseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var receivedBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string>? activeRequest = null;

            try
            {
                activeRequest = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent, responseGate: responseGate.Task,
                    onBodyReceived: body => receivedBody.TrySetResult(body));
                sink.Emit(CreateLogEvent("already running before shutdown"));
                string originalBody = await receivedBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(350);
                Assert.AreEqual(0, GetPrivateField<RetryGate>(sink, "_retryGate").ConsecutiveFailures);
                Assert.IsNotNull(GetClaimOwnerId(connectionString));

                Task<string> repeatedRequest = ReceiveSingleRequestAsync(listener, HttpStatusCode.NoContent);
                Stopwatch stopwatch = Stopwatch.StartNew();
                await sink.DisposeAsync();
                stopwatch.Stop();
                string repeatedBody = await repeatedRequest.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsGreaterThanOrEqualTo(options.Delivery.ShutdownRequestTimeout, stopwatch.Elapsed);
                Assert.IsLessThan(TimeSpan.FromSeconds(1.3), stopwatch.Elapsed);
                Assert.IsFalse(activeRequest.IsCompleted);
                Assert.AreEqual(0L, GetTotalRowCount(connectionString));
                using JsonDocument original = JsonDocument.Parse(originalBody);
                using JsonDocument repeated = JsonDocument.Parse(repeatedBody);
                Assert.AreEqual(original.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString(),
                    repeated.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString());
            }
            finally
            {
                responseGate.TrySetResult(true);
                listener.Stop();
                if (activeRequest is not null)
                {
                    try
                    {
                        await activeRequest.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception exception) when (exception is IOException or SocketException)
                    {
                    }
                }
                await DisposeAndWaitForCleanupAsync(sink);
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task ShutdownPacesFailureAfterAnEarlierBatchSucceeded()
        {
            string directory = CreateTemporaryDirectory();
            string connectionString = $"Data Source={Path.Combine(directory, "relay.db")}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = new SerilogRelayOptions();
            options.Delivery.MinimumBatchEvents = 1;
            options.Delivery.MaximumBatchEvents = 1;
            options.Delivery.ShutdownRetryInterval = TimeSpan.FromMilliseconds(200);
            var sink = new SerilogRelaySink(connectionString, endpoint: null, options);

            try
            {
                sink.Emit(CreateLogEvent("first batch succeeds"));
                sink.Emit(CreateLogEvent("second batch needs retry"));
                SetPrivateField(sink, "_endpoint", $"http://127.0.0.1:{port}/logs");
                Stopwatch stopwatch = Stopwatch.StartNew();
                var requestTimes = new List<TimeSpan>();
                async Task<List<string>> ReceiveAsync()
                {
                    var bodies = new List<string>();
                    foreach (HttpStatusCode status in new[] { HttpStatusCode.NoContent, HttpStatusCode.ServiceUnavailable, HttpStatusCode.NoContent })
                        bodies.Add(await ReceiveSingleRequestAsync(listener, status, onReceived: () => requestTimes.Add(stopwatch.Elapsed)));
                    return bodies;
                }
                Task<List<string>> received = ReceiveAsync();
                await sink.DisposeAsync();
                List<string> bodies = await received.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180), requestTimes[2] - requestTimes[1]);
                Assert.AreEqual(0L, GetTotalRowCount(connectionString));
                using JsonDocument failed = JsonDocument.Parse(bodies[1]);
                using JsonDocument repeated = JsonDocument.Parse(bodies[2]);
                Assert.AreEqual(failed.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString(),
                    repeated.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString());
            }
            finally
            {
                await DisposeAndWaitForCleanupAsync(sink);
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        private static async Task DisposeAndWaitForCleanupAsync(SerilogRelaySink sink)
        {
            await sink.DisposeAsync();
            await GetPrivateField<Task>(sink, "_shutdownCleanupTask").WaitAsync(TimeSpan.FromSeconds(5));
        }

        private static long GetTotalRowCount(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM SerilogRelayEvents;";
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static async Task<List<string>> ReceiveRequestsAsync(TcpListener listener, params HttpStatusCode[] statuses)
        {
            var bodies = new List<string>();
            foreach (HttpStatusCode status in statuses)
                bodies.Add(await ReceiveSingleRequestAsync(listener, status));
            return bodies;
        }

        private static void FillSpoolWithNonReclaimableRows(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            int sequence = 0;
            foreach (int payloadSize in new[] { 4096, 512, 64, 1 })
            {
                while (true)
                {
                    try
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText = """
INSERT INTO SerilogRelayEvents
    (EventId, ApplicationId, ProcessId, Timestamp, Level, RenderMessage, MessageTemplate, Sent)
VALUES
    ($eventId, 'capacity-filler', 0, 'x', 'Information', $payload, $payload, 2);
""";
                        command.Parameters.AddWithValue("$eventId", $"capacity-filler-{sequence++}");
                        command.Parameters.AddWithValue("$payload", new string('f', payloadSize));
                        command.ExecuteNonQuery();
                    }
                    catch (SqliteException ex) when (ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_FULL)
                    {
                        break;
                    }
                }
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
                statusCode == HttpStatusCode.NoContent ? "No Content" : "Error";
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

            string reason = statusCode == HttpStatusCode.NoContent ? "No Content" : "Error";
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)statusCode} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.FlushAsync();
            return authorization;
        }

        private static async Task<string> ReceiveSingleRequestAsync(
            TcpListener listener,
            HttpStatusCode statusCode,
            string? extraHeaders = null,
            Task? responseGate = null,
            Action? onReceived = null,
            Action<string>? onBodyReceived = null,
            bool waitAfterResponseHeaders = false)
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
            onBodyReceived?.Invoke(body);
            onReceived?.Invoke();
            if (!waitAfterResponseHeaders && responseGate is not null)
                await responseGate;
            string reason = statusCode == HttpStatusCode.NoContent ? "No Content" : "Error";
            int responseLength = waitAfterResponseHeaders ? 1 : 0;
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)statusCode} {reason}\r\n{extraHeaders ?? string.Empty}Content-Length: {responseLength}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.FlushAsync();
            if (waitAfterResponseHeaders)
            {
                if (responseGate is not null)
                    await responseGate;
                await stream.WriteAsync(new byte[] { (byte)'x' });
                await stream.FlushAsync();
            }
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
            // Bounded Dispose can return before an already-running SQLite call releases its file.
            for (int attempt = 0; Directory.Exists(directory); attempt++)
            {
                SqliteConnection.ClearAllPools();
                try
                {
                    Directory.Delete(directory, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 100)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
