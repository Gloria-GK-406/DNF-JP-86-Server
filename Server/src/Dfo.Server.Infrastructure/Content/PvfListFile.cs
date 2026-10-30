using Dfo.Pvf;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Shared archive list rows and explicit duplicate policies for ID/path views.</summary>
internal static class PvfListFile
{
    public static IReadOnlyList<(int Id, string RelativePath)> Read(PvfArchive archive, string listPath) =>
        archive.ReadList(listPath).Entries;

    public static IReadOnlyDictionary<int, string> Paths(PvfArchive archive, string listPath,
        PvfListDuplicates duplicates = PvfListDuplicates.Reject, string prefix = "") =>
        archive.ReadList(listPath).Paths(duplicates, prefix);

    public static IReadOnlyDictionary<int, (int Id, string RelativePath)> EntriesById(PvfArchive archive, string listPath) =>
        archive.ReadList(listPath).EntriesById();

    /// <summary>
    /// Positive IDs with a non-blank path. An ID the list declares twice is ambiguous and left out
    /// rather than resolved to either declaration.
    /// </summary>
    public static Dictionary<int, string> Usable(PvfArchive archive, string listPath)
    {
        var entries = Read(archive, listPath).Where(entry => entry.Id > 0 && !string.IsNullOrWhiteSpace(entry.RelativePath)).ToArray();
        var ambiguous = entries.GroupBy(entry => entry.Id).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        return entries.Where(entry => !ambiguous.Contains(entry.Id)).ToDictionary(entry => entry.Id, entry => entry.RelativePath);
    }

    /// <summary>The script a directory's own list names for an ID: <c>dir/dir.lst</c>, first declaration wins.</summary>
    public static string Resolve(PvfArchive archive, string directory, int id) =>
        Paths(archive, $"{directory}/{directory}.lst", PvfListDuplicates.First).TryGetValue(id, out var path)
            ? $"{directory}/{path}"
            : throw new InvalidDataException($"PVF '{directory}': missing identifier {id}.");
}
