using System.Collections.Immutable;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfLostLandContentSource(PvfArchiveProvider provider,
    IDungeonDefinitionSource dungeons) : ILostLandContentSource
{
    public async ValueTask<LostLandDefinition> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dungeon = await dungeons.GetAsync(4000, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing LostLand dungeon declaration.");
        var archive = provider.Archive;
        var document = PvfDocument.Open(archive, dungeon.ArchivePath);
        if (document.Read(PvfField.String("[dungeon type]")) != "lost land" || dungeon.Mazes.Count != 1)
            throw new InvalidDataException("Expected one LostLand exploration maze.");
        var maze = dungeon.Mazes[0];
        var periods = document.Read(PvfField.Int32List("[lost land parameters]"));
        var limits = document.Read(PvfField.Int32List("[limit time]"));
        var fever = document.Read(PvfField.Int32List("[fever time]"));
        if (fever.Count != 8 || fever[0] <= 0)
            throw new InvalidDataException("Invalid LostLand fever parameters.");
        if (periods.Count != 2 || periods.Any(value => value <= 0)
            || limits.Count != 2 || limits.Any(value => value <= 0) || limits[0] > limits[1])
            throw new InvalidDataException("Invalid LostLand time parameters.");
        var entries = PvfDocument.Open(archive, "etc/dungeonetc/dungeonincountinfo.etc")
            .Read(PvfField.Int32List("[dungeon incount info]"));
        if (entries.Count % 4 != 0) throw new InvalidDataException("Truncated dungeon entry count declaration.");
        var matching = Enumerable.Range(0, entries.Count / 4).Select(index => index * 4)
            .Where(index => entries[index + 1] == dungeon.DungeonId).ToArray();
        if (matching.Length != 1 || entries[matching[0] + 2] is < 1 or > byte.MaxValue)
            throw new InvalidDataException("Invalid LostLand base entry count declaration.");
        return new(dungeon, periods[0], periods[1], limits.ToImmutableArray(), entries[matching[0] + 2],
            ReadWarps(document.Sections, maze.Width, maze.Height), ReadPatrols(maze.Width, maze.Height, cancellationToken))
        { FeverParameters = fever.ToImmutableArray() };
    }

    internal static ImmutableArray<LostLandWarp> ReadWarps(IReadOnlyList<PvfSection> sections, byte width, byte height)
    {
        var result = ImmutableArray.CreateBuilder<LostLandWarp>();
        var active = false;
        var seen = false;
        var pending = false;
        MazeCell? source = null;
        foreach (var section in sections)
        {
            switch (section.Name)
            {
                case "[warp map condition]":
                    if (active || seen) throw new InvalidDataException("Repeated LostLand warp block.");
                    active = seen = true;
                    break;
                case "[/warp map condition]" when active:
                    if (pending) throw new InvalidDataException("LostLand warp has no destination.");
                    active = false;
                    break;
                case "[source grid pos]" when active:
                    if (pending) throw new InvalidDataException("LostLand warp has a second source before its destination.");
                    source = Coordinate(section, width, height, wildcard: true);
                    pending = true;
                    break;
                case "[dest grid pos]" when active:
                    if (!pending) throw new InvalidDataException("LostLand warp has no source.");
                    result.Add(new(source, Coordinate(section, width, height, wildcard: false)!.Value));
                    pending = false;
                    break;
                default:
                    if (active) throw new InvalidDataException($"Unknown LostLand warp declaration {section.Name}.");
                    break;
            }
        }
        if (active || pending || result.Count == 0) throw new InvalidDataException("Incomplete LostLand warp block.");
        return result.ToImmutable();
    }

    private ImmutableArray<LostLandPatrol> ReadPatrols(byte width, byte height, CancellationToken ct)
    {
        var routes = ImmutableArray.CreateBuilder<LostLandPatrol>();
        var active = false;
        int? moveTerm = null, loop = null, meet = null;
        string? day = null;
        ImmutableArray<MazeCell> route = [];
        foreach (var section in PvfDocument.Open(provider.Archive, "etc/lostland/chaserpattern.etc").Sections)
        {
            ct.ThrowIfCancellationRequested();
            switch (section.Name)
            {
                case "[patrol route]":
                    if (active) throw new InvalidDataException("Nested LostLand patrol route.");
                    active = true;
                    moveTerm = loop = meet = null;
                    day = null;
                    route = [];
                    break;
                case "[/patrol route]" when active:
                    if (moveTerm is not > 0 || loop is null or < 0 || meet is null or < 0 || day is null || route.IsEmpty)
                        throw new InvalidDataException("Incomplete LostLand patrol route.");
                    routes.Add(new(moveTerm.Value, loop.Value, day, meet.Value, route));
                    active = false;
                    break;
                case "[move term]" when active && moveTerm is null: moveTerm = One(section); break;
                case "[is loop]" when active && loop is null: loop = One(section); break;
                case "[meet condition]" when active && meet is null: meet = One(section); break;
                case "[day condition]" when active && day is null:
                    day = PvfField.String(section.Name).Decode(provider.Archive, section.Values, "etc/lostland/chaserpattern.etc");
                    if (day is not ("day" or "night")) throw new InvalidDataException("Unknown LostLand patrol day condition.");
                    break;
                case "[grid index]" when active && route.IsEmpty:
                    var integers = Ints(section);
                    if (integers.Length == 0 || integers.Length % 2 != 0) throw new InvalidDataException("Truncated LostLand patrol coordinates.");
                    var cells = ImmutableArray.CreateBuilder<MazeCell>();
                    for (var i = 0; i < integers.Length; i += 2)
                        cells.Add(CheckedCell(integers[i], integers[i + 1], width, height));
                    route = cells.ToImmutable();
                    break;
                case "[/grid index]" when active: break;
                default: throw new InvalidDataException($"Unexpected LostLand patrol declaration {section.Name}.");
            }
        }
        if (active || routes.Count == 0) throw new InvalidDataException("Incomplete LostLand patrol catalog.");
        return routes.ToImmutable();
    }

    private static int[] Ints(PvfSection section) => section.Values.Select(token => token.Kind == PvfTokenKind.Int32
        ? token.Value : throw new InvalidDataException($"LostLand {section.Name} requires integers.")).ToArray();
    private static int One(PvfSection section) => Ints(section) is [var value] ? value
        : throw new InvalidDataException($"LostLand {section.Name} requires one integer.");
    private static MazeCell? Coordinate(PvfSection section, byte width, byte height, bool wildcard)
    {
        if (Ints(section) is not [var x, var y]) throw new InvalidDataException("LostLand grid position requires two integers.");
        return wildcard && x == -1 && y == -1 ? null : CheckedCell(x, y, width, height);
    }
    private static MazeCell CheckedCell(int x, int y, byte width, byte height) =>
        x >= 0 && x < width && y >= 0 && y < height ? new((byte)x, (byte)y)
            : throw new InvalidDataException($"LostLand coordinate ({x},{y}) is outside its maze.");
}
