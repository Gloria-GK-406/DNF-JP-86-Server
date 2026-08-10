using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.Accounts;

public interface IAccountRepository
{
    ValueTask<AccountRecord> GetOrCreateAsync(
        string loginId,
        string passwordHash,
        CancellationToken cancellationToken);
}
