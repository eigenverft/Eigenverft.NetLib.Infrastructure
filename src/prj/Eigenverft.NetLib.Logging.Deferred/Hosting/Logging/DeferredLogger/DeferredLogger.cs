using System;

using Microsoft.Extensions.Logging;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Logging.DeferredLogger
{
    /// <summary>
    /// Default implementation of <see cref="IDeferredLogger{TCategoryName}"/> that wraps an <see cref="ILogger{TCategoryName}"/>.
    /// </summary>
    /// <typeparam name="TCategoryName">The logging category type.</typeparam>
    public sealed class DeferredLogger<TCategoryName> : IDeferredLogger<TCategoryName>
    {
        private readonly ILogger<TCategoryName> _inner;

        /// <summary>
        /// Initializes a new instance of the logger wrapper.
        /// </summary>
        /// <param name="inner">The underlying logger.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="inner"/> is null.
        /// </exception>
        public DeferredLogger(ILogger<TCategoryName> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel level)
        {
            // Treat None as "disabled" for convenience.
            return level != LogLevel.None && _inner.IsEnabled(level);
        }

        // -------- GENERIC --------

        /// <inheritdoc />
        public void Log(LogLevel level, Func<string> messageFactory)
        {
            if (!IsEnabled(level) || messageFactory is null)
            {
                return;
            }

            _inner.Log(level, messageFactory());
        }

        /// <inheritdoc />
        public void Log(LogLevel level, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            _inner.Log(level, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void Log(LogLevel level, string messageTemplate, params object?[] arguments)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            _inner.Log(level, messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void Log(LogLevel level, Exception exception, Func<string> messageFactory)
        {
            if (!IsEnabled(level) || messageFactory is null)
            {
                return;
            }

            _inner.Log(level, exception, messageFactory());
        }

        /// <inheritdoc />
        public void Log(LogLevel level, Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            _inner.Log(level, exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void Log(LogLevel level, Exception exception, string messageTemplate, params object?[] arguments)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            _inner.Log(level, exception, messageTemplate, arguments);
        }

        // -------- TRACE --------

        /// <inheritdoc />
        public void LogTrace(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Trace) || messageFactory is null)
            {
                return;
            }

            _inner.LogTrace(messageFactory());
        }

        /// <inheritdoc />
        public void LogTrace(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Trace))
            {
                return;
            }

            _inner.LogTrace(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogTrace(string messageTemplate, params object?[] arguments)
        {
            _inner.LogTrace(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogTrace(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Trace))
            {
                return;
            }

            _inner.LogTrace(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogTrace(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogTrace(exception, messageTemplate, arguments);
        }

        // -------- DEBUG --------

        /// <inheritdoc />
        public void LogDebug(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Debug) || messageFactory is null)
            {
                return;
            }

            _inner.LogDebug(messageFactory());
        }

        /// <inheritdoc />
        public void LogDebug(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            _inner.LogDebug(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogDebug(string messageTemplate, params object?[] arguments)
        {
            _inner.LogDebug(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogDebug(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            _inner.LogDebug(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogDebug(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogDebug(exception, messageTemplate, arguments);
        }

        // -------- INFORMATION --------

        /// <inheritdoc />
        public void LogInformation(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Information) || messageFactory is null)
            {
                return;
            }

            _inner.LogInformation(messageFactory());
        }

        /// <inheritdoc />
        public void LogInformation(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Information))
            {
                return;
            }

            _inner.LogInformation(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogInformation(string messageTemplate, params object?[] arguments)
        {
            _inner.LogInformation(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogInformation(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Information))
            {
                return;
            }

            _inner.LogInformation(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogInformation(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogInformation(exception, messageTemplate, arguments);
        }

        // -------- WARNING --------

        /// <inheritdoc />
        public void LogWarning(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Warning) || messageFactory is null)
            {
                return;
            }

            _inner.LogWarning(messageFactory());
        }

        /// <inheritdoc />
        public void LogWarning(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            _inner.LogWarning(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogWarning(string messageTemplate, params object?[] arguments)
        {
            _inner.LogWarning(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogWarning(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            _inner.LogWarning(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogWarning(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogWarning(exception, messageTemplate, arguments);
        }

        // -------- ERROR --------

        /// <inheritdoc />
        public void LogError(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Error) || messageFactory is null)
            {
                return;
            }

            _inner.LogError(messageFactory());
        }

        /// <inheritdoc />
        public void LogError(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Error))
            {
                return;
            }

            _inner.LogError(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogError(string messageTemplate, params object?[] arguments)
        {
            _inner.LogError(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogError(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Error))
            {
                return;
            }

            _inner.LogError(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogError(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogError(exception, messageTemplate, arguments);
        }

        // -------- CRITICAL --------

        /// <inheritdoc />
        public void LogCritical(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Critical) || messageFactory is null)
            {
                return;
            }

            _inner.LogCritical(messageFactory());
        }

        /// <inheritdoc />
        public void LogCritical(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Critical))
            {
                return;
            }

            _inner.LogCritical(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogCritical(string messageTemplate, params object?[] arguments)
        {
            _inner.LogCritical(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogCritical(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Critical))
            {
                return;
            }

            _inner.LogCritical(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogCritical(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogCritical(exception, messageTemplate, arguments);
        }

        private static object?[] MaterializeArguments(Func<object?>[] argumentFactories)
        {
            if (argumentFactories is null || argumentFactories.Length == 0)
            {
                return Array.Empty<object?>();
            }

            var result = new object?[argumentFactories.Length];

            for (var i = 0; i < argumentFactories.Length; i++)
            {
                var factory = argumentFactories[i];
                result[i] = factory != null ? factory() : null;
            }

            return result;
        }
    }

    /// <summary>
    /// Default implementation of <see cref="IDeferredLogger"/> that wraps an <see cref="ILogger"/>.
    /// </summary>
    /// <remarks>
    /// This implementation can create an <see cref="ILogger"/> via <see cref="ILoggerFactory"/>
    /// using either a fixed category name or an explicitly provided category name.
    /// Consumers may also wrap an existing <see cref="ILogger"/> instance.
    /// </remarks>
    public sealed class DeferredLogger : IDeferredLogger
    {
        private const string DefaultCategoryName = "DeferredLogger";
        private readonly ILogger _inner;

        /// <summary>
        /// Initializes a new instance of the logger wrapper.
        /// </summary>
        /// <param name="loggerFactory">Factory used to create the underlying logger.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="loggerFactory"/> is null.
        /// </exception>
        /// <example>
        /// <code>
        /// // DI will provide ILoggerFactory automatically.
        /// // Inject IDeferredLogger where you need it.
        /// </code>
        /// </example>
        public DeferredLogger(ILoggerFactory loggerFactory)
            : this(loggerFactory, DefaultCategoryName)
        {
        }

        /// <summary>
        /// Initializes a new instance of the logger wrapper using an explicit category name.
        /// </summary>
        /// <param name="loggerFactory">Factory used to create the underlying logger.</param>
        /// <param name="categoryName">Category name used for the underlying logger.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="loggerFactory"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="categoryName"/> is null or whitespace.</exception>
        public DeferredLogger(ILoggerFactory loggerFactory, string categoryName)
        {
            if (loggerFactory is null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                throw new ArgumentException("Category name must be non-empty.", nameof(categoryName));
            }

            _inner = loggerFactory.CreateLogger(categoryName);
        }

        /// <summary>
        /// Initializes a new instance of the logger wrapper by wrapping an existing logger.
        /// </summary>
        /// <param name="inner">The underlying logger.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="inner"/> is null.</exception>
        public DeferredLogger(ILogger inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel level)
        {
            // Treat None as "disabled" for convenience.
            return level != LogLevel.None && _inner.IsEnabled(level);
        }

        // -------- GENERIC --------

        /// <inheritdoc />
        public void Log(LogLevel level, Func<string> messageFactory)
        {
            if (!IsEnabled(level) || messageFactory is null)
            {
                return;
            }

            _inner.Log(level, messageFactory());
        }

        /// <inheritdoc />
        public void Log(LogLevel level, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            _inner.Log(level, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void Log(LogLevel level, string messageTemplate, params object?[] arguments)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            _inner.Log(level, messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void Log(LogLevel level, Exception exception, Func<string> messageFactory)
        {
            if (!IsEnabled(level) || messageFactory is null)
            {
                return;
            }

            _inner.Log(level, exception, messageFactory());
        }

        /// <inheritdoc />
        public void Log(LogLevel level, Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            _inner.Log(level, exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void Log(LogLevel level, Exception exception, string messageTemplate, params object?[] arguments)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            _inner.Log(level, exception, messageTemplate, arguments);
        }

        // -------- TRACE --------

        /// <inheritdoc />
        public void LogTrace(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Trace) || messageFactory is null)
            {
                return;
            }

            _inner.LogTrace(messageFactory());
        }

        /// <inheritdoc />
        public void LogTrace(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Trace))
            {
                return;
            }

            _inner.LogTrace(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogTrace(string messageTemplate, params object?[] arguments)
        {
            _inner.LogTrace(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogTrace(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Trace))
            {
                return;
            }

            _inner.LogTrace(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogTrace(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogTrace(exception, messageTemplate, arguments);
        }

        // -------- DEBUG --------

        /// <inheritdoc />
        public void LogDebug(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Debug) || messageFactory is null)
            {
                return;
            }

            _inner.LogDebug(messageFactory());
        }

        /// <inheritdoc />
        public void LogDebug(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            _inner.LogDebug(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogDebug(string messageTemplate, params object?[] arguments)
        {
            _inner.LogDebug(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogDebug(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            _inner.LogDebug(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogDebug(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogDebug(exception, messageTemplate, arguments);
        }

        // -------- INFORMATION --------

        /// <inheritdoc />
        public void LogInformation(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Information) || messageFactory is null)
            {
                return;
            }

            _inner.LogInformation(messageFactory());
        }

        /// <inheritdoc />
        public void LogInformation(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Information))
            {
                return;
            }

            _inner.LogInformation(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogInformation(string messageTemplate, params object?[] arguments)
        {
            _inner.LogInformation(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogInformation(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Information))
            {
                return;
            }

            _inner.LogInformation(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogInformation(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogInformation(exception, messageTemplate, arguments);
        }

        // -------- WARNING --------

        /// <inheritdoc />
        public void LogWarning(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Warning) || messageFactory is null)
            {
                return;
            }

            _inner.LogWarning(messageFactory());
        }

        /// <inheritdoc />
        public void LogWarning(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            _inner.LogWarning(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogWarning(string messageTemplate, params object?[] arguments)
        {
            _inner.LogWarning(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogWarning(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            _inner.LogWarning(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogWarning(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogWarning(exception, messageTemplate, arguments);
        }

        // -------- ERROR --------

        /// <inheritdoc />
        public void LogError(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Error) || messageFactory is null)
            {
                return;
            }

            _inner.LogError(messageFactory());
        }

        /// <inheritdoc />
        public void LogError(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Error))
            {
                return;
            }

            _inner.LogError(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogError(string messageTemplate, params object?[] arguments)
        {
            _inner.LogError(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogError(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Error))
            {
                return;
            }

            _inner.LogError(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogError(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogError(exception, messageTemplate, arguments);
        }

        // -------- CRITICAL --------

        /// <inheritdoc />
        public void LogCritical(Func<string> messageFactory)
        {
            if (!_inner.IsEnabled(LogLevel.Critical) || messageFactory is null)
            {
                return;
            }

            _inner.LogCritical(messageFactory());
        }

        /// <inheritdoc />
        public void LogCritical(string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Critical))
            {
                return;
            }

            _inner.LogCritical(messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogCritical(string messageTemplate, params object?[] arguments)
        {
            _inner.LogCritical(messageTemplate, arguments);
        }

        /// <inheritdoc />
        public void LogCritical(Exception exception, string messageTemplate, params Func<object?>[] argumentFactories)
        {
            if (!_inner.IsEnabled(LogLevel.Critical))
            {
                return;
            }

            _inner.LogCritical(exception, messageTemplate, MaterializeArguments(argumentFactories));
        }

        /// <inheritdoc />
        public void LogCritical(Exception exception, string messageTemplate, params object?[] arguments)
        {
            _inner.LogCritical(exception, messageTemplate, arguments);
        }

        private static object?[] MaterializeArguments(Func<object?>[] argumentFactories)
        {
            if (argumentFactories is null || argumentFactories.Length == 0)
            {
                return Array.Empty<object?>();
            }

            var result = new object?[argumentFactories.Length];

            for (var i = 0; i < argumentFactories.Length; i++)
            {
                var factory = argumentFactories[i];
                result[i] = factory != null ? factory() : null;
            }

            return result;
        }
    }

    /// <summary>
    /// Factory for creating deferred loggers from an underlying <see cref="ILoggerFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this when you prefer explicit factory creation instead of relying on DI constructor selection.
    /// </para>
    /// <para>
    /// Example registration:
    /// </para>
    /// <code>
    /// services.AddSingleton{DeferredLoggerFactory}();
    /// </code>
    /// <para>
    /// Example usage:
    /// </para>
    /// <code>
    /// var d = deferredLoggerFactory.Create{MyCategory}();
    /// var n = deferredLoggerFactory.Create("MyCategoryName");
    /// </code>
    /// </remarks>
    public sealed class DeferredLoggerFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Initializes a new instance of the factory.
        /// </summary>
        /// <param name="loggerFactory">The underlying logger factory.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="loggerFactory"/> is null.</exception>
        public DeferredLoggerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        /// <summary>
        /// Creates a deferred logger for the given category type.
        /// </summary>
        /// <typeparam name="TCategoryName">The logging category type.</typeparam>
        /// <returns>A deferred logger instance.</returns>
        public IDeferredLogger<TCategoryName> Create<TCategoryName>()
        {
            return new DeferredLogger<TCategoryName>(_loggerFactory.CreateLogger<TCategoryName>());
        }

        /// <summary>
        /// Creates a deferred logger using the default category name of <see cref="DeferredLogger"/>.
        /// </summary>
        /// <returns>A deferred logger instance.</returns>
        public IDeferredLogger Create()
        {
            return new DeferredLogger(_loggerFactory);
        }

        /// <summary>
        /// Creates a deferred logger for the given category name.
        /// </summary>
        /// <param name="categoryName">Category name used for the underlying logger.</param>
        /// <returns>A deferred logger instance.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="categoryName"/> is null or whitespace.</exception>
        public IDeferredLogger Create(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                throw new ArgumentException("Category name must be non-empty.", nameof(categoryName));
            }

            return new DeferredLogger(_loggerFactory, categoryName);
        }
    }
}
