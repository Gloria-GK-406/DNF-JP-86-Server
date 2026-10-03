using System.Globalization;
using System.Text.Json;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

internal sealed partial class SqliteItemDomainStoreSession : IMailItemCustodySession
{
    private const string PilePayloadPrefix = "item-records-v1:";
    private readonly Dictionary<long, MailCustodyReceipt> mailCustody = [];
    private sealed record MailCustodyReceipt(string Kind, int ItemId, int Count, int Uid, string Payload);
    private sealed record MailCustodyRow(long Id, string Kind, int Uid, int ItemId, int Count,
        byte[]? Core, string Payload, bool Claimed, int Recipient, bool Visible, bool Unlimited, string Expires);

    public async ValueTask AcceptMailAttachmentAsync(long attachmentId, IReadOnlyList<ItemRecord> records, CancellationToken ct)
    {
        Active();
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0 || records.Distinct(ReferenceEqualityComparer.Instance).Count() != records.Count)
            throw MailError(MailFailure.InvalidMail, "Every attachment requires distinct detached records.");
        var row = await ReadMailAsync(attachmentId, ct).ConfigureAwait(false)
            ?? throw MailError(MailFailure.NotFound, "Attachment does not exist.");
        if (row.Claimed || mailCustody.ContainsKey(attachmentId))
            throw MailError(MailFailure.AlreadyClaimed, "Attachment already received its records.");
        if (row.Kind is not ("equipment" or "stackable"))
            throw MailError(MailFailure.InvalidMail, "Only supported item attachments take custody.");

        var copies = records.Select(record => record.Snapshot).ToArray();
        var uids = new long[records.Count];
        for (var index = 0; index < records.Count; index++)
        {
            // IsForeign also proves that this is the exact pending object from this transaction.
            var foreign = scope.IsForeign(records[index], identity);
            if (!foreign) uids[index] = Detached(records[index]);
            Validate(copies[index]);
            if (copies[index].ItemId != row.ItemId || copies[index].IsPile != (row.Kind == "stackable"))
                throw MailError(MailFailure.InvalidMail, "Attachment descriptor differs from the detached item.");
            if (!foreign && await HasPositionAsync(uids[index], ct).ConfigureAwait(false))
                throw MailError(MailFailure.NotOwned, "A detached item still occupies an inventory position.");
        }
        if (copies.Sum(item => (long)item.Count) != row.Count)
            throw MailError(MailFailure.InvalidMail, "Attachment quantity differs from its records.");
        if (row.Kind == "equipment")
        {
            if (records.Count != 1 || copies[0].ItemUid?.Value != row.Uid || row.Count != 1
                || !copies[0].Type.HasFlag(ItemTraits.Equipment)
                || await PendingMailCountAsync(row.Uid, ct).ConfigureAwait(false) != 1)
                throw MailError(MailFailure.NotOwned, "Equipment is not exclusively held by this attachment.");
            if (row.Core is null || !row.Core.AsSpan().SequenceEqual(InventoryItemCore.Encode(copies[0].ToCore())))
                throw MailError(MailFailure.InvalidMail, "Attachment projection differs from the real equipment.");
        }
        else if (row.Uid != 0)
            throw MailError(MailFailure.InvalidMail, "An interchangeable attachment cannot name a world identity.");

        await Mutate(async () =>
        {
            // Piles have no public UID. Their exact fragments become the attachment's storage,
            // allowing different expiry/unknown fields to survive without inventing extra mail rows.
            var payload = row.Kind == "stackable"
                ? PilePayloadPrefix + JsonSerializer.Serialize(copies.Select(item => Convert.ToHexString(InventoryItemCore.Encode(item.ToCore()))).ToArray())
                : row.Payload;
            if (row.Kind == "stackable")
            {
                await using var update = await owner.CommandAsync(ct).ConfigureAwait(false);
                update.CommandText = "UPDATE mailbox_attachments SET detail_json=@payload WHERE attachment_id=@id AND claimed_flag=0";
                update.Parameters.AddWithValue("@payload", payload); update.Parameters.AddWithValue("@id", attachmentId);
                if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw MailError(MailFailure.ConcurrentChange, "Attachment changed during receipt.");
            }
            for (var index = 0; index < records.Count; index++)
            {
                if (row.Kind == "stackable" && uids[index] != 0)
                    await RemoveRowAsync(uids[index], ct).ConfigureAwait(false);
                await scope.AcceptAsync(records[index], identity, ct).ConfigureAwait(false);
                detached.Remove(records[index]);
            }
            mailCustody.Add(attachmentId, new(row.Kind, row.ItemId, row.Count, row.Uid, payload));
            return true;
        }).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<ItemRecord>> TakeMailAttachmentAsync(CharacterId recipient,
        long attachmentId, DateTimeOffset now, CancellationToken ct)
    {
        Active();
        var row = await ReadMailAsync(attachmentId, ct).ConfigureAwait(false);
        if (row is null || row.Recipient != recipient.Value || !row.Visible)
            throw MailError(MailFailure.NotFound, "Attachment is not in this recipient's inbox.");
        if (!row.Unlimited && !IsUnlimitedDate(row.Expires)
            && DateTimeOffset.Parse(row.Expires, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal) <= now)
            throw MailError(MailFailure.Expired, "Attachment has expired.");
        if (row.Claimed) throw MailError(MailFailure.AlreadyClaimed, "Attachment was already claimed.");
        if (row.Kind == "legacy") throw MailError(MailFailure.LegacyAttachment, "Legacy attachment is unsupported.");
        if (row.Kind is not ("equipment" or "stackable"))
            throw MailError(MailFailure.InvalidItem, "Attachment does not hold an item.");

        ItemSnapshot[] snapshots;
        if (row.Kind == "equipment")
        {
            if (row.Uid <= 0 || row.Count != 1 || await HasPositionAsync(row.Uid, ct).ConfigureAwait(false)
                || await PendingMailCountAsync(row.Uid, ct).ConfigureAwait(false) != 1)
                throw MailError(MailFailure.NotOwned, "Equipment is not exclusively in mail custody.");
            var snapshot = await ReadMailWorldAsync(row.Uid, ct).ConfigureAwait(false)
                ?? throw MailError(MailFailure.NotOwned, "Equipment row is absent.");
            if (snapshot.ItemId != row.ItemId || !snapshot.Type.HasFlag(ItemTraits.Equipment))
                throw MailError(MailFailure.InvalidItem, "Attachment does not identify supported equipment.");
            snapshots = [snapshot];
        }
        else
        {
            snapshots = ReadMailPiles(row);
        }

        return await Mutate<IReadOnlyList<ItemRecord>>(async () =>
        {
            await using var claim = await owner.CommandAsync(ct).ConfigureAwait(false);
            claim.CommandText = """
                UPDATE mailbox_attachments SET claimed_flag=1
                WHERE attachment_id=@id AND claimed_flag=0 AND asset_kind=@kind
                  AND EXISTS(SELECT 1 FROM mailbox_messages m JOIN mailbox_recipients r ON r.message_id=m.message_id
                    JOIN characters c ON c.character_id=r.character_id
                    WHERE m.message_id=mailbox_attachments.message_id AND m.receiver_character_id=@recipient
                      AND r.character_id=@recipient AND r.folder=0 AND r.saved_flag=0 AND r.deleted_flag=0 AND c.delete_flag=0);
                """;
            claim.Parameters.AddWithValue("@id", attachmentId); claim.Parameters.AddWithValue("@kind", row.Kind);
            claim.Parameters.AddWithValue("@recipient", recipient.Value);
            if (await claim.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw MailError(MailFailure.ConcurrentChange, "Attachment changed during claim.");
            var result = new List<ItemRecord>(snapshots.Length);
            foreach (var snapshot in snapshots)
            {
                var uid = snapshot.IsPile ? await InsertAsync(snapshot, ct).ConfigureAwait(false) : row.Uid;
                result.Add(Issue(uid, snapshot));
            }
            mailCustody.Remove(attachmentId);
            return result;
        }).ConfigureAwait(false);
    }

    private static ItemSnapshot[] ReadMailPiles(MailCustodyRow row)
    {
        ItemSnapshot[] snapshots;
        if (row.Payload.StartsWith(PilePayloadPrefix, StringComparison.Ordinal))
        {
            var fragments = JsonSerializer.Deserialize<string[]>(row.Payload[PilePayloadPrefix.Length..])
                ?? throw new InvalidDataException("Mail pile payload is empty.");
            snapshots = fragments.Select(value =>
            {
                var bytes = Convert.FromHexString(value);
                if (bytes.Length != InventoryItemCore.Size) throw new InvalidDataException("Invalid mailed item core length.");
                return ItemSnapshot.FromCore(InventoryItemCore.Decode(bytes), true);
            }).ToArray();
        }
        else
        {
            // Previously issued stackable mail carries its value in the existing core column.
            if (row.Core is not { Length: InventoryItemCore.Size } || row.Payload.Length != 0)
                throw new InvalidDataException("Unrecognized mail item storage.");
            snapshots = [ItemSnapshot.FromCore(InventoryItemCore.Decode(row.Core), true)];
        }
        if (row.Uid != 0 || snapshots.Length == 0 || snapshots.Any(value => value.ItemId != row.ItemId)
            || snapshots.Sum(value => (long)value.Count) != row.Count)
            throw new InvalidDataException("Mail pile fragments disagree with attachment quantity or template.");
        foreach (var snapshot in snapshots) Validate(snapshot);
        return snapshots;
    }

    private async ValueTask<MailCustodyRow?> ReadMailAsync(long attachmentId, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = """
            SELECT a.attachment_id,a.asset_kind,a.source_item_uid,a.item_template_id,a.item_count,a.item_core,
                a.detail_json,a.claimed_flag,m.receiver_character_id,
                EXISTS(SELECT 1 FROM mailbox_recipients r JOIN characters c ON c.character_id=r.character_id
                    WHERE r.message_id=m.message_id AND r.character_id=m.receiver_character_id
                        AND r.folder=0 AND r.saved_flag=0 AND r.deleted_flag=0 AND c.delete_flag=0),
                m.unlimited_flag,m.expire_at
            FROM mailbox_attachments a JOIN mailbox_messages m ON m.message_id=a.message_id WHERE a.attachment_id=@id;
            """;
        command.Parameters.AddWithValue("@id", attachmentId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.IsDBNull(5) ? null : (byte[])reader.GetValue(5), reader.GetString(6), reader.GetInt32(7) != 0,
                reader.GetInt32(8), reader.GetInt32(9) != 0, reader.GetInt32(10) != 0, reader.GetString(11)) : null;
    }

    private async ValueTask<ItemSnapshot?> ReadMailWorldAsync(int uid, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = """
            SELECT item_core,avatar_expire_date,avatar_clear_id,avatar_jewel_socket,avatar_color1,avatar_color2,avatar_delete_date,
                creature_field04,creature_mode_flag,creature_progress_value,creature_mode1_field0a,creature_mode1_field0b,
                creature_field_after_value,creature_text,creature_tail_flag FROM items WHERE item_uid=@uid;
            """;
        command.Parameters.AddWithValue("@uid", uid);
        await using var r = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        var avatar = ReadAvatarDetail(r, 1);
        var creature = r.IsDBNull(7) ? null : new InventoryCreatureDetailState(r.GetInt32(7), checked((byte)r.GetInt32(8)),
            r.GetInt32(9), r.GetInt32(10), r.GetInt32(11), r.GetInt32(12), r.IsDBNull(13) ? [] : (byte[])r.GetValue(13), checked((byte)r.GetInt32(14)));
        var value = ItemSnapshot.FromCore(InventoryItemCore.Decode((byte[])r.GetValue(0)), false, new(uid), avatar, creature);
        Validate(value);
        return value;
    }

    private async ValueTask<bool> HasPositionAsync(long uid, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM item_slots WHERE core_uid=@uid)";
        command.Parameters.AddWithValue("@uid", uid);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)) != 0;
    }
    private async ValueTask<long> PendingMailCountAsync(long uid, CancellationToken ct)
    {
        await using var command = await owner.CommandAsync(ct).ConfigureAwait(false);
        command.CommandText = "SELECT COUNT(*) FROM mailbox_attachments WHERE source_item_uid=@uid AND claimed_flag=0 AND asset_kind IN ('equipment','legacy')";
        command.Parameters.AddWithValue("@uid", uid);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }
    private async ValueTask ValidateMailCustodyAsync(CancellationToken ct)
    {
        foreach (var (attachmentId, receipt) in mailCustody)
        {
            var row = await ReadMailAsync(attachmentId, ct).ConfigureAwait(false);
            if (row is null || row.Claimed || row.Kind != receipt.Kind || row.ItemId != receipt.ItemId
                || row.Count != receipt.Count || row.Uid != receipt.Uid || row.Payload != receipt.Payload
                || row.Kind == "equipment" && (await HasPositionAsync(row.Uid, ct).ConfigureAwait(false)
                    || await PendingMailCountAsync(row.Uid, ct).ConfigureAwait(false) != 1
                    || await ReadMailWorldAsync(row.Uid, ct).ConfigureAwait(false) is null))
                throw new InvalidOperationException("A received item no longer has its recorded mail custody.");
        }
    }
    private static bool IsUnlimitedDate(string value) => string.CompareOrdinal(value, "9999-01-01") >= 0;
    private static MailException MailError(MailFailure failure, string message) => new(failure, message);
}
