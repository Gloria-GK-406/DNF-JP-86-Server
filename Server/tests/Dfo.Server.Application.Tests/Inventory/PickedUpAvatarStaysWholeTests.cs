using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Dfo.Server.Protocol.Game.GroundPickup;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// What reaches the bag when a piece is taken off a room's floor.
/// </summary>
/// <remarks>
/// A pick-up is two writes to one slot, not one. The bag opens the slot -- which is where an
/// avatar is given the identity of its own and the six facts a row of that kind holds -- and the
/// pick-up then writes the same slot a second time to hand the piece what the floor announced for
/// it: how worn it is, and the value that tells one piece of equipment from another.
/// <para>
/// The second write is where the two sorts of content part company. For a piece of ordinary gear
/// the announced value is the whole point: the floor showed the player a value and the bag has to
/// end up holding that same one. For an avatar the value inside the core is not that kind of
/// value at all -- it is the key the store's fill looks the six facts up by -- so a second write
/// that replaced it would leave the row and its facts unable to find each other, and the entry
/// load refuses a row like that.
/// </para>
/// <para>
/// So none of these asserts a number the file made up. The avatar cases go through the pick-up
/// command the client's frame reaches and then ask the entry load -- the reader that refuses a
/// factless avatar -- to serve the thing again. The ordinary-gear case stands beside them for the
/// same reason the pile case stands beside the drag cases: a repair that made the second write
/// stop happening at all would leave the avatar cases green and that one red.
/// </para>
/// <para>
/// The archive is the operator's own and the store is a real database, for the reason
/// <c>GrantedAvatarIsWholeTests</c> gives: a stubbed archive would let the two sides agree on a
/// classification this file invented. The floor, by contrast, is the run's own -- a piece is put
/// on it by allocating a kill's drops, which is the same call the death slice makes.
/// </para>
/// </remarks>
public sealed class PickedUpAvatarStaysWholeTests
{
    /// <summary><c>equipment.lst</c>'s <c>101550507</c>, an avatar cap or coat.</summary>
    /// <remarks>
    /// The same identifier <c>GrantedAvatarIsWholeTests</c> grants and
    /// <c>DraggedAvatarStaysWholeTests</c> drags, so the three outcomes are talking about one
    /// template the archive is known to declare rather than three picked separately.
    /// </remarks>
    private const int AvatarItemId = 101550507;

    /// <summary>The 铁脊蛇矛, a piece of ordinary gear a kill may drop.</summary>
    /// <remarks>Borrowed from <c>GroundPickupTests</c>, which drops and picks up this one.</remarks>
    private const int WeaponItemId = 114000088;

    /// <summary>The wear this file announces on the floor, so it can look for it in the bag.</summary>
    /// <remarks>
    /// The announcement is this file's own and the assertion is that it arrived: a piece banked at
    /// zero would be a broken one, and nothing on the wire would have said so.
    /// </remarks>
    private const ushort AnnouncedDurability = 45;

    /// <summary>The kind byte storage judges an avatar by, written as a literal.</summary>
    /// <remarks>
    /// Its own constant rather than the delivery path's, so that a change to the constant the
    /// production path writes is a difference this file can see.
    /// </remarks>
    private const byte KindStorageJudgesAnAvatarBy = 8;

    /// <summary>How many bytes an avatar's jewel socket is; the load refuses any other length.</summary>
    private const int JewelSocketLength = 30;

    /// <summary>What a fresh avatar's expiry is set to when nothing has said it expires.</summary>
    private const int NeverExpires = 0;

    private const int Account = 91;

    private const int Character = 77;

    private static readonly CharacterId Who = new(Character);

    /// <summary>A two-room maze, the shape <c>GroundPickupTests</c> runs its floors in.</summary>
    private static readonly DungeonLayout Layout = new(
        DungeonId: 144,
        MazeIndex: 0,
        Width: 2,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(1, 0),
        StartRoomMapId: 8153,
        Seed: 0x0BADF00D,
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> {
            [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153),
            [new MazeCell(1, 0)] = DungeonCellDefinition.Fixed(8154),
        },
        BasisLevel: 86);

    /// <summary>
    /// An avatar taken off the floor is served again by the reader that refuses one without facts.
    /// </summary>
    /// <remarks>
    /// The entry load is the assertion. It throws for an avatar whose six facts it cannot find, so
    /// a pick-up that put down a row carrying none would not reach any comparison below -- and
    /// before this outcome, a pick-up did exactly that:
    /// <c>InvalidDataException: Avatar detail N is unavailable.</c> out of
    /// <c>InventoryEntryAssembly.WithAvatarDetail</c>.
    /// <para>
    /// What the facts are compared against is <c>ArrivingAvatar</c>'s shape rather than a re-read
    /// of the row: a comparison of the row with itself would stay green for a pick-up that wrote
    /// six facts of its own invention.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithASeededCharacterAsync(content, async (side, handler, runs) =>
        {
            var lying = Assert.Single(BeginWithDrops(
                runs, (30011, [Dropped(AvatarItemId)])));

            var taken = await side.InUseCaseAsync(token => PickUp(handler, lying.Number, token));

            Assert.Equal(GroundPickupStatus.PickedUp, taken.Status);
            Assert.Equal(AvatarItemId, taken.ItemId);

            var served = await OneItemAsync(side, taken.SlotIndex);
            Assert.Equal(AvatarItemId, served.Core.ItemId);
            Assert.Equal(KindStorageJudgesAnAvatarBy, served.Core.ItemKind);

            // Its own identity, which is what the load, the save and both projections demand of a
            // standing avatar.
            Assert.True(served.Core.Value > 0);

            var facts = served.AvatarDetail;
            Assert.NotNull(facts);
            Assert.Equal(NeverExpires, facts!.ExpireDate);
            Assert.Equal(0, facts.ClearAvatarId);
            Assert.Equal(JewelSocketLength, facts.JewelSocket.Length);
            Assert.All(facts.JewelSocket, value => Assert.Equal(0, value));
            Assert.Equal(0, facts.Color1);
            Assert.Equal(0, facts.Color2);
            Assert.Equal(0, facts.DeleteDate);
        });
    }

    /// <summary>
    /// Two avatars off one kill are two things, not one thing twice.
    /// </summary>
    /// <remarks>
    /// The sharper half of the same outcome, and it does not depend on a draw coming out any
    /// particular way. Every piece of one kill is announced under one value -- that is what the
    /// captured frames show and what <c>GroundPickupTests.Every_piece_of_one_kill_carries_one_value</c>
    /// holds -- so a pick-up that wrote that announced value into an avatar's core would give
    /// these two rows the same one, every time. Two identities that differ is therefore a
    /// statement about which value the row ended up carrying, not about randomness.
    /// </remarks>
    [SkippableFact]
    public async Task Two_avatars_off_one_kill_keep_two_identities_of_their_own()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithASeededCharacterAsync(content, async (side, handler, runs) =>
        {
            var pair = BeginWithDrops(
                runs, (30012, [Dropped(AvatarItemId), Dropped(AvatarItemId)]));
            Assert.Equal(2, pair.Length);

            var earlier = await side.InUseCaseAsync(token => PickUp(handler, pair[0].Number, token));
            var later = await side.InUseCaseAsync(token => PickUp(handler, pair[1].Number, token));

            Assert.Equal(GroundPickupStatus.PickedUp, earlier.Status);
            Assert.Equal(GroundPickupStatus.PickedUp, later.Status);
            Assert.NotEqual(earlier.SlotIndex, later.SlotIndex);

            // Read out of the container rather than out of the entry load, and read first. The
            // load refuses a row whose facts are missing, so asking it before this comparison
            // would let the refusal answer for the comparison and leave what the two cores
            // actually carry untested.
            var (one, other) = await side.InUseCaseAsync(async token => (
                await Bag(side).GetAsync(earlier.SlotIndex, token).ConfigureAwait(false),
                await Bag(side).GetAsync(later.SlotIndex, token).ConfigureAwait(false)));
            Assert.NotNull(one);
            Assert.NotNull(other);

            // Not each other. One announced value written into both cores would fail here.
            Assert.NotEqual(one!.Snapshot.ToCore().Value, other!.Snapshot.ToCore().Value);

            // And both are still things the load serves, which is the half above.
            Assert.NotNull((await OneItemAsync(side, earlier.SlotIndex)).AvatarDetail);
            Assert.NotNull((await OneItemAsync(side, later.SlotIndex)).AvatarDetail);
        });
    }

    /// <summary>
    /// A piece of ordinary gear still reaches the bag carrying what the floor announced.
    /// </summary>
    /// <remarks>
    /// The regression this outcome is most able to cause. The second write exists for gear: the
    /// player is shown a value on the floor and the bag has to end up holding that same one, and
    /// the wear the piece was created with travels with it because the kill's frame has no field
    /// for it.
    /// <para>
    /// Neither number is written down here. The value is pinned by the shape the wire gives it --
    /// two pieces of one kill carry one value and a second kill's piece carries another -- so a
    /// second write that stopped happening would leave all three rows holding the count the bag
    /// opened their slots with, and the two comparisons below would not both hold. The wear is
    /// pinned against the announcement this file made on the floor.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_piece_of_gear_taken_off_the_floor_still_carries_what_the_floor_announced()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithASeededCharacterAsync(content, async (side, handler, runs) =>
        {
            var all = BeginWithDrops(
                runs,
                (30013, [Dropped(WeaponItemId), Dropped(WeaponItemId)]),
                (30014, [Dropped(WeaponItemId)]));
            var pair = all[..2];
            var alone = all[2];

            var first = await side.InUseCaseAsync(token => PickUp(handler, pair[0].Number, token));
            var second = await side.InUseCaseAsync(token => PickUp(handler, pair[1].Number, token));
            var third = await side.InUseCaseAsync(token => PickUp(handler, alone.Number, token));

            Assert.Equal(GroundPickupStatus.PickedUp, first.Status);
            Assert.Equal(GroundPickupStatus.PickedUp, second.Status);
            Assert.Equal(GroundPickupStatus.PickedUp, third.Status);

            var (one, other, apart) = await side.InUseCaseAsync(async token => (
                await Bag(side, InventoryContainerType.Main).GetAsync(first.SlotIndex, token).ConfigureAwait(false),
                await Bag(side, InventoryContainerType.Main).GetAsync(second.SlotIndex, token).ConfigureAwait(false),
                await Bag(side, InventoryContainerType.Main).GetAsync(third.SlotIndex, token).ConfigureAwait(false)));
            Assert.NotNull(one);
            Assert.NotNull(other);
            Assert.NotNull(apart);

            // One kill, one value.
            Assert.Equal(one!.Snapshot.ToCore().Value, other!.Snapshot.ToCore().Value);

            // Another kill, another value -- and neither is the count the bag opened the slot with.
            Assert.NotEqual(one.Snapshot.ToCore().Value, apart!.Snapshot.ToCore().Value);
            Assert.NotEqual(one.Count, one.Snapshot.ToCore().Value);

            // Not broken, on all three.
            Assert.Equal(AnnouncedDurability, one.Snapshot.ToCore().Durability);
            Assert.Equal(AnnouncedDurability, other.Snapshot.ToCore().Durability);
            Assert.Equal(AnnouncedDurability, apart.Snapshot.ToCore().Durability);
        });
    }

    /// <summary>One piece put on the floor by a kill, announced with wear.</summary>
    /// <remarks>
    /// The kind carried here is the drop rule's word for what the piece is; what decides whether
    /// the pick-up hands the piece anything is the archive's classification of the identifier, not
    /// this field. It is filled in truthfully anyway so the fixture does not describe a floor the
    /// server could not produce.
    /// </remarks>
    private static RolledDrop Dropped(int itemId) =>
        new(itemId,
            1,
            DropRarity.Epic,
            itemId == AvatarItemId ? DropItemKind.Avatar : DropItemKind.Equipment,
            AnnouncedDurability);

    private static DungeonGroundItem[] BeginWithDrops(
        DungeonRunDomainService runs,
        params (ushort ActorSequence, IReadOnlyList<RolledDrop> Drops)[] groups)
    {
        ushort number = 1;
        var items = groups
            .SelectMany(group => group.Drops.Select(drop => new DungeonGroundItem(
                number++,
                drop.ItemId,
                drop.Count,
                drop.Durability,
                IsEquipment: true,
                DungeonGroundItemSource.FromActor(group.ActorSequence),
                drop.Rarity,
                drop.Kind,
                drop.OwnerCharacterId)))
            .ToArray();
        var participant = new DungeonRunParticipant(
            new AccountId(Account), Who, Slot: 0, IsLeader: true);
        _ = runs.PreparePartyEntry(
            [participant],
            Layout,
            difficulty: 0,
            new DungeonCellSeed(
                Layout.StartCell,
                Layout.StartRoomMapId,
                FirstActorSequence: 1,
                Actors: [],
                GroundItems: items),
            DungeonRunId.New(),
            DateTimeOffset.UtcNow);
        return items;
    }

    private static ValueTask<GroundPickupResult> PickUp(
        PickUpGroundDropCommandHandler handler, ushort number, CancellationToken cancellationToken) =>
        handler.HandleAsync(
            default!,
            new PickUpGroundDropCommand(new AccountId(Account), Who, number),
            cancellationToken);

    private static IItemContainer Bag(InventorySide side,
        InventoryContainerType type = InventoryContainerType.Avatar) =>
        side.Containers.GetContainer(Who, type);

    /// <summary>What the entry load serves at that position of the character's main bag.</summary>
    private static async Task<InventoryItemState> OneItemAsync(InventorySide side, int slotIndex)
    {
        var loaded = await side.InUseCaseAsync(token =>
            new CharacterInventoryEntryDomainService(side.CharacterEntries)
                .LoadAsync(new AccountId(Account), Who, token));
        var main = loaded.Containers.Single(
            container => container.ContainerType == InventoryContainerType.Avatar);
        return Assert.Single(main.Items, item => item.SlotIndex == slotIndex);
    }

    /// <summary>
    /// One database with an account, a character and a bound main bag, and the pick-up command
    /// wired the way the server wires it.
    /// </summary>
    /// <remarks>
    /// The character service is left out because gold is the only branch that reaches it, and
    /// nothing here picks up coins. Everything else is the production type: the run book, the
    /// floor, and the character's own persistent container.
    /// </remarks>
    private static async Task WithASeededCharacterAsync(
        ServiceProvider content,
        Func<InventorySide, PickUpGroundDropCommandHandler, DungeonRunDomainService, Task> body)
    {
        var placements = content.GetRequiredService<IItemPlacementDefinitionSource>();
        var path = Path.Combine(Path.GetTempPath(), $"dfo-picked-avatar-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path, placements);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO accounts(account_id,m_id) VALUES({Account},'picked');
                    INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES({Character},{Account},'picked',3,60);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            side.BindMain(Character, placements: placements);
            side.BindList(InventoryContainerType.Avatar, Character, placements: placements);

            var runs = new DungeonRunDomainService();
            var handler = new PickUpGroundDropCommandHandler(
                runs,
                new GroundPickupDomainService(),
                side.Containers,
                null!,
                new NoQuestProgress(),
                new Dfo.Server.Application.Tests.Support.OrdinaryItemGrantDomainService(
                    new ItemPlacementRuleService(placements,
                        content.GetRequiredService<IEquipmentSlotDefinitionSource>()), side.Lifecycle));

            await body(side, handler, runs);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }

    private sealed class NoQuestProgress : IQuestProgressDomainService
    {
        public ValueTask<QuestProgressReport> ReportAsync(
            AccountId accountId,
            CharacterId characterId,
            IReadOnlyList<QuestProgressFact> facts,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new QuestProgressReport(null, [], null));
    }
}
