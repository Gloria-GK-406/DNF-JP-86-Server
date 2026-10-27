using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Mail;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Mail;

public sealed class SqliteMailboxRepositoryTests
{
    [Fact]
    public async Task NewMailReloadsWithIndependentAttachmentIdsAndClaimSettlesOnce()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new MailboxFixture();
            var recipient = new CharacterId(27);
            var now = new DateTimeOffset(2026, 9, 24, 1, 2, 3, TimeSpan.Zero);
            long id;
            ItemIdentity equipmentUid;
            await using (var lease = fixture.Scope.Begin())
            {
                await fixture.Repository.AcquireWriteAsync(default);
                var items = fixture.Items.SessionIn(fixture.Scope.Current!);
                var equipment = await items.CreateAsync(ItemSnapshot.FromCore(Core(1, 5000, 1), false), default);
                var stack = await items.CreateAsync(ItemSnapshot.FromCore(Core(2, 2002, 7), true), default);
                equipmentUid = equipment.ItemUid!.Value;
                var mail = new MailBuilder(MailSource.FromSystem(), recipient)
                    .WithText("Reward", "Body").ExpiresAt(now.AddDays(3))
                    .AddEquipment(equipmentUid).AddStackable(2002, 7).AddGold(345).Build();
                id = await fixture.Repository.InsertAsync(mail, "system", now,
                    [new(mail.Attachments[0], [equipment]), new(mail.Attachments[1], [stack]), new(mail.Attachments[2])], default);
                await lease.CommitAsync(default);
            }

            var reopened = fixture.Reopen();
            var found = Assert.Single(await reopened.Repository.ReadInboxAsync(recipient, default));
            Assert.Equal(id, found.Id);
            Assert.Equal(now, found.CreatedAt);
            Assert.Equal(now.AddDays(3), found.ExpiresAt);
            Assert.Equal("Body", found.Body);
            Assert.Equal(3, found.Attachments.Length);
            Assert.Equal(3, found.Attachments.Select(x => x.Id).Distinct().Count());
            Assert.IsType<MailAsset.Equipment>(found.Attachments[0].Asset);
            Assert.Equal(equipmentUid, ((MailAsset.Equipment)found.Attachments[0].Asset).Identity);
            Assert.True(await reopened.Repository.HoldsEquipmentAsync(equipmentUid, default));
            var goldAttachment = found.Attachments[2].Id;
            Assert.NotEqual(id, goldAttachment);
            Assert.Equal(345, ((MailAsset.Gold)found.Attachments[2].Asset).Amount);
            Assert.Equal(82L, fixture.Scalar($"SELECT length(item_core) FROM mailbox_attachments WHERE attachment_id={found.Attachments[1].Id}"));
            Assert.Equal(2L, fixture.Scalar($"SELECT item_type FROM mailbox_attachments WHERE attachment_id={found.Attachments[1].Id}"));
            Assert.Equal(7L, fixture.Scalar($"SELECT item_count FROM mailbox_attachments WHERE attachment_id={found.Attachments[1].Id}"));
            Assert.Null(await reopened.Repository.FindByAttachmentAsync(new CharacterId(28), goldAttachment, default));

            await using (var lease = reopened.Scope.Begin())
            {
                await reopened.Repository.AcquireWriteAsync(default);
                Assert.True(await reopened.Repository.MarkClaimedAsync(goldAttachment, default));
                Assert.False(await reopened.Repository.MarkClaimedAsync(goldAttachment, default));
                await lease.CommitAsync(default);
            }
            Assert.True((await fixture.Reopen().Repository.FindAsync(recipient, id, default))!
                .Attachments[2].Claimed);
            Assert.Equal(1L, fixture.Scalar($"SELECT received_gold_flag FROM mailbox_recipients WHERE message_id={id}"));
        });
    }

    [Fact]
    public async Task OldRowsAreRetainedGoldBecomesClaimableAndUnprovenEquipmentStaysUnsupported()
    {
        using var fixture = new MailboxFixture();
        fixture.Execute("""
            INSERT INTO mailbox_messages(message_id,receiver_character_id,sender_name,title,body,gold)
            VALUES(10,27,'old','old-title','old-body',99);
            INSERT INTO mailbox_recipients(message_id,character_id,received_gold_flag)
            VALUES(10,27,0);
            INSERT INTO mailbox_attachments(message_id,item_template_id,source_item_uid,item_core)
            VALUES(10,500,12345,X'0102');
            """);
        var reopened = fixture.Reopen();
        var mail = (await reopened.Repository.FindAsync(new CharacterId(27), 10, default))!;
        Assert.Equal("old-body", mail.Body);
        Assert.Null(mail.ExpiresAt);
        Assert.Equal(2, mail.Attachments.Length);
        Assert.Contains(mail.Attachments, x => x.LegacyUnsupported);
        var gold = Assert.Single(mail.Attachments, x => x.Asset is MailAsset.Gold);
        Assert.False(gold.LegacyUnsupported);
        Assert.Equal(99, ((MailAsset.Gold)gold.Asset).Amount);
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE message_id=10 AND asset_kind='gold'"));
        fixture.Reopen();
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE message_id=10 AND asset_kind='gold'"));
        Assert.Equal(12345L, fixture.Scalar("SELECT source_item_uid FROM mailbox_attachments WHERE message_id=10 AND asset_kind='legacy'"));
        Assert.True(await reopened.Repository.HoldsEquipmentAsync(new ItemIdentity(12345), default));
    }

    [Fact]
    public async Task AlreadyReceivedLegacyGoldMigratesAsClaimed()
    {
        await TestGameplay.RunAsync(async () =>
        {
        using var fixture = new MailboxFixture();
        fixture.Execute("""
            INSERT INTO mailbox_messages(message_id,receiver_character_id,gold) VALUES(11,27,50);
            INSERT INTO mailbox_recipients(message_id,character_id,received_gold_flag) VALUES(11,27,1);
            """);
        var reopened = fixture.Reopen();
        var mail = (await reopened.Repository.FindAsync(new CharacterId(27), 11, default))!;
        var gold = Assert.Single(mail.Attachments);
        Assert.True(gold.Claimed);
        await using var lease = reopened.Scope.Begin();
        await reopened.Repository.AcquireWriteAsync(default);
        Assert.False(await reopened.Repository.MarkClaimedAsync(gold.Id, default));
        await lease.CommitAsync(default);
        });
    }

    [Fact]
    public async Task EarlierThreeTableShapeGainsColumnsWithoutDiscardingRows()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-mail-legacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "legacy.db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                connection.Open();
                using var seed = connection.CreateCommand();
                seed.CommandText = """
                    CREATE TABLE mailbox_messages(message_id INTEGER PRIMARY KEY,receiver_character_id INTEGER NOT NULL,
                        created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,gold INTEGER NOT NULL DEFAULT 0);
                    CREATE TABLE mailbox_recipients(recipient_id INTEGER PRIMARY KEY,message_id INTEGER NOT NULL,
                        character_id INTEGER NOT NULL,received_gold_flag INTEGER NOT NULL DEFAULT 0);
                    CREATE TABLE mailbox_attachments(attachment_id INTEGER PRIMARY KEY,message_id INTEGER NOT NULL,
                        item_template_id INTEGER NOT NULL);
                    INSERT INTO mailbox_messages(message_id,receiver_character_id,gold) VALUES(19,27,88);
                    INSERT INTO mailbox_recipients(message_id,character_id) VALUES(19,27);
                    INSERT INTO mailbox_attachments(message_id,item_template_id) VALUES(19,99);
                    """;
                seed.ExecuteNonQuery();
            }
            var scope = new DomainTransactionScope();
            var repo = new SqliteMailboxRepository(new SqliteDatabase(path, scope));
            var mail = (await repo.FindAsync(new CharacterId(27), 19, default))!;
            Assert.Equal(2, mail.Attachments.Length);
            Assert.Contains(mail.Attachments, x => x.LegacyUnsupported);
            Assert.Contains(mail.Attachments, x => x.Asset is MailAsset.Gold g && g.Amount == 88);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MultipleGoldAttachmentsSetLegacyGoldFlagOnlyAfterAllAreClaimed()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new MailboxFixture();
            var mail = new MailBuilder(MailSource.FromSystem(), new CharacterId(27))
                .WithText("gold", "body").AddGold(10).AddGold(20).Build();
            long id;
            await using (var lease = fixture.Scope.Begin())
            {
                await fixture.Repository.AcquireWriteAsync(default);
                id = await fixture.Repository.InsertAsync(mail, "system", DateTimeOffset.UtcNow,
                    mail.Attachments.Select(x => new PreparedMailAttachment(x)).ToArray(), default);
                await lease.CommitAsync(default);
            }
            var rows = (await fixture.Repository.FindAsync(new CharacterId(27), id, default))!.Attachments;
            await using (var lease = fixture.Scope.Begin())
            {
                await fixture.Repository.AcquireWriteAsync(default);
                Assert.True(await fixture.Repository.MarkClaimedAsync(rows[0].Id, default));
                await lease.CommitAsync(default);
            }
            Assert.Equal(0L, fixture.Scalar($"SELECT received_gold_flag FROM mailbox_recipients WHERE message_id={id}"));
            await using (var lease = fixture.Scope.Begin())
            {
                await fixture.Repository.AcquireWriteAsync(default);
                Assert.True(await fixture.Repository.MarkClaimedAsync(rows[1].Id, default));
                await lease.CommitAsync(default);
            }
            Assert.Equal(1L, fixture.Scalar($"SELECT received_gold_flag FROM mailbox_recipients WHERE message_id={id}"));
        });
    }

    private static InventoryItemCoreState Core(byte kind, int itemId, int count) =>
        InventoryItemCore.Decode(new byte[InventoryItemCore.Size]) with { ItemKind = kind, ItemId = itemId, Value = count };

    private sealed class MailboxFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"dfo-mail-{Guid.NewGuid():N}");
        private readonly string path;

        public MailboxFixture()
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "mail.db");
            Scope = new DomainTransactionScope();
            Items = new SqliteItemDomainStore(path);
            Repository = new SqliteMailboxRepository(new SqliteDatabase(path, Scope), Items, Scope);
        }

        public SqliteItemDomainStore Items { get; }
        public DomainTransactionScope Scope { get; }
        public SqliteMailboxRepository Repository { get; }

        public (DomainTransactionScope Scope, SqliteMailboxRepository Repository) Reopen()
        {
            var scope = new DomainTransactionScope();
            return (scope, new SqliteMailboxRepository(new SqliteDatabase(path, scope), new SqliteItemDomainStore(path), scope));
        }

        public void Execute(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public long Scalar(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
