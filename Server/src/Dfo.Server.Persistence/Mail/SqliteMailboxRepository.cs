using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Mail;

/// <summary>Mailbox rows share the current use case's SQLite transaction with item and gold stores.</summary>
internal sealed class SqliteMailboxRepository(SqliteDatabase database, IItemDomainStore items,
    IDomainTransactionAccessor transactions) : IMailboxRepository
{
    private const string Visible = "r.character_id=@recipient AND r.folder=0 AND r.saved_flag=0 AND r.deleted_flag=0";

    public async ValueTask AcquireWriteAsync(CancellationToken cancellationToken)
    {
        // A deferred transaction that first reads cannot later be promoted safely if another
        // connection writes meanwhile. Acquire SQLite's writer reservation before any facts read.
        await database.ExecuteAsync(
            "UPDATE mailbox_messages SET message_id=message_id WHERE 0",
            _ => { }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<long> InsertAsync(Dfo.Server.Data.Mail.Mail mail, string senderName, DateTimeOffset now,
        IReadOnlyList<PreparedMailAttachment> attachments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(attachments);
        if (mail.Attachments.Length != attachments.Count ||
            mail.Attachments.Where((asset, index) => asset != attachments[index].Asset).Any())
            throw new MailException(MailFailure.InvalidMail, "Prepared assets differ from mail contents.");

        long id = 0;
        await database.WriteAsync(async (command, token) =>
        {
            command.CommandText = """
                INSERT INTO mailbox_messages(sender_character_id,sender_name,receiver_character_id,
                    title,body,created_at,unlimited_flag,expire_at,delivery_key)
                VALUES(@sender,@name,@receiver,@title,@body,@created,@unlimited,@expires,@delivery)
                RETURNING message_id
                """;
            command.Parameters.AddWithValue("@sender", mail.Source.Character?.Value ?? 0);
            command.Parameters.AddWithValue("@name", senderName);
            command.Parameters.AddWithValue("@receiver", mail.Recipient.Value);
            command.Parameters.AddWithValue("@title", mail.Title);
            command.Parameters.AddWithValue("@delivery", mail.DeliveryKey);
            command.Parameters.AddWithValue("@body", mail.Body);
            command.Parameters.AddWithValue("@created", now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@unlimited", mail.ExpiresAt is null ? 1 : 0);
            command.Parameters.AddWithValue("@expires", mail.ExpiresAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                ?? "9999-12-31 23:59:59");
            id = (long)(await command.ExecuteScalarAsync(token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Mail insert returned no ID."));
        }, cancellationToken).ConfigureAwait(false);

        await database.ExecuteAsync("""
            INSERT INTO mailbox_recipients(message_id,character_id,folder,created_at)
            VALUES(@id,@recipient,0,@created)
            """, p =>
        {
            p.AddWithValue("@id", id);
            p.AddWithValue("@recipient", mail.Recipient.Value);
            p.AddWithValue("@created", now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }, cancellationToken).ConfigureAwait(false);

        for (var ordinal = 0; ordinal < attachments.Count; ordinal++)
        {
            var prepared = attachments[ordinal];
            var records = prepared.Items;
            if (prepared.Asset is MailAsset.Gold ? records.Count != 0 : records.Count == 0)
                throw new MailException(MailFailure.InvalidMail, "An item attachment requires detached records; gold does not.");
            var snapshot = records.Count == 0 ? null : records[0].Snapshot;
            var (kind, uid, itemId, count, amount) = prepared.Asset switch
            {
                MailAsset.Equipment e when records.Count == 1 && !records[0].IsPile
                    && records[0].ItemUid == e.Identity => ("equipment", e.Identity.Value, records[0].ItemId, 1, 0),
                MailAsset.Stackable stack when records.All(record => record.IsPile && record.ItemId == stack.ItemId)
                    && records.Sum(record => (long)record.Count) == stack.Count => ("stackable", 0, stack.ItemId, stack.Count, 0),
                MailAsset.Gold money => ("gold", 0, 0, 1, money.Amount),
                _ => throw new MailException(MailFailure.InvalidMail, "Prepared records differ from the attachment descriptor.")
            };
            var core = snapshot is null ? null : (snapshot with { Count = count }).ToCore();
            var itemKind = snapshot?.LegacyKind switch { 8 => "avatar", 5 => "creature", _ => kind };
            var detail = snapshot is null ? "" : JsonSerializer.Serialize(new { Avatar = snapshot.AvatarDetail, Creature = snapshot.CreatureDetail });
            long attachmentId = 0;
            await database.WriteAsync(async (command, token) =>
            {
                command.CommandText = """
                    INSERT INTO mailbox_attachments(message_id,ordinal,item_template_id,source_item_uid,
                        item_count,item_core,item_kind,item_type,instance_value,durability,seal_flag,
                        expire_time,marker_16,asset_kind,gold_amount,claimed_flag,detail_json)
                    VALUES(@message,@ordinal,@item,@uid,@count,@core,@itemKind,@type,@instance,@durability,
                        @seal,@expire,@marker,@kind,@gold,0,@detail) RETURNING attachment_id;
                    """;
                var p = command.Parameters;
                p.AddWithValue("@message", id);
                p.AddWithValue("@ordinal", ordinal);
                p.AddWithValue("@item", itemId);
                p.AddWithValue("@uid", uid);
                p.AddWithValue("@count", count);
                p.AddWithValue("@core", core is not null ? InventoryItemCore.Encode(core) : DBNull.Value);
                p.AddWithValue("@kind", kind);
                p.AddWithValue("@itemKind", itemKind);
                p.AddWithValue("@detail", detail);
                p.AddWithValue("@type", core?.ItemKind ?? 0);
                p.AddWithValue("@instance", core?.Value ?? 0);
                p.AddWithValue("@durability", core?.Durability ?? 0);
                p.AddWithValue("@seal", core?.SealFlag ?? 0);
                p.AddWithValue("@expire", core?.ExpireTime ?? 0);
                p.AddWithValue("@marker", core?.Marker16 ?? -1);
                p.AddWithValue("@gold", amount);
                attachmentId = Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
            }, cancellationToken).ConfigureAwait(false);
            if (records.Count != 0)
                await Custody().AcceptMailAttachmentAsync(attachmentId, records, cancellationToken).ConfigureAwait(false);
        }

        return id;
    }

    public ValueTask<MailRecipient?> FindRecipientAsync(byte[] name, CancellationToken cancellationToken) =>
        database.ReadFirstAsync("""
            SELECT character_id,COALESCE(name_bytes,CAST(name AS BLOB)),level,job,grow_type
            FROM characters WHERE delete_flag=0 AND COALESCE(name_bytes,CAST(name AS BLOB))=@name LIMIT 1
            """, p => p.AddWithValue("@name", name),
            r => new MailRecipient(new CharacterId(r.GetInt32(0)), (byte[])r.GetValue(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)), cancellationToken);

    public ValueTask<IReadOnlyList<MailHeader>> ReadInboxHeadersAsync(CharacterId recipient, CancellationToken cancellationToken) =>
        database.ReadAllAsync("""
            SELECT m.message_id,m.created_at,m.unlimited_flag,m.expire_at
            FROM mailbox_messages m JOIN mailbox_recipients r ON r.message_id=m.message_id
            WHERE
            """ + " " + Visible,
            p => p.AddWithValue("@recipient", recipient.Value),
            r => new MailHeader(r.GetInt64(0), ParseDate(r.GetString(1)),
                r.GetInt32(2) != 0 || IsLegacyUnlimited(r.GetString(3)) ? null : ParseDate(r.GetString(3))), cancellationToken);

    public async ValueTask<IReadOnlyList<MailView>> ReadInboxAsync(CharacterId recipient,
        CancellationToken cancellationToken)
    {
        var headers = await ReadHeadersAsync(recipient, null, null, cancellationToken).ConfigureAwait(false);
        var result = new List<MailView>(headers.Count);
        foreach (var header in headers)
            result.Add(await CompleteAsync(header, cancellationToken).ConfigureAwait(false));
        return result;
    }

    public async ValueTask<MailView?> FindAsync(CharacterId recipient, long mailId,
        CancellationToken cancellationToken)
    {
        var headers = await ReadHeadersAsync(recipient, mailId, null, cancellationToken).ConfigureAwait(false);
        return headers.Count == 0 ? null : await CompleteAsync(headers[0], cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MailView?> FindByAttachmentAsync(CharacterId recipient, long attachmentId,
        CancellationToken cancellationToken)
    {
        var headers = await ReadHeadersAsync(recipient, null, attachmentId, cancellationToken).ConfigureAwait(false);
        return headers.Count == 0 ? null : await CompleteAsync(headers[0], cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> HoldsEquipmentAsync(ItemIdentity identity, CancellationToken cancellationToken)
    {
        var ids = await database.ReadAllAsync("""
            SELECT attachment_id FROM mailbox_attachments
            WHERE asset_kind IN ('equipment','legacy') AND source_item_uid=@uid AND claimed_flag=0 LIMIT 1
            """, p => p.AddWithValue("@uid", identity.Value), r => r.GetInt64(0), cancellationToken)
            .ConfigureAwait(false);
        return ids.Count != 0;
    }

    public ValueTask<IReadOnlyList<ItemRecord>> TakeItemAsync(CharacterId recipient, long attachmentId,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        Custody().TakeMailAttachmentAsync(recipient, attachmentId, now, cancellationToken);

    private IMailItemCustodySession Custody() =>
        items.SessionIn(transactions.Current ?? throw new InvalidOperationException("Mail custody requires the item transaction."))
            as IMailItemCustodySession ?? throw new InvalidOperationException("This mailbox has no transactional item custody adapter.");

    public async ValueTask MarkReadAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken)
    {
        await database.ExecuteAsync("""
            UPDATE mailbox_recipients SET read_flag=1
            WHERE message_id=@mail AND character_id=@recipient AND folder=0 AND saved_flag=0 AND deleted_flag=0
            """, p => { p.AddWithValue("@mail", mailId); p.AddWithValue("@recipient", recipient.Value); },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> MarkClaimedAsync(long attachmentId, CancellationToken cancellationToken)
    {
        var count = await database.ExecuteAsync("""
            UPDATE mailbox_attachments SET claimed_flag=1
            WHERE attachment_id=@id AND claimed_flag=0 AND asset_kind='gold'
            """, p => p.AddWithValue("@id", attachmentId), cancellationToken).ConfigureAwait(false);
        return count == 1;
    }

    public async ValueTask MarkDeletedAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken)
    {
        await database.ExecuteAsync("""
            UPDATE mailbox_recipients SET deleted_flag=1
            WHERE message_id=@mail AND character_id=@recipient AND folder=0 AND saved_flag=0 AND deleted_flag=0
            """, p => { p.AddWithValue("@mail", mailId); p.AddWithValue("@recipient", recipient.Value); },
            cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<IReadOnlyList<Header>> ReadHeadersAsync(CharacterId recipient, long? mailId,
        long? attachmentId, CancellationToken cancellationToken) => database.ReadAllAsync("""
            SELECT m.message_id,m.receiver_character_id,m.sender_character_id,m.sender_name,
                   m.title,m.body,m.created_at,m.unlimited_flag,m.expire_at,r.read_flag,m.delivery_key
            FROM mailbox_messages m JOIN mailbox_recipients r ON r.message_id=m.message_id
            WHERE
            """ + " " + Visible + """
             AND (@mail IS NULL OR m.message_id=@mail)
             AND (@attachment IS NULL OR EXISTS
                 (SELECT 1 FROM mailbox_attachments a WHERE a.message_id=m.message_id AND a.attachment_id=@attachment))
            ORDER BY m.message_id DESC
            """, p =>
        {
            p.AddWithValue("@recipient", recipient.Value);
            p.AddWithValue("@mail", (object?)mailId ?? DBNull.Value);
            p.AddWithValue("@attachment", (object?)attachmentId ?? DBNull.Value);
        }, r => new Header(r.GetInt64(0), new CharacterId(r.GetInt32(1)), r.GetInt32(2),
            r.GetString(3), r.GetString(4), r.GetString(5), ParseDate(r.GetString(6)),
            r.GetInt32(7) != 0, r.GetString(8), r.GetInt32(9) != 0, r.GetString(10)), cancellationToken);

    private async ValueTask<MailView> CompleteAsync(Header header, CancellationToken cancellationToken)
    {
        var attachments = await database.ReadAllAsync("""
            SELECT attachment_id,asset_kind,source_item_uid,item_template_id,item_count,gold_amount,claimed_flag
            FROM mailbox_attachments WHERE message_id=@message ORDER BY ordinal,attachment_id
            """, p => p.AddWithValue("@message", header.Id), r =>
        {
            var kind = r.GetString(1);
            MailAsset asset = kind switch
            {
                "equipment" => new MailAsset.Equipment(new ItemIdentity(checked((int)r.GetInt64(2)))),
                "gold" => new MailAsset.Gold(r.GetInt32(5)),
                _ => new MailAsset.Stackable(r.GetInt32(3), r.GetInt32(4))
            };
            return new MailAttachment(r.GetInt64(0), asset, r.GetInt32(6) != 0, kind == "legacy");
        }, cancellationToken).ConfigureAwait(false);
        return new MailView(header.Id, header.Recipient, header.SenderId, header.SenderName,
            header.Title, header.Body, header.Created,
            header.Unlimited || IsLegacyUnlimited(header.Expires) ? null : ParseDate(header.Expires),
            header.Read, attachments.ToImmutableArray()) { DeliveryKey = header.DeliveryKey };
    }

    private static bool IsLegacyUnlimited(string value) =>
        string.CompareOrdinal(value, "9999-01-01") >= 0;

    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private sealed record Header(long Id, CharacterId Recipient, int SenderId, string SenderName,
        string Title, string Body, DateTimeOffset Created, bool Unlimited, string Expires, bool Read, string DeliveryKey);
}
