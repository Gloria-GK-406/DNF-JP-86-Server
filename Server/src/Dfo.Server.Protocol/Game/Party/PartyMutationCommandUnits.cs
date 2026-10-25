using Dfo.Server.Abstractions.Networking;
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
    protected readonly IDungeonRunProjectionDomainService? departureSink;

    protected virtual ValueTask ProjectPartyAsync(PartyNotificationPlan plan, PartyNotificationRecipientPlan recipients,
        CancellationToken token) => DeliverPartyNotificationsAsync(plan, recipients, token);

    protected virtual ValueTask<PartyOperationResult> MutatePartyAsync(PartyChange change,
        SelectedSessionIdentity caller, SelectedSessionIdentity? target, CancellationToken token) =>
        MutatePartyStateAsync(PartyService!, dungeonRuns, change, caller.CharacterId, target?.CharacterId, token);

    protected static async ValueTask<PartyOperationResult> MutatePartyStateAsync(Dfo.Server.Abstractions.Parties.IPartyService parties,
        IDungeonRunDomainService? runs, PartyChange change, Dfo.Server.Data.Characters.CharacterId caller,
        Dfo.Server.Data.Characters.CharacterId? target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var departing = change == PartyChange.Leave ? caller : change == PartyChange.Remove ? target : null;
        var run = departing is null ? null : runs?.Find(departing.Value);
        var result = change switch
        {
            PartyChange.Leave => parties.LeaveParty(caller),
            PartyChange.Remove => parties.RemoveMember(caller, target!.Value),
            PartyChange.Transfer => parties.TransferLeadership(caller, target!.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        if (result.Succeeded && run is not null)
            _ = await runs!.EndAsync(departing!.Value, run.RunId).ConfigureAwait(false);
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

    protected SelectedSessionRecipient[] ProtectedParticipants(IReadOnlyList<SelectedSessionIdentity> participants,
        SelectedSessionIdentity reporter) => participants.Select(session => session == reporter
            ? CaptureParticipant(session) : new SelectedSessionRecipient(session, null)).ToArray();

    protected virtual ValueTask<BossTowerActionResult> LeaveBossPartyTowerAsync(CommandExecutionContext? execution,
        BossTowerActionContext context, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-leave-boss-tower", ProtectedParticipants(participants, context.Session),
                Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: participants),
            (unit, ct) => bossTowers.LeaveAsync(context, false, unit.Footprint, ct), token);

    protected virtual ValueTask<TowerActionResult> LeaveIllusionPartyTowerAsync(CommandExecutionContext? execution,
        TowerActionContext context, IReadOnlyList<SelectedSessionIdentity> owners, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-leave-illusion-tower", [], Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: owners),
            (unit, ct) => illusionTowers.LeaveAsync(context, false, unit.Footprint, ct), token);

    protected virtual ValueTask<BossTowerActionResult> SynchronizeBossTowerPartyTowerAsync(CommandExecutionContext? execution,
        BossTowerActionContext context, SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-synchronize-boss-leader", ProtectedParticipants(participants, context.Session)),
            (_, ct) => bossTowers.SynchronizeLeaderAsync(context, leader, ct), token);

    protected virtual ValueTask<TowerActionResult> SynchronizeIllusionTowerPartyTowerAsync(CommandExecutionContext? execution,
        TowerActionContext context, SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-synchronize-illusion-leader", []),
            (_, ct) => illusionTowers.SynchronizeLeaderAsync(context, leader, ct), token);

    protected virtual ValueTask<BossTowerActionResult> StartBossPartyTowerAsync(CommandExecutionContext? execution,
        BossTowerSnapshot run, SelectedSessionIdentity leader, CancellationToken token) =>
        workUnits!.WriteAsync(execution!, new("party-start-boss-tower", ProtectedParticipants(run.Members.Select(member => member.Session).ToArray(), leader)),
            (_, ct) => bossTowers.StartAsync(BossTowerContext(run, leader), ct), token);
}
