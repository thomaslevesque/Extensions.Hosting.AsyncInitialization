using Extensions.Hosting.AsyncInitialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Linq;
using System.Threading.Tasks;

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
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer<TInitializer>(this IServiceCollection services)
            where TInitializer : class, IAsyncInitializer
        {
            services.AddAsyncInitialization();

            if (!typeof(TInitializer).IsInterface)
                return services.AddTransient<IAsyncInitializer, TInitializer>();

            if (HaveSingleRegisteredService<TInitializer>(services))
                return services.AddTransient<IAsyncInitializer>(x => x.GetRequiredService<TInitializer>());

            if (HaveMultipleRegisteredServices<TInitializer>(services))
                return services.AddDecoratedInitializers<TInitializer>();

            throw new InvalidOperationException($"No Implementation type found for type inteface {typeof(TInitializer).FullName}.");
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
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer(this IServiceCollection services, Func<IServiceProvider, IAsyncInitializer> implementationFactory)
        {
            if (implementationFactory == null)
                throw new ArgumentNullException(nameof(implementationFactory));

            return services
                .AddAsyncInitialization()
                .AddTransient(implementationFactory);
        }

        /// <summary>
        /// Adds an async initializer of the specified type
        /// </summary>
        /// <param name="services">The <see cref="T:Microsoft.Extensions.DependencyInjection.IServiceCollection" /> to add the service to.</param>
        /// <param name="initializerType">The type of the async initializer to add.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IServiceCollection AddAsyncInitializer(this IServiceCollection services, Type initializerType)
        {
            if (initializerType == null)
                throw new ArgumentNullException(nameof(initializerType));

            return services
                .AddAsyncInitialization()
                .AddTransient(typeof(IAsyncInitializer), initializerType);
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
                .AddSingleton<IAsyncInitializer>(new DelegateAsyncInitializer(initializer));
        }

        private static IServiceCollection AddDecoratedInitializers<TInitializer>(this IServiceCollection services) where TInitializer : class, IAsyncInitializer
        {
            return services.AddTransient<IAsyncInitializer, DecoratedInitializer>(x =>
            {
                var initializersList = x.GetServices<TInitializer>();
                var decoratedInitializer = new DecoratedInitializer();

                foreach (var initializer in initializersList)
                {
                    var next = new DecoratedInitializer(initializer, decoratedInitializer);
                    decoratedInitializer = next;
                }

                return decoratedInitializer;
            });
        }

        private class DelegateAsyncInitializer : IAsyncInitializer
        {
            private readonly Func<Task> _initializer;

            public DelegateAsyncInitializer(Func<Task> initializer)
            {
                _initializer = initializer;
            }

            public Task InitializeAsync()
            {
                return _initializer();
            }
        }

        private static bool HaveSingleRegisteredService<TInitializer>(IServiceCollection services) =>
            services.Count(descriptor => descriptor.ServiceType.Equals(typeof(TInitializer))) == 1;

        private static bool HaveMultipleRegisteredServices<TInitializer>(IServiceCollection services) =>
            services.Count(descriptor => descriptor.ServiceType.Equals(typeof(TInitializer))) > 1;
    }
}
