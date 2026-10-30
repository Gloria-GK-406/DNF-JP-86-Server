using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunStore
{
    internal sealed partial class SettlementService
    {
        public ValueTask<DungeonSettlementRules> SettlementRulesForAsync(Dfo.Server.Data.Accounts.AccountId account, CharacterId character,
            DungeonRunState run, CancellationToken token) => settlementRules.ForAsync(account, character, run, token);

        public DungeonClearChange? TryOpenClear(
            CharacterId characterId,
            DungeonRunId expectedRunId,
            long expectedRoomVersion)
        {
            GameplayWriteAssertion.Writing(nameof(SettlementService) + "." + nameof(TryOpenClear));
            return this.store.TryOpenClear(characterId, expectedRunId, expectedRoomVersion, this.clock.GetUtcNow());
        }

        public DungeonRunState? TrySettle(CharacterId characterId)
        {
            GameplayWriteAssertion.Writing(nameof(SettlementService) + "." + nameof(TrySettle));
            var claim = this.TryClaimSettlement(characterId);
            return claim is null ? null : this.CommitSettlement(claim);
        }

        public DungeonSettlementClaim? TryClaimSettlement(CharacterId characterId)
        {
            GameplayWriteAssertion.Writing(nameof(SettlementService) + "." + nameof(TryClaimSettlement));
            return this.TryClaimSettlementCore(characterId, expectedRunId: null);
        }

        public DungeonSettlementClaim? TryClaimSettlement(
            CharacterId characterId,
            DungeonRunId expectedRunId)
        {
            GameplayWriteAssertion.Writing(nameof(SettlementService) + "." + nameof(TryClaimSettlement));
            return this.TryClaimSettlementCore(characterId, expectedRunId);
        }

        private DungeonSettlementClaim? TryClaimSettlementCore(
            CharacterId characterId,
            DungeonRunId? expectedRunId)
        {
            lock (this.store.gate)
            {
                if (this.store.RunOf(characterId) is not { } aggregate
                    || !aggregate.Cells[aggregate.CurrentCell].ClearOpen)
                    return null;
                if (expectedRunId is { } runId && aggregate.RunId != runId)
                    return null;

                var progress = aggregate.ProgressFor(characterId);
                if (progress.IsSettled)
                    return null;

                if (aggregate.Claims.Settlements.ContainsKey(characterId))
                    return null;

                var token = this.store.NextClaim();
                aggregate.Claims.Settlements.Add(characterId, token);
                return new DungeonSettlementClaim(
                    characterId, aggregate.ViewFor(characterId), token, aggregate.RunId);
            }
        }

        public DungeonRunState? CommitSettlement(DungeonSettlementClaim claim)
        {
            GameplayWriteAssertion.Writing(nameof(SettlementService) + "." + nameof(CommitSettlement));
            ArgumentNullException.ThrowIfNull(claim);
            lock (this.store.gate)
            {
                if (!this.TryRemoveSettlementClaim(claim.RunId, claim.CharacterId, claim.Token)
                    || !this.store.runsById.TryGetValue(claim.RunId, out var aggregate)
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
            GameplayWriteAssertion.Writing(nameof(SettlementService) + "." + nameof(ReleaseSettlement));
            ArgumentNullException.ThrowIfNull(claim);
            lock (this.store.gate)
            {
                return this.TryRemoveSettlementClaim(claim.RunId, claim.CharacterId, claim.Token);
            }
        }

        private bool TryRemoveSettlementClaim(DungeonRunId runId, CharacterId characterId, long token) =>
            this.store.ClaimsOf(runId) is { } claims
            && claims.Settlements.TryGetValue(characterId, out var current)
            && current == token
            && claims.Settlements.Remove(characterId);
    }
}
