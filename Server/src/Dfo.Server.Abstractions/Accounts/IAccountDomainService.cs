using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Abstractions.Accounts;

public interface IAccountDomainService
{
    ValueTask<AccountId> LoginAsync(
        OnlineUserId onlineUserId,
        string loginId,
        string passwordHash,
        CancellationToken cancellationToken);
}
