using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Tests.Support;

/// <summary>
/// A hand-written dungeon run service for handler tests: every member answers as the interface's
/// former default implementations did, so a test overrides only what it exercises.
/// </summary>
/// <remarks>
/// The production interfaces carry no default implementations. These doubles are injected into
/// drivers and fixtures that take the aggregate, often feeding several narrow consumers at once,
/// so they keep the aggregate shape here. TryMove(character, target), TrySettle, End(character)
/// and the four-argument FixClearRewardCards are no longer interface members; they stay because
/// the former defaults reach them.
/// </remarks>
internal abstract class DungeonRunServiceStub : IDungeonRunDomainService
{
    public abstract DungeonRunState? Find(CharacterId characterId);

    public virtual DungeonRunState? Find(DungeonRunId runId) => null;

    public virtual DungeonRunState? TryMoveByLostLandItem(CharacterId character, DungeonRunId run, long version,
        MazeCell target, DungeonCellSeed? preparedCell) => throw new NotSupportedException();

    public virtual DungeonEntryChange PreparePartyEntry(
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        DungeonDifficulty difficulty,
        DungeonCellSeed initialCell,
        DungeonRunId runId,
        DateTimeOffset startedAt) => throw new NotSupportedException(
            "This dungeon-run service does not implement complete Cell publication.");

    public virtual DungeonEntryChange? TryMarkParticipantReady(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion) => null;

    public virtual DungeonEntryChange? FindEntryChange(CharacterId characterId)
    {
        var current = Find(characterId);
        return current is null
            ? null
            : new DungeonEntryChange(
                DungeonEntryChangeKind.Snapshot,
                current,
                current.EntryRevision,
                current.Participants.OrderBy(value => value.Slot)
                    .Select(value => value.CharacterId).ToArray());
    }

    public virtual bool TryAbortEntry(
        DungeonRunId expectedRunId,
        long expectedEntryRevision,
        IReadOnlyList<CharacterId> expectedRecipients) => false;

    public virtual DungeonRunState? TryMove(CharacterId characterId, MazeCell target) =>
        throw new NotSupportedException("TryMove is not part of this stub.");

    public virtual DungeonRunState? TryMove(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        MazeCell target)
    {
        var current = Find(characterId);
        return current is not null
            && current.RunId == expectedRunId
            && current.RoomVersion == expectedRoomVersion
                ? TryMove(characterId, target)
                : null;
    }

    public virtual DungeonRunState? TryMove(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        MazeCell target,
        DungeonCellSeed? preparedCell) =>
        TryMove(characterId, expectedRunId, expectedRoomVersion, target);

    public virtual bool StageDynamicCell(DungeonRunId runId, long roomVersion, long cellRevision,
        IReadOnlyList<DungeonCellActorState> actors, bool mechanismBlocksClear) => false;

    public virtual DungeonObjectDestructionClaim? ClaimObjectDestruction(
        CharacterId characterId, DungeonRunId runId, long roomVersion, int objectCode) => null;

    public virtual bool IsActorDeathClaimCurrent(DungeonActorDeathClaim claim) => Find(claim.CharacterId)?.RunId == claim.RunId;

    public virtual bool WillClearMap(DungeonActorDeathClaim claim) => false;

    public virtual bool IsParticipantKillEffectClaimCurrent(DungeonParticipantKillEffectClaim claim) => IsActorDeathClaimCurrent(claim.Death);

    public virtual DungeonActorDeathClaim? TryClaimActorDeath(
        CharacterId characterId,
        ushort actorSequence) => null;

    public virtual DungeonRunState? CommitActorDeath(DungeonActorDeathClaim claim) => claim.Run;

    public virtual DungeonActorDeathChange? CommitActorDeathChange(DungeonActorDeathClaim claim)
    {
        var run = CommitActorDeath(claim);
        if (run is null)
            return null;
        var added = run.CurrentCellState.GroundItems
            .Where(item => item.Source.Kind == DungeonGroundItemSourceKind.ActorDeath
                && item.Source.ActorSequence == claim.Actor.Sequence)
            .OrderBy(item => item.Number)
            .ToArray();
        return new DungeonActorDeathChange(
            run,
            claim.Actor,
            run.RuntimeRevision,
            run.CurrentCellState.Revision,
            added,
            run.CurrentCellState.IsCleared,
            run.Participants.OrderBy(participant => participant.Slot)
                .Select(participant => participant.CharacterId).ToArray());
    }

    public virtual bool ReleaseActorDeath(DungeonActorDeathClaim claim) => false;

    public virtual DungeonParticipantKillEffectClaim? TryClaimParticipantKillEffect(
        DungeonActorDeathClaim death,
        CharacterId characterId) =>
        death.Run.Participants.Count == 0
        || death.Run.Participants.Any(participant => participant.CharacterId == characterId)
            ? new DungeonParticipantKillEffectClaim(death, characterId, 0)
            : null;

    public virtual bool CommitParticipantKillEffect(DungeonParticipantKillEffectClaim claim) => true;

    public virtual bool CommitParticipantKillEffect(DungeonParticipantKillEffectClaim claim, ExperienceAward? award)
        => CommitParticipantKillEffect(claim);

    public virtual bool ReleaseParticipantKillEffect(DungeonParticipantKillEffectClaim claim) => false;

    public virtual bool HasUsedDungeonBuff(CharacterId characterId, DungeonRunId runId) => true;

    public virtual bool StageDungeonBuff(CharacterId characterId, DungeonRunId runId) => false;

    public virtual bool StageRevival(CharacterId payer, CharacterId target, DungeonRunId runId) => false;

    public virtual DungeonRunState? TryMarkParticipantDead(
        CharacterId characterId, DungeonRunId expectedRunId, long expectedRoomVersion) => null;

    public virtual DungeonGroundDropClaim? TryClaimGroundDrop(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        ushort groundDropNumber) => null;

    public virtual DungeonPickupChange? CommitGroundDrop(
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind) =>
        new(
            claim.RunId,
            0,
            claim.CharacterId,
            claim.Drop,
            kind,
            claim.Run.Participants.Select(value => value.CharacterId).ToArray());

    public virtual DungeonPickupCommit StageGroundDrop(
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind) =>
        DungeonPickupCommit.Completed(CommitGroundDrop(claim, kind));

    public virtual DungeonGroundItemsAddedChange? TryAddGroundItems(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        long expectedCellRevision,
        IReadOnlyList<DungeonGroundItemDraft> items) => null;

    public virtual bool ReleaseGroundDrop(DungeonGroundDropClaim claim) => false;

    public virtual ValueTask<DungeonSettlementRules> SettlementRulesForAsync(Dfo.Server.Data.Accounts.AccountId account, CharacterId character,
        DungeonRunState run, CancellationToken token) => throw new NotSupportedException("Settlement rules are unavailable.");

    public virtual DungeonClearChange? TryOpenClear(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion)
    {
        var run = Find(characterId);
        return run is null
            || run.RunId != expectedRunId
            || run.RoomVersion != expectedRoomVersion
            || run.ClearOpen
                ? null
                : new DungeonClearChange(
                    run.RunId,
                    run.RuntimeRevision + 1,
                    run.Participants.Select(value => value.CharacterId).ToArray());
    }

    public virtual DungeonRunState? TrySettle(CharacterId characterId) =>
        throw new NotSupportedException("TrySettle is not part of this stub.");

    public virtual DungeonSettlementClaim? TryClaimSettlement(CharacterId characterId) =>
        TrySettle(characterId) is { } run
            ? new DungeonSettlementClaim(characterId, run, 0, run.RunId)
            : null;

    public virtual DungeonSettlementClaim? TryClaimSettlement(
        CharacterId characterId,
        DungeonRunId expectedRunId)
    {
        var current = Find(characterId);
        return current is not null && current.RunId == expectedRunId
            ? TryClaimSettlement(characterId)
            : null;
    }

    public virtual DungeonRunState? CommitSettlement(DungeonSettlementClaim claim) => claim.Run;

    public virtual bool ReleaseSettlement(DungeonSettlementClaim claim) => false;

    public virtual DungeonCardLayoutChange? TryOpenCardLayout(
        CharacterId characterId,
        DungeonRunId expectedRunId) => null;

    public virtual DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost) =>
        throw new NotSupportedException("FixClearRewardCards is not part of this stub.");

    public virtual DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost,
        ClearRewardCardFace? blackDiamond = null)
    {
        if (blackDiamond is not null)
            throw new NotSupportedException("This run service cannot freeze black-diamond rewards.");
        var current = Find(characterId);
        return current is not null && current.RunId == expectedRunId
            ? FixClearRewardCards(characterId, free, paid, paidFlipCost)
            : null;
    }

    public virtual ClearRewardCardSlot? TryClaimClearRewardCard(
        CharacterId characterId, ClearRewardCardKind kind, int index) =>
        throw new NotSupportedException("TryClaimClearRewardCard is not part of this stub.");

    public virtual DungeonClearRewardCardClaim? TryReserveClearRewardCard(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        ClearRewardCardKind kind,
        int index)
    {
        var current = Find(characterId);
        var slot = current?.RunId == expectedRunId
            ? TryClaimClearRewardCard(characterId, kind, index)
            : null;
        return slot is null
            ? null
            : new DungeonClearRewardCardClaim(
                characterId,
                current!,
                new ClearRewardCardId(kind, index),
                slot,
                0,
                expectedRunId);
    }

    public virtual DungeonCardSelectionCommit CommitClearRewardCard(
        DungeonClearRewardCardClaim claim) => DungeonCardSelectionCommit.Completed(null);

    public virtual bool ReleaseClearRewardCard(DungeonClearRewardCardClaim claim) => false;

    public virtual IReadOnlyList<DungeonRoomMechanismView> AdvanceRoomMechanisms(DateTimeOffset now) => [];

    public virtual DateTimeOffset? NextLostLandCompletionAt() => null;
    public virtual DateTimeOffset? NextLostLandFeverExpiryAt() => null;
    public virtual IReadOnlyList<LostLandCompletion> FindDueLostLandCompletions(DateTimeOffset now) => [];

    public virtual bool StageLostLandCompletion(LostLandCompletion completion, DateTimeOffset now, bool notifyReady) => false;

    public virtual IReadOnlyList<LostLandFeverExpiry> FindExpiredLostLandFevers(DateTimeOffset now) => [];

    public virtual bool StageLostLandFeverExpiry(LostLandFeverExpiry expiry, DateTimeOffset now) => false;

    public virtual ValueTask<DungeonRunState?> TryEndPartyWipeAsync(DungeonRunId runId, DateTimeOffset now) =>
        ValueTask.FromResult(TryEndPartyWipe(runId, now));

    public virtual void ScheduleClearReturn(CharacterId characterId, DungeonRunId runId, DateTimeOffset now) { }

    public virtual IReadOnlyList<DungeonRunId> FindExpiredClearReturns(DateTimeOffset now) => [];

    public virtual DungeonRunState? TryEndClearReturn(DungeonRunId runId, DateTimeOffset now) => null;

    public virtual ValueTask<DungeonRunState?> TryEndClearReturnAsync(DungeonRunId runId, DateTimeOffset now) =>
        ValueTask.FromResult(TryEndClearReturn(runId, now));

    public virtual IReadOnlyList<DungeonRunId> FindExpiredPartyWipes(DateTimeOffset now) => [];

    public virtual DungeonRunState? TryEndPartyWipe(DungeonRunId runId, DateTimeOffset now) => null;

    public virtual ValueTask<bool> EndAsync(CharacterId characterId, DungeonRunId expectedRunId) =>
        ValueTask.FromResult(End(characterId, expectedRunId));

    public virtual ValueTask<IReadOnlyList<CharacterId>> EndRunAsync(DungeonRunId runId) =>
        throw new NotSupportedException("This dungeon-run service does not end whole runs.");

    public virtual bool End(CharacterId characterId) =>
        throw new NotSupportedException("End is not part of this stub.");

    public virtual bool End(CharacterId characterId, DungeonRunId expectedRunId) =>
        Find(characterId) is { } current
        && current.RunId == expectedRunId
        && End(characterId);
}
