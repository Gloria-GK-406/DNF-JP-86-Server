using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class CycleDungeonContentTests
{
    [SkippableFact]
    public async Task The_observed_cycle_pair_requires_region_unlock_and_its_own_quest_prerequisite()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var quests = new QuestContentRuleService(provider.GetRequiredService<IQuestDefinitionSource>(),
            provider.GetRequiredService<IQuestParameterSource>());
        var offers = new WorldContentEntryRuleService(quests, provider.GetRequiredService<IDailyActivityDefinitionSource>(),
            provider.GetRequiredService<ICollectionBoxDefinitionSource>(), provider.GetRequiredService<ICreatureDefinitionSource>());
        var rules = new CycleDungeonRuleService(provider.GetRequiredService<ICycleDungeonDefinitionSource>(), quests, offers);
        var eligible = new WorldContentSubject(88, 12, 0, new Dictionary<int, int> { [2228] = 1, [2211] = 1 },
            [15203], [], [], 0, 0);
        Assert.NotNull(await rules.EligibleAsync(84, 15203, eligible, default));
        Assert.Null(await rules.EligibleAsync(85, 15203, eligible, default));
        Assert.Null(await rules.EligibleAsync(84, 15203, eligible with { Level = 53 }, default));
        Assert.Null(await rules.EligibleAsync(84, 15203, eligible with { ClearedQuestFlags = new Dictionary<int, int> { [2211] = 1 } }, default));
        Assert.Null(await rules.EligibleAsync(84, 15203, eligible with { ClearedQuestFlags = new Dictionary<int, int> { [2228] = 1 } }, default));
        Assert.NotNull(await rules.EligibleAsync(84, 15203, eligible with
            { ClearedQuestFlags = new Dictionary<int, int> { [2228] = 1, [2211] = 1, [15203] = 1 } }, default));
    }
}
