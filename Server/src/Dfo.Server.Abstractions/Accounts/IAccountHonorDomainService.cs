using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Accounts;

public interface IAccountHonorDomainService
{
    ValueTask<AccountHonorLevel> ReadAsync(AccountId accountId, CancellationToken cancellationToken);
}
