using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class AvatarAppearanceTests
{
    private sealed class Definitions : IAvatarAppearanceDefinitionSource
    {
        public ValueTask<bool?> IsCloneAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<bool?>(itemId == 42609);
    }
    private static ItemSnapshot Avatar(int id, int? appearance = null) => ItemSnapshot.FromCore(
        StackableItemCore.Create(8, id, 1234), false, avatar: new(0, 0, new byte[30], 17, 29, 0))
        with { AppearanceItemId = appearance };

    [Fact]
    public async Task Copy_flattens_appearances_keeps_identity_and_dyes_and_empty_slot_resets_to_transparent()
    {
        var rule = new AvatarAppearanceRuleService(new Definitions());
        var clone = Avatar(42609) with { ItemUid = new(123), Attribute = 21 };
        var ordinary = await rule.PrepareAsync(Avatar(40735, 999), null, default);
        Assert.Equal(40735, ordinary.AppearanceItemId);
        var copy = await rule.PrepareAsync(clone, ordinary, default);
        Assert.Equal(40735, copy.AppearanceItemId); Assert.Equal(clone.ItemUid, copy.ItemUid);
        Assert.Equal(clone.ItemId, copy.ItemId); Assert.Equal(clone.InstanceValue, copy.InstanceValue);
        Assert.Equal(clone.Attribute, copy.Attribute); Assert.Equal(0, copy.AvatarDetail!.ClearAvatarId);
        Assert.Equal(17, copy.AvatarDetail.Color1); Assert.Equal(29, copy.AvatarDetail.Color2);
        var chained = await rule.PrepareAsync(clone, copy, default);
        Assert.Equal(40735, chained.AppearanceItemId);
        var empty = await rule.PrepareAsync(chained, null, default);
        Assert.Equal(0, empty.AppearanceItemId); Assert.Equal(0, empty.AvatarDetail!.Color1);
        Assert.Equal(0, (await rule.PrepareAsync(clone, empty, default)).AppearanceItemId);
        Assert.Equal(40735, copy.AppearanceItemId); // independent immutable snapshot
    }

    [Theory]
    [InlineData(null, 456)]
    [InlineData(0, 42609)]
    [InlineData(40735, 40735)]
    public void Drawn_avatar_projects_template_link_and_colour_without_invalid_item_zero(int? appearance, int display)
    {
        var avatar = Avatar(42609, appearance) with { AvatarDetail = new(0, 456, new byte[30], 17, 29, 0) };
        var part = Assert.Single(DrawnAppearance.Of([new(null, 3, avatar.ToCore(), avatar.AvatarDetail)]));
        Assert.Equal(display, part.DisplayItemId);
        Assert.Equal(display == 42609 ? 0 : 42609, part.LinkItemId);
        Assert.Equal(new byte[] { 17, 0, 29, 0 }, part.ExpansionData);
    }

    private static ShopFixture Fixture()
    {
        var f = new ShopFixture(s => s.AddSingleton<IItemPlacementDefinitionSource>(sp =>
            new PvfItemPlacementDefinitionSource(sp.GetRequiredService<PvfArchiveProvider>())));
        f.Sql("UPDATE characters SET job=0"); f.SetLevel(80);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(f.Session.CharacterId,
            new PersistentItemContainer(ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Avatar),
                new(InventoryContainerType.Avatar, 0), f.Services.GetRequiredService<IItemDomainStore>(),
                f.Services.GetRequiredService<IDomainTransactionAccessor>(), f.Services.GetRequiredService<IItemPlacementRuleService>(),
                f.Services.GetRequiredService<IItemSlotRuleService>())));
        return f;
    }
    private static Task<ItemMoveResult> Move(ShopFixture f, BusinessSlot source, BusinessSlot destination) =>
        InventoryMoveTestAccess.MoveAsync(f.Services, new InventoryMoveRequest(f.Session.CharacterId,
            source, 1, destination) { Session = f.Session }).AsTask();
    private static int Appearance(ShopFixture f, int slot, int list = 3) => f.Scalar(
        $"SELECT appearance_item_id FROM items JOIN item_slots ON item_uid=core_uid WHERE owner_id=1 AND list_type={list} AND slot_index={slot}");

    [SkippableFact]
    public async Task Failure_while_binding_appearance_rolls_back_item_values_and_both_slots()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH unavailable.");
        using var f = Fixture();
        await f.SeedCore(3, StackableItemCore.Create(8, 40735, 987), InventoryContainerType.Equipment);
        await f.SeedCore(0, StackableItemCore.Create(8, 42609, 123), InventoryContainerType.Avatar);
        var before = f.StoredCore(0, InventoryContainerType.Avatar);
        f.Sql("CREATE TRIGGER fail_avatar_binding AFTER UPDATE OF appearance_item_id ON items BEGIN SELECT RAISE(ABORT, 'simulated appearance failure'); END");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => Move(f,
            new(BusinessInventorySpace.Avatar, 0), new(BusinessInventorySpace.WornAvatar, 3)));
        Assert.Equal(before, f.StoredCore(0, InventoryContainerType.Avatar));
        Assert.Equal(40735, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(f.StoredCore(3, InventoryContainerType.Equipment).AsSpan(1)));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM items WHERE appearance_item_id IS NOT NULL"));
    }

    [SkippableFact]
    public async Task Real_PVF_wear_and_reverse_swap_copy_the_previous_appearance_and_empty_equip_persists_zero()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH unavailable.");
        using var f = Fixture();
        await f.SeedCore(3, StackableItemCore.Create(8, 40735, 987), InventoryContainerType.Equipment);
        await f.SeedCore(0, StackableItemCore.Create(8, 42609, 123), InventoryContainerType.Avatar);
        Assert.Equal(ItemMoveStatus.Moved, (await Move(f, new(BusinessInventorySpace.Avatar, 0), new(BusinessInventorySpace.WornAvatar, 3))).Status);
        Assert.Equal(40735, Appearance(f, 3));
        Assert.Equal(0, f.Scalar("SELECT SUM(avatar_clear_id) FROM items"));
        await f.SeedCore(1, StackableItemCore.Create(8, 42609, 234), InventoryContainerType.Avatar);
        Assert.Equal(ItemMoveStatus.Moved, (await Move(f, new(BusinessInventorySpace.WornAvatar, 3), new(BusinessInventorySpace.Avatar, 1))).Status);
        Assert.Equal(40735, Appearance(f, 3)); Assert.Equal(40735, Appearance(f, 1, 1));
        // Remove the source costume: only the copied value remains necessary.
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var scope = (DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var tx = scope.Begin())
            {
                var session = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(scope.Current!);
                var avatarId = ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Avatar);
                await session.DeleteItemAsync(await session.TakeAsync((await session.GetAsync(avatarId, 0, default))!, null, default), default);
                Assert.Equal(40735, (await session.GetAsync(ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Equipment), 3, default))!.Snapshot.AppearanceItemId);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(ItemMoveStatus.Moved, (await Move(f, new(BusinessInventorySpace.WornAvatar, 3), new(BusinessInventorySpace.Avatar, 2))).Status);
        Assert.Equal(ItemMoveStatus.Moved, (await Move(f, new(BusinessInventorySpace.Avatar, 2), new(BusinessInventorySpace.WornAvatar, 3))).Status);
        Assert.Equal(0, Appearance(f, 3));
    }
}
