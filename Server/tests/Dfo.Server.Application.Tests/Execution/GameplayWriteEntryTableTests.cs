using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Dfo.Server.Application.World.BossTower;
using Dfo.Server.Application.World.IllusionTower;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Protocol.Game.Joust;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.Tournament;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// RQ-061 / RQ-072 (TASK-18 section 3): the two criteria that keep the write assertions complete.
/// The classification criterion lists every public, internal and explicit-interface member of the write entries
/// (appendix F's "move onto the executor" types, and since TASK-30 every holder of in-process gameplay state that
/// <see cref="GameplayStateCensusTests"/> finds), requires each to be classified here as write, read or appendix B exemption,
/// and reads each member's IL to require that exactly the write members call <see cref="GameplayWriteAssertion"/>
/// under their own name. The source criterion pins every <c>new DomainTransaction(</c> in <c>Server/src</c> and
/// the never-committed write-mode snapshot (TASK-18 ruling 3).
/// </summary>
/// <remarks>
/// The table is the judgement under review: a write member wrongly listed as a read passes both criteria. Each
/// write was read off its body (field or collection change, aggregate change, enlisting a participant, opening
/// a write unit of work, a store write, or a read that also tidies); a member doing both counts as a write.
/// Members only of nested private types are not listed: they cannot be reached from outside their owner.
/// Adding a type or a member means changing this table, and an exemption must name an existing row of spec
/// appendix B (RQ-011).
/// </remarks>
public sealed class GameplayWriteEntryTableTests
{
    internal enum Kind { Read, Write, Exempt }

    internal sealed record Entry(Kind Kind, string? Row = null, string? Reason = null);

    private static Entry W => new(Kind.Write);

    private static Entry R => new(Kind.Read);

    private static Entry ReadNote(string reason) => new(Kind.Read, Reason: reason);

    private static Entry Exempt(string row, string reason) => new(Kind.Exempt, row, reason);

    private const string ProducerRow = "生产者的入队侧（卡牌协调器在延时上的等待与 DrainAsync；GameplayExecutorLifetime 依次排空生产者再停止执行器）";

    private const string ConditionalEnlist =
        "enlists only into a writable ambient transaction, which only an asserted entry (or the never-committed snapshot) opens; what it registers (nothing, or a read set checked at prepare) takes effect only when that transaction commits";

    internal static readonly Type WeaknessHandle =
        typeof(WeaknessDomainService).GetNestedType("Weakness", BindingFlags.NonPublic) ?? throw new MissingMemberException("Weakness");

    internal static readonly IReadOnlyList<Type> Types =
    [
        typeof(OnlineStateRegistry),
        typeof(LoginSessionRegistry),
        typeof(DungeonRunDomainService),
        typeof(CreatureRuntimeDomainService),
        typeof(PartyService),
        typeof(TownAreaDomainService),
        typeof(DungeonParticipationDomainService),
        typeof(DungeonCardPresentationCoordinator),
        typeof(BossTowerDomainService),
        typeof(IllusionTowerDomainService),
        typeof(TournamentDungeonDomainService),
        typeof(AltarDungeonEngine),
        // Not named by appendix F: the altar state the engine works on lives here, and
        // AltarDungeonDomainService.HandleAsync removes from it without going through the engine.
        typeof(AltarStateStore),
        typeof(OnlineCharacterRuntimeDomainService),
        // TASK-30: the rest of the in-process gameplay state, found by GameplayStateCensusTests.
        typeof(ClientSettingsDomainService),
        typeof(WeaknessDomainService),
        // Private to Application: the handle WeaknessDomainService.For hands out behind IWeakness.
        WeaknessHandle,
        typeof(CharacterContainerDomainService),
        typeof(HousingState),
        typeof(ExpertStoreDomainService),
        typeof(PvpLobbyDomainService),
        typeof(DungeonEntryIntentDomainService),
        typeof(JoustMaintenanceService),
        typeof(SelectedSessionSafety),
        typeof(EffectOperationLedger),
        typeof(DungeonRunDeliveryRegistry),
        typeof(AltarQuotaDailyRefreshService),
        typeof(DimensionQuotaDailyRefreshService),
        typeof(GoldSilverHornQuotaDailyRefreshService),
        typeof(ItemUsageDailyRefreshService),
        typeof(MirrorQuotaDailyRefreshService),
        typeof(PremiumDailyRefreshService),
        typeof(ReviveCoinQuotaDailyRefreshService),
        typeof(HerasQuotaDailyRefreshService),
        typeof(TournamentQuotaDailyRefreshService),
        typeof(FatigueDailyRefreshService),
        typeof(PartyProjectionStreamRegistry),
        // Internal to Protocol: the stream handle PartyProjectionStreamRegistry.GetOrAdd hands out.
        typeof(PartyProjectionStreamRegistry).Assembly.GetType("Dfo.Server.Protocol.Game.Party.PartyDeliveryStream", throwOnError: true)!,
        typeof(PartyClientIdRegistry),
        typeof(BossTowerDepartureProjectionQueue),
        typeof(IllusionTowerDepartureProjectionQueue),
        typeof(JoustBetHandler),
    ];

    internal static readonly IReadOnlyDictionary<string, Entry> Table = new Dictionary<string, Entry>(StringComparer.Ordinal)
    {
        // OnlineStateRegistry
        ["OnlineStateRegistry.Authenticate(OnlineUserId, AccountId)"] = W,
        ["OnlineStateRegistry.EnterWorld(SelectedSessionIdentity)"] = W,
        ["OnlineStateRegistry.Fence(SelectedSessionIdentity)"] = W,
        ["OnlineStateRegistry.Find(AccountId)"] = R,
        ["OnlineStateRegistry.FindByCharacter(CharacterId)"] = R,
        ["OnlineStateRegistry.FindByConnection(NetworkConnectionId)"] = R,
        ["OnlineStateRegistry.FindByWireUserId(UInt16)"] = R,
        ["OnlineStateRegistry.FindEndpoint(GameSessionIdentity)"] = R,
        ["OnlineStateRegistry.GetRequiredUser(OnlineUserId)"] = R,
        ["OnlineStateRegistry.IsInWorld(SelectedSessionIdentity)"] = R,
        ["OnlineStateRegistry.IsSelected(SelectedSessionIdentity)"] = R,
        ["OnlineStateRegistry.PrepareAuthentication(OnlineUserId, AccountId)"] = W,
        ["OnlineStateRegistry.RegisterConnection(NetworkConnectionContext)"] = W,
        ["OnlineStateRegistry.ReleaseExact(SelectedSessionIdentity)"] = W,
        ["OnlineStateRegistry.Select(SelectedSessionIdentity)"] = W,
        ["OnlineStateRegistry.SnapshotAuthenticatedGameAccounts()"] = R,
        ["OnlineStateRegistry.SnapshotSelections()"] = R,
        ["OnlineStateRegistry.StandingOf(SelectedSessionIdentity)"] = R,
        ["OnlineStateRegistry.TryDisconnect(OnlineUserId, NetworkConnectionId, Int64)"] = W,
        ["OnlineStateRegistry.TryDisconnectConnection(NetworkConnectionId)"] = W,
        ["OnlineStateRegistry.TryGetByConnection(NetworkConnectionId, OnlineUserSnapshot&)"] = R,
        ["OnlineStateRegistry.TryGetInWorld(CharacterId, SelectedSessionIdentity&)"] = R,
        ["OnlineStateRegistry.TryGetInWorld(NetworkConnectionId, SelectedSessionIdentity&)"] = R,
        ["OnlineStateRegistry.TryGetInWorld(UInt16, SelectedSessionIdentity&)"] = R,
        ["OnlineStateRegistry.TryObserveEndpoint(GameSessionIdentity, SessionEndpoint)"] = W,

        // LoginSessionRegistry
        ["LoginSessionRegistry.CaptureAuthority(NetworkConnectionId)"] = ReadNote("returns a check over the current binding; changes nothing"),
        ["LoginSessionRegistry.Disconnected(NetworkConnectionId)"] = W,
        ["LoginSessionRegistry.IsSuperseded(NetworkConnectionId)"] = R,
        ["LoginSessionRegistry.Issue(AccountId, DateTimeOffset, DateTimeOffset)"] = W,
        ["LoginSessionRegistry.SweepExpired()"] = W,
        ["LoginSessionRegistry.TryBind(NetworkConnectionId, String, String)"] = W,

        // DungeonRunDomainService
        ["DungeonRunDomainService.AdvanceRoomMechanisms(DateTimeOffset)"] = W,
        ["DungeonRunDomainService.ClaimObjectDestruction(CharacterId, DungeonRunId, Int64, Int32)"] = W,
        ["DungeonRunDomainService.CommitActorDeath(DungeonActorDeathClaim)"] = W,
        ["DungeonRunDomainService.CommitActorDeathChange(DungeonActorDeathClaim)"] = W,
        ["DungeonRunDomainService.CommitClearRewardCard(DungeonClearRewardCardClaim)"] = W,
        ["DungeonRunDomainService.CommitGroundDrop(DungeonGroundDropClaim, DungeonPickupKind)"] = W,
        ["DungeonRunDomainService.CommitParticipantKillEffect(DungeonParticipantKillEffectClaim)"] = W,
        ["DungeonRunDomainService.CommitParticipantKillEffect(DungeonParticipantKillEffectClaim, ExperienceAward)"] = W,
        ["DungeonRunDomainService.CommitSettlement(DungeonSettlementClaim)"] = W,
        ["DungeonRunDomainService.End(CharacterId)"] = W,
        ["DungeonRunDomainService.End(CharacterId, DungeonRunId)"] = W,
        ["DungeonRunDomainService.EndAsync(CharacterId, DungeonRunId)"] = W,
        ["DungeonRunDomainService.Find(CharacterId)"] = R,
        ["DungeonRunDomainService.Find(DungeonRunId)"] = R,
        ["DungeonRunDomainService.FindEntryChange(CharacterId)"] = R,
        ["DungeonRunDomainService.FindExpiredClearReturns(DateTimeOffset)"] = R,
        ["DungeonRunDomainService.FindExpiredPartyWipes(DateTimeOffset)"] = W,
        ["DungeonRunDomainService.FixClearRewardCards(CharacterId, ClearRewardCardFace, ClearRewardCardFace, Int32?)"] = W,
        ["DungeonRunDomainService.FixClearRewardCards(CharacterId, DungeonRunId, ClearRewardCardFace, ClearRewardCardFace, Int32?, ClearRewardCardFace)"] = W,
        ["DungeonRunDomainService.HasUsedDungeonBuff(CharacterId, DungeonRunId)"] = R,
        ["DungeonRunDomainService.IsActorDeathClaimCurrent(DungeonActorDeathClaim)"] = R,
        ["DungeonRunDomainService.IsParticipantKillEffectClaimCurrent(DungeonParticipantKillEffectClaim)"] = R,
        ["DungeonRunDomainService.PreparePartyEntry(IReadOnlyList<DungeonRunParticipant>, DungeonLayout, DungeonDifficulty, DungeonCellSeed, DungeonRunId, DateTimeOffset)"] = W,
        ["DungeonRunDomainService.ReleaseActorDeath(DungeonActorDeathClaim)"] = W,
        ["DungeonRunDomainService.ReleaseClearRewardCard(DungeonClearRewardCardClaim)"] = W,
        ["DungeonRunDomainService.ReleaseGroundDrop(DungeonGroundDropClaim)"] = W,
        ["DungeonRunDomainService.ReleaseParticipantKillEffect(DungeonParticipantKillEffectClaim)"] = W,
        ["DungeonRunDomainService.ReleaseSettlement(DungeonSettlementClaim)"] = W,
        ["DungeonRunDomainService.ScheduleClearReturn(CharacterId, DungeonRunId, DateTimeOffset)"] = W,
        ["DungeonRunDomainService.StageDungeonBuff(CharacterId, DungeonRunId)"] = W,
        ["DungeonRunDomainService.StageDynamicCell(DungeonRunId, Int64, Int64, IReadOnlyList<DungeonCellActorState>, Boolean)"] = W,
        ["DungeonRunDomainService.StageGroundDrop(DungeonGroundDropClaim, DungeonPickupKind)"] = W,
        ["DungeonRunDomainService.StageRevival(CharacterId, CharacterId, DungeonRunId)"] = W,
        ["DungeonRunDomainService.TryAbortEntry(DungeonRunId, Int64, IReadOnlyList<CharacterId>)"] = W,
        ["DungeonRunDomainService.TryAddGroundItems(CharacterId, DungeonRunId, Int64, Int64, IReadOnlyList<DungeonGroundItemDraft>)"] = W,
        ["DungeonRunDomainService.TryClaimActorDeath(CharacterId, UInt16)"] = W,
        ["DungeonRunDomainService.TryClaimClearRewardCard(CharacterId, ClearRewardCardKind, Int32)"] = W,
        ["DungeonRunDomainService.TryClaimGroundDrop(CharacterId, DungeonRunId, Int64, UInt16)"] = W,
        ["DungeonRunDomainService.TryClaimParticipantKillEffect(DungeonActorDeathClaim, CharacterId)"] = W,
        ["DungeonRunDomainService.TryClaimSettlement(CharacterId)"] = W,
        ["DungeonRunDomainService.TryClaimSettlement(CharacterId, DungeonRunId)"] = W,
        ["DungeonRunDomainService.TryEndClearReturn(DungeonRunId, DateTimeOffset)"] = W,
        ["DungeonRunDomainService.TryEndClearReturnAsync(DungeonRunId, DateTimeOffset)"] = W,
        ["DungeonRunDomainService.TryEndPartyWipe(DungeonRunId, DateTimeOffset)"] = W,
        ["DungeonRunDomainService.TryEndPartyWipeAsync(DungeonRunId, DateTimeOffset)"] = W,
        ["DungeonRunDomainService.TryMarkParticipantDead(CharacterId, DungeonRunId, Int64)"] = W,
        ["DungeonRunDomainService.TryMarkParticipantReady(CharacterId, DungeonRunId, Int64)"] = W,
        ["DungeonRunDomainService.TryMove(CharacterId, DungeonRunId, Int64, MazeCell)"] = W,
        ["DungeonRunDomainService.TryMove(CharacterId, DungeonRunId, Int64, MazeCell, DungeonCellSeed)"] = W,
        ["DungeonRunDomainService.TryMove(CharacterId, MazeCell)"] = W,
        ["DungeonRunDomainService.TryOpenCardLayout(CharacterId, DungeonRunId)"] = W,
        ["DungeonRunDomainService.TryOpenClear(CharacterId, DungeonRunId, Int64)"] = W,
        ["DungeonRunDomainService.TryReserveClearRewardCard(CharacterId, DungeonRunId, ClearRewardCardKind, Int32)"] = W,
        ["DungeonRunDomainService.TrySettle(CharacterId)"] = W,
        ["DungeonRunDomainService.WillClearMap(DungeonActorDeathClaim)"] = R,

        // CreatureRuntimeDomainService
        ["CreatureRuntimeDomainService.AdvanceAsync(SelectedSessionIdentity, CancellationToken, Boolean?)"] = W,
        ["CreatureRuntimeDomainService.ChangeCombat(CharacterId, Boolean)"] = W,
        ["CreatureRuntimeDomainService.DiscardExact(SelectedSessionIdentity)"] = W,
        ["CreatureRuntimeDomainService.DrainProjections()"] = W,
        ["CreatureRuntimeDomainService.DueSessions()"] = R,
        ["CreatureRuntimeDomainService.Enter(SelectedSessionIdentity)"] = W,
        ["CreatureRuntimeDomainService.GainExperienceAsync(SelectedSessionIdentity, Int32, CancellationToken)"] = W,
        ["CreatureRuntimeDomainService.GainRoomExperienceAsync(SelectedSessionIdentity, DungeonRunId, MazeCell, Int32, CancellationToken)"] = W,
        ["CreatureRuntimeDomainService.HatchAsync(SelectedSessionIdentity, BusinessSlot, CancellationToken)"] = W,
        ["CreatureRuntimeDomainService.Invalidate(CharacterId)"] = W,
        ["CreatureRuntimeDomainService.IsEquipped(SelectedSessionIdentity, Int32)"] = R,
        ["CreatureRuntimeDomainService.LeaveAsync(SelectedSessionIdentity, CancellationToken)"] = W,
        ["CreatureRuntimeDomainService.Of(CharacterId)"] = ReadNote("returns a handle; every operation through it requires an ambient transaction, which only an asserted entry opens"),
        ["CreatureRuntimeDomainService.QueueRoomExperience(SelectedSessionIdentity, DungeonRunId, MazeCell, Int32)"] = W,

        // PartyService
        ["PartyService.AcceptInvite(PartyCharacterSnapshot, PartyCharacterSnapshot)"] = W,
        ["PartyService.CleanupCharacter(CharacterId)"] = W,
        ["PartyService.CreateOrUpdateSettings(PartyCharacterSnapshot, PartySettings, PartyTitle)"] = W,
        ["PartyService.FindByKey(PartyKey)"] = R,
        ["PartyService.FindParty(PartyCharacterSnapshot)"] = R,
        ["PartyService.LeaveParty(CharacterId)"] = W,
        ["PartyService.Query(PartyDirectoryQuery)"] = R,
        ["PartyService.RemoveMember(CharacterId, CharacterId)"] = W,
        ["PartyService.RequestInvite(PartyCharacterSnapshot, PartyCharacterSnapshot, PartyInviteDisplayState)"] = W,
        ["PartyService.TransferLeadership(CharacterId, CharacterId)"] = W,
        ["PartyService.UpdateDirectoryPresence(CharacterId, PartyDirectoryScope)"] = W,

        // TownAreaDomainService
        ["TownAreaDomainService.Enter(TownAreaMember)"] = W,
        ["TownAreaDomainService.Find(CharacterId)"] = R,
        ["TownAreaDomainService.Find(TownAreaKey)"] = R,
        ["TownAreaDomainService.LeaveExact(CharacterId, TownAreaMembershipLease, TownAreaDepartureReason)"] = W,
        ["TownAreaDomainService.MoveExact(CharacterId, TownAreaMembershipLease, CharacterPositionState)"] = W,
        ["TownAreaDomainService.Query(TownAreaFilter)"] = R,
        ["TownAreaDomainService.TransferExact(CharacterId, TownAreaMembershipLease, TownAreaKey, CharacterPositionState, Boolean)"] = W,

        // DungeonParticipationDomainService
        ["DungeonParticipationDomainService.Find(CharacterId)"] = ReadNote(ConditionalEnlist),
        ["DungeonParticipationDomainService.Release(CharacterId, DungeonParticipationOwner)"] = W,
        ["DungeonParticipationDomainService.ReleaseCommitted(CharacterId, DungeonParticipationOwner)"] = W,
        ["DungeonParticipationDomainService.TryClaim(IReadOnlyList<CharacterId>, DungeonParticipationOwner)"] = W,

        // DungeonCardPresentationCoordinator
        ["DungeonCardPresentationCoordinator.Cancel(CharacterId, DungeonRunId)"] = W,
        ["DungeonCardPresentationCoordinator.Dispose()"] = Exempt(ProducerRow, "the stop step of DrainAsync (cancel the delays) without the wait, when the container is disposed after the drain"),
        ["DungeonCardPresentationCoordinator.DrainAsync(CancellationToken)"] = Exempt(ProducerRow, "a producer drain: cancels the card delays and waits for the timer CMDs already queued; GameplayExecutorLifetime runs it off the executor"),
        ["DungeonCardPresentationCoordinator.OpenOnRequestAsync(SelectedSessionIdentity, DungeonRunId, CancellationToken)"] = W,
        ["DungeonCardPresentationCoordinator.ScheduleAfterSettlement(SelectedSessionIdentity, DungeonRunId)"] = W,

        // BossTowerDomainService
        ["BossTowerDomainService.CanRetryAsync(BossTowerActionContext, CancellationToken)"] = W,
        ["BossTowerDomainService.CanShop(SelectedSessionIdentity, Int32, Int32)"] = ReadNote(ConditionalEnlist),
        ["BossTowerDomainService.DieAsync(BossTowerActionContext, IEffectFootprintRecorder, CancellationToken)"] = W,
        ["BossTowerDomainService.EnterAsync(IReadOnlyList<TowerEntryMember>, SelectedSessionIdentity, UInt32, CancellationToken, Byte, Byte, Byte)"] = W,
        ["BossTowerDomainService.Find(SelectedSessionIdentity)"] = ReadNote(ConditionalEnlist),
        ["BossTowerDomainService.FindReturnsDue(DateTimeOffset)"] = R,
        ["BossTowerDomainService.IsInTower(CharacterId)"] = ReadNote(ConditionalEnlist),
        ["BossTowerDomainService.KillAsync(BossTowerActionContext, UInt16, IEffectFootprintRecorder, CancellationToken)"] = W,
        ["BossTowerDomainService.LeaveAsync(BossTowerActionContext, Boolean, IEffectFootprintRecorder, CancellationToken)"] = W,
        ["BossTowerDomainService.LoadAsync(BossTowerActionContext, CancellationToken)"] = W,
        ["BossTowerDomainService.NextLevelAsync(BossTowerActionContext, Byte, Byte, CancellationToken)"] = W,
        ["BossTowerDomainService.PickupAsync(BossTowerActionContext, UInt16, CancellationToken)"] = W,
        ["BossTowerDomainService.ReleaseForSelectionAsync(BossTowerActionContext, Int32, CancellationToken)"] = W,
        ["BossTowerDomainService.SelectResultAsync(BossTowerActionContext, Byte, CancellationToken)"] = W,
        ["BossTowerDomainService.StartAsync(BossTowerActionContext, CancellationToken)"] = W,
        ["BossTowerDomainService.Supports(Int32)"] = R,
        ["BossTowerDomainService.SynchronizeLeaderAsync(BossTowerActionContext, SelectedSessionIdentity, CancellationToken)"] = W,

        // IllusionTowerDomainService
        ["IllusionTowerDomainService.CanContinueAsync(TowerActionContext, CancellationToken)"] = W,
        ["IllusionTowerDomainService.DieAsync(TowerActionContext, IEffectFootprintRecorder, CancellationToken)"] = W,
        ["IllusionTowerDomainService.EnterAsync(IReadOnlyList<TowerEntryMember>, SelectedSessionIdentity, UInt32, CancellationToken, Int32)"] = W,
        ["IllusionTowerDomainService.Find(SelectedSessionIdentity)"] = ReadNote(ConditionalEnlist),
        ["IllusionTowerDomainService.FindReturnsDue(DateTimeOffset)"] = R,
        ["IllusionTowerDomainService.IsInTower(CharacterId)"] = ReadNote(ConditionalEnlist),
        ["IllusionTowerDomainService.KillAsync(TowerActionContext, UInt16, IEffectFootprintRecorder, CancellationToken)"] = W,
        ["IllusionTowerDomainService.LeaveAsync(TowerActionContext, Boolean, IEffectFootprintRecorder, CancellationToken)"] = W,
        ["IllusionTowerDomainService.LoadAsync(TowerActionContext, CancellationToken)"] = W,
        ["IllusionTowerDomainService.MarkSettledAsync(TowerActionContext, TowerSettlementReceipt, CancellationToken)"] = W,
        ["IllusionTowerDomainService.NextLevelAsync(TowerActionContext, CancellationToken)"] = W,
        ["IllusionTowerDomainService.PickupAsync(TowerActionContext, UInt16, CancellationToken)"] = W,
        ["IllusionTowerDomainService.SelectReturnAsync(TowerActionContext, Byte, CancellationToken)"] = W,
        ["IllusionTowerDomainService.StartAsync(TowerActionContext, CancellationToken)"] = W,
        ["IllusionTowerDomainService.SupportsDungeonAsync(Int32, CancellationToken)"] = R,
        ["IllusionTowerDomainService.SynchronizeLeaderAsync(TowerActionContext, SelectedSessionIdentity, CancellationToken)"] = W,
        ["IllusionTowerDomainService.TryReserveOrdinaryEntry(IReadOnlyList<CharacterId>)"] = W,

        // TournamentDungeonDomainService
        ["TournamentDungeonDomainService.AdvanceAsync(SelectedSessionIdentity, DungeonRunId, UInt16, CancellationToken)"] = W,
        ["TournamentDungeonDomainService.BeginRewardSelection(SelectedSessionIdentity, DungeonRunId)"] = W,
        ["TournamentDungeonDomainService.CanKill(DungeonRunState, UInt16)"] = R,
        ["TournamentDungeonDomainService.CanRevive(CharacterId, DungeonRunId)"] = R,
        ["TournamentDungeonDomainService.EliminateAsync(SelectedSessionIdentity, DungeonRunId, CancellationToken)"] = W,
        ["TournamentDungeonDomainService.EnterAsync(DungeonEntryContext, CancellationToken)"] = W,
        ["TournamentDungeonDomainService.Find(CharacterId)"] = R,
        ["TournamentDungeonDomainService.HandleAsync(DomainEventEnvelope<DungeonParticipantLeft>, CancellationToken)"] = W,
        ["TournamentDungeonDomainService.PendingRewards(DateTimeOffset)"] = R,
        ["TournamentDungeonDomainService.ReadQuotaAsync(CharacterId, CancellationToken)"] = R,
        ["TournamentDungeonDomainService.SelectRewardAsync(SelectedSessionIdentity, DungeonRunId, Byte, Byte, CancellationToken)"] = W,
        ["TournamentDungeonDomainService.StageRevival(CharacterId, DungeonRunId)"] = W,
        ["TournamentDungeonDomainService.Supports(Int32)"] = R,

        // AltarDungeonEngine
        ["AltarDungeonEngine.AdvanceAsync(AltarStateStore, DungeonRunId, Int64, Int64, DateTimeOffset, CancellationToken)"] = W,
        ["AltarDungeonEngine.CanRevive(AltarStateStore, CharacterId, DungeonRunId)"] = R,
        ["AltarDungeonEngine.EnterAsync(AltarStateStore, Int32, DungeonEntryContext, CancellationToken)"] = W,
        ["AltarDungeonEngine.FailAsync(AltarStateStore, DungeonRunId, CancellationToken)"] = W,
        ["AltarDungeonEngine.Find(AltarStateStore, DungeonRunId)"] = R,
        ["AltarDungeonEngine.Pending(AltarStateStore)"] = R,
        ["AltarDungeonEngine.Period()"] = R,
        ["AltarDungeonEngine.ReadQuotaAsync(Int32, CharacterId, CancellationToken)"] = R,
        ["AltarDungeonEngine.ReadyAsync(AltarStateStore, SelectedSessionIdentity, DungeonRunId, Int64, CancellationToken)"] = W,
        ["AltarDungeonEngine.SelectDifficultyAsync(AltarStateStore, SelectedSessionIdentity, DungeonRunId, Int64, Int64, Byte, CancellationToken)"] = W,
        ["AltarDungeonEngine.SettleBeforeExitAsync(AltarStateStore, DungeonRunId, CancellationToken, CharacterId?)"] = W,
        ["AltarDungeonEngine.StageRevival(AltarStateStore, CharacterId, DungeonRunId)"] = W,

        // AltarStateStore
        ["AltarStateStore.All()"] = R,
        ["AltarStateStore.Find(DungeonRunId)"] = R,
        ["AltarStateStore.Put(AltarDungeonState, IDomainTransactionAccessor)"] = W,
        ["AltarStateStore.Remove(DungeonRunId)"] = W,

        // OnlineCharacterRuntimeDomainService
        ["OnlineCharacterRuntimeDomainService.ApplySpiritPotion(SelectedSessionIdentity, Double, TimeSpan)"] = W,
        ["OnlineCharacterRuntimeDomainService.Begin(SelectedSessionIdentity, CharacterPositionState, Byte)"] = W,
        ["OnlineCharacterRuntimeDomainService.CrossInto(SelectedSessionIdentity, Byte, Byte, Int16, Int16)"] = W,
        ["OnlineCharacterRuntimeDomainService.EndExact(SelectedSessionIdentity)"] = W,
        ["OnlineCharacterRuntimeDomainService.HpPercentOf(CharacterId)"] = R,
        ["OnlineCharacterRuntimeDomainService.IsCoolingDown(CharacterId, ItemCooldownKey)"] = R,
        ["OnlineCharacterRuntimeDomainService.PositionOf(CharacterId)"] = R,
        ["OnlineCharacterRuntimeDomainService.PositionOf(SelectedSessionIdentity)"] = R,
        ["OnlineCharacterRuntimeDomainService.SpiritPotionBonusRateOf(CharacterId)"] = R,
        ["OnlineCharacterRuntimeDomainService.StartCooldown(SelectedSessionIdentity, ItemCooldownKey, TimeSpan)"] = W,
        ["OnlineCharacterRuntimeDomainService.StepTo(SelectedSessionIdentity, Int16, Int16, Byte)"] = W,

        // ClientSettingsDomainService
        ["ClientSettingsDomainService.ActivateHotkeyProfile(ClientSettingsOrigin, Byte)"] = W,
        ["ClientSettingsDomainService.CaptureHotkeyUpload(ClientSettingsOrigin)"] = R,
        ["ClientSettingsDomainService.ForgetConnection(GameSessionIdentity)"] = W,
        ["ClientSettingsDomainService.ForgetConnection(NetworkConnectionId)"] = W,
        ["ClientSettingsDomainService.ReadAppearanceAsync(AccountId, Byte, CancellationToken)"] = R,
        ["ClientSettingsDomainService.ReadHotkeysAsync(AccountId, CancellationToken)"] = R,
        ["ClientSettingsDomainService.SaveHotkeysAsync(ClientHotkeyUploadContext, Byte[], CancellationToken)"] = W,
        ["ClientSettingsDomainService.SaveMainAsync(ClientSettingsOrigin, Byte[], CancellationToken)"] = W,
        ["ClientSettingsDomainService.SaveQuickChatAsync(ClientSettingsOrigin, Byte, Byte[], CancellationToken)"] = W,

        // WeaknessDomainService (its handle's writes, on the private Weakness class, assert as Weakness.*)
        ["WeaknessDomainService.AdvanceRecovery()"] = W,
        ["WeaknessDomainService.For(CharacterId)"] = ReadNote("returns a handle, the private Weakness listed below; its write members assert"),
        ["WeaknessDomainService.RetryPendingWeakness(CancellationToken)"] = W,
        ["WeaknessDomainService.get_RecoveryInterval()"] = R,

        // Weakness, the private handle (no state of its own: it writes its owner's entries and pending penalties)
        ["Weakness.get_Value()"] = R,
        ["Weakness.PauseRecovery()"] = W,
        ["Weakness.RecoveryGoldCost(CancellationToken)"] = R,
        ["Weakness.Recover()"] = W,
        ["Weakness.ResumeRecovery()"] = W,
        ["Weakness.SetWeakness()"] = W,

        // CharacterContainerDomainService
        ["CharacterContainerDomainService.Clear(CharacterId)"] = W,
        ["CharacterContainerDomainService.FindContainer(CharacterId, InventoryContainerType, Boolean)"] = ReadNote(StagedRead),
        ["CharacterContainerDomainService.GetContainer(CharacterId, InventoryContainerType, Boolean)"] = ReadNote(StagedRead),
        ["CharacterContainerDomainService.GetContainers(CharacterId, InventoryContainerType)"] = ReadNote(StagedRead),
        ["CharacterContainerDomainService.Register(CharacterId, IItemContainer)"] = W,
        ["CharacterContainerDomainService.Remove(CharacterId, ContainerId)"] = W,
        ["CharacterContainerDomainService.ReplaceDefault(CharacterId, ContainerId, IItemContainer)"] = W,
        ["CharacterContainerDomainService.UseContainer(CharacterId, ContainerId)"] = W,

        // HousingState
        ["HousingState.CanEdit(Guid, SelectedSessionIdentity)"] = R,
        ["HousingState.CloseExact(SelectedSessionIdentity)"] = W,
        ["HousingState.Edit(Guid, SelectedSessionIdentity, Boolean)"] = W,
        ["HousingState.EnsureAsync(Guid, CancellationToken)"] = W,
        ["HousingState.IsInside(SelectedSessionIdentity, HousingSnapshot)"] = R,
        ["HousingState.ReadAsync(Guid, CancellationToken)"] = R,
        ["HousingState.RecordHarvestAsync(Guid, Int32, IReadOnlyList<HousingReward>, CancellationToken)"] = W,
        ["HousingState.RememberReturn(SelectedSessionIdentity, CharacterPositionState)"] = W,
        ["HousingState.ReturnFor(SelectedSessionIdentity)"] = R,
        ["HousingState.SaveAsync(HousingSnapshot, CancellationToken)"] = W,

        // ExpertStoreDomainService
        ["ExpertStoreDomainService.Close(CharacterId)"] = W,
        ["ExpertStoreDomainService.CloseExact(SelectedSessionIdentity)"] = W,
        ["ExpertStoreDomainService.HasOpenStore(CharacterId)"] = R,
        ["ExpertStoreDomainService.Open(CharacterId)"] = W,
        ["ExpertStoreDomainService.Open(SelectedSessionIdentity)"] = W,

        // PvpLobbyDomainService
        ["PvpLobbyDomainService.ClearReady(CharacterId)"] = W,
        ["PvpLobbyDomainService.ClearReadyExact(SelectedSessionIdentity)"] = W,
        ["PvpLobbyDomainService.MarkReady(PvpLobbyParticipant)"] = W,
        ["PvpLobbyDomainService.Offer(PvpRoom)"] = W,
        ["PvpLobbyDomainService.OfferedOn(Int32)"] = R,
        ["PvpLobbyDomainService.ReadyOn(Int32, CharacterId)"] = R,
        ["PvpLobbyDomainService.Withdraw(UInt16)"] = W,

        // DungeonEntryIntentDomainService
        ["DungeonEntryIntentDomainService.ClearExact(SelectedSessionIdentity)"] = W,
        ["DungeonEntryIntentDomainService.Consume(SelectedSessionIdentity)"] = W,
        ["DungeonEntryIntentDomainService.Find(SelectedSessionIdentity)"] = R,
        ["DungeonEntryIntentDomainService.Record(SelectedSessionIdentity, DungeonEntryIntent)"] = W,
        ["DungeonEntryIntentDomainService.RestoreIfAbsent(SelectedSessionIdentity, DungeonEntryIntent)"] = W,
        ["DungeonEntryIntentDomainService.TryReplaceExact(SelectedSessionIdentity, DungeonEntryIntent, DungeonEntryIntent)"] = W,

        // JoustMaintenanceService (the private tick body AdvanceAndPublishAsync asserts)
        ["JoustMaintenanceService.StartAsync(CancellationToken)"] = Exempt(StartupRow, "runs the recovery tick as a startup CMD and waits for it before the host is ready; the CMD body asserts"),
        ["JoustMaintenanceService.TickAsync(CancellationToken, Boolean)"] = Exempt(ProducerRow, TimerEntry),

        // SelectedSessionSafety
        ["SelectedSessionSafety.BlockAsync(SelectedSessionRecipient, Exception, String)"] = W,
        ["SelectedSessionSafety.Capture(SelectedSessionIdentity)"] = R,
        ["SelectedSessionSafety.Capture(SelectedSessionIdentity, GameMessageMetadata)"] = R,
        ["SelectedSessionSafety.DemandCurrent(SelectedSessionIdentity)"] = R,
        ["SelectedSessionSafety.DemandCurrent(SelectedSessionRecipient)"] = R,
        ["SelectedSessionSafety.IsBlocked(NetworkConnectionContext)"] = R,
        ["SelectedSessionSafety.PublicationFailedAsync(SelectedSessionRecipient, Exception, String)"] = W,

        // EffectOperationLedger
        ["EffectOperationLedger.Admit(NetworkConnectionContext, IEffectSettlementRequest)"] = W,
        ["EffectOperationLedger.FinalizePending(EffectOperationEntry)"] = W,
        ["EffectOperationLedger.DispatchAsync(NetworkConnectionContext, IEffectSettlementRequest, Func<ValueTask>, CancellationToken)"] = W,

        // DungeonRunDeliveryRegistry
        ["DungeonRunDeliveryRegistry.CompleteKillDelivery(DungeonRunId, RoomActorId)"] = W,
        ["DungeonRunDeliveryRegistry.Find(DungeonRunId)"] = R,
        ["DungeonRunDeliveryRegistry.FindKillResult(DungeonRunId, RoomActorId, CharacterId)"] = R,
        ["DungeonRunDeliveryRegistry.ParticipantDeparted(DungeonRunId, CharacterId)"] = W,
        ["DungeonRunDeliveryRegistry.RecordKillResult(DungeonRunId, RoomActorId, CharacterId, ApplyMonsterKillResult)"] = W,
        ["DungeonRunDeliveryRegistry.Register(DungeonRunId, IReadOnlyList<SelectedSessionIdentity>)"] = W,
        ["DungeonRunDeliveryRegistry.RemoveParticipant(DungeonRunId, CharacterId)"] = W,

        // The ten daily refreshes (each one's private timer body RefreshCoreAsync asserts)
        ["AltarQuotaDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),
        ["DimensionQuotaDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),
        ["GoldSilverHornQuotaDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),
        ["ItemUsageDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),
        ["MirrorQuotaDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),
        ["PremiumDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),
        ["ReviveCoinQuotaDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),
        ["HerasQuotaDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),
        ["TournamentQuotaDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),
        ["FatigueDailyRefreshService.RefreshAsync(CancellationToken)"] = Exempt(ProducerRow, TimerEntry),

        // PartyProjectionStreamRegistry and its stream handle
        ["PartyProjectionStreamRegistry.GetOrAdd(Int64)"] = W,
        ["PartyProjectionStreamRegistry.Dfo.Server.Abstractions.Networking.IPartyProjectionStateDomainService.StreamOf(Int64)"] = W,
        ["PartyDeliveryStream.get_CurrentMembers()"] = W,
        ["PartyDeliveryStream.get_CurrentPartyId()"] = R,
        ["PartyDeliveryStream.get_FullStateMembers()"] = W,
        ["PartyDeliveryStream.get_LatestRevision()"] = R,
        ["PartyDeliveryStream.set_CurrentPartyId(UInt16?)"] = W,
        ["PartyDeliveryStream.set_LatestRevision(Int64)"] = W,

        // PartyClientIdRegistry
        ["PartyClientIdRegistry.For(PartyKey, Int64)"] = W,
        ["PartyClientIdRegistry.Project(PartyNotificationPlan)"] = W,
        ["PartyClientIdRegistry.Project(PartySnapshot)"] = W,

        // The two tower departure queues
        ["BossTowerDepartureProjectionQueue.Enqueue(BossTowerActionResult)"] = W,
        ["BossTowerDepartureProjectionQueue.TryDequeue(BossTowerActionResult&)"] = W,
        ["IllusionTowerDepartureProjectionQueue.Enqueue(TowerActionResult)"] = W,
        ["IllusionTowerDepartureProjectionQueue.TryDequeue(TowerActionResult&)"] = W,

        // JoustBetHandler has no listed member: its protected HandleMessageAsync asserts.
    };

    private const string StartupRow = "开始监听前的启动期工作";

    private const string TimerEntry =
        "enqueues the body as a timer CMD and waits for it; the private CMD body asserts (the TASK-30 counterexample calls it directly)";

    private const string StagedRead =
        "in a transaction it reads a staged copy, which commits nothing unless a write marked it";

    [Fact]
    public void Every_member_of_the_listed_types_is_classified_and_exactly_the_writes_assert()
    {
        var violations = ClassificationViolations(Types, Table);

        if (Environment.GetEnvironmentVariable("DFO_ENTRY_TABLE_OUT") is { Length: > 0 } output)
            File.WriteAllText(output, Render(Types, Table, violations));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void An_unclassified_member_is_refused()
    {
        var table = new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            [nameof(Probe) + "." + "Change(Int32)"] = W,
        };

        var violations = ClassificationViolations([typeof(Probe)], table);

        Assert.Contains(violations, violation => violation.StartsWith("Probe.Look()", StringComparison.Ordinal)
            && violation.Contains("not classified", StringComparison.Ordinal));
    }

    [Fact]
    public void A_write_member_without_the_assertion_is_refused()
    {
        var table = new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            [nameof(Probe) + "." + "Change(Int32)"] = W,
            [nameof(Probe) + "." + "Look()"] = W,
        };

        var violations = ClassificationViolations([typeof(Probe)], table);

        Assert.Equal(["Probe.Look(): write member does not assert"], violations);
    }

    [Fact]
    public void A_read_member_that_asserts_and_a_stale_or_misnamed_row_are_refused()
    {
        var table = new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            [nameof(Probe) + "." + "Change(Int32)"] = R,
            [nameof(Probe) + "." + "Look()"] = R,
            [nameof(Probe) + "." + "Gone()"] = W,
            [nameof(Misnamed) + "." + "Change()"] = W,
        };

        var violations = ClassificationViolations([typeof(Probe), typeof(Misnamed)], table);

        Assert.Contains("Probe.Change(Int32): read member asserts", violations);
        Assert.Contains("Probe.Gone(): stale row, no such member", violations);
        Assert.Contains("Misnamed.Change(): asserts as Probe.Change, not as Misnamed.Change", violations);
    }

    [Fact]
    public void Asynchronous_writes_are_seen_through_their_state_machine()
    {
        var table = new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            [nameof(AsyncProbe) + "." + "ChangeAsync()"] = W,
            [nameof(AsyncProbe) + "." + "LookAsync()"] = W,
        };

        var violations = ClassificationViolations([typeof(AsyncProbe)], table);

        Assert.Equal(["AsyncProbe.LookAsync(): write member does not assert"], violations);
    }

    /// <summary>The <c>new DomainTransaction(</c> sites in <c>Server/src</c>, by file and enclosing method.</summary>
    internal static readonly IReadOnlyDictionary<(string File, string Method), Site> TransactionSites =
        new Dictionary<(string, string), Site>
        {
            [("Dfo.Server.Application/Transactions/DomainTransactionScope.cs", "Begin")] = Site.Write,
            [("Dfo.Server.Application/Transactions/DomainTransactionScope.cs", "BeginSnapshot")] = Site.Snapshot,
            [("Dfo.Server.Application/Characters/CharacterStateAccess.cs", "ReadAsync")] = Site.Read,
            [("Dfo.Server.Application/Characters/CharacterStateAccess.cs", "ChangeAsync")] = Site.Write,
            [("Dfo.Server.Application/Characters/CharacterStateAccess.cs", "AloneAsync")] = Site.Write,
            [("Dfo.Server.Application/Inventory/ItemContainers.cs", "ReadAsync")] = Site.Read,
            [("Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs", "WithinAsync")] = Site.Write,
        };

    internal enum Site { Read, Write, Snapshot }

    private const string SnapshotCaller = "Dfo.Server.Application/Characters/CharacterStateAccess.cs";

    [Fact]
    public void Transactions_are_created_only_at_the_listed_sites_and_the_writes_assert()
    {
        var source = Path.Combine(Repository(), "Server", "src");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal))
                continue;
            files[path] = File.ReadAllText(file);
        }

        var violations = SourceViolations(files);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void A_transaction_at_an_unlisted_site_is_refused()
    {
        var files = Listed();
        files["Dfo.Server.Application/Somewhere/NewService.cs"] =
            "class S\n{\n    private void Save()\n    {\n        var t = new DomainTransaction();\n    }\n}\n";
        files["Dfo.Server.Application/Somewhere/Qualified.cs"] =
            "class Q\n{\n    internal void Keep()\n    {\n        _ = new Dfo.Server.Application.Transactions.DomainTransaction(readOnly: false);\n    }\n}\n";

        var violations = SourceViolations(files);

        Assert.Contains("Dfo.Server.Application/Somewhere/NewService.cs: new DomainTransaction( in Save is not a listed site", violations);
        Assert.Contains("Dfo.Server.Application/Somewhere/Qualified.cs: new DomainTransaction( in Keep is not a listed site", violations);
    }

    [Fact]
    public void A_listed_write_site_without_its_assertion_is_refused()
    {
        var files = Listed();
        files["Dfo.Server.Application/Characters/CharacterStateAccess.cs"] =
            files["Dfo.Server.Application/Characters/CharacterStateAccess.cs"].Replace(
                "        GameplayWriteAssertion.Writing(nameof(AmbientStoreAccess) + \".\" + nameof(ChangeAsync));\n", "");

        Assert.Equal(["Dfo.Server.Application/Characters/CharacterStateAccess.cs: ChangeAsync creates a write transaction before GameplayWriteAssertion.Writing"],
            SourceViolations(files));
    }

    [Fact]
    public void A_second_snapshot_caller_or_a_committing_snapshot_is_refused()
    {
        var files = Listed();
        files["Dfo.Server.Application/Somewhere/NewService.cs"] =
            "class S\n{\n    private async ValueTask Save(DomainTransactionScope scope)\n    {\n        await using var t = scope.BeginSnapshot();\n    }\n}\n";
        files[SnapshotCaller] = files[SnapshotCaller].Replace(
            "        await using var snapshot = scope.BeginSnapshot();\n",
            "        await using var snapshot = scope.BeginSnapshot();\n        await snapshot.CommitAsync(default);\n");

        var violations = SourceViolations(files);

        Assert.Contains("Dfo.Server.Application/Somewhere/NewService.cs: BeginSnapshot called from Save, only ReadSnapshotAsync may", violations);
        Assert.Contains(SnapshotCaller + ": ReadSnapshotAsync commits its snapshot", violations);
    }

    internal static IReadOnlyList<string> ClassificationViolations(
        IReadOnlyList<Type> types, IReadOnlyDictionary<string, Entry> table)
    {
        var violations = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            foreach (var method in Members(type))
            {
                var key = Key(type, method);
                seen.Add(key);
                var calls = WritingCalls(method);
                if (!table.TryGetValue(key, out var entry))
                {
                    violations.Add(key + ": not classified");
                    continue;
                }
                var own = type.Name + "." + MemberName(method);
                if (entry.Kind == Kind.Write)
                {
                    if (calls.Count == 0) violations.Add(key + ": write member does not assert");
                    else if (!calls.Contains(own))
                        violations.Add($"{key}: asserts as {string.Join(", ", calls)}, not as {own}");
                }
                else if (calls.Count > 0)
                {
                    violations.Add(key + (entry.Kind == Kind.Read ? ": read member asserts" : ": exempt member asserts"));
                }
                if (entry.Kind == Kind.Exempt && (string.IsNullOrWhiteSpace(entry.Row) || string.IsNullOrWhiteSpace(entry.Reason)))
                    violations.Add(key + ": an exemption must name its appendix B row and a reason");
            }
        }
        var names = types.Select(type => type.Name + ".").ToArray();
        foreach (var key in table.Keys)
            if (names.Any(name => key.StartsWith(name, StringComparison.Ordinal)) && !seen.Contains(key))
                violations.Add(key + ": stale row, no such member");
        return violations;
    }

    internal static IReadOnlyList<string> SourceViolations(IReadOnlyDictionary<string, string> files)
    {
        var violations = new List<string>();
        var found = new HashSet<(string, string)>();
        foreach (var (path, text) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var code = WithoutComments(text);
            foreach (Match match in NewTransaction.Matches(code))
            {
                var (method, start) = EnclosingMethod(code, match.Index);
                found.Add((path, method));
                if (!TransactionSites.TryGetValue((path, method), out var site))
                {
                    violations.Add($"{path}: new DomainTransaction( in {method} is not a listed site");
                    continue;
                }
                if (site == Site.Write && !code[start..match.Index].Contains("GameplayWriteAssertion.Writing(", StringComparison.Ordinal))
                    violations.Add($"{path}: {method} creates a write transaction before GameplayWriteAssertion.Writing");
            }
            foreach (Match match in SnapshotCall.Matches(code))
            {
                var (method, _) = EnclosingMethod(code, match.Index);
                if (path != SnapshotCaller || method != "ReadSnapshotAsync")
                    violations.Add($"{path}: BeginSnapshot called from {method}, only ReadSnapshotAsync may");
            }
        }
        foreach (var site in TransactionSites.Keys.Where(site => !found.Contains(site)))
            violations.Add($"{site.File}: listed site {site.Method} no longer creates a transaction");
        if (files.TryGetValue(SnapshotCaller, out var caller))
        {
            var body = MethodBody(WithoutComments(caller), "ReadSnapshotAsync");
            if (body is null) violations.Add(SnapshotCaller + ": ReadSnapshotAsync not found");
            else if (body.Contains("Commit", StringComparison.Ordinal)) violations.Add(SnapshotCaller + ": ReadSnapshotAsync commits its snapshot");
        }
        return violations;
    }

    private static readonly Regex NewTransaction = new(@"\bnew\s+(?:[\w.]+\.)?DomainTransaction\s*\(", RegexOptions.Compiled);

    private static readonly Regex SnapshotCall = new(@"(?<!DomainTransactionLease\s)\bBeginSnapshot\s*\(", RegexOptions.Compiled);

    private static readonly Regex Declaration = new(
        @"^[ \t]*(?:(?:public|private|internal|protected)\s+)[^;=\n]*?\b(\w+)(?:<[^>()\n]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static (string Method, int Start) EnclosingMethod(string code, int index)
    {
        Match? last = null;
        foreach (Match match in Declaration.Matches(code[..index])) last = match;
        return last is null ? ("<none>", 0) : (last.Groups[1].Value, last.Index);
    }

    private static string? MethodBody(string code, string name)
    {
        foreach (Match match in Declaration.Matches(code))
        {
            if (match.Groups[1].Value != name) continue;
            var open = code.IndexOf('{', match.Index);
            var depth = 0;
            for (var i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) return code[open..(i + 1)];
            }
        }
        return null;
    }

    private static string WithoutComments(string text) =>
        Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", " ");

    private static Dictionary<string, string> Listed()
    {
        var source = Path.Combine(Repository(), "Server", "src");
        return TransactionSites.Keys.Select(site => site.File).Distinct()
            .ToDictionary(path => path, path => File.ReadAllText(Path.Combine(source, path)).Replace("\r\n", "\n"),
                StringComparer.Ordinal);
    }

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Server", "src"))) return directory.FullName;
        throw new DirectoryNotFoundException("The write entry criterion requires the repository checkout.");
    }

    internal static IEnumerable<MethodInfo> Members(Type type) =>
        type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                        | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.IsSpecialName || !method.IsDefined(typeof(CompilerGeneratedAttribute)))
            .Where(method => method.IsPublic || method.IsAssembly || method.IsFamilyOrAssembly
                             || (method.IsPrivate && method.Name.Contains('.', StringComparison.Ordinal)))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ThenBy(method => Signature(method), StringComparer.Ordinal);

    internal static string Key(Type type, MethodInfo method) => type.Name + "." + Signature(method);

    private static string MemberName(MethodInfo method)
    {
        var name = method.Name;
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    private static string Signature(MethodInfo method) =>
        method.Name + "(" + string.Join(", ", method.GetParameters().Select(parameter => Format(parameter.ParameterType))) + ")";

    private static string Format(Type type)
    {
        if (type.IsByRef) return Format(type.GetElementType()!) + "&";
        if (type.IsArray) return Format(type.GetElementType()!) + "[]";
        if (Nullable.GetUnderlyingType(type) is { } underlying) return Format(underlying) + "?";
        if (!type.IsGenericType) return type.Name;
        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Format)) + ">";
    }

    /// <summary>The entries a method passes to <see cref="GameplayWriteAssertion.Writing"/>, read off its IL.</summary>
    /// <remarks>
    /// An async method's body lives in its state machine's <c>MoveNext</c>. The entry is the constant string
    /// loaded just before the call; the members pass <c>nameof(Type) + "." + nameof(Member)</c>, a constant.
    /// </remarks>
    internal static IReadOnlyList<string> WritingCalls(MethodInfo method)
    {
        var body = method.GetCustomAttribute<AsyncStateMachineAttribute>() is { } machine
            ? machine.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            : method;
        var il = body.GetMethodBody()?.GetILAsByteArray();
        if (il is null) return [];
        var calls = new List<string>();
        string? lastString = null;
        var module = body.Module;
        var typeArguments = body.DeclaringType?.IsGenericType == true ? body.DeclaringType.GetGenericArguments() : null;
        var methodArguments = body.IsGenericMethod ? body.GetGenericArguments() : null;
        for (var offset = 0; offset < il.Length;)
        {
            var code = il[offset] == 0xFE ? (short)(0xFE00 | il[offset + 1]) : il[offset];
            var opCode = OpCodesByValue[code];
            offset += opCode.Size;
            var operand = offset;
            offset += OperandSize(opCode.OperandType, il, offset);
            if (opCode == OpCodes.Ldstr)
            {
                lastString = module.ResolveString(BitConverter.ToInt32(il, operand));
            }
            else if (opCode == OpCodes.Call)
            {
                MethodBase? target;
                try
                {
                    target = module.ResolveMethod(BitConverter.ToInt32(il, operand), typeArguments, methodArguments);
                }
                catch (ArgumentException)
                {
                    target = null;
                }
                if (target?.DeclaringType == typeof(GameplayWriteAssertion) && target.Name == nameof(GameplayWriteAssertion.Writing))
                    calls.Add(lastString ?? "<not a constant>");
            }
        }
        return calls;
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    private static int OperandSize(OperandType type, byte[] il, int offset) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, offset)),
        _ => 4,
    };

    private static string Render(IReadOnlyList<Type> types, IReadOnlyDictionary<string, Entry> table, IReadOnlyList<string> violations)
    {
        var text = new StringBuilder();
        text.AppendLine("# TASK-18 RQ-061 classification table, rendered by GameplayWriteEntryTableTests from the table it checks.");
        text.AppendLine("# columns: type | member signature | read / write / exempt | appendix B row (exempt) | note");
        int writes = 0, reads = 0, exempts = 0;
        foreach (var type in types)
        {
            int w = 0, r = 0, e = 0;
            text.AppendLine();
            text.AppendLine("## " + type.FullName);
            foreach (var method in Members(type))
            {
                var key = Key(type, method);
                if (!table.TryGetValue(key, out var entry))
                {
                    text.AppendLine($"{type.Name} | {key[(type.Name.Length + 1)..]} | UNCLASSIFIED");
                    continue;
                }
                switch (entry.Kind)
                {
                    case Kind.Write: w++; break;
                    case Kind.Read: r++; break;
                    default: e++; break;
                }
                text.AppendLine($"{type.Name} | {key[(type.Name.Length + 1)..]} | {entry.Kind.ToString().ToLowerInvariant()} | {entry.Row ?? ""} | {entry.Reason ?? ""}");
            }
            text.AppendLine($"# {type.Name}: write {w} + read {r} + exempt {e} = {w + r + e}");
            writes += w;
            reads += r;
            exempts += e;
        }
        text.AppendLine();
        text.AppendLine($"# total: write {writes} + read {reads} + exempt {exempts} = {writes + reads + exempts}, over {types.Count} types");
        text.AppendLine($"# violations: {violations.Count}");
        foreach (var violation in violations) text.AppendLine("#   " + violation);
        return text.ToString();
    }

    private sealed class Probe
    {
        private int value;

        public void Change(int next)
        {
            GameplayWriteAssertion.Writing(nameof(Probe) + "." + nameof(Change));
            value = next;
        }

        public int Look() => value;
    }

    private sealed class Misnamed
    {
        private int value;

        public void Change()
        {
            GameplayWriteAssertion.Writing(nameof(Probe) + "." + nameof(Change));
            value++;
        }

        public int Value => value;
    }

    private sealed class AsyncProbe
    {
        private int value;

        public async ValueTask ChangeAsync()
        {
            GameplayWriteAssertion.Writing(nameof(AsyncProbe) + "." + nameof(ChangeAsync));
            await Task.Yield();
            value++;
        }

        public async ValueTask<int> LookAsync()
        {
            await Task.Yield();
            return value;
        }
    }
}
