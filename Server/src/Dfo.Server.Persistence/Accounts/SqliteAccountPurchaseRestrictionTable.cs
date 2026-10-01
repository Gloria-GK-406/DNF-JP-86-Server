using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqliteAccountPurchaseRestrictionTable(SqliteDatabase database) : IAccountPurchaseRestrictionTable
{
    public ValueTask<IReadOnlyList<AccountPurchaseRestrictionRow>> ReadAsync(AccountId accountId, CancellationToken ct) =>
        database.ReadAllAsync("SELECT restriction_id,end_time FROM account_purchase_restrictions WHERE account_id=$owner ORDER BY restriction_id",
            p => p.AddWithValue("$owner", accountId.Value), r => new AccountPurchaseRestrictionRow(r.GetInt32(0), r.GetInt32(1)), ct);

    public async ValueTask<bool> TrySetAsync(AccountId accountId, int restrictionId, int? expectedEnd, int end, CancellationToken ct)
    {
        var sql = expectedEnd is null
            ? "INSERT INTO account_purchase_restrictions(account_id,restriction_id,end_time) VALUES($owner,$id,$end) ON CONFLICT(account_id,restriction_id) DO NOTHING"
            : "UPDATE account_purchase_restrictions SET end_time=$end WHERE account_id=$owner AND restriction_id=$id AND end_time=$expected";
        return await database.ExecuteAsync(sql, p =>
        {
            p.AddWithValue("$owner", accountId.Value); p.AddWithValue("$id", restrictionId); p.AddWithValue("$end", end);
            if (expectedEnd is { } expected) p.AddWithValue("$expected", expected);
        }, ct).ConfigureAwait(false) == 1;
    }
}
