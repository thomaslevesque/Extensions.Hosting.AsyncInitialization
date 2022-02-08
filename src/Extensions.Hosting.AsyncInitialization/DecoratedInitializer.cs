using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Extensions.Hosting.AsyncInitialization
{
    internal class DecoratedInitializer : IAsyncInitializer
    {
        private readonly IAsyncInitializer _next;
        private readonly DecoratedInitializer? _decoratedInitializer;

        public DecoratedInitializer(IAsyncInitializer next, DecoratedInitializer decoratedInitializer) 
            : this(decoratedInitializer ?? throw new ArgumentNullException(nameof(decoratedInitializer)))
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public DecoratedInitializer(DecoratedInitializer? decoratedInitializer = null)
        {
            _decoratedInitializer = decoratedInitializer;
        }

        public async Task InitializeAsync()
        {
            if(_next != null)
                await _next?.InitializeAsync();

            if(_decoratedInitializer != null)
                await _decoratedInitializer?.InitializeAsync();
        }
    }
}
