using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Sessions;

/// <summary>
/// Who a connection is, as one record behind one lock: the online user, the account it has
/// authenticated as, the session that account has selected, and how far that session has got.
/// </summary>
public sealed class OnlineIdentityTests
{
    private static readonly AccountId Account = new(41);
    private static readonly CharacterId Character = new(4101);

    [Fact]
    public void Every_lookup_answers_the_same_session_through_every_stage()
    {
        var identity = new Identity();
        var online = identity.Connect("Game:stages");
        var session = identity.SessionOn(online);

        identity.Sessions.Select(session);
        AssertFound(identity.Sessions, session);
        Assert.Equal(new SelectedSessionStanding(SelectedSessionPhase.Selected, false, false),
            identity.Sessions.StandingOf(session));

        identity.Sessions.EnterWorld(session);
        AssertFound(identity.Sessions, session);
        Assert.Equal(new SelectedSessionStanding(SelectedSessionPhase.InWorld, false, false),
            identity.Sessions.StandingOf(session));

        Assert.True(identity.Sessions.Fence(session));
        AssertFound(identity.Sessions, session);
        Assert.Equal(new SelectedSessionStanding(SelectedSessionPhase.InWorld, true, false),
            identity.Sessions.StandingOf(session));

        Assert.True(identity.Online.TryDisconnectConnection(online.Connection.ConnectionId));
        AssertFound(identity.Sessions, session);
        Assert.Equal(new SelectedSessionStanding(SelectedSessionPhase.InWorld, true, true),
            identity.Sessions.StandingOf(session));
    }

    [Fact]
    public void Standing_is_exact_to_the_generation_that_was_selected()
    {
        var identity = new Identity();
        var online = identity.Connect("Game:exact");
        var session = identity.SessionOn(online);
        identity.Sessions.Select(session);

        var otherGeneration = session with { ConnectionGeneration = session.ConnectionGeneration + 1 };

        Assert.Null(identity.Sessions.StandingOf(otherGeneration));
        Assert.False(identity.Sessions.IsInWorld(otherGeneration));
        Assert.False(identity.Sessions.Fence(otherGeneration));
    }

    [Fact]
    public void Entering_the_world_binds_the_character_to_the_online_user()
    {
        var identity = new Identity();
        var online = identity.Connect("Game:enter");
        var session = identity.SessionOn(online);
        identity.Sessions.Select(session);

        identity.Sessions.EnterWorld(session);

        var after = identity.Online.GetRequired(online.OnlineUserId);
        Assert.Equal(online.Generation + 1, after.Generation);
        Assert.Equal(Character, after.CurrentCharacterId);
        Assert.Equal(OnlineUserState.CharacterActive, after.State);
        Assert.True(identity.Sessions.IsInWorld(session));
    }

    [Fact]
    public void Entering_the_world_is_refused_for_a_stale_or_unregistered_connection()
    {
        var identity = new Identity();
        var online = identity.Connect("Game:refuse");
        var stale = identity.SessionOn(online) with { ConnectionGeneration = online.Generation + 5 };
        identity.Sessions.Select(stale);

        Assert.Throws<InvalidOperationException>(() => identity.Sessions.EnterWorld(stale));

        Assert.Equal(SelectedSessionPhase.Selected, identity.Sessions.StandingOf(stale)!.Phase);
        Assert.Equal(online.Generation, identity.Online.GetRequired(online.OnlineUserId).Generation);

        var unregistered = SelectedSessionIdentity.Create(
            new AccountId(42), new CharacterId(4201), new NetworkConnectionId("Game:nobody"), 2, 10011);
        identity.Sessions.Select(unregistered);
        Assert.Throws<InvalidOperationException>(() => identity.Sessions.EnterWorld(unregistered));

        var notSelected = SelectedSessionIdentity.Create(
            Account, new CharacterId(4199), online.Connection.ConnectionId, online.Generation, 10011);
        Assert.Throws<InvalidOperationException>(() => identity.Sessions.EnterWorld(notSelected));
    }

    [Fact]
    public void Releasing_a_session_in_the_world_unbinds_its_online_user()
    {
        var identity = new Identity();
        var online = identity.Connect("Game:release");
        var session = identity.SessionOn(online);
        identity.Sessions.Select(session);
        identity.Sessions.EnterWorld(session);
        var bound = identity.Online.GetRequired(online.OnlineUserId);

        Assert.True(identity.Sessions.ReleaseExact(session));

        var after = identity.Online.GetRequired(online.OnlineUserId);
        Assert.Null(identity.Sessions.Find(Account));
        Assert.Equal(bound.Generation + 1, after.Generation);
        Assert.Null(after.CurrentCharacterId);
        Assert.Equal(OnlineUserState.Authenticated, after.State);
        Assert.Null(identity.Sessions.StandingOf(session));
        Assert.False(identity.Sessions.IsInWorld(session));
    }

    [Fact]
    public void Releasing_a_replaced_session_leaves_its_replacement_alone()
    {
        var identity = new Identity();
        var online = identity.Connect("Game:replaced");
        var first = identity.SessionOn(online);
        identity.Sessions.Select(first);
        identity.Sessions.EnterWorld(first);
        Assert.True(identity.Sessions.ReleaseExact(first));
        var replacement = SelectedSessionIdentity.Create(
            Account, new CharacterId(4102), online.Connection.ConnectionId,
            identity.Online.GetRequired(online.OnlineUserId).Generation, 10011);
        identity.Sessions.Select(replacement);
        identity.Sessions.EnterWorld(replacement);
        var bound = identity.Online.GetRequired(online.OnlineUserId);

        Assert.False(identity.Sessions.ReleaseExact(first));

        Assert.Equal(replacement, identity.Sessions.Find(Account));
        Assert.Equal(bound, identity.Online.GetRequired(online.OnlineUserId));
        Assert.True(identity.Sessions.IsInWorld(replacement));
    }

    [Fact]
    public void A_revoked_connection_leaves_its_session_waiting_for_cleanup()
    {
        var identity = new Identity();
        var online = identity.Connect("Game:revoked");
        var session = identity.SessionOn(online);
        identity.Sessions.Select(session);
        identity.Sessions.EnterWorld(session);
        Assert.True(identity.Sessions.IsInWorld(session));

        Assert.True(identity.Online.TryDisconnectConnection(online.Connection.ConnectionId));

        Assert.False(identity.Sessions.IsInWorld(session));
        Assert.NotNull(identity.Sessions.StandingOf(session));
        Assert.True(identity.Sessions.StandingOf(session)!.ConnectionRevoked);
        Assert.Equal(session, identity.Sessions.FindByConnection(online.Connection.ConnectionId));

        Assert.True(identity.Sessions.ReleaseExact(session));

        Assert.Null(identity.Sessions.FindByConnection(online.Connection.ConnectionId));
        Assert.Null(identity.Sessions.StandingOf(session));
        Assert.False(identity.Sessions.IsInWorld(session));
    }

    [Fact]
    public void A_selection_whose_connection_was_never_registered_reads_as_revoked()
    {
        var identity = new Identity();
        var session = SelectedSessionIdentity.Create(
            Account, Character, new NetworkConnectionId("Game:absent"), 2, 10011);

        identity.Sessions.Select(session);

        Assert.Equal(new SelectedSessionStanding(SelectedSessionPhase.Selected, false, true),
            identity.Sessions.StandingOf(session));
    }

    [Fact]
    public void Selection_still_refuses_a_character_or_wire_identity_held_by_another_account()
    {
        var identity = new Identity();
        identity.Sessions.Select(SelectedSessionIdentity.Create(
            Account, Character, new NetworkConnectionId("Game:holder"), 2, 10011));

        Assert.Throws<InvalidOperationException>(() => identity.Sessions.Select(
            SelectedSessionIdentity.Create(new AccountId(43), Character,
                new NetworkConnectionId("Game:taker"), 2, 10011)));
        Assert.Null(identity.Sessions.Find(new AccountId(43)));
    }

    [Fact]
    public async Task Fencing_refuses_the_session_and_spares_its_successor()
    {
        var identity = new Identity();
        var online = identity.Connect("Game:fenced");
        var session = identity.SessionOn(online);
        identity.Sessions.Select(session);
        identity.Sessions.EnterWorld(session);
        var safety = identity.Safety();

        await safety.BlockAsync(new SelectedSessionRecipient(session, null), new Exception("unknown"), "test");

        Assert.True(identity.Sessions.StandingOf(session)!.Fenced);
        Assert.Throws<InvalidOperationException>(() => safety.DemandCurrent(session));

        Assert.True(identity.Sessions.ReleaseExact(session));
        var successor = SelectedSessionIdentity.Create(
            Account, Character, online.Connection.ConnectionId,
            identity.Online.GetRequired(online.OnlineUserId).Generation, 10011);
        identity.Sessions.Select(successor);
        identity.Sessions.EnterWorld(successor);

        safety.DemandCurrent(successor);
        Assert.False(identity.Sessions.StandingOf(successor)!.Fenced);
    }

    private static void AssertFound(ISelectedSessionDomainService sessions, SelectedSessionIdentity session)
    {
        Assert.Equal(session, sessions.Find(session.AccountId));
        Assert.Equal(session, sessions.FindByCharacter(session.CharacterId));
        Assert.Equal(session, sessions.FindByConnection(session.ConnectionId));
        Assert.Equal(session, sessions.FindByWireUserId(session.WireUserId));
        Assert.Contains(session, ((ISelectedSessionSnapshotQuery)sessions).Snapshot());
    }

    private sealed class Identity
    {
        private readonly OnlineStateRegistry state = new(new DomainTransactionScope());

        public Identity()
        {
            Sessions = new SelectedSessionDomainService(state);
            Online = new OnlineUserDomainService(state);
        }

        public SelectedSessionDomainService Sessions { get; }

        public OnlineUserDomainService Online { get; }

        public OnlineUserSnapshot Connect(string connectionId)
        {
            var registered = Online.RegisterConnection(new NetworkConnectionContext(
                new NetworkConnectionId(connectionId), NetworkEndpointKind.Game, 10011));
            state.Authenticate(registered.OnlineUserId, Account);
            return Online.GetRequired(registered.OnlineUserId);
        }

        public SelectedSessionIdentity SessionOn(OnlineUserSnapshot online) =>
            SelectedSessionIdentity.Create(
                Account, Character, online.Connection.ConnectionId, online.Generation, 10011);

        public SelectedSessionSafety Safety()
        {
            var services = new ServiceCollection()
                .AddSingleton<ISelectedSessionDomainService>(Sessions)
                .BuildServiceProvider();
            return new SelectedSessionSafety(
                services, new SelectedSessionTransitionGate(), NullLogger<SelectedSessionSafety>.Instance);
        }
    }
}
