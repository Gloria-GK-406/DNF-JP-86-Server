using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Chat;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

// The 1:1 conversation frames more than one slice sends: a line said in a window goes out from
// SendChat (mode 43), and a member leaving is announced from OneToOneChat and from every route that
// delivers a session cleanup (disconnect, return to selection, a selection replacing an older session).

/// <summary>NOTI <c>0x016F</c>: a line said in a conversation window, as the other member receives it.</summary>
/// <remarks>
/// A21 handler VA <c>024EA700</c>: <c>u32 id, dstr sender, dstr text</c>, the sender shorter than 128
/// bytes and the text shorter than 1024. It finds the window by id and drops the line when there is
/// none, or when the sender is the recipient itself: the speaker's own window shows the line already.
/// </remarks>
public sealed record ConversationMessage(ChatConversationId Conversation, byte[] SenderName, byte[] Text) : IGameNotiMessage
{
    /// <summary>The client reads a name only when it is shorter than 128 bytes.</summary>
    public const int NameLimit = 127;
    public const int TextLimit = 1023;

    public ServerMessageType MessageType => ServerMessageType.GameConversationMessage;
}

/// <summary>NOTI <c>0x016E</c>: the named member left (A21 VA <c>024EA5E0</c>, <c>u32 id, dstr name</c>).</summary>
/// <remarks>The recipient's window stays open and shows that the member has left.</remarks>
public sealed record ConversationLeftMessage(ChatConversationId Conversation, byte[] LeaverName) : IGameNotiMessage
{
    public const int NameLimit = ConversationMessage.NameLimit;

    public ServerMessageType MessageType => ServerMessageType.GameConversationLeft;
}

internal sealed class ConversationMessageEncoder : GameNotiMessageEncoder<ConversationMessage>
{
    /// <summary>The longest body this frame can have: <c>4 + 4 + 127 + 4 + 1023</c>.</summary>
    public const int MaximumBodyLength = sizeof(uint) + sizeof(int) + ConversationMessage.NameLimit + sizeof(int) + ConversationMessage.TextLimit;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ConversationMessage message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(ConversationMessage message)
    {
        ChatDString.Require(message.SenderName, 1, ConversationMessage.NameLimit, "conversation sender name");
        ChatDString.Require(message.Text, 1, ConversationMessage.TextLimit, "conversation text");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Conversation.Value);
        ChatDString.Write(writer, message.SenderName);
        ChatDString.Write(writer, message.Text);
        writer.Flush();
        return stream.ToArray();
    }
}

internal sealed class ConversationLeftEncoder : GameNotiMessageEncoder<ConversationLeftMessage>
{
    /// <summary>The longest body this frame can have: <c>4 + 4 + 127</c>.</summary>
    public const int MaximumBodyLength = sizeof(uint) + sizeof(int) + ConversationLeftMessage.NameLimit;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ConversationLeftMessage message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(ConversationLeftMessage message)
    {
        ChatDString.Require(message.LeaverName, 1, ConversationLeftMessage.NameLimit, "conversation leaver name");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Conversation.Value);
        ChatDString.Write(writer, message.LeaverName);
        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>A member left a conversation; the member left behind is told who.</summary>
public sealed record ChatConversationLeft(ChatConversationDeparture Departure) : INotification<ChatConversationLeft>;

/// <summary>Reads nothing: the leaver's name was taken when the conversation was opened.</summary>
public sealed class ChatConversationLeftHandler : INotificationHandler<ChatConversationLeft>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ChatConversationLeft notification, CancellationToken cancellationToken)
    {
        var departure = notification.Departure;
        IReadOnlyList<IServerNetworkMessage> messages = recipient.CharacterId == departure.Remaining.CharacterId
            ? [new ConversationLeftMessage(departure.Conversation, departure.Leaver.Name)]
            : [];
        return ValueTask.FromResult(messages);
    }
}

/// <summary>What a session cleanup has to project for the 1:1 conversations it ended.</summary>
/// <remarks>
/// A cleanup plan is delivered from more than one place -- disconnect, return to selection and a
/// selection that replaces an older session -- and each of them must tell the members left behind.
/// Each place projects these after its party notices, so the rule of who hears what lives here once.
/// </remarks>
internal static class ConversationDepartureNotices
{
    public static IEnumerable<(NotificationAudience Audience, ChatConversationLeft Notification)> For(
        SelectedSessionCleanupNotificationPlan plan) =>
        plan.ConversationDepartures.Select(departure => (
            (NotificationAudience)new NotificationAudience.TheseSessions([departure.Remaining.Session]),
            new ChatConversationLeft(departure)));
}
