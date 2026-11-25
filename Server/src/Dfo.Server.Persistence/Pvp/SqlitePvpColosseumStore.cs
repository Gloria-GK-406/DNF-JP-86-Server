using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Pvp;

internal sealed class SqlitePvpColosseumStore(SqliteDatabase database) : IPvpColosseumStore
{
    public ValueTask<PvpColosseumRun?> ReadAsync(PvpColosseumOwner owner, CancellationToken ct) =>
        database.ReadFirstAsync(
            """
            SELECT run_id,recipient,entry_day,wins,losses,prizes
            FROM pvp_colosseum_runs WHERE kind=@kind AND owner_id=@owner AND closed=0
            """,
            parameters => BindOwner(parameters, owner),
            reader => new PvpColosseumRun(Guid.ParseExact(reader.GetString(0), "N"), owner, new CharacterId(reader.GetInt32(1)),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(3), reader.GetInt32(4),
                JsonSerializer.Deserialize<ImmutableArray<PvpColosseumPrize>>(reader.GetString(5))), ct);

    public async ValueTask<int> EntriesAsync(PvpColosseumOwner owner, DateOnly day, CancellationToken ct) =>
        (await database.ReadFirstAsync(
            "SELECT COUNT(*) FROM pvp_colosseum_runs WHERE kind=@kind AND owner_id=@owner AND entry_day=@day",
            parameters =>
            {
                BindOwner(parameters, owner);
                parameters.AddWithValue("@day", Format(day));
            },
            reader => new EntryCount(reader.GetInt32(0)), ct).ConfigureAwait(false))!.Value;

    public async ValueTask InsertAsync(PvpColosseumRun run, CancellationToken ct) =>
        _ = await database.WriteReturningAsync(
            """
            INSERT INTO pvp_colosseum_runs(run_id,kind,owner_id,recipient,entry_day)
            VALUES(@run,@kind,@owner,@recipient,@day)
            RETURNING run_id
            """,
            parameters =>
            {
                BindOwner(parameters, run.Owner);
                parameters.AddWithValue("@run", run.Id.ToString("N"));
                parameters.AddWithValue("@recipient", run.Recipient.Value);
                parameters.AddWithValue("@day", Format(run.EntryDay));
            },
            reader => reader.GetString(0), ct).ConfigureAwait(false);

    public async ValueTask UpdateAsync(PvpColosseumRun run, CancellationToken ct)
    {
        var updated = await database.WriteReturningAsync(
            """
            UPDATE pvp_colosseum_runs SET wins=@wins,losses=@losses,prizes=@prizes
            WHERE run_id=@run AND closed=0 AND prizes='[]' RETURNING run_id
            """,
            parameters =>
            {
                parameters.AddWithValue("@run", run.Id.ToString("N"));
                parameters.AddWithValue("@wins", run.Wins);
                parameters.AddWithValue("@losses", run.Losses);
                parameters.AddWithValue("@prizes", JsonSerializer.Serialize(run.Prizes));
            },
            reader => reader.GetString(0), ct).ConfigureAwait(false);
        if (updated is null) throw new InvalidOperationException("The Colosseum run is no longer active.");
    }

    public async ValueTask CloseAsync(Guid run, CancellationToken ct)
    {
        var closed = await database.WriteReturningAsync(
            """
            UPDATE pvp_colosseum_runs SET closed=1
            WHERE run_id=@run AND closed=0 AND prizes<>'[]' RETURNING run_id
            """,
            parameters => parameters.AddWithValue("@run", run.ToString("N")),
            reader => reader.GetString(0), ct).ConfigureAwait(false);
        if (closed is null) throw new InvalidOperationException("The Colosseum run has no pending prizes.");
    }

    private static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private sealed record EntryCount(int Value);

    private static void BindOwner(SqliteParameterCollection parameters, PvpColosseumOwner owner)
    {
        parameters.AddWithValue("@kind", (int)owner.Kind);
        parameters.AddWithValue("@owner", owner.Id);
    }
}
