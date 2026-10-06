using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// What happens to a delivered avatar when the player drags it one slot over.
/// </summary>
/// <remarks>
/// A drag settled by the client names a count, and for a plain drag that count is one -- the field
/// <c>MoveItemSpaceDecoder</c> reads out of the body. A container answers such a request by asking
/// how much the slot holds and comparing the two, so what "how much" means for a thing that does
/// not stack is the whole of what these cases are about.
/// <para>
/// The value inside an avatar's core is its identity, not a quantity: it is what the store's fill
/// looks the six facts up by, and what the load, the save and both projections refuse an avatar
/// without. A container that read that number as a quantity would compare a drag's one against it,
/// find them different, and take the request for a request to divide the pile -- leaving the source
/// carrying one less than an identity and the destination carrying a bare row with no facts on it.
/// So these do not assert a number: they grant through the production verb, drag through the
/// production verb, and then ask the entry load -- the reader that refuses a factless avatar -- to
/// serve the thing again.
/// </para>
/// <para>
/// The pile case stands beside them deliberately. Dividing a pile is what the same request means
/// for the other sort of content, and a repair that made every drag move everything would be caught
/// by it rather than by an argument.
/// </para>
/// <para>
/// The archive is the operator's own and the store is a real database, for the same reason
/// <c>GrantedAvatarIsWholeTests</c> gives: a stubbed archive would let the two sides agree on a
/// classification this file invented.
/// </para>
/// </remarks>
public sealed class DraggedAvatarStaysWholeTests
{
    /// <summary><c>equipment.lst</c>'s <c>101550507</c>, an avatar cap or coat.</summary>
    /// <remarks>
    /// The same identifier <c>GrantedAvatarIsWholeTests</c> grants and one of the three
    /// <c>PvfDropRateTableSourceTests.No_avatar_declares_itself_creatable</c> names, so it is one
    /// the archive is known to declare rather than one picked here.
    /// </remarks>
    private const int AvatarItemId = 101550507;

    /// <summary>A second avatar template, so a drag can land on something that is not itself.</summary>
    private const int OtherAvatarItemId = 101550511;

    /// <summary>
    /// <c>stackable/event/chn_southerndale_dungeon_2011/chn_invitation_secret.stk</c>: a pile.
    /// </summary>
    /// <remarks>
    /// Borrowed from <c>ItemClassificationTests</c>, which already holds that the archive reads
    /// this one as interchangeable.
    /// </remarks>
    private const int PileItemId = 2680738;

    /// <summary>The kind byte storage judges an avatar by, written as a literal.</summary>
    private const byte KindStorageJudgesAnAvatarBy = 8;

    private const int Account = 84;

    private const int Character = 93;

    /// <summary>
    /// A drag of one moves the whole avatar and leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// The count sent is the one a plain drag sends. What is asserted afterwards is read out of the
    /// container, both ends: the slot it came from holds nothing at all, and the slot it went to
    /// holds the identity that was standing in the first one before the drag. A request taken for a
    /// division would leave the source holding that identity less one and the destination holding a
    /// one, so neither end could be read as it is here.
    /// </remarks>
    [SkippableFact]
    public async Task A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithASeededCharacterAsync(content, async side =>
        {
            var bag = Bag(side);
            var landed = await GrantAsync(side, AvatarItemId);
            var before = await side.InUseCaseAsync(
                token => bag.GetAsync(landed.SlotIndex, token));
            Assert.NotNull(before);
            var identity = before!.Snapshot.ToCore().Value;

            var target = await FreeSlotBesideAsync(side, content, AvatarItemId, landed.SlotIndex);
            var moved = await side.InUseCaseAsync(
                token => side.MoveAsync(new CharacterId(Character),
                    new(BusinessInventorySpace.Avatar, landed.SlotIndex),
                    new(BusinessInventorySpace.Avatar, target), 1, token));

            Assert.Equal(new ItemMoveOutcome.Moved(1, AvatarItemId), moved);

            var (source, destination) = await side.InUseCaseAsync(async token => (
                await bag.GetAsync(landed.SlotIndex, token).ConfigureAwait(false),
                await bag.GetAsync(target, token).ConfigureAwait(false)));

            // Nothing stayed behind. A division would have left the identity less one here.
            Assert.Null(source);

            // And the thing that arrived is the thing that left, under its own name.
            Assert.NotNull(destination);
            Assert.Equal(AvatarItemId, destination!.Snapshot.ToCore().ItemId);
            Assert.Equal(KindStorageJudgesAnAvatarBy, destination.Snapshot.ToCore().ItemKind);
            Assert.Equal(identity, destination.Snapshot.ToCore().Value);

            // What the container says the slot holds, which is the question the drag turned on.
            Assert.Equal(1, destination.Count);
        });
    }

    /// <summary>
    /// An avatar that has been dragged is served again, whole, by the reader that refuses one that
    /// is not.
    /// </summary>
    /// <remarks>
    /// The entry load is the assertion. It throws for an avatar whose facts it cannot find, so a
    /// drag that put down a row carrying none would not reach any comparison below -- and before
    /// this outcome, a drag did exactly that. What is compared is what the load served before the
    /// drag against what it served after, at the slot the drag aimed at.
    /// </remarks>
    [SkippableFact]
    public async Task An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithASeededCharacterAsync(content, async side =>
        {
            var bag = Bag(side);
            var landed = await GrantAsync(side, AvatarItemId);
            var before = await OneItemAsync(side, landed.SlotIndex);
            var facts = before.AvatarDetail;
            Assert.NotNull(facts);

            var target = await FreeSlotBesideAsync(side, content, AvatarItemId, landed.SlotIndex);
            _ = await side.InUseCaseAsync(
                token => side.MoveAsync(new CharacterId(Character),
                    new(BusinessInventorySpace.Avatar, landed.SlotIndex),
                    new(BusinessInventorySpace.Avatar, target), 1, token));

            var after = await OneItemAsync(side, target);
            Assert.Equal(before.Core.ItemId, after.Core.ItemId);
            Assert.Equal(before.Core.ItemKind, after.Core.ItemKind);
            Assert.Equal(before.Core.Value, after.Core.Value);

            var served = after.AvatarDetail;
            Assert.NotNull(served);
            Assert.Equal(facts!.ExpireDate, served!.ExpireDate);
            Assert.Equal(facts.ClearAvatarId, served.ClearAvatarId);
            Assert.Equal(facts.JewelSocket, served.JewelSocket);
            Assert.Equal(facts.Color1, served.Color1);
            Assert.Equal(facts.Color2, served.Color2);
            Assert.Equal(facts.DeleteDate, served.DeleteDate);

            // And the slot it left is not among what the load serves.
            Assert.DoesNotContain(await AvatarItemsAsync(side), item => item.SlotIndex == landed.SlotIndex);
        });
    }

    /// <summary>
    /// A drag of one onto a slot another avatar stands in trades the two.
    /// </summary>
    /// <remarks>
    /// <b>Measured, not argued.</b> Two outcomes were available to the container here -- trading the
    /// two ends, or refusing the request because only part of the pile was offered for a trade --
    /// and which one it is falls out of what the count of a non-stacking thing is taken to be. This
    /// case records the answer read off a run rather than the answer reasoned to: the drag is
    /// carried out and both ends are read back out of the container afterwards.
    /// <para>
    /// The identities are what the two ends are told apart by. Two rows of two different templates
    /// would swap identifiers under any exchange at all, so comparing templates alone would leave a
    /// container that moved the cores while keeping the names green.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_drag_of_one_onto_another_avatar_trades_the_two_slots()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithASeededCharacterAsync(content, async side =>
        {
            var bag = Bag(side);
            var first = await GrantAsync(side, AvatarItemId);
            var second = await GrantAsync(side, OtherAvatarItemId);
            Assert.NotEqual(first.SlotIndex, second.SlotIndex);

            var (wasFirst, wasSecond) = await side.InUseCaseAsync(async token => (
                await bag.GetAsync(first.SlotIndex, token).ConfigureAwait(false),
                await bag.GetAsync(second.SlotIndex, token).ConfigureAwait(false)));
            Assert.NotNull(wasFirst);
            Assert.NotNull(wasSecond);

            var moved = await side.InUseCaseAsync(
                token => side.MoveAsync(new CharacterId(Character),
                    new(BusinessInventorySpace.Avatar, first.SlotIndex),
                    new(BusinessInventorySpace.Avatar, second.SlotIndex), 1, token));

            Assert.Equal(new ItemMoveOutcome.Moved(1, AvatarItemId), moved);

            var (nowFirst, nowSecond) = await side.InUseCaseAsync(async token => (
                await bag.GetAsync(first.SlotIndex, token).ConfigureAwait(false),
                await bag.GetAsync(second.SlotIndex, token).ConfigureAwait(false)));

            Assert.NotNull(nowFirst);
            Assert.NotNull(nowSecond);
            Assert.Equal(
                (OtherAvatarItemId, wasSecond!.Snapshot.ToCore().Value),
                (nowFirst!.Snapshot.ToCore().ItemId, nowFirst.Snapshot.ToCore().Value));
            Assert.Equal(
                (AvatarItemId, wasFirst!.Snapshot.ToCore().Value),
                (nowSecond!.Snapshot.ToCore().ItemId, nowSecond.Snapshot.ToCore().Value));

            // Both are still avatars the entry load will serve, which a trade that dropped their
            // facts would not leave true.
            Assert.NotNull((await OneItemAsync(side, first.SlotIndex)).AvatarDetail);
            Assert.NotNull((await OneItemAsync(side, second.SlotIndex)).AvatarDetail);
        });
    }

    /// <summary>
    /// The same request divides a pile, which is what it has always meant for one.
    /// </summary>
    /// <remarks>
    /// The contrast is the point. A drag of one is one request shape; a pile of two answers it by
    /// leaving one behind and putting one down, and an avatar answers it by going whole. A repair
    /// that decided every drag moves everything would leave the avatar cases above green and this
    /// one red, which is the only reason to run the two through the same container in one file.
    /// </remarks>
    [SkippableFact]
    public async Task A_drag_of_one_off_a_pile_still_divides_the_pile()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithASeededCharacterAsync(content, async side =>
        {
            var bag = MainBag(side);
            var rules = ArchiveRules(content);
            var placement = await rules.PlacementForAsync(PileItemId, CancellationToken.None);
            Assert.NotNull(placement);
            Assert.Equal(
                ItemWorldClass.Interchangeable,
                await rules.ClassifyAsync(PileItemId, CancellationToken.None));

            // Two is the smallest pile a drag of one can divide, and the archive's limit is read
            // rather than assumed so that a template that stopped stacking is seen here.
            Assert.True(placement!.StackLimit >= 2, "The archive no longer stacks this template.");

            var landed = await side.InUseCaseAsync(
                token => side.Grants.GrantAsync(new AccountId(Account), new CharacterId(Character),
                    side.Containers, PileItemId, 2, token));
            var local = Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(landed.Placement);
            var opened = local with { SlotIndex = LegacyInventoryStorageCoordinates.ToStorage(
                new BusinessSlot(landed.Container.Space, local.SlotIndex)).Index };

            var target = await FreeSlotBesideAsync(side, content, PileItemId, opened.SlotIndex);
            var moved = await side.InUseCaseAsync(
                token => side.MoveAsync(new CharacterId(Character),
                    LegacyInventoryStorageCoordinates.ToBusiness(new(InventoryContainerType.Main, opened.SlotIndex)),
                    LegacyInventoryStorageCoordinates.ToBusiness(new(InventoryContainerType.Main, target)), 1, token));

            Assert.Equal(new ItemMoveOutcome.Moved(1, PileItemId), moved);

            var (source, destination) = await side.InUseCaseAsync(async token => (
                await bag.GetAsync(opened.SlotIndex, token).ConfigureAwait(false),
                await bag.GetAsync(target, token).ConfigureAwait(false)));

            Assert.NotNull(source);
            Assert.NotNull(destination);
            Assert.Equal((PileItemId, 1), (source!.Snapshot.ToCore().ItemId, source.Count));
            Assert.Equal((PileItemId, 1), (destination!.Snapshot.ToCore().ItemId, destination.Count));
        });
    }

    private static IItemContainer Bag(InventorySide side) =>
        side.Containers.GetContainer(new CharacterId(Character), InventoryContainerType.Avatar);

    private static IItemContainer MainBag(InventorySide side) =>
        side.Containers.GetContainer(new CharacterId(Character), InventoryContainerType.Main);

    /// <summary>One item granted through the container verb every reward reaches.</summary>
    private static async Task<ItemPlacementOutcome.PutInEmptySlot> GrantAsync(
        InventorySide side, int itemId)
    {
        var granted = await side.InUseCaseAsync(
            token => side.Grants.GrantAsync(new AccountId(Account), new CharacterId(Character),
                side.Containers, itemId, 1, token));
        return Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(granted.Placement);
    }

    /// <summary>
    /// A slot of the item's own band that nothing is standing in, next to the one it landed in.
    /// </summary>
    /// <remarks>
    /// The band is the archive's rather than a number written here, because a drag onto a slot
    /// outside it is refused before any of this is reached and the case would then be pinning a
    /// refusal it did not mean to ask about.
    /// </remarks>
    private static async Task<int> FreeSlotBesideAsync(
        InventorySide side, ServiceProvider content, int itemId, int landedSlotIndex)
    {
        var placement = await ArchiveRules(content)
            .PlacementForAsync(itemId, CancellationToken.None);
        Assert.NotNull(placement);

        var bag = itemId == PileItemId ? MainBag(side) : Bag(side);
        var taken = (await side.InUseCaseAsync(token => bag.GetAllAsync(token)))
            .Select(item => item.SlotIndex)
            .ToHashSet();
        var mappings = new InventorySlotMappingRuleService();
        for (var index = placement!.Destination.FirstIndex;
             index <= placement.Destination.LastIndex;
             index++)
        {
            var slot = mappings.ToGameSlot(new BusinessSlot(placement.Destination.Space, index)).Index;
            if (slot != landedSlotIndex && !taken.Contains(slot))
            {
                return slot;
            }
        }

        Assert.Fail("The archive's band for this template has no free slot to drag into.");
        return -1;
    }

    /// <summary>What the entry load serves at that position of the character's main bag.</summary>
    private static async Task<InventoryItemState> OneItemAsync(InventorySide side, int slotIndex) =>
        Assert.Single(await AvatarItemsAsync(side), item => item.SlotIndex == slotIndex);

    private static async Task<IReadOnlyList<InventoryItemState>> AvatarItemsAsync(InventorySide side)
    {
        var loaded = await side.InUseCaseAsync(token =>
            new CharacterInventoryEntryDomainService(side.CharacterEntries)
                .LoadAsync(new AccountId(Account), new CharacterId(Character), token));
        return loaded.Containers
            .Single(container => container.ContainerType == InventoryContainerType.Avatar)
            .Items;
    }

    private static ItemPlacementRuleService ArchiveRules(ServiceProvider content) =>
        new(content.GetRequiredService<IItemPlacementDefinitionSource>(),
            content.GetRequiredService<IEquipmentSlotDefinitionSource>());

    /// <summary>One database with an account, a character and a bound main bag, read by the archive.</summary>
    private static async Task WithASeededCharacterAsync(
        ServiceProvider content, Func<InventorySide, Task> body)
    {
        var placements = content.GetRequiredService<IItemPlacementDefinitionSource>();
        var path = Path.Combine(Path.GetTempPath(), $"dfo-dragged-avatar-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path, placements);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO accounts(account_id,m_id) VALUES({Account},'dragged');
                    INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES({Character},{Account},'dragged',3,60);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            side.BindMain(Character, placements: placements);
            side.BindList(InventoryContainerType.Avatar, Character, placements: placements);
            await body(side);
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
}
