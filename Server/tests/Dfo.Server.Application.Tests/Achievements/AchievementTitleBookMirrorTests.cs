using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Achievements;
public sealed partial class AchievementTitleBookTests
{
    private static readonly BusinessSlot TitleWorn = new(BusinessInventorySpace.WornEquipment, 1);
    private static readonly BusinessSlot TitleBag = new(BusinessInventorySpace.CarriedEquipment, 0);
    private static TitleBookSlotDefinition[] OpenTitleSlots(Fixture f) => f.Services.GetRequiredService<ITitleBookRuleService>()
        .Catalog().Single(x=>x.Category==4).Slots.Where(x=>x.SlotType>=0 && x.QuestId==-1 && x.ItemIds.Contains(-1)).Take(2).ToArray();
    private static async ValueTask<ItemSnapshot> SeedTitle(Fixture f, int itemId, ContainerId container, int slot, bool mirror = false)
        => await f.WriteAsync(async () => {
            var s=f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            return (await s.PutAsync(container,slot,await s.CreateAsync(new() {ItemId=itemId,LegacyKind=1,Type=ItemTraits.Equipment,IsMirror=mirror,Attribute=12,EnchantCardId=123,Durability=47},default),default)).Snapshot;
        });
    private static async ValueTask<ItemSnapshot?> ReadTitle(Fixture f, ContainerId container, int slot)
        => await f.WriteAsync(async () => (await f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!).GetAsync(container,slot,default))?.Snapshot);
    private static void BindTitleContainers(Fixture f)
    {
        foreach (var type in new[] {InventoryContainerType.Main,InventoryContainerType.Equipment})
            TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(Character,
                new PersistentItemContainer(ContainerId.ForCharacter(Character,type),new(type,0),
                    f.Services.GetRequiredService<IItemDomainStore>(),f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                    f.Services.GetRequiredService<IItemPlacementRuleService>(),f.Services.GetRequiredService<IItemSlotRuleService>())));
    }
    private static async ValueTask<ItemMoveResult> MoveTitle(Fixture f, BusinessSlot source, BusinessSlot destination)
    {
        var h=new MoveItemSpaceCommandHandler(f.Services.GetRequiredService<IBusinessInventorySlotDomainService>(),f.Services.GetRequiredService<IItemMovementDomainService>());
        return await h.HandleAsync(new(Guid.NewGuid(),typeof(MoveItemSpaceCommand),h.GetType(),CommandExecutionKind.Transactional),new(Character,source,1,destination),default);
    }
    [PvfFact]
    public async Task Switching_book_titles_preserves_both_originals_and_persists_only_the_current_equipment_mirror()
    {
        await using var f=new Fixture();
        var slots=OpenTitleSlots(f); var book=ContainerId.ForCharacter(Character,InventoryContainerType.TitleBookEvent);
        var first=await SeedTitle(f,26596,book,slots[0].Index); var second=await SeedTitle(f,26628,book,slots[1].Index);
        var equip=ContainerId.ForCharacter(Character,InventoryContainerType.Equipment);
        Assert.True((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleWorn,26596,4,slots[0].Index,default))).Success);
        var oldMirror=(await ReadTitle(f,equip,13))!;
        Assert.True(oldMirror.IsMirror); Assert.Equal(first.ItemUid,oldMirror.MirrorSourceUid);
        Assert.True((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleWorn,26628,4,slots[1].Index,default))).Success);
        Assert.DoesNotContain(oldMirror.ItemUid!.Value.Value,f.ItemUids(26596).Select(x=>(int)x));
        Assert.Equal(first,await ReadTitle(f,book,slots[0].Index)); Assert.Equal(second,await ReadTitle(f,book,slots[1].Index));
        var projected = await f.Titles.GetItemsAsync(Character, 4, default);
        Assert.Contains(projected, x => x.Identity == first.ItemUid && x.SlotIndex == slots[0].Index && x.Core == first.ToCore());
        Assert.Contains(projected, x => x.Identity == second.ItemUid && x.SlotIndex == slots[1].Index && x.Core == second.ToCore());
        Assert.Contains((await f.Titles.GetAsync(Character, default)).SelectMany(x => x.Entries), x => x.ItemId == second.ItemId);
        Assert.DoesNotContain(projected, x => x.Identity == oldMirror.ItemUid);
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM items WHERE is_mirror=1"));
        await using var restored=new Fixture(f.Path);
        var current=(await ReadTitle(restored,equip,13))!;
        Assert.True(current.IsMirror); Assert.Equal(second.ItemUid,current.MirrorSourceUid); Assert.Equal(second.ToCore(),current.ToCore());
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM item_slots WHERE list_type=0"));
    }
    [PvfTheory]
    [InlineData(false,true)]
    [InlineData(true,true)]
    [InlineData(true,false)]
    public async Task Mirror_unequip_deletes_only_the_projection_leaves_destination_untouched_and_rolls_back_atomically(bool occupied, bool commit)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f=new Fixture(); var slot=OpenTitleSlots(f)[0];
            var book=ContainerId.ForCharacter(Character,InventoryContainerType.TitleBookEvent); var bag=ContainerId.ForCharacter(Character,InventoryContainerType.Main);
            var source=await SeedTitle(f,26596,book,slot.Index);
            var target=occupied ? await SeedTitle(f,26628,bag,9) : null;
            Assert.True((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleWorn,26596,4,slot.Index,default))).Success);
            BindTitleContainers(f); var before=f.ItemsAndPositions(); var events=f.Scalar("SELECT COUNT(*) FROM domain_events");
            await using (var tx=f.Scope.Begin()) {
                Assert.Equal(ItemMoveStatus.MirrorRemoved,(await MoveTitle(f,TitleWorn,TitleBag)).Status);
                if(commit)await tx.CommitAsync(default);
            }
            if(!commit) {Assert.Equal(before,f.ItemsAndPositions());Assert.Equal(events,f.Scalar("SELECT COUNT(*) FROM domain_events"));return;}
            Assert.Null(await ReadTitle(f,ContainerId.ForCharacter(Character,InventoryContainerType.Equipment),13));
            Assert.Equal(target,await ReadTitle(f,bag,9));Assert.Equal(source,await ReadTitle(f,book,slot.Index));
            Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM items WHERE is_mirror=1"));
            await using var restored=new Fixture(f.Path);
            await restored.WriteAsync(async()=>await restored.Titles.PrepareAsync(Character,default));
            Assert.Null(await ReadTitle(restored,ContainerId.ForCharacter(Character,InventoryContainerType.Equipment),13));
        });
    }
    [PvfFact]
    public async Task Invalid_destination_and_noop_do_not_delete_mirrors_and_real_equipment_can_replace_them()
    {
        await using var f=new Fixture();var slot=OpenTitleSlots(f)[0];var book=ContainerId.ForCharacter(Character,InventoryContainerType.TitleBookEvent);
        await SeedTitle(f,26596,book,slot.Index);
        Assert.True((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleWorn,26596,4,slot.Index,default))).Success);
        var bag=ContainerId.ForCharacter(Character,InventoryContainerType.Main);var real=await SeedTitle(f,26628,bag,9);BindTitleContainers(f);
        var before=f.ItemsAndPositions();
        Assert.Equal(ItemMoveStatus.Unchanged,(await f.WriteAsync(async()=>await MoveTitle(f,TitleWorn,TitleWorn))).Status);
        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot,(await f.WriteAsync(async()=>await MoveTitle(f,TitleWorn,new(BusinessInventorySpace.Consumable,0)))).Status);
        Assert.Equal(before,f.ItemsAndPositions());
        Assert.Equal(ItemMoveStatus.Moved,(await f.WriteAsync(async()=>await MoveTitle(f,TitleBag,TitleWorn))).Status);
        Assert.Equal(real,await ReadTitle(f,ContainerId.ForCharacter(Character,InventoryContainerType.Equipment),13));
        Assert.Null(await ReadTitle(f,bag,9));Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM items WHERE is_mirror=1"));
        Assert.Equal(ItemMoveStatus.Moved,(await f.WriteAsync(async()=>await MoveTitle(f,TitleWorn,TitleBag))).Status);
        Assert.Equal(real,await ReadTitle(f,bag,9));
    }
    private sealed class RejectNewTitleWear : IEquipmentWearDomainService
    {
        public ItemSnapshot UnsealForEquip(ItemSnapshot item) => item with { SealFlag = 0 };
        public ValueTask<bool> CanWearAsync(Dfo.Server.Data.Characters.CharacterId character, int itemId, CancellationToken ct) => ValueTask.FromResult(itemId != 26628);
    }
    [PvfTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task Real_title_replaces_mirror_in_one_transaction_and_refusal_or_rollback_preserves_old_wear(int mode)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new Fixture(wear: mode == 4 ? new RejectNewTitleWear() : null);
            var slot = OpenTitleSlots(f)[0]; var book = ContainerId.ForCharacter(Character, InventoryContainerType.TitleBookEvent);
            var bag = ContainerId.ForCharacter(Character, InventoryContainerType.Main); var equip = ContainerId.ForCharacter(Character, InventoryContainerType.Equipment);
            var original = await SeedTitle(f, 26596, book, slot.Index);
            Assert.True((await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, TitleWorn, 26596, 4, slot.Index, default))).Success);
            var real = await SeedTitle(f, 26628, bag, 9);
            if (mode == 6) real = await f.WriteAsync(async () => {
                var store = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
                var item = (await store.GetAsync(bag, 9, default))!;
                return (await store.UpdateAsync(item, x => x with { ExpireTime = checked((int)DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()) }, default)).Snapshot;
            });
            Assert.True(await f.WriteAsync(async () => await f.Titles.SetAppearanceAsync(Character, 26628, default)));
            if (mode == 3) await f.WriteAsync(async () => {
                var store = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
                var item = (await store.GetAsync(bag, 9, default))!;
                await store.UpdateAsync(item, x => x with { EquipmentLockId = 1 }, default);
            });
            var before = f.ItemsAndPositions(); var events = f.Scalar("SELECT COUNT(*) FROM domain_events");
            await using (var tx = f.Scope.Begin()) {
                var result = await f.Titles.EquipCarriedTitleReplacingMirrorAsync(Character, TitleBag,
                    mode == 1 ? 26596 : 26628, 4, mode == 2 ? slot.Index + 1 : slot.Index, default);
                Assert.Equal(mode is 0 or 5 or 6, result.Success);
                if (result.Success) Assert.True(result.CarriedInventoryChanged);
                if (mode != 5) await tx.CommitAsync(default);
            }
            if (mode is not (0 or 6)) {
                Assert.Equal(before, f.ItemsAndPositions()); Assert.Equal(events, f.Scalar("SELECT COUNT(*) FROM domain_events"));
            } else {
                Assert.Equal(real, await ReadTitle(f, equip, 13)); Assert.Null(await ReadTitle(f, bag, 9));
                Assert.Equal(original, await ReadTitle(f, book, slot.Index)); Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM items WHERE is_mirror=1"));
                Assert.Null((await f.Titles.GetAsync(Character, default))[4].WearSource);
            }
        });
    }
    [PvfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_full_bag_blocks_displacement_of_real_titles_but_does_not_block_switching_mirrors(bool oldIsMirror)
    {
        await using var f=new Fixture();var slots=OpenTitleSlots(f);var book=ContainerId.ForCharacter(Character,InventoryContainerType.TitleBookEvent);
        await SeedTitle(f,26596,book,slots[0].Index);await SeedTitle(f,26628,book,slots[1].Index);
        if(oldIsMirror)Assert.True((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleWorn,26596,4,slots[0].Index,default))).Success);
        else await SeedTitle(f,26596,ContainerId.ForCharacter(Character,InventoryContainerType.Equipment),13);
        var bag=ContainerId.ForCharacter(Character,InventoryContainerType.Main);
        var open=await f.Services.GetRequiredService<IInventoryCapacityDomainService>().OpenSlotCountAsync(bag,TitleBag.Space,default);
        for(var i=0;i<open;i++) {
            var game=f.Services.GetRequiredService<IInventorySlotMappingRuleService>().ToGameSlot(new(TitleBag.Space,i));
            await SeedTitle(f,26628,bag,game.Index);
        }
        var before=f.ItemsAndPositions();
        var result=await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleWorn,26628,4,slots[1].Index,default));
        Assert.Equal(oldIsMirror,result.Success);
        if(!oldIsMirror){Assert.Equal("InventoryFull",result.Reason);Assert.Equal(before,f.ItemsAndPositions());}
        else {Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM items WHERE is_mirror=1"));Assert.Equal(open,f.Scalar("SELECT COUNT(*) FROM item_slots WHERE list_type=0"));}
    }
    [PvfFact]
    public async Task Worn_original_cannot_be_extracted_until_its_mirror_is_cancelled()
    {
        await using var f=new Fixture();var slot=OpenTitleSlots(f)[0];var book=ContainerId.ForCharacter(Character,InventoryContainerType.TitleBookEvent);
        var original=await SeedTitle(f,26596,book,slot.Index);
        Assert.True((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleWorn,26596,4,slot.Index,default))).Success);
        var before = f.ItemsAndPositions();
        var refused = await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleBag,26596,4,slot.Index,default));
        Assert.False(refused.Success);
        Assert.Equal("TitleEquipped", refused.Reason);
        Assert.Equal(before, f.ItemsAndPositions());
        Assert.True((await f.WriteAsync(async()=>await f.Titles.StoreAsync(Character,TitleWorn,26596,4,slot.Index,default))).Success);
        Assert.True((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleBag,26596,4,slot.Index,default))).Success);
        Assert.Equal(original,await ReadTitle(f,ContainerId.ForCharacter(Character,InventoryContainerType.Main),9));
        Assert.Null(await ReadTitle(f,ContainerId.ForCharacter(Character,InventoryContainerType.Equipment),13));
        Assert.Null(await ReadTitle(f,book,slot.Index));Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM items WHERE is_mirror=1"));
    }
    [PvfFact]
    public async Task Mirror_removal_is_generic_equipment_behavior_not_a_title_template_exception()
    {
        await using var f=new Fixture();var equip=ContainerId.ForCharacter(Character,InventoryContainerType.Equipment);
        await SeedTitle(f,10018,equip,14,mirror:true);BindTitleContainers(f);
        Assert.Equal(ItemMoveStatus.MirrorRemoved,(await f.WriteAsync(async()=>await MoveTitle(f,new(BusinessInventorySpace.WornEquipment,2),TitleBag))).Status);
        Assert.Empty(f.ItemUids(10018));Assert.Null(await ReadTitle(f,ContainerId.ForCharacter(Character,InventoryContainerType.Main),9));
    }
    [PvfFact]
    public async Task Worn_title_appearance_is_owned_persistent_and_independent_of_its_book_original()
    {
        await using var f = new Fixture(); var slots = OpenTitleSlots(f);
        var book = ContainerId.ForCharacter(Character, InventoryContainerType.TitleBookEvent);
        var original = await SeedTitle(f, 26596, book, slots[0].Index);
        await SeedTitle(f, 26628, book, slots[1].Index);
        Assert.False(await f.WriteAsync(async () => await f.Titles.SetAppearanceAsync(Character, 26628, default)));
        Assert.True((await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, TitleWorn, 26596, 4, slots[0].Index, default))).Success);
        var equipped = ContainerId.ForCharacter(Character, InventoryContainerType.Equipment);
        var before = (await ReadTitle(f, equipped, 13))!;
        Assert.Equal(26596, before.DisplayItemId);
        Assert.False(await f.WriteAsync(async () => await f.Titles.SetAppearanceAsync(Character, 10018, default))); // not a title
        Assert.False(await f.WriteAsync(async () => await f.Titles.SetAppearanceAsync(Character, -1, default)));
        Assert.True(await f.WriteAsync(async () => await f.Titles.SetAppearanceAsync(Character, 26628, default)));
        var changed = (await ReadTitle(f, equipped, 13))!;
        Assert.Equal(before with { AppearanceItemId = 26628 }, changed);
        Assert.Equal(original, await ReadTitle(f, book, slots[0].Index));
        Assert.Equal(26628, changed.DisplayItemId); Assert.Equal(26596, changed.ItemId);
        Assert.Equal(26628, Assert.Single(await f.Titles.GetItemsAsync(Character, 4, default), x => x.Core.ItemId == 26628).Core.ItemId);
        await using (var restored = new Fixture(f.Path)) Assert.Equal(changed, await ReadTitle(restored, equipped, 13));
        Assert.True(await f.WriteAsync(async () => await f.Titles.SetAppearanceAsync(Character, 0, default)));
        Assert.Equal(before, await ReadTitle(f, equipped, 13));
        Assert.True(await f.WriteAsync(async () => await f.Titles.SetAppearanceAsync(Character, 26628, default)));
        Assert.True((await f.WriteAsync(async () => await f.Titles.StoreAsync(Character, TitleWorn, 26596, 4, slots[0].Index, default))).Success);
        Assert.Null(await ReadTitle(f, equipped, 13)); Assert.Equal(original, await ReadTitle(f, book, slots[0].Index));
        Assert.True((await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, TitleWorn, 26596, 4, slots[0].Index, default))).Success);
        Assert.Equal(26596, (await ReadTitle(f, equipped, 13))!.DisplayItemId);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM item_slots WHERE list_type=0"));
    }

    [PvfFact]
    public async Task Equip_and_cancel_commands_preserve_ownership_and_restore_the_native_book_slot()
    {
        await using var f = new Fixture(); var slot = OpenTitleSlots(f)[0];
        var book = ContainerId.ForCharacter(Character, InventoryContainerType.TitleBookEvent);
        var original = await SeedTitle(f, 26596, book, slot.Index);
        for (var i = 0; i < 6; i++)
        {
            var result = i % 2 == 0
                ? await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, TitleWorn, 26596, 4, slot.Index, default))
                : await f.WriteAsync(async () => await f.Titles.StoreAsync(Character, TitleWorn, 26596, 4, slot.Index, default));
            Assert.True(result.Success);
            var projected = Assert.Single(await f.Titles.GetItemsAsync(Character, 4, default));
            Assert.Equal(26596, projected.Core.ItemId);
            Assert.Contains((await f.Titles.GetAsync(Character, default)).SelectMany(x => x.Entries), x => x.ItemId == 26596);
            Assert.Equal(i % 2 == 0, await ReadTitle(f, ContainerId.ForCharacter(Character, InventoryContainerType.Equipment), 13) is not null);
            Assert.Equal(original, await ReadTitle(f, book, slot.Index));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM item_slots WHERE list_type=0"));
        }
    }

    [PvfFact]
    public async Task Repeated_get_commands_do_not_toggle_the_equipped_title_off()
    {
        await using var f = new Fixture(); var slot = OpenTitleSlots(f)[0];
        var book = ContainerId.ForCharacter(Character, InventoryContainerType.TitleBookEvent);
        var original = await SeedTitle(f, 26596, book, slot.Index);
        await SeedTitle(f, 26628, book, OpenTitleSlots(f)[1].Index);
        ItemIdentity? firstMirror = null;
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, TitleWorn, 26596, 4, slot.Index, default))).Success);
            var equipped = (await ReadTitle(f, ContainerId.ForCharacter(Character, InventoryContainerType.Equipment), 13))!;
            Assert.Equal(original.ItemUid, equipped.MirrorSourceUid);
            Assert.Equal(i == 0 ? null : (int?)26628, equipped.AppearanceItemId);
            if (i == 0) firstMirror = equipped.ItemUid;
            else Assert.Equal(firstMirror, equipped.ItemUid);
            Assert.True(await f.WriteAsync(async () => await f.Titles.SetAppearanceAsync(Character, 26628, default)));
            Assert.Equal(original, await ReadTitle(f, book, slot.Index));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM items WHERE is_mirror=1"));
        }
    }
    [PvfFact]
    public async Task Client_book_projection_distinguishes_same_template_instances_by_source_uid_and_survives_restart()
    {
        await using var f = new Fixture(); var slots = OpenTitleSlots(f);
        var book = ContainerId.ForCharacter(Character, InventoryContainerType.TitleBookEvent);
        var first = await SeedTitle(f, 26596, book, slots[0].Index);
        var second = await SeedTitle(f, 26596, book, slots[1].Index);
        Assert.True((await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, TitleWorn, 26596, 4, slots[1].Index, default))).Success);
        await using var restored = new Fixture(f.Path);
        var before = restored.ItemsAndPositions();
        var delta = await restored.Titles.GetItemsAsync(Character, 4, default);
        Assert.Equal(2, delta.Count);
        Assert.Equal(first.ItemUid, Assert.Single(delta, x => x.SlotIndex == slots[0].Index).Identity);
        Assert.Equal(second.ItemUid, Assert.Single(delta, x => x.SlotIndex == slots[1].Index).Identity);
        var categories = await restored.Titles.GetAsync(Character, default);
        Assert.All(categories, x => Assert.Equal(new Dfo.Server.Data.Characters.CharacterTitleBookWearSource(4, slots[1].Index, 26596), x.WearSource));
        Assert.Equal(2, categories.SelectMany(x => x.Entries).Count());
        Assert.Equal(before, restored.ItemsAndPositions());
        Assert.Equal(second, await ReadTitle(restored, book, slots[1].Index));
    }

}
