using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.BossTowerReward;

/// <summary>What the BossTowerReward slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class BossTowerRewardRegistration
{
    internal static IServiceCollection AddBossTowerRewardSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<BossTowerRewardNotification, BossTowerRewardEncoder>();
        return services;
    }
}
