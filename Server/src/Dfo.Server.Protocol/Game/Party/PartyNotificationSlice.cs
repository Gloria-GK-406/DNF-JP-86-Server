using Dfo.Server.Abstractions.DomainEvents;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Execution;
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
using Dfo.Server.Protocol.Game.DungeonSelection;

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
    private PartyRosterMessage(
        PartySnapshot? snapshot,
        ushort? clearedPartyId,
        bool hostChanged)
    {
        Snapshot = snapshot;
        ClearedPartyId = clearedPartyId;
        HostChanged = hostChanged;
    }

    public PartySnapshot? Snapshot { get; }

    public ushort? ClearedPartyId { get; }

    public bool HostChanged { get; }

    public ServerMessageType MessageType => ServerMessageType.GamePartyRoster;

    public static PartyRosterMessage Refresh(PartySnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null, false);

    public static PartyRosterMessage ChangeHost(PartySnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null, true);

    public static PartyRosterMessage Clear(ushort partyId) => new(null, partyId, false);
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
        var information = message.HostChanged
            ? []
            : PartyInformationBlockEncoder.Encode(snapshot.Settings, snapshot.Title);
        var rosterOffset = 5 + information.Length;
        var body = new byte[rosterOffset + 44];
        BinaryPrimitives.WriteUInt16LittleEndian(body, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), snapshot.PartyId);
        body[4] = message.HostChanged ? (byte)2 : (byte)0;
        information.CopyTo(body.AsSpan(5));

        for (byte slot = 0; slot < 8; slot++)
        {
            var member = snapshot.Members.FirstOrDefault(value => value.Slot == slot);
            BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(rosterOffset + (slot * 5)),
                member?.WireUserId ?? ushort.MaxValue);
        }

        body[rosterOffset + 41] = snapshot.LeaderMember.Slot;
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

public sealed record PartyLeadershipChanged(PartySnapshot Snapshot)
    : INotification<PartyLeadershipChanged>;

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
        sessions.TryGetInWorld(characterId, out var session)
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

public sealed class PartyLeadershipChangedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<PartyLeadershipChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        PartyLeadershipChanged notification,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            PartyNotificationProjection.IsExactMember(recipient, notification.Snapshot, sessions)
                ? [new UdpHostNotification(notification.Snapshot.LeaderMember.Slot),
                    PartyRosterMessage.ChangeHost(notification.Snapshot)]
                : []);
}

public sealed class PartyProjectionStreamRegistry : IPartyProjectionStateDomainService
{
    private readonly ConcurrentDictionary<long, PartyDeliveryStream> streams = [];

    IPartyDeliveryStream IPartyProjectionStateDomainService.StreamOf(long generation)
    {
        GameplayWriteAssertion.Writing(nameof(PartyProjectionStreamRegistry) + "." + nameof(IPartyProjectionStateDomainService.StreamOf));
        return GetOrAdd(generation);
    }

    internal PartyDeliveryStream GetOrAdd(long generation)
    {
        GameplayWriteAssertion.Writing(nameof(PartyProjectionStreamRegistry) + "." + nameof(GetOrAdd));
        return streams.GetOrAdd(generation, static _ => new PartyDeliveryStream());
    }
}

internal sealed class PartyDeliveryStream : IPartyDeliveryStream
{
    // Streams intentionally retain their final revision as a tombstone: a delayed caller from
    // a disbanded generation must never resurrect its older roster after the clear.
    public long LatestRevision
    {
        get;
        set
        {
            GameplayWriteAssertion.Writing(nameof(PartyDeliveryStream) + ".set_" + nameof(LatestRevision));
            field = value;
        }
    }

    // The wire identity currently projected to this generation. A town read-repair may
    // advance a roster revision, but it must not consume a pending leader rekey without
    // retiring the old client object first.
    public ushort? CurrentPartyId
    {
        get;
        set
        {
            GameplayWriteAssertion.Writing(nameof(PartyDeliveryStream) + ".set_" + nameof(CurrentPartyId));
            field = value;
        }
    }

    // The getters hand out the live sets their callers change, so each counts as a write.
    public HashSet<SelectedSessionIdentity> CurrentMembers
    {
        get
        {
            GameplayWriteAssertion.Writing(nameof(PartyDeliveryStream) + ".get_" + nameof(CurrentMembers));
            return field;
        }
    } = [];

    public HashSet<SelectedSessionIdentity> FullStateMembers
    {
        get
        {
            GameplayWriteAssertion.Writing(nameof(PartyDeliveryStream) + ".get_" + nameof(FullStateMembers));
            return field;
        }
    } = [];
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
            .Select(id => sessions.TryGetInWorld(id, out var inWorld) ? inWorld : null)
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
                         scope.AreaId, scope.Instance))?.Members ?? [])
                    .Select(item => item.SharesTownPresence
                        && sessions.TryGetInWorld(item.CharacterId, out var exact)
                        && exact.Owns(item) ? exact : null)
                    .OfType<SelectedSessionIdentity>()
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
    IEventService notifications,
    PartyProjectionStreamRegistry streams,
    ISelectedSessionDomainService? sessions = null,
    ITownAreaDomainService? townAreas = null,
    IPartyService? parties = null,
    IOutboundOrdering? ordering = null)
{
    internal PartyNotificationDelivery(IEventService notifications)
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

    public ValueTask DeliverAsync(
        PartyNotificationPlan plan,
        PartyNotificationRecipientPlan recipients,
        CancellationToken cancellationToken) =>
        new PartyLegacyProjection(new LegacyCommandProjections(notifications), streams.GetOrAdd,
            parties, ordering).DeliverAsync(plan, recipients, cancellationToken);

    public ValueTask DeliverRealtimeAsync(
        SelectedSessionIdentity requester,
        PartySnapshot snapshot,
        CancellationToken cancellationToken) =>
        new PartyLegacyProjection(new LegacyCommandProjections(notifications), streams.GetOrAdd,
            parties, ordering).DeliverRealtimeAsync(requester, snapshot, cancellationToken);
}
