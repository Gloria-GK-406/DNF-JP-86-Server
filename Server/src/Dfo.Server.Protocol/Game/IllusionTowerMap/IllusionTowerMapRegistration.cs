using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.IllusionTowerMap;

/// <summary>What the IllusionTowerMap slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class IllusionTowerMapRegistration
{
    internal static IServiceCollection AddIllusionTowerMapSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<IllusionTowerMapNotification, IllusionTowerMapEncoder>();
        return services;
    }
}
