using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// What the entry save takes away, now that what it keeps is named rather than keyed.
/// </summary>
/// <remarks>
/// The save reconciles: it removes every row of this character's inventory that the lease no longer
/// holds, and it does so by naming what to keep. That scan used to keep by the storage row key,
/// which every row had; it now keeps by the name of the thing, which only a thing with a name has.
/// The difference is the whole risk of this change and it is a silent one -- keep too little and
/// the player's items are gone, with nothing thrown and nothing logged, discovered at the next
/// login.
/// <para>
/// So all three outcomes are pinned here rather than the happy one. A thing with a name keeps its
/// row and therefore its name. A pile, which has none, is taken away and written again in the same
/// save, so what the player holds is unchanged while its row is not. And something the lease has
/// genuinely stopped holding is still removed, because a scan that kept everything would be just as
/// wrong in the other direction and would look perfectly healthy.
/// </para>
/// </remarks>
public sealed class EntrySaveReconciliationTests
{
    private const int Account = 7;

    private const int Character = 70;

    private const int SwordId = 4000;

    private const int SecondSwordId = 4001;

    private const int PotionId = 1000;

    /// <summary>
    /// The named thing keeps its row, the pile is rewritten, and the dropped row goes.
    /// </summary>
    /// <remarks>
    /// The sword's identity is read out of the database before and after and compared, so this is
    /// not the lease being believed about what it did. The pile is checked by what the player holds
    /// rather than by its row: its count and its slot are what matter, and its row key is the thing
    /// that is allowed to change.
    /// </remarks>
    [Fact]
    public async Task ASaveKeepsWhatHasANameRewritesWhatHasNoneAndDropsWhatTheLeaseLetGo()
    {
        var path = NewDatabase();
        try
        {
            await SeedCharacterAsync(path);
            await SeedAsync(path, slot: 3, itemKind: 1, itemId: SwordId, value: 0);
            await SeedAsync(path, slot: 4, itemKind: 1, itemId: SecondSwordId, value: 0);
            await SeedAsync(path, slot: 65, itemKind: 6, itemId: PotionId, value: 5);

            var side = new InventorySide(path, new DeclaredArchive());
            var loaded = await side.InUseCaseAsync(token =>
                new CharacterInventoryEntryDomainService(side.CharacterEntries)
                    .LoadAsync(new AccountId(Account), new CharacterId(Character), token));

            var main = loaded.Containers.Single(
                container => container.ContainerType == InventoryContainerType.Main);
            var sword = main.Items.Single(item => item.Core.ItemId == SwordId);
            var potion = main.Items.Single(item => item.Core.ItemId == PotionId);
            var dropped = main.Items.Single(item => item.Core.ItemId == SecondSwordId);

            // The archive says which of the three is a thing and which is a pile, and the load
            // hands out a name to exactly the things.
            Assert.NotNull(sword.Identity);
            Assert.NotNull(dropped.Identity);
            Assert.Null(potion.Identity);
            Assert.Equal(sword.Identity!.Value.Value, await IdentityAtAsync(path, 3));

            var named = sword.Identity!.Value;
            await side.InUseCaseAsync(token => side.Leases.SaveAsync(
                new EntryInventoryLeaseSnapshot(
                    Session(),
                    new CharacterItemLeaseState(
                        new CharacterId(Character),
                        [
                            new InventoryContainerState(
                                InventoryContainerType.Main,
                                0,
                                [sword with { SlotIndex = 7 }, potion with { SlotIndex = 66 }]),
                        ]),
                    1,
                    true),
                token));

            // The sword is the same sword: the row that stood in slot 3 is the row now standing in
            // slot 7, under the identity it already had.
            Assert.Equal(named.Value, await IdentityAtAsync(path, 7));
            Assert.Null(await IdentityAtAsync(path, 3));

            // The pile is still five potions in slot 66. Which row carries them is not a question
            // anyone may ask, and the scan is free to have answered it differently.
            Assert.Equal((PotionId, 5), await HeldAtAsync(path, 66));
            Assert.Null(await IdentityAtAsync(path, 65));

            // What the lease stopped holding is gone, which is what the scan is for.
            Assert.Null(await IdentityAtAsync(path, 4));
            Assert.Equal(2, await RowCountAsync(path));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>
    /// A save that holds on to nothing empties the inventory, exactly as it did before.
    /// </summary>
    /// <remarks>
    /// The edge the keep list has always had: with nothing to keep, the statement has no exclusion
    /// at all. Worth pinning because the list is now built from a nullable, and a list that came
    /// out empty for the wrong reason would take this same road with the player's things still in
    /// the bag.
    /// </remarks>
    [Fact]
    public async Task ASaveThatHoldsNothingLeavesTheInventoryEmpty()
    {
        var path = NewDatabase();
        try
        {
            await SeedCharacterAsync(path);
            await SeedAsync(path, slot: 3, itemKind: 1, itemId: SwordId, value: 0);
            await SeedAsync(path, slot: 65, itemKind: 6, itemId: PotionId, value: 5);

            var side = new InventorySide(path, new DeclaredArchive());
            await side.InUseCaseAsync(token => side.Leases.SaveAsync(
                new EntryInventoryLeaseSnapshot(
                    Session(),
                    new CharacterItemLeaseState(
                        new CharacterId(Character),
                        [new InventoryContainerState(InventoryContainerType.Main, 0, [])]),
                    1,
                    true),
                token));

            Assert.Equal(0, await RowCountAsync(path));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>
    /// A save naming one thing in two positions is refused, and changes nothing.
    /// </summary>
    /// <remarks>
    /// A thing is in one place. A position names the row rather than holding it now, so a lease
    /// saying the same thing is in two of them would leave two positions pointing at one row --
    /// which the player is shown as two swords and can move away twice, out of one sword. The
    /// older shape could not express that, because a row carried its position and could only carry
    /// one; it lost the first position instead, which was wrong in a smaller and quieter way.
    ///
    /// The database is read afterwards rather than only the exception, because a refusal that had
    /// already emptied the character's positions would leave the inventory gone and still be
    /// reported as a refusal.
    /// </remarks>
    [Fact]
    public async Task ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing()
    {
        var path = NewDatabase();
        try
        {
            await SeedCharacterAsync(path);
            await SeedAsync(path, slot: 3, itemKind: 1, itemId: SwordId, value: 0);

            var side = new InventorySide(path, new DeclaredArchive());
            var loaded = await side.InUseCaseAsync(token =>
                new CharacterInventoryEntryDomainService(side.CharacterEntries)
                    .LoadAsync(new AccountId(Account), new CharacterId(Character), token));
            var sword = loaded.Containers
                .Single(container => container.ContainerType == InventoryContainerType.Main)
                .Items.Single(item => item.Core.ItemId == SwordId);
            var named = sword.Identity!.Value;

            var refused = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await side.InUseCaseAsync(token => side.Leases.SaveAsync(
                    new EntryInventoryLeaseSnapshot(
                        Session(),
                        new CharacterItemLeaseState(
                            new CharacterId(Character),
                            [
                                new InventoryContainerState(
                                    InventoryContainerType.Main,
                                    0,
                                    [sword with { SlotIndex = 7 }, sword with { SlotIndex = 8 }]),
                            ]),
                        1,
                        true),
                    token)));

            Assert.Contains("more than one position", refused.Message, StringComparison.Ordinal);
            Assert.Equal(named.Value, await IdentityAtAsync(path, 3));
            Assert.Equal(1, await RowCountAsync(path));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>An archive that declares one template a worn piece and the rest ordinary.</summary>
    private sealed class DeclaredArchive : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(
            int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(
                itemId is SwordId or SecondSwordId
                    ? new ItemPlacement(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 120))
                        { WorldClass = ItemWorldClass.Equipment }
                    : new ItemPlacement(2, 100, new(BusinessInventorySpace.Consumable, 0, 55)));
    }

    private static SelectedSessionIdentity Session() =>
        SelectedSessionIdentity.Create(
            new AccountId(Account),
            new CharacterId(Character),
            new NetworkConnectionId("Game:reconcile"),
            1,
            10011);

    private static async Task<long?> IdentityAtAsync(string path, int slotIndex)
    {
        var found = await ScalarAsync(
            path,
            "SELECT core_uid FROM item_slots "
            + "WHERE owner_scope='character' AND owner_id=@cid "
            + "AND list_type=0 AND slot_index=@slot",
            command =>
            {
                command.Parameters.AddWithValue("@cid", Character);
                command.Parameters.AddWithValue("@slot", slotIndex);
            });
        return found is null or DBNull ? null : Convert.ToInt64(found, null);
    }

    private static async Task<(int ItemId, int Count)?> HeldAtAsync(string path, int slotIndex)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT held.item_core "
            + "FROM item_slots place JOIN items held ON held.item_uid=place.core_uid "
            + "WHERE place.owner_scope='character' AND place.owner_id=@cid "
            + "AND place.list_type=0 AND place.slot_index=@slot";
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@slot", slotIndex);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var core = (byte[])reader.GetValue(0);
        return (BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4)));
    }

    private static async Task<int> RowCountAsync(string path) =>
        Convert.ToInt32(
            await ScalarAsync(
                path,
                "SELECT COUNT(*) FROM item_slots place JOIN items held ON held.item_uid=place.core_uid "
                + "WHERE place.owner_scope='character' AND place.owner_id=@cid",
                command => command.Parameters.AddWithValue("@cid", Character)),
            null);

    private static async Task SeedAsync(
        string path, int slot, byte itemKind, int itemId, int value)
    {
        var core = new byte[82];
        core[0] = itemKind;
        core[66] = byte.MaxValue;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), value);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(21, 4), -1);
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO items(item_core) VALUES(@core);"
            + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
            + "VALUES('character',@cid,0,@slot,'world',last_insert_rowid(),"
            + "last_insert_rowid(),1)";
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", core);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedCharacterAsync(string path)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO characters(character_id,account_id,name) VALUES(@cid,@aid,'reconcile')";
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@aid", Account);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(
        string path, string sql, Action<SqliteCommand> bind)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind(command);
        return await command.ExecuteScalarAsync();
    }

    private static string NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-reconcile-{Guid.NewGuid():N}.db");

        // The repositories own the schema; everything here only writes into it.
        _ = new SqliteDatabase(path, new DomainTransactionScope());
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
