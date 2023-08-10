using System;
using System.Threading;
using System.Threading.Tasks;
using Extensions.Hosting.AsyncInitialization;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Provides extension methods to perform async initialization of an application.
    /// </summary>
    public static class AsyncInitializationHostExtensions
    {
        /// <summary>
        /// Initializes the application, by calling all registered async initializers.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="cancellationToken">Optionally propagates notifications that the operation should be cancelled</param>
        /// <returns>A task that represents the initialization completion.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the host is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the initialization service has not been registered.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the cancellationToken is cancelled.</exception>
        public static async Task InitAsync(this IHost host, CancellationToken cancellationToken = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            await using var scope = host.Services.CreateAsyncScope();
            var rootInitializer = scope.ServiceProvider.GetService<RootInitializer>()
                ?? throw new InvalidOperationException("The async initialization service isn't registered, register it by calling AddAsyncInitialization() on the service collection or by adding an async initializer.");

            await rootInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Tears down the application, by calling all registered async initializers that implement <see cref="IAsyncTeardown"/> in reverse order.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="cancellationToken">Optionally propagates notifications that the operation should be cancelled</param>
        /// <returns>A task that represents the teardown completion.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the host is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the initialization service has not been registered.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the cancellationToken is cancelled.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the host instance has been disposed.</exception>
        /// <remarks>Caution: Calling this method after IHost.RunAsync() will throw an <see cref="ObjectDisposedException"/> as the <paramref name="host"/> instance is disposed after running.</remarks>
        public static async Task TeardownAsync(this IHost host, CancellationToken cancellationToken = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            await using var scope = host.Services.CreateAsyncScope();
            var rootInitializer = scope.ServiceProvider.GetService<RootInitializer>()
                ?? throw new InvalidOperationException("The async initialization service isn't registered, register it by calling AddAsyncInitialization() on the service collection or by adding an async initializer.");

            await rootInitializer.TeardownAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Initializes and runs the application, by first calling all registered async initializers.
        /// After the host terminates, any registered initializers that implement <see cref="IAsyncTeardown"/> are called in reverse order to perform the teardown.
        /// The <paramref name="host"/> instance is disposed of after running.
        /// </summary>
        /// <param name="host">The <see cref="IHost"/> to initialize and run.</param>
        /// <param name="cancellationToken">Optionally propagates notifications that the operation should be cancelled</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the host is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the initialization service has not been registered.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the cancellationToken is cancelled.</exception>
        public static async Task InitAndRunAsync(this IHost host, CancellationToken cancellationToken = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            await using (host.AsAsyncDisposable().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await host.InitAsync(cancellationToken).ConfigureAwait(false);
                    await host.StartAsync(cancellationToken).ConfigureAwait(false);
                    await host.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await host.TeardownAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        // wraps an IHost instance in an IAsyncDisposable 
        private static IAsyncDisposable AsAsyncDisposable(this IHost host)
        {
            return host as IAsyncDisposable ?? new AsyncDisposableHostWrapper(host);
        }

        // IAsyncDisposable wrapper for IHost
        private class AsyncDisposableHostWrapper : IAsyncDisposable
        {
            private readonly IHost _host;
            public AsyncDisposableHostWrapper(IHost host)
            {
                _host = host ?? throw new ArgumentNullException(nameof(host));
            }
            public ValueTask DisposeAsync()
            {
                if (_host is IAsyncDisposable asyncDisposable)
                {
                    return asyncDisposable.DisposeAsync();
                }
                _host.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}