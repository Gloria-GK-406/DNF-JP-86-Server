using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>When the dungeon-selection screen's state is due.</summary>
internal sealed class DungeonSelectionRuleService : IDungeonSelectionRuleService
{
    /// <summary>The level a character is at until the tutorial's own step raises it.</summary>
    private const byte TutorialLevel = 1;

    public bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false) => characterLevel <= TutorialLevel && !tutorialCompleted;
}
