using System.Text.Json;
using System.Text.Json.Serialization;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Execution;
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
        application.UseMiddleware<HttpListenerBoundaryMiddleware>();
        application.UseMiddleware<GateAuthenticationMiddleware>();
    }

    internal static void MapHttpApiEndpoints(IEndpointRouteBuilder endpoints) => MapHttpApiEndpoints(endpoints, null);

    internal static void MapHttpApiEndpoints(IEndpointRouteBuilder endpoints, HttpListenerScope? listener)
    {
        if (listener is not null) endpoints = endpoints.MapGroup("").WithMetadata(listener);
        MapGateEndpoints(endpoints);
    }

    /// <summary>
    /// The gate group: every route behind the token (<see cref="GateAuthenticationMiddleware"/>), so the
    /// credential routes need no transport rule of their own; HTTPS across networks is the operator's
    /// listener configuration.
    /// </summary>
    private static void MapGateEndpoints(IEndpointRouteBuilder endpoints)
    {
        var gate = endpoints.MapGroup(GateRoutes.Prefix).WithMetadata(GateEndpointMetadata.Instance);
        gate.MapGet(GateRoutes.Status, GateEndpoints.StatusAsync)
            .WithMetadata(new OutsideGameplay("不碰玩法状态的 HTTP 路由：配置、档案摘要与就绪标志"));
        // Hashing runs off the executor first (RQ-007), so these two enqueue their CMD themselves.
        gate.MapPost(GateRoutes.Register, RegisterAsync)
            .WithMetadata(new EnqueuesItself(GameplayCallSource.Http, HttpRouteOperation.Register));
        gate.MapPost(GateRoutes.Login, LoginAsync)
            .WithMetadata(new EnqueuesItself(GameplayCallSource.Http, HttpRouteOperation.Login));
    }

    internal static IResult Unavailable() => ApiResponses.Error(
        StatusCodes.Status503ServiceUnavailable,
        "service_unavailable",
        "服务暂不可用，请稍后重试。");

    private static async Task<IResult> RegisterAsync(
        HttpContext context,
        HttpApiReadiness readiness,
        ICommandBus commands,
        ILauncherCredentialReader credentials,
        ILoggerFactory loggers)
    {
        GateEndpoints.LogCaller(context, loggers, "register");
        if (!readiness.IsReady) return Unavailable();

        var request = await ReadCredentialsAsync(context).ConfigureAwait(false);
        if (request is null)
        {
            return ApiResponses.Error(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "请求字段无效。");
        }

        var prepared = await credentials.PrepareRegistrationAsync(
            request.LoginId!, request.Password!, context.RequestAborted).ConfigureAwait(false);
        if (prepared.Prepared is not { } registration)
        {
            return ApiResponses.Error(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "请求字段无效。",
                prepared.FieldErrors);
        }

        var reply = await HttpGameplay.RunAsync(context, GameplayCallSource.Http,
            new RegisterAccountCommand(registration)).ConfigureAwait(false);
        if (reply.Error is { } registrationFailure) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(registrationFailure);
        var result = (RegistrationResult)reply.Value!;
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
        ILauncherCredentialReader credentials,
        IPublicEndpointSource endpoints,
        IGameServerReadiness game,
        ILoggerFactory loggers)
    {
        GateEndpoints.LogCaller(context, loggers, "login");
        if (!readiness.IsReady || !game.IsReady) return Unavailable();

        var request = await ReadCredentialsAsync(context).ConfigureAwait(false);
        if (request is null)
        {
            return ApiResponses.Error(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "请求字段无效。");
        }

        var result = await AuthenticateAsync(context, commands, credentials, request).ConfigureAwait(false);
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

    /// <summary>
    /// Verifies off the executor, then issues in the CMD. A record that changed in between (a concurrent
    /// legacy migration or rehash) is verified once more against the new record, as the single write
    /// transaction used to do; changed again, it is refused like a wrong password.
    /// </summary>
    private static async Task<AuthenticationResult> AuthenticateAsync(
        HttpContext context,
        ICommandBus commands,
        ILauncherCredentialReader credentials,
        LauncherCredentialsRequest request)
    {
        for (var attempt = 0; ; attempt++)
        {
            var verified = await credentials.VerifyLoginAsync(
                request.LoginId!, request.Password!, context.RequestAborted).ConfigureAwait(false);
            if (verified.Login is not { } login)
                return new AuthenticationResult(verified.Status, FieldErrors: verified.FieldErrors);
            var reply = await HttpGameplay.RunAsync(context, GameplayCallSource.Http,
                new AuthenticateAccountCommand(login)).ConfigureAwait(false);
            if (reply.Error is { } loginFailure) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(loginFailure);
            var result = (AuthenticationResult)reply.Value!;
            if (result.Status != AuthenticationStatus.CredentialChanged) return result;
            if (attempt == 1) return new AuthenticationResult(AuthenticationStatus.InvalidCredentials);
        }
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
        catch (Exception error) when (!context.Response.HasStarted)
        {
            // A CMD's own failure was logged by the executor with its chain (F-30).
            logger.Log(
                GameplayJob.EndedAJob(error) ? LogLevel.Debug : LogLevel.Error,
                "HTTP API request failed with code {ErrorCode}; TraceId={TraceId}.",
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
