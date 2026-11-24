using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiBoxDashboard.ViewModels;

/// <summary>What a model row can ask the dashboard to do.</summary>
public interface IModelActions
{
    Task MeasureSpeedAsync(ModelRow row);
    Task ToggleLoadAsync(ModelRow row);
    Task DeleteAsync(ModelRow row);
}

/// <summary>One installed Ollama model.</summary>
public sealed partial class ModelRow(string name, string details, IModelActions actions) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>Size on disk, parameter count and quantization.</summary>
    public string Details { get; } = details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadButtonText))]
    private bool _isLoaded;

    [ObservableProperty] private string _loadedText = "";

    /// <summary>Result or failure of the last action on this model.</summary>
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isConfirmingDelete;

    public string LoadButtonText => IsLoaded ? "Unload" : "Load";

    [RelayCommand]
    private Task MeasureSpeed() => actions.MeasureSpeedAsync(this);

    [RelayCommand]
    private Task ToggleLoad() => actions.ToggleLoadAsync(this);

    [RelayCommand]
    private void AskDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private Task ConfirmDelete()
    {
        IsConfirmingDelete = false;
        return actions.DeleteAsync(this);
    }
}
