using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Chooses the maze a run uses and where it starts, from content and one explicit seed.
/// </summary>
/// <remarks>
/// A pure function of its arguments. It reads no world state and holds none, so a layout can be
/// produced for a character before anything has been committed on that character's behalf, and the
/// same arguments always produce the same layout.
/// <para>
/// The mixing below is written out rather than delegated to <see cref="Random"/>. The framework
/// does not guarantee that a given seed yields the same sequence across runtime versions, and a
/// layout that changes when the runtime is upgraded would break the one property this type exists
/// to provide.
/// </para>
/// </remarks>
internal sealed class DungeonLayoutRuleService(IDungeonDefinitionSource dungeons)
    : IDungeonLayoutRuleService
{
    public async ValueTask<DungeonLayout?> ResolveAsync(
        int dungeonId,
        DungeonEntryFacts facts,
        int seed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var definition = await dungeons.GetAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        if (definition is null)
            return null;

        var maze = SelectMaze(definition, seed);
        var startCell = Choose(maze.StartCandidates, seed, salt: 1);
        var bossCell = Choose(maze.BossCandidates, seed, salt: 2);

        // A start cell that carries no room would put the character nowhere, and the client would
        // be loading a map the server cannot name. Refusing is the honest answer; the content that
        // produced it is wrong in a way this layer cannot repair.
        if (!maze.Rooms.TryGetValue(startCell, out var startRoomMapId))
        {
            throw new InvalidDataException(
                $"Dungeon '{dungeonId}' maze '{maze.Index}' starts at cell "
                + $"({startCell.X},{startCell.Y}), which declares no room.");
        }

        return new DungeonLayout(
            dungeonId,
            maze.Index,
            maze.Width,
            maze.Height,
            startCell,
            bossCell,
            startRoomMapId,
            seed,
            maze.Rooms);
    }

    /// <remarks>
    /// One maze is the ordinary case and needs no choice. Where a dungeon declares several, the
    /// reference server filters them by the quests the character carries and by difficulty; that
    /// rule has not been read out of it yet, so the choice here is made from the seed alone and
    /// the facts are deliberately unused. The consequence is bounded and known: a quest-linked
    /// maze can be chosen that the character's quest did not ask for. It does not reach the
    /// tutorial, which declares one maze. Recorded in Docs/DungeonEntryNotes.md.
    /// </remarks>
    private static MazeDefinition SelectMaze(DungeonDefinition definition, int seed) =>
        definition.Mazes.Count == 1
            ? definition.Mazes[0]
            : definition.Mazes[(int)(Mix(seed, salt: 0) % (uint)definition.Mazes.Count)];

    private static MazeCell Choose(IReadOnlyList<MazeCell> candidates, int seed, int salt) =>
        candidates.Count switch
        {
            0 => default,
            1 => candidates[0],
            _ => candidates[(int)(Mix(seed, salt) % (uint)candidates.Count)],
        };

    /// <summary>A fixed integer mix, so the same seed and salt always select the same candidate.</summary>
    private static uint Mix(int seed, int salt)
    {
        var value = unchecked((uint)seed * 2654435761u + (uint)salt * 2246822519u);
        value ^= value >> 15;
        value = unchecked(value * 2654435761u);
        value ^= value >> 13;
        return value;
    }
}
