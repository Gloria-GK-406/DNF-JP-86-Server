using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.OneToOneChat;

/// <summary>What the OneToOneChat slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class OneToOneChatRegistration
{
    internal static IServiceCollection AddOneToOneChatSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<OpenConversationDecoder>(ClientMessageType.GameOpenConversation);
        services.AddGameCmdMessageDecoder<LeaveConversationDecoder>(ClientMessageType.GameLeaveConversation);
        services.AddGameCmdMessageDecoder<ConversationUserStateDecoder>(ClientMessageType.GameConversationUserState);
        services.AddGameNotiMessageEncoder<ConversationOpenedMessage, ConversationOpenedEncoder>();
        services.AddGameNotiMessageEncoder<ConversationRefusedMessage, ConversationRefusedEncoder>();
        services.AddGameNotiMessageEncoder<ConversationLeftMessage, ConversationLeftEncoder>();
        services.AddGameNotiMessageEncoder<ConversationMessage, ConversationMessageEncoder>();
        services.AddNotificationHandler<ChatConversationOpened, ChatConversationOpenedHandler>();
        services.AddNotificationHandler<ChatConversationLeft, ChatConversationLeftHandler>();
        services.AddProtocolCommandHandler<GameOpenConversationCommand, bool, GameOpenConversationCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameOpenConversation, typeof(OpenConversationRequest)));
        services.AddProtocolCommandHandler<GameLeaveConversationCommand, bool, GameLeaveConversationCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameLeaveConversation, typeof(LeaveConversationRequest)));
        services.AddProtocolCommandHandler<GameConversationUserStateCommand, bool, GameConversationUserStateCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameConversationUserState, typeof(ConversationUserStateReport)));
        services.RegisterProtocolMessageHandler<OpenConversationHandler, OpenConversationRequest, GameOpenConversationCommand>(ClientMessageType.GameOpenConversation);
        services.RegisterProtocolMessageHandler<LeaveConversationHandler, LeaveConversationRequest, GameLeaveConversationCommand>(ClientMessageType.GameLeaveConversation);
        services.RegisterProtocolMessageHandler<ConversationUserStateHandler, ConversationUserStateReport, GameConversationUserStateCommand>(ClientMessageType.GameConversationUserState);
        return services;
    }
}
