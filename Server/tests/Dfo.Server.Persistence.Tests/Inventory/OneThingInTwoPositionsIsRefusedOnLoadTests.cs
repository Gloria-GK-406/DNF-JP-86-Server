using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// One thing is in one place, checked on the way in as well as on the way out.
/// </summary>
/// <remarks>
/// Since a position names a row rather than holding it, two positions can name the same row. No
/// constraint refuses that: <c>UNIQUE(owner_scope, owner_id, list_type, slot_index)</c> holds one
/// thing per position, which is the other direction, and <c>core_uid</c> carries no uniqueness of
/// its own. So both loads have to say it.
/// <para>
/// The pile case is written out separately because it is the one an implementation is most likely
/// to miss; why it is easy to miss is on <c>InventoryEntryAssembly.RequireOneNameEach</c>.
/// </para>
/// </remarks>
public sealed class OneThingInTwoPositionsIsRefusedOnLoadTests
{
    private const int Account = 71;
    private const int Character = 83;

    /// <remarks>
    /// Both positions are in one container and differ only in <c>slot_index</c>, so the unique
    /// index over positions is satisfied. What it cannot see is that both name one row.
    /// </remarks>
    [Fact]
    public async Task ACharactersInventoryThatNamesOneThingInTwoPositionsIsRefused()
    {
        await WithASeededCharacterAsync(async (path, _) =>
        {
            var side = new InventorySide(path, new Declaring(ItemWorldClass.Equipment));
            var thing = await InsertWorldRowAsync(path, itemKind: 1, itemId: 2001, value: 90210);
            await NameAsync(path, "character", Character, listType: 0, slot: 3, kind: "world",
                id: thing, coreUid: thing, count: 1);
            await NameAsync(path, "character", Character, listType: 0, slot: 4, kind: "world",
                id: thing, coreUid: thing, count: 1);

            var refused = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await side.InUseCaseAsync(token => side.CharacterEntries
                    .LoadCharacterAsync(new AccountId(Account), new CharacterId(Character), token)));

            Assert.Contains(thing.ToString(System.Globalization.CultureInfo.InvariantCulture),
                refused.Message, StringComparison.Ordinal);
        });
    }

    /// <remarks>
    /// The cargo is a second load over the same table, and a shape refused on one side and carried
    /// on the other would be a difference nothing in the storage justifies.
    /// </remarks>
    [Fact]
    public async Task AnAccountCargoThatNamesOneThingInTwoPositionsIsRefused()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await ExecuteAsync(path, $"""
                INSERT INTO account_cargo_state(account_id,selection_key,value32,item_count)
                VALUES({Account},1,2,3);
                """);
            var thing = await InsertWorldRowAsync(path, itemKind: 1, itemId: 2600, value: 5);
            await NameAsync(path, "account", Account, listType: 12, slot: 5, kind: "template",
                id: 2600, coreUid: thing, count: 5);
            await NameAsync(path, "account", Account, listType: 12, slot: 6, kind: "template",
                id: 2600, coreUid: thing, count: 5);

            _ = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await side.InUseCaseAsync(token => side.AccountEntries
                    .LoadAccountAsync(new AccountId(Account), new CharacterId(Character), token)));
        });
    }

    /// <summary>The same refusal for a thing the load gives no identity to.</summary>
    /// <remarks>
    /// The archive stand-in here calls the template interchangeable, which is what makes the load
    /// answer no identity for either position.
    /// </remarks>
    [Fact]
    public async Task APileNamedInTwoPositionsIsRefusedAlthoughItCarriesNoIdentity()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            var pile = await InsertWorldRowAsync(path, itemKind: 2, itemId: 3100, value: 40);
            await NameAsync(path, "character", Character, listType: 0, slot: 7, kind: "template",
                id: 3100, coreUid: pile, count: 40);
            await NameAsync(path, "character", Character, listType: 0, slot: 8, kind: "template",
                id: 3100, coreUid: pile, count: 40);

            _ = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await side.InUseCaseAsync(token => side.CharacterEntries
                    .LoadCharacterAsync(new AccountId(Account), new CharacterId(Character), token)));
        });
    }

    /// <summary>What the refusal must not reach: an inventory that names each thing once.</summary>
    /// <remarks>
    /// Two things in two positions, and the same two positions the refused cases use, so the only
    /// difference between this and the first case is which row each position names. Both loads are
    /// exercised because both grew the check.
    /// </remarks>
    [Fact]
    public async Task AnInventoryThatNamesEachThingOnceIsLoadedUnchanged()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await ExecuteAsync(path, $"""
                INSERT INTO account_cargo_state(account_id,selection_key,value32,item_count)
                VALUES({Account},1,2,3);
                """);
            var first = await InsertWorldRowAsync(path, itemKind: 1, itemId: 2001, value: 11);
            var second = await InsertWorldRowAsync(path, itemKind: 1, itemId: 2002, value: 22);
            await NameAsync(path, "character", Character, listType: 0, slot: 3, kind: "world",
                id: first, coreUid: first, count: 1);
            await NameAsync(path, "character", Character, listType: 0, slot: 4, kind: "world",
                id: second, coreUid: second, count: 1);
            var stored = await InsertWorldRowAsync(path, itemKind: 1, itemId: 2600, value: 33);
            await NameAsync(path, "account", Account, listType: 12, slot: 5, kind: "world",
                id: stored, coreUid: stored, count: 1);

            var carried = await side.InUseCaseAsync(token => side.CharacterEntries
                .LoadCharacterAsync(new AccountId(Account), new CharacterId(Character), token));
            var cargo = await side.InUseCaseAsync(token => side.AccountEntries
                .LoadAccountAsync(new AccountId(Account), new CharacterId(Character), token));

            Assert.Equal([(3, 2001, 11), (4, 2002, 22)], carried.CharacterItems
                .Select(item => (item.SlotIndex, item.Core.ItemId, item.Core.Value)));
            Assert.Equal([(5, 2600, 33)], cargo.AccountCargoItems
                .Select(item => (item.SlotIndex, item.Core.ItemId, item.Core.Value)));
        });
    }

    /// <summary>An archive that calls every template one thing, so rows carry names.</summary>
    private sealed class Declaring(ItemWorldClass declared) : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(
                new ItemPlacement(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 120))
                { WorldClass = declared });
    }

    private static async Task WithASeededCharacterAsync(Func<string, InventorySide, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var path = Path.Combine(Path.GetTempPath(), $"dfo-onething-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path);
            await ExecuteAsync(path, $"""
                INSERT INTO accounts(account_id,m_id) VALUES({Account},'pin');
                INSERT INTO characters(character_id,account_id,name,job,level)
                VALUES({Character},{Account},'pin',3,60);
                """);

            await body(path, side);
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

    /// <summary>Puts one thing into the world and answers the identity it was given.</summary>
    /// <remarks>
    /// The identity is read back rather than left to <c>last_insert_rowid()</c> because a position
    /// is written after it, and that would move on to the position's own row.
    /// </remarks>
    private static async Task<long> InsertWorldRowAsync(
        string path, byte itemKind, int itemId, int value)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO items(item_core) VALUES({Core(itemKind, itemId, value)});"
            + "SELECT last_insert_rowid();";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Task NameAsync(
        string path, string ownerScope, int ownerId, int listType, int slot,
        string kind, long id, long coreUid, int count) =>
        ExecuteAsync(path, $"""
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('{ownerScope}',{ownerId},{listType},{slot},'{kind}',{id},{coreUid},{count});
            """);

    private static string Core(byte itemKind, int itemId, int value)
    {
        var core = new byte[82];
        core[0] = itemKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), value);

        return "X'" + Convert.ToHexString(core) + "'";
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
