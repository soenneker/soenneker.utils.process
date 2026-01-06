using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process;

public sealed partial class ProcessUtil
{
    // No closure allocations: static handlers + per-process state table.
    private static readonly ConditionalWeakTable<System.Diagnostics.Process, CaptureState> s_states = new();

    private static void StdoutHandler(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            if (sender is System.Diagnostics.Process p && s_states.TryGetValue(p, out CaptureState? st))
                st.StdoutDone.TrySetResult();
            return;
        }

        if (sender is not System.Diagnostics.Process proc || !s_states.TryGetValue(proc, out CaptureState? state))
            return;

        state.Lines.Enqueue(e.Data);

        if (state.Log && state.Logger.IsEnabled(LogLevel.Information))
            state.Logger.LogInformation("{Data}", e.Data);
    }

    private static void StderrHandler(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            if (sender is System.Diagnostics.Process p && s_states.TryGetValue(p, out CaptureState? st))
                st.StderrDone.TrySetResult();
            return;
        }

        if (sender is not System.Diagnostics.Process proc || !s_states.TryGetValue(proc, out CaptureState? state))
            return;

        // still allocates a string (required), but avoids interpolation overhead
        state.Lines.Enqueue(string.Concat("ERROR: ", e.Data));

        if (state.Log && state.Logger.IsEnabled(LogLevel.Error))
            state.Logger.LogError("{Data}", e.Data);
    }

    public async ValueTask<List<string>> Start(string fileName, string? workingDirectory = null, string? arguments = null, bool admin = false,
        bool waitForExit = true, TimeSpan? timeout = null, bool log = true, Dictionary<string, string>? environmentalVars = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
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
                psi.Environment[kvp.Key] = kvp.Value;
        }

        if (workingDirectory.HasContent())
            psi.WorkingDirectory = workingDirectory;

        // Elevation on Windows requires UseShellExecute = true (no redirects possible)
        if (admin && _isWindows)
        {
            psi.Verb = "runas";

            if (psi.RedirectStandardOutput || psi.RedirectStandardError)
            {
                psi.RedirectStandardOutput = false;
                psi.RedirectStandardError = false;
                psi.StandardOutputEncoding = null;
                psi.StandardErrorEncoding = null;

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Elevation requested: switching to UseShellExecute=true (output will not be captured).");
            }

            psi.UseShellExecute = true;
            psi.CreateNoWindow = false;
        }

        using var process = new System.Diagnostics.Process();
        process.StartInfo = psi;
        process.EnableRaisingEvents = true;

        ConcurrentQueue<string>? lines = null;
        TaskCompletionSource? stdoutDone = null;
        TaskCompletionSource? stderrDone = null;
        CaptureState? state = null;

        bool canCapture = !psi.UseShellExecute && (psi.RedirectStandardOutput || psi.RedirectStandardError);

        if (canCapture)
        {
            lines = new ConcurrentQueue<string>();

            stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            state = new CaptureState(_logger, log, lines, stdoutDone, stderrDone);
            s_states.Add(process, state);

            if (psi.RedirectStandardOutput)
                process.OutputDataReceived += StdoutHandler;
            if (psi.RedirectStandardError)
                process.ErrorDataReceived += StderrHandler;
        }

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start process '{fileName}'.");

            if (!psi.UseShellExecute)
            {
                if (psi.RedirectStandardOutput)
                    process.BeginOutputReadLine();
                if (psi.RedirectStandardError)
                    process.BeginErrorReadLine();
            }

            if (waitForExit)
            {
                Task allDone;

                if (psi.UseShellExecute)
                {
                    allDone = process.WaitForExitAsync(cancellationToken);
                }
                else
                {
                    Task so = psi.RedirectStandardOutput ? stdoutDone!.Task : Task.CompletedTask;
                    Task se = psi.RedirectStandardError ? stderrDone!.Task : Task.CompletedTask;
                    allDone = Task.WhenAll(process.WaitForExitAsync(cancellationToken), so, se);
                }

                // .NET 10: avoid Task.Delay + WhenAny
                if (timeout.HasValue)
                    await allDone.WaitAsync(timeout.Value, cancellationToken)
                                 .NoSync();
                else
                    await allDone.NoSync();

                if (process.ExitCode != 0)
                {
                    string tail = lines is null ? string.Empty : GetTail(lines, 40);
                    throw new InvalidOperationException(
                        $"Process '{fileName}' exited with code {process.ExitCode}.{(tail.Length > 0 ? Environment.NewLine + tail : string.Empty)}");
                }
            }

            if (lines is null)
                return [];

            // single materialization, no “return [..outputLines]” copy
            var list = new List<string>(Math.Min(256, lines.Count));
            while (lines.TryDequeue(out string? s))
                list.Add(s);

            return list;
        }
        catch (TimeoutException)
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
        catch (OperationCanceledException)
        {
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
        finally
        {
            if (state is not null)
            {
                if (psi.RedirectStandardOutput)
                {
                    process.OutputDataReceived -= StdoutHandler;
                    try
                    {
                        process.CancelOutputRead();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                if (psi.RedirectStandardError)
                {
                    process.ErrorDataReceived -= StderrHandler;
                    try
                    {
                        process.CancelErrorRead();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                s_states.Remove(process);
            }
        }

        static string GetTail(ConcurrentQueue<string> q, int max)
        {
            if (q.IsEmpty)
                return string.Empty;

            // error path only; ToArray alloc is acceptable here
            string[] arr = q.ToArray();
            int start = Math.Max(arr.Length - max, 0);
            return string.Join(Environment.NewLine, arr.AsSpan(start));
        }
    }
}