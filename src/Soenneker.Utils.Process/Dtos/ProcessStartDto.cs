using System;
using System.Collections.Generic;

namespace Soenneker.Utils.Process.Dtos;

public sealed class ProcessStartDto
{
    public required string FileName { get; set; }

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    public bool CreateNoWindow { get; set; } = true;

    public bool RedirectStandardOutput { get; set; }

    public bool RedirectStandardError { get; set; }

    public bool Log { get; set; } = true;

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; set; }

    public Action<string>? OutputCallback { get; set; }

    public Action<string>? ErrorCallback { get; set; }
}