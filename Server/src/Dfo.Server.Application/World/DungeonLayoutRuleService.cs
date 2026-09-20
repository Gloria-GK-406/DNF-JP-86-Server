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

        var candidate = SelectMaze(definition, facts, seed);
        if (candidate is null)
            return null;
        var (maze, purpose) = candidate.Value;
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
            maze.Rooms,
            definition.BasisLevel,
            definition.ExperienceWeight,
            purpose);
    }

    /// <remarks>
    /// Candidate partition precedes random choice. An interactive entry sees only ordinary mazes;
    /// a directed entry sees only quest connections its active/cleared state and difficulty make
    /// eligible. The seed therefore varies a layout only inside one semantic candidate set and
    /// cannot turn an ordinary entry into a quest run.
    /// </remarks>
    private static (MazeDefinition Maze, DungeonRunPurpose Purpose)? SelectMaze(
        DungeonDefinition definition,
        DungeonEntryFacts facts,
        int seed)
    {
        var connected = definition.Mazes
            .Select(maze => (Maze: maze, Connection: maze.QuestConnection ?? definition.QuestConnection))
            .Where(candidate => candidate.Connection is not null)
            .ToArray();

        if (facts.Intent.Kind == DungeonEntryIntentKind.Interactive)
        {
            var ordinary = definition.Mazes
                .Where(maze => maze.QuestConnection is null && definition.QuestConnection is null)
                .ToArray();
            return ChooseMaze(ordinary, seed, DungeonRunPurpose.Ordinary);
        }

        if (facts.Intent.DirectedDungeonId != definition.DungeonId)
            return null;

        if (connected.Length == 0)
            return ChooseMaze(definition.Mazes, seed, DungeonRunPurpose.Ordinary);

        var eligible = connected
            .Where(candidate => IsEligible(candidate.Connection!, facts))
            .ToArray();
        if (eligible.Length == 0)
            return null;

        var selected = eligible[(int)(Mix(seed, salt: 0) % (uint)eligible.Length)];
        return (
            selected.Maze,
            DungeonRunPurpose.QuestLinked(
                selected.Connection!.QuestId,
                selected.Connection.State));
    }

    private static (MazeDefinition Maze, DungeonRunPurpose Purpose)? ChooseMaze(
        IReadOnlyList<MazeDefinition> candidates,
        int seed,
        DungeonRunPurpose purpose) =>
        candidates.Count == 0
            ? null
            : (candidates[(int)(Mix(seed, salt: 0) % (uint)candidates.Count)], purpose);

    private static bool IsEligible(
        DungeonQuestConnection connection,
        DungeonEntryFacts facts) =>
        (connection.MinimumDifficulty < 0 || (int)facts.Difficulty >= connection.MinimumDifficulty)
        && (connection.State == DungeonQuestConnectionState.Active
            ? facts.ActiveQuestIds.Contains(connection.QuestId)
            : facts.ClearedQuestIds.Contains(connection.QuestId));

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
