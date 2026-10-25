using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

public sealed record IllusionTowerPartyAttempt(
    bool Handled,
    PartyOperationResult Result,
    SelectedSessionIdentity? Target = null);

public sealed class IllusionTowerPartyCoordinator(
    IIllusionTowerDomainService towers, IPartyService parties, ISelectedSessionDomainService sessions,
    ICommandBus commands, PartyNotificationDelivery notifications, IllusionTowerProjectionDelivery towerProjection,
    INetworkConnectionCloser connections, ITownAreaDomainService townAreas, ILogger<IllusionTowerPartyCoordinator> logger)
{
    private readonly Legacy legacy = new(towers, parties, sessions, commands, notifications, towerProjection, connections, townAreas, logger);
    public ValueTask<IllusionTowerPartyAttempt> TryLeaveAsync(SelectedSessionIdentity caller, bool allowVirtual,
        IGameServerMessage? departureResponse, CancellationToken token) => legacy.LeaveAsync(caller, allowVirtual, departureResponse, token);
    public ValueTask<IllusionTowerPartyAttempt> TryRemoveAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => legacy.RemoveAsync(caller, targetSlot, token);
    public ValueTask<IllusionTowerPartyAttempt> TryTransferAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => legacy.TransferAsync(caller, targetSlot, token);

    private sealed class Legacy(IIllusionTowerDomainService towers, IPartyService parties, ISelectedSessionDomainService sessions,
        ICommandBus commands, PartyNotificationDelivery notifications, IllusionTowerProjectionDelivery projection,
        INetworkConnectionCloser connections, ITownAreaDomainService townAreas, ILogger logger)
        : PartyMutationProtocolCommandHandler(sessions, parties, townAreas, null!, null!, null, logger,
            illusionTowers: towers,
            close: session => connections.RequestCloseAsync(session.ConnectionId, CancellationToken.None))
    {
        internal ValueTask<IllusionTowerPartyAttempt> LeaveAsync(SelectedSessionIdentity caller, bool allowVirtual, IGameServerMessage? response, CancellationToken token) =>
            CoordinateIllusionTowerPartyAsync(null, PartyChange.Leave, caller, null, allowVirtual, response, token);
        internal ValueTask<IllusionTowerPartyAttempt> RemoveAsync(SelectedSessionIdentity caller, byte slot, CancellationToken token) =>
            CoordinateIllusionTowerPartyAsync(null, PartyChange.Remove, caller, slot, false, null, token);
        internal ValueTask<IllusionTowerPartyAttempt> TransferAsync(SelectedSessionIdentity caller, byte slot, CancellationToken token) =>
            CoordinateIllusionTowerPartyAsync(null, PartyChange.Transfer, caller, slot, false, null, token);
        protected override ValueTask ProjectPartyAsync(PartyNotificationPlan plan, PartyNotificationRecipientPlan recipients, CancellationToken token) =>
            notifications.DeliverAsync(plan, recipients, token);
        protected override ValueTask ProjectIllusionTowerPartyDepartureAsync(CommandExecutionContext? execution, TowerActionResult action,
            SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token) =>
            projection.ProjectCommittedDepartureAsync(action, returning, response, token);
        protected override ValueTask ProjectIllusionTowerPartyHostAsync(IllusionTowerSnapshot run, CancellationToken token) => projection.ProjectCurrentHostAsync(run, token);
        protected override ValueTask<TowerActionResult> LeaveIllusionPartyTowerAsync(CommandExecutionContext? execution, TowerActionContext context,
            CancellationToken token) => commands.SendAsync(new LeaveIllusionTowerCommand(context, false), token);
        protected override ValueTask<TowerActionResult> SynchronizeIllusionTowerPartyTowerAsync(CommandExecutionContext? execution, TowerActionContext context,
            SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
            commands.SendAsync(new SynchronizeIllusionTowerLeaderCommand(context, leader), token);
        protected override ValueTask<PartyOperationResult> MutatePartyAsync(PartyChange change, SelectedSessionIdentity caller, SelectedSessionIdentity? target, CancellationToken token) =>
            change switch
            {
                PartyChange.Leave => commands.SendAsync(new LeavePartyCommand(caller.CharacterId), token),
                PartyChange.Remove => commands.SendAsync(new RemovePartyMemberCommand(caller.CharacterId, target!.CharacterId), token),
                PartyChange.Transfer => commands.SendAsync(new TransferPartyLeadershipCommand(caller.CharacterId, target!.CharacterId), token),
                _ => throw new ArgumentOutOfRangeException(nameof(change)),
            };
    }
}
