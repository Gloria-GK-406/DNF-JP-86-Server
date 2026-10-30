using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Mail;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

/// <summary>Gold kept on the message before gold attachments becomes one claimable attachment.</summary>
public sealed class MailboxGoldMigrationTests
{
    [Fact]
    public async Task OldRowsAreRetainedGoldBecomesClaimableAndUnprovenEquipmentStaysUnsupported()
    {
        using var fixture = new LegacyMailbox();
        fixture.Execute("""
            INSERT INTO mailbox_messages(message_id,receiver_character_id,sender_name,title,body,gold)
            VALUES(10,27,'old','old-title','old-body',99);
            INSERT INTO mailbox_recipients(message_id,character_id,received_gold_flag)
            VALUES(10,27,0);
            INSERT INTO mailbox_attachments(message_id,item_template_id,source_item_uid,item_core)
            VALUES(10,500,12345,X'0102');
            """);
        var reopened = fixture.Migrate();
        var mail = (await reopened.Repository.FindAsync(new CharacterId(27), 10, default))!;
        Assert.Equal("old-body", mail.Body);
        Assert.Null(mail.ExpiresAt);
        Assert.Equal(2, mail.Attachments.Length);
        Assert.Contains(mail.Attachments, x => x.LegacyUnsupported);
        var gold = Assert.Single(mail.Attachments, x => x.Asset is MailAsset.Gold);
        Assert.False(gold.LegacyUnsupported);
        Assert.Equal(99, ((MailAsset.Gold)gold.Asset).Amount);
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE message_id=10 AND asset_kind='gold'"));
        fixture.Migrate();
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE message_id=10 AND asset_kind='gold'"));
        Assert.Equal(12345L, fixture.Scalar("SELECT source_item_uid FROM mailbox_attachments WHERE message_id=10 AND asset_kind='legacy'"));
        Assert.True(await reopened.Repository.HoldsEquipmentAsync(new ItemIdentity(12345), default));
        Assert.Equal(0L, fixture.LegacyGoldColumnCount);
    }

    [Fact]
    public async Task AlreadyReceivedLegacyGoldMigratesAsClaimed()
    {
        await TestGameplay.RunAsync(async () =>
        {
        using var fixture = new LegacyMailbox();
        fixture.Execute("""
            INSERT INTO mailbox_messages(message_id,receiver_character_id,gold) VALUES(11,27,50);
            INSERT INTO mailbox_recipients(message_id,character_id,received_gold_flag) VALUES(11,27,1);
            """);
        var reopened = fixture.Migrate();
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
            Unversioned.Migrate(path);
            var repo = new SqliteMailboxRepository(Unversioned.Open(path));
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

    /// <summary>An unversioned mailbox with the message-level gold columns it had before gold attachments.</summary>
    private sealed class LegacyMailbox : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"dfo-mail-{Guid.NewGuid():N}");
        private readonly string path;

        public LegacyMailbox()
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "mail.db");
            Unversioned.CreateUnstamped(path);
            Execute("""
                ALTER TABLE mailbox_messages ADD COLUMN gold INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE mailbox_recipients ADD COLUMN received_gold_flag INTEGER NOT NULL DEFAULT 0;
                """);
        }

        public long LegacyGoldColumnCount => Scalar("""
            SELECT (SELECT COUNT(*) FROM pragma_table_info('mailbox_messages') WHERE name='gold')
                 + (SELECT COUNT(*) FROM pragma_table_info('mailbox_recipients') WHERE name='received_gold_flag')
            """);

        public (DomainTransactionScope Scope, SqliteMailboxRepository Repository) Migrate()
        {
            Unversioned.Migrate(path);
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
