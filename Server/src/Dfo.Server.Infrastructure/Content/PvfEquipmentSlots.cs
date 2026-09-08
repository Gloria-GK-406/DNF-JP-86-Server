namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// The slot each of the archive's equipment-type names occupies.
/// </summary>
/// <remarks>
/// Zero through eleven are the avatar slots, so the worn pieces begin at twelve. That is why these
/// are not the numbers an older client used, and why they are written out rather than derived from
/// an ordering.
/// <para>
/// <b>The avatar slots stood empty until 2026-09-08</b>, which is the whole reason a costume could
/// not be worn: a piece is put on the slot its own <c>[equipment type]</c> names, the name was
/// looked up here, nothing came back, and the drag was refused as unwearable. Eleven names fill
/// them now. Their numbers were measured against a live client -- eleven costumes dragged onto the
/// costume bar one at a time, each refusal recording the slot the client had asked for -- and the
/// names come from reading each of those templates' own <c>[equipment type]</c> out of the archive.
/// The reference server's <c>EquipmentType</c> enum agrees with both, name for name and number for
/// number, which is a third source and not the one either of the first two came from.
/// </para>
/// <para>
/// <b>Slot eleven has no name here because no template in this archive asks for one.</b> The
/// reference enum calls it <c>AuraSkinAvatar</c> (also spelled <c>AuroraIllusionAvatar</c>), and
/// the whole equipment index was searched for it: 118479 entries, eleven distinct
/// <c>[equipment type]</c> names ending in <c>avatar]</c>, 66328 templates between them, and no
/// twelfth. That is a fact about this archive rather than about the protocol -- a different
/// archive would have to be searched again.
/// </para>
/// <para>
/// An earlier draft of this remark said A21 inserted the aurora-illusion avatar <em>ahead of</em>
/// the weapon. It does not: the reference enum puts <c>WeaponAvatar</c> at ten and
/// <c>AuraSkinAvatar</c> at eleven, so it comes after. The same sentence is in
/// <c>protocol/game/server-to-client/NOTI-0x0002.md</c> and is corrected there too.
/// </para>
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
/// <para>
/// <c>[creature]</c> is 24 as well, because 24 is the slot this server already keeps a worn
/// creature in -- <c>SelectCharacterPostEntryServices.EquippedCreatureSlot</c> reads it there and
/// the entry codec writes the creature marker for it there. Its absence from this table was the
/// whole of the asymmetry: taking a creature off went through the bag's own rule and was allowed,
/// while putting one on asked here, got nothing, and was refused as unwearable. Naming it does not
/// decide the open question of whether 24 or 25 is the creature's slot -- that needs a live sample
/// of a character actually wearing one, and the ninth section of
/// <c>Docs/ProtocolScaffoldDissonances.md</c> is where it stays until there is one.
/// </para>
/// </remarks>
internal static class PvfEquipmentSlots
{
    public static readonly IReadOnlyDictionary<string, byte> ByName =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["[hat avatar]"] = 0,
            ["[hair avatar]"] = 1,
            ["[face avatar]"] = 2,
            ["[coat avatar]"] = 3,
            ["[pants avatar]"] = 4,
            ["[shoes avatar]"] = 5,
            ["[breast avatar]"] = 6,
            ["[waist avatar]"] = 7,
            ["[skin avatar]"] = 8,
            ["[aurora avatar]"] = 9,
            ["[weapon avatar]"] = 10,
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
            ["[creature]"] = 24,
        };

    /// <summary>The first slot a worn piece may occupy; everything below is an avatar.</summary>
    public const byte FirstWornSlot = 12;

    /// <summary>The last slot the equipment container has.</summary>
    public const byte LastWornSlot = 24;
}
