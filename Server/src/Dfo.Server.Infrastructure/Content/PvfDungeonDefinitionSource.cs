using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Projects the archive's dungeon list and dungeon files into <see cref="DungeonDefinition"/>.
/// </summary>
/// <remarks>
/// A dungeon file is a flat sequence of tags. <c>[maze info]</c> opens a maze and is not closed by
/// a matching tag in the observed files -- the tutorial's ends where <c>[special passive object
/// item]</c> begins -- so a maze is closed by the next <c>[maze info]</c>, by an explicit
/// <c>[/maze info]</c> where one exists, or by the end of the file. Reading it as a properly
/// bracketed block would silently drop every maze in a file that never closes one.
/// <para>
/// Inside a maze, <c>[size]</c> gives the grid, <c>[map specification]</c> lists the rooms as
/// repeated <c>"map" x y mapId</c>, and <c>[start map]</c> and <c>[boss map]</c> list candidate
/// cells as bare x/y pairs -- candidates rather than single cells, which is why choosing among
/// them belongs to a rule and not here.
/// </para>
/// </remarks>
internal sealed class PvfDungeonDefinitionSource : IDungeonDefinitionSource
{
    private const string ListPath = "dungeon/dungeon.lst";
    private const string DungeonDirectory = "dungeon";
    private const string RoomKeyword = "map";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, DungeonDefinition?> _cache = [];
    private IReadOnlyDictionary<int, string>? _paths;

    public PvfDungeonDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<DungeonDefinition?> GetAsync(int dungeonId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_cache.TryGetValue(dungeonId, out var cached))
                return ValueTask.FromResult(cached);

            var definition = Read(_provider.Archive, Paths(), dungeonId);
            _cache[dungeonId] = definition;
            return ValueTask.FromResult(definition);
        }
    }

    private IReadOnlyDictionary<int, string> Paths()
    {
        if (_paths is not null)
            return _paths;

        var paths = new Dictionary<int, string>();
        foreach (var (dungeonId, relativePath) in PvfListFile.Read(_provider.Archive, ListPath))
            paths.TryAdd(dungeonId, relativePath);
        if (paths.Count == 0)
            throw new InvalidOperationException($"{ListPath} resolved no dungeon files.");
        return _paths = paths;
    }

    private static DungeonDefinition? Read(
        PvfArchive archive,
        IReadOnlyDictionary<int, string> paths,
        int dungeonId)
    {
        if (!paths.TryGetValue(dungeonId, out var relativePath))
            return null;

        var archivePath = $"{DungeonDirectory}/{relativePath}";
        var fileIndex = archive.FindFile(archivePath);
        if (fileIndex < 0)
            return null;

        var tags = new DungeonTags(archive);
        var mazes = new List<MazeDefinition>();
        MazeDraft? draft = null;
        var section = Section.None;
        byte minimumLevel = 1;
        var consumesFatigue = true;
        var pending = new List<int>();

        var reader = archive.OpenScript(fileIndex);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                var name = archive.ResolveString(token.Value);
                if (name.StartsWith('['))
                {
                    Flush(draft, section, pending);
                    pending.Clear();

                    if (token.Value == tags.MazeInfo)
                    {
                        Close(mazes, draft);
                        draft = new MazeDraft(mazes.Count);
                        section = Section.None;
                    }
                    else if (token.Value == tags.MazeInfoClose)
                    {
                        Close(mazes, draft);
                        draft = null;
                        section = Section.None;
                    }
                    else if (token.Value == tags.Size) section = Section.Size;
                    else if (token.Value == tags.MapSpecification) section = Section.Rooms;
                    else if (token.Value == tags.StartMap) section = Section.Start;
                    else if (token.Value == tags.BossMap) section = Section.Boss;
                    else if (token.Value == tags.MinimumRequiredLevel) section = Section.MinimumLevel;
                    else if (token.Value == tags.NoFatigue)
                    {
                        consumesFatigue = false;
                        section = Section.None;
                    }
                    else
                    {
                        section = Section.None;
                    }

                    continue;
                }
            }

            if (section == Section.Rooms && token.IsString)
            {
                // The kind is quoted and repeats before every room. Anything that is not a room
                // is skipped rather than guessed at: the pending operands are dropped with it.
                Flush(draft, section, pending);
                pending.Clear();
                if (!string.Equals(
                        archive.ResolveString(token.Value), RoomKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    section = Section.SkippedRoomEntry;
                }

                continue;
            }

            if (token.Kind != PvfTokenKind.Int32)
                continue;

            switch (section)
            {
                case Section.MinimumLevel:
                    minimumLevel = token.Value is >= 0 and <= byte.MaxValue
                        ? (byte)token.Value
                        : minimumLevel;
                    section = Section.None;
                    break;
                case Section.Size:
                case Section.Rooms:
                case Section.Start:
                case Section.Boss:
                    pending.Add(token.Value);
                    Flush(draft, section, pending);
                    break;
            }
        }

        Flush(draft, section, pending);
        Close(mazes, draft);

        // No maze means a different structural family, not an ordinary dungeon with nowhere to go.
        return mazes.Count == 0
            ? null
            : new DungeonDefinition(dungeonId, archivePath, minimumLevel, consumesFatigue, mazes);
    }

    /// <summary>Consumes whole rows once enough operands have arrived, leaving partial ones.</summary>
    private static void Flush(MazeDraft? draft, Section section, List<int> pending)
    {
        if (draft is null)
        {
            pending.Clear();
            return;
        }

        switch (section)
        {
            case Section.Size when pending.Count >= 2:
                draft.Width = Clamp(pending[0]);
                draft.Height = Clamp(pending[1]);
                pending.Clear();
                break;
            case Section.Rooms when pending.Count >= 3:
                draft.Rooms[new MazeCell(Clamp(pending[0]), Clamp(pending[1]))] = pending[2];
                pending.Clear();
                break;
            case Section.Start when pending.Count >= 2:
                draft.StartCandidates.Add(new MazeCell(Clamp(pending[0]), Clamp(pending[1])));
                pending.Clear();
                break;
            case Section.Boss when pending.Count >= 2:
                draft.BossCandidates.Add(new MazeCell(Clamp(pending[0]), Clamp(pending[1])));
                pending.Clear();
                break;
        }
    }

    private static void Close(List<MazeDefinition> mazes, MazeDraft? draft)
    {
        if (draft is null || draft.Rooms.Count == 0)
            return;
        mazes.Add(new MazeDefinition(
            draft.Index,
            draft.Width,
            draft.Height,
            draft.Rooms,
            draft.StartCandidates,
            draft.BossCandidates));
    }

    private static byte Clamp(int value) =>
        value is >= 0 and <= byte.MaxValue ? (byte)value : (byte)0;

    private enum Section
    {
        None,
        Size,
        Rooms,
        Start,
        Boss,
        MinimumLevel,
        SkippedRoomEntry,
    }

    private sealed class MazeDraft(int index)
    {
        public int Index { get; } = index;

        public byte Width { get; set; }

        public byte Height { get; set; }

        public Dictionary<MazeCell, int> Rooms { get; } = [];

        public List<MazeCell> StartCandidates { get; } = [];

        public List<MazeCell> BossCandidates { get; } = [];
    }

    private readonly struct DungeonTags
    {
        public DungeonTags(PvfArchive archive)
        {
            MazeInfo = archive.GetTag("[maze info]").Offset;
            MazeInfoClose = archive.GetTag("[/maze info]").Offset;
            Size = archive.GetTag("[size]").Offset;
            MapSpecification = archive.GetTag("[map specification]").Offset;
            StartMap = archive.GetTag("[start map]").Offset;
            BossMap = archive.GetTag("[boss map]").Offset;
            MinimumRequiredLevel = archive.GetTag("[minimum required level]").Offset;
            NoFatigue = archive.GetTag("[no fatigue]").Offset;
        }

        public int MazeInfo { get; }

        public int MazeInfoClose { get; }

        public int Size { get; }

        public int MapSpecification { get; }

        public int StartMap { get; }

        public int BossMap { get; }

        public int MinimumRequiredLevel { get; }

        public int NoFatigue { get; }
    }
}
