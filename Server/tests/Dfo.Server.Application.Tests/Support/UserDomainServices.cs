using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests;

/// <summary>
/// The three online-state services over one registry, as a single object.
/// </summary>
/// <remarks>
/// Production composes these three separately; this fixture exists because these tests exercise the
/// account, online-user and active-character responsibilities of one connection together, and
/// holding three references at every call site would obscure what each test is asserting. It adds
/// no behavior: every member forwards to the same service the container would have injected, and
/// all three share one registry, so the tests still exercise the real cross-responsibility path.
/// </remarks>
internal sealed class UserDomainServices
    : IAccountDomainService, IOnlineUserDomainService, IActiveCharacterDomainService
{
    private readonly AccountDomainService accounts;
    private readonly OnlineUserDomainService online;
    private readonly ActiveCharacterDomainService active;

    public UserDomainServices(Func<IAccountRepository> accountRepository)
    {
        var state = new OnlineStateRegistry();
        accounts = new AccountDomainService(accountRepository, state);
        online = new OnlineUserDomainService(state);
        active = new ActiveCharacterDomainService(state);
    }

    public ValueTask<AccountId> LoginAsync(
        OnlineUserId onlineUserId,
        string loginId,
        string passwordHash,
        CancellationToken cancellationToken) =>
        accounts.LoginAsync(onlineUserId, loginId, passwordHash, cancellationToken);

    public OnlineUserSnapshot RegisterConnection(NetworkConnectionContext connection) =>
        online.RegisterConnection(connection);

    public bool TryDisconnect(
        OnlineUserId onlineUserId,
        NetworkConnectionId connectionId,
        long expectedGeneration) =>
        online.TryDisconnect(onlineUserId, connectionId, expectedGeneration);

    public bool TryDisconnectConnection(NetworkConnectionId connectionId) =>
        online.TryDisconnectConnection(connectionId);

    public OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId) =>
        online.GetRequired(onlineUserId);

    public bool TryGetByConnection(
        NetworkConnectionId connectionId,
        out OnlineUserSnapshot? onlineUser) =>
        online.TryGetByConnection(connectionId, out onlineUser);

    public void Publish(SelectedSessionIdentity session) => active.Publish(session);

    public bool ClearExact(SelectedSessionIdentity session) => active.ClearExact(session);
}
