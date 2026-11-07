using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Protocol.Game.UpgradeChronicle;

namespace Dfo.Server.Protocol.Tests.Game;

[Trait("Category", "Unit")]
public sealed class UpgradeChronicleWireTests
{
    [Fact]
    public void Requiem_request_decodes_the_A21_19_byte_layout()
    {
        // Ticket 10150749 at main 65, requiem weapon 101000028 at main 9, opaque 0x5C, no material (0xFFFF / 0x0000FFFF).
        var body = Convert.FromHexString("41005DE39A00" + "09005C230506" + "5C" + "FFFF" + "FFFF0000");

        var decoded = Assert.IsType<UpgradeChronicleRequest>(new UpgradeChronicleDecoder().Decode(default!, body));

        Assert.Equal((short)65, decoded.TicketSlot);
        Assert.Equal(10150749, decoded.TicketItemId);
        Assert.Equal((short)9, decoded.TargetSlot);
        Assert.Equal(101000028, decoded.TargetItemId);
        Assert.Equal((byte)0x5C, decoded.Opaque);
        Assert.Equal((short)-1, decoded.MaterialSlot);
        Assert.Equal(0xFFFF, decoded.MaterialItemId);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(20)]
    public void Other_lengths_are_rejected_before_dispatch(int length) =>
        Assert.Throws<InvalidDataException>(() => new UpgradeChronicleDecoder().Decode(default!, new byte[length]));

    [Fact]
    public void Success_rows_follow_the_A21_handler_read_order()
    {
        Assert.Equal(Convert.FromHexString("0102" + "00" + "4100" + "01000000" + "00" + "7900" + "0F000000"),
            UpgradeChronicleEncoder.EncodeBody(new(true, Debits: [new(0, 65, 1), new(0, 121, 15)])));
        Assert.Equal(new byte[] { 1, 0 }, UpgradeChronicleEncoder.EncodeBody(new(true)));
        Assert.Equal(new byte[] { 0, 0x16 }, UpgradeChronicleEncoder.EncodeBody(new(false, 0x16)));
    }

    [Theory]
    [InlineData(EquipmentEmancipateRejection.ItemUnavailable, 0x04)]
    [InlineData(EquipmentEmancipateRejection.Durability, 0x07)]
    [InlineData(EquipmentEmancipateRejection.NotApplicable, 0x11)]
    [InlineData(EquipmentEmancipateRejection.InUse, 0x13)]
    [InlineData(EquipmentEmancipateRejection.MaterialUnavailable, 0x16)]
    [InlineData(EquipmentEmancipateRejection.Expired, 0x1B)]
    [InlineData(EquipmentEmancipateRejection.Unpurified, 0xAF)]
    [InlineData(EquipmentEmancipateRejection.Locked, 0xD6)]
    [InlineData(EquipmentEmancipateRejection.NoSession, 0x01)]
    public void Rejections_use_codes_with_A21_text(EquipmentEmancipateRejection rejection, byte code) =>
        Assert.Equal(code, GameUpgradeChronicleCommandHandler.ErrorCode(rejection));
}
