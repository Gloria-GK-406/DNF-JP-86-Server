using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SelectedSessionDirectoryTests
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
        var directory = new SelectedSessionDirectory();
        var first = Session(1, 101, "Game:first", 2);
        var replacement = Session(1, 102, "Game:replacement", 7);

        Assert.Null(directory.RegisterReplacing(first));
        Assert.Equal(first, directory.RegisterReplacing(replacement));
        Assert.False(directory.TryRemoveExact(first));
        Assert.Equal(replacement, directory.Find(new AccountId(1)));
        Assert.True(directory.TryRemoveExact(replacement));
        Assert.Null(directory.Find(new AccountId(1)));
    }

    [Fact]
    public void RecipientResolutionIsStableAndOnlyReturnsCurrentRegistrations()
    {
        var directory = new SelectedSessionDirectory();
        var third = Session(3, 303, "Game:c", 1);
        var first = Session(1, 101, "Game:a", 1);
        var second = Session(2, 202, "Game:b", 1);
        directory.RegisterReplacing(third);
        directory.RegisterReplacing(first);
        directory.RegisterReplacing(second);

        Assert.Equal([101, 202, 303], directory.Current()
            .Select(value => value.CharacterId.Value).ToArray());
        Assert.Equal(second, directory.FindByWireUserId(202));
        Assert.Equal(third, directory.FindByCharacter(new CharacterId(303)));
    }

    [Fact]
    public void RegistrationRejectsCharacterAndWireIdentityCollisionAcrossAccounts()
    {
        var directory = new SelectedSessionDirectory();
        directory.RegisterReplacing(Session(1, 101, "Game:first", 1));

        Assert.Throws<InvalidOperationException>(() =>
            directory.RegisterReplacing(Session(2, 101, "Game:second", 1)));
        Assert.Null(directory.Find(new AccountId(2)));
    }

    private static SelectedSessionIdentity Session(int account, int character, string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(account), new CharacterId(character),
            new NetworkConnectionId(connection), generation, listenerPort: 10011);
}
