using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.Task;

namespace Soenneker.Utils.Process;

public sealed partial class ProcessUtil
{
    public async IAsyncEnumerable<string> StreamLines(string fileName, string? workingDirectory = null, string? arguments = null,
        bool redirectOutput = true, bool redirectError = true, IDictionary<string, string>? environmentVariables = null, ILogger? logger = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        using CancellationTokenRegistration killRegistration = cancellationToken.Register(static p =>
            TryKillProcessTree((System.Diagnostics.Process)p!), process);

        try
        {
            await foreach (ProcessOutputLine line in ReadProcessLines(process, redirectOutput, redirectError, cancellationToken).ConfigureAwait(false))
            {
                string payload = line.StandardError ? string.Concat("[stderr] ", line.Content) : line.Content;
                if (logger is not null)
                {
                    if (line.StandardError)
                        LogWarningLine(logger, payload);
                    else
                        LogInformationLine(logger, payload);
                }

                yield return payload;
            }

            // Closing the output pipes does not mean the process has exited.
            await process.WaitForExitAsync(cancellationToken).NoSync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Process \"{fileName}\" exited with code {process.ExitCode}.");
        }
        finally
        {
            // Also runs when the consumer stops enumerating before the process exits.
            TryKillProcessTree(process);
        }
    }

    private static IAsyncEnumerable<ProcessOutputLine> ReadProcessLines(System.Diagnostics.Process process, bool redirectOutput,
        bool redirectError, CancellationToken cancellationToken)
    {
        // The .NET multiplexing API requires both streams to be redirected.
        return redirectOutput && redirectError
            ? process.ReadAllLinesAsync(cancellationToken)
            : ReadSingleProcessStream(process, redirectOutput, redirectError, cancellationToken);
    }

    private static async IAsyncEnumerable<ProcessOutputLine> ReadSingleProcessStream(System.Diagnostics.Process process, bool redirectOutput,
        bool redirectError, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!redirectOutput && !redirectError)
            yield break;

        StreamReader reader = redirectOutput ? process.StandardOutput : process.StandardError;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            yield return new ProcessOutputLine(line, standardError: redirectError);
    }
}
