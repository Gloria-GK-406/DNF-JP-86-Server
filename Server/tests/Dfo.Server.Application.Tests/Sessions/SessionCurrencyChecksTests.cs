using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Sessions;

/// <summary>
/// The two questions a caller holding a session may ask -- is it still valid, is it in the world --
/// and the one a caller holding only a character, wire user or connection asks: who is in the world there.
/// </summary>
public sealed class SessionCurrencyChecksTests
{
    private static readonly AccountId Account = new(52);
    private static readonly CharacterId Character = new(5201);

    [Fact]
    public void A_selection_is_valid_but_not_yet_in_the_world()
    {
        TestGameplay.Run(() =>
        {
            var identity = new Identity();
            var session = identity.SessionOn(identity.Connect("Game:selected"));
            identity.Sessions.Select(session);

            Assert.True(identity.Sessions.IsSelected(session));
            AssertNotInWorld(identity.Sessions, session);
        });
    }

    [Fact]
    public void Entering_the_world_answers_every_question_with_the_session()
    {
        TestGameplay.Run(() =>
        {
            var identity = new Identity();
            var session = identity.SessionOn(identity.Connect("Game:world"));
            identity.Sessions.Select(session);
            identity.Sessions.EnterWorld(session);

            Assert.True(identity.Sessions.IsSelected(session));
            AssertInWorld(identity.Sessions, session);
        });
    }

    [Fact]
    public void A_fence_does_not_change_the_answers()
    {
        TestGameplay.Run(() =>
        {
            var identity = new Identity();
            var session = identity.SessionOn(identity.Connect("Game:fenced"));
            identity.Sessions.Select(session);
            identity.Sessions.EnterWorld(session);

            Assert.True(identity.Sessions.Fence(session));

            Assert.True(identity.Sessions.IsSelected(session));
            AssertInWorld(identity.Sessions, session);
        });
    }

    [Fact]
    public void A_revoked_connection_leaves_a_record_that_is_neither_valid_nor_in_the_world()
    {
        TestGameplay.Run(() =>
        {
            var identity = new Identity();
            var online = identity.Connect("Game:revoked");
            var session = identity.SessionOn(online);
            identity.Sessions.Select(session);
            identity.Sessions.EnterWorld(session);

            Assert.True(identity.Online.TryDisconnectConnection(online.Connection.ConnectionId));

            Assert.NotNull(identity.Sessions.StandingOf(session));
            Assert.False(identity.Sessions.IsSelected(session));
            AssertNotInWorld(identity.Sessions, session);
        });
    }

    [Fact]
    public void A_connection_revoked_before_entering_the_world_is_no_longer_valid()
    {
        TestGameplay.Run(() =>
        {
            var identity = new Identity();
            var online = identity.Connect("Game:revoked-early");
            var session = identity.SessionOn(online);
            identity.Sessions.Select(session);

            Assert.True(identity.Online.TryDisconnectConnection(online.Connection.ConnectionId));

            Assert.NotNull(identity.Sessions.StandingOf(session));
            Assert.False(identity.Sessions.IsSelected(session));
            AssertNotInWorld(identity.Sessions, session);
        });
    }

    [Fact]
    public void A_replaced_session_answers_nothing_and_its_replacement_answers_for_the_keys()
    {
        TestGameplay.Run(() =>
        {
            var identity = new Identity();
            var online = identity.Connect("Game:replaced");
            var old = identity.SessionOn(online);
            identity.Sessions.Select(old);
            identity.Sessions.EnterWorld(old);
            identity.Sessions.ReleaseExact(old);
            var replacement = identity.SessionOn(identity.Online.GetRequired(online.OnlineUserId));
            identity.Sessions.Select(replacement);
            identity.Sessions.EnterWorld(replacement);

            Assert.NotEqual(old, replacement);
            Assert.False(identity.Sessions.IsSelected(old));
            Assert.False(identity.Sessions.IsInWorld(old));
            AssertInWorld(identity.Sessions, replacement);
        });
    }

    [Fact]
    public void A_released_session_answers_nothing()
    {
        TestGameplay.Run(() =>
        {
            var identity = new Identity();
            var session = identity.SessionOn(identity.Connect("Game:released"));
            identity.Sessions.Select(session);
            identity.Sessions.EnterWorld(session);

            Assert.True(identity.Sessions.ReleaseExact(session));

            Assert.False(identity.Sessions.IsSelected(session));
            AssertNotInWorld(identity.Sessions, session);
        });
    }

    [Fact]
    public void Another_generation_of_the_same_session_is_not_the_session()
    {
        TestGameplay.Run(() =>
        {
            var identity = new Identity();
            var session = identity.SessionOn(identity.Connect("Game:generation"));
            identity.Sessions.Select(session);
            identity.Sessions.EnterWorld(session);

            var other = session with { ConnectionGeneration = session.ConnectionGeneration + 1 };

            Assert.False(identity.Sessions.IsSelected(other));
            Assert.False(identity.Sessions.IsInWorld(other));
        });
    }

    private static void AssertInWorld(ISelectedSessionDomainService sessions, SelectedSessionIdentity session)
    {
        Assert.True(sessions.IsInWorld(session));
        Assert.True(sessions.TryGetInWorld(session.CharacterId, out var byCharacter));
        Assert.Equal(session, byCharacter);
        Assert.True(sessions.TryGetInWorld(session.WireUserId, out var byWire));
        Assert.Equal(session, byWire);
        Assert.True(sessions.TryGetInWorld(session.ConnectionId, out var byConnection));
        Assert.Equal(session, byConnection);
    }

    private static void AssertNotInWorld(ISelectedSessionDomainService sessions, SelectedSessionIdentity session)
    {
        Assert.False(sessions.IsInWorld(session));
        Assert.False(sessions.TryGetInWorld(session.CharacterId, out _));
        Assert.False(sessions.TryGetInWorld(session.WireUserId, out _));
        Assert.False(sessions.TryGetInWorld(session.ConnectionId, out _));
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
    }
}
