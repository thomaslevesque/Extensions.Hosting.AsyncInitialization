using System;
using System.Threading;
using System.Threading.Tasks;
using Extensions.Hosting.AsyncInitialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Provides extension methods to register async initializers.
    /// </summary>
    public static class AsyncInitializationServiceCollectionExtensions
    {
        /// <summary>
        /// Registers necessary services for async initialization support.
        /// </summary>
        /// <param name="services">The <see cref="T:Microsoft.Extensions.DependencyInjection.IServiceCollection" /> to add the service to.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitialization(this IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            services.TryAddTransient<RootInitializer>();
            return services;
        }

        /// <summary>
        /// Adds an async initializer of the specified type.
        /// </summary>
        /// <typeparam name="TInitializer">The type of the async initializer to add.</typeparam>
        /// <param name="services">The <see cref="T:Microsoft.Extensions.DependencyInjection.IServiceCollection" /> to add the service to.</param>
        /// <param name="lifetime">The initializer's lifetime. Defaults to <see cref="ServiceLifetime.Transient"/>.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer<TInitializer>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Transient)
            where TInitializer : class, IAsyncInitializer
        {
            return services
                .AddAsyncInitialization()
                .AddServiceDescriptor(ServiceDescriptor.Describe(typeof(IAsyncInitializer), typeof(TInitializer), lifetime));
        }

        /// <summary>
        /// Adds the specified async initializer instance.
        /// </summary>
        /// <typeparam name="TInitializer">The type of the async initializer to add.</typeparam>
        /// <param name="services">The <see cref="T:Microsoft.Extensions.DependencyInjection.IServiceCollection" /> to add the service to.</param>
        /// <param name="initializer">The service initializer</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer<TInitializer>(this IServiceCollection services, TInitializer initializer)
            where TInitializer : class, IAsyncInitializer
        {
            if (initializer == null)
                throw new ArgumentNullException(nameof(initializer));

            return services
                .AddAsyncInitialization()
                .AddSingleton<IAsyncInitializer>(initializer);
        }

        /// <summary>
        /// Adds an async initializer with a factory specified in <paramref name="implementationFactory" />.
        /// </summary>
        /// <param name="services">The <see cref="T:Microsoft.Extensions.DependencyInjection.IServiceCollection" /> to add the service to.</param>
        /// <param name="implementationFactory">The factory that creates the async initializer.</param>
        /// <param name="lifetime">The initializer's lifetime. Defaults to <see cref="ServiceLifetime.Transient"/>.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer(this IServiceCollection services, Func<IServiceProvider, IAsyncInitializer> implementationFactory, ServiceLifetime lifetime = ServiceLifetime.Transient)
        {
            if (implementationFactory == null)
                throw new ArgumentNullException(nameof(implementationFactory));

            return services
                .AddAsyncInitialization()
                .AddServiceDescriptor(ServiceDescriptor.Describe(typeof(IAsyncInitializer), implementationFactory, lifetime));
        }

        /// <summary>
        /// Adds an async initializer of the specified type
        /// </summary>
        /// <param name="services">The <see cref="T:Microsoft.Extensions.DependencyInjection.IServiceCollection" /> to add the service to.</param>
        /// <param name="initializerType">The type of the async initializer to add.</param>
        /// <param name="lifetime">The initializer's lifetime. Defaults to <see cref="ServiceLifetime.Transient"/>.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer(this IServiceCollection services, Type initializerType, ServiceLifetime lifetime = ServiceLifetime.Transient)
        {
            if (initializerType == null)
                throw new ArgumentNullException(nameof(initializerType));

            return services
                .AddAsyncInitialization()
                .AddServiceDescriptor(ServiceDescriptor.Describe(typeof(IAsyncInitializer), initializerType, lifetime));
        }

        /// <summary>
        /// Adds an async initializer whose implementation is the specified delegate.
        /// </summary>
        /// <param name="services">The <see cref="T:Microsoft.Extensions.DependencyInjection.IServiceCollection" /> to add the service to.</param>
        /// <param name="initializer">The delegate that performs async initialization.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer(this IServiceCollection services, Func<Task> initializer)
        {
            if (initializer == null)
                throw new ArgumentNullException(nameof(initializer));

            return services
                .AddAsyncInitialization()
                .AddSingleton<IAsyncInitializer>(new DelegateAsyncInitializer(_ => initializer()));
        }

        /// <summary>
        /// Adds an async initializer whose implementation is the specified delegate.
        /// </summary>
        /// <param name="services">The <see cref="T:Microsoft.Extensions.DependencyInjection.IServiceCollection" /> to add the service to.</param>
        /// <param name="initializer">The delegate that performs async initialization.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer(this IServiceCollection services, Func<CancellationToken, Task> initializer)
        {
            if (initializer == null)
                throw new ArgumentNullException(nameof(initializer));

            return services
                .AddAsyncInitialization()
                .AddSingleton<IAsyncInitializer>(new DelegateAsyncInitializer(initializer));
        }

        /// <summary>
        /// Adds an async initializer with teardown whose implementations are the specified delegates.
        /// </summary>
        /// <param name="services">The <see cref="T:Microsoft.Extensions.DependencyInjection.IServiceCollection" /> to add the service to.</param>
        /// <param name="initializer">The delegate that performs async initialization.</param>
        /// <param name="teardown">The delegate that performs async teardown.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer(this IServiceCollection services, Func<CancellationToken, Task> initializer, Func<CancellationToken, Task> teardown)
        {
            if (initializer == null)
                throw new ArgumentNullException(nameof(initializer));

            return services
                .AddAsyncInitialization()
                .AddSingleton<IAsyncInitializer>(new DelegateAsyncTeardown(initializer, teardown));
        }

        // Helper to be able to use Add(ServiceDescriptor) fluently (since the return type of Add(ServiceDescriptor) is void).
        private static IServiceCollection AddServiceDescriptor(this IServiceCollection services, ServiceDescriptor serviceDescriptor)
        {
            services.Add(serviceDescriptor);
            return services;
        }

        private class DelegateAsyncInitializer : IAsyncInitializer
        {
            private readonly Func<CancellationToken, Task> _initializer;

            public DelegateAsyncInitializer(Func<CancellationToken, Task> initializer)
            {
                _initializer = initializer;
            }

            public Task InitializeAsync(CancellationToken cancellationToken)
            {
                return _initializer(cancellationToken);
            }
        }

        private class DelegateAsyncTeardown : IAsyncTeardown
        {
            private readonly Func<CancellationToken, Task> _initializer;
            private readonly Func<CancellationToken, Task> _teardown;

            public DelegateAsyncTeardown(Func<CancellationToken, Task> initializer, Func<CancellationToken, Task> teardown)
            {
                _initializer = initializer;
                _teardown = teardown;
            }

            public Task InitializeAsync(CancellationToken cancellationToken)
            {
                return _initializer(cancellationToken);
            }

            public Task TeardownAsync(CancellationToken cancellationToken)
            {
                return _teardown(cancellationToken);
            }
        }
    }
}
