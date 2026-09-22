using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed class CardFlipCostDomainService(IPremiumServiceDomainService premiums,
    IPremiumServiceUsageDomainService usage, IDomainTransactionAccessor transactions) : ICardFlipCostDomainService
{
    public async ValueTask<CardFlipCostSettlement?> SettleAsync(AccountId accountId, CharacterId characterId,
        ClearRewardCardKind kind, int? basePrice, CancellationToken token)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Card cost settlement requires a transaction.");
        if (kind == ClearRewardCardKind.Paid && (basePrice is null || basePrice < 0)) return null;
        var price = kind == ClearRewardCardKind.Free ? 0 : basePrice!.Value;
        if (price > 0 && await usage.TryUseFreeGoldCardAsync(accountId, characterId, token).ConfigureAwait(false))
            price = 0;
        return new(price, new(await premiums.For(accountId).ReadAsync(token).ConfigureAwait(false)),
            await usage.ReadAsync(characterId, token).ConfigureAwait(false));
    }
}
