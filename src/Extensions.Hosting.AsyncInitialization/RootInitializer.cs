using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Extensions.Hosting.AsyncInitialization
{
    internal class RootInitializer
    {
        private readonly ILogger<RootInitializer> _logger;
        private readonly IEnumerable<IAsyncInitializer> _initializers;

        public RootInitializer(ILogger<RootInitializer> logger, IEnumerable<IAsyncInitializer> initializers)
        {
            _logger = logger;
            _initializers = initializers;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting async initialization");

            try
            {
                foreach (var initializer in _initializers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInformation("Starting async initialization for {InitializerType}", initializer.GetType());
                    try
                    {
                        await initializer.InitializeAsync(cancellationToken);
                        _logger.LogInformation("Async initialization for {InitializerType} completed", initializer.GetType());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Async initialization for {InitializerType} failed", initializer.GetType());
                        throw;
                    }
                }

                _logger.LogInformation("Async initialization completed");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Async initialization cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Async initialization failed");
                throw;
            }
        }
    }
}