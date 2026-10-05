using System.Buffers.Binary;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.WeeklyAttendance;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
namespace Dfo.Server.Protocol.Tests.Game.WeeklyAttendance;
public sealed class WeeklyAttendanceProtocolTests
{
    [Fact]
    public void Calendar_chinese_notice_has_known_GBK_bytes_and_decodes_with_A21_default_codepage()
    {
        var info=new EventInfo(2208,"中文","中文","中文","中文","中文",DateTimeOffset.FromUnixTimeSeconds(1),DateTimeOffset.FromUnixTimeSeconds(2),"total attendance check event/0/0");
        using var reader=new BinaryReader(new MemoryStream(EventPayloadCodec.Information([info])));
        Assert.Equal(1,reader.ReadUInt16());Assert.Equal(2208,reader.ReadUInt16());Assert.Equal(0u,reader.ReadUInt32());
        var length=reader.ReadUInt32();var bytes=reader.ReadBytes(checked((int)length));
        // Independent GBK vector: 中=D6 D0, 文=CE C4; the A21 consumer uses MultiByteToWideChar(936).
        Assert.Equal(new byte[]{0xD6,0xD0,0xCE,0xC4},bytes);Assert.Equal(4u,length);
        var consumer=System.Text.CodePagesEncodingProvider.Instance.GetEncoding(936)!;
        Assert.Equal("中文",consumer.GetString(bytes));
    }
    [Fact]
    public void Attendance_matches_A21_fixed_consumer_offsets_and_clears_disabled_state()
    {
        var body=EventPayloadCodec.Attendance(new(true,4,7,4,true,[1,2,3,4]));
        Assert.Equal(450,body.Length);Assert.Equal(4u,BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));
        Assert.Equal(new byte[]{1,2,3,4},body[0x198..0x19C]);Assert.Equal(7u,BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x1B0)));
        Assert.Equal(4u,BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x1BC)));Assert.Equal(1,body[0x1C0]);
        Assert.Equal(0,body[0x1C1]);Assert.Equal(0u,BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x1B8)));
        Assert.All(EventPayloadCodec.Attendance(WeeklyAttendanceState.Empty),b=>Assert.Equal(0,b));
    }
    [Fact]
    public void Request_accepts_opaque_fourteen_bytes_without_interpreting_them_and_rejects_other_lengths()
    {
        var metadata=new GameMessageMetadata(1,0x0441,0,1);var body=Enumerable.Range(0,14).Select(i=>(byte)(255-i)).ToArray();
        Assert.Equal(body,EventPayloadCodec.Decode(metadata,body).Opaque);
        foreach(var size in new[]{0,13,15})Assert.Throws<InvalidDataException>(()=>EventPayloadCodec.Decode(metadata,new byte[size]));
    }
    [Fact]
    public void Calendar_detail_round_trips_client_read_order_and_entry_sends_attendance_after_calendar()
    {
        var info=new EventInfo(2208,"累计周签到","周签到","说明","开始","结束",DateTimeOffset.FromUnixTimeSeconds(1700000000),DateTimeOffset.FromUnixTimeSeconds(1800000000),"total attendance check event/0/0");
        using var reader=new BinaryReader(new MemoryStream(EventPayloadCodec.Information([info])));
        var consumer=System.Text.CodePagesEncodingProvider.Instance.GetEncoding(936)!;
        string Text()=>consumer.GetString(reader.ReadBytes(checked((int)reader.ReadUInt32())));
        Assert.Equal(1,reader.ReadUInt16());Assert.Equal(2208,reader.ReadUInt16());Assert.Equal(0u,reader.ReadUInt32());
        Assert.Equal("开始",Text());Assert.Equal("结束",Text());Assert.Equal(1,reader.ReadByte());Assert.Equal(0,reader.ReadByte());Assert.Equal(5,reader.ReadByte());
        Assert.Equal("累计周签到",Text());Assert.Equal("周签到",Text());Assert.Equal("",Text());Assert.Equal(1700000000u,reader.ReadUInt32());Assert.Equal(1800000000u,reader.ReadUInt32());
        Assert.Equal(info.LinkKey,Text());Assert.Equal("说明",Text());Assert.Equal(1,reader.ReadByte());Assert.Equal(0,reader.ReadByte());Assert.Equal(reader.BaseStream.Length,reader.BaseStream.Position);
        var result=SelectCharacterPayloadTestData.CreateComplete() with { Events=new([info],new(true,0,0,1,false,[])) };
        var messages=SelectedCharacterStateMessageFactory.Create(result).ToList();var calendar=messages.FindIndex(m=>m is EventInformationMessage);
        Assert.True(calendar>0);Assert.IsType<WeeklyAttendanceStateMessage>(messages[calendar+1]);
    }
}
