using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Application.Tests.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Pvp;

public sealed class PvpRoomRuleServiceTests
{
    [SkippableFact]
    public void Map_indices_are_bounded_by_the_archive_map_order()
    {
        var rules = Archived();

        Assert.All(Enumerable.Range(0, 6), index => Assert.True(rules.AcceptsMap(index)));
        Assert.False(rules.AcceptsMap(-1));
        Assert.False(rules.AcceptsMap(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.StartMap(6, 1));
    }

    [SkippableFact]
    public void A_chosen_map_is_played_as_chosen()
    {
        Assert.Equal(3, Archived().StartMap(3, seed: 7));
    }

    [SkippableFact]
    public void The_random_choice_draws_a_real_map_repeatably_from_its_seed()
    {
        var rules = Archived();
        var drawn = Enumerable.Range(0, 200).Select(seed => rules.StartMap(0, seed)).ToArray();

        Assert.Equal(Enumerable.Range(1, 5).Select(x => (byte)x), drawn.Distinct().Order());
        Assert.Equal(drawn, Enumerable.Range(0, 200).Select(seed => rules.StartMap(0, seed)));
    }

    private static PvpRoomRuleService Archived()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        return new(Archive.Value.GetRequiredService<IPvpMapDefinitionSource>());
    }

    private static readonly Lazy<ServiceProvider> Archive = new(ContentArchive.CreateContentProvider);
}
