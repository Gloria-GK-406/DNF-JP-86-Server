using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Persistence.Database;
namespace Dfo.Server.Persistence.Marriage;
internal sealed class SqliteMarriageGuestbookDomainStore(SqliteDatabase database) : IMarriageGuestbookDomainStore
{
    public ValueTask<IReadOnlyList<MarriageGuestComment>> ReadAsync(Guid relation, CancellationToken ct) =>
        database.ReadAllAsync("SELECT id,author_id,author_name,sort_key,text FROM marriage_guest_comments WHERE relation_id=@relation ORDER BY sort_key DESC,id DESC", p => p.AddWithValue("@relation", relation.ToString("N")),
            r => new MarriageGuestComment(checked((uint)r.GetInt64(0)), new(r.GetInt32(1)), (byte[])r[2], checked((uint)r.GetInt64(3)), (byte[])r[4]), ct);
    public async ValueTask AddAsync(Guid relation, CharacterId author, byte[] name, byte[] text, uint sortKey, CancellationToken ct) =>
        _ = await database.ExecuteAsync("INSERT INTO marriage_guest_comments(relation_id,author_id,author_name,sort_key,text) VALUES(@relation,@author,@name,@sort,@text)",
            p => { p.AddWithValue("@relation", relation.ToString("N")); p.AddWithValue("@author", author.Value); p.AddWithValue("@name", name); p.AddWithValue("@sort", sortKey); p.AddWithValue("@text", text); }, ct).ConfigureAwait(false);
    public async ValueTask<bool> DeleteAsync(Guid relation, uint id, CancellationToken ct) =>
        await database.ExecuteAsync("DELETE FROM marriage_guest_comments WHERE relation_id=@relation AND id=@id", p => { p.AddWithValue("@relation", relation.ToString("N")); p.AddWithValue("@id", id); }, ct).ConfigureAwait(false) == 1;
}
