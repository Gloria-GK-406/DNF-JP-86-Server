using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentWearRuleService(IEquipmentRequirementDefinitionSource source) : IEquipmentWearRuleService
{
    public ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken) =>
        source.MinimumLevelAsync(itemId, cancellationToken);
}

internal sealed class EquipmentWearDomainService(ICharacterTable characters,
    IEquipmentWearRuleService rules, IItemPlacementRuleService placements,
    IPremiumServiceDomainService premiums, IPremiumServiceRuleService premiumRules) : IEquipmentWearDomainService
{
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
