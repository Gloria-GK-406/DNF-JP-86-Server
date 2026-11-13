using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Application.PlayerPortal;
using Dfo.Server.Data.PlayerPortal;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.GmWeb;

/// <summary>The player's sign-in and own account (#329); every route is behind <see cref="GmAccessBoundary"/>'s player prefix.</summary>
internal static class PlayerPortalEndpoints
{
    private const string Prefix = GmAccessBoundary.PlayerPrefix;

    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GmAccessBoundary.PlayerStatusPath, async (IPlayerPortalAccessDomainService players, CancellationToken token) =>
            Results.Ok(new { enabled = await players.IsOpenAsync(token) }))
            .WithMetadata(new GmPublicEndpoint(), new OutsideGameplay("只读 SQLite 的查询：玩家后台总开关"));
        group.MapGet(Prefix + "/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }))
            .WithMetadata(new GmPublicEndpoint(), new OutsideGameplay("不碰玩法状态的 HTTP 路由：防伪令牌"));
        // The password is checked off the executor first (RQ-007), so the login enqueues its CMD itself.
        group.MapPost(Prefix + "/auth/login", LoginAsync)
            .WithMetadata(new GmPublicEndpoint(), new EnqueuesItself(GameplayCallSource.Gm, HttpRouteOperation.PlayerLogin));
        group.MapGet(Prefix + "/auth/session", (HttpContext context) => Results.Ok(Identity((PlayerIdentity)context.Items[PlayerRouteMapping.IdentityKey]!)))
            .WithMetadata(new PlayerSessionRequired(), new OutsideGameplay("不碰玩法状态的 HTTP 路由：边界已识别的玩家身份"));
        PlayerRouteMapping.Map(group, "POST", Prefix + "/auth/logout", HttpRouteOperation.PlayerLogout,
            (HttpContext c) => new LogoutPlayerPortalCommand(PlayerRouteMapping.Secret(c)!));
        // Takes nothing from the request: the account is the session's (RQ-003).
        PlayerRouteMapping.Map(group, "GET", Prefix + "/me", HttpRouteOperation.PlayerMe,
            (HttpContext c) => new QueryPlayerMeCommand(PlayerRouteMapping.Secret(c)!));
        // Only the session names the account: an account id in the body is an undeclared field, in the query it is ignored.
        PlayerRouteMapping.Map(group, "POST", Prefix + "/cera/grant", HttpRouteOperation.PlayerGrantCera,
            async (HttpContext c) => new GrantPlayerCeraCommand(PlayerRouteMapping.Secret(c)!, await ReadCeraAsync(c)));
        PlayerRouteMapping.Map(group, "GET", Prefix + "/operations", HttpRouteOperation.PlayerOperations, (HttpContext c) =>
        {
            var query = c.Request.Query;
            foreach (var name in new[] { "kind", "page", "pageSize" })
                if (query.TryGetValue(name, out var values) && values.Count != 1) throw InvalidQuery();
            PlayerPortalOperationKind? kind = !query.ContainsKey("kind") ? null : query["kind"].ToString() switch
            {
                "cera" => PlayerPortalOperationKind.Cera,
                "mail" => PlayerPortalOperationKind.Mail,
                _ => throw InvalidQuery(),
            };
            return new QueryPlayerOperationsCommand(PlayerRouteMapping.Secret(c)!, kind, Number(query, "page", 1), Number(query, "pageSize", 20));
        });
    }

    private const int CeraBodyLimit = 4096;
    private static readonly JsonSerializerOptions CeraOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new RequestIdConverter() },
    };

    private static async Task<PlayerCeraRequest> ReadCeraAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType() || context.Request.ContentLength is > CeraBodyLimit)
            throw new HttpRouteInputException(400, "请求字段无效。");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = CeraBodyLimit;
        try
        {
            return await context.Request.ReadFromJsonAsync<PlayerCeraRequest>(CeraOptions, context.RequestAborted)
                ?? throw new HttpRouteInputException(400, "请求字段无效。");
        }
        catch (Exception error) when (error is JsonException or BadHttpRequestException or NotSupportedException)
        { throw new HttpRouteInputException(400, "请求字段无效。"); }
    }

    private static int Number(IQueryCollection query, string name, int fallback) => !query.ContainsKey(name) ? fallback
        : int.TryParse(query[name], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw InvalidQuery();
    private static HttpRouteInputException InvalidQuery() => new(400, "查询参数无效。");

    private sealed class RequestIdConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && Guid.TryParse(reader.GetString(), out var id) && id != Guid.Empty
                ? id : throw new JsonException("Invalid UUID.");
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    private static async Task<IResult> LoginAsync(HttpContext context, ILauncherCredentialReader credentials,
        [FromKeyedServices(GmLoginLimiter.PlayerKey)] GmLoginLimiter limiter)
    {
        if (!context.Request.HasJsonContentType() || context.Request.ContentLength is > 8192) return Invalid();
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit) bodyLimit.MaxRequestBodySize = 8192;
        Credentials? request;
        try { request = await context.Request.ReadFromJsonAsync<Credentials>(cancellationToken: context.RequestAborted); }
        catch (Exception exception) when (exception is JsonException or BadHttpRequestException or NotSupportedException) { return Invalid(); }
        if (request is not { LoginName.Length: > 0 and <= 256, Password.Length: > 0 and <= 256 }) return Invalid();
        if (!limiter.TryBegin(context.Connection.RemoteIpAddress!.ToString(), request.LoginName, out var complete))
        {
            context.Response.Headers.RetryAfter = "900";
            return PlayerRouteMapping.Error(429, "rate_limited", "尝试次数过多，请稍后重试。");
        }
        var accepted = false;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                // Same as the launcher login: a record that changed in between is checked once more, then refused.
                var verified = await credentials.VerifyLoginAsync(request.LoginName, request.Password, context.RequestAborted);
                var reply = await HttpGameplay.RunAsync(context, GameplayCallSource.Gm,
                    new LoginPlayerPortalCommand(new(request.LoginName, verified.Login, FinalAttempt: attempt == 1)));
                if (reply.Error is { } loginFailure) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(loginFailure);
                var result = (PlayerLoginResult)reply.Value!;
                if (result.Status == PlayerLoginStatus.CredentialChanged && attempt == 0) continue;
                if (result.Status == PlayerLoginStatus.PortalClosed) return PlayerRouteMapping.Error(403, "portal_closed", "玩家后台已关闭。");
                accepted = result.Status == PlayerLoginStatus.Succeeded;
                // Every credential failure answers alike, whether the account exists or not (RQ-001).
                if (!accepted) return PlayerRouteMapping.Error(401, "unauthenticated", "账号或密码错误。");
                context.Response.Cookies.Append(PlayerRouteMapping.SessionCookie, result.SessionSecret!, PlayerRouteMapping.Cookie(context, result.Identity!.ExpiresAt));
                return Results.Ok(Identity(result.Identity));
            }
        }
        finally { complete(accepted); }
    }

    private static object Identity(PlayerIdentity identity) => new { identity.AccountId, identity.LoginName, identity.ExpiresAt };
    private static IResult Invalid() => PlayerRouteMapping.Error(400, "invalid", "请求字段无效。");
    private sealed record Credentials(string? LoginName, string? Password);
}
