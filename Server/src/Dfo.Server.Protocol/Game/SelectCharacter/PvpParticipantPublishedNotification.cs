using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

/// <summary>Somebody has joined the PvP lobby the recipients are standing in.</summary>
/// <remarks>
/// The newcomer travels with the notification because it is what happened, not what anyone's state
/// is: their entry is finished and committed by the time this goes out, and re-reading it would
/// only find the same thing under a later name.
/// </remarks>
public sealed record PvpParticipantPublished(PvpParticipantEntryState Newcomer)
    : INotification<PvpParticipantPublished>;

/// <summary>The one frame each of them should see.</summary>
/// <remarks>
/// Everyone in the lobby gets the same thing here, which is what a fan-out looks like when nobody
/// needs a personalised answer. The seam still asks per recipient, and costs nothing for it: the
/// frame was already encoded once per connection before any of this existed.
/// </remarks>
public sealed class PvpParticipantPublishedHandler : INotificationHandler<PvpParticipantPublished>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        PvpParticipantPublished notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            [new PvpParticipantPublicationMessage(notification.Newcomer)]);
    }
}
