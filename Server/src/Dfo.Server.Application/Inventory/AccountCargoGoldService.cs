using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Inventory;

/// <summary>Moves gold into and out of the account's cargo, one statement at a time.</summary>
/// <remarks>
/// Thin on purpose. Both directions happen in the store rather than here, because two pickups
/// answered at once must not each add to the same total they both read -- and a total that lost a
/// coin to a race is the kind of defect nobody can reconstruct afterwards. The same reasoning is
/// what keeps the check on a charge in the store: a balance read here and acted on afterwards
/// would let two charges both pass a test only one of them could afford.
/// </remarks>
internal sealed class AccountCargoGoldService(IAccountCargoStateTable cargo)
    : IAccountCargoGoldService
{
    public ValueTask<int> AddAsync(
        AccountId accountId, int amount, CancellationToken cancellationToken) =>
        cargo.AddValue32Async(accountId, amount, cancellationToken);

    /// <remarks>
    /// The refusal is the store's answer, not a comparison made here. What this adds is the guard
    /// on the sign: a negative amount would reach the store as a take of negative gold, which the
    /// store's own condition treats as affordable and its subtraction turns into a gift.
    /// </remarks>
    public ValueTask<int?> TrySpendAsync(
        AccountId accountId, int amount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        return cargo.TryTakeValue32Async(accountId, amount, cancellationToken);
    }
}
