using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DisjointItem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.DisjointItem;

public sealed class DisjointItemCodecTests
{
    [Fact]
    public void System_disjoint_is_the_nine_byte_game_command()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x001A);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);

        var request = Assert.IsType<DisjointItemRequest>(decoder.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("090000FFFF00000000")));

        Assert.Equal(ClientMessageType.GameDisjointItem, request.MessageType);
        Assert.Equal(((short)9, InventoryContainerType.Main, (short)-1, 0),
            ((short)request.TargetSlot.Index, request.TargetSlot.Space, request.DisjointToolSlotIndex,
                request.DisjointProviderId));
    }

    [Fact]
    public void Success_ack_names_the_source_and_every_material_landing()
    {
        Assert.Equal(Convert.FromHexString(
                "01090000027900DD0B00000C0000007A005E0C000002000000"),
            DisjointItemResponseEncoder.EncodeBody(new(
                new GameSlot(InventoryContainerType.Main, 9),
                [
                    new(new GameSlot(InventoryContainerType.Main, 121), 3037, 12),
                    new(new GameSlot(InventoryContainerType.Main, 122), 3166, 2),
                ])));
        Assert.Equal(new byte[] { 0, 0x13 },
            DisjointItemResponseEncoder.EncodeBody(new(null, ErrorCode: 0x13)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(10)]
    public void Any_body_length_other_than_nine_is_rejected(int length) =>
        Assert.Throws<InvalidDataException>(() => new DisjointItemMessageDecoder()
            .Decode(GameMessageMetadata.Empty, new byte[length]));
}
