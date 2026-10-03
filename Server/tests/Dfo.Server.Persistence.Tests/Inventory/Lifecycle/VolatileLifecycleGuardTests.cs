using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Xunit;

namespace Dfo.Server.Persistence.Tests.Inventory.Lifecycle;

public sealed class VolatileLifecycleGuardTests
{
    private static readonly ContainerId Main = new("character:60:Main");
    private static ItemSnapshot Sword => new() { ItemId = 123, LegacyKind = 1, Type = ItemTraits.Equipment, Count = 1 };

    [Fact]
    public async Task TypeAndTemplateCannotBeReboundButEquipmentInstanceValueCanChange()
    {
        var tx = new DomainTransaction();
        var store = new VolatileItemDomainStore().SessionIn(tx);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.CreateAsync(Sword with { Type = ItemTraits.Material }, default).AsTask());
        var reference = await store.PutAsync(Main, 9, await store.CreateAsync(Sword, default), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(reference, x => x with { ItemId = 124 }, default).AsTask());
        var changed = await store.UpdateAsync(reference, x => x with { InstanceValue = 123456 }, default);
        Assert.Equal(reference.ItemUid, changed.ItemUid);
        Assert.Equal(123456, changed.Snapshot.InstanceValue);
        await tx.CommitAsync(default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AvatarAndCreatureWireIdentityCannotChange(bool creature)
    {
        var tx = new DomainTransaction();
        var store = new VolatileItemDomainStore().SessionIn(tx);
        var snapshot = creature
            ? new ItemSnapshot { ItemId = 600, LegacyKind = 5, Type = ItemTraits.Creature, CreatureDetail = new(100, 0, 0, 0, 0, 1, [], 0) }
            : new ItemSnapshot { ItemId = 500, LegacyKind = 8, Type = ItemTraits.Avatar, InstanceValue = 9001, AvatarDetail = new(0, 0, new byte[30], 0, 0, 0) };
        var reference = await store.PutAsync(Main, 9, await store.CreateAsync(snapshot, default), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(reference,
            x => x with { InstanceValue = x.InstanceValue + 1 }, default).AsTask());
        await tx.CommitAsync(default);
    }

    [Fact]
    public async Task ZeroRevivalCounterIsRestrictedToItsCharacterSlot()
    {
        var tx = new DomainTransaction();
        var store = new VolatileItemDomainStore().SessionIn(tx);
        var counter = await store.CreateAsync(new ItemSnapshot { ItemId = 1, IsPile = true, Count = 0 }, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.PutAsync(Main, 2, counter, default).AsTask());
        var placed = await store.PutAsync(Main, 1, counter, default);
        Assert.Equal(0, placed.Count);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(placed,
            x => x with { ItemId = 2 }, default).AsTask());
        await tx.CommitAsync(default);
    }
}
