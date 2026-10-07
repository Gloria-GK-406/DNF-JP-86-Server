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
internal sealed class DungeonStructureRuleService(IDungeonDefinitionSource dungeons, IDungeonMapDefinitionSource maps)
    : IDungeonStructureRuleService
{
    public ValueTask<DungeonDefinition?> GetDefinitionAsync(int dungeonId, CancellationToken cancellationToken) =>
        dungeons.GetAsync(dungeonId, cancellationToken);

    public async ValueTask<DungeonMapSelection> SelectMapAsync(
        DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!layout.Cells.TryGetValue(target, out var cell))
            return DungeonMapSelection.Refused(DungeonMapSelectionFailure.CellNotFound);
        if (from is { } previous && !layout.CanTraverse(previous, target))
            return DungeonMapSelection.Refused(DungeonMapSelectionFailure.Disconnected);

        var entrance = from is { } source ? DungeonLayout.EntranceFrom(source, target) : DungeonPathDirection.None;
        var boss = from is not null && target == layout.BossCell;
        var declared = boss ? cell.BossMapCandidates : cell.MapCandidates;
        var candidates = new List<int>();
        if (declared.Count > 0)
        {
            // Explicit resources are checked for entrance compatibility, not replaced with
            // unrelated pool maps if all declared candidates are incompatible.
            foreach (var mapId in declared)
            {
                var map = await maps.GetAsync(mapId, cancellationToken).ConfigureAwait(false);
                if (map is not null && (entrance == DungeonPathDirection.None
                        || map.Connections.Any(pair => (pair.Entrances & entrance) != 0)))
                    candidates.Add(mapId);
            }
        }
        else
        {
            var pool = await maps.ListForDungeonAsync(layout.DungeonId, cancellationToken).ConfigureAwait(false);
            var kind = boss ? DungeonMapKind.Boss : DungeonMapKind.Normal;
            foreach (var map in pool)
            {
                if (from is null ? !map.HasStartArea : map.Kind != kind) continue;
                if (map.Connections.Any(pair => pair.Pattern == cell.Connections
                        && (entrance == DungeonPathDirection.None || (pair.Entrances & entrance) != 0)))
                    candidates.Add(map.MapId);
            }
        }
        if (candidates.Count == 0)
            return DungeonMapSelection.Refused(DungeonMapSelectionFailure.NoCompatibleMap);
        var ordered = candidates.Distinct().Order().ToArray();
        var salt = unchecked(3 + target.X * 257 + target.Y * 65537 + (int)entrance * 16777619);
        return DungeonMapSelection.Selected(ordered[(int)(Mix(layout.Seed, salt) % (uint)ordered.Length)]);
    }

    public async ValueTask<DungeonLayout?> ResolveAsync(
        int dungeonId,
        DungeonEntryFacts facts,
        int seed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var definition = await dungeons.GetAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        if (definition is null || definition.EntryMode != DungeonEntryMode.Maze)
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
        if (!maze.Cells.ContainsKey(startCell))
        {
            throw new InvalidDataException(
                $"Dungeon '{dungeonId}' maze '{maze.Index}' starts at cell "
                + $"({startCell.X},{startCell.Y}), which declares no room.");
        }

        var layout = new DungeonLayout(
            dungeonId,
            maze.Index,
            maze.Width,
            maze.Height,
            startCell,
            bossCell,
            0,
            seed,
            maze.Cells,
            definition.BasisLevel,
            definition.ExperienceWeight,
            purpose, definition.ConsumesFatigue);
        var start = await SelectMapAsync(layout, null, startCell, cancellationToken).ConfigureAwait(false);
        return start.MapId is { } mapId ? layout with { StartRoomMapId = mapId } : null;
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
