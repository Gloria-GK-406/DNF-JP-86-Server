using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.SellItem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.SellItem;

public sealed class SellItemCodecTests
{
    [Theory]
    [InlineData("0009000100020000000A000000", 9, 10)]
    [InlineData("000A000100020000000B000000", 10, 11)]
    public void A21_sell_request_is_dispatched_instead_of_discarded(string body, short slot, int sum)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0016);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        // RVA 1FEE7C4..1FEE814: list, slot, count, NPC, list + slot + count.
        // Slot 10 is the live 2026-09-09 request (seq 30); slot 9 is an instruction-derived vector.
        var request = decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString(body));
        Assert.NotNull(request);
        Assert.Equal((ClientMessageType)0x0001_0016, request.MessageType);
        var sale = Assert.IsType<SellItemRequest>(request);
        Assert.Equal((InventoryContainerType.Main, slot, (short)1, 2, sum),
            (sale.Container, sale.SlotIndex, sale.Count, sale.NpcId, sale.FieldSum));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(14)]
    public void Truncated_and_extended_requests_are_not_interpreted_as_legacy_layouts(int length) =>
        Assert.Throws<InvalidDataException>(() => new SellItemMessageDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

    [Fact]
    public void Success_matches_the_four_reads_in_the_A21_handler()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("codec"), 1, 10011);
        var receipt = new ItemSaleReceipt(session, InventoryContainerType.Main, 65, 5000, 3, 100093, null, DateTimeOffset.UnixEpoch);
        // RVA D222B9..D222DF: i32 gold, u8 container, u16 slot, u16 count.
        Assert.Equal(Convert.FromHexString("01FD8601000041000300"), SellItemResponseEncoder.EncodeBody(new(receipt)));
        Assert.Equal(new byte[] { 0, 22 }, SellItemResponseEncoder.EncodeBody(new(null, 22)));
    }
}
