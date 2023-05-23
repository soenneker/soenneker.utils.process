﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Enumerable;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Extensions.String;

namespace Soenneker.Utils.Process;

/// <inheritdoc cref="IProcessUtil"/>
public class ProcessUtil : IProcessUtil
{
    private readonly ILogger<ProcessUtil> _logger;

    public ProcessUtil(ILogger<ProcessUtil> logger)
    {
        _logger = logger;
    }

    public async ValueTask<List<string>> StartProcess(string name, string directory, string? arguments = null, bool admin = false, bool waitForExit = false)
    {
        // TODO: Add log flag

        _logger.LogInformation("Starting process {name} in {directory} with arguments: {arguments} (admin? {admin}) (wait? {waitForExit}) ...", name, directory, arguments, admin, waitForExit);

        var processOutput = new List<string>();
        
        string fullPath = Path.Combine(directory, name);

        System.Diagnostics.Process process = new()
        {
            StartInfo =
            {
                UseShellExecute = false,
                FileName = fullPath,
                WorkingDirectory = directory,
                Arguments = arguments,
                // CreateNoWindow = true, // We don't need new window
                RedirectStandardOutput = true, // Any output, generated by application will be redirected back
                RedirectStandardError = true // Any error in standard output will be redirected back (for example exceptions)
            }
        };

        process.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e) { OutputHandler(e, processOutput, true); };
        process.OutputDataReceived += delegate(object _, DataReceivedEventArgs e) { OutputHandler(e, processOutput, true); };

        if (admin)
            process.StartInfo.Verb = "runas";

        if (arguments != null)
            process.StartInfo.Arguments = arguments;

        // process.StartInfo.LoadUserProfile = true;
        process.Start();

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        if (waitForExit)
        {
            _logger.LogDebug("Waiting for process ({process}) to end...", name);
            await process.WaitForExitAsync();
        }

        _logger.LogDebug("Process ({process}) has ended", name);

        return processOutput;
    }

    public ValueTask<List<string>> StartIfNotRunning(string name, string directory, string? arguments = null, bool admin = false, bool waitForExit = false)
    {
        if (IsProcessRunning(name))
            return ValueTask.FromResult(new List<string>());

        return StartProcess(name, directory, arguments, admin, waitForExit);
    }

    public void KillProcesses(IEnumerable<string> processNames)
    {
        foreach (string names in processNames)
        {
            KillProcessesByName(names);
        }
    }

    private void OutputHandler(DataReceivedEventArgs outLine, List<string> processOutput, bool log = false)
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

        bool isRunning = System.Diagnostics.Process.GetProcessesByName(name).Any();

        if (isRunning)
            _logger.LogInformation("{process} is running", name);
        else
            _logger.LogInformation("{process} is NOT running", name);

        return isRunning;
    }
}