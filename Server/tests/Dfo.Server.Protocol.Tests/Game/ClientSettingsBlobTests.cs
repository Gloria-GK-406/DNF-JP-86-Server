using System.Buffers.Binary;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.SaveGameOption;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.SaveHotkeys;
using Dfo.Server.Protocol.Game.SaveQuickChat;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class ClientSettingsBlobTests
{
    [Fact]
    public void Unsaved_hotkeys_use_an_empty_text_envelope_instead_of_binary_slots()
    {
        var account = new AccountSettingsEntryState([], [], [], 0, 2);
        Assert.Equal(new byte[5], StateSynchronizationPayloadCodec.EncodeAccountHotkeys(account));
    }

    [Theory]
    [InlineData(int.MaxValue, 8)]
    [InlineData(150, 151)]
    public void Main_upload_rejects_overflow_or_trailing_bytes(int declared, int actual)
    {
        var body = new byte[actual + 4];
        BinaryPrimitives.WriteInt32LittleEndian(body, declared);
        Assert.Throws<InvalidDataException>(() => SaveGameOption1PayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Hotkey_upload_and_replay_preserve_full_odd_length_text_and_profile(int keyType)
    {
        byte[] payload = [65,9,49,10,255,0,127];
        var body = new byte[payload.Length+4]; BinaryPrimitives.WriteInt32LittleEndian(body,payload.Length); payload.CopyTo(body,4);
        var request = Assert.IsType<SaveHotkeysRequest>(new SaveHotkeysDecoder().Decode(GameMessageMetadata.Empty,body));
        body[4] = 0; Assert.Equal(payload,request.Payload);
        var account = new AccountSettingsEntryState([],[],[],99,2)
        { HotkeyProfiles = [new((byte)keyType,request.Payload)] };
        var replay = StateSynchronizationPayloadCodec.EncodeHotkeys(account,null!,(byte)keyType);
        Assert.Equal(keyType,replay[0]); Assert.Equal(payload.Length,BinaryPrimitives.ReadInt32LittleEndian(replay.AsSpan(1)));
        Assert.Equal(payload,replay[5..]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Chat_banks_round_trip_as_independent_length_prefixed_blobs(byte bank)
    {
        var body = new byte[305]; body[0]=bank; BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1),300);
        for (var i=5;i<body.Length;i++) body[i]=(byte)i;
        var request = Assert.IsType<SaveQuickChatRequest>(new SaveQuickChatDecoder().Decode(GameMessageMetadata.Empty,body));
        Assert.Equal(bank,request.Bank); Assert.Equal(body[5..],request.Payload);
        var settings = new AccountSettingsEntryState(new byte[150],bank==0?request.Payload:[],bank==1?request.Payload:[],0,null);
        using var reader = new BinaryReader(new MemoryStream(StateSynchronizationPayloadCodec.EncodeGameOptions(settings)));
        Assert.Equal(150,reader.ReadInt32()); reader.ReadBytes(150);
        for (byte index=0;index<2;index++)
        { var count=reader.ReadInt32(); Assert.Equal(index==bank?300:0,count); Assert.Equal(index==bank?request.Payload:[],reader.ReadBytes(count)); }
        Assert.Equal(reader.BaseStream.Length,reader.BaseStream.Position);
    }

    [Theory]
    [InlineData(0,299,299)]
    [InlineData(1,300,301)]
    [InlineData(2,300,300)]
    [InlineData(255,300,300)]
    public void Invalid_chat_bank_or_length_is_rejected(byte bank,int declared,int actual)
    {
        var body=new byte[5+actual];body[0]=bank;BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1),declared);
        Assert.Throws<InvalidDataException>(()=>new SaveQuickChatDecoder().Decode(GameMessageMetadata.Empty,body));
    }

    [Theory]
    [InlineData(0,0)]
    [InlineData(65536,65536)]
    public void Hotkey_empty_and_bounded_payloads_are_preserved(int declared,int actual)
    {
        var body=new byte[4+actual];BinaryPrimitives.WriteInt32LittleEndian(body,declared);
        var result=Assert.IsType<SaveHotkeysRequest>(new SaveHotkeysDecoder().Decode(GameMessageMetadata.Empty,body));
        Assert.Equal(actual,result.Payload.Length);
    }

    [Theory]
    [InlineData(-1,0)]
    [InlineData(65537,65537)]
    [InlineData(2,3)]
    public void Invalid_hotkey_length_is_rejected(int declared,int actual)
    {
        var body=new byte[4+actual];BinaryPrimitives.WriteInt32LittleEndian(body,declared);
        Assert.Throws<InvalidDataException>(()=>new SaveHotkeysDecoder().Decode(GameMessageMetadata.Empty,body));
    }
}
