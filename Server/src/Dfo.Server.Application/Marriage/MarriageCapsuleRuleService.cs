using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageCapsuleRuleService(IMarriageCapsuleContentSource content, IDungeonDefinitionSource dungeons)
    : IMarriageCapsuleRuleService
{
    public MarriageCapsuleRules Rules() => content.Load();

    public async ValueTask<bool> ChargesOnClearAsync(int dungeonId, int level, CancellationToken ct) =>
        await dungeons.GetAsync(dungeonId, ct).ConfigureAwait(false) is { } dungeon
        && DungeonRecommendation.IsRecommended(dungeon, level);
}
