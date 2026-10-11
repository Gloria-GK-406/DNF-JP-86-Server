using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>One item a tutorial step declares.</summary>
public sealed record DeclaredRewardItem(int ItemId, int Count);

/// <summary>What actually reached the inventory, and where.</summary>
public sealed record PlacedRewardItem(int SlotIndex, int ItemId, int Count);

/// <summary>Everything one tutorial step managed to hand over.</summary>
public sealed record TutorialRewardResult(
    IReadOnlyList<PlacedRewardItem> Placed,
    IReadOnlyList<InventoryRuntimeValueState>? AccountMaterials = null);

/// <summary>One character receiving what a tutorial step declared.</summary>
public sealed record GrantTutorialRewardCommand(
    CharacterId CharacterId,
    IReadOnlyList<DeclaredRewardItem> Declared,
    AccountId AccountId,
    bool CompleteTutorial = false) : ICommand<TutorialRewardResult>;

/// <summary>
/// Puts a tutorial step's items into the character's inventory.
/// </summary>
/// <remarks>
/// A step can declare several items, and they settle together: the command is transactional, so a
/// step that got halfway before something failed leaves the character with none of it rather than
/// with the first two. That also makes each item's placement see the ones before it, because the
/// container's reads inside a use case include what that use case has already written.
///
/// The acknowledgement lists actual inventory changes. Remaining items are sent through system
/// mail; invalid templates or unavailable storage fail the whole transactional step.
/// </remarks>
public sealed class GrantTutorialRewardCommandHandler(
    ICharacterContainerDomainService containers,
    IItemDeliveryDomainService delivery,
    ITutorialCompletionDomainService tutorial)
    : ICommandHandler<GrantTutorialRewardCommand, TutorialRewardResult>
{
    public async ValueTask<TutorialRewardResult> HandleAsync(
        CommandExecutionContext context,
        GrantTutorialRewardCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Completion and any reward commit together; a repeated completion grants nothing.
        if (command.CompleteTutorial && !await tutorial.CompleteOnceAsync(
                new(command.AccountId, command.CharacterId), cancellationToken).ConfigureAwait(false))
            return new TutorialRewardResult([], []);

        var container = containers.FindContainer(command.CharacterId, InventoryContainerType.Main);
        if (container is null)
            throw new InvalidOperationException("Tutorial reward inventory is unavailable.");

        var placed = new List<PlacedRewardItem>(command.Declared.Count);
        var accountChanges = new List<InventoryRuntimeValueState>();
        var receipt = await delivery.DeliverAsync(command.AccountId, command.CharacterId,
            command.Declared.Select(x => new ItemDeliveryGrant(x.ItemId, x.Count)).ToArray(), "教程奖励", cancellationToken, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
        foreach (var item in receipt.Inventory)
        {
            placed.Add(new(ItemGrantWireProjection.Index(item), item.Snapshot.ItemId, item.Count));
            if (ItemGrantWireProjection.Shared(item) is { } shared) accountChanges.Add(shared);
        }

        return new TutorialRewardResult(placed, accountChanges);
    }
}
