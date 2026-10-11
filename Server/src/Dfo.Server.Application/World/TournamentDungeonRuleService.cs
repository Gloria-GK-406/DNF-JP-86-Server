using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class TournamentDungeonRuleService(
    ITournamentDungeonContentSource content, IClearRewardRuleService clearRewards) : ITournamentDungeonRuleService
{
    public bool IsSupported(int dungeonId) => dungeonId == 120;

    public async ValueTask<TournamentPlan> PrepareAsync(int dungeonId, uint seed, CancellationToken cancellationToken)
    {
        if (!IsSupported(dungeonId)) throw new ArgumentOutOfRangeException(nameof(dungeonId));
        var definition = await content.GetAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        if (definition.Candidates.Count < 15 || definition.Candidates.Any(value => value.Code <= 0
            || value.Strength <= 0 || value.Level == 0)
            || definition.Candidates.Select(value => value.Code).Distinct().Count() != definition.Candidates.Count)
            throw new InvalidDataException($"Tournament {dungeonId}: invalid candidate pool.");
        var random = new DropRandom(seed ^ 0x544f55524eUL);
        var candidates = definition.Candidates.ToArray();
        for (var index = candidates.Length - 1; index > 0; index--)
        {
            var target = random.Next(index + 1) - 1;
            (candidates[index], candidates[target]) = (candidates[target], candidates[index]);
        }
        var playerPosition = random.Next(16) - 1;
        var first = new TournamentTeam[16];
        var candidateIndex = 0;
        for (byte position = 0; position < first.Length; position++)
        {
            var candidate = position == playerPosition ? null : candidates[candidateIndex++];
            first[position] = new(position, candidate?.Code ?? 0, candidate?.Strength ?? 0);
        }
        var rounds = new List<TournamentBracketRound>();
        var path = new List<TournamentPathActor>();
        var teams = first;
        for (byte round = 1; round <= 4; round++)
        {
            rounds.Add(new(round, Array.AsReadOnly(teams)));
            var playerIndex = Array.FindIndex(teams, team => team.Code == 0);
            var opponent = candidates.Single(candidate => candidate.Code == teams[playerIndex ^ 1].Code);
            path.Add(new(round, opponent.Code, opponent.Level,
                (byte)((opponent.IsAiCharacter ? 5 : 0) + (round == 4 ? 3 : 0)), opponent.IsAiCharacter));
            if (round == 4) break;
            var next = new TournamentTeam[teams.Length / 2];
            for (byte match = 0; match < next.Length; match++)
            {
                var left = teams[match * 2];
                var right = teams[match * 2 + 1];
                var weight = checked(left.Strength + right.Strength);
                var winner = left.Code == 0 ? left : right.Code == 0 ? right
                    : random.Next(weight) <= left.Strength ? left : right;
                next[match] = winner with { Position = match };
            }
            teams = next;
        }
        return new(definition, rounds.AsReadOnly(), path.AsReadOnly(), seed);
    }

    public IReadOnlyList<IReadOnlyList<TournamentRewardCard>> Rewards(
        TournamentPlan plan, int completedRounds, int characterLevel)
    {
        if (completedRounds is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(completedRounds));
        var definition = plan.Definition;
        var seed = plan.Seed ^ 0x524557415244UL ^ ((ulong)(uint)completedRounds << 32);
        var random = new DropRandom(seed);
        var goldFace = clearRewards.FaceFor(new(ClearRewardCardKind.Free, characterLevel, definition.BasisLevel,
            (DungeonDifficulty)definition.Difficulty, 1, 1), seed);
        var amount = Math.Floor(goldFace.Gold * definition.GoldRate);
        if (!double.IsFinite(amount) || amount < 0) throw new InvalidDataException("Invalid tournament gold reward.");
        var gold = amount >= int.MaxValue ? int.MaxValue : (int)amount;
        IReadOnlyList<TournamentRewardCard> Row(int resultKey)
        {
            var weights = definition.ResultCards.Single(value => value.ResultKey == resultKey);
            var total = checked(weights.GoldWeight + weights.ItemWeight + weights.EmptyWeight);
            if (total <= 0 || weights.GoldWeight < 0 || weights.ItemWeight < 0 || weights.EmptyWeight < 0)
                throw new InvalidDataException("Invalid tournament result weights.");
            var row = new TournamentRewardCard[2];
            for (var index = 0; index < row.Length; index++)
            {
                var roll = random.Next(total);
                if (roll <= weights.GoldWeight) row[index] = new(0, gold);
                else if (roll <= weights.GoldWeight + weights.ItemWeight)
                {
                    var itemTotal = checked(definition.RewardItems.Sum(value => value.Weight));
                    if (itemTotal <= 0 || definition.RewardItems.Any(value => value.Weight <= 0 || value.ItemId <= 0 || value.Count <= 0))
                        throw new InvalidDataException("Invalid tournament item reward weights.");
                    var itemRoll = random.Next(itemTotal);
                    var selected = definition.RewardItems.First(value => (itemRoll -= value.Weight) <= 0);
                    row[index] = new(selected.ItemId, selected.Count);
                }
                else row[index] = new(-1, 0);
            }
            return Array.AsReadOnly(row);
        }
        return Array.AsReadOnly<IReadOnlyList<TournamentRewardCard>>(
            [Row(completedRounds), completedRounds == 4 ? Row(5) : Array.Empty<TournamentRewardCard>()]);
    }
}
