using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Pvp;

internal sealed class SqlitePvpTotalMatchTeamDomainStore(SqliteDatabase database) : IPvpTotalMatchTeamDomainStore
{
    public ValueTask<PvpTotalMatchTeam?> ReadAsync(AccountId account, CancellationToken cancellationToken) =>
        database.ReadFirstAsync(
            """
            SELECT name, first_character_id, second_character_id, third_character_id
            FROM account_pvp_total_match_teams WHERE account_id=@account
            """,
            parameters => parameters.AddWithValue("@account", account.Value),
            row => new PvpTotalMatchTeam(row.GetString(0),
                [new CharacterId(row.GetInt32(1)), new CharacterId(row.GetInt32(2)), new CharacterId(row.GetInt32(3))]),
            cancellationToken);

    public async ValueTask<bool> IsNameAvailableAsync(AccountId account, string name, CancellationToken cancellationToken) =>
        await database.ReadFirstAsync(
            "SELECT name FROM account_pvp_total_match_teams WHERE name_key=@key AND account_id<>@account",
            parameters =>
            {
                parameters.AddWithValue("@account", account.Value);
                parameters.AddWithValue("@key", name.ToUpperInvariant());
            },
            row => row.GetString(0), cancellationToken).ConfigureAwait(false) is null;

    public async ValueTask SaveAsync(AccountId account, PvpTotalMatchTeam team, CancellationToken cancellationToken)
    {
        await database.ExecuteAsync(
            """
            INSERT INTO account_pvp_total_match_teams (account_id, name, name_key, first_character_id, second_character_id, third_character_id)
            VALUES (@account, @name, @key, @first, @second, @third)
            ON CONFLICT(account_id) DO UPDATE SET
                first_character_id=excluded.first_character_id,
                second_character_id=excluded.second_character_id,
                third_character_id=excluded.third_character_id
            """,
            parameters =>
            {
                parameters.AddWithValue("@account", account.Value);
                parameters.AddWithValue("@name", team.Name);
                parameters.AddWithValue("@key", team.Name.ToUpperInvariant());
                parameters.AddWithValue("@first", team.Members[0].Value);
                parameters.AddWithValue("@second", team.Members[1].Value);
                parameters.AddWithValue("@third", team.Members[2].Value);
            },
            cancellationToken).ConfigureAwait(false);
    }
}
