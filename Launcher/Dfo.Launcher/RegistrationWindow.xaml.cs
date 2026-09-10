using System.Windows;
using System.Windows.Input;
using Dfo.Launcher.ViewModels;

namespace Dfo.Launcher;

public partial class RegistrationWindow : Window
{
    private readonly LauncherViewModel viewModel;

    public RegistrationWindow(LauncherViewModel viewModel)
    {
        this.InitializeComponent();
        this.viewModel = viewModel;
        this.DataContext = viewModel;
        this.RegistrationLoginIdBox.Text = viewModel.LoginId;
    }

    private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            this.DragMove();
        }
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        var loginId = this.RegistrationLoginIdBox.Text;
        var password = this.RegistrationPasswordInput.Password;
        var confirmation = this.RegistrationConfirmPasswordInput.Password;
        if (string.IsNullOrWhiteSpace(loginId)
            || string.IsNullOrEmpty(password)
            || !string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            this.RegistrationErrorText.Text = string.IsNullOrWhiteSpace(loginId)
                || string.IsNullOrEmpty(password)
                ? "请填写账号、密码和确认密码。"
                : "两次输入的密码不一致。";
            return;
        }

        var previousLoginId = this.viewModel.LoginId;
        this.viewModel.LoginId = loginId;
        this.viewModel.ShowRegister();
        try
        {
            await this.viewModel.SubmitAsync(password);
            if (this.viewModel.IsLoginMode && this.viewModel.State == LauncherState.Ready)
            {
                this.DialogResult = true;
                return;
            }

            this.viewModel.LoginId = previousLoginId;
            this.RegistrationErrorText.Text = this.viewModel.FieldErrorText
                ?? this.viewModel.GlobalError
                ?? "注册失败，请稍后重试。";
        }
        finally
        {
            password = string.Empty;
            confirmation = string.Empty;
            this.RegistrationPasswordInput.Clear();
            this.RegistrationConfirmPasswordInput.Clear();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => this.Close();

    protected override void OnClosed(EventArgs e)
    {
        if (this.viewModel.IsRegisterMode)
        {
            this.viewModel.ShowLogin();
        }

        base.OnClosed(e);
    }
}
