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
    internal static void Map(RouteGroupBuilder group, GmWebOptions options) => group.MapGet("/api/gm/v1/server-info",
        async (IConfiguration configuration, IContentArchiveIdentity archive, CancellationToken token) => Results.Ok(new
        {
            version = ServerRuntimeInfo.Version,
            networkEnabled = bool.TryParse(configuration["Networking:Enabled"], out var enabled) && enabled,
            channelPort = ServerRuntimeInfo.ChannelPort(configuration), gamePorts = ServerRuntimeInfo.GamePorts(configuration),
            gmPort = options.ListenUrl!.Port, pvfSha256 = await archive.Sha256Async(token),
        })).WithMetadata(new GmRequiredPermission(GmPermission.None), new OutsideGameplay("PVF 内容源与缓存：配置与档案摘要"));
}
