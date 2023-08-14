using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static Extensions.Hosting.AsyncInitialization.Tests.CommonTestTypes;

namespace Extensions.Hosting.AsyncInitialization.Tests
{
    public class AsyncTeardownTests
    {
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
        public async Task Delegate_teardown_is_called()
        {
            var initializer = A.Fake<Func<CancellationToken, Task>>();
            var teardown = A.Fake<Func<CancellationToken, Task>>();

            var host = CreateHost(services => services.AddAsyncInitializer(initializer, teardown));

            await host.TeardownAsync();

            A.CallTo(() => initializer(CancellationToken.None)).MustNotHaveHappened();
            A.CallTo(() => teardown(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Failing_teardown_makes_teardown_fail()
        {
            var initializer1 = A.Fake<IAsyncTeardown>();
            var initializer2 = A.Fake<IAsyncTeardown>();
            var initializer3 = A.Fake<IAsyncTeardown>();

            A.CallTo(() => initializer2.TeardownAsync(default)).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
            });

            var exception = await Record.ExceptionAsync(() => host.TeardownAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            A.CallTo(() => initializer3.TeardownAsync(default)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer1.TeardownAsync(default)).MustNotHaveHappened();
        }

        [Fact]
        public async Task Teardown_with_scoped_dependency_is_resolved()
        {
            var host = CreateHost(
                services =>
                {
                    services.AddScoped(sp => A.Fake<IDependency>());
                    services.AddAsyncInitializer<InitializerWithTearDown>();
                },
                true);

            await host.TeardownAsync();
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
