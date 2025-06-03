using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process.Abstract;

/// <summary>
/// A utility library implementing useful process operations.
/// <para/>
/// Typically registered as Scoped in IoC (unless consumed by a Singleton).
/// </summary>
public interface IProcessUtil
{
    /// <summary>
    /// Starts a new process with the specified parameters, optionally waits for it to exit,
    /// and collects standard output and error lines.
    /// </summary>
    /// <param name="name">The executable name or full path of the process to start.</param>
    /// <param name="directory">The working directory for the process. If null, uses the current directory.</param>
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
    ValueTask<List<string>> Start(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = true,
        TimeSpan? timeout = null, bool log = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a new process only if no existing process with the given name is currently running.
    /// If a matching process is already running, returns an empty list immediately.
    /// </summary>
    /// <param name="name">The executable name (without extension) of the process to check or start.</param>
    /// <param name="directory">The working directory for the new process. If null, uses the current directory.</param>
    /// <param name="arguments">The command-line arguments to pass if the process is started. If null or empty, no arguments are passed.</param>
    /// <param name="admin">If true, attempts to start the process with elevated (administrator) privileges.</param>
    /// <param name="waitForExit">
    /// If true, waits for the process to exit before returning (if it is started). If false, returns immediately.
    /// </param>
    /// <param name="timeout">
    /// The maximum amount of time to wait for the process to exit when <paramref name="waitForExit"/> is true.
    /// If null, waits indefinitely (or until <paramref name="cancellationToken"/> is signaled).
    /// </param>
    /// <param name="log">If true, logs each line of output and error when the process is started.</param>
    /// <param name="cancellationToken">
    /// A token to observe for cancellation while waiting for the process to exit.
    /// </param>
    /// <returns>
    /// If a process with the given <paramref name="name"/> is already running, returns an empty list.
    /// Otherwise, behaves like <see cref="Start(string, string?, string?, bool, bool, TimeSpan?, bool, CancellationToken)"/>.
    /// </returns>
    ValueTask<List<string>> StartIfNotRunning(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = false,
        TimeSpan? timeout = null, bool log = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to kill all processes whose names exactly match any of the given names.
    /// </summary>
    /// <param name="processNames">A collection of process names (without extension) to terminate.</param>
    void KillByNames(IEnumerable<string> processNames);

    /// <summary>
    /// Kills the first process found whose name exactly matches <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The process name (without extension) to kill.</param>
    void Kill(string name);

    /// <summary>
    /// Kills all running processes whose process name (without extension) starts with the specified prefix.
    /// </summary>
    /// <param name="startsWith">The prefix to match against running process names.</param>
    void KillThatStartWith(string startsWith);

    /// <summary>
    /// Kills the specified <see cref="System.Diagnostics.Process"/> instance.
    /// </summary>
    /// <param name="process">The process instance to terminate.</param>
    void Kill(System.Diagnostics.Process process);

    /// <summary>
    /// Checks whether any running process exists with the specified name (without extension).
    /// </summary>
    /// <param name="name">The process name to query.</param>
    /// <returns>True if one or more processes with that name are running; otherwise, false.</returns>
    [Pure]
    bool IsRunning(string name);
}