using System.Collections.ObjectModel;
using Dfo.Pvf.Tokens;

namespace Dfo.Pvf;

/// <summary>How an ID lookup treats repeated declarations; ordered rows always retain them.</summary>
public enum PvfListDuplicates { Reject, First, Last }

/// <summary>An archive-owned, immutable list and its shared ID/path views.</summary>
public sealed class PvfListIndex
{
    private readonly Lock gate = new();
    private readonly Dictionary<(PvfListDuplicates Duplicates, string Prefix), IReadOnlyDictionary<int, string>> paths = [];
    private IReadOnlyDictionary<int, (int Id, string RelativePath)>? entriesById;
    private bool? hasDuplicates;
    public IReadOnlyList<(int Id, string RelativePath)> Entries { get; }

    internal PvfListIndex(PvfArchive archive, int fileIndex, string listPath)
    {
        var entries = new List<(int Id, string RelativePath)>();
        var reader = archive.OpenScript(fileIndex);
        var pendingId = int.MinValue;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32) pendingId = token.Value;
            else if (token.IsString && pendingId != int.MinValue)
            {
                var name = archive.ResolveString(token.Value).Replace('\\', '/');
                if (name.Length > 0) entries.Add((pendingId, name));
                pendingId = int.MinValue;
            }
        }
        if (entries.Count == 0) throw new InvalidOperationException($"{listPath} declared no entries.");
        Entries = entries.AsReadOnly();
    }

    public IReadOnlyDictionary<int, string> Paths(PvfListDuplicates duplicates = PvfListDuplicates.Reject, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (!Enum.IsDefined(duplicates)) throw new ArgumentOutOfRangeException(nameof(duplicates));
        lock (gate)
        {
            if (paths.TryGetValue((duplicates, prefix), out var cached)) return cached;
            if (prefix.Length > 0)
                cached = new ReadOnlyDictionary<int, string>(Paths(duplicates).ToDictionary(x => x.Key, x => prefix + x.Value));
            else
            {
                var result = new Dictionary<int, string>();
                foreach (var (id, path) in Entries)
                    switch (duplicates)
                    {
                        case PvfListDuplicates.Reject: result.Add(id, path); break;
                        case PvfListDuplicates.First: result.TryAdd(id, path); break;
                        case PvfListDuplicates.Last: result[id] = path; break;
                    }
                cached = new ReadOnlyDictionary<int, string>(result);
                hasDuplicates ??= result.Count != Entries.Count;
            }
            if (hasDuplicates == false)
                foreach (var policy in Enum.GetValues<PvfListDuplicates>()) paths.TryAdd((policy, prefix), cached);
            else paths.Add((duplicates, prefix), cached);
            return cached;
        }
    }

    public IReadOnlyDictionary<int, (int Id, string RelativePath)> EntriesById()
    {
        lock (gate)
            return entriesById ??= new ReadOnlyDictionary<int, (int Id, string RelativePath)>(Entries.ToDictionary(x => x.Id));
    }
}
