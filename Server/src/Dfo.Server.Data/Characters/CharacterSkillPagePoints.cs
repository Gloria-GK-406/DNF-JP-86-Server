namespace Dfo.Server.Data.Characters;

public enum SkillGroup
{
    Pve,
    Duel,
}

public sealed record CharacterSkillPagePoints(
    CharacterSkillPointBudget Total,
    CharacterSkillPointBudget Remaining,
    IReadOnlyList<int> MissingSkillIds);
