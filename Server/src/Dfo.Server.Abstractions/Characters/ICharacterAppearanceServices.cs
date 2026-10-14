using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterAppearanceRuleService
{
    CharacterAppearance Resolve(CharacterAvatarCandidates avatars,
        IReadOnlyList<InventoryItemState> equipment, ClientAppearanceOptions options);
}

public interface ICharacterAppearanceDomainService
{
    ValueTask<CharacterAppearance> ResolveAsync(AccountId accountId, CharacterId characterId,
        IReadOnlyList<InventoryItemState> equipment, CancellationToken ct);
}
