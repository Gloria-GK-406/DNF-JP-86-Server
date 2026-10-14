using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Inventory;

internal sealed class ExpiredCharacterItemCleanupDomainService(
    ICharacterTable characters,
    ICharacterItemTable items,
    IDomainTransactionAccessor transactions,
    TimeProvider clock, IItemDomainStore store) : IExpiredCharacterItemCleanupDomainService
{
    public async ValueTask<ExpiredItemCleanupResult> CleanupAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException(
            "Expired-item cleanup requires a transaction.");
        if (!await characters.BelongsToAsync(characterId, accountId, cancellationToken)
                .ConfigureAwait(false))
            return new(false, []);

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var held = await items.ReadByCharacterAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var itemSession = store.SessionIn(transaction);
        var changed = new HashSet<Dfo.Server.Data.Inventory.InventoryContainerType>();
        foreach (var row in held.Where(item => IsExpired(item, now)))
        {
            if (row.OwnerScope != "character" || row.OwnerId != characterId.Value)
                throw new InvalidDataException("Expired item belongs to a different character.");
            var container = ContainerId.ForCharacter(characterId, (Dfo.Server.Data.Inventory.InventoryContainerType)row.ListType);
            var item = await itemSession.GetAsync(container, row.SlotIndex, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Expired item disappeared during cleanup.");
            var expires = item.Snapshot.AvatarDetail?.ExpireDate ?? item.Snapshot.ExpireTime;
            if (expires <= 0 || expires == int.MaxValue || expires > now)
                throw new InvalidDataException("Expired item changed during cleanup.");
            await itemSession.DeleteItemAsync(await itemSession.TakeAsync(item, null, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
            changed.Add((Dfo.Server.Data.Inventory.InventoryContainerType)row.ListType);
        }
        return new(true, [.. changed.Order()]);
    }

    private static bool IsExpired(CharacterItemRow item, long now)
    {
        var expiresAt = item.Core.ItemKind == 8
            ? item.Avatar?.ExpireDate ?? 0
            : item.Core.ExpireTime;
        return expiresAt > 0 && expiresAt != int.MaxValue && expiresAt <= now;
    }
}
