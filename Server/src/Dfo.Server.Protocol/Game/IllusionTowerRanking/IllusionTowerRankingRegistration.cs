using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.IllusionTowerRanking;

/// <summary>What the IllusionTowerRanking slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class IllusionTowerRankingRegistration
{
    internal static IServiceCollection AddIllusionTowerRankingSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<IllusionTowerRankingNotification, IllusionTowerRankingEncoder>();
        return services;
    }
}
