using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Quests;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Transactions;

public sealed class DungeonPickupTransactionTests
{
    private static readonly AccountId Account = new(6);
    private static readonly CharacterId Character = new(60);
    private static readonly ContainerId Main =
        ContainerId.ForCharacter(Character, InventoryContainerType.Main);
    private static readonly DungeonLayout Layout = new(
        145,
        0,
        1,
        1,
        new MazeCell(0, 0),
        new MazeCell(0, 0),
        8154,
        1,
        new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 8154 },
        15);

    [Fact]
    public async Task Sqlite_cas_failure_rolls_back_the_staged_floor_consumption()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-pickup-tx-{Guid.NewGuid():N}.db");
        try
        {
            var bootstrap = new DomainTransactionScope();
            _ = new SqliteDatabase(path, bootstrap);
            await SeedAsync(path);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddCommandDispatching();
            services.AddSingleton<IDungeonRunDomainService, DungeonRunDomainService>();
            services.AddSingleton<IQuestDomainStore>(new SqliteQuestDomainStore(path));
            services.AddSingleton<IItemDomainStore>(new SqliteItemDomainStore(path));
            services.AddCommandHandler<StaleQuestPickupCommand, PickupReceipt,
                StaleQuestPickupHandler>(CommandExecutionKind.Transactional);
            await using var provider = services.BuildServiceProvider();

            var runs = provider.GetRequiredService<IDungeonRunDomainService>();
            runs.Begin(Character, Layout, difficulty: 0);
            var drop = Assert.Single(runs.AllocateMonsterDrops(
                Character,
                0,
                1,
                [new RolledDrop(10149163, 1, DropRarity.Common, DropItemKind.Stackable)]));

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await provider.GetRequiredService<ICommandBus>().SendAsync(
                    new StaleQuestPickupCommand(drop.Number), CancellationToken.None));

            Assert.NotNull(runs.Find(Character)!.FindMonsterDrop(drop.Number));
            Assert.Equal(13, await TriggerAsync(path));
            Assert.Equal(0, await ItemCountAsync(path));
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
                // A pooled test connection may outlive the assertion process briefly.
            }
        }
    }

    private static async Task SeedAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts(account_id,m_id,password_hash)
            VALUES(6,'pickup','x');
            INSERT INTO characters(character_id,account_id,name)
            VALUES(60,6,'pickup');
            INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value)
            VALUES(60,0,1779,13);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> TriggerAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT trigger_value FROM character_active_quests WHERE character_id=60 AND slot=0";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ItemCountAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM item_slots WHERE owner_scope='character' AND owner_id=60";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static InventoryItemCoreState QuestItem() =>
        new(6, 10149163, 1, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0),
            new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0),
            0, byte.MaxValue, 0,
            new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private sealed record StaleQuestPickupCommand(ushort Number) : ICommand<PickupReceipt>;

    private sealed record PickupReceipt(DungeonPickupCommit Commit);

    private sealed class StaleQuestPickupHandler(
        IDungeonRunDomainService runs,
        IQuestDomainStore quests,
        IItemDomainStore items,
        IDomainTransactionAccessor transactions)
        : ICommandHandler<StaleQuestPickupCommand, PickupReceipt>
    {
        public ValueTask<PickupReceipt> HandleAsync(
            CommandExecutionContext context,
            StaleQuestPickupCommand command,
            CancellationToken cancellationToken)
        {
            var run = runs.Find(Character) ?? throw new InvalidOperationException("run missing");
            var claim = runs.TryClaimGroundDrop(
                    Character, run.RunId, run.RoomVersion, command.Number)
                ?? throw new InvalidOperationException("drop claim missing");
            var commit = runs.StageGroundDrop(claim, DungeonPickupKind.Item);

            var transaction = transactions.Current
                ?? throw new InvalidOperationException("transaction missing");
            items.SessionIn(transaction).Write(
                Main,
                new StoredItem(0, QuestItem()) { WorldClass = ItemWorldClass.Interchangeable });
            quests.SessionIn(transaction).Carried.Write(
                new OwnedCharacterId(Account, Character),
                new StoredActiveQuest(0, 1779, 14, ExpectedTriggerValue: 15));
            return ValueTask.FromResult(new PickupReceipt(commit));
        }
    }
}
