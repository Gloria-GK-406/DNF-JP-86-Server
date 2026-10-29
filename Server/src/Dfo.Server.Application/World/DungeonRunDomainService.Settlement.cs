using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    public ValueTask<DungeonSettlementRules> SettlementRulesForAsync(Dfo.Server.Data.Accounts.AccountId account, CharacterId character,
        DungeonRunState run, CancellationToken token) => settlementRules.ForAsync(account, character, run, token);

    public DungeonClearChange? TryOpenClear(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryOpenClear));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.IsMember(characterId)
                || aggregate.Layout.Mode is DungeonRunMode.LostLand
                || aggregate.Cells[aggregate.CurrentCell].ClearOpen)
            {
                return null;
            }

            var cell = aggregate.Cells[aggregate.CurrentCell];
            cell.ClearOpen = true;
            aggregate.ClearedAt = this.clock.GetUtcNow();
            cell.Revision++;
            aggregate.RuntimeRevision++;
            return new DungeonClearChange(
                aggregate.RunId,
                aggregate.RuntimeRevision,
                [.. aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);
        }
    }

    public DungeonRunState? TrySettle(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TrySettle));
        var claim = this.TryClaimSettlement(characterId);
        return claim is null ? null : this.CommitSettlement(claim);
    }

    public DungeonSettlementClaim? TryClaimSettlement(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimSettlement));
        return this.TryClaimSettlementCore(characterId, expectedRunId: null);
    }

    public DungeonSettlementClaim? TryClaimSettlement(
        CharacterId characterId,
        DungeonRunId expectedRunId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimSettlement));
        return this.TryClaimSettlementCore(characterId, expectedRunId);
    }

    private DungeonSettlementClaim? TryClaimSettlementCore(
        CharacterId characterId,
        DungeonRunId? expectedRunId)
    {
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || !aggregate.Cells[aggregate.CurrentCell].ClearOpen)
                return null;
            if (expectedRunId is { } runId && aggregate.RunId != runId)
                return null;

            var progress = aggregate.ProgressFor(characterId);
            if (progress.IsSettled)
                return null;

            var key = new SettlementClaimKey(aggregate.RunId, characterId);
            if (this.settlementClaims.ContainsKey(key))
                return null;

            var token = Interlocked.Increment(ref this.nextClaim);
            this.settlementClaims.Add(key, token);
            return new DungeonSettlementClaim(
                characterId, aggregate.ViewFor(characterId), token, aggregate.RunId);
        }
    }

    public DungeonRunState? CommitSettlement(DungeonSettlementClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitSettlement));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new SettlementClaimKey(claim.RunId, claim.CharacterId);
            if (!this.TryRemoveSettlementClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || !aggregate.IsMember(claim.CharacterId))
            {
                return null;
            }

            var progress = aggregate.ProgressFor(claim.CharacterId);
            if (progress.IsSettled)
                return null;

            progress.IsSettled = true;
            return aggregate.ViewFor(claim.CharacterId);
        }
    }

    public bool ReleaseSettlement(DungeonSettlementClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ReleaseSettlement));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            return this.TryRemoveSettlementClaim(
                new SettlementClaimKey(claim.RunId, claim.CharacterId), claim.Token);
        }
    }

    private bool TryRemoveSettlementClaim(SettlementClaimKey key, long token) =>
        this.settlementClaims.TryGetValue(key, out var current)
        && current == token
        && this.settlementClaims.Remove(key);

    private void ClearSettlementClaims(DungeonRunId runId)
    {
        foreach (var key in this.settlementClaims.Keys
                     .Where(key => key.RunId == runId).ToArray())
        {
            this.settlementClaims.Remove(key);
        }
    }
    private readonly record struct SettlementClaimKey(DungeonRunId RunId, CharacterId CharacterId);
}
