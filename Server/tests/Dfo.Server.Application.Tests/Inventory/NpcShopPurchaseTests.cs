using Dfo.Server.Data.Game;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.BuyItem;
using Dfo.Server.Protocol.Game.ShopPurchaseCount;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class NpcShopPurchaseTests
{
    [Fact]
    public async Task Dungeon_clear_shop_cannot_be_purchased_from_town_even_with_enough_payment()
    {
        using var shop = new ShopFixture();
        shop.Content.Shops[1000] = new(1000, 1000, [new(null, [5000])]) { VisibleOnDungeonClear = true };
        await shop.Seed(121, 1000, 100, 3);
        await shop.Services.GetRequiredService<BuyItemHandler>().HandleAsync(new(shop.Connection),
            new BuyItemRequest(GameMessageMetadata.Empty, 5000, 1, 1000, 1000), default);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(100, shop.Count(1000));
        Assert.Equal(0, shop.Count(5000));
        Assert.Null(Assert.Single(shop.Sender.Messages.OfType<BuyItemResponse>()).Receipt);
    }

    [Fact]
    public async Task Buying_a_sought_item_completes_the_carried_quest_from_the_committed_bag()
    {
        using var shop = new ShopFixture(services =>
        {
            services.RemoveAll<IQuestContentRuleService>();
            services.AddSingleton<IQuestContentRuleService>(new SeekingQuestRules());
        });
        shop.Sql("INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) "
            + "VALUES(1,0,2001,1)");
        await shop.Seed(121, 1000, 100, 3);

        await shop.Buy(2000, 1);

        Assert.Equal(1, shop.Count(2000));
        Assert.Equal(0, shop.Scalar(
            "SELECT trigger_value FROM character_active_quests WHERE character_id=1 AND quest_id=2001"));
    }

    [Theory]
    [InlineData(10, 1, false)]
    [InlineData(9, 3, false)]
    [InlineData(9, 1, true)]
    public async Task Occupied_slots_allow_only_a_purchase_that_fits_completely(
        int firstPile, int bought, bool fits)
    {
        using var shop = new ShopFixture();
        shop.Content.Items[5000] = shop.Content.Items[5000] with
            { Limit = new(NpcShopLimitScope.Account, 5, false) };
        foreach (var slot in new[] { 3, 4, 5, 6, 7, 8, 65, 66 })
            await shop.Seed(slot, 5000, slot == 3 ? firstPile : 10);
        await shop.Seed(121, 1000, bought * 5, 3);

        await shop.Buy(5000, bought);

        Assert.Equal(100000 - (fits ? bought * 100 : 0), shop.Gold);
        Assert.Equal(fits ? 0 : bought * 5, shop.Count(1000));
        Assert.Equal(70 + firstPile + (fits ? bought : 0), shop.Count(5000));
        Assert.Equal(fits ? 1 : 0, shop.Scalar("SELECT COUNT(*) FROM npc_shop_purchase_counts"));
        if (fits)
            Assert.NotNull(Assert.Single(shop.Sender.Messages.OfType<BuyItemResponse>()).Receipt);
        else
        {
            var failure = Assert.IsType<BuyItemResponse>(Assert.Single(shop.Sender.Messages));
            Assert.Null(failure.Receipt);
            Assert.Equal(4, failure.ErrorCode);
        }
    }

    [Fact]
    public async Task Material_payment_commits_before_the_first_event_delivery()
    {
        using var shop = new ShopFixture();
        await shop.Seed(121, 1000, 100, 3);
        shop.Sender.BeforeSend = () =>
        {
            Assert.Equal(99, shop.Count(1000));
            Assert.Equal(1, shop.Count(2000));
            Assert.Equal(100000, shop.Gold);
        };
        await shop.Buy(2000, 1);
        Assert.Equal(new[] { ServerMessageType.GameStateSynchronization, ServerMessageType.GameBuyItem,
            ServerMessageType.GameStateSynchronization }, shop.Sender.Messages.Select(message => message.MessageType));
        var receipt = Assert.Single(shop.Sender.Messages.OfType<BuyItemResponse>()).Receipt!;
        Assert.Equal(321, receipt.SkillPoints);
        Assert.Equal(77, receipt.Cera);
        Assert.False(receipt.GoldSpent);
    }

    [Fact]
    public async Task Equipment_zero_sentinel_buys_one_and_persists_its_real_core()
    {
        using var shop = new ShopFixture();
        await shop.Buy(3000, 0);
        Assert.Equal(82000, shop.Gold);
        Assert.Equal(1, shop.Count(3000));
        var receipt = Assert.Single(shop.Sender.Messages.OfType<BuyItemResponse>()).Receipt!;
        var item = Assert.Single(receipt.GrantedSlots);
        Assert.True(item.Core.Value > 0);
        Assert.Equal(45, item.Core.Durability);
        Assert.Equal(1, receipt.Count);
        Assert.IsType<BuyItemResponse>(shop.Sender.Messages[0]);
        Assert.Equal(3, shop.Sender.Messages.Count);
    }

    [Fact]
    public async Task Mixed_payment_and_split_delivery_are_one_committed_purchase()
    {
        using var shop = new ShopFixture();
        await shop.Seed(121, 1000, 7, 3);
        await shop.Seed(122, 1000, 8, 3);
        await shop.Seed(3, 5000, 9);
        await shop.Buy(5000, 3);
        Assert.Equal(99700, shop.Gold);
        Assert.Equal(0, shop.Count(1000));
        Assert.Equal(12, shop.Count(5000));
        var receipt = Assert.Single(shop.Sender.Messages.OfType<BuyItemResponse>()).Receipt!;
        Assert.Equal(new[] { (BusinessInventorySpace.QuickSlot, 0, 10, 1), (BusinessInventorySpace.QuickSlot, 1, 2, 2) }, receipt.GrantedSlots
            .Select(slot => (slot.Slot.Space, slot.Slot.Index, slot.Core.Value, slot.AddedCount)));
        Assert.All(receipt.MaterialChanges, change => Assert.Null(change.Core));
        Assert.Equal(5, shop.Sender.Messages.Count);
    }

    [Fact]
    public async Task Limit_is_shared_across_characters_and_survives_a_new_day_without_reset_flag()
    {
        using var shop = new ShopFixture();
        await shop.Seed(121, 1000, 100, 3);
        await shop.Buy(4000, 1);
        await shop.Query();
        Assert.Equal(new NpcShopPurchaseCount(4000, 1), Assert.Single(
            shop.Sender.Messages.OfType<ShopPurchaseCountResponse>().Single().Result.Counts));
        shop.Clock.Advance(TimeSpan.FromDays(1));
        shop.Select(2);
        await shop.Seed(121, 1000, 100, 3);
        shop.Sender.Messages.Clear();
        await shop.Buy(4000, 1);
        Assert.Equal(0x85, Assert.IsType<BuyItemResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
        Assert.Equal(100, shop.Count(1000));
        Assert.Equal(0, shop.Count(4000));
        Assert.Equal(1, shop.Scalar("SELECT SUM(count) FROM npc_shop_purchase_counts"));
    }

    [Fact]
    public async Task Concurrent_limit_purchases_allow_exactly_one_payment()
    {
        using var shop = new ShopFixture();
        await shop.Seed(121, 1000, 100, 3);
        var results = await Task.WhenAll(Task.Run(() => shop.Command(4000, 1)), Task.Run(() => shop.Command(4000, 1)));
        Assert.Single(results, result => result.Receipt is not null);
        Assert.Single(results, result => result.Rejection == NpcShopRejection.PurchaseLimitReached);
        Assert.Equal(65, shop.Count(1000));
        Assert.Equal(1, shop.Count(4000));
    }

    [Fact]
    public async Task Failed_item_flush_rolls_back_gold_materials_and_the_limit_without_an_event()
    {
        using var shop = new ShopFixture();
        shop.Content.Items[4000] = shop.Content.Items[4000] with { GoldPrice = 100 };
        await shop.Seed(121, 1000, 100, 3);
        shop.Sql("CREATE TRIGGER reject_product BEFORE INSERT ON item_slots WHEN NEW.id = 4000 BEGIN SELECT RAISE(ABORT, 'forced product failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(async () => await shop.Buy(4000, 1));
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(100, shop.Count(1000));
        Assert.Equal(0, shop.Count(4000));
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM npc_shop_purchase_counts"));
        Assert.Empty(shop.Sender.Messages);
        shop.Sql("DROP TRIGGER reject_product");
        await shop.Buy(4000, 1);
        Assert.Equal(99900, shop.Gold);
        Assert.Equal(65, shop.Count(1000));
        Assert.Equal(1, shop.Count(4000));
    }

    [Fact]
    public async Task Delivery_failure_neither_repeats_the_purchase_nor_rolls_back_its_commit()
    {
        using var shop = new ShopFixture();
        await shop.Seed(121, 1000, 100, 3);
        shop.Sender.BeforeSend = () => throw new IOException("closed socket");
        await shop.Buy(2000, 1);
        Assert.Equal(1, shop.Sender.Attempts);
        Assert.Equal(99, shop.Count(1000));
        Assert.Equal(1, shop.Count(2000));
    }

    [Fact]
    public async Task Rejected_payment_only_sends_a_failure_and_leaves_balances_unchanged()
    {
        using var shop = new ShopFixture();
        await shop.Buy(2000, 1);
        Assert.Equal(1, Assert.IsType<BuyItemResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(0, shop.Count(2000));
    }

    [Fact]
    public async Task A_purchase_from_the_previous_selection_cannot_write_or_notify_the_replacement()
    {
        using var shop = new ShopFixture();
        await shop.Seed(121, 1000, 100, 3);
        var old = shop.Session;
        var committed = await shop.Command(2000, 1);
        shop.Select(2);
        var rejected = await shop.Command(2000, 1, old);
        Assert.Equal(NpcShopRejection.NoSession, rejected.Rejection);
        await shop.Publish(committed.Receipt!);
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(0, shop.Count(2000));
    }

    [Fact]
    public async Task Explicit_daily_reset_allows_a_purchase_in_the_new_period()
    {
        using var shop = new ShopFixture();
        shop.Content.Items[4000] = shop.Content.Items[4000] with
            { Limit = new(NpcShopLimitScope.Account, 1, true) };
        await shop.Seed(121, 1000, 100, 3);
        Assert.NotNull((await shop.Command(4000, 1)).Receipt);
        Assert.Equal(NpcShopRejection.PurchaseLimitReached, (await shop.Command(4000, 1)).Rejection);
        shop.Clock.Advance(TimeSpan.FromDays(1));
        Assert.NotNull((await shop.Command(4000, 1)).Receipt);
        Assert.Equal(30, shop.Count(1000));
        Assert.Equal(2, shop.Count(4000));
    }

    private sealed class SeekingQuestRules : IQuestContentRuleService
    {
        private static readonly QuestDefinition Quest = new(
            2001, "buy a potion", null, null, null, null, null, null, [], "seeking", "",
            null, null, null, null, null, null, null, [], [], [], [],
            SeekingItems: [new SeekingItemRequirement(2000, 1)]);

        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([Quest]);

        public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<QuestDefinition?>(questId == Quest.QuestId ? Quest : null);

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);

        public QuestParameters Parameters() => throw new NotSupportedException();
    }
}

internal sealed class ShopFixture : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dfo-npc-shop-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider provider;
    private readonly NetworkConnectionContext connection = new(new("shop-test"), NetworkEndpointKind.Game, 10011);
    private readonly OnlineUserId onlineUser;
    private IItemContainer bag = null!;
    private IItemContainer equipment = null!;
    private SelectedSessionIdentity session = null!;
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "shop.db") }.ToString();
    public NpcShopTestContent Content { get; } = new();
    public ShopSender Sender { get; } = new();
    public AdvanceableTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    public SelectedSessionIdentity Session => session;
    public IServiceProvider Services => provider;
    public NetworkConnectionContext Connection => connection;
    public OnlineUserId OnlineUser => onlineUser;

    public ShopFixture(Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:Path"] = Path.Combine(directory, "shop.db") }).Build();
        var services = new ServiceCollection().AddLogging();
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new ApplicationModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        services.AddSingleton<INpcShopDefinitionSource>(Content);
        services.AddSingleton<IItemPlacementDefinitionSource>(Content);
        services.AddSingleton<ICharacterSkillPointDomainService>(new SkillPoints());
        services.AddSingleton<INetworkMessageSender>(Sender);
        services.AddSingleton<TimeProvider>(Clock);
        configure?.Invoke(services);
        provider = services.BuildServiceProvider();
        Sender.Services = provider;
        _ = provider.GetRequiredService<SqliteDatabase>();
        Sql("INSERT INTO accounts(account_id,m_id,cera) VALUES(1,'shop',77); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(1,1,'shop1',12,2,0),(2,1,'shop2',12,2,1); INSERT INTO character_stats(character_id,carried_gold) VALUES(1,100000),(2,100000);");
        var registry = provider.GetRequiredService<OnlineStateRegistry>();
        onlineUser = registry.RegisterConnection(connection).OnlineUserId;
        registry.Authenticate(onlineUser, new AccountId(1));
        Select(1);
    }

    public void Select(int character)
    {
        var registry = provider.GetRequiredService<OnlineStateRegistry>();
        session = SelectedSessionIdentity.Create(new(1), new(character), connection.ConnectionId,
            registry.GetRequiredUser(onlineUser).Generation, 10011);
        registry.Publish(session);
        provider.GetRequiredService<ISelectedSessionDomainService>().Select(session);
        provider.GetRequiredService<ITownAreaDomainService>().Enter(new TownAreaMember(
            session.CharacterId,
            session.TownAreaLease(),
            new TownAreaKey(1, 0, 0),
            new CharacterPositionState(0, 0, 0, 0, 0),
            true));
        bag = new PersistentItemContainer(ContainerId.ForCharacter(session.CharacterId, InventoryContainerType.Main),
            session.CharacterId, new(InventoryContainerType.Main, 0), provider.GetRequiredService<IItemDomainStore>(),
            provider.GetRequiredService<IDomainTransactionAccessor>(), null!, provider.GetRequiredService<IItemPlacementRuleService>(),
            provider.GetRequiredService<IItemArrivalRuleService>(), provider.GetRequiredService<IItemSlotRuleService>());
        provider.GetRequiredService<ICharacterContainerDomainService>().Register(session.CharacterId, bag);
        equipment = new PersistentItemContainer(ContainerId.ForCharacter(session.CharacterId, InventoryContainerType.Equipment),
            session.CharacterId, new(InventoryContainerType.Equipment, 0), provider.GetRequiredService<IItemDomainStore>(),
            provider.GetRequiredService<IDomainTransactionAccessor>(), null!, provider.GetRequiredService<IItemPlacementRuleService>(),
            provider.GetRequiredService<IItemArrivalRuleService>(), provider.GetRequiredService<IItemSlotRuleService>());
        provider.GetRequiredService<ICharacterContainerDomainService>().Register(session.CharacterId, equipment);
        provider.GetRequiredService<ICharacterContainerDomainService>().Register(session.CharacterId,
            new PersistentItemContainer(ContainerId.ForAccount(session.AccountId, InventoryContainerType.SharedMaterial),
                session.CharacterId, new(InventoryContainerType.SharedMaterial, 11), provider.GetRequiredService<IItemDomainStore>(),
                provider.GetRequiredService<IDomainTransactionAccessor>(), null, provider.GetRequiredService<IItemPlacementRuleService>(),
                provider.GetRequiredService<IItemArrivalRuleService>(), provider.GetRequiredService<IItemSlotRuleService>()));
    }

    public async Task Seed(int slot, int item, int count, byte kind = 2)
        => await SeedCore(slot, StackableItemCore.Create(kind, item, count));

    public async Task SeedCore(int slot, InventoryItemCoreState core,
        InventoryContainerType container = InventoryContainerType.Main)
    {
        await using var tx = ((DomainTransactionScope)provider.GetRequiredService<IDomainTransactionAccessor>()).Begin();
        await (container == InventoryContainerType.Equipment ? equipment : bag).PutAsync(slot, core, default);
        await tx.CommitAsync(default);
    }

    public IBusinessItemContainer Shared(int account = 1) => provider.GetRequiredService<ICharacterBusinessInventoryDomainService>()
        .GetStore(new ContainerOwner(InventoryOwnerType.Account, account), BusinessInventoryStoreKind.Item)
        .GetContainer(BusinessInventorySpace.SharedMaterial);

    public async Task SeedShared(int item, int count, int account = 1)
    {
        await using var tx = ((DomainTransactionScope)provider.GetRequiredService<IDomainTransactionAccessor>()).Begin();
        await PutShared(item, count, account);
        await tx.CommitAsync(default);
    }
    public async Task PutShared(int item, int count, int account = 1)
    {
        Assert.True(AccountMaterialCatalog.TryGetSlotIndex(item, out var slot));
        if (count == 0) await Shared(account).ClearAsync(slot, default);
        else await Shared(account).PutAsync(slot, StackableItemCore.Create(AccountMaterialCatalog.Definitions[slot].ItemKind, item, count), default);
    }
    public int SharedCount(int item, int account = 1) => Scalar($"SELECT COALESCE(SUM(s.count),0) FROM item_slots s JOIN items i ON s.core_uid=i.item_uid WHERE s.owner_scope='account' AND s.owner_id={account} AND s.list_type=240 AND substr(hex(i.item_core),3,8)='{Convert.ToHexString(BitConverter.GetBytes(item))}'");

    public ValueTask Buy(int item, int count) => provider.GetRequiredService<BuyItemHandler>().HandleAsync(new(connection),
        new BuyItemRequest(GameMessageMetadata.Empty, item, count, 15, 2), default);
    public ValueTask Query() => provider.GetRequiredService<ShopPurchaseCountHandler>().HandleAsync(new(connection),
        new ShopPurchaseCountRequest(GameMessageMetadata.Empty, 2), default);
    public Task<BuyItemOutcome> Command(int item, int count, SelectedSessionIdentity? owner = null) => provider.GetRequiredService<ICommandBus>()
        .SendAsync(new BuyItemCommand(onlineUser, owner ?? session, item, count, 15, 2)).AsTask();
    public async ValueTask Publish(NpcShopPurchaseReceipt receipt) =>
        _ = await provider.GetRequiredService<INotificationBus>().PublishAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]), new NpcShopPurchaseCompleted(receipt), default);
    public int Gold => Scalar($"SELECT carried_gold FROM character_stats WHERE character_id={session.CharacterId.Value}");
    public async ValueTask<ushort?> Durability(InventoryContainerType container, int slot) =>
        (await (container == InventoryContainerType.Equipment ? equipment : bag).ReadSlotAsync(slot, default))?.Core.Durability;
    public int Count(int item) => Scalar($"SELECT COALESCE(SUM(s.count),0) FROM item_slots s JOIN items i ON s.core_uid=i.item_uid WHERE s.owner_scope='character' AND s.owner_id={session.CharacterId.Value} AND substr(hex(i.item_core),3,8)='{Convert.ToHexString(BitConverter.GetBytes(item))}'");
    public int Scalar(string sql)
    {
        using var db = new SqliteConnection(ConnectionString);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }
    public byte[] StoredCore(int slot, InventoryContainerType container = InventoryContainerType.Main)
    {
        using var db = new SqliteConnection(ConnectionString);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT i.item_core FROM items i JOIN item_slots s ON s.core_uid=i.item_uid "
            + "WHERE s.owner_scope='character' AND s.owner_id=$owner AND s.list_type=$list AND s.slot_index=$slot";
        command.Parameters.AddWithValue("$owner", session.CharacterId.Value);
        command.Parameters.AddWithValue("$list", (byte)container);
        command.Parameters.AddWithValue("$slot", slot);
        return (byte[])command.ExecuteScalar()!;
    }

    public void Sql(string sql)
    {
        using var db = new SqliteConnection(ConnectionString);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    public void Dispose()
    {
        provider.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, true);
    }

    private sealed class SkillPoints : ICharacterSkillPointDomainService
    {
    public ValueTask<CharacterSkillPointLedger?> LedgerAsync(CharacterId characterId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CharacterSkillProtocolMirrorState?> RemainingAsync(CharacterId id, CancellationToken ct) =>
            ValueTask.FromResult<CharacterSkillProtocolMirrorState?>(new(321, 0, 0, 0));
    }
}

internal sealed class ShopSender : INetworkMessageSender
{
    public IServiceProvider Services { get; set; } = null!;
    public List<IServerNetworkMessage> Messages { get; } = [];
    public List<(NetworkConnectionContext Connection, IServerNetworkMessage[] Messages, byte[][] Frames)> Batches { get; } = [];
    public Action? BeforeExplicit { get; set; }
    public bool FailExplicit { get; set; }
    public bool FailBatch { get; set; }
    public Action? BeforeSend { get; set; }
    public int Attempts { get; private set; }
    public ValueTask SendAsync(NetworkConnectionId id, IServerNetworkMessage message, CancellationToken ct)
    {
        Attempts++;
        BeforeSend?.Invoke();
        Messages.Add(message);
        return ValueTask.CompletedTask;
    }
    public ValueTask SendExplicitAsync(NetworkConnectionId id, ExplicitMessageDelivery delivery, CancellationToken ct)
    {
        BeforeExplicit?.Invoke();
        if (FailExplicit) throw new IOException("Fixture Explicit publication failed after settlement.");
        foreach (var message in delivery.RequiredDirectMessages) Messages.Add(message);
        foreach (var target in delivery.BestEffortTargets) Messages.AddRange(target.OrderedMessages);
        return ValueTask.CompletedTask;
    }
    public ValueTask SendBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken ct)
    {
        var registrations = Services.GetServices<ServerMessageEncoderRegistration>().ToDictionary(x => x.MessageType);
        var ownedMessages = messages.ToArray();
        var frames = ownedMessages.SelectMany(message =>
        {
            var registration = registrations[message.GetType()];
            Assert.Equal(connection.EndpointKind, registration.Endpoint);
            return ((IServerMessageEncoder)Services.GetRequiredService(registration.EncoderType)).Encode(message);
        }).Select(frame => frame.ToArray()).ToArray();
        Assert.All(frames, frame => Assert.InRange(frame.Length, 15, 1_048_576));
        BeforeSend?.Invoke();
        Batches.Add((connection, ownedMessages, frames));
        if (FailBatch) throw new IOException("Fixture batch transport failed after preencoding.");
        Messages.AddRange(ownedMessages);
        return ValueTask.CompletedTask;
    }
}
