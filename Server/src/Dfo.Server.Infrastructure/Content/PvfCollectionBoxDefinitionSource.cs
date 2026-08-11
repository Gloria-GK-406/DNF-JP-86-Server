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

        var open = archive.GetTag("[Collect Box Info]").Offset;
        var close = archive.GetTag("[/Collect Box Info]").Offset;
        var indexTag = archive.GetTag("[Index]").Offset;
        var baseDate = archive.GetTag("[base expiration date]").Offset;
        var maxDate = archive.GetTag("[max expiration date]").Offset;
        var extendItem = archive.GetTag("[Extend Item]").Offset;
        var positions = archive.GetTag("[Item Position Info]").Offset;

        var ordered = new List<CollectionBoxDefinition>();
        var byIndex = new Dictionary<int, CollectionBoxDefinition>();
        var reader = archive.OpenScript(fileIndex);

        Draft? draft = null;
        var section = -1;

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (token.Value == open)
                {
                    draft = new Draft();
                    section = -1;
                }
                else if (token.Value == close && draft is not null)
                {
                    var definition = draft.ToDefinition();
                    ordered.Add(definition);
                    byIndex.TryAdd(definition.BoxIndex, definition);
                    draft = null;
                }
                else
                {
                    section = token.Value;
                }

                continue;
            }

            if (draft is null)
                continue;

            if (section == indexTag && token.Kind == PvfTokenKind.Int32)
                draft.BoxIndex = token.Value;
            else if (section == baseDate && token.IsString)
                draft.BaseExpiresAt = ParseDate(archive.ResolveString(token.Value));
            else if (section == maxDate && token.IsString)
                draft.MaximumExpiresAt = ParseDate(archive.ResolveString(token.Value));
            else if (section == extendItem && token.Kind == PvfTokenKind.Int32)
                draft.ExtendOperands.Add(token.Value);
            else if (section == positions && token.Kind == PvfTokenKind.Int32)
                draft.PositionOperands.Add(token.Value);
        }

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{TablePath} declared no collection boxes.");

        return new Catalog(ordered, byIndex);
    }

    /// <summary>
    /// Dates are stored as local wall-clock text with no zone. They are read as unspecified
    /// rather than assumed to be UTC, so no zone is invented here.
    /// </summary>
    private static DateTimeOffset? ParseDate(string value) =>
        DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.NoCurrentDateDefault,
            out var parsed)
            ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.Zero)
            : null;

    private sealed class Draft
    {
        public int BoxIndex { get; set; } = -1;

        public DateTimeOffset? BaseExpiresAt { get; set; }

        public DateTimeOffset? MaximumExpiresAt { get; set; }

        public List<int> ExtendOperands { get; } = [];

        public List<int> PositionOperands { get; } = [];

        public CollectionBoxDefinition ToDefinition()
        {
            var columns = PositionOperands.Count >= 2 ? PositionOperands[0] : 0;
            var rows = PositionOperands.Count >= 2 ? PositionOperands[1] : 0;

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
