namespace Dfo.Server.Abstractions.World;

/// <summary>
/// When the dungeon-selection screen's state is due.
/// </summary>
/// <remarks>
/// The reference server asks two things: is the character still level one, and has the tutorial
/// been marked completed. A completed level-one character receives normal selection state.
/// <para>
/// Two frames turn on this and they are built in different slices: the screen's own burst and the
/// answer to entering a dungeon. It lived in one of those slices and was read from the other, which
/// the slice rules do not allow and which held only because the two agreed by accident of being one
/// call.
/// </para>
/// </remarks>
public interface IDungeonSelectionRuleService
{
    /// <summary>Whether the selection state is deferred to the character's first entry.</summary>
    bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false);
}
