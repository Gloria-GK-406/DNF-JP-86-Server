using Dfo.Server.Data.Accounts;
using Dfo.Server.Abstractions.Notifications;

namespace Dfo.Server.Abstractions.Accounts;

public sealed record AccountWalletChanged(AccountId Account) : INotification<AccountWalletChanged>;

public enum AccountCurrency { Cera, TokenCera, HappyTokenCera }
public enum WalletRejection { MissingAccount, InvalidAmount, BalanceLimit, InsufficientFunds, StateChanged }
public sealed class WalletException(WalletRejection rejection) : Exception(rejection.ToString())
{
    public WalletRejection Rejection { get; } = rejection;
}
public sealed record WalletChange(AccountCeraBalances Before, AccountCeraBalances After);

public interface IAccountWalletRuleService
{
    void Validate(AccountCeraBalances balances);
    AccountCeraBalances Add(AccountCeraBalances balances, AccountCurrency currency, int amount);
    AccountCeraBalances Spend(AccountCeraBalances balances, AccountCurrency currency, int amount);
}

public interface IAccountWalletDomainService
{
    ValueTask<AccountCeraBalances?> ReadAsync(AccountId account, CancellationToken ct);
    ValueTask<WalletChange> AddAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct);
    ValueTask<WalletChange> SpendAsync(AccountId account, AccountCurrency currency, int amount, CancellationToken ct);
    /// <summary>Settle a previously planned wallet change with an exact stale-write guard.</summary>
    ValueTask<bool> TryApplyAsync(AccountId account, AccountCeraBalances expected, AccountCeraBalances next, CancellationToken ct);
}
