using System.Text.Json;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Database;
namespace Dfo.Server.Persistence.Events;

internal sealed class SqliteEventPersistence(SqliteDatabase database) : IEventPersistence
{
    public async ValueTask AcquireWriteAsync(CancellationToken ct) =>
        _ = await database.ExecuteAsync("UPDATE event_configuration SET revision=revision WHERE 0;", _ => { }, ct);
    public ValueTask<EventConfiguration?> ConfigurationAsync(int id, CancellationToken ct) => database.ReadFirstAsync(
        "SELECT input FROM event_configuration WHERE event_id=@id;", p => p.AddWithValue("@id", id),
        r => JsonSerializer.Deserialize<EventConfiguration>(r.GetString(0)) ?? throw new InvalidDataException("Invalid event configuration."), ct);
    public async ValueTask<bool> SaveAsync(int id, long revision, EventConfiguration config, CancellationToken ct) =>
        await database.ExecuteAsync("""
        INSERT INTO event_configuration(event_id,revision,input) SELECT @id,1,@input WHERE @rev=0 ON CONFLICT(event_id) DO NOTHING;
        UPDATE event_configuration SET revision=revision+1,input=@input WHERE event_id=@id AND revision=@rev AND @rev>0;
        """, p => { p.AddWithValue("@id", id); p.AddWithValue("@rev", revision); p.AddWithValue("@input", JsonSerializer.Serialize(config)); }, ct) == 1;
    public async ValueTask<bool> HasProgressAsync(int id, string season, CancellationToken ct) =>
        await database.ReadFirstAsync("SELECT 1 FROM event_attendance_week WHERE event_id=@id AND season_id=@season LIMIT 1;",
            p => { p.AddWithValue("@id", id); p.AddWithValue("@season", season); }, _ => new Marker(), ct) is not null;
    public async ValueTask<AttendanceProgress> ProgressAsync(int id, string season, AccountId account, DateTimeOffset week, CancellationToken ct)
    {
        var rows = await database.ReadAllAsync("SELECT week_start,clears,attendance_no FROM event_attendance_week WHERE event_id=@id AND season_id=@season AND account_id=@account ORDER BY attendance_no;",
            p => Bind(p, id, season, account), r => new Row(r.GetInt64(0), r.GetInt32(1), r.GetInt32(2)), ct);
        var current = rows.SingleOrDefault(r => r.Week == week.ToUnixTimeSeconds());
        var claims = rows.Where(r => r.Attendance > 0).Select(r => r.Attendance).ToArray();
        return new(claims.Length, current?.Clears ?? 0, current?.Attendance > 0, claims);
    }
    public async ValueTask<bool> CountClearAsync(int id, string season, AccountId account, DateTimeOffset week, Guid run, CancellationToken ct)
    {
        var inserted = await database.ExecuteAsync("INSERT INTO event_consumed_runs(event_id,season_id,account_id,run_id) VALUES(@id,@season,@account,@run) ON CONFLICT DO NOTHING;",
            p => { Bind(p,id,season,account); p.AddWithValue("@run",run.ToString("N")); }, ct);
        if (inserted == 0) return false;
        await database.ExecuteAsync("""
        INSERT INTO event_attendance_week(event_id,season_id,account_id,week_start,clears,attendance_no) VALUES(@id,@season,@account,@week,1,0)
        ON CONFLICT(event_id,season_id,account_id,week_start) DO UPDATE SET clears=MIN(clears+1,2147483647);
        """, p => { Bind(p,id,season,account); p.AddWithValue("@week",week.ToUnixTimeSeconds()); }, ct);
        return true;
    }
    public async ValueTask<bool> MarkClaimedAsync(int id, string season, AccountId account, DateTimeOffset week, int required, int maximum, CancellationToken ct) =>
        await database.ExecuteAsync("""
        UPDATE event_attendance_week SET attendance_no=(SELECT COUNT(*)+1 FROM event_attendance_week WHERE event_id=@id AND season_id=@season AND account_id=@account AND attendance_no>0)
        WHERE event_id=@id AND season_id=@season AND account_id=@account AND week_start=@week AND attendance_no=0 AND clears>=@required
        AND (SELECT COUNT(*) FROM event_attendance_week WHERE event_id=@id AND season_id=@season AND account_id=@account AND attendance_no>0)<@max;
        """, p => { Bind(p,id,season,account); p.AddWithValue("@week",week.ToUnixTimeSeconds()); p.AddWithValue("@required",required); p.AddWithValue("@max",maximum); }, ct) == 1;
    private static void Bind(Microsoft.Data.Sqlite.SqliteParameterCollection p,int id,string season,AccountId account)
    { p.AddWithValue("@id",id); p.AddWithValue("@season",season); p.AddWithValue("@account",account.Value); }
    private sealed record Row(long Week,int Clears,int Attendance);
    private sealed record Marker;
}
