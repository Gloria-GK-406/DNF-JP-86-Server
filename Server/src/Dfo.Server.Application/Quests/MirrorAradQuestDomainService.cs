using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Quests;

internal sealed class MirrorAradQuestDomainService(IMirrorAradQuestDomainStore store,
    IMirrorAradQuestRuleService rules, IDungeonRunDomainService runs,
    ICharacterQuestLogDomainService logs, ICharacterDomainService characters,
    IInventoryEffectPreflightDomainService preview,
    TimeProvider clock, IEventService events, IUsageQuotaDomainService quotas, IDailyResetRuleService reset) : IMirrorAradQuestDomainService,
    IDomainEventSubscriber<DungeonParticipantLeft>, IDomainEventSubscriber<DungeonMapCleared>
{
    private DungeonRunState? Authorized(AccountId account, CharacterId character, int quest)
    {
        var run = runs.Find(character);
        return run?.Layout.Mode is DungeonRunMode.Mirror { Plan.QuestId: var entered } && entered == quest
            && run.Participants.Any(x => x.CharacterId == character && x.AccountId == account) ? run : null;
    }
    public async ValueTask<QuestAcceptance> AcceptAsync(AccountId account, CharacterId character, ushort quest, CancellationToken ct)
    {
        var run = Authorized(account, character, quest);
        var objective = run is null ? null : await rules.ResolveAsync(run.Layout.DungeonId, quest, ct).ConfigureAwait(false);
        if (run is null || objective is null) return QuestAcceptance.Refused(QuestRefusal.UnsupportedObjective);
        var state = await store.ReadAsync(character, run.RunId, ct).ConfigureAwait(false);
        if (state is { GivenUp: true } or { RewardClaimed: true }) return QuestAcceptance.Refused(QuestRefusal.AlreadyCleared);
        if (state is not null) return QuestAcceptance.Refused(QuestRefusal.AlreadyCarried);
        state = new(character, run.RunId, quest, clock.GetUtcNow(), objective.InitialTrigger);
        await store.WriteAsync(account, state, ct).ConfigureAwait(false);
        await ChangedAsync(character, ct).ConfigureAwait(false);
        return QuestAcceptance.Taken(short.MaxValue, unchecked((int)state.TriggerValue));
    }
    public async ValueTask<QuestAdvance> TriggerAsync(AccountId account, CharacterId character, ushort quest, CancellationToken ct)
    {
        var run = Authorized(account, character, quest);
        var state = run is null ? null : await store.ReadAsync(character, run.RunId, ct).ConfigureAwait(false);
        if (state is null || state.GivenUp || state.RewardClaimed) return QuestAdvance.Refused;
        var objective = await rules.ResolveAsync(run!.Layout.DungeonId, quest, ct).ConfigureAwait(false);
        var trigger = objective is not null && rules.CanFinish(objective, state, run) ? 0 : state.TriggerValue;
        return new(QuestAdvanceVerdict.NotEvaluable, new(trigger));
    }
    public async ValueTask<MirrorQuestCompletionPreparation> PrepareCompletionAsync(AccountId account, CharacterId character, ushort quest,
        ushort selection, ushort count, DateTimeOffset at, SelectedSessionIdentity? session, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(account, character, session));
        MirrorQuestCompletionPreparation Refused() => new(false, quest, null, null, [], null, null, null, null);
        if (count != 1 || selection != ushort.MaxValue) return Refused();
        var run = Authorized(account, character, quest);
        var state = run is null ? null : await store.ReadAsync(character, run.RunId, ct).ConfigureAwait(false);
        var objective = run is null ? null : await rules.ResolveAsync(run.Layout.DungeonId, quest, ct).ConfigureAwait(false);
        if (state is null || objective is null || run is null || state.GivenUp) return Refused();
        var progress = await characters.Get(character).ProgressAsync(ct).ConfigureAwait(false);
        if (progress is null) return Refused();
        var award = ExperienceAward.Nothing(progress.Level, progress.Experience);
        if (state.RewardClaimed)
            return new(true, quest, objective.Quest.ObjectiveTypeCode, award, [], new(0, 0, []), state, null, null, AlreadyClaimed: true);
        if (!rules.CanFinish(objective, state, run)) return Refused();
        var day = reset.PeriodDay(clock.GetUtcNow());
        var quota = await quotas.ReadMirrorAsync(character, day, ct).ConfigureAwait(false);
        var effects = await rules.RewardEffectsAsync(progress.Level, quota, ct).ConfigureAwait(false);
        if (effects is null) return Refused();
        var context = new EffectExecutionContext(account, character, at, footprint, session);
        var inventory = await preview.PreviewAsync(context, effects.Value, ct).ConfigureAwait(false);
        if (inventory.Rejection != InventoryEffectRejection.None || inventory.RewardEntries > byte.MaxValue) return Refused();
        var daily = rules.Catalog.DailyReward;
        var quotaChange = new MirrorRewardRecorded(account, character, day, quota, quota with
        {
            Clears = checked(quota.Clears + 1),
            DailyRewardClaimed = quota.DailyRewardClaimed || (daily.RequiredClears > 0 && quota.Clears + 1 >= daily.RequiredClears),
        });
        var reward = new QuestCompletionRewardPlan(0, 0,
            effects.Value.OfType<GrantBasicCharacterItemEffect>().Select(x => new QuestRewardItemGrant(x.ItemId, x.Count)).ToArray());
        return new(true, quest, objective.Quest.ObjectiveTypeCode, award, effects.Value, reward,
            state, state with { RewardClaimed = true, TriggerValue = 0 }, quotaChange);
    }
    public async ValueTask CommitCompletionAsync(AccountId account, CharacterId character,
        MirrorQuestCompletionPreparation preparation, CancellationToken ct)
    {
        if (!preparation.Prepared || preparation.AlreadyClaimed || preparation.Expected is not { } expected
            || preparation.Completed is not { } completed || expected.CharacterId != character
            || completed.CharacterId != character || completed.RunId != expected.RunId
            || completed.QuestId != expected.QuestId || !completed.RewardClaimed)
            throw new InvalidOperationException("Invalid mirror quest completion decision.");
        var run = Authorized(account, character, preparation.QuestId);
        var current = run is null ? null : await store.ReadAsync(character, run.RunId, ct).ConfigureAwait(false);
        if (current is null || current.RunId != expected.RunId || current.QuestId != expected.QuestId
            || current.AcceptedAt != expected.AcceptedAt || current.TriggerValue != expected.TriggerValue
            || current.RewardClaimed || current.GivenUp || current.ReviveUsed != expected.ReviveUsed
            || current.TargetMapCleared != expected.TargetMapCleared
            || !current.ObservedDeaths.SequenceEqual(expected.ObservedDeaths)
            || !current.ClearedCells.SequenceEqual(expected.ClearedCells))
            throw new InvalidOperationException("Mirror quest changed since completion was prepared.");
        await store.WriteAsync(account, completed, ct).ConfigureAwait(false);
        await ChangedAsync(character, ct).ConfigureAwait(false);
    }
    public async ValueTask<bool> GiveUpAsync(AccountId account, CharacterId character, ushort quest, CancellationToken ct)
    {
        var run = Authorized(account, character, quest);
        var state = run is null ? null : await store.ReadAsync(character, run.RunId, ct).ConfigureAwait(false);
        if (state is null || state.RewardClaimed) return false;
        if (!state.GivenUp)
        {
            await store.WriteAsync(account, state with { GivenUp = true }, ct).ConfigureAwait(false);
            await ChangedAsync(character, ct).ConfigureAwait(false);
        }
        return true;
    }
    public async ValueTask<bool> ReportAsync(AccountId account, CharacterId character, IReadOnlyList<QuestProgressFact> facts, CancellationToken ct)
    {
        var run = runs.Find(character);
        if (run?.Layout.Mode is not DungeonRunMode.Mirror { Plan: var mirror } || Authorized(account, character, mirror.QuestId) is null) return false;
        var state = await store.ReadAsync(character, run.RunId, ct).ConfigureAwait(false);
        if (state is null || state.GivenUp || state.RewardClaimed) return false;
        var objective = await rules.ResolveAsync(run.Layout.DungeonId, mirror.QuestId, ct).ConfigureAwait(false);
        if (objective is null) return false;
        var updated = state;
        foreach (var fact in facts) updated = rules.Apply(objective, updated, run, fact);
        if (updated == state) return false;
        await store.WriteAsync(account, updated, ct).ConfigureAwait(false);
        if (updated.TriggerValue != state.TriggerValue) await ChangedAsync(character, ct).ConfigureAwait(false);
        return updated.TriggerValue != state.TriggerValue;
    }
    public async ValueTask<QuestProgressSnapshot> SnapshotAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await MergeAsync(character, new(await logs.LogOf(account, character).CarriedAsync(ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
    public async ValueTask<QuestProgressSnapshot> MergeAsync(CharacterId character, QuestProgressSnapshot ordinary, CancellationToken ct)
    {
        var run = runs.Find(character);
        if (run?.Layout.Mode is not DungeonRunMode.Mirror { Plan: var mirror }) return ordinary;
        var state = await store.ReadAsync(character, run.RunId, ct).ConfigureAwait(false);
        if (state is null || state.GivenUp || state.RewardClaimed || state.QuestId != mirror.QuestId) return ordinary;
        var objective = await rules.ResolveAsync(run.Layout.DungeonId, mirror.QuestId, ct).ConfigureAwait(false);
        var trigger = objective is not null && rules.CanFinish(objective, state, run) ? 0 : state.TriggerValue;
        return new([.. ordinary.Quests.Where(x => x.QuestId != mirror.QuestId), new(short.MaxValue, mirror.QuestId, unchecked((int)trigger))]);
    }
    public async ValueTask ClearRunAsync(CharacterId character, DungeonRunId run, CancellationToken ct)
    {
        if (await store.DeleteAsync(character, run, ct).ConfigureAwait(false)) await ChangedAsync(character, ct).ConfigureAwait(false);
    }
    public ValueTask HandleAsync(DomainEventEnvelope<DungeonParticipantLeft> message, CancellationToken ct) =>
        ClearRunAsync(message.Payload.CharacterId, message.Payload.RunId, ct);
    public async ValueTask HandleAsync(DomainEventEnvelope<DungeonMapCleared> message, CancellationToken ct)
    {
        var clear = message.Payload;
        foreach (var participant in clear.Participants.DistinctBy(x => x.CharacterId))
        {
            // The originating run may have ended. Never attach a delayed fact to the current run.
            var state = await store.ReadAsync(participant.CharacterId, clear.RunId, ct).ConfigureAwait(false);
            if (state is null || state.GivenUp || state.RewardClaimed || state.AcceptedAt > clear.ClearedAt) continue;
            var objective = await rules.ResolveAsync(clear.DungeonId, state.QuestId, ct).ConfigureAwait(false);
            if (objective is null) continue;
            var updated = rules.ApplyMapClear(objective, state, clear);
            if (updated == state) continue;
            await store.WriteAsync(participant.AccountId, updated, ct).ConfigureAwait(false);
            if (updated.TriggerValue != state.TriggerValue) await ChangedAsync(participant.CharacterId, ct).ConfigureAwait(false);
        }
    }
    private async ValueTask ChangedAsync(CharacterId character, CancellationToken ct) =>
        _ = await events.EnqueueAsync(new MirrorQuestChanged(character), ct).ConfigureAwait(false);
}

internal sealed class MirrorDungeonQuotaDomainService(IUsageQuotaDomainService quotas, TimeProvider clock, IEventService events,
    IDailyResetRuleService daily) : IMirrorDungeonQuotaDomainService
{
    private DateOnly Day() => daily.PeriodDay(clock.GetUtcNow());
    public async ValueTask<byte?> FindBlockedSlotAsync(IReadOnlyList<DungeonRunParticipant> participants, int dailyLimit, CancellationToken ct)
    {
        if (dailyLimit <= 0) return null;
        var day = Day();
        foreach (var member in participants)
            if ((await quotas.ReadMirrorAsync(member.CharacterId, day, ct).ConfigureAwait(false)).Entries >= dailyLimit) return member.Slot;
        return null;
    }
    public async ValueTask<byte?> TryReserveEntryAsync(IReadOnlyList<DungeonRunParticipant> participants, int dailyLimit, CancellationToken ct)
    {
        var day = Day();
        var entries = System.Collections.Immutable.ImmutableArray.CreateBuilder<MirrorEntryQuotaUpdate>();
        foreach (var member in participants)
        {
            var quota = await quotas.ReadMirrorAsync(member.CharacterId, day, ct).ConfigureAwait(false);
            if (dailyLimit > 0 && quota.Entries >= dailyLimit) return member.Slot;
            entries.Add(new(member.AccountId, member.CharacterId, day, quota.Entries, checked(quota.Entries + 1)));
        }
        await events.DirectAsync(new MirrorEntryRecorded(entries.ToImmutable()), ct).ConfigureAwait(false);
        return null;
    }
}

internal sealed class MirrorQuestNotificationSubscriber(IEventService events) : IDomainEventSubscriber<MirrorQuestChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<MirrorQuestChanged> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]),
            new MirrorQuestViewChanged(message.Payload.CharacterId), ct).ConfigureAwait(false);
}
