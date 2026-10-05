using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Data.Initialization;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Initialization;

internal sealed class SqliteInitializationPersistence(SqliteDatabase database) : IInitializationPersistence
{
    public async ValueTask AcquireWriteAsync(CancellationToken ct) =>
        _ = await database.ExecuteAsync("UPDATE initialization_settings SET revision=revision WHERE 0;", _ => { }, ct);
    public ValueTask<InitializationStoredDocument?> ReadAsync(string kind, CancellationToken ct) => database.ReadFirstAsync(
        "SELECT revision,input,updated_at,updated_by FROM initialization_settings WHERE kind=@kind;",
        p => p.AddWithValue("@kind", kind), r => new InitializationStoredDocument(r.GetInt64(0), r.GetString(1),
            DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2)), r.GetString(3)), ct);
    public async ValueTask<bool> SaveAsync(string kind, long expectedRevision, string input, DateTimeOffset at, string actor, CancellationToken ct) =>
        await database.ExecuteAsync("""
            INSERT INTO initialization_settings(kind,revision,input,updated_at,updated_by)
            SELECT @kind,1,@input,@at,@actor WHERE @expected=0
            ON CONFLICT(kind) DO NOTHING;
            UPDATE initialization_settings SET revision=revision+1,input=@input,updated_at=@at,updated_by=@actor
            WHERE kind=@kind AND revision=@expected AND @expected>0;
            """, p => { p.AddWithValue("@kind", kind); p.AddWithValue("@expected", expectedRevision); p.AddWithValue("@input", input);
                p.AddWithValue("@at", at.ToUnixTimeMilliseconds()); p.AddWithValue("@actor", actor); }, ct) == 1;
}
