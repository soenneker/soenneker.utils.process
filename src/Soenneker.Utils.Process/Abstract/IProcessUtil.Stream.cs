using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;

namespace Soenneker.Utils.Process.Abstract;

/// <summary>
/// Defines the process util contract.
/// </summary>
public partial interface IProcessUtil
{
    /// <summary>
    /// Runs <paramref name="fileName"/> and produces every line it writes.
    /// Redirected stdout and stderr are merged in observation order; stderr lines have a <c>[stderr] </c> prefix.
    /// </summary>
    /// <remarks>Cancellation or early disposal stops the process tree. Normal completion waits for process exit, even if output closes first.</remarks>
    /// <returns>Runs <paramref name="fileName"/> and produces each observed line.</returns>
    IAsyncEnumerable<string> StreamLines(string fileName, string? workingDirectory = null, string? arguments = null,
        bool redirectOutput = true, bool redirectError = true, IDictionary<string, string>? environmentVariables = null, ILogger? logger = null,
        CancellationToken cancellationToken = default);
}
