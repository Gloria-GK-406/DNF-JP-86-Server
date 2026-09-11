using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationRegistrationTests
{
    [Fact]
    public async Task RegistrationPersistsDistinctModernRecordsAndOnlyOneConcurrentDuplicateWins()
    {
        using var fixture = new AuthenticationFixture();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "same password")))));
        Assert.Single(results, result => result.Status == AuthenticationStatus.Success);
        Assert.Equal(5, results.Count(result => result.Status == AuthenticationStatus.AccountExists));
        Assert.Equal(AuthenticationStatus.Success, (await fixture.Bus.SendAsync(new RegisterAccountCommand("second", "same password"))).Status);
        var first = await fixture.Store.FindAsync("player", default);
        var second = await fixture.Store.FindAsync("second", default);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.StartsWith(PasswordCredentialService.Scheme, first.PasswordHash);
        Assert.NotEqual(first.PasswordHash, second.PasswordHash);
        Assert.Equal(2L, await fixture.Scalar("SELECT COUNT(*) FROM accounts"));
        Assert.Equal(
            0L,
            await fixture.Scalar(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='login_tickets'"));
        await using var reopened = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False");
        await reopened.OpenAsync();
        using var query = reopened.CreateCommand();
        query.CommandText = "SELECT account_id FROM accounts WHERE m_id='player'";
        Assert.Equal((long)first.AccountId.Value, await query.ExecuteScalarAsync());
    }
}
