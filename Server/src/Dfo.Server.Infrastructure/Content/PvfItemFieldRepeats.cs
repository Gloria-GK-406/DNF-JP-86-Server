using System.Collections.Frozen;
using Dfo.Pvf.Schema;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Repeat policy of the item fields. The A21 importers (ImportStackableScript 02999F40, equipment 0298B0A0)
/// assign these five single-value fields, so a repeated declaration replaces the earlier one; the archive
/// repeats them 95 times, all at the top level (Document/tasks/package-opening-328/refs/a21-duplicate-fields.md).
/// The client decides repeats per handler ([usable job] accumulates, [package data] clears), so every other
/// item field keeps refusing a repeat.
/// </summary>
internal static class PvfItemFieldRepeats
{
    private static readonly FrozenSet<string> Assigned = FrozenSet.ToFrozenSet(
        ["[attach type]", "[stack limit]", "[usable period]", "[equipment type]", "[stackable type]"], StringComparer.Ordinal);

    public static PvfRepeats For(PvfField field) => Assigned.Contains(field.Name) ? PvfRepeats.LastWins : PvfRepeats.Reject;
}
