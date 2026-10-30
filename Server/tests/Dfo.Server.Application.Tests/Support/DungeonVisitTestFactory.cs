using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.Support;

internal static class DungeonVisitTestFactory
{
    public static DungeonVisitDomainService Create(IDungeonStructureRuleService layouts, IDungeonRunDomainService runs,
        IDungeonSelectionRuleService selection, IDungeonCellFactory cells, ICharacterDungeonDifficultyDomainService difficulties,
        IDungeonDifficultyRuleService difficultyRules, ICharacterFatigueDomainService fatigue, IIllusionTowerDomainService? tower = null,
        IDungeonEntryMaterialsDomainService? materials = null, ICharacterDomainService? characters = null,
        IDomainTransactionAccessor? transactions = null, IDungeonAdmissionDomainService? admission = null) =>
        new(layouts, runs, selection, cells, fatigue, admission ?? new DungeonAdmissionDomainService(difficulties,
            difficultyRules, fatigue, materials ?? TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonEntryMaterialsDomainService>(),
            characters ?? Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(new() { Progress = new(85, 0) }), tower ?? TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService>(),
            transactions: transactions ?? TestRequired.TransactionScope(), quotas: TestRequired.Absent<Dfo.Server.Abstractions.UsageQuotas.IUsageQuotaDomainService>(), events: TestRequired.Absent<Dfo.Server.Abstractions.DomainEvents.IEventService>(), sessions: TestRequired.Absent<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>(), onlineCharacters: TestRequired.Absent<Dfo.Server.Abstractions.Characters.IOnlineCharacterRuntimeDomainService>(), runs: TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonRunDomainService>(), admissionRules: TestRequired.AdmissionRulesFrom(layouts), participation: TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>(), couples: new Dfo.Server.Application.World.CoupleDungeonRule(TestRequired.Absent<Dfo.Server.Application.Marriage.IMarriageRelations>()), quests: TestRequired.Absent<Dfo.Server.Abstractions.Quests.ICharacterQuestLogDomainService>()), tower ?? TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService>(), logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.World.DungeonVisitDomainService>.Instance);
}
