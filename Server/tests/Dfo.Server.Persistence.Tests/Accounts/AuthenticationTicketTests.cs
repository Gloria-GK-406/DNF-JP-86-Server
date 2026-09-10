using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationTicketTests
{
    [Fact]
    public async Task PasswordLoginIssuesIndependentSafeTicketsWithOnlyDigestsPersistedForExactlySixtySeconds()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var first = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        var second = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        Assert.Equal(AuthenticationStatus.Success, first.Status);
        Assert.Equal("player", first.LoginId);
        Assert.NotNull(first.Ticket);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", first.Ticket);
        Assert.NotEqual(first.Ticket, second.Ticket);
        Assert.Equal(fixture.Clock.Now.AddSeconds(60), first.ExpiresAtUtc);
        Assert.Equal(2L, await fixture.Scalar("SELECT COUNT(*) FROM login_tickets"));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(first.Ticket)));
        Assert.Equal(1L, await fixture.Scalar($"SELECT COUNT(*) FROM login_tickets WHERE ticket_digest='{digest}'"));
        Assert.DoesNotContain(first.Ticket, (string)(await fixture.Scalar("SELECT group_concat(ticket_digest) FROM login_tickets"))!);
        Assert.DoesNotContain(first.Ticket, first.ToString());
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
        Assert.Equal(0L, await fixture.Scalar("SELECT COUNT(*) FROM login_tickets"));
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
