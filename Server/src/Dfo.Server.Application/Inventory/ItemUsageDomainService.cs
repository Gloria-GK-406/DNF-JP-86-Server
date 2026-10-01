using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemUsageDomainService(IConsumableUsageTable table, IItemUsageRuleSource rules,
    IDailyResetRuleService days, TimeProvider clock, IDomainTransactionAccessor transactions) : IItemUsageDomainService
{
    public async ValueTask<ItemUsageState> GetAsync(CharacterId characterId, int itemId, CancellationToken token)
    {
        var rule = await RuleAsync(itemId, token).ConfigureAwait(false);
        return State(itemId, await table.ReadAsync(characterId, itemId, token).ConfigureAwait(false), rule, clock.GetUtcNow());
    }
    public async ValueTask<ItemUsageState> RecordUseAsync(CharacterId characterId, int itemId, CancellationToken token)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Item usage requires a transaction.");
        var rule = await RuleAsync(itemId, token).ConfigureAwait(false);
        var previous = await table.ReadAsync(characterId, itemId, token).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var state = State(itemId, previous, rule, now);
        if (state.Remaining == 0) throw new ItemUsageRejectedException(ItemUsageRejection.DailyLimitReached);
        if (state.CooldownUntil > now) throw new ItemUsageRejectedException(ItemUsageRejection.CoolingDown);
        var used = checked(state.UsedCount + 1);
        if (await table.TryWriteAsync(characterId, itemId, previous, used, now, token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Item usage changed during settlement.");
        return State(itemId, new(used, now), rule, now);
    }
    public ValueTask<IReadOnlyList<ItemUsageState>> GetSnapshotAsync(CharacterId characterId, CancellationToken token) =>
        GetSnapshotAtAsync(characterId, clock.GetUtcNow(), token);
    public async ValueTask<IReadOnlyList<ItemUsageState>> GetSnapshotAtAsync(CharacterId characterId, DateTimeOffset now, CancellationToken token)
    {
        var rows = await table.ReadAllAsync(characterId, token).ConfigureAwait(false);
        var result = new List<ItemUsageState>();
        foreach (var pair in rows.OrderBy(x => x.Key))
        {
            var rule = await RuleAsync(pair.Key, token).ConfigureAwait(false);
            var state = State(pair.Key, pair.Value, rule, now);
            if (rule.DailyLimit >= 0 || state.CooldownUntil is not null) result.Add(state);
        }
        return result;
    }
    private async ValueTask<ItemUsageRule> RuleAsync(int itemId, CancellationToken token)
    {
        var rule = await rules.GetAsync(itemId, token).ConfigureAwait(false);
        if (rule is not { IsReadable: true, DailyLimit: >= -1, CooldownMilliseconds: >= 0 })
            throw new InvalidDataException($"Missing or invalid item usage rule for {itemId}.");
        return rule;
    }
    private ItemUsageState State(int itemId, ConsumableUsageRow? row, ItemUsageRule rule, DateTimeOffset now)
    {
        if (row?.UsedCount < 0) throw new InvalidDataException("Invalid item usage count.");
        var used = row?.LastUsedAt is { } last && days.IsCurrentPeriod(last, now) ? row.UsedCount : 0;
        var until = row?.LastUsedAt?.AddMilliseconds(rule.CooldownMilliseconds);
        return new(itemId, used, rule.DailyLimit >= 0 ? rule.DailyLimit : null, row?.LastUsedAt,
            until > now ? until : null);
    }
}
