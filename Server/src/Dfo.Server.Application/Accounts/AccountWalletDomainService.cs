using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Accounts;

internal sealed class AccountWalletRuleService : IAccountWalletRuleService
{
    public void Validate(AccountCeraBalances balances)
    {
        if (balances.Cera < 0 || balances.TokenCera < 0 || balances.HappyTokenCera < 0)
            throw new InvalidDataException("Account wallet is out of range.");
    }
    public AccountCeraBalances Add(AccountCeraBalances balances, AccountCurrency currency, int amount) => Change(balances, currency, amount, false);
    public AccountCeraBalances Spend(AccountCeraBalances balances, AccountCurrency currency, int amount) => Change(balances, currency, amount, true);
    private AccountCeraBalances Change(AccountCeraBalances balances, AccountCurrency currency, int amount, bool spend)
    {
        Validate(balances);
        if (amount <= 0) throw new WalletException(WalletRejection.InvalidAmount);
        long current = currency switch
        {
            AccountCurrency.Cera => balances.Cera, AccountCurrency.TokenCera => balances.TokenCera,
            AccountCurrency.HappyTokenCera => balances.HappyTokenCera, _ => throw new ArgumentOutOfRangeException(nameof(currency)),
        };
        var next = current + (spend ? -(long)amount : amount);
        if (next < 0) throw new WalletException(WalletRejection.InsufficientFunds);
        if (next > int.MaxValue) throw new WalletException(WalletRejection.BalanceLimit);
        return currency switch
        {
            AccountCurrency.Cera => balances with { Cera = (int)next },
            AccountCurrency.TokenCera => balances with { TokenCera = (int)next },
            _ => balances with { HappyTokenCera = (int)next },
        };
    }
}

internal sealed class AccountWalletDomainService(IAccountTable accounts, IAccountWalletRuleService rules,
    IDomainTransactionAccessor transactions) : IAccountWalletDomainService
{
    public async ValueTask<AccountCeraBalances?> ReadAsync(AccountId account, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(account.Value);
        var row = await accounts.FindWalletAsync(account, ct).ConfigureAwait(false);
        if (row is null) return null;
        if (row.Cera is < 0 or > int.MaxValue || row.TokenCera is < 0 or > int.MaxValue || row.HappyTokenCera is < 0 or > int.MaxValue)
            throw new InvalidDataException("Account wallet is out of range.");
        return new((int)row.Cera, (int)row.TokenCera, (int)row.HappyTokenCera);
    }
    public ValueTask<WalletChange> AddAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct) => ChangeAsync(account, currency, amount, false, ct);
    public ValueTask<WalletChange> SpendAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct) => ChangeAsync(account, currency, amount, true, ct);
    private async ValueTask<WalletChange> ChangeAsync(AccountId account, AccountCurrency currency, int amount, bool spend, CancellationToken ct)
    {
        RequireTransaction();
        var before = await ReadAsync(account, ct).ConfigureAwait(false) ?? throw new WalletException(WalletRejection.MissingAccount);
        var after = spend ? rules.Spend(before, currency, amount) : rules.Add(before, currency, amount);
        if (!await TryApplyAsync(account, before, after, ct).ConfigureAwait(false)) throw new WalletException(WalletRejection.StateChanged);
        return new(before, after);
    }
    public async ValueTask<bool> TryApplyAsync(AccountId account, AccountCeraBalances expected, AccountCeraBalances next, CancellationToken ct)
    {
        RequireTransaction();
        rules.Validate(expected); rules.Validate(next);
        if (await ReadAsync(account, ct).ConfigureAwait(false) != expected) return false;
        return expected == next || await accounts.TrySetCeraBalancesAsync(account, expected, next, ct).ConfigureAwait(false) == 1;
    }
    private void RequireTransaction()
    {
        if (transactions.Current is null) throw new InvalidOperationException("Wallet mutations require a domain transaction.");
    }
}
