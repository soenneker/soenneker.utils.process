using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process.Abstract;

/// <summary>
/// A utility library implementing useful process operations <para/>
/// Typically is Scoped IoC (unless being consumed by a Singleton)
/// </summary>
public interface IProcessUtil
{
    ValueTask StartProcess(string name, string directory, string? arguments = null, bool admin = false, bool waitForExit = false);

    ValueTask StartIfNotRunning(string name, string directory, string? arguments = null, bool admin = false, bool waitForExit = false);

    void KillProcesses(IEnumerable<string> processNames);

    void KillProcessesByName(string name);

    void KillProcessesThatStartsWith(string startsWith);

    void KillProcess(System.Diagnostics.Process process);

    [Pure]
    bool IsProcessRunning(string name);
}