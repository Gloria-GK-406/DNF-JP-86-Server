using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

/// <summary>
/// The <c>dungeon_persistent_effect_outbox</c> table.
/// </summary>
/// <remarks>
/// Every statement here names all four columns that identify an effect, and the two that act on a
/// claimed one also name the lease. That is what keeps one recovery from finishing an effect
/// another recovery has taken: the row it names is the row it claimed, or no row at all.
/// </remarks>
internal sealed class SqliteDungeonEffectOutboxTable(SqliteDatabase database)
    : IDungeonEffectOutboxTable
{
    /// <summary>How many are taken in one go.</summary>
    /// <remarks>
    /// A character with more waiting than this gets the rest on the next attempt; that is what the
    /// count of unfinished ones is for. The limit keeps one recovery bounded.
    /// </remarks>
    private const int Batch = 256;

    public ValueTask<IReadOnlyList<DungeonEffectOutboxRow>> ReadClaimableAsync(
        CharacterId characterId, long nowUnixMilliseconds, CancellationToken cancellationToken) =>
        database.ReadAllAsync(
            $"""
            SELECT source_event_id,effect_kind,effect_scope,scope_target,account_id,payload_version,payload_json
            FROM dungeon_persistent_effect_outbox
            WHERE character_id=@cid AND (state IN (0,3) OR (state=1 AND lease_expires_at<=@now))
            ORDER BY created_at LIMIT {Batch}
            """,
            parameters =>
            {
                parameters.AddWithValue("@cid", characterId.Value);
                parameters.AddWithValue("@now", nowUnixMilliseconds);
            },
            reader => new DungeonEffectOutboxRow(
                reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt64(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetString(6)),
            cancellationToken);

    public async ValueTask<bool> TryClaimAsync(
        DungeonEffectOutboxRow effect,
        string leaseId,
        long leaseExpiresAtUnixMilliseconds,
        long nowUnixMilliseconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var touched = await database.ExecuteAsync(
            """
            UPDATE dungeon_persistent_effect_outbox SET state=1,lease_id=@lease,
              lease_owner='select-character',lease_expires_at=@expires,attempt_count=attempt_count+1
            WHERE source_event_id=@event AND effect_kind=@kind AND effect_scope=@scope
              AND scope_target=@target AND (state IN(0,3) OR (state=1 AND lease_expires_at<=@now))
            """,
            parameters =>
            {
                parameters.AddWithValue("@lease", leaseId);
                parameters.AddWithValue("@expires", leaseExpiresAtUnixMilliseconds);
                parameters.AddWithValue("@now", nowUnixMilliseconds);
                Identify(parameters, effect);
            },
            cancellationToken).ConfigureAwait(false);
        return touched == 1;
    }

    public async ValueTask SettleAsync(
        DungeonEffectOutboxRow effect,
        int state,
        string failureReason,
        string resultJson,
        string leaseId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var touched = await database.ExecuteAsync(
            """
            UPDATE dungeon_persistent_effect_outbox SET state=@state,result_version=CASE WHEN @state=2 THEN 1 ELSE NULL END,
              result_json=CASE WHEN @state=2 THEN @result ELSE NULL END,last_error=@error,
              committed_at=CASE WHEN @state=2 THEN @now ELSE NULL END,updated_at=@now,
              lease_id=NULL,lease_owner=NULL,lease_expires_at=0
            WHERE source_event_id=@event AND effect_kind=@kind AND effect_scope=@scope AND scope_target=@target
              AND (@lease IS NULL OR (state=1 AND lease_id=@lease))
            """,
            parameters =>
            {
                parameters.AddWithValue("@state", state);
                parameters.AddWithValue("@error", failureReason);
                parameters.AddWithValue("@result", resultJson);
                parameters.AddWithValue("@lease", leaseId);
                parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Identify(parameters, effect);
            },
            cancellationToken).ConfigureAwait(false);
        if (touched != 1)
        {
            throw new InvalidDataException(
                "Dungeon persistent effect lost its recovery identity.");
        }
    }

    public async ValueTask<int> CountUnfinishedAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            "SELECT COUNT(*) FROM dungeon_persistent_effect_outbox "
            + "WHERE character_id=@cid AND state IN(0,1,3)",
            parameters => parameters.AddWithValue("@cid", characterId.Value),
            reader => (object)reader.GetInt32(0),
            cancellationToken).ConfigureAwait(false) is int count ? count : 0;

    private static void Identify(
        Microsoft.Data.Sqlite.SqliteParameterCollection parameters, DungeonEffectOutboxRow effect)
    {
        parameters.AddWithValue("@event", effect.SourceEventId);
        parameters.AddWithValue("@kind", effect.EffectKind);
        parameters.AddWithValue("@scope", effect.EffectScope);
        parameters.AddWithValue("@target", effect.ScopeTarget);
    }
}
