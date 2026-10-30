using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.World;

/// <summary>Keeps process-local dungeon runs as shared aggregates with participant views.</summary>
internal sealed partial class DungeonRunDomainService : IDungeonRunDomainService
{
    private readonly ILogger<DungeonRunDomainService> logger = NullLogger<DungeonRunDomainService>.Instance;
    private readonly IDomainTransactionAccessor? transactions;
    private readonly IPartyService parties;
    private readonly IDungeonParticipationDomainService? participation;
    private readonly IWeaknessDomainService? weakness;
    private readonly ICreatureRuntimeDomainService? creatures;
    private readonly IDungeonLifecycleDomainService? lifecycle;
    private readonly TimeProvider clock = TimeProvider.System;
    private readonly IDungeonSettlementRuleService settlementRules = new DungeonSettlementRuleService();
    private readonly object gate = new();
    private readonly IDungeonRoomMechanismRuleService mechanismRules = new DungeonRoomMechanismRuleService();
    private readonly ISealedTreasureRuleService sealedTreasureRules = new SealedTreasureRuleService();
    private readonly IDungeonDeadlineWake? deadlines;
    private readonly IGiantDragonRuleService? giantDragons;
    private readonly ILostLandFeverRuleService lostLandFever;
    private readonly Dictionary<DungeonRunId, DateTimeOffset> wipeDeadlines = [];
    private readonly Dictionary<DungeonRunId, DateTimeOffset> clearReturnDeadlines = [];
    // A party holds at most one run. Who is in it is the party's answer, so there is no
    // character-to-run index to keep in step with party changes.
    private readonly Dictionary<PartyKey, DungeonRunAggregate> runsByParty = new();
    private readonly Dictionary<DungeonRunId, DungeonRunAggregate> runsById = new();
    private readonly Dictionary<DeathClaimKey, long> deathClaims = new();
    private readonly Dictionary<ParticipantEffectClaimKey, long> participantEffectClaims = new();
    private readonly Dictionary<SettlementClaimKey, long> settlementClaims = new();
    private readonly Dictionary<GroundDropClaimKey, long> groundDropClaims = new();
    private readonly HashSet<GroundDropClaimKey> preparedGroundDropClaims = [];
    private readonly Dictionary<ClearRewardCardClaimKey, ClearRewardReservation>
        clearRewardCardClaims = new();
    private readonly HashSet<ClearRewardCardClaimKey> preparedClearRewardCardClaims = [];
    private long nextClaim;

    public DungeonRunDomainService(
        IDomainTransactionAccessor transactions,
        IPartyService parties,
        ILostLandFeverRuleService lostLandFever,
        IWeaknessDomainService? weakness = null,
        ICreatureRuntimeDomainService? creatures = null,
        IDungeonLifecycleDomainService? lifecycle = null, TimeProvider? clock = null,
        IDungeonRoomMechanismRuleService? mechanismRules = null, IDungeonDeadlineWake? deadlines = null,
        ISealedTreasureRuleService? sealedTreasureRules = null, IDungeonParticipationDomainService? participation = null,
        ILogger<DungeonRunDomainService>? logger = null, IDungeonSettlementRuleService? settlementRules = null,
        IGiantDragonRuleService? giantDragons = null)
    {
        this.settlementRules = settlementRules ?? new DungeonSettlementRuleService();
        this.logger = logger ?? NullLogger<DungeonRunDomainService>.Instance;
        this.transactions = transactions;
        this.participation = participation;
        this.parties = parties ?? throw new ArgumentNullException(nameof(parties));
        this.weakness = weakness;
        this.creatures = creatures;
        this.lifecycle = lifecycle;
        this.clock = clock ?? TimeProvider.System;
        this.mechanismRules = mechanismRules ?? new DungeonRoomMechanismRuleService();
        this.deadlines = deadlines;
        this.sealedTreasureRules = sealedTreasureRules ?? new SealedTreasureRuleService();
        this.giantDragons = giantDragons;
        this.lostLandFever = lostLandFever;
    }
}
