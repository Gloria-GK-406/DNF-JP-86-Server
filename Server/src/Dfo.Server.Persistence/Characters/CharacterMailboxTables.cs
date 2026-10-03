using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

/// <summary>The <c>mailbox_messages</c> table.</summary>
/// <remarks>
/// Two of the columns are not stored: whether the message is still live, and how many seconds it
/// has left. Both are the stored expiry measured against now, and now belongs to the database --
/// working them out anywhere else would compare a text timestamp against a different clock.
/// </remarks>
internal sealed class SqliteMailboxMessageTable(SqliteDatabase database) : IMailboxMessageTable
{
    /// <summary>
    /// How many ids go into one statement.
    /// </summary>
    /// <remarks>
    /// SQLite counts every bound value against one limit for the whole statement, and an inbox
    /// has no bound of its own, so the ids are asked for a batch at a time rather than all at
    /// once. Nothing depends on the batching: the rows come back the same either way, and what
    /// order they are in is decided afterwards.
    /// </remarks>
    private const int Batch = 500;

    public async ValueTask<IReadOnlyList<MailboxMessageRow>> ReadAsync(
        IReadOnlyList<long> messageIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        var rows = new List<MailboxMessageRow>(messageIds.Count);
        foreach (var batch in messageIds.Chunk(Batch))
        {
            rows.AddRange(await ReadBatchAsync(batch, cancellationToken).ConfigureAwait(false));
        }

        return rows;
    }

    private ValueTask<IReadOnlyList<MailboxMessageRow>> ReadBatchAsync(
        long[] messageIds, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT message_id, sender_character_id, sender_name, mail_type,
                   source_protocol, title, body, gold,
                   CASE WHEN unlimited_flag!=0 OR expire_at >= '9999-01-01 00:00:00' THEN 0
                        ELSE MIN(2147483647, MAX(0, CAST(strftime('%s',expire_at) AS INTEGER)-CAST(strftime('%s','now') AS INTEGER))) END,
                   CAST(strftime('%s',created_at) AS INTEGER),
                   CASE WHEN unlimited_flag != 0 OR datetime(expire_at) > CURRENT_TIMESTAMP
                        THEN 1 ELSE 0 END
            FROM mailbox_messages
            WHERE message_id IN (
            """
            + string.Join(',', messageIds.Select((_, index) => $"@m{index}"))
            + ")",
            parameters =>
            {
                for (var index = 0; index < messageIds.Length; index++)
                {
                    parameters.AddWithValue($"@m{index}", messageIds[index]);
                }
            },
            reader => new MailboxMessageRow(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7),
                reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10) != 0),
            cancellationToken);
}

/// <summary>The <c>mailbox_attachments</c> table.</summary>
internal sealed class SqliteMailboxAttachmentTable(SqliteDatabase database)
    : IMailboxAttachmentTable
{
    public ValueTask<IReadOnlyList<CharacterMailboxAttachmentStorageRow>> ReadAsync(
        long messageId, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            """
            SELECT attachment_id,ordinal,item_type,source_list_type,source_slot_index,
                   source_item_uid,item_template_id,item_kind,item_count,instance_value,
                   durability,seal_flag,option_value,expire_time,marker_16,
                   pet_serial_or_handle,COALESCE(item_core,X''),detail_json
            FROM mailbox_attachments WHERE message_id=@messageId AND claimed_flag=0 AND asset_kind!='gold' ORDER BY ordinal
            """,
            parameters => parameters.AddWithValue("@messageId", messageId),
            reader => new CharacterMailboxAttachmentStorageRow(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetString(7),
                reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11),
                reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15),
                (byte[])reader.GetValue(16), reader.GetString(17)),
            cancellationToken);
}
