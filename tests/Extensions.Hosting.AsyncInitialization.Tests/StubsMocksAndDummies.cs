using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Extensions.Hosting.AsyncInitialization.Tests
{
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
        public Task InitializeAsync() => Task.CompletedTask;
    }

    public interface IDummyInitializer : IAsyncInitializer
    {

    }

    public class DummyInitializer : IDummyInitializer
    {
        private readonly Spy _spy;
        public DummyInitializer(Spy spy)
        {
            _spy = spy;
        }

        public Task InitializeAsync()
        {
            _spy.Initialized = true;
            return Task.CompletedTask;
        }
    }

    public class Spy
    {
        public bool Initialized { get; set; }
    }

    public class AnotherSpy : Spy
    {

    }

    public class AnotherDummyInitializer : IDummyInitializer
    {
        private readonly AnotherSpy _anotherSpy;

        public AnotherDummyInitializer(AnotherSpy anotherSpy)
        {
            _anotherSpy = anotherSpy;
        }

        public Task InitializeAsync()
        {
            _anotherSpy.Initialized = true;
            return Task.CompletedTask;
        }
    }
}
