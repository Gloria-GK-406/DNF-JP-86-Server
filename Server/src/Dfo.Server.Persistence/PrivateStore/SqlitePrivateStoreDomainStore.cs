using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.PrivateStore;

/// <summary>The <c>private_store_custody</c> table, read across sellers.</summary>
internal sealed class SqlitePrivateStoreDomainStore(SqliteDatabase database) : IPrivateStoreDomainStore
{
    public ValueTask<IReadOnlyList<CharacterId>> OwnersWithCustodyAsync(CancellationToken ct) =>
        database.ReadAllAsync("SELECT DISTINCT owner_id FROM private_store_custody ORDER BY owner_id", _ => { },
            r => new CharacterId(r.GetInt32(0)), ct);
}
