using System;
using System.IO;
using System.Text.Json;

namespace AiBoxDashboard.Services;

/// <summary>Where the dashboard reads from. Saved per user between runs.</summary>
public sealed class AppSettings
{
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    /// <summary>SSH host for GPU and container readings. Empty means this machine.</summary>
    public string SshHost { get; set; } = "";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiBoxDashboard",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken settings file should not stop the app from starting.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; the dashboard still works without them.
        }
    }

    /// <summary>Applies <c>--ollama URL</c> and <c>--ssh HOST</c> for this run only.</summary>
    public AppSettings WithCommandLine(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--ollama") OllamaUrl = args[i + 1];
            if (args[i] == "--ssh") SshHost = args[i + 1];
        }

        return this;
    }
}
