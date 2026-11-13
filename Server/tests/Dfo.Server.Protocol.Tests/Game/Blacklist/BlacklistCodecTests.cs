using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Blacklist;

namespace Dfo.Server.Protocol.Tests.Game.Blacklist;

[Trait("Category", "Unit")]
public sealed class BlacklistCodecTests
{
    private static readonly GameMessageMetadata Metadata = new(1, 0x0076, 0, 1);

    [Fact]
    public void Register_and_delete_requests_read_one_counted_name_that_fills_the_body()
    {
        var register = Assert.IsType<RegisterBlacklistRequest>(new RegisterBlacklistDecoder().Decode(Metadata, Convert.FromHexString("03000000616263")));
        var delete = Assert.IsType<DeleteBlacklistRequest>(new DeleteBlacklistDecoder().Decode(Metadata, Convert.FromHexString("0100000062")));

        Assert.Equal("abc"u8.ToArray(), register.Name);
        Assert.Equal("b"u8.ToArray(), delete.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("000000")]
    [InlineData("00000000")]
    [InlineData("0200000061")]
    [InlineData("02000000616263")]
    [InlineData("FFFFFFFF")]
    [InlineData("FFFFFF7F61")]
    public void A_name_that_does_not_fill_its_body_is_still_a_request_but_has_no_name(string hex)
    {
        // The client waits on an answer to both, so a malformed body is refused rather than dropped.
        Assert.Null(Assert.IsType<RegisterBlacklistRequest>(new RegisterBlacklistDecoder().Decode(Metadata, Convert.FromHexString(hex))).Name);
        Assert.Null(Assert.IsType<DeleteBlacklistRequest>(new DeleteBlacklistDecoder().Decode(Metadata, Convert.FromHexString(hex))).Name);
    }

    [Fact]
    public void A_name_longer_than_the_register_reply_can_carry_has_no_name()
    {
        var longest = new byte[BlacklistPayloadCodec.RequestNameLimit];
        var over = new byte[BlacklistPayloadCodec.RequestNameLimit + 1];

        Assert.Equal(longest, Assert.IsType<RegisterBlacklistRequest>(new RegisterBlacklistDecoder().Decode(Metadata, Counted(longest))).Name);
        Assert.Null(Assert.IsType<RegisterBlacklistRequest>(new RegisterBlacklistDecoder().Decode(Metadata, Counted(over))).Name);
        Assert.Null(Assert.IsType<DeleteBlacklistRequest>(new DeleteBlacklistDecoder().Decode(Metadata, Counted(over))).Name);
    }

    [Fact]
    public void List_request_is_taken_whatever_it_carries()
    {
        Assert.IsType<BlacklistListRequest>(new BlacklistListDecoder().Decode(Metadata, []));
        Assert.IsType<BlacklistListRequest>(new BlacklistListDecoder().Decode(Metadata, [1, 2]));
    }

    [Fact]
    public void Register_success_is_ok_and_the_targets_name()
    {
        var body = RegisterBlacklistResponseEncoder.EncodeBody(new(RegisterBlacklistResult.Registered, "abc"u8.ToArray()));

        Assert.Equal("01" + "03000000" + "616263", Convert.ToHexString(body));
    }

    [Theory]
    [InlineData(RegisterBlacklistResult.Duplicate, "004A")]
    [InlineData(RegisterBlacklistResult.NoSuchCharacter, "004C")]
    [InlineData(RegisterBlacklistResult.Full, "004E")]
    [InlineData(RegisterBlacklistResult.Refused, "0000")]
    public void Register_refusal_is_not_ok_and_its_reason(RegisterBlacklistResult result, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(RegisterBlacklistResponseEncoder.EncodeBody(new(result, []))));
    }

    [Theory]
    [InlineData(CharacterRelationAddOutcome.Added, RegisterBlacklistResult.Registered)]
    [InlineData(CharacterRelationAddOutcome.AlreadyPresent, RegisterBlacklistResult.Duplicate)]
    [InlineData(CharacterRelationAddOutcome.TargetMissing, RegisterBlacklistResult.NoSuchCharacter)]
    [InlineData(CharacterRelationAddOutcome.Full, RegisterBlacklistResult.Full)]
    [InlineData(CharacterRelationAddOutcome.Self, RegisterBlacklistResult.Refused)]
    public void Every_domain_outcome_maps_to_one_register_result(CharacterRelationAddOutcome outcome, RegisterBlacklistResult result)
    {
        Assert.Equal(result, BlacklistPayloadCodec.ResultOf(outcome));
    }

    [Fact]
    public void Register_success_name_is_bounded_by_what_the_client_reads()
    {
        Assert.Throws<InvalidDataException>(() => RegisterBlacklistResponseEncoder.EncodeBody(new(RegisterBlacklistResult.Registered, [])));
        Assert.Throws<InvalidDataException>(() => RegisterBlacklistResponseEncoder.EncodeBody(
            new(RegisterBlacklistResult.Registered, new byte[RegisterBlacklistResponse.NameLimit + 1])));
        Assert.Equal(1 + 4 + 255, RegisterBlacklistResponseEncoder.EncodeBody(
            new(RegisterBlacklistResult.Registered, new byte[RegisterBlacklistResponse.NameLimit])).Length);
    }

    [Fact]
    public void Delete_success_is_ok_and_the_name_and_refusal_is_reason_75()
    {
        Assert.Equal("01" + "02000000" + "6162", Convert.ToHexString(DeleteBlacklistResponseEncoder.EncodeBody(new("ab"u8.ToArray()))));
        Assert.Equal("004B", Convert.ToHexString(DeleteBlacklistResponseEncoder.EncodeBody(new(null))));
    }

    [Fact]
    public void Delete_success_name_is_one_to_thirty_bytes()
    {
        Assert.Equal(1 + 4 + 30, DeleteBlacklistResponseEncoder.EncodeBody(new(new byte[30])).Length);
        Assert.Throws<InvalidDataException>(() => DeleteBlacklistResponseEncoder.EncodeBody(new(new byte[31])));
        Assert.Throws<InvalidDataException>(() => DeleteBlacklistResponseEncoder.EncodeBody(new([])));
    }

    [Fact]
    public void Empty_list_is_ok_and_count_zero()
    {
        Assert.Equal("0100", Convert.ToHexString(BlacklistListResponseEncoder.EncodeBody(new([]))));
    }

    [Fact]
    public void List_entries_are_name_then_year_since_1900_month_from_zero_and_day()
    {
        var body = BlacklistListResponseEncoder.EncodeBody(new([
            new("ab"u8.ToArray(), new DateOnly(2026, 1, 31)),
            new("c"u8.ToArray(), new DateOnly(1999, 12, 1)),
        ]));

        Assert.Equal("01" + "02"
            + "02000000" + "6162" + "7E" + "00" + "1F"
            + "01000000" + "63" + "63" + "0B" + "01", Convert.ToHexString(body));
    }

    [Fact]
    public void List_entry_name_is_one_to_thirty_bytes()
    {
        Assert.Equal(2 + 4 + 30 + 3, BlacklistListResponseEncoder.EncodeBody(new([new(new byte[30], new DateOnly(2026, 1, 1))])).Length);
        Assert.Throws<InvalidDataException>(() => BlacklistListResponseEncoder.EncodeBody(new([new(new byte[31], new DateOnly(2026, 1, 1))])));
        Assert.Throws<InvalidDataException>(() => BlacklistListResponseEncoder.EncodeBody(new([new([], new DateOnly(2026, 1, 1))])));
    }

    [Fact]
    public void List_years_and_count_outside_a_byte_are_not_encoded()
    {
        Assert.Throws<InvalidDataException>(() => BlacklistListResponseEncoder.EncodeBody(new([new("a"u8.ToArray(), new DateOnly(1899, 12, 31))])));
        Assert.Throws<InvalidDataException>(() => BlacklistListResponseEncoder.EncodeBody(new([new("a"u8.ToArray(), new DateOnly(2156, 1, 1))])));
        Assert.Throws<InvalidDataException>(() => BlacklistListResponseEncoder.EncodeBody(new(
            [.. Enumerable.Repeat(new BlacklistListEntry("a"u8.ToArray(), new DateOnly(2026, 1, 1)), 256)])));
    }

    [Fact]
    public void Each_reply_is_the_cmd_of_its_own_type()
    {
        Assert.Equal(ServerMessageType.GameRegisterBlacklist, new RegisterBlacklistResponse(RegisterBlacklistResult.Refused, []).MessageType);
        Assert.Equal(ServerMessageType.GameDeleteBlacklist, new DeleteBlacklistResponse(null).MessageType);
        Assert.Equal(ServerMessageType.GameRequestBlacklist, new BlacklistListResponse([]).MessageType);
        Assert.Equal(0x0001_0076, (int)ServerMessageType.GameRegisterBlacklist);
        Assert.Equal(0x0001_0077, (int)ServerMessageType.GameDeleteBlacklist);
        Assert.Equal(0x0001_0078, (int)ServerMessageType.GameRequestBlacklist);
    }

    private static byte[] Counted(byte[] name) => [.. BitConverter.GetBytes(name.Length), .. name];
}
