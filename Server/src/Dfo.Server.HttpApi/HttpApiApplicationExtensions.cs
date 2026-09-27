using System.Text.Json;
using System.Text.Json.Serialization;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.HttpApi;

public static class HttpApiApplicationExtensions
{
    private const string BootstrapPath = "/api/launcher/v1/bootstrap";
    internal const string RegisterPath = "/api/launcher/v1/register";
    internal const string LoginPath = "/api/launcher/v1/login";

    public static WebApplication MapHttpApi(this WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        UseHttpApiMiddleware(application);
        MapHttpApiEndpoints(application);
        return application;
    }

    internal static void UseHttpApiMiddleware(IApplicationBuilder application)
    {
        application.UseMiddleware<HttpApiExceptionMiddleware>();
        application.UseRouting();
        application.UseMiddleware<CredentialTransportGateMiddleware>();
    }

    internal static void MapHttpApiEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(BootstrapPath, (HttpApiReadiness readiness) =>
            readiness.IsReady
                ? Results.Json(new BootstrapResponse("1", "ready", true))
                : ApiResponses.Error(
                    StatusCodes.Status503ServiceUnavailable,
                    "service_unavailable",
                    "服务暂不可用，请稍后重试。"));
        endpoints.MapPost(RegisterPath, RegisterAsync)
            .WithMetadata(CredentialEndpointMetadata.Instance);
        endpoints.MapPost(LoginPath, LoginAsync)
            .WithMetadata(CredentialEndpointMetadata.Instance);
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context,
        HttpApiReadiness readiness,
        ICommandBus commands)
    {
        if (!readiness.IsReady)
        {
            return ApiResponses.Error(
                StatusCodes.Status503ServiceUnavailable,
                "service_unavailable",
                "服务暂不可用，请稍后重试。");
        }

        var request = await ReadCredentialsAsync(context).ConfigureAwait(false);
        if (request is null)
        {
            return ApiResponses.Error(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "请求字段无效。");
        }

        var result = await commands.SendAsync(
            new RegisterAccountCommand(request.LoginId!, request.Password!),
            context.RequestAborted).ConfigureAwait(false);
        return result.Status switch
        {
            AuthenticationStatus.Success => Results.Json(
                new RegistrationResponse(result.LoginId!),
                statusCode: StatusCodes.Status201Created),
            AuthenticationStatus.InvalidRequest => ApiResponses.Error(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "请求字段无效。",
                result.FieldErrors),
            AuthenticationStatus.AccountExists => ApiResponses.Error(
                StatusCodes.Status409Conflict,
                "account_exists",
                "账号已存在。"),
            _ => throw new InvalidOperationException(
                "The registration command returned an unsupported status."),
        };
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context,
        HttpApiReadiness readiness,
        ICommandBus commands,
        IPublicEndpointSource endpoints,
        IGameServerReadiness game)
    {
        if (!readiness.IsReady || !game.IsReady)
        {
            return ApiResponses.Error(
                StatusCodes.Status503ServiceUnavailable,
                "service_unavailable",
                "服务暂不可用，请稍后重试。");
        }

        var request = await ReadCredentialsAsync(context).ConfigureAwait(false);
        if (request is null)
        {
            return ApiResponses.Error(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "请求字段无效。");
        }

        var result = await commands.SendAsync(
            new AuthenticateAndIssueTicketCommand(request.LoginId!, request.Password!),
            context.RequestAborted).ConfigureAwait(false);
        return result.Status switch
        {
            AuthenticationStatus.Success => Results.Json(new LoginResponse(
                result.MId!,
                result.Ticket!,
                result.ExpiresAtUtc!.Value,
                endpoints.Snapshot.ChannelBootstrap)),
            AuthenticationStatus.InvalidRequest => ApiResponses.Error(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "请求字段无效。",
                result.FieldErrors),
            AuthenticationStatus.InvalidCredentials => ApiResponses.Error(
                StatusCodes.Status401Unauthorized,
                "invalid_credentials",
                "账号或密码错误。"),
            _ => throw new InvalidOperationException(
                "The authentication command returned an unsupported status."),
        };
    }

    private static async ValueTask<LauncherCredentialsRequest?> ReadCredentialsAsync(
        HttpContext context)
    {
        if (!context.Request.HasJsonContentType()
            || context.Request.ContentLength is > 8192)
        {
            return null;
        }

        try
        {
            var request = await context.Request.ReadFromJsonAsync<LauncherCredentialsRequest>(
                cancellationToken: context.RequestAborted).ConfigureAwait(false);
            return request is { LoginId: not null, Password: not null } ? request : null;
        }
        catch (Exception exception) when (
            exception is JsonException or BadHttpRequestException or NotSupportedException)
        {
            return null;
        }
    }
}

internal sealed class CredentialTransportGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<CredentialEndpointMetadata>() is not null
            && !context.Request.IsHttps
            && (context.Connection.RemoteIpAddress is null
                || !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)))
        {
            await ApiResponses.WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "secure_transport_required",
                "远程登录必须使用 HTTPS。").ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}

internal sealed class CredentialEndpointMetadata
{
    public static CredentialEndpointMetadata Instance { get; } = new();

    private CredentialEndpointMetadata() { }
}

internal sealed class HttpApiExceptionMiddleware(
    RequestDelegate next,
    ILogger<HttpApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (!context.Response.HasStarted)
        {
            logger.LogError(
                "Launcher API request failed with code {ErrorCode}; TraceId={TraceId}.",
                "internal_error",
                context.TraceIdentifier);
            await ApiResponses.WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "服务器暂时无法处理请求。").ConfigureAwait(false);
        }
    }
}

internal static class ApiResponses
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IResult Error(
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, string>? fieldErrors = null) =>
        Results.Json(new ErrorResponse(code, message, fieldErrors), statusCode: statusCode);

    public static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new ErrorResponse(code, message),
            JsonOptions,
            context.RequestAborted).ConfigureAwait(false);
    }
}

internal sealed record LauncherCredentialsRequest(string? LoginId, string? Password);
internal sealed record BootstrapResponse(string ApiVersion, string Status, bool RegistrationEnabled);
internal sealed record RegistrationResponse(string LoginId);
internal sealed record LoginResponse(
    string MId,
    string Ticket,
    DateTimeOffset ExpiresAtUtc,
    Dfo.Server.Data.Configuration.PublicNetworkEndpoint Channel);
internal sealed record ErrorResponse(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string>? FieldErrors = null);
