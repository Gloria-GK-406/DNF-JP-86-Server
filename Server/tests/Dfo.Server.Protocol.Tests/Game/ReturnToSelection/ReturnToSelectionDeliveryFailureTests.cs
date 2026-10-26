using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging.Abstractions;
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
        var handler = new ReturnWorkflow(new OneUser(context), commands, outbound);
        await handler.HandleAsync(DomainCommandFixture.Context(Guid.NewGuid(), typeof(GameReturnToSelectionCommand), typeof(ReturnWorkflow)),
            new GameReturnToSelectionCommand(new NetworkDispatchContext(context), new ReturnToSelectionRequest(GameMessageMetadata.Empty)), CancellationToken.None);
    }

    // Exercise the real preparation and completion against a selected session. Count the
    // existing cleanup capability, rather than overriding the command's completion stage.
    private sealed class ReturnWorkflow
    {
        private readonly GameReturnToSelectionCommandHandler command;
        public ReturnWorkflow(IOnlineUserQueryDomainService users, ReturnCommands stages, XoDelivery outbound)
        {
            var sessions = TestSessions.Directory(out var state);
            sessions.InWorld(state, new AccountId(8501), new CharacterId(1), Connection.Value);
            command = ProtocolAlgorithmFixture.Create<GameReturnToSelectionCommandHandler>(
                users, sessions, new Roster(), new Cleanup(stages), new TestOutbox(outbound),
                new TestClosures(outbound));
        }
        public ValueTask<bool> HandleAsync(CommandExecutionContext execution,
            GameReturnToSelectionCommand input, CancellationToken ct) => command.HandleAsync(
                execution with { Request = new CommandRequestContext.ProtocolNonEffect(input.Dispatch, input.Input,
                    new ProtocolFrameMetadata.Game(input.Input.Metadata), () => true) }, input, ct);
    }
    private sealed class Cleanup(ReturnCommands stages) : ICommandSelectedSessionCleanup
    {
        public void RecordDisconnectedDeliveryFailure(CommandExecutionContext context, Exception cause) => throw new NotSupportedException();
        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(CommandExecutionContext context,
            SelectedSessionIdentity session, bool includeOwner, CommandWorkUnit? action = null,
            CancellationToken cancellationToken = default)
        {
            stages.Completed++;
            return ValueTask.FromResult(SelectedSessionCleanupNotificationPlan.Empty(session));
        }
    }
    private sealed class Roster : ICharacterDomainService
    {
        public ValueTask<AccountCharacterRoster> GetAccountRosterWithAppearanceAsync(AccountId account, CancellationToken ct) =>
            ValueTask.FromResult(new AccountCharacterRoster(account, 8, []));
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(AccountId account, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ActiveCharacterState> LoadForSelectionAsync(AccountId account, ushort slot, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(AccountId account, ushort index, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<CharacterNameStatus> CheckNameAsync(byte[] name, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<CharacterCreationResult> CreateCharacterAsync(AccountId account, byte job, byte[] name, CancellationToken ct) => throw new NotSupportedException();
        public ICharacter Get(CharacterId id) => throw new NotSupportedException();
        public ValueTask<CharacterDeletionResult> DeleteCharacterAsync(AccountId account, ushort slot, byte[] name, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TestClosures(XoDelivery outbound) : ICommandConnectionClosures
    { public ValueTask RequestCloseAsync(NetworkConnectionContext c,CancellationToken ct) => outbound.Closer.RequestCloseExactAsync(c,ct); }
    private sealed class TestOutbox(XoDelivery outbound) : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage m,CancellationToken ct) => outbound.Game.SendCmdAsync(id,m,ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage m,CancellationToken ct) => outbound.Game.SendNotiAsync(id,m,ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct) => outbound.Game.SendExplicitAsync(id,d,ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class ReturnCommands { public int Completed; }

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
