using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqliteFatigueConsumptionSettingsPersistence(SqliteDatabase database) : IFatigueConsumptionSettingsPersistence
{
    public async ValueTask<bool> IsSuspendedAsync(CancellationToken ct) =>
        (await database.ReadFirstAsync("SELECT suspended FROM fatigue_consumption_settings WHERE id=1;",
            _ => { }, r => new Setting(r.GetBoolean(0)), ct))?.Suspended ?? false;

    public async ValueTask SetSuspendedAsync(bool suspended, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO fatigue_consumption_settings(id,suspended) VALUES(1,@suspended)
            ON CONFLICT(id) DO UPDATE SET suspended=excluded.suspended;
            """, p => p.AddWithValue("@suspended", suspended), ct);

    private sealed record Setting(bool Suspended);
}
