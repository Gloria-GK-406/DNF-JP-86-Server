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
            f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(Character,
                new PersistentItemContainer(ContainerId.ForCharacter(Character,type),new(type,0),
                    f.Services.GetRequiredService<IItemDomainStore>(),f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                    f.Services.GetRequiredService<IItemPlacementRuleService>(),f.Services.GetRequiredService<IItemSlotRuleService>()));
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
    public async Task Explicit_extraction_of_the_original_to_a_bag_removes_its_active_mirror()
    {
        await using var f=new Fixture();var slot=OpenTitleSlots(f)[0];var book=ContainerId.ForCharacter(Character,InventoryContainerType.TitleBookEvent);
        var original=await SeedTitle(f,26596,book,slot.Index);
        Assert.True((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,TitleWorn,26596,4,slot.Index,default))).Success);
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
}
