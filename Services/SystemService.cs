using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiBoxDashboard.Models;

namespace AiBoxDashboard.Services;

/// <summary>
/// Reads CPU, memory and disk figures from a Linux machine through one small shell script.
/// CPU load is the share of busy time between two readings, so the first reading has none.
/// </summary>
public sealed class SystemService(CommandRunner runner)
{
    // Each section starts with a "#name" line so the parser does not depend on line counts.
    private const string Script = """
        echo '#stat'; head -n 1 /proc/stat 2>/dev/null
        echo '#threads'; grep -c '^processor' /proc/cpuinfo 2>/dev/null
        echo '#mem'; grep -E '^(MemTotal|MemAvailable):' /proc/meminfo 2>/dev/null
        echo '#temp'
        for d in /sys/class/hwmon/hwmon*; do
          case "$(cat "$d/name" 2>/dev/null)" in
            coretemp|k10temp|zenpower) cat "$d/temp1_input" 2>/dev/null; break;;
          esac
        done
        echo '#df'; df -kP 2>/dev/null
        """;

    private (long Busy, long Total)? _previousCpu;

    public async Task<SystemSample> ReadAsync(CancellationToken ct)
    {
        var output = await runner.RunAsync("sh", ["-c", Script], TimeSpan.FromSeconds(8), ct);
        var reading = Parse(output);
        if (reading.Cpu is null && reading.MemoryTotalKb == 0)
            throw new InvalidOperationException("CPU, RAM and disk readings need a Linux machine.");

        double? cpuPercent = null;
        if (reading.Cpu is { } now && _previousCpu is { } before && now.Total > before.Total)
            cpuPercent = Math.Clamp(100.0 * (now.Busy - before.Busy) / (now.Total - before.Total), 0, 100);
        _previousCpu = reading.Cpu;

        return new SystemSample(
            cpuPercent,
            reading.CpuTemperatureC,
            reading.Threads,
            reading.MemoryTotalKb,
            Math.Max(reading.MemoryTotalKb - reading.MemoryAvailableKb, 0),
            reading.Disks);
    }

    public sealed record RawReading(
        (long Busy, long Total)? Cpu,
        int Threads,
        long MemoryTotalKb,
        long MemoryAvailableKb,
        double? CpuTemperatureC,
        IReadOnlyList<DiskUsage> Disks);

    public static RawReading Parse(string output)
    {
        (long Busy, long Total)? cpu = null;
        var threads = 0;
        long memoryTotal = 0, memoryAvailable = 0;
        double? temperature = null;
        var disks = new List<DiskUsage>();
        var section = "";

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('#'))
            {
                section = line;
                continue;
            }

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (section)
            {
                case "#stat" when fields.Length >= 9 && fields[0] == "cpu":
                    // user nice system idle iowait irq softirq steal
                    var times = fields.Skip(1).Take(8).Select(Long).ToArray();
                    var idle = times[3] + times[4];
                    cpu = (times.Sum() - idle, times.Sum());
                    break;
                case "#threads":
                    threads = (int)Long(fields[0]);
                    break;
                case "#mem" when fields.Length >= 2 && fields[0] == "MemTotal:":
                    memoryTotal = Long(fields[1]);
                    break;
                case "#mem" when fields.Length >= 2 && fields[0] == "MemAvailable:":
                    memoryAvailable = Long(fields[1]);
                    break;
                case "#temp" when Long(fields[0]) > 0:
                    temperature = Long(fields[0]) / 1000.0; // millidegrees
                    break;
                case "#df" when fields.Length >= 6 && IsRealDisk(fields[0], fields[5]):
                    if (disks.All(d => d.Device != fields[0]))
                        disks.Add(new DiskUsage(fields[0], string.Join(' ', fields.Skip(5)), Long(fields[2]), Long(fields[2]) + Long(fields[3])));
                    break;
            }
        }

        return new RawReading(cpu, threads, memoryTotal, memoryAvailable, temperature,
            disks.OrderBy(d => d.Mount, StringComparer.Ordinal).ToList());
    }

    // Leaves out memory-backed filesystems, snap images and the small boot partitions.
    private static bool IsRealDisk(string device, string mount)
        => device.StartsWith("/dev/", StringComparison.Ordinal)
           && !device.StartsWith("/dev/loop", StringComparison.Ordinal)
           && !mount.StartsWith("/boot", StringComparison.Ordinal)
           && !mount.StartsWith("/snap", StringComparison.Ordinal);

    private static long Long(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
