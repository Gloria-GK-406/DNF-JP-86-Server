using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
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
                body.AsSpan(offset), members[index].WireUserId);
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
    private static readonly SessionEndpoint UnknownEndpoint = new(
        NatType: 0,
        InnerAddress: new SessionIpv4Address(127, 0, 0, 1),
        OuterAddress: new SessionIpv4Address(127, 0, 0, 1),
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
                body.AsSpan(offset), member.WireUserId);
            WriteAddress(body.AsSpan(offset + 2), endpoint.InnerAddress);
            WriteAddress(body.AsSpan(offset + 6), endpoint.OuterAddress);
            // A21 consumes this socket field in network order. The handler's later htons call
            // does not by itself prove a little-endian wire value; live formation regresses when
            // these two bytes are reversed while the client-reported endpoint remains unchanged.
            body[offset + 10] = checked((byte)(endpoint.Port >> 8));
            body[offset + 11] = (byte)(endpoint.Port & byte.MaxValue);

            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(offset + 12), checked((uint)member.CharacterId.Value));
            body[offset + 16] = endpoint.NatType;
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(offset + 17), endpoint.Mtu);
            body[offset + 21] = 0;
        }

        return body;
    }

    private static void WriteAddress(Span<byte> destination, SessionIpv4Address address)
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
                member?.WireUserId ?? ushort.MaxValue);
        }

        body[rosterOffset + 41] = snapshot.Members.FirstOrDefault(
            value => value.WireUserId == snapshot.LeaderWireUserId)?.Slot ?? 0;
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
    ImmutableArray<CharacterId> ExactAudience,
    ushort? ClearPartyId) : INotification<PartyRosterChanged>
{
    public bool ClearsParty => Snapshot is null;

    public static PartyRosterChanged Refresh(
        PartySnapshot snapshot,
        ImmutableArray<CharacterId> audience) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), audience, null);

    public static PartyRosterChanged Clear(
        ImmutableArray<CharacterId> audience,
        ushort partyId) => new(null, audience, partyId);
}

internal static class PartyNotificationProjection
{
    public static bool IsExact(
        NotificationRecipient recipient,
        CharacterId characterId,
        ISelectedSessionDomainService sessions) =>
        sessions.FindByCharacter(characterId) is { } session
        && recipient.CharacterId == characterId
        && recipient.ConnectionId == session.ConnectionId;

    public static bool IsExactMember(
        NotificationRecipient recipient,
        PartySnapshot snapshot,
        ISelectedSessionDomainService sessions) =>
        snapshot.Members.Any(member => IsExact(recipient, member.CharacterId, sessions));
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
                recipient, notification.Invitation.TargetCharacterId, sessions)
                ? [new PartyInviteRequestMessage(
                    notification.Invitation.InviterWireUserId,
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
                recipient, notification.Acceptance.InviterCharacterId, sessions)
                ? [new PartyInviteAcceptedMessage(
                    notification.Acceptance.InviteeWireUserId,
                    notification.Acceptance.InviterWireUserId)]
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
            var refreshExact = notification.ExactAudience.Any(characterId =>
                PartyNotificationProjection.IsExact(recipient, characterId, sessions));
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
                refreshExact
                    ? [PartyRosterMessage.Refresh(snapshot)]
                    : []);
        }

        if (notification.ClearPartyId is not { } partyId)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var clearExact = notification.ExactAudience.Any(characterId =>
            PartyNotificationProjection.IsExact(recipient, characterId, sessions));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            clearExact ? [PartyRosterMessage.Clear(partyId)] : []);
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

public sealed class PartyProjectionStreamRegistry
{
    private readonly ConcurrentDictionary<long, PartyDeliveryStream> streams = [];
    // A character can cross from one party generation to another. Generation gates cannot order
    // that transition by themselves, so every full mutation/read-repair also owns the affected
    // character gates in stable order until its state-shaped sends finish.
    private readonly ConcurrentDictionary<int, SemaphoreSlim> participantGates = [];

    internal PartyDeliveryStream GetOrAdd(long generation) =>
        streams.GetOrAdd(generation, static _ => new PartyDeliveryStream());

    internal async ValueTask<IDisposable> EnterParticipantsAsync(
        IEnumerable<SelectedSessionIdentity> participants,
        CancellationToken cancellationToken)
    {
        var gates = participants
            .Select(participant => participant.CharacterId.Value)
            .Distinct()
            .Order()
            .Select(characterId => participantGates.GetOrAdd(
                characterId, static _ => new SemaphoreSlim(1, 1)))
            .ToArray();
        var acquired = 0;
        try
        {
            for (; acquired < gates.Length; acquired++)
                await gates[acquired].WaitAsync(cancellationToken).ConfigureAwait(false);
            return new PartyParticipantLease(gates);
        }
        catch
        {
            for (var index = acquired - 1; index >= 0; index--)
                gates[index].Release();
            throw;
        }
    }
}

internal sealed class PartyParticipantLease(SemaphoreSlim[] gates) : IDisposable
{
    private SemaphoreSlim[]? held = gates;

    public void Dispose()
    {
        var releasing = Interlocked.Exchange(ref held, null);
        if (releasing is null)
            return;
        for (var index = releasing.Length - 1; index >= 0; index--)
            releasing[index].Release();
    }
}

internal sealed class PartyDeliveryStream
{
    // Streams intentionally retain their final revision as a tombstone: a delayed caller from
    // a disbanded generation must never resurrect its older roster after the clear.
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public long LatestRevision { get; set; }

    // The wire identity currently projected to this generation. A town read-repair may
    // advance a roster revision, but it must not consume a pending leader rekey without
    // retiring the old client object first.
    public ushort? CurrentPartyId { get; set; }

    public HashSet<SelectedSessionIdentity> CurrentMembers { get; } = [];

    public HashSet<SelectedSessionIdentity> FullStateMembers { get; } = [];
}

internal static class PartyNotificationRecipientCapture
{
    public static PartyNotificationRecipientPlan Capture(
        PartyNotificationPlan plan,
        ISelectedSessionDomainService? sessions,
        ITownAreaDomainService? townAreas)
    {
        if (sessions is null)
            return PartyNotificationRecipientPlan.Empty;

        var characterIds = new HashSet<CharacterId>(plan.ClearAudience);
        if (plan.Snapshot is { } snapshot)
            characterIds.UnionWith(snapshot.Members.Select(member => member.CharacterId));
        if (plan.Invitation is { } invitation)
            characterIds.Add(invitation.TargetCharacterId);
        if (plan.Acceptance is { } acceptance)
        {
            characterIds.Add(acceptance.InviterCharacterId);
            characterIds.Add(acceptance.InviteeCharacterId);
        }

        var characterRecipients = characterIds
            .Select(sessions.FindByCharacter)
            .Where(candidate => candidate is not null)
            .Cast<SelectedSessionIdentity>()
            .Distinct()
            .ToArray();
        if (townAreas is null)
            return new PartyNotificationRecipientPlan(characterRecipients, []);

        var directoryRecipients = new[]
            {
                plan.DirectoryChange?.Previous,
                plan.DirectoryChange?.Current,
            }
            .Where(scope => scope is not null)
            .Cast<PartyDirectoryScope>()
            .Distinct()
            .Select(scope => new PartyDirectoryRecipientGroup(
                scope,
                (townAreas.Find(new TownAreaKey(
                         scope.ChannelId,
                         scope.TownId,
                         scope.AreaId))?.Members ?? [])
                    .Where(item => item.SharesTownPresence
                        && sessions.FindByCharacter(item.CharacterId) is { } exact
                        && exact.Owns(item))
                    .Select(item => sessions.FindByCharacter(item.CharacterId)!)
                    .Distinct()
                    .ToArray()))
            .ToArray();
        return new PartyNotificationRecipientPlan(characterRecipients, directoryRecipients);
    }
}

internal sealed class PartyNotificationRecipientResolver(PartyNotificationRecipientPlan plan)
{
    private readonly IReadOnlyDictionary<CharacterId, SelectedSessionIdentity> byCharacter =
        plan.CharacterRecipients
            .Distinct()
            .GroupBy(session => session.CharacterId)
            .ToDictionary(group => group.Key, group => group.First());
    private readonly IReadOnlyDictionary<PartyDirectoryScope, IReadOnlyList<SelectedSessionIdentity>> byScope =
        plan.DirectoryRecipients
            .GroupBy(group => group.Scope)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SelectedSessionIdentity>)group
                    .SelectMany(item => item.Sessions)
                    .Distinct()
                    .ToArray());

    public ImmutableArray<SelectedSessionIdentity> Characters(IEnumerable<CharacterId> characterIds) =>
        characterIds
            .Distinct()
            .Select(characterId => byCharacter.GetValueOrDefault(characterId))
            .Where(session => session is not null)
            .Cast<SelectedSessionIdentity>()
            .ToImmutableArray();

    public SelectedSessionIdentity? Character(CharacterId characterId) =>
        byCharacter.GetValueOrDefault(characterId);

    public IEnumerable<SelectedSessionIdentity> Scope(PartyDirectoryScope? scope) =>
        scope is not null && byScope.TryGetValue(scope, out var recipients)
            ? recipients
            : [];
}

public sealed class PartyNotificationDelivery(
    INotificationBus notifications,
    PartyProjectionStreamRegistry streams,
    ISelectedSessionDomainService? sessions = null,
    ITownAreaDomainService? townAreas = null,
    IPartyService? parties = null)
{
    internal PartyNotificationDelivery(INotificationBus notifications)
        : this(notifications, new PartyProjectionStreamRegistry(), null, null, null)
    {
    }

    public async ValueTask DeliverAsync(
        PartyNotificationPlan plan,
        CancellationToken cancellationToken)
    {
        var recipients = PartyNotificationRecipientCapture.Capture(plan, sessions, townAreas);
        await DeliverAsync(plan, recipients, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeliverAsync(
        PartyNotificationPlan plan,
        PartyNotificationRecipientPlan recipients,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(recipients);
        ValidatePlanShape(plan);
        var resolver = new PartyNotificationRecipientResolver(recipients);
        if (plan.Invitation is { } invitation)
        {
            var target = resolver.Characters([invitation.TargetCharacterId]);
            await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications,
                target,
                new PartyInviteIssued(invitation),
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryGetPartyVersion(plan, out var generation, out var revision))
            return;

        var clearAudience = resolver.Characters(plan.ClearAudience);
        using var participantLease = await streams.EnterParticipantsAsync(
            PlanParticipants(plan, clearAudience, resolver), cancellationToken).ConfigureAwait(false);
        var stream = streams.GetOrAdd(generation);
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
                var staleClearAudience = clearAudience
                    .Where(session => !stream.CurrentMembers.Contains(session))
                    .ToImmutableArray();
                var delivered = await DeliverStaleSideEffectsAsync(
                    plan,
                    staleClearAudience,
                    stream.CurrentMembers,
                    resolver,
                    cancellationToken).ConfigureAwait(false);
                if (delivered)
                    stream.FullStateMembers.ExceptWith(staleClearAudience);
                return;
            }
            var promoteSettingsUpdate = plan.SettingsChange == PartySettingsChangeKind.Updated
                && plan.Snapshot!.Members.Any(member =>
                    resolver.Character(member.CharacterId) is not { } current
                    || !stream.FullStateMembers.Contains(current));
            var stateDelivered = await DeliverPartyStateAsync(
                plan, promoteSettingsUpdate, resolver, cancellationToken).ConfigureAwait(false);
            if (!stateDelivered)
                return;

            stream.LatestRevision = revision;
            stream.CurrentPartyId = plan.Snapshot?.PartyId;
            if (plan.Snapshot is { } latestSnapshot)
            {
                stream.CurrentMembers.Clear();
                stream.CurrentMembers.UnionWith(
                    resolver.Characters(latestSnapshot.Members.Select(member => member.CharacterId)));
            }
            else
            {
                stream.CurrentMembers.ExceptWith(clearAudience);
            }
            stream.FullStateMembers.ExceptWith(clearAudience);
            if (plan.Snapshot is not null
                && (plan.SettingsChange != PartySettingsChangeKind.Updated || promoteSettingsUpdate))
            {
                stream.FullStateMembers.UnionWith(
                    resolver.Characters(plan.Snapshot.Members.Select(member => member.CharacterId)));
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
    /// of party membership. A requester-only answer never advances the full-mutation watermark.
    /// </summary>
    public async ValueTask DeliverRealtimeAsync(
        SelectedSessionIdentity requester,
        PartySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(snapshot);
        var stream = streams.GetOrAdd(snapshot.Generation);
        await stream.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (snapshot.Revision < stream.LatestRevision)
                return;
            await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications,
                [requester],
                new PartyRealtimeChanged(snapshot),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stream.Gate.Release();
        }
    }

    private async ValueTask<bool> DeliverPartyStateAsync(
        PartyNotificationPlan plan,
        bool promoteSettingsUpdate,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {

        if (plan.SettingsChange == PartySettingsChangeKind.Updated && !promoteSettingsUpdate)
        {
            var settingsSnapshot = plan.Snapshot!;
            var settingsAudience = resolver.Characters(
                settingsSnapshot.Members.Select(value => value.CharacterId));
            var settingsDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications,
                settingsAudience,
                new PartySettingsChanged(settingsSnapshot),
                cancellationToken).ConfigureAwait(false);
            return !settingsDelivery.HasFailures
                && await DeliverObserverStateAsync(plan, resolver, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (plan.LeadershipTransfer is { } transfer)
        {
            var transferAudience = resolver.Characters(
                transfer.Members.Select(value => value.CharacterId));
            if ((await PartyExactNotificationDelivery.PublishWithRetryAsync(
                    notifications, transferAudience,
                    new PartyRealtimeChanged(transfer), cancellationToken)
                    .ConfigureAwait(false)).HasFailures
                || (await PartyExactNotificationDelivery.PublishWithRetryAsync(
                    notifications, transferAudience,
                    new PartyEndpointsChanged(transfer), cancellationToken)
                    .ConfigureAwait(false)).HasFailures
                || (await PartyExactNotificationDelivery.PublishWithRetryAsync(
                    notifications, transferAudience,
                    PartyRosterChanged.Refresh(transfer,
                        transferAudience.Select(value => value.CharacterId).ToImmutableArray()),
                    cancellationToken).ConfigureAwait(false)).HasFailures)
            {
                return false;
            }
        }

        if (!await DeliverClearAsync(
            resolver.Characters(plan.ClearAudience),
            plan.ClearPartyId,
            cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (plan.Snapshot is not { } snapshot)
            return await DeliverObserverStateAsync(plan, resolver, cancellationToken)
                .ConfigureAwait(false);

        var memberAudience = resolver.Characters(
            snapshot.Members.Select(value => value.CharacterId));
        var realtimeDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            memberAudience,
            new PartyRealtimeChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
        if (realtimeDelivery.HasFailures)
            return false;
        if (plan.Acceptance is { } acceptance)
        {
            if (!await DeliverAcceptanceAsync(acceptance, resolver, cancellationToken)
                    .ConfigureAwait(false))
                return false;
        }
        var endpointsDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            memberAudience,
            new PartyEndpointsChanged(snapshot),
            cancellationToken).ConfigureAwait(false);
        if (endpointsDelivery.HasFailures)
            return false;
        var rosterDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            memberAudience,
            PartyRosterChanged.Refresh(
                snapshot,
                memberAudience.Select(value => value.CharacterId).ToImmutableArray()),
            cancellationToken).ConfigureAwait(false);
        if (rosterDelivery.HasFailures)
            return false;
        if (plan.Acceptance is not null)
        {
            // A21 drops the first realtime summary when it arrives before the party slots exist.
            // Replay the same immutable snapshot after the full roster so both clients can bind
            // the current HP without relying on an optional per-client 0x00A6 follow-up query.
            var replayDelivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications,
                memberAudience,
                new PartyRealtimeChanged(snapshot),
                cancellationToken).ConfigureAwait(false);
            if (replayDelivery.HasFailures)
                return false;
        }

        return await DeliverObserverStateAsync(plan, resolver, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> DeliverObserverStateAsync(
        PartyNotificationPlan plan,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {
        if (plan.DirectoryChange is not { } change)
            return true;

        if (plan.Snapshot is { } projected
            && parties is not null
            && projected.Members
                .Select(member => parties.FindParty(member.Character))
                .FirstOrDefault(current => !current.IsVirtual
                    && current.Generation == projected.Generation)
                is { } latest
            && latest.Revision > projected.Revision
            && latest.Members.Single(member => member.Slot == 0).Character.DirectoryScope
                != change.Current)
        {
            // Presence migration advances the Party revision even though the movement protocol
            // performs its own directory read-repair. Do not let a delayed pre-move plan publish
            // the old scope again after that newer revision is already authoritative.
            return true;
        }

        var rekeyed = plan.Snapshot is { } currentSnapshot
            && plan.ClearPartyId is { } oldId
            && oldId != currentSnapshot.PartyId;
        var excluded = plan.Snapshot is { } snapshot
            ? snapshot.Members.Select(member => member.CharacterId).ToHashSet()
            : plan.ClearAudience.ToHashSet();
        if (rekeyed)
            excluded.UnionWith(plan.ClearAudience);
        var previousObservers = resolver.Scope(change.Previous)
            .Where(session => !excluded.Contains(session.CharacterId))
            .Distinct()
            .ToImmutableArray();
        var currentObservers = resolver.Scope(change.Current)
            .Where(session => !excluded.Contains(session.CharacterId))
            .Distinct()
            .ToImmutableArray();

        if (change.Previous is not null && (change.Previous != change.Current || rekeyed)
            && !previousObservers.IsEmpty)
        {
            var partyId = plan.ClearPartyId ?? plan.Snapshot?.PartyId
                ?? throw new InvalidOperationException("A moved party directory entry requires its party ID.");
            var clear = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications,
                previousObservers,
                PartyRosterChanged.Clear(
                    previousObservers.Select(value => value.CharacterId).ToImmutableArray(),
                    partyId),
                cancellationToken).ConfigureAwait(false);
            if (clear.HasFailures)
                return false;
        }

        if (plan.Snapshot is { } refresh && !currentObservers.IsEmpty)
        {
            var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications,
                currentObservers,
                PartyRosterChanged.Refresh(
                    refresh,
                    currentObservers.Select(value => value.CharacterId).ToImmutableArray()),
                cancellationToken).ConfigureAwait(false);
            return !delivery.HasFailures;
        }

        if (plan.Snapshot is null && change.Previous is not null
            && change.Previous == change.Current && !previousObservers.IsEmpty)
        {
            var partyId = plan.ClearPartyId
                ?? throw new InvalidOperationException("An observer party clear requires its party ID.");
            var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
                notifications,
                previousObservers,
                PartyRosterChanged.Clear(
                    previousObservers.Select(value => value.CharacterId).ToImmutableArray(),
                    partyId),
                cancellationToken).ConfigureAwait(false);
            return !delivery.HasFailures;
        }

        return true;
    }

    private async ValueTask<bool> DeliverStaleSideEffectsAsync(
        PartyNotificationPlan plan,
        ImmutableArray<SelectedSessionIdentity> staleClearAudience,
        HashSet<SelectedSessionIdentity> currentMembers,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!await DeliverClearAsync(
            staleClearAudience,
            plan.ClearPartyId,
            cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        if (plan.Acceptance is { } acceptance
            && resolver.Character(acceptance.InviterCharacterId) is { } inviter
            && resolver.Character(acceptance.InviteeCharacterId) is { } invitee
            && currentMembers.Contains(inviter)
            && currentMembers.Contains(invitee))
        {
            return await DeliverAcceptanceAsync(acceptance, resolver, cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    private async ValueTask<bool> DeliverClearAsync(
        ImmutableArray<SelectedSessionIdentity> clearAudience,
        ushort? clearPartyId,
        CancellationToken cancellationToken)
    {
        if (clearAudience.IsDefaultOrEmpty)
            return true;

        var partyId = clearPartyId
            ?? throw new InvalidOperationException("A party clear requires its party ID.");
        var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            clearAudience,
            PartyRosterChanged.Clear(
                clearAudience.Select(value => value.CharacterId).ToImmutableArray(),
                partyId),
            cancellationToken).ConfigureAwait(false);
        return !delivery.HasFailures;
    }

    private async ValueTask<bool> DeliverAcceptanceAsync(
        PartyAcceptanceNotification acceptance,
        PartyNotificationRecipientResolver resolver,
        CancellationToken cancellationToken)
    {
        var inviter = resolver.Characters([acceptance.InviterCharacterId]);
        var delivery = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            inviter,
            new PartyInviteAccepted(acceptance),
            cancellationToken).ConfigureAwait(false);
        return !delivery.HasFailures;
    }

    private static void ValidatePlanShape(PartyNotificationPlan plan)
    {
        if (plan.LeadershipTransfer is { } transfer
            && (plan.Snapshot is not { } final
                || transfer.PartyId != final.PartyId
                || transfer.Generation != final.Generation
                || transfer.Revision >= final.Revision
                || plan.ClearAudience.Length != 1
                || !transfer.Members.Any(member =>
                    member.CharacterId == plan.ClearAudience[0])))
        {
            throw new InvalidOperationException(
                "A leadership transfer must precede one departing member's final roster.");
        }

        var settingsChange = plan.SettingsChange;
        if (settingsChange is not null
            && settingsChange is not PartySettingsChangeKind.Created
                and not PartySettingsChangeKind.Updated)
        {
            throw new InvalidOperationException("A party plan contains an unknown settings change kind.");
        }

        var hasClearAudience = !plan.ClearAudience.IsDefaultOrEmpty;
        var hasDirectoryChange = plan.DirectoryChange is not null;
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

        if (hasDirectoryChange && plan.Snapshot is null && !hasClearAudience)
        {
            throw new InvalidOperationException(
                "A directory change requires a party snapshot or a complete party clear.");
        }

        if (plan.Invitation is not null
            && (plan.Snapshot is not null
                || plan.Acceptance is not null
                || hasClearAudience
                || settingsChange is not null))
        {
            throw new InvalidOperationException("A party invitation plan cannot contain another operation.");
        }

        if (plan.Acceptance is { } acceptance
            && (plan.Snapshot is null
                || settingsChange is not null
                || (hasClearAudience
                    && (plan.ClearAudience.Length != 1
                        || plan.ClearAudience[0] != acceptance.InviteeCharacterId))))
        {
            throw new InvalidOperationException(
                "A party acceptance requires its resulting snapshot and may clear only the invitee.");
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

    private IEnumerable<SelectedSessionIdentity> PlanParticipants(
        PartyNotificationPlan plan,
        ImmutableArray<SelectedSessionIdentity> clearAudience,
        PartyNotificationRecipientResolver resolver) =>
        resolver.Characters(
                plan.Snapshot?.Members.Select(member => member.CharacterId)
                ?? Enumerable.Empty<CharacterId>())
            .Concat(clearAudience)
            .Distinct();

}
