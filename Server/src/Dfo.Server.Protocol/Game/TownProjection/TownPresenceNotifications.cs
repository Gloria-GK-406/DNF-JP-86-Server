using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.TownProjection;

/// <summary>A character has become visible in the recipients' town area.</summary>
public sealed record TownParticipantPublished(TownPresenceSynchronizationState State)
    : INotification<TownParticipantPublished>;

/// <summary>A visible character changed position inside the recipients' town area.</summary>
public sealed record TownPositionChanged(
    ushort WireUserId,
    short X,
    short Y,
    byte Direction,
    ushort Trailer) : INotification<TownPositionChanged>;

/// <summary>The authoritative occupants left in one area after its membership changed.</summary>
/// <remarks>
/// An area crossing must not be projected as USER_LEAVE. That notification removes the user by
/// its global wire ID and A21 also drops the party/P2P member object attached to it. AREA_USERS is
/// scoped by town and area, so it can remove only the old town actor while preserving the selected
/// session, party membership and UDP endpoint.
/// </remarks>
public sealed record TownAreaRosterReconciled(
    byte TownId,
    byte AreaId,
    IReadOnlyList<AreaUserPresence> RemainingUsers)
    : INotification<TownAreaRosterReconciled>;

public sealed class TownParticipantPublishedHandler
    : INotificationHandler<TownParticipantPublished>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        TownParticipantPublished notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            TownPresenceMessages.ForExistingCharacter(
                notification.State,
                recipient.CharacterId));
    }
}

public sealed class TownPositionChangedHandler : INotificationHandler<TownPositionChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        TownPositionChanged notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            [new UserPositionNotification(
                notification.WireUserId,
                notification.X,
                notification.Y,
                notification.Direction,
                notification.Trailer)]);
    }
}

public sealed class TownAreaRosterReconciledHandler
    : INotificationHandler<TownAreaRosterReconciled>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        TownAreaRosterReconciled notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            [new AreaUsersNotification(
                notification.TownId,
                notification.AreaId,
                notification.RemainingUsers)]);
    }
}

internal static class TownPresenceMessages
{
    public static IReadOnlyList<IGameServerMessage> ForNewcomer(
        TownPresenceSynchronizationState state)
    {
        var messages = new List<IGameServerMessage>((state.ExistingCharacters.Count * 3) + 1);
        foreach (var character in state.ExistingCharacters)
            AddCharacter(messages, character);
        messages.Add(AreaRoster(state));
        return messages;
    }

    public static IReadOnlyList<IServerNetworkMessage> ForExistingCharacter(
        TownPresenceSynchronizationState state,
        CharacterId recipientCharacterId)
    {
        var messages = new List<IGameServerMessage>(4);
        AddCharacter(messages, state.Newcomer);
        messages.Add(AreaRoster(state, recipientCharacterId));
        return messages;
    }

    private static void AddCharacter(
        ICollection<IGameServerMessage> messages,
        TownParticipantEntryState participant)
    {
        var wireUserId = SelectedSessionIdentity.ToWireUserId(
            participant.Character.Identity.CharacterId);
        messages.Add(new CurrentUserStateMessage(
            participant.Character,
            participant.Public,
            participant.Equipment,
            participant.Pets));
        messages.Add(new UserVisibilityMessage(
            wireUserId,
            checked((byte)participant.Public.VisibilityBits)));
        messages.Add(new UserAreaNotification(
            wireUserId,
            participant.Spawn,
            participant.Spawn.AreaId));
    }

    private static AreaUsersNotification AreaRoster(TownPresenceSynchronizationState state)
    {
        var roster = new List<AreaUserPresence>(state.ExistingCharacters.Count + 1)
        {
            Presence(state.Newcomer),
        };
        roster.AddRange(state.ExistingCharacters.Select(Presence));
        return new AreaUsersNotification(state.TownId, state.AreaId, roster);
    }

    private static AreaUsersNotification AreaRoster(
        TownPresenceSynchronizationState state,
        CharacterId firstCharacterId)
    {
        var all = state.ExistingCharacters.Prepend(state.Newcomer).ToArray();
        var first = all.SingleOrDefault(value =>
            value.Character.Identity.CharacterId == firstCharacterId)
            ?? throw new InvalidDataException(
                $"Town notification recipient {firstCharacterId.Value} is not in its roster.");
        var roster = new List<AreaUserPresence>(all.Length) { Presence(first) };
        roster.AddRange(all
            .Where(value => value.Character.Identity.CharacterId != firstCharacterId)
            .Select(Presence));
        return new AreaUsersNotification(state.TownId, state.AreaId, roster);
    }

    private static AreaUserPresence Presence(TownParticipantEntryState participant) =>
        new(
            SelectedSessionIdentity.ToWireUserId(participant.Character.Identity.CharacterId),
            participant.Spawn.X,
            participant.Spawn.Y,
            participant.Spawn.Direction,
            participant.Spawn.AreaState);
}
