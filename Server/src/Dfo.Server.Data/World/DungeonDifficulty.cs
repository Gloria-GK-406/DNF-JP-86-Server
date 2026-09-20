namespace Dfo.Server.Data.World;

/// <summary>Ordered ordinary dungeon difficulty. Values are the A21 packet values.</summary>
public enum DungeonDifficulty : byte
{
    Normal = 0,
    Adventure = 1,
    // The remaining numeric tiers are confirmed; their displayed names are not yet verified.
    Tier2 = 2,
    Tier3 = 3,
    Tier4 = 4,
}
