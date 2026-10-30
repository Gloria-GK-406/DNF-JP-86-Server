using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// What comes of taking part of one pile into a slot of its own.
/// </summary>
/// <remarks>Splitting preserves core restrictions and expiry but creates no world identity.</remarks>
public sealed class SplitPileTests
{
    private const int Character = 70;

    private const int Account = 7;

    private const int PotionId = 1234;

    /// <summary>The kind byte the seeded pile carries, and the one a new pile must carry too.</summary>
    private const byte PotionKind = 2;

    /// <summary>
    /// Splitting a pile leaves two usable slots, and the new one is marked with nothing.
    /// </summary>
    /// <remarks>Both halves retain the original payload except for count.</remarks>
    [Fact]
    public async Task SplittingAPileLeavesTwoUsablePilesAndMarksNeither()
    {
        var path = NewDatabase();
        try
        {
            await SeedCharacterAsync(path);
            await SeedPileAsync(path, slot: 65, Marked(5));

            var side = new InventorySide(path);
            side.BindMain(Character);
            var bag = side.Containers.GetContainer(
                new CharacterId(Character), InventoryContainerType.Main);

            var moved = await side.InUseCaseAsync(
                token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Consumable, 0), new(BusinessInventorySpace.Consumable, 1), 2, token));

            Assert.Equal(new ItemMoveOutcome.Moved(2, PotionId), moved);

            var (left, opened) = await side.InUseCaseAsync(async token => (
                await bag.GetAsync(65, token).ConfigureAwait(false),
                await bag.GetAsync(66, token).ConfigureAwait(false)));

            // Both slots hold a usable pile, said the same way: what sort of thing it is, what it
            // is, and how many. Nothing but the count differs between them.
            Assert.NotNull(left);
            Assert.NotNull(opened);
            Assert.True(left.IsPile);
            Assert.True(opened.IsPile);
            Assert.Equal((PotionKind, PotionId, 3), (left.Snapshot.LegacyKind, left.ItemId, left.Count));
            Assert.Equal((PotionKind, PotionId, 2), (opened.Snapshot.LegacyKind, opened.ItemId, opened.Count));

            // Splitting must not remove restrictions or expiry from either half.
            Assert.Equal(Marked(3), left.Snapshot.ToCore());
            Assert.Equal(Marked(2), opened.Snapshot.ToCore());

            // Neither position names a world row, so neither half has a name for anything to tell
            // it apart by -- which is the same thing the projection says a moment later.
            Assert.Equal(new Slot("template", PotionId, 3), await SlotAsync(path, 65));
            Assert.Equal(new Slot("template", PotionId, 2), await SlotAsync(path, 66));

            var projected = await side.InUseCaseAsync(token =>
                side.ReadMainItemsAsync(new AccountId(Account), new CharacterId(Character), token));

            Assert.Equal(
                [(65, 3, (ItemIdentity?)null), (66, 2, null)],
                projected
                    .Select(item => (item.SlotIndex, item.Core.Value, item.Identity))
                    .OrderBy(item => item.SlotIndex)
                    .ToArray());
            Assert.All(
                projected,
                item => Assert.Null(item.Identity));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>
    /// Each half can be moved on its own afterwards, which is what "usable" means.
    /// </summary>
    /// <remarks>
    /// Bytes that look right are not the claim. A half that could not be picked up again, or one
    /// whose position the store would refuse to rewrite, would satisfy every assertion above and
    /// still be a slot the player cannot touch. So each is moved once more, separately, and the
    /// counts are read back from the database afterwards.
    /// </remarks>
    [Fact]
    public async Task EitherHalfCanBeMovedOnItsOwnAfterTheSplit()
    {
        var path = NewDatabase();
        try
        {
            await SeedCharacterAsync(path);
            await SeedPileAsync(path, slot: 65, Marked(5));

            var side = new InventorySide(path);
            side.BindMain(Character);
            var bag = side.Containers.GetContainer(
                new CharacterId(Character), InventoryContainerType.Main);

            _ = await side.InUseCaseAsync(token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Consumable, 0), new(BusinessInventorySpace.Consumable, 1), 2, token));
            var newHalf = await side.InUseCaseAsync(token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Consumable, 1), new(BusinessInventorySpace.Consumable, 2), 0, token));
            var oldHalf = await side.InUseCaseAsync(token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Consumable, 0), new(BusinessInventorySpace.Consumable, 3), 0, token));

            Assert.Equal(new ItemMoveOutcome.Moved(2, PotionId), newHalf);
            Assert.Equal(new ItemMoveOutcome.Moved(3, PotionId), oldHalf);
            Assert.Null(await SlotAsync(path, 65));
            Assert.Null(await SlotAsync(path, 66));
            Assert.Equal(new Slot("template", PotionId, 2), await SlotAsync(path, 67));
            Assert.Equal(new Slot("template", PotionId, 3), await SlotAsync(path, 68));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>
    /// Pouring the two halves back together leaves one pile again, with nothing left over.
    /// </summary>
    /// <remarks>
    /// The other half of interchangeability: if the split had made two different things, putting
    /// them back would have to choose which one survived. It does not have to, because there is
    /// nothing to choose between -- the merge is a count and a cleared slot.
    /// </remarks>
    [Fact]
    public async Task TheTwoHalvesPourBackIntoOnePile()
    {
        var path = NewDatabase();
        try
        {
            await SeedCharacterAsync(path);
            await SeedPileAsync(path, slot: 65, Marked(5));

            var side = new InventorySide(path);
            side.BindMain(Character);
            var bag = side.Containers.GetContainer(
                new CharacterId(Character), InventoryContainerType.Main);

            _ = await side.InUseCaseAsync(token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Consumable, 0), new(BusinessInventorySpace.Consumable, 1), 2, token));
            var poured = await side.InUseCaseAsync(token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Consumable, 1), new(BusinessInventorySpace.Consumable, 0), 0, token));

            Assert.Equal(new ItemMoveOutcome.Moved(2, PotionId), poured);
            Assert.Null(await SlotAsync(path, 66));
            Assert.Equal(new Slot("template", PotionId, 5), await SlotAsync(path, 65));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>A pile with payload metadata, expiry and trade restrictions.</summary>
    private static InventoryItemCoreState Marked(int count) =>
        StackableItemCore.Create(PotionKind, PotionId, count) with
        {
            Durability = 4242,
            EnchantUpgradeCount = 9,
            SealFlag = 3,
            ExpireTime = 1800000000,
            TradeRestriction = 1,
        };

    /// <summary>What one position of <c>item_slots</c> says it holds.</summary>
    private sealed record Slot(string Kind, int Id, int Count);

    private static async Task<Slot?> SlotAsync(string path, int slotIndex)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT kind,id,count FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=@cid AND list_type=0 AND slot_index=@slot";
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@slot", slotIndex);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new Slot(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2))
            : null;
    }

    private static async Task SeedPileAsync(string path, int slot, InventoryItemCoreState core)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO items(item_core) VALUES(@core);"
            + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
            + "VALUES('character',@cid,0,@slot,'template',@id,last_insert_rowid(),@count)";
        command.Parameters.AddWithValue("@id", core.ItemId);
        command.Parameters.AddWithValue("@count", core.Value);
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", InventoryItemCore.Encode(core));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedCharacterAsync(string path)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO characters(character_id,account_id,name) VALUES(@cid,@aid,'split')";
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@aid", Account);
        await command.ExecuteNonQueryAsync();
    }

    private static string NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-split-{Guid.NewGuid():N}.db");

        // The repositories own the schema; everything here only writes into it.
        _ = new SqliteDatabase(path, TestRequired.TransactionScope());
        return path;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        return connection;
    }

    private static void Clean(string path)
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
