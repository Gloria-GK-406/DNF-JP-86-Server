using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.MoveItemSpace;

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

    /// <summary>A costume the archive declares: kind 8, band 9..64, world class avatar.</summary>
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

    /// <summary>A slot inside the bag's equipment band, which is where a costume may sit.</summary>
    private const short BagSlot = 12;

    /// <summary>Where the archive says the ordinary coat is worn.</summary>
    private const short CoatSlot = 14;

    [Fact]
    public async Task A_costume_moves_from_the_avatar_list_into_the_bag()
    {
        var side = Build(avatarAt: AvatarSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Main, BagSlot);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(CostumeTemplate, result.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot));
        Assert.Equal(CostumeTemplate, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    [Fact]
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
    [Fact]
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
    [Fact]
    public async Task A_costume_does_not_go_past_the_end_of_the_avatar_list()
    {
        var side = Build(costumeInBagAt: BagSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Avatar, 210);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status);
        Assert.Equal(CostumeTemplate, result.ItemId);
    }

    /// <summary>A move inside one list is that list's own business, and reaches it.</summary>
    [Fact]
    public async Task A_move_inside_the_avatar_list_is_handed_to_that_list()
    {
        var side = Build(avatarAt: AvatarSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Avatar, OtherAvatarSlot);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(
            (AvatarSlot, OtherAvatarSlot),
            side.Container(InventoryContainerType.Avatar).LastMove);
    }

    /// <summary>
    /// Wearing a costume out of the avatar list gets as far as the archive, and is refused there.
    /// </summary>
    /// <remarks>
    /// <b>This is the whole of what this change delivers today.</b> The costume still cannot be
    /// worn -- no avatar slot is declared anywhere this server can read, which is its own piece of
    /// work -- so the answer is still a refusal. What changed is which refusal: it was
    /// <c>ListNotServed</c>, decided from the two list numbers with nothing read, and it is now
    /// <c>NotWearable</c>, decided after the costume was read, carrying the template it was about.
    /// A log line that names the template is what a slot map can be measured from; one that says
    /// <c>item 0</c> is not.
    /// </remarks>
    [Fact]
    public async Task Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list()
    {
        var side = Build(avatarAt: AvatarSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Equipment, 3);

        Assert.Equal(ItemMoveStatus.NotWearable, result.Status);
        Assert.Equal(CostumeTemplate, result.ItemId);
    }

    /// <summary>Taking a costume off into the avatar list reaches the list rather than a refusal.</summary>
    [Fact]
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
    /// Nothing is worn in a costume slot, so nothing is displaced out of one.
    /// </summary>
    /// <remarks>
    /// The swap that would put an ordinary piece into the avatar list needs something standing in
    /// a costume slot to be displaced, and nothing can be: a piece goes on the slot its archive
    /// entry names, and the lowest slot <c>PvfEquipmentSlots.ByName</c> names is twelve. This
    /// asserts that emptiness at the only place it can be seen from here -- the request is refused
    /// for naming a slot the coat does not belong in, and a costume is refused the same way.
    /// <para>
    /// A case was written here claiming to pin the swap itself. It used slot fourteen, which is the
    /// coat's slot and not a costume slot, and seeded a costume into it -- a state its own argument
    /// said could not exist. It proved nothing and is gone. What replaces it is this, which is
    /// small and true, and the note in <c>PutOnAsync</c> saying who has to revisit the question.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(OrdinaryTemplate)]
    [InlineData(CostumeTemplate)]
    public async Task Nothing_may_be_worn_in_a_costume_slot(int template)
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
    [Fact]
    public async Task A_piece_may_not_be_worn_in_a_slot_its_archive_entry_does_not_name()
    {
        var side = Build(ordinaryInBagAt: BagSlot);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, AvatarSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status);
        Assert.Equal(OrdinaryTemplate, result.ItemId);
    }

    /// <summary>
    /// The lists still nobody serves, which no play reaches and no other case covers.
    /// </summary>
    /// <remarks>
    /// One with an unserved list as the source, one with it as the destination, and one with both.
    /// They are here because the way to break this change is to stop looking at the list numbers,
    /// and nothing else in the suite would notice.
    /// </remarks>
    public static TheoryData<InventoryContainerType, InventoryContainerType> ListsStillNotServed() =>
        new()
        {
            { InventoryContainerType.PersonalCargo, InventoryContainerType.Main },
            { InventoryContainerType.Main, InventoryContainerType.Pet },
            { InventoryContainerType.PersonalCargo, InventoryContainerType.AccountCargo },
            { InventoryContainerType.Avatar, InventoryContainerType.PersonalCargo },
            { InventoryContainerType.QuickSlot, InventoryContainerType.Avatar },
        };

    [Theory]
    [MemberData(nameof(ListsStillNotServed))]
    public async Task A_list_this_server_does_not_serve_is_still_refused(
        InventoryContainerType source, InventoryContainerType destination)
    {
        var side = Build(avatarAt: AvatarSlot, costumeInBagAt: BagSlot);

        var result = await side.MoveAsync(source, 0, destination, 0);

        Assert.Equal(ItemMoveStatus.ListNotServed, result.Status);
    }

    private Side Build(
        short? avatarAt = null,
        short? costumeInBagAt = null,
        short? ordinaryInBagAt = null,
        short? ordinaryInAvatarListAt = null,
        short? wornAt = null,
        bool wornIsOrdinary = false)
    {
        var character = new CharacterId(Character);
        var containers = new CharacterContainerDomainService();
        var bag = new RecordingContainer(InventoryContainerType.Main, character);
        var avatars = new RecordingContainer(InventoryContainerType.Avatar, character);
        var worn = new RecordingContainer(InventoryContainerType.Equipment, character);

        if (avatarAt is { } inList)
            avatars.Seed(inList, CostumeTemplate);
        if (costumeInBagAt is { } costume)
            bag.Seed(costume, CostumeTemplate);
        if (ordinaryInBagAt is { } ordinary)
            bag.Seed(ordinary, OrdinaryTemplate);
        if (ordinaryInAvatarListAt is { } stranded)
            avatars.Seed(stranded, OrdinaryTemplate);
        if (wornAt is { } slot)
            worn.Seed(slot, wornIsOrdinary ? OrdinaryTemplate : CostumeTemplate);

        containers.Register(character, bag);
        containers.Register(character, avatars);
        containers.Register(character, worn);

        return new Side(
            containers,
            new MoveItemSpaceCommandHandler(
                containers,
                new ItemMovementDomainService(
                    new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots),
                    new ItemSlotRuleService())),
            character);
    }

    private sealed record Side(
        CharacterContainerDomainService Containers,
        MoveItemSpaceCommandHandler Handler,
        CharacterId Character)
    {
        public RecordingContainer Container(InventoryContainerType type) =>
            (RecordingContainer)this.Containers.GetContainer(this.Character, type);

        public ValueTask<ItemMoveResult> MoveAsync(
            InventoryContainerType source,
            short sourceSlot,
            InventoryContainerType destination,
            short destinationSlot) =>
            this.Handler.HandleAsync(
                new CommandExecutionContext(
                    Guid.Empty,
                    typeof(MoveItemSpaceCommand),
                    typeof(Side),
                    CommandExecutionKind.Transactional),
                new MoveItemSpaceCommand(
                    this.Character, (byte)source, sourceSlot, 1, (byte)destination, destinationSlot),
                CancellationToken.None);

        public async ValueTask<ContainerSlotItem?> ReadAsync(
            InventoryContainerType type, short slotIndex) =>
            await this.Container(type)
                .ReadSlotAsync(slotIndex, CancellationToken.None);
    }

    /// <summary>
    /// A container that keeps its slots in a dictionary and remembers being asked to move.
    /// </summary>
    /// <remarks>
    /// The move inside one list belongs to the container and is asserted where a real one is; what
    /// is being checked here is that the request arrives at the right container at all, so this
    /// records the pair of slots and reports success. Everything the served paths do not touch
    /// raises rather than answering emptily, so a path that started using one of them fails here
    /// instead of being quietly told the container is empty.
    /// </remarks>
    private sealed class RecordingContainer(InventoryContainerType type, CharacterId character)
        : IItemContainer
    {
        private readonly Dictionary<int, ContainerSlotItem> bySlot = [];

        public ContainerId Id { get; } = ContainerId.ForCharacter(character, type);

        public InventoryContainerType Type => type;

        public ContainerShape Shape { get; } = new(type, 0);

        public bool IsRemovable => false;

        public (int From, int To)? LastMove { get; private set; }

        public void Seed(int slotIndex, int itemId) =>
            this.bySlot[slotIndex] = new ContainerSlotItem(
                slotIndex,
                StackableItemCore.Create(1, itemId, 1),
                ItemWorldClass.Equipment);

        public ValueTask<ContainerSlotItem?> ReadSlotAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(this.bySlot.GetValueOrDefault(slotIndex));

        public ValueTask<ContainerSlotPair> ReadSlotsAsync(
            int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ContainerSlotPair(
                this.bySlot.GetValueOrDefault(firstSlotIndex),
                this.bySlot.GetValueOrDefault(secondSlotIndex)));

        public ValueTask PutAsync(
            int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken)
        {
            this.bySlot[slotIndex] = new ContainerSlotItem(
                slotIndex, core, ItemWorldClass.Equipment);
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken)
        {
            this.bySlot.Remove(slotIndex);
            return ValueTask.CompletedTask;
        }

        public ValueTask<ItemMoveOutcome> MoveToAsync(
            int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken)
        {
            this.LastMove = (fromSlotIndex, toSlotIndex);
            var moving = this.bySlot.GetValueOrDefault(fromSlotIndex);
            return ValueTask.FromResult<ItemMoveOutcome>(moving is null
                ? new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty)
                : new ItemMoveOutcome.Moved(count, moving.ItemId));
        }

        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> AddAsync(
            int itemId, int count, CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> RefillAsync(
            ItemRefill refill, CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() =>
            new("This container answers slot reads, writes and moves and nothing else.");
    }
}
