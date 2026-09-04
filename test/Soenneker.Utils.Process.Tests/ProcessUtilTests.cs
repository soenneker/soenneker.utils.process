using Soenneker.Utils.Process.Abstract;
using Soenneker.Utils.Process.Dtos;
using Soenneker.Tests.HostedUnit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Runtime.InteropServices;
using AwesomeAssertions;

namespace Soenneker.Utils.Process.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class ProcessUtilTests : HostedUnitTest
{
    private readonly IProcessUtil _util;

    public ProcessUtilTests(Host host) : base(host)
    {
        _util = Resolve<IProcessUtil>(true);
    }

    [Test]
    public async Task Start_ProcessCompletesSuccessfully_ReturnsOutput(CancellationToken cancellationToken)
    {
        // Arrange
        string command = GetEchoCommand();
        string arguments = GetEchoArguments("Hello, World!");

        // Act
        List<string> output = await _util.Start(fileName: command, arguments: arguments, waitForExit: true, log: false, cancellationToken: cancellationToken);

        // Assert
        output.Should().Contain("Hello, World!");
    }

    [Test]
    public async Task Start_ProcessDoesNotWaitForExit_ReturnsImmediately(CancellationToken cancellationToken)
    {
        // Arrange
        string command = GetSleepCommand();
        string arguments = GetSleepArguments(5); // Sleep for 5 seconds

        // Act
        List<string> output = await _util.Start(fileName: command, arguments: arguments, waitForExit: false, log: false, cancellationToken: cancellationToken);

        // Assert
        // Since we are not waiting for exit, output should be empty
        output.Should().BeEmpty();
    }

    private string GetSleepCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "powershell";
        }
        else
        {
            return "sleep";
        }
    }


    private string GetSleepArguments(int seconds)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // PowerShell command to sleep for specified seconds
            return $"-Command \"Start-Sleep -Seconds {seconds}\"";
        }
        else
        {
            // Unix-based sleep command
            return $"{seconds}";
        }
    }

    [Test]
    public async Task Start_ProcessIsCanceledBeforeCompletion_ThrowsTaskCanceledException()
    {
        string command = GetSleepCommand();
        string arguments = GetSleepArguments(10);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _util.Start(fileName: command, arguments: arguments, waitForExit: true, log: false, cancellationToken: cts.Token).AsTask());

        // Optional: verify it was YOUR token
        ex!.CancellationToken.Should().Be(cts.Token);
    }

    [Test]
    public async Task Start_ProcessWithArguments_ReturnsExpectedOutput(CancellationToken cancellationToken)
    {
        // Arrange
        string command = GetEchoCommand();
        string arguments = GetEchoArguments("Test Argument");

        // Act
        List<string> output = await _util.Start(fileName: command, arguments: arguments, waitForExit: true, log: false, cancellationToken: cancellationToken);

        // Assert
        output.Should().Contain("Test Argument");
    }

    [Test]
    public async Task Start_CapturesStandardErrorWithoutLosingStandardOutput(CancellationToken cancellationToken)
    {
        string command;
        string arguments;

        if (OperatingSystem.IsWindows())
        {
            command = "cmd.exe";
            arguments = "/d /c \"echo standard&echo failure>&2\"";
        }
        else
        {
            command = "/bin/sh";
            arguments = "-c \"echo standard; echo failure >&2\"";
        }

        List<string> output = await _util.Start(command, arguments: arguments, log: false, cancellationToken: cancellationToken);

        output.Should().Contain("standard");
        output.Should().Contain("ERROR: failure");
    }

    [Test]
    public async Task StartAndWait_DrainsLargeOutput(CancellationToken cancellationToken)
    {
        string command;
        string arguments;

        if (OperatingSystem.IsWindows())
        {
            command = "cmd.exe";
            arguments = "/d /c \"for /L %i in (1,1,2000) do @echo line\"";
        }
        else
        {
            command = "/bin/sh";
            arguments = "-c \"i=0; while [ $i -lt 2000 ]; do echo line; i=$((i+1)); done\"";
        }

        await _util.StartAndWait(command, arguments: arguments, log: false, cancellationToken: cancellationToken);
    }

    [Test]
    public async Task StartAndWait_TimeoutKillsProcess()
    {
        string command = GetSleepCommand();
        string arguments = GetSleepArguments(10);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            _util.StartAndWait(command, arguments: arguments, timeout: TimeSpan.FromMilliseconds(100), log: false).AsTask());
    }

    [Test]
    public async Task StartAndGetOutput_ReturnsStandardOutput(CancellationToken cancellationToken)
    {
        string output = await _util.StartAndGetOutput(GetEchoCommand(), GetEchoArguments("whole output"), cancellationToken: cancellationToken);

        output.Should().Contain("whole output");
    }

    [Test]
    public async Task StartAndGetOutput_TimeoutKillsProcess()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _util.StartAndGetOutput(GetSleepCommand(), GetSleepArguments(10), timeout: TimeSpan.FromMilliseconds(100)).AsTask());
    }

    [Test]
    public async Task StreamLines_ReturnsStandardOutput(CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        await foreach (string line in _util.StreamLines(GetEchoCommand(), arguments: GetEchoArguments("streamed"),
                           cancellationToken: cancellationToken))
        {
            lines.Add(line);
        }

        lines.Should().Contain("streamed");
    }

    [Test]
    public async Task StartDetached_CancellationTokenKillsProcess()
    {
        string command = GetSleepCommand();
        string arguments = GetSleepArguments(10);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        System.Diagnostics.Process? process = await _util.StartDetached(new ProcessStartDto
        {
            FileName = command,
            Arguments = arguments,
            Log = false
        }, cts.Token);

        Assert.NotNull(process);

        using (process)
        {
            await process.WaitForExitAsync(System.Threading.CancellationToken.None);
            process.HasExited.Should().BeTrue();
        }
    }

    private string GetEchoCommand()
    {
        if (OperatingSystem.IsWindows())
            return "cmd.exe";
        else
            return "echo";
    }

    private string GetEchoArguments(string message)
    {
        if (OperatingSystem.IsWindows())
            return $"/c echo {message}";
        else
            return message;
    }
}

