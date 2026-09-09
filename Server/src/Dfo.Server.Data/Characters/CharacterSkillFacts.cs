namespace Dfo.Server.Data.Characters;

public sealed record CharacterLearnedSkill(int SkillId, int Level);
public sealed record CharacterLearnedSkillPage(byte PageIndex, IReadOnlyList<CharacterLearnedSkill> Skills);

// Pages retain slot/extra configuration for protocol projection; learned facts expose only ID/level.
public sealed record CharacterSkillFacts(
    CharacterCareer Career,
    int Level,
    IReadOnlyList<CharacterSkillPageState> Pages,
    CharacterSkillPointBasisState PointBasis)
{
    public IReadOnlyList<CharacterLearnedSkillPage> LearnedPages => Array.AsReadOnly(Pages.Select(page =>
        new CharacterLearnedSkillPage(page.PageIndex, Array.AsReadOnly(page.Skills.Select(skill =>
            new CharacterLearnedSkill(checked((int)skill.SkillId), skill.Level)).ToArray()))).ToArray());
}
