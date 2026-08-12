using System.Text.Json;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Characters;

internal sealed class SqliteDungeonPersistentEffectRecoveryRepository : IDungeonPersistentEffectRecoveryRepository
{
    private const string LuckyStarKind = "suitable-dungeon-lucky-star";
    private const string ExperienceKind = "settlement-experience-grant";
    private const string ScoreExperienceKind = "settlement-score-experience-adjustment";
    private readonly string connectionString;
    private readonly IDungeonExperienceProgressionPolicySource progression;

    public SqliteDungeonPersistentEffectRecoveryRepository(
        string databasePath, IDungeonExperienceProgressionPolicySource progression)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        this.progression = progression;
    }

    public async ValueTask<DungeonPersistentEffectRecoveryOutcome> RecoverAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!await TableExistsAsync(connection, "dungeon_persistent_effect_outbox", cancellationToken)
                .ConfigureAwait(false))
            return new(0, 0, 0, 0);

        var records = new List<(string EventId, string Kind, int Scope, long Target, int Aid, int Version, string Json)>();
        await using (var load = connection.CreateCommand())
        {
            load.CommandText = """
                SELECT source_event_id,effect_kind,effect_scope,scope_target,account_id,payload_version,payload_json
                FROM dungeon_persistent_effect_outbox
                WHERE character_id=@cid AND (state IN (0,3) OR (state=1 AND lease_expires_at<=@now))
                ORDER BY created_at LIMIT 256
                """;
            load.Parameters.AddWithValue("@cid", characterId.Value);
            load.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await using var reader = await load.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                records.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt64(3),
                    reader.GetInt32(4), reader.GetInt32(5), reader.GetString(6)));
        }

        var committed = 0;
        var dead = 0;
        var failed = 0;
        foreach (var record in records)
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            var leaseId = Guid.NewGuid().ToString("N");
            await using (var claim = connection.CreateCommand())
            {
                claim.Transaction = transaction;
                claim.CommandText = """
                    UPDATE dungeon_persistent_effect_outbox SET state=1,lease_id=@lease,
                      lease_owner='select-character',lease_expires_at=@expires,attempt_count=attempt_count+1
                    WHERE source_event_id=@event AND effect_kind=@kind AND effect_scope=@scope
                      AND scope_target=@target AND (state IN(0,3) OR (state=1 AND lease_expires_at<=@now))
                    """;
                claim.Parameters.AddWithValue("@lease", leaseId);
                claim.Parameters.AddWithValue("@expires", DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds());
                claim.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                claim.Parameters.AddWithValue("@event", record.EventId); claim.Parameters.AddWithValue("@kind", record.Kind);
                claim.Parameters.AddWithValue("@scope", record.Scope); claim.Parameters.AddWithValue("@target", record.Target);
                if (await claim.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                { await transaction.RollbackAsync(cancellationToken); continue; }
            }
            if (record.Version != 1 || record.Aid != accountId.Value
                || record.Scope != 3 || record.Target != characterId.Value
                || record.Kind is not (LuckyStarKind or ExperienceKind or ScoreExperienceKind))
            {
                await SetStateAsync(connection, transaction, record, 4,
                    "Unsupported persistent effect kind, version, scope, target, or owner.",
                    cancellationToken, leaseId: leaseId).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                dead++;
                continue;
            }
            try
            {
                var resultJson = "{}";
                if (record.Kind == LuckyStarKind)
                {
                    var payload = JsonSerializer.Deserialize<LuckyStarPayload>(record.Json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? throw new InvalidDataException("Dungeon persistent effect payload is invalid.");
                    if (payload.CharacterId != characterId.Value || payload.AccountId != accountId.Value
                        || payload.Amount != 1)
                        throw new InvalidDataException("Dungeon persistent effect payload identity is invalid.");
                    await using var wallet = connection.CreateCommand(); wallet.Transaction = transaction;
                    wallet.CommandText = "SELECT lucky_star FROM accounts WHERE account_id=@aid";
                    wallet.Parameters.AddWithValue("@aid", accountId.Value);
                    var current = Convert.ToInt32(await wallet.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                    var granted = current < 999;
                    var newTotal = Math.Min(999, current + (granted ? 1 : 0));
                    if (granted)
                        await ExecuteAsync(connection, transaction,
                            "UPDATE accounts SET lucky_star=@total WHERE account_id=@aid",
                            ("@total", newTotal), ("@aid", accountId.Value), cancellationToken).ConfigureAwait(false);
                    resultJson = JsonSerializer.Serialize(new { granted, newTotal });
                }
                else
                    resultJson = await ApplyExperienceAsync(connection, transaction, record, accountId, characterId,
                        progression.Load(), cancellationToken)
                        .ConfigureAwait(false);
                await SetStateAsync(connection, transaction, record, 2, "", cancellationToken,
                    resultJson, leaseId).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                committed++;
            }
            catch (Exception error) when (error is InvalidDataException or JsonException)
            {
                // Permanent validation failures are detected before a domain mutation is
                // accepted. Keep the original lease and attempt increment, and complete the
                // dead letter through that exact claim so another recovery can never be
                // overwritten between rollback and re-claim.
                await SetStateAsync(connection, transaction, record, 4, error.Message,
                    CancellationToken.None, leaseId: leaseId).ConfigureAwait(false);
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                dead++;
            }
            catch
            {
                failed++;
            }
        }
        var remaining = await CountRemainingAsync(connection, characterId, cancellationToken).ConfigureAwait(false);
        return new(committed, dead, failed, remaining);
    }

    private static async ValueTask<string> ApplyExperienceAsync(SqliteConnection connection, SqliteTransaction transaction,
        (string EventId, string Kind, int Scope, long Target, int Aid, int Version, string Json) record,
        AccountId accountId, CharacterId characterId, DungeonExperienceProgressionPolicy policy,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ExperiencePayload>(record.Json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Dungeon experience payload is invalid.");
        if (payload.CharacterId != characterId.Value || payload.AccountId != accountId.Value)
            throw new InvalidDataException("Dungeon experience payload identity is invalid.");
        await using var load = connection.CreateCommand(); load.Transaction = transaction;
        load.CommandText = "SELECT level,exp FROM characters WHERE character_id=@cid";
        load.Parameters.AddWithValue("@cid", characterId.Value);
        await using var reader = await load.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Dungeon experience character is unavailable.");
        var level = reader.GetInt32(0); var exp = reader.GetInt64(1);
        await reader.DisposeAsync().ConfigureAwait(false);
        if (level != payload.ExpectedDatabaseLevel || exp != payload.ExpectedDatabaseExp)
            throw new InvalidDataException("Dungeon experience expected progress does not match persisted progress.");
        // The persisted pair above is solely the stale-write guard. Settlement carries
        // the authoritative in-dungeon progress separately; calculating from the DB pair
        // would discard experience accumulated in the live dungeon session.
        var settlementLevel = payload.PreviousLevel;
        var settlementExp = payload.PreviousExp;
        var maxEntry = policy.LevelThresholds[84];
        var saturated = Math.Min(uint.MaxValue, (ulong)settlementExp + payload.RawGain);
        var honor = settlementLevel >= 86 ? payload.RawGain
            : saturated <= maxEntry ? 0U
            : checked((uint)(saturated - Math.Max((ulong)settlementExp, maxEntry)));
        var normal = payload.RawGain - honor;
        var newExp = Math.Min(uint.MaxValue, (ulong)settlementExp + normal);
        if (settlementLevel >= 86 && payload.NormalizeMaxLevelExp) newExp = maxEntry;
        var normalizedMaximum = settlementLevel >= 86
            && payload.NormalizeMaxLevelExp && settlementExp != maxEntry;
        var newLevel = (int)settlementLevel;
        while (newLevel < 86 && newLevel <= policy.LevelThresholds.Count
               && newExp >= policy.LevelThresholds[newLevel - 1]) newLevel++;
        ulong oldHonor = 0;
        uint oldCapsule = 0;
        if (honor > 0)
        {
            // Resolve every required owner before the first write. Permanent validation
            // errors can then be dead-lettered through the same claim transaction without
            // committing a partial domain mutation.
            await using var account = connection.CreateCommand(); account.Transaction = transaction;
            account.CommandText = "SELECT honor_exp,growth_capsule_exp FROM accounts WHERE account_id=@aid";
            account.Parameters.AddWithValue("@aid", accountId.Value);
            await using var accountReader = await account.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await accountReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("Dungeon experience account is unavailable.");
            oldHonor = checked((ulong)Math.Max(0, accountReader.GetInt64(0)));
            oldCapsule = checked((uint)Math.Max(0, accountReader.GetInt64(1)));
        }
        await ExecuteAsync(connection, transaction,
            "UPDATE characters SET level=@level,exp=@exp WHERE character_id=@cid",
            [("@level", newLevel), ("@exp", (long)newExp), ("@cid", characterId.Value)], cancellationToken)
            .ConfigureAwait(false);
        ulong totalHonor = 0; uint totalCapsule = 0; uint appliedCapsule = 0;
        if (honor > 0)
        {
            totalHonor = Math.Min(policy.MaximumHonorExperience, oldHonor + honor);
            var capsuleGain = checked((uint)Math.Min(uint.MaxValue,
                Math.Max(honor > 0 && policy.GrowthCapsuleRatePercent > 0 ? 1UL : 0UL,
                    (ulong)honor * (uint)policy.GrowthCapsuleRatePercent / 100UL)));
            totalCapsule = Math.Min(policy.GrowthCapsuleMaximum, checked(oldCapsule + capsuleGain));
            appliedCapsule = totalCapsule - Math.Min(oldCapsule, policy.GrowthCapsuleMaximum);
            await ExecuteAsync(connection, transaction,
                "UPDATE accounts SET honor_exp=@honor,growth_capsule_exp=@capsule WHERE account_id=@aid",
                [("@honor", checked((long)totalHonor)), ("@capsule", (long)totalCapsule), ("@aid", accountId.Value)],
                cancellationToken).ConfigureAwait(false);
        }
        return JsonSerializer.Serialize(new
        {
            rawGain = payload.RawGain, honorExpGain = honor, normalExpGain = normal,
            previousLevel = payload.PreviousLevel, previousExp = payload.PreviousExp,
            newLevel, newExp, normalizedMaxLevelExp = normalizedMaximum,
            persisted = normal > 0 || newLevel != settlementLevel || normalizedMaximum,
            growthCapsuleExpGain = appliedCapsule,
            totalHonorExp = totalHonor, totalGrowthCapsuleExp = totalCapsule,
        });
    }

    private static async ValueTask SetStateAsync(SqliteConnection connection, SqliteTransaction transaction,
        (string EventId, string Kind, int Scope, long Target, int Aid, int Version, string Json) record,
        int state, string error, CancellationToken cancellationToken, string resultJson = "{}",
        string? leaseId = null)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            UPDATE dungeon_persistent_effect_outbox SET state=@state,result_version=CASE WHEN @state=2 THEN 1 ELSE NULL END,
              result_json=CASE WHEN @state=2 THEN @result ELSE NULL END,last_error=@error,
              committed_at=CASE WHEN @state=2 THEN @now ELSE NULL END,updated_at=@now,
              lease_id=NULL,lease_owner=NULL,lease_expires_at=0
            WHERE source_event_id=@event AND effect_kind=@kind AND effect_scope=@scope AND scope_target=@target
              AND (@lease IS NULL OR (state=1 AND lease_id=@lease))
            """;
        command.Parameters.AddWithValue("@state", state); command.Parameters.AddWithValue("@error", error);
        command.Parameters.AddWithValue("@result", resultJson);
        command.Parameters.AddWithValue("@lease", leaseId is null ? DBNull.Value : leaseId);
        command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("@event", record.EventId); command.Parameters.AddWithValue("@kind", record.Kind);
        command.Parameters.AddWithValue("@scope", record.Scope); command.Parameters.AddWithValue("@target", record.Target);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("Dungeon persistent effect lost its recovery identity.");
    }

    private static async ValueTask ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction,
        string sql, (string Name, object Value) parameter, CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, transaction, sql, [parameter], cancellationToken).ConfigureAwait(false);
    private static async ValueTask ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction,
        string sql, (string Name, object Value) a, (string Name, object Value) b,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, transaction, sql, [a, b], cancellationToken).ConfigureAwait(false);
    private static async ValueTask ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction,
        string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("Dungeon persistent effect mutation did not affect its exact owner.");
    }

    private static async ValueTask<bool> TableExistsAsync(
        SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name";
        command.Parameters.AddWithValue("@name", table);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async ValueTask<int> CountRemainingAsync(
        SqliteConnection connection, CharacterId characterId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dungeon_persistent_effect_outbox WHERE character_id=@cid AND state IN(0,1,3)";
        command.Parameters.AddWithValue("@cid", characterId.Value);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private sealed record LuckyStarPayload(int CharacterId, int AccountId, int DungeonId, int ClearLevel, int Amount);
    private sealed record ExperiencePayload(int CharacterId, int AccountId, byte PreviousLevel,
        uint PreviousExp, uint RawGain, bool NormalizeMaxLevelExp,
        byte ExpectedDatabaseLevel, uint ExpectedDatabaseExp);
}
