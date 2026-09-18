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
            (request.Container, request.SlotIndex, request.MaterialSlot, request.QuickRepair));
    }

    [Fact]
    public void Success_and_failure_bodies_follow_the_upstream_ack_layout()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("repair"), 1, 10011);
        var receipt = new EquipmentRepairReceipt(session, InventoryContainerType.Equipment, 12, 99887);
        Assert.Equal(Convert.FromHexString("012F860100030C000000"),
            RepairItemResponseEncoder.EncodeBody(new(receipt)));
        Assert.Equal(new byte[] { 0, 10 }, RepairItemResponseEncoder.EncodeBody(new(null, 10)));
    }
}
