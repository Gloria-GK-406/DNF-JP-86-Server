using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// A title book is a container of <c>items</c> like any other, and these are the four things that
/// makes true.
/// </summary>
/// <remarks>
/// The title book used to be a table of its own, keyed by a category of 0 to 4. Container types are
/// a different numbering in which 0 is the main inventory, so carrying the category across as it
/// stood would give a title-book row the same key as a main-inventory row. The five title-book
/// container types already existed -- 19 through 23 -- so a category is stored as nineteen plus
/// itself and read back as itself less nineteen.
///
/// Every one of these would stay green if the title book simply stopped existing, so each is
/// written to fail for one specific reason: the wrong number stored, the wrong number read back, a
/// key that collides, a read that returns rows it cannot parse, and a save that sweeps away rows it
/// was never asked about.
/// </remarks>
public sealed class TitleBookCarrierTests
{
    private static readonly AccountId Account = new(1);
    private static readonly CharacterId Character = new(1);

    /// <summary>The container type each of the five categories is stored as.</summary>
    [Theory]
    [InlineData(0, 19)]
    [InlineData(1, 20)]
    [InlineData(2, 21)]
    [InlineData(3, 22)]
    [InlineData(4, 23)]
    public async Task ACategoryIsStoredAsItsOwnContainerTypeAndReadBackAsTheCategory(
        int category, int containerType)
    {
        await WithACharacterAsync(async (path, _) =>
        {
            await InsertTitleBookAsync(path, category, slot: 1, itemId: 700 + category);

            Assert.Equal(
                containerType,
                await ScalarAsync(path, "SELECT list_type FROM item_slots WHERE owner_id=1"));

            var loaded = await LoadCharacterAsync(path);

            var book = Assert.Single(loaded.Features.TitleBooks);
            Assert.Equal((byte)category, book.Category);
            Assert.Equal(1, book.SlotIndex);
            Assert.Equal(700 + category, BinaryPrimitives.ReadInt32LittleEndian(book.ItemCore.AsSpan(1)));
        });
    }

    /// <remarks>
    /// The case the transform exists for. Both rows are slot zero of container zero as their own
    /// tables numbered them, and one table cannot hold two of those.
    /// </remarks>
    [Fact]
    public async Task ACharacterMayHoldBothTheFirstMainSlotAndTheFirstTitleBookSlot()
    {
        await WithACharacterAsync(async (path, side) =>
        {
            await InsertMainItemAsync(path, slot: 0, itemId: 2001);
            await InsertTitleBookAsync(path, category: 0, slot: 0, itemId: 700);

            Assert.Equal(
                2L,
                await ScalarAsync(path, "SELECT COUNT(*) FROM item_slots WHERE owner_id=1"));

            var inventory = await side.InUseCaseAsync(token => side.CharacterEntries
                .LoadCharacterAsync(Account, Character, token));
            var loaded = await LoadCharacterAsync(path);

            var item = Assert.Single(inventory.CharacterItems);
            Assert.Equal(InventoryContainerType.Main, item.ContainerType);
            Assert.Equal(2001, item.Core.ItemId);
            var book = Assert.Single(loaded.Features.TitleBooks);
            Assert.Equal((byte)0, book.Category);
        });
    }

    /// <remarks>
    /// The inventory load parses a container type that admits only 0, 1, 2, 3 and 12, so a
    /// title-book row reaching it is refused rather than ignored. Left in the read, a character with
    /// a title book could not enter at all.
    /// </remarks>
    [Fact]
    public async Task TheInventoryReadLeavesTheTitleBookOutRatherThanChokingOnIt()
    {
        await WithACharacterAsync(async (path, side) =>
        {
            await InsertTitleBookAsync(path, category: 2, slot: 3, itemId: 702);

            var inventory = await side.InUseCaseAsync(token => side.CharacterEntries
                .LoadCharacterAsync(Account, Character, token));

            Assert.Empty(inventory.CharacterItems);
        });
    }

    /// <summary>
    /// Saving what a character is carrying does not touch what is in its title book.
    /// </summary>
    /// <remarks>
    /// The save is authoritative for the containers it was given and sweeps away every other row the
    /// character owns. Those rows used to be in a different table; now they are rows of the same
    /// one, and a sweep that does not say so takes the title book with it -- which shows up as five
    /// empty categories on the next entry and nowhere before that.
    /// </remarks>
    [Fact]
    public async Task SavingTheInventoryLeavesTheTitleBookWhereItIs()
    {
        await WithACharacterAsync(async (path, side) =>
        {
            await InsertMainItemAsync(path, slot: 0, itemId: 2001);
            await InsertTitleBookAsync(path, category: 0, slot: 0, itemId: 700);
            await InsertTitleBookAsync(path, category: 4, slot: 2, itemId: 704);
            var loaded = await side.InUseCaseAsync(token => side.CharacterEntries
                .LoadCharacterAsync(Account, Character, token));
            var held = Assert.Single(loaded.CharacterItems);

            await side.PerCall.SaveAsync(
                new EntryInventoryLeaseSnapshot(
                    SelectedSessionIdentity.Create(
                        Account,
                        Character,
                        new NetworkConnectionId("Game:titlebook"),
                        1,
                        10011),
                    new CharacterItemLeaseState(
                        Character,
                        [
                            new InventoryContainerState(
                                InventoryContainerType.Main,
                                0,
                                [new InventoryItemState(held.Identity, 4, held.Core)]),
                        ]),
                    1,
                    true),
                CancellationToken.None);

            Assert.Equal(2, (await LoadCharacterAsync(path)).Features.TitleBooks.Count);
            Assert.Equal(4L, await ScalarAsync(
                path, "SELECT slot_index FROM item_slots WHERE owner_id=1 AND list_type=0"));
        });
    }

    /// <remarks>
    /// The character is made rather than written, because entering one reads six tables that a
    /// hand-written row would leave empty. Making it is also the only way its ids are the ones the
    /// server would have given it.
    /// </remarks>
    private static async Task WithACharacterAsync(Func<string, InventorySide, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-titlebook-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "characters.db");
        try
        {
            var created = await new AccountSide(path).GetOrCreateAsync("10038", "hash");
            Assert.Equal(Account, created.AccountId);
            var characterId = await new CharacterSide(path).PerCall.CreateAsync(
                new NewCharacterSpec(
                    Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);
            Assert.Equal(Character, characterId);

            await body(path, new InventorySide(path));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }

    private static Task<CharacterEntryStorageRecord> LoadCharacterAsync(string path) =>
        new CharacterSide(path).EntryState(new GoldLimitRuleService())
            .LoadAsync(Account, Character, CancellationToken.None)
            .AsTask();

    private static Task InsertTitleBookAsync(string path, int category, int slot, int itemId) =>
        ExecuteAsync(path, $"""
            INSERT INTO items(item_core) VALUES({Core(itemId)});
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',{Character.Value},{19 + category},{slot},'world',
                last_insert_rowid(),last_insert_rowid(),1);
            """);

    private static Task InsertMainItemAsync(string path, int slot, int itemId) =>
        ExecuteAsync(path, $"""
            INSERT INTO items(item_core) VALUES({Core(itemId)});
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',{Character.Value},0,{slot},'world',
                last_insert_rowid(),last_insert_rowid(),1);
            """);

    private static string Core(int itemId)
    {
        var core = new byte[82];
        core[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), itemId);
        core[66] = byte.MaxValue;

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

    private static async Task<long> ScalarAsync(string path, string statement)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = statement;

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
