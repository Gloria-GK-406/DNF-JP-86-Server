using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// A container that keeps nothing, put through every operation without an identifier anywhere.
/// </summary>
/// <remarks>
/// The container this stands in for used to hand out row numbers of its own -- counting from one,
/// pretending to be a database -- because the vocabulary above it addressed items by storage row
/// and it had no storage. Nothing here counts anything. Each step names a container and a slot, and
/// what comes back out names a slot and what is in it.
/// </remarks>
public sealed class TransientContainerNeedsNoIdentifierTests
{
    private static readonly ContainerId Run = new("dungeon");

    [Fact]
    public async Task EveryOperationIsSaidWithNothingButAContainerAndASlot()
    {
        var container = Container();
        var token = CancellationToken.None;

        // Put two piles in.
        await container.PutAsync(65, Core(8474, 5), token);
        await container.PutAsync(66, Core(9001, 3), token);

        // Move one whole pile to an empty slot.
        var moving = await container.ReadSlotAsync(65, token);
        await ItemContainerOperations.RelocateAsync(container, moving!, 3, token);

        // Split it.
        var whole = await container.ReadSlotAsync(3, token);
        await ItemContainerOperations.SplitAsync(container, whole!, 67, 2, token);

        // Trade two slots.
        var left = await container.ReadSlotAsync(67, token);
        var right = await container.ReadSlotAsync(66, token);
        await ItemContainerOperations.SwapAsync(container, left!, right!, token);

        // Pour one pile into another of the same item.
        var pouring = await container.ReadSlotAsync(66, token);
        var into = await container.ReadSlotAsync(3, token);
        await ItemContainerOperations.MergeAsync(container, pouring!, into!, pouring!.Count, token);

        // And take one off.
        var pile = await container.ReadSlotAsync(3, token);
        var remaining = await ItemContainerOperations.ConsumeAsync(container, pile!, 1, token);

        Assert.Equal(4, remaining);
        Assert.Equal((3, 8474, 4), Describe(await container.ReadSlotAsync(3, token)));
        Assert.Equal((67, 9001, 3), Describe(await container.ReadSlotAsync(67, token)));
        Assert.Null(await container.ReadSlotAsync(65, token));
        Assert.Null(await container.ReadSlotAsync(66, token));
    }

    /// <remarks>
    /// What the container hands back describes a place and its contents. There is no member on it
    /// for an item's identity, so a caller could not carry one even if it wanted to -- which is the
    /// form the requirement takes once the vocabulary is gone rather than merely unused.
    /// </remarks>
    [Fact]
    public async Task WhatAContainerHandsBackNamesASlotAndWhatIsInIt()
    {
        var container = Container();
        await container.PutAsync(65, Core(8474, 5), CancellationToken.None);

        var item = await container.ReadSlotAsync(65, CancellationToken.None);

        Assert.Equal(65, item!.SlotIndex);
        Assert.Equal(8474, item.ItemId);
        Assert.Equal(5, item.Count);
        Assert.DoesNotContain(
            typeof(ContainerSlotItem).GetProperties(),
            property => property.Name.Contains("Entry", StringComparison.Ordinal)
                || property.Name.Contains("Uid", StringComparison.Ordinal)
                || property.Name.Contains("RowId", StringComparison.Ordinal));
    }

    private static IItemContainer Container() =>
        new ItemContainerFactory(new MemoryItemStore(), new DomainTransactionScope())
            .CreateTransient(Run, new ContainerShape(InventoryContainerType.Main, 0));

    private static InventoryItemCoreState Core(int itemId, int count) =>
        StackableItemCore.Create(2, itemId, count);

    private static (int Slot, int ItemId, int Count)? Describe(ContainerSlotItem? item) =>
        item is null ? null : (item.SlotIndex, item.ItemId, item.Count);
}
