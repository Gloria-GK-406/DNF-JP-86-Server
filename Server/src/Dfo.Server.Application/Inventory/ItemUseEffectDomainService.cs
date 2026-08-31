using Dfo.Server.Abstractions.Characters;
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
    IItemUseJudgementService judgement,
    ICharacterItemCooldownDomainService cooldowns) : IItemUseEffectDomainService
{
    private const int ConsumedPerUse = 1;

    public async ValueTask<IReadOnlyList<IItemUseEffect>> PlanAsync(
        int itemId,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        var rule = await judgement.RuleForAsync(itemId, cancellationToken)
            .ConfigureAwait(false);
        if (rule is null)
            return [];

        var effects = new List<IItemUseEffect>(2);
        if (rule.IsConsumedOnUse)
            effects.Add(new ConsumeFromContainerEffect(slotIndex, ConsumedPerUse));

        if (rule.CooldownMilliseconds > 0)
        {
            effects.Add(new StartItemCooldownEffect(
                judgement.CooldownKeyFor(itemId, rule),
                TimeSpan.FromMilliseconds(rule.CooldownMilliseconds)));
        }

        return effects;
    }

    public async ValueTask<ItemUseApplication> ApplyAsync(
        CharacterId characterId,
        IItemContainer container,
        ContainerSlotItem item,
        IReadOnlyList<IItemUseEffect> effects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(effects);

        // The container goes first, as it did when it could still refuse. It no longer can -- the
        // slot was read inside this same use case and nothing outside can have moved it since -- but
        // starting a cooldown before taking the item would still be the wrong order to leave lying
        // around for the next effect somebody adds.
        //
        // Only the consumption of the item being used decides the count reported back. An effect
        // set that also consumed something else -- a reagent, a charge -- would otherwise pair that
        // other pile's remainder with this item's identifier, and the two would name different
        // slots.
        var remaining = item.Count;
        foreach (var consumption in effects.OfType<ConsumeFromContainerEffect>())
        {
            // Today an effect set names only the slot the used item came from, which was read in
            // this same use case and so cannot be empty. An effect naming some other slot -- a
            // reagent, a charge -- would be a decision taken against something never read, and
            // passing over it silently would take the item and skip its cost.
            var taken = consumption.SlotIndex == item.SlotIndex
                ? item
                : await container.ReadSlotAsync(consumption.SlotIndex, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Using item {item.ItemId} was to consume from slot {consumption.SlotIndex}, "
                    + "which holds nothing.");

            var left = await ItemContainerOperations
                .ConsumeAsync(container, taken, consumption.Count, cancellationToken)
                .ConfigureAwait(false);
            if (consumption.SlotIndex == item.SlotIndex)
                remaining = left;
        }

        foreach (var cooldown in effects.OfType<StartItemCooldownEffect>())
            cooldowns.Start(characterId, cooldown.Key, cooldown.Duration);

        return new ItemUseApplication(item.ItemId, remaining);
    }
}
