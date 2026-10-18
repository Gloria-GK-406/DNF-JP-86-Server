namespace Dfo.Server.Abstractions.World;

public enum AltarDungeonKind : byte
{
    Endless = 1,
    Ultimate = 2,
}

public sealed record AltarMonsterDefinition(
    int MonsterCode, int TemplateType, int X, int Y, int Z, int DurationMilliseconds,
    int SpawnIntervalMilliseconds, int BaseSpawnCount, int SpawnCountIncrement, int BatchCount);

public sealed record AltarPhaseDefinition(
    int Round, int MonsterTemplateIndex, int DelayMilliseconds, double Scale,
    int Flag, int ConcurrentPhaseCount, byte Difficulty);

public sealed record AltarRoundDefinition(int Number, IReadOnlyList<AltarPhaseDefinition> Phases);

public sealed record AltarMapDefinition(
    int MapId, IReadOnlyList<AltarMonsterDefinition> Monsters, IReadOnlyList<AltarRoundDefinition> Rounds);

public sealed record AltarUltimateRewardProbability(int Point, int Item1251Probability, int Item1252Probability);

public sealed record AltarRewardDefinition(
    int GoldCandidateWeight, int ItemCandidateWeight, int RewardWeightPerProgress,
    double GoldAmountWeight, IReadOnlyList<double> RewardExperienceWeights,
    IReadOnlyList<double> EntryCountExperienceWeights, IReadOnlyList<int> UltimateDifficultyPoints,
    IReadOnlyList<AltarUltimateRewardProbability> UltimateRewards, string ItemRewardTablePath);

public sealed record AltarDungeonDefinition(
    int DungeonId, AltarDungeonKind Kind, int MinimumLevel, int BasisLevel, int MaxRounds,
    int CoinLimit, IReadOnlyList<AltarMapDefinition> Maps, AltarRewardDefinition Rewards, int DailyEntries)
{
    public AltarAdmissionPricing? AdmissionPricing { get; init; }
}
public sealed record AltarPriceBand(int MinimumLevel, int MaximumLevel, int Gold);
public sealed record AltarAdmissionPricing(IReadOnlyList<AltarPriceBand> Prices,
    IReadOnlyDictionary<int, int> EntryIncreasePercent, int LotteryGold);

public interface IAltarDungeonContentSource
{
    ValueTask<AltarDungeonDefinition> GetAsync(int dungeonId, CancellationToken cancellationToken);
}
