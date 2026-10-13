using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

public sealed record TournamentCandidate(int Code, int Strength, byte Level, bool IsAiCharacter);
public sealed record TournamentResultCard(int ResultKey, int GoldWeight, int ItemWeight, int EmptyWeight);
public sealed record TournamentRewardItem(int ItemId, int Weight, int Count);
public sealed record TournamentDefinition(
    int DungeonId, int MapId, int MinimumLevel, int BasisLevel, byte Difficulty, int RoundFatigue,
    IReadOnlyList<DungeonRequiredItem> RequiredItems, IReadOnlyList<TournamentCandidate> Candidates,
    IReadOnlyList<uint> ExperienceByRound, IReadOnlyList<TournamentResultCard> ResultCards,
    IReadOnlyList<TournamentRewardItem> RewardItems, double GoldRate)
{
    public int CoinLimit { get; init; }
    public bool ConsumesFatigue { get; init; } = true;
}
public sealed record TournamentTeam(byte Position, int Code, int Strength);
public sealed record TournamentBracketRound(byte Number, IReadOnlyList<TournamentTeam> Teams);
public sealed record TournamentPathActor(ushort Sequence, int Code, byte Level, byte Type, bool IsAiCharacter);
public sealed record TournamentPlan(
    TournamentDefinition Definition, IReadOnlyList<TournamentBracketRound> Rounds,
    IReadOnlyList<TournamentPathActor> PathActors, uint Seed);
public sealed record TournamentRewardCard(int ItemId, int Count);

public interface ITournamentDungeonContentSource
{
    ValueTask<TournamentDefinition> GetAsync(int dungeonId, CancellationToken cancellationToken);
}

public interface ITournamentDungeonRuleService
{
    TimeSpan RewardSelectionWindow { get; }
    bool IsSupported(int dungeonId);
    ValueTask<TournamentPlan> PrepareAsync(int dungeonId, uint seed, CancellationToken cancellationToken);
    IReadOnlyList<IReadOnlyList<TournamentRewardCard>> Rewards(TournamentPlan plan, int completedRounds, int characterLevel);
}
