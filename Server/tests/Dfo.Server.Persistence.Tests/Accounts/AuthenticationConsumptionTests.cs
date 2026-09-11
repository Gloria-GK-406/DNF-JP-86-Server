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
        Assert.NotNull(login.MId);
        Assert.Null(await fixture.Consume("000000", login.Ticket));
        Assert.Null(await fixture.Consume(login.MId, new string('A', 32)));
        Assert.Null(await fixture.Consume(login.MId, new string('x', 32)));
        Assert.Null(await fixture.Consume(login.MId, login.Ticket + "?"));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => fixture.Consume(login.MId, login.Ticket))));
        var winner = Assert.Single(results, result => result.HasValue);
        Assert.Equal((await fixture.Store.FindAsync("player", default))!.AccountId, winner);
        Assert.Null(await fixture.Consume(login.MId, login.Ticket));
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
            Assert.NotNull(await service.ConsumeTicketAsync(login.MId!, login.Ticket, default));
        fixture.Clock.Now = login.ExpiresAtUtc!.Value.AddTicks(-1);
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket));
        var expiring = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        fixture.Clock.Now = expiring.ExpiresAtUtc!.Value;
        Assert.Null(await fixture.Consume(expiring.MId!, expiring.Ticket!));
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
        Assert.Null(await fixture.Consume(expiring.MId!, expiring.Ticket!));
    }
}
