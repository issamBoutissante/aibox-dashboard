using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiBoxDashboard.ViewModels;

/// <summary>One installed Ollama model.</summary>
public sealed partial class ModelRow(string name, string details, Func<ModelRow, Task> measureSpeed) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>Size on disk, parameter count and quantization.</summary>
    public string Details { get; } = details;

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private string _loadedText = "";
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private bool _isMeasuring;

    [RelayCommand]
    private Task MeasureSpeed() => measureSpeed(this);
}
