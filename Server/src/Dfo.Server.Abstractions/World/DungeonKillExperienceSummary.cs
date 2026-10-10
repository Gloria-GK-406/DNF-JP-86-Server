using System.Collections.Immutable;

namespace Dfo.Server.Abstractions.World;

/// <summary>One successfully granted kill reward, identified across cells and owned by one participant.</summary>
public sealed record DungeonKillExperienceEntry(
    RoomActorId Actor, int MonsterCode, MonsterRank Rank, bool IsNamedMonster, ExperienceBreakdown Experience);

/// <summary>Already granted kill EXP, never an additional settlement grant.</summary>
public sealed record DungeonKillExperienceSummary(
    uint Ordinary = 0, uint Champion = 0, uint SuperChampion = 0, uint Boss = 0,
    uint GrowthContract = 0)
{
    public ImmutableArray<DungeonKillExperienceEntry> Actors { get; init; } = [];

    public DungeonKillExperienceSummary Record(DungeonKillExperienceEntry entry) =>
        Add(entry.Rank, entry.Experience.Base) with
        {
            GrowthContract = checked(GrowthContract + entry.Experience.GrowthContract),
            Actors = Actors.Add(entry),
        };

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
