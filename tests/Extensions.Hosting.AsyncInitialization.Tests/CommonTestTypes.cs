using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;
using System.Threading;
using Xunit.Abstractions;

namespace Extensions.Hosting.AsyncInitialization.Tests
{
    public class CommonTestTypes
    {

        public interface IDependency
        {

        }

        public interface IDisposableDependency : IDependency, IDisposable
        {
            IServiceScope SomeMethod();
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
            private readonly ITestOutputHelper _output;

            public InitializerWithTearDown(IDependency dependency, ITestOutputHelper output)
            {
                _dependency = dependency;
                _output = output;
            }

            public Task InitializeAsync(CancellationToken cancellationToken)
            {
                if (_dependency is IDisposableDependency dep)
                {
                    _= dep.SomeMethod();
                    _output.WriteLine("InitializeAsync Call to DisposableDependency");
                }
                return Task.CompletedTask;
            }

            public Task TeardownAsync(CancellationToken cancellationToken)
            {
                if (_dependency is IDisposableDependency dep)
                {
                    _= dep.SomeMethod();
                    _output.WriteLine("TeardownAsync Call to DisposableDependency");
                }
                return Task.CompletedTask;
            }
        }


        public class DisposableDependency : IDisposableDependency
        {
            private readonly ITestOutputHelper _output;

            public DisposableDependency(IServiceProvider serviceProvider, ITestOutputHelper output)
            {
                ServiceProvider = serviceProvider;
                _output = output;
            }

            public IServiceProvider ServiceProvider { get; private set; }

            public IServiceScope SomeMethod()
            {
                return ServiceProvider.CreateScope();
            }

            public void Dispose()
            {
                _output.WriteLine("Disposing DisposableDependency");
            }
        }

        public class TestService : BackgroundService
        {
            private readonly ITestOutputHelper _output;
            public TestService(ITestOutputHelper output)
            {
                _output = output;
            }

            protected override Task ExecuteAsync(CancellationToken stoppingToken)
            {
                //Throwing exception to stop service
                throw new Exception("host");
            }

            public override void Dispose()
            {
                _output.WriteLine("Disposing TestService");
                base.Dispose();
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
