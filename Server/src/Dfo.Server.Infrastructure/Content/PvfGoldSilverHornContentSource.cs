using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfGoldSilverHornContentSource(PvfArchiveProvider provider) : IGoldSilverHornContentSource
{
    private readonly Lazy<GoldSilverHornContent> cache = new(() => Read(provider.Archive));
    public GoldSilverHornContent Load() => cache.Value;

    internal static GoldSilverHornContent Read(PvfArchive archive)
    {
        const string path = "etc/chn_goldsilverhorndungeon.etc";
        int? dungeon = null, maximum = null;
        var silver = false;
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            // This archive closes the silver block by repeating its opening marker.
            if (section.Name == "[silver horn dungeon]") { silver = !silver; continue; }
            if (section.Name == "[/silver horn dungeon]") { silver = false; continue; }
            if (section.Name == "[gold horn dungeon]")
                throw new InvalidDataException("A configured legacy gold slot requires its own quota projection.");
            if (!silver || section.Name is not ("[dungeon index]" or "[max enter count]")) continue;
            var values = PvfField.Int32List(section.Name).Decode(archive, section.Values, path);
            if (values.Count != 1 || values[0] <= 0 || values[0] > ushort.MaxValue)
                throw new InvalidDataException($"{path}: invalid {section.Name}.");
            if (section.Name == "[dungeon index]" && dungeon is null) dungeon = values[0];
            else if (section.Name == "[max enter count]" && maximum is null) maximum = values[0];
            else throw new InvalidDataException($"{path}: duplicate {section.Name}.");
        }
        return !silver && dungeon is { } id && maximum is { } count
            ? new(id, count) : throw new InvalidDataException($"{path}: incomplete silver horn configuration.");
    }
}
