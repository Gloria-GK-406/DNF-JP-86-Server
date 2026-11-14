using System.Collections.Immutable;
using System.Reflection;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests.Game.PvpRoom;

/// <summary>A room owner whose answers a test scripts; it records which writes were asked for.</summary>
internal sealed class ScriptedPvpRooms : IPvpRoomDomainService
{
    public List<string> Calls { get; } = [];

    /// <summary>The room <see cref="FindSeated"/> answers for its members.</summary>
    public PvpRoomSnapshot? Room { get; set; }

    public HashSet<CharacterId> AlsoSeated { get; } = [];

    public PvpRoomOutcome Next { get; set; } = new PvpRoomOutcome.Refused(PvpRoomRefusal.NotSeated);

    public PvpInviteOutcome NextInvite { get; set; } = new PvpInviteOutcome.Refused(PvpRoomRefusal.NotSeated);

    public (PvpSeatAction Action, int Seat, PvpTeam Team)? LastSeat { get; private set; }

    public IReadOnlyList<PvpRoomSnapshot> RoomsOn(int listenerPort) => Room is { } room ? [room] : [];
    public PvpRoomSnapshot? Find(PvpRoomId id) => Room?.Id == id ? Room : null;
    public PvpRoomSnapshot? FindSeated(CharacterId characterId) => Room?.SeatOf(characterId) is not null ? Room : null;
    public bool IsSeated(CharacterId characterId) => FindSeated(characterId) is not null || AlsoSeated.Contains(characterId);

    private ValueTask<PvpRoomOutcome> Answer(string call)
    {
        Calls.Add(call);
        return ValueTask.FromResult(Next);
    }

    public ValueTask<PvpRoomOutcome> CreateAsync(PvpRoomCreation request, CancellationToken cancellationToken) => Answer(nameof(CreateAsync));
    public ValueTask<PvpRoomOutcome> EnterAsync(SelectedSessionIdentity session, ushort number, ImmutableArray<byte>? password, CancellationToken cancellationToken) =>
        Answer(nameof(EnterAsync));
    public ValueTask<PvpRoomOutcome> SetSeatAsync(SelectedSessionIdentity actor, int seat, PvpSeatAction action, PvpTeam team, CancellationToken cancellationToken)
    {
        LastSeat = (action, seat, team);
        return Answer(nameof(SetSeatAsync));
    }
    public ValueTask<PvpRoomOutcome> SetModeAsync(SelectedSessionIdentity actor, PvpBattleMode mode, CancellationToken cancellationToken) => Answer(nameof(SetModeAsync));
    public ValueTask<PvpRoomOutcome> SetMapAsync(SelectedSessionIdentity actor, int mapIndex, CancellationToken cancellationToken) => Answer(nameof(SetMapAsync));
    public ValueTask<PvpRoomOutcome> SetReadyAsync(SelectedSessionIdentity actor, bool ready, CancellationToken cancellationToken) => Answer(nameof(SetReadyAsync));
    public int? LastPeerToken { get; private set; }

    public ValueTask<PvpInviteOutcome> InviteAsync(SelectedSessionIdentity inviter, SelectedSessionIdentity invitee, CancellationToken cancellationToken, int peerToken = 0)
    {
        LastPeerToken = peerToken;
        Calls.Add(nameof(InviteAsync));
        return ValueTask.FromResult(NextInvite);
    }
    public ValueTask<PvpRoomOutcome> AcceptInviteAsync(SelectedSessionIdentity invitee, CharacterId inviter, CancellationToken cancellationToken, int? peerToken = null)
    {
        LastPeerToken = peerToken;
        return Answer(nameof(AcceptInviteAsync));
    }
    public ValueTask<PvpRoomOutcome> ReportLoadedAsync(SelectedSessionIdentity session, CancellationToken cancellationToken) => Answer(nameof(ReportLoadedAsync));
    public ValueTask<PvpRoomOutcome> ReportPeerLinkedAsync(SelectedSessionIdentity session, CancellationToken cancellationToken) => Answer(nameof(ReportPeerLinkedAsync));
    public ValueTask<PvpRoomOutcome> HeartbeatAsync(SelectedSessionIdentity session, CancellationToken cancellationToken) => Answer(nameof(HeartbeatAsync));

    /// <summary>The killer the last death report named.</summary>
    public CharacterId? LastKiller { get; private set; }

    /// <summary>What the last time-out report carried.</summary>
    public ImmutableArray<byte> LastTimeOut { get; private set; }

    public ValueTask<PvpRoomOutcome> ReportDeathAsync(SelectedSessionIdentity session, CharacterId? killer, CancellationToken cancellationToken)
    {
        LastKiller = killer;
        return Answer(nameof(ReportDeathAsync));
    }
    public ValueTask<PvpRoomOutcome> ReportTimeoutAsync(SelectedSessionIdentity session, ImmutableArray<byte> clientValues, CancellationToken cancellationToken)
    {
        LastTimeOut = clientValues;
        return Answer(nameof(ReportTimeoutAsync));
    }
    public ValueTask<PvpRoomOutcome> AcknowledgeRankAsync(SelectedSessionIdentity session, CancellationToken cancellationToken) => Answer(nameof(AcknowledgeRankAsync));
    public ValueTask<PvpRoomOutcome> AcknowledgeResultAsync(SelectedSessionIdentity session, CancellationToken cancellationToken) => Answer(nameof(AcknowledgeResultAsync));
    public ValueTask<PvpRoomOutcome> DepartAsync(SelectedSessionIdentity session, PvpDepartureReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();
    public PvpDeadlineScan Deadlines(DateTimeOffset now) => throw new NotSupportedException();

    /// <summary>What the next deadline advance, settlement drop or shutdown cancel answers.</summary>
    public PvpRoomOutcome.Changed? NextAdvance { get; set; }

    public IReadOnlyList<PvpRoomOutcome.Changed> NextCancelled { get; set; } = [];

    public ValueTask<PvpRoomOutcome.Changed?> AdvanceDueAsync(PvpRoomId room, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(AdvanceDueAsync));
        return ValueTask.FromResult(NextAdvance);
    }
    public IReadOnlyList<PvpMatchConclusion> PendingConclusions() => throw new NotSupportedException();
    public ValueTask<IReadOnlyList<PvpRoomOutcome.Changed>> CancelForShutdownAsync(CancellationToken cancellationToken)
    {
        Calls.Add(nameof(CancelForShutdownAsync));
        return ValueTask.FromResult(NextCancelled);
    }
    public ValueTask<PvpRoomOutcome.Changed?> AbandonSettlementAsync(PvpMatchId match, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(AbandonSettlementAsync));
        return ValueTask.FromResult(NextAdvance);
    }
}

/// <summary>Town moves already made, with nothing to show for them.</summary>
internal sealed class NoTownMoves : IPvpTownMoves
{
    public static NoTownMoves Instance { get; } = new();
    public bool Applied => true;
    public IReadOnlyList<TownDepartureResult> Departures => [];
    public IReadOnlyList<TownReturnAdmission> Returns => [];
}

/// <summary>A lobby holding exactly the sessions it was given, all on one listener.</summary>
internal sealed class FixedPvpLobby(params SelectedSessionIdentity[] ready) : IPvpLobbyDomainService
{
    public void MarkReady(PvpLobbyParticipant participant) => throw new NotSupportedException();
    public bool ClearReady(CharacterId characterId) => throw new NotSupportedException();
    public bool ClearReadyExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    public IReadOnlyList<PvpLobbyParticipant> ReadyOn(int listenerPort, CharacterId excluding) =>
        [.. ready.Where(x => x.ListenerPort == listenerPort && x.CharacterId != excluding)
            .Select(x => new PvpLobbyParticipant(x.CharacterId, x.ListenerPort, x))];
}

/// <summary>Records every projection and the notification it carried.</summary>
internal sealed class RecordingProjections : ICommandProjections
{
    public List<(NotificationAudience Audience, object Notification)> Projected { get; } = [];

    public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience,
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification<TNotification>
    {
        Projected.Add((audience, notification));
        return ValueTask.FromResult(new NotificationDelivery([]));
    }

    /// <summary>The frames each exact session was sent through room broadcasts, in order.</summary>
    public IReadOnlyList<IGameServerMessage> FramesFor(SelectedSessionIdentity session) =>
        [.. Projected.Where(x => x.Audience is NotificationAudience.TheseSessions these && these.Sessions.Contains(session))
            .Select(x => x.Notification).OfType<Dfo.Server.Protocol.Game.PvpRoom.PvpRoomBroadcast>().SelectMany(x => x.Messages)];
}

/// <summary>Answers every call with its return type's default: null, false, an empty task.</summary>
public class DefaultAnswers : DispatchProxy
{
    public static T Create<T>() where T : class => Create<T, DefaultAnswers>();

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var type = method!.ReturnType;
        if (type == typeof(void)) return null;
        if (type == typeof(ValueTask)) return ValueTask.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            return Activator.CreateInstance(type, [type.GetGenericArguments()[0].IsValueType
                ? Activator.CreateInstance(type.GetGenericArguments()[0]) : null]);
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

internal static class PvpRooms
{
    public const int Port = 10011;

    public static SelectedSessionIdentity Session(int id, int port = Port) => SelectedSessionIdentity.Create(
        new AccountId(id), new CharacterId(id), new NetworkConnectionId($"c{id}"), 1, port);

    public static PvpRoomSnapshot Room(
        ushort number, PvpBattleMode mode, int ownerSeat, PvpRoomPhase phase = PvpRoomPhase.Waiting, int map = 0,
        params (int Seat, PvpSeat State)[] seats)
    {
        var all = Enumerable.Repeat(PvpSeat.OpenSeat, PvpRoomSnapshot.SeatCount).ToImmutableArray();
        foreach (var (seat, state) in seats) all = all.SetItem(seat, state);
        return new PvpRoomSnapshot(new PvpRoomId(new Guid(number, 0, 0, new byte[8])), number,
            new ChannelEntryIdentity(1, Port, ChannelEntryKind.Pvp, 8), 0, [0x41, 0x42], false, map, mode, ownerSeat, all,
            phase, 1, PvpRoomOrigin.PlayerCreated, null);
    }

    public static PvpSeat.Taken Taken(SelectedSessionIdentity session, PvpTeam team = PvpTeam.First, bool ready = false) =>
        new(session, team, ready);

    public static PvpRoomOutcome.Changed Changed(PvpRoomChangeKind kind, PvpRoomSnapshot? before, PvpRoomSnapshot? after,
        params PvpSeatRelease[] released) => new(kind, before, after, released, NoTownMoves.Instance);
}

/// <summary>One record of every frame a command registers, direct or projected, in registration order.</summary>
internal sealed class Timeline : IGameCommandOutbox, ICommandProjections
{
    public List<(NetworkConnectionId? Connection, NotificationAudience? Audience, IGameServerMessage Frame)> Frames { get; } = [];

    public ValueTask RegisterCmdAsync(NetworkConnectionId connection, IGameCmdMessage message, CancellationToken token) => Direct(connection, [message]);
    public ValueTask RegisterNotiAsync(NetworkConnectionId connection, IGameNotiMessage message, CancellationToken token) => Direct(connection, [message]);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken token) =>
        Direct(connection.ConnectionId, messages);
    public ValueTask RegisterExplicitAsync(NetworkConnectionId connection, GameExplicitMessageDelivery delivery, CancellationToken token) =>
        Direct(connection, delivery.RequiredDirectMessages);

    public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience,
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification<TNotification>
    {
        if (notification is Dfo.Server.Protocol.Game.PvpRoom.PvpRoomBroadcast broadcast)
            foreach (var frame in broadcast.Messages) Frames.Add((null, audience, frame));
        return ValueTask.FromResult(new NotificationDelivery([]));
    }

    /// <summary>What this exact session was sent, directly or through a room broadcast, in order.</summary>
    public IReadOnlyList<IGameServerMessage> For(SelectedSessionIdentity session) =>
        [.. Frames.Where(x => x.Connection == session.ConnectionId
            || x.Audience is NotificationAudience.TheseSessions these && these.Sessions.Contains(session)).Select(x => x.Frame)];

    private ValueTask Direct(NetworkConnectionId connection, IReadOnlyList<IGameServerMessage> messages)
    {
        foreach (var message in messages) Frames.Add((connection, null, message));
        return ValueTask.CompletedTask;
    }
}

/// <summary>Town moves already made, returning these admissions.</summary>
internal sealed class ReturnedTownMoves(IReadOnlyList<TownReturnAdmission> returns) : IPvpTownMoves
{
    public bool Applied => true;
    public IReadOnlyList<TownDepartureResult> Departures => [];
    public IReadOnlyList<TownReturnAdmission> Returns => returns;
}
