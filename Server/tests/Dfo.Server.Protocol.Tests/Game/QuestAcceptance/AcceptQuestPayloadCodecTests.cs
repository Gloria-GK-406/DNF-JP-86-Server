using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.QuestAcceptance;

namespace Dfo.Server.Protocol.Tests.Game.QuestAcceptance;

/// <summary>
/// The request the client sends to take a quest on, and the answer it waits for.
/// </summary>
/// <remarks>
/// Both directions are pinned to bytes that were observed rather than derived. The two requests
/// are from this server's own log on 2026-08-27, when a player accepted two quests and nothing
/// answered; the answer is the reference server's, from its capture of the same exchange.
/// </remarks>
public sealed class AcceptQuestPayloadCodecTests
{
    /// <summary>The first two bytes repeat the packet's type and are not a field.</summary>
    [Theory]
    [InlineData("1F00F803", 1016)]
    [InlineData("1F006500", 101)]
    public void Decode_ReadsTheQuestPastTheEchoedType(string body, int questId)
    {
        var request = AcceptQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString(body));

        Assert.Equal(questId, request.QuestId);
        Assert.Equal(ClientMessageType.GameAcceptQuest, request.MessageType);
    }

    /// <remarks>
    /// Refused rather than skipped. A body that does not open with its own type is one this codec
    /// has not understood, and reading past it would take the two bytes after as the quest.
    /// </remarks>
    [Fact]
    public void Decode_RejectsABodyThatDoesNotEchoItsType()
    {
        Assert.Throws<InvalidDataException>(() => AcceptQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("2000F803")));
    }

    [Theory]
    [InlineData("1F00F8")]
    [InlineData("1F00F80300")]
    public void Decode_RejectsABodyOfTheWrongLength(string body)
    {
        Assert.Throws<InvalidDataException>(() => AcceptQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString(body)));
    }

    /// <summary>The captured answer, byte for byte: quest 1016, trigger 1, no items.</summary>
    [Fact]
    public void Encode_MatchesTheCapturedAcceptance()
    {
        var body = AcceptQuestPayloadCodec.Encode(
            new AcceptQuestAcknowledgement(true, 1016, 1, [], 0));

        Assert.Equal(Convert.FromHexString("01F8030100000000"), body);
    }

    /// <remarks>
    /// The count is a byte and each entry is ten, so an acceptance that hands two items over is
    /// twenty-eight bytes. Pinned because the slot is what tells the client where to draw the
    /// item, and a shifted field would draw it somewhere else.
    /// </remarks>
    [Fact]
    public void Encode_LaysTheHandedOverItemsOutAfterTheCount()
    {
        var body = AcceptQuestPayloadCodec.Encode(
            new AcceptQuestAcknowledgement(
                true,
                0x0102,
                0x0000_0201,
                [new AcceptedQuestItem(7, 3040011, 2), new AcceptedQuestItem(9, 3040012, 1)],
                0));

        Assert.Equal(
            Convert.FromHexString(
                "01" + "0201" + "01020000" + "02"
                + "0700" + "0B632E00" + "02000000"
                + "0900" + "0C632E00" + "01000000"),
            body);
        Assert.Equal(28, body.Length);
    }

    /// <summary>A refusal is two bytes and carries no quest at all.</summary>
    [Fact]
    public void Encode_AnswersARefusalWithItsReasonAlone()
    {
        var body = AcceptQuestPayloadCodec.Encode(
            new AcceptQuestAcknowledgement(
                false, 1016, 0, [], (byte)QuestAcceptRefusal.AlreadyActive));

        Assert.Equal(new byte[] { 0x00, 18 }, body);
    }
}
