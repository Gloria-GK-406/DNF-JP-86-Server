namespace Dfo.Server.Abstractions.World;

/// <summary>Earned amounts by business source. Zero means that source did not contribute.
/// This record contains no level cap or protocol offsets. Total cannot disagree with the fields.</summary>
public sealed record ExperienceBreakdown(
    uint Base = 0,
    uint Channel = 0,
    uint GrowthContract = 0,
    uint SpiritPotion = 0,
    uint FatigueBurning = 0,
    uint Party = 0,
    uint ClearRank = 0,
    uint FatigueBuff = 0,
    uint InternetCafe = 0,
    uint RapidGrowthBuff = 0,
    uint SeriaWelcome = 0,
    uint Guild = 0,
    uint Other = 0)
{
    // Fail explicitly rather than returning a total that contradicts the reward breakdown.
    public uint Total => checked(Base + Channel + GrowthContract + SpiritPotion + FatigueBurning
        + Party + ClearRank + FatigueBuff + InternetCafe + RapidGrowthBuff + SeriaWelcome + Guild + Other);

    public ExperienceBreakdown Add(ExperienceBonusKind kind, uint amount) => kind switch
    {
        ExperienceBonusKind.SeriaBlessing => this with { Channel = checked(Channel + amount) },
        ExperienceBonusKind.GrowthContract => this with { GrowthContract = checked(GrowthContract + amount) },
        ExperienceBonusKind.FatigueBurn => this with { FatigueBurning = checked(FatigueBurning + amount) },
        ExperienceBonusKind.PartyReward => this with { Party = checked(Party + amount) },
        ExperienceBonusKind.FatigueBuff => this with { FatigueBuff = checked(FatigueBuff + amount) },
        ExperienceBonusKind.InternetCafe => this with { InternetCafe = checked(InternetCafe + amount) },
        ExperienceBonusKind.RapidGrowthBuff => this with { RapidGrowthBuff = checked(RapidGrowthBuff + amount) },
        ExperienceBonusKind.SeriaWelcome => this with { SeriaWelcome = checked(SeriaWelcome + amount) },
        ExperienceBonusKind.GuildReward => this with { Guild = checked(Guild + amount) },
        ExperienceBonusKind.Unlisted => this with { Other = checked(Other + amount) },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
