using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.RoomMechanisms;

internal sealed record ElevatorStateMessage(byte Stage, DungeonRoomMechanismOutcome Outcome) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameElevatorState;
}

internal sealed class ElevatorStateEncoder : GameNotiMessageEncoder<ElevatorStateMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ElevatorStateMessage message) =>
        OnePayload(0x00FE, new byte[] { message.Stage, (byte)message.Outcome });
}

internal sealed class RoomMechanismNotificationHandler(IDungeonRunDomainService runs,
    ISelectedSessionDomainService sessions) : INotificationHandler<DungeonRoomMechanismsNotification>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        DungeonRoomMechanismsNotification notification, CancellationToken cancellationToken)
    {
        var view = notification.View;
        var current = runs.Find(recipient.CharacterId);
        if (!sessions.TryGetInWorld(recipient.CharacterId, out var session) || session.ConnectionId != recipient.ConnectionId
            || recipient.ExpectedSession is { } expected && session != expected
            || !view.Recipients.Contains(recipient.CharacterId)
            || current is null || current.RunId != view.RunId || current.RoomVersion != view.RoomVersion
            || current.CurrentCell != view.Cell || !current.ReadyParticipants.Contains(recipient.CharacterId)
            || !current.CurrentCellState.Mechanisms.SequenceEqual(view.States))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(view.States.Select(state =>
            state.Definition.Kind switch
            {
                DungeonRoomMechanismKind.Elevator => (IServerNetworkMessage)new ElevatorStateMessage(state.Stage, state.Outcome),
                _ => throw new InvalidDataException("Unsupported room mechanism projection."),
            }).ToArray());
    }
}

/// <summary>Loading snapshot recovery, through the same event projection as timer transitions.</summary>
public sealed class RoomMechanismProjectionDelivery(IEventService events)
{
    public async ValueTask SendCurrentAsync(SelectedSessionIdentity session, DungeonRunState current,
        CancellationToken cancellationToken)
    {
        var states = current.CurrentCellState.Mechanisms;
        if (states.Count == 0 || states.Any(state => state.StartedAt is null)) return;
        await events.DirectAsync(new DungeonRoomMechanismsChanged(new(current.RunId, current.CurrentCell,
            current.RoomVersion, states, [session.CharacterId])), cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record MaintainRoomMechanismsCommand : ICommand<RoomMechanismNextDeadline>;
internal sealed record RoomMechanismNextDeadline(DateTimeOffset? At);
internal sealed class MaintainRoomMechanismsCommandHandler(IRoomMechanismMaintenanceDomainService maintenance, TimeProvider clock)
    : ICommandHandler<MaintainRoomMechanismsCommand, RoomMechanismNextDeadline>
{
    public async ValueTask<RoomMechanismNextDeadline> HandleAsync(CommandExecutionContext context, MaintainRoomMechanismsCommand command, CancellationToken token) =>
        new(await maintenance.AdvanceAndProjectAsync(clock.GetUtcNow(), token).ConfigureAwait(false));
}
internal sealed class RoomMechanismMaintenanceService(ICommandBus commands, IDungeonRoomMechanismWakeSignal wake,
    ILogger<RoomMechanismMaintenanceService> logger) : TypedGameplayTimerSource(commands, logger)
{
    private DateTimeOffset? next;
    protected override string SourceName => "timer:RoomMechanismMaintenanceService";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token) =>
        next = (await SubmitAsync(new MaintainRoomMechanismsCommand(), root, SourceName, token).ConfigureAwait(false)).At;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                next = null;
                await HostedAsync(stoppingToken).ConfigureAwait(false);
                await wake.WaitAsync(next, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
