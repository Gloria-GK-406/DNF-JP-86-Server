using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class CharacterGrowthVisibilityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(24)]
    [InlineData(255)]
    public void Public_appearance_preserves_visibility_flags_at_the_A21_tail_field(byte flags)
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var entry = source.CharacterEntry.Public;
        var disabled = StateSynchronizationPayloadCodec.EncodeCurrentUserState(
            source.Character, entry with { VisibilityBits = 0 }, [], []);
        var enabled = StateSynchronizationPayloadCodec.EncodeCurrentUserState(
            source.Character, entry with { VisibilityBits = flags }, [], []);

        // USERINFO0's final 64-byte block: byte 47 is the same visibility state as NOTI0165.
        Assert.Equal(flags, enabled[^17]);
        Assert.Equal(disabled.Length, enabled.Length);
        Assert.Equal(disabled[..^17], enabled[..^17]);
        Assert.Equal(disabled[^16..], enabled[^16..]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void Public_appearance_rejects_visibility_that_cannot_fit_the_wire(long flags)
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        Assert.Throws<OverflowException>(() => StateSynchronizationPayloadCodec.EncodeCurrentUserState(
            source.Character, source.CharacterEntry.Public with { VisibilityBits = flags }, [], []));
    }
}
