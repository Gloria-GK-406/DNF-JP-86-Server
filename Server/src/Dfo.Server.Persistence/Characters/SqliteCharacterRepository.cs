using System.Globalization;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteCharacterRepository : ICharacterRepository
{
    private readonly string connectionString;

    public SqliteCharacterRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path cannot be empty.", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        EnsureSchema();
    }

    /// <summary>
    /// A new character enters at full health. Written explicitly because the column's schema
    /// default is zero, which the client renders as an empty bar under a full maximum.
    /// </summary>
    private const int FullHpMpPercent = 100;

    public async ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ID must be positive.");
        }

        var characters = new List<CharacterSummary>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id,
                   slot_index,
                   COALESCE(name_bytes, CAST(name AS BLOB)),
                   job,
                   grow_type,
                   level,
                   clone_title_item_id
            FROM characters
            WHERE account_id = @accountId AND delete_flag = 0
            ORDER BY slot_index, character_id;
            """;
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            characters.Add(new CharacterSummary(
                new CharacterId(reader.GetInt32(0)),
                checked((ushort)reader.GetInt32(1)),
                reader.IsDBNull(2) ? [] : (byte[])reader.GetValue(2),
                checked((byte)reader.GetInt32(3)),
                checked((byte)reader.GetInt32(4)),
                checked((byte)reader.GetInt32(5)),
                reader.IsDBNull(6) ? 0u : checked((uint)reader.GetInt64(6))));
        }

        return characters;
    }

    public async ValueTask<ActiveCharacterState?> FindByAccountAndSlotAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken)
    {
        if (accountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ID must be positive.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id,
                   slot_index,
                   COALESCE(name_bytes, CAST(name AS BLOB)),
                   job,
                   grow_type,
                   level
            FROM characters
            WHERE account_id = @accountId
              AND slot_index = @slotIndex
              AND delete_flag = 0
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        command.Parameters.AddWithValue("@slotIndex", slotIndex);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var characterId = new CharacterId(reader.GetInt32(0));
        return new ActiveCharacterState(
            new CharacterIdentityState(
                characterId,
                checked((ushort)reader.GetInt32(1)),
                reader.IsDBNull(2) ? [] : (byte[])reader.GetValue(2),
                checked((byte)reader.GetInt32(3)),
                checked((byte)reader.GetInt32(4))),
            new CharacterProgressionState(checked((byte)reader.GetInt32(5)), 0),
            new CharacterFatigueState(188, 0),
            new CharacterPositionState(0, 0, 0, 0, 5),
            DateTimeOffset.UnixEpoch,
            DarkKnightCombo: null);
    }

    public async ValueTask<bool> NameExistsAsync(byte[] name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Compared as a blob, not as text. A name is whatever bytes the client sent, and two
        // distinct byte sequences can share a decoded form; matching on TEXT would let one of them
        // masquerade as the other.
        command.CommandText = """
            SELECT 1
            FROM characters
            WHERE COALESCE(name_bytes, CAST(name AS BLOB)) = @name
              AND delete_flag = 0
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@name", name);
        var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return found is not null and not DBNull;
    }

    public async ValueTask<CharacterId> CreateAsync(
        NewCharacterSpec spec,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.AccountId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), "Account ID must be positive.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // name and name_bytes are both written: name_bytes is the authority every read path uses
        // through COALESCE, while name stays populated because the column is NOT NULL and remains
        // the only human-readable handle when inspecting the database by hand.
        command.CommandText = """
            INSERT INTO characters (account_id, name, name_bytes, job, grow_type, level, slot_index)
            VALUES (@accountId, @name, @nameBytes, @job, 0, 1, @slotIndex)
            RETURNING character_id;
            """;
        command.Parameters.AddWithValue("@accountId", spec.AccountId.Value);
        command.Parameters.AddWithValue("@name", DecodeForDisplay(spec.Name));
        command.Parameters.AddWithValue("@nameBytes", spec.Name);
        command.Parameters.AddWithValue("@job", spec.Job);
        command.Parameters.AddWithValue("@slotIndex", spec.SlotIndex);
        var created = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Character insert returned no identifier.");
        var characterId = Convert.ToInt32(created, CultureInfo.InvariantCulture);

        await InsertInitialSkillsAsync(
            connection, transaction, characterId, spec.InitialSkills, cancellationToken)
            .ConfigureAwait(false);

        await InsertRequiredEntryStateAsync(
            connection, transaction, characterId, spec.Stats, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CharacterId(characterId);
    }

    /// <summary>
    /// Writes the three entry-state rows without which the character cannot be entered.
    /// </summary>
    /// <remarks>
    /// The entry profile query inner-joins <c>character_subtype0_fields</c>,
    /// <c>character_subtype1_fields</c> and <c>character_init_flags</c>, and reports their absence
    /// as "incomplete required profile state". That is an assertion about corruption, not a
    /// tolerated case, so a character row must never exist without them -- which is why this runs
    /// inside the same transaction as the character insert rather than as a second step. Two
    /// connections cannot share a SQLite transaction, so a follow-up call from the service layer
    /// could leave exactly the half-built character the assertion is meant to catch.
    ///
    /// Every NOT NULL column carries a schema default, so those need no values here -- and the
    /// defaults are not all zero (<c>user_state_bits</c> 3, <c>pc_room_id</c> 65537,
    /// <c>return_user_flag</c> 1, <c>skill_tree_index</c> -1), which is why they are left to the
    /// schema rather than restated at the insert.
    ///
    /// <c>pc_room_state</c> is the exception and must be written. It is the one nullable column
    /// whose reader refuses NULL: <c>SqliteAccountEntryStateRepository.LoadPcRoomStatesAsync</c>
    /// left-joins this table for every live character on the account and throws "PC-room
    /// compatibility state is unavailable" on a null. The other two nullable columns
    /// (<c>character_option_blob</c> here, <c>creature_buffer</c> in subtype0) are read through
    /// helpers that accept null explicitly. Upstream declares this column NOT NULL DEFAULT 0; our
    /// schema does not, so the value is supplied here instead of relying on a default we do not
    /// have. Zero means "not in a PC room", which is what a fresh character is.
    ///
    /// The combat stats are the other exception. They have schema defaults, but the default is
    /// zero and a character with zero maximum HP cannot act, so they are written from the job's
    /// archive entry that <paramref name="stats"/> carries. Nothing recomputes them afterwards:
    /// a character is created at level one and no path advances a level yet.
    ///
    /// <c>current_hp_mp_percent</c> is the third, and its default of zero is the worst of the
    /// three: it is the percentage of maximum HP/MP the character enters holding, so a zero
    /// presents full maxima alongside an empty bar.
    /// </remarks>
    /// <summary>
    /// Writes the character's starting skills onto page zero.
    /// </summary>
    /// <remarks>
    /// In the creating transaction, alongside the rows the entry query joins against. This project
    /// has already paid once for splitting a character's creation across transactions: a character
    /// was created with only its <c>characters</c> row and could not enter the game, because the
    /// entry profile inner-joins three further tables. A character whose skills were written
    /// separately would fail in the same shape, and only for the characters whose second write
    /// failed.
    /// </remarks>
    private static async ValueTask InsertInitialSkillsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int characterId,
        IReadOnlyList<NewCharacterSkill>? skills,
        CancellationToken cancellationToken)
    {
        if (skills is null || skills.Count == 0)
            return;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO character_skills (character_id, page_index, slot, skill_id, level, extra_values)
            VALUES (@characterId, 0, @slot, @skillId, @level, NULL);
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        var slot = command.Parameters.Add("@slot", SqliteType.Integer);
        var skillId = command.Parameters.Add("@skillId", SqliteType.Integer);
        var level = command.Parameters.Add("@level", SqliteType.Integer);
        foreach (var skill in skills)
        {
            slot.Value = skill.Slot;
            skillId.Value = skill.SkillId;
            level.Value = skill.Level;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask InsertRequiredEntryStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int characterId,
        CharacterBaseStats stats,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO character_subtype0_fields (character_id) VALUES (@characterId)
                ON CONFLICT(character_id) DO NOTHING;
            INSERT INTO character_subtype1_fields (
                character_id, stat_hp_max, stat_mp_max,
                stat_physical_attack, stat_physical_defense,
                stat_magical_attack, stat_magical_defense,
                stat_fire_resistance, stat_water_resistance,
                stat_dark_resistance, stat_light_resistance,
                stat_inventory_limit, stat_hp_regen_speed, stat_mp_regen_speed,
                stat_move_speed, stat_attack_speed, stat_cast_speed,
                stat_hit_recovery, stat_jump_power, stat_weight, current_hp_mp_percent)
                VALUES (
                    @characterId, @hpMax, @mpMax,
                    @physicalAttack, @physicalDefense,
                    @magicalAttack, @magicalDefense,
                    @fireResistance, @waterResistance,
                    @darkResistance, @lightResistance,
                    @inventoryLimit, @hpRegenSpeed, @mpRegenSpeed,
                    @moveSpeed, @attackSpeed, @castSpeed,
                    @hitRecovery, @jumpPower, @weight, @currentHpMpPercent)
                ON CONFLICT(character_id) DO NOTHING;
            INSERT INTO character_init_flags (character_id, pc_room_state)
                VALUES (@characterId, 0)
                ON CONFLICT(character_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@hpMax", stats.HpMax);
        command.Parameters.AddWithValue("@mpMax", stats.MpMax);
        command.Parameters.AddWithValue("@physicalAttack", stats.PhysicalAttack);
        command.Parameters.AddWithValue("@physicalDefense", stats.PhysicalDefense);
        command.Parameters.AddWithValue("@magicalAttack", stats.MagicalAttack);
        command.Parameters.AddWithValue("@magicalDefense", stats.MagicalDefense);
        command.Parameters.AddWithValue("@fireResistance", stats.FireResistance);
        command.Parameters.AddWithValue("@waterResistance", stats.WaterResistance);
        command.Parameters.AddWithValue("@darkResistance", stats.DarkResistance);
        command.Parameters.AddWithValue("@lightResistance", stats.LightResistance);
        command.Parameters.AddWithValue("@inventoryLimit", stats.InventoryLimit);
        command.Parameters.AddWithValue("@hpRegenSpeed", stats.HpRegenSpeed);
        command.Parameters.AddWithValue("@mpRegenSpeed", stats.MpRegenSpeed);
        command.Parameters.AddWithValue("@moveSpeed", stats.MoveSpeed);
        command.Parameters.AddWithValue("@attackSpeed", stats.AttackSpeed);
        command.Parameters.AddWithValue("@castSpeed", stats.CastSpeed);
        command.Parameters.AddWithValue("@hitRecovery", stats.HitRecovery);
        command.Parameters.AddWithValue("@jumpPower", stats.JumpPower);
        command.Parameters.AddWithValue("@weight", stats.Weight);
        command.Parameters.AddWithValue("@currentHpMpPercent", FullHpMpPercent);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> SoftDeleteAsync(
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE characters
            SET delete_flag = 1
            WHERE character_id = @characterId AND delete_flag = 0;
            """;
        command.Parameters.AddWithValue("@characterId", characterId.Value);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>Best-effort text form for the NOT NULL <c>name</c> column.</summary>
    /// <remarks>
    /// Never used for matching. Undecodable bytes fall back to hex so the row stays inspectable
    /// instead of carrying replacement characters that look like real content.
    /// </remarks>
    private static string DecodeForDisplay(byte[] name)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(name);
        }
        catch (DecoderFallbackException)
        {
            return Convert.ToHexString(name);
        }
    }

    private void EnsureSchema()
    {
        CharacterDatabaseSchema.Ensure(connectionString);
    }
}
