using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

/// <summary>
/// Whether an identity this server can hand a creature is one the creature list frame can say.
/// </summary>
/// <remarks>
/// The frame writes a creature's identity as a signed 32-bit integer, and there is one identity
/// space for a creature -- the world identity itself -- rather than a second, wider one kept
/// beside it. That makes the width a property of what the database is willing to allocate, so both
/// ends of it are checked here against a real database rather than against a constructed value:
/// the largest identity it will give out goes through the list and out onto the wire, and the next
/// one after that is refused at the point of allocation.
///
/// The count is the other bound the frame carries. It is a single byte, so a character with more
/// creatures than a byte can count is a frame that cannot be written, and the refusal has to
/// arrive as a refusal rather than as a truncated list.
/// </remarks>
public sealed class CreatureIdentityFitsTheWireTests
{
    private const int Character = 71;

    private const byte CreatureKind = 5;

    /// <summary>The largest identity the wire can say, which is the largest this may allocate.</summary>
    private const int LargestSayable = int.MaxValue;

    /// <summary>
    /// The largest identity the database will allocate reaches the frame and is written whole.
    /// </summary>
    /// <remarks>
    /// Allocated rather than chosen: the sequence is wound forward and the row is inserted through
    /// the lifecycle store, so the number under test is the one the column handed out. It is then read
    /// back through the creature list and projected exactly as entry projects it, so nothing
    /// between the allocation and the four bytes on the wire is stubbed.
    /// </remarks>
    [Fact]
    public async Task TheLargestIdentityItWillAllocateIsWrittenIntoTheCreatureList()
    {
        await WithADatabaseAsync(async (path, database, scope) =>
        {
            await WindTheSequenceToAsync(path, LargestSayable - 1);
            var store = new SqliteItemDomainStore(path, TestRequired.NoContentsEvents);
            var identity = await InUseCaseAsync(scope, async token =>
            {
                var session = store.SessionIn(scope.Current!);
                var record = await session.CreateAsync(ItemSnapshot.FromCore(Core(), false,
                    creature: new InventoryCreatureDetailState(1, 1, 4321, 7, 9, 11, [1, 2, 3], 13)), token);
                var placed = await session.PutAsync(ContainerId.ForCharacter(new CharacterId(Character),
                    InventoryContainerType.Equipment), 24, record, token);
                return placed.ItemUid!.Value.Value;
            });

            Assert.Equal(LargestSayable, identity);

            var stored = await new SqliteCharacterCreatureTable(database)
                .ReadAsync(new CharacterId(Character), CancellationToken.None);
            Assert.Equal(LargestSayable, Assert.Single(stored).CreatureKey);

            var pets = Pets(stored);
            var body = new CreatureListMessage(pets).EncodeBody();

            Assert.Equal(1, body[0]);
            Assert.Equal(LargestSayable, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1, 4)));
        });
    }

    /// <summary>
    /// The database refuses to allocate an identity the frame could not say, and says so.
    /// </summary>
    /// <remarks>
    /// This is the half of the width the frame cannot check for itself. A store that handed out
    /// one number more would leave the conversion in the encoder as the thing that failed, a long
    /// way from the write that caused it and with nothing to say about what had happened.
    /// </remarks>
    [Fact]
    public async Task AnIdentityBeyondWhatTheWireCanSayIsRefusedAtAllocation()
    {
        await WithADatabaseAsync(async (path, database, scope) =>
        {
            await WindTheSequenceToAsync(path, LargestSayable);
            var store = new SqliteItemDomainStore(path, TestRequired.NoContentsEvents);
            var refusal = await Assert.ThrowsAsync<SqliteException>(
                () => InUseCaseAsync(scope, token => store.SessionIn(scope.Current!).CreateAsync(
                    ItemSnapshot.FromCore(Core(), false,
                        creature: new InventoryCreatureDetailState(1, 0, 0, 0, 0, 0, [], 0)), token)));
            Assert.Equal(19, refusal.SqliteErrorCode);
            await using var connection = new SqliteConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM items";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        });
    }

    /// <summary>
    /// The list holds as many creatures as a byte can count, and refuses one more.
    /// </summary>
    /// <remarks>
    /// The count is written as a single byte, so the bound is part of the frame rather than a
    /// policy about creatures. Both sides are pinned: two hundred and fifty-five encode with the
    /// count byte saying so, and two hundred and fifty-six is refused rather than wrapping round
    /// to zero or writing a list the count does not match.
    /// </remarks>
    [Fact]
    public void TheListCountsCreaturesInOneByteAndRefusesWhatItCannotCount()
    {
        var full = Enumerable.Range(1, 255)
            .Select(index => new CharacterPetRuntimeState(index, 0, 0, 0, 0, 0, 0, [], 0))
            .ToArray();

        var body = new CreatureListMessage(full).EncodeBody();
        Assert.Equal(255, body[0]);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1, 4)));

        var overfull = full.Append(new CharacterPetRuntimeState(256, 0, 0, 0, 0, 0, 0, [], 0))
            .ToArray();

        var refusal = Assert.Throws<InvalidDataException>(
            () => new CreatureListMessage(overfull).EncodeBody());
        Assert.Equal("creature count cannot be represented as u8.", refusal.Message);
    }

    /// <summary>The creature list entry projection, the one entry itself uses.</summary>
    private static IReadOnlyList<CharacterPetRuntimeState> Pets(
        IReadOnlyList<CharacterPetStorageRow> stored) =>
        new CharacterFeatureEntryProjection().Build(
            new CharacterFeatureStorageRecord(
                stored,
                [],
                [],
                [],
                (0, 0, 0, 0, 0, []),
                new CharacterMailboxStorageRecord(0, []),
                [])).Pets;

    private static InventoryItemCoreState Core()
    {
        var core = new byte[82];
        core[0] = CreatureKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), 63001);
        return InventoryItemCore.Decode(core);
    }

    /// <summary>
    /// Winds the identity column's high-water mark forward, so the next row allocated is the one
    /// under test rather than the two-billionth row a test would otherwise have to insert.
    /// </summary>
    /// <remarks>
    /// The sequence row exists only once the table has allocated something, so one row is written
    /// and taken away again first. That row's identity is spent, which is the point of an
    /// <c>AUTOINCREMENT</c> column and is why the mark can be moved but not what it means.
    /// </remarks>
    private static async Task WindTheSequenceToAsync(string path, int mark)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO items(item_core) VALUES(@core);
            DELETE FROM items WHERE item_uid = last_insert_rowid();
            UPDATE sqlite_sequence SET seq=@mark WHERE name='items';
            """;
        command.Parameters.AddWithValue("@core", new byte[82]);
        command.Parameters.AddWithValue("@mark", mark);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs one call inside a use case and settles it, the way one command would.</summary>
    /// <remarks>
    /// A write to this database is refused outside one, which is the boundary rather than an
    /// inconvenience: the row an allocation lands is a change somebody has to be able to take back.
    /// </remarks>
    private static async Task<TResult> InUseCaseAsync<TResult>(
        DomainTransactionScope scope, Func<CancellationToken, ValueTask<TResult>> work)
    {
        return await TestGameplay.RunAsync(async () =>
        {
            await using var lease = scope.Begin();
            var result = await work(CancellationToken.None);
            await lease.CommitAsync(CancellationToken.None);
            return result;
        });
    }

    private static async Task WithADatabaseAsync(
        Func<string, SqliteDatabase, DomainTransactionScope, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var path = Path.Combine(Path.GetTempPath(), $"dfo-creaturewire-{Guid.NewGuid():N}.db");
        try
        {
            var scope = TestRequired.TransactionScope();
            var database = new SqliteDatabase(path, scope);
            await body(path, database, scope);
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
