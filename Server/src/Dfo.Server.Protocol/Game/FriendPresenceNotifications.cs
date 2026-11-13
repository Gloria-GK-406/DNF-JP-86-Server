using System.Globalization;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

// What a character's observers -- the characters keeping it as a friend -- are told when it comes
// online, goes offline, levels, changes grow type or is deleted (Spec RQ-050 – RQ-055). Every push
// is a projection to TheseCharacters(observers): the notification service finds each observer's
// current connection when it is composed, so an observer that reconnected is reached on its new
// session and never on the old one (Plan SD-8). Who the observers are is read by whoever raises the
// push; what each one is shown is composed here, per recipient.

/// <summary>The character came online: its observers are shown the channel it is on (Spec AM-2: same channel included).</summary>
public sealed record FriendCameOnline(CharacterId Character) : INotification<FriendCameOnline>;

/// <summary>The character went offline: its observers are shown channel 0.</summary>
/// <param name="Name">Carried because the session it could be read from is already gone when this is composed.</param>
public sealed record FriendWentOffline(CharacterId Character, byte[] Name) : INotification<FriendWentOffline>;

/// <summary>Something an observer's friend list shows changed; each recipient is sent its own list again.</summary>
/// <param name="Zone">
/// The server's local zone, for the blacklist dates that follow the list. Carried by whoever raises
/// this, because a handler may not take a clock. Only an in-memory projection may carry a zone; a
/// persisted form would carry its id.
/// </param>
public sealed record FriendListChanged(TimeZoneInfo Zone) : INotification<FriendListChanged>;

/// <summary>A friend was deleted: the recipient's client is told the name is gone (NOTI 0x0111 subcmd 2).</summary>
/// <param name="Name">The deleted character's name, as the deletion request carried and matched it.</param>
public sealed record FriendRemoved(byte[] Name) : INotification<FriendRemoved>;

/// <summary>Shows the recipient on which channel the friend now is, and whether the recipient blacklisted it.</summary>
internal sealed class FriendCameOnlineHandler(ICharacterRelationQueryDomainService relations,
    ICharacterIdentityQueryDomainService identities, ISelectedSessionDomainService sessions,
    IChannelEntryDomainService channels) : INotificationHandler<FriendCameOnline>
{
    /// <remarks>
    /// Abstains when the friend is no longer in the world by the time this is composed: a presence
    /// line naming a channel it already left would show it online until its next change. A listener
    /// with no configured channel resolves to 0, which the client would print as the friend leaving;
    /// that fails this recipient instead, and the notification bus logs it.
    /// </remarks>
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        FriendCameOnline notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(notification);
        var friend = notification.Character;
        if (recipient.CharacterId == friend || !sessions.TryGetInWorld(friend, out var session)) return [];
        if (!(await identities.ReadAsync([friend], cancellationToken).ConfigureAwait(false)).TryGetValue(friend, out var identity))
            return [];
        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken).ConfigureAwait(false);
        var number = channel.ChannelId is >= 1 and <= ushort.MaxValue
            ? (ushort)channel.ChannelId
            : throw new InvalidDataException($"Channel {channel.ChannelId} cannot be shown as a friend's channel.");
        var blacklisted = await relations.ExistsAsync(recipient.CharacterId, friend, CharacterRelationKind.Blacklist,
            cancellationToken).ConfigureAwait(false);
        return [new FriendPresenceMessage(number, identity.Name, blacklisted)];
    }
}

/// <summary>Shows the recipient channel 0 for the friend, keeping the recipient's own blacklist mark.</summary>
internal sealed class FriendWentOfflineHandler(ICharacterRelationQueryDomainService relations) : INotificationHandler<FriendWentOffline>
{
    /// <remarks>The mark is read, not assumed: the client overwrites its own with this byte, so a plain 0 would clear it.</remarks>
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        FriendWentOffline notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(notification);
        if (recipient.CharacterId == notification.Character) return [];
        var blacklisted = await relations.ExistsAsync(recipient.CharacterId, notification.Character,
            CharacterRelationKind.Blacklist, cancellationToken).ConfigureAwait(false);
        return [new FriendPresenceMessage(OfflineChannel, notification.Name, blacklisted)];
    }

    /// <summary>The channel the client reads as "not online" (A21 VA <c>0116F000</c>; NOTI-0x0112).</summary>
    private const ushort OfflineChannel = 0;
}

/// <summary>The recipient's whole friend list, then its blacklist, as they stand now.</summary>
internal sealed class FriendListChangedHandler(ICharacterRelationQueryDomainService relations,
    ICharacterIdentityQueryDomainService identities, ISelectedSessionDomainService sessions,
    IChannelEntryDomainService channels) : INotificationHandler<FriendListChanged>
{
    /// <remarks>
    /// The two frames, NOTI 0x0111 then CMD 0x0078, go out as one batch for this recipient: a full
    /// list wipes the client's blacklist, so the blacklist must never arrive without it or before it.
    /// Entries the client could not take are left out silently here; selection and the friend
    /// commands, which have a logger, report the same rows whenever they build the list.
    /// </remarks>
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        FriendListChanged notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(notification);
        var refresh = await FriendListRefresh.BuildAsync(recipient.CharacterId, relations, identities, sessions, channels,
            notification.Zone, cancellationToken).ConfigureAwait(false);
        return refresh.Messages;
    }
}

/// <summary>Reads nothing: the name was taken from the deletion request.</summary>
internal sealed class FriendRemovedHandler : INotificationHandler<FriendRemoved>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        FriendRemoved notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new FriendRemovedMessage([notification.Name])]);
    }
}

/// <summary>Who among a character's observers can be told anything now.</summary>
internal static class FriendObservers
{
    /// <summary>The characters keeping <paramref name="character"/> as a friend that are in the world.</summary>
    public static async ValueTask<IReadOnlyList<CharacterId>> InWorldAsync(CharacterId character,
        ICharacterRelationQueryDomainService relations, ISelectedSessionDomainService sessions, CancellationToken cancellationToken) =>
        InWorld(await relations.ObserversOfAsync(character, CharacterRelationKind.Friend, cancellationToken).ConfigureAwait(false),
            character, sessions);

    /// <summary>Those of <paramref name="observers"/> in the world, never <paramref name="character"/> itself.</summary>
    public static IReadOnlyList<CharacterId> InWorld(IEnumerable<CharacterId> observers, CharacterId character,
        ISelectedSessionDomainService sessions) =>
        [.. observers.Where(observer => observer != character && sessions.TryGetInWorld(observer, out _)).Distinct()];
}

/// <summary>What a session cleanup has to project for the friends of the character that left.</summary>
/// <remarks>
/// A cleanup plan is delivered from three places -- disconnect, return to selection and a selection
/// that replaces an older session -- and each of them must tell the friends, or they keep showing the
/// character online. Each place projects this after its conversation notices, so the rule lives here once.
/// </remarks>
internal static class FriendDepartureNotices
{
    public static IEnumerable<(NotificationAudience Audience, FriendWentOffline Notification)> For(
        SelectedSessionCleanupNotificationPlan plan) =>
        plan.FriendDeparture is { Observers.Count: > 0 } departure
            ? [(new NotificationAudience.TheseCharacters(departure.Observers), new FriendWentOffline(departure.Character, departure.Name))]
            : [];
}

/// <summary>Sends a character's observers their lists again after its level or grow type changed (Spec RQ-052).</summary>
/// <remarks>
/// Live only: a refresh that missed its moment is worth nothing, because the next login sends the
/// real list anyway. Coalesced per character, so the levels one award grants in one batch refresh once.
/// </remarks>
internal sealed class FriendObserverRefreshSubscriber(ICharacterRelationQueryDomainService relations,
    ISelectedSessionDomainService sessions, IEventService events, TimeProvider clock)
    : IDomainEventSubscriber<CharacterLevelChanged>, IDomainEventSubscriber<CharacterGrowTypeChanged>
{
    public ValueTask HandleAsync(DomainEventEnvelope<CharacterLevelChanged> message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return RefreshAsync(message.Payload.CharacterId, cancellationToken);
    }

    public ValueTask HandleAsync(DomainEventEnvelope<CharacterGrowTypeChanged> message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return RefreshAsync(message.Payload.CharacterId, cancellationToken);
    }

    /// <remarks>
    /// Runs after the change committed, outside any business transaction, so the observers and every
    /// list are read as they stand now: two refreshes in a row leave the later state last (RQ-055).
    /// </remarks>
    private async ValueTask RefreshAsync(CharacterId character, CancellationToken cancellationToken)
    {
        var observers = await FriendObservers.InWorldAsync(character, relations, sessions, cancellationToken).ConfigureAwait(false);
        if (observers.Count == 0) return;
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters(observers),
            new FriendListChanged(clock.LocalTimeZone), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The coalescing key both registrations use: the character, so one refresh serves one batch's changes.</summary>
    public static string KeyOf(CharacterId character) => character.Value.ToString(CultureInfo.InvariantCulture);
}
