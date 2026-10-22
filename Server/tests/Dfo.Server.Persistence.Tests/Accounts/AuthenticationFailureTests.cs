using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationFailureTests
{
    [Fact]
    public async Task LegacyUpgradeAndMemorySessionIssuanceDoNotLogSecrets()
    {
        const string password = "秘密-sentinel-password";
        const string loginId = "sentinel-login";
        var legacy = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(password)));
        using var fixture = new AuthenticationFixture(settings: new()
        { ["LauncherAuth:LegacyMd5Migration:AllowedAccountIds:0"] = "1" });
        await fixture.Seed(loginId, legacy);
        var result = await fixture.LoginAsync(loginId, password);
        Assert.Equal(AuthenticationStatus.Success, result.Status);
        var modern = (await fixture.Store.FindAsync(loginId, default))!.PasswordHash;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(result.Ticket!)));
        var reader = fixture.Services.GetRequiredService<ILauncherCredentialReader>();
        var verified = (await reader.VerifyLoginAsync(loginId, password, default)).Login!;
        var prepared = (await reader.PrepareRegistrationAsync(loginId, password, default)).Prepared!;
        var observed = fixture.Logs.Text + result + new AuthenticateAndIssueTicketCommand(verified) + verified
            + new RegisterAccountCommand(prepared) + prepared;
        foreach (var secret in new[] { password, loginId, legacy, modern, result.Ticket!, digest })
            Assert.DoesNotContain(secret, observed);
    }

    [Fact]
    public async Task CancellationBeforeCommitLeavesTicketAvailableAndCredentialCompareAndSwapRejectsStaleValues()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.RegisterAsync("player", "test password");
        var account = (await fixture.Store.FindAsync("player", default))!;
        var login = await fixture.LoginAsync("player", "test password");
        var service = fixture.Services.GetRequiredService<IAuthenticationDomainService>();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var lease = fixture.Scope.Begin())
            {
                Assert.NotNull(await service.BindSessionAsync(new Dfo.Server.Data.Networking.NetworkConnectionId("test-binding"), login.MId!, login.Ticket!, default));
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await lease.CommitAsync(canceled.Token));
            }
        });
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket!));
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var lease = fixture.Scope.Begin())
            {
                Assert.False(await fixture.Store.TryReplaceCredentialAsync(account.AccountId, "stale", "replacement", default));
                await lease.CommitAsync(default);
            }
        });
        Assert.Equal(account.PasswordHash, (await fixture.Store.FindAsync("player", default))!.PasswordHash);
    }

    [Fact]
    public async Task DefaultEmptyAllowlistAndInvalidCommandsCannotCreateCredentialsOrTickets()
    {
        using var fixture = new AuthenticationFixture();
        const string password = "short";
        var legacy = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(password)));
        await fixture.Seed("legacy", legacy);
        Assert.Equal(AuthenticationStatus.InvalidCredentials, (await fixture.LoginAsync("legacy", password)).Status);
        var invalidRegister = await fixture.RegisterAsync("invalid?name", "long password");
        var invalidLogin = await fixture.LoginAsync("legacy", "");
        Assert.Equal(AuthenticationStatus.InvalidRequest, invalidRegister.Status);
        Assert.True(invalidRegister.FieldErrors!.ContainsKey("loginId"));
        Assert.Equal(AuthenticationStatus.InvalidRequest, invalidLogin.Status);
        Assert.True(invalidLogin.FieldErrors!.ContainsKey("password"));
        Assert.Equal(1L, await fixture.Scalar("SELECT COUNT(*) FROM accounts"));
        Assert.Equal(
            0L,
            await fixture.Scalar(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='login_tickets'"));
        Assert.Equal(legacy, (await fixture.Store.FindAsync("legacy", default))!.PasswordHash);
    }
}
