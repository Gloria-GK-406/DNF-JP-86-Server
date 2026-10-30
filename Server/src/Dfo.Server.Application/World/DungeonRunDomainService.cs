using Microsoft.Extensions.Logging;
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
    private readonly ILogger<DungeonRunDomainService> logger;
    private readonly IDomainTransactionAccessor transactions;
    private readonly IPartyService parties;
    private readonly IDungeonParticipationDomainService participation;
    private readonly IWeaknessDomainService weakness;
    private readonly ICreatureRuntimeDomainService creatures;
    private readonly IDungeonLifecycleDomainService lifecycle;
    private readonly TimeProvider clock;
    private readonly IDungeonSettlementRuleService settlementRules;
    private readonly object gate = new();
    private readonly IDungeonRoomMechanismRuleService mechanismRules;
    private readonly ISealedTreasureRuleService sealedTreasureRules;
    private readonly IDungeonDeadlineWake deadlines;
    private readonly IGiantDragonRuleService giantDragons;
    private readonly ILostLandFeverRuleService lostLandFever;
    // A party holds at most one run. Who is in it is the party's answer, so there is no
    // character-to-run index to keep in step with party changes.
    private readonly Dictionary<PartyKey, DungeonRunAggregate> runsByParty = new();
    private readonly Dictionary<DungeonRunId, DungeonRunAggregate> runsById = new();
    private long nextClaim;

    public DungeonRunDomainService(
        IDomainTransactionAccessor transactions,
        IPartyService parties,
        ILostLandFeverRuleService lostLandFever,
        IWeaknessDomainService weakness,
        ICreatureRuntimeDomainService creatures,
        IDungeonLifecycleDomainService lifecycle, TimeProvider clock,
        IDungeonRoomMechanismRuleService mechanismRules, IDungeonDeadlineWake deadlines,
        ISealedTreasureRuleService sealedTreasureRules, IDungeonParticipationDomainService participation,
        ILogger<DungeonRunDomainService> logger, IDungeonSettlementRuleService settlementRules,
        IGiantDragonRuleService giantDragons)
    {
        this.settlementRules = settlementRules;
        this.logger = logger;
        this.transactions = transactions;
        this.participation = participation;
        this.parties = parties;
        this.weakness = weakness;
        this.creatures = creatures;
        this.lifecycle = lifecycle;
        this.clock = clock;
        this.mechanismRules = mechanismRules;
        this.deadlines = deadlines;
        this.sealedTreasureRules = sealedTreasureRules;
        this.giantDragons = giantDragons;
        this.lostLandFever = lostLandFever;
    }
}
