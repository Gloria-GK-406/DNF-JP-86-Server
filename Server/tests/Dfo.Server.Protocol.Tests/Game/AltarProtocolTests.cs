using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.Altar;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class AltarProtocolTests
{
    [Fact]
    public void A21_info_has_eight_flag_bytes_after_dungeon_id()
    {
        var endless = AltarPayloadCodec.Info(11006, AltarDungeonKind.Endless);
        Assert.Equal(12, endless.Length);
        Assert.Equal(11006, BinaryPrimitives.ReadInt32LittleEndian(endless));
        Assert.Equal(new byte[] { 0, 0, 2, 0, 0, 0, 0, 0 }, endless[4..]);
        Assert.Equal(new byte[8], AltarPayloadCodec.Info(11007, AltarDungeonKind.Ultimate)[4..]);
    }
    [Fact]
    public void Map_has_u32_id_then_three_empty_content_counts()
    {
        var body = AltarPayloadCodec.Map(1, 0, 0x11223344, 17030);
        Assert.Equal(15, body.Length);
        Assert.Equal(new byte[] { 1, 0, 0x44, 0x33, 0x22, 0x11, 0, 1 }, body[..8]);
        Assert.Equal(17030, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8)));
        Assert.Equal(new byte[3], body[12..]);
    }
    [Fact]
    public void Dynamic_spawn_is_seventeen_bytes_and_tail_follows_all_rows()
    {
        var body = AltarPayloadCodec.Wave([new(0x1234, 0x12345678, 3, 70, 150, 400, 240, 0, 19)], 3000);
        Assert.Equal(21, body.Length);
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(body));
        Assert.Equal((byte)19, body[2]);
        Assert.Equal((ushort)0x1234, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(3)));
        Assert.Equal(0x12345678, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
        Assert.Equal((byte)3, body[9]); Assert.Equal((byte)70, body[10]);
        Assert.Equal((ushort)150, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(11)));
        Assert.Equal((ushort)400, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(13)));
        Assert.Equal((ushort)240, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(15)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(17)));
        Assert.Equal((ushort)3000, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(19)));
    }
    [Fact]
    public void Empty_rewards_still_have_all_four_participant_group_counts()
    {
        Assert.Equal(new byte[] { 7, 7, 0, 0, 0, 0 }, AltarPayloadCodec.Rewards(7, 7, []));
    }
    [Fact]
    public void Ranking_has_six_u32_values_and_does_not_fake_a_history()
    {
        var body = AltarPayloadCodec.Ranking(12345, 7, 7, 8000);
        Assert.Equal(24, body.Length);
        Assert.Equal(12345u, BinaryPrimitives.ReadUInt32LittleEndian(body));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));
        Assert.Equal(new byte[8], body[8..16]);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16)));
        Assert.Equal(8000u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(20)));
    }
    [Fact]
    public void Difficulty_prompt_and_resolution_have_different_shapes()
    {
        Assert.Equal(new byte[] { 0, 1, 10, 0 }, AltarPayloadCodec.DifficultyPrompt(1, 10));
        Assert.Equal(new byte[] { 2 }, new AltarDifficultyResolvedMessage(2).EncodeBody());
        Assert.Equal(new byte[] { 1, 2 }, new AltarDifficultyResponse(true, 2).EncodeBody());
        Assert.Equal(new byte[] { 0, 1 }, new AltarDifficultyResponse(false, 2).EncodeBody());
    }
    [Fact]
    public void Batch_death_decoder_rejects_truncated_or_extra_fields_without_throwing()
    {
        var decoder = new AltarDeathBatchDecoder();
        Assert.Null(decoder.Decode(default!, []));
        Assert.Null(decoder.Decode(default!, [2, 1, 0]));
        Assert.Null(decoder.Decode(default!, [1, 1, 0, 99]));
        var request = Assert.IsType<AltarDeathBatchRequest>(decoder.Decode(default!, [2, 1, 0, 0x34, 0x12]));
        Assert.Equal(new ushort[] { 1, 0x1234 }, request.Actors);
    }
}
