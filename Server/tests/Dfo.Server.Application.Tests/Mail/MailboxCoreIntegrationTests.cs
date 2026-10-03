using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Mail;

public sealed partial class MailboxCoreIntegrationTests
{
    private static readonly CharacterId Sender = new(1);
    private static readonly CharacterId Recipient = new(2);
    private static readonly CharacterId Stranger = new(3);

    [Fact]
    public async Task EquipmentRetainsUidAndCoreWhileDetachedAndAfterClaim()
    {
        using var fixture = new MailFixture();
        var uid = fixture.SeedEquipment(Sender, 9, 3000);
        var before = fixture.EquipmentCore(uid);
        var receipt = await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromCharacter(Sender), Recipient)
            .WithText("equipment", "same instance").AddEquipment(new ItemIdentity(uid)).Build());
        Assert.Equal(0, fixture.EquipmentSlots(Sender, uid));
        Assert.Equal(before, fixture.EquipmentCore(uid));
        Assert.Equal(1, fixture.EquipmentRows(uid));
        var mail = Assert.Single(await fixture.Mail.InboxAsync(Recipient));
        var attachment = Assert.Single(mail.Attachments);
        Assert.Equal(uid, Assert.IsType<MailAsset.Equipment>(attachment.Asset).Identity.Value);
        await AssertMailFailureAsync(MailFailure.NotOwned, () => fixture.Mail.SendAsync(
            new MailBuilder(MailSource.FromCharacter(Sender), Stranger).WithText("repeat", "repeat")
                .AddEquipment(new ItemIdentity(uid)).Build()).AsTask());
        Assert.Equal(1, fixture.InboxRows(Recipient));

        using var reopened = fixture.Reopen();
        await reopened.Mail.ClaimAsync(Recipient, attachment.Id);
        Assert.Equal(1, fixture.EquipmentSlots(Recipient, uid));
        Assert.Equal(0, fixture.EquipmentSlots(Sender, uid));
        Assert.Equal(1, fixture.EquipmentRows(uid));
        Assert.Equal(before, fixture.EquipmentCore(uid));
        Assert.Equal(receipt.MailId, Assert.Single(await reopened.Mail.InboxAsync(Recipient)).Id);
    }

    [Fact]
    public async Task LoginPageKeepsTotalCountAndOnlyReturnsOldestTwentyInReverseOrder()
    {
        using var fixture = new MailFixture();
        var ids = new List<long>();
        for (var i = 0; i < 25; i++)
            ids.Add((await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromSystem(), Recipient)
                .WithText($"mail {i}", "body").Build())).MailId);
        var page = await fixture.Query.PageAsync(Recipient, 20);
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(ids.Take(20).Reverse(), page.Messages.Select(x => x.Id));
        Assert.All(page.Messages, x => Assert.False(x.Read));
    }

    [Fact]
    public void BuilderRejectsInvalidStructureAndCopiesAttachmentList()
    {
        Assert.Equal(MailFailure.InvalidMail, Assert.Throws<MailException>(() =>
            new MailBuilder(MailSource.FromSystem(), new CharacterId(0)).Build()).Reason);
        Assert.Equal(MailFailure.InvalidMail, Assert.Throws<MailException>(() =>
            new MailBuilder(MailSource.FromSystem(), Recipient).AddStackable(1000, 0).Build()).Reason);
        var builder = new MailBuilder(MailSource.FromSystem(), Recipient).AddGold(5);
        var mail = builder.Build();
        builder.AddGold(7);
        Assert.Single(mail.Attachments);
    }

    [Fact]
    public async Task CharacterSendAndClaim_ConserveStackAndGoldAcrossReload()
    {
        using var fixture = new MailFixture();
        await fixture.SeedStackAsync(Sender, 121, 1000, 7, 3);
        var receipt = await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromCharacter(Sender), Recipient)
            .WithText("transfer", "three materials and gold")
            .AddStackable(1000, 3).AddGold(250).Build());

        Assert.True(receipt.MailId > 0);
        Assert.Equal(4, await fixture.StackAsync(Sender, 1000));
        Assert.Equal(750, fixture.Gold(Sender));
        Assert.Equal(1000, fixture.Gold(Recipient));
        Assert.Equal(1, fixture.InboxRows(Recipient));

        var reopened = fixture.Reopen();
        using (reopened)
        {
            var mail = Assert.Single(await reopened.Mail.InboxAsync(Recipient));
            Assert.Equal(receipt.MailId, mail.Id);
            Assert.Equal("sender", mail.SenderName);
            Assert.Equal(2, mail.Attachments.Length);
            Assert.NotEqual(mail.Attachments[0].Id, mail.Attachments[1].Id);
            await reopened.Mail.ClaimAsync(Recipient, mail.Attachments[0].Id);
            await reopened.Mail.ClaimAsync(Recipient, mail.Attachments[1].Id);
            Assert.Equal(3, await fixture.StackAsync(Recipient, 1000));
            Assert.Equal(1250, fixture.Gold(Recipient));
            Assert.All((await reopened.Mail.InboxAsync(Recipient)).Single().Attachments, x => Assert.True(x.Claimed));
        }
        Assert.Equal(7, await fixture.TotalStackAsync(1000));
        Assert.Equal(2000, fixture.Gold(Sender) + fixture.Gold(Recipient));
    }

    [Fact]
    public async Task SystemCanDeliverToOfflineCharacterAndOnlyRecipientCanClaim()
    {
        using var fixture = new MailFixture();
        var receipt = await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromSystem(), Recipient)
            .WithText("offline", "reward")
            .AddStackable(1000, 2).Build());
        Assert.Equal(1000, fixture.Gold(Sender));
        Assert.Equal(1000, fixture.Gold(Recipient));

        using var reopened = fixture.Reopen();
        var mail = Assert.Single(await reopened.Mail.InboxAsync(Recipient));
        Assert.Equal(receipt.MailId, mail.Id);
        Assert.Equal("reward", mail.Body);
        Assert.Empty(mail.SenderName);
        var attachment = Assert.Single(mail.Attachments);
        await AssertMailFailureAsync(MailFailure.NotFound, () => reopened.Mail.ClaimAsync(Stranger, attachment.Id).AsTask());
        Assert.False(Assert.Single((await reopened.Mail.InboxAsync(Recipient)).Single().Attachments).Claimed);
        await reopened.Mail.ClaimAsync(Recipient, attachment.Id);
        Assert.Equal(2, await fixture.StackAsync(Recipient, 1000));
        await AssertMailFailureAsync(MailFailure.AlreadyClaimed, () => reopened.Mail.ClaimAsync(Recipient, attachment.Id).AsTask());
        Assert.Equal(2, await fixture.StackAsync(Recipient, 1000));
    }

    [Fact]
    public async Task FailedSendAndFullBagClaim_RollBackEveryAssetAndClaimFlag()
    {
        using var fixture = new MailFixture();
        await AssertMailFailureAsync(MailFailure.InsufficientAssets, () => fixture.Mail.SendAsync(
            new MailBuilder(MailSource.FromCharacter(Sender), Recipient).WithText("bad", "bad")
                .AddGold(200).AddStackable(1000, 1).Build()).AsTask());
        Assert.Equal(1000, fixture.Gold(Sender));
        Assert.Empty(await fixture.Mail.InboxAsync(Recipient));

        await fixture.SeedStackAsync(Recipient, 121, 1000, 999, 3);
        await fixture.SeedStackAsync(Recipient, 122, 1000, 1000, 3);
        for (var slot = 3; slot <= 8; slot++)
            await fixture.SeedStackAsync(Recipient, slot, 4000, 1, 2);
        var id = (await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromSystem(), Recipient)
            .WithText("full", "full").AddStackable(1000, 2).Build())).MailId;
        var attachment = Assert.Single((await fixture.Mail.ReadAsync(Recipient, id)).Attachments);
        await AssertMailFailureAsync(MailFailure.NoCapacity, () => fixture.Mail.ClaimAsync(Recipient, attachment.Id).AsTask());
        Assert.Equal(1999, await fixture.StackAsync(Recipient, 1000));
        Assert.False(Assert.Single((await fixture.Mail.InboxAsync(Recipient)).Single().Attachments).Claimed);
    }

    [Fact]
    public async Task GoldCapRefusalRollsBackClaimAndReadDeleteExpirePersist()
    {
        using var fixture = new MailFixture();
        fixture.Sql("UPDATE character_stats SET carried_gold=2000 WHERE character_id=2");
        var goldId = (await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromSystem(), Recipient)
            .WithText("gold", "gold").AddGold(20).Build())).MailId;
        var goldAttachment = Assert.Single((await fixture.Mail.InboxAsync(Recipient)).Single().Attachments).Id;
        await AssertMailFailureAsync(MailFailure.UnclaimedAttachments, () => fixture.Mail.DeleteAsync(Recipient, goldId).AsTask());
        await AssertMailFailureAsync(MailFailure.GoldLimit, () => fixture.Mail.ClaimAsync(Recipient, goldAttachment).AsTask());
        Assert.Equal(2000, fixture.Gold(Recipient));
        Assert.False(Assert.Single((await fixture.Mail.InboxAsync(Recipient)).Single().Attachments).Claimed);

        fixture.Sql("UPDATE character_stats SET carried_gold=1000 WHERE character_id=2");
        var read = await fixture.Mail.ReadAsync(Recipient, goldId);
        Assert.True(read.Read);
        await fixture.Mail.ClaimAsync(Recipient, goldAttachment);
        await fixture.Mail.DeleteAsync(Recipient, goldId);
        Assert.Empty(await fixture.Mail.InboxAsync(Recipient));

        var expires = fixture.Clock.GetUtcNow().AddMinutes(1);
        var expiring = (await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromSystem(), Recipient)
            .WithText("short", "short").ExpiresAt(expires).AddGold(7).Build())).MailId;
        Assert.Single(await fixture.Mail.InboxAsync(Recipient));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(await fixture.Mail.InboxAsync(Recipient));
        await AssertMailFailureAsync(MailFailure.Expired, () => fixture.Mail.ReadAsync(Recipient, expiring).AsTask());
        Assert.Equal(1020, fixture.Gold(Recipient));
        using var reopened = fixture.Reopen();
        Assert.Empty(await reopened.Mail.InboxAsync(Recipient));
    }

    [Fact]
    public async Task ConcurrentClaimCreditsAttachmentAtMostOnce()
    {
        using var fixture = new MailFixture();
        var id = (await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromSystem(), Recipient)
            .WithText("race", "race").AddGold(5).Build())).MailId;
        var attachment = Assert.Single((await fixture.Mail.InboxAsync(Recipient)).Single().Attachments).Id;
        using var another = fixture.Reopen();
        var results = await Task.WhenAll(
            Task.Run(() => AttemptAsync(() => fixture.Mail.ClaimAsync(Recipient, attachment).AsTask())),
            Task.Run(() => AttemptAsync(() => another.Mail.ClaimAsync(Recipient, attachment).AsTask())));
        Assert.Equal(1, results.Count(x => x is null));
        Assert.Equal(1005, fixture.Gold(Recipient));
        Assert.True(Assert.Single((await fixture.Mail.ReadAsync(Recipient, id)).Attachments).Claimed);
    }

    [Fact]
    public async Task SendRulesRejectRestrictedEquipmentAndCrossAccountBoundStacks()
    {
        using var fixture = new MailFixture();
        var uid = fixture.SeedEquipment(Sender, 9, 3001);
        await AssertMailFailureAsync(MailFailure.NotSendable, () => fixture.Mail.SendAsync(new MailBuilder(
            MailSource.FromCharacter(Sender), Recipient).AddEquipment(new ItemIdentity(uid)).Build()).AsTask());
        Assert.Equal(1, fixture.EquipmentSlots(Sender, uid));
        await fixture.SeedStackAsync(Sender, 121, 1001, 2, 3);
        fixture.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'other'); UPDATE characters SET account_id=2 WHERE character_id=3");
        await AssertMailFailureAsync(MailFailure.InsufficientAssets, () => fixture.Mail.SendAsync(new MailBuilder(
            MailSource.FromCharacter(Sender), Stranger).AddStackable(1001, 1).Build()).AsTask());
        Assert.Equal(2, await fixture.StackAsync(Sender, 1001));
        await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromCharacter(Sender), Recipient).AddStackable(1001, 1).Build());
        Assert.Equal(1, await fixture.StackAsync(Sender, 1001));
    }

    [Fact]
    public async Task AccountMaterialAttachmentsUseAccountBalanceAndRollbackWithMail()
    {
        using var fixture = new MailFixture();
        await fixture.SeedShared(3037, 10, 1);
        var sent = await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromCharacter(Sender), Recipient)
            .AddStackable(3037, 3).Build());
        Assert.Equal(7, fixture.SharedCount(3037, 1));
        Assert.Equal(BusinessInventorySpace.SharedMaterial, Assert.Single(sent.Changes).Slot!.Value.Space);
        var attachment = Assert.Single((await fixture.Mail.ReadAsync(Recipient, sent.MailId)).Attachments);
        var claimed = await fixture.Mail.ClaimAsync(Recipient, attachment.Id);
        Assert.Equal(BusinessInventorySpace.SharedMaterial, Assert.Single(claimed.Changes).Slot!.Value.Space);
        Assert.Equal(10, fixture.SharedCount(3037, 1));
        Assert.Equal(0, await fixture.TotalStackAsync(3037));
        await AssertMailFailureAsync(MailFailure.InsufficientAssets, () => fixture.Mail.SendAsync(
            new MailBuilder(MailSource.FromCharacter(Sender), Recipient).AddStackable(3037, 2).AddGold(1001).Build()).AsTask());
        Assert.Equal(10, fixture.SharedCount(3037, 1));
        var reward = await fixture.Mail.SendAsync(new MailBuilder(MailSource.FromSystem(), Recipient).AddStackable(3037, 4).Build());
        await fixture.Mail.ClaimAsync(Recipient, Assert.Single((await fixture.Mail.ReadAsync(Recipient, reward.MailId)).Attachments).Id);
        Assert.Equal(14, fixture.SharedCount(3037, 1));
        Assert.Equal(0, await fixture.TotalStackAsync(3037));
    }

    [Fact]
    public async Task InvalidTemplateAndDeletedRecipientDoNotCreateMailOrDebit()
    {
        using var fixture = new MailFixture();
        await AssertMailFailureAsync(MailFailure.InvalidItem, () => fixture.Mail.SendAsync(new MailBuilder(
            MailSource.FromCharacter(Sender), Recipient).AddGold(5).AddStackable(int.MaxValue, 1).Build()).AsTask());
        Assert.Equal(1000, fixture.Gold(Sender));
        fixture.Sql("UPDATE characters SET delete_flag=1 WHERE character_id=2");
        await AssertMailFailureAsync(MailFailure.CharacterNotFound, () => fixture.Mail.SendAsync(new MailBuilder(
            MailSource.FromSystem(), Recipient).AddGold(5).Build()).AsTask());
        Assert.Equal(0, fixture.InboxRows(Recipient));
    }

    [Fact]
    public async Task DatabaseFailureAndCaughtRefusalCannotCommitPartialAssets()
    {
        using var fixture = new MailFixture();
        await fixture.SeedStackAsync(Sender, 121, 1000, 4, 3);
        fixture.Sql("CREATE TRIGGER fail_mail BEFORE INSERT ON mailbox_messages BEGIN SELECT RAISE(ABORT,'mail test failure'); END");
        await Assert.ThrowsAsync<SqliteException>(() => fixture.Mail.SendAsync(new MailBuilder(MailSource.FromCharacter(Sender), Recipient)
            .AddGold(20).AddStackable(1000, 2).Build()).AsTask());
        Assert.Equal(1000, fixture.Gold(Sender));
        Assert.Equal(4, await fixture.StackAsync(Sender, 1000));
        Assert.Equal(0, fixture.InboxRows(Recipient));
        fixture.Sql("DROP TRIGGER fail_mail");
        await fixture.CaughtFailureCannotCommitAsync();
        Assert.Equal(1000, fixture.Gold(Sender));
    }

    private static async Task<Exception?> AttemptAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }

    private static async Task AssertMailFailureAsync(MailFailure expected, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<MailException>(action);
        Assert.True(expected == exception.Reason, $"Expected {expected}, got {exception.Reason}: {exception.Message}");
    }

    private sealed class MailFixture : IDisposable
    {
        private readonly string directory;
        private readonly string path;
        private readonly ServiceProvider provider;
        private readonly bool ownsDirectory;
        private readonly Action<IServiceCollection>? configure;
        public IServiceProvider Services => provider;
        public AdvanceableTimeProvider Clock { get; }
        public IMailboxQueryDomainService Query => provider.GetRequiredService<IMailboxQueryDomainService>();
        public IMailboxDomainService Mail => provider.GetRequiredService<IMailboxDomainService>();

        public MailFixture(Action<IServiceCollection>? configure = null) : this(Path.Combine(Path.GetTempPath(), "dfo-mail-core-" + Guid.NewGuid().ToString("N")),
            new AdvanceableTimeProvider(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero)), true, configure)
        {
            Sql("INSERT INTO accounts(account_id,m_id,cera) VALUES(1,'mail-test',0); " +
                "INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES" +
                "(1,1,'sender',12,2,0),(2,1,'receiver',12,2,1),(3,1,'stranger',12,2,2); " +
                "INSERT INTO character_stats(character_id,carried_gold) VALUES(1,1000),(2,1000),(3,1000)");
        }

        private MailFixture(string directory, AdvanceableTimeProvider clock, bool ownsDirectory, Action<IServiceCollection>? configure)
        {
            this.configure = configure;
            this.directory = directory;
            this.ownsDirectory = ownsDirectory;
            path = Path.Combine(directory, "mail.db");
            Clock = clock;
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Database:Path"] = path, ["Game:DailyResetTime"] = "06:00:00" }).Build();
            var services = new ServiceCollection().AddLogging();
            new InfrastructureModule().ConfigureServices(services, config);
            new PersistenceModule().ConfigureServices(services, config);
            new ApplicationModule().ConfigureServices(services, config);
            services.AddSingleton<IItemPlacementDefinitionSource>(new MailPlacements());
            services.AddSingleton<IMailItemDefinitionSource>(new MailDefinitions());
            services.AddSingleton<IGoldCarryLimitSource>(new FixedGoldCarryLimit());
            services.AddSingleton<TimeProvider>(Clock);
            configure?.Invoke(services);
            provider = services.BuildServiceProvider();
            _ = provider.GetRequiredService<SqliteDatabase>();
        }

        public async Task CaughtFailureCannotCommitAsync()
        {
            var scope = provider.GetRequiredService<DomainTransactionScope>();
            await using var tx = scope.Begin();
            await AssertMailFailureAsync(MailFailure.InsufficientAssets, () => Mail.SendAsync(new MailBuilder(
                MailSource.FromCharacter(Sender), Recipient).AddGold(10).AddStackable(1000, 1000).Build()).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(default).AsTask());
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

        public MailFixture Reopen() => new(directory, Clock, false, configure);

        public async Task SeedStackAsync(CharacterId owner, int slot, int itemId, int count, byte kind,
            InventoryContainerType container = InventoryContainerType.Main)
        {
            var scope = (DomainTransactionScope)provider.GetRequiredService<IDomainTransactionAccessor>();
            await using var transaction = scope.Begin();
            var store = provider.GetRequiredService<IItemDomainStore>();
            store.SessionIn(scope.Current!).Write(ContainerId.ForCharacter(owner, container),
                new StoredItem(slot, StackableItemCore.Create(kind, itemId, count))
                    { WorldClass = ItemWorldClass.Interchangeable });
            await transaction.CommitAsync(default);
        }

        public int SeedEquipment(CharacterId owner, int slot, int itemId)
        {
            var core = new byte[82];
            core[0] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
            core[21] = 0x7f;
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO items(item_core,created_at,updated_at)
                VALUES(@core,'2020-01-02 03:04:05','2020-01-03 04:05:06');
                INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('character',@owner,0,@slot,'world',last_insert_rowid(),last_insert_rowid(),1);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("@core", core);
            command.Parameters.AddWithValue("@owner", owner.Value);
            command.Parameters.AddWithValue("@slot", slot);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public string EquipmentCore(int uid)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT hex(item_core) FROM items WHERE item_uid=@uid";
            command.Parameters.AddWithValue("@uid", uid);
            return (string)command.ExecuteScalar()!;
        }
        public int EquipmentRows(int uid) => Scalar($"SELECT COUNT(*) FROM items WHERE item_uid={uid}");
        public int EquipmentSlots(CharacterId owner, int uid) => Scalar(
            $"SELECT COUNT(*) FROM item_slots WHERE owner_scope='character' AND owner_id={owner.Value} AND core_uid={uid}");

        public async Task<int> StackAsync(CharacterId owner, int itemId)
        {
            var store = provider.GetRequiredService<IItemDomainStore>();
            var tx = new DomainTransaction();
            try
            {
                var rows = await store.SessionIn(tx).ReadAllAsync(ContainerId.ForCharacter(owner, InventoryContainerType.Main), default);
                return rows.Where(x => x.Core.ItemId == itemId).Sum(x => x.Core.Value);
            }
            finally { await tx.RollbackAsync(); }
        }

        public async Task<int> TotalStackAsync(int itemId) =>
            await StackAsync(Sender, itemId) + await StackAsync(Recipient, itemId);

        public int Gold(CharacterId owner) => Scalar($"SELECT carried_gold FROM character_stats WHERE character_id={owner.Value}");
        public int InboxRows(CharacterId owner) => Scalar($"SELECT COUNT(*) FROM mailbox_recipients WHERE character_id={owner.Value}");
        public int Scalar(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }
        public void Sql(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        public void Dispose()
        {
            provider.Dispose();
            if (!ownsDirectory) return;
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }

        private sealed class MailDefinitions : IMailItemDefinitionSource
        {
            public ValueTask<MailItemDefinition?> GetAsync(int itemId, CancellationToken ct) =>
                ValueTask.FromResult<MailItemDefinition?>(new(itemId is 2000 or 3001 ? MailItemAttachType.Trade : itemId == 1001 ? MailItemAttachType.Account : MailItemAttachType.Free, "test declaration", null));
        }
        private sealed class MailPlacements : IItemPlacementDefinitionSource
        {
            public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken ct) => itemId == int.MaxValue
                ? ValueTask.FromResult<ItemPlacement?>(null) : itemId == 1001
                ? ValueTask.FromResult<ItemPlacement?>(new ItemPlacement(3, 1000, new BusinessSlotRange(BusinessInventorySpace.Material, 0, 55)))
                : itemId == 3037
                ? ValueTask.FromResult<ItemPlacement?>(new ItemPlacement(11, 1000, new BusinessSlotRange(BusinessInventorySpace.Material, 0, 1)))
                : new NpcShopTestContent().GetAsync(itemId, ct);
        }

        private sealed class FixedGoldCarryLimit : IGoldCarryLimitSource
        {
            public int BaseLimitFor(int characterLevel) => 2000;
        }
    }
}
