using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process.Abstract;

public partial interface IProcessUtil
{
    /// <summary>
    /// Starts a new process with the specified parameters, optionally waits for it to exit,
    /// and collects standard output and error lines.
    /// </summary>
    /// <param name="fileName">The executable name or full path of the process to start.</param>
    /// <param name="workingDirectory">The working directory for the process. If null, uses the current directory.</param>
    /// <param name="arguments">The command-line arguments to pass to the process. If null or empty, no arguments are passed.</param>
    /// <param name="admin">If true, attempts to start the process with elevated (administrator) privileges.</param>
    /// <param name="waitForExit">
    /// If true, waits for the process to exit before returning. If false, returns immediately and does not wait.
    /// </param>
    /// <param name="timeout">
    /// The maximum amount of time to wait for the process to exit when <paramref name="waitForExit"/> is true.
    /// If null, waits indefinitely (or until <paramref name="cancellationToken"/> is signaled).
    /// </param>
    /// <param name="log">If true, logs each line of output and error using the injected logger.</param>
    /// <param name="environmentalVars"></param>
    /// <param name="cancellationToken">
    /// A token to observe for cancellation. If canceled while waiting, the process is killed and an exception is thrown.
    /// </param>
    /// <returns>
    /// A list of strings containing each line written to standard output or prefixed with "ERROR: " for each
    /// line written to standard error.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the process fails to start or exits with a non-zero code.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown if <paramref name="waitForExit"/> is true and the process does not exit within <paramref name="timeout"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown if <paramref name="cancellationToken"/> is canceled while waiting for the process to exit.
    /// </exception>
    ValueTask<List<string>> Start(string fileName, string? workingDirectory = null, string? arguments = null, bool admin = false, bool waitForExit = true,
        TimeSpan? timeout = null, bool log = true, Dictionary<string, string>? environmentalVars = null, CancellationToken cancellationToken = default);
}