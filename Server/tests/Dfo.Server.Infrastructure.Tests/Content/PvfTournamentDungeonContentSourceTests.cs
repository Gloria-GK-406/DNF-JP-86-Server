using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfTournamentDungeonContentSourceTests
{
    [SkippableFact]
    public async Task Current_archive_reads_nested_result_matrix_and_the_owning_arena()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfTournamentDungeonContentSource(provider, new PvfDungeonMapDefinitionSource(provider));
        var definition = await source.GetAsync(120, default);
        Assert.Equal(17100, definition.MapId);
        Assert.Equal(69, definition.MinimumLevel);
        Assert.Equal(72, definition.BasisLevel);
        Assert.Equal(2, definition.Difficulty);
        Assert.Equal(2, definition.RoundFatigue);
        Assert.Equal(3, definition.CoinLimit);
        Assert.Equal(16, definition.GoldRate);
        Assert.Equal(new DungeonRequiredItem(4726, 1, true), Assert.Single(definition.RequiredItems));
        Assert.Equal(15, definition.Candidates.Count);
        Assert.All(definition.Candidates, value => { Assert.Equal(72, value.Level); Assert.False(value.IsAiCharacter); });
        Assert.Equal(new uint[] { 0, 30000, 50000, 100000, 200000 }, definition.ExperienceByRound);
        Assert.Equal(new TournamentResultCard(0, 0, 0, 100), definition.ResultCards[0]);
        Assert.Equal(new TournamentResultCard(4, 100, 0, 0), definition.ResultCards[4]);
        Assert.Equal(new TournamentResultCard(5, 0, 100, 0), definition.ResultCards[5]);
        Assert.Equal(new TournamentRewardItem[] { new(3329, 60, 1), new(3329, 35, 2), new(3329, 5, 3) }, definition.RewardItems);
        Assert.Same(definition, await source.GetAsync(120, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => source.GetAsync(121, default).AsTask());
    }
}
