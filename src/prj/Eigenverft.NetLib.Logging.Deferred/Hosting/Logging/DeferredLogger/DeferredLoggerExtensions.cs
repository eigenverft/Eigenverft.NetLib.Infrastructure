using System;

using Microsoft.Extensions.Logging;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Logging.DeferredLogger
{
    /// <summary>
    /// Provides adapters from standard Microsoft logging contracts to deferred logging.
    /// </summary>
    public static class DeferredLoggerExtensions
    {
        /// <summary>
        /// Wraps an existing typed logger with deferred message and argument evaluation.
        /// </summary>
        /// <typeparam name="TCategoryName">The logging category type.</typeparam>
        /// <param name="logger">The logger to wrap.</param>
        /// <returns>A deferred logger backed by the supplied logger instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="logger"/> is null.</exception>
        /// <remarks>
        /// The supplied logger is wrapped directly and is not cached or replaced. This makes the adapter suitable for
        /// bootstrap loggers as well as normal dependency-injected loggers, regardless of the underlying logging provider.
        /// </remarks>
        public static IDeferredLogger<TCategoryName> ToDeferred<TCategoryName>(this ILogger<TCategoryName> logger)
        {
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            return new DeferredLogger<TCategoryName>(logger);
        }

        /// <summary>
        /// Wraps an existing logger with deferred message and argument evaluation.
        /// </summary>
        /// <param name="logger">The logger to wrap.</param>
        /// <returns>A deferred logger backed by the supplied logger instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="logger"/> is null.</exception>
        /// <remarks>
        /// The supplied logger is wrapped directly and is not cached or replaced. Provider-specific loggers can therefore
        /// be adapted through their Microsoft <see cref="ILogger"/> bridge without introducing provider dependencies here.
        /// </remarks>
        public static IDeferredLogger ToDeferred(this ILogger logger)
        {
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            return new DeferredLogger(logger);
        }
    }
}
