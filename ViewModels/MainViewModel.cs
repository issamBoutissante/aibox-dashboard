using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AiBoxDashboard.Models;
using AiBoxDashboard.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiBoxDashboard.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>Readings kept for the charts: 120 readings at 2 s is four minutes.</summary>
    public const int HistoryLength = 120;

    private static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SlowPoll = TimeSpan.FromMilliseconds(500);

    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<double> _temperature = [];
    private readonly List<double> _utilization = [];
    private readonly List<double> _memory = [];
    private readonly List<double> _power = [];

    private OllamaService? _ollama;
    private GpuService? _gpu;
    private DockerService? _docker;
    private IReadOnlyList<OllamaRunningModel> _running = [];
    private int _connection;
    private bool _slowRefreshDue = true;

    public MainViewModel() : this(new AppSettings())
    {
    }

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _ollamaUrl = settings.OllamaUrl;
        _sshHost = settings.SshHost;
        Connect();
    }

    public ObservableCollection<ModelRow> Models { get; } = [];
    public ObservableCollection<ContainerRow> Containers { get; } = [];

    // Connection
    [ObservableProperty] private string _ollamaUrl;
    [ObservableProperty] private string _sshHost;
    [ObservableProperty] private string _connectionError = "";
    [ObservableProperty] private string _machineText = "";

    // GPU
    [ObservableProperty] private string _gpuName = "GPU";
    [ObservableProperty] private string _gpuMessage = "Reading GPU…";
    [ObservableProperty] private string _temperatureText = "–";
    [ObservableProperty] private string _utilizationText = "–";
    [ObservableProperty] private string _memoryText = "–";
    [ObservableProperty] private string _memoryDetail = "";
    [ObservableProperty] private string _powerText = "–";
    [ObservableProperty] private double _memoryTotal = 1;
    [ObservableProperty] private double _powerMaximum = 150;
    [ObservableProperty] private IReadOnlyList<double> _temperatureHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _utilizationHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _memoryHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _powerHistory = [];

    // Ollama and containers
    [ObservableProperty] private string _ollamaStatus = "";
    [ObservableProperty] private string _modelsMessage = "Asking Ollama…";
    [ObservableProperty] private string _containersMessage = "Asking Docker…";

    public void Start()
    {
        _ = RunFastAsync();
        _ = RunSlowAsync();
    }

    [RelayCommand]
    private void ApplyConnection()
    {
        Connect();
        if (ConnectionError.Length == 0)
        {
            _settings.OllamaUrl = OllamaUrl.Trim();
            _settings.SshHost = SshHost.Trim();
            _settings.Save();
        }
    }

    private void Connect()
    {
        _connection++;
        _ollama?.Dispose();
        _ollama = null;
        _gpu = null;
        _docker = null;
        _running = [];
        _slowRefreshDue = true;

        _temperature.Clear();
        _utilization.Clear();
        _memory.Clear();
        _power.Clear();
        PublishHistory();
        Models.Clear();
        Containers.Clear();
        OllamaStatus = "";
        ModelsMessage = "Asking Ollama…";
        ContainersMessage = "Asking Docker…";
        GpuMessage = "Reading GPU…";
        ConnectionError = "";

        try
        {
            var runner = new CommandRunner(SshHost);
            _gpu = new GpuService(runner);
            _docker = new DockerService(runner);
            _ollama = new OllamaService(OllamaUrl);
            MachineText = runner.IsRemote ? $"GPU and containers on {SshHost.Trim()} over SSH" : "GPU and containers on this machine";
        }
        catch (ArgumentException e)
        {
            ConnectionError = e.Message;
            MachineText = "";
            GpuMessage = "";
            ModelsMessage = "";
            ContainersMessage = "";
        }
    }

    // GPU readings must keep their rhythm, so the slower Ollama and Docker lists run in their own loop.
    private async Task RunFastAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            await Task.WhenAll(RefreshGpuAsync(), RefreshRunningAsync());
            if (!await DelayAsync(FastInterval)) return;
        }
    }

    private async Task RunSlowAsync()
    {
        var sinceRefresh = SlowInterval;
        while (!_shutdown.IsCancellationRequested)
        {
            if (_slowRefreshDue || sinceRefresh >= SlowInterval)
            {
                _slowRefreshDue = false;
                sinceRefresh = TimeSpan.Zero;
                await Task.WhenAll(RefreshModelsAsync(), RefreshContainersAsync());
            }

            if (!await DelayAsync(SlowPoll)) return;
            sinceRefresh += SlowPoll;
        }
    }

    private async Task<bool> DelayAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _shutdown.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RefreshGpuAsync()
    {
        if (_gpu is not { } gpu) return;
        var connection = _connection;
        try
        {
            var sample = await gpu.ReadAsync(_shutdown.Token);
            if (connection != _connection) return;

            GpuName = sample.Name;
            GpuMessage = "";
            TemperatureText = $"{sample.TemperatureC:0} °C";
            UtilizationText = $"{sample.UtilizationPercent:0} %";
            MemoryText = $"{sample.MemoryUsedMiB / 1024:0.0} GB";
            MemoryDetail = $"of {sample.MemoryTotalMiB / 1024:0.0} GB";
            MemoryTotal = Math.Max(sample.MemoryTotalMiB, 1);
            PowerText = sample.PowerW is { } watts ? $"{watts:0} W" : "n/a";

            Append(_temperature, sample.TemperatureC);
            Append(_utilization, sample.UtilizationPercent);
            Append(_memory, sample.MemoryUsedMiB);
            Append(_power, sample.PowerW ?? 0);
            PowerMaximum = Math.Max(150, _power.Max());
            PublishHistory();
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (connection == _connection)
                GpuMessage = $"GPU: {e.Message}";
        }
    }

    private async Task RefreshRunningAsync()
    {
        if (_ollama is not { } ollama) return;
        var connection = _connection;
        try
        {
            var running = await ollama.GetRunningAsync(_shutdown.Token);
            if (connection != _connection) return;
            _running = running;
            ApplyRunning();
        }
        catch (Exception e) when (IsExpected(e))
        {
            // The slower model refresh reports connection problems; avoid flickering two messages.
        }
    }

    private async Task RefreshModelsAsync()
    {
        if (_ollama is not { } ollama) return;
        var connection = _connection;
        try
        {
            var version = await ollama.GetVersionAsync(_shutdown.Token);
            var models = await ollama.GetModelsAsync(_shutdown.Token);
            if (connection != _connection) return;

            OllamaStatus = $"Ollama {version}";
            var existing = Models.ToDictionary(m => m.Name);
            var wanted = models.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var gone in Models.Where(m => wanted.All(w => w.Name != m.Name)).ToList())
                Models.Remove(gone);

            for (var i = 0; i < wanted.Count; i++)
            {
                if (existing.ContainsKey(wanted[i].Name)) continue;
                Models.Insert(Math.Min(i, Models.Count), new ModelRow(wanted[i].Name, Describe(wanted[i]), MeasureSpeedAsync));
            }

            ModelsMessage = Models.Count == 0 ? "No models installed. Pull one with: ollama pull <model>" : "";
            ApplyRunning();
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (connection != _connection) return;
            OllamaStatus = "Ollama not reachable";
            Models.Clear();
            ModelsMessage = $"Could not reach Ollama at {OllamaUrl.Trim()}: {e.Message}";
        }
    }

    private async Task RefreshContainersAsync()
    {
        if (_docker is not { } docker) return;
        var connection = _connection;
        try
        {
            var containers = await docker.ListAsync(_shutdown.Token);
            if (connection != _connection) return;

            var rows = containers
                .Select(c => new ContainerRow(c.Name, c.Image, c.Status, c.State == "running"))
                .ToList();
            if (!rows.SequenceEqual(Containers))
            {
                Containers.Clear();
                foreach (var row in rows)
                    Containers.Add(row);
            }

            ContainersMessage = rows.Count == 0 ? "No containers." : "";
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (connection != _connection) return;
            Containers.Clear();
            ContainersMessage = $"Docker: {e.Message}";
        }
    }

    private async Task MeasureSpeedAsync(ModelRow row)
    {
        if (_ollama is not { } ollama || row.IsMeasuring) return;
        row.IsMeasuring = true;
        row.SpeedText = "measuring…";
        try
        {
            var tokensPerSecond = await ollama.MeasureSpeedAsync(row.Name, _shutdown.Token);
            row.SpeedText = $"{tokensPerSecond:0.0} tokens/s";
        }
        catch (Exception e) when (IsExpected(e))
        {
            row.SpeedText = $"failed: {e.Message}";
        }
        finally
        {
            row.IsMeasuring = false;
        }
    }

    private void ApplyRunning()
    {
        foreach (var row in Models)
        {
            var running = _running.FirstOrDefault(r => r.Name == row.Name);
            row.IsLoaded = running is not null;
            row.LoadedText = running is null ? "not loaded" : DescribeRunning(running);
        }
    }

    private static string Describe(OllamaModel model)
    {
        var parts = new List<string> { $"{model.Size / 1e9:0.0} GB" };
        if (!string.IsNullOrEmpty(model.Details?.ParameterSize)) parts.Add(model.Details.ParameterSize);
        if (!string.IsNullOrEmpty(model.Details?.QuantizationLevel)) parts.Add(model.Details.QuantizationLevel);
        return string.Join(" · ", parts);
    }

    private static string DescribeRunning(OllamaRunningModel running)
    {
        var share = running.Size > 0 ? 100.0 * running.SizeVram / running.Size : 0;
        var text = $"loaded, {running.SizeVram / 1e9:0.0} GB on GPU ({share:0} %)";
        if (running.ExpiresAt is { } expires)
        {
            var minutes = (expires - DateTimeOffset.Now).TotalMinutes;
            if (minutes is > 0 and < 60 * 24)
                text += minutes >= 1 ? $", unloads in {minutes:0} min" : ", unloads in under a minute";
        }

        return text;
    }

    private static void Append(List<double> history, double value)
    {
        history.Add(value);
        if (history.Count > HistoryLength)
            history.RemoveAt(0);
    }

    private void PublishHistory()
    {
        TemperatureHistory = _temperature.ToArray();
        UtilizationHistory = _utilization.ToArray();
        MemoryHistory = _memory.ToArray();
        PowerHistory = _power.ToArray();
    }

    private static bool IsExpected(Exception e)
        => e is InvalidOperationException or TimeoutException or HttpRequestException or FormatException
            or System.Text.Json.JsonException or System.IO.IOException;

    public void Dispose()
    {
        _shutdown.Cancel();
        _ollama?.Dispose();
    }
}
