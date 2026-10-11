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
        new(layouts, runs, selection, cells, fatigue, admission ?? new DungeonAdmissionDomainService(layouts, difficulties,
            difficultyRules, fatigue, materials, characters, tower, transactions: transactions), tower);
}
