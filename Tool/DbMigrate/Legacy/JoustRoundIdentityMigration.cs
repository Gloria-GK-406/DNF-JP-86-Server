using System.Globalization;
using System.Text.Json;
using Dfo.Server.Data.Events;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Legacy;

/// <summary>Preserves saved rounds and their references while retiring season-prefixed IDs.</summary>
internal static class JoustRoundIdentityMigration
{
    public static void Apply(SqliteConnection connection)
    {
        using var transaction=connection.BeginTransaction();
        using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="PRAGMA defer_foreign_keys=ON;";command.ExecuteNonQuery();
        command.CommandText="SELECT round_id,input FROM event_joust_round ORDER BY starts_at,round_id;";
        var rounds=new List<(string OldId,JoustRound Round)>();
        using(var reader=command.ExecuteReader())
            while(reader.Read())rounds.Add((reader.GetString(0),JsonSerializer.Deserialize<JoustRound>(reader.GetString(1))
                ?? throw new InvalidDataException("Invalid saved joust round.")));
        var ids=rounds.Select(r=>r.Round.StartsAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)).ToArray();
        if(ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)
            throw new InvalidDataException("马战旧数据存在相同开场时间的多场比赛，不能无损迁移时间 ID。");
        foreach(var (oldId,round) in rounds)
        {
            var id=round.StartsAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            if(oldId==id && round.RoundId==id)continue;
            command.Parameters.Clear();command.Parameters.AddWithValue("@old",oldId);
            command.CommandText="SELECT character_id,request_id,input FROM event_joust_bet WHERE round_id=@old;";
            var bets=new List<(long Character,string Request,JoustBet Bet)>();
            using(var reader=command.ExecuteReader())
                while(reader.Read())bets.Add((reader.GetInt64(0),reader.GetString(1),JsonSerializer.Deserialize<JoustBet>(reader.GetString(2))
                    ?? throw new InvalidDataException("Invalid saved joust bet.")));
            command.Parameters.AddWithValue("@id",id);
            command.Parameters.AddWithValue("@input",JsonSerializer.Serialize(round with{RoundId=id}));
            command.CommandText="UPDATE event_joust_round SET round_id=@id,input=@input WHERE round_id=@old;";
            command.ExecuteNonQuery();
            command.CommandText="UPDATE event_joust_statistics_round SET round_id=@id WHERE round_id=@old;";
            command.ExecuteNonQuery();
            command.CommandText="UPDATE event_joust_result SET round_id=@id WHERE round_id=@old;";
            command.ExecuteNonQuery();
            foreach(var bet in bets)
            {
                command.Parameters["@input"].Value=JsonSerializer.Serialize(bet.Bet with{RoundId=id});
                command.Parameters.AddWithValue("@char",bet.Character);command.Parameters.AddWithValue("@request",bet.Request);
                command.CommandText="UPDATE event_joust_bet SET round_id=@id,input=@input WHERE character_id=@char AND request_id=@request;";
                command.ExecuteNonQuery();command.Parameters.RemoveAt("@char");command.Parameters.RemoveAt("@request");
            }
        }
        transaction.Commit();
    }
}
