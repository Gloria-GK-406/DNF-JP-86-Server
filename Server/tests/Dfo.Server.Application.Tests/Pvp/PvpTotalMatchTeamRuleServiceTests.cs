using System.Text;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Pvp;

[Trait("Category", "Unit")]
public sealed class PvpTotalMatchTeamRuleServiceTests
{
    private readonly PvpTotalMatchTeamRuleService rules = new();

    [Fact]
    public void Awakening_does_not_make_the_same_advancement_a_distinct_team_member()
    {
        CharacterSummary[] roster = [Member(10, 0x11), Member(20, 0x21), Member(30, 0x22)];
        Assert.Null(rules.SelectMembers([0, 1, 2], roster, new(10)));

        roster[1] = roster[1] with { Job = 1 };
        Assert.Equal([new CharacterId(30), new(10), new(20)],
            rules.SelectMembers([2, 0, 1], roster, new(10))!.Select(member => member.CharacterId));
    }

    [Fact]
    public void Team_names_use_a_character_limit_and_retain_the_original_spelling()
    {
        var name = new string('队', 12);
        Assert.Equal(name, rules.ReadName(Encoding.UTF8.GetBytes(name)));
        Assert.Null(rules.ReadName(Encoding.UTF8.GetBytes(name + '友')));
        Assert.Null(rules.ReadName([0xFF]));
        Assert.Equal("FirstTeam", rules.NameToSave("firstteam", "FirstTeam"));
        Assert.Null(rules.NameToSave("OtherTeam", "FirstTeam"));
    }

    private static CharacterSummary Member(int id, byte growType) =>
        new(new(id), 0, [], 0, growType, 86, 0, null);
}
