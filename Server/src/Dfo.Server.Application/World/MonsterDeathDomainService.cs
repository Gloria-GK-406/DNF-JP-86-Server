using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

public sealed class MonsterDeathDomainService(IDomainTransactionAccessor transactions,
    ISelectedSessionDomainService sessions,
    ICharacterDomainService characters, IDungeonRunDomainService runs, IMonsterKillDomainService kills,
    IMonsterKillRewardsDomainService rewards, IQuestOfferRefreshDomainService offers,
    IQuestItemDropDomainService questItems, IQuestProgressDomainService progress,
    ILogger<MonsterDeathDomainService> logger, ITournamentDungeonDomainService? tournaments = null,
    IEventService? events = null, ISaoTowerDomainService? sao = null, ISouthernDaleDomainService? southern = null) : IMonsterDeathDomainService
{
    public async ValueTask<ObjectDestructionResult> DestroyObjectAsync(SelectedSessionIdentity reporter,
        DungeonRunId runId, long roomVersion, int objectCode, IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken ct)
    {
        if (!sessions.IsInWorld(reporter))
            return new(null);
        var run = runs.Find(reporter.CharacterId);
        if (run is null || run.RunId != runId || run.RoomVersion != roomVersion)
            return new(null);
        // A reporting client is not the loot owner. The exact sessions frozen at the protocol
        // boundary are revalidated here, inside the command's transaction.
        if (!recipients.Contains(reporter)
            || recipients.Count != run.Participants.Count
            || recipients.Select(value => value.CharacterId).Distinct().Count() != recipients.Count
            || recipients.Any(recipient => !sessions.IsInWorld(recipient)
                || runs.Find(recipient.CharacterId) is not { } memberRun
                || memberRun.RunId != runId || memberRun.RoomVersion != roomVersion
                || !run.Participants.Any(member => member.CharacterId == recipient.CharacterId
                    && member.AccountId == recipient.AccountId)))
            return new(null);
        var claim = runs.ClaimObjectDestruction(reporter.CharacterId, runId,
            roomVersion, objectCode);
        if (claim is null) return new(null);
        var rewards = new List<ObjectQuestReward>();
        foreach (var recipient in recipients)
        {
            var additions = await questItems.GrantAsync(recipient.AccountId, recipient.CharacterId,
                claim.PendingLoot, ct).ConfigureAwait(false);
            var updated = additions.Count > 0
                ? await progress.ReportAsync(recipient.AccountId, recipient.CharacterId,
                    [new QuestHoldingsChanged()], ct).ConfigureAwait(false)
                : null;
            rewards.Add(new(recipient, additions, updated?.NotificationSnapshot));
        }
        return new(rewards);
    }

    public async ValueTask<MonsterDeathSettlementResult> SettleAsync(MonsterDeathSettlementRequest request, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(request.Reporter.AccountId, request.Reporter.CharacterId, request.Reporter));
        var transaction = transactions.Current ?? throw new InvalidOperationException("Monster death requires one use-case transaction.");
        if (transaction.IsReadOnly) throw new InvalidOperationException("Monster death cannot run in a read-only transaction.");
        var run = runs.Find(request.Reporter.CharacterId);
        MonsterDeathSettlementResult Refused(string reason)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Monster death settlement ignored character={CharacterId} run={RunId} expectedVersion={ExpectedVersion} actualVersion={ActualVersion} actor={Actor} reason={Reason}",
                    request.Reporter.CharacterId, request.RunId, request.RoomVersion, run?.RoomVersion, request.Actor, reason);
            return new([]);
        }
        if (run is null || run.RunId != request.RunId || run.RoomVersion != request.RoomVersion
            || !request.Participants.Contains(request.Reporter)) return Refused("StaleRunOrParticipant");
        var isTournament = run.Layout.Mode is DungeonRunMode.Tournament;
        if (isTournament && !(tournaments ?? throw new InvalidOperationException("Tournament lifecycle is unavailable."))
            .CanKill(run, request.Actor)) return Refused("TournamentCannotKill");
        foreach (var session in request.Participants)
            if (!sessions.IsInWorld(session)
                || runs.Find(session.CharacterId)?.RunId != run.RunId)
                throw new InvalidOperationException("A frozen dungeon participant changed before settlement.");
        var identification = await kills.IdentifyAsync(run, request.Actor, ct).ConfigureAwait(false);
        if (identification.Kill is not { } kill) return Refused("UnidentifiedActor");
        var pointLedgers = new Dictionary<CharacterId, Dfo.Server.Data.Characters.CharacterSkillProtocolMirrorState>();
        foreach (var session in request.Participants.DistinctBy(x => x.CharacterId))
        {
            var points = await characters.Get(session.CharacterId).RemainingSkillPointsAsync(ct).ConfigureAwait(false);
            if (points is null) return Refused("MissingSkillPointLedger");
            pointLedgers.Add(session.CharacterId, points);
        }
        footprint.DemandEffects(new(request.Reporter.AccountId, request.Reporter.CharacterId, request.Reporter));
        var death = runs.TryClaimActorDeath(request.Reporter.CharacterId, request.Actor);
        if (death is null) return Refused("ActorDeathNotClaimed");
        var clearsMap = runs.WillClearMap(death);
        var pending = new DeathCommit(runs, death, clearsMap);
        try { transaction.Enlist((runs, death.RunId, death.Actor, "death-settlement"), () => pending); }
        catch { runs.ReleaseActorDeath(death); throw; }
        var result = new List<MonsterDeathParticipantResult>();
        foreach (var session in request.Participants.DistinctBy(x => x.CharacterId))
        {
            footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
            var effect = runs.TryClaimParticipantKillEffect(death, session.CharacterId)
                ?? throw new InvalidOperationException("A participant's kill effect is already owned by another settlement.");
            pending.Add(effect);
            var character = characters.Get(session.CharacterId);
            var points = pointLedgers[session.CharacterId];
            var applied = await rewards.ApplyAsync(session, session.AccountId, session.CharacterId, kill,
                run.Layout.DungeonId, run.Difficulty, run.Layout.ExperienceWeight, run.PartyMemberCount,
                run.PartyKey, !isTournament && run.Layout.Mode is not DungeonRunMode.Altar && run.IsBossRoom && kill.IsMonster && kill.Rank == MonsterRank.Boss,
                run.RunId, death.Actor, (run.Layout.Mode as DungeonRunMode.Mirror)?.Plan.WorldMapId, death.ActorState?.PendingLoot, footprint, ct).ConfigureAwait(false);
            footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
            pending.Awards[session.CharacterId] = applied.Award;
            var progress = applied.Award is { } award ? new CharacterProgressionState(award.Level, award.Total)
                : await character.ProgressAsync(ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The character progression is unavailable.");
            Dfo.Server.Data.World.AvailableQuestEntryState? available = null;
            if (applied.Award?.LeveledUp == true)
            {
                points = await character.RemainingSkillPointsAsync(ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The updated skill point ledger is unavailable.");
                available = await offers.ReadAvailableAsync(session.AccountId, session.CharacterId, progress.Level, ct).ConfigureAwait(false);
            }
            result.Add(new(session, progress, points, applied, available));
        }
        footprint.DemandEffects(new(request.Reporter.AccountId, request.Reporter.CharacterId, request.Reporter));
        var tournament = isTournament
            ? await tournaments!.AdvanceAsync(request.Reporter, run.RunId, request.Actor, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The accepted tournament death could not advance its round.")
            : null;
        footprint.DemandEffects(new(request.Reporter.AccountId, request.Reporter.CharacterId, request.Reporter));
        if (clearsMap && run.IsBossRoom && southern?.Supports(run.Layout.DungeonId) == true)
            await southern.CompleteClearAsync(death, request.Participants, ct).ConfigureAwait(false);
        footprint.DemandEffects(new(request.Reporter.AccountId, request.Reporter.CharacterId, request.Reporter));
        if (clearsMap && run.Layout.Mode is DungeonRunMode.Sao && run.IsBossRoom)
            await (sao ?? throw new InvalidOperationException("SAO lifecycle is unavailable."))
                .CompleteClearAsync(death, request.Participants, ct).ConfigureAwait(false);
        footprint.DemandEffects(new(request.Reporter.AccountId, request.Reporter.CharacterId, request.Reporter));
        if (clearsMap)
            await (events ?? throw new InvalidOperationException("Map-clear events are unavailable."))
                .EnqueueAsync(new DungeonMapCleared(run.RunId, run.Layout.DungeonId, run.Difficulty,
                    run.CurrentCell, run.CurrentRoomMapId, run.RoomVersion, death.OccurredAt!.Value,
                    [.. request.Participants.DistinctBy(x => x.CharacterId).Select(session =>
                        run.Participants.Single(x => x.CharacterId == session.CharacterId))]) { IsBossRoom = run.IsBossRoom }, ct).ConfigureAwait(false);
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Monster death prepared run={RunId} dungeon={DungeonId} room={RoomId} version={RoomVersion} actor={Actor} participants={Count} clearsMap={ClearsMap} boss={IsBossRoom} cycleQuest={CycleQuestId}",
                run.RunId, run.Layout.DungeonId, run.CurrentRoomMapId, run.RoomVersion, request.Actor,
                result.Count, clearsMap, run.IsBossRoom, run.Layout.Purpose.QuestId);
        return new(result, pending) { Tournament = tournament };
    }

    private sealed class DeathCommit(IDungeonRunDomainService runs, DungeonActorDeathClaim death, bool clearsMap)
        : ITransactionParticipant, ICommittedActorDeath
    {
        private readonly List<DungeonParticipantKillEffectClaim> effects = [];
        public Dictionary<CharacterId, ExperienceAward?> Awards { get; } = [];
        public DungeonActorDeathChange? Change { get; private set; }
        public bool CommitCanFail => false;
        public void Add(DungeonParticipantKillEffectClaim effect) => effects.Add(effect);
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            var current = runs.Find(death.CharacterId);
            if (current is null || current.RunId != death.RunId || current.RoomVersion != death.Run.RoomVersion
                || current.CurrentCell != death.Actor.Cell || !runs.IsActorDeathClaimCurrent(death)
                || effects.Any(x => !runs.IsParticipantKillEffectClaimCurrent(x))
                || runs.WillClearMap(death) != clearsMap)
                throw new InvalidOperationException("Dungeon death reservation changed before commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            foreach (var effect in effects)
                if (!runs.CommitParticipantKillEffect(effect, Awards.GetValueOrDefault(effect.CharacterId)))
                    throw new InvalidOperationException("Prepared participant kill effect could not be published.");
            Change = runs.CommitActorDeathChange(death)
                ?? throw new InvalidOperationException("Prepared monster death could not be published.");
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct)
        {
            foreach (var effect in effects) runs.ReleaseParticipantKillEffect(effect);
            runs.ReleaseActorDeath(death);
            return ValueTask.CompletedTask;
        }
    }
}
