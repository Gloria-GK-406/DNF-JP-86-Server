using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>Server-owned facts for a completed run; character level is supplied inside the award transaction.</summary>
public readonly record struct ClearCircumstances(
    int BasisLevel, DungeonDifficulty Difficulty, double DungeonWeight, byte ClientRankPoint,
    int KilledMonsterCount = 0, int PartyMemberCount = 1, int CharacterLevel = 0);
