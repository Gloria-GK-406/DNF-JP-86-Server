using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dfo.Server.Persistence.Tests.Inventory.Lifecycle;

public sealed class MailCustodyTests
{
    private static readonly ContainerId Bag = new("character:60:Main");
    private static readonly ContainerId RecipientBag = new("character:61:Main");
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 1, 0, 0, TimeSpan.Zero);
    private static ItemSnapshot Sword() => new() { ItemId = 123, LegacyKind = 1, Type = ItemTraits.Equipment,
        Count = 1, InstanceValue = 4321, Durability = 31, TailUnknown3 = 93 };

    [Fact]
    public async Task MailAttachmentIsRealCustodyAndPreservesUidAndAllBytes()
    {
        using var db = new Database();
        var tx = new DomainTransaction(); var items = db.Store.SessionIn(tx);
        var created = await items.CreateAsync(Sword(), default);
        await items.PutAsync(Bag, 9, created, default);
        var reference = (await items.GetAsync(Bag, 9, default))!;
        var mailed = await items.TakeAsync(reference, null, default);
        var attachment = await db.Attach(tx, mailed);
        await ((IMailItemCustodySession)items).AcceptMailAttachmentAsync(attachment, [mailed], default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => items.UpdateAsync(reference, x => x with { Durability = 0 }, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => items.PutAsync(Bag, 10, mailed, default).AsTask());
        await tx.CommitAsync(default);
        var claim = new DomainTransaction(); items = db.Store.SessionIn(claim);
        var received = Assert.Single(await ((IMailItemCustodySession)items).TakeMailAttachmentAsync(new(61), attachment, Now, default));
        Assert.Equal(mailed.ItemUid, received.ItemUid);
        Assert.Equal(mailed.Snapshot.ToCore(), received.Snapshot.ToCore());
        await items.PutAsync(RecipientBag, 9, received, default);
        await claim.CommitAsync(default);
    }

    [Fact]
    public async Task ForgedRecordCannotReleaseCustodyAndARecordCannotBeReceivedTwice()
    {
        using var db = new Database(); var tx = new DomainTransaction(); var items = db.Store.SessionIn(tx);
        var record = await items.CreateAsync(Sword(), default); var id = await db.Attach(tx, record);
        var mail = (IMailItemCustodySession)items;
        await Assert.ThrowsAsync<InvalidOperationException>(() => mail.AcceptMailAttachmentAsync(id, [new ItemRecord(record.Snapshot)], default).AsTask());
        await mail.AcceptMailAttachmentAsync(id, [record], default);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => mail.AcceptMailAttachmentAsync(id, [record], default).AsTask());
        await tx.CommitAsync(default);
    }

    [Fact]
    public async Task MailRefusesStrangersExpiredAndDeletedMessagesWithoutIssuingRecords()
    {
        using var db = new Database(); var seed = new DomainTransaction(); var items = db.Store.SessionIn(seed);
        var record = await items.CreateAsync(Sword(), default); var id = await db.Attach(seed, record);
        await ((IMailItemCustodySession)items).AcceptMailAttachmentAsync(id, [record], default); await seed.CommitAsync(default);
        var tx = new DomainTransaction(); var mail = (IMailItemCustodySession)db.Store.SessionIn(tx);
        Assert.Equal(MailFailure.NotFound, (await Assert.ThrowsAsync<MailException>(() => mail.TakeMailAttachmentAsync(new(62), id, Now, default).AsTask())).Reason);
        Assert.Equal(MailFailure.Expired, (await Assert.ThrowsAsync<MailException>(() => mail.TakeMailAttachmentAsync(new(61), id, Now.AddMonths(2), default).AsTask())).Reason);
        await db.Execute(tx, "UPDATE mailbox_recipients SET deleted_flag=1");
        Assert.Equal(MailFailure.NotFound, (await Assert.ThrowsAsync<MailException>(() => mail.TakeMailAttachmentAsync(new(61), id, Now, default).AsTask())).Reason);
        await tx.RollbackAsync();
        var valid = new DomainTransaction(); items = db.Store.SessionIn(valid);
        await items.PutAsync(RecipientBag, 9, Assert.Single(await ((IMailItemCustodySession)items).TakeMailAttachmentAsync(new(61), id, Now, default)), default);
        await valid.CommitAsync(default);
    }

    [Fact]
    public async Task UnplacedClaimRollsBackClaimFlagAndWorldRowAndCanBeRetried()
    {
        using var db = new Database(); var seed = new DomainTransaction(); var items = db.Store.SessionIn(seed);
        var record = await items.CreateAsync(Sword(), default); var id = await db.Attach(seed, record);
        await ((IMailItemCustodySession)items).AcceptMailAttachmentAsync(id, [record], default); await seed.CommitAsync(default);
        var bad = new DomainTransaction(); items = db.Store.SessionIn(bad);
        _ = await ((IMailItemCustodySession)items).TakeMailAttachmentAsync(new(61), id, Now, default);
        await items.ApplyDecidedWritesAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bad.CommitAsync(default).AsTask());
        var retry = new DomainTransaction(); items = db.Store.SessionIn(retry);
        var reclaimed = Assert.Single(await ((IMailItemCustodySession)items).TakeMailAttachmentAsync(new(61), id, Now, default));
        Assert.Equal(record.ItemUid, reclaimed.ItemUid);
        await items.PutAsync(RecipientBag, 9, reclaimed, default); await retry.CommitAsync(default);
        var duplicate = new DomainTransaction(); items = db.Store.SessionIn(duplicate);
        Assert.Equal(MailFailure.AlreadyClaimed, (await Assert.ThrowsAsync<MailException>(() =>
            ((IMailItemCustodySession)items).TakeMailAttachmentAsync(new(61), id, Now, default).AsTask())).Reason);
        await duplicate.RollbackAsync();
    }

    [Fact]
    public async Task MailPilesPreserveDifferentUnknownFieldsAndQuantitiesWithoutWorldIdentities()
    {
        using var db = new Database(); var tx = new DomainTransaction(); var items = db.Store.SessionIn(tx);
        var pile = new ItemSnapshot { ItemId = 999, Count = 4, IsPile = true, LegacyKind = 3, Type = ItemTraits.Material,
            TailUnknown2 = 17, ExpireTime = 12345 };
        var first = await items.CreateAsync(pile, default);
        var second = await items.CreateAsync(pile with { Count = 6, TailUnknown2 = 23 }, default);
        var id = await db.Attach(tx, first, second);
        await ((IMailItemCustodySession)items).AcceptMailAttachmentAsync(id, [first, second], default);
        await tx.CommitAsync(default);
        var claim = new DomainTransaction(); items = db.Store.SessionIn(claim);
        var received = await ((IMailItemCustodySession)items).TakeMailAttachmentAsync(new(61), id, Now, default);
        Assert.Equal(2, received.Count); Assert.Equal(10, received.Sum(value => value.Count));
        Assert.All(received, value => Assert.Null(value.ItemUid));
        Assert.Equal(pile.ToCore(), received[0].Snapshot.ToCore());
        Assert.Equal((byte)23, received[1].Snapshot.TailUnknown2);
        Assert.False(received[0].Snapshot.CanStackWith(received[1].Snapshot));
        await items.PutAsync(RecipientBag, 9, received[0], default);
        await items.PutAsync(RecipientBag, 10, received[1], default);
        await claim.CommitAsync(default);
    }

    [Fact]
    public async Task ExistingStackAttachmentWithoutFragmentPayloadRemainsClaimable()
    {
        using var db = new Database(); var seed = new DomainTransaction(); var items = db.Store.SessionIn(seed);
        var record = await items.CreateAsync(new ItemSnapshot { ItemId = 999, Count = 7, IsPile = true,
            LegacyKind = 3, Type = ItemTraits.Material, TailUnknown3 = 43 }, default);
        var id = await db.Attach(seed, record);
        await ((IMailItemCustodySession)items).AcceptMailAttachmentAsync(id, [record], default); await seed.CommitAsync(default);
        var migration = new DomainTransaction(); _ = db.Store.SessionIn(migration);
        await db.Execute(migration, "UPDATE mailbox_attachments SET detail_json=''"); await migration.CommitAsync(default);
        var tx = new DomainTransaction(); items = db.Store.SessionIn(tx);
        var loaded = Assert.Single(await ((IMailItemCustodySession)items).TakeMailAttachmentAsync(new(61), id, Now, default));
        Assert.Equal(record.Snapshot.ToCore(), loaded.Snapshot.ToCore());
        await items.PutAsync(RecipientBag, 9, loaded, default); await tx.CommitAsync(default);
    }

    [Fact]
    public async Task AlteringAcceptedAttachmentBeforeCommitCannotOrphanItsEquipment()
    {
        using var db = new Database(); var tx = new DomainTransaction(); var items = db.Store.SessionIn(tx);
        var record = await items.CreateAsync(Sword(), default); var id = await db.Attach(tx, record);
        await ((IMailItemCustodySession)items).AcceptMailAttachmentAsync(id, [record], default);
        await db.Execute(tx, "UPDATE mailbox_attachments SET claimed_flag=1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(default).AsTask());
    }

    [Theory]
    [InlineData("avatar_color1", -1L)]
    [InlineData("avatar_color1", 65536L)]
    [InlineData("avatar_color1", long.MaxValue)]
    [InlineData("avatar_color2", -1L)]
    [InlineData("avatar_color2", 65536L)]
    [InlineData("avatar_color2", long.MaxValue)]
    public async Task MailClaimRejectsMalformedAvatarColorsAndRetainsCustody(string column, long color)
    {
        using var db = new Database();
        var seed = new DomainTransaction();
        var claim = new DomainTransaction();
        var retry = new DomainTransaction();
        try
        {
            var items = db.Store.SessionIn(seed);
            var record = await items.CreateAsync(Sword(), default);
            var id = await db.Attach(seed, record);
            await ((IMailItemCustodySession)items).AcceptMailAttachmentAsync(id, [record], default);
            await seed.CommitAsync(default);
            items = db.Store.SessionIn(claim);
            // Corrupt a supported equipment attachment with malformed persisted avatar columns.
            // Normal mail does not admit avatars, but its world-row decoder still must reject bad data.
            await db.Execute(claim, $"UPDATE items SET avatar_expire_date=0,avatar_color1=1,avatar_color2=2,{column}={color}");
            await Assert.ThrowsAsync<InvalidDataException>(() => ((IMailItemCustodySession)items)
            .TakeMailAttachmentAsync(new(61), id, Now, default).AsTask());
            await claim.RollbackAsync();
            items = db.Store.SessionIn(retry);
            var restored = Assert.Single(await ((IMailItemCustodySession)items)
            .TakeMailAttachmentAsync(new(61), id, Now, default));
            Assert.Equal(record.ItemUid, restored.ItemUid);
            Assert.Equal(record.Snapshot.ToCore(), restored.Snapshot.ToCore());
            Assert.Null(restored.Snapshot.AvatarDetail);
            await items.PutAsync(RecipientBag, 9, restored, default);
            await retry.CommitAsync(default);
        }
        finally
        {
            await seed.RollbackAsync();
            await claim.RollbackAsync();
            await retry.RollbackAsync();
        }
    }

    private sealed class Database : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"mail-custody-{Guid.NewGuid():N}.db");
        public SqliteItemDomainStore Store { get; }
        public Database()
        {
            using var connection = new SqliteConnection($"Data Source={path}"); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = """
                CREATE TABLE items(item_uid INTEGER PRIMARY KEY AUTOINCREMENT,item_core BLOB NOT NULL,updated_at TEXT,
                    avatar_expire_date INTEGER,avatar_clear_id INTEGER,avatar_jewel_socket BLOB,avatar_color1 INTEGER,avatar_color2 INTEGER,avatar_delete_date INTEGER,
                    creature_field04 INTEGER,creature_mode_flag INTEGER,creature_progress_value INTEGER,creature_mode1_field0a INTEGER,creature_mode1_field0b INTEGER,creature_field_after_value INTEGER,creature_text BLOB,creature_tail_flag INTEGER, is_mirror INTEGER NOT NULL DEFAULT 0 CHECK(is_mirror IN (0,1)), mirror_source_uid INTEGER, appearance_item_id INTEGER CHECK(appearance_item_id > 0));
                CREATE TABLE item_slots(owner_scope TEXT,owner_id INTEGER,list_type INTEGER,slot_index INTEGER,kind TEXT,id INTEGER,core_uid INTEGER,count INTEGER,UNIQUE(owner_scope,owner_id,list_type,slot_index));
                CREATE TABLE characters(character_id INTEGER PRIMARY KEY,delete_flag INTEGER NOT NULL DEFAULT 0);
                INSERT INTO characters(character_id) VALUES(60),(61),(62);
                CREATE TABLE mailbox_messages(message_id INTEGER PRIMARY KEY,receiver_character_id INTEGER,unlimited_flag INTEGER,expire_at TEXT);
                CREATE TABLE mailbox_recipients(message_id INTEGER,character_id INTEGER,folder INTEGER DEFAULT 0,saved_flag INTEGER DEFAULT 0,deleted_flag INTEGER DEFAULT 0);
                CREATE TABLE mailbox_attachments(attachment_id INTEGER PRIMARY KEY AUTOINCREMENT,message_id INTEGER,asset_kind TEXT,source_item_uid INTEGER,item_template_id INTEGER,item_count INTEGER,item_core BLOB,detail_json TEXT DEFAULT '',claimed_flag INTEGER DEFAULT 0);
                CREATE UNIQUE INDEX pending_uid ON mailbox_attachments(source_item_uid) WHERE asset_kind='equipment' AND claimed_flag=0 AND source_item_uid>0;
                INSERT INTO mailbox_messages VALUES(1,61,0,'2026-11-01T00:00:00.0000000+00:00');
                INSERT INTO mailbox_recipients(message_id,character_id) VALUES(1,61);
                """;
            command.ExecuteNonQuery(); Store = new(path);
        }
        public async ValueTask<long> Attach(DomainTransaction tx, params ItemRecord[] records)
        {
            var record = records[0]; var count = records.Sum(value => value.Count);
            await using var command = await Command(tx);
            command.CommandText = "INSERT INTO mailbox_attachments(message_id,asset_kind,source_item_uid,item_template_id,item_count,item_core) VALUES(1,@kind,@uid,@item,@count,@core) RETURNING attachment_id";
            command.Parameters.AddWithValue("@kind", record.IsPile ? "stackable" : "equipment");
            command.Parameters.AddWithValue("@uid", record.ItemUid?.Value ?? 0);
            command.Parameters.AddWithValue("@item", record.ItemId);
            command.Parameters.AddWithValue("@count", count);
            command.Parameters.AddWithValue("@core", InventoryItemCore.Encode((record.Snapshot with { Count = count }).ToCore()));
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        public async ValueTask Execute(DomainTransaction tx, string sql)
        {
            await using var command = await Command(tx); command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
        public ValueTask<SqliteCommand> Command(DomainTransaction tx) =>
            tx.Enlist<SqliteDatabaseParticipant>(Store.StoreIdentity, () => throw new InvalidOperationException()).CommandAsync(default);
        public void Dispose() { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
