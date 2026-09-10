using System.IO;
using System.Windows;
using Dfo.Launcher.Api;
using Dfo.Launcher.Configuration;
using Dfo.Launcher.Launching;
using Dfo.Launcher.Preferences;
using Dfo.Launcher.ViewModels;

namespace Dfo.Launcher;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var directory = AppContext.BaseDirectory;
        var viewModel = new LauncherViewModel(
            new LauncherConfigurationFileSource(Path.Combine(directory, "Launcher.ini")),
            new LauncherApiClientFactory(),
            new JsonLauncherPreferencesStore(JsonLauncherPreferencesStore.DefaultPath),
            new GameLauncher(
                directory,
                TimeProvider.System,
                new SystemGameProcessStarter(),
                NullLauncherEventLog.Instance),
            NullLauncherEventLog.Instance);
        var window = new MainWindow(viewModel);
        this.MainWindow = window;
        window.Show();

        var preview = e.Args.SingleOrDefault(argument =>
            argument.StartsWith("--visual-state=", StringComparison.OrdinalIgnoreCase));
        if (preview is not null)
        {
            viewModel.ApplyVisualPreview(preview[(preview.IndexOf('=') + 1)..]);
            return;
        }

        await viewModel.InitializeAsync();
        window.RestoreRememberedPassword();
    }
}
