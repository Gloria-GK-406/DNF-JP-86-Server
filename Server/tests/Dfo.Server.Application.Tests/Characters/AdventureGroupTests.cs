using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// The adventure group (冒险团): points, level and clear bonus worked out from an account's
/// characters, against the A21 <c>charactermanage.etc</c> tables.
/// </summary>
/// <remarks>
/// Expected values come from the threshold rule, not from any worked example: points are the sum
/// of each character's <c>[point bonus]</c> up to its level, capped at the tenth threshold.
/// </remarks>
public sealed class AdventureGroupTests
{
    private static readonly AdventureGroupRuleService Rules = new(new A21Table());

    [Theory]
    [InlineData(1, 0)]
    [InlineData(39, 0)]
    [InlineData(40, 100)]
    [InlineData(50, 1375)]
    [InlineData(70, 5425)]
    [InlineData(86, 10785)]
    public void A_character_contributes_the_bonus_of_every_level_up_to_its_own(int level, int points) =>
        Assert.Equal(points, Rules.Evaluate([level]).Standing.Point);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1374, 0)]
    [InlineData(1375, 1)]
    [InlineData(10849, 2)]
    [InlineData(10850, 3)]
    [InlineData(21570, 3)]
    [InlineData(21699, 3)]
    [InlineData(21700, 4)]
    [InlineData(175349, 9)]
    [InlineData(175350, 10)]
    public void The_level_is_the_highest_threshold_the_points_reach(int points, int level) =>
        Assert.Equal(level, LevelFor(points));

    [Fact]
    public void Two_level_86_characters_make_level_3()
    {
        var evaluation = Rules.Evaluate([86, 86]);

        Assert.Equal(new AdventureGroupStanding(3, 21570), evaluation.Standing);
        Assert.Equal(86, evaluation.HighestCharacterLevel);
    }

    [Fact]
    public void Points_past_the_top_threshold_are_capped_and_the_level_stops_at_ten()
    {
        // Twenty level-100 characters: far past 175350.
        var standing = Rules.Evaluate(Enumerable.Repeat(100, 20)).Standing;

        Assert.Equal(new AdventureGroupStanding(10, 175350), standing);
    }

    [Fact]
    public void Every_level_gives_ten_percent_and_level_zero_gives_nothing()
    {
        Assert.Equal(0, Rules.ClearExperienceRate(new(new(0, 0), 86), 50));
        for (byte level = 1; level <= 10; level++)
            Assert.Equal(0.10, Rules.ClearExperienceRate(new(new(level, 1), 86), 50), 10);
    }

    [Theory]
    [InlineData(60, 0.10)]
    [InlineData(86, 0)]
    [InlineData(90, 0)]
    public void Only_a_character_below_the_highest_gains_the_clear_bonus(int characterLevel, double rate) =>
        Assert.Equal(rate, Rules.ClearExperienceRate(new(new(3, 21570), 86), characterLevel), 10);

    [Fact]
    public async Task The_owner_counts_only_the_accounts_characters_that_remain()
    {
        var roster = new Roster((1, 86), (2, 86), (3, 30));
        var service = new AdventureGroupDomainService(roster.Table, Rules);

        Assert.Equal(new AdventureGroupStanding(3, 21570), await service.ReadAsync(Roster.Account, default));

        roster.Delete(2);
        Assert.Equal(new AdventureGroupStanding(2, 10785), await service.ReadAsync(Roster.Account, default));

        roster.Add(4, 39);
        Assert.Equal(new AdventureGroupStanding(2, 10785), await service.ReadAsync(Roster.Account, default));

        roster.SetLevel(4, 86);
        Assert.Equal(new AdventureGroupStanding(3, 21570), await service.ReadAsync(Roster.Account, default));
    }

    [Fact]
    public async Task The_clear_bonus_skips_the_highest_and_its_equals()
    {
        var roster = new Roster((1, 86), (2, 86), (3, 60), (4, 90));
        var service = new AdventureGroupDomainService(roster.Table, Rules);

        Assert.Equal(0.10, await service.ClearExperienceRateAsync(Roster.Account, new(3), default), 10);
        Assert.Equal(0.10, await service.ClearExperienceRateAsync(Roster.Account, new(1), default), 10);
        Assert.Equal(0, await service.ClearExperienceRateAsync(Roster.Account, new(4), default));

        roster.Delete(4);
        Assert.Equal(0, await service.ClearExperienceRateAsync(Roster.Account, new(1), default));
        Assert.Equal(0, await service.ClearExperienceRateAsync(Roster.Account, new(2), default));
    }

    [Fact]
    public async Task A_single_level_change_rises_by_the_levels_it_crossed()
    {
        // 86 + 85 = 10785 + 10375 = 21160: level 3. At 86 + 86 = 21570 still level 3.
        var roster = new Roster((1, 86), (2, 86));
        var service = new AdventureGroupDomainService(roster.Table, Rules);

        Assert.Null(await service.LevelRiseAsync(new(new(2), 85, 86), default));

        // One character from 1 to 86 next to an 86: 10785 (level 2) to 21570 (level 3).
        Assert.Equal(new AdventureGroupLevelRise(Roster.Account, 2, 3),
            await service.LevelRiseAsync(new(new(2), 1, 86), default));

        // Next to a 70 (5425, level 2), a character from 1 to 100 adds 17575: 23000 is level 4,
        // two levels in one change.
        roster.SetLevel(1, 70);
        var rise = await service.LevelRiseAsync(new(new(2), 1, 100), default);
        Assert.Equal(new AdventureGroupLevelRise(Roster.Account, 2, 4), rise);
        Assert.Equal(2, rise!.Levels);
    }

    [Fact]
    public async Task A_fall_or_a_deleted_character_raises_nothing()
    {
        var roster = new Roster((1, 86), (2, 86));
        var service = new AdventureGroupDomainService(roster.Table, Rules);

        Assert.Null(await service.LevelRiseAsync(new(new(2), 86, 1), default));
        roster.Delete(2);
        Assert.Null(await service.LevelRiseAsync(new(new(2), 1, 86), default));
    }

    private static int LevelFor(int points)
    {
        // One character whose single level is worth exactly these points.
        var rules = new AdventureGroupRuleService(new A21Table(flatPoints: points));
        return rules.Evaluate(points == 0 ? [] : [1]).Standing.Level;
    }

    /// <summary>The A21 tables, or the same thresholds with level one worth a chosen number of points.</summary>
    private sealed class A21Table(int? flatPoints = null) : ICharacterManageContentSource
    {
        public CharacterManageTable Table { get; } = flatPoints is { } points
            ? new AdventureGroupTestContent().Table with { PointBands = [new CharacterManagePointBand(1, 1, points)] }
            : new AdventureGroupTestContent().Table;
    }

    /// <summary>One account's characters that have not been deleted, as the character table returns them.</summary>
    private sealed class Roster
    {
        public static readonly AccountId Account = new(7);

        private readonly Dictionary<int, int> levels = [];

        public Roster(params (int Id, int Level)[] characters)
        {
            foreach (var (id, level) in characters)
                levels[id] = level;
            Table = TestRequired.Absent<ICharacterTable>((method, arguments) => method.Name switch
            {
                nameof(ICharacterTable.ReadByAccountAsync) => ValueTask.FromResult<IReadOnlyList<CharacterRosterRow>>(
                    (AccountId)arguments![0]! == Account
                        ? levels.OrderBy(pair => pair.Key)
                            .Select((pair, slot) => new CharacterRosterRow(pair.Key, slot, pair.Value)).ToArray()
                        : []),
                nameof(ICharacterTable.FindOwnerAsync) => ValueTask.FromResult<AccountId?>(
                    levels.ContainsKey(((CharacterId)arguments![0]!).Value) ? Account : null),
                _ => null,
            });
        }

        public ICharacterTable Table { get; }

        public void Delete(int id) => levels.Remove(id);

        public void Add(int id, int level) => levels[id] = level;

        public void SetLevel(int id, int level) => levels[id] = level;
    }
}
