using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Chat;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.OneToOneChat;

/// <summary>A conversation was opened; each member is told of it with the other member named first.</summary>
public sealed record ChatConversationOpened(ChatConversation Conversation) : INotification<ChatConversationOpened>;

/// <summary>Reads nothing: the names were taken when the conversation was opened.</summary>
public sealed class ChatConversationOpenedHandler : INotificationHandler<ChatConversationOpened>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ChatConversationOpened notification, CancellationToken cancellationToken)
    {
        var conversation = notification.Conversation;
        IReadOnlyList<IServerNetworkMessage> messages =
            recipient.CharacterId == conversation.First.CharacterId ? [Opened(conversation, conversation.First)]
            : recipient.CharacterId == conversation.Second.CharacterId ? [Opened(conversation, conversation.Second)]
            : [];
        return ValueTask.FromResult(messages);
    }

    private static ConversationOpenedMessage Opened(ChatConversation conversation, ChatConversationMember self) =>
        new(conversation.Id, conversation.Other(self).Name, self.Name);
}
