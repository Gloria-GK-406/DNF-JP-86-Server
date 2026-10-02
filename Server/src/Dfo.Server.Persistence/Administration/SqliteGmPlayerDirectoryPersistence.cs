using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Administration;

internal sealed class SqliteGmPlayerDirectoryPersistence(SqliteDatabase database) : IGmPlayerDirectoryPersistence
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const string AccountFrom = "FROM accounts a WHERE (@account IS NULL OR a.account_id=@account) AND (@q IS NULL OR instr(a.m_id,@q)>0) AND (@online IS NULL OR a.account_id IN (SELECT value FROM json_each(@online)))";
    private const string CharacterFrom = "FROM characters c JOIN accounts a ON a.account_id=c.account_id WHERE (@account IS NULL OR c.account_id=@account) AND (@character IS NULL OR c.character_id=@character) AND (@q IS NULL OR (gm_valid_name(c.name_bytes)=1 AND instr(CAST(c.name_bytes AS TEXT),@q)>0)) AND (@online IS NULL OR c.character_id IN (SELECT value FROM json_each(@online)))";

    public ValueTask<GmStoredPage<GmAccountRecord>> AccountsAsync(GmPlayerFilter filter, IReadOnlyCollection<int>? onlineIds, CancellationToken token) =>
        PageAsync(filter, onlineIds, AccountFrom,
            "a.account_id,a.m_id,a.cera,a.token_cera,a.happy_token_cera,(SELECT COUNT(*) FROM characters c WHERE c.account_id=a.account_id)",
            "a.account_id", reader => new GmAccountRecord(reader.GetInt32(0), reader.GetString(1), Balances(reader, 2), reader.GetInt32(5)), token);

    public ValueTask<GmStoredPage<GmCharacterRecord>> CharactersAsync(GmPlayerFilter filter, IReadOnlyCollection<int>? onlineIds, CancellationToken token) =>
        PageAsync(filter, onlineIds, CharacterFrom,
            "c.character_id,c.account_id,a.m_id,c.name_bytes,c.job,c.grow_type,c.level,c.delete_flag,c.town_id,c.area_id,c.pos_x,c.pos_y,c.direction,a.cera,a.token_cera,a.happy_token_cera",
            "c.character_id", reader => new GmCharacterRecord(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : (byte[])reader.GetValue(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7) != 0,
                new CharacterPositionState(checked((byte)reader.GetInt32(8)), checked((byte)reader.GetInt32(9)),
                    checked((short)reader.GetInt32(10)), checked((short)reader.GetInt32(11)), checked((byte)reader.GetInt32(12))), Balances(reader, 13)), token);

    public ValueTask<(long Accounts, long Characters)> CountsAsync(CancellationToken token) => database.ReadAsync(async (command, cancellation) =>
    {
        command.CommandText = "SELECT (SELECT COUNT(*) FROM accounts),(SELECT COUNT(*) FROM characters);";
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        await reader.ReadAsync(cancellation);
        return (reader.GetInt64(0), reader.GetInt64(1));
    }, token);

    private ValueTask<GmStoredPage<T>> PageAsync<T>(GmPlayerFilter filter, IReadOnlyCollection<int>? onlineIds,
        string from, string columns, string order, Func<SqliteDataReader, T> read, CancellationToken token) =>
        database.ReadAsync(async (command, cancellation) =>
        {
            command.Connection!.CreateFunction<byte[]?, bool>("gm_valid_name", ValidName, isDeterministic: true);
            command.Parameters.AddWithValue("@account", (object?)filter.AccountId ?? DBNull.Value);
            command.Parameters.AddWithValue("@character", (object?)filter.CharacterId ?? DBNull.Value);
            command.Parameters.AddWithValue("@q", (object?)filter.Q ?? DBNull.Value);
            command.Parameters.AddWithValue("@online", onlineIds is null ? DBNull.Value : JsonSerializer.Serialize(onlineIds));
            command.CommandText = "SELECT COUNT(*) " + from;
            var total = Convert.ToInt64(await command.ExecuteScalarAsync(cancellation));
            command.Parameters.AddWithValue("@limit", filter.PageSize);
            command.Parameters.AddWithValue("@offset", (long)(filter.Page - 1) * filter.PageSize);
            command.CommandText = $"SELECT {columns} {from} ORDER BY {order} LIMIT @limit OFFSET @offset;";
            var items = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation)) items.Add(read(reader));
            return new GmStoredPage<T>(items, total);
        }, token);

    private static bool ValidName(byte[]? bytes)
    {
        if (bytes is null) return false;
        try { _ = StrictUtf8.GetString(bytes); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    private static AccountCeraBalances Balances(SqliteDataReader reader, int offset) =>
        new(Currency(reader, offset), Currency(reader, offset + 1), Currency(reader, offset + 2));
    private static int Currency(SqliteDataReader reader, int column)
    {
        var amount = reader.GetInt64(column);
        return amount is >= 0 and <= int.MaxValue ? (int)amount : throw new InvalidDataException("Account currency is outside its supported range.");
    }
}
