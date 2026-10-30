using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

// Each run service: its dependencies, and the run lookup every narrow interface inherits from the store.
internal sealed partial class DungeonRunStore
{
    /// <summary>Entry, moves and the room transitions they commit.</summary>
    internal sealed partial class EntryService : IDungeonRunEntryDomainService
    {
        private readonly DungeonRunStore store;
        private readonly ILogger<EntryService> logger;
        private readonly IDomainTransactionAccessor transactions;
        private readonly IPartyService parties;
        private readonly IDungeonParticipationDomainService participation;
        private readonly IWeaknessDomainService weakness;
        private readonly ICreatureRuntimeDomainService creatures;
        private readonly TimeProvider clock;
        private readonly IDungeonRoomMechanismRuleService mechanismRules;
        private readonly ISealedTreasureRuleService sealedTreasureRules;
        private readonly IDungeonDeadlineWake deadlines;

        public EntryService(DungeonRunStore store, ILogger<EntryService> logger, IDomainTransactionAccessor transactions, IPartyService parties, IDungeonParticipationDomainService participation, IWeaknessDomainService weakness, ICreatureRuntimeDomainService creatures, TimeProvider clock, IDungeonRoomMechanismRuleService mechanismRules, ISealedTreasureRuleService sealedTreasureRules, IDungeonDeadlineWake deadlines)
        {
            this.store = store;
            this.logger = logger;
            this.transactions = transactions;
            this.parties = parties;
            this.participation = participation;
            this.weakness = weakness;
            this.creatures = creatures;
            this.clock = clock;
            this.mechanismRules = mechanismRules;
            this.sealedTreasureRules = sealedTreasureRules;
            this.deadlines = deadlines;
        }

        public DungeonRunState? Find(CharacterId characterId) => this.store.Find(characterId);
        public DungeonRunState? Find(DungeonRunId runId) => this.store.Find(runId);
    }

    /// <summary>Actor deaths, kill effects, object destruction and dynamic actors.</summary>
    internal sealed partial class CombatService : IDungeonRunCombatDomainService
    {
        private readonly DungeonRunStore store;
        private readonly ILogger<CombatService> logger;
        private readonly IDomainTransactionAccessor transactions;
        private readonly TimeProvider clock;
        private readonly IDungeonRoomMechanismRuleService mechanismRules;
        private readonly ISealedTreasureRuleService sealedTreasureRules;
        private readonly IDungeonDeadlineWake deadlines;
        private readonly IGiantDragonRuleService giantDragons;
        private readonly ILostLandFeverRuleService lostLandFever;

        public CombatService(DungeonRunStore store, ILogger<CombatService> logger, IDomainTransactionAccessor transactions, TimeProvider clock, IDungeonRoomMechanismRuleService mechanismRules, ISealedTreasureRuleService sealedTreasureRules, IDungeonDeadlineWake deadlines, IGiantDragonRuleService giantDragons, ILostLandFeverRuleService lostLandFever)
        {
            this.store = store;
            this.logger = logger;
            this.transactions = transactions;
            this.clock = clock;
            this.mechanismRules = mechanismRules;
            this.sealedTreasureRules = sealedTreasureRules;
            this.deadlines = deadlines;
            this.giantDragons = giantDragons;
            this.lostLandFever = lostLandFever;
        }

        public DungeonRunState? Find(CharacterId characterId) => this.store.Find(characterId);
        public DungeonRunState? Find(DungeonRunId runId) => this.store.Find(runId);
    }

    /// <summary>Participant death, revival and the dungeon buff.</summary>
    internal sealed partial class ParticipantService : IDungeonRunParticipantDomainService
    {
        private readonly DungeonRunStore store;
        private readonly IDomainTransactionAccessor transactions;

        public ParticipantService(DungeonRunStore store, IDomainTransactionAccessor transactions)
        {
            this.store = store;
            this.transactions = transactions;
        }

        public DungeonRunState? Find(CharacterId characterId) => this.store.Find(characterId);
        public DungeonRunState? Find(DungeonRunId runId) => this.store.Find(runId);
    }

    /// <summary>Ground items and their pickup claims.</summary>
    internal sealed partial class GroundDropService : IDungeonRunGroundDropDomainService
    {
        private readonly DungeonRunStore store;
        private readonly IDomainTransactionAccessor transactions;
        private readonly TimeProvider clock;
        private readonly ISealedTreasureRuleService sealedTreasureRules;

        public GroundDropService(DungeonRunStore store, IDomainTransactionAccessor transactions, TimeProvider clock, ISealedTreasureRuleService sealedTreasureRules)
        {
            this.store = store;
            this.transactions = transactions;
            this.clock = clock;
            this.sealedTreasureRules = sealedTreasureRules;
        }

        public DungeonRunState? Find(CharacterId characterId) => this.store.Find(characterId);
        public DungeonRunState? Find(DungeonRunId runId) => this.store.Find(runId);
    }

    /// <summary>Opening the clear and settling each participant.</summary>
    internal sealed partial class SettlementService : IDungeonRunSettlementDomainService
    {
        private readonly DungeonRunStore store;
        private readonly TimeProvider clock;
        private readonly IDungeonSettlementRuleService settlementRules;

        public SettlementService(DungeonRunStore store, TimeProvider clock, IDungeonSettlementRuleService settlementRules)
        {
            this.store = store;
            this.clock = clock;
            this.settlementRules = settlementRules;
        }

        public DungeonRunState? Find(CharacterId characterId) => this.store.Find(characterId);
        public DungeonRunState? Find(DungeonRunId runId) => this.store.Find(runId);
    }

    /// <summary>Clear reward cards: layout, reservation and selection.</summary>
    internal sealed partial class CardService : IDungeonRunCardDomainService
    {
        private readonly DungeonRunStore store;
        private readonly IDomainTransactionAccessor transactions;

        public CardService(DungeonRunStore store, IDomainTransactionAccessor transactions)
        {
            this.store = store;
            this.transactions = transactions;
        }

        public DungeonRunState? Find(CharacterId characterId) => this.store.Find(characterId);
        public DungeonRunState? Find(DungeonRunId runId) => this.store.Find(runId);
    }

    /// <summary>Party wipe and clear return deadlines, room mechanisms and Lost Land limits.</summary>
    internal sealed partial class TimerService : IDungeonRunTimerDomainService
    {
        private readonly DungeonRunStore store;
        private readonly IDomainTransactionAccessor transactions;
        private readonly IWeaknessDomainService weakness;
        private readonly IDungeonLifecycleDomainService lifecycle;
        private readonly IDungeonRoomMechanismRuleService mechanismRules;

        public TimerService(DungeonRunStore store, IDomainTransactionAccessor transactions, IWeaknessDomainService weakness, IDungeonLifecycleDomainService lifecycle, IDungeonRoomMechanismRuleService mechanismRules)
        {
            this.store = store;
            this.transactions = transactions;
            this.weakness = weakness;
            this.lifecycle = lifecycle;
            this.mechanismRules = mechanismRules;
        }

        public DungeonRunState? Find(CharacterId characterId) => this.store.Find(characterId);
        public DungeonRunState? Find(DungeonRunId runId) => this.store.Find(runId);
    }

    /// <summary>Leaving a run and ending it.</summary>
    internal sealed partial class DepartureService : IDungeonRunDepartureDomainService
    {
        private readonly DungeonRunStore store;
        private readonly IWeaknessDomainService weakness;
        private readonly IDungeonLifecycleDomainService lifecycle;

        public DepartureService(DungeonRunStore store, IWeaknessDomainService weakness, IDungeonLifecycleDomainService lifecycle)
        {
            this.store = store;
            this.weakness = weakness;
            this.lifecycle = lifecycle;
        }

        public DungeonRunState? Find(CharacterId characterId) => this.store.Find(characterId);
        public DungeonRunState? Find(DungeonRunId runId) => this.store.Find(runId);
    }
}
