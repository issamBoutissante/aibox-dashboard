using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiBoxDashboard.Models;

namespace AiBoxDashboard.Services;

/// <summary>Lists containers through the <c>docker</c> CLI.</summary>
public sealed class DockerService(CommandRunner runner)
{
    private static readonly string[] Args = ["ps", "--all", "--format", "{{json .}}"];

    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(CancellationToken ct)
    {
        var output = await runner.RunAsync("docker", Args, TimeSpan.FromSeconds(8), ct);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonSerializer.Deserialize<DockerPsLine>(line))
            .OfType<DockerPsLine>()
            .Select(c => new ContainerInfo(c.Names, c.Image, c.State, c.Status))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
