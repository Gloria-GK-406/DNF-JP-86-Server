using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Warehouse;

namespace Dfo.Server.Protocol.Tests.Game.Warehouse;

public sealed class WarehouseCodecTests
{
    [Theory]
    [InlineData(0x0131,0)] [InlineData(0x0132,0)] [InlineData(0x0133,4)]
    [InlineData(0x0134,4)] [InlineData(0x0198,2)] [InlineData(0x01AF,2)]
    public void Requests_require_exact_known_body_width(int type,int length)
    {
        var metadata=GameMessageMetadata.Empty with { Type=(ushort)type };
        var body=new byte[length];
        if(length==2) BinaryPrimitives.WriteUInt16LittleEndian(body,65);
        if(length==4) BinaryPrimitives.WriteInt32LittleEndian(body,400000000);
        var decoder=new WarehouseDecoder();
        var result=Assert.IsType<WarehouseRequest>(decoder.Decode(metadata,body));
        Assert.Equal(length==0?0:length==2?65:400000000,result.Value);
        Assert.Equal((ClientMessageType)(0x10000|type),result.MessageType);
        Assert.Throws<InvalidDataException>(()=>decoder.Decode(metadata,new byte[length+1]));
        if(length>0) Assert.Throws<InvalidDataException>(()=>decoder.Decode(metadata,new byte[length-1]));
    }
    [Theory]
    [InlineData(0x0133)] [InlineData(0x0134)]
    public void Successful_gold_responses_carry_absolute_cargo_balance(int type)
    {
        var body=WarehouseResponseEncoder.EncodeBody(new((ushort)type,true,Gold:400000000));
        Assert.Equal(5,body.Length); Assert.Equal(1,body[0]);
        Assert.Equal(400000000,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1)));
        Assert.Equal(new byte[]{0,95},WarehouseResponseEncoder.EncodeBody(new((ushort)type,false,95)));
    }
    [Fact]
    public void Upgrade_success_has_no_invented_payload_and_inventory_upgrade_has_no_cmd_response()
    {
        foreach(var type in new ushort[]{0x0131,0x0132,0x0198})
            Assert.Equal(new byte[]{1},WarehouseResponseEncoder.EncodeBody(new(type,true)));
        Assert.Throws<InvalidDataException>(()=>WarehouseResponseEncoder.EncodeBody(new(0x01AF,true)));
    }
}
