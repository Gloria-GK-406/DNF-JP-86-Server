using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
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

public sealed record BossTowerPartyAttempt(
    bool Handled,
    PartyOperationResult Result,
    SelectedSessionIdentity? Target = null);

public sealed class BossTowerPartyCoordinator(
    IBossTowerDomainService towers, IPartyService parties, ISelectedSessionDomainService sessions,
    ICommandBus commands, PartyNotificationDelivery notifications, BossTowerProjectionDelivery towerProjection,
    INetworkConnectionCloser connections, ITownAreaDomainService townAreas, ILogger<BossTowerPartyCoordinator> logger,
    IDungeonRunDomainService? runs = null)
{
    private readonly Legacy legacy = new(towers, parties, sessions, commands, notifications, towerProjection, connections, townAreas, logger, runs);
    public ValueTask<BossTowerPartyAttempt> TryLeaveAsync(SelectedSessionIdentity caller, bool allowVirtual,
        IGameServerMessage? departureResponse, CancellationToken token) => legacy.LeaveAsync(caller, allowVirtual, departureResponse, token);
    public ValueTask<BossTowerPartyAttempt> TryRemoveAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => legacy.RemoveAsync(caller, targetSlot, token);
    public ValueTask<BossTowerPartyAttempt> TryTransferAsync(SelectedSessionIdentity caller, byte targetSlot, CancellationToken token) => legacy.TransferAsync(caller, targetSlot, token);

    private sealed class Legacy(IBossTowerDomainService towers, IPartyService parties, ISelectedSessionDomainService sessions,
        ICommandBus commands, PartyNotificationDelivery notifications, BossTowerProjectionDelivery projection,
        INetworkConnectionCloser connections, ITownAreaDomainService townAreas, ILogger logger, IDungeonRunDomainService? runs)
        : PartyMutationProtocolCommandHandler(sessions, parties, townAreas, null!, null!, null, logger,
            bossTowers: towers,
            close: session => connections.RequestCloseAsync(session.ConnectionId, CancellationToken.None), dungeonRuns: runs)
    {
        internal ValueTask<BossTowerPartyAttempt> LeaveAsync(SelectedSessionIdentity caller, bool allowVirtual, IGameServerMessage? response, CancellationToken token) =>
            CoordinateBossTowerPartyAsync(null, PartyChange.Leave, caller, null, allowVirtual, response, token);
        internal ValueTask<BossTowerPartyAttempt> RemoveAsync(SelectedSessionIdentity caller, byte slot, CancellationToken token) =>
            CoordinateBossTowerPartyAsync(null, PartyChange.Remove, caller, slot, false, null, token);
        internal ValueTask<BossTowerPartyAttempt> TransferAsync(SelectedSessionIdentity caller, byte slot, CancellationToken token) =>
            CoordinateBossTowerPartyAsync(null, PartyChange.Transfer, caller, slot, false, null, token);
        protected override ValueTask ProjectPartyAsync(PartyNotificationPlan plan, PartyNotificationRecipientPlan recipients, CancellationToken token) =>
            notifications.DeliverAsync(plan, recipients, token);
        protected override ValueTask ProjectBossTowerPartyDepartureAsync(CommandExecutionContext? execution, BossTowerActionResult action,
            SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token) =>
            projection.ProjectCommittedDepartureAsync(action, returning, response, token);
        protected override ValueTask ProjectBossTowerPartyHostAsync(BossTowerSnapshot run, CancellationToken token) => projection.ProjectCurrentHostAsync(run, token);
        protected override ValueTask<BossTowerActionResult> LeaveBossPartyTowerAsync(CommandExecutionContext? execution, BossTowerActionContext context,
            IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
            commands.SendAsync(new LeaveBossTowerCommand(context, false) { Sessions = participants }, token);
        protected override ValueTask<BossTowerActionResult> SynchronizeBossTowerPartyTowerAsync(CommandExecutionContext? execution, BossTowerActionContext context,
            SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
            commands.SendAsync(new SynchronizeBossTowerLeaderCommand(context, leader) { Sessions = participants }, token);
    }
}
