using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Guilds;

internal sealed class SqliteGuildContributionStore(SqliteDatabase database) : IGuildContributionStore
{
    public async ValueTask<bool> TryRecordDailyAsync(GuildId guild, CharacterId character, GuildDailyContributionKind kind,
        int day, long earnedAt, int points, CancellationToken ct) => await database.ExecuteAsync("""
            INSERT INTO guild_daily_contributions (guild_id,character_id,kind,day,earned_at,points,membership_id)
            SELECT guild_id,character_id,@kind,@day,@earnedAt,@points,membership_id FROM guild_members
            WHERE guild_id=@guild AND character_id=@character
            ON CONFLICT (character_id,kind,day) DO NOTHING
            """, p =>
            {
                p.AddWithValue("@guild", (long)guild.Value);
                p.AddWithValue("@character", character.Value);
                p.AddWithValue("@kind", (int)kind);
                p.AddWithValue("@day", day);
                p.AddWithValue("@earnedAt", earnedAt);
                p.AddWithValue("@points", points);
            }, ct).ConfigureAwait(false) == 1;

    public async ValueTask<int> RecordClearAsync(GuildId guild, CharacterId character, Guid run, GuildClearContributionKind kind,
        int day, long earnedAt, int requiredClears, int points, CancellationToken ct)
    {
        var awarded = 0;
        await database.WriteAsync(async (command, token) =>
        {
            command.CommandText = """
                INSERT INTO guild_clear_contributions(guild_id,character_id,run_id,kind,day,earned_at,points,membership_id)
                SELECT guild_id,character_id,@run,@kind,@day,@earnedAt,
                    CASE WHEN @required=0 OR (SELECT count(*) FROM guild_clear_contributions
                        WHERE character_id=@character AND kind=@kind AND day=@day)=@required-1 THEN @points ELSE 0 END, membership_id
                FROM guild_members WHERE guild_id=@guild AND character_id=@character
                ON CONFLICT(character_id,run_id) DO NOTHING RETURNING points
                """;
            command.Parameters.AddWithValue("@guild", (long)guild.Value);
            command.Parameters.AddWithValue("@character", character.Value);
            command.Parameters.AddWithValue("@run", run.ToString("N"));
            command.Parameters.AddWithValue("@kind", (int)kind);
            command.Parameters.AddWithValue("@day", day);
            command.Parameters.AddWithValue("@earnedAt", earnedAt);
            command.Parameters.AddWithValue("@required", requiredClears);
            command.Parameters.AddWithValue("@points", points);
            awarded = Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);
        return awarded;
    }

    public ValueTask<IReadOnlyList<GuildContributionMember>> ReadLeaderboardAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAllAsync("""
            WITH awarded AS (
                SELECT sender_id AS character_id, contribution AS points FROM guild_red_packets
                WHERE guild_id=@guild AND sender_departed=0 AND contribution>0
                UNION ALL
                SELECT reward.character_id,reward.points FROM guild_daily_contributions reward
                JOIN guild_members member ON member.guild_id=reward.guild_id AND member.character_id=reward.character_id
                WHERE reward.guild_id=@guild AND reward.membership_id=member.membership_id
                UNION ALL
                SELECT reward.character_id,reward.points FROM guild_clear_contributions reward
                JOIN guild_members member ON member.guild_id=reward.guild_id AND member.character_id=reward.character_id
                WHERE reward.guild_id=@guild AND reward.membership_id=member.membership_id AND reward.points>0
            ), totals AS (
                SELECT character_id,sum(points) AS points FROM awarded GROUP BY character_id
            )
            SELECT member.character_id,c.name,CASE WHEN member.is_leader=1 THEN 1 ELSE member.member_rank END,
                min(2147483647,coalesce(totals.points,0))
            FROM guild_members member JOIN characters c USING(character_id) LEFT JOIN totals USING(character_id)
            WHERE member.guild_id=@guild
            ORDER BY 4 DESC,member.is_leader DESC,member.character_id LIMIT 30
            """, p => p.AddWithValue("@guild", (long)guild.Value),
            r => new GuildContributionMember(new(r.GetInt32(0)), r.GetString(1), r.GetInt32(2), r.GetInt32(3)), ct);
}
