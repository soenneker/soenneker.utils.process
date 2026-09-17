using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Soenneker.Utils.Process.Dtos;

namespace Soenneker.Utils.Process.Tests;

public sealed class ProcessUtilDetachedTests
{
    [Test]
    public async Task Cancellation_kills_the_child_and_completes_an_uncancelled_exit_wait(CancellationToken cancellationToken)
    {
        var util = new ProcessUtil(NullLogger<ProcessUtil>.Instance);
        using var cancellation = new CancellationTokenSource();
        using System.Diagnostics.Process? process = await util.StartDetached(new ProcessStartDto
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows()
                ? "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\""
                : "-c \"sleep 30\"",
            Log = false,
            OutputCallback = _ => { },
            ErrorCallback = _ => { }
        }, cancellation.Token);

        process.Should().NotBeNull();
        await cancellation.CancelAsync();
        await process!.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        process.HasExited.Should().BeTrue();
    }

    [Test]
    public async Task WaitForExit_drains_output_even_when_callbacks_are_slower_than_the_child(CancellationToken cancellationToken)
    {
        var stdout = new ConcurrentQueue<string>();
        var stderr = new ConcurrentQueue<string>();
        var util = new ProcessUtil(NullLogger<ProcessUtil>.Instance);
        using System.Diagnostics.Process? process = await util.StartDetached(new ProcessStartDto
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows()
                ? "/d /c \"for /L %i in (1,1,200) do @echo line-%i\""
                : "-c \"i=1; while [ $i -le 200 ]; do echo line-$i; i=$((i+1)); done\"",
            Log = false,
            OutputCallback = line =>
            {
                Thread.Sleep(1);
                stdout.Enqueue(line);
            },
            ErrorCallback = stderr.Enqueue
        }, cancellationToken);

        process.Should().NotBeNull();
        await process!.WaitForExitAsync(cancellationToken);

        process.ExitCode.Should().Be(0);
        stdout.Should().HaveCount(200);
        stdout.Should().Contain("line-200");
        stderr.Should().BeEmpty();
    }

    [Test]
    public async Task Immediate_exit_preserves_both_streams_and_nonzero_exit_code(CancellationToken cancellationToken)
    {
        var util = new ProcessUtil(NullLogger<ProcessUtil>.Instance);
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var stdout = new ConcurrentQueue<string>();
            var stderr = new ConcurrentQueue<string>();
            using System.Diagnostics.Process? process = await util.StartDetached(new ProcessStartDto
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                Arguments = OperatingSystem.IsWindows()
                    ? "/d /c \"echo output&echo error>&2&exit /b 2\""
                    : "-c \"echo output; echo error >&2; exit 2\"",
                Log = false,
                OutputCallback = stdout.Enqueue,
                ErrorCallback = stderr.Enqueue
            }, cancellationToken);

            process.Should().NotBeNull();
            await process!.WaitForExitAsync(cancellationToken);
            process.ExitCode.Should().Be(2);
            stdout.Should().Contain("output");
            stderr.Should().Contain("error");
        }
    }
}
