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
    /// The band the worn pieces occupy is unchanged, and no name reaches outside it.
    /// </summary>
    /// <remarks>
    /// Naming the creature could have been done by widening the container instead, and that would
    /// have been a different change with different consequences on the wire. It was not: the
    /// creature was given a slot the container already had.
    /// </remarks>
    [Fact]
    public void The_worn_band_is_unchanged_and_holds_every_name_in_the_table()
    {
        Assert.Equal(12, PvfEquipmentSlots.FirstWornSlot);
        Assert.Equal(24, PvfEquipmentSlots.LastWornSlot);
        Assert.All(
            PvfEquipmentSlots.ByName.Values,
            slot => Assert.InRange(
                slot, PvfEquipmentSlots.FirstWornSlot, PvfEquipmentSlots.LastWornSlot));
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
