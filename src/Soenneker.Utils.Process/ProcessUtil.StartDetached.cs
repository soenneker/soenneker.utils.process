using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Utils.Process.Dtos;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process;

public sealed partial class ProcessUtil
{
    private static readonly ConditionalWeakTable<System.Diagnostics.Process, DetachedProcessState> s_detachedStates = new();

    private static void DetachedStdoutHandler(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
            return;

        if (sender is not System.Diagnostics.Process process || !s_detachedStates.TryGetValue(process, out DetachedProcessState? state))
            return;

        state.OutputCallback?.Invoke(e.Data);

        if (state.Log && state.Logger.IsEnabled(LogLevel.Information))
            state.Logger.LogInformation("{Data}", e.Data);
    }

    private static void DetachedStderrHandler(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
            return;

        if (sender is not System.Diagnostics.Process process || !s_detachedStates.TryGetValue(process, out DetachedProcessState? state))
            return;

        state.ErrorCallback?.Invoke(e.Data);

        if (state.Log && state.Logger.IsEnabled(LogLevel.Error))
            state.Logger.LogError("{Data}", e.Data);
    }

    public ValueTask<System.Diagnostics.Process?> StartDetached(ProcessStartDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentException.ThrowIfNullOrWhiteSpace(dto.FileName);

        var psi = new ProcessStartInfo
        {
            FileName = dto.FileName,
            Arguments = dto.Arguments ?? string.Empty,
            WorkingDirectory = dto.WorkingDirectory.HasContent() ? dto.WorkingDirectory! : Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = dto.CreateNoWindow,
            RedirectStandardOutput = dto.RedirectStandardOutput || dto.OutputCallback is not null,
            RedirectStandardError = dto.RedirectStandardError || dto.ErrorCallback is not null
        };

        if (psi.RedirectStandardOutput)
            psi.StandardOutputEncoding = System.Text.Encoding.UTF8;

        if (psi.RedirectStandardError)
            psi.StandardErrorEncoding = System.Text.Encoding.UTF8;

        if (dto.EnvironmentVariables is { Count: > 0 })
        {
            foreach (KeyValuePair<string, string> kvp in dto.EnvironmentVariables)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }
        }

        var process = new System.Diagnostics.Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        bool trackState = psi.RedirectStandardOutput || psi.RedirectStandardError || cancellationToken.CanBeCanceled;

        if (trackState)
        {
            var state = new DetachedProcessState(_logger, dto.Log, dto.OutputCallback, dto.ErrorCallback);
            s_detachedStates.Add(process, state);

            if (psi.RedirectStandardOutput)
                process.OutputDataReceived += DetachedStdoutHandler;

            if (psi.RedirectStandardError)
                process.ErrorDataReceived += DetachedStderrHandler;
        }

        try
        {
            if (!process.Start())
            {
                if (dto.Log && _logger.IsEnabled(LogLevel.Error))
                    _logger.LogError("Failed to start process '{FileName}' with arguments '{Arguments}'", dto.FileName, psi.Arguments);

                CleanupDetached(process, psi);
                process.Dispose();
                return ValueTask.FromResult<System.Diagnostics.Process?>(null);
            }

            if (dto.Log && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Started process '{FileName}' with arguments '{Arguments}' (PID {Pid})", dto.FileName, psi.Arguments, process.Id);
            }

            if (psi.RedirectStandardOutput)
                process.BeginOutputReadLine();

            if (psi.RedirectStandardError)
                process.BeginErrorReadLine();

            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.Register(static state =>
                {
                    var proc = (System.Diagnostics.Process)state!;

                    try
                    {
                        if (!proc.HasExited)
                            proc.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                }, process);

                process.Exited += (_, _) =>
                {
                    registration.Dispose();
                    CleanupDetached(process, psi);
                };
            }
            else
            {
                process.Exited += (_, _) => CleanupDetached(process, psi);
            }

            return ValueTask.FromResult<System.Diagnostics.Process?>(process);
        }
        catch (Exception ex)
        {
            CleanupDetached(process, psi);
            process.Dispose();

            if (dto.Log && _logger.IsEnabled(LogLevel.Error))
                _logger.LogError(ex, "Error starting process '{FileName}' with arguments '{Arguments}'", dto.FileName, psi.Arguments);

            return ValueTask.FromResult<System.Diagnostics.Process?>(null);
        }
    }

    private static void CleanupDetached(System.Diagnostics.Process process, ProcessStartInfo psi)
    {
        if (psi.RedirectStandardOutput)
        {
            process.OutputDataReceived -= DetachedStdoutHandler;

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
            process.ErrorDataReceived -= DetachedStderrHandler;

            try
            {
                process.CancelErrorRead();
            }
            catch (InvalidOperationException)
            {
            }
        }

        s_detachedStates.Remove(process);
    }
}