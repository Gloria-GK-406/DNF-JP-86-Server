using System.Text.Json;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
namespace Dfo.Server.Persistence.Events;

internal sealed class SqliteJoustPersistence(SqliteDatabase database) : IJoustPersistence
{
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("Invalid joust record.");
    public ValueTask<JoustScheduleConfiguration?> ScheduleAsync(CancellationToken ct) => database.ReadFirstAsync(
        "SELECT input FROM event_joust_schedule WHERE id=1;", _=>{}, r=>Read<JoustScheduleConfiguration>(r.GetString(0)), ct);
    public async ValueTask<bool> SaveScheduleAsync(long expectedRevision, JoustScheduleConfiguration s, CancellationToken ct) =>
        await database.ExecuteAsync("""
        INSERT INTO event_joust_schedule(id,revision,input) SELECT 1,1,@input WHERE @rev=0 ON CONFLICT DO NOTHING;
        UPDATE event_joust_schedule SET revision=revision+1,input=@input WHERE id=1 AND revision=@rev AND @rev>0;
        """, p=>{p.AddWithValue("@rev",expectedRevision);p.AddWithValue("@input",JsonSerializer.Serialize(s));},ct)==1;
    public ValueTask<JoustRound?> RoundAsync(string id, CancellationToken ct) => database.ReadFirstAsync(
        "SELECT input FROM event_joust_round WHERE round_id=@id;", p=>p.AddWithValue("@id",id), r=>Read<JoustRound>(r.GetString(0)), ct);
    public ValueTask<JoustRound?> CurrentRoundAsync(DateTimeOffset now,CancellationToken ct)=>database.ReadFirstAsync(
        "SELECT input FROM event_joust_round WHERE starts_at<=@now AND ends_at>@now ORDER BY starts_at DESC LIMIT 1;",
        p=>p.AddWithValue("@now",now.ToUnixTimeSeconds()),r=>Read<JoustRound>(r.GetString(0)),ct);
    public async ValueTask<bool> HasUnsettledAsync(CancellationToken ct)=>await database.ReadFirstAsync(
        "SELECT 1 FROM event_joust_round WHERE settled=0 LIMIT 1;",_=>{},_=>new Marker(),ct) is not null;
    private sealed record Marker;
    public async ValueTask SaveRoundAsync(JoustRound r, CancellationToken ct) => _=await database.ExecuteAsync("""
        INSERT INTO event_joust_round(round_id,starts_at,ends_at,settled,input) VALUES(@id,@start,@end,@settled,@input)
        ON CONFLICT(round_id) DO UPDATE SET settled=excluded.settled,input=excluded.input;
        """,p=>{p.AddWithValue("@id",r.RoundId);p.AddWithValue("@start",r.StartsAt.ToUnixTimeSeconds());p.AddWithValue("@end",r.EndsAt.ToUnixTimeSeconds());
            p.AddWithValue("@settled",r.Settled?1:0);p.AddWithValue("@input",JsonSerializer.Serialize(r));},ct);
    public async ValueTask<JoustRound[]> UnsettledAsync(CancellationToken ct) => [..await database.ReadAllAsync(
        "SELECT input FROM event_joust_round WHERE settled=0 ORDER BY starts_at;",_=>{},r=>Read<JoustRound>(r.GetString(0)),ct)];
    public async ValueTask<JoustRound[]> HistoryAsync(CancellationToken ct) => [..await database.ReadAllAsync(
        "SELECT input FROM event_joust_round WHERE settled=1 ORDER BY starts_at DESC LIMIT 500;",_=>{},r=>Read<JoustRound>(r.GetString(0)),ct)];
    public async ValueTask<bool> HasUnrecordedStatisticsAsync(CancellationToken ct) => await database.ReadFirstAsync(
        "SELECT 1 FROM event_joust_round r WHERE settled=1 AND NOT EXISTS(SELECT 1 FROM event_joust_statistics_round s WHERE s.round_id=r.round_id) LIMIT 1;",
        _=>{},_=>new Marker(),ct) is not null;
    public async ValueTask<JoustRound[]> UnrecordedStatisticsAsync(CancellationToken ct) => [..await database.ReadAllAsync(
        "SELECT input FROM event_joust_round r WHERE settled=1 AND NOT EXISTS(SELECT 1 FROM event_joust_statistics_round s WHERE s.round_id=r.round_id) ORDER BY starts_at;",
        _=>{},r=>Read<JoustRound>(r.GetString(0)),ct)];
    public async ValueTask<JoustKnightStatistics[]> KnightStatisticsAsync(CancellationToken ct) => [..await database.ReadAllAsync(
        "SELECT knight_index,wins,losses,championships FROM event_joust_knight_statistics ORDER BY knight_index;",
        _=>{},r=>new JoustKnightStatistics(r.GetInt32(0),r.GetInt64(1),r.GetInt64(2),r.GetInt64(3)),ct)];
    public async ValueTask<bool> RecordStatisticsAsync(string roundId,IReadOnlyList<JoustKnightStatistics> statistics,CancellationToken ct)
    {
        var inserted=await database.ExecuteAsync("""
            INSERT INTO event_joust_statistics_round(round_id)
            SELECT round_id FROM event_joust_round WHERE round_id=@id AND settled=1
            ON CONFLICT(round_id) DO NOTHING;
            """,p=>p.AddWithValue("@id",roundId),ct);
        if(inserted==0)return false;
        foreach(var s in statistics)
            await database.ExecuteAsync("""
                INSERT INTO event_joust_knight_statistics(knight_index,wins,losses,championships) VALUES(@knight,@wins,@losses,@titles)
                ON CONFLICT(knight_index) DO UPDATE SET wins=wins+excluded.wins,losses=losses+excluded.losses,
                    championships=championships+excluded.championships;
                """,p=>{p.AddWithValue("@knight",s.KnightIndex);p.AddWithValue("@wins",s.Wins);
                    p.AddWithValue("@losses",s.Losses);p.AddWithValue("@titles",s.Championships);},ct);
        return true;
    }
    public async ValueTask<JoustBet[]> BetsAsync(string id,CancellationToken ct) => [..await database.ReadAllAsync(
        "SELECT input FROM event_joust_bet WHERE round_id=@id;",p=>p.AddWithValue("@id",id),r=>Read<JoustBet>(r.GetString(0)),ct)];
    public ValueTask<JoustBet?> RequestAsync(CharacterId character,string request,CancellationToken ct)=> database.ReadFirstAsync(
        "SELECT input FROM event_joust_bet WHERE character_id=@char AND request_id=@request;",
        p=>{p.AddWithValue("@char",character.Value);p.AddWithValue("@request",request);},r=>Read<JoustBet>(r.GetString(0)),ct);
    public async ValueTask InsertBetAsync(JoustBet b,CancellationToken ct)=>_=await database.ExecuteAsync("""
        INSERT INTO event_joust_bet(character_id,request_id,round_id,input) VALUES(@char,@request,@round,@input);
        """,p=>{p.AddWithValue("@char",b.Character.Value);p.AddWithValue("@request",b.RequestId);p.AddWithValue("@round",b.RoundId);
            p.AddWithValue("@input",JsonSerializer.Serialize(b));},ct);
}
