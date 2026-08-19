using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>One item a tutorial step declares.</summary>
public sealed record DeclaredRewardItem(int ItemId, int Count);

/// <summary>What actually reached the inventory, and where.</summary>
public sealed record PlacedRewardItem(int SlotIndex, int ItemId, int Count);

/// <summary>Everything one tutorial step managed to hand over.</summary>
public sealed record TutorialRewardResult(IReadOnlyList<PlacedRewardItem> Placed);

/// <summary>One character receiving what a tutorial step declared.</summary>
public sealed record GrantTutorialRewardCommand(
    CharacterId CharacterId,
    IReadOnlyList<DeclaredRewardItem> Declared) : ICommand<TutorialRewardResult>;

/// <summary>
/// Puts a tutorial step's items into the character's inventory.
/// </summary>
/// <remarks>
/// A step can declare several items, and they settle together: the command is transactional, so a
/// step that got halfway before something failed leaves the character with none of it rather than
/// with the first two. That also makes each item's placement see the ones before it, because the
/// container's reads inside a use case include what that use case has already written.
///
/// What the archive declares and what the character receives are not the same list. An item the
/// archive knows nowhere to put, or whose band is full, is left out -- naming it in the
/// acknowledgement would tell the client to draw something the server does not hold.
/// </remarks>
public sealed class GrantTutorialRewardCommandHandler(
    ICharacterContainerDomainService containers,
    IItemPlacementDefinitionSource placements,
    IItemArrivalRuleService arrivalRules)
    : ICommandHandler<GrantTutorialRewardCommand, TutorialRewardResult>
{
    public async ValueTask<TutorialRewardResult> HandleAsync(
        CommandExecutionContext context,
        GrantTutorialRewardCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var container = containers.FindContainer(command.CharacterId, InventoryContainerType.Main);
        if (container is null)
            return new TutorialRewardResult([]);

        var placed = new List<PlacedRewardItem>(command.Declared.Count);
        foreach (var declared in command.Declared)
        {
            var placement = await placements.GetAsync(declared.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (placement is null)
                continue;

            var held = await container.ReadAllAsync(cancellationToken).ConfigureAwait(false);
            var arrival = arrivalRules.ForGrant(
                [.. held.Where(item => item.ItemId == declared.ItemId)
                    .Select(item => new ItemPile(item.SlotIndex, item.Count))],
                declared.Count,
                placement,
                held.Select(item => item.SlotIndex).ToHashSet());

            switch (arrival)
            {
                case ItemArrival.OntoExistingPile topped:
                    var pile = held.Single(item => item.SlotIndex == topped.SlotIndex);
                    await container.PutAsync(
                        topped.SlotIndex, pile.Core with { Value = topped.Total }, cancellationToken)
                        .ConfigureAwait(false);
                    placed.Add(new PlacedRewardItem(
                        topped.SlotIndex, declared.ItemId, topped.Added));
                    break;

                case ItemArrival.IntoEmptySlot opened:
                    await container.PutAsync(
                        opened.SlotIndex,
                        StackableItemCore.Create(placement.ItemKind, declared.ItemId, opened.Count),
                        cancellationToken).ConfigureAwait(false);
                    placed.Add(new PlacedRewardItem(
                        opened.SlotIndex, declared.ItemId, opened.Count));
                    break;

                default:
                    // The item's band is full. It is left out of the acknowledgement, which is what
                    // the caller reports and logs.
                    break;
            }
        }

        return new TutorialRewardResult(placed);
    }
}
