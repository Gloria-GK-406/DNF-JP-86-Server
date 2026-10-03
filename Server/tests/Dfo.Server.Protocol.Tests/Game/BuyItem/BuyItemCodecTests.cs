using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.BuyItem;
using Dfo.Server.Protocol.Game.ShopPurchaseCount;
using Dfo.Server.Protocol.Game.StateSynchronization;
using System.Buffers.Binary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.BuyItem;

public sealed class BuyItemCodecTests
{
    [Fact]
    public async Task Purchase_projects_changed_quest_progress_after_the_inventory_update()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("purchase-quest"), 1, 10011);
        var sessions = new SelectedSessionDomainService();
        sessions.Select(session);
        var receipt = new NpcShopPurchaseReceipt(session, 2000, 1, 100, 0, 0, false, [],
            [new(new(BusinessInventorySpace.QuickSlot, 0), ItemSnapshot.FromCore(StackableItemCore.Create(2, 2000, 1), true), 1)], DateTimeOffset.UnixEpoch);
        var messages = await new NpcShopPurchaseCompletedHandler(sessions).ComposeAsync(
            new NotificationRecipient(session.CharacterId, session.ConnectionId),
            new NpcShopPurchaseCompleted(receipt,
                new QuestProgressSnapshot([new CarriedQuest(0, 2001, 0)])),
            CancellationToken.None);

        Assert.IsType<BuyItemResponse>(messages[0]);
        var progress = Assert.IsType<AcceptedQuestProgressMessage>(messages[^1]);
        var frame = Assert.Single(
            new SupplementalNotiSynchronizationEncoder<AcceptedQuestProgressMessage>().Encode(progress));
        Assert.Equal(0, frame[0]);
        Assert.Equal(0x023F, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(15, 4)));
        Assert.Equal((ushort)2001, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(19, 2)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(21, 4)));
    }

    [Theory]
    [InlineData("4CDC341D010000000F00000002000000")]
    [InlineData("B06A000000000000570000002E010000")]
    public void The_live_gold_and_material_requests_reach_a_decoder(string bodyHex)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0015);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);

        var request = decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString(bodyHex));

        var decoded = Assert.IsType<BuyItemRequest>(request);
        Assert.Equal(bodyHex.StartsWith("4C", StringComparison.Ordinal) ? (490003532, 1, 15, 2) : (27312, 0, 87, 302),
            (decoded.ItemId, decoded.RawCount, decoded.ShopId, decoded.NpcId));
        Assert.Equal((ClientMessageType)0x0001_0015, request.MessageType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void Buy_requires_exactly_four_int32_fields(int length) =>
        Assert.Throws<InvalidDataException>(() => new BuyItemMessageDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void Count_query_requires_exactly_one_int32_field(int length) =>
        Assert.Throws<InvalidDataException>(() => new ShopPurchaseCountDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

    [Fact]
    public void A21_ack_reads_four_wallet_fields_then_101_bytes_then_counted_product_pairs()
    {
        // Reader evidence: RVA D357DE..D3581E (4*i32, 0x65 bytes), D359AA..D359E8 (u8, pairs).
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("codec"), 1, 10011);
        var final = ItemSnapshot.FromCore(StackableItemCore.Create(2, 5000, 10), true);
        var receipt = new NpcShopPurchaseReceipt(session, 5000, 3, 99700, 321, 77, true, [],
            [new(new(BusinessInventorySpace.QuickSlot, 0), final, 1),
                new(new(BusinessInventorySpace.QuickSlot, 1), final with { Count = 2 }, 2)], DateTimeOffset.UnixEpoch);
        var body = BuyItemResponseEncoder.EncodeBody(new(receipt));
        Assert.Equal(127, body.Length);
        Assert.Equal(1, body[0]);
        Assert.Equal(new[] { 99700, 321, 0, 77 }, Enumerable.Range(0, 4)
            .Select(index => BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1 + index * 4))));
        Assert.Equal(3, BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(17)));
        Assert.Equal(5000, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(19)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(23))); // Added, not final pile size.
        Assert.Equal(1, body[118]);
        Assert.Equal(5000, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(119)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(123)));
        Assert.Equal(new byte[] { 0, 0x85 }, BuyItemResponseEncoder.EncodeBody(new(null, 0x85)));
    }

    [Fact]
    public void Count_response_includes_both_list_lengths_even_when_empty()
    {
        var encoder = new ShopPurchaseCountEncoder();
        Assert.Equal(Convert.FromHexString("010000000000000000"),
            Assert.Single(encoder.Encode(new ShopPurchaseCountResponse(new(NpcShopRejection.None, []))))[15..]);
        Assert.Equal(Convert.FromHexString("01010000004EDC341D0100000000000000"),
            Assert.Single(encoder.Encode(new ShopPurchaseCountResponse(new(NpcShopRejection.None, [new(490003534, 1)]))))[15..]);
    }
}
