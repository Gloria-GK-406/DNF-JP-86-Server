using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Party;

internal abstract partial class PartyMutationProtocolCommandHandler
{
    protected enum PartyChange { Leave, Remove, Transfer }
    protected readonly ICommandWorkUnits? workUnits;
    protected readonly IOnlineUserQueryDomainService? onlineUsers;
    protected readonly IDungeonRunDomainService? dungeonRuns;
    protected readonly IDungeonRunParticipantDepartureSink? departureSink;

    protected virtual ValueTask ProjectPartyAsync(PartyNotificationPlan plan, PartyNotificationRecipientPlan recipients,
        CancellationToken token) => DeliverPartyNotificationsAsync(plan, recipients, token);

    protected virtual async ValueTask<PartyOperationResult> MutatePartyAsync(PartyChange change,
        SelectedSessionIdentity caller, SelectedSessionIdentity? target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var departing = change == PartyChange.Leave ? caller : change == PartyChange.Remove ? target : null;
        var run = departing is null ? null : dungeonRuns?.Find(departing.CharacterId);
        var result = change switch
        {
            PartyChange.Leave => PartyService!.LeaveParty(caller.CharacterId),
            PartyChange.Remove => PartyService!.RemoveMember(caller.CharacterId, target!.CharacterId),
            PartyChange.Transfer => PartyService!.TransferLeadership(caller.CharacterId, target!.CharacterId),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        if (result.Succeeded && run is not null)
            _ = await dungeonRuns!.EndAsync(departing!.CharacterId, run.RunId).ConfigureAwait(false);
        return result;
    }

    protected SelectedSessionRecipient CaptureParticipant(SelectedSessionIdentity session)
    {
        NetworkConnectionContext? connection = null;
        if (onlineUsers?.TryGetByConnection(session.ConnectionId, out var user) == true && user is not null)
        {
            if (user.AccountId != session.AccountId || user.CurrentCharacterId != session.CharacterId)
                throw new InvalidOperationException("The tower participant connection no longer owns the captured character.");
            connection = user.Connection;
        }
        return new(session, connection);
    }

    protected virtual ValueTask<BossTowerActionResult> LeaveBossPartyTowerAsync(CommandExecutionContext? execution,
        BossTowerActionContext context, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-leave-boss-tower", participants.Select(CaptureParticipant).ToArray(),
                Footprint: CommandFootprintMode.NonRequest),
            (unit, ct) => bossTowers.LeaveAsync(context, false, unit.Footprint, ct), token);

    protected virtual ValueTask<TowerActionResult> LeaveIllusionPartyTowerAsync(CommandExecutionContext? execution,
        TowerActionContext context, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-leave-illusion-tower", [], Footprint: CommandFootprintMode.NonRequest),
            (unit, ct) => illusionTowers.LeaveAsync(context, false, unit.Footprint, ct), token);

    protected virtual ValueTask<BossTowerActionResult> SynchronizeBossTowerPartyTowerAsync(CommandExecutionContext? execution,
        BossTowerActionContext context, SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-synchronize-boss-leader", participants.Select(CaptureParticipant).ToArray()),
            (_, ct) => bossTowers.SynchronizeLeaderAsync(context, leader, ct), token);

    protected virtual ValueTask<TowerActionResult> SynchronizeIllusionTowerPartyTowerAsync(CommandExecutionContext? execution,
        TowerActionContext context, SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-synchronize-illusion-leader", []),
            (_, ct) => illusionTowers.SynchronizeLeaderAsync(context, leader, ct), token);

    protected virtual ValueTask<BossTowerActionResult> StartBossPartyTowerAsync(CommandExecutionContext? execution,
        BossTowerSnapshot run, SelectedSessionIdentity leader, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-start-boss-tower", run.Members.Select(member => CaptureParticipant(member.Session)).ToArray()),
            (_, ct) => bossTowers.StartAsync(BossTowerContext(run, leader), ct), token);
}
