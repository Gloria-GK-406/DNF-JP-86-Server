using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteEpicBookDomainStore(SqliteDatabase database) : IEpicBookDomainStore
{
    public ValueTask<IReadOnlyList<EpicPieceBalance>> ReadAsync(AccountId account, CancellationToken cancellationToken) =>
        database.ReadAsync<IReadOnlyList<EpicPieceBalance>>(async (command, ct) =>
        {
            command.CommandText = "SELECT piece_id,count FROM account_epic_pieces WHERE account_id=@account ORDER BY piece_id";
            command.Parameters.AddWithValue("@account", account.Value);
            var entries = new List<EpicPieceBalance>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) entries.Add(new(reader.GetInt32(0), reader.GetInt32(1)));
            return entries;
        }, cancellationToken);

    public async ValueTask<EpicPieceAddition> AddAsync(AccountId account, int pieceId, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(account.Value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pieceId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        EpicPieceAddition? result = null;
        await database.WriteAsync(async (command, ct) =>
        {
            await AcquireWriterAsync(command, ct).ConfigureAwait(false);
            result = await AddAsync(command, account.Value, pieceId, count, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return result!;
    }

    private static async ValueTask AcquireWriterAsync(SqliteCommand command, CancellationToken ct)
    {
        command.CommandText = "UPDATE account_epic_pieces SET count=count WHERE 0";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async ValueTask<EpicPieceAddition> AddAsync(SqliteCommand command, long account, int piece, int count, CancellationToken ct)
    {
        command.Parameters.Clear();
        command.Parameters.AddWithValue("@account", account);
        command.Parameters.AddWithValue("@piece", piece);
        command.CommandText = "SELECT COUNT(*) FROM accounts WHERE account_id=@account";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)) != 1)
            throw new InvalidDataException("Epic book owner is missing.");
        command.CommandText = "SELECT count FROM account_epic_pieces WHERE account_id=@account AND piece_id=@piece";
        var old = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var current = old is null ? 0 : Convert.ToInt32(old);
        var total = (int)Math.Min(int.MaxValue, (long)current + count);
        if (total != current)
        {
            command.Parameters.AddWithValue("@total", total);
            command.CommandText = "INSERT INTO account_epic_pieces(account_id,piece_id,count) VALUES(@account,@piece,@total) "
                + "ON CONFLICT(account_id,piece_id) DO UPDATE SET count=excluded.count";
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return new(piece, count, total - current, total);
    }

    public ValueTask MigrateLegacyAsync(IReadOnlyList<EpicPieceDefinition> catalog, CancellationToken cancellationToken) =>
        database.WriteAsync(async (command, ct) =>
        {
            var pieces = catalog.Select(x => x.PieceId).ToHashSet();
            if (pieces.Count == 0) return;
            await AcquireWriterAsync(command, ct).ConfigureAwait(false);
            command.CommandText = """
                SELECT s.owner_scope,s.owner_id,s.list_type,s.slot_index,s.id,s.count,i.item_core,
                    CASE WHEN s.owner_scope='account' THEN s.owner_id ELSE c.account_id END,s.kind
                FROM item_slots s LEFT JOIN items i ON i.item_uid=s.core_uid
                LEFT JOIN characters c ON s.owner_scope='character' AND c.character_id=s.owner_id
                WHERE s.kind='template'
                """;
            var legacy = new List<(string Scope, long Owner, int List, int Slot, int Piece, int Count, long Account)>();
            await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (!pieces.Contains(reader.GetInt32(4))) continue;
                    if (reader.IsDBNull(6) || reader.IsDBNull(7) || reader.GetString(8) != "template"
                        || InventoryItemCore.Decode((byte[])reader[6]).ItemId != reader.GetInt32(4)
                        || reader.GetInt64(5) is <= 0 or > int.MaxValue)
                        throw new InvalidDataException("Invalid legacy epic piece custody.");
                    legacy.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3),
                        reader.GetInt32(4), reader.GetInt32(5), reader.GetInt64(7)));
                }
            }
            foreach (var row in legacy)
            {
                await AddAsync(command, row.Account, row.Piece, row.Count, ct).ConfigureAwait(false);
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@scope", row.Scope);
                command.Parameters.AddWithValue("@owner", row.Owner);
                command.Parameters.AddWithValue("@list", row.List);
                command.Parameters.AddWithValue("@slot", row.Slot);
                command.CommandText = "DELETE FROM item_slots WHERE owner_scope=@scope AND owner_id=@owner AND list_type=@list AND slot_index=@slot";
                if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new InvalidDataException("Legacy epic piece custody changed during migration.");
            }
        }, cancellationToken);
}
