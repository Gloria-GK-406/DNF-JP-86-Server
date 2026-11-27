using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Guilds;

internal sealed class SqliteGuildMonthlyContributionStore(SqliteDatabase database) : IGuildMonthlyContributionStore
{
    public async ValueTask<bool> RecordAsync(GuildId guild, CharacterId character, GuildContributionSource source, string key,
        int points, DateTimeOffset at, CancellationToken ct) =>
        await database.ExecuteAsync("""
            INSERT INTO guild_monthly_contribution_events(guild_id,character_id,character_name,source,event_key,earned_at,points,membership_id)
            SELECT m.guild_id,c.character_id,COALESCE(c.name_bytes,CAST(c.name AS BLOB)),@source,@key,@at,@points,m.membership_id
            FROM guild_members m JOIN characters c USING(character_id)
            WHERE m.guild_id=@guild AND m.character_id=@character AND c.delete_flag=0 AND m.joined_at<=@at
            ON CONFLICT(character_id,event_key) DO NOTHING
            """, parameters =>
        {
            parameters.AddWithValue("@guild", (long)guild.Value);
            parameters.AddWithValue("@character", character.Value);
            parameters.AddWithValue("@source", (int)source);
            parameters.AddWithValue("@key", key);
            parameters.AddWithValue("@at", at.ToUnixTimeSeconds());
            parameters.AddWithValue("@points", points);
        }, ct).ConfigureAwait(false) == 1;

    public async ValueTask<GuildContributionTotals> ReadTotalsAsync(
        GuildId guild, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var rows = await database.ReadAllAsync("""
            SELECT e.character_id, SUM(e.points),
                SUM(CASE WHEN e.earned_at>=@start THEN e.points ELSE 0 END),
                SUM(CASE WHEN e.membership_id=m.membership_id THEN e.points ELSE 0 END),
                SUM(CASE WHEN e.membership_id=m.membership_id AND e.earned_at>=@start THEN e.points ELSE 0 END)
            FROM guild_contribution_entries e
            LEFT JOIN guild_members m ON m.character_id=e.character_id AND m.guild_id=e.guild_id
            WHERE e.guild_id=@guild AND e.earned_at<@end
            GROUP BY e.character_id
            """, parameters =>
        {
            parameters.AddWithValue("@guild", (long)guild.Value);
            BindPeriod(parameters, start, end);
        }, reader => (Character: new CharacterId(reader.GetInt32(0)), Lifetime: reader.GetInt64(1),
            Monthly: reader.GetInt64(2), Personal: Amounts(reader.GetInt64(3), reader.GetInt64(4))), ct)
            .ConfigureAwait(false);
        return new(Amounts(rows.Sum(row => row.Lifetime), rows.Sum(row => row.Monthly)),
            rows.ToDictionary(row => row.Character, row => row.Personal));
    }

    public ValueTask<IReadOnlyList<GuildContributionEntry>> ReadHistoryAsync(GuildId guild, int limit, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT character_name,points,earned_at,source FROM guild_contribution_entries
            WHERE guild_id=@guild ORDER BY earned_at DESC,source DESC,source_id DESC LIMIT @limit
            """, parameters =>
        {
            parameters.AddWithValue("@guild", (long)guild.Value);
            parameters.AddWithValue("@limit", limit);
        }, reader => new GuildContributionEntry((byte[])reader[0], reader.GetInt32(1),
            DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)), (GuildContributionSource)reader.GetInt32(3)), ct);

    public ValueTask<IReadOnlyList<GuildMonthlyContributionRecipient>> ReadUnpaidAsync(
        DateTimeOffset start, DateTimeOffset end, int target, int limit, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT c.account_id,m.character_id,m.guild_id
            FROM guild_contribution_entries e
            JOIN guild_members m ON m.character_id=e.character_id AND m.guild_id=e.guild_id AND m.membership_id=e.membership_id
            JOIN characters c ON c.character_id=m.character_id AND c.delete_flag=0
            WHERE e.earned_at>=@start AND e.earned_at<@end
                AND NOT EXISTS (SELECT 1 FROM guild_monthly_contribution_rewards r
                    WHERE r.period_start=@start AND r.character_id=m.character_id)
            GROUP BY m.character_id HAVING SUM(e.points)>=@target
            ORDER BY m.character_id LIMIT @limit
            """, parameters =>
        {
            BindPeriod(parameters, start, end);
            parameters.AddWithValue("@target", target);
            parameters.AddWithValue("@limit", limit);
        }, reader => new GuildMonthlyContributionRecipient(new(reader.GetInt32(0)), new(reader.GetInt32(1)),
            new(checked((uint)reader.GetInt64(2)))), ct);

    public async ValueTask MarkPaidAsync(GuildMonthlyContributionRecipient recipient, DateTimeOffset start, CancellationToken ct)
    {
        await database.ExecuteAsync("""
            INSERT INTO guild_monthly_contribution_rewards(period_start,character_id,guild_id)
            VALUES(@start,@character,@guild)
            """, parameters =>
        {
            parameters.AddWithValue("@start", start.ToUnixTimeSeconds());
            parameters.AddWithValue("@character", recipient.Character.Value);
            parameters.AddWithValue("@guild", (long)recipient.Guild.Value);
        }, ct).ConfigureAwait(false);
    }

    private static void BindPeriod(SqliteParameterCollection parameters, DateTimeOffset start, DateTimeOffset end)
    {
        parameters.AddWithValue("@start", start.ToUnixTimeSeconds());
        parameters.AddWithValue("@end", end.ToUnixTimeSeconds());
    }

    private static GuildContributionAmounts Amounts(long lifetime, long monthly) =>
        new((uint)Math.Min(lifetime, uint.MaxValue), (uint)Math.Min(monthly, uint.MaxValue));
}
