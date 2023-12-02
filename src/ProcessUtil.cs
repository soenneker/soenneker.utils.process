using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Enumerable;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;

namespace Soenneker.Utils.Process;

/// <inheritdoc cref="IProcessUtil"/>
public class ProcessUtil : IProcessUtil
{
    private readonly ILogger<ProcessUtil> _logger;

    public ProcessUtil(ILogger<ProcessUtil> logger)
    {
        _logger = logger;
    }

    public async ValueTask<List<string>> StartProcess(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = false, bool log = true)
    {
        if (log)
            _logger.LogInformation("Starting process ({name}) in directory ({directory}) with arguments ({arguments}) (admin? {admin}) (wait? {waitForExit}) ...", name, directory, arguments, admin, waitForExit);

        var processOutput = new List<string>();

        string fullPath = directory != null ? Path.Combine(directory, name) : name;

        var startInfo = new ProcessStartInfo(fullPath)
        {
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (arguments != null)
            startInfo.Arguments = arguments;

        if (directory != null)
            startInfo.WorkingDirectory = directory;

        if (admin)
            startInfo.Verb = "runas";

        var process = new System.Diagnostics.Process {StartInfo = startInfo};

        process.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e) { OutputHandler(e, processOutput, log); };
        process.OutputDataReceived += delegate(object _, DataReceivedEventArgs e) { OutputHandler(e, processOutput, log); };

        process.Start();

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        if (waitForExit)
        {
            if (log)
                _logger.LogDebug("Waiting for process ({process}) to end...", name);

            await process.WaitForExitAsync().NoSync();
        }

        if (log)
            _logger.LogDebug("Process ({process}) has ended", name);

        return processOutput;
    }

    public ValueTask<List<string>> StartIfNotRunning(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = false, bool log = true)
    {
        if (IsProcessRunning(name))
            return ValueTask.FromResult(new List<string>());

        return StartProcess(name, directory, arguments, admin, waitForExit, log);
    }

    public void KillProcesses(IEnumerable<string> processNames)
    {
        foreach (string names in processNames)
        {
            KillProcessesByName(names);
        }
    }

    private void OutputHandler(DataReceivedEventArgs outLine, List<string> processOutput, bool log = true)
    {
        if (outLine.Data.IsNullOrEmpty())
            return;

        processOutput.Add(outLine.Data);

        if (log)
            _logger.LogDebug("{output}", outLine.Data);
    }

    public void KillProcessesByName(string name)
    {
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(name);

        if (processes.Empty())
        {
            _logger.LogDebug("No processes running by name {name}", name);
            return;
        }

        _logger.LogDebug("Killing {num} processes...", processes.Length);

        foreach (System.Diagnostics.Process process in processes)
        {
            KillProcess(process);
            break;
        }
    }

    public void KillProcessesThatStartsWith(string startsWith)
    {
        System.Diagnostics.Process[] totalProcesses = System.Diagnostics.Process.GetProcesses();

        List<System.Diagnostics.Process> processesToKill = totalProcesses.Where(process => process.ProcessName.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase)).ToList();

        if (processesToKill.Empty())
        {
            _logger.LogDebug("No processes start with {startsWith}", startsWith);
            return;
        }

        _logger.LogDebug("Killing {num} processes...", processesToKill.Count);

        foreach (System.Diagnostics.Process process in processesToKill)
        {
            KillProcess(process);
        }
    }

    public void KillProcess(System.Diagnostics.Process process)
    {
        _logger.LogInformation("Killing process {processName} (id {id}) ...", process.ProcessName, process.Id);
        process.Kill(false);
    }

    public bool IsProcessRunning(string name)
    {
        _logger.LogInformation("Checking if {process} is running...", name);

        bool isRunning = System.Diagnostics.Process.GetProcessesByName(name).Length > 0;

        if (isRunning)
            _logger.LogInformation("{process} is running", name);
        else
            _logger.LogInformation("{process} is NOT running", name);

        return isRunning;
    }
}