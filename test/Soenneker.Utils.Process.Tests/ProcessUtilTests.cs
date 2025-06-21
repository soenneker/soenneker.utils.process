using Soenneker.Utils.Process.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Runtime.InteropServices;

namespace Soenneker.Utils.Process.Tests;

[Collection("Collection")]
public sealed class ProcessUtilTests : FixturedUnitTest
{
    private readonly IProcessUtil _util;

    public ProcessUtilTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IProcessUtil>(true);
    }

    [Fact]
    public async Task Start_ProcessCompletesSuccessfully_ReturnsOutput()
    {
        // Arrange
        string command = GetEchoCommand();
        string arguments = GetEchoArguments("Hello, World!");

        // Act
        List<string> output = await _util.Start(name: command, arguments: arguments, waitForExit: true, log: false, cancellationToken: CancellationToken);

        // Assert
        Assert.Contains("Hello, World!", output);
    }

    [Fact]
    public async Task Start_ProcessDoesNotWaitForExit_ReturnsImmediately()
    {
        // Arrange
        string command = GetSleepCommand();
        string arguments = GetSleepArguments(5); // Sleep for 5 seconds

        // Act
        List<string> output = await _util.Start(name: command, arguments: arguments, waitForExit: false, log: false, cancellationToken: CancellationToken);

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

    [Fact]
    public async Task Start_ProcessIsCanceledBeforeCompletion_ThrowsOperationCanceledException()
    {
        // Arrange
        string command = GetSleepCommand();
        string arguments = GetSleepArguments(10); // Sleep for 10 seconds
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(2000); // Cancel after 1 second

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _util.Start(name: command, arguments: arguments, waitForExit: true, log: false, cancellationToken: cts.Token);
        });

        Assert.IsType<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task Start_ProcessWithArguments_ReturnsExpectedOutput()
    {
        // Arrange
        string command = GetEchoCommand();
        string arguments = GetEchoArguments("Test Argument");

        // Act
        List<string> output = await _util.Start(name: command, arguments: arguments, waitForExit: true, log: false, cancellationToken: CancellationToken);

        // Assert
        Assert.Contains("Test Argument", output);
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