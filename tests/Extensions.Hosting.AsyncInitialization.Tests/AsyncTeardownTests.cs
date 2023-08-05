using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using static Extensions.Hosting.AsyncInitialization.Tests.CommonTestTypes;

namespace Extensions.Hosting.AsyncInitialization.Tests
{
    public class AsyncTeardownTests
    {
        public AsyncTeardownTests(ITestOutputHelper testOutput)
        {
            OutputHelper = testOutput;
        }
        private ITestOutputHelper OutputHelper { get; }


        [Fact]
        public async Task Single_teardown_is_called()
        {
            var initializer = A.Fake<IAsyncTeardown>();

            var host = CreateHost(services => services.AddAsyncInitializer(initializer));

            await host.TeardownAsync();

            A.CallTo(() => initializer.TeardownAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Multiple_teardown_are_called_in_reverse_order()
        {
            var initializer1 = A.Fake<IAsyncTeardown>();
            var initializer2 = A.Fake<IAsyncTeardown>();
            var initializer3 = A.Fake<IAsyncTeardown>();

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
            });

            await host.TeardownAsync();

            A.CallTo(() => initializer3.TeardownAsync(default)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => initializer2.TeardownAsync(default)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => initializer1.TeardownAsync(default)).MustHaveHappenedOnceExactly());
        }

        [Fact]
        public async Task TeardownAsync_throws_InvalidOperationException_when_services_are_not_registered()
        {
            var host = CommonTestTypes.CreateHost(services => { });
            var exception = await Record.ExceptionAsync(() => host.TeardownAsync());
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("The async initialization service isn't registered, register it by calling AddAsyncInitialization() on the service collection or by adding an async initializer.", exception.Message);
        }

        [Fact]
        public async Task TeardownAsync_throws_ObjectDisposedException_when_called_after_RunAsync()
        {
            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(factory => A.Fake<IAsyncTeardown>());
            });
            
            await Assert.ThrowsAsync<OperationCanceledException>(() => host.RunAsync(new CancellationToken(true)));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => host.TeardownAsync());
            
        }

        [Fact]
        public async Task TeardownAsync_after_StartAsync_does_not_fail()
        {
            var initializer = A.Fake<IAsyncTeardown>();
            var host = CreateHost(services => services.AddAsyncInitializer(initializer));   

            await host.InitAsync();
            await host.StartAsync();
            await host.WaitForShutdownAsync(new CancellationToken(true));
            await host.TeardownAsync();

            A.CallTo(() => initializer.InitializeAsync(default)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer.TeardownAsync(default)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Singleton_disposable_dependency_is_not_disposed()
        {
            var dependency = A.Fake<IDisposableDependency>();

            var host = CreateHost(services =>
            {
                services.AddSingleton<IDependency>(factory => dependency);
                services.AddAsyncInitializer<InitializerWithTearDown>();
                services.AddTransient<ITestOutputHelper>(factory => OutputHelper);
            }, true);

            await host.InitAsync();
            await host.TeardownAsync();

            A.CallTo(() => dependency.Dispose()).MustNotHaveHappened();
        }

        [Fact]
        public async Task Scoped_disposable_dependency_is_disposed_twice()
        {
            var dependency = A.Fake<IDisposableDependency>();   

            var host = CreateHost(services =>
            {
                services.AddScoped<IDependency>(factory => dependency);
                services.AddAsyncInitializer<InitializerWithTearDown>();
                services.AddTransient<ITestOutputHelper>(factory => OutputHelper);
            }, true);

            await host.InitAsync();
            await host.TeardownAsync();

            A.CallTo(() => dependency.Dispose()).MustHaveHappenedTwiceExactly();
            
        }

        [Fact]
        public async Task Singleton_Initializer_does_no_dispose_singleton_dependency()
        {
            var dependency = A.Fake<IDisposableDependency>();

            var host = CreateHost(services =>
            {
                services.AddSingleton<IDependency>(factory => dependency);
                services.AddSingleton<IAsyncTeardown, InitializerWithTearDown>();
                services.AddAsyncInitializer(factory => factory.GetRequiredService<IAsyncTeardown>());
                services.AddTransient<ITestOutputHelper>(factory => OutputHelper);
            }, true);

            await host.InitAsync();
            await host.TeardownAsync();

            A.CallTo(() => dependency.Dispose()).MustNotHaveHappened();
        }

        [Fact]
        public async Task Singleton_Initializer_is_called()
        {
            var initializer = A.Fake<IAsyncTeardown>();
            
            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
            }, true);

            await host.InitAsync();
            await host.TeardownAsync();

            A.CallTo(() => initializer.InitializeAsync(default)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer.TeardownAsync(default)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Captive_Dependency()
        {
            var host = CreateHost(services =>
            {
                services.AddScoped<IDependency>(factory => A.Fake<IDisposableDependency>());
                services.AddSingleton<IAsyncTeardown, InitializerWithTearDown>();
                services.AddAsyncInitializer(factory => factory.GetRequiredService<IAsyncTeardown>());
                services.AddTransient<ITestOutputHelper>(factory => OutputHelper);
            }, true);

            await Assert.ThrowsAsync<InvalidOperationException>(() => host.TeardownAsync());

        }

        [Fact]
        public async Task Cancelled_Teardown_makes_teardown_fail()
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            var initializer1 = A.Fake<IAsyncTeardown>();
            var initializer2 = A.Fake<IAsyncTeardown>();
            var initializer3 = A.Fake<IAsyncTeardown>();


            A.CallTo(() => initializer3.TeardownAsync(A<CancellationToken>._)).Invokes(_ => cancellationTokenSource.Cancel());

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
            });

            var exception = await Record.ExceptionAsync(() => host.TeardownAsync(cancellationTokenSource.Token));
            Assert.IsType<OperationCanceledException>(exception);

            A.CallTo(() => initializer3.TeardownAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer2.TeardownAsync(A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => initializer1.TeardownAsync(A<CancellationToken>._)).MustNotHaveHappened();
        }

    }
}
