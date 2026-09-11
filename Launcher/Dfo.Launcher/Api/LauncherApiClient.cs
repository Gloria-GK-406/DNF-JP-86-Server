using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dfo.Launcher.Api;

public sealed record LauncherBootstrap(
    string ApiVersion,
    string Status,
    bool RegistrationEnabled);

public sealed record LauncherChannelEndpoint(string Host, int Port);

public sealed record LauncherLogin(
    string MId,
    string Ticket,
    DateTimeOffset ExpiresAtUtc,
    LauncherChannelEndpoint Channel);

public sealed record LauncherApiResult<T>(
    T? Value,
    string? ErrorCode = null,
    string? Message = null,
    IReadOnlyDictionary<string, string>? FieldErrors = null)
{
    public bool Succeeded => Value is not null;
}

public interface ILauncherApiClient : IDisposable
{
    Task<LauncherApiResult<LauncherBootstrap>> GetBootstrapAsync(
        CancellationToken cancellationToken);

    Task<LauncherApiResult<string>> RegisterAsync(
        string loginId,
        string password,
        CancellationToken cancellationToken);

    Task<LauncherApiResult<LauncherLogin>> LoginAsync(
        string loginId,
        string password,
        CancellationToken cancellationToken);
}

public interface ILauncherApiClientFactory
{
    ILauncherApiClient Create(Uri baseUrl);
}

public sealed class LauncherApiClientFactory(TimeSpan? timeout = null) : ILauncherApiClientFactory
{
    public ILauncherApiClient Create(Uri baseUrl)
    {
        var operationTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = baseUrl,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new LauncherApiClient(httpClient, operationTimeout);
    }
}

public sealed class LauncherApiClient : ILauncherApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly TimeSpan operationTimeout;

    public LauncherApiClient(HttpClient httpClient, TimeSpan? operationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.operationTimeout = operationTimeout ?? httpClient.Timeout;
        if (this.operationTimeout != Timeout.InfiniteTimeSpan
            && this.operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                "The operation timeout must be positive or infinite.");
        }
    }

    public Task<LauncherApiResult<LauncherBootstrap>> GetBootstrapAsync(
        CancellationToken cancellationToken) =>
        SendAsync<LauncherBootstrap>(
            new HttpRequestMessage(HttpMethod.Get, "api/launcher/v1/bootstrap"),
            cancellationToken);

    public async Task<LauncherApiResult<string>> RegisterAsync(
        string loginId,
        string password,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync<RegistrationResponse>(
            CreateCredentialRequest("api/launcher/v1/register", loginId, password),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? new LauncherApiResult<string>(result.Value!.LoginId)
            : new LauncherApiResult<string>(
                null,
                result.ErrorCode,
                result.Message,
                result.FieldErrors);
    }

    public Task<LauncherApiResult<LauncherLogin>> LoginAsync(
        string loginId,
        string password,
        CancellationToken cancellationToken) =>
        SendAsync<LauncherLogin>(
            CreateCredentialRequest("api/launcher/v1/login", loginId, password),
            cancellationToken);

    public void Dispose() => this.httpClient.Dispose();

    private static HttpRequestMessage CreateCredentialRequest(
        string path,
        string loginId,
        string password) =>
        new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new CredentialsRequest(loginId, password)),
        };

    private async Task<LauncherApiResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        where T : class
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (this.operationTimeout != Timeout.InfiniteTimeSpan)
        {
            deadline.CancelAfter(this.operationTimeout);
        }

        var operationToken = deadline.Token;
        try
        {
            using (request)
            using (var response = await this.httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                operationToken).ConfigureAwait(false))
            {
                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    return Failure<T>(
                        "redirect_not_allowed",
                        "服务器返回了不允许的跳转，请检查服务配置后重试。");
                }

                if (response.IsSuccessStatusCode)
                {
                    var value = await response.Content.ReadFromJsonAsync<T>(
                        JsonOptions,
                        operationToken).ConfigureAwait(false);
                    return value is null
                        ? Failure<T>("invalid_response", "服务器返回了无法识别的响应。")
                        : new LauncherApiResult<T>(value);
                }

                var error = await ReadErrorAsync(response, operationToken).ConfigureAwait(false);
                return new LauncherApiResult<T>(
                    null,
                    error.Code,
                    error.Message,
                    error.FieldErrors);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure<T>("request_timeout", "连接服务器超时，请重试。");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or NotSupportedException)
        {
            return Failure<T>("network_error", "无法连接服务器，请检查网络后重试。");
        }
    }

    private static async Task<ApiError> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return error is { Code.Length: > 0, Message.Length: > 0 }
                ? error
                : SafeHttpError;
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return SafeHttpError;
        }
    }

    private static LauncherApiResult<T> Failure<T>(string code, string message) =>
        new(default, code, message);

    private static ApiError SafeHttpError { get; } =
        new("request_failed", "服务器拒绝了请求，请重试。");

    private sealed record CredentialsRequest(string LoginId, string Password);

    private sealed record RegistrationResponse(string LoginId);

    private sealed record ApiError(
        string Code,
        string Message,
        IReadOnlyDictionary<string, string>? FieldErrors = null);
}
