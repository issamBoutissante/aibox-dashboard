using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AiBoxDashboard.Models;

/// <summary>One reading of the first NVIDIA GPU.</summary>
public sealed record GpuSample(
    string Name,
    double TemperatureC,
    double UtilizationPercent,
    double MemoryUsedMiB,
    double MemoryTotalMiB,
    double? PowerW);

/// <summary>One reading of the machine itself. Sizes are in kibibytes, as Linux reports them.</summary>
public sealed record SystemSample(
    double? CpuPercent,
    double? CpuTemperatureC,
    int CpuThreads,
    long MemoryTotalKb,
    long MemoryUsedKb,
    IReadOnlyList<DiskUsage> Disks);

/// <summary>Space on one mounted disk. Capacity counts only what ordinary users can fill, as <c>df</c> does.</summary>
public sealed record DiskUsage(string Device, string Mount, long UsedKb, long CapacityKb);

public sealed record ContainerInfo(string Name, string Image, string State, string Status);

public sealed class OllamaVersion
{
    public string Version { get; set; } = "";
}

public sealed class OllamaTagList
{
    public List<OllamaModel> Models { get; set; } = [];
}

public sealed class OllamaModel
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public OllamaModelDetails? Details { get; set; }
}

public sealed class OllamaModelDetails
{
    public string? ParameterSize { get; set; }
    public string? QuantizationLevel { get; set; }
}

public sealed class OllamaRunningList
{
    public List<OllamaRunningModel> Models { get; set; } = [];
}

public sealed class OllamaRunningModel
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public long SizeVram { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class OllamaGenerateResult
{
    public long EvalCount { get; set; }

    /// <summary>Generation time in nanoseconds.</summary>
    public long EvalDuration { get; set; }
}

/// <summary>One line of the stream Ollama sends while pulling a model.</summary>
public sealed class OllamaPullLine
{
    public string? Status { get; set; }
    public string? Digest { get; set; }
    public long? Total { get; set; }
    public long? Completed { get; set; }
    public string? Error { get; set; }
}

public sealed class OllamaError
{
    public string? Error { get; set; }
}

/// <summary>Download progress of a pull. <see cref="Fraction"/> is null while the size is unknown.</summary>
public sealed record PullProgress(string Status, double? Fraction);

public sealed class DockerPsLine
{
    [JsonPropertyName("Names")] public string Names { get; set; } = "";
    [JsonPropertyName("Image")] public string Image { get; set; } = "";
    [JsonPropertyName("State")] public string State { get; set; } = "";
    [JsonPropertyName("Status")] public string Status { get; set; } = "";
}
