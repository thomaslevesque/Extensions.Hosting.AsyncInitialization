using Extensions.Hosting.AsyncInitialization;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Provides extension methods to perform async initialization of an application.
    /// </summary>
    public static class AsyncInitializationHostExtensions
    {
        /// <summary>
        /// The default timeout value applied when performing teardown.
        /// </summary>
        public static readonly TimeSpan DefaultTeardownTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Initializes the application, by calling all registered async initializers.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="cancellationToken">Optionally propagates notifications that the operation should be cancelled</param>
        /// <returns>A <see cref="Task"/> that represents the initialization completion.</returns>
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
        /// <returns>A <see cref="Task"/> that represents the teardown completion.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the host is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the initialization service has not been registered.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the cancellationToken is cancelled.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the host instance has been disposed.</exception>
        /// <remarks>
        /// Attention: This method can only be used in combination with manually calling StartAsync() and WaitForShutdownAsync() on the <paramref name="host"/> instance and before disposing the host.
        /// Calling this method after IHost.RunAsync() will throw an <see cref="ObjectDisposedException"/> as the <paramref name="host"/> instance is disposed after running.
        /// </remarks>
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
        /// <remarks>
        /// Cancelling the <paramref name="cancellationToken"/> will not affect the teardown process. 
        /// Teardown, when configured, is always performed, even if the process is cancelled or an exception is thrown.
        /// To prevent teardown from blocking forever, a <see cref="CancellationToken"/> with a <see cref="DefaultTeardownTimeout"/> value is passed to any registered initializers that implement <see cref="IAsyncTeardown"/>. 
        /// The entire teardown process will be cancelled if the timeout expires before all initializers have completed teardown.
        /// </remarks>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the host is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the initialization service has not been registered.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the cancellationToken is cancelled.</exception>
        /// <exception cref="TimeoutException">Thrown when teardown times out.</exception>
        /// <exception cref="AggregateException">Thrown when multiple exceptions occur.</exception>
        public static async Task InitAndRunAsync(this IHost host, CancellationToken cancellationToken = default) 
            => await host.InitAndRunAsync(DefaultTeardownTimeout, cancellationToken).ConfigureAwait(false);


#pragma warning disable 1573
        /// <inheritdoc  cref="InitAndRunAsync(IHost, CancellationToken)" path="/*[not(self::remarks)]"/>
        /// <param name="teardownTimeout">The <see cref="TimeSpan"/> timeout value to use for teardown. Setting this value to <see cref="Timeout.InfiniteTimeSpan"/> will disable timeout handling.</param>
        /// <remarks>
        /// Cancelling the <paramref name="cancellationToken"/> will not affect the teardown process. 
        /// Teardown, when configured, is always performed, regardless if the process is cancelled or if an exception is thrown, using a timeout as specified by the <paramref name="teardownTimeout"/> parameter.
        /// The entire teardown process will be cancelled if the timeout expires before all initializers have completed teardown.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when an invalid <paramref name="teardownTimeout"/> value is passed.</exception>
        public static async Task InitAndRunAsync(this IHost host, TimeSpan teardownTimeout, CancellationToken cancellationToken = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            await using (host.AsAsyncDisposable().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                using var cts = new CancellationTokenSource();
                Exception? innerException = null;
                try
                {
                    try
                    {
                        await host.InitAsync(cancellationToken).ConfigureAwait(false);
                        await host.StartAsync(cancellationToken).ConfigureAwait(false);
                        await host.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        innerException = ex;
                        throw;
                    }
                    finally
                    {
                        cts.CancelAfter(teardownTimeout);
                        await host.TeardownAsync(cts.Token).WaitAsync(cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    var timeoutException = new TimeoutException("Teardown cancelled due to timeout.");
                    if (innerException != null)
                    {
                        throw new AggregateException(innerException, timeoutException);
                    }
                    throw timeoutException;
                }
            }
        }
#pragma warning restore 1573

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