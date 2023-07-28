using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Extensions.Hosting.AsyncInitialization.Tests
{
    public class AsyncInitializationAndRunTests 
    {
        [Fact]
        public async Task Initializer_with_teardown_and_scoped_dependency_does_not_fail_on_host_shutdown()
        {
            var host = CreateHost(services =>
            {
                services.AddScoped<IDisposableDependency, DisposableDependency>();
                services.AddAsyncInitializer<InitializerWithTearDownAndDisposableDependency>();
                services.AddHostedService<MyService>();
            });
            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<ApplicationException>(exception);
        }

        [Fact]
        public async Task Multiple_initializers_with_teardown_are_called_in_reverse_order()
        {
            var initializer1 = A.Fake<IAsyncTeardown>();
            var initializer2 = A.Fake<IAsyncTeardown>();
            var initializer3 = A.Fake<IAsyncTeardown>();

            var host = CreateHost(services =>
            {
                services.AddScoped<IDisposableDependency, DisposableDependency>();
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
                services.AddHostedService<MyService>();
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<ApplicationException>(exception);

            A.CallTo(() => initializer3.TeardownAsync(default)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => initializer2.TeardownAsync(default)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => initializer1.TeardownAsync(default)).MustHaveHappenedOnceExactly());
        }


        [Fact]
        public async Task Failing_initializer_skips_host_run_and_calls_teardown()
        {
            var initializer = A.Fake<IAsyncTeardown>();
            A.CallTo(() => initializer.InitializeAsync(default)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddScoped<IDisposableDependency, DisposableDependency>();
                services.AddAsyncInitializer(initializer);
                services.AddHostedService<MyService>();
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            A.CallTo(() => initializer.InitializeAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer.TeardownAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Single_initializer_with_teardown_is_called()
        {
            var initializer = A.Fake<IAsyncTeardown>();

            var host = CreateHost(services =>
            {
                services.AddScoped<IDisposableDependency, DisposableDependency>();
                services.AddAsyncInitializer(initializer);
                services.AddHostedService<MyService>();
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<ApplicationException>(exception);

            A.CallTo(() => initializer.InitializeAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer.TeardownAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }


        private static IHost CreateHost(Action<IServiceCollection> configureServices, bool validateScopes = false) =>
            new HostBuilder()
                .ConfigureServices(configureServices)
                .UseServiceProviderFactory(new DefaultServiceProviderFactory(
                    new ServiceProviderOptions
                    {
                        ValidateScopes = validateScopes
                    }
                ))
                .Build();

        public interface IDependency
        {
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


        public interface IDisposableDependency : IDisposable
        {
            IServiceScope SomeMethod();
        }

        public class DisposableDependency : IDisposableDependency
        {
            public DisposableDependency(IServiceProvider serviceProvider)
            {
                ServiceProvider = serviceProvider;
            }

            public IServiceProvider ServiceProvider { get; private set; }

            public void Dispose() { }

            public IServiceScope SomeMethod()
            {
                return ServiceProvider.CreateScope();
            }
        }

        public class MyService : BackgroundService
        {
            private readonly IDisposableDependency _dependency;
            public MyService(IDisposableDependency dependency) 
            {
                _dependency = dependency ?? throw new ArgumentNullException(nameof(dependency));
            }

            protected override Task ExecuteAsync(CancellationToken stoppingToken)
            {
                throw new ApplicationException();
            }
        }


        public class InitializerWithTearDownAndDisposableDependency : IAsyncTeardown
        {
            private readonly IDisposableDependency _dependency;
            public InitializerWithTearDownAndDisposableDependency(IDisposableDependency dependency)
            {
                _dependency = dependency;
            }
            public Task InitializeAsync(CancellationToken cancellationToken)
            {
                _= _dependency.SomeMethod();
                return Task.CompletedTask;
            }

            public Task TeardownAsync(CancellationToken cancellationToken)
            {
                _= _dependency.SomeMethod();
                return Task.CompletedTask;
            }
        }

    }
}
