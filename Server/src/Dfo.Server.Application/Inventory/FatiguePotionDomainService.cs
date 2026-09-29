using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class FatiguePotionDomainService(IOpeningInventoryDomainService inventory,
    IStackableActionDefinitionSource definitions, ICharacterDomainService characters,
    ICharacterFatigueDomainService fatigue, IConsumableUsageTable usage,
    IDailyResetRuleService days, ITownAreaDomainService towns, IDomainTransactionAccessor transactions)
    : IFatiguePotionDomainService
{
    public async ValueTask<OpeningInventoryReceipt> UseAsync(SelectedSessionIdentity session,
        int sourceSlot, DateTimeOffset now, CancellationToken token)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Fatigue potion requires a transaction.");
        var source = await inventory.SourceAsync(session, sourceSlot, now, token).ConfigureAwait(false);
        var rule = await definitions.GetAsync(source.ItemId, token).ConfigureAwait(false);
        if (rule is not { IsReadable: true, Action: "[add fatigue]", FatigueRecovery: > 0 and <= ushort.MaxValue }
            || rule.DailyUseLimit < -1 || rule.CooldownMilliseconds < 0)
            throw Rejected("Not a supported fatigue potion.");
        var progress = await characters.Get(session.CharacterId).ProgressAsync(token).ConfigureAwait(false);
        if (towns.Find(session.CharacterId) is null || progress is null
            || progress.Level < rule.MinimumLevel || progress.Level > rule.MaximumLevel
            || rule.ExpiresAtUnixSeconds > 0 && rule.ExpiresAtUnixSeconds <= now.ToUnixTimeSeconds())
            throw Rejected("Fatigue potion is not usable here, at this level, or after expiration.");
        var previous = await usage.ReadAsync(session.CharacterId, source.ItemId, token).ConfigureAwait(false);
        var used = previous?.LastUsedAt is { } stamp && days.IsCurrentPeriod(stamp, now) ? previous.UsedCount : 0;
        if (previous?.UsedCount < 0) throw new InvalidDataException("Invalid consumable usage counter.");
        if (rule.DailyUseLimit >= 0 && used >= rule.DailyUseLimit)
            throw Rejected("Daily usage limit reached.");
        // Cooldown is elapsed time, not a business-day quota; midnight/reset does not shorten it.
        if (previous?.LastUsedAt is { } last && now - last < TimeSpan.FromMilliseconds(rule.CooldownMilliseconds))
            throw Rejected("Fatigue potion is cooling down.");
        var balance = await fatigue.BalanceOfAsync(session.CharacterId, token).ConfigureAwait(false);
        if (balance.Used < rule.FatigueRecovery) throw Rejected("Not enough fatigue recovery space.");

        var receipt = await inventory.SettleAsync(session,
            [new OpeningSlotConsumption(sourceSlot, source.ItemId, 1)], [], now, token).ConfigureAwait(false);
        await fatigue.RefundAsync(session.CharacterId, checked((ushort)rule.FatigueRecovery), token).ConfigureAwait(false);
        if (await usage.TryWriteAsync(session.CharacterId, source.ItemId, previous, checked(used + 1), now, token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Consumable usage changed during settlement.");
        return receipt;
    }

    private static OpeningRejectedException Rejected(string message) => new(OpeningRejection.ItemUnusable, message);
}
