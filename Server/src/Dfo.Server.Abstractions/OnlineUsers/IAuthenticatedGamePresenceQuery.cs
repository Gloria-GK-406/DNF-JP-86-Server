using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.OnlineUsers;

/// <summary>Readings owned by the existing online aggregate; anonymous connections are excluded.</summary>
public interface IAuthenticatedGamePresenceQuery
{
    IReadOnlyList<AccountId> SnapshotAccounts();
}
