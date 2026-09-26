using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
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
    public class SerilogRelaySinkTests
    {
        [TestMethod]
        public void ConstructorRejectsInvalidConfiguration()
        {
            const string connectionString = "Data Source=:memory:";

            Assert.ThrowsExactly<ArgumentException>(() => new SerilogRelaySink(
                connectionString,
                string.Empty,
                endpoint: null,
                minBatchItems: 1,
                maxBatchItems: 10,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(3)));

            Assert.ThrowsExactly<ArgumentException>(() => new SerilogRelaySink(
                connectionString,
                "invalid-table",
                endpoint: null,
                minBatchItems: 1,
                maxBatchItems: 10,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(3)));

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SerilogRelaySink(
                connectionString,
                "logs",
                endpoint: null,
                minBatchItems: 0,
                maxBatchItems: 10,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(3)));

            Assert.ThrowsExactly<ArgumentException>(() => new SerilogRelaySink(
                connectionString,
                "logs",
                endpoint: null,
                minBatchItems: 11,
                maxBatchItems: 10,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(3)));
        }

        [TestMethod]
        public void WriteToExtensionPersistsLocallyWithoutEndpoint()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";

            try
            {
                using (Logger logger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(
                        connectionString,
                        "logs",
                        endpoint: null,
                        minimumBatchSize: 20,
                        maximumBatchSize: 100,
                        baseInterval: TimeSpan.FromMilliseconds(20))
                    .CreateLogger())
                {
                    logger.Information("Hello {Value}", 42);
                }

                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT EventId, Level, RenderMessage, MessageTemplate, Properties, Sent FROM logs ORDER BY Id LIMIT 1;";

                using SqliteDataReader reader = command.ExecuteReader();
                Assert.IsTrue(reader.Read());
                Assert.IsTrue(Guid.TryParseExact(reader.GetString(0), "D", out _));
                Assert.AreEqual("Information", reader.GetString(1));
                Assert.AreEqual("Hello 42", reader.GetString(2));
                Assert.AreEqual("Hello {Value}", reader.GetString(3));
                StringAssert.Contains(reader.GetString(4), "\"Value\":\"42\"");
                Assert.AreEqual(0L, reader.GetInt64(5));
                Assert.IsFalse(reader.Read());
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task BackgroundSenderPostsBatchAndMarksRowSent()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);

            try
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<string> requestTask = ReceiveSingleRequestAsync(listener, HttpStatusCode.OK);

                var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    $"http://127.0.0.1:{port}/logs",
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                using Logger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
                logger.Information("Relay {Value}", 7);

                string body = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
                using JsonDocument document = JsonDocument.Parse(body);
                Assert.AreEqual(1, document.RootElement.GetProperty("count").GetInt32());
                Assert.AreEqual(1, document.RootElement.GetProperty("protocolVersion").GetInt32());
                Assert.IsTrue(Guid.TryParseExact(document.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString(), "D", out _));
                Assert.AreEqual("Relay 7", document.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString());

                await WaitForSentStateAsync(connectionString, expectedSent: 1L, TimeSpan.FromSeconds(5));
                await Task.Delay(200);
            }
            finally
            {
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task DisposeFlushesPendingRowsBelowMinimumBatchSize()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);

            try
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<string> requestTask = ReceiveSingleRequestAsync(listener, HttpStatusCode.OK);

                var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    $"http://127.0.0.1:{port}/logs",
                    minBatchItems: 20,
                    maxBatchItems: 100,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                using (Logger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger())
                {
                    logger.Information("Flush {Value}", 9);
                }

                string body = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
                using JsonDocument document = JsonDocument.Parse(body);
                Assert.AreEqual(1, document.RootElement.GetProperty("count").GetInt32());
                Assert.AreEqual("Flush 9", document.RootElement.GetProperty("logs")[0].GetProperty("renderMessage").GetString());

                await WaitForSentStateAsync(connectionString, expectedSent: 1L, TimeSpan.FromSeconds(5));
                await Task.Delay(200);
            }
            finally
            {
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }



        [TestMethod]
        public async Task LegacySpoolMigrationAssignsStableEventIds()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "legacy.db");
            string connectionString = $"Data Source={databasePath}";

            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
CREATE TABLE logs (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
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
);
INSERT INTO logs (Timestamp, Level, RenderMessage, MessageTemplate, Sent)
VALUES ('legacy', 'Information', 'legacy', 'legacy', 0);";
                    command.ExecuteNonQuery();
                }

                string firstEventId;
                await using (var firstSink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    endpoint: null,
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3)))
                {
                    firstEventId = ReadSingleEventId(connectionString);
                    Assert.IsTrue(Guid.TryParseExact(firstEventId, "D", out _));
                }

                await using (var secondSink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    endpoint: null,
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3)))
                {
                    Assert.AreEqual(firstEventId, ReadSingleEventId(connectionString));
                }
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task DisposeIsIdempotentAcrossSyncAndAsyncCallers()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";

            try
            {
                var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    endpoint: null,
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                SetPrivateField(
                    sink,
                    "_senderTask",
                    Task.FromCanceled(new CancellationToken(canceled: true)));

                Task firstDispose = sink.DisposeAsync().AsTask();
                Task secondDispose = sink.DisposeAsync().AsTask();
                await Task.WhenAll(firstDispose, secondDispose);

                sink.Dispose();

                Assert.ThrowsExactly<ObjectDisposedException>(
                    () => sink.Emit(CreateLogEvent("after dispose")));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public void ExplicitOptionalSettingsAndRetentionOffsetsAreSupported()
        {
            string directory = CreateTemporaryDirectory();
            string explicitDatabasePath = Path.Combine(directory, "explicit.db");
            string defaultDatabasePath = Path.Combine(directory, "defaults.db");

            try
            {
                using (Logger explicitLogger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.SerilogRelay(
                        $"Data Source={explicitDatabasePath}",
                        "logs",
                        endpoint: null,
                        minimumBatchSize: 2,
                        maximumBatchSize: 3,
                        baseInterval: TimeSpan.FromMilliseconds(17),
                        sentRetention: new TimeSpan(1, 2, 3, 4),
                        unsentRetention: TimeSpan.FromHours(2),
                        restrictedToMinimumLevel: LogEventLevel.Verbose)
                    .CreateLogger())
                {
                }

                using (Logger defaultLogger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay($"Data Source={defaultDatabasePath}", "logs")
                    .CreateLogger())
                {
                }

                string compositeOffset = InvokeBuildSqliteOffset(new TimeSpan(1, 2, 3, 4));
                Assert.AreEqual("-93784 seconds", compositeOffset);
                Assert.AreEqual("0 seconds", InvokeBuildSqliteOffset(TimeSpan.Zero));

                using var offsetConnection = new SqliteConnection("Data Source=:memory:");
                offsetConnection.Open();
                using var offsetCommand = offsetConnection.CreateCommand();
                offsetCommand.CommandText = "SELECT datetime('2026-01-02 03:04:05', $offset);";
                offsetCommand.Parameters.AddWithValue("$offset", compositeOffset);
                Assert.AreEqual(
                    "2026-01-01 01:01:01",
                    Convert.ToString(offsetCommand.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public void EmitPersistsTraceSpanExceptionAndMultipleProperties()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";

            try
            {
                using var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    endpoint: null,
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromHours(2),
                    TimeSpan.FromMinutes(30));

                ActivityTraceId traceId = ActivityTraceId.CreateRandom();
                ActivitySpanId spanId = ActivitySpanId.CreateRandom();
                var exception = new InvalidOperationException("expected failure");
                MessageTemplate template = new MessageTemplateParser().Parse("Failure {One} {Two}");
                var properties = new[]
                {
                    new LogEventProperty("One", new ScalarValue("line\n\"quoted\"\\slash\u0001")),
                    new LogEventProperty("Two", new ScalarValue(2)),
                };
                var logEvent = new LogEvent(
                    DateTimeOffset.UtcNow,
                    LogEventLevel.Error,
                    exception,
                    template,
                    properties,
                    traceId,
                    spanId);

                sink.Emit(logEvent);

                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT TraceId, SpanId, Exception, Properties FROM logs ORDER BY Id LIMIT 1;";
                using SqliteDataReader reader = command.ExecuteReader();

                Assert.IsTrue(reader.Read());
                Assert.AreEqual(traceId.ToHexString(), reader.GetString(0));
                Assert.AreEqual(spanId.ToHexString(), reader.GetString(1));
                StringAssert.Contains(reader.GetString(2), "expected failure");

                using JsonDocument propertiesDocument = JsonDocument.Parse(reader.GetString(3));
                Assert.IsTrue(propertiesDocument.RootElement.TryGetProperty("One", out _));
                Assert.IsTrue(propertiesDocument.RootElement.TryGetProperty("Two", out _));
                Assert.AreEqual(
                    properties[0].Value.ToString(),
                    propertiesDocument.RootElement.GetProperty("One").GetString());
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public void BusyErrorClassificationMatchesSqliteCodes()
        {
            Assert.IsTrue(InvokeIsBusyError(new SqliteException("busy", 5)));
            Assert.IsTrue(InvokeIsBusyError(new SqliteException("locked", 6)));
            Assert.IsFalse(InvokeIsBusyError(new SqliteException("other", 1)));
        }

        [TestMethod]
        public async Task ProcessPendingShortCircuitsAndDefendsMinimumBatchInvariant()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";

            try
            {
                await using (var noEndpointSink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    endpoint: null,
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3)))
                {
                    Assert.IsFalse(await InvokeProcessPendingAsync(noEndpointSink, ignoreMinBatch: true, CancellationToken.None));
                }

                int closedPort = ReserveAndReleasePort();
                await using var thresholdSink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    $"http://127.0.0.1:{closedPort}/logs",
                    minBatchItems: 2,
                    maxBatchItems: 10,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                Assert.IsFalse(await InvokeProcessPendingAsync(thresholdSink, ignoreMinBatch: false, CancellationToken.None));

                SetPrivateField(thresholdSink, "_pendingCount", 2L);
                Assert.IsFalse(await InvokeProcessPendingAsync(thresholdSink, ignoreMinBatch: false, CancellationToken.None));

                InsertRawLog(connectionString, nullOptionals: false);
                SetPrivateField(thresholdSink, "_pendingCount", 2L);
                Assert.IsFalse(await InvokeProcessPendingAsync(thresholdSink, ignoreMinBatch: false, CancellationToken.None));

                SetPrivateField(thresholdSink, "_pendingCount", 0L);
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DoNotParallelize]
        public async Task SendBatchHandlesServerErrorsRetryAfterAndConnectionFailure()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            var entries = new List<LogEntry> { CreateLogEntry(1, "HTTP test") };
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            using var selfLog = new StringWriter(CultureInfo.InvariantCulture);
            SelfLog.Enable(selfLog);

            try
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                await using var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    $"http://127.0.0.1:{port}/logs",
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                Task<string> serverErrorRequest = ReceiveSingleRequestAsync(listener, HttpStatusCode.InternalServerError);
                Assert.IsFalse(await InvokeSendBatchAsync(sink, entries, CancellationToken.None));
                string firstAttemptBody = await serverErrorRequest.WaitAsync(TimeSpan.FromSeconds(5));
                StringAssert.Contains(selfLog.ToString(), "HTTP relay returned status code 500.");

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    OperationCanceledException? cancellationException = null;
                    try
                    {
                        await InvokeSendBatchAsync(sink, entries, cancellation.Token);
                    }
                    catch (OperationCanceledException ex)
                    {
                        cancellationException = ex;
                    }

                    Assert.IsNotNull(cancellationException);
                }

                Task<string> throttledWithoutRetryAfter = ReceiveSingleRequestAsync(listener, HttpStatusCode.TooManyRequests);
                Assert.IsFalse(await InvokeSendBatchAsync(sink, entries, CancellationToken.None));
                _ = await throttledWithoutRetryAfter.WaitAsync(TimeSpan.FromSeconds(5));

                Task<string> retryRequest = ReceiveSingleRequestAsync(
                    listener,
                    HttpStatusCode.TooManyRequests,
                    "Retry-After: 1\r\n");
                Assert.IsFalse(await InvokeSendBatchAsync(sink, entries, CancellationToken.None));
                string retryAttemptBody = await retryRequest.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(TimeSpan.FromSeconds(1), GetPrivateField<TimeSpan>(sink, "_currentInterval"));

                using JsonDocument firstAttempt = JsonDocument.Parse(firstAttemptBody);
                using JsonDocument retryAttempt = JsonDocument.Parse(retryAttemptBody);
                Assert.AreEqual(1, firstAttempt.RootElement.GetProperty("protocolVersion").GetInt32());
                Assert.AreEqual(entries[0].EventId, firstAttempt.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString());
                Assert.AreEqual(entries[0].EventId, retryAttempt.RootElement.GetProperty("logs")[0].GetProperty("eventId").GetString());
                Assert.AreNotEqual(
                    firstAttempt.RootElement.GetProperty("batchId").GetString(),
                    retryAttempt.RootElement.GetProperty("batchId").GetString());
            }
            finally
            {
                SelfLog.Disable();
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }

            string failureDirectory = CreateTemporaryDirectory();
            using var failureSelfLog = new StringWriter(CultureInfo.InvariantCulture);
            SelfLog.Enable(failureSelfLog);
            try
            {
                int closedPort = ReserveAndReleasePort();
                await using var failureSink = new SerilogRelaySink(
                    $"Data Source={Path.Combine(failureDirectory, "relay.db")}",
                    "logs",
                    $"http://127.0.0.1:{closedPort}/logs",
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                Assert.IsFalse(await InvokeSendBatchAsync(failureSink, entries, CancellationToken.None));
                StringAssert.Contains(failureSelfLog.ToString(), "HTTP relay request failed:");
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(failureDirectory);
            }
        }

        [TestMethod]
        [DoNotParallelize]
        public void EmitReportsNonBusySqliteFailureWithoutThrowing()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            using var selfLog = new StringWriter(CultureInfo.InvariantCulture);
            SelfLog.Enable(selfLog);

            try
            {
                using var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    endpoint: null,
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "DROP TABLE logs;";
                    command.ExecuteNonQuery();
                }

                sink.Emit(CreateLogEvent("will fail"));

                StringAssert.Contains(selfLog.ToString(), "Failed to write log to SQLite");
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task EmitRetriesWhenSqliteIsBusy()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath};Default Timeout=1";

            try
            {
                using var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    endpoint: null,
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                using var blocker = new SqliteConnection(connectionString);
                blocker.Open();
                using var transaction = blocker.BeginTransaction();
                using (var command = blocker.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO logs (Timestamp, Level, RenderMessage, MessageTemplate, Sent) VALUES ('x','Information','blocker','blocker',0);";
                    command.ExecuteNonQuery();
                }

                Task emitTask = Task.Run(() => sink.Emit(CreateLogEvent("after busy")));
                await Task.Delay(TimeSpan.FromMilliseconds(7000));
                transaction.Commit();
                await emitTask.WaitAsync(TimeSpan.FromSeconds(5));

                using var verifyConnection = new SqliteConnection(connectionString);
                verifyConnection.Open();
                using var verify = verifyConnection.CreateCommand();
                verify.CommandText = "SELECT COUNT(*) FROM logs WHERE RenderMessage = 'after busy';";
                Assert.AreEqual(1L, Convert.ToInt64(verify.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task RestartRecoveryDrainsBacklogIncludingNullLegacyColumns()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);

            try
            {
                using (Logger logger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(connectionString, "logs", endpoint: null)
                    .CreateLogger())
                {
                    for (int index = 0; index < 6; index++)
                    {
                        logger.Information("Backlog {Index}", index);
                    }
                }

                InsertRawLog(connectionString, nullOptionals: true);

                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<List<string>> serverTask = ReceiveRequestsAsync(listener, 7, HttpStatusCode.OK);

                await using var relay = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    $"http://127.0.0.1:{port}/logs",
                    minBatchItems: 1,
                    maxBatchItems: 1,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                await WaitForUnsentCountAsync(connectionString, 0L, TimeSpan.FromSeconds(10));
                List<string> bodies = await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual(7, bodies.Count);
                await Task.Delay(150);
            }
            finally
            {
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        public async Task FailedShutdownFlushRetriesUntilDeadline()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            int closedPort = ReserveAndReleasePort();

            try
            {
                var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    $"http://127.0.0.1:{closedPort}/logs",
                    minBatchItems: 20,
                    maxBatchItems: 100,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                sink.Emit(CreateLogEvent("pending shutdown"));

                Stopwatch stopwatch = Stopwatch.StartNew();
                await sink.DisposeAsync();
                stopwatch.Stop();

                Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(2), stopwatch.Elapsed);

                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Sent FROM logs ORDER BY Id LIMIT 1;";
                Assert.AreEqual(0L, Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestMethod]
        [DoNotParallelize]
        public async Task SenderLoopReportsDatabaseFailure()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            int closedPort = ReserveAndReleasePort();
            using var selfLog = new StringWriter(CultureInfo.InvariantCulture);

            try
            {
                var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    $"http://127.0.0.1:{closedPort}/logs",
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "DROP TABLE logs;";
                    command.ExecuteNonQuery();
                }

                SelfLog.Enable(selfLog);
                SetPrivateField(sink, "_pendingCount", 1L);
                SetPrivateField(sink, "_hasNewLogs", true);

                await WaitUntilAsync(
                    () => selfLog.ToString().Contains("Error in sender loop", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(3));

                await Task.Delay(100);

                SetPrivateField(sink, "_pendingCount", 0L);
                using (var repairConnection = new SqliteConnection(connectionString))
                {
                    repairConnection.Open();
                    using var repairCommand = repairConnection.CreateCommand();
                    repairCommand.CommandText = "CREATE TABLE logs (Sent INTEGER NOT NULL DEFAULT 0);";
                    repairCommand.ExecuteNonQuery();
                }
                await sink.DisposeAsync();
            }
            finally
            {
                SelfLog.Disable();
                DeleteTemporaryDirectory(directory);
            }
        }



        [TestMethod]
        public async Task SenderLoopHandlesCancellationDuringPostBatchDelay()
        {
            string directory = CreateTemporaryDirectory();
            string databasePath = Path.Combine(directory, "relay.db");
            string connectionString = $"Data Source={databasePath}";
            using var listener = new TcpListener(IPAddress.Loopback, 0);

            try
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<string> requestTask = ReceiveSingleRequestAsync(listener, HttpStatusCode.OK);

                var sink = new SerilogRelaySink(
                    connectionString,
                    "logs",
                    $"http://127.0.0.1:{port}/logs",
                    minBatchItems: 1,
                    maxBatchItems: 10,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3));

                sink.Emit(CreateLogEvent("cancel after send"));
                _ = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
                await WaitForSentStateAsync(connectionString, expectedSent: 1L, TimeSpan.FromSeconds(5));

                CancellationTokenSource cancellation = GetPrivateField<CancellationTokenSource>(sink, "_cts");
                cancellation.Cancel();

                Task senderTask = GetPrivateField<Task>(sink, "_senderTask");
                await senderTask.WaitAsync(TimeSpan.FromSeconds(5));

                SetPrivateField(sink, "_pendingCount", 0L);
                await sink.DisposeAsync();
            }
            finally
            {
                listener.Stop();
                DeleteTemporaryDirectory(directory);
            }
        }

        private static string InvokeBuildSqliteOffset(TimeSpan span)
        {
            MethodInfo method = GetPrivateMethod("BuildSqliteOffset", isStatic: true);
            return (string)method.Invoke(null, new object[] { span })!;
        }

        private static bool InvokeIsBusyError(SqliteException exception)
        {
            MethodInfo method = GetPrivateMethod("IsBusyError", isStatic: true);
            return (bool)method.Invoke(null, new object[] { exception })!;
        }

        private static async Task<bool> InvokeProcessPendingAsync(
            SerilogRelaySink sink,
            bool ignoreMinBatch,
            CancellationToken cancellationToken)
        {
            MethodInfo method = GetPrivateMethod("ProcessPendingAsync", isStatic: false);
            var task = (Task<bool>)method.Invoke(sink, new object[] { cancellationToken, ignoreMinBatch })!;
            return await task;
        }

        private static async Task<bool> InvokeSendBatchAsync(
            SerilogRelaySink sink,
            List<LogEntry> entries,
            CancellationToken cancellationToken)
        {
            MethodInfo method = GetPrivateMethod("SendBatchAsync", isStatic: false);
            var task = (Task<bool>)method.Invoke(sink, new object[] { entries, cancellationToken })!;
            return await task;
        }

        private static MethodInfo GetPrivateMethod(string name, bool isStatic)
        {
            BindingFlags flags = BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            return typeof(SerilogRelaySink).GetMethod(name, flags)
                ?? throw new MissingMethodException(typeof(SerilogRelaySink).FullName, name);
        }

        private static void SetPrivateField<T>(SerilogRelaySink sink, string name, T value)
        {
            FieldInfo field = typeof(SerilogRelaySink).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(SerilogRelaySink).FullName, name);
            field.SetValue(sink, value);
        }

        private static T GetPrivateField<T>(SerilogRelaySink sink, string name)
        {
            FieldInfo field = typeof(SerilogRelaySink).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(SerilogRelaySink).FullName, name);
            return (T)field.GetValue(sink)!;
        }


        private static string ReadSingleEventId(string connectionString)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EventId FROM logs ORDER BY Id LIMIT 1;";
            return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
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

        private static LogEntry CreateLogEntry(long id, string renderedMessage)
        {
            return new LogEntry
            {
                Id = id,
                EventId = Guid.NewGuid().ToString("D"),
                Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Level = LogEventLevel.Information.ToString(),
                RenderMessage = renderedMessage,
                MessageTemplate = renderedMessage,
                Properties = "{}",
            };
        }

        private static void InsertRawLog(string connectionString, bool nullOptionals)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            string eventId = Guid.NewGuid().ToString("D");
            command.CommandText = nullOptionals
                ? "INSERT INTO logs (EventId, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties, Sent) VALUES ($eventId,'legacy','Information','legacy nulls','legacy nulls',NULL,NULL,NULL,NULL,0);"
                : "INSERT INTO logs (EventId, Timestamp, Level, RenderMessage, MessageTemplate, TraceId, SpanId, Exception, Properties, Sent) VALUES ($eventId,'legacy','Information','legacy','legacy','','','','{}',0);";
            command.Parameters.AddWithValue("$eventId", eventId);
            command.ExecuteNonQuery();
        }

        private static int ReserveAndReleasePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task<List<string>> ReceiveRequestsAsync(
            TcpListener listener,
            int count,
            HttpStatusCode statusCode)
        {
            var bodies = new List<string>(count);
            for (int index = 0; index < count; index++)
            {
                bodies.Add(await ReceiveSingleRequestAsync(listener, statusCode));
            }

            return bodies;
        }

        private static async Task WaitForUnsentCountAsync(
            string connectionString,
            long expectedCount,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM logs WHERE Sent = 0;";
                long count = Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
                if (count == expectedCount)
                {
                    return;
                }

                await Task.Delay(25);
            }

            Assert.Fail($"Timed out waiting for pending count {expectedCount}.");
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (predicate())
                {
                    return;
                }

                await Task.Delay(25);
            }

            Assert.Fail("Timed out waiting for the expected condition.");
        }

        private static async Task<string> ReceiveSingleRequestAsync(TcpListener listener, HttpStatusCode statusCode, string? extraHeaders = null)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync();
            using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

            int contentLength = 0;
            while (true)
            {
                string? line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }

                const string contentLengthPrefix = "Content-Length:";
                if (line.StartsWith(contentLengthPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line.Substring(contentLengthPrefix.Length).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            char[] bodyBuffer = new char[contentLength];
            int totalRead = 0;
            while (totalRead < contentLength)
            {
                int read = await reader.ReadAsync(bodyBuffer.AsMemory(totalRead, contentLength - totalRead));
                if (read == 0)
                {
                    break;
                }

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

        private static async Task WaitForSentStateAsync(string connectionString, long expectedSent, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Sent FROM logs ORDER BY Id LIMIT 1;";
                object? result = await command.ExecuteScalarAsync();
                if (result is not null && Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture) == expectedSent)
                {
                    return;
                }

                await Task.Delay(25);
            }

            Assert.Fail($"Timed out waiting for Sent={expectedSent}.");
        }

        private static string CreateTemporaryDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Eigenverft.NetLib.SerilogRelay.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void DeleteTemporaryDirectory(string directory)
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
