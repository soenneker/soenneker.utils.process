using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Soenneker.Tests.HostedUnit;
using Soenneker.Utils.Process.Abstract;

namespace Soenneker.Utils.Process.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class ProcessOutputTests : HostedUnitTest
{
    private readonly IProcessUtil _util;

    public ProcessOutputTests(Host host) : base(host)
    {
        _util = Resolve<IProcessUtil>(true);
    }

    [Test]
    public async Task Capture_DrainsBothPipes_AndReportsFailure(CancellationToken cancellationToken)
    {
        (string command, string arguments) = Shell(
            "for /L %i in (1,1,3000) do @(echo output&echo error>&2)",
            "i=0; while [ $i -lt 3000 ]; do echo output; echo error >&2; i=$((i+1)); done");
        string output = await _util.StartAndGetOutput(command, arguments, timeout: TimeSpan.FromSeconds(15), cancellationToken: cancellationToken);
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(3000);
        output.Should().NotContain("error");

        (command, arguments) = Shell("echo failure>&2&exit /b 7", "echo failure >&2; exit 7");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _util.StartAndGetOutput(command, arguments, cancellationToken: cancellationToken).AsTask());
        error!.Message.Should().Contain("7").And.Contain("failure");
    }

    [Test]
    public async Task Capture_CancellationPropagatesCallerToken()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        (string command, string arguments) = Shell("ping -n 30 127.0.0.1 >nul", "sleep 30");
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _util.StartAndGetOutput(command, arguments, cancellationToken: cancellation.Token).AsTask());
        error!.CancellationToken.Should().Be(cancellation.Token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Capture_PreservesBomDetection(bool utf16, CancellationToken cancellationToken)
    {
        string command;
        string arguments;
        if (OperatingSystem.IsWindows())
        {
            command = "powershell.exe";
            string bytes = utf16 ? "255,254,104,0,105,0" : "239,187,191,104,105";
            arguments = $"-NoProfile -Command \"[Console]::OpenStandardOutput().Write([byte[]]({bytes}),0,{(utf16 ? 6 : 5)})\"";
        }
        else
        {
            (command, arguments) = Shell(string.Empty, utf16 ? "printf '\\377\\376h\\000i\\000'" : "printf '\\357\\273\\277hi'");
        }

        string output = await _util.StartAndGetOutput(command, arguments, cancellationToken: cancellationToken);
        output.Should().Be("hi");
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task Stream_RespectsRedirection(bool stdout, bool stderr, CancellationToken cancellationToken)
    {
        (string command, string arguments) = Shell("echo output&echo error>&2", "echo output; echo error >&2");
        var lines = new List<string>();
        await foreach (string line in _util.StreamLines(command, arguments: arguments, redirectOutput: stdout, redirectError: stderr,
                           cancellationToken: cancellationToken))
            lines.Add(line);

        lines.Contains("output").Should().Be(stdout);
        lines.Contains("[stderr] error").Should().Be(stderr);
        lines.Should().HaveCount((stdout ? 1 : 0) + (stderr ? 1 : 0));
    }

    [Test]
    public async Task Stream_EarlyDisposalKillsChild(CancellationToken cancellationToken)
    {
        string command = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh";
        string arguments = OperatingSystem.IsWindows()
            ? "-NoProfile -Command \"[Console]::WriteLine($PID); Start-Sleep -Seconds 30\""
            : "-c \"echo $$; sleep 30\"";
        await using IAsyncEnumerator<string> enumerator = _util.StreamLines(command, arguments: arguments,
            cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        using var child = System.Diagnostics.Process.GetProcessById(int.Parse(enumerator.Current));

        await enumerator.DisposeAsync();

        await child.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        child.HasExited.Should().BeTrue();
    }

    [Test]
    public async Task Stream_WithoutRedirection_WaitsForSuccessfulExit(CancellationToken cancellationToken)
    {
        (string command, string arguments) = Shell("ping -n 2 127.0.0.1 >nul&exit /b 0", "sleep 1; exit 0");
        var lines = new List<string>();
        await foreach (string line in _util.StreamLines(command, arguments: arguments, redirectOutput: false, redirectError: false,
                           cancellationToken: cancellationToken))
            lines.Add(line);
        lines.Should().BeEmpty();
    }

    [Test]
    public async Task Stream_CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        (string command, string arguments) = Shell("ping -n 30 127.0.0.1 >nul", "sleep 30");
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (string _ in _util.StreamLines(command, arguments: arguments, cancellationToken: cancellation.Token))
            {
            }
        });
    }

    [Test]
    public async Task Stream_NonzeroExitIsReported(CancellationToken cancellationToken)
    {
        (string command, string arguments) = Shell("echo failure>&2&exit /b 7", "echo failure >&2; exit 7");
        var lines = new List<string>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (string line in _util.StreamLines(command, arguments: arguments, cancellationToken: cancellationToken))
                lines.Add(line);
        });
        lines.Should().Contain("[stderr] failure");
        error!.Message.Should().Contain("7");
    }

    [Test]
    public async Task ShellRun_DrainsBothStreams_AndReportsNonzeroExit(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await _util.CmdRun("echo output&echo error>&2", Environment.CurrentDirectory, cancellationToken: cancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _util.CmdRun("exit /b 7", Environment.CurrentDirectory, cancellationToken: cancellationToken).AsTask());
        }
        else
        {
            await _util.BashRun("echo output; echo error >&2", Environment.CurrentDirectory, cancellationToken: cancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _util.BashRun("exit 7", Environment.CurrentDirectory, cancellationToken: cancellationToken).AsTask());
        }
    }

    [Test]
    public async Task ShellRun_CancellationStopsWaiting()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<OperationCanceledException>(() => OperatingSystem.IsWindows()
            ? _util.CmdRun("ping -n 30 127.0.0.1 >nul", Environment.CurrentDirectory, cancellationToken: cancellation.Token).AsTask()
            : _util.BashRun("sleep 30", Environment.CurrentDirectory, cancellationToken: cancellation.Token).AsTask());
    }

    private static (string Command, string Arguments) Shell(string windows, string unix) => OperatingSystem.IsWindows()
        ? ("cmd.exe", $"/d /c \"{windows}\"")
        : ("/bin/sh", $"-c \"{unix}\"");
}
