using System.Threading;
using System.Threading.Tasks;

namespace Extensions.Hosting.AsyncInitialization
{
    /// <summary>
    /// Represents a type that performs async initialization.
    /// </summary>
    public interface IAsyncInitializer
    {
        /// <summary>
        /// Performs async initialization.
        /// </summary>
        /// <param name="cancellationToken">Notifies that the operation should be cancelled</param>
        /// <returns>A task that represents the initialization completion.</returns>
        Task InitializeAsync(CancellationToken cancellationToken);
    }
}
