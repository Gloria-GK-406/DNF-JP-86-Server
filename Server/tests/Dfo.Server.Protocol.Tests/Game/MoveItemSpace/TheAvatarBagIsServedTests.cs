using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.MoveItemSpace;

/// <summary>
/// Which lists a move may name, now that the character carries things in two of them.
/// </summary>
/// <remarks>
/// The avatar list was never bound, so every request naming it was refused as
/// <c>ListNotServed</c> -- and that refusal is decided from the two list numbers alone, before any
/// slot is read, so it could not say which costume the player had been dragging. Binding the list
/// and serving the combinations is what these cases are about.
/// <para>
/// Half of them assert what is now served and half assert what still is not, and the second half
/// is the half that matters. The cheapest wrong way to serve a new list is to stop checking which
/// lists were named at all, and almost nothing in this repository notices: opening every door was
/// tried, and it reddened these five and exactly one case that was already here
/// (<c>MoveItemSpaceHandlerTests.A_list_other_than_the_main_one_is_refused</c>, which aims at the
/// personal cargo). One case covering one list is not the guard this needs, which is why these
/// name a list as the source, a list as the destination, and both at once.
/// </para>
/// <para>
/// What is exercised here is the dispatch and the carrying between two lists. Whether a costume
/// may sit in a given slot is <c>ItemSlotRuleService</c>'s answer and is asserted where that lives.
/// </para>
/// </remarks>
public sealed class TheAvatarBagIsServedTests(ArchiveContentFixture archive)
    : IClassFixture<ArchiveContentFixture>
{
    private const int Character = 60;

    /// <summary>A costume the archive declares: kind 8, avatar range, world class avatar.</summary>
    private const int CostumeTemplate = 413500000;

    /// <summary>An ordinary coat, so that "not a costume" is a real template rather than a hole.</summary>
    /// <remarks>
    /// The same one <c>RefusedEquipIsRecordedWithItsTemplateTests</c> already leans on for being
    /// ordinary. The first draft of this case reached for <c>101500690</c> instead, which looks
    /// ordinary and is not: the archive declares it under <c>[coat avatar]</c>, so it went into the
    /// avatar list exactly as it should have, and the case failed for being wrong about its own
    /// fixture rather than about the rule.
    /// </remarks>
    private const int OrdinaryTemplate = 10018;

    private const short AvatarSlot = 0;

    private const short OtherAvatarSlot = 5;

    /// <summary>A slot inside the ordinary equipment band.</summary>
    private const short BagSlot = 12;

    /// <summary>Where the archive says the ordinary coat is worn.</summary>
    private const short CoatSlot = 14;

    /// <summary>Where the archive names a coat avatar, measured against a live client.</summary>
    private const short CoatAvatarSlot = 3;

    /// <summary>A second coat avatar, so a swap has a costume at both ends.</summary>
    private const int OtherCostumeTemplate = 101500690;

    [Dfo.Server.Testing.PvfFact]
    public async Task A_costume_cannot_leave_the_avatar_list_for_the_ordinary_equipment_bag()
    {
        var side = Build(avatarAt: AvatarSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Main, BagSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status);
        Assert.Equal(CostumeTemplate, result.ItemId);
        Assert.Equal(CostumeTemplate, (await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Main, BagSlot));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task A_costume_moves_from_the_bag_back_into_the_avatar_list()
    {
        var side = Build(costumeInBagAt: BagSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Avatar, AvatarSlot);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Main, BagSlot));
        Assert.Equal(CostumeTemplate, (await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
    }

    /// <summary>The avatar list takes costumes, and says so before anything is written.</summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task Something_that_is_not_a_costume_does_not_get_into_the_avatar_list()
    {
        var side = Build(ordinaryInBagAt: BagSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Avatar, AvatarSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status);
        Assert.Equal(OrdinaryTemplate, result.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot));
        Assert.Equal(OrdinaryTemplate, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    /// <summary>Past the end of the list is past the end of the list.</summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_business_slot_past_the_end_of_the_avatar_list_is_rejected_as_an_invariant_failure()
    {
        var side = Build(costumeInBagAt: BagSlot);

        await Assert.ThrowsAsync<InventorySlotMappingException>(() =>
            side.MoveBusinessAsync(
                new InventorySlotMappingRuleService().ToBusinessSlot(
                    new GameSlot(InventoryContainerType.Main, BagSlot)),
                new BusinessSlot(BusinessInventorySpace.Avatar, 210)).AsTask());

        Assert.Equal(CostumeTemplate,
            (await side.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    /// <summary>A move inside one list uses local slots and changes that list.</summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_move_inside_the_avatar_list_is_handed_to_that_list()
    {
        var side = Build(avatarAt: AvatarSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Avatar, OtherAvatarSlot);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot));
        Assert.Equal(CostumeTemplate,
            (await side.ReadAsync(InventoryContainerType.Avatar, OtherAvatarSlot))!.ItemId);
    }

    /// <summary>A costume is worn out of the avatar list, onto the slot the archive names for it.</summary>
    /// <remarks>
    /// <b>This case used to assert a refusal, and that refusal was the whole of what serving the
    /// avatar list delivered.</b> The costume reached the archive, the archive named no slot for
    /// any costume, and the answer was <c>NotWearable</c> carrying the template -- which was the
    /// point, because that line is what the eleven slots were then measured with.
    /// <para>
    /// The measuring is done and the names are in the table, so the same request now goes through.
    /// The costume leaves the avatar list and stands in slot three, where the archive says a coat
    /// avatar goes and where a live client asked for it three separate times.
    /// </para>
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_costume_is_worn_out_of_the_avatar_list_onto_its_own_slot()
    {
        var side = Build(avatarAt: AvatarSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Equipment, CoatAvatarSlot);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(CostumeTemplate, result.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot));
        Assert.Equal(
            CostumeTemplate,
            (await side.ReadAsync(InventoryContainerType.Equipment, CoatAvatarSlot))!.ItemId);
    }

    /// <summary>A costume aimed at a costume slot that is not its own is turned away.</summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_costume_does_not_go_on_a_costume_slot_that_is_not_its_own()
    {
        var side = Build(avatarAt: AvatarSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Equipment, 5);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status);
        Assert.Equal(CostumeTemplate, result.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Equipment, 5));
    }

    /// <summary>The same costume goes on from the bag, which is a different source list.</summary>
    /// <remarks>
    /// Both sources reach the same lines of <c>PutOnAsync</c>, but a case proves one path. The bag
    /// is the path a costume takes today, because that is where a granted one lands.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_costume_is_worn_out_of_the_bag_too()
    {
        var side = Build(costumeInBagAt: BagSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, CoatAvatarSlot);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(
            CostumeTemplate,
            (await side.ReadAsync(InventoryContainerType.Equipment, CoatAvatarSlot))!.ItemId);
    }

    /// <summary>What comes off a costume slot goes back where the arriving costume came from.</summary>
    /// <remarks>
    /// The question <c>PutOnAsync</c> was asked to leave open until the costume slots had names.
    /// This is the answer, asserted rather than argued: both ends of the swap are costumes, because
    /// only the eleven <c>avatar]</c> names reach slots <c>0..10</c>, and the slot the displaced one
    /// returns to is where the arriving one was standing -- a slot that list plainly admits a
    /// costume in. Until today it could not be asserted at all: nothing could stand in a costume
    /// slot to be displaced out of one.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task What_comes_off_a_costume_slot_goes_back_into_the_avatar_list()
    {
        var side = Build(avatarAt: AvatarSlot, wornAt: CoatAvatarSlot, wornIsOtherCostume: true);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Equipment, CoatAvatarSlot);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(
            CostumeTemplate,
            (await side.ReadAsync(InventoryContainerType.Equipment, CoatAvatarSlot))!.ItemId);
        Assert.Equal(
            OtherCostumeTemplate,
            (await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
    }

    /// <summary>Taking a costume off into the avatar list reaches the list rather than a refusal.</summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task Taking_something_off_into_the_avatar_list_reaches_that_list()
    {
        var side = Build(wornAt: 3);

        var result = await side.MoveAsync(
            InventoryContainerType.Equipment, 3, InventoryContainerType.Avatar, AvatarSlot);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(CostumeTemplate, result.ItemId);
        Assert.Equal(CostumeTemplate, (await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
    }

    /// <summary>
    /// Nothing goes on the first costume slot but the costume whose slot it is.
    /// </summary>
    /// <remarks>
    /// A piece goes on the slot its own archive entry names and no other. Slot zero belongs to the
    /// hat avatar, so neither an ordinary coat (fourteen) nor a coat avatar (three) may be put
    /// there, and both are turned away by the same line.
    /// <para>
    /// <b>This used to say something stronger and, as of 2026-09-08, false</b>: that nothing could
    /// be worn in a costume slot at all, because the lowest slot the table named was twelve. That
    /// was true while the costume slots had no names -- it is what let <c>PutOnAsync</c> leave the
    /// displacement question open -- and giving them names is exactly what this change did. The
    /// case went on passing only because the slot it aims at is zero and neither template's slot is
    /// zero. What it can still say is the sentence above, which is the rule the swap case a few
    /// lines up now leans on.
    /// </para>
    /// </remarks>
    [Dfo.Server.Testing.PvfTheory]
    [InlineData(OrdinaryTemplate)]
    [InlineData(CostumeTemplate)]
    public async Task Nothing_goes_on_a_costume_slot_but_the_costume_whose_slot_it_is(int template)
    {
        var side = Build(
            ordinaryInBagAt: template == OrdinaryTemplate ? BagSlot : null,
            costumeInBagAt: template == CostumeTemplate ? BagSlot : null);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, AvatarSlot);

        Assert.Equal(template, result.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Equipment, AvatarSlot));
    }

    /// <summary>Nothing may be worn in a slot its own archive entry does not name.</summary>
    /// <remarks>
    /// This is the rule the swap above leans on, asserted directly. The coat names slot fourteen,
    /// so aiming it at a costume slot is refused -- which is why a costume slot can never come to
    /// hold a coat, and why what comes off one is always something the avatar list will take.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_piece_may_not_be_worn_in_a_slot_its_archive_entry_does_not_name()
    {
        var side = Build(ordinaryInBagAt: BagSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, AvatarSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status);
        Assert.Equal(OrdinaryTemplate, result.ItemId);
    }

    /// <summary>
    /// Cargo requires the selected session and its bound containers.
    /// </summary>
    /// <remarks>
    /// One with cargo as the source, one as the destination, and one with both.
    /// They are here because the way to break this change is to stop looking at the list numbers,
    /// and nothing else in the suite would notice.
    /// </remarks>
    public static TheoryData<InventoryContainerType, InventoryContainerType> UnboundCargoLists() =>
        new()
        {
            { InventoryContainerType.PersonalCargo, InventoryContainerType.Main },
            { InventoryContainerType.PersonalCargo, InventoryContainerType.AccountCargo },
            { InventoryContainerType.Avatar, InventoryContainerType.PersonalCargo },
        };

    [Dfo.Server.Testing.PvfTheory]
    [MemberData(nameof(UnboundCargoLists))]
    public async Task A_cargo_list_without_a_bound_session_is_refused(
        InventoryContainerType source, InventoryContainerType destination)
    {
        var side = Build(avatarAt: AvatarSlot, costumeInBagAt: BagSlot);

        var result = await side.MoveAsync(source, SlotIn(source), destination, SlotIn(destination));

        Assert.Equal(ItemMoveStatus.NoBoundContainer, result.Status);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task A_bound_pet_list_is_served_even_when_both_slots_are_empty()
    {
        var side = Build();
        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Pet, 0);
        Assert.Equal(ItemMoveStatus.BothSlotsEmpty, result.Status);
    }

    private static short SlotIn(InventoryContainerType type) => type switch
    {
        InventoryContainerType.Main => BagSlot,
        _ => 0,
    };

    private Side Build(
        short? avatarAt = null,
        short? costumeInBagAt = null,
        short? ordinaryInBagAt = null,
        short? ordinaryInAvatarListAt = null,
        short? wornAt = null,
        bool wornIsOrdinary = false,
        bool wornIsOtherCostume = false)
    {
        var character = new CharacterId(Character);
        var containers = new CharacterContainerDomainService();
        var fixture = new MoveItemFixture();
        var bag = new RecordingContainer(InventoryContainerType.Main, character, fixture);
        var avatars = new RecordingContainer(InventoryContainerType.Avatar, character, fixture);
        var worn = new RecordingContainer(InventoryContainerType.Equipment, character, fixture);
        var pet = new RecordingContainer(InventoryContainerType.Pet, character, fixture);

        if (avatarAt is { } inList)
            avatars.Seed(inList, CostumeTemplate);
        if (costumeInBagAt is { } costume)
            bag.Seed(costume, CostumeTemplate);
        if (ordinaryInBagAt is { } ordinary)
            bag.Seed(ordinary, OrdinaryTemplate);
        if (ordinaryInAvatarListAt is { } stranded)
            avatars.Seed(stranded, OrdinaryTemplate);
        if (wornAt is { } slot)
            worn.Seed(slot, wornIsOrdinary ? OrdinaryTemplate
                : wornIsOtherCostume ? OtherCostumeTemplate : CostumeTemplate);

        TestGameplay.Run(() =>
        {
            containers.Register(character, bag);
            containers.Register(character, avatars);
            containers.Register(character, worn);
            containers.Register(character, pet);
        });

        var mappings = new InventorySlotMappingRuleService();
        return new Side(
            containers,
            new InventoryMoveDomainService(
                new BusinessInventorySlotDomainService(containers, mappings),
                new ItemMovementDomainService(
                    new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots),
                    new ItemSlotRuleService(), new TheAvatarBagIsServedTestsWearEligibility())),
            character, fixture);
    }

    private sealed record Side(
        CharacterContainerDomainService Containers,
        InventoryMoveDomainService Handler,
        CharacterId Character, MoveItemFixture Fixture)
    {
        public RecordingContainer Container(InventoryContainerType type) =>
            (RecordingContainer)this.Containers.GetContainer(this.Character, type);

        public ValueTask<ItemMoveResult> MoveAsync(
            InventoryContainerType source,
            short sourceSlot,
            InventoryContainerType destination,
            short destinationSlot) =>
            this.Fixture.RunAsync(() => this.Handler.MoveAsync(
                new InventoryMoveRequest(
                    this.Character,
                    ToBusinessSlot(source, sourceSlot), 1,
                    ToBusinessSlot(destination, destinationSlot)),
                CancellationToken.None));

        public ValueTask<ItemMoveResult> MoveBusinessAsync(
            BusinessSlot source,
            BusinessSlot destination) =>
            this.Fixture.RunAsync(() => this.Handler.MoveAsync(
                new InventoryMoveRequest(this.Character, source, 1, destination),
                CancellationToken.None));

        private static BusinessSlot ToBusinessSlot(
            InventoryContainerType space, int index) =>
            new InventorySlotMappingRuleService().ToBusinessSlot(new GameSlot(space, index));

        public async ValueTask<ItemSnapshot?> ReadAsync(
            InventoryContainerType type, short slotIndex) =>
            await this.Container(type)
                .ReadSnapshotAsync(slotIndex, CancellationToken.None);
    }

    /// <summary>Container sharing the command transaction and item custody backend.</summary>
    private sealed class RecordingContainer(InventoryContainerType type, CharacterId character, MoveItemFixture fixture)
        : LifecycleTestContainer(type, character, fixture)
    {
        public void Seed(int slotIndex, int itemId) => SeedItem(slotIndex, itemId, itemId is CostumeTemplate or 101500690 ? (byte)8 : (byte)1);
    }
}

internal sealed class TheAvatarBagIsServedTestsWearEligibility : Dfo.Server.Abstractions.Inventory.IEquipmentWearDomainService
{
    public Dfo.Server.Data.Inventory.ItemSnapshot UnsealForEquip(Dfo.Server.Data.Inventory.ItemSnapshot item) => item;

    public ValueTask<bool> CanWearAsync(Dfo.Server.Data.Characters.CharacterId characterId, int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(true);
}
