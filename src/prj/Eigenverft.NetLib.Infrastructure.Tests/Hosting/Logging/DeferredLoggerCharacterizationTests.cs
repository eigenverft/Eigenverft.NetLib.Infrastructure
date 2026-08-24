using System;

using Eigenverft.NetLib.Infrastructure.Hosting.Logging.DeferredLogger;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Eigenverft.NetLib.Infrastructure.Tests
{
    [TestClass]
    public sealed class DeferredLoggerCharacterizationTests
    {
        [TestMethod]
        public void AddDeferredLogging_ResolvesGenericLoggerFromDependencyInjection()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDeferredLogging();

            using ServiceProvider provider = services.BuildServiceProvider();

            IDeferredLogger<DeferredLoggerCharacterizationTests> logger =
                provider.GetRequiredService<IDeferredLogger<DeferredLoggerCharacterizationTests>>();

            Assert.IsNotNull(logger);
        }

        [TestMethod]
        public void DisabledLevel_DoesNotEvaluateDeferredMessageOrArguments()
        {
            var inner = new TestLogger<DeferredLoggerCharacterizationTests>(LogLevel.Warning);
            var logger = new DeferredLogger<DeferredLoggerCharacterizationTests>(inner);
            var messageEvaluations = 0;
            var argumentEvaluations = 0;

            logger.LogDebug(() =>
            {
                messageEvaluations++;
                return "debug message";
            });

            logger.LogDebug(
                "Debug value {Value}",
                () =>
                {
                    argumentEvaluations++;
                    return 42;
                });

            Assert.AreEqual(0, messageEvaluations);
            Assert.AreEqual(0, argumentEvaluations);
            Assert.AreEqual(0, inner.LogCalls);
        }

        [TestMethod]
        public void EnabledLevel_EvaluatesDeferredArgumentExactlyOnce()
        {
            var inner = new TestLogger<DeferredLoggerCharacterizationTests>(LogLevel.Debug);
            var logger = new DeferredLogger<DeferredLoggerCharacterizationTests>(inner);
            var argumentEvaluations = 0;

            logger.LogDebug(
                "Debug value {Value}",
                () =>
                {
                    argumentEvaluations++;
                    return 42;
                });

            Assert.AreEqual(1, argumentEvaluations);
            Assert.AreEqual(1, inner.LogCalls);
        }

        private sealed class TestLogger<TCategoryName> : ILogger<TCategoryName>
        {
            private readonly LogLevel _minimumLevel;

            public TestLogger(LogLevel minimumLevel)
            {
                _minimumLevel = minimumLevel;
            }

            public int LogCalls { get; private set; }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= _minimumLevel && logLevel != LogLevel.None;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    LogCalls++;
                }
            }
        }
    }
}
