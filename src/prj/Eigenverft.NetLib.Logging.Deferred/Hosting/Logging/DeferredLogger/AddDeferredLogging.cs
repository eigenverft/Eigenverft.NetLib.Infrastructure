using System;

using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Logging.DeferredLogger
{
    /// <summary>
    /// Provides extension methods to register deferred logging services.
    /// </summary>
    /// <remarks>
    /// This extension registers the generic <see cref="IDeferredLogger{TCategoryName}"/>
    /// as a singleton that wraps the underlying <see cref="Microsoft.Extensions.Logging.ILogger{TCategoryName}"/>.
    /// </remarks>
    public static partial class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the deferred logger wrapper(s) to the service collection.
        /// </summary>
        /// <remarks>
        /// This method registers:
        /// <see cref="IDeferredLogger{TCategoryName}"/> using <see cref="DeferredLogger{TCategoryName}"/>,
        /// and <see cref="IDeferredLogger"/> using <see cref="DeferredLogger"/>.
        /// </remarks>
        /// <param name="services">The service collection to configure.</param>
        /// <returns>
        /// The same <see cref="IServiceCollection"/> instance so that multiple calls can be chained.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="services"/> is null.
        /// </exception>
        /// <example>
        /// <code>
        /// var services = new ServiceCollection();
        /// services.AddLogging();
        /// services.AddDeferredLogging();
        /// </code>
        /// </example>
        public static IServiceCollection AddDeferredLogging(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddSingleton(typeof(IDeferredLogger<>), typeof(DeferredLogger<>));
            services.AddSingleton<IDeferredLogger, DeferredLogger>();

            return services;
        }
    }
}