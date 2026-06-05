using System;
using System.Collections.Generic;

namespace Soenneker.Utils.Process.Dtos;

/// <summary>
/// Represents the process start dto.
/// </summary>
public sealed class ProcessStartDto
{
    /// <summary>
    /// Gets or sets file name.
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// Gets or sets arguments.
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// Gets or sets working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether create no window.
    /// </summary>
    public bool CreateNoWindow { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether redirect standard output.
    /// </summary>
    public bool RedirectStandardOutput { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether redirect standard error.
    /// </summary>
    public bool RedirectStandardError { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether log.
    /// </summary>
    public bool Log { get; set; } = true;

    /// <summary>
    /// Gets or sets environment variables.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Gets or sets output callback.
    /// </summary>
    public Action<string>? OutputCallback { get; set; }

    /// <summary>
    /// Gets or sets error callback.
    /// </summary>
    public Action<string>? ErrorCallback { get; set; }
}