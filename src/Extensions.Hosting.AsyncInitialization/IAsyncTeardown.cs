using System.Threading.Tasks;
using System.Threading;

namespace Extensions.Hosting.AsyncInitialization
{
    /// <summary>
    /// Represents a type that performs async teardown for types implementing <see cref="IAsyncInitializer"/>
    /// </summary>
    public interface IAsyncTeardown : IAsyncInitializer
    {
        /// <summary>
        /// Performs async teardown.
        /// </summary>
        /// <param name="cancellationToken">Notifies that the operation should be cancelled</param>
        /// <returns>A task that represents the teardown completion.</returns>
        Task TeardownAsync(CancellationToken cancellationToken);
    }
}