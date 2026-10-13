using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DespairTowerDomainService(IDespairTowerRuleService rules,
    IDespairTowerDomainStore progress, IDungeonAdmissionDomainService admission,
    IDungeonStructureRuleService layouts, IDungeonVisitDomainService visits,
    IDungeonRunDomainService runs, IUsageQuotaDomainService quotas, IEventService events,
    IDailyResetRuleService daily, TimeProvider clock, IDomainTransactionAccessor transactions,
    ICharacterDomainService characters, IItemDeliveryDomainService delivery,
    IQuestProgressDomainService quests) : IDespairTowerDomainService
{
    private DateOnly Period() => DateOnly.FromDateTime(daily.PeriodStart(clock.GetUtcNow())
        .ToOffset(TimeSpan.FromHours(8)).DateTime);
    public bool IsTower(int dungeonId) => rules.IsTower(dungeonId);
    public async ValueTask<DespairTowerStatus> ReadStatusAsync(CharacterId character, CancellationToken ct)
    {
        var next = rules.NextFloor(await progress.ReadAsync(character, ct).ConfigureAwait(false));
        var period = Period();
        var entries = await quotas.ReadAsync(character, UsageQuotaKey.Despair("entries", period), ct).ConfigureAwait(false);
        var clears = await quotas.ReadAsync(character, UsageQuotaKey.Despair("clears", period), ct).ConfigureAwait(false);
        var free = next is not null && (entries?.UsedCount ?? 0) == 0
            && (clears?.UsedCount ?? 0) < rules.Catalog.MaximumDailyClears;
        return new(next?.Floor ?? rules.Catalog.Floors.Max(x => x.Floor), free ? (byte)1 : (byte)0,
            rules.Catalog.Floors.Select(x => x.DungeonId).ToArray());
    }
    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (!IsTower(context.DungeonId) || context.Participants.Count != 1
            || context.RequestMode != 0 || context.PracticeMode != 0
            || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        var player = context.Participants[0];
        var character = await characters.Get(player.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        if (character is null || character.Level < rules.Catalog.MinimumLevel)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.LevelTooLow);
        var floor = rules.NextFloor(await progress.ReadAsync(player.CharacterId, ct).ConfigureAwait(false));
        if (floor is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DailyEntryLimitReached);
        var period = Period();
        var clears = await quotas.ReadAsync(player.CharacterId, UsageQuotaKey.Despair("clears", period), ct).ConfigureAwait(false);
        if ((clears?.UsedCount ?? 0) >= rules.Catalog.MaximumDailyClears)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.DailyEntryLimitReached);
        var entries = await quotas.ReadAsync(player.CharacterId, UsageQuotaKey.Despair("entries", period), ct).ConfigureAwait(false);
        var facts = context.Facts with { Difficulty = DungeonDifficulty.Normal, Intent = DungeonEntryIntent.Interactive };
        var target = context with { DungeonId = floor.DungeonId, Facts = facts };
        return await admission.ExecuteAsync(target, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false,
            RequiredItemsOverride = (entries?.UsedCount ?? 0) == 0 ? [] : floor.Tickets,
            Quota = new(UsageQuotaFamily.DespairTower, period, int.MaxValue),
        }, async (receipt, token) =>
        {
            DungeonLayout? layout;
            try { layout = await layouts.ResolveAsync(floor.DungeonId, facts, Random.Shared.Next(), token).ConfigureAwait(false); }
            catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
            if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
            layout = layout with { Despair = new(floor.Floor, period), ConsumesFatigue = false };
            return await visits.EnterGrantedPartyAsync(target.Participants, layout, facts, receipt, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask<DespairTowerSettlement?> SettleAsync(AccountId account, CharacterId character,
        DungeonRunId runId, CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Tower clear requires a transaction.");
        var run = runs.Find(character);
        if (run is null || run.RunId != runId || !run.ClearOpen || run.Layout.Despair is not { } plan
            || run.Participant?.AccountId != account) return null;
        if (!await progress.TryAdvanceAsync(character, plan.Floor - 1, runId.Value, ct).ConfigureAwait(false)) return null;
        var standing = await characters.Get(character).ProgressAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Tower recipient has no progression.");
        var rewards = rules.Rewards(plan.Floor, standing.Level, run.Layout.BasisLevel, unchecked((ulong)run.Layout.Seed));
        var receipt = await delivery.DeliverAsync(account, character,
            rewards.Select(x => new ItemDeliveryGrant(x.ItemId, x.Count) { Durability = x.Durability }).ToArray(),
            "绝望之塔奖励", ct, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
        var updates = receipt.Inventory.GroupBy(x => x.Slot).ToDictionary(x => x.Key,
            x => new DespairTowerInventoryUpdate(x.Key, x.Last().Snapshot));
        // The entry period owns the run even when its last kill crosses the daily reset.
        var keyClears = UsageQuotaKey.Despair("clears", plan.Period);
        var before = await quotas.ReadAsync(character, keyClears, ct).ConfigureAwait(false);
        await events.DirectAsync(new DespairQuotaRecorded(character, plan.Period, "clears", before,
            checked((before?.UsedCount ?? 0) + 1)), ct).ConfigureAwait(false);
        await quests.ReportAsync(account, character,
            [new QuestDungeonCleared(run.Layout.DungeonId, run.Difficulty, run.RunId, run.ClearedAt ?? clock.GetUtcNow()), new QuestHoldingsChanged()], ct).ConfigureAwait(false);
        if (transaction.IsRollbackOnly) throw new InvalidOperationException("Tower clear was rolled back.");
        var elapsed = (run.ClearedAt ?? clock.GetUtcNow()) - run.StartedAt;
        return new(plan.Floor, (uint)Math.Clamp(elapsed.TotalMilliseconds, 0, uint.MaxValue),
            rewards.ToImmutableArray(), updates.Values.ToImmutableArray());
    }


}
