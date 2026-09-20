using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Inventory;

internal sealed class SqliteAccountMaterialBagStore : IAccountMaterialBagStore
{
    private static readonly object FacetKey = typeof(SqliteAccountMaterialBagStoreSession);
    private readonly string connectionString;

    public SqliteAccountMaterialBagStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        this.connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
        }.ToString();
    }

    public object StoreIdentity => this.connectionString;
    public bool IsDurable => true;

    public IAccountMaterialBagStoreSession SessionIn(IDomainTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var database = transaction.Enlist(
            this.StoreIdentity,
            () => new SqliteDatabaseParticipant(this.connectionString));
        return database.Facet(
            FacetKey,
            owner => new SqliteAccountMaterialBagStoreSession(owner));
    }
}

internal sealed class SqliteAccountMaterialBagStoreSession(SqliteDatabaseParticipant owner)
    : IAccountMaterialBagStoreSession, ISqliteStoreFacet
{
    private readonly Dictionary<AccountId, AccountMaterialBagState> staged = [];

    public bool HasStagedWork => this.staged.Count > 0;

    public async ValueTask<AccountMaterialBagState?> ReadAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        if (this.staged.TryGetValue(accountId, out var pending))
            return pending;

        await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = """
            SELECT cube_black,cube_white,cube_red,cube_blue,cube_clear,cube_gold,
                   soul_common,soul_advanced,soul_rare,soul_unique,soul_legendary
            FROM accounts WHERE account_id=@accountId;
            """;
        command.Parameters.AddWithValue("@accountId", accountId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        var state = new AccountMaterialBagState(
            accountId,
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
            reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
            reader.GetInt32(9), reader.GetInt32(10));
        if (Enum.GetValues<AccountMaterialKind>().Any(kind => state.Count(kind) < 0))
            throw new InvalidDataException("Account material counts cannot be negative.");
        return state;
    }

    public void Write(AccountMaterialBagState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.AccountId.Value <= 0
            || Enum.GetValues<AccountMaterialKind>().Any(kind => state.Count(kind) < 0))
            throw new ArgumentOutOfRangeException(nameof(state));
        this.staged[state.AccountId] = state;
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var state in this.staged.Values.OrderBy(value => value.AccountId.Value))
        {
            await using var command = await owner.CommandAsync(cancellationToken).ConfigureAwait(false);
            command.CommandText = """
                UPDATE accounts SET
                    cube_black=@black,cube_white=@white,cube_red=@red,cube_blue=@blue,
                    cube_clear=@clear,cube_gold=@gold,soul_common=@common,
                    soul_advanced=@advanced,soul_rare=@rare,soul_unique=@unique,
                    soul_legendary=@legendary
                WHERE account_id=@accountId;
                """;
            command.Parameters.AddWithValue("@black", state.BlackCube);
            command.Parameters.AddWithValue("@white", state.WhiteCube);
            command.Parameters.AddWithValue("@red", state.RedCube);
            command.Parameters.AddWithValue("@blue", state.BlueCube);
            command.Parameters.AddWithValue("@clear", state.ClearCube);
            command.Parameters.AddWithValue("@gold", state.GoldCube);
            command.Parameters.AddWithValue("@common", state.CommonSoul);
            command.Parameters.AddWithValue("@advanced", state.AdvancedSoul);
            command.Parameters.AddWithValue("@rare", state.RareSoul);
            command.Parameters.AddWithValue("@unique", state.UniqueSoul);
            command.Parameters.AddWithValue("@legendary", state.LegendarySoul);
            command.Parameters.AddWithValue("@accountId", state.AccountId.Value);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException(
                    $"Account {state.AccountId.Value} disappeared while saving its material bag.");
        }
    }

    public void Discard() => this.staged.Clear();
}
