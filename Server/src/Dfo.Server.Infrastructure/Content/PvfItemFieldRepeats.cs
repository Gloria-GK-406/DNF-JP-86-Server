using System.Collections.Frozen;
using Dfo.Pvf.Schema;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Repeat policy of the item fields. The A21 importers (ImportStackableScript 02999F40, equipment 0298B0A0)
/// assign these single-value fields, so a repeated declaration replaces the earlier one.
/// The client decides repeats per handler ([usable job] accumulates, [package data] clears), so every other
/// item field keeps refusing a repeat.
/// </summary>
internal static class PvfItemFieldRepeats
{
    private static readonly FrozenSet<string> Assigned = FrozenSet.ToFrozenSet(
        ["[attach type]", "[stack limit]", "[usable period]", "[equipment type]", "[stackable type]", "[cool time]"], StringComparer.Ordinal);

    public static PvfRepeats For(PvfField field) => Assigned.Contains(field.Name) ? PvfRepeats.LastWins : PvfRepeats.Reject;
}
