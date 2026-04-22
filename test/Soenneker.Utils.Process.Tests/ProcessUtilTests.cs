using Soenneker.Utils.Process.Abstract;
using Soenneker.Utils.Process.Dtos;
using Soenneker.Tests.HostedUnit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Runtime.InteropServices;

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
    public async Task Start_ProcessCompletesSuccessfully_ReturnsOutput()
    {
        // Arrange
        string command = GetEchoCommand();
        string arguments = GetEchoArguments("Hello, World!");

        // Act
        List<string> output = await _util.Start(fileName: command, arguments: arguments, waitForExit: true, log: false, cancellationToken: CancellationToken);

        // Assert
        Assert.Contains("Hello, World!", output);
    }

    [Test]
    public async Task Start_ProcessDoesNotWaitForExit_ReturnsImmediately()
    {
        // Arrange
        string command = GetSleepCommand();
        string arguments = GetSleepArguments(5); // Sleep for 5 seconds

        // Act
        List<string> output = await _util.Start(fileName: command, arguments: arguments, waitForExit: false, log: false, cancellationToken: CancellationToken);

        // Assert
        // Since we are not waiting for exit, output should be empty
        Assert.Empty(output);
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
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Test]
    public async Task Start_ProcessWithArguments_ReturnsExpectedOutput()
    {
        // Arrange
        string command = GetEchoCommand();
        string arguments = GetEchoArguments("Test Argument");

        // Act
        List<string> output = await _util.Start(fileName: command, arguments: arguments, waitForExit: true, log: false, cancellationToken: CancellationToken);

        // Assert
        Assert.Contains("Test Argument", output);
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
            await process.WaitForExitAsync(CancellationToken.None);
            Assert.True(process.HasExited);
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