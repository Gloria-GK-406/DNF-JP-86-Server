using System.Buffers.Binary;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// The rules about avatars that live only in code, because none of them can be written as a check.
/// </summary>
/// <remarks>
/// Which rows carry avatar facts is decided by a byte inside the item core, and no constraint can
/// reach inside a BLOB. So these are conditions the load enforces and the database cannot: an item
/// of kind 8 belonging to a character must carry its facts and must carry a usable avatar UID,
/// while the same item in the account cargo carries neither and is fine.
///
/// They are written down here because the item side is being re-cut and these are the part of it a
/// green suite would not miss. A load that stopped looking, or looked in the wrong place, would
/// change what the client is sent for every avatar -- and nothing else on this path says so.
/// </remarks>
public sealed class AvatarCarrierRulesTests
{
    private const int Account = 41;
    private const int Character = 57;

    /// <remarks>
    /// The contrast that gives the two refusals below their meaning: an ordinary item has no avatar
    /// facts anywhere and is loaded without complaint.
    /// </remarks>
    [Fact]
    public async Task AnItemThatIsNotAnAvatarIsLoadedWithoutAvatarFacts()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertCharacterItemAsync(path, listType: 0, slot: 3, itemKind: 1, itemId: 2001, value: 7);

            var loaded = await side.InUseCaseAsync(token => side.CharacterEntries
                .LoadCharacterAsync(new AccountId(Account), new CharacterId(Character), token));

            var item = Assert.Single(loaded.CharacterItems);
            Assert.Equal(1, item.Core.ItemKind);
            Assert.Null(item.AvatarDetail);
        });
    }

    /// <remarks>
    /// The avatar UID is where the item's facts are found, so a character's avatar that has none is
    /// not an avatar the server can serve. It is refused before anything goes looking.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ACharactersAvatarWithoutAUsableUidIsRefused(int avatarUid)
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertCharacterItemAsync(
                path, listType: 1, slot: 4, itemKind: 8, itemId: 2500, value: avatarUid);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await side.InUseCaseAsync(token => side.CharacterEntries
                    .LoadCharacterAsync(new AccountId(Account), new CharacterId(Character), token)));
        });
    }

    /// <remarks>
    /// A character's avatar whose facts are missing is an error rather than an avatar with default
    /// facts -- the encoder refuses to write an avatar with none, so serving one would fail further
    /// away from the cause.
    /// </remarks>
    [Fact]
    public async Task ACharactersAvatarWithNoFactsIsRefusedRatherThanServedEmpty()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertCharacterItemAsync(
                path, listType: 1, slot: 4, itemKind: 8, itemId: 2500, value: 4242);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await side.InUseCaseAsync(token => side.CharacterEntries
                    .LoadCharacterAsync(new AccountId(Account), new CharacterId(Character), token)));
        });
    }

    /// <remarks>
    /// And the same item in the account cargo is carried with no facts at all. This is the one rule
    /// of the three that the whole-record characterization also states; it is repeated here so the
    /// three read together, because they are one rule over two columns.
    /// </remarks>
    [Fact]
    public async Task AnAvatarInTheCargoIsCarriedWithNoFactsAndIsNotRefused()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await ExecuteAsync(path, $"""
                INSERT INTO account_cargo_state(account_id,selection_key,value32,item_count)
                VALUES({Account},1,2,3);
                INSERT INTO items(cargo_character_id,item_core)
                VALUES({Character},{Core(8, 2700, 5151)});
                INSERT INTO item_slots(
                    owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('account',{Account},12,5,'world',last_insert_rowid(),
                    last_insert_rowid(),1);
                """);

            var loaded = await side.InUseCaseAsync(token => side.AccountEntries
                .LoadAccountAsync(new AccountId(Account), new CharacterId(Character), token));

            var avatar = Assert.Single(loaded.AccountCargoItems);
            Assert.Equal(8, avatar.Core.ItemKind);
            Assert.Equal(5151, avatar.Core.Value);
            Assert.Null(avatar.AvatarDetail);
        });
    }

    private static async Task WithASeededCharacterAsync(Func<string, InventorySide, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var path = Path.Combine(Path.GetTempPath(), $"dfo-avatar-{Guid.NewGuid():N}.db");
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

    private static Task InsertCharacterItemAsync(
        string path, int listType, int slot, byte itemKind, int itemId, int value) =>
        ExecuteAsync(path, $"""
            INSERT INTO items(item_core) VALUES({Core(itemKind, itemId, value)});
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',{Character},{listType},{slot},'world',last_insert_rowid(),
                last_insert_rowid(),1);
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
