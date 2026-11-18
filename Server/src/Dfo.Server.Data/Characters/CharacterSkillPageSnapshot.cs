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

public sealed record CharacterSkillPageSnapshot(
    CharacterSkillPageState Page,
    IReadOnlyList<CharacterSkillState> HotkeySkills,
    CharacterSkillPagePoints? Points);
