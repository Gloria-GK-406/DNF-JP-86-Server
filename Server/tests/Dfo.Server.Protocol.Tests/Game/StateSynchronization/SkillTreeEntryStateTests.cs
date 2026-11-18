using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class SkillTreeEntryStateTests
{
    [Theory]
    [InlineData(-1, 0xFF)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void Userinfo_subtype_zero_reports_the_locked_or_active_skill_page(int treeIndex, int expected)
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var entry = source.CharacterEntry.Public;
        var locked = StateSynchronizationPayloadCodec.EncodeCurrentUserState(
            source.Character, entry with { Extras = entry.Extras with { SkillTreeIndex = -1 } }, [], []);
        var body = StateSynchronizationPayloadCodec.EncodeCurrentUserState(
            source.Character, entry with { Extras = entry.Extras with { SkillTreeIndex = treeIndex } }, [], []);

        // The skill tree index is at byte 61 of the 64-byte tail.
        Assert.Equal(expected, body[^3]);
        Assert.Equal(locked[..^3], body[..^3]);
        Assert.Equal(locked[^2..], body[^2..]);
    }
}
