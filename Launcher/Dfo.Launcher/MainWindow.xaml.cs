using System.Windows;
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

    private void ShowLogin_Click(object sender, RoutedEventArgs e)
    {
        this.PasswordInput.Clear();
        this.viewModel.ShowLogin();
    }

    private void ShowRegister_Click(object sender, RoutedEventArgs e)
    {
        this.PasswordInput.Clear();
        this.viewModel.ShowRegister();
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
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
    }

    protected override void OnClosed(EventArgs e)
    {
        this.viewModel.Dispose();
        base.OnClosed(e);
    }
}
