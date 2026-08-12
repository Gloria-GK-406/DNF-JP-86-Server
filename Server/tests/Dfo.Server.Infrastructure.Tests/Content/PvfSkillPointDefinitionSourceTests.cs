using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfSkillPointDefinitionSourceTests
{
    [SkippableFact]
    public async Task Real_archive_projects_totals_free_grants_and_learned_skill_costs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfSkillPointDefinitionSource(provider);

        var result = await source.GetAsync(0, 1, 86, [1], CancellationToken.None);

        Assert.Equal(11010, result.TotalSp);
        Assert.Equal(37, result.TotalTp);
        Assert.InRange(result.FreeGrants.Count, 16, 16);
        Assert.Equal(new SkillPointGrantDefinition(5, 1), result.FreeGrants[0]);
        Assert.True(result.Skills.TryGetValue(1, out var skill));
        Assert.Equal([20], skill!.Costs);
    }
}
