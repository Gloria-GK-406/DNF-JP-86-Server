using System.Collections.Immutable;
using System.Security.Cryptography;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpColosseumRuleService(IPvpColosseumRuleSource definitions)
{
    public PvpColosseumRules Load() => definitions.Load();

    public ImmutableArray<PvpColosseumPrize> DrawPrizes(int wins, PvpColosseumRules policy) =>
        [.. policy.Rewards[wins].GroupBy(option => option.Group).OrderBy(group => group.Key).Select(group =>
        {
            var position = RandomNumberGenerator.GetInt32(group.Sum(option => option.Weight));
            foreach (var option in group)
            {
                if (position < option.Weight) return option.Prize;
                position -= option.Weight;
            }
            throw new InvalidDataException("Empty Colosseum reward group.");
        })];
}
