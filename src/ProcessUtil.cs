using Microsoft.Extensions.Logging;
using Soenneker.Utils.Process.Abstract;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.Utils.Process;

/// <inheritdoc cref="IProcessUtil"/>
public sealed partial class ProcessUtil : IProcessUtil
{
    private readonly ILogger<ProcessUtil> _logger;

    private static bool _isWindows => OperatingSystem.IsWindows();

    public ProcessUtil(ILogger<ProcessUtil> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> CommandExists(string command, CancellationToken ct = default)
    {
        try
        {
            if (_isWindows)
                await StartAndGetOutput("where", command, "", ct)
                    .NoSync();
            else
                await StartAndGetOutput("/usr/bin/env", $"bash -lc \"command -v {command}\"", "", ct)
                    .NoSync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<string> StartAndGetOutput(string fileName = "", string arguments = "", string workingDirectory = "",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🟢 Starting: {fileName} (in {workingDir})", fileName, workingDirectory);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var process = new System.Diagnostics.Process();
        process.StartInfo = processStartInfo;
        process.Start();

        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(stdOutTask, stdErrTask, process.WaitForExitAsync(cancellationToken))
                  .NoSync();

        string stdOut = stdOutTask.Result;
        string stdErr = stdErrTask.Result;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"'{fileName} {arguments}' exited with {process.ExitCode}{(stdErr.Length > 0 ? Environment.NewLine + stdErr : string.Empty)}");

        return stdOut;
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
            await Kill(name, waitForExit, cancellationToken: cancellationToken)
                .NoSync();
        }
    }

    public async Task Kill(string name, bool waitForExit = false, CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(name);

        if (processes.Length == 0)
        {
            _logger.LogDebug("No processes found with name '{Name}'", name);
            return;
        }

        _logger.LogDebug("Killing first of {Count} process(es) named '{Name}'", processes.Length, name);
        System.Diagnostics.Process? first = null;

        try
        {
            first = processes[0];

            for (var i = 1; i < processes.Length; i++)
                processes[i].Dispose();

            await Kill(first, waitForExit, cancellationToken).NoSync();
        }
        finally
        {
            // Ensure we release the handle for the process we waited on too.
            if (first is not null)
            {
                try
                {
                    first.Dispose();
                }
                catch
                {
                }
            }
        }
    }

    public async ValueTask KillThatStartWith(string prefix, bool waitForExit = false, CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Process[] total = System.Diagnostics.Process.GetProcesses();
        var killed = 0;

        foreach (System.Diagnostics.Process process in total)
        {
            try
            {
                if (process.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    await Kill(process, waitForExit, cancellationToken: cancellationToken)
                        .NoSync();
                    killed++;
                }
            }
            finally
            {
                process.Dispose();
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
        try
        {
            _logger.LogInformation("Killing process '{Name}' (PID {Id})...", process.ProcessName, process.Id);
            process.Kill(entireProcessTree: false);
        }
        catch (InvalidOperationException)
        {
            /* already exited */
        }
        catch (System.ComponentModel.Win32Exception)
        {
            /* access/privilege issues */
        }

        return waitForExit ? process.WaitForExitAsync(cancellationToken) : Task.CompletedTask;
    }

    public bool IsRunning(string name)
    {
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(name);
        bool running = processes.Length > 0;

        for (var i = 0; i < processes.Length; i++)
            processes[i].Dispose();

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
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        if (environmentalVars is { Count: > 0 })
        {
            foreach ((string k, string v) in environmentalVars)
                startInfo.Environment[k] = v;
        }

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = startInfo;
        proc.EnableRaisingEvents = true;

        // complete when e.Data == null
        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        DataReceivedEventHandler outHandler = (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult();
                return;
            }

            _logger.LogInformation("[stdout] {line}", e.Data);
        };

        DataReceivedEventHandler errHandler = (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult();
                return;
            }

            _logger.LogWarning("[stderr] {line}", e.Data);
        };

        proc.OutputDataReceived += outHandler;
        proc.ErrorDataReceived += errHandler;

        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start bash process.");

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // wait for: exit + both streams drained
            Task allDone = Task.WhenAll(proc.WaitForExitAsync(cancellationToken), stdoutDone.Task, stderrDone.Task);

            await allDone.NoSync();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"Run failed with exit code {proc.ExitCode} for command: {command}");
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
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        if (environmentalVars is { Count: > 0 })
        {
            foreach ((string k, string v) in environmentalVars)
                startInfo.Environment[k] = v;
        }

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = startInfo;
        proc.EnableRaisingEvents = true;

        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        DataReceivedEventHandler outHandler = (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult();
                return;
            }

            _logger.LogInformation("{Line}", e.Data);
        };

        DataReceivedEventHandler errHandler = (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult();
                return;
            }

            _logger.LogError("{Line}", e.Data);
        };

        proc.OutputDataReceived += outHandler;
        proc.ErrorDataReceived += errHandler;

        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start cmd.exe");

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // wait for: exit + both streams drained
            Task allDone = Task.WhenAll(proc.WaitForExitAsync(cancellationToken), stdoutDone.Task, stderrDone.Task);

            await allDone.NoSync();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"CMD '{command}' exited with code {proc.ExitCode}.");
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
}