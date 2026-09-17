using Soenneker.Utils.Process.Dtos;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process.Abstract;

/// <summary>
/// Defines the process util contract.
/// </summary>
public partial interface IProcessUtil
{
    /// <summary>
    /// Starts a process without waiting for it to exit and optionally wires output/error callbacks and cancellation.
    /// </summary>
    /// <remarks>
    /// The caller owns and must dispose the returned process. Await its <c>WaitForExitAsync</c> method before
    /// reading the exit code or consuming the complete captured output. Output callbacks remain attached until
    /// both redirected streams reach EOF. Non-zero exit codes are returned to the caller without throwing.
    /// </remarks>
    /// <param name="dto">The detached process configuration.</param>
    /// <param name="cancellationToken">A token that will kill the process tree when canceled.</param>
    /// <returns>The started process, or <see langword="null"/> if the process could not be started.</returns>
    ValueTask<System.Diagnostics.Process?> StartDetached(ProcessStartDto dto, CancellationToken cancellationToken = default);
}
