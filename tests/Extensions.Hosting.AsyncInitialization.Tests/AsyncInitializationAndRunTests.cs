using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
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
        public async Task Multiple_initializers_with_teardown_are_called_in_correct_order()
        {
            var initializer1 = A.Fake<IAsyncTeardown>();
            var initializer2 = A.Fake<IAsyncTeardown>();
            var initializer3 = A.Fake<IAsyncTeardown>();

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
                services.AddHostedService<StoppingService>();
            });

            await host.InitAndRunAsync();
            
            A.CallTo(() => initializer1.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => initializer2.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => initializer3.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => initializer3.TeardownAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => initializer2.TeardownAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => initializer1.TeardownAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly());
        }

        [Fact]
        public async Task Cancelled_initializer_skips_host_run_and_calls_teardown()
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            var initializer1 = A.Fake<IAsyncTeardown>();
            var initializer2 = A.Fake<IAsyncTeardown>();
            var initializer3 = A.Fake<IAsyncTeardown>();
            var service = A.Fake<TestService>();

            A.CallTo(() => initializer1.InitializeAsync(A<CancellationToken>._)).Invokes(_ => cancellationTokenSource.Cancel());

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
                services.AddHostedService(factory => service);
            });

            await Assert.ThrowsAsync<OperationCanceledException>(() => host.InitAndRunAsync(cancellationTokenSource.Token));

            A.CallTo(() => initializer1.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer2.InitializeAsync(A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => initializer3.InitializeAsync(A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => initializer1.TeardownAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer2.TeardownAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer3.TeardownAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => service.StartAsync(A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task Failing_initializer_skips_host_run_and_calls_teardown()
        {
            var service = A.Fake<TestService>();
            var initializer = A.Fake<IAsyncTeardown>();
            A.CallTo(() => initializer.InitializeAsync(A<CancellationToken>._)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddHostedService(factory => service);
            });
            
            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            A.CallTo(() => initializer.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => service.StartAsync(A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => initializer.TeardownAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Host_is_disposed_after_successful_run(bool forceIDisposableHost)
        {
            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(sp => A.Fake<IAsyncTeardown>());
                services.AddHostedService<StoppingService>();
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            await host.InitAndRunAsync();
            Assert.Throws<ObjectDisposedException>(host.Services.CreateScope);

        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Host_is_disposed_after_failing_teardown(bool forceIDisposableHost)
        {
            var initializer = A.Fake<IAsyncTeardown>();
            A.CallTo(() => initializer.TeardownAsync(A<CancellationToken>._)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddHostedService<StoppingService>();
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            Assert.Throws<ObjectDisposedException>(host.Services.CreateScope);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Host_is_disposed_after_teardown_timeout(bool forceIDisposableHost, bool supportsCancellation)
        {
            var timeout = TimeSpan.FromMilliseconds(100);  

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(sp => new EndlessTeardownInitializer(supportsCancellation));
                services.AddHostedService<StoppingService>();
                //services.AddLogging(builder => builder.AddXUnit(OutputHelper).SetMinimumLevel(LogLevel.Debug));
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            await Assert.ThrowsAnyAsync<TimeoutException>(() => host.InitAndRunAsync(timeout));

            Assert.Throws<ObjectDisposedException>(host.Services.CreateScope);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InitAndRunAsync_throws_TimeoutException_when_teardown_exceeds_timeout(bool supportsCancellation)
        {
            var timeout = TimeSpan.FromMilliseconds(100);

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(sp => new EndlessTeardownInitializer(supportsCancellation));
                services.AddHostedService<StoppingService>();
                //services.AddLogging(builder => builder.AddXUnit(OutputHelper).SetMinimumLevel(LogLevel.Debug));
            });

            await Assert.ThrowsAsync<TimeoutException>(() => host.InitAndRunAsync(timeout));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InitAndRunAsync_throws_AggregateException_when_host_fails_and_teardown_exceeds_timeout(bool supportsCancellation)
        {
            var timeout = TimeSpan.FromMilliseconds(100);

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(sp => new EndlessTeardownInitializer(supportsCancellation));
                services.AddHostedService<FaultingService>();
                //services.AddLogging(builder => builder.AddXUnit(OutputHelper).SetMinimumLevel(LogLevel.Debug));
            });

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync(timeout));
            Assert.IsType<AggregateException>(exception);
            var innerExceptions = ((AggregateException)exception).InnerExceptions;
            Assert.Collection(innerExceptions, 
                item => Assert.IsType<TimeoutException>(item),
                item => Assert.IsType<ApplicationException>(item));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        
        public async Task Host_is_disposed_with_infinite_teardown_timeout(bool forceIDisposableHost)
        {
            var timeout = Timeout.InfiniteTimeSpan;

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(sp => A.Fake<IAsyncTeardown>());
                services.AddHostedService<StoppingService>();
                //services.AddLogging(builder => builder.AddXUnit(OutputHelper).SetMinimumLevel(LogLevel.Debug));
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            await host.InitAndRunAsync(timeout);

            Assert.Throws<ObjectDisposedException>(host.Services.CreateScope);
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
                services.AddHostedService<TestService>();
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            var exception = await Record.ExceptionAsync(() => host.InitAndRunAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            Assert.Throws<ObjectDisposedException>(host.Services.CreateScope);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Host_is_disposed_after_cancellation(bool forceIDisposableHost)
        {
            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(sp => A.Fake<IAsyncTeardown>());
                services.AddHostedService<TestService>();
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            using var cancellationTokenSource = new CancellationTokenSource();
            using var ctr = lifetime.ApplicationStarted.Register(cancellationTokenSource.Cancel);

            await host.InitAndRunAsync(cancellationTokenSource.Token);

            Assert.Throws<ObjectDisposedException>(host.Services.CreateScope);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Host_is_disposed_after_service_fails(bool forceIDisposableHost)
        {
            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(sp => A.Fake<IAsyncTeardown>());
                services.AddHostedService<FaultingService>();   
            }, forceIDisposableHost: forceIDisposableHost);

            OutputHelper.WriteLine(host is IAsyncDisposable ? "Using IAsyncDisposable Host" : "Using IDisposable Host");

            await Assert.ThrowsAsync<ApplicationException>(() => host.InitAndRunAsync()); 
            Assert.Throws<ObjectDisposedException>(host.Services.CreateScope);  
        }


        [Fact]
        public async Task Initializer_with_teardown_and_scoped_dependency_is_resolved()
        {

            var host = CreateHost(
                services =>
                {
                    services.AddScoped(sp => A.Fake<IDependency>());
                    services.AddAsyncInitializer<InitializerWithTearDown>();
                    services.AddHostedService<StoppingService>();
                },
                true);

            await host.InitAndRunAsync();    
        }

        
        [Fact]
        public async Task Single_initializer_with_teardown_is_called()
        {
            var initializer = A.Fake<IAsyncTeardown>();

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddHostedService<StoppingService>();
            });

            await host.InitAndRunAsync();
            
            A.CallTo(() => initializer.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer.TeardownAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task InitAndRunAsync_throws_OperationCancelledException_when_called_with_cancelled_token()
        {
            var initializer = A.Fake<IAsyncTeardown>();
            var service = A.Fake<TestService>();

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer);
                services.AddHostedService(factory => service);
            });

            await Assert.ThrowsAsync<OperationCanceledException>(() => host.InitAndRunAsync(new CancellationToken(true)));
            
            A.CallTo(() => initializer.InitializeAsync(A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => initializer.TeardownAsync(A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => service.StartAsync(A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task InitAndRunAsync_without_initializer_does_not_fail()
        {
            var host = CreateHost(services =>
            {
                services.AddAsyncInitialization();
                services.AddHostedService<StoppingService>();
            });

            await host.InitAndRunAsync();
        }
    }
}
