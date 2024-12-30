using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Enumerable;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Extensions.Task;
using System.Collections.Concurrent;

namespace Soenneker.Utils.Process;

/// <inheritdoc cref="IProcessUtil"/>
public class ProcessUtil : IProcessUtil
{
    private readonly ILogger<ProcessUtil> _logger;

    public ProcessUtil(ILogger<ProcessUtil> logger)
    {
        _logger = logger;
    }

    public async ValueTask<List<string>> Start(
        string name,
        string? directory = null,
        string? arguments = null,
        bool admin = false,
        bool waitForExit = true,
        bool log = true,
        CancellationToken cancellationToken = default)
    {
        // Use ConcurrentQueue to store output lines in a thread-safe manner while preserving order
        var outputLines = new ConcurrentQueue<string>();

        var startInfo = new ProcessStartInfo
        {
            FileName = name,
            Arguments = arguments ?? string.Empty,
            RedirectStandardOutput = true, // Always redirect to capture output
            RedirectStandardError = true, // Always redirect to capture error
            UseShellExecute = false, // Must be false to redirect streams
            CreateNoWindow = true, // Do not create a window
        };

        if (!string.IsNullOrEmpty(directory))
        {
            startInfo.WorkingDirectory = directory;
        }

        if (admin)
        {
            startInfo.Verb = "runas";
        }

        using var process = new System.Diagnostics.Process();
        process.StartInfo = startInfo;
        process.EnableRaisingEvents = true;

        // Assign named event handlers

        DataReceivedEventHandler outputHandler = (sender, e) =>
        {
            if (e.Data != null)
            {
                outputLines.Enqueue(e.Data);

                if (log)
                    _logger.LogInformation(e.Data);
            }
        };

        DataReceivedEventHandler errorHandler = (sender, e) =>
        {
            if (e.Data != null)
            {
                var errorLine = $"ERROR: {e.Data}";
                outputLines.Enqueue(errorLine);

                if (log)
                    _logger.LogError(e.Data);
            }
        };

        process.OutputDataReceived += outputHandler;
        process.ErrorDataReceived += errorHandler;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process '{name}'.");
            }

            // Begin asynchronous reading of the output streams
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (waitForExit)
            {
                // Register cancellation
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task waitTask = process.WaitForExitAsync(linkedCts.Token);

                // Handle cancellation
                Task completedTask = await Task.WhenAny(waitTask, Task.Delay(Timeout.Infinite, cancellationToken)).NoSync();

                if (completedTask == waitTask)
                {
                    // Process exited
                    await waitTask.NoSync(); // Ensure any exceptions/cancellations are observed
                }
                else
                {
                    // Cancellation requested
                    try
                    {
                        await linkedCts.CancelAsync().NoSync();
                        // Optionally, kill the process if cancellation is requested
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // Ignore exceptions from killing the process
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }

                // After process exits, check exit code
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Process '{name}' exited with code {process.ExitCode}.");
                }
            }

            return new List<string>(outputLines);
        }
        catch (OperationCanceledException)
        {
            // Handle cancellation
            _logger.LogWarning("Process execution was canceled.");
            throw;
        }
        catch (Exception ex)
        {
            // Log and rethrow exceptions
            _logger.LogError(ex, "An error occurred while starting the process '{name}'.", name);
            throw new InvalidOperationException($"An error occurred while starting the process '{name}'.", ex);
        }
        finally
        {
            // Unhook event handlers to prevent memory leaks and unintended behavior
            process.OutputDataReceived -= outputHandler;
            process.ErrorDataReceived -= errorHandler;

            // Ensure that asynchronous event handling is stopped
            try
            {
                process.CancelOutputRead();
            }
            catch (InvalidOperationException)
            {
                // No async read operation is in progress on the stream.
                // Ignore the exception.
            }

            try
            {
                process.CancelErrorRead();
            }
            catch (InvalidOperationException)
            {
                // No async read operation is in progress on the stream.
                // Ignore the exception.
            }
        }
    }

    public ValueTask<List<string>> StartIfNotRunning(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = false, bool log = true,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning(name))
            return ValueTask.FromResult(new List<string>());

        return Start(name, directory, arguments, admin, waitForExit, log, cancellationToken);
    }

    public void KillByNames(IEnumerable<string> processNames)
    {
        foreach (string names in processNames)
        {
            Kill(names);
        }
    }

    public void Kill(string name)
    {
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(name);

        if (processes.Empty())
        {
            _logger.LogDebug("No processes running by name {name}", name);
            return;
        }

        _logger.LogDebug("Killing {num} processes...", processes.Length);

        foreach (System.Diagnostics.Process process in processes)
        {
            Kill(process);
            break;
        }
    }

    public void KillThatStartWith(string startsWith)
    {
        System.Diagnostics.Process[] totalProcesses = System.Diagnostics.Process.GetProcesses();

        List<System.Diagnostics.Process> processesToKill = totalProcesses.Where(process => process.ProcessName.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase)).ToList();

        if (processesToKill.Empty())
        {
            _logger.LogDebug("No processes start with {startsWith}", startsWith);
            return;
        }

        _logger.LogDebug("Killing {num} processes...", processesToKill.Count);

        foreach (System.Diagnostics.Process process in processesToKill)
        {
            Kill(process);
        }
    }

    public void Kill(System.Diagnostics.Process process)
    {
        _logger.LogInformation("Killing process {processName} (id {id}) ...", process.ProcessName, process.Id);
        process.Kill(false);
    }

    public bool IsRunning(string name)
    {
        _logger.LogInformation("Checking if {process} is running...", name);

        bool isRunning = System.Diagnostics.Process.GetProcessesByName(name).Length > 0;

        if (isRunning)
            _logger.LogInformation("{process} is running", name);
        else
            _logger.LogInformation("{process} is NOT running", name);

        return isRunning;
    }
}