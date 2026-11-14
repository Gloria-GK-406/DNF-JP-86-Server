using System.Collections.Frozen;

namespace Dfo.Pvf.Schema;

/// <summary>
/// Single-value item fields that the A21 client assigns: a repeated declaration overrides the earlier one
/// (ImportStackableScript 02999F40 and the equipment importer 0298B0A0; Document/tasks/package-opening-328).
/// Readers take the last occurrence of these. Every other field keeps refusing a repeat, because the client
/// decides repeats per handler (for example [usable job] accumulates and [package data] clears).
/// </summary>
public static class PvfRepeatedFields
{
    private static readonly FrozenSet<string> LaterWins = FrozenSet.ToFrozenSet(
        ["[attach type]", "[stack limit]", "[usable period]", "[equipment type]", "[stackable type]"], StringComparer.Ordinal);

    public static bool LaterDeclarationWins(string name) => LaterWins.Contains(name);

    /// <summary>The occurrence a reader uses, or null when a repeat of this field is malformed.</summary>
    public static T? Effective<T>(IReadOnlyList<T> found, string name) where T : class =>
        found.Count == 0 ? null : found.Count == 1 || LaterDeclarationWins(name) ? found[^1] : null;
}
