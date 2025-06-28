using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.Utils.Process;

public sealed partial class ProcessUtil
{
    /// <summary>
    /// Runs <paramref name="fileName"/> and produces every line it writes.  When both stdout *and* stderr are redirected
    /// they are merged in‑order of arrival so the caller sees the exact chronological sequence.
    /// </summary>
    public async IAsyncEnumerable<string> StreamLines(string fileName, string? workingDirectory = null, string? arguments = null,
        bool redirectOutput = true, bool redirectError = true, IDictionary<string, string>? environmentVariables = null, ILogger? logger = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectError,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        if (environmentVariables is {Count: > 0})
        {
            foreach ((string key, string value) in environmentVariables)
                psi.Environment[key] = value;
        }

        using var process = new System.Diagnostics.Process();
        process.StartInfo = psi;
        process.EnableRaisingEvents = true;

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process \"{fileName}\".");

        // Kill the whole tree if the token is canceled or the enumerator is disposed early.
        await using CancellationTokenRegistration killRegistration = cancellationToken.Register(static p =>
        {
            if (p is System.Diagnostics.Process {HasExited: false} proc)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    /* ignored */
                }
            }
        }, process);

        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        Task stdoutTask = Task.CompletedTask, stderrTask = Task.CompletedTask;

        if (redirectOutput)
            stdoutTask = PumpAsync(process.StandardOutput, channel.Writer, logger, isError: false, cancellationToken);

        if (redirectError)
            stderrTask = PumpAsync(process.StandardError, channel.Writer, logger, isError: true, cancellationToken);

        // Close the writer once *both* pumps finish.
        Task closeWriterTask = Task.WhenAll(stdoutTask, stderrTask)
                                   .ContinueWith(_ => channel.Writer.TryComplete(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                                       TaskScheduler.Default);

        // Forward lines downstream.
        while (await channel.Reader.WaitToReadAsync(cancellationToken).NoSync())
        {
            while (channel.Reader.TryRead(out string? line))
                yield return line;
        }

        await process.WaitForExitAsync(cancellationToken).NoSync();
        await Task.WhenAll(stdoutTask, stderrTask, closeWriterTask).NoSync();

        if (process.ExitCode != 0 && !cancellationToken.IsCancellationRequested)
            throw new InvalidOperationException($"Process \"{fileName}\" exited with code {process.ExitCode}.");
    }

    /// <summary>
    /// Continuously reads <paramref name="reader"/> and forwards each line to <paramref name="writer"/>.
    /// </summary>
    private static async Task PumpAsync(StreamReader reader, ChannelWriter<string> writer, ILogger? logger, bool isError, CancellationToken cancellationToken)
    {
        LogLevel logLevel = isError ? LogLevel.Warning : LogLevel.Information;
        string prefix = isError ? "[stderr] " : string.Empty;

        try
        {
            while (true)
            {
                // ReadLineAsync with token is .NET 8; earlier versions can use WaitAsync.
                string? line = await reader.ReadLineAsync(cancellationToken).NoSync();
                if (line is null)
                    break;

                string payload = prefix + line;
                logger?.Log(logLevel, "{Line}", payload);
                await writer.WriteAsync(payload, cancellationToken).NoSync();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is handled by the outer method.
        }
        finally
        {
            writer.TryComplete();
        }
    }
}