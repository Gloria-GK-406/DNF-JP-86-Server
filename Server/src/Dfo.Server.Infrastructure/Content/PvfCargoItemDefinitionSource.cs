using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfCargoItemDefinitionSource(PvfArchiveProvider provider) : ICargoItemDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static readonly PvfField<string> Attach = PvfField.String("[attach type]", optional: true);
    private static readonly PvfField<IReadOnlyList<string>> Impossible = PvfField.Strings("[impossible contents]", optional: true);
    private readonly Lock gate = new();
    private readonly Dictionary<int, CargoItemDefinition?> cache = [];
    private Dictionary<int, string>? paths;

    public string Name => "cargo-item-index";

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
        paths ??= new[] { "equipment", "stackable" }.SelectMany(category =>
                PvfListFile.Read(archive, $"{category}/{category}.lst")
                    .Select(entry => new KeyValuePair<int, string>(entry.Id, category + "/" + entry.RelativePath)))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    public ValueTask<CargoItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!cache.TryGetValue(itemId, out var value)) cache[itemId] = value = Read(itemId);
            return ValueTask.FromResult(value);
        }
    }

    private CargoItemDefinition? Read(int itemId)
    {
        var archive = provider.Archive;
        PathsIndex(archive);
        if (!paths.TryGetValue(itemId, out var path)) return null;
        var document = PvfDocument.Open(archive, path);
        var attach = document.TryRead(Attach, out var raw) ? Normalize(raw) : string.Empty;
        var impossible = document.TryRead(Impossible, out var declarations)
            ? declarations.Select(Normalize).ToFrozenSet(StringComparer.Ordinal) : FrozenSet<string>.Empty;
        return new(attach, impossible);
    }

    private static string Normalize(string value) => value.Trim().Trim('[', ']').Trim().ToLowerInvariant();
}
