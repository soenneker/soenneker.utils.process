using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process;

public sealed partial class ProcessUtil
{
    public async ValueTask<List<string>> Start(string fileName, string? workingDirectory = null, string? arguments = null, bool admin = false,
        bool waitForExit = true, TimeSpan? timeout = null, bool log = true, Dictionary<string, string>? environmentalVars = null,
        CancellationToken cancellationToken = default)
    {
        bool runElevated = admin && _isWindows;
        bool captureOutput = waitForExit && !runElevated;

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            UseShellExecute = runElevated,
            CreateNoWindow = !runElevated
        };

        if (captureOutput)
        {
            psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
            psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
        }

        if (runElevated)
            psi.Verb = "runas";

        if (environmentalVars is { Count: > 0 })
        {
            foreach (KeyValuePair<string, string> pair in environmentalVars)
                psi.Environment[pair.Key] = pair.Value;
        }

        if (workingDirectory.HasContent())
            psi.WorkingDirectory = workingDirectory;

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        List<string>? lines = captureOutput ? [] : null;
        Task? completion = null;

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start process '{fileName}'.");

            if (!waitForExit)
                return [];

            if (captureOutput)
            {
                Task stdoutTask = CaptureLines(process.StandardOutput, lines!, _logger, isError: false, log, cancellationToken);
                Task stderrTask = CaptureLines(process.StandardError, lines!, _logger, isError: true, log, cancellationToken);
                completion = WaitForExitAndDrain(process, stdoutTask, stderrTask, cancellationToken);
            }
            else
            {
                completion = process.WaitForExitAsync(cancellationToken);
            }

            if (timeout.HasValue)
                await completion.WaitAsync(timeout.Value, cancellationToken).NoSync();
            else
                await completion.NoSync();

            if (process.ExitCode != 0)
            {
                string tail = lines is null ? string.Empty : GetTail(lines, 40);
                throw new InvalidOperationException(
                    $"Process '{fileName}' exited with code {process.ExitCode}.{(tail.Length > 0 ? Environment.NewLine + tail : string.Empty)}");
            }

            return lines ?? [];
        }
        catch (TimeoutException)
        {
            TryKillProcessTree(process);

            if (completion is not null)
                await ObserveCompletion(completion).NoSync();

            throw new TimeoutException($"Process '{fileName}' did not exit within {timeout!.Value.TotalMilliseconds} ms.");
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);

            if (log && _logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("Process '{Name}' was canceled.", fileName);

            throw;
        }
        catch (Exception ex)
        {
            if (log && _logger.IsEnabled(LogLevel.Error))
                _logger.LogError(ex, "Error while running process '{Name}'", fileName);

            string tail = lines is null ? string.Empty : GetTail(lines, 40);
            throw new InvalidOperationException($"Error running process '{fileName}'.{(tail.Length > 0 ? Environment.NewLine + tail : string.Empty)}", ex);
        }
    }

    public async ValueTask StartAndWait(string fileName, string? workingDirectory = null, string? arguments = null, bool admin = false,
        TimeSpan? timeout = null, bool log = true, Dictionary<string, string>? environmentalVars = null, CancellationToken cancellationToken = default)
    {
        bool runElevated = admin && _isWindows;
        bool redirectOutput = !runElevated;

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
            UseShellExecute = runElevated,
            CreateNoWindow = !runElevated
        };

        if (runElevated)
            psi.Verb = "runas";

        if (environmentalVars is { Count: > 0 })
        {
            foreach (KeyValuePair<string, string> pair in environmentalVars)
                psi.Environment[pair.Key] = pair.Value;
        }

        if (workingDirectory.HasContent())
            psi.WorkingDirectory = workingDirectory;

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        Task? completion = null;

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start process '{fileName}'.");

            if (redirectOutput)
            {
                Task stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
                Task stderrTask = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
                completion = WaitForExitAndDrain(process, stdoutTask, stderrTask, cancellationToken);
            }
            else
            {
                completion = process.WaitForExitAsync(cancellationToken);
            }

            if (timeout.HasValue)
                await completion.WaitAsync(timeout.Value, cancellationToken).NoSync();
            else
                await completion.NoSync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}.");
        }
        catch (TimeoutException)
        {
            TryKillProcessTree(process);

            if (completion is not null)
                await ObserveCompletion(completion).NoSync();

            throw new TimeoutException($"Process '{fileName}' did not exit within {timeout!.Value.TotalMilliseconds} ms.");
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);

            if (log && _logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning("Process '{Name}' was canceled.", fileName);

            throw;
        }
        catch (Exception ex)
        {
            if (log && _logger.IsEnabled(LogLevel.Error))
                _logger.LogError(ex, "Error while running process '{Name}'", fileName);

            throw new InvalidOperationException($"Error running process '{fileName}'.", ex);
        }
    }

    private static async Task CaptureLines(StreamReader reader, List<string> lines, ILogger logger, bool isError, bool log,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).NoSync();
            if (line is null)
                return;

            string value = isError ? string.Concat("ERROR: ", line) : line;

            lock (lines)
            {
                lines.Add(value);
            }

            if (log)
            {
                if (isError)
                    LogErrorData(logger, line);
                else
                    LogInformationData(logger, line);
            }
        }
    }

    private static async Task WaitForExitAndDrain(System.Diagnostics.Process process, Task stdoutTask, Task stderrTask,
        CancellationToken cancellationToken)
    {
        Exception? error = null;

        try
        {
            await process.WaitForExitAsync(cancellationToken).NoSync();
        }
        catch (Exception ex)
        {
            error = ex;
        }

        try
        {
            await stdoutTask.NoSync();
        }
        catch (Exception ex)
        {
            error ??= ex;
        }

        try
        {
            await stderrTask.NoSync();
        }
        catch (Exception ex)
        {
            error ??= ex;
        }

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static async Task ObserveCompletion(Task completion)
    {
        try
        {
            await completion.NoSync();
        }
        catch
        {
            // The original timeout is the actionable failure.
        }
    }

    private static string GetTail(List<string> lines, int maxLines)
    {
        if (lines.Count == 0)
            return string.Empty;

        ReadOnlySpan<string> values = CollectionsMarshal.AsSpan(lines);
        int start = Math.Max(values.Length - maxLines, 0);
        return string.Join(Environment.NewLine, values[start..]);
    }
}
