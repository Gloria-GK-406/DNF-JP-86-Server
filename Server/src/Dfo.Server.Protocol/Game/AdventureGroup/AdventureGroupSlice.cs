using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.AdventureGroup;

/// <summary>NOTI 0x01A0 <c>MANAGE_LEVEL_UP</c>: the account's adventure group rose one level.</summary>
/// <remarks>
/// The body is empty, as in the 70 server (70Reference); A21's handler sits in the packed region,
/// so its own layout is not known statically. The client's handler (<c>0x014E5C50</c> →
/// <c>0x014E5AF0</c>) adds one to its adventure-group level at <c>0x014E5B78</c> and plays the
/// level-up text and animation (ClientStatic). One frame is one level, so a rise of several levels
/// is sent as that many frames.
/// </remarks>
public sealed record AdventureGroupLevelUpNotice : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAdventureGroupLevelUp;
}

internal sealed class AdventureGroupLevelUpNoticeEncoder : GameNotiMessageEncoder<AdventureGroupLevelUpNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AdventureGroupLevelUpNotice message) =>
        OnePayload(message, Array.Empty<byte>());
}

/// <summary>The adventure group rose by <paramref name="Levels"/> while this session was in the world.</summary>
internal sealed record AdventureGroupLevelUp(SelectedSessionIdentity Owner, int Levels)
    : INotification<AdventureGroupLevelUp>;

/// <summary>
/// Tells the account's playing session when a character's level change raised its adventure group.
/// </summary>
/// <remarks>
/// Subscribed live-only: the consumption is committed before this runs and is never replayed, a
/// change from an earlier process is skipped, and no coalescing key merges two changes. So each
/// level change is told at most once, which is what keeps the client -- which adds one per frame --
/// from counting a rise twice; nothing about the adventure group needs to be stored for it. A rise
/// the session misses (not in the world, or the hint expired) is set right by the absolute values
/// USERINFO 1 and 0x01BA carry the next time the client enters the world.
/// </remarks>
internal sealed class AdventureGroupLevelChangedSubscriber(
    IAdventureGroupDomainService adventureGroups,
    ISelectedSessionDomainService sessions,
    IEventService events) : IDomainEventSubscriber<CharacterLevelChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<CharacterLevelChanged> message, CancellationToken ct)
    {
        if (await adventureGroups.LevelRiseAsync(message.Payload, ct).ConfigureAwait(false) is not { } rise)
            return;
        if (sessions.Find(rise.AccountId) is not { } owner || !sessions.IsInWorld(owner))
            return;

        await events.ProjectAsync(new NotificationAudience.TheseSessions([owner]),
            new AdventureGroupLevelUp(owner, rise.Levels), ct).ConfigureAwait(false);
    }
}

/// <summary>One empty 0x01A0 per level risen, to the session the rise was meant for.</summary>
internal sealed class AdventureGroupLevelUpHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<AdventureGroupLevelUp>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, AdventureGroupLevelUp notification, CancellationToken ct)
    {
        var owner = notification.Owner;
        if (recipient.CharacterId != owner.CharacterId || recipient.ConnectionId != owner.ConnectionId
            || !sessions.IsInWorld(owner) || notification.Levels <= 0)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);

        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            Enumerable.Repeat<IServerNetworkMessage>(new AdventureGroupLevelUpNotice(), notification.Levels).ToArray());
    }
}
