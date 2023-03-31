using Extensions.Hosting.AsyncInitialization;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Hosting
{
    /// <summary>
    /// Provides extension methods to perform async initialization of an application.
    /// </summary>
    public static class AsyncInitializationWebHostExtensions
    {
        /// <summary>
        /// Initializes the application, by calling all registered async initializers.
        /// </summary>
        /// <param name="webHost">The host.</param>
        /// <param name="cancellationToken">Optionally propagates notifications that the operation should be cancelled</param>
        /// <returns>A task that represents the initialization completion.</returns>
        public static async Task InitAsync(this IWebHost webHost, CancellationToken cancellationToken = default)
        {
            if (webHost == null)
                throw new ArgumentNullException(nameof(webHost));

            using IServiceScope scope = webHost.Services.CreateScope();
            RootInitializer? rootInitializer = scope.ServiceProvider.GetService<RootInitializer?>();
            if (rootInitializer == null)
            {
                throw new InvalidOperationException("The async initialization service isn't registered, register it by calling AddAsyncInitialization() on the service collection or by adding an async initializer.");
            }

            await rootInitializer.InitializeAsync(cancellationToken);
        }
    }
}
