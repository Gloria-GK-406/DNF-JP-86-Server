using Dfo.Server.Application.Transactions;
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
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Creatures;

public sealed class CreatureDomainStoreTests
{
    private static readonly CharacterId Owner = new(7);

    [Fact]
    public async Task CreatureStateFollowsItemIdentityAndKeepsUnknownFields()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var store = new SqliteCreatureDomainStore(fixture.Path, TestRequired.NoContentsEvents);
        await InTransactionAsync(async transaction =>
        {
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
            Assert.Equal((2, 8, 9, 11), (original.ModeFlag, original.Mode1Field0A, original.Mode1Field0B, original.TailFlag));
            await session.WriteAsync((await session.FindItemAsync(Owner, 101, default))!, original with
                { Stomach = 80, Experience = 900, Level = 5, NameBytes = [0x82, 0x41] }, null, default);
            Assert.Equal(900, (await session.ReadAsync(Owner, 101, default))!.Experience);
        });
        await fixture.ExecuteAsync("UPDATE item_slots SET slot_index=14 WHERE core_uid=101");
        await InTransactionAsync(async transaction =>
        {
            var session = store.SessionIn(transaction);
            var after = Assert.Single(await session.ReadAllAsync(Owner, default));
            Assert.Equal((101, 63001, 80, 900, 5), (after.ItemUid, after.ItemTemplateId, after.Stomach, after.Experience, after.Level));
            Assert.Equal([0x82, 0x41], after.NameBytes);
            Assert.Equal((2, 8, 9, 11), (after.ModeFlag, after.Mode1Field0A, after.Mode1Field0B, after.TailFlag));
            Assert.Null(await session.ReadCurrentAsync(Owner, default));
        });
    }

    [Fact]
    public async Task CreatureAndConsumedItemCommitTogetherOrRollbackTogether()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var creatures = new SqliteCreatureDomainStore(fixture.Path, TestRequired.NoContentsEvents);
        var items = new SqliteItemDomainStore(fixture.Path, TestRequired.NoContentsEvents);
        var main = ContainerId.ForCharacter(Owner, InventoryContainerType.Main);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InTransactionAsync(async transaction =>
        {
            var itemSession = items.SessionIn(transaction);
            await itemSession.DeleteItemAsync(await itemSession.TakeAsync((await itemSession.GetAsync(main, 65, default))!, null, default), default);
            var pets = creatures.SessionIn(transaction);
            var reference = (await pets.FindItemAsync(Owner, 101, default))!;
            var state = (await pets.ReadAsync(Owner, 101, default))!;
            // Templates may evolve; replacing the instance identity is the invalid operation.
            await pets.WriteAsync(reference, state with { ItemUid = 99999, Stomach = 85 }, null, default);
        }));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM item_slots WHERE slot_index=65"));
        Assert.Equal(73L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
        await InTransactionAsync(async transaction =>
        {
            var itemSession = items.SessionIn(transaction);
            await itemSession.DeleteItemAsync(await itemSession.TakeAsync((await itemSession.GetAsync(main, 65, default))!, null, default), default);
            var pets = creatures.SessionIn(transaction);
            var state = (await pets.ReadAsync(Owner, 101, default))!;
            await pets.WriteAsync((await pets.FindItemAsync(Owner, 101, default))!, state with { Stomach = 85 }, null, default);
        });
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM item_slots WHERE slot_index=65"));
        Assert.Equal(85L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
    }

    [Fact]
    public async Task AnotherCharacterGetsNoPetReferenceAndOldConditionViewSharesTheFacet()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var creatures = new SqliteCreatureDomainStore(fixture.Path, TestRequired.NoContentsEvents);
        ItemReference? oldReference = null;
        CreatureState? state = null;
        await InTransactionAsync(async transaction =>
        {
            var session = creatures.SessionIn(transaction);
            state = await session.ReadAsync(Owner, 101, default);
            oldReference = await session.FindItemAsync(Owner, 101, default);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => InTransactionAsync(async transaction =>
        {
            var session = creatures.SessionIn(transaction);
            Assert.Null(await session.ReadAsync(new CharacterId(8), 101, default));
            Assert.Null(await session.FindItemAsync(new CharacterId(8), 101, default));
            await session.WriteAsync(oldReference!, state! with { Stomach = 1 }, null, default);
        }));
        Assert.Equal(73L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
        await InTransactionAsync(async transaction =>
        {
            var old = new Dfo.Server.Persistence.Characters.SqliteCharacterDomainStore(fixture.Path).SessionIn(transaction).Creatures;
            old.Write(new(new(3), Owner), new(101, 1));
            Assert.Equal(1, (await creatures.SessionIn(transaction).ReadAsync(Owner, 101, default))!.Stomach);
        });
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
    }

    [Fact]
    public async Task CreatureDetailWithNonCreatureCoreIsRejectedOnRead()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.ExecuteAsync("UPDATE items SET item_core=zeroblob(82) WHERE item_uid=101");
        await Assert.ThrowsAsync<InvalidDataException>(() => InTransactionAsync(async transaction =>
        {
            await new SqliteCreatureDomainStore(fixture.Path, TestRequired.NoContentsEvents).SessionIn(transaction).ReadCurrentAsync(Owner, default);
        }));
    }

    [Fact]
    public async Task StagedMoveIsVisibleToCurrentAndAllCreatureReads()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await InTransactionAsync(async transaction =>
        {
            var items = new SqliteItemDomainStore(fixture.Path, TestRequired.NoContentsEvents).SessionIn(transaction);
            var creatures = new SqliteCreatureDomainStore(fixture.Path, TestRequired.NoContentsEvents).SessionIn(transaction);
            var equipment = ContainerId.ForCharacter(Owner, InventoryContainerType.Equipment);
            var petBag = ContainerId.ForCharacter(Owner, InventoryContainerType.Pet);
            var moving = await items.TakeAsync((await items.GetAsync(equipment, 25, default))!, null, default);
            var landed = await items.PutAsync(petBag, 0, moving, default);
            Assert.Null(await creatures.ReadCurrentAsync(Owner, default));
            Assert.Equal(101, Assert.Single(await creatures.ReadAllAsync(Owner, default)).ItemUid);
            await items.PutAsync(equipment, 25, await items.TakeAsync(landed, null, default), default);
            Assert.Equal(101, (await creatures.ReadCurrentAsync(Owner, default))!.ItemUid);
        });
    }

    [Fact]
    public async Task ChangingCreatureTemplateKeepsIdentityAndAllDetails()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await InTransactionAsync(async transaction =>
        {
            var items = new SqliteItemDomainStore(fixture.Path, TestRequired.NoContentsEvents).SessionIn(transaction);
            var source = (await items.GetAsync(ContainerId.ForCharacter(Owner, InventoryContainerType.Equipment), 25, default))!;
            await items.UpdateAsync(source, snapshot => snapshot with { ItemId = 63002 }, default);
            var creatures = new SqliteCreatureDomainStore(fixture.Path, TestRequired.NoContentsEvents).SessionIn(transaction);
            var updated = (await creatures.ReadCurrentAsync(Owner, default))!;
            Assert.Equal(101, updated.ItemUid);
            Assert.Equal(63002, updated.ItemTemplateId);
            Assert.Equal((73, 301, 4), (updated.Stomach, updated.Experience, updated.Level));
            Assert.Equal([0x81, 0x40], updated.NameBytes);
        });
        Assert.Equal(101L, await fixture.ScalarAsync("SELECT core_uid FROM item_slots WHERE owner_id=7 AND list_type=3 AND slot_index=25"));
        Assert.Equal(73L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
    }

    [Fact]
    public async Task ReviveIfEmptyRejectsASecondRevival()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.ExecuteAsync("UPDATE items SET creature_field04=0 WHERE item_uid=101");
        var store = new SqliteCreatureDomainStore(fixture.Path, TestRequired.NoContentsEvents);
        await InTransactionAsync(async transaction =>
        {
            var session = store.SessionIn(transaction);
            session.ReviveIfEmpty(Owner, 101);
            Assert.Equal(1, (await session.ReadCurrentAsync(Owner, default))!.Stomach);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => InTransactionAsync(transaction =>
        {
            store.SessionIn(transaction).ReviveIfEmpty(Owner, 101);
            return ValueTask.CompletedTask;
        }));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT creature_field04 FROM items WHERE item_uid=101"));
    }

    private static async Task InTransactionAsync(Func<IDomainTransaction, ValueTask> action)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            await using var transaction = scope.Begin();
            await action(scope.Current!);
            await transaction.CommitAsync(default);
        });
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

}
