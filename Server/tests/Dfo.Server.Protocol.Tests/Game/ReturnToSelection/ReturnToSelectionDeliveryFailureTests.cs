using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ReturnToSelection;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Outbox;

namespace Dfo.Server.Protocol.Tests.Game.ReturnToSelection;

/// <summary>DR-11 (TASK-24): the return-to-selection reply through the real registration adapters and outbox.</summary>
public sealed class ReturnToSelectionDeliveryFailureTests
{
    private static readonly NetworkConnectionId Connection = new("return-1");

    /// <summary>T13: the reply fails in transport; the sender closes the connection and the teardown ran once.</summary>
    [Fact]
    public async Task A_reply_that_fails_in_transport_is_closed_by_the_sender_and_the_teardown_is_not_repeated()
    {
        await using var outbound = new XoDelivery();
        outbound.Connect(Connection);
        outbound.Transport.Fail(Connection);
        var commands = new ReturnCommands();

        await outbound.RunFromAsync(Connection, () => HandleAsync(outbound, commands));
        await outbound.SettledAsync();

        Assert.Contains(outbound.Messages(Connection), message => message is ReturnToSelectionResponse);
        Assert.True(await outbound.ClosedAsync(Connection));
        Assert.DoesNotContain(outbound.Recorder.Entries, entry => entry.IsClose);
        Assert.Equal(1, commands.Completed);
    }

    /// <summary>R10, the kept branch: the reply cannot be registered; the handler closes the source and rethrows.</summary>
    [Fact]
    public async Task A_reply_that_cannot_be_registered_closes_the_connection_and_the_failure_is_thrown()
    {
        await using var outbound = new XoDelivery();
        outbound.Connect(Connection);
        outbound.Encoder.Fail<ReturnToSelectionResponse>(Connection);
        var commands = new ReturnCommands();

        await Assert.ThrowsAsync<InvalidDataException>(() => outbound.RunAsync(() => HandleAsync(outbound, commands)));
        await outbound.SettledAsync();

        Assert.DoesNotContain(outbound.Messages(Connection), message => message is ReturnToSelectionResponse);
        Assert.True(outbound.CloseRegistered(Connection));
        Assert.True(await outbound.ClosedAsync(Connection));
        Assert.Equal(1, commands.Completed);
    }

    private static async Task HandleAsync(XoDelivery outbound, ReturnCommands commands)
    {
        var context = outbound.Context(Connection);
        var handler = new ReturnToSelectionHandler(new OneUser(context), commands,
            new CharacterSelectionDelivery(outbound.Game, new NoCleanup()), outbound.Closer);
        await handler.HandleAsync(new NetworkDispatchContext(context), new ReturnToSelectionRequest(GameMessageMetadata.Empty), CancellationToken.None);
    }

    private sealed class ReturnCommands : ICommandBus
    {
        public int Completed { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            object result = command switch
            {
                PrepareReturnToSelectionCommand prepare => new PreparedReturnToSelection(
                    prepare.AccountId, prepare.ConnectionId, prepare.Generation, null,
                    new AccountCharacterRoster(prepare.AccountId, 8, []), false),
                CompleteReturnToSelectionCommand => Complete(),
                _ => throw new NotSupportedException(command.GetType().Name),
            };
            return ValueTask.FromResult((TResult)result);
        }

        private CompletedReturnToSelection Complete()
        {
            Completed++;
            return new CompletedReturnToSelection(null);
        }
    }

    private sealed class OneUser(NetworkConnectionContext connection) : IOnlineUserQueryDomainService
    {
        private readonly OnlineUserSnapshot user = new(OnlineUserId.New(), connection, new AccountId(8501), null,
            OnlineUserState.Authenticated, 1);

        public OnlineUserSnapshot GetRequired(OnlineUserId id) => user;

        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? found)
        {
            found = id == connection.ConnectionId ? user : null;
            return found is not null;
        }
    }

    private sealed class NoCleanup : ICleanupNotificationDelivery
    {
        public ValueTask DeliverAsync(SelectedSessionCleanupNotificationPlan plan, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
