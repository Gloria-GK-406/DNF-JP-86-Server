using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.PvpColosseum;
using Dfo.Server.Protocol.Game.PvpRoom;
using Dfo.Server.Protocol.Game.PvpTotalMatchTeam;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.PvpBattle;

/// <summary>
/// What the duel battles show their fighters once a change has committed: deaths and the end of the fight, the
/// results, and through <see cref="PvpRoomProjector"/> what the room and the lobby see.
/// </summary>
/// <remarks>
/// Every source projects through here: the battle commands, the deadline commands (a passed deadline, a settlement,
/// a dropped settlement, the shutdown cancel) and the departures selected-session cleanup hands over. Called only
/// after the write unit has returned. Frame order (design-r1 §4 b, upstream <c>PvpRoomHandler</c>): to the members
/// still seated <c>0x002E</c> for a death or a fighter who fled, then <c>0x0031</c> when the battle ended; the room
/// and lobby frames after them; per fighter <c>0x002F</c> and <c>0x0030</c> once the results are written.
/// </remarks>
internal sealed class PvpBattleProjector(PvpRoomProjector rooms, ILogger logger)
{
    public PvpRoomProjector Rooms => rooms;

    /// <summary>A committed room change of a battle: its battle frames to the members, then the room's own projection.</summary>
    public async ValueTask ChangedAsync(PvpRoomOutcome.Changed change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var frames = BattleFrames(change);
        if (frames.Count > 0 && change.After is { } room)
            await rooms.BroadcastAsync([.. room.Members.Select(x => x.Member.Session)], frames, cancellationToken).ConfigureAwait(false);
        await rooms.ProjectAsync(change, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A written settlement: each seated member gets <c>0x002F</c> and its record <c>0x0030</c>, then
    /// the room's move to its result screen is projected. Nothing when the room no longer waits on that battle.
    /// </summary>
    /// <param name="views">The current records and the grade bands used by the settlement.</param>
    public async ValueTask SettledAsync(PvpSettlementOutcome.Settled settled,
        PvpBattleResultViews views, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settled);
        if (settled.Room is not { } change) return;
        if (change.After is { } room)
        {
            var conclusion = settled.Conclusion;
            var results = settled.Stored.Results.ToDictionary(result => result.Character);
            var roster = conclusion.Fighters.OrderBy(fighter => fighter.Seat).Select(fighter =>
            {
                var result = results.GetValueOrDefault(fighter.CharacterId)
                    ?? throw new InvalidOperationException($"Duel battle {conclusion.MatchId.Value} has no result for a fighter.");
                return new EndPvpFighter(fighter.Session.WireUserId, fighter.Kills, result.Grade, result.Experience)
                {
                    Rating = result.Rating?.Standing ?? PvpRatingStanding.Initial,
                };
            }).ToArray();
            var winner = WinnerSeat(conclusion);
            var draw = conclusion.Terminal.Kind == PvpMatchOutcomeKind.Draw;
            var total = room.Match?.ModeState as PvpTotalMatchBattle;
            foreach (var (seat, member) in room.Members)
            {
                PvpFighterResult? own = null;
                if (member.Observation is null && (!conclusion.Fighters.Any(fighter => fighter.Session == member.Session)
                    || !results.TryGetValue(member.CharacterId, out own)))
                    continue;
                var record = views.Records.GetValueOrDefault(member.CharacterId);
                var rankedTotal = room.IsMatchmaking && room.Mode == PvpBattleMode.TotalMatch;
                if (room.Mode == PvpBattleMode.TotalMatch && !rankedTotal && record is not null)
                    record = record with { TotalMatchRating = PvpRatingStanding.Initial };
                var grade = own?.Grade ?? record?.Grade;
                if (grade is null) continue;
                var lineup = total?.Sides.SingleOrDefault(side => side.Seat == seat)?.Lineup;
                var candidates = lineup is null ? [] : CandidateExperience(lineup, results, views);
                var experienceChange = candidates.Length == 0
                    ? own?.ExperienceChange ?? 0 : candidates.Sum(candidate => candidate.After - candidate.Before);
                var end = new EndPvpMessage(winner, grade.Value, experienceChange, roster, draw);
                if (record is null)
                    logger.LogWarning("Duel record of character {Character} unreadable after battle {Match}; its 0x0030 skipped",
                        member.CharacterId.Value, conclusion.MatchId.Value);
                // A21 reads the standing and all three experience bars when 0x002F opens the result screen.
                IReadOnlyList<IGameServerMessage> frames = [end];
                if (record is not null)
                {
                    var notice = new PvpRecordMessage(record) { TotalMatchExperience = candidates };
                    frames = own?.Rating is not null || candidates.Length > 0 ? [notice, end] : [end, notice];
                }
                // The result panel reads the user's Colosseum counters during the result sequence.
                if (settled.ColosseumStates.TryGetValue(seat, out var colosseum))
                    frames = [new ColosseumContextNotice(member.Session.WireUserId, colosseum), .. frames];
                if (settled.VictoryEmblemBalances.TryGetValue(member.CharacterId, out var balance))
                    frames = [.. frames, GameInventoryDelivery.VirtualResourceNotification(
                        [new InventoryRuntimeValueState(2, 2, balance)], conclusion.Terminal.At)];
                await rooms.BroadcastAsync([member.Session], frames, cancellationToken).ConfigureAwait(false);
            }
        }
        await ChangedAsync(change, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The settled representatives and their frozen lineups, including any reserves that never fought.</summary>
    public static IReadOnlyList<CharacterId> RecordsFor(PvpSettlementOutcome? settled)
    {
        if (settled is not PvpSettlementOutcome.Settled { Room.After: { } room }) return [];
        var seated = room.Members.Select(x => x.Member.CharacterId);
        return room.Match?.ModeState is PvpTotalMatchBattle total
            ? [.. seated.Concat(total.Sides.Where(side => room.Seats[side.Seat] is PvpSeat.Taken)
                .SelectMany(side => side.Lineup.Candidates)
                .Select(candidate => candidate.Character.Identity.CharacterId)).Distinct()]
            : [.. seated];
    }

    private static PvpCandidateExperience[] CandidateExperience(PvpTotalMatchLineup lineup,
        IReadOnlyDictionary<CharacterId, PvpFighterResult> results, PvpBattleResultViews views) =>
        [.. lineup.Candidates.Select(candidate =>
        {
            var character = candidate.Character.Identity.CharacterId;
            var current = views.Records[character];
            var band = new PvpExperienceBand(current.LowerBound, current.UpperBound);
            if (!results.TryGetValue(character, out var result))
                return new PvpCandidateExperience(current.Experience, current.Experience, current.Grade, current.Grade, band, band);
            return new PvpCandidateExperience(result.Experience - result.ExperienceChange, result.Experience,
                result.PreviousGrade, result.Grade, views.PreviousBands[result.PreviousGrade], band);
        })];

    /// <summary>
    /// The seat <c>0x002F</c> names as the winner: for a team, the first seat still standing on it, as upstream
    /// <c>FindTerminalWinnerSeat</c>; 0xFF for a draw.
    /// </summary>
    public static byte WinnerSeat(PvpMatchConclusion conclusion)
    {
        if (conclusion.Terminal is not { Kind: PvpMatchOutcomeKind.Decided, Winner: { } winner }) return PvpBattlePayloadCodec.Nobody;
        return PvpBattleModeCapabilities.Of(conclusion.Mode).Terminal switch
        {
            PvpBattleTerminal.LastFighterStanding => winner,
            PvpBattleTerminal.LastTeamStanding => conclusion.Fighters.Where(fighter => (byte)fighter.Team == winner)
                .OrderBy(fighter => fighter.Alive ? 0 : 1).ThenBy(fighter => fighter.Seat)
                .Select(fighter => (byte)fighter.Seat).DefaultIfEmpty(PvpBattlePayloadCodec.Nobody).First(),
        };
    }

    /// <summary>
    /// <c>0x002E</c> for the death the change recorded and for every fighter it made flee (left the fight or fell
    /// silent: dead, killed by nobody), then <c>0x0031</c> when it ended the battle.
    /// </summary>
    public static IReadOnlyList<IGameServerMessage> BattleFrames(PvpRoomOutcome.Changed change)
    {
        List<IGameServerMessage> frames = [];
        if (change.Death is { } death) frames.Add(new PvpDeathMessage(death.VictimSeat, death.KillerSeat));
        if (change.Before?.Match is { } was && change.After?.Match is { } now && was.Id == now.Id)
            for (var index = 0; index < now.Fighters.Length; index++)
                if (!was.Fighters[index].Fled && now.Fighters[index].Fled
                    && (was.ModeState is not PvpTotalMatchBattle totalMatch
                        || totalMatch.Phase is PvpTotalMatchPhase.Fighting or PvpTotalMatchPhase.BetweenRounds
                        && totalMatch.Sides.Any(side => side.Seat == now.Fighters[index].Seat && side.Choice is not null)))
                    frames.Add(new PvpDeathMessage(now.Fighters[index].Seat, null));
        if (change.Concluded is not null) frames.Add(PvpRankRequestMessage.Instance);
        if (change.RelayRequestSeat is { } seat) frames.Add(new PvpRequestFightMessage(seat));
        if (change.After is { Phase: PvpRoomPhase.Fighting, Match.ModeState: PvpRelayBattle { TurnDue: null, FirstSeat: { } first, SecondSeat: { } second } }
            && change.Before?.Match?.ModeState is PvpRelayBattle { TurnDue: not null })
            frames.Add(new PvpTurnPlayerMessage(first, second));
        if (change.After is { Phase: PvpRoomPhase.Fighting, Match: { ModeState: PvpTotalMatchBattle total } match }
            && change.Before?.Match?.ModeState is PvpTotalMatchBattle before && before.Phase != total.Phase)
        {
            if (total.Phase == PvpTotalMatchPhase.Selecting)
                frames.Add(PvpTotalMatchSelectionStarted.Instance);
            else if (total.Phase == PvpTotalMatchPhase.Fighting)
            {
                var firstSide = total.Sides[0];
                var secondSide = total.Sides[1];
                frames.Add(new PvpTotalMatchFight(
                    match.Fighters.Single(fighter => fighter.Seat == firstSide.Seat).Session.WireUserId, firstSide.Choice!.Value,
                    match.Fighters.Single(fighter => fighter.Seat == secondSide.Seat).Session.WireUserId, secondSide.Choice!.Value));
            }
        }
        return frames;
    }
}

/// <summary>
/// The duel room departures awaiting projection: selected-session cleanup hands each committed departure over, the
/// duel deadline scheduler is woken, and its round claims them in order before it looks at the deadlines.
/// </summary>
public sealed class PvpDepartureProjectionQueue(IPvpDeadlineWake wake) : IPvpDepartureProjectionDomainService, IPvpRoomDepartureSink
{
    private sealed record Entry(PvpDepartureEntryId Id, PvpCommittedDeparture Departure);

    private readonly ConcurrentQueue<Entry> pending = new();

    public ValueTask DepartedAsync(PvpRoomOutcome.Changed departure, PvpSettlementOutcome? settled, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpDepartureProjectionQueue) + "." + nameof(DepartedAsync));
        ArgumentNullException.ThrowIfNull(departure);
        pending.Enqueue(new(new(Guid.NewGuid()), new(departure, settled)));
        wake.Wake();
        return ValueTask.CompletedTask;
    }

    public ImmutableArray<PvpDepartureEntryId> SnapshotPending()
    {
        GameplayJob.DemandHealthy();
        return [.. pending.Select(entry => entry.Id)];
    }

    public bool TryClaim(PvpDepartureEntryId expected, out PvpCommittedDeparture? departure)
    {
        GameplayWriteAssertion.Writing(nameof(PvpDepartureProjectionQueue) + "." + nameof(TryClaim));
        departure = null;
        if (!pending.TryPeek(out var head) || head.Id != expected) return false;
        if (!pending.TryDequeue(out var claimed) || !ReferenceEquals(head, claimed))
            throw new InvalidOperationException("The duel departure queue changed outside its gameplay owner.");
        departure = claimed.Departure;
        return true;
    }
}

/// <summary>Who is owed a room's seats from a battle's result screen (Spec RQ-035), one room per member.</summary>
public sealed class PvpResultScreenDeferrals : IPvpResultScreenDeferralDomainService
{
    private readonly Lock gate = new();
    private readonly Dictionary<SelectedSessionIdentity, PvpRoomId> owed = [];

    public void Defer(SelectedSessionIdentity member, PvpRoomId room)
    {
        GameplayWriteAssertion.Writing(nameof(PvpResultScreenDeferrals) + "." + nameof(Defer));
        ArgumentNullException.ThrowIfNull(member);
        lock (gate) owed[member] = room;
    }

    public bool Owes(SelectedSessionIdentity member, PvpRoomId room)
    {
        ArgumentNullException.ThrowIfNull(member);
        lock (gate) return owed.TryGetValue(member, out var owing) && owing == room;
    }

    public PvpRoomId? Release(SelectedSessionIdentity member)
    {
        GameplayWriteAssertion.Writing(nameof(PvpResultScreenDeferrals) + "." + nameof(Release));
        ArgumentNullException.ThrowIfNull(member);
        lock (gate) return owed.Remove(member, out var room) ? room : null;
    }

    public void DropRoom(PvpRoomId room)
    {
        GameplayWriteAssertion.Writing(nameof(PvpResultScreenDeferrals) + "." + nameof(DropRoom));
        lock (gate)
            foreach (var member in owed.Where(x => x.Value == room).Select(x => x.Key).ToArray())
                owed.Remove(member);
    }
}
