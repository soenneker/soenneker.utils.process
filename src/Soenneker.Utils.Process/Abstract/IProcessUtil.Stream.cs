using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Soenneker.Utils.Process.Abstract;

public partial interface IProcessUtil
{
    /// <summary>
    /// Runs <paramref name="fileName"/> and produces every line it writes.  When both stdout *and* stderr are redirected
    /// they are merged in‑order of arrival so the caller sees the exact chronological sequence.
    /// </summary>
    IAsyncEnumerable<string> StreamLines(string fileName, string? workingDirectory = null, string? arguments = null,
        bool redirectOutput = true, bool redirectError = true, IDictionary<string, string>? environmentVariables = null, ILogger? logger = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);
}