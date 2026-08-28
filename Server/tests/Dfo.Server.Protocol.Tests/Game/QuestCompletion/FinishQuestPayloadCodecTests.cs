using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.QuestCompletion;

namespace Dfo.Server.Protocol.Tests.Game.QuestCompletion;

/// <summary>
/// The request the client sends to hand a quest in, and the answer it waits for.
/// </summary>
/// <remarks>
/// Both directions are pinned to one capture, taken on 2026-08-27 by running the reference server
/// against the real A21 client and finishing quest 1016 -- 与赛丽亚的初次见面 -- at its NPC. The
/// exchange is in <c>ServerS4A21/.../packet_log.upstream-finishquest-20260827.txt</c> and the
/// request half also appeared in this server's own log, where it went unanswered and was resent.
///
/// The capture is why these bytes are trustworthy where the reference server's own self-test is
/// not: that test hands its builder <c>Exp = 10</c> for this very quest and the wire says
/// <c>101</c>. The number in it was transcribed wrong, and the whole exp derivation was chased
/// down a false trail because of it.
/// </remarks>
public sealed class FinishQuestPayloadCodecTests
{
    /// <summary>The captured request, field by field past the echoed type.</summary>
    [Fact]
    public void Decode_ReadsTheCapturedRequest()
    {
        var request = FinishQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("2200F803FFFF0100FFFF"));

        Assert.Equal(1016, request.QuestId);
        Assert.Equal(0xFFFF, request.RewardSelection);
        Assert.False(request.HasRewardSelection);
        Assert.Equal(1, request.CompletionCount);
        Assert.Equal(ClientMessageType.GameFinishQuest, request.MessageType);
    }

    /// <summary>A quest that offered a choice carries the index instead of the absent marker.</summary>
    [Fact]
    public void Decode_ReadsARewardSelectionWhenOneWasMade()
    {
        var request = FinishQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("2200F80302000100FFFF"));

        Assert.Equal(2, request.RewardSelection);
        Assert.True(request.HasRewardSelection);
    }

    [Fact]
    public void Decode_RejectsABodyThatDoesNotEchoItsType()
    {
        Assert.Throws<InvalidDataException>(() => FinishQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("2100F803FFFF0100FFFF")));
    }

    /// <remarks>
    /// Three little-endian fields of the same width run together, so a body of the right length
    /// with the wrong contents reads as three plausible numbers. The closing 0xFFFF is what the
    /// reference server tests to tell this frame from that, and so does this.
    /// </remarks>
    [Fact]
    public void Decode_RejectsABodyWhoseSentinelIsMissing()
    {
        Assert.Throws<InvalidDataException>(() => FinishQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("2200F803FFFF01000000")));
    }

    [Theory]
    [InlineData("2200F803FFFF0100FF")]
    [InlineData("2200F803FFFF0100FFFF00")]
    [InlineData("2200F803")]
    public void Decode_RejectsABodyOfTheWrongLength(string body)
    {
        Assert.Throws<InvalidDataException>(() => FinishQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString(body)));
    }

    /// <summary>
    /// The captured answer, byte for byte: quest 1016, a met-npc objective, 101 experience, and
    /// the three items it hands over in the slots they landed in.
    /// </summary>
    [Fact]
    public void Encode_MatchesTheCapturedCompletion()
    {
        var body = FinishQuestPayloadCodec.Encode(
            new FinishQuestAcknowledgement(
                true,
                1016,
                0x04,
                101,
                [
                    new FinishedQuestItem(4, 10088609, 3),
                    new FinishedQuestItem(5, 10088610, 3),
                    new FinishedQuestItem(10, 100260012, 1),
                ],
                0x6A902D45,
                0));

        Assert.Equal(
            "01F8030465000000" + "00000000" + "00" + "03"
                + "0400A1F0990003000000" + "000000" + "452D906A" + "0000"
                + "0500A2F0990003000000" + "000000" + "452D906A" + "0000"
                + "0A00ACD8F90501000000" + "000000" + "452D906A" + "0000",
            Convert.ToHexString(body));
        Assert.Equal(71, body.Length);
    }

    /// <summary>A completion that hands nothing over is the fourteen-byte head alone.</summary>
    [Fact]
    public void Encode_WritesTheHeadAloneWhenNothingWasHandedOver()
    {
        var body = FinishQuestPayloadCodec.Encode(
            new FinishQuestAcknowledgement(true, 101, 0x04, 6932, [], 0x6A902D45, 0));

        Assert.Equal("0165000414" + "1B0000" + "00000000" + "00" + "00", Convert.ToHexString(body));
        Assert.Equal(14, body.Length);
    }

    /// <summary>A refusal is two bytes and says nothing but the code.</summary>
    [Fact]
    public void Encode_WritesARefusalAsAZeroAndACode()
    {
        var body = FinishQuestPayloadCodec.Encode(
            new FinishQuestAcknowledgement(false, 1016, 0, 0, [], 0, 22));

        Assert.Equal("0016", Convert.ToHexString(body));
    }

    /// <remarks>
    /// A refusal carries neither quest nor reward, so nothing on the message may reach the wire.
    /// Asserted because refusal and completion are one record: an edit that wrote the head
    /// unconditionally would pass every other test here.
    /// </remarks>
    [Fact]
    public void Encode_LeavesEverythingOutOfARefusal()
    {
        var refused = FinishQuestPayloadCodec.Encode(
            new FinishQuestAcknowledgement(
                false, 1016, 4, 101, [new FinishedQuestItem(4, 10088609, 3)], 0x6A902D45, 22));

        Assert.Equal(2, refused.Length);
    }
}
