using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.IllusionTowerReward;

/// <summary>What the IllusionTowerReward slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class IllusionTowerRewardRegistration
{
    internal static IServiceCollection AddIllusionTowerRewardSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<IllusionTowerRewardNotification, IllusionTowerRewardEncoder>();
        return services;
    }
}
