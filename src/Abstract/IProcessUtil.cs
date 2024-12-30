using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process.Abstract;

/// <summary>
/// A utility library implementing useful process operations <para/>
/// Typically is Scoped IoC (unless being consumed by a Singleton)
/// </summary>
public interface IProcessUtil
{
    ValueTask<List<string>> Start(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = false, bool log = true, CancellationToken cancellationToken = default);

    ValueTask<List<string>> StartIfNotRunning(string name, string? directory = null, string? arguments = null, bool admin = false, bool waitForExit = false, bool log = true, CancellationToken cancellationToken = default);

    void KillByNames(IEnumerable<string> processNames);

    void Kill(string name);

    void KillThatStartWith(string startsWith);

    void Kill(System.Diagnostics.Process process);

    [Pure]
    bool IsRunning(string name);
}