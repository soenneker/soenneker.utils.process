using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Task;
using Soenneker.Utils.Process.Abstract;

namespace Soenneker.Utils.Process;

public sealed class ProcessUtil : IProcessUtil
{
    private readonly ILogger<ProcessUtil> _logger;

    public ProcessUtil(ILogger<ProcessUtil> logger)
    {
        _logger = logger;
    }

    public async ValueTask<List<string>> Start(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = true,
        bool log = true, CancellationToken cancellationToken = default)
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

        if (!string.IsNullOrEmpty(directory))
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
                Task completedTask = await Task.WhenAny(waitTask, Task.Delay(Timeout.Infinite, cancellationToken)).NoSync();

                if (completedTask != waitTask)
                {
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

                await waitTask.NoSync();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Process '{name}' exited with code {process.ExitCode}.");
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
        bool waitForExit = false, bool log = true, CancellationToken cancellationToken = default)
    {
        return IsRunning(name) ? ValueTask.FromResult(new List<string>(0)) : Start(name, directory, arguments, admin, waitForExit, log, cancellationToken);
    }

    public void KillByNames(IEnumerable<string> processNames)
    {
        foreach (string name in processNames)
            Kill(name);
    }

    public void Kill(string name)
    {
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(name);

        if (processes.Length == 0)
        {
            _logger.LogDebug("No processes found with name '{Name}'", name);
            return;
        }

        _logger.LogDebug("Killing first of {Count} process(es) named '{Name}'", processes.Length, name);
        Kill(processes[0]);
    }

    public void KillThatStartWith(string prefix)
    {
        System.Diagnostics.Process[] total = System.Diagnostics.Process.GetProcesses();
        var killed = 0;

        foreach (System.Diagnostics.Process process in total)
        {
            if (process.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Kill(process);
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

    public void Kill(System.Diagnostics.Process process)
    {
        _logger.LogInformation("Killing process '{Name}' (PID {Id})", process.ProcessName, process.Id);
        process.Kill(entireProcessTree: false);
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
}