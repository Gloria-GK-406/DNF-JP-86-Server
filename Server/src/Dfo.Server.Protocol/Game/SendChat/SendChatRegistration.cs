using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.SendChat;

/// <summary>What the SendChat slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class SendChatRegistration
{
    internal static IServiceCollection AddSendChatSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<SendChatDecoder>(ClientMessageType.GameSendChat);
        services.AddGameCmdMessageDecoder<ItemLinkChatDecoder>(ClientMessageType.GameItemLinkChat);
        services.AddGameCmdMessageEncoder<SendChatRefusal, SendChatRefusalEncoder>();
        services.AddGameCmdMessageEncoder<ItemLinkChatRefusal, ItemLinkChatRefusalEncoder>();
        services.AddGameNotiMessageEncoder<ChatLineMessage, ChatLineEncoder>();
        services.AddGameNotiMessageEncoder<WhisperMessage, WhisperEncoder>();
        services.AddGameNotiMessageEncoder<MegaphoneMessage, MegaphoneEncoder>();
        services.AddNotificationHandler<ChatMessagePosted, ChatMessagePostedHandler>();
        services.AddProtocolCommandHandler<GameSendChatCommand, bool, GameSendChatCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSendChat, typeof(SendChatRequest)));
        services.AddProtocolCommandHandler<GameItemLinkChatCommand, bool, GameItemLinkChatCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameItemLinkChat, typeof(ItemLinkChatRequest)));
        services.RegisterProtocolMessageHandler<SendChatHandler, SendChatRequest, GameSendChatCommand>(ClientMessageType.GameSendChat);
        services.RegisterProtocolMessageHandler<ItemLinkChatHandler, ItemLinkChatRequest, GameItemLinkChatCommand>(ClientMessageType.GameItemLinkChat);
        return services;
    }
}
