using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.Party;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.MoveItemSpace;

/// <summary>
/// What follows a move that changed what the character is wearing.
/// </summary>
/// <remarks>
/// The acknowledgement says where an item went and nothing about how the character now looks, so a
/// client that has been told once at character selection goes on drawing what it was told then.
/// Observed live on 2026-09-08: eleven costumes went on, the acknowledgements all reported success,
/// the store held them, and the character did not change until the owner left to character
/// selection and came back.
/// <para>
/// The frame that answers this already exists and is already sent once, at entry. What is new is
/// sending it again when a move changed what it would say. The current view reads the store on
/// demand; the entry-time fixture is deliberately stale so a regression that reuses it is visible.
/// </para>
/// </remarks>
public sealed class TheCharacterChangesAsYouDressItTests(ArchiveContentFixture archive)
    : IClassFixture<ArchiveContentFixture>
{
    private const int Character = 60;

    /// <summary>A costume the archive declares under <c>[coat avatar]</c>, worn at slot 3.</summary>
    private const int Costume = 413500000;

    /// <summary>A second costume, so the store and the session copy can disagree.</summary>
    private const int OtherCostume = 101500690;

    private const short BagSlot = 12;

    private const short OtherBagSlot = 13;

    private const short AvatarSlot = 0;

    private const short WornCostumeSlot = 3;

    private static readonly NetworkConnectionId Connection = new("dressing");

    private static readonly NetworkDispatchContext Dispatch = new(
        new NetworkConnectionContext(Connection, NetworkEndpointKind.Game, 10011));

    [Dfo.Server.Testing.PvfFact]
    public async Task Putting_on_and_taking_off_a_title_refreshes_the_overhead_appearance_after_acknowledgement()
    {
        await TestGameplay.RunAsync(async () =>
        {
            const int title = 26628;
            const short titleBagSlot = 9;
            const short titleWornSlot = 13;
            var world = Build(inBagAt: titleBagSlot, itemId: title, itemKind: 1);

            await world.MoveAsync(InventoryContainerType.Main, titleBagSlot, InventoryContainerType.Equipment, titleWornSlot);

            Assert.Collection(world.Sender.Sent,
                first => Assert.Null(Assert.IsType<MoveItemSpaceResponse>(first).ErrorCode),
                second => Assert.Equal(title,
                    Assert.Single(Assert.IsType<CurrentUserStateMessage>(second).Equipment).Core.ItemId),
                third => Assert.Equal(title, Assert.Single(Assert.IsType<ExtendedUserStateMessage>(third).Equipment).Core.ItemId));

            world.Sender.Sent.Clear();
            await world.MoveAsync(InventoryContainerType.Equipment, titleWornSlot, InventoryContainerType.Main, titleBagSlot);

            Assert.Collection(world.Sender.Sent,
                first => Assert.Null(Assert.IsType<MoveItemSpaceResponse>(first).ErrorCode),
                second => Assert.Empty(Assert.IsType<CurrentUserStateMessage>(second).Equipment),
                third => Assert.Empty(Assert.IsType<ExtendedUserStateMessage>(third).Equipment));
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Expired_wear_qualification_refuses_equipping_but_allows_taking_off()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var equip = Build(inAvatarAt: AvatarSlot, canWear: false);
            await equip.MoveAsync(InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Equipment, WornCostumeSlot);
            Assert.NotNull(Assert.IsType<MoveItemSpaceResponse>(Assert.Single(equip.Sender.Sent)).ErrorCode);
            var remove = Build(wornAt: WornCostumeSlot, canWear: false);
            await remove.MoveAsync(InventoryContainerType.Equipment, WornCostumeSlot, InventoryContainerType.Avatar, AvatarSlot);
            Assert.Null(Assert.IsType<MoveItemSpaceResponse>(remove.Sender.Sent[0]).ErrorCode);
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Putting_a_costume_on_tells_the_client_what_the_character_now_wears()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot);

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

            Assert.Collection(
                world.Sender.Sent,
                first => Assert.Null(Assert.IsType<MoveItemSpaceResponse>(first).ErrorCode),
                second => Assert.Equal(
                    Costume,
                    Assert.Single(Assert.IsType<CurrentUserStateMessage>(second).Equipment).Core.ItemId),
                third => Assert.Equal(Costume, Assert.Single(Assert.IsType<ExtendedUserStateMessage>(third).Equipment).Core.ItemId));
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Taking_a_costume_off_tells_the_client_the_character_is_no_longer_wearing_it()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(wornAt: WornCostumeSlot);

            await world.MoveAsync(InventoryContainerType.Equipment, WornCostumeSlot, InventoryContainerType.Avatar, AvatarSlot);

            Assert.Collection(
                world.Sender.Sent,
                first => Assert.Null(Assert.IsType<MoveItemSpaceResponse>(first).ErrorCode),
                second => Assert.Empty(Assert.IsType<CurrentUserStateMessage>(second).Equipment),
                third => Assert.Empty(Assert.IsType<ExtendedUserStateMessage>(third).Equipment));
        });
    }

    /// <summary>
    /// The frame reports the store, not the copy the session has been carrying since selection.
    /// </summary>
    /// <remarks>
    /// The session's copy is seeded with a different costume from the one the store holds. An
    /// implementation that built the frame from the session would send that other costume and pass
    /// every other case here, because everywhere else the two agree.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task The_frame_follows_the_store_and_not_the_session_copy()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

            var frame = Assert.IsType<CurrentUserStateMessage>(world.Sender.Sent[1]);
            Assert.Equal(Costume, Assert.Single(frame.Equipment).Core.ItemId);
            Assert.DoesNotContain(frame.Equipment, item => item.Core.ItemId == OtherCostume);
        });
    }

    /// <summary>Subsequent projections read the store without replacing a session copy.</summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task The_current_view_follows_the_move_without_session_copy_replacement()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

            Assert.Equal(Costume, Assert.Single(
                (await world.OpenDungeonSelectionAsync()).Equipment).Core.ItemId);
            Assert.Equal(OtherCostume, Assert.Single(
                world.Runtime.Original.CharacterInventory.Containers.Single(
                    value => value.ContainerType == InventoryContainerType.Equipment).Items).Core.ItemId);
        });
    }

    /// <summary>
    /// Building the current view changes only its inventory projection, not entry data.
    /// </summary>
    /// <remarks>
    /// These readers must retain their captured objects rather than newly prepared entry values.
    /// This checks an uncontended refresh; it does not assert aggregate concurrency safety.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task Current_view_reads_inventory_without_modifying_entry_data()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);
            var before = world.Runtime.Original;

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

            var after = Assert.IsType<SelectCharacterResult>(
                await world.CurrentViews.TryLoadAsync(OneSession.Session, CancellationToken.None));
            Assert.NotSame(before.CharacterInventory, after.CharacterInventory);
            Assert.Same(before.Character, after.Character);
            Assert.Same(before.CharacterEntry, after.CharacterEntry);
            Assert.Same(before.AccountInventory, after.AccountInventory);
            Assert.Same(before.Account, after.Account);
            Assert.Same(before.World, after.World);
            Assert.Same(before.Session, after.Session);
            Assert.Same(before.Roster, after.Roster);
            Assert.Same(before.RosterEntry, after.RosterEntry);
        });
    }

    /// <summary>
    /// A move that changed nothing about how the character is drawn needs no appearance projection.
    /// </summary>
    /// <remarks>
    /// A successful move needs a refresh only when it affects the drawn appearance:
    /// the immediate frame reads A21 slots 0..13, support weapon 24 and creature 25;
    /// the readers take the whole container. A move inside the bag changes inventory without
    /// changing any of the parts drawn by the immediate frame.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_move_that_changes_nothing_drawn_needs_no_appearance_projection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);

            await world.MoveAsync(
                InventoryContainerType.Main, BagSlot, InventoryContainerType.Main, OtherBagSlot);

            Assert.Equal(0, world.Inventory.Loads);
            Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
        });
    }

    /// <summary>A refused move needs no current appearance projection.</summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_refused_move_needs_no_appearance_projection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(sessionCopyWears: OtherCostume);

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

            Assert.Equal(0, world.Inventory.Loads);
        });
    }

    /// <summary>
    /// A session displaced before the appearance read still gets its acknowledgement.
    /// </summary>
    /// <remarks>
    /// The move has committed, so losing the optional current projection cannot cost the answer.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_displaced_session_view_still_gets_its_acknowledgement()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(
                inBagAt: BagSlot, sessionCopyWears: OtherCostume, sessionRefusesReplacement: true);

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

            var answer = Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
            Assert.Null(answer.ErrorCode);
            Assert.Equal(1, answer.MoveValue);
            Assert.Empty(world.Logger.Errors);
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task A_move_inside_the_bag_sends_the_acknowledgement_alone()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot);

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Main, OtherBagSlot);

            Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task A_move_between_the_bag_and_the_avatar_page_sends_the_acknowledgement_alone()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot);

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Avatar, AvatarSlot);

            Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
        });
    }

    /// <summary>A worn slot the frame does not draw from does not make it worth sending.</summary>
    /// <remarks>
    /// Slot 22 is inside the worn list and outside every run subtype zero reads -- the appearance
    /// takes 0..13 and 24, and the creature triple takes 25. A move there changes what the
    /// character owns and not what it looks like.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_move_onto_a_worn_slot_the_frame_ignores_sends_the_acknowledgement_alone()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(wornAt: 22);

            await world.MoveAsync(InventoryContainerType.Equipment, 22, InventoryContainerType.Main, BagSlot);

            Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
        });
    }

    /// <summary>
    /// A move that succeeded still gets its acknowledgement when the appearance cannot be built.
    /// </summary>
    /// <remarks>
    /// The move is committed before the appearance is worked out, and the client holds the
    /// inventory shut until it is answered -- so a failure here costing the acknowledgement would
    /// bring back the very symptom this frame removes, wearing an unrelated cause. The store is
    /// asked after the transaction has closed, and asking it can raise.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_move_is_still_acknowledged_when_the_appearance_cannot_be_built()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot, storeRefusesToLoad: true);

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

            var acknowledgement = Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
            Assert.Null(acknowledgement.ErrorCode);
            Assert.Equal(1, acknowledgement.MoveValue);
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task A_refused_move_sends_the_refusal_alone()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build();

            await world.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, WornCostumeSlot);

            var refusal = Assert.IsType<MoveItemSpaceResponse>(Assert.Single(world.Sender.Sent));
            Assert.NotNull(refusal.ErrorCode);
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Opening_dungeon_selection_after_dressing_reads_the_current_equipment()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inAvatarAt: AvatarSlot, sessionCopyWears: OtherCostume);
            var before = await world.OpenDungeonSelectionAsync();
            Assert.Empty(before.Equipment);

            await world.MoveAsync(InventoryContainerType.Avatar, AvatarSlot,
                InventoryContainerType.Equipment, WornCostumeSlot);

            var after = await world.OpenDungeonSelectionAsync();
            Assert.Equal(Costume, Assert.Single(after.Equipment).Core.ItemId);
            Assert.Same(before.Character, after.Character);

            await world.MoveAsync(InventoryContainerType.Equipment, WornCostumeSlot,
                InventoryContainerType.Avatar, AvatarSlot);
            Assert.Empty((await world.OpenDungeonSelectionAsync()).Equipment);
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Dressing_reads_the_inventory_once_for_both_frames_and_copy()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot, sessionCopyWears: OtherCostume);
            await world.MoveAsync(InventoryContainerType.Main, BagSlot,
                InventoryContainerType.Equipment, WornCostumeSlot);
            Assert.Equal(1, world.Inventory.Loads);
            Assert.Equal(Costume, Assert.Single(
                Assert.Single(world.Sender.Sent.OfType<CurrentUserStateMessage>()).Equipment).Core.ItemId);
            Assert.Equal(Costume, Assert.Single(
                (await world.OpenDungeonSelectionAsync()).Equipment).Core.ItemId);
        });
    }

    [Dfo.Server.Testing.PvfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_preserves_the_real_directorys_position_persistence_flag(bool persists)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(inBagAt: BagSlot, shouldPersistPosition: persists);
            await world.MoveAsync(InventoryContainerType.Main, BagSlot,
                InventoryContainerType.Equipment, WornCostumeSlot);
            Assert.True(world.Runtime.TryGet(OneSession.Session, out _, out var after));
            Assert.Equal(persists, after);
        });
    }

    [Dfo.Server.Testing.PvfTheory]
    [InlineData((byte)1)]
    [InlineData((byte)5)]
    public async Task Dungeon_selection_reports_refreshed_character_values_in_the_existing_frame_order(byte level)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build(sessionCopyWears: OtherCostume);
            var current = world.Runtime.Original with
            {
                Character = world.Runtime.Original.Character with { Progression = new(level, 9000) },
                CharacterEntry = world.Runtime.Original.CharacterEntry with
                {
                    Public = world.Runtime.Original.CharacterEntry.Public with
                    {
                        DerivedStats = world.Runtime.Original.CharacterEntry.Public.DerivedStats with { HpMaximum = 9876 },
                    },
                    Skills = world.Runtime.Original.CharacterEntry.Skills with
                    {
                        Pages = [new CharacterSkillPageState(0, [new CharacterSkillState(7, 1, 4, [8, 9])])],
                        ProtocolMirror = new(11, 12, 13, 14),
                    },
                },
            };
            var frame = await world.OpenDungeonSelectionAsync(new FixedCurrentState(current));
            Assert.Equal(level, frame.Character.Progression.Level);
            Assert.Equal(9876, frame.Public.DerivedStats.HpMaximum);
            Assert.Equal(new CharacterSkillProtocolMirrorState(11, 12, 13, 14), frame.Skills.ProtocolMirror);
            Assert.Equal(4, Assert.Single(Assert.Single(frame.Skills.Pages).Skills).Level);
            Assert.Equal(OtherCostume, Assert.Single(frame.Equipment).Core.ItemId);
            var expected = new List<Type> { typeof(ExtendedUserStateMessage), typeof(EnterSelectDungeonAcknowledgement),
                typeof(UserStateNotification), typeof(UdpHostNotification) };
            if (level > 1)
            {
                expected.Add(typeof(DungeonPermissionStateMessage));
                expected.Add(typeof(EnterSelectDungeonNotification));
                Assert.Equal(current.CharacterEntry.Progression.Dungeons,
                    Assert.Single(world.Sender.Sent.OfType<DungeonPermissionStateMessage>()).Dungeons);
            }
            Assert.Equal(expected, world.Sender.Sent.Select(message => message.GetType()));
        });
    }

    private sealed class FixedCurrentState(SelectCharacterResult result) : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(SelectCharacterResult ignored, CancellationToken token) => ValueTask.FromResult(result);
    }

    private World Build(
        short? inBagAt = null,
        short? wornAt = null,
        int? sessionCopyWears = null,
        bool storeRefusesToLoad = false,
        bool sessionRefusesReplacement = false,
        bool shouldPersistPosition = false, bool canWear = true,
        short? inAvatarAt = null,
        int itemId = Costume,
        byte itemKind = 8)
    {
        var character = new CharacterId(Character);
        var containers = new CharacterContainerDomainService();
        var fixture = new MoveItemFixture();
        var bag = new SlotBook(InventoryContainerType.Main, character, fixture);
        var avatars = new SlotBook(InventoryContainerType.Avatar, character, fixture);
        var worn = new SlotBook(InventoryContainerType.Equipment, character, fixture);
        if (inBagAt is { } bagSlot)
            bag.Seed(bagSlot, itemId, itemKind);
        if (inAvatarAt is { } avatarSlot)
            avatars.Seed(avatarSlot, Costume);
        if (wornAt is { } wornSlot)
            worn.Seed(wornSlot, wornSlot == WornCostumeSlot ? Costume : OtherCostume);
        containers.Register(character, bag);
        containers.Register(character, avatars);
        containers.Register(character, worn);

        var mappings = new InventorySlotMappingRuleService();
        var commandHandler = new MoveItemSpaceCommandHandler(
            new BusinessInventorySlotDomainService(containers, mappings),
            new ItemMovementDomainService(
                new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots),
                new ItemSlotRuleService(), new TheCharacterChangesAsYouDressItTestsWearEligibility(canWear)));

        var runtime = new RecordingRuntime(sessionCopyWears, sessionRefusesReplacement, shouldPersistPosition);
        var inventory = new StoreBackedInventory(worn, character);
        var currentViews = new RuntimeCurrentView(runtime, inventory:
            storeRefusesToLoad ? new InventoryThatRaises() : inventory);
        var sender = new RecordingSender();
        var logger = new RecordingLogger();
        return new World(
            new MoveItemSpaceHandler(
                new OneSession(),
                currentViews,
                mappings,
                new DirectMoveBus(commandHandler, fixture),
                sender,
                logger, null!, null!, TimeProvider.System),
            sender,
            runtime,
            currentViews,
            inventory,
            logger);
    }

    private sealed class UnchangedCurrentState : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(SelectCharacterResult result, CancellationToken token) => ValueTask.FromResult(result);
    }

    private sealed record World(
        MoveItemSpaceHandler Handler, RecordingSender Sender, RecordingRuntime Runtime,
        RuntimeCurrentView CurrentViews,
        StoreBackedInventory Inventory, RecordingLogger Logger)
    {
        public async ValueTask<ExtendedUserStateMessage> OpenDungeonSelectionAsync(ICharacterStateProjectionDomainService? current = null)
        {
            Sender.Sent.Clear();
            await new EnterSelectDungeonHandler(current ?? new UnchangedCurrentState(), new OneSession(), CurrentViews, new SelectionVisits(), new IgnoredEntryIntents(),
                Sender,
                TownAreaProjectionTestFactory.Create(new OneSession(), CurrentViews, Sender),
                NullLogger<EnterSelectDungeonHandler>.Instance,
                new VirtualOnlyPartyService()).HandleAsync(
                    Dispatch, new EnterSelectDungeonRequest(GameMessageMetadata.Empty, 10000),
                    CancellationToken.None);
            return Assert.Single(Sender.Sent.OfType<ExtendedUserStateMessage>());
        }

        public ValueTask MoveAsync(
            InventoryContainerType source,
            short sourceSlot,
            InventoryContainerType destination,
            short destinationSlot) =>
            this.Handler.HandleAsync(
                Dispatch,
                new MoveItemSpaceRequest(
                    GameMessageMetadata.Empty,
                    new GameSlot(source, sourceSlot),
                    SourceInstanceValue: 0,
                    MoveCount: 1,
                    new GameSlot(destination, destinationSlot),
                    DestinationInstanceValue: 0),
                CancellationToken.None);
    }

    private sealed class IgnoredEntryIntents : IDungeonEntryIntentDomainService
    {
        public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent) { }

        public DungeonEntryIntent? Consume(SelectedSessionIdentity session) => null;

        public void RestoreIfAbsent(SelectedSessionIdentity session, DungeonEntryIntent intent) { }
        public bool ClearExact(SelectedSessionIdentity session) => false;
    }

    /// <summary>
    /// The inventory the store would hand back, read from the same worn container the move wrote to.
    /// </summary>
    /// <remarks>
    /// Reading it out of the container the command just wrote to is what makes this the store's
    /// answer rather than a fixture's: an implementation that never asked would not see the move.
    /// </remarks>
    private sealed class StoreBackedInventory(SlotBook worn, CharacterId character)
        : ICharacterInventoryEntryDomainService
    {
        public int Loads { get; private set; }

        public ValueTask<CharacterInventoryEntryState> ReadCurrentAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) => LoadAsync(accountId, characterId, cancellationToken);

        public async ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
        {
            Loads++;
            var items = await worn.ReadProjectionAsync(cancellationToken);
            var shape = SelectCharacterPayloadTestData.CreateComplete().CharacterInventory;
            return shape with
            {
                CharacterId = character,
                Containers =
                [
                    .. shape.Containers
                        .Where(value => value.ContainerType != InventoryContainerType.Equipment),
                    new InventoryContainerState(
                        InventoryContainerType.Equipment,
                        0,
                        [.. items.Select(item => new InventoryItemState(
                            item.Identity, item.SlotIndex, item.Core, item.AvatarDetail, item.Noti2CreatureMarker))]),
                ],
            };
        }
    }

    /// <summary>A store that will not answer, which is what a read after the transaction can do.</summary>
    private sealed class InventoryThatRaises : ICharacterInventoryEntryDomainService
    {
        public ValueTask<CharacterInventoryEntryState> ReadCurrentAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) => LoadAsync(accountId, characterId, cancellationToken);
        public ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            throw new InvalidDataException("The stored inventory could not be projected.");
    }

    /// <summary>Historical entry data used only to prove later reads use the store.</summary>
    private sealed class RecordingRuntime
        : ILegacySessionSnapshotFixture
    {
        private readonly bool persistsPosition;
        private readonly SelectCharacterResult state;
        private readonly bool unavailable;

        public RecordingRuntime(int? wears, bool refusesReplacement, bool shouldPersistPosition)
        {
            var source = Snapshot(wears);
            state = source with { Character = source.Character with
                { Identity = source.Character.Identity with { CharacterId = new CharacterId(Character) } } };
            this.unavailable = refusesReplacement;
            this.persistsPosition = shouldPersistPosition;
        }

        /// <summary>The copy as it was taken at selection.</summary>
        public SelectCharacterResult Original => this.state;


        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            shouldPersistPosition = false;
            var published = !unavailable && session == OneSession.Session;
            shouldPersistPosition = published && persistsPosition;
            result = published ? state : null;
            return published;
        }

        public bool RemoveExact(SelectedSessionIdentity session) => false;

        private static SelectCharacterResult Snapshot(int? wears)
        {
            var source = SelectCharacterPayloadTestData.CreateComplete();
            if (wears is not { } itemId)
                return source;

            return source with
            {
                CharacterInventory = source.CharacterInventory with
                {
                    Containers =
                    [
                        .. source.CharacterInventory.Containers
                            .Where(value => value.ContainerType != InventoryContainerType.Equipment),
                        new InventoryContainerState(
                            InventoryContainerType.Equipment,
                            0,
                            [new InventoryItemState(
                                new ItemIdentity(99),
                                WornCostumeSlot,
                                StackableItemCore.Create(8, itemId, 1))]),
                    ],
                },
            };
        }
    }

    private sealed class SlotBook(InventoryContainerType type, CharacterId character, MoveItemFixture fixture)
        : LifecycleTestContainer(type, character, fixture)
    {
        public void Seed(int slotIndex, int itemId, byte itemKind = 8) => SeedItem(slotIndex, itemId, itemKind);
    }

    private sealed class DirectMoveBus(
        ICommandHandler<MoveItemSpaceCommand, ItemMoveResult> move, MoveItemFixture fixture) : ICommandBus
    {
        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var context = new CommandExecutionContext(
                Guid.Empty, command.GetType(), typeof(DirectMoveBus),
                CommandExecutionKind.Transactional);
            var result = await fixture.RunAsync(() => move.HandleAsync(
                context, (MoveItemSpaceCommand)(object)command, cancellationToken));
            return (TResult)(object)result;
        }
    }

    private sealed class RecordingLogger : ILogger<MoveItemSpaceHandler>
    {
        public List<Exception> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error && exception is not null) Errors.Add(exception);
        }
    }

    private sealed class SelectionVisits : IDungeonVisitDomainService
    {
        public bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false) => characterLevel <= 1 && !tutorialCompleted;
        public ValueTask<Dfo.Server.Abstractions.World.DungeonEntry?> EnterAsync(CharacterId characterId, int dungeonId,
            DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId characterId,
            DungeonRunState current, MazeCell target, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class OneSession : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(Character), Connection, 1, 10011);

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => null;

        public bool ReleaseExact(SelectedSessionIdentity session) => false;

        public SelectedSessionIdentity? Find(AccountId accountId) => Session;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;
    }

    /// <summary>
    /// The direct messages of the explicit deliveries a real registration adapter registered, read from its outbox;
    /// every call goes to the real protocol sender over it (TASK-25). A CMD or NOTI outside an explicit delivery fails
    /// where it is registered, with the exception the old double threw.
    /// </summary>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public RecordingSender()
        {
            outbound.FailAny(message => outbound.ExplicitInFlight is null && message is IGameCmdMessage,
                () => new InvalidOperationException("A move answers explicitly."));
            outbound.FailAny(message => outbound.ExplicitInFlight is null && message is IGameNotiMessage,
                () => new InvalidOperationException("A move answers explicitly."));
        }

        public RecordedView<IGameServerMessage> Sent =>
            new(outbound, () => [.. outbound.Explicits.SelectMany(call => call.Delivery.RequiredDirectMessages).Cast<IGameServerMessage>()]);

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }
}

internal sealed class TheCharacterChangesAsYouDressItTestsWearEligibility(bool allowed) : Dfo.Server.Abstractions.Inventory.IEquipmentWearDomainService
{
    public Dfo.Server.Data.Inventory.ItemSnapshot UnsealForEquip(Dfo.Server.Data.Inventory.ItemSnapshot item) => item;

    public ValueTask<bool> CanWearAsync(Dfo.Server.Data.Characters.CharacterId characterId, int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(allowed);
}
