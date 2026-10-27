using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.QuestProgress;

namespace Dfo.Server.Protocol.Tests.Game.QuestProgress;

/// <summary>
/// The report the client sends when a quest moves, and the answer it waits for.
/// </summary>
/// <remarks>
/// The request is pinned to bytes that were observed: <c>21 00 F8 03 00 00</c>, from this server's
/// own log on 2026-08-27, sent five times over while nothing answered it. The player had clicked
/// 赛丽亚 and quest 1016 -- 与赛丽亚的初次见面 -- was reporting one fewer on the whole word.
///
/// <b>The answer is not pinned to a capture, because no capture of it exists.</b> Its shape comes
/// from the reference server's <c>QuestAckBuilder.BuildSetTrigger</c>, which writes a 0x01, the
/// quest and the trigger word; every reference session on file ends before a quest was taken to an
/// NPC, so none of them carries this frame in either direction. What the expectations below assert
/// is that this codec writes what that builder writes.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SetQuestTriggerPayloadCodecTests
{
    /// <summary>The observed report, read field by field past the echoed type.</summary>
    [Fact]
    public void Decode_ReadsTheObservedReport()
    {
        var request = SetQuestTriggerPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("2100F8030000"));

        Assert.Equal(1016, request.QuestId);
        Assert.Equal(0, request.Selector);
        Assert.False(request.Increment);
        Assert.Equal(ClientMessageType.GameSetQuestTrigger, request.MessageType);
    }

    /// <summary>Any non-zero last byte is "one more"; the reference server tests it the same way.</summary>
    [Theory]
    [InlineData("2100F8032001", 1016, 0x20, true)]
    [InlineData("2100F8032002", 1016, 0x20, true)]
    [InlineData("210065000100", 101, 0x01, false)]
    public void Decode_ReadsTheSelectorAndDirection(
        string body, int questId, byte selector, bool increment)
    {
        var request = SetQuestTriggerPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString(body));

        Assert.Equal(questId, request.QuestId);
        Assert.Equal(selector, request.Selector);
        Assert.Equal(increment, request.Increment);
    }

    /// <remarks>
    /// The reference server reads the direction only when a fourth byte is there and treats its
    /// absence as "one fewer". Followed rather than tightened to the six bytes seen live: a
    /// five-byte body is one it would accept, and refusing it here would be a difference invented
    /// rather than found.
    /// </remarks>
    [Fact]
    public void Decode_ReadsAReportWithNoDirectionByteAsOneFewer()
    {
        var request = SetQuestTriggerPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("2100F80300"));

        Assert.Equal(1016, request.QuestId);
        Assert.False(request.Increment);
    }

    /// <remarks>
    /// Refused rather than skipped. A body that does not open with its own type is one this codec
    /// has not understood, and reading past it would take the two bytes after as the quest.
    /// </remarks>
    [Fact]
    public void Decode_RejectsABodyThatDoesNotEchoItsType()
    {
        Assert.Throws<InvalidDataException>(() => SetQuestTriggerPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("2000F8030000")));
    }

    [Theory]
    [InlineData("2100F803")]
    [InlineData("2100")]
    [InlineData("")]
    public void Decode_RejectsABodyTooShortToCarryASelector(string body)
    {
        Assert.Throws<InvalidDataException>(() => SetQuestTriggerPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString(body)));
    }

    /// <summary>Quest 1016 at trigger 0, which is its objective met.</summary>
    [Fact]
    public void Encode_WritesTheQuestAndItsWholeTriggerWord()
    {
        var body = SetQuestTriggerPayloadCodec.Encode(
            new SetQuestTriggerAcknowledgement(true, 1016, 0, 0));

        Assert.Equal("01F8030000 0000".Replace(" ", ""), Convert.ToHexString(body));
    }

    /// <summary>The word is four bytes and little-endian, like every other trigger on this wire.</summary>
    [Fact]
    public void Encode_WritesTheTriggerAsFourLittleEndianBytes()
    {
        var body = SetQuestTriggerPayloadCodec.Encode(
            new SetQuestTriggerAcknowledgement(true, 101, 0x0002_0001, 0));

        Assert.Equal("0165000100 0200".Replace(" ", ""), Convert.ToHexString(body));
    }

    /// <summary>A refusal is two bytes and says nothing but the code.</summary>
    [Fact]
    public void Encode_WritesARefusalAsAZeroAndACode()
    {
        var body = SetQuestTriggerPayloadCodec.Encode(
            new SetQuestTriggerAcknowledgement(false, 1016, 0, 22));

        Assert.Equal("0016", Convert.ToHexString(body));
    }

    /// <remarks>
    /// A refusal carries no quest, so the quest on the message must not reach the wire. Asserted
    /// because the refusal and the answer are one record: a future edit that wrote the quest
    /// unconditionally would pass every other test here.
    /// </remarks>
    [Fact]
    public void Encode_LeavesTheQuestOutOfARefusal()
    {
        var refused = SetQuestTriggerPayloadCodec.Encode(
            new SetQuestTriggerAcknowledgement(false, 1016, 5, 22));

        Assert.Equal(2, refused.Length);
    }
}
