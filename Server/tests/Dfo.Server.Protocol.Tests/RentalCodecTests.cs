using System.Buffers.Binary;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Rental;

namespace Dfo.Server.Protocol.Tests;

[Trait("Category", "Unit")]
public sealed class RentalCodecTests
{
    [Fact]
    public void Native_bodies_keep_the_opaque_prefix_and_read_only_the_two_tail_fields()
    {
        var body=Enumerable.Repeat((byte)0xA7,22).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14),401000039);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(18),0x12345678);
        var rent=Assert.IsType<RentEquipmentRequest>(new RentEquipmentDecoder().Decode(GameMessageMetadata.Empty,body));
        Assert.Equal(401000039,rent.ItemId);Assert.Equal(0x12345678U,rent.Context);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14),1);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(18),2);
        var purchase=Assert.IsType<ChargeRentPointRequest>(new ChargeRentPointDecoder().Decode(GameMessageMetadata.Empty,body));
        Assert.Equal(1,purchase.Mode);Assert.Equal(2,purchase.Count);
        foreach(var length in new[]{0,8,14,21,23,36})
        {
            Assert.Throws<InvalidDataException>(()=>new RentEquipmentDecoder().Decode(GameMessageMetadata.Empty,new byte[length]));
            Assert.Throws<InvalidDataException>(()=>new ChargeRentPointDecoder().Decode(GameMessageMetadata.Empty,new byte[length]));
        }
    }

    [Fact]
    public void Cmd_successes_and_rejections_match_the_native_reader_envelope()
    {
        Assert.Equal(new byte[]{1,0,0,0,0},RentEquipmentEncoder.Body(new()));
        Assert.Equal(new byte[]{1,1,0,0,0},RentEquipmentEncoder.Body(new(1)));
        Assert.Equal(new byte[]{1,2,0,0,0},RentEquipmentEncoder.Body(new(2)));
        Assert.Equal(new byte[]{0,1},RentEquipmentEncoder.Body(new(Error:1)));
        Assert.Equal(new byte[]{0,1},ChargeRentPointEncoder.Body(new(1,0,0,1)));
        Assert.Equal(new byte[]{1,1,0,0,0,2,0,0,0,7,0,0,0},ChargeRentPointEncoder.Body(new(1,2,7)));
        // mode2/second1 is the native cap warning, never a one-star reward.
        Assert.Equal(new byte[]{1,2,0,0,0,0,0,0,0,8,0,0,0},ChargeRentPointEncoder.Body(new(2,0,8)));
        var rentFrame=Assert.Single(new RentEquipmentEncoder().Encode(new RentEquipmentResponse()));
        var chargeFrame=Assert.Single(new ChargeRentPointEncoder().Encode(new ChargeRentPointResponse(1,2,7)));
        Assert.Equal(0x03CF,BinaryPrimitives.ReadUInt16LittleEndian(rentFrame.AsSpan(1)));
        Assert.Equal(0x03D0,BinaryPrimitives.ReadUInt16LittleEndian(chargeFrame.AsSpan(1)));
        Assert.Equal(20,rentFrame.Length);Assert.Equal(28,chargeFrame.Length);
    }

    [Fact]
    public void Full_panel_encodes_template_ids_and_absolute_deadlines_instead_of_uids()
    {
        var at=DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var panel=new EquipmentRentalPanel(37,[new(new ItemIdentity(9123),401000039,1700086400),new(new ItemIdentity(9124),401000037,1699999999)]);
        var message=RentalPanelProjection.Full(panel,at);
        var body=message.EncodeBody();
        Assert.Equal(0x03C1,message.Type);Assert.Equal(16,body.Length);
        Assert.Equal(37U,BinaryPrimitives.ReadUInt32LittleEndian(body));
        Assert.Equal(1U,BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));
        Assert.Equal(401000039U,BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
        Assert.Equal(1700086400U,BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));
    }
}
