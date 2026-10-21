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
/// The online-identity services over one registry, as a single object.
/// </summary>
/// <remarks>
/// Production composes these separately; this fixture exists because these tests exercise the
/// online-user, selected-session and endpoint responsibilities of one connection together, and
/// holding several references at every call site would obscure what each test is asserting.
/// Operations forward to production services sharing one registry. AuthenticateForTestAsync
/// explicitly seeds the identity that real ticket redemption publishes; it does not test authentication.
/// </remarks>
internal sealed class UserDomainServices
    : IOnlineUserDomainService, IGameSessionEndpointDomainService
{
    private readonly AccountId accountId;
    private readonly OnlineStateRegistry state;
    private readonly OnlineUserDomainService online;
    private readonly GameSessionEndpointDomainService endpoints;

    public UserDomainServices(AccountId accountId)
    {
        var state = new OnlineStateRegistry(new DomainTransactionScope());
        this.accountId = accountId;
        this.state = state;
        online = new OnlineUserDomainService(state);
        Sessions = new Dfo.Server.Application.Sessions.SelectedSessionDomainService(state);
        endpoints = new GameSessionEndpointDomainService(state);
    }

    /// <summary>The selections made over these connections.</summary>
    public Dfo.Server.Application.Sessions.SelectedSessionDomainService Sessions { get; }

    public OnlineStateRegistry State => state;

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

    /// <summary>Selects the session if it is not already selected, then enters it into the world.</summary>
    public void Publish(SelectedSessionIdentity session)
    {
        if (Sessions.Find(session.AccountId) != session)
            Sessions.Select(session);
        Sessions.EnterWorld(session);
    }

    public bool ClearExact(SelectedSessionIdentity session) => Sessions.ReleaseExact(session);

    public bool IsActiveExact(SelectedSessionIdentity session) => online.IsActiveExact(session);

    public bool TryObserve(GameSessionIdentity session, SessionEndpoint endpoint) =>
        endpoints.TryObserve(session, endpoint);

    public SessionEndpoint? Find(GameSessionIdentity session) => endpoints.Find(session);
}
