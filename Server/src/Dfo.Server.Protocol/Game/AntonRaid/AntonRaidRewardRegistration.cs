using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal static class AntonRaidRewardRegistration
{
    internal static void AddAntonRaidRewardsSlice(this IServiceCollection services)
    {
        ClientMessageType[] types = [ClientMessageType.GameFinishAntonRaidMovie, ClientMessageType.GameSelectAntonRaidReward];
        services.AddProtocolCommandHandler<GameAntonRaidRewardCommand, bool, AntonRaidRewardCommandHandler>(
            [.. types.Select(type => new ProtocolCommandMessageBinding(type, typeof(AntonRaidRewardRequest)))]);
        foreach (var type in types)
        {
            services.AddGameCmdMessageDecoder<AntonRaidRewardRequestDecoder>(type);
            services.RegisterProtocolMessageHandler<AntonRaidRewardMessageHandler, AntonRaidRewardRequest, GameAntonRaidRewardCommand>(type);
        }
        services.AddGameNotiMessageEncoder<AntonRaidRewardNotice, AntonRaidRewardNoticeEncoder>();
        services.AddInternalCommandHandler<QueryAntonRaidRewardDeadlines, IReadOnlyList<Dfo.Server.Abstractions.Raids.AntonRaidRewardDeadline>,
            AntonRaidRewardCommandHandler>();
        services.AddInternalCommandHandler<AdvanceAntonRaidRewardDeadline, bool, AntonRaidRewardCommandHandler>();
        services.TryAddSingleton<AntonRaidRewardProjection>();
        services.AddDungeonDeadlineSource<AntonRaidRewardDeadlineSource>();
    }
}
