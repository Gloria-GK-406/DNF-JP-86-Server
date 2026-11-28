using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemUsageRuleService(IItemUsageRuleSource rules, IItemUseRuleService uses, IDailyResetRuleService days) : IItemUsageRuleService
{
    public async ValueTask<ItemUsageRule> RuleAsync(int itemId, CancellationToken token) =>
        await TryRuleAsync(itemId, token).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Missing or invalid item usage rule for {itemId}.");

    public async ValueTask<ItemUsageRule?> TryRuleAsync(int itemId, CancellationToken token)
    {
        var rule = await rules.GetAsync(itemId, token).ConfigureAwait(false);
        if (rule is not { IsReadable: true, DailyLimit: >= -1, CooldownMilliseconds: >= 0 }) return null;
        if (!rule.CooldownMaintenance.PersistOnServer || rule.CooldownMilliseconds == 0) return rule;

        // Only fatigue settlement owns elapsed cooldowns on the legacy quota rows.
        var use = await uses.RuleForAsync(itemId, token).ConfigureAwait(false);
        return use?.Action is "[add fatigue]" or "[random recovery fatigue]"
            ? rule : rule with { CooldownMaintenance = default };
    }
    public ItemUsageState StateOf(int itemId, ConsumableUsageRow? row, ItemUsageRule rule, DateTimeOffset now)
    {
        if (row?.UsedCount < 0) throw new InvalidDataException("Invalid item usage count.");
        var used = row?.LastUsedAt is { } last && days.IsCurrentPeriod(last, now) ? row.UsedCount : 0;
        var until = rule.CooldownUntil(row?.LastUsedAt);
        return new(itemId, used, rule.DailyLimit >= 0 ? rule.DailyLimit : null, row?.LastUsedAt,
            until > now ? until : null);
    }
}
