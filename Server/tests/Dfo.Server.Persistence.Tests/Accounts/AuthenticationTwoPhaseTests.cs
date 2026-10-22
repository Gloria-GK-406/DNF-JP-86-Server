using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Persistence.Tests.Accounts;

/// <summary>
/// RQ-007 splits a login in two: the record is verified off the executor, then the login CMD writes.
/// The CMD must notice a record that changed in between instead of trusting the earlier check.
/// </summary>
public sealed class AuthenticationTwoPhaseTests
{
    /// <summary>L1: a record replaced between the two phases issues nothing and tells the caller so.</summary>
    [Fact]
    public async Task A_record_replaced_between_the_check_and_the_login_cmd_issues_no_session()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.RegisterAsync("player", "test password");
        var earlier = await fixture.LoginAsync("player", "test password");
        var verified = await fixture.Services.GetRequiredService<ILauncherCredentialReader>()
            .VerifyLoginAsync("player", "test password", default);
        Assert.NotNull(verified.Login);

        var replacement = new PasswordCredentialService(100).Hash("another password");
        await fixture.Scalar($"UPDATE accounts SET password_hash='{replacement}' WHERE m_id='player'; SELECT 1");
        var result = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand(verified.Login));

        Assert.Equal(new AuthenticationResult(AuthenticationStatus.CredentialChanged), result);
        // Issuing a session would have revoked the earlier pair.
        Assert.NotNull(await fixture.Consume(earlier.MId!, earlier.Ticket!));
        Assert.Equal(replacement, (await fixture.Store.FindAsync("player", default))!.PasswordHash);
    }
}
