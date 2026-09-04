using Microsoft.Extensions.Logging;

namespace Soenneker.Utils.Process;

public sealed partial class ProcessUtil
{
    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{Data}")]
    private static partial void LogInformationData(ILogger logger, string data);

    [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "{Data}")]
    private static partial void LogErrorData(ILogger logger, string data);

    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "{Line}")]
    private static partial void LogInformationLine(ILogger logger, string line);

    [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "{Line}")]
    private static partial void LogWarningLine(ILogger logger, string line);

    [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "{Line}")]
    private static partial void LogErrorLine(ILogger logger, string line);

    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "[stdout] {Line}")]
    private static partial void LogStandardOutput(ILogger logger, string line);

    [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "[stderr] {Line}")]
    private static partial void LogStandardError(ILogger logger, string line);
}
