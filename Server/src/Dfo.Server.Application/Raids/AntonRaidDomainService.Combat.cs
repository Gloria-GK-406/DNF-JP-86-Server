using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Raids;

internal sealed partial class AntonRaidDomainService
{
    private readonly HashSet<Guid> pendingReturns = [];
    private readonly Dictionary<(DungeonRunId Run, SelectedSessionIdentity Session), AntonRaidMemberDeparture> pendingMemberDepartures = [];

    public DungeonRunKind Kind => DungeonRunKind.AntonRaid;

    public async ValueTask<bool> IsRaidChannelAsync(SelectedSessionIdentity session, CancellationToken ct) =>
        (await channelEntry.ResolveAsync(session.ListenerPort, ct).ConfigureAwait(false)).Kind == ChannelEntryKind.Raid;

    public bool SupportsDungeon(int dungeonId) => raidRules.SupportsDungeon(dungeonId);

    public DungeonEntryIntent? Selection(SelectedSessionIdentity leader, int selection)
    {
        if (Of(leader) is not { Stage: AntonRaidStage.Attacking, Combat: { } combat } raid
            || RecruitmentChannel(leader) != raid.Channel || !combat.AttackOpenAt(clock.GetUtcNow()))
            return null;
        var squad = raid.Members.Single(member => member.Session == leader).Squad;
        if (squad == 0 || parties.FindByKey(raid.Lease.Keys[squad - 1])?.LeaderCharacterId != leader.CharacterId
            || selection != 0 && !WithoutAbortedEntries(combat).HasCapacity(selection, squad))
            return null;
        var identity = new AntonRaidEntryIdentity(raid.Lease.Owner, raid.Number, combat.Phase, combat.Generation, squad);
        return DungeonEntryIntent.AntonRaid(identity, selection == 0 ? null : selection);
    }

    public DungeonEntryIntent? HatcherySelection(SelectedSessionIdentity leader, int dungeonId)
    {
        if (dungeonRuns.Find(leader.CharacterId) is not { Layout.DungeonId: 218, Layout.Mode: DungeonRunMode.AntonRaid { Identity: var identity } } run
            || RaidSortie(run) is not { } owned || !owned.Combat.AttackOpenAt(clock.GetUtcNow())
            || Of(leader) != owned.Raid || !sessions.IsInWorld(leader)
            || owned.Sortie.Participants.GetValueOrDefault(leader.CharacterId) != leader
            || parties.FindByKey(owned.Raid.Lease.Keys[owned.Sortie.Squad - 1])?.LeaderCharacterId != leader.CharacterId
            || AntonRaidRuleService.HatcheryAt(run.CurrentRoomMapId) != dungeonId || !owned.Combat.HasCapacity(dungeonId, owned.Sortie.Squad, run.RunId))
            return null;
        return DungeonEntryIntent.AntonRaid(identity, dungeonId, run.RunId);
    }

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        var source = context.Facts.Intent.RaidTransferFrom;
        if (!context.IsRaidChannel || context.Facts.Intent is not { Kind: DungeonEntryIntentKind.AntonRaid, Raid: { } identity }
            || context.RequestMode != 0 && (context.RequestMode != 2 || source is null)
            || context.PracticeMode != 0 || context.MirrorQuestId is not null
            || CurrentCombat(identity) is not { Combat: { } live } raid
            || !live.AttackOpenAt(clock.GetUtcNow())
            || raidRules.Current.Hatcheries.Contains(context.DungeonId) != source.HasValue)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);

        var combat = WithoutAbortedEntries(live);
        if (!combat.HasCapacity(context.DungeonId, identity.Squad, source))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        var roster = SquadOrder(raid, identity.Squad, raid.Members.Where(member => member.Squad == identity.Squad
            && member.RejoinUntil is null && !raid.DeferredSquadMembers.Contains(member.Session.CharacterId))).ToArray();
        var party = parties.FindByKey(raid.Lease.Keys[identity.Squad - 1]);
        if (roster.Length == 0 || party is null || roster.Any(member => !sessions.IsInWorld(member.Session))
            || !context.Participants.OrderBy(member => member.Slot).Select(member => member.CharacterId)
                .SequenceEqual(roster.Select(member => member.Session.CharacterId))
            || context.Participants.SingleOrDefault(member => member.IsLeader)?.CharacterId != party.LeaderCharacterId)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (source is not null && (HatcherySelection(roster[0].Session, context.DungeonId) != context.Facts.Intent
            || context.Participants.Any(member => dungeonRuns.Find(member.CharacterId)?.RunId != source)))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);

        var rule = raidRules.Current.Phases[combat.Phase].Maps.Single(map => map.DungeonId == context.DungeonId);
        var facts = context.Facts with { Difficulty = rule.Difficulty, Intent = DungeonEntryIntent.Interactive };
        var result = await admission.ExecuteAsync(context with { Facts = facts },
            new() { EnforceOrdinaryDifficulty = false, ConsumesFatigue = false, RequiredItemsOverride = [], RaidTransferFrom = source },
            (receipt, token) => visits.ResolveAndEnterAsync(structures, context.Participants, context.DungeonId, facts, receipt,
                layout => layout with
                {
                    Mode = new DungeonRunMode.AntonRaid(identity),
                    ConsumesFatigue = false,
                    ChampionRates = null,
                }, token), ct).ConfigureAwait(false);
        if (result.Entry?.Visits.Values.FirstOrDefault()?.Run is { } run)
        {
            var next = combat.Copy();
            next.StartSortie(run.RunId, context.DungeonId, identity.Squad, isTransfer: source is not null);
            if (source is not null || combat != live)
                await PublishCombatAsync(raid, next, ct, participation: true).ConfigureAwait(false);
            StageCombat(raid, next);
        }
        return result;
    }

    private AntonRaidPhaseRuntime WithoutAbortedEntries(AntonRaidPhaseRuntime combat)
    {
        var abandoned = combat.Sorties.Values.Where(sortie => dungeonRuns.Find(sortie.Run) is null).ToArray();
        if (abandoned.Length == 0) return combat;
        var next = combat.Copy();
        foreach (var sortie in abandoned)
            next.Sorties.Remove(sortie.Squad);
        return next;
    }

    public async ValueTask RegisterReadyAsync(SelectedSessionIdentity session, DungeonRunState run, CancellationToken ct)
    {
        if (run.RoomVersion != 0 || !run.ReadyParticipants.Contains(session.CharacterId) || !sessions.IsInWorld(session)
            || RaidSortie(run) is not { } owned || Of(session) != owned.Raid
            || owned.Sortie.Participants.ContainsKey(session.CharacterId))
            return;
        var combat = owned.Combat.Copy();
        var participants = new Dictionary<CharacterId, SelectedSessionIdentity>(owned.Sortie.Participants)
        {
            [session.CharacterId] = session,
        };
        combat.Sorties[owned.Sortie.Squad] = owned.Sortie with { Participants = participants };
        combat.Participants.Add(session.CharacterId);
        await PublishCombatAsync(owned.Raid, combat, ct, participation: true).ConfigureAwait(false);
        StageCombat(owned.Raid, combat);
    }

    public async ValueTask CompleteClearAsync(DungeonActorDeathClaim death,
        IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken ct)
    {
        if (RaidSortie(death.Run) is not { } owned) return;
        var present = participants.Where(member => sessions.IsInWorld(member) && Of(member) == owned.Raid
                && owned.Sortie.Participants.GetValueOrDefault(member.CharacterId) == member
                && death.Run.ReadyParticipants.Contains(member.CharacterId))
            .Select(member => member.CharacterId).ToArray();
        var next = owned.Combat.Copy();
        var now = death.OccurredAt ?? clock.GetUtcNow();
        if (!next.Clear(owned.Sortie, present, now, Random.Shared.Next())) return;
        await CommitCombatResultAsync(owned.Raid, next, now, ct, participation: true).ConfigureAwait(false);
    }

    public async ValueTask RegisterDeathAsync(SelectedSessionIdentity session, DungeonRunState run, CancellationToken ct)
    {
        if (RaidSortie(run) is not { } owned || owned.Sortie.Participants.GetValueOrDefault(session.CharacterId) != session
            || !run.DeadParticipants.Contains(session.CharacterId))
            return;
        var next = owned.Combat.Copy();
        next.DeathCount++;
        await PublishCombatAsync(owned.Raid, next, ct, life: new(session.CharacterId, AntonRaidLifeState.Dead))
            .ConfigureAwait(false);
        StageCombat(owned.Raid, next);
    }

    public bool CanRevive(DungeonRunState run, CharacterId target) =>
        RaidSortie(run) is { } owned && owned.Sortie.Participants.ContainsKey(target)
        && owned.Sortie.UsedCoins < raidRules.Current.Phases[owned.Combat.Phase].Maps
            .Single(map => map.DungeonId == run.Layout.DungeonId).CoinLimit + owned.Sortie.BonusCoins;

    public async ValueTask<bool> StageRevivalAsync(DungeonRunState run, CharacterId target, CancellationToken ct)
    {
        if (!CanRevive(run, target) || RaidSortie(run) is not { } owned)
            return false;
        var next = owned.Combat.Copy();
        next.Sorties[owned.Sortie.Squad] = owned.Sortie with { UsedCoins = owned.Sortie.UsedCoins + 1 };
        await PublishCombatAsync(owned.Raid, next, ct, participation: true,
            life: new(target, AntonRaidLifeState.Revived)).ConfigureAwait(false);
        StageCombat(owned.Raid, next);
        return true;
    }

    public AntonRaidCombatSnapshot? CombatSnapshot(SelectedSessionIdentity session)
    {
        if (Of(session) is not { Combat: { } combat } raid) return null;
        var snapshot = combat.Snapshot();
        return raid.Stage == AntonRaidStage.Break && raid.Deadline is { } until
            ? snapshot with { Timers = [.. snapshot.Timers, BreakDeadline(raid, until)] }
            : snapshot;
    }

    public async ValueTask<bool> ChangeShieldAsync(SelectedSessionIdentity session, uint symbol, uint value,
        AntonRaidShieldOperation operation, CancellationToken ct)
    {
        if (dungeonRuns.Find(session.CharacterId) is not { } run || RaidSortie(run) is not { } owned
            || !sessions.IsInWorld(session)
            || owned.Sortie.Participants.GetValueOrDefault(session.CharacterId) != session
            || !owned.Combat.HasShieldSymbol(symbol))
            return false;
        var now = clock.GetUtcNow();
        var next = owned.Combat.Copy();
        if (!next.ApplyShield(value, operation, now, Random.Shared.Next()))
            return false;
        await PublishCombatAsync(owned.Raid, next, ct).ConfigureAwait(false);
        StageCombat(owned.Raid, next);
        return true;
    }

    public async ValueTask<bool> UseCombatBuffAsync(SelectedSessionIdentity session, byte type, byte? squad,
        IReadOnlyList<CharacterId> targets, CancellationToken ct)
    {
        if (Of(session) is not { Stage: AntonRaidStage.Attacking, Combat: { } combat } raid
            || !sessions.IsInWorld(session) || raidRules.Current.Buffs.SingleOrDefault(buff => buff.Type == type) is not { } rule)
            return false;
        var target = session.CharacterId;
        byte? targetSquad = null;
        if (rule.Party)
        {
            if (squad is not { } destination || destination is 0 or > AntonRaidRuleService.SquadCount
                || !combat.Sorties.TryGetValue(destination, out var sortie) || sortie.Participants.Count == 0
                || targets.Any(character => !sortie.Participants.ContainsKey(character)))
                return false;

            target = targets.Count == 0 ? sortie.Participants.Keys.First() : targets[0];
            targetSquad = destination;
        }
        var next = combat.Copy();
        if (!next.ActivateBuff(rule, targetSquad, target, clock.GetUtcNow()))
            return false;
        await PublishCombatAsync(raid, next, ct, participation: AntonRaidRuleService.GrantsExtraCoins(rule),
            buffsChanged: true).ConfigureAwait(false);
        StageCombat(raid, next);
        return true;
    }

    public async ValueTask<bool> ReportMonsterStatusAsync(SelectedSessionIdentity session, IReadOnlyList<uint> hitPoints,
        CancellationToken ct)
    {
        if (hitPoints.Count is < 1 or > 5 || dungeonRuns.Find(session.CharacterId) is not { } run
            || RaidSortie(run) is not { } owned || !owned.Combat.AttackOpenAt(clock.GetUtcNow()) || !sessions.IsInWorld(session)
            || owned.Sortie.Participants.GetValueOrDefault(session.CharacterId) != session)
            return false;
        var next = owned.Combat.Copy();
        next.Sorties[owned.Sortie.Squad] = owned.Sortie with { HitPoints = hitPoints.ToArray() };
        await PublishCombatAsync(owned.Raid, next, ct, participation: true).ConfigureAwait(false);
        StageCombat(owned.Raid, next);
        return true;
    }

    public IReadOnlyList<AntonRaidCombatDeadline> CombatDeadlines() => raids.Values.SelectMany(raid =>
        raid.Stage == AntonRaidStage.Attacking && raid.Combat is { } combat ? combat.Deadlines
        : raid.Stage == AntonRaidStage.Break && raid.Deadline is { } until
            ? [BreakDeadline(raid, until)] : []).ToArray();

    private AntonRaidCombatDeadline BreakDeadline(Recruitment raid, DateTimeOffset until) =>
        new(raid.Lease.Owner, raid.Number, raid.Phase, raid.PreparationGeneration, AntonRaidCombatDeadlineKind.Intermission,
            0, 0, until, (int)raidRules.Current.IntermissionTime.TotalSeconds);

    public IReadOnlyList<SelectedSessionIdentity> CombatParticipants(AntonRaidCombatDeadline deadline) =>
        raids.TryGetValue(deadline.Number, out var raid) && raid.Lease.Owner == deadline.Instance ? Audience(raid) : [];

    public async ValueTask<bool> AdvanceCombatAsync(AntonRaidCombatDeadline deadline, CancellationToken ct)
    {
        if (!raids.TryGetValue(deadline.Number, out var raid) || raid.Lease.Owner != deadline.Instance
            || raid is not { Stage: AntonRaidStage.Attacking, Combat: { } combat } || combat.Phase != deadline.Phase)
            return false;
        var now = clock.GetUtcNow();
        var next = combat.Copy();
        if (!next.Advance(deadline, now, Random.Shared.Next()))
            return false;
        await CommitCombatResultAsync(raid, next, now, ct, participation: next.Ends.Count > 0).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<AntonRaidStart> FinishIntermissionAsync(AntonRaidCombatDeadline deadline, CancellationToken ct)
    {
        if (deadline.Kind != AntonRaidCombatDeadlineKind.Intermission || !raids.TryGetValue(deadline.Number, out var raid)
            || raid.Lease.Owner != deadline.Instance || raid.Stage != AntonRaidStage.Break
            || raid.Phase != deadline.Phase || raid.PreparationGeneration != deadline.Generation
            || raid.Deadline != deadline.At || deadline.At > clock.GetUtcNow())
            return AntonRaidStart.Refused;
        var start = await StartAsync(raid.Leader.Session, automatic: true, ct).ConfigureAwait(false);
        if (!start.Started)
            StageAdmission(raid, () => raid.Deadline = null);
        return start;
    }

    public AntonRaidPhaseOutcome? PhaseOutcome(Guid instance, byte phase) =>
        raids.Values.FirstOrDefault(raid => raid.Lease.Owner == instance)?.Outcomes.GetValueOrDefault(phase);

    public (Guid Instance, byte Phase)? SettlementOf(SelectedSessionIdentity session) =>
        Of(session) is { Stage: AntonRaidStage.Settling } raid ? (raid.Lease.Owner, raid.Phase) : null;

    public DateTimeOffset? StageDeadline(SelectedSessionIdentity session) => Of(session)?.Deadline;

    public bool HasTerminalRaidCleanup() => raids.Values.Any(TerminalRaidHasReturned);

    public IReadOnlyList<AntonRaidRecruitmentChange> CleanTerminalRaids()
    {
        GameplayWriteAssertion.Writing(nameof(CleanTerminalRaids));
        var changes = new List<AntonRaidRecruitmentChange>();
        foreach (var raid in raids.Values.Where(TerminalRaidHasReturned).ToArray())
        {
            if (raid.Stage == AntonRaidStage.Failed)
            {
                foreach (var member in raid.Members)
                {
                    memberships.Remove(member.Session.CharacterId);
                    InvalidateApprovals(member.Session);
                }
                raid.Members.Clear();
                raids.Remove(raid.Number);
            }
            raid.PreparedSquads.Clear();
            raid.DeferredSquadMembers.Clear();
            var squads = ReplaceSquads(raid);
            ReturnLease(raid.Lease);
            raid.LeaseReturned = true;
            changes.Add(Change(raid) with { Squads = squads });
        }
        return changes;
    }

    private bool TerminalRaidHasReturned(Recruitment raid) =>
        raid.Stage is AntonRaidStage.Failed or AntonRaidStage.Completed && !raid.LeaseReturned && (raid.Combat is not { } combat
            || combat.Ends.All(run => !pendingReturns.Contains(run.Value) && dungeonRuns.Find(run) is null
                && pendingMemberDepartures.Keys.All(departure => departure.Run != run)));

    public IReadOnlyList<Guid> PendingRunReturns() => pendingReturns.ToArray();

    public bool CompleteRunReturn(Guid run) => pendingReturns.Remove(run);

    public void QueueMemberDeparture(AntonRaidMemberDeparture departure)
    {
        GameplayWriteAssertion.Writing(nameof(QueueMemberDeparture));
        var key = (departure.Run, departure.Session);
        if (!pendingMemberDepartures.TryGetValue(key, out var previous) || departure.EnableClear && !previous.EnableClear)
        {
            pendingMemberDepartures[key] = departure;
            deadlines.Wake(DungeonDeadlineKind.AntonRaid);
        }
    }

    public IReadOnlyList<AntonRaidMemberDeparture> PendingMemberDepartures() => pendingMemberDepartures.Values.ToArray();

    public bool CompleteMemberDeparture(AntonRaidMemberDeparture departure)
    {
        GameplayWriteAssertion.Writing(nameof(CompleteMemberDeparture));
        var key = (departure.Run, departure.Session);
        if (pendingMemberDepartures.GetValueOrDefault(key) != departure)
            return false;
        pendingMemberDepartures.Remove(key);
        if (dungeonRuns.Find(departure.Run) is null)
            OnRaidRunEnded(departure.Run);
        return true;
    }

    public AntonRaidRecruitmentChange? CompletePhaseSettlement(Guid instance, byte phase)
    {
        GameplayWriteAssertion.Writing(nameof(CompletePhaseSettlement));
        var raid = raids.Values.FirstOrDefault(raid => raid.Lease.Owner == instance);
        if (raid is not { Stage: AntonRaidStage.Settling } || raid.Phase != phase
            || !raid.Outcomes.TryGetValue(phase, out var outcome))
            return null;
        var stage = !outcome.Succeeded ? AntonRaidStage.Failed : phase == 0 ? AntonRaidStage.Break : AntonRaidStage.Completed;
        var change = StageChange(raid, stage, phase);
        StageAdmission(raid, () =>
        {
            raid.Stage = stage;
            raid.Deadline = stage == AntonRaidStage.Break ? clock.GetUtcNow() + raidRules.Current.IntermissionTime : null;
            if (raid.Combat is { } combat)
                pendingReturns.UnionWith(combat.Ends.Select(run => run.Value));
            deadlines.Wake(DungeonDeadlineKind.AntonRaid);
        });
        return change;
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<DungeonParticipantLeft> message, CancellationToken ct)
    {
        var departure = message.Payload;
        var runtime = dungeonRuns.Find(departure.RunId);
        if (runtime is null)
            OnRaidRunEnded(departure.RunId);
        var owned = raids.Values.Where(raid => raid.Stage == AntonRaidStage.Attacking && raid.Combat is not null)
            .SelectMany(raid => raid.Combat!.Sorties.Values.Select(sortie => (Raid: raid, Sortie: sortie)))
            .FirstOrDefault(pair => pair.Sortie.Run == departure.RunId);
        if (owned.Raid is null || owned.Raid.Combat is not { } combat)
            return;
        if (runtime is not null && !owned.Sortie.Participants.ContainsKey(departure.CharacterId))
            return;
        var next = combat.Copy();
        if (runtime is null)
        {
            next.Sorties.Remove(owned.Sortie.Squad);
        }
        else
        {
            var remaining = new Dictionary<CharacterId, SelectedSessionIdentity>(owned.Sortie.Participants);
            remaining.Remove(departure.CharacterId);
            next.Sorties[owned.Sortie.Squad] = owned.Sortie with { Participants = remaining };
        }
        await PublishCombatAsync(owned.Raid, next, ct, participation: true).ConfigureAwait(false);
        StageCombat(owned.Raid, next);
    }

    private Recruitment? CurrentCombat(AntonRaidEntryIdentity identity) =>
        identity.Squad is >= 1 and <= AntonRaidRuleService.SquadCount && raids.TryGetValue(identity.Number, out var raid)
        && raid.Lease.Owner == identity.Instance && raid.Stage == AntonRaidStage.Attacking
        && raid.Combat is { } combat && combat.Phase == identity.Phase && combat.Generation == identity.Generation ? raid : null;

    private (Recruitment Raid, AntonRaidPhaseRuntime Combat, AntonRaidPhaseRuntime.Sortie Sortie)? RaidSortie(DungeonRunState run) =>
        run.Layout.Mode is DungeonRunMode.AntonRaid { Identity: var identity }
        && CurrentCombat(identity) is { Combat: { } combat } raid
        && combat.Sorties.TryGetValue(identity.Squad, out var sortie) && sortie.Run == run.RunId ? (raid, combat, sortie) : null;

    private async ValueTask CommitCombatResultAsync(Recruitment raid, AntonRaidPhaseRuntime next, DateTimeOffset now,
        CancellationToken ct, bool participation)
    {
        AntonRaidPhaseOutcome? outcome = null;
        if (next.Succeeded is { } success)
        {
            var eligible = success ? next.ClearParticipants : next.Participants;
            var settlementMembers = raid.Members
                .Where(member => member.RejoinUntil is null || member.RejoinUntil > now).ToArray();
            var members = settlementMembers.Where(member => eligible.Contains(member.Session.CharacterId)).Select(member =>
                new AntonRaidPhaseParticipant(member.Session.AccountId, member.Session.CharacterId, member.Squad,
                    member == raid.Leader ? (byte)0 : member.Squad != 0
                    && SquadOrder(raid, member.Squad, settlementMembers.Where(candidate => candidate.Squad == member.Squad)).First() == member
                        ? (byte)1 : (byte)2)).ToArray();
            outcome = new(next.Instance, next.Number, next.Phase, success, next.DeathCount,
                next.StartedAt, now, Array.AsReadOnly(members), settlementMembers.Select(member => member.Session.CharacterId).ToArray());
        }
        await PublishCombatAsync(raid, next, ct, participation).ConfigureAwait(false);
        if (outcome is not null)
            await events.EnqueueAsync(new AntonRaidPhaseConcluded(outcome), ct).ConfigureAwait(false);
        StageCombat(raid, next, outcome);
    }

    private async ValueTask PublishCombatAsync(Recruitment raid, AntonRaidPhaseRuntime next, CancellationToken ct,
        bool participation = false, bool full = false, AntonRaidLifeChange? life = null, bool buffsChanged = false)
    {
        var change = new AntonRaidCombatChanged(next.Snapshot(), Audience(raid).Select(member => member.CharacterId).ToArray(),
            next.MapChanges.ToArray(),
            next.SymbolUpdates.ToArray(), next.TimerChanges.ToArray(), next.Ends.Select(run => run.Value).Distinct().ToArray(),
            participation, full)
        {
            ParticipationChanges = participation ? ParticipationChanges(raid.Combat, next) : [],
            LifeChange = life,
            BuffsChanged = buffsChanged,
            InfectedMapChanged = raid.Combat?.InfectedMap != next.InfectedMap,
        };
        _ = await events.EnqueueAsync(change, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<AntonRaidParticipationChange> ParticipationChanges(AntonRaidPhaseRuntime? previous,
        AntonRaidPhaseRuntime next)
    {
        if (previous is null) return [];
        var changes = new List<AntonRaidParticipationChange>();
        foreach (var old in previous.Sorties.Values)
        {
            var current = next.Sorties.GetValueOrDefault(old.Squad);
            var retained = current?.Run == old.Run ? current.Participants.Keys.ToHashSet() : [];
            var removed = old.Participants.Where(member => !retained.Contains(member.Key)).Select(member => member.Key).ToArray();
            if (removed.Length > 0)
                changes.Add(new(old.DungeonId, AntonRaidParticipationKind.Departed, removed));
        }
        foreach (var current in next.Sorties.Values)
        {
            var old = previous.Sorties.GetValueOrDefault(current.Squad);
            var newlyReady = current.Participants.Where(member => old?.Participants.GetValueOrDefault(member.Key) != member.Value)
                .Select(member => member.Key).ToArray();
            if (newlyReady.Length > 0)
                changes.Add(new(current.DungeonId, AntonRaidParticipationKind.Ready, newlyReady));
            if (current.Cleared && old?.Cleared != true)
                changes.Add(new(current.DungeonId, AntonRaidParticipationKind.Cleared, current.Participants.Keys.ToArray()));
        }
        return changes;
    }

    private void StageCombat(Recruitment raid, AntonRaidPhaseRuntime next, AntonRaidPhaseOutcome? outcome = null) =>
        StageAdmission(raid, () =>
        {
            raid.Combat = next;
            if (outcome is null)
            {
                pendingReturns.UnionWith(next.Ends.Select(run => run.Value));
            }
            else
            {
                raid.Stage = AntonRaidStage.Settling;
                raid.Outcomes.Add(outcome.Phase, outcome);
                raid.Deadline = null;
            }
            deadlines.Wake(DungeonDeadlineKind.AntonRaid);
        });

    private IReadOnlyList<SelectedSessionIdentity> Audience(Recruitment raid) =>
        raid.Members.Select(member => member.Session).Where(sessions.IsInWorld).ToArray();
}
