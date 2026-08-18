namespace Dfo.Server.Abstractions.Content;

/// <summary>One cell of a maze grid.</summary>
public readonly record struct MazeCell(byte X, byte Y);

/// <summary>
/// One maze a dungeon can be run as: its grid, the rooms on it, and where a run may start and
/// where its boss may stand.
/// </summary>
/// <param name="Index">
/// Position in the dungeon's declared order. This is the number the client is told, so it must
/// stay tied to the archive's order rather than to any sorting we find convenient.
/// </param>
/// <param name="Rooms">
/// The cells that carry a room, and the map each resolves to. A grid cell with no entry is not a
/// room; the grid is larger than the maze that occupies it.
/// </param>
/// <param name="StartCandidates">
/// The cells a run may start on. The archive lists candidates rather than one cell, so which is
/// used is a choice the layout makes from its seed rather than a fact of the content.
/// </param>
public sealed record MazeDefinition(
    int Index,
    byte Width,
    byte Height,
    IReadOnlyDictionary<MazeCell, int> Rooms,
    IReadOnlyList<MazeCell> StartCandidates,
    IReadOnlyList<MazeCell> BossCandidates);

/// <summary>
/// A dungeon as the archive declares it.
/// </summary>
/// <param name="Mazes">
/// Every maze declared, in archive order. Most dungeons declare one; the ones that declare
/// several expect the server to choose, which is what makes maze choice a rule rather than a
/// lookup.
/// </param>
/// <remarks>
/// Deliberately not carrying a dungeon "type". The archive declares no such thing: only 284 of 839
/// dungeons carry a <c>[dungeon type]</c> tag at all and it marks exception families rather than
/// the ordinary case, so a type enum here would model something that does not exist. What does
/// divide dungeons is structural -- whether they have a maze -- and that shows up as this record
/// being unavailable rather than as a field on it. See Docs/DungeonEntryNotes.md.
/// </remarks>
public sealed record DungeonDefinition(
    int DungeonId,
    string ArchivePath,
    byte MinimumRequiredLevel,
    bool ConsumesFatigue,
    IReadOnlyList<MazeDefinition> Mazes);

/// <summary>Reads dungeon declarations out of the content archive.</summary>
public interface IDungeonDefinitionSource
{
    /// <summary>
    /// The dungeon with this identifier, or null when the archive declares none, or declares one
    /// with no maze. A dungeon without a maze is a different structural family -- Tower of Death
    /// is the example -- and answering with an empty maze list would let a caller treat it as an
    /// ordinary dungeon that happens to have nowhere to go.
    /// </summary>
    ValueTask<DungeonDefinition?> GetAsync(int dungeonId, CancellationToken cancellationToken);
}
