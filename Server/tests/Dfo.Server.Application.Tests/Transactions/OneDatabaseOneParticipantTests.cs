using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Characters;
using Dfo.Server.Infrastructure.Inventory;
using Dfo.Server.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Transactions;

/// <summary>
/// One use case changing two different kinds of state that live in the same database.
/// </summary>
/// <remarks>
/// This is the case the whole carrier protocol exists for, and the one that decides its shape. A
/// transaction admits at most one participant whose commit can still fail. If each kind of state
/// on a database joined on its own, the second would be refused outright and no use case could
/// ever change items and anything else together. So the participant is the database, and each kind
/// of state contributes a facet of it.
///
/// The second kind of state here is a stand-in that exists only in this file. Items are the only
/// real one so far; proving the rule now is what keeps the protocol from being shaped around a
/// single store and having to be rebuilt when the next one arrives.
/// </remarks>
public sealed class OneDatabaseOneParticipantTests
{
    private static readonly ContainerId Main =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);

    [Fact]
    public async Task TwoKindsOfStateInOneDatabaseTakePartAsOneParticipant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new RecordingTransaction();

        _ = fixture.Items.SessionIn(transaction);
        _ = fixture.Marks.SessionIn(transaction);

        Assert.Single(transaction.Participants);
    }

    [Fact]
    public async Task TwoKindsOfStateInOneDatabaseBecomeVisibleTogether()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();

        fixture.Items.SessionIn(transaction).Write(Main, new StoredItem(66, Potion(3)));
        fixture.Marks.SessionIn(transaction).Mark(7);
        await transaction.CommitAsync(CancellationToken.None);

        Assert.Equal([(65, 5), (66, 3)], await fixture.ReadItemsAsync());
        Assert.Equal(7, await fixture.ReadMarkAsync());
    }

    /// <remarks>
    /// The failure is raised while the database is turning the staged writes into statements, which
    /// is the moment both kinds of state have work outstanding and neither is visible yet.
    /// </remarks>
    [Fact]
    public async Task AFailureInOneKindOfStateLeavesTheOtherOneUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();

        fixture.Items.SessionIn(transaction).Write(Main, new StoredItem(66, Potion(3)));
        var marks = fixture.Marks.SessionIn(transaction);
        marks.Mark(7);
        marks.FailWhileWriting();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transaction.CommitAsync(CancellationToken.None));
        Assert.Equal([(65, 5)], await fixture.ReadItemsAsync());
        Assert.Equal(0, await fixture.ReadMarkAsync());
    }

    /// <remarks>
    /// Two databases are still two participants, and both of them could fail while committing. That
    /// pairing is refused rather than accepted and hoped for, which is what keeps the rule above
    /// from quietly meaning "any number of durable stores are fine".
    /// </remarks>
    [Fact]
    public async Task TwoDatabasesStillCannotBothTakePart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();
        _ = fixture.Items.SessionIn(transaction);

        Assert.Throws<InvalidOperationException>(() => fixture.OtherItems.SessionIn(transaction));
    }

    private const int PotionId = 1000;

    private static InventoryItemCoreState Potion(int count) =>
        new(6, PotionId, count, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Counts what joined, so a test can prove one participant rather than two.</summary>
    private sealed class RecordingTransaction : IDomainTransaction
    {
        private readonly Dictionary<object, ITransactionParticipant> enlisted = [];

        public IReadOnlyCollection<ITransactionParticipant> Participants => this.enlisted.Values;

        public TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create)
            where TParticipant : ITransactionParticipant
        {
            if (this.enlisted.TryGetValue(identity, out var existing))
            {
                return (TParticipant)existing;
            }

            var participant = create();
            this.enlisted.Add(identity, participant);
            return participant;
        }
    }

    /// <summary>A second kind of state kept in the same database, for this file only.</summary>
    private sealed class MarkStore : IDomainStore<MarkSession>
    {
        private readonly string connectionString;

        public MarkStore(string databasePath) =>
            this.connectionString = new SqliteConnectionStringBuilder
                { DataSource = Path.GetFullPath(databasePath) }.ToString();

        public object StoreIdentity => this.connectionString;

        public bool IsDurable => true;

        public MarkSession SessionIn(IDomainTransaction transaction)
        {
            var database = transaction.Enlist(
                this.StoreIdentity,
                () => new SqliteDatabaseParticipant(this.connectionString));
            return database.Facet(typeof(MarkSession), owner => new MarkSession(owner));
        }
    }

    private sealed class MarkSession(SqliteDatabaseParticipant owner) : ISqliteStoreFacet
    {
        private int? staged;

        private bool refuses;

        public bool HasStagedWork => this.staged is not null;

        public void Mark(int value) => this.staged = value;

        public void FailWhileWriting() => this.refuses = true;

        public async ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            if (this.refuses)
            {
                throw new InvalidOperationException("This kind of state refused to be written.");
            }

            await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
            command.CommandText = "UPDATE characters SET level = @level WHERE character_id = 60;";
            command.Parameters.AddWithValue("@level", this.staged);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Discard() => this.staged = null;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path, string otherPath)
        {
            this.Path = path;
            this.OtherPath = otherPath;
            this.Items = new SqliteItemStore(path);
            this.OtherItems = new SqliteItemStore(otherPath);
            this.Marks = new MarkStore(path);
        }

        public string Path { get; }

        public string OtherPath { get; }

        public SqliteItemStore Items { get; }

        public SqliteItemStore OtherItems { get; }

        public MarkStore Marks { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(NewDatabase(), NewDatabase());
            await using var connection = Open(fixture.Path);
            await using (var character = connection.CreateCommand())
            {
                character.CommandText =
                    "INSERT INTO characters(character_id,account_id,name,level) "
                    + "VALUES(60,6,'both',0)";
                await character.ExecuteNonQueryAsync();
            }

            await using var item = connection.CreateCommand();
            item.CommandText =
                "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,"
                + "slot_index,item_core) VALUES('character',60,60,0,65,@core)";
            item.Parameters.AddWithValue(
                "@core", SqliteInventoryEntryRepository.EncodeItemCore(Potion(5)));
            await item.ExecuteNonQueryAsync();
            return fixture;
        }

        public async Task<List<(int Slot, int Count)>> ReadItemsAsync()
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT slot_index,item_core FROM character_new_items "
                + "WHERE character_id=60 AND list_type=0 ORDER BY slot_index";
            var rows = new List<(int, int)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var core = SqliteInventoryEntryRepository.DecodeItemCore((byte[])reader.GetValue(1));
                rows.Add((reader.GetInt32(0), core.Value));
            }

            return rows;
        }

        public async Task<int> ReadMarkAsync()
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT level FROM characters WHERE character_id=60";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static string NewDatabase()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-onedb-{Guid.NewGuid():N}.db");
            // The repositories own the schema; the stores only write into it.
            _ = new SqliteInventoryEntryRepository(path);
            _ = new SqliteCharacterRepository(path);
            return path;
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
            foreach (var path in new[] { this.Path, this.OtherPath })
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
