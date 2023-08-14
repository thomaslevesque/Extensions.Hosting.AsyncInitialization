using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Extensions.Hosting.AsyncInitialization.Tests
{
    public class CommonTestTypes
    {
        public interface IDependency {}

        public class Dependency : IDependency { }

        public interface IDisposableDependency : IDependency, IDisposable {}

        public class DisposableDependency : IDisposableDependency
        {
            public void Dispose() {}
        }


        public class Initializer : IAsyncInitializer
        {
            // ReSharper disable once NotAccessedField.Local
            private readonly IDependency _dependency;

            public Initializer(IDependency dependency)
            {
                _dependency = dependency;
            }

            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public class InitializerWithTearDown : IAsyncTeardown
        {
            // ReSharper disable once NotAccessedField.Local
            private readonly IDependency _dependency;

            public InitializerWithTearDown(IDependency dependency)
            {
                _dependency = dependency;
            }

            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task TeardownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public class EndlessTeardownInitializer : IAsyncTeardown
        {
            private bool _supportsCancellation;
            public EndlessTeardownInitializer(bool supportsCancellation = true)
            {
                _supportsCancellation = supportsCancellation;
            }
            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task TeardownAsync(CancellationToken cancellationToken)
            {
                return _supportsCancellation ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken) : Task.Delay(Timeout.InfiniteTimeSpan);
            }
        }

        public class TestService : BackgroundService
        {
            protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
        }

        public class FaultingService : BackgroundService
        {
            protected override Task ExecuteAsync(CancellationToken stoppingToken) => throw new ApplicationException(nameof(FaultingService));
        }

        public class StoppingService : BackgroundService
        {
            private readonly IHostApplicationLifetime _lifetime;
            public StoppingService(IHostApplicationLifetime lifetime) 
            { 
                _lifetime = lifetime;
            }
            protected override Task ExecuteAsync(CancellationToken stoppingToken)
            {
                _lifetime.StopApplication();
                return Task.CompletedTask;
            }
        }



        public class SyncDisposableHostWrapper : IHost, IDisposable
        {
            private readonly IHost _host;
            public SyncDisposableHostWrapper(IHost host)
            {
                _host = host ?? throw new ArgumentNullException(nameof(host));
            }

            public IServiceProvider Services => _host.Services;

            public void Dispose()
            {
                _host.Dispose();
            }

            public Task StartAsync(CancellationToken cancellationToken = default)
            {
                return _host.StartAsync(cancellationToken);
            }

            public Task StopAsync(CancellationToken cancellationToken = default)
            {
                return _host.StopAsync(cancellationToken);
            }
        }

        public static IHost CreateHost(Action<IServiceCollection> configureServices, bool validateScopes = false, bool forceIDisposableHost = false)
        {
            var host = new HostBuilder()
              .ConfigureServices(configureServices)
              .UseServiceProviderFactory(new DefaultServiceProviderFactory(
                  new ServiceProviderOptions
                  {
                      ValidateScopes = validateScopes
                  }
              ))
              .Build();

            return forceIDisposableHost ? new SyncDisposableHostWrapper(host) : host;
        }
    }
}
