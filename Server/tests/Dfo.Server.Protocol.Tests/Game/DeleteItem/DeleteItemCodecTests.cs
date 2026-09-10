using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DeleteItem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.DeleteItem;

public sealed class DeleteItemCodecTests
{
    [Theory]
    [InlineData("000101004100B4B39800010000000000000000", 65, 10007476)]
    [InlineData("000101000900B06A0000010000000000000000", 9, 27312)]
    public void A21_ordinary_discard_vectors_keep_all_wire_semantics(string body, ushort slot, int itemId)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0012);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);

        var request = Assert.IsType<DeleteItemRequest>(
            decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString(body)));

        Assert.Equal(ClientMessageType.GameDeleteItem, request.MessageType);
        Assert.Equal((InventoryContainerType.Main, (byte)1, (ushort)1, slot, itemId, 1, 0u, (byte)0),
            (request.Container, request.EntryCount, request.OperationType, request.SlotIndex,
                request.ItemId, request.DeleteCount, request.ReservedZero, request.SafeModeSkipIndex));
    }

    [Fact]
    public void Security_exception_context_is_carried_without_changing_the_layout()
    {
        var body = Convert.FromHexString("000101004100B4B39800010000000000000001");
        var request = Assert.IsType<DeleteItemRequest>(
            new DeleteItemMessageDecoder().Decode(GameMessageMetadata.Empty, body));
        Assert.Equal((uint)0, request.ReservedZero);
        Assert.Equal((byte)1, request.SafeModeSkipIndex);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(20)]
    public void Alternate_or_extended_builders_are_not_misread_as_the_ordinary_layout(int length) =>
        Assert.Throws<InvalidDataException>(() =>
            new DeleteItemMessageDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

    [Fact]
    public void A21_success_and_failure_responses_match_the_client_reads()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("codec"), 1, 10011);
        var receipt = new ItemDiscardReceipt(session, InventoryContainerType.Main, 9, 27312, 1, null,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(Convert.FromHexString("0100010900010000000000"),
            DeleteItemResponseEncoder.EncodeBody(new(receipt)));
        Assert.Equal(Convert.FromHexString("001700"),
            DeleteItemResponseEncoder.EncodeBody(new(null, DeleteItemResponse.CannotDelete,
                InventoryContainerType.Main)));
    }
}
