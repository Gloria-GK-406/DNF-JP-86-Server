using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonClearSettlementDomainService(
    IDungeonRunSettlementDomainService runs,
    ISelectedSessionDomainService sessions,
    ICharacterDomainService characters,
    ICardRewardDomainService cardRewards,
    ICharacterExperienceAwardDomainService experience,
    ICharacterDungeonDifficultyDomainService difficulties,
    IWeeklyAttendanceDomainService attendance,
    IQuestProgressDomainService quests,
    IDespairTowerDomainService despair,
    IAccountLuckyStarDomainService luckyStars,
    TimeProvider clock) : IDungeonClearSettlementDomainService
{
    public async ValueTask<ClearSettlementStart> BeginAsync(SelectedSessionIdentity session, DungeonRunState run, CancellationToken cancellationToken)
    {
        // Both story and ordinary runs settle from the run's verified clear fact. A party
        // member's personal quest progress is not a second completion gate.
        var rules = await runs.SettlementRulesForAsync(session.AccountId, session.CharacterId, run, cancellationToken).ConfigureAwait(false);
        if (!rules.CanComplete) return new(ClearSettlementStartKind.NotReady, rules);

        // Claimed, not merely looked up. The client says when a run is over, and since 2026-08-29
        // saying so pays experience -- so a repeated report would be a repeated payment. The same
        // guard a kill has, for the same reason, and the run is left in progress either way: what
        // ends it is the character walking out.
        if (!sessions.IsInWorld(session)) return new(ClearSettlementStartKind.NotInWorld, rules);
        var claim = runs.TryClaimSettlement(session.CharacterId, run.RunId);
        if (claim is null) return new(ClearSettlementStartKind.AlreadySettled, rules);
        try
        {
            return await FixAsync(session, claim, rules, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (!TransactionOutcomeUncertainException.Contains(failure))
        {
            runs.ReleaseSettlement(claim);
            throw;
        }
    }

    private async ValueTask<ClearSettlementStart> FixAsync(SelectedSessionIdentity session, DungeonSettlementClaim claim,
        DungeonSettlementRules rules, CancellationToken cancellationToken)
    {
        var run = claim.Run;
        // The cards are fixed here, and here is the only place they can be fixed. Above is the
        // claim: a repeated result report is turned back before it reaches this line, so one run
        // draws one pair of faces however many times its result is reported. Below is every frame
        // this settlement sends, and the reward breakdown is one of them -- so a face fixed after
        // them would be reported as the zeroes it was before it existed.
        //
        // The level handed in is the one the character cleared at, taken before the award below
        // can move it: what band a clear's cards are drawn from is a fact about the run that was
        // just fought, not about the level the last of its experience happened to buy.
        var clearProgress = await characters.Get(session.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false);
        if (clearProgress is null)
        {
            runs.ReleaseSettlement(claim);
            return new(ClearSettlementStartKind.Abandoned, rules);
        }
        var settled = !run.Layout.Mode.Capabilities.DrawsClearCards
            ? new ClearRewardSettlementResult(new(new(ClearRewardCardKind.Free, [], 0),
                new(ClearRewardCardKind.Paid, [], 0), System.Collections.Immutable.ImmutableHashSet<ClearRewardCardId>.Empty, null), null)
            : await cardRewards.SettleAsync(
            new ClearRewardSettlement(
                session.CharacterId,
                clearProgress.Level,
                run.PartyMemberCount,
                run.RunId),
            cancellationToken).ConfigureAwait(false);
        if (settled is null)
        {
            runs.ReleaseSettlement(claim);
            return new(ClearSettlementStartKind.Abandoned, rules);
        }
        // Every client needs the same reward row indexed by party seat. Fixing the
        // other faces is idempotent and does not grant their experience or inventory.
        List<ClearSettlementMemberCards>? memberCards = null;
        if (run.Participants.Count > 1)
        {
            memberCards = [];
            foreach (var participant in run.Participants.OrderBy(value => value.Slot))
            {
                if (runs.Find(participant.CharacterId)?.RunId != run.RunId) continue;
                var participantProgress = await characters.Get(participant.CharacterId)
                    .ProgressAsync(cancellationToken).ConfigureAwait(false);
                if (participantProgress is null) continue;
                var cards = participant.CharacterId == session.CharacterId ? settled
                    : await cardRewards.SettleAsync(new ClearRewardSettlement(
                        participant.CharacterId, participantProgress.Level, run.PartyMemberCount,
                        run.RunId), cancellationToken).ConfigureAwait(false);
                if (cards is not null) memberCards.Add(new(participant.Slot, cards));
            }
        }

        // Asked before anything is paid, and the same refusal a kill makes. The experience frame
        // carries the character's remaining skill points as well as the total, so without them
        // there is no frame to report the new standing on -- and paying anyway would leave the
        // character credited, the run spent, and the client still drawing the total it entered
        // with.
        //
        // Asked of the character rather than read off the session, for the reason a kill asks:
        // the session's copy is entry's projection and nothing recomputes it, so a character who
        // levelled during the run would be told the figure they walked in with.
        //
        // The settlement claim remains retryable until the award commits. Missing this mirror
        // therefore leaves the run unpaid rather than spending its one settlement attempt.
        var mirror = await characters.Get(session.CharacterId).RemainingSkillPointsAsync(cancellationToken).ConfigureAwait(false);
        var attempt = new ClearSettlementAttempt(claim, clearProgress.Level, settled, memberCards, mirror);
        if (mirror is null)
        {
            runs.ReleaseSettlement(claim);
            return new(ClearSettlementStartKind.Unpayable, rules, attempt);
        }
        return new(ClearSettlementStartKind.Claimed, rules, attempt);
    }

    public void Finish(ClearSettlementAttempt attempt)
    {
        if (runs.CommitSettlement(attempt.Claim) is null)
        {
            throw new InvalidOperationException(
                "The dungeon settlement claim was no longer current after its durable award.");
        }
    }

    public void Abandon(ClearSettlementAttempt attempt) => runs.ReleaseSettlement(attempt.Claim);

    public async ValueTask<ClearExperienceResult> GrantAsync(AccountId accountId, CharacterId characterId, int dungeonId, ClearCircumstances clear,
        SelectedSessionIdentity? session, Guid runId, int settlementLevel, DateTimeOffset? settledAt, IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        var now = settledAt ?? clock.GetUtcNow();
        DespairTowerSettlement? tower = null;
        if (runs.Find(characterId) is { Layout.Mode: DungeonRunMode.Despair } towerRun)
        {
            tower = await (despair ?? throw new InvalidOperationException("Despair settlement is unavailable."))
                .SettleAsync(accountId, characterId, new(runId), cancellationToken).ConfigureAwait(false);
            if (tower is null) return new(null, 0, 0);
        }
        if (runs.Find(characterId) is { Layout.Mode: DungeonRunMode.Mirror } mirror
            && mirror.RunId.Value == runId && mirror.ClearOpen)
            await quests.ReportAsync(accountId, characterId,
                [new QuestDungeonCleared(dungeonId, mirror.Difficulty, mirror.RunId, mirror.ClearedAt ?? now)], cancellationToken).ConfigureAwait(false);
        if (runId != Guid.Empty)
            await luckyStars.GrantForClearAsync(accountId, characterId, new(runId), cancellationToken).ConfigureAwait(false);
        var award = await experience.AwardAsync(new ClearExperienceContext(
                new ExperienceOccasion(ExperienceEvent.DungeonClear, accountId,
                    characterId, dungeonId), clear), now, session,
            footprint, cancellationToken).ConfigureAwait(false);
        var changed = award is not null && runId != Guid.Empty
            && await attendance.RecordClearAsync(accountId,runId,dungeonId,settlementLevel,now,cancellationToken);
        var unlocked = runs.Find(characterId)?.Layout.Mode.Capabilities.UnlocksDifficulty is false ? null
            : await difficulties.UnlockAfterClearAsync(characterId, dungeonId,
            (DungeonDifficulty)clear.Difficulty, cancellationToken).ConfigureAwait(false);
        return new(award, award?.Breakdown.Base ?? 0, award?.Breakdown.ClearRank ?? 0,changed,unlocked) { Despair = tower };
    }
}
