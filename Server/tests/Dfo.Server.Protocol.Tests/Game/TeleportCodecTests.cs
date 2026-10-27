using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Teleport;

namespace Dfo.Server.Protocol.Tests.Game;

[Trait("Category", "Unit")]
public sealed class TeleportCodecTests
{
    [Fact]
    public void Actual_selection_request_decodes_the_owned_source_item_and_target_town()
    {
        var body = Convert.FromHexString("6A004EAC2700000B00FF7FFF7F");
        var request = Assert.IsType<TeleportRequest>(new TeleportDecoder().Decode(GameMessageMetadata.Empty, body));
        Assert.Equal(106, request.SourceSlot); Assert.Equal(2600014, request.ItemId);
        Assert.Equal(11, request.TownId); Assert.True(request.IsSupported);
        Assert.Throws<InvalidDataException>(() => new TeleportDecoder().Decode(GameMessageMetadata.Empty, body[..12]));
        Assert.Throws<InvalidDataException>(() => new TeleportDecoder().Decode(GameMessageMetadata.Empty, new byte[14]));
    }
    [Fact]
    public void Cooldown_is_one_counted_item_and_seconds_as_read_by_A21()
    {
        var frame = Assert.Single(new TeleportCooldownEncoder().Encode(new TeleportCooldownNotification(2600014, 10))).ToArray();
        Assert.Equal(0, frame[0]); Assert.Equal(0x00AC, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal("014EAC27000A000000", Convert.ToHexString(frame.AsSpan(15)));
    }
}
