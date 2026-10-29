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

internal sealed class RoomMechanismNotificationHandler(IDungeonRunQueryDomainService runs,
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



internal sealed record MaintainRoomMechanismsCommand : ICommand<RoomMechanismNextDeadline>;
internal sealed record ProjectRoomMechanismCommand(DungeonRoomMechanismView View) : ICommand<bool>;
internal sealed record RoomMechanismNextDeadline(DateTimeOffset? At, IReadOnlyList<DungeonRoomMechanismView> Views);
internal sealed class MaintainRoomMechanismsCommandHandler(IRoomMechanismMaintenanceDomainService maintenance, TimeProvider clock)
    : ICommandHandler<MaintainRoomMechanismsCommand, RoomMechanismNextDeadline>, ICommandHandler<ProjectRoomMechanismCommand, bool>
{
    public ValueTask<RoomMechanismNextDeadline> HandleAsync(CommandExecutionContext context, MaintainRoomMechanismsCommand command, CancellationToken token)
    {
        var scan = maintenance.Advance(clock.GetUtcNow());
        return ValueTask.FromResult(new RoomMechanismNextDeadline(scan.Next, scan.Views.Where(maintenance.NeedsProjection).ToArray()));
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ProjectRoomMechanismCommand command, CancellationToken token)
    {
        await maintenance.ProjectAsync(command.View, token).ConfigureAwait(false);
        return true;
    }
}
internal sealed class RoomMechanismMaintenanceService(ICommandBus commands, IDungeonRoomMechanismWakeSignal wake,
    ILogger<RoomMechanismMaintenanceService> logger, TimeProvider clock) : TypedGameplayTimerSource(commands, logger)
{
    private DateTimeOffset? next;
    protected override string SourceName => "timer:RoomMechanismMaintenanceService";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token)
    {
        var scan = await SubmitAsync(new MaintainRoomMechanismsCommand(), root, SourceName, token).ConfigureAwait(false);
        foreach (var view in scan.Views)
            await ItemAsync(new ProjectRoomMechanismCommand(view), root, SourceName + "/room", token).ConfigureAwait(false);
        var remaining = await SubmitAsync(new MaintainRoomMechanismsCommand(), root, SourceName, token).ConfigureAwait(false);
        next = remaining.At;
        if (remaining.Views.Count > 0)
        {
            var retry = clock.GetUtcNow().AddSeconds(1);
            if (next is null || retry < next) next = retry;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                next = clock.GetUtcNow().AddSeconds(1);
                await HostedAsync(stoppingToken).ConfigureAwait(false);
                await wake.WaitAsync(next, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
