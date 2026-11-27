using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game.GuildRoster;

namespace Dfo.Server.Protocol.Tests.Game.GuildRoster;

public sealed class GuildRosterCodecTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Roster_carries_online_and_offline_members_and_one_representative_per_account(bool all)
    {
        var type = all ? ServerMessageType.GameGuildAllMemberList : ServerMessageType.GameGuildMemberList;
        var joinedAt = DateTimeOffset.UnixEpoch;
        var roster = new GuildRosterSnapshot(new(42), "会长"u8.ToArray(),
        [
            new(new(new(new(42), new(11), true, joinedAt), new(1), "会长"u8.ToArray(), 30, 1, 2), 11),
            new(new(new(new(42), new(12), false, joinedAt, Memo: "周末组队"), new(2), "成员"u8.ToArray(), 29, 3, 4), byte.MaxValue),
            new(new(new(new(42), new(13), false, joinedAt), new(2), "另一个角色"u8.ToArray(), 28, 3, 4), byte.MaxValue),
        ]);
        var frame = Assert.Single(new GuildRosterReplyEncoder().Encode(new GuildRosterReply(type, roster)));
        Assert.Equal((byte)1, frame[0]);
        Assert.Equal(all ? (ushort)0x008C : (ushort)0x0043, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        using var body = new MemoryStream(frame[GameServerFrameEncoder.HeaderLength..]);
        using var reader = new BinaryReader(body);
        Assert.Equal((byte)1, reader.ReadByte());
        Assert.Equal(42u, reader.ReadUInt32());
        Assert.Equal("会长"u8.ToArray(), ReadText(reader));
        Assert.Equal(0u, reader.ReadUInt32());
        Assert.Equal((ushort)3, reader.ReadUInt16());
        Assert.Equal((ushort)3, reader.ReadUInt16());

        AssertRow(11, "会长"u8.ToArray(), 30, 1, 2, 11, 1, 1, true);
        AssertRow(12, "成员"u8.ToArray(), 29, 3, 4, byte.MaxValue, 4, 2, true, "周末组队"u8.ToArray());
        AssertRow(13, "另一个角色"u8.ToArray(), 28, 3, 4, byte.MaxValue, 4, 2, false);
        if (all)
            Assert.Equal(0u, reader.ReadUInt32());
        Assert.Equal(body.Length, body.Position);

        void AssertRow(int id, byte[] name, ushort level, byte job, byte grow, byte channel, byte rank, int account,
            bool representative, byte[]? memo = null)
        {
            Assert.Equal(id, reader.ReadInt32());
            Assert.Equal(name, ReadText(reader));
            Assert.Equal(memo ?? [], ReadText(reader));
            Assert.Equal(level, reader.ReadUInt16());
            Assert.Equal(new byte[] { job, grow, 1, channel, 0, 0, rank }, reader.ReadBytes(7));
            Assert.Equal(0u, reader.ReadUInt32());
            Assert.Equal(account, reader.ReadInt32());
            Assert.Equal(representative ? (byte)1 : (byte)0, reader.ReadByte());
            Assert.Empty(ReadText(reader));
        }
    }

    [Fact]
    public void Only_the_empty_default_request_and_zero_full_list_cursor_are_accepted()
    {
        var regular = new GuildMemberListDecoder();
        var full = new GuildAllMemberListDecoder();
        Assert.True(Assert.IsType<GuildRosterRequest>(regular.Decode(GameMessageMetadata.Empty, [])).Valid);
        Assert.False(Assert.IsType<GuildRosterRequest>(regular.Decode(GameMessageMetadata.Empty, [0])).Valid);
        Assert.True(Assert.IsType<GuildRosterRequest>(full.Decode(GameMessageMetadata.Empty, [0, 0, 0, 0])).Valid);
        Assert.False(Assert.IsType<GuildRosterRequest>(full.Decode(GameMessageMetadata.Empty, [1, 0, 0, 0])).Valid);
        Assert.False(Assert.IsType<GuildRosterRequest>(full.Decode(GameMessageMetadata.Empty, [])).Valid);
    }

    private static byte[] ReadText(BinaryReader reader) => reader.ReadBytes(reader.ReadInt32());
}
