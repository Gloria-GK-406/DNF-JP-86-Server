using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// Works out what using an item changes, and changes it.
/// </summary>
/// <remarks>
/// The eighty-four items whose <c>[stackable type]</c> begins with <c>unlimited</c> differ from
/// every other consumable by producing no consumption, not by surviving one. Written as a guard
/// around the decrement, the same exception would have to be repeated in front of each effect
/// added later; written as an absent effect, the rest of the model never learns about it.
/// </remarks>
internal sealed class ItemUseEffectDomainService(
    IItemUseRuleDefinitionSource rules,
    ICharacterItemCooldownDomainService cooldowns) : IItemUseEffectDomainService
{
    private const int ConsumedPerUse = 1;

    public async ValueTask<IReadOnlyList<IItemUseEffect>> PlanAsync(
        int itemId,
        long storageEntryId,
        CancellationToken cancellationToken)
    {
        var rule = await rules.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (rule is null)
            return [];

        var effects = new List<IItemUseEffect>(2);
        if (rule.IsConsumedOnUse)
            effects.Add(new ConsumeFromContainerEffect(storageEntryId, ConsumedPerUse));

        if (rule.CooldownMilliseconds > 0)
        {
            effects.Add(new StartItemCooldownEffect(
                ItemUsabilityDomainService.CooldownKeyFor(itemId, rule),
                TimeSpan.FromMilliseconds(rule.CooldownMilliseconds)));
        }

        return effects;
    }

    public async ValueTask<ItemUseApplication?> ApplyAsync(
        CharacterId characterId,
        IItemContainer container,
        ContainerSlotItem item,
        IReadOnlyList<IItemUseEffect> effects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(effects);

        // The container goes first. It is the only effect that can be refused, and starting a
        // cooldown for a use that never happened would lock the player out of an item they still
        // hold.
        //
        // Only the consumption of the item being used decides the count reported back. An effect
        // set that also consumed something else -- a reagent, a charge -- would otherwise pair that
        // other pile's remainder with this item's identifier, and the two would name different
        // slots.
        var remaining = item.Count;
        foreach (var consumption in effects.OfType<ConsumeFromContainerEffect>())
        {
            var applied = await container
                .ConsumeAsync(consumption.StorageEntryId, consumption.Count, cancellationToken)
                .ConfigureAwait(false);
            if (applied is null)
                return null;

            if (consumption.StorageEntryId == item.StorageEntryId)
                remaining = applied.RemainingCount;
        }

        foreach (var cooldown in effects.OfType<StartItemCooldownEffect>())
            cooldowns.Start(characterId, cooldown.Key, cooldown.Duration);

        return new ItemUseApplication(item.ItemId, remaining);
    }
}
