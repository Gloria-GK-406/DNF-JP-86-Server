using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.ItemSockets;
using Microsoft.Data.Sqlite;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentSocketTests
{
    [Theory]
    [InlineData(0x037A, "0900B80B00004100")]
    [InlineData(0x00CE, "0000B80B00004100")]
    [InlineData(0x0379, "0900B80B00000142000208000000")]
    [InlineData(0x00C9, "010000B80B00000142000208000000")]
    public void A21_socket_requests_are_registered_and_decode_the_actual_branch_layout(int type, string hex)
    {
        using var f = new ShopFixture();
        var registration = Assert.Single(f.Services.GetServices<GameMessageDecoderRegistration>(), x => x.Selector == 1 && x.Type == type);
        var decoder = (IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType);
        var request = Assert.IsType<ItemSocketRequest>(decoder.Decode(new(1, checked((ushort)type), 0, 1), Convert.FromHexString(hex)));
        Assert.True(request.Valid); Assert.Equal(3000, request.ExpectedItemId);
        Assert.Equal(type is 0xC9 or 0xCE ? 0 : 9, request.TargetSlot);
        if (!request.Open) { var emblem = Assert.Single(request.Emblems); Assert.Equal(66, emblem.MaterialSlot); Assert.Equal(2050, emblem.ItemId); Assert.Equal(0, emblem.SocketIndex); }
        else Assert.Equal(65, request.DrillSlot);
    }

    private sealed class Content : IItemSocketDefinitionSource, IItemPlacementDefinitionSource
    {
        public long DrillExpiresAt { get; set; }
        public ImmutableArray<ushort> EquipmentTypes { get; set; } = [4, 4];
        public ImmutableArray<ushort> AvatarTypes { get; set; } = [16, 4, 4];
        public ImmutableArray<ushort> AvatarDefaults { get; set; } = [];
        public ValueTask<ItemSocketDefinition?> TargetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<ItemSocketDefinition?>(id switch { 3000 => new(false, EquipmentTypes, []), 3001 => new(true, AvatarTypes, AvatarDefaults), _ => null });
        public ValueTask<SocketDrillDefinition?> DrillAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<SocketDrillDefinition?>(id switch { 4000 => new(false, DrillExpiresAt), 4001 => new(true), _ => null });
        public ValueTask<ushort> EmblemTypesAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<ushort>(id switch { 5000 or 5001 => 4, 5002 => 1, 5003 => 16, _ => 0 });
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) => id switch
        {
            3001 => ValueTask.FromResult<ItemPlacement?>(new(8, 1, new(BusinessInventorySpace.Avatar, 0, 209)) { WorldClass = ItemWorldClass.Avatar }),
            >= 5000 and <= 5004 => ValueTask.FromResult<ItemPlacement?>(new(9, 1000, new(BusinessInventorySpace.AvatarEmblem, 0, 62))),
            _ => new NpcShopTestContent().GetAsync(id, ct),
        };
    }
    private static ShopFixture Fixture(Content? content = null)
    {
        content ??= new();
        var f = new ShopFixture(s => { s.AddSingleton<IItemSocketDefinitionSource>(content); s.AddSingleton<IItemPlacementDefinitionSource>(content); });
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(f.Session.CharacterId,
            new PersistentItemContainer(ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Avatar), new(InventoryContainerType.Avatar, 0),
                f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
        return f;
    }
    private static InventoryItemCoreState Equipment(byte sockets = 0) => StackableItemCore.Create(1, 3000, 123456) with
    { Attribute = 7, Durability = 45, EnchantCardId = 3600, EmblemSocketCount = sockets, EmblemId1 = sockets > 0 ? -1 : 0, EmblemId2 = sockets == 2 ? -1 : 0 };
    private static async Task Seed(ShopFixture f, byte sockets = 0, int drillCount = 2, int emblemCount = 3)
    { await f.SeedCore(9, Equipment(sockets)); await f.Seed(65, 4000, drillCount); await f.Seed(289, 5000, emblemCount, 9); }
    private static ItemSocketInput Open(bool avatar = false) => new(avatar, true,
        new(avatar ? BusinessInventorySpace.Avatar : BusinessInventorySpace.CarriedEquipment, 0), avatar ? 3001 : 3000,
        new(BusinessInventorySpace.Consumable, 0), []);
    private static ItemSocketInput Embed(params EmblemPlacement[] emblems) => new(false, false,
        new(BusinessInventorySpace.CarriedEquipment, 0), 3000, null, emblems.ToImmutableArray());
    private static EmblemPlacement Green(byte index, int id = 5000, int slot = 0) => new(new(BusinessInventorySpace.AvatarEmblem, slot), id, index);
    private static Task<ItemSocketResult> Apply(ShopFixture f, ItemSocketInput input) =>
        f.Services.GetRequiredService<ICommandBus>().SendAsync(new ItemSocketCommand(f.OnlineUser, f.Session, input)).AsTask();
    private static async Task<ItemSnapshot> Read(ShopFixture f, InventoryContainerType list, int slot)
    {
        return await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = ((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            return (await f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Services.GetRequiredService<IDomainTransactionAccessor>().Current!).GetAsync(ContainerId.ForCharacter(f.Session.CharacterId, list), slot, default))!.Snapshot;
        });
    }
    private static ValueTask SendOpen(ShopFixture f, ushort sequence = 7) => EffectRequestTransport.Send(f,
        new ItemSocketRequest(new(1, 0x037A, 0, sequence), InventoryContainerType.Main, 9, 3000, 65, []));
    private static ValueTask SendEmbed(ShopFixture f, ushort sequence = 7) => EffectRequestTransport.Send(f,
        new ItemSocketRequest(new(1, 0x0379, 0, sequence), InventoryContainerType.Main, 9, 3000, 0, [new(289, 5000, 0), new(289, 5000, 1)]));
    private static byte[][] Frames(ShopFixture f) => f.Sender.Messages.SelectMany(m =>
    {
        var registration = f.Services.GetServices<ServerMessageEncoderRegistration>().Single(x => x.MessageType == m.GetType());
        return ((IServerMessageEncoder)f.Services.GetRequiredService(registration.EncoderType)).Encode(m);
    }).ToArray();

    [Fact]
    public async Task Opening_keeps_uid_and_unrelated_fields_and_consumes_exactly_one_drill()
    {
        using var f = Fixture(); await Seed(f); var before = await Read(f, InventoryContainerType.Main, 9);
        Assert.NotNull((await Apply(f, Open())).Receipt);
        var after = await Read(f, InventoryContainerType.Main, 9);
        Assert.Equal(before.ItemUid, after.ItemUid); Assert.Equal((byte)2, after.EmblemSocketCount);
        Assert.Equal(-1, after.EmblemId1); Assert.Equal(-1, after.EmblemId2); Assert.Equal(1, f.Count(4000));
        Assert.Equal(before, after with { EmblemSocketCount = 0, EmblemId1 = 0, EmblemId2 = 0 });
        Assert.Equal(ItemSocketRejection.AlreadyOpen, (await Apply(f, Open())).Rejection); Assert.Equal(1, f.Count(4000));
    }
    [Fact]
    public async Task Two_sockets_can_consume_one_shared_pile_and_replacement_does_not_refund_previous_emblem()
    {
        using var f = Fixture(); await Seed(f, 2);
        Assert.NotNull((await Apply(f, Embed(Green(0), Green(1)))).Receipt);
        Assert.Equal(1, f.Count(5000)); var target = await Read(f, InventoryContainerType.Main, 9);
        Assert.Equal(5000, target.EmblemId1); Assert.Equal(5000, target.EmblemId2);
        await f.Seed(290, 5001, 1, 9);
        Assert.NotNull((await Apply(f, Embed(Green(0, 5001, 1)))).Receipt);
        target = await Read(f, InventoryContainerType.Main, 9);
        Assert.Equal(5001, target.EmblemId1); Assert.Equal(5000, target.EmblemId2);
        Assert.Equal(0, f.Count(5001)); Assert.Equal(1, f.Count(5000));
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("locked")]
    [InlineData("wrongid")]
    [InlineData("duplicate")]
    [InlineData("colour")]
    [InlineData("unknown")]
    [InlineData("unopened")]
    [InlineData("expired")]
    [InlineData("index")]
    public async Task Invalid_batch_is_rejected_without_any_material_or_target_change(string reason)
    {
        using var f = Fixture(); await Seed(f, reason == "unopened" ? (byte)0 : (byte)2, emblemCount: reason == "missing" ? 1 : 3);
        if (reason == "locked") await f.SeedCore(9, Equipment(2) with { EquipmentLockId = 1 });
        if (reason == "expired") await f.SeedCore(9, Equipment(2) with { ExpireTime = checked((int)f.Clock.GetUtcNow().AddSeconds(-1).ToUnixTimeSeconds()) });
        if (reason is "colour" or "unknown") await f.Seed(290, reason == "colour" ? 5002 : 5004, 1, 9);
        var input = reason switch
        {
            "wrongid" => Embed(Green(0, 5001)), "duplicate" => Embed(Green(0), Green(0)),
            "colour" => Embed(Green(0), Green(1, 5002, 1)), "unknown" => Embed(Green(0), Green(1, 5004, 1)),
            "index" => Embed(Green(2)), _ => Embed(Green(0), Green(1)),
        };
        var before = f.StoredCore(9); var count = f.Count(5000);
        Assert.Null((await Apply(f, input)).Receipt); Assert.Equal(before, f.StoredCore(9)); Assert.Equal(count, f.Count(5000));
        Assert.Empty(f.Sender.Messages);
    }
    [Fact]
    public async Task A_single_socket_rejects_two_aliases_of_the_same_socket_without_spending()
    {
        using var f = Fixture(new() { EquipmentTypes = [16] }); await Seed(f, 1); await f.Seed(289, 5003, 2, 9);
        Assert.Equal(ItemSocketRejection.InvalidRequest, (await Apply(f, Embed(Green(0, 5003), Green(1, 5003)))).Rejection);
        Assert.Equal(2, f.Count(5003)); Assert.Equal(-1, (await Read(f, InventoryContainerType.Main, 9)).EmblemId1);
        Assert.NotNull((await Apply(f, Embed(Green(1, 5003)))).Receipt);
    }
    [Fact]
    public async Task Persistence_failure_rolls_back_both_target_and_materials()
    {
        using var f = Fixture(); await Seed(f, 2); var before = f.StoredCore(9);
        f.Sql("CREATE TRIGGER socket_failure BEFORE UPDATE ON items BEGIN SELECT RAISE(ABORT,'socket-failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Apply(f, Embed(Green(0), Green(1))));
        Assert.Equal(before, f.StoredCore(9)); Assert.Equal(3, f.Count(5000));
    }
    [Fact]
    public async Task Database_commit_failure_does_not_leave_open_sockets_or_spent_drill()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(); await Seed(f); var before = f.StoredCore(9);
            f.Sql("CREATE TABLE socket_commit_probe(account_id INTEGER REFERENCES accounts(account_id) DEFERRABLE INITIALLY DEFERRED)");
            await using (var tx = ((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                Assert.NotNull((await f.Services.GetRequiredService<IItemSocketDomainService>().ApplyAsync(f.Session, Open(), default)).Receipt);
                await f.Services.GetRequiredService<SqliteDatabase>().WriteAsync(async (cmd, ct) => { cmd.CommandText = "INSERT INTO socket_commit_probe VALUES(99999)"; await cmd.ExecuteNonQueryAsync(ct); }, default);
                await Assert.ThrowsAsync<SqliteException>(async () => await tx.CommitAsync(default));
            }
            Assert.Equal(before, f.StoredCore(9)); Assert.Equal(2, f.Count(4000));
        });
    }
    [Fact]
    public async Task Open_ack_is_five_bytes_before_absolute_material_and_target_updates_and_replay_does_not_decrement_twice()
    {
        using var f = Fixture(); await Seed(f); f.Sender.BeforeSend = () => Assert.Equal(1, f.Count(4000));
        await SendOpen(f); var frames = Frames(f); Assert.Equal(3, frames.Length);
        Assert.Equal(new byte[] { 1, 9, 0, 65, 0 }, frames[0][15..]); Assert.Equal((ushort)0x037A, BitConverter.ToUInt16(frames[0], 1));
        Assert.Equal((ushort)65, BitConverter.ToUInt16(frames[1], 18)); Assert.Equal((ushort)9, BitConverter.ToUInt16(frames[2], 18));
        await SendOpen(f); Assert.Equal(1, f.Count(4000)); Assert.Single(f.Sender.Messages.OfType<ItemSocketResponse>());
    }
    [Fact]
    public async Task Embed_ack_reports_each_material_pile_once_with_its_absolute_remaining_count()
    {
        using var f = Fixture(); await Seed(f, 2); await SendEmbed(f); var frames = Frames(f);
        Assert.Equal(new byte[] { 1, 1, 0x21, 1, 1, 0, 0, 0 }, frames[0][15..]);
        Assert.Equal((ushort)0x0379, BitConverter.ToUInt16(frames[0], 1));
        Assert.Equal(1, f.Count(5000)); Assert.Equal(3, frames.Length);
        await SendEmbed(f); Assert.Equal(1, f.Count(5000)); Assert.Single(f.Sender.Messages.OfType<ItemSocketResponse>());
    }
    [Fact]
    public async Task Refusal_returns_a_two_byte_error_only_and_stale_session_cannot_mutate()
    {
        using var f = Fixture(); await Seed(f, drillCount: 0); await SendOpen(f);
        Assert.Equal(new byte[] { 0, 0x11 }, Assert.Single(Frames(f))[15..]);
        await f.Seed(65, 4000, 2); var old = f.Session; f.Select(2);
        await Assert.ThrowsAnyAsync<Exception>(async () => await f.Services.GetRequiredService<ICommandBus>().SendAsync(new ItemSocketCommand(f.OnlineUser, old, Open())));
        Assert.Equal(2, f.Scalar("SELECT SUM(count) FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=65"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Avatar_keeps_identity_dyes_and_appearance_and_updates_all_three_socket_records(bool worn)
    {
        using var f = Fixture(); var list = worn ? InventoryContainerType.Equipment : InventoryContainerType.Avatar;
        await f.SeedCore(0, StackableItemCore.Create(8, 3001, 98765), list); await f.Seed(65, 4001, 2);
        await f.Seed(289, 5003, 1, 9); await f.Seed(290, 5000, 2, 9);
        var slot = new BusinessSlot(worn ? BusinessInventorySpace.WornAvatar : BusinessInventorySpace.Avatar, 0);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = ((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                var bound = f.Services.GetRequiredService<IBusinessInventorySlotDomainService>().Find(f.Session.CharacterId, slot)!;
                await bound.Container.UpdateAsync((await bound.Container.GetAsync(0, default))!, x => x with { AppearanceItemId = 123, AvatarDetail = x.AvatarDetail! with { Color1 = 12, Color2 = 34 } }, default);
                await tx.CommitAsync(default);
            }
        });
        var before = await Read(f, list, 0);
        Assert.NotNull((await Apply(f, Open(true) with { Target = slot })).Receipt);
        Assert.NotNull((await Apply(f, new(true, false, slot, 3001, null, [Green(0, 5003), Green(1, 5000, 1), Green(2, 5000, 1)]))).Receipt);
        var after = await Read(f, list, 0); Assert.Equal(before.ItemUid, after.ItemUid); Assert.Equal(123, after.AppearanceItemId);
        Assert.Equal(12, after.AvatarDetail!.Color1); Assert.Equal(34, after.AvatarDetail.Color2); Assert.Equal(98765, after.InstanceValue);
        Assert.Equal(new byte[] { 16, 0, 0x8B, 0x13, 0, 0, 4, 0, 0x88, 0x13, 0, 0, 4, 0, 0x88, 0x13, 0, 0 }, after.AvatarDetail.JewelSocket[..18]);
        Assert.Equal(1, f.Count(4001)); Assert.Equal(0, f.Count(5000)); Assert.Equal(0, f.Count(5003));
    }
    [Fact]
    public async Task Avatar_layout_larger_than_A21_projection_is_rejected_without_losing_materials()
    {
        using var f = Fixture(new() { AvatarTypes = [1, 2, 4, 8] });
        await f.SeedCore(0, StackableItemCore.Create(8, 3001, 123), InventoryContainerType.Avatar); await f.Seed(65, 4001, 1);
        Assert.Equal(ItemSocketRejection.UnsupportedContent, (await Apply(f, Open(true))).Rejection); Assert.Equal(1, f.Count(4001));
        Assert.Equal(new byte[30], (await Read(f, InventoryContainerType.Avatar, 0)).AvatarDetail!.JewelSocket);
    }
    [SkippableFact]
    public async Task Real_pvf_defines_eligible_parts_drills_and_colours_without_opening_weapons()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH unavailable.");
        using var f = new ShopFixture(); var source = f.Services.GetRequiredService<IItemSocketDefinitionSource>();
        Assert.Equal(new ushort[] { 4, 4 }, (await source.TargetAsync(10018, default))!.OpenTypes);
        Assert.Null(await source.TargetAsync(27001, default));
        Assert.False((await source.DrillAsync(2749124, default))!.Avatar); Assert.True((await source.DrillAsync(7421, default))!.Avatar);
        Assert.Null(await source.DrillAsync(15, default)); Assert.Equal(4, await source.EmblemTypesAsync(2052, default));
        Assert.Equal(new ushort[] { 16, 4, 4 }, (await source.TargetAsync(42609, default))!.OpenTypes);
    }
    [Theory]
    [InlineData(0x037A, "0900B80B0000410000")]
    [InlineData(0x0379, "0900B80B00000242000208000000")]
    [InlineData(0x0379, "000900B80B00000142000208000000")]
    [InlineData(0x00C9, "010000B80B000000")]
    public void Truncated_overlong_or_wrong_branch_layout_is_refused(int type, string hex)
    {
        using var f = Fixture(); var reg = f.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Type == type && x.Selector == 1);
        Assert.False(Assert.IsType<ItemSocketRequest>(((IGameMessageDecoder)f.Services.GetRequiredService(reg.DecoderType)).Decode(new(1, checked((ushort)type), 0, 1), Convert.FromHexString(hex))).Valid);
    }

    [SkippableTheory]
    [InlineData(10018, 2749124, false, 0)]
    [InlineData(40735, 7421, true, 0)]
    [InlineData(42609, 7421, true, 1)]
    public async Task Real_pvf_open_and_embed_use_the_production_transport_and_persist_uid(int targetId, int drillId, bool avatar, byte firstSocket)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH unavailable.");
        using var f = new ShopFixture(s => s.AddSingleton<IItemPlacementDefinitionSource>(sp =>
            new PvfItemPlacementDefinitionSource(sp.GetRequiredService<PvfArchiveProvider>())));
        if (avatar) f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(f.Session.CharacterId,
            new PersistentItemContainer(ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Avatar), new(InventoryContainerType.Avatar, 0),
                f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
        var list = avatar ? InventoryContainerType.Avatar : InventoryContainerType.Main;
        ushort slot = avatar ? (ushort)0 : (ushort)9;
        await f.SeedCore(slot, StackableItemCore.Create(avatar ? (byte)8 : (byte)1, targetId, 123456), list);
        await f.Seed(65, drillId, 2); await f.Seed(289, 2052, 3, 9);
        var before = await Read(f, list, slot);
        await EffectRequestTransport.Send(f, new ItemSocketRequest(new(1, avatar ? (ushort)0xCE : (ushort)0x37A, 0, 31), list, slot, targetId, 65, []));
        Assert.True(Assert.Single(f.Sender.Messages.OfType<ItemSocketResponse>()).Success);
        Assert.Equal(1, f.Count(drillId)); f.Sender.Messages.Clear();
        await EffectRequestTransport.Send(f, new ItemSocketRequest(new(1, avatar ? (ushort)0xC9 : (ushort)0x379, 0, 32), list, slot, targetId, 0,
            [new(289, 2052, firstSocket), new(289, 2052, checked((byte)(firstSocket + 1)))]));
        Assert.True(Assert.Single(f.Sender.Messages.OfType<ItemSocketResponse>()).Success);
        Assert.Equal(1, f.Count(2052)); var after = await Read(f, list, slot);
        Assert.Equal(before.ItemUid, after.ItemUid);
        if (avatar) Assert.Equal(2052, BinaryPrimitives.ReadInt32LittleEndian(after.AvatarDetail!.JewelSocket.AsSpan(firstSocket * 6 + 2)));
        else { Assert.Equal(2052, after.EmblemId1); Assert.Equal(2052, after.EmblemId2); }
    }
    [Fact]
    public async Task Expired_drill_declaration_is_refused_without_mutation()
    {
        using var f = Fixture(new Content { DrillExpiresAt = 1 }); await Seed(f);
        Assert.Equal(ItemSocketRejection.MissingMaterial, (await Apply(f, Open())).Rejection);
        Assert.Equal(2, f.Count(4000)); Assert.Equal(0, (await Read(f, InventoryContainerType.Main, 9)).EmblemSocketCount);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Optional_body_checksum_is_validated(bool corrupt)
    {
        using var f = Fixture(); var bytes = Convert.FromHexString("0900B80B00004100");
        var body = new byte[12]; bytes.CopyTo(body, 0); var hash = System.Security.Cryptography.MD5.HashData(bytes);
        body[8] = (byte)(hash[0] ^ hash[13] ^ hash[8] ^ hash[10] ^ 0x81);
        body[9] = (byte)(hash[1] ^ hash[9] ^ hash[5] ^ hash[12] ^ 0x78);
        body[10] = (byte)(hash[2] ^ hash[6] ^ hash[14] ^ hash[4] ^ 0x1a);
        body[11] = (byte)(hash[3] ^ hash[15] ^ hash[11] ^ hash[7] ^ 0xbf);
        if (corrupt) body[11] ^= 1;
        var reg = f.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Type == 0x37A && x.Selector == 1);
        var result = Assert.IsType<ItemSocketRequest>(((IGameMessageDecoder)f.Services.GetRequiredService(reg.DecoderType)).Decode(new(1, 0x37A, 0, 5), body));
        Assert.Equal(!corrupt, result.Valid);
    }
}
