using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class BlackDiamondNameIconTests
{
    [Fact]
    public void BlackDiamond_changes_only_the_name_marker_not_pc_room_or_packet_shape()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var publicState = source.CharacterEntry.Public;
        var disabled = StateSynchronizationPayloadCodec.EncodeCurrentUserState(
            source.Character, publicState with { HasBlackDiamond = false }, [], []);
        var enabled = StateSynchronizationPayloadCodec.EncodeCurrentUserState(
            source.Character, publicState with { HasBlackDiamond = true }, [], []);

        Assert.Equal(disabled.Length, enabled.Length);
        Assert.Equal(0, disabled[^63]);
        Assert.Equal(1, enabled[^63]);
        Assert.Equal(0, enabled[^64]); // Separate PC-room flag must stay off.
        Assert.Equal([disabled.Length - 63], Enumerable.Range(0, disabled.Length)
            .Where(index => disabled[index] != enabled[index]).ToArray());
    }
}
