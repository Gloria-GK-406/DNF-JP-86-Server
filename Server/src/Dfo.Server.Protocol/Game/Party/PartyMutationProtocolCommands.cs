using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Party;

internal sealed record GameLeavePartyCommand(NetworkDispatchContext Dispatch, LeavePartyRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed record GameRemovePartyMemberCommand(NetworkDispatchContext Dispatch, RemovePartyMemberRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed record GameChangePartyLeaderCommand(NetworkDispatchContext Dispatch, ChangePartyLeaderRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GamePartyMutationCommandHandler : PartyMutationProtocolCommandHandler,
    ICommandHandler<GameLeavePartyCommand, bool>, ICommandHandler<GameRemovePartyMemberCommand, bool>,
    ICommandHandler<GameChangePartyLeaderCommand, bool>
{
    public GamePartyMutationCommandHandler(ISelectedSessionDomainService sessions, IPartyService parties,
        ITownAreaDomainService townAreas, ICommandProjections projections, IPartyProjectionStateDomainService projectionState,
        ILogger<GamePartyMutationCommandHandler> logger, IOutboundOrdering? ordering = null,
        ISelectedCharacterCurrentViewDomainService? currentViews = null, ICharacterStateProjectionDomainService? currentState = null,
        ICharacterDomainService? characters = null, IOnlineCharacterRuntimeDomainService? onlineCharacters = null, ITownMovementDomainService? towns = null,
        IMarriageHousingQueryDomainService? housing = null, IBossTowerDomainService? bossTowers = null, IIllusionTowerDomainService? illusionTowers = null,
        TimeProvider? clock = null, IGameCommandOutbox? outbox = null, ICommandConnectionClosures? closures = null,
        IOnlineUserQueryDomainService? onlineUsers = null, IWeaknessDomainService? weakness = null, ICommandWorkUnits? workUnits = null,
        IDungeonRunDomainService? runs = null, IDungeonRunProjectionDomainService? departureSink = null)
        : base(sessions, parties, townAreas, projections, projectionState.StreamOf, ordering, logger,
            currentViews, currentState, characters, onlineCharacters, towns, housing, bossTowers, illusionTowers, clock,
            outbox is null ? null : outbox.RegisterExplicitAsync,
            session => onlineUsers?.TryGetByConnection(session.ConnectionId, out var user) == true && user is not null
                && user.AccountId == session.AccountId && user.CurrentCharacterId == session.CharacterId
                    ? closures!.RequestCloseAsync(user.Connection, CancellationToken.None) : ValueTask.CompletedTask,
            (session, token) => sessions.IsInWorld(session)
                ? outbox!.RegisterNotiAsync(session.ConnectionId, new WeaknessProjectionNotification(weakness!.For(session.CharacterId).Value), token)
                : ValueTask.CompletedTask,
            workUnits: workUnits, onlineUsers: onlineUsers, dungeonRuns: runs, departureSink: departureSink)
    {
    }

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameLeavePartyCommand command, CancellationToken token) =>
        HandleMutationAsync(execution, command.Dispatch, command.Input.Metadata, PartyChange.Leave, null, token);
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameRemovePartyMemberCommand command, CancellationToken token) =>
        HandleMutationAsync(execution, command.Dispatch, command.Input.Metadata, PartyChange.Remove, command.Input.TargetSlot, token);
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameChangePartyLeaderCommand command, CancellationToken token) =>
        HandleMutationAsync(execution, command.Dispatch, command.Input.Metadata, PartyChange.Transfer, command.Input.TargetSlot, token);
}

internal abstract partial class PartyMutationProtocolCommandHandler
{
    protected async ValueTask<bool> HandleMutationAsync(CommandExecutionContext execution, NetworkDispatchContext dispatch,
        GameMessageMetadata metadata, PartyChange change, byte? targetSlot, CancellationToken token)
    {
        var request = execution.Request ?? throw new InvalidOperationException("A party protocol request is required.");
        request.DemandCurrent();
        if (TowerProtocol.IsStale(metadata)) return false;
        var caller = sessions.FindByConnection(dispatch.Connection.ConnectionId);
        if (caller is null) return false;

        var boss = bossTowers is null ? new BossTowerPartyAttempt(false, PartyOperationResult.Success()) : await CoordinateBossTowerPartyAsync(execution, change, caller, targetSlot, false, null, token).ConfigureAwait(false);
        if (boss.Handled) return boss.Result.Succeeded;
        var illusion = illusionTowers is null ? new IllusionTowerPartyAttempt(false, PartyOperationResult.Success()) : await CoordinateIllusionTowerPartyAsync(execution, change, caller, targetSlot, false, null, token).ConfigureAwait(false);
        if (illusion.Handled)
        {
            if (change == PartyChange.Remove && !illusion.Result.Succeeded)
                await register(dispatch.Connection.ConnectionId, new GameExplicitMessageDelivery([new RemovePartyMemberRefusal()], []), token).ConfigureAwait(false);
            LogPartyMutation(change, caller, illusion.Target, targetSlot, illusion.Result);
            return illusion.Result.Succeeded;
        }

        SelectedSessionIdentity? target = null;
        if (change != PartyChange.Leave)
        {
            var roster = PartyService!.FindParty(new(caller.AccountId, caller.CharacterId, caller.WireUserId, null));
            var member = roster.IsVirtual ? null : roster.Members.SingleOrDefault(member => member.Slot == targetSlot);
            target = member is not null && sessions.TryGetInWorld(member.CharacterId, out var inWorld) ? inWorld : null;
            if (target is null)
            {
                if (change == PartyChange.Remove)
                    await register(dispatch.Connection.ConnectionId, new GameExplicitMessageDelivery([new RemovePartyMemberRefusal()], []), token).ConfigureAwait(false);
                return false;
            }
        }
        var departing = change == PartyChange.Leave ? caller : change == PartyChange.Remove ? target : null;
        var departingRun = departing is null ? null : dungeonRuns?.Find(departing.CharacterId);
        var runtime = change == PartyChange.Remove && departingRun is not null && currentViews is not null
            ? await currentViews.TryLoadAsync(target!, token).ConfigureAwait(false) : null;
        var position = change == PartyChange.Remove && departingRun is not null ? onlineCharacters?.PositionOf(target!) : null;
        var result = !sessions.IsInWorld(caller) || target is not null && !sessions.IsInWorld(target)
            ? PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent)
            : await MutatePartyAsync(change, caller, target, token).ConfigureAwait(false);
        var recipients = PartyNotificationRecipientCapture.Capture(result.Notifications, sessions, townAreas);
        if (!result.Succeeded)
        {
            if (change == PartyChange.Remove)
                await register(dispatch.Connection.ConnectionId, new GameExplicitMessageDelivery([new RemovePartyMemberRefusal()], []), token).ConfigureAwait(false);
        }
        else
        {
            if (departingRun is not null && dungeonRuns?.Find(departing!.CharacterId) is null)
                departureSink?.ParticipantDeparted(departingRun.RunId, departing!.CharacterId);
            await ProjectPartyAsync(result.Notifications, recipients, token).ConfigureAwait(false);
            if (change == PartyChange.Remove && departingRun is not null && runtime is not null && position is not null && sessions.IsInWorld(target!))
                await ReturnTownAsync([new TownAreaEntry(target!, runtime, position)], token).ConfigureAwait(false);
        }
        LogPartyMutation(change, caller, target, targetSlot, result);
        return result.Succeeded;
    }

    private void LogPartyMutation(PartyChange change, SelectedSessionIdentity caller, SelectedSessionIdentity? target,
        byte? slot, PartyOperationResult result) => logger.LogInformation(
            "Party operation={Operation} caller={Caller} target={Target} slot={Slot} accepted={Accepted} failure={Failure}",
            change, caller.CharacterId, target?.CharacterId, slot, result.Succeeded, result.Failure);
}
