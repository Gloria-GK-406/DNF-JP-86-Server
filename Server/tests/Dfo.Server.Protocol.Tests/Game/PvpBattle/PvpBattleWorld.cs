using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.PvpBattle;
using Dfo.Server.Protocol.Game.PvpRoom;
using Dfo.Server.Protocol.Tests.Game.PvpRoom;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.PvpBattle;

/// <summary>
/// Three sessions in the duel lobby of one listener, a scripted room owner and settlement, and one timeline of every
/// frame registered, direct or projected, in order.
/// </summary>
internal sealed class PvpBattleWorld
{
    public static readonly PvpMatchId MatchId = new(Guid.Parse("6f9d7a0c-2b1e-4c8a-9b55-0d3a6f1c2e77"));
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public PvpBattleWorld()
    {
        Sessions = TestSessions.Directory(out var state);
        A = Sessions.InWorld(state, new AccountId(1), new CharacterId(1), "a", PvpRooms.Port);
        B = Sessions.InWorld(state, new AccountId(2), new CharacterId(2), "b", PvpRooms.Port);
        C = Sessions.InWorld(state, new AccountId(3), new CharacterId(3), "c", PvpRooms.Port);
        foreach (var session in new[] { A, B, C }) Views.Publish(session, PvpRoomCommandTests.EntryFor(session));
        Departures = new PvpDepartureProjectionQueue(Wake);
    }

    public SelectedSessionDomainService Sessions { get; }
    public SelectedSessionIdentity A { get; }
    public SelectedSessionIdentity B { get; }
    public SelectedSessionIdentity C { get; }
    public ScriptedPvpRooms Rooms { get; } = new();
    public ScriptedSettlement Settlement { get; } = new();
    public FixedRecords Records { get; } = new();
    public PvpResultScreenDeferrals Deferrals { get; } = new();
    public CountingWake Wake { get; } = new();
    public PvpDepartureProjectionQueue Departures { get; }
    public Timeline Timeline { get; } = new();
    public FixedCurrentViews Views { get; } = new();

    /// <summary>When set, room and battle broadcasts go here instead of the timeline.</summary>
    public ICommandProjections? Projections { get; set; }

    /// <summary>Who stands ready in the lobby; a session that ended is no longer there.</summary>
    public SelectedSessionIdentity[]? Ready { get; set; }

    public IPvpLobbyDomainService Lobby => new FixedPvpLobby(Ready ?? [A, B, C]);

    public IReadOnlyList<IGameServerMessage> For(SelectedSessionIdentity session) => Timeline.For(session);

    public GamePvpBattleCommandHandler Battle() => new(Sessions, Rooms, DefaultAnswers.Create<IPvpMatchmakingDomainService>(),
        Settlement, Records, Deferrals, Lobby, Views,
        DefaultAnswers.Create<IOnlineCharacterRuntimeDomainService>(), ProtocolAlgorithmFixture.NoOnline(),
        DefaultAnswers.Create<IGameSessionEndpointDomainService>(), ProtocolAlgorithmFixture.Default<ITownAreaDomainService>(),
        ProtocolAlgorithmFixture.Default<IPartyService>(), ProtocolAlgorithmFixture.Default<IPartyProjectionStateDomainService>(),
        ProtocolAlgorithmFixture.Default<IWeaknessDomainService>(), ProtocolAlgorithmFixture.Default<ICommandWorkUnits>(),
        Projections ?? Timeline, Timeline, ProtocolAlgorithmFixture.Default<IPvpDailyMissionQueryDomainService>(),
        ProtocolAlgorithmFixture.Default<IPvpColosseumDomainService>(), NullLogger<GamePvpBattleCommandHandler>.Instance);

    public PvpBattleDeadlineCommandHandler Deadlines() => new(Rooms, Settlement, Records, Departures, Deferrals, Sessions, Lobby, Views,
        DefaultAnswers.Create<IOnlineCharacterRuntimeDomainService>(), ProtocolAlgorithmFixture.NoOnline(),
        DefaultAnswers.Create<IGameSessionEndpointDomainService>(), ProtocolAlgorithmFixture.Default<ITownAreaDomainService>(),
        ProtocolAlgorithmFixture.Default<IPartyService>(), ProtocolAlgorithmFixture.Default<IPartyProjectionStateDomainService>(),
        ProtocolAlgorithmFixture.Default<IWeaknessDomainService>(), ProtocolAlgorithmFixture.Default<ICommandWorkUnits>(),
        Projections ?? Timeline, Timeline, TimeProvider.System, ProtocolAlgorithmFixture.Default<IPvpDailyMissionQueryDomainService>(),
        ProtocolAlgorithmFixture.Default<IPvpColosseumDomainService>(), NullLogger<PvpBattleDeadlineCommandHandler>.Instance);

    public GamePvpRoomCommandHandler Room() => new(Sessions, Rooms, Lobby, Views,
        DefaultAnswers.Create<IOnlineCharacterRuntimeDomainService>(), ProtocolAlgorithmFixture.NoOnline(),
        DefaultAnswers.Create<IGameSessionEndpointDomainService>(), ProtocolAlgorithmFixture.Default<ITownAreaDomainService>(),
        ProtocolAlgorithmFixture.Default<IPartyService>(), ProtocolAlgorithmFixture.Default<IPartyProjectionStateDomainService>(),
        ProtocolAlgorithmFixture.Default<IWeaknessDomainService>(), ProtocolAlgorithmFixture.Default<ICommandWorkUnits>(),
        Projections ?? Timeline, Timeline, Deferrals, NullLogger<GamePvpRoomCommandHandler>.Instance,
        ProtocolAlgorithmFixture.Default<Dfo.Server.Abstractions.Pvp.IPvpTotalMatchCandidateQueryDomainService>(), TimeProvider.System);

    public async Task<bool> SendAsync<TCommand>(object handler, TCommand command) where TCommand : class, IProtocolCommand<bool>
    {
        var request = new CommandRequestContext.ProtocolNonEffect(command.Dispatch, command.Message, command.Frame, () => true);
        return await ((ICommandHandler<TCommand, bool>)handler).HandleAsync(
            new(Guid.NewGuid(), typeof(TCommand), handler.GetType()) { Request = request }, command, default);
    }

    /// <summary>An internal command, under a request context the fixture's work units accept.</summary>
    public async Task<T> RunAsync<TCommand, T>(TCommand command) where TCommand : class, ICommand<T> where T : notnull
    {
        var handler = Deadlines();
        var dispatch = Dispatch(A);
        var request = new CommandRequestContext.ProtocolNonEffect(dispatch, new EndPvpResultRequest(GameMessageMetadata.Empty, 0),
            new ProtocolFrameMetadata.Game(GameMessageMetadata.Empty), () => true);
        return await ((ICommandHandler<TCommand, T>)(object)handler).HandleAsync(
            new(Guid.NewGuid(), typeof(TCommand), handler.GetType()) { Request = request }, command, default);
    }

    public static NetworkDispatchContext Dispatch(SelectedSessionIdentity session) => PvpRoomCommandTests.Dispatch(session);

    /// <summary>
    /// A room whose members sit on seats 0, 1, ... as individual fighters, with a battle of all of them when it is not
    /// waiting.
    /// </summary>
    public static PvpRoomSnapshot RoomOf(PvpRoomPhase phase, params SelectedSessionIdentity[] members)
    {
        var room = PvpRooms.Room(1, PvpBattleMode.Individual, 0, phase,
            seats: [.. members.Select((member, seat) => (seat, (PvpSeat)PvpRooms.Taken(member, PvpTeam.Individual)))]);
        if (phase == PvpRoomPhase.Waiting) return room;
        return room with
        {
            Match = new PvpMatch(MatchId,
                [.. members.Select((member, seat) => new PvpFighter(seat, member.CharacterId, member, PvpTeam.Individual))],
                1, Now, Now + TimeSpan.FromSeconds(30)),
        };
    }

    /// <summary>The same room with <paramref name="seat"/> emptied and its fighter fled.</summary>
    public static PvpRoomSnapshot Without(PvpRoomSnapshot room, int seat) => room with
    {
        Seats = room.Seats.SetItem(seat, PvpSeat.OpenSeat),
        Match = room.Match is { } match
            ? match with { Fighters = match.Fighters.SetItem(seat, match.Fighters[seat] with { Alive = false, Fled = true, Departed = true }) }
            : null,
    };

    public static PvpMatchConclusion ConclusionOf(PvpRoomSnapshot room, PvpMatchTerminal terminal) => new(
        MatchId, room.Id, room.Number, room.Channel, room.Mode, Now, terminal, room.Match!.Fighters, Now);

    /// <summary>
    /// A decided battle the fighter on seat 0 won and every other fighter lost, written, and its room moved to the result
    /// screen. The winner's record ends at 1100 (+100), every loser's at 950 (-50).
    /// </summary>
    public PvpSettlementOutcome.Settled Settled(PvpRoomSnapshot awaitingRank)
    {
        var conclusion = ConclusionOf(awaitingRank, new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, 0, Now));
        PvpFighterResult[] results = [.. conclusion.Fighters.Select(fighter => fighter.Seat == 0
            ? new PvpFighterResult(fighter.CharacterId, 0, 0, PvpOutcome.Win, 1, 0, false, true, 0, 1, 100, 1100, 1)
            : new PvpFighterResult(fighter.CharacterId, (byte)fighter.Seat, 0, PvpOutcome.Loss, 0, 1, fighter.Fled, true, 1, 1, -50, 950, 0))];
        foreach (var fighter in conclusion.Fighters)
            Records.Views[fighter.CharacterId] = fighter.Seat == 0
                ? new PvpRecordView(1, 0, 1100, 1000, 2000, 5, 2)
                : new PvpRecordView(1, 0, 950, 900, 2000, 3, 6);
        var result = awaitingRank with { Phase = PvpRoomPhase.AwaitResult, Match = awaitingRank.Match! with { Results = [.. results] } };
        return new PvpSettlementOutcome.Settled(conclusion, new PvpSettleOutcome.Created(results),
            PvpRooms.Changed(PvpRoomChangeKind.Settled, awaitingRank, result));
    }
}

internal sealed class ScriptedSettlement : IPvpSettlementDomainService
{
    public List<PvpMatchId> Calls { get; } = [];

    public PvpSettlementOutcome Next { get; set; } = new PvpSettlementOutcome.NotPending();

    public ValueTask<PvpSettlementOutcome> SettleAsync(PvpMatchId match, CancellationToken cancellationToken)
    {
        Calls.Add(match);
        return ValueTask.FromResult(Next);
    }
}

internal sealed class FixedRecords : IPvpRecordDomainService
{
    public Dictionary<CharacterId, PvpRecordView> Views { get; } = [];

    public ValueTask<PvpRecordView> ViewAsync(CharacterId character, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Views[character]);
    public PvpExperienceBand ExperienceBandOf(byte grade)
    {
        var view = Views.Values.First(record => record.Grade == grade);
        return new(view.LowerBound, view.UpperBound);
    }
}

internal sealed class CountingWake : IPvpDeadlineWake
{
    public int Count { get; private set; }

    public void Wake() => Count++;
}

/// <summary>Every projection fails, as a notification composer that throws would.</summary>
internal sealed class ThrowingProjections : ICommandProjections
{
    public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience,
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification<TNotification> =>
        throw new InvalidOperationException("The projection failed.");
}
