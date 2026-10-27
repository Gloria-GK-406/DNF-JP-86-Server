using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
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

/// <summary>Owns the tournament lifecycle; the shared dungeon run owns its single Cell.</summary>
internal sealed class TournamentDungeonDomainService(ITournamentDungeonRuleService rules,
    IDungeonAdmissionDomainService admission, IDungeonRunDomainService runs,
    ICharacterDomainService characters, ICharacterFatigueDomainService fatigue,
    IItemDeliveryDomainService delivery, IQuestProgressDomainService quests,
    ISelectedSessionDomainService sessions, IDomainTransactionAccessor transactions, TimeProvider clock,
    ICharacterGameplayFactDomainService gameplay, IQuestEnemyDropDomainService questLoot,
    IUsageQuotaDomainService quotas, IDailyResetRuleService daily)
    : ITournamentDungeonDomainService, IDomainEventSubscriber<DungeonParticipantLeft>
{
    private readonly IDungeonRunDomainService dungeonRuns = runs;
    private readonly Lock gate = new();
    private readonly Dictionary<CharacterId, TournamentDungeonState> states = [];
    public bool Supports(int dungeonId) => rules.IsSupported(dungeonId);
    private DateOnly Period() => DateOnly.FromDateTime(daily.PeriodStart(clock.GetUtcNow()).ToOffset(TimeSpan.FromHours(8)).DateTime);
    public async ValueTask<IReadOnlyList<TournamentQuotaSnapshot>> ReadQuotaAsync(CharacterId characterId, CancellationToken ct)
    {
        var period = Period();
        var result = new List<TournamentQuotaSnapshot>();
        foreach (var id in rules.DungeonIds)
        {
            var definition = await rules.GetDefinitionAsync(id, ct).ConfigureAwait(false);
            if (definition.DailyEntries is not { } maximum) continue;
            var used = (await quotas.ReadAsync(characterId, UsageQuotaKey.Tournament(id, period), ct).ConfigureAwait(false))?.UsedCount ?? 0;
            result.Add(new(id, period, used, Math.Max(0, maximum - used), maximum));
        }
        return result.AsReadOnly();
    }
    public TournamentDungeonState? Find(CharacterId characterId)
    {
        lock (gate)
        {
            var state = states.GetValueOrDefault(characterId);
            return state is not null && dungeonRuns.Find(characterId)?.RunId == state.RunId ? state : null;
        }
    }

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(TournamentDungeonDomainService) + "." + nameof(EnterAsync));
        if (!Supports(context.DungeonId) || context.Participants.Count != 1 || context.RequestMode != 0
            || context.PracticeMode != 0 || context.MirrorQuestId is not null
            || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        var player = context.Participants[0];
        if (dungeonRuns.Find(player.CharacterId) is not null)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
        var selected = context.Participants
            .Select(member => sessions.TryGetInWorld(member.CharacterId, out var inWorld) ? inWorld : null).ToArray();
        if (selected.Any(value => value is null)) return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        TournamentPlan plan;
        try { plan = await rules.PrepareAsync(context.DungeonId, unchecked((uint)Random.Shared.Next()),
            selected.Select(value => value!.WireUserId).ToHashSet(), ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        var facts = context.Facts with { Difficulty = (DungeonDifficulty)plan.Definition.Difficulty };
        return await admission.ExecuteAsync(context with { Facts = facts }, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = plan.Definition.ConsumesFatigue, RequiredItemsOverride = plan.Definition.RequiredItems,
            Quota = plan.Definition.DailyEntries is { } maximum
                ? new(UsageQuotaFamily.TournamentDungeon, Period(), maximum, context.DungeonId) : null,
        }, async (receipt, token) =>
        {
            if (admission.BeginEntry(receipt, context.Participants, context.DungeonId) is { } refusal) return refusal;
            var runId = DungeonRunId.New();
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(context.DungeonId, 0, 1, 1, cell, cell, plan.Definition.MapId,
                unchecked((int)plan.Seed), new Dictionary<MazeCell, DungeonCellDefinition>
                { [cell] = DungeonCellDefinition.Fixed(plan.Definition.MapId) }, (byte)plan.Definition.BasisLevel,
                ConsumesFatigue: false) { Tournament = plan };
            var actors = new List<DungeonCellActorState>();
            foreach (var actor in plan.PathActors)
            {
                var pending = await questLoot.DrawAsync(new(actor.IsAiCharacter ? QuestEnemyType.APC
                    : QuestEnemyType.Monster, actor.Code, actor.Sequence,
                    context.DungeonId, facts.Difficulty, unchecked((int)plan.Seed), cell.X, cell.Y, plan.Definition.MapId),
                    new(player.AccountId,player.CharacterId), token).ConfigureAwait(false);
                actors.Add(new(actor.Sequence, new(actor.Code, actor.Level, actor.Type, RandomDropCount: 0) { IsHostile = true },
                    actor.IsAiCharacter, false, pending.ToArray(), !actor.IsAiCharacter));
            }
            if (plan.Definition.RoundFatigue > 0)
                await fatigue.SpendAsync(player.CharacterId, checked((ushort)plan.Definition.RoundFatigue), token).ConfigureAwait(false);
            var change = dungeonRuns.PreparePartyEntry(context.Participants, layout, facts.Difficulty,
                new(cell, plan.Definition.MapId, plan.PathActors[0].Sequence, actors, []), runId, clock.GetUtcNow());
            Put(new(runId, player.CharacterId, plan));
            return DungeonEntryAttempt.Accepted(new(layout,
                new Dictionary<CharacterId, DungeonRoomVisit>
                {
                    [player.CharacterId] = new(change.Run, true, [], [], plan.PathActors[0].Sequence),
                }, change));
        }, ct).ConfigureAwait(false);
    }

    public bool CanKill(DungeonRunState run, ushort actor)
    {
        var state = Find(run.Participant?.CharacterId ?? run.Participants[0].CharacterId);
        return state is { IsFinished: false } && state.RunId == run.RunId
            && run.ReadyParticipants.Contains(state.CharacterId) && !run.DeadParticipants.Contains(state.CharacterId)
            && state.Plan.PathActors[state.CompletedRounds].Sequence == actor
            && run.CurrentCellState.FindActor(actor) is { IsDead: false };
    }

    public async ValueTask<TournamentActionResult?> AdvanceAsync(SelectedSessionIdentity session,
        DungeonRunId runId, ushort actor, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(TournamentDungeonDomainService) + "." + nameof(AdvanceAsync));
        var run = CurrentRun(session, runId);
        if (run is null || !CanKill(run, actor)) return null;
        var state = Find(session.CharacterId)!;
        var next = state with { CompletedRounds = state.CompletedRounds + 1 };
        if (next.CompletedRounds == 4) return await FinishAsync(session, next, TournamentStatus.Champion, ct).ConfigureAwait(false);
        if (next.Plan.Definition.RoundFatigue > 0)
            await fatigue.SpendAsync(session.CharacterId, checked((ushort)next.Plan.Definition.RoundFatigue), ct).ConfigureAwait(false);
        Put(next);
        return new(next) { Fatigue = await fatigue.BalanceOfAsync(session.CharacterId, ct).ConfigureAwait(false) };
    }

    public async ValueTask<TournamentActionResult?> EliminateAsync(SelectedSessionIdentity session, DungeonRunId runId, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(TournamentDungeonDomainService) + "." + nameof(EliminateAsync));
        var run = CurrentRun(session, runId);
        var state = Find(session.CharacterId);
        if (run is null || state is null || state.IsFinished || !run.DeadParticipants.Contains(session.CharacterId)) return null;
        return await FinishAsync(session, state, TournamentStatus.Eliminated, ct).ConfigureAwait(false);
    }

    private async ValueTask<TournamentActionResult> FinishAsync(SelectedSessionIdentity session,
        TournamentDungeonState state, TournamentStatus status, CancellationToken ct)
    {
        var character = characters.Get(session.CharacterId);
        var progress = await character.ProgressAsync(ct).ConfigureAwait(false) ?? throw new InvalidDataException("Tournament character is unavailable.");
        var experience = state.Plan.Definition.ExperienceByRound[state.CompletedRounds];
        var award = await character.AddExpAsync(new ExperienceBreakdown(Base: experience), progress, ct).ConfigureAwait(false);
        var next = state with { Status = status, Rewards = rules.Rewards(state.Plan, state.CompletedRounds, progress.Level) };
        QuestProgressReport? report = null;
        if (status == TournamentStatus.Champion)
        {
            report = await quests.ReportAsync(session.AccountId, session.CharacterId,
                [new QuestDungeonCleared(state.Plan.Definition.DungeonId, (DungeonDifficulty)state.Plan.Definition.Difficulty,
                    state.RunId, clock.GetUtcNow())], ct).ConfigureAwait(false);
            await gameplay.RecordDungeonCompletedAsync(new(session.CharacterId, state.Plan.Definition.DungeonId,
                state.Plan.Definition.Difficulty), ct).ConfigureAwait(false);
        }
        Put(next);
        return new(next) { Experience = award, SkillPoints = await character.RemainingSkillPointsAsync(ct).ConfigureAwait(false),
            Quests = report?.NotificationSnapshot, QuestChanges = report?.Changes ?? [] };
    }

    public async ValueTask<TournamentActionResult?> SelectRewardAsync(SelectedSessionIdentity session,
        DungeonRunId runId, byte row, byte index, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(TournamentDungeonDomainService) + "." + nameof(SelectRewardAsync));
        var state = Find(session.CharacterId);
        if (CurrentRun(session, runId) is null || state is not { IsFinished: true } || row >= state.Rewards.Count
            || index >= state.Rewards[row].Count) return null;
        // A retransmitted selection acknowledges the frozen row without granting it twice.
        if (state.Selections[row].HasValue) return new(state);
        var reward = state.Rewards[row][index];
        ItemDeliveryReceipt? receipt = null;
        int? gold = null;
        if (reward.ItemId > 0 && reward.Count > 0)
        {
            receipt = await delivery.DeliverAsync(session.AccountId, session.CharacterId, [new(reward.ItemId, reward.Count)],
                "武斗大会奖励", ct, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
        }
        else if (reward.ItemId == 0 && reward.Count > 0)
            gold = await characters.Get(session.CharacterId).AddGoldAsync(reward.Count, ct).ConfigureAwait(false);
        var selections = state.Selections.ToArray();
        selections[row] = index;
        var next = state with { Selections = Array.AsReadOnly(selections) };
        QuestProgressReport? report = receipt is not null
            ? await quests.ReportAsync(session.AccountId, session.CharacterId, [new QuestHoldingsChanged()], ct).ConfigureAwait(false) : null;
        Put(next);
        return new(next) { Delivery = receipt, GoldBalance = gold, Quests = report?.NotificationSnapshot,
            QuestChanges = report?.Changes ?? [] };
    }

    public bool CanRevive(CharacterId characterId, DungeonRunId runId)
    {
        var state = Find(characterId);
        return state is not null && state.RunId == runId && !state.IsFinished && state.Revivals < state.Plan.Definition.CoinLimit;
    }

    public IReadOnlyList<TournamentDungeonState> PendingRewards(DateTimeOffset now)
    {
        lock (gate)
            return states.Values.Where(x => x.IsFinished && x.RewardSelectionEndsAt <= now
                && x.Rewards.Where((row, index) => row.Count > 0 && !x.Selections[index].HasValue).Any()).ToArray();
    }

    public bool BeginRewardSelection(SelectedSessionIdentity session, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(TournamentDungeonDomainService) + "." + nameof(BeginRewardSelection));
        var state = Find(session.CharacterId);
        if (CurrentRun(session, runId) is null || state is not { IsFinished: true }) return false;
        if (state.RewardSelectionEndsAt is null)
            Put(state with { RewardSelectionEndsAt = clock.GetUtcNow() + rules.RewardSelectionWindow });
        return true;
    }

    public bool StageRevival(CharacterId characterId, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(TournamentDungeonDomainService) + "." + nameof(StageRevival));
        var state = Find(characterId);
        if (state is null || state.RunId != runId || state.IsFinished || state.Revivals >= state.Plan.Definition.CoinLimit) return false;
        Put(state with { Revivals = state.Revivals + 1 });
        return true;
    }

    private DungeonRunState? CurrentRun(SelectedSessionIdentity session, DungeonRunId runId) =>
        sessions.IsInWorld(session) && dungeonRuns.Find(session.CharacterId) is { } run
        && run.RunId == runId && run.Layout.Tournament is not null ? run : null;

    private void Put(TournamentDungeonState state)
    {
        var tx = transactions.Current ?? throw new InvalidOperationException("Tournament mutations require a transaction.");
        tx.Enlist(this, () => new Changes(this)).Put(state);
    }

    public ValueTask HandleAsync(DomainEventEnvelope<DungeonParticipantLeft> message, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(TournamentDungeonDomainService) + "." + nameof(HandleAsync));
        lock (gate)
            if (states.GetValueOrDefault(message.Payload.CharacterId)?.RunId == message.Payload.RunId)
                states.Remove(message.Payload.CharacterId);
        return ValueTask.CompletedTask;
    }

    private sealed class Changes(TournamentDungeonDomainService owner) : ITransactionParticipant
    {
        private readonly Dictionary<CharacterId, TournamentDungeonState?> before = [];
        private readonly Dictionary<CharacterId, TournamentDungeonState> after = [];
        public bool CommitCanFail => false;
        public void Put(TournamentDungeonState state)
        {
            lock (owner.gate) before.TryAdd(state.CharacterId, owner.states.GetValueOrDefault(state.CharacterId));
            after[state.CharacterId] = state;
        }
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            lock (owner.gate)
                if (before.Any(x => owner.states.GetValueOrDefault(x.Key) != x.Value))
                    throw new InvalidOperationException("Tournament state changed before commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            lock (owner.gate)
                foreach (var (character, state) in after)
                {
                    owner.states[character] = state;
                    if (state.IsFinished && owner.dungeonRuns.Find(character) is { ClearOpen: false } run && run.RunId == state.RunId)
                        owner.dungeonRuns.TryOpenClear(character, state.RunId, run.RoomVersion);
                }
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
