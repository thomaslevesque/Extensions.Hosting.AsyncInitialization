using FakeItEasy;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Moq;

using System;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace Extensions.Hosting.AsyncInitialization.Tests
{
    public class WebHostAsyncInitializationTests
    {
        [Fact]
        public async Task Single_initializer_is_called()
        {
            IAsyncInitializer initializer = A.Fake<IAsyncInitializer>();

            IWebHost host = CreateWebHost(services => services.AddAsyncInitializer(initializer));

            await host.InitAsync();

            A.CallTo(() => initializer.InitializeAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Delegate_initializer_is_called()
        {
            Func<CancellationToken, Task> initializer = A.Fake<Func<CancellationToken, Task>>();

            IWebHost host = CreateWebHost(services => services.AddAsyncInitializer(initializer));

            await host.InitAsync();

            A.CallTo(() => initializer(CancellationToken.None)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Multiple_initializers_are_called_in_order()
        {
            IAsyncInitializer initializer1 = A.Fake<IAsyncInitializer>();
            IAsyncInitializer initializer2 = A.Fake<IAsyncInitializer>();
            IAsyncInitializer initializer3 = A.Fake<IAsyncInitializer>();

            IWebHost host = CreateWebHost(services =>
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
            IWebHost host = CreateWebHost(
                services =>
                {
                    services.AddScoped(sp => A.Fake<IDependency>());
                    services.AddAsyncInitializer<Initializer>();
                });

            await host.InitAsync();
        }

        [Fact]
        public async Task Failing_initializer_makes_initialization_fail()
        {
            IAsyncInitializer initializer1 = A.Fake<IAsyncInitializer>();
            IAsyncInitializer initializer2 = A.Fake<IAsyncInitializer>();
            IAsyncInitializer initializer3 = A.Fake<IAsyncInitializer>();

            A.CallTo(() => initializer2.InitializeAsync(default)).ThrowsAsync(() => new Exception("oops"));

            IWebHost host = CreateWebHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
            });

            Exception exception = await Record.ExceptionAsync(() => host.InitAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            A.CallTo(() => initializer1.InitializeAsync(default)).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer3.InitializeAsync(default)).MustNotHaveHappened();
        }

        [Fact]
        public async Task Cancelled_initializer_makes_initialization_fail()
        {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            Mock<IAsyncInitializer> initializer1Mock = new Mock<IAsyncInitializer>(MockBehavior.Strict);
            Mock<IAsyncInitializer> initializer2Mock = new Mock<IAsyncInitializer>(MockBehavior.Strict);
            Mock<IAsyncInitializer> initializer3Mock = new Mock<IAsyncInitializer>(MockBehavior.Strict);


            initializer1Mock.Setup(mock => mock.InitializeAsync(It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask)
                            .Callback(() =>
                            {
                                cancellationTokenSource.Cancel();
                            });

            IWebHost host = CreateWebHost(services =>
            {
                services.AddAsyncInitializer(initializer1Mock.Object);
                services.AddAsyncInitializer(initializer2Mock.Object);
                services.AddAsyncInitializer(initializer3Mock.Object);
            });

            Func<Task> initializingHost = async () => await host.InitAsync(cancellationTokenSource.Token);

            await initializingHost.Should().ThrowAsync<OperationCanceledException>();

            initializer1Mock.Verify(mock => mock.InitializeAsync(It.IsAny<CancellationToken>()), Moq.Times.Once);
        }

        [Fact]
        public async Task InitAsync_throws_InvalidOperationException_when_services_are_not_registered()
        {
            IWebHost host = CreateWebHost(services => { });
            Exception exception = await Record.ExceptionAsync(() => host.InitAsync());
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("The async initialization service isn't registered, register it by calling AddAsyncInitialization() on the service collection or by adding an async initializer.", exception.Message);
        }

        private static IWebHost CreateWebHost(Action<IServiceCollection> configureServices) =>
              new WebHostBuilder()
                .ConfigureServices(configureServices)
                .UseStartup<Startup>()
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

            ///<inheritdoc/>
            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public class Startup
        {
            public void Configure(IServiceCollection services) { }
        }
    }
}