using Dfo.Server.Application.Authentication;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationConsumptionTests
{
    [Fact]
    public async Task ExactCredentialBindsAgainButOverlappingUncommittedBindsAreRejected()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.RegisterAsync("player", "test password");
        var login = await fixture.LoginAsync("player", "test password");
        Assert.Null(await fixture.Consume("000000", login.Ticket!));
        Assert.Null(await fixture.Consume(login.MId!, new string('A', 32)));
        Assert.Null(await fixture.Consume(login.MId!, new string('x', 32)));
        Assert.Null(await fixture.Consume(login.MId!, login.Ticket + "?"));
        var service = (Dfo.Server.Abstractions.Authentication.IAuthenticationDomainService)fixture.Services.GetService(typeof(Dfo.Server.Abstractions.Authentication.IAuthenticationDomainService))!;
        await using (var first = fixture.Scope.Begin())
        {
            Assert.NotNull(await service.BindSessionAsync(new Dfo.Server.Data.Networking.NetworkConnectionId("first"), login.MId!, login.Ticket!, default));
            // Suppress ambient transaction flow: the competing connection owns another transaction.
            Task<Dfo.Server.Data.Accounts.AccountId?> competing;
            using (System.Threading.ExecutionContext.SuppressFlow())
                competing = Task.Run(() => fixture.Consume(login.MId!, login.Ticket!));
            Assert.Null(await competing);
            await first.CommitAsync(default);
        }
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket!));
    }

    [Fact]
    public async Task ExpiryIsExclusiveAndUncommittedConsumptionCanBeRetried()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.RegisterAsync("player", "test password");
        var login = await fixture.LoginAsync("player", "test password");
        Assert.NotNull(login.Ticket);
        var service = fixture.Services.GetService(typeof(Dfo.Server.Abstractions.Authentication.IAuthenticationDomainService)) as Dfo.Server.Abstractions.Authentication.IAuthenticationDomainService;
        Assert.NotNull(service);
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (fixture.Scope.Begin())
                Assert.NotNull(await service.BindSessionAsync(new Dfo.Server.Data.Networking.NetworkConnectionId("test-binding"), login.MId!, login.Ticket, default));
        });
        fixture.Clock.Now = login.ExpiresAtUtc!.Value.AddTicks(-1);
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket));
        var expiring = await fixture.LoginAsync("player", "test password");
        fixture.Clock.Now = expiring.ExpiresAtUtc!.Value;
        Assert.Null(await fixture.Consume(expiring.MId!, expiring.Ticket!));
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
        Assert.Null(await fixture.Consume(expiring.MId!, expiring.Ticket!));
    }
}
