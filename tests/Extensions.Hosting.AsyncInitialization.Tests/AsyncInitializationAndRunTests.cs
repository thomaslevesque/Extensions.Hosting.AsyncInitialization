using FakeItEasy;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Extensions.Hosting.AsyncInitialization.Tests
{
    public class AsyncInitializationAndRunTests 
    {
        private readonly ITestOutputHelper _testOutput;
        public AsyncInitializationAndRunTests(ITestOutputHelper testOutput)
        {
            _testOutput = testOutput;
        }

        [Fact]
        public async Task InitAndRunAsync_throws_InvalidOperationException_when_services_are_not_registered()
        {
            var host = CreateHost(services => { });
            var exception = await Record.ExceptionAsync(() => host.InitAsync());
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("The async initialization service isn't registered, register it by calling AddAsyncInitialization() on the service collection or by adding an async initializer.", exception.Message);
        }

        [Fact]
        public async Task Initializer_with_teardown_and_scoped_dependency_does_not_fail_on_host_shutdown()
        {
            var host = CreateHost(services =>
            {
                services.AddScoped(sp => A.Fake<IDependency>());
                services.AddAsyncInitializer<InitializerWithTearDown>();
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            });
            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("host", exception.Message);
        }

        [Fact]
        public async Task Initializer_with_teardown_and_scoped_disposable_dependency_does_not_fail_on_host_shutdown()
        {
            var host = CreateHost(services =>
            {
                services.AddScoped<IDependency,DisposableDependency>();
                services.AddAsyncInitializer<InitializerWithTearDown>();
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            });
            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("host", exception.Message);
        }

        [Fact]
        public async Task Multiple_initializers_with_teardown_are_called_in_reverse_order()
        {
            var initializer1 = A.Fake<IAsyncTeardown>();
            var initializer2 = A.Fake<IAsyncTeardown>();
            var initializer3 = A.Fake<IAsyncTeardown>();

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("host", exception.Message);

            A.CallTo(() => initializer3.TeardownAsync(default)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => initializer2.TeardownAsync(default)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => initializer1.TeardownAsync(default)).MustHaveHappenedOnceExactly());
        }

        [Fact]
        public async Task Cancelled_initializer_skips_host_run_and_calls_teardown()
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            var initializer1 = A.Fake<IAsyncTeardown>();
            var initializer2 = A.Fake<IAsyncTeardown>();
            var initializer3 = A.Fake<IAsyncTeardown>();


            A.CallTo(() => initializer1.InitializeAsync(A<CancellationToken>._)).Invokes(_ => cancellationTokenSource.Cancel());

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync(cancellationTokenSource.Token));
            Assert.IsType<OperationCanceledException>(exception);

            A.CallTo(() => initializer1.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer2.InitializeAsync(A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => initializer3.InitializeAsync(A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => initializer1.TeardownAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer2.TeardownAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer3.TeardownAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Failing_initializer_skips_host_run_and_calls_teardown()
        {
            var initializer = A.Fake<IAsyncTeardown>();
            A.CallTo(() => initializer.InitializeAsync(default)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            });
            
            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            A.CallTo(() => initializer.InitializeAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer.TeardownAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Host_is_disposed_after_successful_run()
        {
            var host = CreateHost(services =>
            {
                services.AddScoped<IDependency, DisposableDependency>();
                services.AddAsyncInitializer<InitializerWithTearDown>();
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            }, true);

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("host", exception.Message);

            exception = Record.Exception(() => host.Services.CreateScope());
            Assert.IsType<ObjectDisposedException>(exception);
        }

        [Fact]
        public async Task Host_is_disposed_after_failing_teardown()
        {
            var initializer = A.Fake<IAsyncTeardown>();
            A.CallTo(() => initializer.TeardownAsync(default)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            exception = Record.Exception(() => host.Services.CreateScope());
            Assert.IsType<ObjectDisposedException>(exception);
        }

        [Fact]
        public async Task Host_is_disposed_after_failing_initializer()
        {
            var initializer = A.Fake<IAsyncTeardown>();
            A.CallTo(() => initializer.InitializeAsync(default)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            exception = Record.Exception(() => host.Services.CreateScope());
            Assert.IsType<ObjectDisposedException>(exception);
        }

        [Fact]
        public async Task Host_is_disposed_after_cancellation()
        {
            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(sp => A.Fake<IAsyncTeardown>());
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            });
            
            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync(new CancellationToken(true)));
            Assert.IsType<OperationCanceledException>(exception);

            exception = Record.Exception(() => host.Services.CreateScope());
            Assert.IsType<ObjectDisposedException>(exception);
        }

        [Fact]
        public async Task Single_initializer_with_teardown_is_called()
        {
            var initializer = A.Fake<IAsyncTeardown>();

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddTransient(factory => _testOutput);
                services.AddHostedService<TestService>();
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("host", exception.Message);

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

        private static IHost CreateWebHost(Action<IServiceCollection> configureServices, bool validateScopes = false)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Host
                .ConfigureServices(configureServices)
                .UseServiceProviderFactory(new DefaultServiceProviderFactory(
                    new ServiceProviderOptions
                    {
                        ValidateScopes = validateScopes
                    }
                ));
            return builder.Build();
        }

        public interface IDependency
        {
        }

        public interface IDisposableDependency : IDependency, IDisposable
        {
            IServiceScope SomeMethod();
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
    }
}
