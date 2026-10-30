using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>What rank a monster is, in the numbering the client draws it by.</summary>
public enum MonsterRank : byte
{
    Ordinary = 0,
    Champion = 1,
    SuperChampion = 2,
    Boss = 3,
}

/// <summary>Everything about one kill that decides what it is worth.</summary>
/// <remarks>
/// Nothing about who made the kill beyond their level. What a character carries is added on top of
/// this and belongs to a different rule; anyone of the same level killing the same monster in the
/// same run earns the same amount here.
/// </remarks>
public readonly record struct KillCircumstances(
    int MonsterLevel,
    MonsterRank Rank,
    int CharacterLevel,
    DungeonDifficulty Difficulty,
    double DungeonWeight,
    int PartyMemberCount,
    bool IsNamedMonster = false);
