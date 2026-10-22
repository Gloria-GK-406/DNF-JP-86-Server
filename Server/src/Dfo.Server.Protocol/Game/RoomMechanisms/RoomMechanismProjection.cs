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

/// <summary>One worker sleeps until a room deadline or lifecycle wakeup; no per-room threads.</summary>
internal sealed class RoomMechanismMaintenanceService(IDungeonRunDomainService runs,
    IDungeonRoomMechanismRuleService rules, IDungeonRoomMechanismWakeSignal wake, IEventService events,
    TimeProvider clock, ILogger<RoomMechanismMaintenanceService> logger,
    IGameplayQueue queue) : BackgroundService
{
    private readonly CoalescingGameplaySource ticks = new(queue);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read and written only by the timer CMDs below.
        var delivered = new Dictionary<(DungeonRunId, long), DungeonRoomMechanismState[]>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // One round (advance and deliver) is one timer CMD; only the wait is outside.
                DateTimeOffset? next = null;
                await GameplayTimerWork.TickAsync(ticks, "timer:RoomMechanismMaintenanceService", async token =>
                {
                    var views = runs.AdvanceRoomMechanisms(clock.GetUtcNow());
                    var active = views.Select(view => (view.RunId, view.RoomVersion)).ToHashSet();
                    foreach (var key in delivered.Keys.Where(key => !active.Contains(key)).ToArray()) delivered.Remove(key);
                    foreach (var view in views)
                    {
                        foreach (var state in view.States)
                            if (rules.NextDeadline(state) is { } deadline && (next is null || deadline < next)) next = deadline;
                        var key = (view.RunId, view.RoomVersion);
                        if (delivered.TryGetValue(key, out var previous) && previous.SequenceEqual(view.States)) continue;
                        try
                        {
                            await events.DirectAsync(new DungeonRoomMechanismsChanged(view), token).ConfigureAwait(false);
                            delivered[key] = view.States.ToArray();
                            logger.LogInformation("Room mechanism state run={RunId} roomVersion={RoomVersion} states={States}",
                                view.RunId, view.RoomVersion, string.Join(',', view.States.Select(state => $"{state.Definition.Kind}:{state.Stage}:{state.Outcome}")));
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception error)
                        {
                            logger.LogWarning(error, "Room mechanism projection failed run={RunId}", view.RunId);
                            var retry = clock.GetUtcNow().AddSeconds(1);
                            if (next is null || retry < next) next = retry;
                        }
                    }
                }, stoppingToken).ConfigureAwait(false);
                await wake.WaitAsync(next, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
