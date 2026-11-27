using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Guilds;

internal sealed class SqliteGuildRedPacketStore(SqliteDatabase database) : IGuildRedPacketStore
{
    private const string Fields = """
        packet_id,guild_id,sender_id,sender_account_id,sender_name,item_id,total_amount,contribution_rate,
        shares,remaining,shares_left,issued_at,expires_at,sender_departed,settled_at,contribution
        """;

    public ValueTask<int> PurchasedAsync(CharacterId character, int day, CancellationToken ct) =>
        ReadIntAsync("""
            SELECT amount FROM guild_red_packet_purchases WHERE character_id=@actor AND day=@day
            """, p => { p.AddWithValue("@actor", character.Value); p.AddWithValue("@day", day); }, ct);

    public async ValueTask RecordPurchaseAsync(CharacterId character, int day, int amount, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO guild_red_packet_purchases(character_id,day,amount) VALUES(@actor,@day,@amount)
            ON CONFLICT(character_id,day) DO UPDATE SET amount=amount+excluded.amount
            """, p =>
            {
                p.AddWithValue("@actor", character.Value);
                p.AddWithValue("@day", day);
                p.AddWithValue("@amount", amount);
            }, ct).ConfigureAwait(false);

    public ValueTask<int> CountActiveAsync(GuildId guild, long now, CancellationToken ct) =>
        ReadIntAsync("""
            SELECT count(*) FROM guild_red_packets WHERE guild_id=@guild
                AND settled_at IS NULL AND expires_at>@now AND shares_left>0
            """, p => BindGuildTime(p, guild, now), ct);

    public ValueTask<int> CountTakenAsync(AccountId account, int day, CancellationToken ct) =>
        ReadIntAsync("""
            SELECT count(*) FROM guild_red_packet_claims WHERE account_id=@account AND day=@day
            """, p => { p.AddWithValue("@account", account.Value); p.AddWithValue("@day", day); }, ct);

    public ValueTask<GuildRedPacket?> FindAsync(uint packet, GuildId guild, CancellationToken ct) =>
        database.ReadFirstAsync($"""
            SELECT {Fields} FROM guild_red_packets WHERE packet_id=@id AND guild_id=@guild
            """, p => { p.AddWithValue("@id", (long)packet); p.AddWithValue("@guild", (long)guild.Value); }, ReadPacket, ct);

    public ValueTask<IReadOnlyList<GuildRedPacket>> ListAsync(GuildId guild, long now, CancellationToken ct) =>
        database.ReadAllAsync($"""
            SELECT {Fields} FROM guild_red_packets WHERE guild_id=@guild
            ORDER BY (settled_at IS NULL AND expires_at>@now AND shares_left>0) DESC,packet_id DESC LIMIT 500
            """, p => BindGuildTime(p, guild, now), ReadPacket, ct);

    public ValueTask<IReadOnlyList<GuildRedPacketClaim>> HistoryAsync(uint packet, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT account_id,character_id,character_name,amount,taken_at FROM guild_red_packet_claims
            WHERE packet_id=@id ORDER BY taken_at,account_id
            """, p => p.AddWithValue("@id", (long)packet),
            r => new GuildRedPacketClaim(packet, new(r.GetInt32(0)), new(r.GetInt32(1)), r.GetString(2),
                r.GetInt32(3), r.GetInt64(4)), ct);

    public async ValueTask<IReadOnlySet<uint>> TakenPacketsAsync(GuildId guild, AccountId account, CancellationToken ct) =>
        (await database.ReadAllAsync("""
            SELECT c.packet_id FROM guild_red_packet_claims c JOIN guild_red_packets p USING(packet_id)
            WHERE c.account_id=@account AND p.guild_id=@guild ORDER BY c.packet_id DESC LIMIT 500
            """, p => { p.AddWithValue("@account", account.Value); p.AddWithValue("@guild", (long)guild.Value); },
            r => checked((uint)r.GetInt64(0)), ct).ConfigureAwait(false)).ToHashSet();

    public async ValueTask<bool> HasTakenAsync(uint packet, AccountId account, CancellationToken ct) =>
        await ReadIntAsync("""
            SELECT count(*) FROM guild_red_packet_claims WHERE packet_id=@id AND account_id=@account
            """, p => { p.AddWithValue("@id", (long)packet); p.AddWithValue("@account", account.Value); }, ct)
            .ConfigureAwait(false) > 0;

    public async ValueTask AddAsync(GuildRedPacket packet, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO guild_red_packets(guild_id,sender_id,sender_account_id,sender_name,item_id,total_amount,
                contribution_rate,shares,remaining,shares_left,issued_at,expires_at)
            VALUES(@guild,@sender,@account,@name,@item,@amount,@rate,@shares,@amount,@shares,@now,@end)
            """, p =>
            {
                BindGuildTime(p, packet.Guild, packet.IssuedAt);
                p.AddWithValue("@sender", packet.Sender.Value);
                p.AddWithValue("@account", packet.SenderAccount.Value);
                p.AddWithValue("@name", packet.SenderName);
                p.AddWithValue("@item", packet.ItemId);
                p.AddWithValue("@amount", packet.Amount);
                p.AddWithValue("@rate", packet.ContributionRate);
                p.AddWithValue("@shares", packet.Shares);
                p.AddWithValue("@end", packet.ExpiresAt);
            }, ct).ConfigureAwait(false);

    public async ValueTask<bool> TakeAsync(GuildRedPacket packet, GuildRedPacketClaim claim, int day, CancellationToken ct)
    {
        var updated = await database.ExecuteAsync("""
            UPDATE guild_red_packets SET remaining=remaining-@amount,shares_left=shares_left-1
            WHERE packet_id=@id AND shares_left=@shares AND remaining=@remaining AND settled_at IS NULL AND expires_at>@now
            """, p =>
            {
                p.AddWithValue("@id", (long)packet.Id);
                p.AddWithValue("@amount", claim.Amount);
                p.AddWithValue("@shares", packet.SharesLeft);
                p.AddWithValue("@remaining", packet.Remaining);
                p.AddWithValue("@now", claim.TakenAt);
            }, ct).ConfigureAwait(false);
        if (updated != 1)
            return false;
        _ = await database.ExecuteAsync("""
            INSERT INTO guild_red_packet_claims(packet_id,account_id,character_id,character_name,amount,taken_at,day)
            VALUES(@id,@account,@actor,@name,@amount,@now,@day)
            """, p =>
            {
                p.AddWithValue("@id", (long)packet.Id);
                p.AddWithValue("@account", claim.Account.Value);
                p.AddWithValue("@actor", claim.Character.Value);
                p.AddWithValue("@name", claim.Name);
                p.AddWithValue("@amount", claim.Amount);
                p.AddWithValue("@now", claim.TakenAt);
                p.AddWithValue("@day", day);
            }, ct).ConfigureAwait(false);
        return true;
    }

    public ValueTask<IReadOnlyList<GuildRedPacket>> FindDueAsync(long now, int count, CancellationToken ct) =>
        database.ReadAllAsync($"""
            SELECT {Fields} FROM guild_red_packets WHERE settled_at IS NULL AND expires_at<=@now
            ORDER BY expires_at,packet_id LIMIT @count
            """, p => { p.AddWithValue("@now", now); p.AddWithValue("@count", count); }, ReadPacket, ct);

    public async ValueTask SettleAsync(GuildRedPacket packet, long now, int refunded, int points, CancellationToken ct)
    {
        var updated = await database.ExecuteAsync("""
            UPDATE guild_red_packets SET settled_at=@now,refunded=@refund,contribution=@points
            WHERE packet_id=@id AND settled_at IS NULL
            """, p =>
            {
                p.AddWithValue("@id", (long)packet.Id);
                p.AddWithValue("@now", now);
                p.AddWithValue("@refund", refunded);
                p.AddWithValue("@points", points);
            }, ct).ConfigureAwait(false);
        if (updated != 1)
            throw new InvalidOperationException("The guild red packet settlement changed within its transaction.");
    }

    private async ValueTask<int> ReadIntAsync(string sql, Action<SqliteParameterCollection> bind, CancellationToken ct) =>
        (await database.ReadAllAsync(sql, bind, r => r.GetInt32(0), ct).ConfigureAwait(false)).SingleOrDefault();

    private static void BindGuildTime(SqliteParameterCollection p, GuildId guild, long now)
    {
        p.AddWithValue("@guild", (long)guild.Value);
        p.AddWithValue("@now", now);
    }

    private static GuildRedPacket ReadPacket(SqliteDataReader r) => new(
        checked((uint)r.GetInt64(0)), new(checked((uint)r.GetInt64(1))), new(r.GetInt32(2)), new(r.GetInt32(3)),
        r.GetString(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9),
        r.GetInt32(10), r.GetInt64(11), r.GetInt64(12), r.GetBoolean(13), r.IsDBNull(14) ? null : r.GetInt64(14), r.GetInt32(15));
}
