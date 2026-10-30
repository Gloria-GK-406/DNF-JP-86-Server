using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.BossTowerMap;

/// <summary>What the BossTowerMap slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class BossTowerMapRegistration
{
    internal static IServiceCollection AddBossTowerMapSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<BossTowerMapNotification, BossTowerMapEncoder>();
        return services;
    }
}
