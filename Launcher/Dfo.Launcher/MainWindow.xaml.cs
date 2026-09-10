using System.Windows;
using System.Windows.Input;
using Dfo.Launcher.ViewModels;

namespace Dfo.Launcher;

public partial class MainWindow : Window
{
    private readonly LauncherViewModel viewModel;

    public MainWindow(LauncherViewModel viewModel)
    {
        this.InitializeComponent();
        this.viewModel = viewModel;
        this.DataContext = viewModel;
    }

    public void RestoreRememberedPassword()
    {
        if (this.IsLoaded && this.viewModel.RememberCredentials)
        {
            this.PasswordInput.Password = this.viewModel.RememberedPassword;
        }
    }

    private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            this.DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        this.WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => this.Close();

    private void OpenRegistration_Click(object sender, RoutedEventArgs e)
    {
        if (!this.viewModel.RegistrationEnabled || !this.viewModel.CanSubmit)
        {
            return;
        }

        var registration = new RegistrationWindow(this.viewModel) { Owner = this };
        _ = registration.ShowDialog();
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        this.viewModel.ShowLogin();
        var password = this.PasswordInput.Password;
        try
        {
            await this.viewModel.SubmitAsync(password);
        }
        finally
        {
            password = string.Empty;
            this.PasswordInput.Clear();
        }

        if (this.viewModel.IsLaunched)
        {
            this.Close();
        }
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        this.PasswordInput.Clear();
        await this.viewModel.RetryAsync();
        this.RestoreRememberedPassword();
    }

    protected override void OnClosed(EventArgs e)
    {
        this.viewModel.Dispose();
        base.OnClosed(e);
    }
}
