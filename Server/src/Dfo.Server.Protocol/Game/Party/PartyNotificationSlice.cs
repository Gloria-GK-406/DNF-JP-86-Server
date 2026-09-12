using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Party;

public sealed record PartyInviteRequestMessage(
    ushort InviterWireUserId,
    PartyInviteDisplayState Display)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePartyInviteRequest;
}

public sealed record PartyInviteAcceptedMessage(
    ushort InviteeWireUserId,
    ushort InviterWireUserId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePartyInviteAccepted;
}

public sealed record PartyRealtimeMessage(PartySnapshot Snapshot) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePartyRealtime;
}

public sealed record PartyEndpointsMessage(PartySnapshot Snapshot) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePartyEndpoints;
}

public sealed record PartySettingsMessage(PartySnapshot Snapshot) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePartySettings;
}

public sealed record PartyRosterMessage : IGameNotiMessage
{
    private PartyRosterMessage(PartySnapshot? snapshot, ushort? clearedPartyId)
    {
        Snapshot = snapshot;
        ClearedPartyId = clearedPartyId;
    }

    public PartySnapshot? Snapshot { get; }

    public ushort? ClearedPartyId { get; }

    public ServerMessageType MessageType => ServerMessageType.GamePartyRoster;

    public static PartyRosterMessage Refresh(PartySnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static PartyRosterMessage Clear(ushort partyId) => new(null, partyId);
}

internal sealed class PartyInviteRequestEncoder
    : GameNotiMessageEncoder<PartyInviteRequestMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        PartyInviteRequestMessage message) =>
        OnePayload(GameProtocol.PartyInviteRequestNotificationType, EncodeBody(message));

    internal static byte[] EncodeBody(PartyInviteRequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.Display);
        if (message.Display.ReviveCoinCount < 0)
            throw new InvalidDataException("Party invitation revive-coin count cannot be negative.");
        var body = new byte[13];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.InviterWireUserId);
        body[2] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(
            body.AsSpan(3), message.Display.ReviveCoinCount);
        BinaryPrimitives.WriteUInt16LittleEndian(
            body.AsSpan(7), message.Display.RemainingFatigue);
        BinaryPrimitives.WriteUInt16LittleEndian(
            body.AsSpan(9), message.Display.Stamina);
        BinaryPrimitives.WriteUInt16LittleEndian(
            body.AsSpan(11), message.Display.AntiMagic);
        return body;
    }
}

internal sealed class PartyInviteAcceptedEncoder
    : GameNotiMessageEncoder<PartyInviteAcceptedMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        PartyInviteAcceptedMessage message) =>
        OnePayload(GameProtocol.PartyInviteAcceptedNotificationType, EncodeBody(message));

    internal static byte[] EncodeBody(PartyInviteAcceptedMessage message)
    {
        var body = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.InviteeWireUserId);
        body[2] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), message.InviterWireUserId);
        return body;
    }
}

internal sealed class PartyRealtimeEncoder : GameNotiMessageEncoder<PartyRealtimeMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        PartyRealtimeMessage message) =>
        OnePayload(GameProtocol.PartyRealtimeNotificationType, EncodeBody(message));

    internal static byte[] EncodeBody(PartyRealtimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var members = message.Snapshot.Members;
        var body = new byte[1 + (members.Length * 5)];
        body[0] = checked((byte)members.Length);
        for (var index = 0; index < members.Length; index++)
        {
            var offset = 1 + (index * 5);
            BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(offset), members[index].Session.WireUserId);
            body[offset + 2] = members[index].HpPercent;
            body[offset + 3] = 0;
            body[offset + 4] = members[index].Slot;
        }

        return body;
    }
}

internal sealed class PartyEndpointsEncoder : GameNotiMessageEncoder<PartyEndpointsMessage>
{
    // A21's party endpoint consumer immediately constructs its P2P peer state. The legacy
    // server therefore never emits 0.0.0.0:0 when SET_UDP_IP_PORT has not arrived: it uses
    // loopback and the client's conventional base port until a real report replaces them.
    private static readonly PartyEndpoint UnknownEndpoint = new(
        NatType: 0,
        InnerAddress: new PartyIpv4Address(127, 0, 0, 1),
        OuterAddress: new PartyIpv4Address(127, 0, 0, 1),
        Port: 10000,
        Mtu: 1500);

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        PartyEndpointsMessage message) =>
        OnePayload(GameProtocol.PartyEndpointsNotificationType, EncodeBody(message));

    internal static byte[] EncodeBody(PartyEndpointsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var members = message.Snapshot.Members;
        var body = new byte[1 + (members.Length * 22)];
        body[0] = checked((byte)members.Length);
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            var endpoint = member.Endpoint ?? UnknownEndpoint;
            var offset = 1 + (index * 22);
            BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(offset), member.Session.WireUserId);
            WriteAddress(body.AsSpan(offset + 2), endpoint.InnerAddress);
            WriteAddress(body.AsSpan(offset + 6), endpoint.OuterAddress);
            body[offset + 10] = checked((byte)(endpoint.Port >> 8));
            body[offset + 11] = (byte)(endpoint.Port & byte.MaxValue);

            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(offset + 12), checked((uint)member.Session.CharacterId.Value));
            body[offset + 16] = endpoint.NatType;
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(offset + 17), endpoint.Mtu);
            body[offset + 21] = 0;
        }

        return body;
    }

    private static void WriteAddress(Span<byte> destination, PartyIpv4Address address)
    {
        destination[0] = address.A;
        destination[1] = address.B;
        destination[2] = address.C;
        destination[3] = address.D;
    }
}

internal sealed class PartyRosterEncoder : GameNotiMessageEncoder<PartyRosterMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        PartyRosterMessage message) =>
        OnePayload(GameProtocol.PartyRosterNotificationType, EncodeBody(message));

    internal static byte[] EncodeBody(PartyRosterMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.ClearedPartyId is { } clearedPartyId)
        {
            var clear = new byte[5];
            BinaryPrimitives.WriteUInt16LittleEndian(clear, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(clear.AsSpan(2), clearedPartyId);
            clear[4] = 3;
            return clear;
        }

        var snapshot = message.Snapshot
            ?? throw new InvalidOperationException("A party roster refresh requires a snapshot.");
        var information = PartyInformationBlockEncoder.Encode(snapshot.Settings, snapshot.Title);
        var rosterOffset = 5 + information.Length;
        var body = new byte[rosterOffset + 44];
        BinaryPrimitives.WriteUInt16LittleEndian(body, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), snapshot.PartyId);
        body[4] = 0;
        information.CopyTo(body.AsSpan(5));

        for (byte slot = 0; slot < 8; slot++)
        {
            var member = snapshot.Members.FirstOrDefault(value => value.Slot == slot);
            BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(rosterOffset + (slot * 5)),
                member?.Session.WireUserId ?? ushort.MaxValue);
        }

        body[rosterOffset + 41] = snapshot.Members.FirstOrDefault(
            value => value.Session.WireUserId == snapshot.LeaderWireUserId)?.Slot ?? 0;
        // The three roster trailers and the final has-extra byte otherwise remain zero.
        return body;
    }
}

internal sealed class PartySettingsEncoder : GameNotiMessageEncoder<PartySettingsMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        PartySettingsMessage message) =>
        OnePayload(GameProtocol.PartyRosterNotificationType, EncodeBody(message));

    internal static byte[] EncodeBody(PartySettingsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var information = PartyInformationBlockEncoder.Encode(
            message.Snapshot.Settings,
            message.Snapshot.Title);
        var body = new byte[5 + information.Length + 1];
        BinaryPrimitives.WriteUInt16LittleEndian(body, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), message.Snapshot.PartyId);
        body[4] = 1;
        information.CopyTo(body.AsSpan(5));
        // The final has-extra byte remains zero: no u32 extension records follow.
        return body;
    }
}

internal static class PartyInformationBlockEncoder
{
    public static byte[] Encode(PartySettings settings, PartyTitle title)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(title);
        var canonical = settings.ToWireBytes();
        if (canonical[0] != 0)
            return canonical;

        var titleBytes = title.ToBytes();
        var encoded = new byte[checked(canonical.Length + sizeof(int) + titleBytes.Length)];
        encoded[0] = canonical[0];
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(1), titleBytes.Length);
        titleBytes.CopyTo(encoded.AsSpan(1 + sizeof(int)));
        canonical.AsSpan(1).CopyTo(encoded.AsSpan(1 + sizeof(int) + titleBytes.Length));
        return encoded;
    }
}

public sealed record PartyInviteIssued(PartyInviteNotification Invitation)
    : INotification<PartyInviteIssued>;

public sealed record PartyRealtimeChanged(PartySnapshot Snapshot)
    : INotification<PartyRealtimeChanged>;

public sealed record PartyInviteAccepted(PartyAcceptanceNotification Acceptance)
    : INotification<PartyInviteAccepted>;

public sealed record PartyEndpointsChanged(PartySnapshot Snapshot)
    : INotification<PartyEndpointsChanged>;

public sealed record PartySettingsChanged(PartySnapshot Snapshot)
    : INotification<PartySettingsChanged>;

public sealed record PartyRosterChanged(
    PartySnapshot? Snapshot,
    ImmutableArray<SelectedSessionIdentity> ClearAudience,
    ushort? ClearPartyId) : INotification<PartyRosterChanged>
{
    public bool ClearsParty => Snapshot is null;

    public static PartyRosterChanged Refresh(PartySnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), [], null);

    public static PartyRosterChanged Clear(
        ImmutableArray<SelectedSessionIdentity> audience,
        ushort partyId) => new(null, audience, partyId);
}

internal static class PartyNotificationProjection
{
    public static bool IsExact(
        NotificationRecipient recipient,
        SelectedSessionIdentity session,
        ISelectedSessionDomainService sessions) =>
        recipient.CharacterId == session.CharacterId
        && recipient.ConnectionId == session.ConnectionId
        && sessions.Find(session.AccountId) == session;

    public static bool IsExactMember(
        NotificationRecipient recipient,
        PartySnapshot snapshot,
        ISelectedSessionDomainService sessions) =>
        snapshot.Members.Any(member => IsExact(recipient, member.Session, sessions));
}

public sealed class PartyInviteIssuedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<PartyInviteIssued>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        PartyInviteIssued notification,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            PartyNotificationProjection.IsExact(
                recipient, notification.Invitation.Target, sessions)
                ? [new PartyInviteRequestMessage(
                    notification.Invitation.Inviter.WireUserId,
                    notification.Invitation.Display)]
                : []);
}

public sealed class PartyRealtimeChangedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<PartyRealtimeChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        PartyRealtimeChanged notification,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            PartyNotificationProjection.IsExactMember(recipient, notification.Snapshot, sessions)
                ? [new PartyRealtimeMessage(notification.Snapshot)]
                : []);
}

public sealed class PartyInviteAcceptedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<PartyInviteAccepted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        PartyInviteAccepted notification,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            PartyNotificationProjection.IsExact(
                recipient, notification.Acceptance.Inviter, sessions)
                ? [new PartyInviteAcceptedMessage(
                    notification.Acceptance.Invitee.WireUserId,
                    notification.Acceptance.Inviter.WireUserId)]
                : []);
}

public sealed class PartyEndpointsChangedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<PartyEndpointsChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        PartyEndpointsChanged notification,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            PartyNotificationProjection.IsExactMember(recipient, notification.Snapshot, sessions)
                ? [new PartyEndpointsMessage(notification.Snapshot)]
                : []);
}

public sealed class PartyRosterChangedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<PartyRosterChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        PartyRosterChanged notification,
        CancellationToken cancellationToken)
    {
        if (notification.Snapshot is { } snapshot)
        {
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
                PartyNotificationProjection.IsExactMember(recipient, snapshot, sessions)
                    ? [PartyRosterMessage.Refresh(snapshot)]
                    : []);
        }

        if (notification.ClearPartyId is not { } partyId)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var exact = notification.ClearAudience.Any(session =>
            PartyNotificationProjection.IsExact(recipient, session, sessions));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            exact ? [PartyRosterMessage.Clear(partyId)] : []);
    }
}

public sealed class PartySettingsChangedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<PartySettingsChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        PartySettingsChanged notification,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            PartyNotificationProjection.IsExactMember(recipient, notification.Snapshot, sessions)
                ? [new PartySettingsMessage(notification.Snapshot)]
                : []);
}

public sealed class PartyNotificationDelivery(INotificationBus notifications)
{
    private readonly ConcurrentDictionary<long, PartyDeliveryStream> partyStreams = [];

    public async ValueTask DeliverAsync(
        PartyNotificationPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidatePlanShape(plan);
        if (plan.Invitation is { } invitation)
        {
            await notifications.PublishAsync(
                Audience([invitation.Target]),
                new PartyInviteIssued(invitation),
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryGetPartyVersion(plan, out var generation, out var revision))
            return;

        var stream = partyStreams.GetOrAdd(generation, static _ => new PartyDeliveryStream());
        await stream.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Commands release their selected-session gates before transport work begins. A later
            // revision can therefore reach this singleton before an earlier caller, or while it is
            // between phases. One stream per immutable party generation keeps each four-phase
            // projection contiguous. A late older caller cannot replay its snapshot, but its
            // recipient-specific clear or acceptance acknowledgement must still be delivered.
            if (revision <= stream.LatestRevision)
            {
                var staleClearAudience = plan.ClearAudience
                    .Where(session => !stream.CurrentMembers.Contains(session))
                    .ToImmutableArray();
                await DeliverStaleSideEffectsAsync(
                    plan,
                    staleClearAudience,
                    stream.CurrentMembers,
                    cancellationToken).ConfigureAwait(false);
                stream.FullStateMembers.ExceptWith(staleClearAudience);
                return;
            }
            stream.LatestRevision = revision;
            var promoteSettingsUpdate = plan.SettingsChange == PartySettingsChangeKind.Updated
                && plan.Snapshot!.Members.Any(
                    member => !stream.FullStateMembers.Contains(member.Session));
            await DeliverPartyStateAsync(plan, promoteSettingsUpdate, cancellationToken)
                .ConfigureAwait(false);
            if (plan.Snapshot is { } latestSnapshot)
            {
                stream.CurrentMembers.Clear();
                stream.CurrentMembers.UnionWith(
                    latestSnapshot.Members.Select(member => member.Session));
            }
            else
            {
                stream.CurrentMembers.ExceptWith(plan.ClearAudience);
            }
            stream.FullStateMembers.ExceptWith(plan.ClearAudience);
            if (plan.Snapshot is not null
                && (plan.SettingsChange != PartySettingsChangeKind.Updated || promoteSettingsUpdate))
            {
                stream.FullStateMembers.UnionWith(
                    plan.Snapshot.Members.Select(member => member.Session));
            }
        }
        finally
        {
            stream.Gate.Release();
        }
    }

    /// <summary>
    /// Answers one member's explicit realtime query without broadcasting another refresh to the
    /// whole party. The shared generation stream keeps a delayed old query behind or below a newer
    /// roster revision, while equal revisions remain answerable because HP can change independently
    /// of party membership.
    /// </summary>
    public async ValueTask DeliverRealtimeAsync(
        SelectedSessionIdentity requester,
        PartySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(snapshot);
        var stream = partyStreams.GetOrAdd(
            snapshot.Generation, static _ => new PartyDeliveryStream());
        await stream.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (snapshot.Revision < stream.LatestRevision)
                return;
            stream.LatestRevision = Math.Max(stream.LatestRevision, snapshot.Revision);
            await notifications.PublishAsync(
                Audience([requester]),
                new PartyRealtimeChanged(snapshot),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stream.Gate.Release();
        }
    }

    private async ValueTask DeliverPartyStateAsync(
        PartyNotificationPlan plan,
        bool promoteSettingsUpdate,
        CancellationToken cancellationToken)
    {

        if (plan.SettingsChange == PartySettingsChangeKind.Updated && !promoteSettingsUpdate)
        {
            var settingsSnapshot = plan.Snapshot!;
            await notifications.PublishAsync(
                Audience(settingsSnapshot.Members.Select(value => value.Session)),
                new PartySettingsChanged(settingsSnapshot),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await DeliverClearAsync(
            plan.ClearAudience,
            plan.ClearPartyId,
            cancellationToken).ConfigureAwait(false);

        if (plan.Snapshot is not { } snapshot)
            return;

        var memberAudience = Audience(snapshot.Members.Select(value => value.Session));
        await notifications.PublishAsync(
            memberAudience,
            new PartyRealtimeChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
        if (plan.Acceptance is { } acceptance)
        {
            await DeliverAcceptanceAsync(acceptance, cancellationToken).ConfigureAwait(false);
        }
        await notifications.PublishAsync(
            memberAudience,
            new PartyEndpointsChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
        await notifications.PublishAsync(
            memberAudience,
            PartyRosterChanged.Refresh(snapshot),
            cancellationToken).ConfigureAwait(false);
        if (plan.Acceptance is not null)
        {
            // A21 drops the first realtime summary when it arrives before the party slots exist.
            // Replay the same immutable snapshot after the full roster so both clients can bind
            // the current HP without relying on an optional per-client 0x00A6 follow-up query.
            await notifications.PublishAsync(
                memberAudience,
                new PartyRealtimeChanged(snapshot),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask DeliverStaleSideEffectsAsync(
        PartyNotificationPlan plan,
        ImmutableArray<SelectedSessionIdentity> staleClearAudience,
        HashSet<SelectedSessionIdentity> currentMembers,
        CancellationToken cancellationToken)
    {
        await DeliverClearAsync(
            staleClearAudience,
            plan.ClearPartyId,
            cancellationToken).ConfigureAwait(false);
        if (plan.Acceptance is { } acceptance
            && currentMembers.Contains(acceptance.Inviter)
            && currentMembers.Contains(acceptance.Invitee))
        {
            await DeliverAcceptanceAsync(acceptance, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask DeliverClearAsync(
        ImmutableArray<SelectedSessionIdentity> clearAudience,
        ushort? clearPartyId,
        CancellationToken cancellationToken)
    {
        if (clearAudience.IsDefaultOrEmpty)
            return;

        var partyId = clearPartyId
            ?? throw new InvalidOperationException("A party clear requires its party ID.");
        await notifications.PublishAsync(
            Audience(clearAudience),
            PartyRosterChanged.Clear(clearAudience, partyId),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DeliverAcceptanceAsync(
        PartyAcceptanceNotification acceptance,
        CancellationToken cancellationToken) =>
        await notifications.PublishAsync(
            Audience([acceptance.Inviter]),
            new PartyInviteAccepted(acceptance),
            cancellationToken).ConfigureAwait(false);

    private static void ValidatePlanShape(PartyNotificationPlan plan)
    {
        var settingsChange = plan.SettingsChange;
        if (settingsChange is not null
            && settingsChange is not PartySettingsChangeKind.Created
                and not PartySettingsChangeKind.Updated)
        {
            throw new InvalidOperationException("A party plan contains an unknown settings change kind.");
        }

        var hasClearAudience = !plan.ClearAudience.IsDefaultOrEmpty;
        var hasAllClearMetadata = plan.ClearPartyId is not null
            && plan.ClearPartyGeneration is not null
            && plan.ClearPartyRevision is not null;
        var hasAnyClearMetadata = plan.ClearPartyId is not null
            || plan.ClearPartyGeneration is not null
            || plan.ClearPartyRevision is not null;
        if ((hasClearAudience && !hasAllClearMetadata)
            || (!hasClearAudience && hasAnyClearMetadata))
        {
            throw new InvalidOperationException(
                "A party clear requires an audience, party ID, generation, and revision together.");
        }

        if (plan.Invitation is not null
            && (plan.Snapshot is not null
                || plan.Acceptance is not null
                || hasClearAudience
                || settingsChange is not null))
        {
            throw new InvalidOperationException("A party invitation plan cannot contain another operation.");
        }

        if (plan.Acceptance is not null
            && (plan.Snapshot is null || hasClearAudience || settingsChange is not null))
        {
            throw new InvalidOperationException(
                "A party acceptance requires only its resulting snapshot.");
        }

        if (settingsChange is not null
            && (plan.Snapshot is null
                || plan.Invitation is not null
                || plan.Acceptance is not null
                || hasClearAudience))
        {
            throw new InvalidOperationException(
                "A party settings change requires only its resulting snapshot.");
        }
    }

    private static bool TryGetPartyVersion(
        PartyNotificationPlan plan,
        out long generation,
        out long revision)
    {
        if (plan.Snapshot is { } snapshot)
        {
            generation = snapshot.Generation;
            revision = snapshot.Revision;
            if (!plan.ClearAudience.IsDefaultOrEmpty
                && (plan.ClearPartyGeneration != generation
                    || plan.ClearPartyRevision != revision))
            {
                throw new InvalidOperationException(
                    "A party clear and survivor refresh must describe one party revision.");
            }
            return true;
        }

        if (!plan.ClearAudience.IsDefaultOrEmpty)
        {
            generation = plan.ClearPartyGeneration
                ?? throw new InvalidOperationException("A party clear requires its generation.");
            revision = plan.ClearPartyRevision
                ?? throw new InvalidOperationException("A party clear requires its revision.");
            return true;
        }

        generation = 0;
        revision = 0;
        return false;
    }

    private static NotificationAudience Audience(IEnumerable<SelectedSessionIdentity> sessions) =>
        new NotificationAudience.TheseCharacters(
            sessions.Select(value => value.CharacterId).ToArray());

    private sealed class PartyDeliveryStream
    {
        // Streams intentionally retain their final revision as a tombstone: a delayed caller from
        // a disbanded generation must never resurrect its older roster after the clear.
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public long LatestRevision { get; set; }

        public HashSet<SelectedSessionIdentity> CurrentMembers { get; } = [];

        public HashSet<SelectedSessionIdentity> FullStateMembers { get; } = [];
    }
}
