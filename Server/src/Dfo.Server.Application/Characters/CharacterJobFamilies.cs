using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Maps a character's job number onto the job family the content names.
/// </summary>
/// <remarks>
/// Reverse-engineered from the legacy quest index, which resolves a job number to one of seven
/// base families and separately decides whether the awakened spelling applies. Two pairs collapse:
/// job 8 and job 10 are both the awakened mage, and job 9 and job 11 are both the awakened
/// swordman.
/// <para>
/// <see cref="QuestJobRequirement.DemonicSwordman"/> is deliberately unreachable here. The content
/// declares it, but no job number maps to it in the legacy table, so no character currently
/// matches content restricted to it. Inventing a mapping would be a behaviour change dressed as a
/// port, so the gap is left visible instead.
/// </para>
/// <para>
/// Shared because the archive spells a job the same way wherever it names one -- a quest's target
/// restriction and an item's <c>[usable job]</c> draw on the same fourteen families. A second copy
/// of this table would be a second thing to get right the next time a family is added, and the
/// reference server's inventory module shows what that costs.
/// </para>
/// </remarks>
internal static class CharacterJobFamilies
{
    public static QuestJobRequirement Resolve(byte job) => job switch
    {
        0 => QuestJobRequirement.Swordman,
        1 => QuestJobRequirement.Fighter,
        2 => QuestJobRequirement.Gunner,
        3 => QuestJobRequirement.Mage,
        4 => QuestJobRequirement.Priest,
        5 => QuestJobRequirement.AwakenedGunner,
        6 => QuestJobRequirement.Thief,
        7 => QuestJobRequirement.AwakenedFighter,
        8 or 10 => QuestJobRequirement.AwakenedMage,
        9 or 11 => QuestJobRequirement.AwakenedSwordman,
        12 => QuestJobRequirement.Knight,
        _ => QuestJobRequirement.Unrecognized,
    };
}
