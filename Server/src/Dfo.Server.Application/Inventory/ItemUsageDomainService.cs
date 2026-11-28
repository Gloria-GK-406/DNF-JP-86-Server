using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemUsageDomainService(IUsageQuotaDomainService quotas, IItemUsageRuleService rules,
    IConsumableCooldownTable cooldowns, IItemUseRuleService itemUse,
    TimeProvider clock, IDomainTransactionAccessor transactions, IEventService events) : IItemUsageDomainService
{
    public async ValueTask<ItemUsageState> GetAsync(CharacterId characterId, int itemId, CancellationToken token)
    {
        var rule = await rules.RuleAsync(itemId, token).ConfigureAwait(false);
        return rules.StateOf(itemId, (await quotas.ReadAsync(characterId, UsageQuotaKey.Consumable(itemId), token).ConfigureAwait(false)).Consumable(), rule, clock.GetUtcNow());
    }
    public async ValueTask<ItemUsageState> RecordUseAsync(CharacterId characterId, int itemId, CancellationToken token)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Item usage requires a transaction.");
        var rule = await rules.RuleAsync(itemId, token).ConfigureAwait(false);
        var previous = (await quotas.ReadAsync(characterId, UsageQuotaKey.Consumable(itemId), token).ConfigureAwait(false)).Consumable();
        var now = clock.GetUtcNow();
        var state = rules.StateOf(itemId, previous, rule, now);
        if (state.Remaining == 0) throw new ItemUsageRejectedException(ItemUsageRejection.DailyLimitReached);
        if (state.CooldownUntil > now) throw new ItemUsageRejectedException(ItemUsageRejection.CoolingDown);
        var used = checked(state.UsedCount + 1);
        await events.DirectAsync(new ConsumableUsageRecorded(characterId, itemId, previous, new(used, now)), token).ConfigureAwait(false);
        return rules.StateOf(itemId, new(used, now), rule, now);
    }
    public ValueTask<IReadOnlyList<ItemUsageState>> GetSnapshotAsync(CharacterId characterId, CancellationToken token) =>
        GetSnapshotAtAsync(characterId, clock.GetUtcNow(), token);
    public async ValueTask<IReadOnlyList<ItemUsageState>> GetSnapshotAtAsync(CharacterId characterId, DateTimeOffset now, CancellationToken token)
    {
        var rows = await quotas.ReadConsumablesAsync(characterId, token).ConfigureAwait(false);
        var result = new Dictionary<int, ItemUsageState>();
        foreach (var pair in rows)
        {
            var rule = await rules.TryRuleAsync(pair.Key, token).ConfigureAwait(false);
            if (rule is null) continue;
            var state = rules.StateOf(pair.Key, pair.Value, rule, now);
            if (state.DailyLimit is not null || state.CooldownUntil is not null) result.Add(pair.Key, state);
        }

        foreach (var row in await cooldowns.ReadActiveAsync(characterId, now, token).ConfigureAwait(false))
        {
            var use = await itemUse.RuleForAsync(row.ItemId, token).ConfigureAwait(false);
            if (use is null || itemUse.CooldownFor(row.ItemId, use) is not { Scope: ItemCooldownScope.Persistent } policy
                || policy.Key != row.Key)
                continue;
            result[row.ItemId] = result.TryGetValue(row.ItemId, out var state)
                ? state with { CooldownUntil = row.ExpiresAt }
                : new(row.ItemId, 0, null, null, row.ExpiresAt);
        }
        return result.Values.OrderBy(state => state.ItemId).ToArray();
    }
}
