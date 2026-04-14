using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Soenneker.Utils.Process.Dtos;

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