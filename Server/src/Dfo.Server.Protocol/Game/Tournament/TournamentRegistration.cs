using Dfo.Server.Application.Execution;
using Dfo.Server.Protocol.Game.Tournament;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Tournament;

/// <summary>What the Tournament slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class TournamentRegistration
{
    internal static IServiceCollection AddTournamentSlice(this IServiceCollection services)
    {
        services.AddTypedGameplayTimer<Dfo.Server.Protocol.Game.Tournament.TournamentQuotaDailyRefreshService>();
        services.AddTypedGameplayTimer<Dfo.Server.Protocol.Game.Tournament.HerasQuotaDailyRefreshService>();
        services.AddTypedGameplayTimer<TournamentRewardSweepService>();
        services.AddInternalCommandHandler<SweepTournamentRewardsCommand, bool, SweepTournamentRewardsCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.AddInternalCommandHandler<SweepTournamentCandidateCommand, bool, SweepTournamentRewardsCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.RegisterProtocolMessageHandler<TournamentRewardHandler, TournamentRewardRequest, GameTournamentRewardCommand>(ClientMessageType.GameTournamentRewardStatus);
        services.AddProtocolCommandHandler<GameTournamentRewardCommand, bool, GameTournamentRewardCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameTournamentRewardStatus, typeof(TournamentRewardRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameTournamentSelectReward, typeof(TournamentRewardRequest)));
        services.RegisterProtocolMessageHandler<TournamentRewardHandler, TournamentRewardRequest, GameTournamentRewardCommand>(ClientMessageType.GameTournamentSelectReward);
        services.AddGameCmdMessageDecoder<TournamentStatusDecoder>(0x01CA, ClientMessageType.GameTournamentRewardStatus);
        services.AddGameCmdMessageDecoder<TournamentSelectionDecoder>(0x01CB, ClientMessageType.GameTournamentSelectReward);
        services.AddGameNotiMessageEncoder<TournamentInfoMessage, TournamentInfoEncoder>();
        services.AddGameNotiMessageEncoder<TournamentMapMessage, TournamentMapEncoder>();
        services.AddGameNotiMessageEncoder<TournamentRewardMessage, TournamentRewardEncoder>();
        services.AddGameCmdMessageEncoder<TournamentStatusResponse, TournamentStatusEncoder>();
        services.AddGameCmdMessageEncoder<TournamentSelectionResponse, TournamentSelectionEncoder>();
        return services;
    }
}
