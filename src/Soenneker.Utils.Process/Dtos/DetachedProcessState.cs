using Microsoft.Extensions.Logging;
using System;

namespace Soenneker.Utils.Process.Dtos;

internal sealed class DetachedProcessState
{
    public ILogger Logger { get; }
    public bool Log { get; }
    public Action<string>? OutputCallback { get; }
    public Action<string>? ErrorCallback { get; }

    public DetachedProcessState(ILogger logger, bool log, Action<string>? outputCallback, Action<string>? errorCallback)
    {
        Logger = logger;
        Log = log;
        OutputCallback = outputCallback;
        ErrorCallback = errorCallback;
    }
}