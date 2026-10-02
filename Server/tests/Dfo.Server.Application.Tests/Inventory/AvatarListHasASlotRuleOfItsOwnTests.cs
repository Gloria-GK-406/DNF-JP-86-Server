using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// What the avatar list lets in, and what every list nobody has decided about lets in.
/// </summary>
/// <remarks>
/// The avatar list is the second one this server serves, and a served list is one with a rule
/// written down. Two hundred and ten slots and costumes only: read out of the reference A21
/// server, whose <c>GetAvatarOpenRange</c> returns <c>0..209</c> whatever parameter it is handed
/// and whose slot-kind lookup answers <c>KindAvatar</c> for every slot of that list rather than
/// dividing it into bands. <b>Read from upstream source; no client was asked.</b>
/// <para>
/// The other half of these cases is the half that was wrong last time something like this was
/// added: the lists that are <em>not</em> served. Nothing in this repository drags a thing into
/// the cargo or the pet list today, so a rule that quietly said yes to them would pass every test
/// there is. They are asserted here for that reason and no other.
/// </para>
/// </remarks>
public sealed class AvatarListHasASlotRuleOfItsOwnTests
{
    private static readonly ItemSlotRuleService Rules = new();

    /// <summary>A costume: the equipment band in the bag, and its own list beside it.</summary>
    /// <remarks>
    /// The band is the one the live archive answers for every costume asked of it -- kind 8,
    /// stack 1, <c>9..64</c> -- so this stands for a real template rather than a shape invented to
    /// suit the case. What makes it a costume here is <see cref="ItemWorldClass.Avatar"/>, which is
    /// what the archive's own <c>[equipment type]</c> makes it, not the kind byte beside it.
    /// </remarks>
    private static readonly ItemPlacement Costume =
        new(8, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 55))
        { WorldClass = ItemWorldClass.Avatar };

    private static readonly ItemPlacement Shoulder =
        new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 55))
        { WorldClass = ItemWorldClass.Equipment };

    private static readonly ItemPlacement Potion =
        new(2, 100, new(BusinessInventorySpace.Consumable, 0, 55))
        { WorldClass = ItemWorldClass.Interchangeable };

    private static readonly ItemPlacement Creature =
        new(5, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 55))
        { WorldClass = ItemWorldClass.Creature };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(104)]
    [InlineData(209)]
    public void A_costume_may_sit_anywhere_in_the_avatar_list(int slotIndex)
    {
        Assert.True(Rules.CanOccupySlot(
            new BusinessSlot(BusinessInventorySpace.Avatar, slotIndex), Costume));
    }

    /// <remarks>
    /// Two hundred and ten slots, so the first slot past the end is 210. The negative is here
    /// because a slot index arrives as a signed sixteen-bit field off the wire and nothing between
    /// there and here makes it positive.
    /// </remarks>
    [Theory]
    [InlineData(-1)]
    [InlineData(210)]
    [InlineData(289)]
    public void The_avatar_list_ends_at_two_hundred_and_nine(int slotIndex) =>
        Assert.Throws<InventorySlotMappingException>(() =>
            new InventorySlotMappingRuleService().ToGameSlot(
                new BusinessSlot(BusinessInventorySpace.Avatar, slotIndex)));

    /// <summary>
    /// The avatar list takes costumes, and the band a thing has in the bag does not get it in.
    /// </summary>
    /// <remarks>
    /// The shoulder and the creature share the costume's bag band exactly -- all three are
    /// <c>9..64</c> -- so a rule that judged by band would let both in. What tells them apart is
    /// what the archive says each one is.
    /// </remarks>
    public static TheoryData<ItemPlacement> ThingsThatAreNotCostumes() =>
        [Shoulder, Potion, Creature];

    [Theory]
    [MemberData(nameof(ThingsThatAreNotCostumes))]
    public void The_avatar_list_takes_nothing_but_costumes(ItemPlacement placement)
    {
        Assert.False(Rules.CanOccupySlot(
            new BusinessSlot(BusinessInventorySpace.Avatar, 0), placement));
    }

    /// <summary>Every list with no rule written for it is shut, costume or not.</summary>
    /// <remarks>
    /// This is the half no other test covers and no play reaches. A move naming one of these lists
    /// is refused before this is asked -- these lists do not accept costume or potion placement --
    /// so if that refusal were ever loosened, this is what would still be standing.
    /// </remarks>
    public static TheoryData<BusinessInventorySpace> ListsWithNoRuleOfTheirOwn() =>
    [
        BusinessInventorySpace.WornEquipment,
        BusinessInventorySpace.CreatureInventory,
        BusinessInventorySpace.GuildMedal,
    ];

    [Theory]
    [MemberData(nameof(ListsWithNoRuleOfTheirOwn))]
    public void A_list_nobody_decided_about_is_shut(BusinessInventorySpace space)
    {
        Assert.False(Rules.CanOccupySlot(new BusinessSlot(space, 0), Costume));
        Assert.False(Rules.CanOccupySlot(new BusinessSlot(space, 0), Potion));
    }

    /// <summary>The mapped main-list bands retain their established admission rules.</summary>
    /// <remarks>
    /// The protocol offsets are translated before this rule is asked. These expectations pin the
    /// old main-list behavior to the resulting business space without restating the offset table
    /// inside production code.
    /// </remarks>
    [Theory]
    [InlineData(3, false, true, true, false)]
    [InlineData(8, false, true, true, false)]
    [InlineData(9, true, true, false, true)]
    [InlineData(64, true, true, false, true)]
    [InlineData(65, false, false, true, false)]
    [InlineData(120, false, false, true, false)]
    [InlineData(351, false, false, false, false)]
    public void The_main_list_mapping_preserves_admission(
        int slotIndex,
        bool costume,
        bool shoulder,
        bool potion,
        bool creature)
    {
        var slot = new InventorySlotMappingRuleService().ToBusinessSlot(
            new GameSlot(InventoryContainerType.Main, slotIndex));

        Assert.Equal(costume, Rules.CanOccupySlot(slot, Costume));
        Assert.Equal(shoulder, Rules.CanOccupySlot(slot, Shoulder));
        Assert.Equal(potion, Rules.CanOccupySlot(slot, Potion));
        Assert.Equal(creature, Rules.CanOccupySlot(slot, Creature));
    }
}
