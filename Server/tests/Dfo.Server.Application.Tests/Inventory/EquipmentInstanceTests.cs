using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentInstanceTests
{
    private static readonly BusinessSlot First = new(BusinessInventorySpace.CarriedEquipment, 0);

    [Fact]
    public async Task Staged_equipment_update_retains_its_real_store_identity()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1234));
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var transaction = scope.Begin();
        var session = shop.Services.GetRequiredService<IItemDomainStore>().SessionIn(scope.Current!);
        var address = ContainerId.ForCharacter(shop.Session.CharacterId, InventoryContainerType.Main);
        var original = (await session.ReadAsync(address, 9, default))!;
        var bag = shop.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(shop.Session.CharacterId, BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.CarriedEquipment);

        await bag.PutAsync(0, original.Core with { EnchantUpgradeCount = 1 }, default);

        var current = (await session.ReadAsync(address, 9, default))!;
        Assert.Equal(original.Identity, current.Identity);
        Assert.Equal((byte)1, current.Core.EnchantUpgradeCount);
        Assert.Equal(original.Identity, Assert.Single(await session.ReadAllAsync(address, default)).Identity);
    }

    [Fact]
    public async Task Multiple_handles_merge_changes_on_only_the_selected_uid()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1234)
            with { Durability = 20, Attribute = 0xA0, EnchantUpgradeCount = 7 });
        await shop.SeedCore(10, StackableItemCore.Create(1, 3000, 1234)
            with { Durability = 20, Attribute = 0xA0, EnchantUpgradeCount = 7 });
        await using var transaction = Scope(shop).Begin();
        var collection = Collection(shop);
        var first = (await collection.FindAtAsync(First, default))!;
        var second = (await collection.FindAsync(first.ItemUid, default))!;
        var other = (await collection.FindAtAsync(First with { Index = 1 }, default))!;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = first.SetReinforcementLevelAsync(32, default);
        });
        Assert.Equal((byte)0xA0, (await first.ReadAsync(default)).Core.Attribute);
        await first.SetReinforcementLevelAsync(3, default);
        await second.SetEnchantmentAsync(7654, default);
        await first.DecreaseDurabilityAsync(2, default);

        var current = await second.ReadAsync(default);
        Assert.Equal(first.ItemUid, current.ItemUid);
        Assert.Equal((byte)3, current.Core.ReinforcementLevel);
        Assert.Equal((byte)0xA3, current.Core.Attribute);
        Assert.Equal((byte)7, current.Core.EnchantUpgradeCount);
        Assert.Equal(7654, current.Core.EnchantCardId);
        Assert.Equal((ushort)18, current.Core.Durability);
        var untouched = await other.ReadAsync(default);
        Assert.Equal((byte)0, untouched.Core.ReinforcementLevel);
        Assert.Equal((byte)7, untouched.Core.EnchantUpgradeCount);
        Assert.Equal(0, untouched.Core.EnchantCardId);
        Assert.NotEqual(current.ItemUid, untouched.ItemUid);
        await transaction.CommitAsync(default);
    }

    [Fact]
    public async Task Updated_identity_follows_consecutive_transfers_and_ignores_same_template_replacement()
    {
        using var shop = new ShopFixture();
        var core = StackableItemCore.Create(1, 3000, 1234) with { Durability = 20 };
        await shop.SeedCore(9, core);
        await shop.SeedCore(10, core);
        var uid = 0;
        var replacementUid = 0;
        await using (var transaction = Scope(shop).Begin())
        {
            var collection = Collection(shop);
            var instance = (await collection.FindAtAsync(First, default))!;
            uid = instance.ItemUid;
            replacementUid = (await collection.FindAtAsync(First with { Index = 1 }, default))!.ItemUid;
            await instance.SetEnchantmentAsync(5432, default);
            var bag = Bag(shop);
            await Move(bag, 0, 2);
            await Move(bag, 1, 0);
            await Move(bag, 2, 3);
            await instance.SetReinforcementLevelAsync(2, default);
            var current = await instance.ReadAsync(default);
            Assert.Equal(First with { Index = 3 }, current.Slot);
            Assert.Equal(5432, current.Core.EnchantCardId);
            Assert.Equal((byte)2, current.Core.ReinforcementLevel);
            var replacement = (await collection.FindAtAsync(First, default))!;
            Assert.Equal(replacementUid, replacement.ItemUid);
            Assert.Equal(core, (await replacement.ReadAsync(default)).Core);
            await transaction.CommitAsync(default);
        }
        await using var read = Scope(shop).Begin();
        var persisted = (await Collection(shop).FindAsync(uid, default))!;
        Assert.Equal(First with { Index = 3 }, (await persisted.ReadAsync(default)).Slot);
        Assert.Equal(replacementUid, (await Collection(shop).FindAtAsync(First, default))!.ItemUid);
    }

    [Fact]
    public async Task Another_owner_cannot_find_or_modify_the_uid()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1234));
        await using var transaction = Scope(shop).Begin();
        var instance = (await Collection(shop).FindAtAsync(First, default))!;
        var other = shop.Services.GetRequiredService<IEquipmentDomainService>().Of(new(2));
        Assert.Null(await other.FindAsync(instance.ItemUid, default));
        Assert.Null(await other.FindAtAsync(First, default));
        Assert.Equal(shop.Session.CharacterId, (await instance.ReadAsync(default)).Owner);
    }

    [Fact]
    public async Task Destroyed_uid_invalidates_every_handle_without_affecting_other_equipment()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1234));
        await shop.SeedCore(10, StackableItemCore.Create(1, 3000, 1234));
        await using var transaction = Scope(shop).Begin();
        var collection = Collection(shop);
        var first = (await collection.FindAtAsync(First, default))!;
        var second = (await collection.FindAsync(first.ItemUid, default))!;
        var removed = await first.DestroyAsync(default);
        Assert.Equal(first.ItemUid, removed.ItemUid);
        Assert.Null(await collection.FindAsync(first.ItemUid, default));
        Assert.Null(await collection.FindAtAsync(First, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.ReadAsync(default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.SetDurabilityAsync(10, default).AsTask());
        Assert.NotNull(await collection.FindAtAsync(First with { Index = 1 }, default));
        await transaction.CommitAsync(default);
    }

    [Fact]
    public async Task Replacing_a_destroyed_slot_with_identical_core_cannot_revive_old_handles()
    {
        using var shop = new ShopFixture();
        var core = StackableItemCore.Create(1, 3000, 1234);
        await shop.SeedCore(9, core);
        await using var transaction = Scope(shop).Begin();
        var old = (await Collection(shop).FindAtAsync(First, default))!;
        var another = (await Collection(shop).FindAsync(old.ItemUid, default))!;
        await old.DestroyAsync(default);
        await Bag(shop).PutAsync(0, core, default);

        Assert.Null(await Collection(shop).FindAsync(old.ItemUid, default));
        Assert.Null(await Collection(shop).FindAtAsync(First, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => another.SetReinforcementLevelAsync(1, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => old.ReadAsync(default).AsTask());
    }

    [Fact]
    public async Task Rollback_preserves_original_state_and_old_handles_cannot_join_new_transaction()
    {
        using var shop = new ShopFixture();
        var core = StackableItemCore.Create(1, 3000, 1234) with { Durability = 20 };
        await shop.SeedCore(9, core);
        IEquipmentInstance old;
        IEquipmentCollection collection;
        await using (var transaction = Scope(shop).Begin())
        {
            collection = Collection(shop);
            old = (await collection.FindAtAsync(First, default))!;
            await old.SetDimensionalAttributeAsync(2, 100, default);
            await old.DestroyAsync(default);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => old.ReadAsync(default).AsTask());
        await using var next = Scope(shop).Begin();
        await Assert.ThrowsAsync<InvalidOperationException>(() => collection.FindAsync(old.ItemUid, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => old.SetReinforcementLevelAsync(1, default).AsTask());
        var current = (await Collection(shop).FindAsync(old.ItemUid, default))!;
        Assert.Equal(core, (await current.ReadAsync(default)).Core);
    }

    [Fact]
    public async Task Commit_invalidates_instance_and_collection_and_does_not_allow_detached_access()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1234));
        var domain = shop.Services.GetRequiredService<IEquipmentDomainService>();
        Assert.Throws<InvalidOperationException>(() => domain.Of(shop.Session.CharacterId));
        await using var transaction = Scope(shop).Begin();
        var collection = Collection(shop);
        var instance = (await collection.FindAtAsync(First, default))!;
        await instance.SetForgingLevelAsync(1, default);
        await transaction.CommitAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => instance.ReadAsync(default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => collection.FindAtAsync(First, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => instance.DestroyAsync(default).AsTask());
    }

    [Fact]
    public async Task Newly_staged_equipment_has_no_instance_until_its_uid_is_allocated()
    {
        using var shop = new ShopFixture();
        await using (var transaction = Scope(shop).Begin())
        {
            await Bag(shop).PutAsync(0, StackableItemCore.Create(1, 3000, 1234), default);
            Assert.Null(await Collection(shop).FindAtAsync(First, default));
            await transaction.CommitAsync(default);
        }
        await using var next = Scope(shop).Begin();
        Assert.True((await Collection(shop).FindAtAsync(First, default))!.ItemUid > 0);
    }

    private static DomainTransactionScope Scope(ShopFixture shop) =>
        (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
    private static IEquipmentCollection Collection(ShopFixture shop) =>
        shop.Services.GetRequiredService<IEquipmentDomainService>().Of(shop.Session.CharacterId);
    private static IBusinessItemContainer Bag(ShopFixture shop) =>
        shop.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(shop.Session.CharacterId, BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.CarriedEquipment);
    private static async ValueTask Move(IBusinessItemContainer bag, int from, int to)
    {
        var core = (await bag.ReadSlotAsync(from, default))!.Core;
        await bag.PutTransferredAsync(to, core, bag, from, default);
        await bag.ClearAsync(from, default);
        await bag.FlushAsync(default);
    }
}
