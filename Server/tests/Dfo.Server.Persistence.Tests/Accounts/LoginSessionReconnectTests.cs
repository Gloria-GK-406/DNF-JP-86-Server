using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class LoginSessionReconnectTests
{
    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(301, false)]
    public async Task DisconnectGraceExpiresAtFiveMinutes(int elapsedSeconds, bool allowed)
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var login = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        var connection = new NetworkConnectionId("first");
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket!, connection));
        var sessions = fixture.Services.GetRequiredService<LoginSessionRegistry>();
        sessions.Disconnected(connection);
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(elapsedSeconds);
        Assert.Equal(allowed, (await fixture.Consume(login.MId!, login.Ticket!)).HasValue);
    }

    [Fact]
    public async Task OldDisconnectCannotRevokeOrExpireReplacementConnection()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var login = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        var sessions = fixture.Services.GetRequiredService<LoginSessionRegistry>();
        var old = new NetworkConnectionId("old");
        var current = new NetworkConnectionId("current");
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket!, old));
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket!, current));
        Assert.True(sessions.IsSuperseded(old));
        Assert.True(sessions.CloseRequests.TryRead(out var requested));
        Assert.Equal(old, requested);
        sessions.Disconnected(old);
        fixture.Clock.Now = fixture.Clock.Now.AddHours(3);
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket!));
    }

    [Fact]
    public async Task DuplicateDisconnectDoesNotExtendGrace()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var login = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        var sessions = fixture.Services.GetRequiredService<LoginSessionRegistry>();
        var connection = new NetworkConnectionId("first");
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket!, connection));
        sessions.Disconnected(connection);
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(299);
        sessions.Disconnected(connection);
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
        Assert.Null(await fixture.Consume(login.MId!, login.Ticket!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewPasswordLoginReplacesActiveOrDisconnectedCredential(bool disconnect)
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var first = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        var sessions = fixture.Services.GetRequiredService<LoginSessionRegistry>();
        var connection = new NetworkConnectionId("first");
        Assert.NotNull(await fixture.Consume(first.MId!, first.Ticket!, connection));
        if (disconnect) sessions.Disconnected(connection);
        var second = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        Assert.Null(await fixture.Consume(first.MId!, first.Ticket!));
        Assert.Equal(!disconnect, sessions.IsSuperseded(connection));
        Assert.NotNull(await fixture.Consume(second.MId!, second.Ticket!));
        sessions.Disconnected(connection);
        fixture.Clock.Now = fixture.Clock.Now.AddHours(1);
        Assert.NotNull(await fixture.Consume(second.MId!, second.Ticket!));
    }

    [Fact]
    public async Task ActivatedCredentialCanAuthenticateAnotherGameConnectionAfterInitialDeadline()
    {
        using var fixture = new AuthenticationFixture();
        await fixture.Bus.SendAsync(new RegisterAccountCommand("player", "test password"));
        var login = await fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand("player", "test password"));
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket!));
        fixture.Clock.Now = fixture.Clock.Now.AddHours(2);
        Assert.NotNull(await fixture.Consume(login.MId!, login.Ticket!));
    }
}
