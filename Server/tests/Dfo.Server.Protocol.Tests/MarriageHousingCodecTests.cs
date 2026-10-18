using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.BuyItem;
using Dfo.Server.Protocol.Game.MarriageHousing;

namespace Dfo.Server.Protocol.Tests;

public sealed class MarriageHousingCodecTests
{
    [Fact]
    public async Task Successful_upgrade_notifies_the_spouse_once_and_never_duplicates_the_actor_ack()
    {
        var services = new ServiceCollection();
        new Dfo.Server.Application.ApplicationModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var sessions = provider.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>();
        var actor = SelectedSessionIdentity.Create(new(1), new(71), new("ring-actor"), 1, 10011);
        var peer = SelectedSessionIdentity.Create(new(2), new(72), new("ring-peer"), 1, 10011);
        sessions.Select(actor);
        var handler = new HousingChangedHandler(sessions);
        var change = new HousingChanged(actor, new(HousingAction.UpgradeRing), new(0) { RingLevel = 1 });
        Assert.Empty(await handler.ComposeAsync(new(actor.CharacterId, actor.ConnectionId, actor), change, default));
        var message = Assert.IsType<HousingRingMessage>(Assert.Single(await handler.ComposeAsync(new(peer.CharacterId, peer.ConnectionId, peer), change, default)));
        var frame = Assert.Single(new HousingRingEncoder().Encode(message));
        Assert.Equal(0x03D1, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))); Assert.Equal(1, frame[15]);
    }

    [Fact]
    public void Requests_decode_only_business_fields_after_native_opaque_prefix()
    {
        var body = Enumerable.Repeat((byte)0xA5, 28).ToArray();
        body[14] = 1; BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(15), 3); BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(17), 10002688);
        body[21] = 0; BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(22), 6); BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(24), 10002688);
        var move = Assert.IsType<HousingNetworkRequest>(new HousingMoveDecoder().Decode(GameMessageMetadata.Empty, body));
        Assert.Equal(new HousingEndpoint(1, 3, 10002688), move.Request.Source); Assert.Equal(new HousingEndpoint(0, 6, 10002688), move.Request.Destination);
        var plant = Enumerable.Repeat((byte)0xFF, 20).ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(plant.AsSpan(14), 65); BinaryPrimitives.WriteInt32LittleEndian(plant.AsSpan(16), 10002766);
        Assert.Equal(65, Assert.IsType<HousingNetworkRequest>(new HousingPlantDecoder().Decode(GameMessageMetadata.Empty, plant)).Request.Slot);
        var open = Enumerable.Repeat((byte)0x7B, 15).ToArray(); open[14] = 0;
        Assert.Equal(HousingAction.Storage, Assert.IsType<HousingNetworkRequest>(new HousingStorageDecoder().Decode(GameMessageMetadata.Empty, open)).Request.Action);
        Assert.Throws<InvalidDataException>(() => new HousingPlantDecoder().Decode(GameMessageMetadata.Empty, plant[..^1]));
        open[14] = 2; Assert.Throws<InvalidDataException>(() => new HousingStorageDecoder().Decode(GameMessageMetadata.Empty, open));
        new ReplayProjectionRequest(move.MinimumReplayNeeds, []).Validate();
    }
    [Theory]
    [InlineData(0x03FD, 0, 6)] [InlineData(0x03FD, -4, 6)]
    [InlineData(0x03FE, -20, 5)] [InlineData(0x03FF, 0, 1)]
    [InlineData(0x0400, 0, 5)] [InlineData(0x0401, -6, 5)]
    [InlineData(0x0402, -7, 5)] [InlineData(0x0403, 0, 5)]
    [InlineData(0x03F4, 0, 6)] [InlineData(0x03F4, -7, 6)]
    public void Native_acknowledgements_have_exact_cmd_width(int opcode, int status, int length)
    {
        var frame = Assert.Single(new HousingAckEncoder().Encode(new HousingAck((ushort)opcode, status, 1)));
        Assert.Equal(1, frame[0]); Assert.Equal(opcode, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))); Assert.Equal(length, frame.Length - 15);
        Assert.Equal(1, frame[15]); // Common receiver accepts and advances its cursor by one.
        if (length >= 5) Assert.Equal(status, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16)));
        if (opcode is 0x03FD or 0x03F4) Assert.Equal(1, frame[20]);
    }
    [Fact]
    public void Nonempty_room_storage_and_tree_keep_native_record_width_and_order()
    {
        var tree = new HousingTree(Guid.NewGuid(), 2, DateTimeOffset.UnixEpoch, DateTimeOffset.FromUnixTimeSeconds(2000000000), 20, 0, 0, 2, 1);
        var room = new HousingSnapshot(Guid.NewGuid(), 1, [new(3, 10002688, 4)], [new(6, 10002688)], tree);
        var b = Assert.Single(new HousingRoomEncoder().Encode(new HousingRoomMessage(room)))[15..]; Assert.Equal(23, b.Length);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(b)); Assert.Equal(6, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4)));
        Assert.Equal(10002688, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(6))); Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(10)));
        Assert.Equal(2000000000u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(14))); Assert.Equal(20, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(18))); Assert.Equal(2, b[22]);
        var storage = Assert.Single(new HousingStorageEncoder().Encode(new HousingStorageMessage(room)))[15..]; Assert.Equal(18, storage.Length);
        Assert.Equal(3, BinaryPrimitives.ReadInt16LittleEndian(storage.AsSpan(4))); Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(storage.AsSpan(10)));
        var move = new HousingRequest(HousingAction.Move, Source: new(1, 3, 10002688), Destination: new(0, 6, 10002688));
        var delta = Assert.Single(new HousingMoveEncoder().Encode(new HousingMoveMessage(move, room)))[15..]; Assert.Equal(23, delta.Length);
        Assert.Equal(1, delta[1]); Assert.Equal(10002688, BinaryPrimitives.ReadInt32LittleEndian(delta.AsSpan(2))); Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(delta.AsSpan(6)));
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(delta.AsSpan(8))); Assert.Equal(0, delta[12]); Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(delta.AsSpan(19)));
    }
    [Fact]
    public void Furniture_purchase_ack_names_couple_storage_slot_and_never_needs_personal_inventory_grant()
    {
        var actor = SelectedSessionIdentity.Create(new(1), new(71), new("housing-purchase"), 1, 10011);
        var room = new HousingSnapshot(Guid.NewGuid(), 1, [new(3, 10002688, 4)], [], null);
        var receipt = new NpcShopPurchaseReceipt(actor, 10002688, 1, 5, 0, 0, false, [], [], DateTimeOffset.UnixEpoch)
        { Housing = room, HousingItem = new ItemSnapshot { ItemId = 10002688, Count = 1, IsPile = true, LegacyKind = 2 } };
        var bytes = BuyItemResponseEncoder.EncodeBody(new(receipt) { InventoryAlreadyProjected = true });
        Assert.Equal(119, bytes.Length); Assert.Equal(3, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(17)));
        Assert.Equal(10002688, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(19))); Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(23)));
        Assert.Empty(receipt.GrantedSlots);
    }
}
