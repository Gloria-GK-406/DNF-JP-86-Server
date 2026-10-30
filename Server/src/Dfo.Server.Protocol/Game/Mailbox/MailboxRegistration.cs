using Dfo.Server.Protocol.Game.Mailbox;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ClientHardwareDetail;
using Dfo.Server.Protocol.Game.AvatarCompound;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Mailbox;

/// <summary>What the Mailbox slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MailboxRegistration
{
    internal static IServiceCollection AddMailboxSlice(this IServiceCollection services)
    {
        services.RegisterProtocolMessageHandler<MailboxSendHandler, MailboxSendRequest, GameMailboxCommand>(ClientMessageType.GameMailboxSend);
        services.RegisterProtocolMessageHandler<MailboxSendHandler, MailboxSendRequest, GameMailboxCommand>(ClientMessageType.GameMailboxSendMulti);
        services.AddGameCmdMessageDecoder<MailboxSendDecoder>(ClientMessageType.GameMailboxSend);
        services.AddGameCmdMessageDecoder<MailboxSendMultiDecoder>(ClientMessageType.GameMailboxSendMulti);
        services.AddGameCmdMessageEncoder<MailboxSendResponse, MailboxSendEncoder>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Mail.MailDelivered, MailDeliveredHandler>();
        services.AddProtocolCommandHandler<GameMailboxCommand, GameMailboxOutcome, GameMailboxCommandHandler>(CommandSelfSettlementCapability.SelfSettledAction,
            new(ClientMessageType.GameMailboxSend, typeof(MailboxSendRequest)),
            new(ClientMessageType.GameMailboxSendMulti, typeof(MailboxSendRequest)),
            new(ClientMessageType.GameMailboxClaim, typeof(MailboxClaimRequest)),
            new(ClientMessageType.GameMailboxAction, typeof(MailboxActionRequest)),
            new(ClientMessageType.GameMailboxOpen, typeof(MailboxOpenRequest)),
            new(ClientMessageType.GameMailboxQuery, typeof(MailboxQueryRequest)));
        services.AddNotificationHandler<MailboxCompleted, MailboxCompletedHandler>();
        services.RegisterProtocolMessageHandler<MailboxClaimHandler, MailboxClaimRequest, GameMailboxCommand>(ClientMessageType.GameMailboxClaim);
        services.AddGameCmdMessageDecoder<MailboxClaimDecoder>(ClientMessageType.GameMailboxClaim);
        services.AddGameCmdMessageEncoder<MailboxClaimResponse, MailboxClaimEncoder>();
        services.RegisterProtocolMessageHandler<MailboxActionHandler, MailboxActionRequest, GameMailboxCommand>(ClientMessageType.GameMailboxAction);
        services.AddGameCmdMessageDecoder<MailboxActionDecoder>(ClientMessageType.GameMailboxAction);
        services.AddGameCmdMessageEncoder<MailboxActionResponse, MailboxActionEncoder>();
        // A21 character 000, 2026-09-24: CMD 0060 seq=28 has an empty body.
        services.RegisterProtocolMessageHandler<MailboxOpenHandler, MailboxOpenRequest, GameMailboxCommand>(ClientMessageType.GameMailboxOpen);
        services.AddGameCmdMessageDecoder<MailboxOpenDecoder>(ClientMessageType.GameMailboxOpen);
        services.AddGameCmdMessageEncoder<MailboxOpenResponse, MailboxOpenEncoder>();
        services.RegisterProtocolMessageHandler<MailboxQueryHandler, MailboxQueryRequest, GameMailboxCommand>(ClientMessageType.GameMailboxQuery);
        services.AddGameCmdMessageDecoder<MailboxQueryDecoder>(ClientMessageType.GameMailboxQuery);
        services.AddGameCmdMessageEncoder<MailboxQueryResponse, MailboxQueryEncoder>();
        services.AddGameNotiMessageEncoder<MailboxAlarm, MailboxAlarmEncoder>();
        return services;
    }
}
