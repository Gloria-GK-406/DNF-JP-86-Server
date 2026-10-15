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
    ISelectedSessionDomainService sessions, ISelectedSessionRuntimeDirectory published,
    ICharacterDomainService characters, IDungeonRunDomainService runs, IMonsterKillDomainService kills,
    IMonsterKillRewardsDomainService rewards, IQuestOfferRefreshDomainService offers,
    ILogger<MonsterDeathDomainService> logger, ITournamentDungeonDomainService? tournaments = null,
    IEventService? events = null, ISaoTowerDomainService? sao = null) : IMonsterDeathDomainService
{
    public async ValueTask<MonsterDeathSettlementResult> SettleAsync(MonsterDeathSettlementRequest request, CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Monster death requires one use-case transaction.");
        if (transaction.IsReadOnly) throw new InvalidOperationException("Monster death cannot run in a read-only transaction.");
        var run = runs.Find(request.Reporter.CharacterId);
        if (run is null || run.RunId != request.RunId || run.RoomVersion != request.RoomVersion
            || !request.Participants.Contains(request.Reporter)) return new([]);
        var isTournament = run.Layout.Tournament is not null;
        if (isTournament && !(tournaments ?? throw new InvalidOperationException("Tournament lifecycle is unavailable."))
            .CanKill(run, request.Actor)) return new([]);
        foreach (var session in request.Participants)
            if (sessions.FindByCharacter(session.CharacterId) != session || !published.IsPublishedExact(session)
                || runs.Find(session.CharacterId)?.RunId != run.RunId)
                throw new InvalidOperationException("A frozen dungeon participant changed before settlement.");
        var identification = await kills.IdentifyAsync(run, request.Actor, ct).ConfigureAwait(false);
        if (identification.Kill is not { } kill) return new([]);
        var pointLedgers = new Dictionary<CharacterId, Dfo.Server.Data.Characters.CharacterSkillProtocolMirrorState>();
        foreach (var session in request.Participants.DistinctBy(x => x.CharacterId))
        {
            var points = await characters.Get(session.CharacterId).RemainingSkillPointsAsync(ct).ConfigureAwait(false);
            if (points is null) return new([]);
            pointLedgers.Add(session.CharacterId, points);
        }
        var death = runs.TryClaimActorDeath(request.Reporter.CharacterId, request.Actor);
        if (death is null) return new([]);
        var clearsMap = runs.WillClearMap(death);
        var pending = new DeathCommit(runs, death, clearsMap);
        try { transaction.Enlist((runs, death.RunId, death.Actor, "death-settlement"), () => pending); }
        catch { runs.ReleaseActorDeath(death); throw; }
        var result = new List<MonsterDeathParticipantResult>();
        foreach (var session in request.Participants.DistinctBy(x => x.CharacterId))
        {
            var effect = runs.TryClaimParticipantKillEffect(death, session.CharacterId)
                ?? throw new InvalidOperationException("A participant's kill effect is already owned by another settlement.");
            pending.Add(effect);
            var character = characters.Get(session.CharacterId);
            var points = pointLedgers[session.CharacterId];
            var applied = await rewards.ApplyAsync(session, session.AccountId, session.CharacterId, kill,
                run.Layout.DungeonId, run.Difficulty, run.Layout.ExperienceWeight, run.PartyMemberCount,
                run.PartyKey, !isTournament && run.Layout.Altar is null && run.IsBossRoom && kill.IsMonster && kill.Rank == MonsterRank.Boss,
                run.RunId, death.Actor, run.Layout.Mirror?.WorldMapId, death.ActorState?.PendingLoot, ct).ConfigureAwait(false);
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
        var tournament = isTournament
            ? await tournaments!.AdvanceAsync(request.Reporter, run.RunId, request.Actor, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The accepted tournament death could not advance its round.")
            : null;
        if (clearsMap && run.Layout.Sao is not null && run.IsBossRoom)
            await (sao ?? throw new InvalidOperationException("SAO lifecycle is unavailable."))
                .CompleteClearAsync(death, request.Participants, ct).ConfigureAwait(false);
        if (clearsMap)
            await (events ?? throw new InvalidOperationException("Map-clear events are unavailable."))
                .EnqueueAsync(new DungeonMapCleared(run.RunId, run.Layout.DungeonId, run.Difficulty,
                    run.CurrentCell, run.CurrentRoomMapId, run.RoomVersion, death.OccurredAt!.Value,
                    [.. request.Participants.DistinctBy(x => x.CharacterId).Select(session =>
                        run.Participants.Single(x => x.CharacterId == session.CharacterId))]), ct).ConfigureAwait(false);
        logger.LogInformation("Monster death prepared run={RunId} actor={Actor} participants={Count}; one transaction, no entry inventory query",
            run.RunId, request.Actor, result.Count);
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
