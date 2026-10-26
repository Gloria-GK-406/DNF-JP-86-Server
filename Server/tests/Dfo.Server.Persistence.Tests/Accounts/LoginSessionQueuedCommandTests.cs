using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
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
            .AddFixtureCommand<QueuedWrite, bool, WriteHandler>(FixtureUnitKind.Transactional));
        await fixture.RegisterAsync("player", "test password");
        var original = (await fixture.Store.FindAsync("player", default))!;
        var issued = await fixture.LoginAsync("player", "test password");
        var old = new NetworkConnectionId("old-writer");
        Assert.NotNull(await fixture.Consume(issued.MId!, issued.Ticket!, old));
        var registry = fixture.Services.GetRequiredService<LoginSessionRegistry>();
        // The old request's authority is captured before the replacement, as a request already queued
        // would have; the write it carries runs only after the replacement login and the disconnect.
        var authority = registry.CaptureAuthority(old)!;
        await fixture.LoginAsync("player", "test password");
        TestGameplay.Run(fixture.Services, () => registry.Disconnected(old)); // The transient superseded marker is now gone.
        var queued = TestGameplay.RunAsync(fixture.Services, async () =>
        {
            var connection=new NetworkConnectionContext(old,NetworkEndpointKind.Game,10011);
            var input=new QueuedMessage();
            var request=new CommandRequestContext.ProtocolNonEffect(new(connection),input,new ProtocolFrameMetadata.Game(GameMessageMetadata.Empty),authority);
            var context=new CommandExecutionContext(Guid.NewGuid(),typeof(QueuedWrite),typeof(WriteHandler)){Request=request};
            return await fixture.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(context,new("queued-write",[]),(_,ct)=>
                new WriteHandler(fixture.Services.GetRequiredService<IAuthenticationPersistence>()).HandleAsync(context,new QueuedWrite(original.AccountId,original.PasswordHash),ct));

        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => queued);
        Assert.Equal(original.PasswordHash, (await fixture.Store.FindAsync("player", default))!.PasswordHash);
    }

    private sealed record QueuedMessage : IClientNetworkMessage {public ClientMessageType MessageType=>ClientMessageType.GameLogin;}
    public sealed record QueuedWrite(AccountId AccountId, string PreviousHash) : ICommand<bool>;

    public sealed class WriteHandler(IAuthenticationPersistence store) : ICommandHandler<QueuedWrite, bool>
    {
        public ValueTask<bool> HandleAsync(CommandExecutionContext context, QueuedWrite command, CancellationToken cancellationToken) =>
            store.TryReplaceCredentialAsync(command.AccountId, command.PreviousHash, "unauthorized-write", cancellationToken);
    }
}
