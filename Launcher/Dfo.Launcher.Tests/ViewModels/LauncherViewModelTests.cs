using System.Net;
using System.Net.Http;
using System.Text;
using Dfo.Launcher.Api;
using Dfo.Launcher.Configuration;
using Dfo.Launcher.Launching;
using Dfo.Launcher.Preferences;
using Dfo.Launcher.ViewModels;

namespace Dfo.Launcher.Tests.ViewModels;

public sealed class LauncherViewModelTests
{
    private static readonly LauncherBootstrap ReadyBootstrap = new("1", "ready", true);
    private static readonly LauncherLogin SuccessfulLogin = new(
        "player",
        "ticket_123",
        DateTimeOffset.UtcNow.AddMinutes(1),
        new("203.0.113.10", 7001));

    [Fact]
    public async Task Invalid_configuration_blocks_HTTP_and_retry_recovers_to_ready()
    {
        var invalid = true;
        var configuration = new DelegateConfigurationSource(() => invalid
            ? throw new LauncherConfigurationException("bad configuration")
            : new LauncherConfiguration(new Uri("https://server.example/")));
        var client = new FakeApiClient { BootstrapResult = Success(ReadyBootstrap) };
        var factory = new FakeApiClientFactory(client);
        using var viewModel = Create(configuration, factory);

        await viewModel.InitializeAsync();
        Assert.Equal(LauncherState.ConfigError, viewModel.State);
        Assert.True(viewModel.CanRetry);
        Assert.Empty(factory.BaseUrls);

        invalid = false;
        await viewModel.RetryAsync();

        Assert.Equal(LauncherState.Ready, viewModel.State);
        Assert.True(viewModel.CanSubmit);
        Assert.Equal([new Uri("https://server.example/")], factory.BaseUrls);
    }

    [Fact]
    public async Task Bootstrap_network_and_version_failures_remain_retryable()
    {
        var networkClient = new FakeApiClient
        {
            BootstrapResult = Failure<LauncherBootstrap>("network_error", "network"),
        };
        var versionClient = new FakeApiClient
        {
            BootstrapResult = Success(new LauncherBootstrap("2", "ready", true)),
        };
        var readyClient = new FakeApiClient { BootstrapResult = Success(ReadyBootstrap) };
        var factory = new FakeApiClientFactory(networkClient, versionClient, readyClient);
        using var viewModel = Create(ValidConfiguration(), factory);

        await viewModel.InitializeAsync();
        Assert.Equal(LauncherState.Error, viewModel.State);
        Assert.True(viewModel.CanRetry);

        await viewModel.RetryAsync();
        Assert.Equal(LauncherState.Error, viewModel.State);
        Assert.Contains("版本", viewModel.GlobalError!, StringComparison.Ordinal);

        await viewModel.RetryAsync();
        Assert.Equal(LauncherState.Ready, viewModel.State);
        Assert.True(viewModel.RegistrationEnabled);
    }

    [Fact]
    public async Task Bootstrap_api_field_errors_never_unlock_credentials_and_retry_requires_success()
    {
        var bootstrapRequests = 0;
        var loginRequests = 0;
        using var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                var response = Interlocked.Increment(ref bootstrapRequests) == 1
                    ? Json(
                        HttpStatusCode.BadRequest,
                        """{"code":"invalid_request","message":"请求字段无效。","fieldErrors":{"request":"入口不支持当前请求。"}}""")
                    : Json(
                        HttpStatusCode.OK,
                        """{"apiVersion":"1","status":"ready","registrationEnabled":true}""");
                return Task.FromResult(response);
            }

            Interlocked.Increment(ref loginRequests);
            return Task.FromResult(Json(
                HttpStatusCode.Unauthorized,
                """{"code":"invalid_credentials","message":"账号或密码错误。"}"""));
        });
        var factory = new DelegateApiClientFactory(baseUrl =>
            new LauncherApiClient(new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = baseUrl,
                Timeout = TimeSpan.FromSeconds(5),
            }));
        using var viewModel = Create(ValidConfiguration(), factory);

        await viewModel.InitializeAsync();
        var failedBootstrapState = viewModel.State;
        var canSubmitAfterFailure = viewModel.CanSubmit;
        var fieldErrorAfterFailure = viewModel.FieldErrorText;
        await viewModel.SubmitAsync("password-secret-sentinel");
        var loginRequestsAfterBlockedSubmit = Volatile.Read(ref loginRequests);
        await viewModel.RetryAsync();

        Assert.Equal(
            (LauncherState.Error, false, null, 0, LauncherState.Ready, true),
            (
                failedBootstrapState,
                canSubmitAfterFailure,
                fieldErrorAfterFailure,
                loginRequestsAfterBlockedSubmit,
                viewModel.State,
                viewModel.CanSubmit));
        Assert.Equal(2, Volatile.Read(ref bootstrapRequests));
    }

    [Fact]
    public async Task Registration_success_returns_to_login_without_launching()
    {
        var client = new FakeApiClient
        {
            BootstrapResult = Success(ReadyBootstrap),
            RegisterResult = new LauncherApiResult<string>("new-player"),
        };
        var launcher = new FakeGameLauncher();
        using var viewModel = Create(ValidConfiguration(), new FakeApiClientFactory(client), launcher: launcher);
        await viewModel.InitializeAsync();
        viewModel.LoginId = "new-player";
        viewModel.ShowRegister();

        await viewModel.SubmitAsync("password-secret");

        Assert.Equal(1, client.RegisterCalls);
        Assert.Equal(0, client.LoginCalls);
        Assert.Equal(LauncherMode.Login, viewModel.Mode);
        Assert.Equal(LauncherState.Ready, viewModel.State);
        Assert.Empty(launcher.Logins);
        Assert.Contains("申请成功", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_success_saves_the_remembered_credentials_then_launches()
    {
        var client = new FakeApiClient
        {
            BootstrapResult = Success(ReadyBootstrap),
            LoginResult = Success(SuccessfulLogin),
        };
        var preferences = new FakePreferencesStore();
        var launcher = new FakeGameLauncher();
        using var viewModel = Create(
            ValidConfiguration(),
            new FakeApiClientFactory(client),
            preferences,
            launcher);
        await viewModel.InitializeAsync();
        viewModel.LoginId = "player";
        viewModel.RememberCredentials = true;

        await viewModel.SubmitAsync("password-secret-sentinel");

        Assert.Equal(LauncherState.Launched, viewModel.State);
        Assert.Equal(
            [new LauncherPreferences("player", "password-secret-sentinel", true)],
            preferences.Saved);
        Assert.Equal([SuccessfulLogin], launcher.Logins);
    }

    [Fact]
    public async Task Initialize_restores_remembered_login_and_password_for_the_login_form()
    {
        var client = new FakeApiClient { BootstrapResult = Success(ReadyBootstrap) };
        var preferences = new FakePreferencesStore
        {
            Loaded = new LauncherPreferences("remembered-player", "remembered-password", true),
        };
        using var viewModel = Create(
            ValidConfiguration(),
            new FakeApiClientFactory(client),
            preferences);

        await viewModel.InitializeAsync();

        Assert.Equal("remembered-player", viewModel.LoginId);
        Assert.Equal("remembered-password", viewModel.RememberedPassword);
        Assert.True(viewModel.RememberCredentials);
    }

    [Fact]
    public async Task Duplicate_submit_is_gated_and_busy_form_never_overlaps_loading()
    {
        var completion = new TaskCompletionSource<LauncherApiResult<LauncherLogin>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeApiClient
        {
            BootstrapResult = Success(ReadyBootstrap),
            Login = (_, _, _) => completion.Task,
        };
        using var viewModel = Create(ValidConfiguration(), new FakeApiClientFactory(client));
        await viewModel.InitializeAsync();

        var first = viewModel.SubmitAsync("password-one");
        await client.LoginEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(LauncherState.Busy, viewModel.State);
        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.IsFormVisible);
        Assert.False(viewModel.CanSubmit);

        await viewModel.SubmitAsync("password-two");
        Assert.Equal(1, client.LoginCalls);
        completion.SetResult(Success(SuccessfulLogin));
        await first;

        Assert.Equal(LauncherState.Launched, viewModel.State);
    }

    [Fact]
    public async Task Api_field_error_keeps_the_form_editable_and_allows_immediate_resubmit()
    {
        const string secret = "password-secret-sentinel";
        var loginCalls = 0;
        var client = new FakeApiClient
        {
            BootstrapResult = Success(ReadyBootstrap),
            Login = (_, _, _) => Task.FromResult(++loginCalls == 1
                ? new LauncherApiResult<LauncherLogin>(
                    null,
                    "invalid_request",
                    "请修复输入。",
                    new Dictionary<string, string> { ["loginId"] = "账号格式错误。" })
                : Success(SuccessfulLogin)),
        };
        var launcher = new FakeGameLauncher();
        var log = new RecordingEventLog();
        using var viewModel = Create(
            ValidConfiguration(),
            new FakeApiClientFactory(client),
            launcher: launcher,
            log: log);
        await viewModel.InitializeAsync();

        await viewModel.SubmitAsync(secret);

        Assert.Equal(LauncherState.Ready, viewModel.State);
        Assert.True(viewModel.IsFormVisible);
        Assert.True(viewModel.CanSubmit);
        Assert.Equal("账号格式错误。", viewModel.FieldErrorText);
        Assert.Empty(launcher.Logins);
        viewModel.LoginId = "corrected-player";
        await viewModel.SubmitAsync("corrected-password");

        Assert.Equal(2, client.LoginCalls);
        Assert.Equal(LauncherState.Launched, viewModel.State);
        Assert.Single(launcher.Logins);
        Assert.DoesNotContain(secret, string.Join('|', log.Events), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Launch_failure_is_recoverable_and_never_claims_launched()
    {
        var client = new FakeApiClient
        {
            BootstrapResult = Success(ReadyBootstrap),
            LoginResult = Success(SuccessfulLogin),
        };
        var launcher = new FakeGameLauncher
        {
            Result = new(false, "game_not_found", "启动器目录中未找到 DNF.exe。"),
        };
        using var viewModel = Create(ValidConfiguration(), new FakeApiClientFactory(client), launcher: launcher);
        await viewModel.InitializeAsync();

        await viewModel.SubmitAsync("password");

        Assert.Equal(LauncherState.Error, viewModel.State);
        Assert.False(viewModel.IsLaunched);
        Assert.True(viewModel.CanRetry);
        Assert.Contains("DNF.exe", viewModel.GlobalError!, StringComparison.Ordinal);
    }

    private static LauncherViewModel Create(
        ILauncherConfigurationSource configuration,
        ILauncherApiClientFactory factory,
        ILauncherPreferencesStore? preferences = null,
        IGameLauncher? launcher = null,
        ILauncherEventLog? log = null) =>
        new(
            configuration,
            factory,
            preferences ?? new FakePreferencesStore(),
            launcher ?? new FakeGameLauncher(),
            log ?? new RecordingEventLog());

    private static ILauncherConfigurationSource ValidConfiguration() =>
        new DelegateConfigurationSource(() =>
            new LauncherConfiguration(new Uri("https://server.example/")));

    private static LauncherApiResult<T> Success<T>(T value) => new(value);

    private static LauncherApiResult<T> Failure<T>(string code, string message) =>
        new(default, code, message);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class DelegateConfigurationSource(Func<LauncherConfiguration> read)
        : ILauncherConfigurationSource
    {
        public LauncherConfiguration Read() => read();
    }

    private sealed class FakeApiClientFactory(params FakeApiClient[] clients) : ILauncherApiClientFactory
    {
        private readonly Queue<FakeApiClient> clients = new(clients);

        public List<Uri> BaseUrls { get; } = [];

        public ILauncherApiClient Create(Uri baseUrl)
        {
            this.BaseUrls.Add(baseUrl);
            return this.clients.Dequeue();
        }
    }

    private sealed class DelegateApiClientFactory(Func<Uri, ILauncherApiClient> create)
        : ILauncherApiClientFactory
    {
        public ILauncherApiClient Create(Uri baseUrl) => create(baseUrl);
    }

    private sealed class DelegateHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request, cancellationToken);
    }

    private sealed class FakeApiClient : ILauncherApiClient
    {
        public LauncherApiResult<LauncherBootstrap> BootstrapResult { get; init; } =
            Failure<LauncherBootstrap>("unset", "unset");

        public LauncherApiResult<string> RegisterResult { get; init; } =
            Failure<string>("unset", "unset");

        public LauncherApiResult<LauncherLogin> LoginResult { get; init; } =
            Failure<LauncherLogin>("unset", "unset");

        public Func<string, string, CancellationToken, Task<LauncherApiResult<LauncherLogin>>>? Login { get; init; }

        public int RegisterCalls { get; private set; }

        public int LoginCalls { get; private set; }

        public TaskCompletionSource LoginEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LauncherApiResult<LauncherBootstrap>> GetBootstrapAsync(
            CancellationToken cancellationToken) => Task.FromResult(this.BootstrapResult);

        public Task<LauncherApiResult<string>> RegisterAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken)
        {
            this.RegisterCalls++;
            return Task.FromResult(this.RegisterResult);
        }

        public Task<LauncherApiResult<LauncherLogin>> LoginAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken)
        {
            this.LoginCalls++;
            this.LoginEntered.TrySetResult();
            return this.Login is null
                ? Task.FromResult(this.LoginResult)
                : this.Login(loginId, password, cancellationToken);
        }

        public void Dispose() { }
    }

    private sealed class FakePreferencesStore : ILauncherPreferencesStore
    {
        public LauncherPreferences Loaded { get; init; } = LauncherPreferences.Empty;

        public List<LauncherPreferences> Saved { get; } = [];

        public ValueTask<LauncherPreferences> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(this.Loaded);

        public ValueTask SaveAsync(
            LauncherPreferences preferences,
            CancellationToken cancellationToken)
        {
            this.Saved.Add(preferences);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeGameLauncher : IGameLauncher
    {
        public GameLaunchResult Result { get; init; } = new(true);

        public List<LauncherLogin> Logins { get; } = [];

        public GameLaunchResult Launch(LauncherLogin login)
        {
            this.Logins.Add(login);
            return this.Result;
        }
    }

    private sealed class RecordingEventLog : ILauncherEventLog
    {
        public List<string> Events { get; } = [];

        public void Write(string eventCode) => this.Events.Add(eventCode);
    }
}
