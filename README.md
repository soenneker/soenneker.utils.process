[![](https://img.shields.io/nuget/v/Soenneker.Utils.Process.svg?style=for-the-badge)](https://www.nuget.org/packages/Soenneker.Utils.Process/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.process/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.utils.process/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/Soenneker.Utils.Process.svg?style=for-the-badge)](https://www.nuget.org/packages/Soenneker.Utils.Process/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.process/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.utils.process/actions/workflows/codeql.yml)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Utils.Process
Starts, captures, streams, probes, and terminates operating-system processes, with explicit shell-command helpers when shell syntax is required.

## Installation

```bash
dotnet add package Soenneker.Utils.Process
```

## Quick start

```csharp
using Soenneker.Utils.Process.Registrars;

services.AddProcessUtilAsSingleton();
```

## Start and capture a process

```csharp
List<string> lines = await processUtil.Start(
    fileName: "dotnet",
    arguments: "--info",
    workingDirectory: repositoryPath,
    waitForExit: true,
    timeout: TimeSpan.FromSeconds(30),
    cancellationToken: cancellationToken);

string stdout = await processUtil.StartAndGetOutput(
    "git",
    "status --short",
    repositoryPath,
    TimeSpan.FromSeconds(10),
    cancellationToken);

// Drains output without retaining it when only success or failure matters.
await processUtil.StartAndWait(
    "git",
    repositoryPath,
    "fetch --prune",
    timeout: TimeSpan.FromSeconds(30),
    cancellationToken: cancellationToken);
```

These methods launch the executable directly with `UseShellExecute = false`; pipes, redirection,
globs, and shell operators are not interpreted. `Start` returns stdout lines and prefixes stderr
lines with `ERROR: `. A nonzero exit, start failure, or capture failure throws
`InvalidOperationException`. `StartAndGetOutput` returns stdout as one string and includes stderr
only in its nonzero-exit exception. `StartAndWait` drains both streams without creating output
strings or a result collection, making it preferable for commands whose output is not consumed.

Timeout and requested cancellation kill the process tree on a best-effort basis. `Start` only
captures output when `waitForExit: true`; with `false` it returns an empty list after launching and
does not retain a process handle. Use `StartDetached` when the caller needs to manage a running
child.

## Stream lines

```csharp
await foreach (string line in processUtil.StreamLines(
    "dotnet",
    arguments: "test",
    workingDirectory: repositoryPath,
    cancellationToken: cancellationToken))
{
    Handle(line);
}
```

Stdout is yielded unchanged and stderr is prefixed with `[stderr] `. When both are enabled, their
lines are merged in the order observed by separate asynchronous readers, which is not a guaranteed
byte-level chronology. Cancellation or stopping enumeration early kills the process tree.

## Detached processes

```csharp
Process? process = await processUtil.StartDetached(new ProcessStartDto
{
    FileName = "worker",
    Arguments = "--listen",
    RedirectStandardOutput = true,
    OutputCallback = line => Handle(line)
}, cancellationToken);

if (process is not null)
{
    // Keep the handle while needed, then dispose it after the process exits.
}
```

`StartDetached` returns `null` when startup fails and logs the failure when enabled. The caller owns
the returned `Process` handle. Canceling the supplied token kills the process tree; it does not
dispose the returned handle. Callbacks run on process output event threads and must be thread-safe;
callback exceptions are caught and logged.

## Shell commands

`BashRun` executes a complete command through `/bin/bash -lc`; `CmdRun` executes through
`cmd.exe /c`. Both log output, throw on nonzero exit, and kill the process tree on requested
cancellation. Because the command string is interpreted by a shell, do not concatenate untrusted
input into it. Use `Start` with separately controlled executable and arguments when shell syntax is
not required.

## Probes and termination

- `CommandExists` checks PATH resolution without launching the target; `CommandExistsAndRuns` also runs its version command and requires exit code zero. Operational failures return `false`, while requested cancellation propagates. Treat the command name as trusted input because Unix resolution uses Bash.
- `IsRunning` is a point-in-time exact process-name check. `StartIfNotRunning` is a check-then-start convenience, not an atomic cross-process lock.
- `Kill(name)` terminates the first exact-name match. `KillThatStartWith(prefix)` attempts every prefix match. `KillByNames` applies the exact-name operation to each supplied name.
- Kill operations are destructive and name matching can include unrelated processes. Prefer retaining the `Process` returned for a child you started. Access-denied and already-exited failures are treated as best effort by the low-level kill method.

Environment-variable dictionaries are added to the inherited child environment. Avoid logging
command lines or values containing credentials; process arguments and environment variables can
also be visible to other software on the host.
