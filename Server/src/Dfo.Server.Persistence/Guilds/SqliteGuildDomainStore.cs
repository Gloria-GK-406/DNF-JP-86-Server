using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Accounts;
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

    public async ValueTask<bool> TryRenameAsync(GuildId guild, string originalName, string name, string nameKey,
        CancellationToken ct) =>
        await database.ExecuteAsync("""
            UPDATE guilds SET name=@name,name_key=@key
            WHERE guild_id=@guild AND name=@original
              AND NOT EXISTS (SELECT 1 FROM guilds WHERE name_key=@key)
            """, p =>
            {
                BindGuild(p, guild);
                p.AddWithValue("@original", originalName);
                p.AddWithValue("@name", name);
                p.AddWithValue("@key", nameKey);
            }, ct).ConfigureAwait(false) == 1;

    public ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct) =>
        database.ReadFirstAsync("""
            SELECT g.guild_id,g.name,g.promotion,g.level,g.recommended_channel,g.gm_points,
                   g.announcement,g.mark_id,g.spent_gm_points,g.medal_aura_symbol,g.medal_aura_frame,
                   m.character_id,m.is_leader,m.joined_at,m.donation_points,m.member_rank,m.memo,m.membership_id
            FROM guild_members m JOIN guilds g ON g.guild_id=m.guild_id WHERE m.character_id=@character
            """, p => BindCharacter(p, character), r =>
            {
                var guild = ReadGuild(r);
                return new GuildMembership(guild, ReadMember(r, guild.Id, 11));
            }, ct);

    public ValueTask<GuildMember?> FindRosterMemberAsync(GuildId guild, byte[] name, CancellationToken ct) =>
        database.ReadFirstAsync("""
            SELECT m.character_id,m.is_leader,m.joined_at,m.donation_points,m.member_rank,m.memo,m.membership_id
            FROM guild_members m JOIN characters c ON c.character_id=m.character_id AND c.delete_flag=0
            WHERE m.guild_id=@guild AND COALESCE(c.name_bytes,CAST(c.name AS BLOB))=@name
            """, p =>
            {
                BindGuild(p, guild);
                p.AddWithValue("@name", name);
            }, r => ReadMember(r, guild, 0), ct);

    public async ValueTask SetMemberRankAsync(GuildId guild, CharacterId character, byte rank, CancellationToken ct)
    {
        var updated = await database.ExecuteAsync("""
            UPDATE guild_members SET member_rank=@rank
            WHERE guild_id=@guild AND character_id=@character AND is_leader=0
            """, p =>
            {
                BindGuild(p, guild);
                BindCharacter(p, character);
                p.AddWithValue("@rank", rank);
            }, ct).ConfigureAwait(false);
        if (updated != 1)
            throw new InvalidOperationException("Guild rank update did not persist an eligible member.");
    }

    public ValueTask TransferLeadershipAsync(GuildLeadershipTransfer transfer, CancellationToken ct) =>
        database.WriteAsync(async (command, token) =>
        {
            BindGuild(command.Parameters, transfer.FormerLeader.Guild);
            command.Parameters.AddWithValue("@leader", transfer.FormerLeader.Character.Value);
            command.Parameters.AddWithValue("@successor", transfer.NewLeader.Character.Value);
            command.Parameters.AddWithValue("@formerIsLeader", transfer.FormerLeader.IsLeader);
            command.Parameters.AddWithValue("@successorIsLeader", transfer.NewLeader.IsLeader);
            command.Parameters.AddWithValue("@formerRank", transfer.FormerLeader.MemberRank);
            command.Parameters.AddWithValue("@successorRank", transfer.NewLeader.MemberRank);

            // The partial unique index checks each update immediately, so demote before promoting in this transaction.
            command.CommandText = """
                UPDATE guild_members SET is_leader=@formerIsLeader,member_rank=@formerRank
                WHERE guild_id=@guild AND character_id=@leader AND is_leader=1
                """;
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The current guild leader changed during the transfer.");

            command.CommandText = """
                UPDATE guild_members SET is_leader=@successorIsLeader,member_rank=@successorRank
                WHERE guild_id=@guild AND character_id=@successor AND is_leader=0
                """;
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The guild successor changed during the transfer.");
        }, ct);

    public async ValueTask<bool> TrySetMemberMemoAsync(CharacterId character, string memo, CancellationToken ct) =>
        await database.ExecuteAsync("""
            UPDATE guild_members SET memo=@memo WHERE character_id=@character
            """, p =>
            {
                BindCharacter(p, character);
                p.AddWithValue("@memo", memo);
            }, ct).ConfigureAwait(false) == 1;

    public ValueTask<GuildDiscoveryDetails?> FindGuildByNameKeyAsync(string nameKey, CancellationToken ct) =>
        database.ReadFirstAsync("""
            SELECT g.guild_id,g.name,g.promotion,(
                SELECT COUNT(*) FROM guild_members m JOIN characters c ON c.character_id=m.character_id
                WHERE m.guild_id=g.guild_id AND c.delete_flag=0)
            FROM guilds g WHERE g.name_key=@name
            """, p => p.AddWithValue("@name", nameKey),
            r => new GuildDiscoveryDetails(ReadGuildId(r), r.GetString(1), r.GetInt32(3), r.GetString(2)), ct);

    public ValueTask<IReadOnlyList<GuildAccountEntry>> ReadAccountGuildsAsync(AccountId account, int limit, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT COALESCE(c.name_bytes,CAST(c.name AS BLOB)),g.name
            FROM characters c JOIN guild_members m ON m.character_id=c.character_id
            JOIN guilds g ON g.guild_id=m.guild_id
            WHERE c.account_id=@account AND c.delete_flag=0
            ORDER BY c.slot_index,c.character_id LIMIT @limit
            """, p =>
            {
                p.AddWithValue("@account", account.Value);
                p.AddWithValue("@limit", limit);
            }, r => new GuildAccountEntry((byte[])r.GetValue(0), r.GetString(1)), ct);

    public ValueTask<IReadOnlyList<GuildRecommendation>> ReadRecommendationsAsync(CharacterId viewer, int limit,
        CancellationToken ct) =>
        database.ReadAllAsync("""
            WITH viewer_guild AS (SELECT guild_id FROM guild_members WHERE character_id=@character),
                 excluded AS (
                    SELECT guild_id FROM viewer_guild
                    UNION ALL
                    SELECT CASE WHEN a.first_id=v.guild_id THEN a.second_id ELSE a.first_id END
                    FROM viewer_guild v JOIN guild_alliances a ON v.guild_id IN (a.first_id,a.second_id)
                 )
            SELECT g.guild_id,g.name,COALESCE(c.name_bytes,CAST(c.name AS BLOB))
            FROM guilds g JOIN guild_members m ON m.guild_id=g.guild_id AND m.is_leader=1
            JOIN characters c ON c.character_id=m.character_id AND c.delete_flag=0
            WHERE g.guild_id NOT IN (SELECT guild_id FROM excluded)
            ORDER BY (SELECT COUNT(*) FROM guild_members n WHERE n.guild_id=g.guild_id) DESC,g.guild_id
            LIMIT @limit
            """, p =>
            {
                BindCharacter(p, viewer);
                p.AddWithValue("@limit", limit);
            },
            r => new GuildRecommendation(ReadGuildId(r), r.GetString(1), (byte[])r.GetValue(2)), ct);

    public ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct) =>
        database.ReadFirstAsync("""
            SELECT guild_id,character_id,message,applied_at FROM guild_applications WHERE character_id=@character
            """, p => BindCharacter(p, applicant), ReadApplication, ct);

    public ValueTask<GuildPendingApplication?> FindPendingApplicationAsync(CharacterId applicant, CancellationToken ct) =>
        database.ReadFirstAsync("""
            SELECT a.guild_id,a.character_id,a.message,a.applied_at,g.name
            FROM guild_applications a JOIN guilds g ON g.guild_id=a.guild_id
            WHERE a.character_id=@character
            """, p => BindCharacter(p, applicant), r => new GuildPendingApplication(ReadApplication(r), r.GetString(4)), ct);

    public ValueTask<IReadOnlyList<GuildApplicationEntry>> ReadApplicationEntriesAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT a.guild_id,a.character_id,a.message,a.applied_at,
                   COALESCE(c.name_bytes,CAST(c.name AS BLOB)),c.job,c.grow_type,c.level
            FROM guild_applications a JOIN characters c ON c.character_id=a.character_id AND c.delete_flag=0
            WHERE a.guild_id=@guild
            ORDER BY a.applied_at,a.character_id
            """, p => BindGuild(p, guild), r => new GuildApplicationEntry(
                ReadApplication(r), (byte[])r.GetValue(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7)), ct);

    public ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT m.guild_id,m.character_id,m.is_leader,m.joined_at,m.donation_points,m.member_rank,m.memo,m.membership_id
            FROM guild_members self JOIN guild_members m ON m.guild_id=self.guild_id
            WHERE self.character_id=@character ORDER BY m.character_id
            """, p => BindCharacter(p, viewer), r => ReadMember(r, ReadGuildId(r), 1), ct);

    public ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT m.guild_id,m.character_id,m.is_leader,m.joined_at,m.donation_points,m.member_rank,m.memo,m.membership_id,
                   c.account_id,COALESCE(c.name_bytes,CAST(c.name AS BLOB)),c.level,c.job,c.grow_type
            FROM guild_members self JOIN guild_members m ON m.guild_id=self.guild_id
            JOIN characters c ON c.character_id=m.character_id AND c.delete_flag=0
            WHERE self.character_id=@character ORDER BY m.character_id
            """, p => BindCharacter(p, viewer), r => new GuildRosterEntry(
                ReadMember(r, ReadGuildId(r), 1),
                new(r.GetInt32(8)), (byte[])r.GetValue(9), r.GetInt32(10), r.GetInt32(11), r.GetInt32(12)), ct);

    public ValueTask<IReadOnlyList<GuildDonationEntry>> ReadDonationHistoryAsync(GuildId guild, int limit, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT donor_name, gold, points, donated_at FROM guild_gold_donations
            WHERE guild_id=@guild ORDER BY donated_at DESC, donation_id DESC LIMIT @limit
            """, p =>
            {
                BindGuild(p, guild);
                p.AddWithValue("@limit", limit);
            }, r => new GuildDonationEntry((byte[])r.GetValue(0), r.GetInt32(1), r.GetInt32(2),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(3))), ct);

    public async ValueTask<bool> RecordAttendanceAsync(CharacterId character, DateOnly day, CancellationToken ct) =>
        await database.ExecuteAsync("""
            INSERT INTO guild_attendance(guild_id, day, character_id)
            SELECT m.guild_id, @day, m.character_id FROM guild_members m
            JOIN characters c ON c.character_id=m.character_id AND c.delete_flag=0
            WHERE m.character_id=@character
            ON CONFLICT(guild_id, day, character_id) DO NOTHING
            """, p =>
            {
                BindCharacter(p, character);
                p.AddWithValue("@day", day.DayNumber);
            }, ct).ConfigureAwait(false) == 1;

    public ValueTask<IReadOnlyList<byte[]>> ReadAttendanceAsync(CharacterId viewer, DateOnly day, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT COALESCE(c.name_bytes, CAST(c.name AS BLOB))
            FROM guild_members self JOIN guild_attendance a ON a.guild_id=self.guild_id AND a.day=@day
            JOIN guild_members m ON m.guild_id=a.guild_id AND m.character_id=a.character_id
            JOIN characters c ON c.character_id=m.character_id AND c.delete_flag=0
            WHERE self.character_id=@character ORDER BY a.character_id
            """, p =>
            {
                BindCharacter(p, viewer);
                p.AddWithValue("@day", day.DayNumber);
            }, r => (byte[])r.GetValue(0), ct);

    public ValueTask<IReadOnlyList<CharacterId>> ReadRosterCharacterIdsAsync(CharacterId member, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT roster.character_id
            FROM guild_members AS member JOIN guild_members AS roster ON roster.guild_id=member.guild_id
            WHERE member.character_id=@character ORDER BY roster.character_id
            """, p => BindCharacter(p, member), r => new CharacterId(r.GetInt32(0)), ct);

    public ValueTask<int> CountAttendingMembersAsync(GuildId guild, DateOnly firstDay, DateOnly lastDay, CancellationToken ct) =>
        database.ReadAsync(async (command, token) =>
        {
            command.CommandText = """
                SELECT COUNT(DISTINCT a.character_id)
                FROM guild_attendance a
                JOIN guild_members m ON m.guild_id=a.guild_id AND m.character_id=a.character_id
                JOIN characters c ON c.character_id=m.character_id AND c.delete_flag=0
                WHERE a.guild_id=@guild AND a.day BETWEEN @firstDay AND @lastDay
                """;
            BindGuild(command.Parameters, guild);
            command.Parameters.AddWithValue("@firstDay", firstDay.DayNumber);
            command.Parameters.AddWithValue("@lastDay", lastDay.DayNumber);
            return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        }, ct);

    public ValueTask<IReadOnlyList<GuildBoardPost>> ReadBoardPostsAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT id,author_character_id,author_name,author_job,author_is_leader,text,posted_at
            FROM guild_board_posts WHERE guild_id=@guild ORDER BY id DESC
            """, p => BindGuild(p, guild), r => new GuildBoardPost(checked((uint)r.GetInt64(0)), new(r.GetInt32(1)),
                (byte[])r.GetValue(2), r.GetByte(3), r.GetBoolean(4), r.GetString(5),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(6))), ct);

    public async ValueTask<bool> TryWriteBoardPostAsync(GuildId guild, CharacterId author, string text,
        DateTimeOffset postedAt, int capacity, CancellationToken ct) =>
        await database.ExecuteAsync("""
            INSERT INTO guild_board_posts(guild_id,author_character_id,author_name,author_job,author_is_leader,text,posted_at)
            SELECT m.guild_id,c.character_id,COALESCE(c.name_bytes, CAST(c.name AS BLOB)),c.job,m.is_leader,@text,@postedAt
            FROM guild_members m JOIN characters c USING(character_id)
            WHERE m.guild_id=@guild AND m.character_id=@character AND c.delete_flag=0
                AND (SELECT COUNT(*) FROM guild_board_posts WHERE guild_id=@guild)<@capacity
            """, p =>
            {
                BindGuild(p, guild);
                BindCharacter(p, author);
                p.AddWithValue("@text", text);
                p.AddWithValue("@postedAt", postedAt.ToUnixTimeSeconds());
                p.AddWithValue("@capacity", capacity);
            }, ct).ConfigureAwait(false) == 1;

    public async ValueTask<bool> DeleteBoardPostAsync(GuildId guild, uint postId, CharacterId? author, CancellationToken ct) =>
        await database.ExecuteAsync("""
            DELETE FROM guild_board_posts WHERE guild_id=@guild AND id=@id
                AND (@author IS NULL OR author_character_id=@author)
            """, p =>
            {
                BindGuild(p, guild);
                p.AddWithValue("@id", (long)postId);
                p.AddWithValue("@author", author is { } character ? character.Value : DBNull.Value);
            }, ct).ConfigureAwait(false) == 1;

    public ValueTask<IReadOnlyList<CharacterId>> ReadMemberIdsAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAllAsync("SELECT character_id FROM guild_members WHERE guild_id=@guild ORDER BY character_id",
            p => BindGuild(p, guild), r => new CharacterId(r.GetInt32(0)), ct);

    public ValueTask<IReadOnlyList<CharacterId>> ReadDisbandRecipientsAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT character_id FROM guild_members WHERE guild_id=@guild
            UNION SELECT character_id FROM guild_applications WHERE guild_id=@guild
            ORDER BY character_id
            """, p => BindGuild(p, guild), r => new CharacterId(r.GetInt32(0)), ct);

    public async ValueTask<Guild> InsertGuildAsync(
        string name, string nameKey, string promotion, DateTimeOffset at, CancellationToken ct) =>
        await database.WriteReturningAsync("""
            INSERT INTO guilds(name,name_key,promotion,created_at) VALUES(@name,@key,@promotion,@at)
            RETURNING guild_id,name,promotion,level,recommended_channel,gm_points,announcement,mark_id,spent_gm_points,
                      medal_aura_symbol,medal_aura_frame
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

    public ValueTask<IReadOnlyList<GuildRankOverride>> ReadRankSettingsAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT rank, permission_mask, name FROM guild_rank_settings WHERE guild_id=@guild ORDER BY rank
            """, p => BindGuild(p, guild), r => new GuildRankOverride(
                checked((byte)r.GetInt32(0)), r.IsDBNull(1) ? null : checked((uint)r.GetInt64(1)),
                r.IsDBNull(2) ? null : r.GetString(2)), ct);

    public async ValueTask SetPromotionAsync(GuildId guild, string promotion, CancellationToken ct)
    {
        var updated = await database.ExecuteAsync("UPDATE guilds SET promotion=@promotion WHERE guild_id=@guild", p =>
        {
            BindGuild(p, guild);
            p.AddWithValue("@promotion", promotion);
        }, ct).ConfigureAwait(false);
        if (updated != 1)
            throw new InvalidOperationException("Guild promotion update did not persist a guild.");
    }

    public async ValueTask SetAnnouncementAsync(GuildId guild, string announcement, CancellationToken ct)
    {
        var updated = await database.ExecuteAsync("UPDATE guilds SET announcement=@text WHERE guild_id=@guild", p =>
        {
            BindGuild(p, guild);
            p.AddWithValue("@text", announcement);
        }, ct).ConfigureAwait(false);
        if (updated != 1)
            throw new InvalidOperationException("Guild announcement update did not persist a guild.");
    }

    public async ValueTask SetMarkAsync(GuildId guild, byte mark, CancellationToken ct)
    {
        var changed = await database.ExecuteAsync("UPDATE guilds SET mark_id=@mark WHERE guild_id=@guild", p =>
        {
            BindGuild(p, guild);
            p.AddWithValue("@mark", mark);
        }, ct).ConfigureAwait(false);
        if (changed != 1)
            throw new InvalidOperationException("Guild mark could not be updated.");
    }

    public async ValueTask SetRankNameAsync(GuildId guild, byte rank, string name, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO guild_rank_settings(guild_id, rank, name) VALUES(@guild, @rank, @name)
            ON CONFLICT(guild_id, rank) DO UPDATE SET name=excluded.name
            """, p =>
            {
                BindGuild(p, guild);
                p.AddWithValue("@rank", rank);
                p.AddWithValue("@name", name);
            }, ct).ConfigureAwait(false);

    public async ValueTask SetRankPermissionsAsync(GuildId guild, byte rank, uint permissions, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO guild_rank_settings(guild_id, rank, permission_mask) VALUES(@guild, @rank, @permissions)
            ON CONFLICT(guild_id, rank) DO UPDATE SET permission_mask=excluded.permission_mask
            """, p =>
            {
                BindGuild(p, guild);
                p.AddWithValue("@rank", rank);
                p.AddWithValue("@permissions", (long)permissions);
            }, ct).ConfigureAwait(false);

    public async ValueTask SetRecommendedChannelAsync(GuildId guild, int channel, CancellationToken ct)
    {
        var changed = await database.ExecuteAsync("UPDATE guilds SET recommended_channel=@channel WHERE guild_id=@guild",
            p => { BindGuild(p, guild); p.AddWithValue("@channel", channel); }, ct).ConfigureAwait(false);
        if (changed != 1)
            throw new InvalidOperationException("Guild channel update did not persist a guild.");
    }

    public ValueTask<IReadOnlyList<GuildPurchasedContent>> ReadContentsAsync(GuildId guild, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT kind,product_id,value,remaining_uses,expires_at FROM guild_contents
            WHERE guild_id=@guild ORDER BY kind
            """, p => BindGuild(p, guild), r => new GuildPurchasedContent(
                (GuildContentKind)r.GetInt32(0), checked((uint)r.GetInt64(1)), r.GetInt32(2), r.GetInt32(3), r.GetInt64(4)), ct);

    public ValueTask<int?> ReadMonsterExperiencePercentAsync(CharacterId character, long now, CancellationToken ct) =>
        database.ReadAsync<int?>(async (command, token) =>
        {
            command.CommandText = """
                SELECT c.value FROM guild_members m
                JOIN guild_contents c ON c.guild_id=m.guild_id AND c.kind=@kind
                WHERE m.character_id=@character AND c.expires_at>@now
                """;
            BindCharacter(command.Parameters, character);
            command.Parameters.AddWithValue("@kind", (int)GuildContentKind.Experience);
            command.Parameters.AddWithValue("@now", now);
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is long percent
                ? checked((int)percent) : null;
        }, ct);

    public async ValueTask<IReadOnlyList<GuildExpiredContent>> RemoveExpiredContentsAsync(
        long now, int limit, CancellationToken ct)
    {
        var expired = new List<GuildExpiredContent>();
        await database.WriteAsync(async (command, token) =>
        {
            command.CommandText = """
                DELETE FROM guild_contents WHERE rowid IN (
                    SELECT rowid FROM guild_contents WHERE expires_at>0 AND expires_at<=@now
                    ORDER BY expires_at, guild_id, kind LIMIT @limit)
                RETURNING guild_id,kind,value
                """;
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@limit", limit);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                expired.Add(new(new(checked((uint)reader.GetInt64(0))), (GuildContentKind)reader.GetInt32(1), reader.GetInt32(2)));
        }, ct).ConfigureAwait(false);
        return expired;
    }

    public async ValueTask PutContentAsync(GuildId guild, GuildPurchasedContent content, CancellationToken ct)
    {
        var changed = await database.ExecuteAsync("""
            INSERT INTO guild_contents(guild_id,kind,product_id,value,remaining_uses,expires_at)
            VALUES(@guild,@kind,@product,@value,@uses,@expiry)
            ON CONFLICT(guild_id,kind) DO UPDATE SET product_id=excluded.product_id,value=excluded.value,
                remaining_uses=excluded.remaining_uses,expires_at=excluded.expires_at
            """, p =>
            {
                BindGuild(p, guild);
                p.AddWithValue("@kind", (int)content.Kind);
                p.AddWithValue("@product", (long)content.ProductId);
                p.AddWithValue("@value", content.Value);
                p.AddWithValue("@uses", content.RemainingUses);
                p.AddWithValue("@expiry", content.ExpiresAtUnixSeconds);
            }, ct).ConfigureAwait(false);
        if (changed != 1)
            throw new InvalidOperationException("Guild content purchase did not persist an entitlement.");
    }

    public async ValueTask<bool> TryConsumeTeleportAsync(GuildId guild, CancellationToken ct)
    {
        void Bind(SqliteParameterCollection p)
        {
            BindGuild(p, guild);
            p.AddWithValue("@kind", (int)GuildContentKind.PartyTeleport);
        }

        if (await database.ExecuteAsync("""
            DELETE FROM guild_contents WHERE guild_id=@guild AND kind=@kind AND remaining_uses=1
            """, Bind, ct).ConfigureAwait(false) == 1)
            return true;

        return await database.ExecuteAsync("""
            UPDATE guild_contents SET remaining_uses=remaining_uses-1
            WHERE guild_id=@guild AND kind=@kind AND remaining_uses>1
            """, Bind, ct).ConfigureAwait(false) == 1;
    }

    public async ValueTask AddDonationAsync(
        GuildId guild, CharacterId donor, int gold, int points, DateTimeOffset at, CancellationToken ct)
    {
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

        var recorded = await database.ExecuteAsync("""
            INSERT INTO guild_gold_donations(guild_id, donor_character_id, donor_name, gold, points, donated_at, membership_id)
            SELECT m.guild_id, c.character_id, COALESCE(c.name_bytes, CAST(c.name AS BLOB)), @gold, @points, @at, m.membership_id
            FROM characters c JOIN guild_members m USING(character_id)
            WHERE c.character_id=@character AND c.delete_flag=0 AND m.guild_id=@guild
            """, p =>
            {
                BindGuild(p, guild);
                BindCharacter(p, donor);
                p.AddWithValue("@gold", gold);
                p.AddWithValue("@points", points);
                p.AddWithValue("@at", at.ToUnixTimeSeconds());
            }, ct).ConfigureAwait(false);
        if (recorded != 1)
            throw new InvalidOperationException("Guild donation did not record the history entry.");
    }

    public async ValueTask<bool> TrySetMedalAuraAsync(GuildId guild, CharacterId leader, GuildMedalAura aura, CancellationToken ct) =>
        await database.ExecuteAsync("""
            UPDATE guilds SET medal_aura_symbol=@symbol, medal_aura_frame=@frame
            WHERE guild_id=@guild AND EXISTS (
                SELECT 1 FROM guild_members WHERE guild_id=@guild AND character_id=@character AND is_leader=1)
            """, p =>
            {
                BindGuild(p, guild);
                BindCharacter(p, leader);
                p.AddWithValue("@symbol", aura.Symbol);
                p.AddWithValue("@frame", aura.Frame);
            }, ct).ConfigureAwait(false) == 1;

    private static Guild ReadGuild(SqliteDataReader row) =>
        new(ReadGuildId(row), row.GetString(1), row.GetString(2), row.GetInt32(3), row.GetInt32(4), row.GetInt32(5),
            row.GetString(6), checked((byte)row.GetInt32(7)), row.GetInt32(8))
        {
            MedalAura = new(row.GetInt32(9), row.GetInt32(10)),
        };

    private static GuildMember ReadMember(SqliteDataReader row, GuildId guild, int offset) =>
        new(guild, new(row.GetInt32(offset)), row.GetBoolean(offset + 1),
            DateTimeOffset.FromUnixTimeSeconds(row.GetInt64(offset + 2)), row.GetInt32(offset + 3),
            checked((byte)row.GetInt32(offset + 4)), row.GetString(offset + 5))
        { MembershipId = row.GetInt64(offset + 6) };

    private static GuildApplication ReadApplication(SqliteDataReader row) =>
        new(ReadGuildId(row), new(row.GetInt32(1)), row.GetString(2), DateTimeOffset.FromUnixTimeSeconds(row.GetInt64(3)));

    private static GuildId ReadGuildId(SqliteDataReader row) => new(checked((uint)row.GetInt64(0)));

    private static void BindGuild(SqliteParameterCollection parameters, GuildId guild) =>
        parameters.AddWithValue("@guild", (long)guild.Value);

    private static void BindCharacter(SqliteParameterCollection parameters, CharacterId character) =>
        parameters.AddWithValue("@character", character.Value);
}
