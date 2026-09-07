using Dfo.Server.Abstractions.Content;
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
        new(8, 1, 9, 64) { WorldClass = ItemWorldClass.Avatar };

    private static readonly ItemPlacement Shoulder =
        new(1, 1, 9, 64) { WorldClass = ItemWorldClass.Equipment };

    private static readonly ItemPlacement Potion =
        new(2, 100, 65, 120) { WorldClass = ItemWorldClass.Interchangeable };

    private static readonly ItemPlacement Creature =
        new(5, 1, 9, 64) { WorldClass = ItemWorldClass.Creature };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(104)]
    [InlineData(209)]
    public void A_costume_may_sit_anywhere_in_the_avatar_list(int slotIndex)
    {
        Assert.True(Rules.CanOccupySlot(InventoryContainerType.Avatar, Costume, slotIndex));
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
    public void The_avatar_list_ends_at_two_hundred_and_nine(int slotIndex)
    {
        Assert.False(Rules.CanOccupySlot(InventoryContainerType.Avatar, Costume, slotIndex));
    }

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
        Assert.False(Rules.CanOccupySlot(InventoryContainerType.Avatar, placement, 0));
    }

    /// <summary>Every list with no rule written for it is shut, costume or not.</summary>
    /// <remarks>
    /// This is the half no other test covers and no play reaches. A move naming one of these lists
    /// is refused before this is asked -- the handler serves four combinations and nothing else --
    /// so if that refusal were ever loosened, this is what would still be standing.
    /// </remarks>
    public static TheoryData<InventoryContainerType> ListsWithNoRuleOfTheirOwn() =>
    [
        InventoryContainerType.PersonalCargo,
        InventoryContainerType.Equipment,
        InventoryContainerType.Pet,
        InventoryContainerType.AccountCargo,
        InventoryContainerType.TitleBookGeneral,
        InventoryContainerType.QuickSlot,
        InventoryContainerType.KnightShieldEquipped,
    ];

    [Theory]
    [MemberData(nameof(ListsWithNoRuleOfTheirOwn))]
    public void A_list_nobody_decided_about_is_shut(InventoryContainerType containerType)
    {
        Assert.False(Rules.CanOccupySlot(containerType, Costume, 0));
        Assert.False(Rules.CanOccupySlot(containerType, Potion, 0));
    }

    /// <summary>
    /// Asking by name and asking the main list are the same question about the main list.
    /// </summary>
    /// <remarks>
    /// The two are one implementation -- the named form calls the main one -- and this is what
    /// says so out loud. It is worth an assertion because the alternative shape, two rules that
    /// each know the bands, is exactly the drift this list-taking form was added to avoid: the
    /// same item would be admitted by one caller and refused by the other, and both would look
    /// right on their own.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(120)]
    [InlineData(351)]
    public void The_main_list_answers_the_same_either_way(int slotIndex)
    {
        foreach (var placement in new[] { Costume, Shoulder, Potion, Creature })
        {
            Assert.Equal(
                Rules.CanOccupyMainSlot(placement, slotIndex),
                Rules.CanOccupySlot(InventoryContainerType.Main, placement, slotIndex));
        }
    }
}
