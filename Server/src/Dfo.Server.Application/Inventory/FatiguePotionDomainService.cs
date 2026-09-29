using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class FatiguePotionDomainService(IOpeningInventoryDomainService inventory,
    IStackableActionDefinitionSource definitions, ICharacterDomainService characters,
    ICharacterFatigueDomainService fatigue, IItemUsageDomainService usage, ITownAreaDomainService towns, IDomainTransactionAccessor transactions)
    : IFatiguePotionDomainService
{
    public async ValueTask<OpeningInventoryReceipt> UseAsync(SelectedSessionIdentity session,
        int sourceSlot, DateTimeOffset now, CancellationToken token)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Fatigue potion requires a transaction.");
        var source = await inventory.SourceAsync(session, sourceSlot, now, token).ConfigureAwait(false);
        var rule = await definitions.GetAsync(source.ItemId, token).ConfigureAwait(false);
        if (rule is not { IsReadable: true, Action: "[add fatigue]", FatigueRecovery: > 0 and <= ushort.MaxValue })
            throw Rejected("Not a supported fatigue potion.");
        var progress = await characters.Get(session.CharacterId).ProgressAsync(token).ConfigureAwait(false);
        if (towns.Find(session.CharacterId) is null || progress is null
            || progress.Level < rule.MinimumLevel || progress.Level > rule.MaximumLevel
            || rule.ExpiresAtUnixSeconds > 0 && rule.ExpiresAtUnixSeconds <= now.ToUnixTimeSeconds())
            throw Rejected("Fatigue potion is not usable here, at this level, or after expiration.");
        var balance = await fatigue.BalanceOfAsync(session.CharacterId, token).ConfigureAwait(false);
        if (balance.Used < rule.FatigueRecovery) throw Rejected("Not enough fatigue recovery space.");

        var receipt = await inventory.SettleAsync(session,
            [new OpeningSlotConsumption(sourceSlot, source.ItemId, 1)], [], now, token).ConfigureAwait(false);
        await fatigue.RefundAsync(session.CharacterId, checked((ushort)rule.FatigueRecovery), token).ConfigureAwait(false);
        try { await usage.RecordUseAsync(session.CharacterId, source.ItemId, token).ConfigureAwait(false); }
        catch (ItemUsageRejectedException ex) { throw Rejected(ex.Reason.ToString()); }
        return receipt;
    }

    private static OpeningRejectedException Rejected(string message) => new(OpeningRejection.ItemUnusable, message);
}
