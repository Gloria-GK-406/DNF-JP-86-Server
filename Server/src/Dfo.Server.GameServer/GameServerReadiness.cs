using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Networking.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.GameServer;

internal sealed class GameServerReadiness(
    NetworkingOptions options,
    IHostApplicationLifetime lifetime) : IGameServerReadiness
{
    public bool NetworkEnabled => options.Enabled;
    public bool IsReady => options.Enabled
        && lifetime.ApplicationStarted.IsCancellationRequested
        && !lifetime.ApplicationStopping.IsCancellationRequested;
}
