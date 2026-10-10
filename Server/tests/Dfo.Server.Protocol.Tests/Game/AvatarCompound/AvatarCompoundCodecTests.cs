using System.Buffers.Binary;
using System.Security.Cryptography;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.AvatarCompound;

namespace Dfo.Server.Protocol.Tests.Game.AvatarCompound;
public sealed class AvatarCompoundCodecTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Complete_request_preserves_all_unsigned_slots_options_and_category(bool checksum)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write((ushort)65); w.Write((ushort)300); w.Write(39086); w.Write((ushort)301); w.Write(39087);
        w.Write(1); w.Write((ushort)40); w.Write((byte)1); w.Write((byte)1);
        var b = stream.ToArray();
        if (checksum)
        {
            var m = MD5.HashData(b);
            b = [.. b, (byte)(m[0]^m[13]^m[8]^m[10]^0x81), (byte)(m[1]^m[9]^m[5]^m[12]^0x78),
                (byte)(m[2]^m[6]^m[14]^m[4]^0x1a), (byte)(m[3]^m[15]^m[11]^m[7]^0xbf)];
        }
        var r = Assert.IsType<AvatarCompoundRequest>(new AvatarCompoundDecoder().Decode(GameMessageMetadata.Empty, b));
        Assert.True(r.Valid); Assert.Equal(65, r.MaterialSlot); Assert.Equal(300, r.FirstSlot); Assert.Equal(301, r.SecondSlot);
        Assert.Equal(39086, r.FirstItemId); Assert.Equal(39087, r.SecondItemId); Assert.Equal(1, r.TargetItemId);
        Assert.Equal(40, r.Ability); Assert.Equal(1, r.RandomTarget); Assert.Equal(1, r.Category);
    }
    [Fact]
    public void Refusal_is_only_standard_two_byte_failure_envelope()
        => Assert.Equal(new byte[] { 0, 0x16 }, Body(new(null, 0x16)));
    [Fact]
    public void Recovery_has_no_consumption_no_extra_blocks_and_only_empty_results()
    {
        var body = Body(new(null, Recovery: true));
        Assert.Equal(26, body.Length); Assert.Equal(new byte[] { 1, 0 }, body[..2]);
        foreach (var offset in new[] { 2, 14 })
        { Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(offset)));
            Assert.Equal(new byte[10], body[(offset + 2)..(offset + 12)]); }
    }
    private static byte[] Body(AvatarCompoundResponse r)
    {
        var frame = Assert.Single(new AvatarCompoundEncoder().Encode(r));
        Assert.Equal(1, frame[0]); Assert.Equal(0x63, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        return frame[15..];
    }
}
