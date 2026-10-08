using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEpicBookDefinitionSource(PvfArchiveProvider provider) : IEpicBookDefinitionSource
{
    private readonly Lazy<IReadOnlyList<EpicPieceDefinition>> catalog = new(() => Read(provider));
    public IReadOnlyList<EpicPieceDefinition> Load() => catalog.Value;
    private static IReadOnlyList<EpicPieceDefinition> Read(PvfArchiveProvider provider)
    {
        const string path = "etc/epicpieceinfo.etc";
        var archive = provider.Archive;
        if (archive.FindFile(path) < 0) return [];
        var document = PvfDocument.Open(archive, path);
        var section = document.Sections.Single(x => x.Name == "[piece list]");
        var values = PvfField.Int32List(section.Name).Decode(archive, section.Values, path).ToArray();
        if (values.Length % 2 != 0 || values.Any(x => x <= 0)) throw new InvalidDataException("Invalid epic piece pairs.");
        var entries = new List<EpicPieceDefinition>();
        for (var at = 0; at < values.Length; at += 2) entries.Add(new(values[at + 1], values[at]));
        return entries.AsReadOnly();
    }
}
