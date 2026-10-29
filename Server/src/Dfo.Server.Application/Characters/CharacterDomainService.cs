using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <remarks>
/// Built rather than looked up. A character holds no state, so there is nothing a registry could
/// keep for it and nothing for its absence to mean; what is made per call is one small object over
/// services held here.
/// </remarks>
internal sealed class CharacterDomainService(
    ICharacterCarriedGoldTable carriedGold,
    ICarriedGoldRuleService goldRules,
    ICharacterTable characters,
    ICharacterSkillPointDomainService skillPoints,
    ICharacterStatRuleService statRules,
    ICharacterSkillStateDomainService skillState,
    IExperienceRuleService experienceRules,
    IExperienceCapsuleDomainService capsules,
    IDomainTransactionAccessor transactions,
    IEventService events) : ICharacterDomainService
{
    public ICharacter Get(CharacterId characterId) => new PlayingCharacter(characterId, carriedGold, goldRules,
        characters, skillPoints, statRules, skillState, experienceRules, capsules, transactions, events);
}
