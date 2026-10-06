using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.Transactions;
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
/// holding three references at every call site would obscure what each test is asserting.
/// Online operations forward to production services sharing one registry. AuthenticateForTestAsync
/// explicitly seeds the identity that real ticket redemption publishes; it does not test authentication.
/// </remarks>
internal sealed class UserDomainServices
    : IOnlineUserDomainService, IActiveCharacterDomainService, IGameSessionEndpointDomainService
{
    private readonly AccountId accountId;
    private readonly OnlineStateRegistry state;
    private readonly OnlineUserDomainService online;
    private readonly ActiveCharacterDomainService active;
    private readonly GameSessionEndpointDomainService endpoints;

    public UserDomainServices(AccountId accountId)
    {
        var state = new OnlineStateRegistry(new DomainTransactionScope());
        this.accountId = accountId;
        this.state = state;
        online = new OnlineUserDomainService(state);
        active = new ActiveCharacterDomainService(state);
        endpoints = new GameSessionEndpointDomainService(state);
    }

    /// <summary>Seeds the identity that successful ticket redemption would publish.</summary>
    public ValueTask<AccountId> AuthenticateForTestAsync(OnlineUserId onlineUserId)
    {
        this.state.Authenticate(onlineUserId, this.accountId);
        return ValueTask.FromResult(this.accountId);
    }

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

    public bool TryObserve(GameSessionIdentity session, SessionEndpoint endpoint) =>
        endpoints.TryObserve(session, endpoint);

    public SessionEndpoint? Find(GameSessionIdentity session) => endpoints.Find(session);
}
