using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

public sealed class CeraShopStorageTests
{
    [Fact]
    public async Task Restriction_compare_and_set_persists_only_matching_account_state_and_requires_transaction()
    {
        using var fixture = new StorageFixture();
        var table = new SqliteAccountPurchaseRestrictionTable(fixture.Database);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await table.TrySetAsync(new(1),32,null,100,default));
        await using (var transaction = fixture.Scope.Begin())
        {
            Assert.True(await table.TrySetAsync(new(1),32,null,100,default));
            Assert.False(await table.TrySetAsync(new(1),32,null,200,default));
            Assert.False(await table.TrySetAsync(new(1),32,99,200,default));
            Assert.False(await table.TrySetAsync(new(2),32,100,200,default));
            Assert.True(await table.TrySetAsync(new(1),32,100,200,default));
            await transaction.CommitAsync(default);
        }
        var reopened = new SqliteAccountPurchaseRestrictionTable(new SqliteDatabase(
            new SqliteConnectionStringBuilder(fixture.Database.ConnectionString).DataSource, fixture.Scope));
        Assert.Equal(200, Assert.Single(await reopened.ReadAsync(new(1),default)).EndsAtUnixSeconds);
        Assert.Empty(await reopened.ReadAsync(new(2),default));
    }

    [Fact]
    public async Task New_creature_reloads_with_all_facts_and_the_same_wire_identity()
    {
        using var fixture = new StorageFixture();
        var facts = new InventoryCreatureDetailState(100, 1, 123, 4, 5, 6, [65, 66], 7);
        await using (var transaction = fixture.Scope.Begin())
        {
            var created = await fixture.Items.CreateAsync(ItemSnapshot.FromCore(
                StackableItemCore.Create(5, 63022, 0), false, creature: facts), default);
            await fixture.Items.PutAsync(fixture.Pets, 0, created, default);
            await transaction.CommitAsync(default);
        }

        var pet = Assert.Single(await new SqliteCharacterCreatureTable(fixture.Database).ReadAsync(new(1), default));
        var item = Assert.Single(await new SqliteCharacterItemTable(fixture.Database).ReadByCharacterAsync(new(1), default));
        Assert.Equal(item.ItemUid, pet.CreatureKey);
        Assert.Equal(pet.CreatureKey, item.Core.Value);
        Assert.Equal((100, 1, 123, 4, 5, 6, 7),
            (pet.Field04, pet.ModeFlag, pet.ProgressValue, pet.Mode1Field0A, pet.Mode1Field0B, pet.FieldAfterValue, pet.TailFlag));
        Assert.Equal(facts.CreatureText, pet.CreatureText);

        await using (var transaction = fixture.Scope.Begin())
        {
            var moving = await fixture.Items.TakeAsync((await fixture.Items.GetAsync(fixture.Pets, 0, default))!, null, default);
            await fixture.Items.PutAsync(fixture.Pets, 10, moving, default);
            await transaction.CommitAsync(default);
        }
        var moved = Assert.Single(await new SqliteCharacterCreatureTable(fixture.Database).ReadAsync(new(1), default));
        Assert.Equal(pet.CreatureKey, moved.CreatureKey);
        Assert.Equal(pet.CreatureText, moved.CreatureText);
        Assert.Equal(pet with { CreatureText = moved.CreatureText }, moved);
    }

    [Fact]
    public async Task Currency_compare_exchange_preserves_other_columns_and_rejects_stale_or_negative_balances()
    {
        using var fixture = new StorageFixture();
        await using (var transaction = fixture.Scope.Begin())
        {
            var accounts = new SqliteAccountTable(fixture.Database);
            Assert.Equal(1, await accounts.TrySetCeraBalancesAsync(new(1), new(100000, 20, 30), new(96400, 20, 30), default));
            Assert.Equal(0, await accounts.TrySetCeraBalancesAsync(new(1), new(100000, 20, 30), new(92800, 20, 30), default));
            Assert.Equal(0, await accounts.TrySetCeraBalancesAsync(new(1), new(96400, 20, 30), new(-1, 20, 30), default));
            await transaction.CommitAsync(default);
        }
        var wallet = await new SqliteAccountTable(fixture.Database).FindWalletAsync(new(1), default);
        Assert.Equal((96400L, 20L, 30L, 4L, 11L, 23L),
            (wallet!.Cera, wallet.TokenCera, wallet.HappyTokenCera, wallet.LuckyStar, wallet.HonorExperience, wallet.GrowthCapsuleExperience));
        Assert.Equal(777, fixture.Scalar("SELECT cera FROM accounts WHERE account_id=2"));
    }

    [Fact]
    public async Task Failure_after_payment_and_first_item_rolls_back_every_write()
    {
        using var fixture = new StorageFixture();
        fixture.Sql("CREATE TRIGGER fail_creature BEFORE INSERT ON items WHEN NEW.creature_field04 IS NOT NULL BEGIN SELECT RAISE(ABORT,'forced creature failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await using var transaction = fixture.Scope.Begin();
            Assert.Equal(1, await new SqliteAccountTable(fixture.Database).TrySetCeraBalancesAsync(
                new(1), new(100000, 20, 30), new(96400, 0, 0), default));
            await fixture.Items.PutAsync(ContainerId.ForCharacter(new(1), InventoryContainerType.Main), 65,
                await fixture.Items.CreateAsync(ItemSnapshot.FromCore(StackableItemCore.Create(2, 3, 1), true), default), default);
            await fixture.Items.ApplyDecidedWritesAsync(default);
            await fixture.Items.PutAsync(fixture.Pets, 0, await fixture.Items.CreateAsync(ItemSnapshot.FromCore(
                StackableItemCore.Create(5, 63022, 0), false,
                creature: new InventoryCreatureDetailState(100, 0, 0, 0, 0, 1, [], 0)), default), default);
            await fixture.Items.ApplyDecidedWritesAsync(default);
            await transaction.CommitAsync(default);
        });
        Assert.Equal(100000, fixture.Scalar("SELECT cera FROM accounts WHERE account_id=1"));
        Assert.Equal(20, fixture.Scalar("SELECT token_cera FROM accounts WHERE account_id=1"));
        Assert.Equal(30, fixture.Scalar("SELECT happy_token_cera FROM accounts WHERE account_id=1"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM item_slots"));
    }

    private sealed class StorageFixture : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"dfo-cera-storage-{Guid.NewGuid():N}.db");
        private readonly SqliteItemDomainStore store;
        public DomainTransactionScope Scope { get; } = new();
        public SqliteDatabase Database { get; }
        public ContainerId Pets { get; } = ContainerId.ForCharacter(new CharacterId(1), InventoryContainerType.Pet);
        public IItemDomainStoreSession Items => store.SessionIn(Scope.Current!);

        public StorageFixture()
        {
            Database = new(path, Scope);
            store = new(path);
            Sql("INSERT INTO accounts(account_id,m_id,cera,token_cera,happy_token_cera,lucky_star,honor_exp,growth_capsule_exp) VALUES(1,'cera-storage',100000,20,30,4,11,23),(2,'other',777,0,0,0,0,0); INSERT INTO characters(character_id,account_id,name,job,level) VALUES(1,1,'buyer',12,85);");
        }

        public void Sql(string sql)
        {
            using var connection = new SqliteConnection(Database.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public int Scalar(string sql)
        {
            using var connection = new SqliteConnection(Database.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }
}
