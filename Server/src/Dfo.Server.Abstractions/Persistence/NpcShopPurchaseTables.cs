namespace Dfo.Server.Abstractions.Persistence;

public sealed record NpcShopPurchaseKey(string OwnerScope, int OwnerId, int NpcId, int ItemId, string Period);

public interface INpcShopPurchaseTable
{
    ValueTask<int> ReadAsync(NpcShopPurchaseKey key, CancellationToken cancellationToken);
    ValueTask<bool> TryAddAsync(NpcShopPurchaseKey key, int count, int limit, CancellationToken cancellationToken);
}
