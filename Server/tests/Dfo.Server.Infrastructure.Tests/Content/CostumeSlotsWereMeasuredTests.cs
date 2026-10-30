using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The slot each costume is worn in, asked of the archive one template at a time.
/// </summary>
/// <remarks>
/// These eleven numbers are the only ones in <c>PvfEquipmentSlots.ByName</c> that were <b>measured
/// rather than read</b>. A live client was given the eleven pieces of one class's costume set and
/// each was dragged onto the costume bar in turn; the server could not place any of them, and every
/// refusal recorded the slot the client had asked for. Eleven pieces, eleven different slots,
/// <c>0..10</c> with no gap.
/// <para>
/// The names beside them come from the archive -- each template's own <c>[equipment type]</c> --
/// and the reference server's <c>EquipmentType</c> enum agrees with both, name for name and number
/// for number. Three sources, none derived from another.
/// </para>
/// <para>
/// The cases go from a template through the production reader rather than asserting the dictionary,
/// because the dictionary is not what a costume asks: it asks the archive what it is, and the
/// archive's answer is looked up. A table right and a lookup wrong would pass an assertion on the
/// table alone.
/// </para>
/// </remarks>
public sealed class CostumeSlotsWereMeasuredTests
{
    /// <summary>The pieces that were dragged, and the slot the client asked for each time.</summary>
    /// <remarks>
    /// One class's set, which is what a live account had. The coat is the piece with the most
    /// behind it: three separate drags -- twice out of the bag and once out of the avatar list, from
    /// three different source slots -- all named slot three.
    /// </remarks>
    public static TheoryData<int, byte, string> WhatWasMeasured() => new()
    {
        { 413550000, 0, "[hat avatar]" },
        { 413560000, 1, "[hair avatar]" },
        { 413570000, 2, "[face avatar]" },
        { 413500000, 3, "[coat avatar]" },
        { 413510000, 4, "[pants avatar]" },
        { 413540000, 5, "[shoes avatar]" },
        { 413520000, 6, "[breast avatar]" },
        { 413530000, 7, "[waist avatar]" },
        { 413580000, 8, "[skin avatar]" },
        { 413590000, 9, "[aurora avatar]" },
        { 413600000, 10, "[weapon avatar]" },
    };

    [SkippableTheory]
    [MemberData(nameof(WhatWasMeasured))]
    public async Task A_costume_is_worn_in_the_slot_it_was_measured_in(
        int itemId, byte slot, string equipmentType)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var read = await new PvfEquipmentSlotDefinitionSource(provider)
            .GetAsync(itemId, CancellationToken.None);

        Assert.Equal(new BusinessSlot(BusinessInventorySpace.WornAvatar, slot), read);
        Assert.Equal(slot, PvfEquipmentSlots.ByName[equipmentType]);
    }

    /// <summary>Every costume is one of the eleven kinds, and the archive says which.</summary>
    /// <remarks>
    /// The other half of the pair: the slot came back because the template's own
    /// <c>[equipment type]</c> is one of the names now in the table, not because something
    /// downstream guessed from the identifier.
    /// </remarks>
    [SkippableTheory]
    [MemberData(nameof(WhatWasMeasured))]
    public async Task A_costume_is_what_the_archive_calls_an_avatar(
        int itemId, byte slot, string equipmentType)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        _ = slot;
        _ = equipmentType;

        var placement = await new PvfItemPlacementDefinitionSource(provider, new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService())
            .GetAsync(itemId, CancellationToken.None);

        Assert.NotNull(placement);
        Assert.Equal(ItemWorldClass.Avatar, placement.WorldClass);
    }

    /// <summary>
    /// Slot eleven has no name because no template in this archive asks for one.
    /// </summary>
    /// <remarks>
    /// The reference server's enum has <c>AuraSkinAvatar = 11</c>, so the gap is deliberate rather
    /// than an oversight. The whole equipment index was searched: 118479 entries, eleven distinct
    /// <c>[equipment type]</c> names ending in <c>avatar]</c> between them, and no twelfth. This
    /// asserts the consequence of that search rather than repeating it -- a search over the index
    /// takes long enough to be worth doing once, and its result is the shape of this table.
    /// <para>
    /// <b>A different archive would have to be searched again.</b> If a twelfth name ever appears,
    /// this is the case that will still be claiming there are eleven.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_name_reaches_the_twelfth_costume_slot()
    {
        var costumes = PvfEquipmentSlots.ByName
            .Where(pair => pair.Key.EndsWith("avatar]", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(11, costumes.Length);
        Assert.DoesNotContain(costumes, pair => pair.Value == 11);
    }
}
