using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class MirrorDungeonRuleService(IMirrorDungeonDefinitionSource source,
    IDungeonDefinitionSource dungeons, IQuestDefinitionSource quests,
    MirrorDungeonEntryTestSettings? entryTestSettings = null) : IMirrorDungeonRuleService
{
    public MirrorDungeonCatalog Catalog => source.Load();
    public MirrorDungeonEntryTestSettings EntryTestSettings => entryTestSettings ?? MirrorDungeonEntryTestSettings.Disabled;

    public async ValueTask<MirrorDungeonEntryRule?> ResolveAsync(int dungeonId, int questId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = Catalog;
        if (dungeonId == catalog.WrapperDungeonId
            || !catalog.Candidates.Contains(new(dungeonId, questId))) return null;
        var quest = await quests.GetAsync(questId, cancellationToken).ConfigureAwait(false);
        if (quest?.Grade != QuestGrade.Crack
            || !quest.Rewards.Any(x => x.Kind == QuestRewardKind.CrackOfDimension)) return null;
        var dungeon = await dungeons.GetAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        if (dungeon is null || dungeon.EntryMode != DungeonEntryMode.Maze
            || !dungeon.Mazes.Any(x => x.QuestConnection is null && dungeon.QuestConnection is null)) return null;
        // Upstream mirror entry ignores the character's historical-map unlock ledger, but
        // still uses only difficulties the selected historical DGN actually declares.
        var tiers = dungeon.SupportedDifficulties;
        if (tiers is not { Count: > 0 } || tiers.Any(x => !Enum.IsDefined((DungeonDifficulty)x)))
            throw new InvalidDataException($"Mirror historical dungeon {dungeonId} has no valid difficulty tiers.");
        return new(catalog, (DungeonDifficulty)tiers.Max());
    }
}
