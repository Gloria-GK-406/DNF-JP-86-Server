using Dfo.Server.Abstractions.Execution;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>Shared operations only; each concrete domain owns its own state store.</summary>
internal sealed class AltarDungeonEngine(IAltarDungeonRuleService rules,
    IDungeonAdmissionDomainService admission, IDungeonStructureRuleService layouts, IDungeonVisitDomainService visits,
    IDungeonRunDomainService runs, IUsageQuotaDomainService quotas, ICharacterDomainService characters,
    ISelectedSessionDomainService sessions, IMonsterDropDomainService loot, IItemDeliveryDomainService delivery,
    IDailyResetRuleService daily, IEventService events, IDomainTransactionAccessor transactions, TimeProvider clock,
    ICharacterGameplayFactDomainService gameplay)
{
    public DateOnly Period() => DateOnly.FromDateTime(daily.PeriodStart(clock.GetUtcNow()).ToOffset(TimeSpan.FromHours(8)).DateTime);
    public AltarDungeonState? Find(AltarStateStore store, DungeonRunId runId) => runs.Find(runId) is not null ? store.Find(runId) : null;
    public IReadOnlyList<AltarDeadline> Pending(AltarStateStore store) => store.All()
        .Where(x => x.NextDeadline is not null && runs.Find(x.RunId) is not null)
        .Select(x => new AltarDeadline(x.RunId, x.RoomVersion, x.Generation, x.NextDeadline!.Value)).ToArray();
    public async ValueTask<AltarQuotaSnapshot> ReadQuotaAsync(int dungeonId, CharacterId character, CancellationToken ct)
    {
        var definition = await rules.GetDefinitionAsync(dungeonId, ct).ConfigureAwait(false);
        var period = Period();
        var used = (await quotas.ReadAsync(character, UsageQuotaKey.Altar(dungeonId, period), ct).ConfigureAwait(false))?.UsedCount ?? 0;
        return new(dungeonId, period, used, Math.Max(0, definition.DailyEntries - used), definition.DailyEntries);
    }
    public async ValueTask<DungeonEntryAttempt> EnterAsync(AltarStateStore store, int dungeonId, DungeonEntryContext context, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(AltarDungeonEngine) + "." + nameof(EnterAsync));
        if (context.DungeonId != dungeonId || context.Participants.Count is < 1 or > 4 || context.RequestMode != 0
            || context.PracticeMode != 0 || context.MirrorQuestId is not null || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (context.Participants.Any(x => runs.Find(x.CharacterId) is not null))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
        var at = clock.GetUtcNow();
        if (!rules.IsOpen(dungeonId, at)) return DungeonEntryAttempt.Refused(DungeonEntryFailure.OutsideOpeningHours);
        AltarDungeonDefinition definition;
        DungeonLayout? layout;
        var period = Period();
        var players = new List<AltarParticipantEntry>();
        try
        {
            definition = await rules.GetDefinitionAsync(dungeonId, ct).ConfigureAwait(false);
            foreach (var member in context.Participants)
            {
                var progress = await characters.Get(member.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
                if (progress is null || progress.Level < definition.MinimumLevel)
                    return new(null, DungeonEntryFailure.LevelTooLow, member.Slot);
                var used = (await quotas.ReadAsync(member.CharacterId, UsageQuotaKey.Altar(dungeonId, period), ct).ConfigureAwait(false))?.UsedCount ?? 0;
                if (used >= definition.DailyEntries) return new(null, DungeonEntryFailure.DailyEntryLimitReached, member.Slot);
                players.Add(new(member, progress.Level, checked(used + 1), rules.AdmissionGold(definition, progress.Level, used)));
            }
            layout = await layouts.ResolveAsync(dungeonId, context.Facts, Random.Shared.Next(), ct).ConfigureAwait(false);
            if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
            if (!definition.Maps.Any(x => x.MapId == layout.StartRoomMapId && x.Rounds.Count > 0))
                throw new InvalidDataException("Altar initial map is not backed by a round plan.");
            layout = layout with { Altar = new(definition.Kind, period), ConsumesFatigue = false };
        }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        var frozenLayout = layout;
        return await admission.ExecuteAsync(context, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false,
            Quota = new(UsageQuotaFamily.AltarDungeon, period, definition.DailyEntries, dungeonId),
            GoldCosts = players.ToDictionary(x => x.Participant.CharacterId, x => x.AdmissionGold),
        }, async (receipt, token) =>
        {
            var entered = await visits.EnterGrantedPartyAsync(context.Participants, frozenLayout, context.Facts, receipt, token).ConfigureAwait(false);
            if (entered.Entry?.Change?.Run is { } run)
                store.Put(new(run.RunId, definition, players.ToArray(), at, run.RoomVersion, run.CurrentRoomMapId), transactions);
            return entered;
        }, ct).ConfigureAwait(false);
    }
    public ValueTask<AltarActionResult?> ReadyAsync(AltarStateStore store, SelectedSessionIdentity session,
        DungeonRunId runId, long roomVersion, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(AltarDungeonEngine) + "." + nameof(ReadyAsync));
        ct.ThrowIfCancellationRequested();
        var run = runs.Find(runId);
        var state = Find(store, runId);
        if (state is null || run is null || run.RoomVersion != roomVersion || !sessions.IsInWorld(session)
            || !run.ReadyParticipants.Contains(session.CharacterId)
            || !run.Participants.Any(x => x.CharacterId == session.CharacterId && x.IsLeader)
            || state.PreparationRequested
            || state.Status is not (AltarStatus.AwaitingReady or AltarStatus.AwaitingRoom)) return ValueTask.FromResult<AltarActionResult?>(null);
        if (state.Status == AltarStatus.AwaitingRoom)
        {
            if (run.RoomVersion <= state.RoomVersion || run.ClearedCells.Contains(run.CurrentCell)) return ValueTask.FromResult<AltarActionResult?>(null);
            state = state with { RoomVersion = run.RoomVersion, MapId = run.CurrentRoomMapId, LocalRound = 0, Status = AltarStatus.AwaitingReady };
        }
        else if (state.RoomVersion != roomVersion) return ValueTask.FromResult<AltarActionResult?>(null);
        if (run.Participants.Any(x => !run.ReadyParticipants.Contains(x.CharacterId)))
        {
            var waiting = state with { PreparationRequested = true, Generation = checked(state.Generation + 1),
                NextDeadline = clock.GetUtcNow().AddMilliseconds(250) };
            store.Put(waiting, transactions);
            return ValueTask.FromResult<AltarActionResult?>(new(waiting));
        }
        return ValueTask.FromResult<AltarActionResult?>(StartRound(store, state, clock.GetUtcNow(), false));
    }
    private AltarActionResult StartRound(AltarStateStore store, AltarDungeonState state, DateTimeOffset now, bool difficultyResolved)
    {
        var map = state.Definition.Maps.Single(x => x.MapId == state.MapId);
        var difficulty = state.Definition.Kind == AltarDungeonKind.Endless ? (byte)0
            : state.LocalRound == 0 || state.LocalRound == map.Rounds.Count - 1 ? (byte)1 : state.Difficulty;
        var waves = rules.PlanRound(map, state.LocalRound, difficulty, state.CompletedRounds,
            checked((byte)state.Definition.BasisLevel), state.NextActorSequence);
        var started = now + rules.Policy.RoundInterval;
        var next = state with { Status = AltarStatus.Fighting, PreparationRequested = false, Generation = checked(state.Generation + 1), Difficulty = difficulty,
            Waves = waves, NextWave = 0, RoundStartedAt = started, NextDeadline = started.AddMilliseconds(waves[0].OffsetMilliseconds),
            NextActorSequence = checked(state.NextActorSequence + waves.Sum(x => x.Actors.Count)) };
        store.Put(next, transactions);
        return new(next) { RoundStarted = true, DifficultyResolved = difficultyResolved };
    }
    public async ValueTask<AltarActionResult?> AdvanceAsync(AltarStateStore store, DungeonRunId runId, long roomVersion,
        long generation, DateTimeOffset now, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(AltarDungeonEngine) + "." + nameof(AdvanceAsync));
        var run = runs.Find(runId);
        var state = Find(store, runId);
        if (run is null || state is null || state.Generation != generation || state.RoomVersion != roomVersion
            || run.RoomVersion != roomVersion || state.NextDeadline is null || state.NextDeadline > now) return null;
        if (state.Status == AltarStatus.AwaitingReady && state.PreparationRequested)
        {
            if (run.Participants.All(x => run.ReadyParticipants.Contains(x.CharacterId)))
                return StartRound(store, state, now, false);
            var waiting = state with { Generation = checked(state.Generation + 1), NextDeadline = now.AddMilliseconds(250) };
            store.Put(waiting, transactions);
            return new(waiting);
        }
        if (state.Status == AltarStatus.ChoosingDifficulty)
            return StartRound(store, state with { Difficulty = 1 }, now, true);
        if (state.Status == AltarStatus.Ranking)
        {
            var next = state with { Status = AltarStatus.RewardDisplay, Generation = checked(state.Generation + 1), NextDeadline = now + rules.Policy.RewardDisplayWindow };
            store.Put(next, transactions);
            return new(next) { ShowRewards = true };
        }
        if (state.Status == AltarStatus.RewardDisplay) return await AwardAsync(store, state, ct).ConfigureAwait(false);
        if (state.Status != AltarStatus.Fighting) return null;
        // A shared batch must not be published into a participant's loading screen.
        if (run.Participants.Any(x => !run.ReadyParticipants.Contains(x.CharacterId)))
        {
            var waiting = state with { Generation = checked(state.Generation + 1), NextDeadline = now.AddMilliseconds(250) };
            store.Put(waiting, transactions);
            return new(waiting);
        }
        // Preserve each wire spawn group's compatibility tail even when several deadlines elapsed.
        var due = state.Waves.Skip(state.NextWave).Take(1).Where(x => state.RoundStartedAt!.Value.AddMilliseconds(x.OffsetMilliseconds) <= now).ToArray();
        if (due.Length > 0)
        {
            var spawns = due.SelectMany(x => x.Actors).ToArray();
            var actors = await CreateActorsAsync(run, spawns, ct).ConfigureAwait(false);
            if (!runs.StageDynamicCell(runId, roomVersion, run.CurrentCellState.Revision, actors, true)) return null;
            var waveIndex = state.NextWave + due.Length;
            var next = state with { NextWave = waveIndex, Generation = checked(state.Generation + 1), NextDeadline = waveIndex < state.Waves.Count
                ? state.RoundStartedAt!.Value.AddMilliseconds(state.Waves[waveIndex].OffsetMilliseconds) : now.AddMilliseconds(250) };
            store.Put(next, transactions);
            return new(next) { Spawns = spawns, SpawnTail = due[0].SpawnTail };
        }
        if (state.NextWave < state.Waves.Count || run.CurrentCellState.Actors.Any(x => x.BlocksClear && !x.IsDead))
        {
            var next = state with { NextDeadline = now.AddMilliseconds(250), Generation = checked(state.Generation + 1) };
            store.Put(next, transactions);
            return new(next);
        }
        var map = state.Definition.Maps.Single(x => x.MapId == state.MapId);
        state = state with { CompletedRounds = state.CompletedRounds + 1, LocalRound = state.LocalRound + 1,
            DifficultyPoints = state.DifficultyPoints + (state.Definition.Kind == AltarDungeonKind.Ultimate
                ? state.Definition.Rewards.UltimateDifficultyPoints[state.Difficulty - 1] : 0) };
        if (state.CompletedRounds >= state.Definition.MaxRounds)
            return await FinishAsync(store, state, run, false, now, ct).ConfigureAwait(false);
        if (state.LocalRound >= map.Rounds.Count)
        {
            if (!runs.StageDynamicCell(runId, roomVersion, run.CurrentCellState.Revision, [], false)) return null;
            await ClearMapAsync(run, now, ct).ConfigureAwait(false);
            var next = state with { Status = AltarStatus.AwaitingRoom, Generation = checked(state.Generation + 1), NextDeadline = null };
            store.Put(next, transactions);
            return new(next) { RoomCleared = true };
        }
        if (state.Definition.Kind == AltarDungeonKind.Ultimate && state.LocalRound != map.Rounds.Count - 1)
        {
            var next = state with { Status = AltarStatus.ChoosingDifficulty, Generation = checked(state.Generation + 1),
                NextDeadline = now + rules.Policy.DifficultySelectionWindow };
            store.Put(next, transactions);
            return new(next) { DifficultyPrompt = true };
        }
        return StartRound(store, state, now, false);
    }
    public ValueTask<AltarActionResult?> SelectDifficultyAsync(AltarStateStore store, SelectedSessionIdentity session,
        DungeonRunId runId, long roomVersion, long generation, byte difficulty, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(AltarDungeonEngine) + "." + nameof(SelectDifficultyAsync));
        ct.ThrowIfCancellationRequested();
        var run = runs.Find(runId);
        var state = Find(store, runId);
        if (run is null || state?.Status != AltarStatus.ChoosingDifficulty || state.RoomVersion != roomVersion
            || run.RoomVersion != roomVersion || state.Generation != generation || difficulty is not (1 or 2)
            || state.NextDeadline <= clock.GetUtcNow() || !sessions.IsInWorld(session)
            || !run.Participants.Any(x => x.CharacterId == session.CharacterId && x.IsLeader)) return ValueTask.FromResult<AltarActionResult?>(null);
        return ValueTask.FromResult<AltarActionResult?>(StartRound(store, state with { Difficulty = difficulty }, clock.GetUtcNow(), true));
    }
    public async ValueTask<AltarActionResult?> FailAsync(AltarStateStore store, DungeonRunId runId, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(AltarDungeonEngine) + "." + nameof(FailAsync));
        var run = runs.Find(runId);
        var state = Find(store, runId);
        if (run is null || state is null || state.Status is AltarStatus.Ranking or AltarStatus.RewardDisplay or AltarStatus.Awarded) return null;
        return await FinishAsync(store, state, run, true, clock.GetUtcNow(), ct).ConfigureAwait(false);
    }
    public async ValueTask<AltarActionResult?> SettleBeforeExitAsync(AltarStateStore store, DungeonRunId runId, CancellationToken ct, CharacterId? participant = null)
    {
        GameplayWriteAssertion.Writing(nameof(AltarDungeonEngine) + "." + nameof(SettleBeforeExitAsync));
        var run = runs.Find(runId);
        var state = Find(store, runId);
        if (run is null || state is null || state.Status == AltarStatus.Awarded) return null;
        if (participant is { } member && (!run.Participants.Any(x => x.CharacterId == member) || state.SettledParticipants.Contains(member))) return null;
        if (participant is { } leaving && run.Participants.Count > 1)
        {
            if (state.Status is not (AltarStatus.Ranking or AltarStatus.RewardDisplay))
                state = state with { Rewards = rules.Rewards(state, run.Layout.Seed) };
            return await AwardAsync(store, state, ct, leaving).ConfigureAwait(false);
        }
        if (state.Status is not (AltarStatus.Ranking or AltarStatus.RewardDisplay))
            state = (await FinishAsync(store, state, run, true, clock.GetUtcNow(), ct).ConfigureAwait(false)).State;
        return await AwardAsync(store, state, ct).ConfigureAwait(false);
    }
    private async ValueTask<AltarActionResult> FinishAsync(AltarStateStore store, AltarDungeonState state,
        DungeonRunState run, bool failed, DateTimeOffset now, CancellationToken ct)
    {
        var finished = state with { Failed = failed, FinishedAt = now, Status = AltarStatus.Ranking,
            Generation = checked(state.Generation + 1), NextDeadline = now + rules.Policy.RankingWindow };
        finished = finished with { Rewards = rules.Rewards(finished, run.Layout.Seed) };
        if (!failed)
        {
            if (!runs.StageDynamicCell(run.RunId, run.RoomVersion, run.CurrentCellState.Revision, [], false))
                throw new InvalidOperationException("Altar final map changed before settlement.");
            await ClearMapAsync(run, now, ct).ConfigureAwait(false);
            await events.EnqueueAsync(new AltarChallengeCompleted(run.RunId, run.Layout.DungeonId, run.Difficulty, now,
                run.Participants.ToImmutableArray()), ct).ConfigureAwait(false);
            foreach (var participant in run.Participants)
            {
                await gameplay.RecordDungeonCompletedAsync(new(participant.CharacterId, run.Layout.DungeonId, (int)run.Difficulty), ct).ConfigureAwait(false);
            }
        }
        store.Put(finished, transactions);
        return new(finished) { ShowRanking = true, RoomCleared = !failed };
    }
    private async ValueTask<AltarActionResult> AwardAsync(AltarStateStore store, AltarDungeonState state, CancellationToken ct, CharacterId? participant = null)
    {
        var run = runs.Find(state.RunId) ?? throw new InvalidOperationException("Altar run disappeared before award.");
        var awards = new List<AltarParticipantAward>();
        foreach (var reward in state.Rewards.Where(x => run.Participants.Any(y => y.CharacterId == x.Participant.CharacterId)
            && !state.SettledParticipants.Contains(x.Participant.CharacterId)
            && (participant is null || x.Participant.CharacterId == participant)))
        {
            var character = characters.Get(reward.Participant.CharacterId);
            var standing = await character.ProgressAsync(ct).ConfigureAwait(false) ?? throw new InvalidDataException("Altar character is unavailable.");
            var exp = reward.Experience > 0 ? await character.AddExpAsync(new ExperienceBreakdown(Base: reward.Experience), standing, ct).ConfigureAwait(false) : null;
            var receipt = reward.Items.Count > 0 ? await delivery.DeliverAsync(reward.Participant.AccountId, reward.Participant.CharacterId,
                reward.Items.Select(x => new ItemDeliveryGrant(x.ItemId, x.Count) { Durability = x.Durability }).ToArray(),
                state.Definition.Kind == AltarDungeonKind.Endless ? "无尽的祭坛奖励" : "极限的祭坛奖励", ct,
                ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false) : null;
            var gold = reward.Gold > 0 ? await character.AddGoldAsync(reward.Gold, ct).ConfigureAwait(false) : await character.GoldAsync(ct).ConfigureAwait(false);
            awards.Add(new(reward.Participant, exp, await character.RemainingSkillPointsAsync(ct).ConfigureAwait(false), receipt, gold));
        }
        var next = state with { Status = participant is null ? AltarStatus.Awarded : state.Status,
            Generation = checked(state.Generation + 1), NextDeadline = participant is null ? null : state.NextDeadline,
            SettledParticipants = state.SettledParticipants.Union(awards.Select(x => x.Participant.CharacterId)) };
        store.Put(next, transactions);
        return new(next) { Awards = awards };
    }
    private ValueTask<Guid> ClearMapAsync(DungeonRunState run, DateTimeOffset now, CancellationToken ct) =>
        events.EnqueueAsync(new DungeonMapCleared(run.RunId, run.Layout.DungeonId, run.Difficulty, run.CurrentCell,
            run.CurrentRoomMapId, run.RoomVersion, now, run.Participants.ToImmutableArray()), ct);
    private async ValueTask<IReadOnlyList<DungeonCellActorState>> CreateActorsAsync(DungeonRunState run, IReadOnlyList<AltarSpawn> spawns, CancellationToken ct)
    {
        var participants = run.Participants.Select(x => new MonsterDropParticipant(x.AccountId, x.CharacterId)).ToImmutableArray();
        var owner = run.Participants.First(x => x.IsLeader).CharacterId;
        var actors = new List<DungeonCellActorState>();
        foreach (var spawn in spawns)
        {
            var dropContext = new MonsterDropContext(run.RunId, run.Layout.DungeonId, run.Layout.Seed, run.CurrentCell,
                run.CurrentRoomMapId, run.Difficulty, spawn.Sequence, spawn.Level, spawn.ActorType, owner, participants) { RandomDropCount = 0 };
            var pending = new List<RolledDrop>(await loot.GenerateGroundAsync(spawn.MonsterCode, dropContext, ct).ConfigureAwait(false));
            foreach (var participant in participants)
                pending.AddRange(await loot.GeneratePersonalAsync(spawn.MonsterCode, participant.CharacterId, dropContext, ct).ConfigureAwait(false));
            actors.Add(new(spawn.Sequence, new(spawn.MonsterCode, spawn.Level, spawn.ActorType, RandomDropCount: 0)
                { IsHostile = true }, false, false, pending));
        }
        return actors;
    }
    public bool CanRevive(AltarStateStore store, CharacterId character, DungeonRunId runId)
    {
        var state = Find(store, runId);
        return state is not null && state.Participants.Any(x => x.Participant.CharacterId == character)
            && state.Status is not (AltarStatus.Ranking or AltarStatus.RewardDisplay or AltarStatus.Awarded)
            && state.Revivals.GetValueOrDefault(character) < state.Definition.CoinLimit;
    }
    public bool StageRevival(AltarStateStore store, CharacterId character, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(AltarDungeonEngine) + "." + nameof(StageRevival));
        if (!CanRevive(store, character, runId)) return false;
        var state = Find(store, runId)!;
        var revivals = state.Revivals.ToDictionary(x => x.Key, x => x.Value);
        revivals[character] = revivals.GetValueOrDefault(character) + 1;
        store.Put(state with { Revivals = revivals }, transactions);
        return true;
    }
}

/// <summary>One domain's transactionally committed RunId states; no cross-domain global dictionary.</summary>
internal sealed class AltarStateStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<DungeonRunId, AltarDungeonState> states = [];
    public AltarDungeonState? Find(DungeonRunId runId) { lock (gate) return states.GetValueOrDefault(runId); }
    public IReadOnlyList<AltarDungeonState> All() { lock (gate) return states.Values.ToArray(); }
    public void Remove(DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(AltarStateStore) + "." + nameof(Remove));
        lock (gate) states.Remove(runId);
    }
    public void Put(AltarDungeonState state, IDomainTransactionAccessor transactions)
    {
        GameplayWriteAssertion.Writing(nameof(AltarStateStore) + "." + nameof(Put));
        var transaction = transactions.Current ?? throw new InvalidOperationException("Altar changes require a transaction.");
        if (transaction.IsReadOnly) throw new InvalidOperationException("Altar changes require a writable transaction.");
        transaction.Enlist(this, () => new Changes(this)).Put(state);
    }
    private sealed class Changes(AltarStateStore owner) : ITransactionParticipant
    {
        private readonly Dictionary<DungeonRunId, AltarDungeonState?> before = [];
        private readonly Dictionary<DungeonRunId, AltarDungeonState> after = [];
        public bool CommitCanFail => false;
        public void Put(AltarDungeonState state)
        {
            lock (owner.gate) before.TryAdd(state.RunId, owner.states.GetValueOrDefault(state.RunId));
            after[state.RunId] = state;
        }
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            lock (owner.gate)
                if (before.Any(x => owner.states.GetValueOrDefault(x.Key) != x.Value)) throw new InvalidOperationException("Altar state changed before commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            lock (owner.gate) foreach (var (run, state) in after) owner.states[run] = state;
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
