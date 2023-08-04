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
    public class AsyncInitializationAndRunTests 
    {
        public AsyncInitializationAndRunTests(ITestOutputHelper testOutput)
        {
            OutputHelper = testOutput;
        }

        private ITestOutputHelper OutputHelper { get; }

        [Fact]
        public async Task InitAndRunAsync_throws_InvalidOperationException_when_services_are_not_registered()
        {
            var host = CommonTestTypes.CreateHost(services => { });
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
                services.AddTransient(factory => OutputHelper);
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
                services.AddTransient(factory => OutputHelper);
                services.AddHostedService<TestService>();
            });
            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("host", exception.Message);
        }

        [Fact]
        public async Task Initializer_with_teardown_and_singleton_disposable_dependency_does_not_fail_on_host_shutdown()
        {
            var host = CreateHost(services =>
            {
                services.AddSingleton<IDependency, DisposableDependency>();
                services.AddAsyncInitializer<InitializerWithTearDown>();
                services.AddTransient(factory => OutputHelper   );
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
                services.AddTransient(factory => OutputHelper);
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
                services.AddTransient(factory => OutputHelper);
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
                services.AddTransient(factory => OutputHelper);
                services.AddHostedService<TestService>();
            });
            
            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            A.CallTo(() => initializer.InitializeAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer.TeardownAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Host_is_disposed_after_successful_run(bool forceIDisposableHost)
        {
            var host = CreateHost(services =>
            {
                services.AddScoped<IDependency, DisposableDependency>();
                services.AddAsyncInitializer<InitializerWithTearDown>();
                services.AddTransient(factory => OutputHelper);
                services.AddHostedService<TestService>();
            }, true);

            if (forceIDisposableHost) 
                host = new SyncDisposableHostWrapper(host);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("host", exception.Message);

            exception = Record.Exception(() => host.Services.CreateScope());
            Assert.IsType<ObjectDisposedException>(exception);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Host_is_disposed_after_failing_teardown(bool forceIDisposableHost)
        {
            var initializer = A.Fake<IAsyncTeardown>();
            A.CallTo(() => initializer.TeardownAsync(default)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddTransient(factory => OutputHelper);
                services.AddHostedService<TestService>();
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            exception = Record.Exception(() => host.Services.CreateScope());
            Assert.IsType<ObjectDisposedException>(exception);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Host_is_disposed_after_failing_initializer(bool forceIDisposableHost)
        {
            var initializer = A.Fake<IAsyncTeardown>();
            A.CallTo(() => initializer.InitializeAsync(default)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddTransient(factory => OutputHelper);
                services.AddHostedService<TestService>();
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            exception = Record.Exception(() => host.Services.CreateScope());
            Assert.IsType<ObjectDisposedException>(exception);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Host_is_disposed_after_cancellation(bool forceIDisposableHost)
        {
            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(sp => A.Fake<IAsyncTeardown>());
                services.AddTransient(factory => OutputHelper);
                services.AddHostedService<TestService>();
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

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
                services.AddTransient(factory => OutputHelper);
                services.AddHostedService<TestService>();
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("host", exception.Message);

            A.CallTo(() => initializer.InitializeAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer.TeardownAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }
    }
}
