using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.RepairItem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.RepairItem;

public sealed class RepairItemRegistrationTests
{
    [Fact]
    public void Automatic_repair_accepts_only_the_verified_seven_byte_shape()
    {
        var decoder = new RepairItemMessageDecoder();
        var body = Convert.FromHexString("00FFFFFFFF0100");
        var request = Assert.IsType<RepairItemRequest>(decoder.Decode(GameMessageMetadata.Empty, body));
        Assert.Null(request.Slot);
        Assert.Equal(1, request.AutoRepair);
        Assert.Equal(0, request.QuickRepair);
        for (var index = 0; index < body.Length; index++)
        {
            var invalid = body.ToArray();
            invalid[index] ^= 1;
            Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, invalid));
        }
    }

    [Fact]
    public void Repair_command_is_decoded_as_a_game_command()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0017);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var request = Assert.IsType<RepairItemRequest>(
            decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString("030C00FFFF000000")));
        Assert.Equal((ClientMessageType)0x0001_0017, request.MessageType);
        Assert.Equal((InventoryContainerType.Equipment, (short)12, (short)-1, (byte)0),
            (request.Slot!.Value.Space, (short)request.Slot.Value.Index,
                request.MaterialSlot, request.QuickRepair));
    }

    [Fact]
    public void Repair_all_preserves_the_request_list_for_its_success_acknowledgement()
    {
        var decoder = new RepairItemMessageDecoder();
        var request = Assert.IsType<RepairItemRequest>(decoder.Decode(
            GameMessageMetadata.Empty,
            Convert.FromHexString("02FFFFFFFF000000")));

        Assert.Null(request.Slot);
        Assert.Equal(InventoryContainerType.PersonalCargo, request.RepairAllSpace);
        Assert.Equal(Convert.FromHexString("010000000002FFFF0000"),
            RepairItemResponseEncoder.EncodeBody(new(
                null, 0, RepairAllSpace: request.RepairAllSpace)));
    }

    [Fact]
    public void Success_and_failure_bodies_follow_the_upstream_ack_layout()
    {
        Assert.Equal(Convert.FromHexString("012F860100030C000000"),
            RepairItemResponseEncoder.EncodeBody(new(
                new GameSlot(InventoryContainerType.Equipment, 12), 99887)));
        Assert.Equal(new byte[] { 0, 10 },
            RepairItemResponseEncoder.EncodeBody(new(null, ErrorCode: 10)));
    }
}
