using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
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
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

internal sealed class IllusionTowerPartyCommandStages(
    IIllusionTowerDomainService towers, ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews, ICharacterStateProjectionDomainService currentState,
    ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService onlineCharacters, TimeProvider clock,
    Func<NetworkConnectionId, GameExplicitMessageDelivery, CancellationToken, ValueTask> register,
    Func<SelectedSessionIdentity, ValueTask> close, ILogger logger,
    Func<IReadOnlyList<TownAreaEntry>, CancellationToken, ValueTask<int>> enterTown)
    : Dfo.Server.Protocol.Game.Party.PartyMutationProtocolCommandHandler(sessions, null, null,
        null!, null!, null, logger, currentViews, currentState, characters, onlineCharacters,
        illusionTowers: towers, clock: clock, register: register, close: close)
{
    internal ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> ProjectActionAsync(
        TowerActionResult action, IGameServerMessage? shared, CancellationToken token, SelectedSessionIdentity? requester = null,
        IGameServerMessage? requesterResponse = null) => IllusionTowerProjectActionAsync(action, shared, token, requester, requesterResponse);
    internal ValueTask SendCommittedBatchAsync(IllusionTowerSnapshot run, SelectedSessionIdentity member, GameExplicitMessageDelivery batch,
        CancellationToken token) => IllusionTowerSendCommittedBatchAsync(run, member, batch, token);
    internal ValueTask AppendExperienceAsync(List<IGameServerMessage> frames, SelectedSessionIdentity session, ExperienceAward award,
        CancellationToken token) => IllusionTowerAppendExperienceAsync(frames, session, award, token);
    internal static void AppendInventory(List<IGameServerMessage> frames, IReadOnlyList<TowerInventoryUpdate> updates, DateTimeOffset at) =>
        IllusionTowerAppendInventory(frames, updates, at);
    internal static void AppendQuest(List<IGameServerMessage> frames, Dfo.Server.Abstractions.Quests.QuestProgressSnapshot? progress) =>
        IllusionTowerAppendQuest(frames, progress);
    internal ValueTask<List<TownAreaEntry>> CaptureReturnEntriesAsync(IReadOnlyList<SelectedSessionIdentity> returning,
        IGameServerMessage? response, List<(SelectedSessionIdentity, Exception)> failures, CancellationToken token, bool restoreInventory) =>
        IllusionTowerCaptureReturnEntriesAsync(returning, response, failures, token, restoreInventory);
    internal bool Exact(SelectedSessionIdentity session, IllusionTowerSnapshot expected) => IllusionTowerExact(session, expected);
    internal IReadOnlyList<SelectedSessionIdentity> ExactMembers(IllusionTowerSnapshot run) => IllusionTowerExactMembers(run);
    internal IllusionTowerSnapshot? CurrentRun(IllusionTowerSnapshot run) => IllusionTowerCurrentRun(run);
    internal ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> SendToExactAsync(IllusionTowerSnapshot run,
        Func<SelectedSessionIdentity, IReadOnlyList<IGameServerMessage>> frames, CancellationToken token) => IllusionTowerSendToExactAsync(run, frames, token);
    internal static TowerActionContext Context(IllusionTowerSnapshot run, SelectedSessionIdentity session) => IllusionTowerContext(run, session);
    internal ValueTask CloseQuietlyAsync(SelectedSessionIdentity session) => IllusionTowerCloseQuietlyAsync(session);
    internal ValueTask CloseProjectionFailuresAsync(IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures, Guid runId) =>
        IllusionTowerCloseProjectionFailuresAsync(failures, runId);

    internal ValueTask ProjectCommittedDepartureAsync(TowerActionResult action, SelectedSessionIdentity? returning,
        IGameServerMessage? response, CancellationToken token) => ProjectIllusionTowerPartyDepartureAsync(null, action, returning, response, token);
    internal ValueTask ProjectCurrentHostAsync(IllusionTowerSnapshot captured, CancellationToken token) => ProjectIllusionTowerPartyHostAsync(captured, token);
    protected override ValueTask<int> ReturnTownAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) => enterTown(entries, token);
}
