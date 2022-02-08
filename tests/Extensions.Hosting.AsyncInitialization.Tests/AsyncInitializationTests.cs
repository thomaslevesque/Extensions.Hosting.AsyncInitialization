using System;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
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

            A.CallTo(() => initializer.InitializeAsync()).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Delegate_initializer_is_called()
        {
            var initializer = A.Fake<Func<Task>>();

            var host = CreateHost(services => services.AddAsyncInitializer(initializer));

            await host.InitAsync();

            A.CallTo(() => initializer()).MustHaveHappenedOnceExactly();
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

            A.CallTo(() => initializer1.InitializeAsync()).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => initializer2.InitializeAsync()).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => initializer3.InitializeAsync()).MustHaveHappenedOnceExactly());
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

            A.CallTo(() => initializer2.InitializeAsync()).ThrowsAsync(() => new Exception("oops"));

            var host = CreateHost(services =>
            {
                services.AddAsyncInitializer(initializer1);
                services.AddAsyncInitializer(initializer2);
                services.AddAsyncInitializer(initializer3);
            });

            var exception = await Record.ExceptionAsync(() => host.InitAsync());
            Assert.IsType<Exception>(exception);
            Assert.Equal("oops", exception.Message);

            A.CallTo(() => initializer1.InitializeAsync()).MustHaveHappenedOnceExactly();
            A.CallTo(() => initializer3.InitializeAsync()).MustNotHaveHappened();
        }

        [Fact]
        public async Task Registering_An_Interface_As_Initializer_Should_Call_The_Concrete_Implementations()
        {
            var spy = new Spy();
            var host = CreateHost(services => 
                services.AddSingleton(spy)
                    .AddTransient<IDummyInitializer, DummyInitializer>()
                    .AddAsyncInitializer<IDummyInitializer>())
                    ;

            await host.InitAsync();

            spy.Initialized.Should().BeTrue();
        }

        [Fact]
        public async Task Registering_MultipleInterfaces_As_Initializer_Should_Decorate_IAsyncInitializer()
        {
            var spy = new Spy();
            var anotherSpy = new AnotherSpy();

            var host = CreateHost(services =>
                services.AddSingleton(spy)
                    .AddSingleton(anotherSpy)
                    .AddTransient<IDummyInitializer, DummyInitializer>()
                    .AddTransient<IDummyInitializer, AnotherDummyInitializer>()
                    .AddAsyncInitializer<IDummyInitializer>())
                    ;

            await host.InitAsync();

            spy.Initialized.Should().BeTrue();
            anotherSpy.Initialized.Should().BeTrue();
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
    }
}