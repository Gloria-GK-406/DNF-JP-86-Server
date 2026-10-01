using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Creatures;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Creatures;

public sealed class CreatureDomainStoreTests
{
    private static readonly CharacterId Owner = new(7);

    [Fact]
    public async Task CreatureStateFollowsItemIdentityAndKeepsUnknownFields()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var store = new SqliteCreatureDomainStore(fixture.Path);

        var transaction = new StubTransaction();
        var session = store.SessionIn(transaction);
        var original = Assert.IsType<CreatureState>(await session.ReadCurrentAsync(Owner, default));
        Assert.Equal(101, original.ItemUid);
        Assert.Equal(63001, original.ItemTemplateId);
        Assert.Equal(73, original.Stomach);
        Assert.Equal(73d, original.ExactStomach);
        Assert.Equal(80d, (original with { ExactStomach = 73.5, Stomach = 80 }).ExactStomach);
        Assert.Equal(301, original.Experience);
        Assert.Equal(4, original.Level);
        Assert.Equal([0x81, 0x40], original.NameBytes);
        Assert.Equal((2, 8, 9, 11),
            (original.ModeFlag, original.Mode1Field0A, original.Mode1Field0B, original.TailFlag));
        session.Write(Owner, original with
        {
            Stomach = 80, Experience = 900, Level = 5, NameBytes = [0x82, 0x41]
        });
        Assert.Equal(900, (await session.ReadAsync(Owner, 101, default))!.Experience);
        await transaction.SettleAsync();

        await fixture.ExecuteAsync("UPDATE item_slots SET slot_index=14 WHERE core_uid=101");
        var again = new StubTransaction();
        var after = Assert.Single(await store.SessionIn(again).ReadAllAsync(Owner, default));
        Assert.Equal((101, 63001, 80, 900, 5),
            (after.ItemUid, after.ItemTemplateId, after.Stomach, after.Experience, after.Level));
        Assert.Equal([0x82, 0x41], after.NameBytes);
        Assert.Equal((2, 8, 9, 11),
            (after.ModeFlag, after.Mode1Field0A, after.Mode1Field0B, after.TailFlag));
        Assert.Null(await store.SessionIn(again).ReadCurrentAsync(Owner, default));
        await again.SettleAsync();
    }

    [Fact]
    public async Task CreatureAndConsumedItemCommitTogetherOrRollbackTogether()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var creatures = new SqliteCreatureDomainStore(fixture.Path);
        var items = new SqliteItemDomainStore(fixture.Path);

        var failing = new StubTransaction();
        items.SessionIn(failing).Clear(ContainerId.ForCharacter(Owner, InventoryContainerType.Main), 65);
        creatures.SessionIn(failing).Write(Owner,
            new CreatureState(101, 99999, 85, 400, 4, [0x81], 2, 8, 9, 11));
        await Assert.ThrowsAsync<InvalidDataException>(() => failing.SettleAsync());
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM item_slots WHERE slot_index=65"));
        Assert.Equal(73L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));

        var successful = new StubTransaction();
        items.SessionIn(successful).Clear(ContainerId.ForCharacter(Owner, InventoryContainerType.Main), 65);
        var old = (await creatures.SessionIn(successful).ReadAsync(Owner, 101, default))!;
        creatures.SessionIn(successful).Write(Owner, old with { Stomach = 85 });
        await successful.SettleAsync();
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM item_slots WHERE slot_index=65"));
        Assert.Equal(85L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
    }

    [Fact]
    public async Task AnotherCharacterCannotUpdateThePetAndOldConditionViewSharesTheFacet()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var creatures = new SqliteCreatureDomainStore(fixture.Path);
        var owner = new StubTransaction();
        var state = (await creatures.SessionIn(owner).ReadAsync(Owner, 101, default))!;
        await owner.SettleAsync();

        var foreign = new StubTransaction();
        Assert.Null(await creatures.SessionIn(foreign).ReadAsync(new CharacterId(8), 101, default));
        creatures.SessionIn(foreign).Write(new CharacterId(8), state with { Stomach = 1 });
        await Assert.ThrowsAsync<InvalidDataException>(() => foreign.SettleAsync());
        Assert.Equal(73L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));

        var compatible = new StubTransaction();
        var old = new Dfo.Server.Persistence.Characters.SqliteCharacterDomainStore(fixture.Path)
            .SessionIn(compatible).Creatures;
        old.Write(new(new(3), Owner), new(101, 1));
        Assert.Equal(1, (await creatures.SessionIn(compatible).ReadAsync(Owner, 101, default))!.Stomach);
        await compatible.SettleAsync();
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
    }

    [Fact]
    public async Task CreatureDetailWithNonCreatureCoreIsRejectedOnRead()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.ExecuteAsync("UPDATE items SET item_core=zeroblob(82) WHERE item_uid=101");

        var transaction = new StubTransaction();
        var session = new SqliteCreatureDomainStore(fixture.Path).SessionIn(transaction);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await session.ReadCurrentAsync(Owner, default));
        await transaction.SettleAsync();
    }

    [Fact]
    public async Task StagedMoveIsVisibleToCurrentAndAllCreatureReads()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var transaction = new StubTransaction();
        var itemSession = new SqliteItemDomainStore(fixture.Path).SessionIn(transaction);
        var creatureSession = new SqliteCreatureDomainStore(fixture.Path).SessionIn(transaction);
        var equipment = ContainerId.ForCharacter(Owner, InventoryContainerType.Equipment);
        var petBag = ContainerId.ForCharacter(Owner, InventoryContainerType.Pet);
        itemSession.Clear(equipment, 25);
        itemSession.Write(petBag, new StoredItem(0, InventoryItemCore.Decode(Fixture.Core(5, 63001, 101)))
        {
            WorldClass = ItemWorldClass.Creature,
            TransferFrom = new ItemTransferSource(equipment, 25)
        });

        Assert.Null(await creatureSession.ReadCurrentAsync(Owner, default));
        Assert.Equal(101, Assert.Single(await creatureSession.ReadAllAsync(Owner, default)).ItemUid);

        itemSession.Clear(petBag, 0);
        itemSession.Write(equipment, new StoredItem(25, InventoryItemCore.Decode(Fixture.Core(5, 63001, 101)))
        {
            WorldClass = ItemWorldClass.Creature,
            TransferFrom = new ItemTransferSource(petBag, 0)
        });
        Assert.Equal(101, (await creatureSession.ReadCurrentAsync(Owner, default))!.ItemUid);
        await transaction.SettleAsync();
    }

    [Fact]
    public async Task ChangingCreatureTemplateKeepsIdentityAndAllDetails()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var transaction = new StubTransaction();
        var itemSession = new SqliteItemDomainStore(fixture.Path).SessionIn(transaction);
        itemSession.Write(ContainerId.ForCharacter(Owner, InventoryContainerType.Equipment),
            new StoredItem(25, InventoryItemCore.Decode(Fixture.Core(5, 63002, 101)))
            {
                WorldClass = ItemWorldClass.Creature
            });
        var creatureSession = new SqliteCreatureDomainStore(fixture.Path).SessionIn(transaction);
        var updated = (await creatureSession.ReadCurrentAsync(Owner, default))!;
        Assert.Equal(101, updated.ItemUid);
        Assert.Equal(63002, updated.ItemTemplateId);
        Assert.Equal((73, 301, 4), (updated.Stomach, updated.Experience, updated.Level));
        Assert.Equal([0x81, 0x40], updated.NameBytes);
        await transaction.SettleAsync();
        Assert.Equal(101L, await fixture.ScalarAsync(
            "SELECT core_uid FROM item_slots WHERE owner_id=7 AND list_type=3 AND slot_index=25"));
        Assert.Equal(73L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
    }

    [Fact]
    public async Task ReviveIfEmptyRejectsASecondRevival()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.ExecuteAsync("UPDATE items SET creature_field04=0 WHERE item_uid=101");
        var store = new SqliteCreatureDomainStore(fixture.Path);
        var first = new StubTransaction();
        var session = store.SessionIn(first);
        session.ReviveIfEmpty(Owner, 101);
        Assert.Equal(1, (await session.ReadCurrentAsync(Owner, default))!.Stomach);
        await first.SettleAsync();

        var second = new StubTransaction();
        store.SessionIn(second).ReviveIfEmpty(Owner, 101);
        await Assert.ThrowsAsync<InvalidDataException>(() => second.SettleAsync());
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"dfo-creature-domain-{Guid.NewGuid():N}.db");

        public Fixture() => CharacterDatabaseSchema.Ensure(
            new SqliteConnectionStringBuilder { DataSource = this.Path }.ToString());

        public async Task SeedAsync()
        {
            await this.ExecuteAsync("""
                INSERT INTO accounts(account_id,m_id) VALUES(3,'pet-test');
                INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES(7,3,'owner',3,60),(8,3,'other',3,60);
                """);
            await using var connection = new SqliteConnection($"Data Source={this.Path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO items(item_uid,item_core,creature_field04,creature_mode_flag,
                    creature_progress_value,creature_mode1_field0a,creature_mode1_field0b,
                    creature_field_after_value,creature_text,creature_tail_flag)
                VALUES(101,@pet,73,2,301,8,9,4,@name,11);
                INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('character',7,3,25,'world',101,101,1);
                INSERT INTO items(item_uid,item_core) VALUES(102,@food);
                INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('character',7,0,65,'world',102,102,1);
                """;
            command.Parameters.AddWithValue("@pet", Core(5, 63001, 101));
            command.Parameters.AddWithValue("@name", new byte[] { 0x81, 0x40 });
            command.Parameters.AddWithValue("@food", Core(1, 200));
            await command.ExecuteNonQueryAsync();
        }

        internal static byte[] Core(byte kind, int template, int identity = 0)
        {
            var core = new byte[82];
            core[0] = kind;
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), template);
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), identity);
            return core;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={this.Path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> ScalarAsync(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={this.Path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            File.Delete(this.Path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubTransaction : IDomainTransaction
    {
        private readonly Dictionary<object, ITransactionParticipant> participants = [];

        public TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create)
            where TParticipant : ITransactionParticipant
        {
            if (this.participants.TryGetValue(identity, out var participant))
                return (TParticipant)participant;
            var added = create();
            this.participants.Add(identity, added);
            return added;
        }

        public async Task SettleAsync()
        {
            try
            {
                foreach (var participant in this.participants.Values)
                    await participant.PrepareAsync(default);
                foreach (var participant in this.participants.Values)
                    await participant.CommitAsync(default);
            }
            catch
            {
                foreach (var participant in this.participants.Values)
                    await participant.RollbackAsync(default);
                throw;
            }
        }
    }
}
