using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.Forging;

namespace Dfo.Server.Protocol.Tests.Game;

[Trait("Category", "Unit")]
public sealed class EquipmentForgingWireTests
{
    [Fact]
    public void Ordinary_A21_request_decodes_list_slots_template_name_and_variant()
    {
        var body = new byte[18];
        body[0] = 0;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), 9);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), 3000);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(7), 121);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), 4);
        body[13] = 0x41; body[14] = 0x42; body[15] = 0x43; body[16] = 0x44;
        body[17] = 0;

        var decoded = Assert.IsType<ForgingRequest>(
            new ForgingDecoder().Decode(default!, body));
        Assert.Equal((byte)0, decoded.TargetListType);
        Assert.Equal((short)9, decoded.TargetSlot);
        Assert.Equal(3000, decoded.TargetItemId);
        Assert.Equal((short)121, decoded.MaterialSlot);
        Assert.Equal("41424344", Convert.ToHexString(decoded.ReportedNameBytes));
        Assert.Equal((byte)0, decoded.Variant);
    }

    [Fact]
    public void Malformed_dynamic_length_is_rejected_before_dispatch()
    {
        var body = new byte[18];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), 5);
        Assert.Throws<InvalidDataException>(() => new ForgingDecoder().Decode(default!, body));
    }

    [Fact]
    public void Success_and_error_follow_A21_handler_read_order()
    {
        Assert.Equal(new byte[] { 1, 121, 0, 11, 0, 0, 0, 0, 0, 1, 0, 9, 0 },
            ForgingEncoder.EncodeBody(new(true, MaterialSlot: 121, MaterialRemaining: 11,
                OldLevel: 0, ForgeSucceeded: true, NewLevel: 1,
                TargetListType: 0, TargetSlot: 9)));
        Assert.Equal(new byte[] { 1, 121, 0, 7, 0, 0, 0, 1, 1, 1, 0, 9, 0 },
            ForgingEncoder.EncodeBody(new(true, MaterialSlot: 121, MaterialRemaining: 7,
                OldLevel: 1, ForgeSucceeded: false, NewLevel: 1,
                TargetListType: 0, TargetSlot: 9)));
        Assert.Equal(new byte[] { 0, 0x0D },
            ForgingEncoder.EncodeBody(new(false, ErrorCode: 0x0D)));
    }
}
