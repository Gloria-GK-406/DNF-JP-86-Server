using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.EquipmentDurability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.EquipmentDurability;

public sealed class DecreaseDurabilityCodecTests
{
    [Fact]
    public void Client_report_is_a_single_equipment_slot_byte()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0030);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var request = Assert.IsType<DecreaseDurabilityRequest>(
            decoder.Decode(GameMessageMetadata.Empty, [12]));
        Assert.Equal((byte)12, request.SlotIndex);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [12, 0]));
    }

    [Fact]
    public void Success_and_failure_ack_match_the_upstream_two_byte_shape()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("wear"), 1, 10011);
        var receipt = new EquipmentDurabilityReceipt(session,
            new BusinessSlot(BusinessInventorySpace.WornEquipment, 0), 1);
        Assert.Equal(new byte[] { 1, 12 }, DecreaseDurabilityResponseEncoder.EncodeBody(new(receipt)));
        Assert.Equal(new byte[] { 1, 30 }, DecreaseDurabilityResponseEncoder.EncodeBody(new(receipt with
        { Slot = new(BusinessInventorySpace.WornEquipmentExtension, 1) })));
        Assert.Equal(new byte[] { 0, 17 }, DecreaseDurabilityResponseEncoder.EncodeBody(new(null, 17)));
    }
}
