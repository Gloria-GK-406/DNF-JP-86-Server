using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterSkillRule
{
    byte Job { get; }
    int SkillId { get; }
    string Name { get; }
    string Description { get; }
    IReadOnlyList<SkillPrerequisite> Prerequisites { get; }
    int? GetMaxLevel(CharacterCareer career, int characterLevel);
    bool CanLearn(CharacterCareer career, int characterLevel, int targetLevel,
        IReadOnlyDictionary<int, int>? learnedSkills = null);
    long GetSP(int skillLevel);
    long GetTP(int skillLevel);
    int GetFreeLevel(CharacterCareer career, int characterLevel);
}

public interface ICharacterSkillRuleService
{
    ICharacterSkillRule? Get(byte job, int skillId);
    IReadOnlyList<ICharacterSkillRule> GetAll(byte job);
    CharacterSkillPointBudget GetTotalPoints(byte job, int characterLevel);
    CharacterSkillPointLedger GetLedger(CharacterCareer career, int characterLevel,
        IReadOnlyList<CharacterSkillPageState> pages, CharacterSkillPointBasisState basis);
}
