using Dfo.Server.Application.Authentication;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationConsumptionTests
{
    [Fact]
    public async Task ExactAccountCanConsumeOnlyOnceAndCompetingConsumersHaveExactlyOneWinner()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var login = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        Assert.NotNull(login.Ticket);
        Assert.Null(await fixture.Consume("other", login.Ticket));
        Assert.Null(await fixture.Consume("Player", login.Ticket));
        Assert.Null(await fixture.Consume("player", new string('x', 43)));
        Assert.Null(await fixture.Consume("player", login.Ticket + "?"));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => fixture.Consume("player", login.Ticket))));
        var winner = Assert.Single(results, result => result.HasValue);
        Assert.Equal((await fixture.Store.FindAsync("player", default))!.AccountId, winner);
        Assert.Null(await fixture.Consume("player", login.Ticket));
        Assert.Equal(1L, await fixture.Scalar("SELECT COUNT(*) FROM login_tickets WHERE consumed_at IS NOT NULL"));
    }

    [Fact]
    public async Task ExpiryIsExclusiveAndUncommittedConsumptionCanBeRetried()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var login = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        Assert.NotNull(login.Ticket);
        var service = fixture.Services.GetService(typeof(Dfo.Server.Abstractions.Authentication.IAuthenticationDomainService)) as Dfo.Server.Abstractions.Authentication.IAuthenticationDomainService;
        Assert.NotNull(service);
        await using (fixture.Scope.Begin())
            Assert.NotNull(await service.ConsumeTicketAsync("player", login.Ticket, default));
        Assert.Equal(0L, await fixture.Scalar("SELECT COUNT(*) FROM login_tickets WHERE consumed_at IS NOT NULL"));
        fixture.Clock.Now = login.ExpiresAtUtc!.Value.AddTicks(-1);
        Assert.NotNull(await fixture.Consume("player", login.Ticket));
        var expiring = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        fixture.Clock.Now = expiring.ExpiresAtUtc!.Value;
        Assert.Null(await fixture.Consume("player", expiring.Ticket!));
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
        Assert.Null(await fixture.Consume("player", expiring.Ticket!));
    }
}
