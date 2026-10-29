using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class GuardianAltarDomainService(IGuardianAltarRuleService rules, IGuardianAltarProgressDomainStore progress,
    GuardianAltarRuntimeStore state, ISelectedSessionDomainService sessions, ICharacterDomainService characters,
    IDungeonAdmissionDomainService admission, IDungeonVisitDomainService visits, IDungeonRunDomainService runs,
    IDungeonEntryIntentDomainService intents, IItemDeliveryDomainService delivery, IDailyResetRuleService daily,
    IDomainTransactionAccessor transactions, TimeProvider clock, IEventManagementDomainService events, IPartyService parties,
    IDungeonParticipationDomainService participation, IOnlineCharacterRuntimeDomainService onlineCharacters, IExpertStoreDomainService stores) : IGuardianAltarDomainService,
    IDomainEventSubscriber<DungeonParticipantLeft>
{
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);
    public GuardianAltarProgressView DescribeProgress(GuardianAltarProgress value) => rules.DescribeProgress(value);
    public GuardianAltarSpawnView DescribeSpawn(GuardianAltarSpawn spawn) => rules.DescribeSpawn(spawn);
    private async ValueTask<bool> IsOpenAsync(SelectedSessionIdentity session, CancellationToken ct) =>
        (await events.GetEventInfoListAsync(session.AccountId, clock.GetUtcNow(), ct).ConfigureAwait(false)).Any(x => x.EventId == rules.EventId);
    private DateOnly Period() => DateOnly.FromDateTime(daily.PeriodStart(clock.GetUtcNow()).ToOffset(TimeSpan.FromHours(8)).DateTime);
    private static void Writing(string method) => GameplayWriteAssertion.Writing(nameof(GuardianAltarDomainService) + "." + method);
    public async ValueTask<GuardianAltarProgress> ReadProgressAsync(CharacterId character, CancellationToken ct) =>
        rules.RefreshTickets(await progress.ReadAsync(character, ct).ConfigureAwait(false) ?? rules.Initial(Period()), Period());
    private async ValueTask<GuardianAltarProgress> LoadAsync(CharacterId character, CancellationToken ct)
    {
        var old = await progress.ReadAsync(character, ct).ConfigureAwait(false);
        var value = rules.RefreshTickets(old ?? rules.Initial(Period()), Period());
        if (old is null) await progress.SaveAsync(character, value, null, ct).ConfigureAwait(false);
        else if (value != old) { value = value with { Revision = checked(old.Revision + 1) }; await progress.SaveAsync(character, value, old.Revision, ct).ConfigureAwait(false); }
        return value;
    }
    private async ValueTask<GuardianAltarProgress> SaveAsync(CharacterId character, GuardianAltarProgress old, GuardianAltarProgress value, CancellationToken ct)
    {
        value = value with { Revision = checked(old.Revision + 1) };
        await progress.SaveAsync(character, value, old.Revision, ct).ConfigureAwait(false);
        return value;
    }
    public async ValueTask<GuardianAltarProgress?> OpenAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        Writing(nameof(OpenAsync));
        if (!sessions.IsInWorld(session) || runs.Find(session.CharacterId) is not null
            || participation.Find(session.CharacterId) is not null || onlineCharacters.StandingOf(session) is null
            || stores.HasOpenStore(session.CharacterId)
            || !parties.FindParty(new(session.AccountId, session.CharacterId, session.WireUserId, null)).IsVirtual
            || !await IsOpenAsync(session, ct).ConfigureAwait(false)
            || ((await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false))?.Level ?? 0) < rules.MinimumLevel) return null;
        var value = await LoadAsync(session.CharacterId, ct).ConfigureAwait(false);
        state.Put(session.CharacterId, new(session, true, null));
        intents.Record(session, DungeonEntryIntent.Interactive);
        return value;
    }
    public bool IsSelecting(SelectedSessionIdentity session) => sessions.IsInWorld(session)
        && state.Find(session.CharacterId) is { Selecting: true } owner && owner.Session == session;
    public GuardianAltarRunState? Find(DungeonRunId run) => runs.Find(run) is { Layout.Mode: DungeonRunMode.Guardian } dungeon
        && state.Find(dungeon.Participants.Single().CharacterId)?.Run is { } current && current.RunId == run ? current : null;
    public IReadOnlyList<GuardianAltarRunState> Pending() => state.All().Where(x => x.Run?.Deadline is not null)
        .Select(x => x.Run!).ToArray();
    public void PruneStaleSessions()
    {
        Writing(nameof(PruneStaleSessions));
        foreach (var owner in state.All())
            if (!sessions.IsInWorld(owner.Session) || owner.Run is { } run && runs.Find(run.RunId) is null)
                state.Put(owner.Session.CharacterId, null);
    }
    private GuardianAltarRunState? Owned(SelectedSessionIdentity session) => sessions.IsInWorld(session)
        && state.Find(session.CharacterId) is { } owner && owner.Session == session
        && owner.Run is { } run && runs.Find(session.CharacterId) is { } current
        && current.RunId == run.RunId && current.RoomVersion == run.RoomVersion ? run : null;
    private void Put(GuardianAltarRunState run) => state.Put(run.Session.CharacterId, new(run.Session, false, run));

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        Writing(nameof(EnterAsync));
        // A21 sends special-map mode 3 for altar entry; mode 0 remains valid for ordinary entry.
        if (!Supports(context.DungeonId) || context.Participants.Count != 1 || context.RequestMode is not (0 or 3) || context.PracticeMode != 0
            || context.MirrorQuestId is not null || context.Facts.Intent.Kind != DungeonEntryIntentKind.Interactive)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite,
                $"guardian request mode={context.RequestMode} practice={context.PracticeMode} participants={context.Participants.Count} mirror={context.MirrorQuestId} intent={context.Facts.Intent.Kind}");
        var member = context.Participants[0]; var session = sessions.FindByCharacter(member.CharacterId);
        if (session is null || !IsSelecting(session)) return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite,
            $"guardian selection missing: session={session is not null} selecting={session is not null && IsSelecting(session)}");
        if (!await IsOpenAsync(session, ct).ConfigureAwait(false)) return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite, "guardian activity disabled");
        if (runs.Find(member.CharacterId) is not null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
        var old = await LoadAsync(member.CharacterId, ct).ConfigureAwait(false);
        var plan = rules.Admission(old, context.DungeonId, (GuardianAltarDifficulty)context.Facts.Difficulty);
        if (plan is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DifficultyNotUnlocked);
        // An unlocked tower at level zero is a shop offer, not an owned map actor (70 getTowerInfo).
        if (old.FreeTickets <= 0 && old.ExtraTickets <= 0) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DailyEntryLimitReached);
        int[] maps = [plan.Map.MapId];
        var cells = maps.Select((map, index) => (cell: new MazeCell(checked((byte)index), 0), map))
            .ToDictionary(x => x.cell, x => DungeonCellDefinition.Fixed(x.map));
        var layout = new DungeonLayout(context.DungeonId, 0, checked((byte)maps.Length), 1, new(0, 0),
            new(checked((byte)(maps.Length - 1)), 0), plan.Map.MapId, Random.Shared.Next(), cells,
            BasisLevel: checked((byte)plan.BasisLevel), ExperienceWeight: 0, ConsumesFatigue: false) { Mode = new DungeonRunMode.Guardian(plan), NoGiveupPenalty = true };
        return await admission.ExecuteAsync(context, new() { ConsumesFatigue = false, EnforceOrdinaryDifficulty = false }, async (receipt, token) =>
        {
            var entered = await visits.EnterGrantedPartyAsync(context.Participants, layout, context.Facts, receipt, token).ConfigureAwait(false);
            if (entered.Entry?.Visits[member.CharacterId].Run is not { } run) return entered;
            Put(new(run.RunId, session, plan, old.TicketPeriod, GuardianAltarStatus.Loading, run.RoomVersion, 1,
                clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(2), 0, 0, 0, 0, run.CurrentCellState.Actors.Select(x => (int)x.Sequence).DefaultIfEmpty(0).Max() + 1,
                [], 0, ImmutableDictionary<byte, DateTimeOffset>.Empty));
            return entered;
        }, ct).ConfigureAwait(false);
    }
    public ValueTask<GuardianAltarActionResult?> ReadyAsync(SelectedSessionIdentity session, DungeonRunId run, CancellationToken ct)
    {
        Writing(nameof(ReadyAsync)); ct.ThrowIfCancellationRequested();
        var current = Owned(session);
        if (current is not { Status: GuardianAltarStatus.Loading } || current.RunId != run
            || !runs.Find(run)!.ReadyParticipants.Contains(session.CharacterId)) return ValueTask.FromResult<GuardianAltarActionResult?>(null);
        var now = clock.GetUtcNow();
        var next = current with { Status = GuardianAltarStatus.Fighting, At = now, Deadline = now.AddSeconds(1),
            Generation = checked(current.Generation + 1), PlannedSpawns = rules.PlanPhase(current.Admission, 0, 0) };
        Put(next); return ValueTask.FromResult<GuardianAltarActionResult?>(new(next));
    }
    public ValueTask<GuardianAltarActionResult?> AdvanceAsync(DungeonRunId runId, long generation, DateTimeOffset now, CancellationToken ct)
    {
        Writing(nameof(AdvanceAsync));
        return AdvanceCoreAsync(runId, generation, now, ct, false);
    }
    private async ValueTask<GuardianAltarActionResult?> AdvanceCoreAsync(DungeonRunId runId, long generation, DateTimeOffset now, CancellationToken ct, bool force)
    {
        var current = Find(runId);
        if (current is null || current.Generation != generation || current.Deadline is null || !force && current.Deadline > now) return null;
        if (Owned(current.Session) is null) { state.Put(current.Session.CharacterId, null); return null; }
        var run = runs.Find(runId)!;
        if (current.Status == GuardianAltarStatus.Loading) return End(current, GuardianAltarStatus.Lost, now);
        if (current.Status == GuardianAltarStatus.Ending) return End(current, GuardianAltarStatus.Lost, now);
        if (current.Status == GuardianAltarStatus.BetweenRounds)
        {
            var nextPlan = rules.Admission(await LoadAsync(current.Session.CharacterId, ct).ConfigureAwait(false),
                current.Admission.Stage.DungeonId, current.Admission.Difficulty, current.Admission.SurvivalRound + 1)!
                with { Towers = current.Admission.Towers };
            var next = current with { Admission = nextPlan, Status = GuardianAltarStatus.Fighting,
                At = now, Deadline = now.AddSeconds(1), Generation = checked(current.Generation + 1),
                Phase = 0, PhaseElapsedMilliseconds = 0, PlannedSpawns = rules.PlanPhase(nextPlan, 0, 0), NextSpawn = 0 };
            Put(next); return new(next) { RoundStarted = true };
        }
        if (current.Status != GuardianAltarStatus.Fighting) return null;
        var center = run.CurrentCellState.Actors.SingleOrDefault(x => (x.Actor.Declaration?.Code ?? x.Actor.Code) == current.Admission.Map.CenterMonsterCode);
        if (run.DeadParticipants.Contains(current.Session.CharacterId))
            return BeginFailure(current, GuardianAltarEndReason.CharacterDied, now);
        if (center is null || center.IsDead) return BeginFailure(current, GuardianAltarEndReason.CenterDestroyed, now);
        var elapsed = checked((int)Math.Min(int.MaxValue, Math.Max(0, (now - current.At).TotalMilliseconds)));
        var energy = (int)Math.Min(rules.MaximumEnergy(current.EnergyLevel), current.Energy + (long)elapsed * rules.EnergyPerSecond(current.EnergyLevel) / 1000);
        var phaseElapsed = checked(current.PhaseElapsedMilliseconds + elapsed); var phase = current.Phase;
        var planned = current.PlannedSpawns; var nextSpawn = current.NextSpawn;
        var spawns = ImmutableArray.CreateBuilder<(ushort Sequence, GuardianAltarSpawn Spawn)>();
        var sequence = current.NextActorSequence;
        while (phase < current.Admission.Map.Phases.Length)
        {
            while (nextSpawn < planned.Length && planned[nextSpawn].OffsetMilliseconds <= phaseElapsed)
            {
                if (sequence > ushort.MaxValue) return End(current, GuardianAltarStatus.Lost, now);
                spawns.Add((checked((ushort)sequence++), planned[nextSpawn++]));
            }
            var duration = current.Admission.Map.Phases[phase].DurationMilliseconds;
            if (phaseElapsed < duration) break;
            phaseElapsed -= duration; phase++; nextSpawn = 0;
            planned = phase < current.Admission.Map.Phases.Length ? rules.PlanPhase(current.Admission, phase,
                run.CurrentCellState.Actors.Count(x => !x.IsDead && x.BlocksClear) + spawns.Count) : [];
        }
        var finished = phase == current.Admission.Map.Phases.Length && spawns.Count == 0
            && !run.CurrentCellState.Actors.Any(x => !x.IsDead && x.BlocksClear);
        if (!runs.StageDynamicCell(runId, current.RoomVersion, run.CurrentCellState.Revision,
            spawns.Select(x => Actor(x.Sequence, x.Spawn)).ToArray(), true)) return null;
        if (finished)
        {
            var result = await SettleAsync(current with { Energy = energy, Phase = phase,
                PhaseElapsedMilliseconds = phaseElapsed, NextActorSequence = sequence, PlannedSpawns = planned, NextSpawn = nextSpawn }, now, ct).ConfigureAwait(false);
            return result;
        }
        var advanced = current with { At = now, Deadline = now.AddSeconds(1), Generation = checked(current.Generation + 1),
            Phase = phase, PhaseElapsedMilliseconds = phaseElapsed, Energy = energy, NextActorSequence = sequence,
            PlannedSpawns = planned, NextSpawn = nextSpawn };
        Put(advanced); return new(advanced) { Spawns = spawns.ToImmutable() };
    }
    private static DungeonCellActorState Actor(ushort sequence, GuardianAltarSpawn spawn) => new(sequence,
        new(spawn.MonsterCode, spawn.Level, spawn.Rank, RandomDropCount: 0) { IsHostile = spawn.Hostile }, false, false, [], IsMonster: false);
    private GuardianAltarActionResult End(GuardianAltarRunState current, GuardianAltarStatus status, DateTimeOffset now)
    {
        // 70Reference RegistTimerEPLPReturnVillage schedules 0xC350 ms after the result.
        var next = current with { Status = status, Deadline = now.AddSeconds(50), At = now, Generation = checked(current.Generation + 1) };
        Put(next); return new(next) { StageEnded = true };
    }
    private GuardianAltarActionResult BeginFailure(GuardianAltarRunState current, GuardianAltarEndReason reason, DateTimeOffset occurredAt)
    {
        // Native RegistTimerEPLP waits 1000 ms from the failure event, then starts the 50-second return.
        var next = current with { Status = GuardianAltarStatus.Ending, EndReason = reason, At = occurredAt,
            Deadline = occurredAt.AddSeconds(1), Generation = checked(current.Generation + 1) };
        Put(next); return new(next);
    }
    private async ValueTask<GuardianAltarActionResult> SettleAsync(GuardianAltarRunState current, DateTimeOffset now, CancellationToken ct)
    {
        var character = current.Session.CharacterId;
        var old = await LoadAsync(character, ct).ConfigureAwait(false);
        if (!await progress.TryRecordSettlementAsync(character, current.RunId.Value, current.Admission.SurvivalRound, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Guardian altar settlement was already committed without its terminal runtime state.");
        if (old.FreeTickets <= 0 && old.ExtraTickets <= 0) throw new InvalidOperationException("Guardian clear has no reward entitlement.");
        var cleared = rules.RecordClear(old, current.Admission);
        var updated = await SaveAsync(character, old, old.FreeTickets > 0 ? cleared with { FreeTickets = old.FreeTickets - 1 }
            : cleared with { ExtraTickets = old.ExtraTickets - 1 }, ct).ConfigureAwait(false);
        var rewards = rules.Rewards(current.Admission);
        var receipt = await DeliverAsync(current.Session, rewards, ct).ConfigureAwait(false);
        var continuing = current.Admission.Stage.Survival && current.Admission.SurvivalRound < current.Admission.Stage.SurvivalMaps.Length
            && updated.FreeTickets + updated.ExtraTickets > 0;
        var next = current with { Status = continuing ? GuardianAltarStatus.BetweenRounds : GuardianAltarStatus.Won,
            Deadline = continuing ? now.AddSeconds(3) : now.AddSeconds(50), At = now, Generation = checked(current.Generation + 1) };
        Put(next); return new(next) { StageEnded = true, Progress = updated, Delivery = receipt, Rewards = rewards,
            StarDelta = updated.GameStars - old.GameStars, NewSurvivalRecord = updated.BestSurvival > old.BestSurvival,
            UnlockedPurchases = updated.Purchases.Where(x => !old.Purchases.Any(y => y.Kind == x.Kind && y.Id == x.Id)).ToImmutableArray() };
    }
    private ValueTask<ItemDeliveryReceipt> DeliverAsync(SelectedSessionIdentity session, ImmutableArray<GuardianAltarReward> rewards, CancellationToken ct) =>
        delivery.DeliverAsync(session.AccountId, session.CharacterId, rewards.Select(x => new ItemDeliveryGrant(x.ItemId, x.Count)).ToArray(),
            "守护者祭坛奖励", ct, ItemDeliveryMode.InventoryThenMail);
    public async ValueTask<GuardianAltarActionResult?> SummonAsync(SelectedSessionIdentity session, byte slot, int resourceId, int expectedEnergyLevel, CancellationToken ct)
    {
        Writing(nameof(SummonAsync)); var current = Owned(session); var now = clock.GetUtcNow();
        if (current is not { Status: GuardianAltarStatus.Fighting } || current.EnergyLevel != expectedEnergyLevel
            || current.UnitCooldowns.GetValueOrDefault(slot) > now) return null;
        var unit = rules.SlotUnit(await LoadAsync(session.CharacterId, ct).ConfigureAwait(false), slot);
        if (unit is null || unit.ResourceId != resourceId || unit.NeedPoint is null || current.Energy < checked(unit.NeedPoint.Value * 1000)) return null;
        // ClientStatic 01106FF8 dispatches the accepted slot's item effect. The resource is a STK,
        // not a monster code; emitting an enemy-spawn NOTI here would create a second, invalid unit.
        var next = current with { Energy = current.Energy - unit.NeedPoint.Value * 1000,
            UnitCooldowns = current.UnitCooldowns.SetItem(slot, now.AddMilliseconds(unit.SummonCooldownMilliseconds)),
            Generation = checked(current.Generation + 1) };
        Put(next); return new(next);
    }
    public ValueTask<GuardianAltarActionResult?> UpgradeEnergyAsync(SelectedSessionIdentity session, int expectedLevel, CancellationToken ct)
    {
        Writing(nameof(UpgradeEnergyAsync)); ct.ThrowIfCancellationRequested(); var current = Owned(session);
        if (current is not { Status: GuardianAltarStatus.Fighting } || current.EnergyLevel != expectedLevel
            || current.EnergyLevel >= rules.MaximumEnergyLevel
            || current.Energy < rules.UpgradeEnergyCost(current.EnergyLevel)) return ValueTask.FromResult<GuardianAltarActionResult?>(null);
        var next = current with { Energy = current.Energy - rules.UpgradeEnergyCost(current.EnergyLevel),
            EnergyLevel = current.EnergyLevel + 1, Generation = checked(current.Generation + 1) };
        Put(next); return ValueTask.FromResult<GuardianAltarActionResult?>(new(next));
    }
    public async ValueTask<GuardianAltarActionResult?> PauseAsync(SelectedSessionIdentity session, bool pause, CancellationToken ct)
    {
        Writing(nameof(PauseAsync)); ct.ThrowIfCancellationRequested(); var current = Owned(session); var now = clock.GetUtcNow();
        if (current is null || (pause ? current.Status != GuardianAltarStatus.Fighting : current.Status != GuardianAltarStatus.Paused))
            return null;
        GuardianAltarActionResult? advanced = null;
        if (pause)
        {
            advanced = await AdvanceCoreAsync(current.RunId, current.Generation, now, ct, true).ConfigureAwait(false);
            if (advanced is null || advanced.State.Status != GuardianAltarStatus.Fighting) return advanced;
            current = advanced.State;
        }
        var delta = now - current.At;
        var next = current with { Status = pause ? GuardianAltarStatus.Paused : GuardianAltarStatus.Fighting, At = now,
            Deadline = pause ? null : now.AddSeconds(1), Generation = checked(current.Generation + 1),
            UnitCooldowns = pause ? current.UnitCooldowns : current.UnitCooldowns.ToImmutableDictionary(x => x.Key, x => x.Value + delta) };
        Put(next); return advanced is null ? new(next) : advanced with { State = next };
    }
    public ValueTask<GuardianAltarActionResult?> AbandonAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        Writing(nameof(AbandonAsync)); ct.ThrowIfCancellationRequested(); var current = Owned(session);
        return ValueTask.FromResult(current is null || current.Status is GuardianAltarStatus.Won or GuardianAltarStatus.Lost or GuardianAltarStatus.Abandoned
            ? null : End(current, GuardianAltarStatus.Abandoned, clock.GetUtcNow()));
    }
    public ValueTask<bool> ReportDeathAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, ushort actor, CancellationToken ct)
    {
        Writing(nameof(ReportDeathAsync)); ct.ThrowIfCancellationRequested(); var current = Owned(session);
        if (current is not { Status: GuardianAltarStatus.Fighting } || current.RunId != runId || current.RoomVersion != roomVersion)
            return ValueTask.FromResult(false);
        var claim = runs.TryClaimActorDeath(session.CharacterId, actor);
        if (claim is null) return ValueTask.FromResult(false);
        try { transactions.Current!.Enlist((this, runId, roomVersion, actor), () => new DeathCommit(runs, claim)); }
        catch { runs.ReleaseActorDeath(claim); throw; }
        var deadActor = claim.Run.CurrentCellState.FindActor(actor)?.Actor;
        if ((deadActor?.Declaration?.Code ?? deadActor?.Code) == current.Admission.Map.CenterMonsterCode)
            _ = BeginFailure(current, GuardianAltarEndReason.CenterDestroyed, clock.GetUtcNow());
        return ValueTask.FromResult(true);
    }
    public ValueTask<bool> ReportCharacterDeathAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion,
        DateTimeOffset occurredAt, CancellationToken ct)
    {
        Writing(nameof(ReportCharacterDeathAsync)); ct.ThrowIfCancellationRequested(); var current = Owned(session);
        if (current is null || current.Status is not (GuardianAltarStatus.Fighting or GuardianAltarStatus.Paused)
            || current.RunId != runId || current.RoomVersion != roomVersion
            || !runs.Find(runId)!.DeadParticipants.Contains(session.CharacterId)) return ValueTask.FromResult(false);
        _ = BeginFailure(current, GuardianAltarEndReason.CharacterDied, occurredAt);
        return ValueTask.FromResult(true);
    }
    private sealed class DeathCommit(IDungeonRunDomainService runs, DungeonActorDeathClaim claim) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            if (!runs.IsActorDeathClaimCurrent(claim)) throw new InvalidOperationException("Guardian actor death ownership changed.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            _ = runs.CommitActorDeathChange(claim) ?? throw new InvalidOperationException("Prepared guardian actor death could not commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct) { runs.ReleaseActorDeath(claim); return ValueTask.CompletedTask; }
    }
    private async ValueTask<GuardianAltarProgress?> ChangeAsync(SelectedSessionIdentity session,
        Func<GuardianAltarProgress, GuardianAltarProgress?> change, CancellationToken ct)
    {
        if (!IsSelecting(session) || runs.Find(session.CharacterId) is not null) return null;
        var old = await LoadAsync(session.CharacterId, ct).ConfigureAwait(false); var updated = change(old);
        return updated is null ? null : await SaveAsync(session.CharacterId, old, updated, ct).ConfigureAwait(false);
    }
    public ValueTask<GuardianAltarProgress?> BuyAsync(SelectedSessionIdentity session, GuardianAltarShopKind kind, int id, int firstLevel, int lastLevel, CancellationToken ct)
    {
        Writing(nameof(BuyAsync));
        return ChangeAsync(session, x => rules.BuyLevels(x, kind, id, firstLevel, lastLevel), ct);
    }
    public ValueTask<GuardianAltarProgress?> SetSlotAsync(SelectedSessionIdentity session, byte slot, GuardianAltarShopKind kind, int id, CancellationToken ct)
    { Writing(nameof(SetSlotAsync)); return ChangeAsync(session, x => rules.SetSlot(x, slot, kind, id), ct); }
    public ValueTask<GuardianAltarProgress?> ExchangeSlotsAsync(SelectedSessionIdentity session, byte first, byte second, int expectedFirstId, int expectedSecondId, CancellationToken ct)
    {
        Writing(nameof(ExchangeSlotsAsync));
        return ChangeAsync(session, x => (x.Slots.SingleOrDefault(y => y.Index == first)?.Id ?? 0) == expectedFirstId
            && (x.Slots.SingleOrDefault(y => y.Index == second)?.Id ?? 0) == expectedSecondId ? rules.ExchangeSlots(x, first, second) : null, ct);
    }
    public async ValueTask<GuardianAltarProgress?> ResetStarsAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        Writing(nameof(ResetStarsAsync));
        if (!IsSelecting(session) || runs.Find(session.CharacterId) is not null) return null;
        var old = await LoadAsync(session.CharacterId, ct).ConfigureAwait(false);
        if (await characters.Get(session.CharacterId).TrySpendGoldAsync(rules.ResetGold(old), ct).ConfigureAwait(false) is null) return null;
        return await SaveAsync(session.CharacterId, old, rules.ResetStars(old), ct).ConfigureAwait(false);
    }
    public async ValueTask<(GuardianAltarProgress Progress, ItemDeliveryReceipt Delivery)?> ClaimAchievementAsync(SelectedSessionIdentity session, int id, CancellationToken ct)
    {
        Writing(nameof(ClaimAchievementAsync));
        if (!IsSelecting(session) || runs.Find(session.CharacterId) is not null) return null;
        var old = await LoadAsync(session.CharacterId, ct).ConfigureAwait(false);
        var reward = rules.ClaimableAchievement(old, id); if (reward is null) return null;
        var updated = await SaveAsync(session.CharacterId, old, old with { ClaimedAchievements = old.ClaimedAchievements.Add(id) }, ct).ConfigureAwait(false);
        return (updated, await DeliverAsync(session, reward.Rewards, ct).ConfigureAwait(false));
    }
    public ValueTask HandleAsync(DomainEventEnvelope<DungeonParticipantLeft> message, CancellationToken ct)
    {
        Writing(nameof(HandleAsync));
        foreach (var owner in state.All().Where(x => x.Run?.RunId == message.Payload.RunId))
            if (runs.Find(message.Payload.RunId) is null) state.Put(owner.Session.CharacterId, null);
        return ValueTask.CompletedTask;
    }
}
