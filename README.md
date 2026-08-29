[![](https://img.shields.io/nuget/v/Soenneker.Utils.Process.svg?style=for-the-badge)](https://www.nuget.org/packages/Soenneker.Utils.Process/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.process/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.utils.process/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/Soenneker.Utils.Process.svg?style=for-the-badge)](https://www.nuget.org/packages/Soenneker.Utils.Process/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.process/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.utils.process/actions/workflows/codeql.yml)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Utils.Process
A utility library implementing useful process operations.

## Installation

```bash
dotnet add package Soenneker.Utils.Process
```

## Quick start

```csharp
using Soenneker.Utils.Process.Registrars;

services.AddProcessUtilAsSingleton();
```

## Common operations

- `CommandExists()` - Checks whether an executable can be resolved without launching it.
- `CommandExistsAndRuns()` - Resolves the executable and runs its version command within an optional timeout.
- `StartAndGetOutput()` - Starts a process, waits for it, and returns captured output; timeout and cancellation can stop the wait.
- `StartIfNotRunning()` - Starts only when no process with the name exists, returning captured output lines.
- `KillByNames()` - Kills processes matching any supplied name, optionally waiting for exit.
- `Kill()` - Kills processes with the exact requested name, optionally waiting for exit.
- `KillThatStartWith()` - Kills processes whose names begin with the prefix, optionally waiting for exit.
- `IsRunning()` - Returns `true` when a process with the exact name is running.
- `BashRun()` - Runs the command through Bash in the requested working directory and waits for completion.
- `CmdRun()` - Runs the command through Windows `cmd.exe` in the requested working directory and waits for completion.
- `Start()` - Starts a process, waits for completion, and returns captured output as lines.
- `StartDetached()` - Starts without waiting and returns the `Process` when available.

The package also includes one additional operation for more specialized cases.
