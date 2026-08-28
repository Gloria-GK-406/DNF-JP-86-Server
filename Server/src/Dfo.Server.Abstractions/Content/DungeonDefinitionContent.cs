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
/// <param name="ExperienceWeight">
/// What this dungeon multiplies a monster's worth by, from its <c>[experience increasing point]</c>.
/// It is how one dungeon pays better than another at the same level and difficulty; the archive
/// gives dungeon 144 a weight of 1.45.
/// </param>
/// <param name="BasisLevel">
/// What a monster in this dungeon is worth when its map declines to say. A map is shared between
/// dungeons and so cannot carry it; the dungeon names it under <c>[basis level]</c>, and most maps
/// declare monster level zero and lean on it entirely.
/// </param>
public sealed record DungeonDefinition(
    int DungeonId,
    string ArchivePath,
    byte MinimumRequiredLevel,
    bool ConsumesFatigue,
    IReadOnlyList<MazeDefinition> Mazes,
    byte BasisLevel = 1,
    double ExperienceWeight = 1.0);

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
