using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Hands a character the quest items a kill or an object rolled straight to them.
/// </summary>
/// <remarks>
/// Only drops delivered as direct quest items, owned by this character, for a quest it is carrying,
/// are granted. Each is capped by its grant limit, counting what the character's default bag holds
/// and what already waits in the mailbox; overflow goes to the mailbox. Runs inside the caller's
/// write unit. Who may receive at all (party membership, exact sessions) is the caller's gate.
/// </remarks>
public interface IQuestItemDropDomainService
{
    /// <returns>The inventory additions, in delivery order; mailed overflow is not listed.</returns>
    ValueTask<IReadOnlyList<ItemDeliveryAddition>> GrantAsync(
        AccountId accountId, CharacterId characterId, IReadOnlyList<RolledDrop>? loot,
        CancellationToken cancellationToken);
}
