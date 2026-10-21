using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Testing;

/// <summary>Selected sessions for tests, over the online identity they belong to.</summary>
/// <remarks>
/// A selection lives beside the online user that made it, so a test that needs a session in the
/// world needs its connection registered and authenticated first, and the session has to carry
/// the generation that connection was at. <see cref="InWorld"/> does exactly what selecting a
/// character does, in that order, and hands back the session it produced.
/// </remarks>
internal static class TestSessions
{
    public static SelectedSessionDomainService Directory() => Directory(out _);

    public static SelectedSessionDomainService Directory(out OnlineStateRegistry state)
    {
        state = new OnlineStateRegistry(new DomainTransactionScope());
        return new SelectedSessionDomainService(state);
    }

    /// <summary>Registers, authenticates, selects and enters one game connection.</summary>
    public static SelectedSessionIdentity InWorld(
        this Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService sessions,
        OnlineStateRegistry state,
        AccountId accountId,
        CharacterId characterId,
        string connectionId,
        int listenerPort = 10011)
    {
        var session = Selected(sessions, state, accountId, characterId, connectionId, listenerPort);
        sessions.EnterWorld(session);
        return session;
    }

    /// <summary>Registers, authenticates and selects one game connection, without entering it.</summary>
    public static SelectedSessionIdentity Selected(
        this Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService sessions,
        OnlineStateRegistry state,
        AccountId accountId,
        CharacterId characterId,
        string connectionId,
        int listenerPort = 10011)
    {
        var online = state.RegisterConnection(new NetworkConnectionContext(
            new NetworkConnectionId(connectionId), NetworkEndpointKind.Game, listenerPort));
        state.Authenticate(online.OnlineUserId, accountId);
        var session = SelectedSessionIdentity.Create(
            accountId, characterId, online.Connection.ConnectionId,
            state.GetRequiredUser(online.OnlineUserId).Generation, listenerPort);
        sessions.Select(session);
        return session;
    }

    /// <summary>
    /// Enters the session into the world unless a fixture has already done so. Entering twice is
    /// refused in production, as publishing twice always was.
    /// </summary>
    public static void EnsureInWorld(
        this Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService sessions,
        SelectedSessionIdentity session)
    {
        if (!sessions.IsInWorld(session))
            sessions.EnterWorld(session);
    }

    /// <summary>
    /// Enters an already-built session: registers its connection, and authenticates it, under the
    /// generation the session carries. Only for sessions created at the authenticated generation.
    /// </summary>
    public static void EnterWorldWithConnection(
        this Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService sessions,
        OnlineStateRegistry state,
        SelectedSessionIdentity session)
    {
        if (!state.TryGetByConnection(session.ConnectionId, out _))
        {
            var online = state.RegisterConnection(new NetworkConnectionContext(
                session.ConnectionId, NetworkEndpointKind.Game, session.ListenerPort));
            state.Authenticate(online.OnlineUserId, session.AccountId);
        }
        if (sessions.Find(session.AccountId) != session)
            sessions.Select(session);
        sessions.EnterWorld(session);
    }

    /// <summary>
    /// The same connection selects the same character again: the previous session is released
    /// first, and the new one carries the generation the connection has reached by then.
    /// </summary>
    public static SelectedSessionIdentity ReselectOnSameConnection(
        this Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService sessions,
        OnlineStateRegistry state,
        SelectedSessionIdentity previous)
    {
        sessions.ReleaseExact(previous);
        if (!state.TryGetByConnection(previous.ConnectionId, out var owner) || owner is null)
            throw new InvalidOperationException("The connection is no longer registered.");
        var session = previous with { ConnectionGeneration = owner.Generation };
        sessions.Select(session);
        sessions.EnterWorld(session);
        return session;
    }
}
