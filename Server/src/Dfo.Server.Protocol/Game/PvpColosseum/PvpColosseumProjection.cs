using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.PvpColosseum;

public sealed class PvpColosseumProjection(IPvpColosseumDomainService colosseum, IGameCommandOutbox outbox)
{
    public async ValueTask ProjectAsync(SelectedSessionIdentity session, ChannelEntryIdentity channel, CancellationToken ct)
    {
        if (!channel.SupportsMatchmaking) return;
        var state = await colosseum.ReadLobbyAsync(session, ct).ConfigureAwait(false);
        await outbox.RegisterNotiAsync(session.ConnectionId, new ColosseumContextNotice(session.WireUserId, state), ct)
            .ConfigureAwait(false);
        var run = channel.Kind == ChannelEntryKind.TotalMatchDuel ? state.TotalMatch : state.Individual;
        if (run is { Complete: true })
            await outbox.RegisterNotiAsync(session.ConnectionId, new ColosseumRewardsNotice(run.Prizes), ct).ConfigureAwait(false);
    }
}

internal static class PvpColosseumRegistration
{
    internal static IServiceCollection AddPvpColosseumSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<EnterColosseumDecoder>(ClientMessageType.GameEnterPvpColosseum);
        services.AddGameCmdMessageDecoder<UpdateColosseumRewardDecoder>(ClientMessageType.GameUpdatePvpColosseumReward);
        services.AddGameCmdMessageEncoder<EnterColosseumResponse, EnterColosseumResponseEncoder>();
        services.AddGameNotiMessageEncoder<ColosseumContextNotice, ColosseumContextEncoder>();
        services.AddGameNotiMessageEncoder<ColosseumRewardsNotice, ColosseumRewardsEncoder>();
        services.AddProtocolCommandHandler<EnterColosseumCommand, bool, ColosseumCommands>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameEnterPvpColosseum, typeof(EnterColosseumRequest)));
        services.AddProtocolCommandHandler<UpdateColosseumRewardCommand, bool, ColosseumCommands>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameUpdatePvpColosseumReward, typeof(UpdateColosseumRewardRequest)));
        services.RegisterProtocolMessageHandler<EnterColosseumHandler, EnterColosseumRequest, EnterColosseumCommand>(
            ClientMessageType.GameEnterPvpColosseum);
        services.RegisterProtocolMessageHandler<UpdateColosseumRewardHandler, UpdateColosseumRewardRequest, UpdateColosseumRewardCommand>(
            ClientMessageType.GameUpdatePvpColosseumReward);
        return services;
    }
}
