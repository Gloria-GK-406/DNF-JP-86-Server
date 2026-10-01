using Dfo.Pvf.Schema;
using System.Globalization;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Projects the archive's collection-box table into <see cref="CollectionBoxDefinition"/>.
/// </summary>
/// <remarks>
/// The table is a sequence of <c>[Collect Box Info]</c> blocks. Each declares a grid whose
/// first two integers are its width and height, followed by one item/tier pair per
/// position in row-major order.
/// </remarks>
internal sealed class PvfCollectionBoxDefinitionSource : ICollectionBoxDefinitionSource
{
    private static class Fields
    {
        internal static readonly PvfField<bool> CollectBoxInfoField = PvfField.Marker("[Collect Box Info]", optional: true);
        internal static readonly PvfField<bool> CloseCollectBoxInfoField = PvfField.Marker("[/Collect Box Info]", optional: true);
        internal static readonly PvfField<int> IndexField = PvfField.Int32("[Index]", optional: true);
        internal static readonly PvfField<string> BaseExpirationDateField = PvfField.String("[base expiration date]", optional: true);
        internal static readonly PvfField<string> MaxExpirationDateField = PvfField.String("[max expiration date]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> ExtendItemField = PvfField.Int32List("[Extend Item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> ItemPositionInfoField = PvfField.Int32List("[Item Position Info]", optional: true);
    }

    private const string TablePath = "localization/collectbox/collectboxinfo.etc";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private Catalog? _catalog;

    public PvfCollectionBoxDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<IReadOnlyList<CollectionBoxDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().Ordered);
    }

    public ValueTask<CollectionBoxDefinition?> GetAsync(int boxIndex, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().ByIndex.GetValueOrDefault(boxIndex));
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
        var fileIndex = archive.FindFile(TablePath);
        if (fileIndex < 0)
            throw new InvalidOperationException($"The archive does not contain {TablePath}.");

        var ordered = new List<CollectionBoxDefinition>();
        var byIndex = new Dictionary<int, CollectionBoxDefinition>();
        Draft? draft = null;
        foreach (var section in PvfDocument.Open(archive, TablePath).Sections)
        {
            if (Fields.CollectBoxInfoField.Matches(section.Name))
            {
                Fields.CollectBoxInfoField.Decode(archive, section.Values, TablePath);
                if (draft is not null) throw new InvalidDataException($"{TablePath}: nested or unclosed collection box.");
                draft = new Draft();
            }
            else if (Fields.CloseCollectBoxInfoField.Matches(section.Name))
            {
                Fields.CloseCollectBoxInfoField.Decode(archive, section.Values, TablePath);
                if (draft is null) throw new InvalidDataException($"{TablePath}: collection box close without opening.");
                var definition = draft.ToDefinition();
                ordered.Add(definition); byIndex.TryAdd(definition.BoxIndex, definition); draft = null;
            }
            else if (Fields.IndexField.Matches(section.Name))
                RequireDraft(draft).BoxIndex = Fields.IndexField.Decode(archive, section.Values, TablePath);
            else if (Fields.BaseExpirationDateField.Matches(section.Name))
                RequireDraft(draft).BaseExpiresAt = ParseDate(Fields.BaseExpirationDateField.Decode(archive, section.Values, TablePath), Fields.BaseExpirationDateField);
            else if (Fields.MaxExpirationDateField.Matches(section.Name))
                RequireDraft(draft).MaximumExpiresAt = ParseDate(Fields.MaxExpirationDateField.Decode(archive, section.Values, TablePath), Fields.MaxExpirationDateField);
            else if (Fields.ExtendItemField.Matches(section.Name))
            {
                var values = Fields.ExtendItemField.Decode(archive, section.Values, TablePath);
                if (values.Count % 2 != 0) throw new InvalidDataException($"{TablePath}: {Fields.ExtendItemField.Name} requires item/count pairs.");
                RequireDraft(draft).ExtendOperands.AddRange(values);
            }
            else if (Fields.ItemPositionInfoField.Matches(section.Name))
                RequireDraft(draft).PositionOperands.AddRange(Fields.ItemPositionInfoField.Decode(archive, section.Values, TablePath));
        }
        if (draft is not null) throw new InvalidDataException($"{TablePath}: unclosed collection box.");

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{TablePath} declared no collection boxes.");

        return new Catalog(ordered, byIndex);
    }

    /// <summary>
    /// Dates are stored as local wall-clock text with no zone. They are read as unspecified
    /// rather than assumed to be UTC, so no zone is invented here.
    /// </summary>
    private static Draft RequireDraft(Draft? draft) => draft
        ?? throw new InvalidDataException($"{TablePath}: collection field outside a collection box.");

    private static DateTimeOffset ParseDate(string value, PvfField<string> field)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out var parsed))
            throw new InvalidDataException($"{TablePath}: {field.Name} contains an invalid date.");
        return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.Zero);
    }

    private sealed class Draft
    {
        public int BoxIndex { get; set; } = -1;

        public DateTimeOffset? BaseExpiresAt { get; set; }

        public DateTimeOffset? MaximumExpiresAt { get; set; }

        public List<int> ExtendOperands { get; } = [];

        public List<int> PositionOperands { get; } = [];

        public CollectionBoxDefinition ToDefinition()
        {
            if (PositionOperands.Count < 2 || PositionOperands[0] <= 0 || PositionOperands[1] <= 0 ||
                PositionOperands.Count != 2L + 2L * PositionOperands[0] * PositionOperands[1])
                throw new InvalidDataException($"{TablePath}: {Fields.ItemPositionInfoField.Name} requires width/height and one item/tier pair per position.");
            var columns = PositionOperands[0];
            var rows = PositionOperands[1];

            var slots = new List<CollectionBoxSlot>();
            for (var i = 2; i + 1 < PositionOperands.Count; i += 2)
            {
                var position = (i - 2) / 2;
                if (columns <= 0)
                    break;

                slots.Add(new CollectionBoxSlot(
                    position % columns,
                    position / columns,
                    PositionOperands[i],
                    PositionOperands[i + 1]));
            }

            var extensions = new List<CollectionBoxExtension>();
            for (var i = 0; i + 1 < ExtendOperands.Count; i += 2)
                extensions.Add(new CollectionBoxExtension(ExtendOperands[i], ExtendOperands[i + 1]));

            return new CollectionBoxDefinition(
                BoxIndex, columns, rows, BaseExpiresAt, MaximumExpiresAt, slots, extensions);
        }
    }

    private sealed record Catalog(
        IReadOnlyList<CollectionBoxDefinition> Ordered,
        Dictionary<int, CollectionBoxDefinition> ByIndex);
}
