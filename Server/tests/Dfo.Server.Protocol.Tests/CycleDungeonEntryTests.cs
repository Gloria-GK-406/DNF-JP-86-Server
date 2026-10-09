using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CycleDungeonEntry;

namespace Dfo.Server.Protocol.Tests;

public sealed class CycleDungeonEntryTests
{
    [Fact]
    public void Captured_request_and_native_acceptance_continuation_have_the_expected_operands()
    {
        var request = PrepareCycleDungeonEntryCodec.Decode(GameMessageMetadata.Empty,
            Convert.FromHexString("54000000633B0000"));
        Assert.True(request.Valid);
        Assert.Equal(84, request.DungeonId);
        Assert.Equal(15203, request.QuestId);
        Assert.NotEqual(ClientMessageType.GameGetOtherCharacterDetail, request.MessageType);
        Assert.Equal(Convert.FromHexString("0101000000633B0000"),
            PrepareCycleDungeonEntryCodec.Encode(new(true, request.QuestId)));
        Assert.Equal(new byte[] { 0 }, PrepareCycleDungeonEntryCodec.Encode(new(false)));
        Assert.False(PrepareCycleDungeonEntryCodec.Decode(GameMessageMetadata.Empty, new byte[7]).Valid);
    }
}
