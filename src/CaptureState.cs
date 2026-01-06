using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Soenneker.Utils.Process;

internal sealed class CaptureState
{
    public readonly ILogger Logger;
    public readonly bool Log;
    public readonly ConcurrentQueue<string> Lines;
    public readonly TaskCompletionSource StdoutDone;
    public readonly TaskCompletionSource StderrDone;

    public CaptureState(ILogger logger, bool log, ConcurrentQueue<string> lines, TaskCompletionSource stdoutDone, TaskCompletionSource stderrDone)
    {
        Logger = logger;
        Log = log;
        Lines = lines;
        StdoutDone = stdoutDone;
        StderrDone = stderrDone;
    }
}