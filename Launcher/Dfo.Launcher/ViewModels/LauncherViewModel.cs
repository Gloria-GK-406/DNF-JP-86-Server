using System.ComponentModel;
using System.Runtime.CompilerServices;
using Dfo.Launcher.Api;
using Dfo.Launcher.Configuration;
using Dfo.Launcher.Launching;
using Dfo.Launcher.Preferences;

namespace Dfo.Launcher.ViewModels;

public enum LauncherState
{
    ConfigError,
    Checking,
    Ready,
    Busy,
    Error,
    Launched,
}

public enum LauncherMode
{
    Login,
    Register,
}

public sealed class LauncherViewModel(
    ILauncherConfigurationSource configuration,
    ILauncherApiClientFactory clients,
    ILauncherPreferencesStore preferences,
    IGameLauncher gameLauncher,
    ILauncherEventLog log) : INotifyPropertyChanged, IDisposable
{
    private ILauncherApiClient? apiClient;
    private LauncherState state = LauncherState.Checking;
    private LauncherMode mode = LauncherMode.Login;
    private string loginId = string.Empty;
    private bool rememberLoginId;
    private bool registrationEnabled;
    private string statusMessage = "正在检查服务器…";
    private string? globalError;
    private string? fieldErrorText;
    private int activeOperation;
    private bool preferencesLoaded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LauncherState State
    {
        get => this.state;
        private set
        {
            if (this.Set(ref this.state, value))
            {
                this.RaiseStateProperties();
            }
        }
    }

    public LauncherMode Mode
    {
        get => this.mode;
        private set
        {
            if (this.Set(ref this.mode, value))
            {
                this.OnPropertyChanged(nameof(this.PrimaryActionText));
                this.OnPropertyChanged(nameof(this.IsLoginMode));
                this.OnPropertyChanged(nameof(this.IsRegisterMode));
            }
        }
    }

    public string LoginId
    {
        get => this.loginId;
        set => this.Set(ref this.loginId, value ?? string.Empty);
    }

    public bool RememberLoginId
    {
        get => this.rememberLoginId;
        set => this.Set(ref this.rememberLoginId, value);
    }

    public bool RegistrationEnabled
    {
        get => this.registrationEnabled;
        private set => this.Set(ref this.registrationEnabled, value);
    }

    public string StatusMessage
    {
        get => this.statusMessage;
        private set => this.Set(ref this.statusMessage, value);
    }

    public string? GlobalError
    {
        get => this.globalError;
        private set
        {
            if (this.Set(ref this.globalError, value))
            {
                this.OnPropertyChanged(nameof(this.HasError));
            }
        }
    }

    public string? FieldErrorText
    {
        get => this.fieldErrorText;
        private set => this.Set(ref this.fieldErrorText, value);
    }

    public bool IsLoginMode => this.Mode == LauncherMode.Login;

    public bool IsRegisterMode => this.Mode == LauncherMode.Register;

    public bool IsLoading => this.State is LauncherState.Checking or LauncherState.Busy;

    public bool HasError => this.State is LauncherState.ConfigError or LauncherState.Error;

    public bool IsLaunched => this.State == LauncherState.Launched;

    public bool IsFormVisible => this.State == LauncherState.Ready;

    public bool CanRetry => this.State is LauncherState.ConfigError or LauncherState.Error;

    public bool CanSubmit =>
        this.State == LauncherState.Ready && Volatile.Read(ref this.activeOperation) == 0;

    public string PrimaryActionText => this.IsLoginMode ? "登录并启动" : "申请账号";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!this.preferencesLoaded)
        {
            var saved = await preferences.LoadAsync(cancellationToken);
            this.preferencesLoaded = true;
            if (saved.Remember)
            {
                this.LoginId = saved.LoginId ?? string.Empty;
                this.RememberLoginId = true;
            }
        }

        await this.CheckServiceAsync(cancellationToken);
    }

    public Task RetryAsync(CancellationToken cancellationToken = default) =>
        this.CheckServiceAsync(cancellationToken);

    public void ShowLogin()
    {
        if (!this.IsLoading)
        {
            this.Mode = LauncherMode.Login;
            this.ClearErrors();
        }
    }

    public void ShowRegister()
    {
        if (!this.IsLoading && this.RegistrationEnabled)
        {
            this.Mode = LauncherMode.Register;
            this.ClearErrors();
        }
    }

    public async Task SubmitAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (this.State != LauncherState.Ready
            || this.apiClient is null
            || !this.TryBeginOperation())
        {
            return;
        }

        try
        {
            this.ClearErrors();
            this.State = LauncherState.Busy;
            this.StatusMessage = this.IsLoginMode ? "正在登录…" : "正在申请账号…";
            if (this.IsRegisterMode)
            {
                var registration = await this.apiClient.RegisterAsync(
                    this.LoginId,
                    password,
                    cancellationToken);
                if (!registration.Succeeded)
                {
                    this.ApplyCredentialError(registration);
                    return;
                }

                log.Write("registration_succeeded");
                this.Mode = LauncherMode.Login;
                this.State = LauncherState.Ready;
                this.StatusMessage = "账号申请成功，请登录。";
                return;
            }

            var login = await this.apiClient.LoginAsync(
                this.LoginId,
                password,
                cancellationToken);
            if (!login.Succeeded)
            {
                this.ApplyCredentialError(login);
                return;
            }

            await preferences.SaveAsync(
                new LauncherPreferences(
                    this.RememberLoginId ? this.LoginId : null,
                    this.RememberLoginId),
                cancellationToken);
            var launched = gameLauncher.Launch(login.Value!);
            if (!launched.Succeeded)
            {
                this.State = LauncherState.Error;
                this.GlobalError = launched.Message;
                this.StatusMessage = "启动失败";
                return;
            }

            log.Write("login_and_launch_succeeded");
            this.State = LauncherState.Launched;
            this.StatusMessage = "游戏已启动，可以关闭启动器。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this.State = LauncherState.Error;
            this.GlobalError = "操作已取消，可以重试。";
            this.StatusMessage = "操作已取消";
        }
        catch
        {
            log.Write("launcher_operation_failed");
            this.State = LauncherState.Error;
            this.GlobalError = "操作失败，请重试。";
            this.StatusMessage = "操作失败";
        }
        finally
        {
            password = string.Empty;
            this.EndOperation();
        }
    }

    internal void ApplyVisualPreview(string preview)
    {
        this.RegistrationEnabled = true;
        this.GlobalError = null;
        this.FieldErrorText = null;
        switch (preview.ToLowerInvariant())
        {
            case "login":
                this.Mode = LauncherMode.Login;
                this.State = LauncherState.Ready;
                this.StatusMessage = "服务器已就绪";
                break;
            case "register":
                this.Mode = LauncherMode.Register;
                this.State = LauncherState.Ready;
                this.StatusMessage = "创建一个新的游戏账号";
                break;
            case "loading":
                this.State = LauncherState.Checking;
                this.StatusMessage = "正在检查服务器…";
                break;
            case "error":
                this.State = LauncherState.Error;
                this.GlobalError = "无法连接服务器，请检查网络后重试。";
                this.StatusMessage = "连接失败";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(preview));
        }
    }

    public void Dispose() => this.apiClient?.Dispose();

    private async Task CheckServiceAsync(CancellationToken cancellationToken)
    {
        if (!this.TryBeginOperation())
        {
            return;
        }

        try
        {
            this.ClearErrors();
            this.State = LauncherState.Checking;
            this.StatusMessage = "正在检查服务器…";
            LauncherConfiguration current;
            try
            {
                current = configuration.Read();
            }
            catch (LauncherConfigurationException exception)
            {
                this.State = LauncherState.ConfigError;
                this.GlobalError = exception.Message;
                this.StatusMessage = "配置需要修复";
                log.Write("launcher_configuration_invalid");
                return;
            }

            this.apiClient?.Dispose();
            this.apiClient = clients.Create(current.BaseUrl);
            var bootstrap = await this.apiClient.GetBootstrapAsync(cancellationToken);
            if (!bootstrap.Succeeded)
            {
                this.ApplyServiceError(bootstrap);
                return;
            }

            if (!string.Equals(bootstrap.Value!.ApiVersion, "1", StringComparison.Ordinal))
            {
                this.State = LauncherState.Error;
                this.GlobalError = "服务器版本与此启动器不兼容。";
                this.StatusMessage = "版本不兼容";
                return;
            }

            if (!string.Equals(bootstrap.Value.Status, "ready", StringComparison.Ordinal))
            {
                this.State = LauncherState.Error;
                this.GlobalError = "服务暂不可用，请稍后重试。";
                this.StatusMessage = "服务暂不可用";
                return;
            }

            this.RegistrationEnabled = bootstrap.Value.RegistrationEnabled;
            this.State = LauncherState.Ready;
            this.StatusMessage = "服务器已就绪";
            log.Write("bootstrap_ready");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this.State = LauncherState.Error;
            this.GlobalError = "服务检查已取消，可以重试。";
            this.StatusMessage = "检查已取消";
        }
        catch
        {
            log.Write("bootstrap_failed");
            this.State = LauncherState.Error;
            this.GlobalError = "无法检查服务器，请重试。";
            this.StatusMessage = "检查失败";
        }
        finally
        {
            this.EndOperation();
        }
    }

    private void ApplyCredentialError<T>(LauncherApiResult<T> result)
    {
        if (result.FieldErrors is { Count: > 0 })
        {
            this.GlobalError = null;
            this.FieldErrorText = string.Join(
                "\n",
                result.FieldErrors.OrderBy(pair => pair.Key).Select(pair => pair.Value));
            this.State = LauncherState.Ready;
            this.StatusMessage = result.Message ?? "请修正输入后重试。";
        }
        else
        {
            this.State = LauncherState.Error;
            this.GlobalError = result.Message ?? "操作失败，请重试。";
            this.FieldErrorText = null;
            this.StatusMessage = "操作失败";
        }

        this.LogApiOperationRejected();
    }

    private void ApplyServiceError<T>(LauncherApiResult<T> result)
    {
        this.State = LauncherState.Error;
        this.GlobalError = result.Message ?? "无法检查服务器，请重试。";
        this.FieldErrorText = null;
        this.StatusMessage = "检查失败";
        this.LogApiOperationRejected();
    }

    private void LogApiOperationRejected() => log.Write("api_operation_rejected");

    private void ClearErrors()
    {
        this.GlobalError = null;
        this.FieldErrorText = null;
    }

    private bool TryBeginOperation()
    {
        var acquired = Interlocked.CompareExchange(ref this.activeOperation, 1, 0) == 0;
        if (acquired)
        {
            this.OnPropertyChanged(nameof(this.CanSubmit));
        }

        return acquired;
    }

    private void EndOperation()
    {
        Volatile.Write(ref this.activeOperation, 0);
        this.OnPropertyChanged(nameof(this.CanSubmit));
    }

    private void RaiseStateProperties()
    {
        this.OnPropertyChanged(nameof(this.IsLoading));
        this.OnPropertyChanged(nameof(this.HasError));
        this.OnPropertyChanged(nameof(this.IsLaunched));
        this.OnPropertyChanged(nameof(this.IsFormVisible));
        this.OnPropertyChanged(nameof(this.CanRetry));
        this.OnPropertyChanged(nameof(this.CanSubmit));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        this.OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
