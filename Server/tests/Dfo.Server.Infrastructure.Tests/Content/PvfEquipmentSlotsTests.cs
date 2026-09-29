using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// Which slot each equipment-type name owns, held still.
/// </summary>
/// <remarks>
/// The creature's name was added to this table so a creature taken off can be put back on, and the
/// one thing that change was not allowed to do is move anything else. There is no live sample
/// behind most of these numbers, so a slot that drifted would not fail anywhere a player could
/// see -- it would simply dress a character differently from the reference server and stay that
/// way. Written out one name at a time so a drift fails on the name that drifted.
/// <para>
/// The creature's own entry is deliberately not asserted here. What it is for is the round trip in
/// <c>CreatureGoesBackOnTests</c>, and stating it twice would make removing the entry fail in two
/// places while telling nobody anything the round trip does not already say.
/// </para>
/// </remarks>
public sealed class PvfEquipmentSlotsTests
{
    [Fact]
    public void Every_name_that_had_a_slot_before_the_creature_was_named_still_has_that_slot()
    {
        Assert.Equal(12, PvfEquipmentSlots.ByName["[weapon]"]);
        Assert.Equal(13, PvfEquipmentSlots.ByName["[title]"]);
        Assert.Equal(13, PvfEquipmentSlots.ByName["[title name]"]);
        Assert.Equal(14, PvfEquipmentSlots.ByName["[coat]"]);
        Assert.Equal(15, PvfEquipmentSlots.ByName["[shoulder]"]);
        Assert.Equal(16, PvfEquipmentSlots.ByName["[pants]"]);
        Assert.Equal(17, PvfEquipmentSlots.ByName["[shoes]"]);
        Assert.Equal(18, PvfEquipmentSlots.ByName["[waist]"]);
        Assert.Equal(19, PvfEquipmentSlots.ByName["[amulet]"]);
        Assert.Equal(20, PvfEquipmentSlots.ByName["[wrist]"]);
        Assert.Equal(21, PvfEquipmentSlots.ByName["[ring]"]);
        Assert.Equal(22, PvfEquipmentSlots.ByName["[support]"]);
        Assert.Equal(23, PvfEquipmentSlots.ByName["[magic stone]"]);
        Assert.Equal(24, PvfEquipmentSlots.ByName["[earring]"]);
        Assert.Equal(24, PvfEquipmentSlots.ByName["[support weapon]"]);
    }

    /// <summary>
    /// Every name in the table is either a costume slot or a worn one, and the worn band is unchanged.
    /// </summary>
    /// <remarks>
    /// Naming the creature could have been done by widening the container instead, and that would
    /// have been a different change with different consequences on the wire. It was not: the
    /// creature was given a slot the container already had. The same is true of the costumes named
    /// on 2026-09-08 -- slots <c>0..10</c> were already part of the equipment container and were
    /// simply standing empty.
    /// <para>
    /// This used to assert that <em>every</em> name fell in the worn band, which is how the table
    /// looked while the costume slots had no names. It now says the thing that assertion was really
    /// protecting: the two bands are what the table covers, the worn one is untouched, and nothing
    /// sits between or beyond them. Slot eleven is in neither set on purpose -- the reference
    /// server has a name for it and this archive has no template that asks for one.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_name_is_a_costume_slot_or_a_worn_one_and_the_worn_band_is_unchanged()
    {
        Assert.Equal(12, PvfEquipmentSlots.FirstWornSlot);
        Assert.Equal(25, PvfEquipmentSlots.LastWornSlot);

        var costumes = PvfEquipmentSlots.ByName
            .Where(pair => pair.Key.EndsWith("avatar]", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var worn = PvfEquipmentSlots.ByName.Except(costumes).ToArray();

        Assert.Equal(11, costumes.Length);
        Assert.All(costumes, pair => Assert.InRange(pair.Value, (byte)0, (byte)10));
        Assert.Equal(
            Enumerable.Range(0, 11).Select(value => (byte)value),
            costumes.Select(pair => pair.Value).OrderBy(value => value));
        Assert.All(worn, pair => Assert.InRange(pair.Value, (byte)12, (byte)28));
        Assert.Equal(new Dfo.Server.Data.Inventory.BusinessSlot(
            Dfo.Server.Data.Inventory.BusinessInventorySpace.WornCreature, 0),
            PvfEquipmentSlots.BusinessByName["[creature]"]);
    }

    /// <summary>
    /// Character creation names only slots this change did not touch.
    /// </summary>
    /// <remarks>
    /// This table has two readers, and the other one is the creation table's: a job's
    /// <c>[create equipment list]</c> names slots by the same spellings. Naming a new one here is
    /// therefore only safe while no job's creation list uses that name, which is what this holds --
    /// every job in the archive names exactly weapon, coat and pants, so what a character is
    /// created wearing could not have changed.
    /// </remarks>
    [SkippableFact]
    public async Task No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfInitialEquipmentDefinitionSource(provider);

        var seen = new HashSet<byte>();
        for (byte job = 0; job < 32; job++)
        {
            foreach (var grant in await source.GetAsync(job, CancellationToken.None))
                seen.Add(grant.SlotIndex);
        }

        var expected = new byte[]
        {
            PvfEquipmentSlots.ByName["[weapon]"],
            PvfEquipmentSlots.ByName["[coat]"],
            PvfEquipmentSlots.ByName["[pants]"],
        };

        Assert.Equal(expected.Order().ToArray(), seen.Order().ToArray());
    }
}
