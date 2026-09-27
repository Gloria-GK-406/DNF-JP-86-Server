using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationTicketTests
{
    [Fact]
    public async Task PasswordLoginIssuesRotatingMemoryOnlyCredentialsAndRevokesTheOlderPair()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var first = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        var second = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        Assert.Equal(AuthenticationStatus.Success, first.Status);
        Assert.Matches("^[0-9]{6}$", first.MId!);
        Assert.NotNull(first.Ticket);
        Assert.Matches("^[0-9A-F]{32}$", first.Ticket);
        Assert.NotEqual(first.MId, second.MId);
        Assert.NotEqual(first.Ticket, second.Ticket);
        Assert.Equal(fixture.Clock.Now.AddSeconds(180), first.ExpiresAtUtc);
        Assert.Null(await fixture.Consume(first.MId!, first.Ticket));
        Assert.NotNull(await fixture.Consume(second.MId!, second.Ticket!));
        Assert.Equal(
            0L,
            await fixture.Scalar(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='login_tickets'"));
        Assert.DoesNotContain(first.Ticket, first.ToString());
    }

    [Fact]
    public async Task NewProcessRegistryStartsEmptyWhileThePersistentAccountSurvives()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var login = await fixture.Bus.SendAsync(
            new AuthenticateAndIssueTicketCommand("player", "test password"));

        var afterRestart = new LoginSessionRegistry(fixture.Scope, fixture.Clock, new LoginSessionOptions());
        await using var transaction = fixture.Scope.Begin();
        Assert.Null(afterRestart.TryBind(new Dfo.Server.Data.Networking.NetworkConnectionId("restart"), login.MId!, login.Ticket!));
        await transaction.CommitAsync(default);

        Assert.NotNull(await fixture.Store.FindAsync("player", default));
        Assert.Equal(
            0L,
            await fixture.Scalar(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='login_tickets'"));
    }

    [Fact]
    public async Task MissingAccountAndWrongPasswordShareFailureAndNeitherWritesATicket()
    {
        var passwords = new ObservedPasswords();
        using var fixture = new AuthenticationFixture(passwords);
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var wrong = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "wrong"));
        var missing = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("missing", "wrong"));
        Assert.Equal(new AuthenticationResult(AuthenticationStatus.InvalidCredentials), wrong);
        Assert.Equal(wrong, missing);
        Assert.Equal(1, passwords.DummyVerifications);
        Assert.Equal(
            0L,
            await fixture.Scalar(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='login_tickets'"));
        Assert.Equal(1L, await fixture.Scalar("SELECT COUNT(*) FROM accounts"));
    }

    private sealed class ObservedPasswords : IPasswordCredentialRuleService
    {
        private readonly PasswordCredentialService inner = new(100);
        public int DummyVerifications { get; private set; }
        public string Hash(string password) => inner.Hash(password);
        public PasswordCheck Verify(string record, string password) => inner.Verify(record, password);
        public void VerifyDummy(string password) { DummyVerifications++; inner.VerifyDummy(password); }
    }
}
