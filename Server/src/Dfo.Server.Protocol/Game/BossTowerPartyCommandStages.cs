using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;

using Dfo.Server.Protocol.Game.GroundPickup;




using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

using Dfo.Server.Protocol.Game.BossTowerInfo;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.BossTowerContinue;

internal sealed class BossTowerPartyCommandStages(
    IBossTowerDomainService towers, ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews, ICharacterStateProjectionDomainService currentState,
    ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService onlineCharacters, TimeProvider clock,
    Func<NetworkConnectionId, GameExplicitMessageDelivery, CancellationToken, ValueTask> register,
    Func<SelectedSessionIdentity, ValueTask> close, ILogger logger,
    ICommandBus commands, Func<IReadOnlyList<TownAreaEntry>, CancellationToken, ValueTask<int>> enterTown)
    : Dfo.Server.Protocol.Game.Party.PartyMutationProtocolCommandHandler(sessions, null, null,
        null!, null!, null, logger, currentViews, currentState, characters, onlineCharacters,
        bossTowers: towers, clock: clock, register: register, close: close)
{
    internal ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> ProjectActionAsync(
        BossTowerActionResult action, IGameServerMessage? shared, CancellationToken token, SelectedSessionIdentity? requester = null,
        IGameServerMessage? requesterResponse = null) => BossTowerProjectActionAsync(action, shared, token, requester, requesterResponse);
    internal ValueTask SendCommittedBatchAsync(BossTowerSnapshot run, SelectedSessionIdentity member, GameExplicitMessageDelivery batch,
        CancellationToken token) => BossTowerSendCommittedBatchAsync(run, member, batch, token);
    internal ValueTask AppendExperienceAsync(List<IGameServerMessage> frames, SelectedSessionIdentity session, ExperienceAward award,
        CancellationToken token) => BossTowerAppendExperienceAsync(frames, session, award, token);
    internal static void AppendInventory(List<IGameServerMessage> frames, IReadOnlyList<TowerInventoryUpdate> updates, DateTimeOffset at) =>
        BossTowerAppendInventory(frames, updates, at);
    internal static void AppendQuest(List<IGameServerMessage> frames, Dfo.Server.Abstractions.Quests.QuestProgressSnapshot? progress) =>
        BossTowerAppendQuest(frames, progress);
    internal ValueTask<List<TownAreaEntry>> CaptureReturnEntriesAsync(IReadOnlyList<SelectedSessionIdentity> returning,
        IGameServerMessage? response, List<(SelectedSessionIdentity, Exception)> failures, CancellationToken token, bool restoreInventory) =>
        BossTowerCaptureReturnEntriesAsync(returning, response, failures, token, restoreInventory);
    internal bool Exact(SelectedSessionIdentity session, BossTowerSnapshot expected) => BossTowerExact(session, expected);
    internal IReadOnlyList<SelectedSessionIdentity> ExactMembers(BossTowerSnapshot run) => BossTowerExactMembers(run);
    internal BossTowerSnapshot? CurrentRun(BossTowerSnapshot run) => BossTowerCurrentRun(run);
    internal ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> SendToExactAsync(BossTowerSnapshot run,
        Func<SelectedSessionIdentity, IReadOnlyList<IGameServerMessage>> frames, CancellationToken token) => BossTowerSendToExactAsync(run, frames, token);
    internal static BossTowerActionContext Context(BossTowerSnapshot run, SelectedSessionIdentity session) => BossTowerContext(run, session);
    internal ValueTask CloseQuietlyAsync(SelectedSessionIdentity session) => BossTowerCloseQuietlyAsync(session);
    internal ValueTask CloseProjectionFailuresAsync(IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures, Guid runId) =>
        BossTowerCloseProjectionFailuresAsync(failures, runId);
    internal static SelectedSessionIdentity[] Members(BossTowerSnapshot run) => BossTowerMembers(run);
    internal ValueTask ProjectCommittedDepartureAsync(BossTowerActionResult action, SelectedSessionIdentity? returning,
        IGameServerMessage? response, CancellationToken token) => ProjectBossTowerPartyDepartureAsync(null, action, returning, response, token);
    internal ValueTask ProjectCurrentHostAsync(BossTowerSnapshot captured, CancellationToken token) => ProjectBossTowerPartyHostAsync(captured, token);
    protected override ValueTask<int> ReturnTownAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) => enterTown(entries, token);
    protected override ValueTask<BossTowerActionResult> StartBossPartyTowerAsync(CommandExecutionContext? execution,
        BossTowerSnapshot run, SelectedSessionIdentity leader, CancellationToken token) =>
        commands.SendAsync(new StartBossTowerCommand(Context(run, leader)) { Sessions = Members(run) }, token);

}
