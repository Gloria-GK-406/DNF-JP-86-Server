using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class LoginSessionQueuedCommandTests
{
    [Fact]
    public async Task ReplacedLoginCannotWriteAfterWaitingForTheAccountGateEvenAfterDisconnectCallback()
    {
        using var fixture = new AuthenticationFixture(configure: services => services
            .AddCommandHandler<QueuedWrite, bool, WriteHandler>(CommandExecutionKind.Transactional,
                accountAccessMode: UserAccessMode.Exclusive));
        await fixture.RegisterAsync("player", "test password");
        var original = (await fixture.Store.FindAsync("player", default))!;
        var issued = await fixture.LoginAsync("player", "test password");
        var old = new NetworkConnectionId("old-writer");
        Assert.NotNull(await fixture.Consume(issued.MId!, issued.Ticket!, old));
        var registry = fixture.Services.GetRequiredService<LoginSessionRegistry>();
        var authorization = fixture.Services.GetRequiredService<NetworkCommandAuthorization>();
        // The old request's authority is captured before the replacement, as a request already queued
        // would have; the write it carries runs only after the replacement login and the disconnect.
        var authority = registry.CaptureAuthority(old)!;
        await fixture.LoginAsync("player", "test password");
        registry.Disconnected(old); // The transient superseded marker is now gone.
        var queued = TestGameplay.RunAsync(fixture.Services, async () =>
        {
            using var request = authorization.Enter(authority);
            return await fixture.Bus.SendAsync(new QueuedWrite(original.AccountId, original.PasswordHash));
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => queued);
        Assert.Equal(original.PasswordHash, (await fixture.Store.FindAsync("player", default))!.PasswordHash);
    }

    public sealed record QueuedWrite(AccountId AccountId, string PreviousHash) : ICommand<bool>, IAccountCommand;

    public sealed class WriteHandler(IAuthenticationPersistence store) : ICommandHandler<QueuedWrite, bool>
    {
        public ValueTask<bool> HandleAsync(CommandExecutionContext context, QueuedWrite command, CancellationToken cancellationToken) =>
            store.TryReplaceCredentialAsync(command.AccountId, command.PreviousHash, "unauthorized-write", cancellationToken);
    }
}
