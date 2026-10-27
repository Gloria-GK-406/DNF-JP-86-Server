using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

[Trait("Category", "Unit")]
public sealed class DarkElfRuinsRuleTests
{
    [Fact]
    public void Ruins_kind_owns_special_results_and_revival_without_ordinary_cards_difficulty_or_retry()
    {
        var mode = new DungeonRunMode.DarkElfRuins(new(new(2026, 10, 29), false, 1));
        Assert.Equal(DungeonRunKind.DarkElfRuins, mode.Kind);
        Assert.True(mode.Capabilities.EndsOnBossClear);
        Assert.False(mode.Capabilities.DrawsClearCards);
        Assert.False(mode.Capabilities.UnlocksDifficulty);
        Assert.False(mode.Capabilities.AllowsOrdinaryRechallenge);
        Assert.Equal(DungeonRevivalPolicy.DarkElfRuinsLife, mode.Capabilities.Revival);
    }
    [Fact]
    public void Guardian_equipment_distribution_keeps_the_empty_tail_instead_of_forcing_a_drop()
    {
        var rules = new DarkElfRuinsRuleService(new Content(), new Dungeons());
        var tier = new DarkElfRuinsTierReward(3, [], [new(100, 800), new(101, 5600)]);
        var draws = Enumerable.Range(0, 10000).Select(ticket => rules.GuardianDrop(tier, ticket)).ToArray();
        Assert.Equal(800, draws.Count(x => x?.ItemId == 100));
        Assert.Equal(5600, draws.Count(x => x?.ItemId == 101));
        Assert.Equal(3600, draws.Count(x => x is null));
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.GuardianDrop(tier, 10000));
    }
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 7)]
    public void Shared_tier_needs_the_completed_count_then_a_separate_no_revive_clear(int license, int required)
    {
        var rules = new DarkElfRuinsRuleService(new Content(), new Dungeons());
        var progress = new DarkElfRuinsProgress(License: license);
        for (var i = 0; i < required; i++)
        {
            progress = rules.Advance(progress, license, false);
            Assert.Equal(license, progress.License);
        }
        Assert.Equal(required, progress.Clears);
        progress = rules.Advance(progress, license, true);
        Assert.Equal(required, progress.Clears);
        Assert.Equal(license, progress.License);
        Assert.Equal(progress, rules.Advance(progress, license - 1, false));
        var advanced = rules.Advance(progress, license, false);
        Assert.Equal(license + 1, advanced.License);
        Assert.Equal(0, advanced.Clears);
    }

    [Fact]
    public void Early_clears_can_use_coins_and_do_not_require_a_consecutive_streak()
    {
        var rules = new DarkElfRuinsRuleService(new Content(), new Dungeons());
        var progress = new DarkElfRuinsProgress(License: 2);
        progress = rules.Advance(progress, 2, false);
        progress = rules.Advance(progress, 2, true);
        progress = rules.Advance(progress, 2, false);
        Assert.Equal(3, progress.Clears);
        Assert.Equal(3, rules.Advance(progress, 2, false).License);
        var maximum = new DarkElfRuinsProgress(License: 4);
        Assert.Equal(maximum, rules.Advance(maximum, 4, false));
    }

    private sealed class Content : IDarkElfRuinsContentSource
    { public DarkElfRuinsCatalog Load() => throw new NotSupportedException("Progression does not read content."); }
    private sealed class Dungeons : IDungeonDefinitionSource
    { public ValueTask<DungeonDefinition?> GetAsync(int dungeonId, CancellationToken ct) => throw new NotSupportedException(); }
}
