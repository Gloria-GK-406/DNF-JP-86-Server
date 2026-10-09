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
        // The frozen hell replacement still has to satisfy this cell's actual door layout.
        // It never falls back to ordinary resources when its own metadata is unavailable.
        if (layout.Hell is { } hell && target == hell.Cell)
        {
            var map = await maps.GetAsync(hell.MapId, cancellationToken).ConfigureAwait(false);
            return map is not null && IsCompatible(map, cell, entrance)
                ? DungeonMapSelection.Selected(hell.MapId)
                : DungeonMapSelection.Refused(DungeonMapSelectionFailure.NoCompatibleMap);
        }
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
                if (map is not null && IsCompatible(map, cell, entrance))
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
                if (IsCompatible(map, cell, entrance))
                    candidates.Add(map.MapId);
            }
        }
        if (candidates.Count == 0)
            return DungeonMapSelection.Refused(DungeonMapSelectionFailure.NoCompatibleMap);
        var ordered = candidates.Distinct().Order().ToArray();
        var salt = unchecked(3 + target.X * 257 + target.Y * 65537 + (int)entrance * 16777619);
        return DungeonMapSelection.Selected(ordered[(int)(Mix(layout.Seed, salt) % (uint)ordered.Length)]);
    }

    private static bool IsCompatible(DungeonMapDefinition map, DungeonCellDefinition cell,
        DungeonPathDirection entrance) =>
        !cell.HasDeclaredConnections && entrance == DungeonPathDirection.None
        || map.Connections.Any(pair => (!cell.HasDeclaredConnections || pair.Pattern == cell.Connections)
            && (entrance == DungeonPathDirection.None || (pair.Entrances & entrance) != 0));

    public async ValueTask<DungeonLayout?> ResolveAsync(
        int dungeonId,
        DungeonEntryFacts facts,
        int seed,
        CancellationToken cancellationToken)
    {
        var topology = await ResolveTopologyAsync(dungeonId, facts, seed, cancellationToken).ConfigureAwait(false);
        return topology is null ? null : await PrepareLayoutAsync(topology, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<DungeonLayout?> ResolveTopologyAsync(
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
        ValidateMaze(dungeonId, maze);
        var startCell = Choose(maze.StartCandidates, seed, salt: 1);
        var bossCell = Choose(maze.BossCandidates, seed, salt: 2);

        return new DungeonLayout(
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
    }

    public async ValueTask<DungeonLayout> PrepareLayoutAsync(DungeonLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Hell is { } hell && (hell.MapId <= 0 || !layout.Cells.ContainsKey(hell.Cell)))
            throw ConfigurationFailure(layout.DungeonId, layout.MazeIndex,
                "hell replacement requires an occupied cell and a positive map id");
        var startCell = layout.StartCell;
        var start = await SelectMapAsync(layout, null, startCell, cancellationToken).ConfigureAwait(false);
        if (start.MapId is not { } mapId)
            throw ConfigurationFailure(layout.DungeonId, layout.MazeIndex,
                $"start cell ({startCell.X},{startCell.Y}) has no compatible map");
        layout = layout with { StartRoomMapId = mapId };
        await ValidateReachableMapsAsync(layout, cancellationToken).ConfigureAwait(false);
        return layout;
    }

    /// <summary>Content syntax is read globally; run semantics belong only to the selected maze.</summary>
    private static void ValidateMaze(int dungeonId, MazeDefinition maze)
    {
        if (maze.ConfigurationError is { } error)
            throw ConfigurationFailure(dungeonId, maze.Index, error);
        if (maze.Width == 0 || maze.Height == 0 || maze.Cells.Count == 0)
            throw ConfigurationFailure(dungeonId, maze.Index, "maze size and occupied cells must be nonempty");
        foreach (var (coordinate, cell) in maze.Cells)
        {
            if (coordinate.X >= maze.Width || coordinate.Y >= maze.Height)
                throw ConfigurationFailure(dungeonId, maze.Index,
                    $"occupied cell ({coordinate.X},{coordinate.Y}) is outside the maze size");
            if (cell.HasDeclaredConnections && (cell.Connections == DungeonPathDirection.None
                    || (cell.Connections & ~DungeonPathDirection.All) != 0))
                throw ConfigurationFailure(dungeonId, maze.Index,
                    $"occupied cell ({coordinate.X},{coordinate.Y}) has an invalid door pattern");
            if (cell.MapCandidates.Concat(cell.BossMapCandidates).Any(id => id <= 0))
                throw ConfigurationFailure(dungeonId, maze.Index,
                    $"cell ({coordinate.X},{coordinate.Y}) has an invalid map candidate id");
        }
        ValidateCandidates(maze.StartCandidates, "start");
        ValidateCandidates(maze.BossCandidates, "boss");

        void ValidateCandidates(IReadOnlyList<MazeCell> candidates, string kind)
        {
            if (candidates.Count == 0)
                throw ConfigurationFailure(dungeonId, maze.Index, $"{kind} candidates are empty");
            foreach (var cell in candidates)
                if (cell.X >= maze.Width || cell.Y >= maze.Height || !maze.Cells.ContainsKey(cell))
                    throw ConfigurationFailure(dungeonId, maze.Index,
                        $"{kind} cell ({cell.X},{cell.Y}) declares no room within the maze");
        }
    }

    /// <summary>
    /// Refuse incompatible content before paying for entry. This checks candidates; each cell's
    /// actual map remains selected only on its first visit and retained by the run thereafter.
    /// </summary>
    private async ValueTask ValidateReachableMapsAsync(DungeonLayout layout, CancellationToken token)
    {
        var reachable = new HashSet<MazeCell> { layout.StartCell };
        var pending = new Queue<MazeCell>();
        pending.Enqueue(layout.StartCell);
        while (pending.TryDequeue(out var source))
        {
            token.ThrowIfCancellationRequested();
            if (source == layout.BossCell) continue;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var x = source.X + dx;
                var y = source.Y + dy;
                if (x < 0 || x >= layout.Width || y < 0 || y >= layout.Height) continue;
                var target = new MazeCell((byte)x, (byte)y);
                if (!layout.CanTraverse(source, target)) continue;
                // The start resource is already validated and created before any door move;
                // revisiting it retains that Cell instead of selecting a new MAP.
                if (target == layout.StartCell) continue;
                var selected = await SelectMapAsync(layout, source, target, token).ConfigureAwait(false);
                if (selected.MapId is null)
                    throw ConfigurationFailure(layout.DungeonId, layout.MazeIndex,
                        $"cell ({target.X},{target.Y}) has no compatible map for entrance "
                        + $"'{DungeonLayout.EntranceFrom(source, target)}'");
                if (reachable.Add(target)) pending.Enqueue(target);
            }
        }
        if (!reachable.Contains(layout.BossCell))
            throw ConfigurationFailure(layout.DungeonId, layout.MazeIndex,
                $"boss cell ({layout.BossCell.X},{layout.BossCell.Y}) is unreachable from the start");
    }

    private static InvalidDataException ConfigurationFailure(int dungeonId, int mazeIndex, string detail) =>
        new($"Dungeon '{dungeonId}' maze '{mazeIndex}': {detail}.");

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

        if (facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
        {
            if (facts.Intent.CycleQuestId is not { } questId
                || !facts.ActiveQuestIds.Contains(questId)) return null;
            var candidates = connected.Where(candidate => candidate.Connection!.QuestId == questId
                && candidate.Connection.State == DungeonQuestConnectionState.Active
                && IsEligible(candidate.Connection, facts)).ToArray();
            if (candidates.Length == 0) return null;
            var cycle = candidates[(int)(Mix(seed, salt: 0) % (uint)candidates.Length)];
            return (cycle.Maze, new DungeonRunPurpose(DungeonRunPurposeKind.Cycle, questId, cycle.Connection!.State));
        }

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
