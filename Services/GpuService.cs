using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiBoxDashboard.Models;

namespace AiBoxDashboard.Services;

/// <summary>Reads the first NVIDIA GPU through <c>nvidia-smi</c>.</summary>
public sealed class GpuService(CommandRunner runner)
{
    private static readonly string[] Args =
    [
        "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw",
        "--format=csv,noheader,nounits",
    ];

    public async Task<GpuSample> ReadAsync(CancellationToken ct)
    {
        var output = await runner.RunAsync("nvidia-smi", Args, TimeSpan.FromSeconds(8), ct);
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                   ?? throw new InvalidOperationException("nvidia-smi returned no GPU.");
        return Parse(line);
    }

    public static GpuSample Parse(string csvLine)
    {
        var parts = csvLine.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 6)
            throw new FormatException($"Unexpected nvidia-smi output: {csvLine}");

        return new GpuSample(
            parts[0],
            Number(parts[1]) ?? 0,
            Number(parts[2]) ?? 0,
            Number(parts[3]) ?? 0,
            Number(parts[4]) ?? 0,
            Number(parts[5]));
    }

    // nvidia-smi prints "[N/A]" for sensors a GPU does not expose.
    private static double? Number(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
