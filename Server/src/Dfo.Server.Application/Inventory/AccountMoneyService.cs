using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Inventory;

/// <summary>Adds to the account's purse, in one statement.</summary>
/// <remarks>
/// Thin on purpose. The clamping and the addition both happen in the store, because two pickups
/// answered at once must not each add to the same total they both read -- and a purse that lost a
/// coin to a race is the kind of defect nobody can reconstruct afterwards.
/// </remarks>
internal sealed class AccountMoneyService(IAccountCargoStateTable cargo) : IAccountMoneyService
{
    public ValueTask<int> AddAsync(
        AccountId accountId, int amount, CancellationToken cancellationToken) =>
        cargo.AddValue32Async(accountId, amount, cancellationToken);
}
