using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Guilds;

internal sealed class SqliteGuildGiftStore(SqliteDatabase database) : IGuildGiftStore
{
    private const string Fields = """
        sender_id,sender_name,recipient_id,recipient_name,product_id,kind,content_value,price,duration_days,sent_at,claimed_at
        """;

    public async ValueTask<bool> HasSentAsync(GuildId sender, GuildId recipient, DateOnly day, CancellationToken ct) =>
        await database.ReadFirstAsync("""
            SELECT sender_name FROM guild_gifts WHERE sender_id=@sender AND recipient_id=@recipient AND sent_day=@day
            """, p =>
            {
                BindPair(p, sender, recipient);
                p.AddWithValue("@day", day.DayNumber);
            }, r => r.GetString(0), ct).ConfigureAwait(false) is not null;

    public async ValueTask AddAsync(GuildGift gift, DateOnly day, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO guild_gifts(sender_id,sender_name,recipient_id,recipient_name,sent_day,sent_at,
                                    product_id,kind,content_value,duration_days,price)
            VALUES(@sender,@senderName,@recipient,@recipientName,@day,@sent,@product,@kind,@value,@days,@price)
            """, p =>
            {
                BindPair(p, gift.Sender, gift.Recipient);
                p.AddWithValue("@senderName", gift.SenderName);
                p.AddWithValue("@recipientName", gift.RecipientName);
                p.AddWithValue("@day", day.DayNumber);
                p.AddWithValue("@sent", gift.SentAt);
                p.AddWithValue("@product", (long)gift.Content.ProductId);
                p.AddWithValue("@kind", (int)gift.Content.Kind);
                p.AddWithValue("@value", gift.Content.Value);
                p.AddWithValue("@days", gift.Content.DurationDays);
                p.AddWithValue("@price", gift.Content.Price);
            }, ct).ConfigureAwait(false);

    public ValueTask<IReadOnlyList<GuildGift>> ReadAsync(GuildId guild, bool history, CancellationToken ct) =>
        database.ReadAllAsync(history ? $"""
            SELECT {Fields} FROM guild_gifts
            WHERE recipient_id=@guild AND voided=0
            ORDER BY sent_at DESC,sender_id,recipient_id LIMIT 255
            """ : $"""
            SELECT {Fields} FROM guild_gifts
            WHERE recipient_id=@guild AND claimed_at IS NULL AND voided=0
            ORDER BY sent_at,sender_id LIMIT 255
            """, p => p.AddWithValue("@guild", (long)guild.Value), ReadGift, ct);

    public ValueTask<GuildGift?> FindPendingAsync(GuildId recipient, GuildGiftKey key, CancellationToken ct) =>
        database.ReadFirstAsync($"""
            SELECT {Fields} FROM guild_gifts
            WHERE sender_id=@sender AND recipient_id=@recipient AND sent_at=@sent
              AND product_id=@product AND kind=@kind AND claimed_at IS NULL AND voided=0
            """, p => BindGift(p, recipient, key), ReadGift, ct);

    public async ValueTask<bool> TryMarkClaimedAsync(GuildId recipient, GuildGiftKey key, long claimedAt, CancellationToken ct) =>
        await database.ExecuteAsync("""
            UPDATE guild_gifts SET claimed_at=@claimed
            WHERE sender_id=@sender AND recipient_id=@recipient AND sent_at=@sent
              AND product_id=@product AND kind=@kind AND claimed_at IS NULL AND voided=0
            """, p =>
            {
                BindGift(p, recipient, key);
                p.AddWithValue("@claimed", claimedAt);
            }, ct).ConfigureAwait(false) == 1;

    private static GuildGift ReadGift(SqliteDataReader row) => new(
        new(checked((uint)row.GetInt64(0))), row.GetString(1), new(checked((uint)row.GetInt64(2))), row.GetString(3),
        new(checked((uint)row.GetInt64(4)), (GuildContentKind)row.GetInt32(5), row.GetInt32(6), row.GetInt32(7),
            row.GetInt32(8), CanGift: true), row.GetInt64(9), row.IsDBNull(10) ? null : row.GetInt64(10));

    private static void BindPair(SqliteParameterCollection parameters, GuildId sender, GuildId recipient)
    {
        parameters.AddWithValue("@sender", (long)sender.Value);
        parameters.AddWithValue("@recipient", (long)recipient.Value);
    }

    private static void BindGift(SqliteParameterCollection parameters, GuildId recipient, GuildGiftKey key)
    {
        BindPair(parameters, key.Sender, recipient);
        parameters.AddWithValue("@sent", (long)key.SentAt);
        parameters.AddWithValue("@product", key.ProductId);
        parameters.AddWithValue("@kind", key.Kind);
    }
}
