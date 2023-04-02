using System;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Extensions.Hosting.AsyncInitialization.Tests
{
    public class AsyncInitializationTests
    {
        [Fact]
        public async Task Single_initializer_is_called()
        {
            var initializer = A.Fake<IAsyncInitializer>();

            var host = CreateHost(services => services.AddAsyncInitializer(initializer));

            await host.InitAsync();

            A.CallTo(() => initializer.InitializeAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Delegate_initializer_is_called()
        {
            var initializer = A.Fake<Func<CancellationToken, Task>>();

            var host = CreateHost(services => services.AddAsyncInitializer(initializer));

            await host.InitAsync();

            A.CallTo(() => initializer(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Multiple_initializers_are_called_in_order()
        {
            var initializer1 = A.Fake<IAsyncInitializer>();
            var initializer2 = A.Fake<IAsyncInitializer>();
            var initializer3 = A.Fake<IAsyncInitializer>();

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
            });

            await host.InitAsync();

            A.CallTo(() => initializer1.InitializeAsync(default)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => initializer2.InitializeAsync(default)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => initializer3.InitializeAsync(default)).MustHaveHappenedOnceExactly());
        }

        [Fact]
        public async Task Initializer_with_scoped_dependency_is_resolved()
        {
            var host = CreateHost(
                services =>
                {
                    services.AddScoped(sp => A.Fake<IDependency>());
                    services.AddAsyncInitializer<Initializer>();
                },
                true);

            await host.InitAsync();
        }

        [Fact]
        public async Task Failing_initializer_makes_initialization_fail()
        {
            var initializer1 = A.Fake<IAsyncInitializer>();
            var initializer2 = A.Fake<IAsyncInitializer>();
            var initializer3 = A.Fake<IAsyncInitializer>();

            A.CallTo(() => initializer2.InitializeAsync(default)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
            });

            var exception = await Record.ExceptionAsync(() => host.InitAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            A.CallTo(() => initializer1.InitializeAsync(default)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer3.InitializeAsync(default)).MustNotHaveHappened();
        }

        [Fact]
        public async Task Cancelled_initializer_makes_initialization_fail()
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            var initializer1 = A.Fake<IAsyncInitializer>();
            var initializer2 = A.Fake<IAsyncInitializer>();
            var initializer3 = A.Fake<IAsyncInitializer>();


            A.CallTo(() => initializer1.InitializeAsync(A<CancellationToken>._)).Invokes(_ => cancellationTokenSource.Cancel());

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
            });

            var exception = await Record.ExceptionAsync(() => host.InitAsync(cancellationTokenSource.Token));
            Assert.IsType<OperationCanceledException>(exception);
            
            A.CallTo(() => initializer1.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer2.InitializeAsync(A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => initializer3.InitializeAsync(A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task InitAsync_throws_InvalidOperationException_when_services_are_not_registered()
        {
            var host = CreateHost(services => { });
            var exception = await Record.ExceptionAsync(() => host.InitAsync());
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("The async initialization service isn't registered, register it by calling AddAsyncInitialization() on the service collection or by adding an async initializer.", exception.Message);
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
    }
}