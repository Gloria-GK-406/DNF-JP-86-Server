using System.Text.Json.Serialization;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Data.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.HttpApi;

internal static class GateRoutes
{
    public const string Prefix = "/api/gate/v1";
    public const string Status = "/status";
    public const string Register = "/register";
    public const string Login = "/login";
}

/// <summary>
/// The gate's own handlers. Register and login share the launcher handlers; what is gate-specific
/// about them is the authentication in front and the client address they log.
/// </summary>
internal static class GateEndpoints
{
    /// <summary>
    /// Status for the gateway and the standalone launcher. Unavailable (503, as the credential routes
    /// answer) until HTTP and the game are ready and the archive digest can be read.
    /// </summary>
    internal static async Task<IResult> StatusAsync(
        HttpApiReadiness readiness,
        IGameServerReadiness game,
        IContentArchiveIdentity archive,
        IPublicEndpointSource endpoints,
        GateOptions options,
        [FromServices] IEnumerable<GateAdminAddress> admins,
        HostStartTime started,
        IConfiguration configuration,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (!readiness.IsReady || !game.IsReady) return HttpApiApplicationExtensions.Unavailable();

        string? digest;
        try
        {
            digest = await archive.Sha256Async(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            loggers.CreateLogger(typeof(GateEndpoints)).LogWarning(
                "Gate status could not read the content archive digest ({ErrorType}).", error.GetType().Name);
            return HttpApiApplicationExtensions.Unavailable();
        }

        if (string.IsNullOrWhiteSpace(digest)) return HttpApiApplicationExtensions.Unavailable();

        return Results.Json(new GateStatusResponse(
            ServerRuntimeInfo.Version,
            "running",
            digest,
            started.Value,
            options.Launch,
            endpoints.Snapshot.ChannelBootstrap,
            ServerRuntimeInfo.GamePorts(configuration),
            GateRoutes.Prefix,
            options.AdminUrl ?? admins.FirstOrDefault()?.Url,
            Registration: true));
    }

    /// <summary>Names the client the authentication middleware settled for this gate request.</summary>
    internal static void LogCaller(HttpContext context, ILoggerFactory loggers, string operation)
    {
        if (context.Features.Get<GateClientAddress>() is not { } client) return;
        loggers.CreateLogger(typeof(GateEndpoints)).LogInformation(
            "Gate {Operation} from {ClientAddress}.", operation, client.Address);
    }
}

internal sealed record GateStatusResponse(
    string Version,
    string State,
    string PvfSha256,
    DateTimeOffset StartedAt,
    string Launch,
    PublicNetworkEndpoint Channel,
    IReadOnlyList<int> GamePorts,
    string Login,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Admin,
    bool Registration);
