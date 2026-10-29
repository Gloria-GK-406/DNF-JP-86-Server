using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

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
            Assert.Equal(1, item.Snapshot.ToCore().ItemKind);
            Assert.Null(item.Snapshot.AvatarDetail);
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
                INSERT INTO account_cargo_state(account_id,selection_key,value32)
                VALUES({Account},1,2);
                INSERT INTO items(item_core)
                VALUES({Core(8, 2700, 5151)});
                INSERT INTO item_slots(
                    owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('account',{Account},12,5,'world',last_insert_rowid(),
                    last_insert_rowid(),1);
                """);

            var loaded = await side.InUseCaseAsync(token => side.AccountEntries
                .LoadAccountAsync(new AccountId(Account), new CharacterId(Character), token));

            var avatar = Assert.Single(loaded.AccountCargoItems);
            Assert.Equal(8, avatar.Snapshot.ToCore().ItemKind);
            Assert.Equal(5151, avatar.Snapshot.ToCore().Value);
            Assert.Null(avatar.Snapshot.AvatarDetail);
        });
    }

    /// <summary>
    /// The third of the rules: an avatar's facts have to be facts the wire can carry.
    /// </summary>
    /// <remarks>
    /// The ones above ask whether the facts are there. These ask whether they can be sent: the
    /// socket is a fixed thirty-byte block and each colour goes out as an unsigned sixteen-bit
    /// value, so a row disagreeing with either cannot be written at all.
    /// <para>
    /// These samples are real persisted rows. Only the deliberately damaged socket fixture disables
    /// SQLite CHECK constraints on its seed connection; normal reads still apply lifecycle validation.
    /// <para>
    /// The exception type is asserted rather than merely that something was thrown. Take the range
    /// test away and a colour past the ceiling reaches the <c>checked</c> conversion just below it
    /// and throws <see cref="OverflowException"/> instead -- still an exception, no longer this
    /// rule, and an assertion that accepted any exception would not notice.
    /// </para>
    /// </remarks>
    [Fact]
    public Task AnAvatarWhoseJewelSocketIsNotThirtyBytesIsRefused() =>
        TheLoadRefusesAsync(UsableFacts() with { JewelSocket = new byte[29] });

    [Theory]
    [InlineData(-1L)]
    [InlineData(65536L)]
    [InlineData(long.MaxValue)]
    public Task AnAvatarWhoseFirstColourIsOutsideTheWireRangeIsRefused(long color) =>
        TheLoadRefusesAsync(UsableFacts() with { Color1 = color });

    [Theory]
    [InlineData(-1L)]
    [InlineData(65536L)]
    [InlineData(long.MaxValue)]
    public Task AnAvatarWhoseSecondColourIsOutsideTheWireRangeIsRefused(long color) =>
        TheLoadRefusesAsync(UsableFacts() with { Color2 = color });

    /// <summary>The row each of the three malformed ones is exactly one change away from.</summary>
    /// <remarks>
    /// Without this the three would also pass if the stand-in row were unusable for some reason
    /// none of them names. The first colour sits on the ceiling rather than below it, which is the
    /// value a range test written with the wrong comparison would reject.
    /// </remarks>
    [Fact]
    public async Task AnAvatarWhoseFactsAreAllInRangeIsCarried()
    {
        var carried = await TheLoadAnswersAsync(UsableFacts());

        var avatar = Assert.Single(carried.CharacterItems);
        Assert.NotNull(avatar.Snapshot.AvatarDetail);
        Assert.Equal(30, avatar.Snapshot.AvatarDetail.JewelSocket.Length);
        Assert.Equal(ushort.MaxValue, (int)avatar.Snapshot.AvatarDetail.Color1);
        Assert.Equal(0, (int)avatar.Snapshot.AvatarDetail.Color2);
    }

    private static AvatarDetailRow UsableFacts() =>
        new(7, 8, new byte[30], ushort.MaxValue, 0, 9);

    private static async Task TheLoadRefusesAsync(AvatarDetailRow handed)
    {
        var refused = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await TheLoadAnswersAsync(handed));

        Assert.NotEmpty(refused.Message);
    }

    private static async Task<CharacterInventoryEntryStorageRecord> TheLoadAnswersAsync(
        AvatarDetailRow handed)
    {
        return await TestGameplay.RunAsync(async () =>
        {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-avatar-facts-{Guid.NewGuid():N}.db");
        try
        {
            var scope = new DomainTransactionScope();
            var database = new SqliteDatabase(path, scope);
            await ExecuteAsync(path, $"""
                INSERT INTO accounts(account_id,m_id) VALUES({Account},'pin');
                INSERT INTO characters(character_id,account_id,name,job,level)
                VALUES({Character},{Account},'pin',3,60);
                """);

            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    PRAGMA ignore_check_constraints=ON;
                    INSERT INTO items(item_core,avatar_expire_date,avatar_clear_id,avatar_jewel_socket,
                        avatar_color1,avatar_color2,avatar_delete_date)
                    VALUES({Core(8, 2500, 4242)},@expire,@clear,@socket,@color1,@color2,@delete);
                    INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                    VALUES('character',{Character},1,4,'world',last_insert_rowid(),last_insert_rowid(),1);
                    PRAGMA ignore_check_constraints=OFF;
                    """;
                command.Parameters.AddWithValue("@expire", handed.ExpireDate);
                command.Parameters.AddWithValue("@clear", handed.ClearAvatarId);
                command.Parameters.AddWithValue("@socket", handed.JewelSocket);
                command.Parameters.AddWithValue("@color1", handed.Color1);
                command.Parameters.AddWithValue("@color2", handed.Color2);
                command.Parameters.AddWithValue("@delete", handed.DeleteDate);
                await command.ExecuteNonQueryAsync();
            }
            var side = new InventorySide(path, new EveryTemplateAnAvatar());
            var entries = side.CharacterEntries;
            await using var lease = scope.Begin();
            var loaded = await entries.LoadCharacterAsync(
                new AccountId(Account), new CharacterId(Character), CancellationToken.None);
            await lease.CommitAsync(CancellationToken.None);
            return loaded;
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
        });
    }

    private sealed class EveryTemplateAnAvatar : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(
                new ItemPlacement(8, 1, new(BusinessInventorySpace.Avatar, 0, 120))
                { WorldClass = ItemWorldClass.Avatar });
    }

    private sealed class NoWornSlots : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BusinessSlot?>(null);
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
