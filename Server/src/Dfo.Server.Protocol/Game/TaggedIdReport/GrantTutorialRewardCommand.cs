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
/// acknowledgement would tell the client to draw something the server does not hold. Deciding that
/// is this handler's own business: the bag reports what became of each item, and only something
/// that knows an acknowledgement exists can decide that two of those answers mean "say nothing".
/// </remarks>
public sealed class GrantTutorialRewardCommandHandler(
    ICharacterContainerDomainService containers)
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
            var outcome = await container
                .AddAsync(declared.ItemId, declared.Count, cancellationToken)
                .ConfigureAwait(false);
            switch (outcome)
            {
                case ItemPlacementOutcome.AddedToPile topped:
                    placed.Add(new PlacedRewardItem(
                        topped.SlotIndex, declared.ItemId, topped.Added));
                    break;

                case ItemPlacementOutcome.PutInEmptySlot opened:
                    placed.Add(new PlacedRewardItem(
                        opened.SlotIndex, declared.ItemId, opened.Count));
                    break;

                case ItemPlacementOutcome.NoRoom:
                case ItemPlacementOutcome.UnknownItem:
                    // Left out of the acknowledgement, which is what the caller reports and logs.
                    break;

                default:
                    // Listed rather than defaulted: an outcome nobody read would quietly become a
                    // reward the player was promised and never given, with nothing said.
                    throw new ArgumentOutOfRangeException(
                        nameof(command), outcome, "No reading was decided for this outcome.");
            }
        }

        return new TutorialRewardResult(placed);
    }
}
