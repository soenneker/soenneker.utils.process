using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;
using Soenneker.Utils.Process.Abstract;

namespace Soenneker.Utils.Process;

///<inheritdoc cref="IProcessUtil"/>
public sealed class ProcessUtil : IProcessUtil
{
    private readonly ILogger<ProcessUtil> _logger;

    public ProcessUtil(ILogger<ProcessUtil> logger)
    {
        _logger = logger;
    }

    public async ValueTask<List<string>> Start(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = true,
        TimeSpan? timeout = null, bool log = true, CancellationToken cancellationToken = default)
    {
        var outputLines = new List<string>(128);
        var sync = new object();

        var startInfo = new ProcessStartInfo
        {
            FileName = name,
            Arguments = arguments ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (directory.HasContent())
            startInfo.WorkingDirectory = directory;

        if (admin)
            startInfo.Verb = "runas";

        using var process = new System.Diagnostics.Process();
        process.StartInfo = startInfo;
        process.EnableRaisingEvents = true;

        void OutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;

            lock (sync)
            {
                outputLines.Add(e.Data);
            }

            if (log)
                _logger.LogInformation("{Data}", e.Data);
        }

        void ErrorHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;

            var line = $"ERROR: {e.Data}";

            lock (sync)
            {
                outputLines.Add(line);
            }

            if (log)
                _logger.LogError("{Data}", e.Data);
        }

        process.OutputDataReceived += OutputHandler;
        process.ErrorDataReceived += ErrorHandler;

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start process '{name}'.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (waitForExit)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task waitTask = process.WaitForExitAsync(linkedCts.Token);

                // choose a delay task based on timeout (infinite if null)
                Task delayTask = timeout.HasValue ? Task.Delay(timeout.Value, cancellationToken) : Task.Delay(Timeout.Infinite, cancellationToken);

                Task completed = await Task.WhenAny(waitTask, delayTask).NoSync();

                if (completed == waitTask)
                {
                    // process exited before timeout/cancellation
                    await waitTask.NoSync();

                    if (process.ExitCode != 0)
                        throw new InvalidOperationException($"Process '{name}' exited with code {process.ExitCode}.");
                }
                else
                {
                    // delayTask finished: either timeout or cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        // caller canceled
                        try
                        {
                            await linkedCts.CancelAsync().NoSync();

                            if (!process.HasExited)
                                process.Kill(entireProcessTree: true);
                        }
                        catch
                        {
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    else
                    {
                        // timeout expired
                        try
                        {
                            if (!process.HasExited)
                                process.Kill(entireProcessTree: true);
                        }
                        catch
                        {
                        }

                        throw new TimeoutException($"Process '{name}' did not exit within {timeout.Value.TotalMilliseconds} ms.");
                    }
                }
            }

            lock (sync)
            {
                return outputLines;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Process '{Name}' was canceled.", name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while running process '{Name}'", name);
            throw new InvalidOperationException($"Error running process '{name}'", ex);
        }
        finally
        {
            process.OutputDataReceived -= OutputHandler;
            process.ErrorDataReceived -= ErrorHandler;

            try
            {
                process.CancelOutputRead();
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                process.CancelErrorRead();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    public ValueTask<List<string>> StartIfNotRunning(string name, string? directory = null, string? arguments = null, bool admin = false,
        bool waitForExit = false, TimeSpan? timeout = null, bool log = true, CancellationToken cancellationToken = default)
    {
        return IsRunning(name)
            ? ValueTask.FromResult(new List<string>(0))
            : Start(name, directory, arguments, admin, waitForExit, timeout, log, cancellationToken);
    }

    public async ValueTask KillByNames(IEnumerable<string> processNames, bool waitForExit = false, CancellationToken cancellationToken = default)
    {
        foreach (string name in processNames)
        {
            await Kill(name, cancellationToken: cancellationToken).NoSync();
        }
    }

    public Task Kill(string name, bool waitForExit = false, CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(name);

        if (processes.Length == 0)
        {
            _logger.LogDebug("No processes found with name '{Name}'", name);
            return Task.CompletedTask;
        }

        _logger.LogDebug("Killing first of {Count} process(es) named '{Name}'", processes.Length, name);
        return Kill(processes[0], waitForExit, cancellationToken);
    }

    public async ValueTask KillThatStartWith(string prefix, bool waitForExit = false, CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Process[] total = System.Diagnostics.Process.GetProcesses();
        var killed = 0;

        foreach (System.Diagnostics.Process process in total)
        {
            if (process.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await Kill(process, cancellationToken: cancellationToken).NoSync();
                killed++;
            }
        }

        if (killed == 0)
        {
            _logger.LogDebug("No processes found starting with '{Prefix}'", prefix);
        }
        else
        {
            _logger.LogDebug("Killed {Count} process(es) starting with '{Prefix}'", killed, prefix);
        }
    }

    public Task Kill(System.Diagnostics.Process process, bool waitForExit = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Killing process '{Name}' (PID {Id})...", process.ProcessName, process.Id);
        process.Kill(entireProcessTree: false);

        if (waitForExit)
        {
            _logger.LogDebug("Waiting for process '{Name}' (PID {Id}) to exit...", process.ProcessName, process.Id);
            return process.WaitForExitAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }

    public bool IsRunning(string name)
    {
        bool running = System.Diagnostics.Process.GetProcessesByName(name).Length > 0;

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{Name} is {State}", name, running ? "running" : "not running");
        }

        return running;
    }

    public async ValueTask BashRun(string cmd, string args, string workingDir, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🟢 Running command: {cmd} {args} (in {cwd})", cmd, args, workingDir);

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{cmd} {args}\"",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi)!;

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                _logger.LogInformation("[stdout] {line}", e.Data);
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                _logger.LogWarning("[stderr] {line}", e.Data);
        };

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(cancellationToken).NoSync();

        if (proc.ExitCode != 0)
            throw new Exception($"BashRun failed with exit code {proc.ExitCode}: {cmd} {args}");
    }
}