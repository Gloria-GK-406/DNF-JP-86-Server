using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterEntryService(
    ICharacterService characters,
    IInventoryEntryService inventory,
    IActiveCharacterService activeCharacters,
    TimeProvider timeProvider) : ICharacterEntryService
{
    public async ValueTask<SelectCharacterResult> EnterAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken)
    {
        var character = await characters.LoadForSelectionAsync(
            accountId,
            slotIndex,
            cancellationToken).ConfigureAwait(false);

        var inventoryEntry = await inventory.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);

        activeCharacters.Publish(accountId, character);
        var preparedAt = timeProvider.GetUtcNow();
        return new SelectCharacterResult(
            character,
            inventoryEntry.CharacterInventory,
            inventoryEntry.AccountInventory,
            new AccountEntryState(accountId),
            new GameWorldEntryState(preparedAt),
            new CharacterSessionEntryState(accountId, character.Identity.CharacterId));
    }
}
