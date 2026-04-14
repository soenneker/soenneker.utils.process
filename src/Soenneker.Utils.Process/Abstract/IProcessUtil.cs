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
public partial interface IProcessUtil
{
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
    /// <param name="environmentalVars">Optional environment variables to add to the process.</param>
    /// <param name="cancellationToken">
    /// A token to observe for cancellation while waiting for the process to exit.
    /// </param>
    /// <returns>
    /// If a process with the given <paramref name="name"/> is already running, returns an empty list.
    /// Otherwise, behaves like <see cref="Start(string, string?, string?, bool, bool, TimeSpan?, bool, Dictionary{string, string}?, CancellationToken)"/>.
    /// </returns>
    ValueTask<List<string>> StartIfNotRunning(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = false,
        TimeSpan? timeout = null, bool log = true, Dictionary<string, string>? environmentalVars = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to kill all processes whose names exactly match any of the given names.
    /// </summary>
    /// <param name="processNames">A collection of process names (without extension) to terminate.</param>
    /// <param name="waitForExit">If true, waits for each process to exit after it is killed.</param>
    /// <param name="cancellationToken">A token to cancel waiting for process exit.</param>
    ValueTask KillByNames(IEnumerable<string> processNames, bool waitForExit = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills the first process found whose name exactly matches <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The process name (without extension) to kill.</param>
    /// <param name="waitForExit">If true, waits for the killed process to exit.</param>
    /// <param name="cancellationToken">A token to cancel waiting for process exit.</param>
    Task Kill(string name, bool waitForExit = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills all running processes whose process name (without extension) starts with the specified prefix.
    /// </summary>
    /// <param name="startsWith">The prefix to match against running process names.</param>
    /// <param name="waitForExit">If true, waits for each killed process to exit.</param>
    /// <param name="cancellationToken">A token to cancel waiting for process exit.</param>
    ValueTask KillThatStartWith(string startsWith, bool waitForExit = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills the specified <see cref="System.Diagnostics.Process"/> instance.
    /// </summary>
    /// <param name="process">The process instance to terminate.</param>
    /// <param name="waitForExit">If true, waits for the process to exit after killing it.</param>
    /// <param name="cancellationToken">A token to cancel waiting for process exit.</param>
    Task Kill(System.Diagnostics.Process process, bool waitForExit = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether any running process exists with the specified name (without extension).
    /// </summary>
    /// <param name="name">The process name to query.</param>
    /// <returns>True if one or more processes with that name are running; otherwise, false.</returns>
    [Pure]
    bool IsRunning(string name);

    /// <summary>
    /// Executes a command using the Bash shell with the specified arguments in the given working directory.
    /// </summary>
    /// <param name="command">The shell command to run (for example, <c>make</c> or <c>git</c>).</param>
    /// <param name="workingDir">The directory in which to execute the command.</param>
    /// <param name="environmentalVars">Optional environment variables to add to the process.</param>
    /// <param name="cancellationToken">Optional token to cancel the command execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    /// <exception cref="Exception">Thrown if the command exits with a non-zero status code.</exception>
    /// <remarks>
    /// This method uses <c>/bin/bash -c</c> to execute the full command, allowing for Bash-specific syntax like pipes, globs, and redirection.
    /// </remarks>
    ValueTask BashRun(string command, string workingDir, Dictionary<string, string>? environmentalVars = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously executes a command-line process in the specified working directory, with optional environment
    /// variables and cancellation support.
    /// </summary>
    /// <param name="command">The command to execute. This should be the name or path of a valid executable or script.</param>
    /// <param name="workingDirectory">The directory in which to execute the command. Must be a valid file system path.</param>
    /// <param name="environmentalVars">A dictionary containing environment variables to set for the process, or null to use the current environment variables.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation before the command completes.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation of executing the command.</returns>
    ValueTask CmdRun(string command, string workingDirectory, Dictionary<string, string>? environmentalVars = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a process with the specified parameters and asynchronously retrieves its standard output as a string.
    /// </summary>
    /// <param name="fileName">The name or path of the executable file to start. If not specified, an empty string is used.</param>
    /// <param name="arguments">The command-line arguments to pass to the executable. If not specified, an empty string is used.</param>
    /// <param name="workingDirectory">The directory in which to start the process. If not specified, the current working directory is used.</param>
    /// <param name="timeout">The maximum duration to wait for the process to complete. If null, the operation waits indefinitely.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation. The default value is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the standard output of the process as a string.</returns>
    ValueTask<string> StartAndGetOutput(string fileName = "", string arguments = "", string workingDirectory = "", TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously determines whether the specified command is available on the system.
    /// </summary>
    /// <param name="command">The name of the command to check for existence. This parameter cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation. The default value is <see cref="CancellationToken.None"/>.</param>
    /// <returns><see langword="true"/> if the command is found; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> CommandExists(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether the specified command exists on the system and can be executed successfully.
    /// </summary>
    /// <param name="command">The name of the command-line executable to check for existence and execution capability.</param>
    /// <param name="versionArgs">The arguments to pass to the command to verify its execution, typically used to retrieve version information.</param>
    /// <param name="timeout">The maximum duration to wait for the command to execute before timing out.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the command exists and runs successfully; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> CommandExistsAndRuns(string command, string versionArgs = "--version", TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}