using System.Text;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.UnitedFriend;

namespace Dfo.Server.Protocol.Tests.Game.UnitedFriend;

/// <summary>CMD 0x0122 / 0x0123 requests and replies, and NOTI 0x0111 / 0x0112, byte for byte.</summary>
[Trait("Category", "Unit")]
public sealed class FriendCodecTests
{
    private static readonly GameMessageMetadata Metadata = new(1, 0x0122, 0, 1);

    [Fact]
    public void Add_request_reads_uid_server_and_name_and_ignores_the_uid()
    {
        // The archived wire sample 0x0122-2026-09-11-01: uid 2, server 1, "1123".
        var request = Assert.IsType<AddFriendRequest>(new AddFriendDecoder().Decode(Metadata, Convert.FromHexString("0200" + "01" + "04000000" + "31313233")));

        Assert.Equal(new FriendTarget(1, "1123"u8.ToArray()), request.Target, TargetComparer.Instance);
    }

    [Fact]
    public void Add_request_keeps_a_foreign_server_group_for_the_command_to_refuse()
    {
        var request = Assert.IsType<AddFriendRequest>(new AddFriendDecoder().Decode(Metadata, Convert.FromHexString("FFFF" + "02" + "0100000061")));

        Assert.Equal((byte)2, request.Target!.Server);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0200")]
    [InlineData("020001")]
    [InlineData("02000100000000")]
    [InlineData("02000104000000313132")]
    [InlineData("02000104000000313132" + "3300")]
    [InlineData("020001FFFFFFFF")]
    public void A_malformed_add_request_has_no_target(string hex)
    {
        Assert.Null(Assert.IsType<AddFriendRequest>(new AddFriendDecoder().Decode(Metadata, Convert.FromHexString(hex))).Target);
    }

    [Fact]
    public void Delete_request_reads_server_and_name()
    {
        var request = Assert.IsType<DeleteFriendRequest>(new DeleteFriendDecoder().Decode(Metadata, Convert.FromHexString("01" + "03000000" + "616263")));

        Assert.Equal(new FriendTarget(1, "abc"u8.ToArray()), request.Target, TargetComparer.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("0100000000")]
    [InlineData("010300000061")]
    [InlineData("01010000006100")]
    public void A_malformed_delete_request_has_no_target(string hex)
    {
        Assert.Null(Assert.IsType<DeleteFriendRequest>(new DeleteFriendDecoder().Decode(Metadata, Convert.FromHexString(hex))).Target);
    }

    [Fact]
    public void A_name_longer_than_the_list_can_carry_has_no_target()
    {
        var longest = new byte[FriendListMessage.NameLimit];
        var over = new byte[FriendListMessage.NameLimit + 1];

        Assert.NotNull(Assert.IsType<AddFriendRequest>(new AddFriendDecoder().Decode(Metadata, [0xFF, 0xFF, 1, .. Counted(longest)])).Target);
        Assert.Null(Assert.IsType<AddFriendRequest>(new AddFriendDecoder().Decode(Metadata, [0xFF, 0xFF, 1, .. Counted(over)])).Target);
        Assert.NotNull(Assert.IsType<DeleteFriendRequest>(new DeleteFriendDecoder().Decode(Metadata, [1, .. Counted(longest)])).Target);
        Assert.Null(Assert.IsType<DeleteFriendRequest>(new DeleteFriendDecoder().Decode(Metadata, [1, .. Counted(over)])).Target);
    }

    [Theory]
    [InlineData(AddFriendResult.Added, "01")]
    [InlineData(AddFriendResult.Full, "0004")]
    [InlineData(AddFriendResult.OtherServerGroup, "0007")]
    [InlineData(AddFriendResult.NoSuchCharacter, "0015")]
    [InlineData(AddFriendResult.Refused, "0001")]
    public void Add_reply_is_ok_alone_or_not_ok_and_the_clients_reason(AddFriendResult result, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(AddFriendResponseEncoder.EncodeBody(new(result))));
    }

    [Theory]
    [InlineData(true, "01")]
    [InlineData(false, "0015")]
    public void Delete_reply_is_ok_alone_or_reason_0x15(bool removed, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(DeleteFriendResponseEncoder.EncodeBody(new(removed))));
    }

    [Fact]
    public void Full_list_is_subcmd_zero_a_count_and_each_entry_in_the_clients_order()
    {
        var body = FriendListEncoder.EncodeBody(new([
            new(3, "abc"u8.ToArray(), 70, 0x0C, 2),
            new(0, "z"u8.ToArray(), 1, 0, 0),
        ]));

        Assert.Equal(
            "00000000" + "02000000"
            + "01" + "0300" + "03000000" + "616263" + "46" + "0C000000" + "02" + "00" + "00000000"
            + "01" + "0000" + "01000000" + "7A" + "01" + "00000000" + "00" + "00" + "00000000",
            Convert.ToHexString(body));
    }

    [Fact]
    public void An_empty_full_list_is_still_sent_with_count_zero()
    {
        Assert.Equal("0000000000000000", Convert.ToHexString(FriendListEncoder.EncodeBody(new([]))));
    }

    [Fact]
    public void A_full_list_name_is_one_to_255_bytes()
    {
        var longest = FriendListEncoder.EncodeBody(new([new(0, new byte[255], 1, 0, 0)]));

        Assert.Equal(8 + FriendListMessage.EntryBytesBeyondName + 255, longest.Length);
        Assert.Throws<InvalidDataException>(() => FriendListEncoder.EncodeBody(new([new(0, new byte[256], 1, 0, 0)])));
        Assert.Throws<InvalidDataException>(() => FriendListEncoder.EncodeBody(new([new(0, [], 1, 0, 0)])));
    }

    [Fact]
    public void Sixty_four_longest_entries_are_the_documented_maximum_body()
    {
        var entries = Enumerable.Range(0, 64).Select(index => new FriendListEntry(1, Name255(index), 1, 0, 0)).ToArray();

        Assert.Equal(8 + 64 * 273, FriendListEncoder.EncodeBody(new(entries)).Length);
    }

    [Fact]
    public void Removal_is_subcmd_two_a_count_and_each_server_and_name()
    {
        Assert.Equal(
            "02000000" + "01000000" + "01" + "03000000" + "616263",
            Convert.ToHexString(FriendRemovedEncoder.EncodeBody(new(["abc"u8.ToArray()]))));
        Assert.Throws<InvalidDataException>(() => FriendRemovedEncoder.EncodeBody(new([new byte[256]])));
    }

    [Theory]
    [InlineData(7, true, "0700" + "01" + "03000000616263" + "01")]
    [InlineData(0, false, "0000" + "01" + "03000000616263" + "00")]
    public void Presence_is_channel_server_name_and_whether_the_recipient_blacklisted_them(int channel, bool blacklisted, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(FriendPresenceEncoder.EncodeBody(new((ushort)channel, "abc"u8.ToArray(), blacklisted))));
        Assert.Throws<InvalidDataException>(() => FriendPresenceEncoder.EncodeBody(new(1, new byte[256], false)));
    }

    [Fact]
    public void Each_frame_carries_its_wire_identity()
    {
        Assert.Equal(ServerMessageType.GameFriendInfo, new FriendListMessage([]).MessageType);
        Assert.Equal(ServerMessageType.GameFriendInfo, new FriendRemovedMessage([]).MessageType);
        Assert.Equal(ServerMessageType.GameFriendPresence, new FriendPresenceMessage(0, [1], false).MessageType);
        Assert.Equal(ServerMessageType.GameAddFriend, new AddFriendResponse(AddFriendResult.Added).MessageType);
        Assert.Equal(ServerMessageType.GameDeleteFriend, new DeleteFriendResponse(true).MessageType);
        Assert.Equal(0x0000_0111, (int)ServerMessageType.GameFriendInfo);
        Assert.Equal(0x0000_0112, (int)ServerMessageType.GameFriendPresence);
        Assert.Equal(0x0001_0122, (int)ServerMessageType.GameAddFriend);
        Assert.Equal(0x0001_0123, (int)ServerMessageType.GameDeleteFriend);
    }

    private static byte[] Name255(int index)
    {
        var name = Enumerable.Repeat((byte)'n', 255).ToArray();
        Encoding.ASCII.GetBytes(index.ToString("D3")).CopyTo(name, 0);
        return name;
    }

    private static byte[] Counted(byte[] name) => [.. BitConverter.GetBytes(name.Length), .. name];

    private sealed class TargetComparer : IEqualityComparer<FriendTarget?>
    {
        public static TargetComparer Instance { get; } = new();

        public bool Equals(FriendTarget? x, FriendTarget? y) =>
            x is not null && y is not null && x.Server == y.Server && x.Name.AsSpan().SequenceEqual(y.Name);

        public int GetHashCode(FriendTarget? obj) => 0;
    }
}
