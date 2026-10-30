using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.IllusionTowerInfo;

/// <summary>What the IllusionTowerInfo slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class IllusionTowerInfoRegistration
{
    internal static IServiceCollection AddIllusionTowerInfoSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<IllusionTowerInfoNotification, IllusionTowerInfoEncoder>();
        return services;
    }
}
