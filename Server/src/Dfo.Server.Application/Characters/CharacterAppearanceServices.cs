using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Characters;

public sealed class CharacterAppearanceRuleService : ICharacterAppearanceRuleService
{
    public static CharacterAppearanceRuleService Shared { get; } = new();

    public CharacterAppearance Resolve(CharacterAvatarCandidates avatars,
        IReadOnlyList<InventoryItemState> equipment, ClientAppearanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(avatars);
        ArgumentNullException.ThrowIfNull(equipment);
        ArgumentNullException.ThrowIfNull(options);
        return new(avatars, options.ShowAwakeningAvatar ? avatars.Awakening : avatars.Default,
            options.ShowGrowEffect, options.ShowFullAvatar, options.ShowAwakeningAvatar,
            options.VisibilityBits,
            checked((uint)(equipment.SingleOrDefault(x => x.SlotIndex == 13)?.Core.AppearanceItemId ?? 0)),
            Array.AsReadOnly(EquipmentAppearanceParts.Of(equipment).ToArray()));
    }
}

internal sealed class CharacterAppearanceDomainService(ICharacterDomainService characters,
    ICharacterSettingsDomainService characterSettings, IClientSettingsDomainService settings,
    ICharacterAppearanceRuleService rules) : ICharacterAppearanceDomainService
{
    public async ValueTask<CharacterAppearance> ResolveAsync(AccountId accountId, CharacterId characterId,
        IReadOnlyList<InventoryItemState> equipment, CancellationToken ct)
    {
        var avatars = await characters.Get(characterId).GetAvatarCandidatesAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Character appearance identity is unavailable.");
        var legacy = await characterSettings.FindAsync(characterId, ct).ConfigureAwait(false);
        var options = await settings.ReadAppearanceAsync(accountId, checked((byte)(legacy?.VisibilityBits ?? 0)), ct)
            .ConfigureAwait(false);
        return rules.Resolve(avatars, equipment, options);
    }
}

// Compatibility facade for existing callers; production projections use the complete result.
public static class DrawnAppearance
{
    public static bool DrawsFrom(int slot) => EquipmentAppearanceParts.DrawsFrom(slot);
    public static IReadOnlyList<Dfo.Server.Data.Game.CharacterRosterAppearanceState> Of(IReadOnlyList<InventoryItemState> worn) =>
        CharacterAppearanceRuleService.Shared.Resolve(new(new(0)), worn, new(null, 0)).Parts;
}
