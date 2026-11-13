using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Relations;

/// <summary>The <c>character_relations</c> table.</summary>
/// <remarks>
/// Reads join <c>characters</c> so that a deleted character drops out of every list and count the
/// moment it is marked, whether or not its rows have been purged yet. Point questions
/// (<see cref="ExistsAsync"/>, inserts, deletes) look at the row alone; deciding whether a
/// character is live is the domain service's, through the identity read.
/// </remarks>
internal sealed class SqliteCharacterRelationDomainStore(SqliteDatabase database) : ICharacterRelationDomainStore
{
    public ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(
        CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        database.ReadAllAsync("""
            SELECT r.target_id,r.created_at FROM character_relations r
            JOIN characters c ON c.character_id=r.target_id AND c.delete_flag=0
            WHERE r.owner_id=@owner AND r.kind=@kind ORDER BY r.created_at,r.target_id
            """,
            p => { p.AddWithValue("@owner", owner.Value); p.AddWithValue("@kind", (int)kind); },
            r => new CharacterRelation(new CharacterId(r.GetInt32(0)), DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(1))),
            cancellationToken);

    public ValueTask<IReadOnlyList<CharacterId>> OwnersOfAsync(
        CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        database.ReadAllAsync(LiveOwnersOf,
            p => { p.AddWithValue("@target", target.Value); p.AddWithValue("@kind", (int)kind); },
            r => new CharacterId(r.GetInt32(0)), cancellationToken);

    public async ValueTask<bool> ExistsAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        (await database.ReadAllAsync(
            "SELECT 1 FROM character_relations WHERE owner_id=@owner AND kind=@kind AND target_id=@target",
            p => Bind(p, owner, target, kind), r => r.GetInt32(0), cancellationToken).ConfigureAwait(false)).Count != 0;

    public async ValueTask<int> CountAsync(CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        (await database.ReadAllAsync("""
            SELECT COUNT(*) FROM character_relations r
            JOIN characters c ON c.character_id=r.target_id AND c.delete_flag=0
            WHERE r.owner_id=@owner AND r.kind=@kind
            """,
            p => { p.AddWithValue("@owner", owner.Value); p.AddWithValue("@kind", (int)kind); },
            r => r.GetInt32(0), cancellationToken).ConfigureAwait(false))[0];

    /// <remarks>
    /// Only a primary-key conflict counts as "already there". A self relation or an unknown
    /// character still fails on the table's own constraints, because reaching here with one is a
    /// caller that skipped the rules, not a repeat.
    /// </remarks>
    public async ValueTask<bool> InsertAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, DateTimeOffset at,
        CancellationToken cancellationToken) =>
        await database.ExecuteAsync("""
            INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES(@owner,@target,@kind,@at)
            ON CONFLICT(owner_id,kind,target_id) DO NOTHING
            """,
            p => { Bind(p, owner, target, kind); p.AddWithValue("@at", at.ToUnixTimeSeconds()); },
            cancellationToken).ConfigureAwait(false) == 1;

    public async ValueTask<bool> DeleteAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        await database.ExecuteAsync(
            "DELETE FROM character_relations WHERE owner_id=@owner AND kind=@kind AND target_id=@target",
            p => Bind(p, owner, target, kind), cancellationToken).ConfigureAwait(false) == 1;

    /// <remarks>
    /// The owners are read before the delete and in the same transaction, so the answer names exactly
    /// the rows that were removed. They are read through the write path: a purge is one change, and
    /// reading its own answer outside the use case would let it describe another moment.
    /// </remarks>
    public async ValueTask<IReadOnlyList<CharacterId>> PurgeAsync(CharacterId character, CancellationToken cancellationToken)
    {
        var owners = new List<CharacterId>();
        await database.WriteAsync(async (command, token) =>
        {
            command.CommandText = LiveOwnersOf;
            command.Parameters.AddWithValue("@target", character.Value);
            command.Parameters.AddWithValue("@kind", (int)CharacterRelationKind.Friend);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) owners.Add(new CharacterId(reader.GetInt32(0)));
        }, cancellationToken).ConfigureAwait(false);
        await database.ExecuteAsync(
            "DELETE FROM character_relations WHERE owner_id=@character OR target_id=@character",
            p => p.AddWithValue("@character", character.Value), cancellationToken).ConfigureAwait(false);
        return owners;
    }

    private const string LiveOwnersOf = """
        SELECT r.owner_id FROM character_relations r
        JOIN characters c ON c.character_id=r.owner_id AND c.delete_flag=0
        WHERE r.target_id=@target AND r.kind=@kind ORDER BY r.owner_id
        """;

    private static void Bind(SqliteParameterCollection p, CharacterId owner, CharacterId target, CharacterRelationKind kind)
    {
        p.AddWithValue("@owner", owner.Value);
        p.AddWithValue("@target", target.Value);
        p.AddWithValue("@kind", (int)kind);
    }
}
