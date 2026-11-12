using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Characters;

/// <summary>Live characters' public identity, by exact name or by a set of identifiers.</summary>
/// <remarks>
/// The one name lookup on the server -- mail, whispers, 1:1 conversations, friends and blacklists all
/// resolve through it: the stored bytes, falling back to the legacy text column cast to bytes, compared exactly. A name is only unique among live characters, so the
/// deleted ones are excluded in the statement rather than after it.
/// </remarks>
internal sealed class SqliteCharacterIdentityRepository(SqliteDatabase database) : ICharacterIdentityRepository
{
    private const string Columns = "character_id,COALESCE(name_bytes,CAST(name AS BLOB)),level,job,grow_type";

    /// <summary>Identifiers per statement, well under SQLite's bound on parameters.</summary>
    private const int Batch = 500;

    public ValueTask<CharacterPublicIdentity?> FindLiveByNameAsync(byte[] name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        return database.ReadFirstAsync(
            $"SELECT {Columns} FROM characters WHERE delete_flag=0 AND COALESCE(name_bytes,CAST(name AS BLOB))=@name LIMIT 1",
            p => p.AddWithValue("@name", name), Row, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<CharacterPublicIdentity>> ReadLiveAsync(
        IReadOnlyList<CharacterId> characters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(characters);
        var ids = characters.Select(x => x.Value).Distinct().ToArray();
        var found = new List<CharacterPublicIdentity>(ids.Length);
        foreach (var chunk in ids.Chunk(Batch))
            found.AddRange(await database.ReadAllAsync(
                $"SELECT {Columns} FROM characters WHERE delete_flag=0 AND character_id IN ({CharacterIdList.Placeholders(chunk)})",
                p => CharacterIdList.Bind(p, chunk), Row, cancellationToken).ConfigureAwait(false));
        return found;
    }

    private static CharacterPublicIdentity Row(SqliteDataReader r) => new(
        new CharacterId(r.GetInt32(0)), (byte[])r.GetValue(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4));
}
