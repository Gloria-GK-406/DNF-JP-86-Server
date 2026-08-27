namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// The slot each of the archive's equipment-type names occupies.
/// </summary>
/// <remarks>
/// Zero through eleven are the avatar slots, so the worn pieces begin at twelve. A21 inserted the
/// aurora-illusion avatar ahead of the weapon, which is why these are not the numbers an older
/// client used and why they are written out rather than derived from an ordering.
/// <para>
/// Shared because it is one table with two readers, and they read it from two different places in
/// the archive. Character creation names a slot and then the item that fills it; a wearable names
/// its own slot under <c>[equipment type]</c>. The names are the same either way, which is what
/// makes a piece the creation table puts in slot 15 the same piece the player may drag there.
/// </para>
/// <para>
/// <c>[earring]</c> and <c>[support weapon]</c> are both 24. The two names are the same slot under
/// two spellings; the reference server's own enum calls it SupportWeapon and the creation tables
/// use both. See <c>Docs/ProtocolScaffoldDissonances.md</c>.
/// </para>
/// </remarks>
internal static class PvfEquipmentSlots
{
    public static readonly IReadOnlyDictionary<string, byte> ByName =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["[weapon]"] = 12,
            ["[title]"] = 13,
            ["[title name]"] = 13,
            ["[coat]"] = 14,
            ["[shoulder]"] = 15,
            ["[pants]"] = 16,
            ["[shoes]"] = 17,
            ["[waist]"] = 18,
            ["[amulet]"] = 19,
            ["[wrist]"] = 20,
            ["[ring]"] = 21,
            ["[support]"] = 22,
            ["[magic stone]"] = 23,
            ["[earring]"] = 24,
            ["[support weapon]"] = 24,
        };

    /// <summary>The first slot a worn piece may occupy; everything below is an avatar.</summary>
    public const byte FirstWornSlot = 12;

    /// <summary>The last slot the equipment container has.</summary>
    public const byte LastWornSlot = 24;
}
