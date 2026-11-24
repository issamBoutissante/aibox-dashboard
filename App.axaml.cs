using AiBoxDashboard.Services;
using AiBoxDashboard.ViewModels;
using AiBoxDashboard.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AiBoxDashboard;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load().WithCommandLine(desktop.Args ?? []);
            var viewModel = new MainViewModel(settings);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += (_, _) => viewModel.Dispose();
            viewModel.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
