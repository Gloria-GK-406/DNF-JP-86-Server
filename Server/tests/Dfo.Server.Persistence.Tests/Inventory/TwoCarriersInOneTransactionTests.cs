using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// One use case changing items in two places at once, one of which outlives the process.
/// </summary>
/// <remarks>
/// The transaction's own tests prove all-or-nothing with stand-in participants, and each store has
/// its own rollback test. Neither of those puts a real database and a real in-memory store in one
/// transaction and then fails it -- which is the case the whole design exists for, and the one a
/// player would meet: an item taken out of the character's inventory and put into what a dungeon
/// handed them, with the run ending badly in between.
/// </remarks>
public sealed class TwoCarriersInOneTransactionTests
{
    private static readonly ContainerId Kept =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);

    private static readonly ContainerId Run = new("dungeon");

    [Fact]
    public async Task AUseCaseThatEndsWellLeavesBothChangesVisible()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.MoveOneAcrossAsync(settle: true);

        Assert.Empty(await fixture.ReadDatabaseAsync());
        Assert.Equal([(0, PotionId, 1)], await fixture.ReadMemoryAsync());
    }

    /// <remarks>
    /// The durable side is the one that could be left half-applied, so it is the one asserted
    /// first: the row is still there, untouched, and the memory side never received the item.
    /// </remarks>
    [Fact]
    public async Task AUseCaseThatFailsLeavesNeitherChangeVisible()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.MoveOneAcrossAsync(settle: false);

        Assert.Equal([(65, PotionId, 1)], await fixture.ReadDatabaseAsync());
        Assert.Empty(await fixture.ReadMemoryAsync());
    }

    /// <remarks>
    /// Two stores that both keep things cannot be settled together without a protocol this server
    /// does not have, so the second one is refused rather than accepted and hoped for.
    /// </remarks>
    [Fact]
    public async Task ASecondStoreThatKeepsThingsIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();
        _ = fixture.Durable.SessionIn(transaction);

        Assert.Throws<InvalidOperationException>(
            () => fixture.SecondDurable.SessionIn(transaction));
    }

    private const int PotionId = 8474;

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path, string secondPath)
        {
            this.Path = path;
            this.Durable = new SqliteItemDomainStore(path);
            this.SecondDurable = new SqliteItemDomainStore(secondPath);
            this.SecondPath = secondPath;
        }

        public string Path { get; }

        public string SecondPath { get; }

        public SqliteItemDomainStore Durable { get; }

        public SqliteItemDomainStore SecondDurable { get; }

        public VolatileItemDomainStore Volatile { get; } = new();

        public static async Task<Fixture> CreateAsync()
        {
            var path = NewDatabase();
            var fixture = new Fixture(path, NewDatabase());
            await using var connection = Open(path);
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'both')";
            await character.ExecuteNonQueryAsync();
            await using var item = connection.CreateCommand();
            item.CommandText =
                "INSERT INTO items(owner_scope,owner_id,list_type,"
                + "slot_index,item_core) VALUES('character',60,0,65,@core)";
            item.Parameters.AddWithValue("@core", Core(1));
            await item.ExecuteNonQueryAsync();
            return fixture;
        }

        /// <summary>
        /// Takes the item out of what is kept and puts it into what is not, settling or not.
        /// </summary>
        public async Task MoveOneAcrossAsync(bool settle)
        {
            var transaction = new DomainTransaction();
            var kept = this.Durable.SessionIn(transaction);
            var run = this.Volatile.SessionIn(transaction);

            var moving = await kept.ReadAsync(Kept, 65, CancellationToken.None);
            kept.Clear(Kept, 65);
            run.Write(Run, new StoredItem(0, moving!.Core));

            if (settle)
            {
                await transaction.CommitAsync(CancellationToken.None);
            }
            else
            {
                await transaction.RollbackAsync();
            }
        }

        public async Task<List<(int Slot, int ItemId, int Count)>> ReadDatabaseAsync()
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT slot_index,item_core FROM items "
                + "WHERE owner_scope='character' AND owner_id=60 AND list_type=0 "
                + "ORDER BY slot_index";
            var rows = new List<(int, int, int)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var core = (byte[])reader.GetValue(1);
                rows.Add((
                    reader.GetInt32(0),
                    BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4))));
            }

            return rows;
        }

        public async Task<List<(int Slot, int ItemId, int Count)>> ReadMemoryAsync()
        {
            var items = await this.Volatile.SessionIn(new DomainTransaction())
                .ReadAllAsync(Run, CancellationToken.None);
            return [.. items.Select(item => (item.SlotIndex, item.Core.ItemId, item.Core.Value))];
        }

        private static string NewDatabase()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-both-{Guid.NewGuid():N}.db");
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            return path;
        }

        private static byte[] Core(int count)
        {
            var core = new byte[82];
            core[0] = 2;
            core[66] = byte.MaxValue;
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), PotionId);
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), count);
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(21, 4), -1);
            return core;
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { this.Path, this.SecondPath })
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // A file the runtime still holds is left for the temp directory to reap.
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
