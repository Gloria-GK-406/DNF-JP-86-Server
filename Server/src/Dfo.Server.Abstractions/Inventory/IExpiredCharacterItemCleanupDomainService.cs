using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Removes items whose stored lifetime ended before character entry.</summary>
public interface IExpiredCharacterItemCleanupDomainService
{
    /// <returns>False only when the character no longer belongs to the account.</returns>
    ValueTask<bool> CleanupAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken);
}
