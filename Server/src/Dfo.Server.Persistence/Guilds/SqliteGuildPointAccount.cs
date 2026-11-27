using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Guilds;

internal sealed class SqliteGuildPointAccount(SqliteDatabase database) : IGuildPointAccount
{
    private const string CreditCondition = "guild_id=@guild AND gm_points+spent_gm_points<=2147483647-@amount";

    public ValueTask<bool> CanCreditAsync(GuildId guild, int amount, CancellationToken ct) =>
        database.ReadAsync(async (command, token) =>
        {
            command.CommandText = "SELECT 1 FROM guilds WHERE " + CreditCondition;
            Bind(command.Parameters, guild, amount);
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null;
        }, ct);

    public async ValueTask<bool> TryCreditAsync(GuildId guild, int amount, CancellationToken ct) =>
        await database.ExecuteAsync("UPDATE guilds SET gm_points=gm_points+@amount WHERE " + CreditCondition,
            p => Bind(p, guild, amount), ct).ConfigureAwait(false) == 1;

    public async ValueTask<bool> TrySpendAsync(GuildId guild, int amount, CancellationToken ct) =>
        await database.ExecuteAsync("""
            UPDATE guilds SET gm_points=gm_points-@amount,spent_gm_points=spent_gm_points+@amount
            WHERE guild_id=@guild AND gm_points>=@amount
            """, p => Bind(p, guild, amount), ct).ConfigureAwait(false) == 1;

    private static void Bind(SqliteParameterCollection parameters, GuildId guild, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        parameters.AddWithValue("@guild", (long)guild.Value);
        parameters.AddWithValue("@amount", amount);
    }
}
