using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.Process.Abstract;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process;

///<inheritdoc cref="IProcessUtil"/>
public sealed partial class ProcessUtil : IProcessUtil
{
    private readonly ILogger<ProcessUtil> _logger;

    public ProcessUtil(ILogger<ProcessUtil> logger)
    {
        _logger = logger;
    }

    public async ValueTask<List<string>> Start(string fileName, string? workingDirectory = null, string? arguments = null, bool admin = false,
        bool waitForExit = true, TimeSpan? timeout = null, bool log = true, Dictionary<string, string>? environmentalVars = null,
        CancellationToken cancellationToken = default)
    {
        var outputLines = new List<string>(256);
        Lock sync = new();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        if (environmentalVars is { Count: > 0 })
        {
            foreach (KeyValuePair<string, string> kvp in environmentalVars)
                startInfo.Environment[kvp.Key] = kvp.Value;
        }

        if (workingDirectory.HasContent())
            startInfo.WorkingDirectory = workingDirectory;

        // Elevation on Windows requires UseShellExecute = true (no redirects possible)
        if (admin && OperatingSystem.IsWindows())
        {
            startInfo.Verb = "runas";

            if (startInfo.RedirectStandardOutput || startInfo.RedirectStandardError)
            {
                // We must disable redirection for elevation
                startInfo.RedirectStandardOutput = false;
                startInfo.RedirectStandardError = false;
                startInfo.StandardOutputEncoding = null;
                startInfo.StandardErrorEncoding = null;
                _logger.LogDebug("Elevation requested: switching to UseShellExecute=true (output will not be captured).");
            }

            startInfo.UseShellExecute = true;
            startInfo.CreateNoWindow = false; // elevated prompts require a window
        }

        using var process = new System.Diagnostics.Process();
        process.StartInfo = startInfo;
        process.EnableRaisingEvents = true;

        // TCS that completes when the async readers finish (e.Data == null)
        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OutputHandler(object? _, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult();
                return;
            }

            using (sync.EnterScope())
                outputLines.Add(e.Data);

            if (log)
                _logger.LogInformation("{Data}", e.Data);
        }

        void ErrorHandler(object? _, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult();
                return;
            }

            var line = $"ERROR: {e.Data}";
            using (sync.EnterScope())
                outputLines.Add(line);

            if (log)
                _logger.LogError("{Data}", e.Data);
        }

        if (startInfo.RedirectStandardOutput)
            process.OutputDataReceived += OutputHandler;
        if (startInfo.RedirectStandardError)
            process.ErrorDataReceived += ErrorHandler;

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start process '{fileName}'.");

            if (startInfo.RedirectStandardOutput)
                process.BeginOutputReadLine();
            if (startInfo.RedirectStandardError)
                process.BeginErrorReadLine();

            if (waitForExit)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                Task allDone = startInfo.UseShellExecute
                    ? process.WaitForExitAsync(linkedCts.Token) // no redirection, nothing to drain
                    : Task.WhenAll(process.WaitForExitAsync(linkedCts.Token), stdoutDone.Task, stderrDone.Task);

                Task delayTask = timeout.HasValue ? Task.Delay(timeout.Value, linkedCts.Token) : Task.Delay(Timeout.Infinite, linkedCts.Token);

                Task completed = await Task.WhenAny(allDone, delayTask).NoSync();

                if (completed != allDone)
                {
                    // timeout or external cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            linkedCts.Cancel();
                            if (!process.HasExited)
                                process.Kill(entireProcessTree: true);
                        }
                        catch
                        {
                            /* best-effort */
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    else
                    {
                        try
                        {
                            if (!process.HasExited)
                                process.Kill(entireProcessTree: true);
                        }
                        catch
                        {
                            /* best-effort */
                        }

                        throw new TimeoutException($"Process '{fileName}' did not exit within {timeout!.Value.TotalMilliseconds} ms.");
                    }
                }

                // ensure the awaited task actually throws if needed
                await allDone.NoSync();

                if (process.ExitCode != 0)
                {
                    string tail = GetTail(outputLines, 40);
                    throw new InvalidOperationException(
                        $"Process '{fileName}' exited with code {process.ExitCode}.{(tail.Length > 0 ? Environment.NewLine + tail : string.Empty)}");
                }
            }

            // snapshot the lines
            using (sync.EnterScope())
                return [..outputLines];
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

            string tail = GetTail(outputLines, 40);
            // Wrap with captured context to aid callers
            throw new InvalidOperationException($"Error running process '{fileName}'.{(tail.Length > 0 ? Environment.NewLine + tail : string.Empty)}", ex);
        }
        finally
        {
            if (startInfo.RedirectStandardOutput)
            {
                process.OutputDataReceived -= OutputHandler;
                try
                {
                    process.CancelOutputRead();
                }
                catch (InvalidOperationException)
                {
                }
            }

            if (startInfo.RedirectStandardError)
            {
                process.ErrorDataReceived -= ErrorHandler;
                try
                {
                    process.CancelErrorRead();
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        static string GetTail(List<string> lines, int max)
        {
            int count = lines.Count;
            if (count == 0)
                return string.Empty;
            int start = Math.Max(count - max, 0);
            return string.Join(Environment.NewLine, CollectionsMarshal.AsSpan(lines).Slice(start));
        }
    }

    public async ValueTask<bool> CommandExists(string command, CancellationToken ct = default)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                await StartAndGetOutput("where", command, "", ct).NoSync();
            else
                await StartAndGetOutput("/usr/bin/env", $"bash -lc \"command -v {command}\"", "", ct).NoSync();
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

        Task<string> stdOut = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stdErr = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdOut, stdErr, process.WaitForExitAsync(cancellationToken)).NoSync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"'{fileName} {arguments}' exited with {process.ExitCode}{(stdErr.Result.Length > 0 ? Environment.NewLine + stdErr.Result : string.Empty)}");

        return stdOut.Result;
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
            await Kill(name, waitForExit, cancellationToken: cancellationToken).NoSync();
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
                await Kill(process, waitForExit, cancellationToken: cancellationToken).NoSync();
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

        // capture (optional) while ensuring we detect end-of-stream
        var outputLines = new List<string>(128);
        Lock sync = new();

        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        DataReceivedEventHandler outHandler = (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult();
                return;
            }

            using (sync.EnterScope())
                outputLines.Add(e.Data);

            _logger.LogInformation("{Line}", e.Data);
        };

        DataReceivedEventHandler errHandler = (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult();
                return;
            }

            var err = $"ERROR: {e.Data}";
            using (sync.EnterScope())
                outputLines.Add(err);

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