using System.Globalization;
using System.Reflection;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Administration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Dfo.Server.HttpApi;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.GmWeb;

internal static class GmServerInfoEndpoints
{
    // Build metadata after '+' is the source revision; operators compare release numbers.
    private static readonly string Version = (typeof(GmServerInfoEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(GmServerInfoEndpoints).Assembly.GetName().Version?.ToString() ?? "0.0.0").Split('+')[0];

    internal static void Map(RouteGroupBuilder group, GmWebOptions options) => group.MapGet("/api/gm/v1/server-info",
        async (IConfiguration configuration, IContentArchiveIdentity archive, CancellationToken token) =>
        {
            // Same reading and defaults as NetworkingModule; reported as configured even while networking is off.
            var section = configuration.GetSection("Networking");
            int? Port(string? raw) => int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535 ? port : null;
            var games = section.GetSection("GamePorts").GetChildren().Select(child => Port(child.Value)).OfType<int>().ToArray();
            return Results.Ok(new
            {
                version = Version, networkEnabled = bool.TryParse(section["Enabled"], out var enabled) && enabled,
                channelPort = section["ChannelPort"] is { } channel ? Port(channel) : 7001, gamePorts = games.Length == 0 ? [10011] : games,
                gmPort = options.ListenUrl!.Port, pvfSha256 = await archive.Sha256Async(token),
            });
        }).WithMetadata(new GmRequiredPermission(GmPermission.None), new OutsideGameplay("PVF 内容源与缓存：配置与档案摘要"));
}
