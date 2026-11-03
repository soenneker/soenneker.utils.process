using Microsoft.Extensions.Logging;
using Soenneker.Utils.Process.Abstract;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.Runtime;

namespace Soenneker.Utils.Process;

public sealed partial class ProcessUtil : IProcessUtil
{
    private readonly ILogger<ProcessUtil> _logger;

    private readonly bool _isWindows = RuntimeUtil.IsWindows();

    public ProcessUtil(ILogger<ProcessUtil> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> CommandExists(string command, CancellationToken ct = default)
    {
        try
        {
            if (_isWindows)
            {
                await StartAndGetOutput("where", command, "", timeout: TimeSpan.FromSeconds(2), cancellationToken: ct)
                    .NoSync();
            }
            else
            {
                await StartAndGetOutput("/usr/bin/env", $"bash -lc \"command -v {command}\"", "", timeout: TimeSpan.FromSeconds(2), cancellationToken: ct)
                    .NoSync();
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<bool> CommandExistsAndRuns(string command, string versionArgs = "--version", TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // 1) fast path: does it exist on PATH?
        if (!await CommandExists(command, cancellationToken)
                .NoSync())
            return false;

        // 2) can it actually run and exit?
        try
        {
            _ = await StartAndGetOutput(command, versionArgs, "", timeout ?? TimeSpan.FromSeconds(3), cancellationToken)
                .NoSync();

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<string> StartAndGetOutput(string fileName = "", string arguments = "", string workingDirectory = "", TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("🟢 Starting: {file} {args} (in {cwd})", fileName, arguments, workingDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var process = new System.Diagnostics.Process();
        process.StartInfo = psi;

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start '{fileName}'.");

        // Prevent interactive tools from waiting on stdin
        try
        {
            process.StandardInput.Close();
        }
        catch
        {
        }

        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<(byte[] StandardOutput, byte[] StandardError)> outputTask = process.ReadAllBytesAsync(readCancellation.Token);
        Task completion = Task.WhenAll(process.WaitForExitAsync(readCancellation.Token), outputTask);

        try
        {
            if (timeout.HasValue)
                await completion.WaitAsync(timeout.Value, cancellationToken).NoSync();
            else
                await completion.NoSync();
        }
        catch (TimeoutException)
        {
            TryKillProcessTree(process);
            await readCancellation.CancelAsync().NoSync();
            await ObserveCompletion(completion).NoSync();
            throw new TimeoutException($"Process '{fileName}' did not exit within {timeout!.Value.TotalMilliseconds} ms.");
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await ObserveCompletion(completion).NoSync();
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        (byte[] standardOutput, byte[] standardError) = await outputTask.NoSync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'{fileName} {arguments}' exited with {process.ExitCode}" +
                                                (standardError.Length > 0 ? Environment.NewLine + DecodeProcessOutput(standardError) : string.Empty));

        return DecodeProcessOutput(standardOutput);
    }

    private static string DecodeProcessOutput(byte[] bytes)
    {
        // ReadAllTextAsync decodes with the configured encoding without detecting a BOM.
        // Retain the previous StreamReader behavior for UTF-8/UTF-16/UTF-32 output.
        using var reader = new StreamReader(new ReadOnlyMemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
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
                processes[i]
                    .Dispose();

            await Kill(first, waitForExit, cancellationToken)
                .NoSync();
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
            processes[i]
                .Dispose();

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

        cancellationToken.ThrowIfCancellationRequested();
        using var proc = new System.Diagnostics.Process { StartInfo = startInfo };

        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start bash process.");

            await foreach (ProcessOutputLine line in proc.ReadAllLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (line.StandardError)
                    LogStandardError(_logger, line.Content);
                else
                    LogStandardOutput(_logger, line.Content);
            }

            await proc.WaitForExitAsync(cancellationToken).NoSync();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"Run failed with exit code {proc.ExitCode} for command: {command}");
        }
        finally
        {
            TryKillProcessTree(proc);
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

        cancellationToken.ThrowIfCancellationRequested();
        using var proc = new System.Diagnostics.Process { StartInfo = startInfo };

        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start cmd.exe");

            await foreach (ProcessOutputLine line in proc.ReadAllLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (line.StandardError)
                    LogErrorLine(_logger, line.Content);
                else
                    LogInformationLine(_logger, line.Content);
            }

            await proc.WaitForExitAsync(cancellationToken).NoSync();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"CMD '{command}' exited with code {proc.ExitCode}.");
        }
        finally
        {
            TryKillProcessTree(proc);
        }
    }

    private static void TryKillProcessTree(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup during cancellation or early termination.
        }
    }
}
