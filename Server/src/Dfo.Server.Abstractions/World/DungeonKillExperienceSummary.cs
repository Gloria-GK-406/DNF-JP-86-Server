namespace Dfo.Server.Abstractions.World;

/// <summary>Already granted kill EXP, never an additional settlement grant.</summary>
public sealed record DungeonKillExperienceSummary(
    uint Ordinary = 0, uint Champion = 0, uint SuperChampion = 0, uint Boss = 0,
    uint GrowthContract = 0)
{
    public uint SpecialTotal => checked(Champion + SuperChampion + Boss);
    public DungeonKillExperienceSummary Add(MonsterRank rank, uint amount) => rank switch
    {
        MonsterRank.Ordinary => this with { Ordinary = checked(Ordinary + amount) },
        MonsterRank.Champion => this with { Champion = checked(Champion + amount) },
        MonsterRank.SuperChampion => this with { SuperChampion = checked(SuperChampion + amount) },
        MonsterRank.Boss => this with { Boss = checked(Boss + amount) },
        _ => throw new ArgumentOutOfRangeException(nameof(rank)),
    };
}
