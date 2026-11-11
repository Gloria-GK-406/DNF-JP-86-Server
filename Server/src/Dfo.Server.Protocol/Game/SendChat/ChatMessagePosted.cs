using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SendChat;

/// <summary>A chat line, built once by the command, to be handed to every recipient as it is.</summary>
/// <param name="SpeakerFollowUp">
/// What only the speaker receives after the line itself: the megaphone slot's state. The client takes
/// a megaphone off its own count when its own line arrives, so the server's figure has to come after
/// the line to be the one left standing (NOTI <c>0x000E</c> assigns the slot, it does not add).
/// </param>
public sealed record ChatMessagePosted(IGameNotiMessage Line, CharacterId Speaker,
    IReadOnlyList<IGameServerMessage> SpeakerFollowUp) : INotification<ChatMessagePosted>;

/// <summary>Reads nothing: who receives the line was decided when it was posted.</summary>
public sealed class ChatMessagePostedHandler : INotificationHandler<ChatMessagePosted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ChatMessagePosted notification, CancellationToken cancellationToken)
    {
        IReadOnlyList<IServerNetworkMessage> messages = recipient.CharacterId == notification.Speaker
            ? [notification.Line, .. notification.SpeakerFollowUp]
            : [notification.Line];
        return ValueTask.FromResult(messages);
    }
}
