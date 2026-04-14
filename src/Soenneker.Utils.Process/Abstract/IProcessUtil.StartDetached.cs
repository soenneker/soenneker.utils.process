using Soenneker.Utils.Process.Dtos;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process.Abstract;

public partial interface IProcessUtil
{
    /// <summary>
    /// Starts a process without waiting for it to exit and optionally wires output/error callbacks and cancellation.
    /// </summary>
    /// <param name="dto">The detached process configuration.</param>
    /// <param name="cancellationToken">A token that will kill the process tree when canceled.</param>
    /// <returns>The started process, or <see langword="null"/> if the process could not be started.</returns>
    ValueTask<System.Diagnostics.Process?> StartDetached(ProcessStartDto dto, CancellationToken cancellationToken = default);
}