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
    TimeProvider clock) : IExpiredCharacterItemCleanupDomainService
{
    public async ValueTask<bool> CleanupAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        _ = transactions.Current ?? throw new InvalidOperationException(
            "Expired-item cleanup requires a transaction.");
        if (!await characters.BelongsToAsync(characterId, accountId, cancellationToken)
                .ConfigureAwait(false))
            return false;

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var held = await items.ReadByCharacterAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var expired = held.Where(item => IsExpired(item, now)).Select(item => item.ItemUid).ToArray();
        if (expired.Length == 0)
            return true;

        // The table checks character ownership again at deletion time. A mismatch must roll back
        // this command, rather than return false after a partial write and commit the transaction.
        var removed = await items.DeleteOwnByUidsAsync(characterId, expired, cancellationToken)
            .ConfigureAwait(false);
        if (removed != expired.Length)
            throw new InvalidOperationException(
                $"Expired-item cleanup removed {removed} of {expired.Length} items for {characterId}.");
        return true;
    }

    private static bool IsExpired(CharacterItemRow item, long now)
    {
        var expiresAt = item.Core.ItemKind == 8
            ? item.Avatar?.ExpireDate ?? 0
            : item.Core.ExpireTime;
        return expiresAt > 0 && expiresAt != int.MaxValue && expiresAt <= now;
    }
}
