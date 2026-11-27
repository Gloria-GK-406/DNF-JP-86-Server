using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Guilds;

internal sealed class SqliteGuildAllianceStore(SqliteDatabase database) : IGuildAllianceStore
{
    public ValueTask<GuildAlliancePeer?> FindGuildAsync(GuildId guild, CancellationToken ct) =>
        database.ReadFirstAsync("""
            SELECT g.guild_id,g.name,g.mark_id,m.character_id
            FROM guilds g JOIN guild_members m ON m.guild_id=g.guild_id AND m.is_leader=1
            WHERE g.guild_id=@guild
            """, p => p.AddWithValue("@guild", (long)guild.Value), ReadPeer, ct);

    public ValueTask<GuildAlliancePeer?> FindAllyAsync(GuildId guild, CancellationToken ct) =>
        database.ReadFirstAsync("""
            SELECT g.guild_id,g.name,g.mark_id,m.character_id
            FROM guild_alliances a
            JOIN guilds g ON g.guild_id=CASE WHEN a.first_id=@guild THEN a.second_id ELSE a.first_id END
            JOIN guild_members m ON m.guild_id=g.guild_id AND m.is_leader=1
            WHERE a.first_id=@guild OR a.second_id=@guild
            """, p => p.AddWithValue("@guild", (long)guild.Value), ReadPeer, ct);

    public ValueTask<IReadOnlyList<GuildAllianceApplication>> ReadApplicationsAsync(GuildId guild, bool incoming,
        CancellationToken ct) =>
        database.ReadAllAsync(incoming ? """
            SELECT g.guild_id,g.name,g.mark_id,m.character_id,a.message,a.applied_at
            FROM guild_alliance_applications a JOIN guilds g ON g.guild_id=a.sender_id
            JOIN guild_members m ON m.guild_id=g.guild_id AND m.is_leader=1
            WHERE a.target_id=@guild ORDER BY a.applied_at,a.sender_id
            """ : """
            SELECT g.guild_id,g.name,g.mark_id,m.character_id,a.message,a.applied_at
            FROM guild_alliance_applications a JOIN guilds g ON g.guild_id=a.target_id
            JOIN guild_members m ON m.guild_id=g.guild_id AND m.is_leader=1
            WHERE a.sender_id=@guild ORDER BY a.applied_at,a.target_id
            """, p => p.AddWithValue("@guild", (long)guild.Value),
            r => new GuildAllianceApplication(ReadPeer(r), r.GetString(4), r.GetInt64(5)), ct);

    public async ValueTask<bool> TryApplyAsync(GuildId sender, GuildId target, string message, long appliedAt,
        CancellationToken ct) => await database.ExecuteAsync("""
            INSERT INTO guild_alliance_applications(sender_id,target_id,message,applied_at)
            SELECT @sender,@target,@message,@applied
            WHERE NOT EXISTS (SELECT 1 FROM guild_alliance_applications
                WHERE (sender_id=@sender AND target_id=@target) OR (sender_id=@target AND target_id=@sender))
            AND (SELECT COUNT(*) FROM guild_alliance_applications WHERE sender_id=@sender)<255
            AND (SELECT COUNT(*) FROM guild_alliance_applications WHERE target_id=@target)<255
            """, p =>
            {
                BindPair(p, sender, target);
                p.AddWithValue("@message", message);
                p.AddWithValue("@applied", appliedAt);
            }, ct).ConfigureAwait(false) == 1;

    public async ValueTask<bool> TryRemoveApplicationAsync(GuildId sender, GuildId target, CancellationToken ct) =>
        await database.ExecuteAsync("""
            DELETE FROM guild_alliance_applications WHERE sender_id=@sender AND target_id=@target
            """, p => BindPair(p, sender, target), ct).ConfigureAwait(false) == 1;

    public async ValueTask<bool> TryFormAsync(GuildId sender, GuildId target, CancellationToken ct) =>
        await database.ExecuteAsync("""
            INSERT INTO guild_alliances(first_id,second_id)
            SELECT min(@sender,@target),max(@sender,@target)
            WHERE EXISTS (SELECT 1 FROM guild_alliance_applications WHERE sender_id=@sender AND target_id=@target)
            AND NOT EXISTS (SELECT 1 FROM guild_alliances
                WHERE first_id IN (@sender,@target) OR second_id IN (@sender,@target))
            """, p => BindPair(p, sender, target), ct).ConfigureAwait(false) == 1;

    public async ValueTask<bool> TryDissolveAsync(GuildId guild, GuildId ally, CancellationToken ct) =>
        await database.ExecuteAsync("""
            DELETE FROM guild_alliances WHERE first_id=min(@sender,@target) AND second_id=max(@sender,@target)
            """, p => BindPair(p, guild, ally), ct).ConfigureAwait(false) == 1;

    private static GuildAlliancePeer ReadPeer(SqliteDataReader reader) =>
        new(new GuildId(checked((uint)reader.GetInt64(0))), reader.GetString(1), checked((byte)reader.GetInt32(2)),
            new CharacterId(reader.GetInt32(3)));

    private static void BindPair(SqliteParameterCollection parameters, GuildId sender, GuildId target)
    {
        parameters.AddWithValue("@sender", (long)sender.Value);
        parameters.AddWithValue("@target", (long)target.Value);
    }
}
