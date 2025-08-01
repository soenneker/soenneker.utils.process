﻿using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;
using Soenneker.Utils.Process.Abstract;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.Utils.Process;

///<inheritdoc cref="IProcessUtil"/>
public sealed partial class ProcessUtil : IProcessUtil
{
    private readonly ILogger<ProcessUtil> _logger;

    public ProcessUtil(ILogger<ProcessUtil> logger)
    {
        _logger = logger;
    }

    public async ValueTask<List<string>> Start(string fileName, string? workingDirectory = null, string? arguments = null, bool admin = false, bool waitForExit = true,
        TimeSpan? timeout = null, bool log = true, Dictionary<string, string>? environmentalVars = null, CancellationToken cancellationToken = default)
    {
        var outputLines = new List<string>(128);
        Lock sync = new();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (environmentalVars is {Count: > 0})
        {
            foreach ((string k, string v) in environmentalVars)
                startInfo.Environment[k] = v; // .Environment works on every OS
        }

        if (workingDirectory.HasContent())
            startInfo.WorkingDirectory = workingDirectory;

        if (admin)
            startInfo.Verb = "runas";

        using var process = new System.Diagnostics.Process();
        process.StartInfo = startInfo;
        process.EnableRaisingEvents = true;

        void OutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;

            using (sync.EnterScope())
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

            using (sync.EnterScope())
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
                throw new InvalidOperationException($"Failed to start process '{fileName}'.");

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
                        throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}.");
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

                        throw new TimeoutException($"Process '{fileName}' did not exit within {timeout.Value.TotalMilliseconds} ms.");
                    }
                }
            }

            using (sync.EnterScope())
            {
                return outputLines;
            }
        }
        catch (OperationCanceledException)
        {
            if (log)
                _logger.LogWarning("Process '{Name}' was canceled.", fileName);

            throw;
        }
        catch (Exception ex)
        {
            if (log)
                _logger.LogError(ex, "Error while running process '{Name}'", fileName);

            throw new InvalidOperationException($"Error running process '{fileName}'", ex);
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

    public async ValueTask<bool> CommandExists(string command, CancellationToken cancellationToken = default)
    {
        try
        {
            await StartAndGetOutput("where", command, "", cancellationToken).NoSync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<string> StartAndGetOutput(string fileName = "", string arguments = "", string workingDirectory = "", CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🟢 Starting: {fileName} (in {workingDir})", fileName, workingDirectory);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new System.Diagnostics.Process();
        process.StartInfo = processStartInfo;
        process.Start();

        Task<string> readTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task waitTask = process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(readTask, waitTask).NoSync();
        return readTask.Result;
    }

    public ValueTask<List<string>> StartIfNotRunning(string name, string? directory = null, string? arguments = null, bool admin = false,
        bool waitForExit = false, TimeSpan? timeout = null, bool log = true, Dictionary<string, string>? environmentalVars = null,
        CancellationToken cancellationToken = default)
    {
        return IsRunning(name)
            ? ValueTask.FromResult(new List<string>(0))
            : Start(name, directory, arguments, admin, waitForExit, timeout, log, environmentalVars, cancellationToken);
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

    public async ValueTask BashRun(string command, string workingDir, Dictionary<string, string>? environmentalVars = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🟢 Running command: {command} (in {cwd})", command, workingDir);

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-lc \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (environmentalVars is {Count: > 0})
        {
            foreach ((string k, string v) in environmentalVars)
                startInfo.Environment[k] = v; // .Environment works on every OS
        }

        using System.Diagnostics.Process proc = System.Diagnostics.Process.Start(startInfo)!;

        DataReceivedEventHandler outHandler = (_, e) =>
        {
            if (e.Data != null)
                _logger.LogInformation("[stdout] {line}", e.Data);
        };
        DataReceivedEventHandler errHandler = (_, e) =>
        {
            if (e.Data != null)
                _logger.LogWarning("[stderr] {line}", e.Data);
        };

        proc.OutputDataReceived += outHandler;
        proc.ErrorDataReceived += errHandler;

        try
        {
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(cancellationToken).NoSync();

            if (proc.ExitCode != 0)
                throw new Exception($"Run failed with exit code {proc.ExitCode} for command: {command}");
        }
        finally
        {
            proc.OutputDataReceived -= outHandler;
            proc.ErrorDataReceived -= errHandler;
            try
            {
                proc.CancelOutputRead();
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                proc.CancelErrorRead();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    public async ValueTask CmdRun(string command, string workingDirectory, Dictionary<string, string>? environmentalVars = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔧 Running CMD command: {Command} (in {Cwd})", command, workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (environmentalVars is {Count: > 0})
        {
            foreach ((string k, string v) in environmentalVars)
                startInfo.Environment[k] = v; // .Environment works on every OS
        }

        using System.Diagnostics.Process proc = System.Diagnostics.Process.Start(startInfo)!;
        var outputLines = new List<string>(128);
        Lock sync = new();

        DataReceivedEventHandler outHandler = (_, e) =>
        {
            if (e.Data == null) return;
            using (sync.EnterScope())
            {
                outputLines.Add(e.Data);
            }

            _logger.LogInformation("{Line}", e.Data);
        };

        DataReceivedEventHandler errHandler = (_, e) =>
        {
            if (e.Data == null) return;
            var err = $"ERROR: {e.Data}";
            using (sync.EnterScope())
            {
                outputLines.Add(err);
            }

            _logger.LogError("{Line}", e.Data);
        };

        proc.OutputDataReceived += outHandler;
        proc.ErrorDataReceived += errHandler;

        try
        {
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(cancellationToken).NoSync();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"CMD '{command}' exited with code {proc.ExitCode}.");
        }
        finally
        {
            // unsubscribe and cancel reading
            proc.OutputDataReceived -= outHandler;
            proc.ErrorDataReceived -= errHandler;
            try
            {
                proc.CancelOutputRead();
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                proc.CancelErrorRead();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}