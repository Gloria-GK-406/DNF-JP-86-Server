using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationFailureTests
{
    [Fact]
    public async Task TicketWriteFailureRollsBackLegacyUpgradeAndDoesNotLogSecrets()
    {
        const string password = "秘密-sentinel-password";
        const string loginId = "sentinel-login";
        var legacy = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(password)));
        using var fixture = new AuthenticationFixture(settings: new()
        { ["LauncherAuth:LegacyMd5Migration:AllowedAccountIds:0"] = "1" });
        await fixture.Seed(loginId, legacy);
        await fixture.Scalar("CREATE TRIGGER reject_ticket BEFORE INSERT ON login_tickets BEGIN SELECT RAISE(ABORT,'Authentication write rejected.'); END; SELECT 1");
        var failure = await Assert.ThrowsAsync<SqliteException>(async () =>
            await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand(loginId, password)));
        Assert.Equal(legacy, (await fixture.Store.FindAsync(loginId, default))!.PasswordHash);
        Assert.Equal(0L, await fixture.Scalar("SELECT COUNT(*) FROM login_tickets"));
        await fixture.Scalar("DROP TRIGGER reject_ticket; SELECT 1");
        var result = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand(loginId, password));
        Assert.Equal(AuthenticationStatus.Success, result.Status);
        var modern = (await fixture.Store.FindAsync(loginId, default))!.PasswordHash;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(result.Ticket!)));
        var observed = fixture.Logs.Text + failure + result + new AuthenticateAndIssueTicketCommand(loginId, password)
            + new RegisterAccountCommand(loginId, password);
        foreach (var secret in new[] { password, loginId, legacy, modern, result.Ticket!, digest })
            Assert.DoesNotContain(secret, observed);
    }

    [Fact]
    public async Task CancellationBeforeCommitLeavesTicketAvailableAndCredentialCompareAndSwapRejectsStaleValues()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var account = (await fixture.Store.FindAsync("player", default))!;
        var login = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        var service = fixture.Services.GetRequiredService<IAuthenticationDomainService>();
        await using (var lease = fixture.Scope.Begin())
        {
            Assert.NotNull(await service.ConsumeTicketAsync("player", login.Ticket!, default));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await lease.CommitAsync(canceled.Token));
        }
        Assert.NotNull(await fixture.Consume("player", login.Ticket!));
        await using (var lease = fixture.Scope.Begin())
        {
            Assert.False(await fixture.Store.TryReplaceCredentialAsync(account.AccountId, "stale", "replacement", default));
            await lease.CommitAsync(default);
        }
        Assert.Equal(account.PasswordHash, (await fixture.Store.FindAsync("player", default))!.PasswordHash);
    }

    [Fact]
    public async Task DefaultEmptyAllowlistAndInvalidCommandsCannotCreateCredentialsOrTickets()
    {
        using var fixture = new AuthenticationFixture();
        const string password = "short";
        var legacy = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(password)));
        await fixture.Seed("legacy", legacy);
        Assert.Equal(AuthenticationStatus.InvalidCredentials, (await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("legacy", password))).Status);
        var invalidRegister = await fixture.Bus.SendAsync(new RegisterAccountCommand("invalid?name", "long password"));
        var invalidLogin = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("legacy", ""));
        Assert.Equal(AuthenticationStatus.InvalidRequest, invalidRegister.Status);
        Assert.True(invalidRegister.FieldErrors!.ContainsKey("loginId"));
        Assert.Equal(AuthenticationStatus.InvalidRequest, invalidLogin.Status);
        Assert.True(invalidLogin.FieldErrors!.ContainsKey("password"));
        Assert.Equal(1L, await fixture.Scalar("SELECT COUNT(*) FROM accounts"));
        Assert.Equal(0L, await fixture.Scalar("SELECT COUNT(*) FROM login_tickets"));
        Assert.Equal(legacy, (await fixture.Store.FindAsync("legacy", default))!.PasswordHash);
    }
}
