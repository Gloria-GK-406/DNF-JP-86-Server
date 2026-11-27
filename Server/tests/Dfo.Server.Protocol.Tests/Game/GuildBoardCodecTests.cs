using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildBoard;

namespace Dfo.Server.Protocol.Tests.Game.GuildBoard;

public sealed class GuildBoardCodecTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Open_and_refresh_share_the_native_query(byte mode)
    {
        var input = Assert.IsType<GuildBoardRequest>(new GuildBoardOpenDecoder().Decode(new(1, 0x0154, 0, 1), [mode]));
        Assert.True(input.Valid);
    }

    [Fact]
    public void Delete_reads_the_full_little_endian_post_key()
    {
        var input = Assert.IsType<GuildBoardRequest>(
            new GuildBoardDeleteDecoder().Decode(new(1, 0x0156, 0, 1), [0x34, 0x12, 0x01, 0x00]));
        Assert.True(input.Valid);
        Assert.Equal(0x11234u, input.PostId);
    }

    [Fact]
    public void Board_reply_writes_the_native_row_and_relative_post_age()
    {
        var postedAt = DateTimeOffset.UnixEpoch;
        var reply = new GuildBoardReply(ServerMessageType.GameGuildBoardOpened,
            new(new(4), postedAt.AddSeconds(100), [new(42, new(4), "Iris"u8.ToArray(), 2, true, "Hello", postedAt)]));

        var frame = Assert.Single(new GuildBoardEncoder().Encode(reply));
        Assert.Equal("01010101020104000000497269732A000000640000000500000048656C6C6F", Convert.ToHexString(frame.AsSpan(15)));
    }
}
