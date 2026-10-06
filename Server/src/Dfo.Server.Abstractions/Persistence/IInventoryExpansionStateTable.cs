using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Persistence;

public interface IInventoryExpansionStateTable
{
    ValueTask<bool> ChangeCharacterAsync(CharacterId character, InventoryContainerType type, int expected, int value, CancellationToken ct);
    ValueTask<bool> ChangeAccountAsync(AccountId account, int expected, int value, CancellationToken ct);
    ValueTask<bool> ChangeAccountGoldAsync(AccountId account, int capacity, int expected, int value, CancellationToken ct);
}
