using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Projects the archive's town list, town files and map files into
/// <see cref="TownDefinition"/> and <see cref="MapGeometry"/>.
/// </summary>
/// <remarks>
/// A town file is a sequence of <c>[area]</c> blocks. Each opens with the area identifier
/// and its map path, may carry a permission block, and then states its kind as a quoted
/// value such as <c>[gate]</c> followed by up to two operands. The kind arrives quoted
/// rather than as an identifier, so it cannot be matched as a section tag.
/// <para>
/// A map file states its geometry as flat integer runs: <c>[town movable area]</c> in rows
/// of six — bounds, then the town and area it leads to — and
/// <c>[virtual movable area]</c> in rows of four bounds.
/// </para>
/// </remarks>
internal sealed class PvfTownDefinitionSource : ITownDefinitionSource
{
    private const string ListPath = "town/town.lst";
    private const string TownDirectory = "town";
    private const string MapDirectory = "map";
    private const int TransitRowLength = 6;
    private const int WalkableRowLength = 4;

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, MapGeometry?> _geometry = new(StringComparer.OrdinalIgnoreCase);
    private Catalog? _catalog;

    public PvfTownDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<IReadOnlyList<TownDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().Ordered);
    }

    public ValueTask<TownDefinition?> GetAsync(int townId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().ById.GetValueOrDefault(townId));
    }

    public ValueTask<MapGeometry?> GetMapGeometryAsync(string mapPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(mapPath);

        lock (_gate)
        {
            if (_geometry.TryGetValue(mapPath, out var cached))
                return ValueTask.FromResult(cached);

            var geometry = ReadGeometry(_provider.Archive, mapPath);
            _geometry[mapPath] = geometry;
            return ValueTask.FromResult(geometry);
        }
    }

    private Catalog Load()
    {
        if (_catalog is not null)
            return _catalog;

        lock (_gate)
            return _catalog ??= Build(_provider.Archive);
    }

    private static Catalog Build(PvfArchive archive)
    {
        var ordered = new List<TownDefinition>();
        var byId = new Dictionary<int, TownDefinition>();
        var tags = new TownTags(archive);

        foreach (var (townId, relativePath) in PvfListFile.Read(archive, ListPath))
        {
            var fileIndex = archive.FindFile($"{TownDirectory}/{relativePath}");
            if (fileIndex < 0)
                continue;

            var definition = ReadTown(archive, tags, townId, fileIndex);
            ordered.Add(definition);
            byId.TryAdd(townId, definition);
        }

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{ListPath} resolved no town files.");

        return new Catalog(ordered, byId);
    }

    private static TownDefinition ReadTown(PvfArchive archive, TownTags tags, int townId, int fileIndex)
    {
        var areas = new List<TownAreaDefinition>();
        var reader = archive.OpenScript(fileIndex);
        var name = string.Empty;

        AreaDraft? draft = null;
        var inPermission = false;
        var readingName = false;

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                readingName = false;

                if (token.Value == tags.AreaOpen)
                {
                    draft = new AreaDraft();
                    inPermission = false;
                }
                else if (token.Value == tags.AreaClose)
                {
                    if (draft is not null)
                        areas.Add(draft.ToDefinition());

                    draft = null;
                }
                else if (token.Value == tags.PermissionOpen)
                {
                    // A permission block carries its own integers. Without this the level
                    // it requires would be mistaken for the area's operands.
                    inPermission = true;
                }
                else if (token.Value == tags.PermissionClose)
                {
                    inPermission = false;
                }
                else if (token.Value == tags.Name)
                {
                    readingName = true;
                }

                continue;
            }

            if (readingName && token.Kind == PvfTokenKind.QuotedString)
            {
                name = archive.ResolveString(token.Value);
                readingName = false;
                continue;
            }

            if (draft is null || inPermission)
                continue;

            draft.Accept(archive, token);
        }

        return new TownDefinition(townId, name, areas);
    }

    private static MapGeometry? ReadGeometry(PvfArchive archive, string mapPath)
    {
        var normalized = mapPath.Replace('\\', '/');
        var fileIndex = archive.FindFile($"{MapDirectory}/{normalized}");
        if (fileIndex < 0)
            return null;

        var transit = RequireTag(archive, "[town movable area]");
        var transitClose = RequireTag(archive, "[/town movable area]");
        var walkable = RequireTag(archive, "[virtual movable area]");
        var walkableClose = RequireTag(archive, "[/virtual movable area]");

        var transitValues = new List<int>();
        var walkableValues = new List<int>();
        List<int>? active = null;
        var nested = 0;
        var reader = archive.OpenScript(fileIndex);

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (token.Value == transit)
                {
                    active = transitValues;
                    nested = 0;
                }
                else if (token.Value == walkable)
                {
                    active = walkableValues;
                    nested = 0;
                }
                else if (token.Value == transitClose || token.Value == walkableClose)
                {
                    active = null;
                    nested = 0;
                }
                else if (active is not null)
                {
                    // A section can contain a nested block, such as a condition on one row.
                    // Its integers are not coordinates, but the section continues after it,
                    // so the block is skipped rather than treated as the section's end. An
                    // earlier version ended collection here and silently dropped every row
                    // that followed.
                    nested += archive.ResolveString(token.Value)
                        .StartsWith("[/", StringComparison.Ordinal)
                        ? -1
                        : 1;
                    nested = Math.Max(0, nested);
                }

                continue;
            }

            if (active is not null && nested == 0 && token.Kind == PvfTokenKind.Int32)
                active.Add(token.Value);
        }

        var transits = new List<TownTransitArea>(transitValues.Count / TransitRowLength);
        for (var offset = 0; offset + TransitRowLength <= transitValues.Count; offset += TransitRowLength)
        {
            transits.Add(new TownTransitArea(
                new MapRectangle(
                    transitValues[offset],
                    transitValues[offset + 1],
                    transitValues[offset + 2],
                    transitValues[offset + 3]),
                transitValues[offset + 4],
                transitValues[offset + 5]));
        }

        var walkables = new List<MapRectangle>(walkableValues.Count / WalkableRowLength);
        for (var offset = 0; offset + WalkableRowLength <= walkableValues.Count; offset += WalkableRowLength)
        {
            walkables.Add(new MapRectangle(
                walkableValues[offset],
                walkableValues[offset + 1],
                walkableValues[offset + 2],
                walkableValues[offset + 3]));
        }

        return new MapGeometry(transits, walkables);
    }

    /// <summary>
    /// The name-table offset of a tag this parser depends on.
    /// </summary>
    /// <remarks>
    /// An unresolved tag means the archive does not use that name anywhere, so this parser's
    /// assumption about its shape is wrong. Passing the unresolved sentinel through would
    /// match nothing, yield an empty-but-present geometry, and surface later as an accusation
    /// that the content states no arrival point — blaming the archive for a parser fault.
    /// </remarks>
    private static int RequireTag(PvfArchive archive, string tag)
    {
        var resolved = archive.GetTag(tag);
        return resolved.IsResolved
            ? resolved.Offset
            : throw new InvalidOperationException(
                $"The archive contains no '{tag}' tag, so map geometry cannot be read from it.");
    }

    private sealed class AreaDraft
    {
        private const string MinimapOpen = "[minimap rect]";
        private const string MinimapClose = "[/minimap rect]";

        private readonly List<int> _operands = [];
        private int _areaId = TownAreaDefinition.NoOperand;
        private string _mapPath = string.Empty;
        private TownAreaKind _kind = TownAreaKind.Unrecognized;
        private bool _haveAreaId;
        private bool _haveKind;
        private bool _inMinimapRect;

        public void Accept(PvfArchive archive, in PvfToken token)
        {
            if (token.Kind == PvfTokenKind.Int32)
            {
                // The minimap rectangle is four integers of presentation data. Counting them
                // as operands would put a rectangle corner where a spawn position belongs.
                if (_inMinimapRect)
                    return;

                if (!_haveAreaId)
                {
                    _areaId = token.Value;
                    _haveAreaId = true;
                }
                else if (_haveKind)
                {
                    // Only the numbers following the kind are its operands. Modifier markers
                    // carry their own numbers, and counting those would shift a gate's spawn
                    // position onto whatever the modifier happened to state.
                    _operands.Add(token.Value);
                }

                return;
            }

            if (token.Kind != PvfTokenKind.QuotedString)
                return;

            var value = archive.ResolveString(token.Value).Trim();
            switch (value)
            {
                case MinimapOpen:
                    _inMinimapRect = true;
                    return;
                case MinimapClose:
                    _inMinimapRect = false;
                    return;
            }

            if (_mapPath.Length == 0)
            {
                _mapPath = value.Replace('\\', '/');
                return;
            }

            // The kind is recognised by what it says, not by where it sits. The content puts
            // modifier markers such as `[use private store]` between the minimap rectangle
            // and the kind, so taking the next quoted value would classify 54 of the
            // archive's 151 areas by a modifier instead of by their kind.
            if (!_haveKind && TryParseKind(value, out var kind))
            {
                _kind = kind;
                _haveKind = true;
            }
        }

        public TownAreaDefinition ToDefinition() => new(
            _areaId,
            _mapPath,
            _kind,
            _operands.Count > 0 ? _operands[0] : TownAreaDefinition.NoOperand,
            _operands.Count > 1 ? _operands[1] : TownAreaDefinition.NoOperand);

        /// <remarks>
        /// The kinds modelled here are the ones entry policy acts on. The content declares
        /// others, such as the PvP safe zones and the private-store modifier, which leave
        /// the area <see cref="TownAreaKind.Unrecognized"/> rather than being guessed at: no
        /// entry rule depends on them, and naming them would assert a meaning this work has
        /// not confirmed.
        /// </remarks>
        private static bool TryParseKind(string value, out TownAreaKind kind)
        {
            kind = value switch
            {
                "[normal]" => TownAreaKind.Normal,
                "[gate]" => TownAreaKind.Gate,
                "[dungeon gate]" => TownAreaKind.DungeonGate,
                _ => TownAreaKind.Unrecognized,
            };

            return kind is not TownAreaKind.Unrecognized;
        }
    }

    private readonly struct TownTags
    {
        public TownTags(PvfArchive archive)
        {
            AreaOpen = archive.GetTag("[area]").Offset;
            AreaClose = archive.GetTag("[/area]").Offset;
            PermissionOpen = archive.GetTag("[permission]").Offset;
            PermissionClose = archive.GetTag("[/permission]").Offset;
            Name = archive.GetTag("[name]").Offset;
        }

        public int AreaOpen { get; }

        public int AreaClose { get; }

        public int PermissionOpen { get; }

        public int PermissionClose { get; }

        public int Name { get; }
    }

    private sealed record Catalog(
        IReadOnlyList<TownDefinition> Ordered,
        Dictionary<int, TownDefinition> ById);
}
