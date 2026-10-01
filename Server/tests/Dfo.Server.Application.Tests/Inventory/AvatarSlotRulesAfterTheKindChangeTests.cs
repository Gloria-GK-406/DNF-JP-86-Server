using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// What the two slot rules answer for one avatar template now that the archive gives it kind 8.
/// </summary>
/// <remarks>
/// The bar's exclusion is not new. Before this outcome <c>ItemSlotRuleService</c>'s own remarks
/// already read "The exclusions are the kinds whose home is another list entirely -- creatures and
/// their gear, avatars and their emblems", and <c>AllowsQuickSlot</c> already named
/// <c>KindAvatar</c>. What changed is the byte this template arrives carrying:
/// <c>ItemPlacementRuleService.ClassifyAsync</c> records the earlier state as "the byte and the
/// archive still disagree about an avatar -- stored as kind 8, given kind 1 by the equipment
/// index", and 1 is a byte the bar was observed to take -- see the last assertion of the first
/// case below. So the rule read here is the rule that was already written down; what reaches it
/// is new.
/// <para>
/// Both cases are asked of the placement the operator's own archive hands out for one template,
/// not of a placement written here, so a delivery that stopped agreeing with the archive would
/// change what these read rather than leaving them agreeing with themselves.
/// </para>
/// </remarks>
public sealed class AvatarSlotRulesAfterTheKindChangeTests
{
    /// <summary>
    /// <c>equipment.lst</c>'s <c>101550507</c>, the template <c>GrantedAvatarIsWholeTests</c>
    /// grants.
    /// </summary>
    private const int AvatarItemId = 101550507;

    /// <summary>The kind byte storage judges an avatar by, and the one on the bar's exclusions.</summary>
    private const byte KindStorageJudgesAnAvatarBy = 8;

    /// <summary>
    /// The kind the equipment index handed this template before this outcome.
    /// </summary>
    /// <remarks>
    /// Carried here so that the two experiments below can be run over one placement twice, once
    /// under each byte, rather than being argued from what the code used to read.
    /// </remarks>
    private const byte KindTheEquipmentIndexHandedThisTemplateBefore = 1;

    /// <summary>A potion's kind: one the bar takes, used below to show the bar was reachable.</summary>
    private const byte ConsumableKind = 2;

    private static readonly ItemSlotRuleService SlotRules = new();

    private static readonly ItemArrivalRuleService ArrivalRules = new();

    /// <summary>
    /// The bar slot this case names refuses the avatar the archive declares.
    /// <b>Observed change.</b>
    /// </summary>
    /// <remarks>
    /// Three things are asked of one placement at one slot of the bar. It is refused there; it is
    /// accepted at the first slot of the band the archive gave it, so the refusal is about the bar
    /// and not about this template being unplaceable; and the same placement carrying the earlier
    /// kind byte is accepted at that same slot of the bar, so what the answer turned on was the
    /// byte rather than the band or the slot.
    /// <para>
    /// The third assertion is also what keeps the first from being satisfiable by an accident: a
    /// <c>CanOccupyMainSlot</c> that had begun refusing this slot without regard to the byte would
    /// fail it.
    /// </para>
    /// </remarks>
    [SkippableTheory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public async Task A_quick_slot_refuses_the_avatar_the_archive_declares(int slotIndex)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var placement = await ArchivePlacementAsync();
        Assert.Equal(KindStorageJudgesAnAvatarBy, placement.ItemKind);

        Assert.False(SlotRules.CanOccupySlot(
            new BusinessSlot(BusinessInventorySpace.QuickSlot, slotIndex - 3), placement));

        Assert.True(SlotRules.CanOccupySlot(
            new BusinessSlot(BusinessInventorySpace.Avatar, 0), placement));

        Assert.True(SlotRules.CanOccupySlot(
            new BusinessSlot(BusinessInventorySpace.QuickSlot, slotIndex - 3),
            placement with { ItemKind = KindTheEquipmentIndexHandedThisTemplateBefore }));
    }

    /// <summary>
    /// One avatar template under two kind bytes: the arrival answers the same in both.
    /// <b>Checked, and observed unchanged.</b>
    /// </summary>
    /// <remarks>
    /// The bands an arrival considers are private, so what is read here is the landing they
    /// produce. Two configurations are put to the arrival, and each is put to it twice -- once
    /// with the kind the archive gives this template now, once with the kind it handed the same
    /// template before -- and the two answers of each pair are compared.
    /// <para>
    /// The second configuration is the one that can tell them apart: the template's own band is
    /// full and the bar is free, so an arrival that offered the bar as a fallback would answer a
    /// slot on it instead of no room. The last assertion is what says the bar was free in that
    /// same occupied set, by handing it a kind the bar takes and watching it land there; without
    /// it, an occupied set that had covered the bar as well would produce the same "no room".
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var asItIsNow = await ArchivePlacementAsync();
        Assert.Equal(KindStorageJudgesAnAvatarBy, asItIsNow.ItemKind);
        var asItWasBefore = asItIsNow with
        {
            ItemKind = KindTheEquipmentIndexHandedThisTemplateBefore,
        };

        // Nothing occupied: each lands at the first slot of the band the archive gave.
        var nothingOccupied = new HashSet<BusinessSlot>();
        Assert.Equal(
            new ItemArrival.IntoEmptySlot(
                new(asItIsNow.Destination.Space, asItIsNow.Destination.FirstIndex), 1),
            ArrivalRules.ForGrant([], 1, asItIsNow, nothingOccupied));
        Assert.Equal(
            ArrivalRules.ForGrant([], 1, asItWasBefore, nothingOccupied),
            ArrivalRules.ForGrant([], 1, asItIsNow, nothingOccupied));

        // Its own band full, the bar free: each answers no room.
        var ownBandFull = Enumerable.Range(
                asItIsNow.Destination.FirstIndex,
                asItIsNow.Destination.LastIndex - asItIsNow.Destination.FirstIndex + 1)
            .Select(index => new BusinessSlot(asItIsNow.Destination.Space, index))
            .ToHashSet();
        Assert.IsType<ItemArrival.NoRoom>(ArrivalRules.ForGrant([], 1, asItIsNow, ownBandFull));
        Assert.Equal(
            ArrivalRules.ForGrant([], 1, asItWasBefore, ownBandFull),
            ArrivalRules.ForGrant([], 1, asItIsNow, ownBandFull));

        // And the bar was free in that same occupied set.
        Assert.Equal(
            new ItemArrival.IntoEmptySlot(new(BusinessInventorySpace.QuickSlot, 0), 1),
            ArrivalRules.ForGrant(
                [], 1, asItIsNow with { ItemKind = ConsumableKind }, ownBandFull));
    }

    /// <summary>What the operator's own archive hands out for that one template.</summary>
    private static async Task<ItemPlacement> ArchivePlacementAsync()
    {
        using var content = ContentArchive.CreateContentProvider();
        var rules = new ItemPlacementRuleService(
            content.GetRequiredService<IItemPlacementDefinitionSource>(),
            content.GetRequiredService<IEquipmentSlotDefinitionSource>());
        var placement = await rules.PlacementForAsync(AvatarItemId, CancellationToken.None);
        Assert.NotNull(placement);
        return placement!;
    }
}
