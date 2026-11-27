using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildMonthlyContributionRuleService(IGuildEconomySource content, IDungeonDefinitionSource dungeons)
{
    public GuildMonthlyContributionRule? ReadMonthlyContribution() => content.ReadMonthlyContribution();

    public async ValueTask<bool> IsRecommendedAsync(int dungeonId, int level, CancellationToken ct) =>
        await dungeons.GetAsync(dungeonId, ct).ConfigureAwait(false) is { } dungeon
        && DungeonRecommendation.IsRecommended(dungeon, level);
}
