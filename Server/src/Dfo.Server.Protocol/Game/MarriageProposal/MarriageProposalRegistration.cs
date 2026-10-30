using Dfo.Server.Application.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

/// <summary>What the MarriageProposal slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MarriageProposalRegistration
{
    internal static IServiceCollection AddMarriageProposalSlice(this IServiceCollection services)
    {
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingProposalCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.GameMarriageCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingProposal, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalRequest)));
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingResponseCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.GameMarriageCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingResponse, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseRequest)));
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingChurchEnterCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.GameMarriageCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingChurchEntry, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterRequest)));
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingPartnerCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.GameMarriageCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingPartnerQuery, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerQuery)));
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingCommentCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.GameMarriageCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingCommentPost, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingCommentDelete, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingCommentRead, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentRequest)));
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingLetterCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.GameMarriageCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingSendLetter, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterRequest)));
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingGiftCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.GameMarriageCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingMoneyGift, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftRequest)));
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingLovePointCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingLovePointCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameWeddingLovePoint, typeof(Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointRequest)));
        services.AddTypedGameplayTimer<Dfo.Server.Protocol.Game.MarriageProposal.MarriageWeddingScheduler>();
        services.AddInternalCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.AdvanceWeddingTickCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.GameMarriageCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentPostDecoder>(0x03F8, ClientMessageType.GameWeddingCommentPost);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentDeleteDecoder>(0x03F9, ClientMessageType.GameWeddingCommentDelete);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentReadDecoder>(0x03FA, ClientMessageType.GameWeddingCommentRead);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentRequest, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingCommentCommand>(ClientMessageType.GameWeddingCommentPost);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentRequest, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingCommentCommand>(ClientMessageType.GameWeddingCommentDelete);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentRequest, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingCommentCommand>(ClientMessageType.GameWeddingCommentRead);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingBeginNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingBeginEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingAvatarNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingAvatarEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingInvitedGuestsNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingInvitedGuestsEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterDecoder>(0x03F6, ClientMessageType.GameWeddingSendLetter);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterRequest, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingLetterCommand>(ClientMessageType.GameWeddingSendLetter);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterAckEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftDecoder>(0x03F2, ClientMessageType.GameWeddingMoneyGift);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftRequest, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingGiftCommand>(ClientMessageType.GameWeddingMoneyGift);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftAckEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalDecoder>(GameProtocol.WeddingProposalType, ClientMessageType.GameWeddingProposal);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterDecoder>(GameProtocol.WeddingChurchEntryType, ClientMessageType.GameWeddingChurchEntry);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterRequest, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingChurchEnterCommand>(ClientMessageType.GameWeddingChurchEntry);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchPreparationNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchPreparationEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointDecoder>(0x03F5, ClientMessageType.GameWeddingLovePoint);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointRequest, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingLovePointCommand>(ClientMessageType.GameWeddingLovePoint);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointNotificationEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerDecoder>(GameProtocol.WeddingPartnerType, ClientMessageType.GameWeddingPartnerQuery);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerQuery, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingPartnerCommand>(ClientMessageType.GameWeddingPartnerQuery);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingInfoMessage, Dfo.Server.Protocol.Game.MarriageProposal.WeddingInfoEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseDecoder>(GameProtocol.WeddingResponseType, ClientMessageType.GameWeddingResponse);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalRequest, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingProposalCommand>(ClientMessageType.GameWeddingProposal);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseHandler, Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseRequest, Dfo.Server.Protocol.Game.MarriageProposal.GameWeddingResponseCommand>(ClientMessageType.GameWeddingResponse);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalAckEncoder>();
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalNotificationEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseNotificationEncoder>();
        return services;
    }
}
