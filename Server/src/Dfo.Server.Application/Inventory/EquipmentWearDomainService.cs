using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentWearRuleService(IEquipmentRequirementDefinitionSource source,
    IEquipmentSlotDefinitionSource slots, ICharacterSupportWeaponDefinitionSource supportWeapons) : IEquipmentWearRuleService
{
    public ItemSnapshot UnsealForEquip(ItemSnapshot item) =>
        item.LegacyKind is 1 or 6 && item.SealFlag == 1 ? item with { SealFlag = 0 } : item;

    public ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken) =>
        source.MinimumLevelAsync(itemId, cancellationToken);

    // The weapon's [sub type] is job-relative, so it is matched only against the allowance of the
    // wearer's own job; the main-weapon path does not check [usable job] either, and neither does this.
    public async ValueTask<bool> MayCarryAsSupportWeaponAsync(CharacterCareer career, int itemId, BusinessSlot slot,
        CancellationToken cancellationToken) =>
        await slots.GetSupportWeaponAsync(itemId, cancellationToken).ConfigureAwait(false) is { } candidate
        && candidate.Slot == slot
        && (await supportWeapons.SubTypesAsync(career, cancellationToken).ConfigureAwait(false)).Contains(candidate.SubType);
}

internal sealed class EquipmentWearDomainService(ICharacterTable characters,
    IEquipmentWearRuleService rules, IItemPlacementRuleService placements,
    IPremiumServiceDomainService premiums, IPremiumServiceRuleService premiumRules, AvatarAppearanceRuleService appearances) : IEquipmentWearDomainService
{
    public ItemSnapshot UnsealForEquip(ItemSnapshot item) => rules.UnsealForEquip(item);

    public ValueTask<ItemSnapshot> PrepareForWearAsync(ItemSnapshot item, ItemSnapshot? previous, CancellationToken ct) =>
        appearances.PrepareAsync(rules.UnsealForEquip(item), previous, ct);

    public async ValueTask<bool> MayWearAsSupportWeaponAsync(CharacterId characterId, int itemId, BusinessSlot slot,
        CancellationToken cancellationToken) =>
        await characters.FindStandingAsync(characterId, cancellationToken).ConfigureAwait(false) is { } standing
        && await rules.MayCarryAsSupportWeaponAsync(
            CharacterCareer.FromPacked(checked((byte)standing.Job), standing.GrowType), itemId, slot, cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<bool> CanWearAsync(CharacterId characterId, int itemId, CancellationToken cancellationToken)
    {
        var required = await rules.MinimumLevelAsync(itemId, cancellationToken).ConfigureAwait(false);
        var standing = await characters.FindStandingAsync(characterId, cancellationToken).ConfigureAwait(false);
        if (required is null || standing is null) return false;
        if (standing.Level >= required.Value) return true;
        var kind = await placements.ClassifyAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (kind is not (ItemWorldClass.Equipment or ItemWorldClass.Avatar)) return false;
        var owner = await characters.FindOwnerAsync(characterId, cancellationToken).ConfigureAwait(false);
        return owner is { } account
            && await premiums.For(account).HasBenefitAsync(PremiumBenefit.EquipItemsEarly, cancellationToken).ConfigureAwait(false)
            && (long)standing.Level + premiumRules.LevelAllowance(PremiumBenefit.EquipItemsEarly) >= required.Value;
    }
}
