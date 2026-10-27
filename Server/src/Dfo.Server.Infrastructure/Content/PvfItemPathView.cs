using System.Collections;
using Dfo.Pvf;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>A zero-copy view of the strict equipment/stackable indexes used by item metadata readers.</summary>
internal sealed class PvfItemPathView : IReadOnlyDictionary<int, string>
{
    private readonly IReadOnlyDictionary<int, string> equipment;
    private readonly IReadOnlyDictionary<int, string> stackables;

    public PvfItemPathView(PvfArchive archive)
    {
        equipment = PvfListFile.Paths(archive, "equipment/equipment.lst", prefix: "equipment/");
        stackables = PvfListFile.Paths(archive, "stackable/stackable.lst", prefix: "stackable/");
        foreach (var id in stackables.Keys)
            if (equipment.ContainsKey(id)) throw new ArgumentException($"Duplicate item identifier {id}.");
    }

    public int Count => equipment.Count + stackables.Count;
    public IEnumerable<int> Keys => equipment.Keys.Concat(stackables.Keys);
    public IEnumerable<string> Values => equipment.Values.Concat(stackables.Values);
    public string this[int key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
    public bool ContainsKey(int key) => equipment.ContainsKey(key) || stackables.ContainsKey(key);
    public bool TryGetValue(int key, out string value) => equipment.TryGetValue(key, out value!) || stackables.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<int, string>> GetEnumerator() => equipment.Concat(stackables).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
