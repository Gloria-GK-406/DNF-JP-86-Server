using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Effects;

internal sealed class SetPurchaseRestrictionEffectHandler(IAccountPurchaseRestrictionTable restrictions)
    : EffectHandler<SetPurchaseRestrictionEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext c, SetPurchaseRestrictionEffect e, CancellationToken ct)
    {
        if (e.RestrictionId <= 0 || e.End <= c.At.ToUnixTimeSeconds()
            || e.End > CeraShopPurchaseRestrictionDomainService.PermanentEnd
            || e.ExpectedEnd is <= 0 || e.ExpectedEnd > c.At.ToUnixTimeSeconds())
            throw new InvalidOperationException("Invalid purchase restriction effect.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext c, SetPurchaseRestrictionEffect e, CancellationToken ct)
    {
        if (!await restrictions.TrySetAsync(c.AccountId, e.RestrictionId, e.ExpectedEnd, e.End, ct).ConfigureAwait(false))
            throw new CeraShopPurchaseRejectedException(CeraShopRejection.StateChanged, "Purchase restriction changed before settlement.");
        return new PurchaseRestrictionChange(e.RestrictionId, e.End);
    }
}
