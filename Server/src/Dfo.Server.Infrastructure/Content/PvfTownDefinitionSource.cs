using Dfo.Pvf.Schema;
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
internal sealed class PvfTownDefinitionSource : ITownDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> TownMovableAreaField = PvfField.Int32List("[town movable area]", optional: true);
        internal static readonly PvfField<bool> CloseTownMovableAreaField = PvfField.Marker("[/town movable area]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> VirtualMovableAreaField = PvfField.Int32List("[virtual movable area]", optional: true);
        internal static readonly PvfField<bool> CloseVirtualMovableAreaField = PvfField.Marker("[/virtual movable area]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> AreaField = PvfField.Tokens("[area]", optional: true);
        internal static readonly PvfField<bool> CloseAreaField = PvfField.Marker("[/area]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> PermissionField = PvfField.Tokens("[permission]", optional: true);
        internal static readonly PvfField<bool> ClosePermissionField = PvfField.Marker("[/permission]", optional: true);
        internal static readonly PvfField<string> NameField = PvfField.String("[name]", optional: true);
        internal static readonly PvfField<int> NeedLevelField = PvfField.Int32("[need level]", optional: true);
    }

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

    public string Name => "towns";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Load().Ordered.Count);

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
        var scriptEntry = archive.GetEntry(fileIndex);
        var scriptPath = $"{archive.ResolveString(scriptEntry.PathOffset)}/{archive.ResolveString(scriptEntry.NameOffset)}";
        // Quoted area operands may resume immediately after [/permission]; the scoped
        // reader owns that boundary rather than treating them as closing-marker operands.
        PvfDocument.Open(archive, scriptPath).ValidateFields(Fields.NameField, Fields.CloseAreaField);
        var reader = archive.OpenScript(fileIndex);
        var name = string.Empty;

        AreaDraft? draft = null;
        var inPermission = false;
        var readingName = false;
        var readingLevel = false;
        var levelTag = archive.GetTag(Fields.NeedLevelField);

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (readingLevel) throw new InvalidDataException($"PVF '{scriptPath}', field '{Fields.NeedLevelField}': missing Int32 level.");
                readingName = false;

                if (token.Value == tags.AreaOpen)
                {
                    if (draft is not null) throw new InvalidDataException($"PVF '{scriptPath}', field '{Fields.AreaField}': nested or unclosed area.");
                    draft = new AreaDraft(scriptPath);
                    inPermission = false;
                }
                else if (token.Value == tags.AreaClose)
                {
                    if (draft is null || inPermission) throw new InvalidDataException($"PVF '{scriptPath}', field '{Fields.AreaField}': area close without opening or with unclosed permission.");
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
                else if (inPermission && levelTag.IsResolved && token.Value == levelTag.Offset)
                {
                    readingLevel = true;
                }

                continue;
            }

            if (readingName && token.Kind == PvfTokenKind.QuotedString)
            {
                name = archive.ResolveString(token.Value);
                readingName = false;
                continue;
            }

            if (readingLevel)
            {
                if (draft is null || token.Kind != PvfTokenKind.Int32 || token.Value is < 0 or > byte.MaxValue)
                    throw new InvalidDataException($"PVF '{scriptPath}', field '{Fields.NeedLevelField}': expected valid Int32 level.");
                draft.MinimumLevel = Math.Max(draft.MinimumLevel, token.Value);
                readingLevel = false;
                continue;
            }
            if (draft is null || inPermission)
                continue;

            draft.Accept(archive, token);
        }

        if (draft is not null) throw new InvalidDataException($"PVF '{scriptPath}', field '{Fields.AreaField}': unclosed area.");
        return new TownDefinition(townId, name, areas);
    }

    private static MapGeometry? ReadGeometry(PvfArchive archive, string mapPath)
    {
        var normalized = mapPath.Replace('\\', '/');
        var fileIndex = archive.FindFile($"{MapDirectory}/{normalized}");
        if (fileIndex < 0)
            return null;

        var transit = RequireTag(archive, Fields.TownMovableAreaField);
        var transitClose = RequireTag(archive, Fields.CloseTownMovableAreaField);
        var walkable = RequireTag(archive, Fields.VirtualMovableAreaField);
        var walkableClose = RequireTag(archive, Fields.CloseVirtualMovableAreaField);

        var transitValues = new List<int>();
        var walkableValues = new List<int>();
        List<int>? active = null;
        var segmentStart = 0;
        var nested = 0;
        var reader = archive.OpenScript(fileIndex);

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (token.Value == transit)
                {
                    ValidateSegment();
                    active = transitValues;
                    segmentStart = active.Count;
                    nested = 0;
                }
                else if (token.Value == walkable)
                {
                    ValidateSegment();
                    active = walkableValues;
                    segmentStart = active.Count;
                    nested = 0;
                }
                else if (token.Value == transitClose || token.Value == walkableClose)
                {
                    ValidateSegment();
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

            if (active is not null && nested == 0)
            {
                if (token.Kind != PvfTokenKind.Int32)
                    throw new InvalidDataException($"PVF '{MapDirectory}/{normalized}', field '{(ReferenceEquals(active, transitValues) ? Fields.TownMovableAreaField.Name : Fields.VirtualMovableAreaField.Name)}': expected Int32 geometry.");
                active.Add(token.Value);
            }
        }

        ValidateSegment();
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

        void ValidateSegment()
        {
            if (active is null) return;
            var isTransit = ReferenceEquals(active, transitValues);
            var field = isTransit ? Fields.TownMovableAreaField : Fields.VirtualMovableAreaField;
            var width = isTransit ? TransitRowLength : WalkableRowLength;
            if (nested != 0 || (active.Count - segmentStart) % width != 0)
                throw new InvalidDataException($"PVF '{MapDirectory}/{normalized}', field '{field}': incomplete row or nested block in this declaration.");
        }
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
    private static int RequireTag(PvfArchive archive, PvfField tag)
    {
        var resolved = archive.GetTag(tag);
        return resolved.IsResolved
            ? resolved.Offset
            : throw new InvalidOperationException(
                $"The archive contains no '{tag}' tag, so map geometry cannot be read from it.");
    }

    private sealed class AreaDraft(string source)
    {
        public int MinimumLevel { get; set; }
        private static readonly PvfField<IReadOnlyList<PvfToken>> MinimapOpen = PvfField.Tokens("[minimap rect]", optional: true, tokenKind: PvfTokenKind.QuotedString);
        private static readonly PvfField<bool> MinimapClose = PvfField.Marker("[/minimap rect]", optional: true, tokenKind: PvfTokenKind.QuotedString);

        private readonly List<int> _operands = [];
        private int _areaId = TownAreaDefinition.NoOperand;
        private string _mapPath = string.Empty;
        private TownAreaKind _kind = TownAreaKind.Unrecognized;
        private bool _haveAreaId;
        private bool _haveKind;
        private bool _inMinimapRect;
        private int _minimapIntegers;
        private bool _haveMinimap;

        private InvalidDataException Invalid(PvfField field, string reason) =>
            new($"PVF '{source}', field '{field}': {reason}.");

        public void Accept(PvfArchive archive, in PvfToken token)
        {
            if (token.Kind == PvfTokenKind.Int32)
            {
                // The minimap rectangle is four integers of presentation data. Counting them
                // as operands would put a rectangle corner where a spawn position belongs.
                if (_inMinimapRect) { _minimapIntegers++; return; }

                if (!_haveAreaId)
                {
                    if (token.Value < 0) throw Invalid(Fields.AreaField, "area ID must be nonnegative");
                    _areaId = token.Value;
                    _haveAreaId = true;
                }
                else if (_mapPath.Length == 0) throw Invalid(Fields.AreaField, "expected map path after area ID");
                else if (_haveKind)
                {
                    // Only the numbers following the kind are its operands. Modifier markers
                    // carry their own numbers, and counting those would shift a gate's spawn
                    // position onto whatever the modifier happened to state.
                    _operands.Add(token.Value);
                }

                return;
            }

            if (token.Kind != MinimapOpen.TokenKind)
                throw Invalid(Fields.AreaField, $"unexpected {token.Kind}");

            var value = archive.ResolveString(token.Value).Trim();
            switch (value)
            {
                case var fieldName when MinimapOpen.Matches(fieldName):
                    if (!_haveAreaId || _mapPath.Length == 0 || _haveMinimap) throw Invalid(MinimapOpen, "rectangle must follow a complete area header and occur once");
                    _haveMinimap = true;
                    _inMinimapRect = true;
                    _minimapIntegers = 0;
                    return;
                case var fieldName when MinimapClose.Matches(fieldName):
                    if (!_inMinimapRect || _minimapIntegers != 4) throw Invalid(MinimapOpen, "expected four Int32 coordinates");
                    _inMinimapRect = false;
                    return;
            }

            if (_inMinimapRect) throw Invalid(MinimapOpen, "expected Int32 coordinate or closing marker");
            if (!_haveAreaId) throw Invalid(Fields.AreaField, "expected area ID before map path");
            if (_mapPath.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith('[')) throw Invalid(Fields.AreaField, "expected nonempty map path");
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

        public TownAreaDefinition ToDefinition()
        {
            if (_inMinimapRect) throw Invalid(MinimapOpen, "unclosed rectangle");
            if (!_haveAreaId || _mapPath.Length == 0) throw Invalid(Fields.AreaField, "incomplete area ID/map header");
            return new(_areaId, _mapPath, _kind,
                _operands.Count > 0 ? _operands[0] : TownAreaDefinition.NoOperand,
                _operands.Count > 1 ? _operands[1] : TownAreaDefinition.NoOperand) { MinimumLevel = MinimumLevel };
        }

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
            AreaOpen = archive.GetTag(Fields.AreaField).Offset;
            AreaClose = archive.GetTag(Fields.CloseAreaField).Offset;
            PermissionOpen = archive.GetTag(Fields.PermissionField).Offset;
            PermissionClose = archive.GetTag(Fields.ClosePermissionField).Offset;
            Name = archive.GetTag(Fields.NameField).Offset;
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
