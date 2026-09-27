using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationMigrationTests
{
    private const string Password = "历史短密";
    private static string Legacy => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(Password)));
    private static Dictionary<string, string?> AllowFirst => new() { ["LauncherAuth:LegacyMd5Migration:AllowedAccountIds:0"] = "1" };

    [Fact]
    public async Task OnlyAuthorizedMatchingLegacyCredentialsMigrateWithoutChangingIdentityOrAccountState()
    {
        using var fixture = new AuthenticationFixture(settings: AllowFirst);
        await fixture.Seed("legacy", Legacy);
        await fixture.Seed("unauthorized", Legacy);
        await fixture.Scalar("UPDATE accounts SET cera=789,lucky_star=12 WHERE m_id='legacy'; SELECT 1");
        await fixture.Scalar("INSERT INTO characters(character_id,account_id,name,level) VALUES(123,1,'kept-character',86); SELECT 1");
        var original = await fixture.Store.FindAsync("legacy", default);
        var unauthorized = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("unauthorized", Password));
        var wrong = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("legacy", "wrong"));
        Assert.Equal(new AuthenticationResult(AuthenticationStatus.InvalidCredentials), unauthorized);
        Assert.Equal(unauthorized, wrong);
        Assert.Equal(Legacy, (await fixture.Store.FindAsync("legacy", default))!.PasswordHash);
        Assert.Equal(Legacy, (await fixture.Store.FindAsync("unauthorized", default))!.PasswordHash);
        var success = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("legacy", Password));
        Assert.Equal(AuthenticationStatus.Success, success.Status);
        var upgraded = await fixture.Store.FindAsync("legacy", default);
        Assert.Equal(original!.AccountId, upgraded!.AccountId);
        Assert.Equal(original.CreatedAt, upgraded.CreatedAt);
        Assert.StartsWith(PasswordCredentialService.Scheme, upgraded.PasswordHash);
        Assert.Equal(789L, await fixture.Scalar("SELECT cera FROM accounts WHERE m_id='legacy'"));
        Assert.Equal(12L, await fixture.Scalar("SELECT lucky_star FROM accounts WHERE m_id='legacy'"));
        Assert.Equal("1:kept-character:86", await fixture.Scalar("SELECT account_id||':'||name||':'||level FROM characters WHERE character_id=123"));
        var noAllowlist = new AuthenticationDomainService(fixture.Store, new PasswordCredentialService(100), fixture.Clock,
            AuthenticationRuleService.From(new ConfigurationBuilder().Build()),
            new LoginSessionRegistry(fixture.Scope, fixture.Clock, new LoginSessionOptions()), new LoginSessionOptions());
        await using var transaction = fixture.Scope.Begin();
        Assert.Equal(AuthenticationStatus.Success, (await noAllowlist.AuthenticateAndIssueTicketAsync("legacy", Password, default)).Status);
        await transaction.CommitAsync(default);
        Assert.Equal(upgraded.PasswordHash, (await fixture.Store.FindAsync("legacy", default))!.PasswordHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-md5")]
    [InlineData("gggggggggggggggggggggggggggggggg")]
    [InlineData("future:01234567890123456789012345678901")]
    public async Task AllowlistingNeverMakesAnUnknownRecordTrustworthy(string record)
    {
        using var fixture = new AuthenticationFixture(settings: AllowFirst);
        await fixture.Seed("legacy", record);
        Assert.Equal(AuthenticationStatus.InvalidCredentials,
            (await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("legacy", Password))).Status);
        Assert.Equal(record, (await fixture.Store.FindAsync("legacy", default))!.PasswordHash);
    }

    [Fact]
    public async Task ModernRehashIsPersistedAndAnUncommittedUpgradeRollsBackWithItsTicket()
    {
        using var fixture = new AuthenticationFixture();
        var old = new PasswordCredentialService(50).Hash("test password");
        await fixture.Seed("modern", old);
        await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("modern", "test password"));
        var upgraded = (await fixture.Store.FindAsync("modern", default))!.PasswordHash;
        Assert.NotEqual(old, upgraded);
        Assert.Equal(PasswordCheck.Verified, new PasswordCredentialService(100).Verify(upgraded, "test password"));
        await fixture.Seed("rollback", old);
        var service = (IAuthenticationDomainService)fixture.Services.GetService(typeof(IAuthenticationDomainService))!;
        AuthenticationResult rolledBack;
        await using (fixture.Scope.Begin())
        {
            rolledBack = await service.AuthenticateAndIssueTicketAsync(
                "rollback",
                "test password",
                default);
            Assert.Equal(AuthenticationStatus.Success, rolledBack.Status);
        }
        Assert.Equal(old, (await fixture.Store.FindAsync("rollback", default))!.PasswordHash);
        Assert.Null(await fixture.Consume(rolledBack.MId!, rolledBack.Ticket!));
        Assert.Equal(
            0L,
            await fixture.Scalar(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='login_tickets'"));
    }
}
