using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterService(Func<ICharacterRepository> characterRepository)
    : ICharacterService, IEquipmentEligibilityCharacterReader
{
    private const ushort DefaultSlotLimit = 32;

    public async ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var characters = await characterRepository().ListByAccountAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        return new AccountCharacterRoster(accountId, DefaultSlotLimit, characters);
    }

    public async ValueTask<ActiveCharacterState> LoadForSelectionAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken)
    {
        return await characterRepository().FindByAccountAndSlotAsync(
            accountId,
            slotIndex,
            cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Account '{accountId}' has no character in slot '{slotIndex}'.");
    }

    public async ValueTask<EquipmentEligibilityCharacterFacts> GetAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var roster = await GetAccountRosterAsync(accountId, cancellationToken).ConfigureAwait(false);
        var character = roster.Characters.FirstOrDefault(item => item.CharacterId == characterId)
            ?? throw new KeyNotFoundException(
                $"Account '{accountId}' has no character '{characterId}'.");

        return new EquipmentEligibilityCharacterFacts(
            character.CharacterId,
            character.Level,
            character.Job,
            character.GrowType);
    }
}
