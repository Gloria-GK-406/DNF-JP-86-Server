using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Guilds;

internal sealed class SqliteGuildDomainStore(SqliteDatabase database) : IGuildDomainStore
{
    public async ValueTask<bool> ExistsAsync(GuildId guild, CancellationToken ct) =>
        await database.ReadFirstAsync("SELECT name FROM guilds WHERE guild_id=@guild",
            p => BindGuild(p, guild), r => r.GetString(0), ct).ConfigureAwait(false) is not null;

    public async ValueTask<bool> NameTakenAsync(string nameKey, CancellationToken ct) =>
        await database.ReadFirstAsync("SELECT name FROM guilds WHERE name_key=@key",
            p => p.AddWithValue("@key", nameKey), r => r.GetString(0), ct).ConfigureAwait(false) is not null;

    public ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct) =>
        database.ReadFirstAsync("""
            SELECT g.guild_id,g.name,g.promotion,g.level,g.recommended_channel,g.gm_points,
                   m.character_id,m.is_leader,m.joined_at,m.donation_points
            FROM guild_members m JOIN guilds g ON g.guild_id=m.guild_id WHERE m.character_id=@character
            """, p => BindCharacter(p, character), r =>
            {
                var guild = ReadGuild(r);
                return new GuildMembership(guild, ReadMember(r, guild.Id, 6));
            }, ct);

    public ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct) =>
        database.ReadFirstAsync("""
            SELECT guild_id,character_id,message,applied_at FROM guild_applications WHERE character_id=@character
            """, p => BindCharacter(p, applicant), ReadApplication, ct);

    public ValueTask<IReadOnlyList<GuildApplication>> ReadApplicationsAsync(CharacterId leader, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT a.guild_id,a.character_id,a.message,a.applied_at
            FROM guild_members self JOIN guild_applications a ON a.guild_id=self.guild_id
            WHERE self.character_id=@character AND self.is_leader=1
            ORDER BY a.applied_at,a.character_id
            """, p => BindCharacter(p, leader), ReadApplication, ct);

    public ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT m.guild_id,m.character_id,m.is_leader,m.joined_at,m.donation_points
            FROM guild_members self JOIN guild_members m ON m.guild_id=self.guild_id
            WHERE self.character_id=@character ORDER BY m.character_id
            """, p => BindCharacter(p, viewer), r => ReadMember(r, ReadGuildId(r), 1), ct);

    public ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT m.guild_id,m.character_id,m.is_leader,m.joined_at,m.donation_points,
                   c.account_id,COALESCE(c.name_bytes,CAST(c.name AS BLOB)),c.level,c.job,c.grow_type
            FROM guild_members self JOIN guild_members m ON m.guild_id=self.guild_id
            JOIN characters c ON c.character_id=m.character_id AND c.delete_flag=0
            WHERE self.character_id=@character ORDER BY m.character_id
            """, p => BindCharacter(p, viewer), r => new GuildRosterEntry(
                ReadMember(r, ReadGuildId(r), 1),
                new(r.GetInt32(5)), (byte[])r.GetValue(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9)), ct);

    public ValueTask<IReadOnlyList<CharacterId>> AffectedCharactersAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT character_id FROM guild_members WHERE guild_id=@guild
            UNION SELECT character_id FROM guild_applications WHERE guild_id=@guild
            ORDER BY character_id
            """, p => BindGuild(p, guild), r => new CharacterId(r.GetInt32(0)), ct);

    public async ValueTask<Guild> InsertGuildAsync(
        string name, string nameKey, string promotion, DateTimeOffset at, CancellationToken ct) =>
        await database.WriteReturningAsync("""
            INSERT INTO guilds(name,name_key,promotion,created_at) VALUES(@name,@key,@promotion,@at)
            RETURNING guild_id,name,promotion,level,recommended_channel,gm_points
            """, p =>
            {
                p.AddWithValue("@name", name);
                p.AddWithValue("@key", nameKey);
                p.AddWithValue("@promotion", promotion);
                p.AddWithValue("@at", at.ToUnixTimeSeconds());
            }, ReadGuild, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Guild insert returned no identity.");

    public async ValueTask AddMemberAsync(GuildId guild, CharacterId member, bool isLeader, DateTimeOffset at, CancellationToken ct)
    {
        var inserted = await database.ExecuteAsync("""
            INSERT INTO guild_members(guild_id,character_id,is_leader,joined_at) VALUES(@guild,@character,@leader,@at)
            """, p =>
            {
                BindGuild(p, guild);
                BindCharacter(p, member);
                p.AddWithValue("@leader", isLeader);
                p.AddWithValue("@at", at.ToUnixTimeSeconds());
            }, ct).ConfigureAwait(false);
        if (inserted != 1)
            throw new InvalidOperationException("Guild member insert did not persist a member.");
    }

    public async ValueTask AddApplicationAsync(GuildApplication application, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO guild_applications(guild_id,character_id,message,applied_at)
            VALUES(@guild,@character,@message,@at)
            """, p =>
            {
                BindGuild(p, application.Guild);
                BindCharacter(p, application.Character);
                p.AddWithValue("@message", application.Message);
                p.AddWithValue("@at", application.AppliedAt.ToUnixTimeSeconds());
            }, ct).ConfigureAwait(false);

    public async ValueTask DeleteApplicationAsync(CharacterId applicant, CancellationToken ct) =>
        _ = await database.ExecuteAsync("DELETE FROM guild_applications WHERE character_id=@character",
            p => BindCharacter(p, applicant), ct).ConfigureAwait(false);

    public async ValueTask RemoveMemberAsync(GuildId guild, CharacterId member, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            DELETE FROM guild_members WHERE guild_id=@guild AND character_id=@character AND is_leader=0
            """, p => { BindGuild(p, guild); BindCharacter(p, member); }, ct).ConfigureAwait(false);

    public async ValueTask DisbandAsync(GuildId guild, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            DELETE FROM guild_applications WHERE guild_id=@guild;
            DELETE FROM guild_members WHERE guild_id=@guild;
            DELETE FROM guilds WHERE guild_id=@guild;
            """, p => BindGuild(p, guild), ct).ConfigureAwait(false);

    public async ValueTask SetRecommendedChannelAsync(GuildId guild, int channel, CancellationToken ct)
    {
        var changed = await database.ExecuteAsync("UPDATE guilds SET recommended_channel=@channel WHERE guild_id=@guild",
            p => { BindGuild(p, guild); p.AddWithValue("@channel", channel); }, ct).ConfigureAwait(false);
        if (changed != 1)
            throw new InvalidOperationException("Guild channel update did not persist a guild.");
    }

    public async ValueTask AddDonationAsync(GuildId guild, CharacterId donor, int points, CancellationToken ct)
    {
        var guildChanged = await database.ExecuteAsync("""
            UPDATE guilds SET gm_points=gm_points+@points WHERE guild_id=@guild
            """, p =>
            {
                BindGuild(p, guild);
                p.AddWithValue("@points", points);
            }, ct).ConfigureAwait(false);
        if (guildChanged != 1)
            throw new InvalidOperationException("Guild donation did not update the guild.");

        var donorChanged = await database.ExecuteAsync("""
            UPDATE guild_members SET donation_points=donation_points+@points
            WHERE guild_id=@guild AND character_id=@character
            """, p =>
            {
                BindGuild(p, guild);
                BindCharacter(p, donor);
                p.AddWithValue("@points", points);
            }, ct).ConfigureAwait(false);
        if (donorChanged != 1)
            throw new InvalidOperationException("Guild donation did not update the donor.");
    }

    private static Guild ReadGuild(SqliteDataReader row) =>
        new(ReadGuildId(row), row.GetString(1), row.GetString(2), row.GetInt32(3), row.GetInt32(4), row.GetInt32(5));

    private static GuildMember ReadMember(SqliteDataReader row, GuildId guild, int offset) =>
        new(guild, new(row.GetInt32(offset)), row.GetBoolean(offset + 1),
            DateTimeOffset.FromUnixTimeSeconds(row.GetInt64(offset + 2)), row.GetInt32(offset + 3));

    private static GuildApplication ReadApplication(SqliteDataReader row) =>
        new(ReadGuildId(row), new(row.GetInt32(1)), row.GetString(2), DateTimeOffset.FromUnixTimeSeconds(row.GetInt64(3)));

    private static GuildId ReadGuildId(SqliteDataReader row) => new(checked((uint)row.GetInt64(0)));

    private static void BindGuild(SqliteParameterCollection parameters, GuildId guild) =>
        parameters.AddWithValue("@guild", (long)guild.Value);

    private static void BindCharacter(SqliteParameterCollection parameters, CharacterId character) =>
        parameters.AddWithValue("@character", character.Value);
}
