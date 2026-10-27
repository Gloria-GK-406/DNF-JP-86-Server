using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.MirrorDungeonEntry;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

[Trait("Category", "Unit")]
public sealed class MirrorDungeonEntryCodecTests
{
    [Fact]
    public void Captured_request_preserves_the_client_pair_and_routes_to_mirror()
    {
        var request = Assert.IsType<SelectDungeonRequest>(new MirrorDungeonEntryDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("20000000DB370000")));
        Assert.Equal(32, request.DungeonId);
        Assert.Equal(14299, request.MirrorQuestId);
        Assert.Equal(ClientMessageType.GameMirrorDungeonEntry, request.MessageType);
        Assert.Equal(0, request.RequestMode);
        Assert.Equal(0, request.PracticeMode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    public void Malformed_requests_reach_explicit_refusal_without_becoming_ordinary_entry(int size)
    {
        var request = Assert.IsType<SelectDungeonRequest>(new MirrorDungeonEntryDecoder().Decode(GameMessageMetadata.Empty, new byte[size]));
        Assert.Equal(0, request.DungeonId);
        Assert.Equal(0, request.MirrorQuestId);
        Assert.Equal(ClientMessageType.GameMirrorDungeonEntry, request.MessageType);
    }

    [Fact]
    public void Mission_push_is_a_four_byte_quest_id_and_never_an_entry_ack()
    {
        var frame = new MirrorDungeonMissionEncoder().Encode(new MirrorDungeonMissionNotification(14299)).Single().ToArray();
        Assert.Equal(0, frame[0]);
        Assert.Equal(0x0278, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(Convert.FromHexString("DB370000"), frame[15..]);
    }

    [Theory]
    [InlineData(DungeonEntryFailure.InsufficientFatigue, 1)]
    [InlineData(DungeonEntryFailure.DailyEntryLimitReached, 1)]
    [InlineData(DungeonEntryFailure.ConfigurationInvalid, 1)]
    [InlineData(DungeonEntryFailure.MissingPrerequisite, 0x13)]
    [InlineData(DungeonEntryFailure.AlreadyInDungeon, 0x13)]
    public void Mirror_refusals_use_the_native_mirror_error_switch(DungeonEntryFailure failure, byte expected)
    {
        var code = DungeonEntryRefusalCodes.ForMirror(failure);
        Assert.Equal(expected, code);
        Assert.NotEqual(0x12, code); // Native automatic re-roll/retry.
        Assert.NotEqual(0x16, code); // Native member error, not fatigue.
        var frame = new SelectDungeonRefusalEncoder().Encode(new SelectDungeonRefusal(code, 0, true)).Single().ToArray();
        Assert.Equal(1, frame[0]);
        Assert.Equal(0x02B6, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(new byte[] { 0, expected, 0 }, frame[15..]);
    }
}
