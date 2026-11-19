using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Raids;

/// <summary>Owns the process-local Anton raid and its roster, squads, and lifecycle.</summary>
public interface IAntonRaidDomainService : IDungeonBossClearHook
{
    int? RecruitmentChannel(SelectedSessionIdentity session);
    bool IsRecruitingMember(SelectedSessionIdentity session);
    IReadOnlyList<AntonRaidRecruitmentSnapshot> Directory(int channel);
    AntonRaidRecruitmentSnapshot? Find(int channel, uint number);
    uint? CheckApplicantTarget(SelectedSessionIdentity applicant, SelectedSessionIdentity target);
    AntonRaidRecruitmentResult Create(AntonRaidMember leader, ReadOnlyMemory<byte> title);
    AntonRaidApproval? Request(AntonRaidMember sender, AntonRaidMember recipient);
    AntonRaidRecruitmentResult Respond(SelectedSessionIdentity recipient, SelectedSessionIdentity sender, bool accepted);
    AntonRaidRecruitmentResult Assign(SelectedSessionIdentity sender, ushort target, uint squad);
    AntonRaidRecruitmentResult Leave(SelectedSessionIdentity sender, ushort target);
    AntonRaidRecruitmentChange? Disconnected(SelectedSessionIdentity session);
    AntonRaidRejoinOffer? RejoinOffer(SelectedSessionIdentity session);
    AntonRaidRecruitmentResult Rejoin(SelectedSessionIdentity session, uint number);
    IReadOnlyList<AntonRaidRejoinExpiry> RejoinExpirations();
    AntonRaidRecruitmentChange? ExpireRejoin(AntonRaidRejoinExpiry expiry);
    bool HasSquadRestorations();
    IReadOnlyList<PartyNotificationPlan> RestoreAvailableSquads();
    AntonRaidRecruitmentResult TransferLeadership(SelectedSessionIdentity sender, ushort target);
    AntonRaidRecruitmentResult ChangeTitle(SelectedSessionIdentity sender, ReadOnlyMemory<byte> title);
    AntonRaidRecruitmentResult MoveFromPartyRequest(SelectedSessionIdentity sender, SelectedSessionIdentity peer, AntonRaidSquadSignal signal);
    AntonRaidRecruitmentResult LeaveSquad(SelectedSessionIdentity sender);
    AntonRaidRecruitmentResult SetSquadLeader(SelectedSessionIdentity sender, SelectedSessionIdentity target);
    ValueTask<AntonRaidStart> StartAsync(SelectedSessionIdentity leader, CancellationToken ct);
    ValueTask<IReadOnlyList<AntonRaidTicket>> TicketsAsync(SelectedSessionIdentity session, CancellationToken ct);
    bool IsPreparing(SelectedSessionIdentity session);
    PartyOperationResult PrepareSquad(SelectedSessionIdentity sender, AntonRaidSquadSignal signal, SelectedSessionIdentity? peer = null);
    AntonRaidPreparationOutcome CancelPreparationFor(SelectedSessionIdentity session);
    IReadOnlyList<AntonRaidDeadline> PreparationDeadlines();
    IReadOnlyList<SelectedSessionIdentity> PreparationParticipants(AntonRaidDeadline deadline);
    ValueTask<AntonRaidPreparationOutcome> FinishPreparationAsync(AntonRaidDeadline deadline, CancellationToken ct);
    AntonRaidPreparationOutcome CancelPreparation(AntonRaidDeadline deadline);
    ValueTask<bool> IsRaidChannelAsync(SelectedSessionIdentity session, CancellationToken ct);
    DungeonEntryIntent? Selection(SelectedSessionIdentity leader, int selection);
    DungeonEntryIntent? HatcherySelection(SelectedSessionIdentity leader, int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
    ValueTask RegisterReadyAsync(SelectedSessionIdentity session, DungeonRunState run, CancellationToken ct);
    ValueTask RegisterDeathAsync(SelectedSessionIdentity session, DungeonRunState run, CancellationToken ct);
    bool CanRevive(DungeonRunState run, CharacterId target);
    ValueTask<bool> StageRevivalAsync(DungeonRunState run, CharacterId target, CancellationToken ct);
    AntonRaidCombatSnapshot? CombatSnapshot(SelectedSessionIdentity session);
    ValueTask<bool> ChangeShieldAsync(SelectedSessionIdentity session, uint symbol, uint value, byte operation, CancellationToken ct);
    ValueTask<bool> UseCombatBuffAsync(SelectedSessionIdentity session, byte type, ushort squad, IReadOnlyList<uint> targets, CancellationToken ct);
    ValueTask<bool> ReportMonsterStatusAsync(SelectedSessionIdentity session, IReadOnlyList<uint> hitPoints, CancellationToken ct);
    IReadOnlyList<AntonRaidCombatDeadline> CombatDeadlines();
    IReadOnlyList<SelectedSessionIdentity> CombatParticipants(AntonRaidCombatDeadline deadline);
    ValueTask<bool> AdvanceCombatAsync(AntonRaidCombatDeadline deadline, CancellationToken ct);
    ValueTask<AntonRaidStart> FinishIntermissionAsync(AntonRaidCombatDeadline deadline, CancellationToken ct);
    IReadOnlyList<Guid> PendingRunReturns();
    bool CompleteRunReturn(Guid run);
    AntonRaidPhaseOutcome? PhaseOutcome(Guid instance, byte phase);
    AntonRaidRecruitmentChange? CompletePhaseSettlement(Guid instance, byte phase);
}
