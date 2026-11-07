namespace Dfo.Server.Data.Inventory;

/// <summary>Which item shapes may change template in place, keeping their UID and occupancy.</summary>
/// <remarks>
/// A pet evolves into its next template; growth and emancipation weapons (dragon-leap stars,
/// requiem awakening) turn into the template their <c>[emancipate]</c> block names. Everything else
/// keeps its template for life, so a template change there is a corrupted update. The storage
/// identity checks still require the shape itself — pile, traits, legacy kind — to stay the same.
/// </remarks>
public static class ItemTemplateTransition
{
    public static bool Allowed(ItemSnapshot next) =>
        !next.IsPile && (next.Type == ItemTraits.Creature && next.LegacyKind == 5
            || next.Type == ItemTraits.Equipment);
}
