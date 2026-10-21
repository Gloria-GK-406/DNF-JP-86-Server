using Dfo.Server.Application.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SelectedSessionDomainServiceTests
{
    [Fact]
    public void WireUserIdentityRejectsZeroNegativeAndTruncatingCharacterIds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SelectedSessionIdentity.ToWireUserId(new CharacterId(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SelectedSessionIdentity.ToWireUserId(new CharacterId(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SelectedSessionIdentity.ToWireUserId(new CharacterId(65_536)));
        Assert.Equal((ushort)65_535, SelectedSessionIdentity.ToWireUserId(new CharacterId(65_535)));
    }

    [Fact]
    public void ReplacementAndStaleRemovalPreserveTheNewExactGeneration()
    {
        TestGameplay.Run(() =>
        {
            var sessions = Dfo.Server.Testing.TestSessions.Directory();
            var first = Session(1, 101, "Game:first", 2);
            var replacement = Session(1, 102, "Game:replacement", 7);

            Assert.Null(sessions.Select(first));
            Assert.Equal(first, sessions.Select(replacement));
            Assert.False(sessions.ReleaseExact(first));
            Assert.Equal(replacement, sessions.Find(new AccountId(1)));
            Assert.True(sessions.ReleaseExact(replacement));
            Assert.Null(sessions.Find(new AccountId(1)));
        });
    }

    [Fact]
    public void WireIdAndCharacterLookupsEachFindTheirOwnSession()
    {
        TestGameplay.Run(() =>
        {
            var sessions = Dfo.Server.Testing.TestSessions.Directory();
            var third = Session(3, 303, "Game:c", 1);
            var first = Session(1, 101, "Game:a", 1);
            var second = Session(2, 202, "Game:b", 1);
            sessions.Select(third);
            sessions.Select(first);
            sessions.Select(second);

            Assert.Equal(second, sessions.FindByWireUserId(202));
            Assert.Equal(third, sessions.FindByCharacter(new CharacterId(303)));
        });
    }

    [Fact]
    public void RegistrationRejectsCharacterAndWireIdentityCollisionAcrossAccounts()
    {
        TestGameplay.Run(() =>
        {
            var sessions = Dfo.Server.Testing.TestSessions.Directory();
            sessions.Select(Session(1, 101, "Game:first", 1));

            Assert.Throws<InvalidOperationException>(() =>
                sessions.Select(Session(2, 101, "Game:second", 1)));
            Assert.Null(sessions.Find(new AccountId(2)));
        });
    }

    private static SelectedSessionIdentity Session(int account, int character, string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(account), new CharacterId(character),
            new NetworkConnectionId(connection), generation, listenerPort: 10011);
}
