using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.PrivateStore;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dfo.Server.Persistence.Tests.PrivateStore;

/// <summary>Stall custody as written to the real schema.</summary>
public sealed class PrivateStoreCustodyTests : IDisposable
{
    private static readonly CharacterId Seller = new(71);
    private static readonly ContainerId Bag = new("character:71:Main");
    private static readonly ContainerId BuyerBag = new("character:72:Main");

    private readonly string directory = Path.Combine(Path.GetTempPath(), $"dfo-stall-{Guid.NewGuid():N}");
    private readonly string path;
    private readonly DomainTransactionScope scope = TestRequired.TransactionScope();

    public PrivateStoreCustodyTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "stall.db");
        _ = new SqliteDatabase(path, scope);
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts(account_id,m_id) VALUES(1,'one'),(2,'two');
            INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,delete_flag) VALUES
                (71,1,'seller',0,0,1,0,0),(72,2,'buyer',0,0,1,0,0);
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }

    private static ItemSnapshot Sword() => new() { ItemId = 123, LegacyKind = 1, Type = ItemTraits.Equipment, Count = 1,
        InstanceValue = 4321, Durability = 31, Attribute = 7 };

    private static ItemSnapshot Stones(int count) => new() { ItemId = 3037, LegacyKind = 2, Type = ItemTraitClassification.For(2, 3037),
        Count = count, IsPile = true };

    private SqliteItemDomainStore Items() => new(path, contentsEvents: TestRequired.NoContentsEvents);

    private Task InTransaction(Func<IItemDomainStoreSession, IPrivateStoreItemCustodySession, Task> body) => TestGameplay.RunAsync(async () =>
    {
        await using var transaction = scope.Begin();
        var items = Items().SessionIn(scope.Current!);
        await body(items, (IPrivateStoreItemCustodySession)items);
        await transaction.CommitAsync(default);
    });

    [Fact]
    public async Task Lines_keep_price_identity_and_owner_until_the_last_unit_goes()
    {
        ItemRecord? sword = null;
        await InTransaction(async (items, stall) =>
        {
            await items.PutAsync(Bag, 9, await items.CreateAsync(Sword(), default), default);
            await items.PutAsync(Bag, 20, await items.CreateAsync(Stones(50), default), default);
            sword = await items.TakeAsync((await items.GetAsync(Bag, 9, default))!, null, default);
            await stall.AcceptPrivateStoreLineAsync(Seller, 0, 1_000, sword, default);
            await stall.AcceptPrivateStoreLineAsync(Seller, 3, 25,
                await items.TakeAsync((await items.GetAsync(Bag, 20, default))!, 30, default), default);
            Assert.Equal(20, (await items.GetAsync(Bag, 20, default))!.Count);
        });
        Assert.Equal([Seller], await new SqlitePrivateStoreDomainStore(new SqliteDatabase(path, scope)).OwnersWithCustodyAsync(default));

        await InTransaction(async (items, stall) =>
        {
            var lines = await stall.ReadPrivateStoreLinesAsync(Seller, default);
            Assert.Equal([(0, 1_000L, 123, 1), (3, 25L, 3037, 30)],
                lines.Select(line => ((int)line.DisplaySlot, line.UnitPrice, line.Snapshot.ItemId, line.Snapshot.Count)));
            var part = await stall.TakePrivateStoreLineAsync(Seller, 3, 12, default);
            Assert.Equal(12, part.Count);
            await items.PutAsync(BuyerBag, 20, part, default);
            var bought = await stall.TakePrivateStoreLineAsync(Seller, 0, 1, default);
            Assert.Equal(sword!.ItemUid, bought.ItemUid);
            Assert.Equal(sword.Snapshot.ToCore(), bought.Snapshot.ToCore());
            await items.PutAsync(BuyerBag, 3, bought, default);
        });

        await InTransaction(async (items, stall) =>
        {
            Assert.Equal(18, Assert.Single(await stall.ReadPrivateStoreLinesAsync(Seller, default)).Snapshot.Count);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => stall.TakePrivateStoreLineAsync(Seller, 3, 19, default).AsTask());
            await items.PutAsync(Bag, 21, await stall.TakePrivateStoreLineAsync(Seller, 3, null, default), default);
            Assert.Empty(await stall.ReadPrivateStoreLinesAsync(Seller, default));
        });
        Assert.Empty(await new SqlitePrivateStoreDomainStore(new SqliteDatabase(path, scope)).OwnersWithCustodyAsync(default));
    }

    [Fact]
    public async Task Equipment_cannot_be_split()
    {
        await InTransaction(async (items, stall) =>
        {
            await items.PutAsync(Bag, 9, await items.CreateAsync(Sword(), default), default);
            await stall.AcceptPrivateStoreLineAsync(Seller, 1, 10,
                await items.TakeAsync((await items.GetAsync(Bag, 9, default))!, null, default), default);
        });
        await InTransaction(async (_, stall) =>
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => stall.TakePrivateStoreLineAsync(Seller, 1, 2, default).AsTask()));
    }
}
