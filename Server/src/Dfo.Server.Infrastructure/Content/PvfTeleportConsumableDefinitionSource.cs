using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfTeleportConsumableDefinitionSource(PvfArchiveProvider provider)
    : ITeleportConsumableDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<PvfToken>> StackableType = PvfField.Tokens("[stackable type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> TownIds = PvfField.Int32List("[int data]");
    }
    private readonly Lock gate = new();
    private readonly Dictionary<int, TeleportConsumableDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? paths;
    public string Name => "teleport-consumable-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var archive = provider.Archive;
            PathsIndex(archive);
            return ValueTask.FromResult(paths.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(paths))]
    private void PathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        paths ??= PvfListFile.Read(archive, "stackable/stackable.lst").ToDictionary(x => x.Id, x => "stackable/" + x.RelativePath);
    }

    public ValueTask<TeleportConsumableDefinition?> GetAsync(int itemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out var found)) return ValueTask.FromResult(found);
            var archive = provider.Archive;
            PathsIndex(archive);
            found = null;
            if (paths.TryGetValue(itemId, out var path))
            {
                var document = PvfDocument.Open(archive, path).AtRoot();
                if (document.TryRead(Fields.StackableType, out var type) && type.Count > 0
                    && type[0].IsString && archive.ResolveString(type[0].Value) == "[teleport potion]")
                {
                    // Only town-selection potion variant 0 is implemented.
                    if (type.Count == 2 && type[1].Kind == PvfTokenKind.Int32 && type[1].Value == 0)
                    {
                        var towns = document.Read(Fields.TownIds);
                        if (towns.Count == 0 || towns.Any(x => x is <= 0 or > byte.MaxValue))
                            throw new InvalidDataException($"PVF '{path}', field '{Fields.TownIds}': expected nonempty valid town IDs.");
                        found = new(towns.Distinct().ToArray());
                    }
                }
            }
            cache[itemId] = found;
            return ValueTask.FromResult(found);
        }
    }
}
