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
}
