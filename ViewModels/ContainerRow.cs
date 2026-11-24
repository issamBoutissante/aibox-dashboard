namespace AiBoxDashboard.ViewModels;

public sealed record ContainerRow(string Name, string Image, string Status, bool IsRunning);

/// <summary>One disk in the Disks tile. <see cref="Percent"/> is 0–100.</summary>
public sealed record DiskRow(string Mount, string UsageText, double Percent);
