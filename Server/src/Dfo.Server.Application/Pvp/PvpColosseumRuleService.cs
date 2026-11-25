using System.Collections.Immutable;
using System.Security.Cryptography;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpColosseumRuleService(IPvpColosseumRuleSource definitions)
{
    public (int Item, int Cost, int MinimumRp) EntryTerms()
    {
        var policy = definitions.Load();
        return (policy.EntranceItem, policy.EntranceCost, policy.MinimumRp);
    }

    public bool CanResume(PvpColosseumRun current, CharacterId recipient) =>
        !current.Complete && current.Recipient == recipient;

    public bool CanStart(int entriesToday) => entriesToday < definitions.Load().DailyLimit;

    public PvpColosseumRun Advance(PvpColosseumRun run, bool won)
    {
        var policy = definitions.Load();
        var updated = won ? run with { Wins = run.Wins + 1 } : run with { Losses = run.Losses + 1 };
        return updated.Wins >= policy.MaxWins || updated.Losses >= policy.MaxLosses
            ? updated with { Prizes = DrawPrizes(updated.Wins, policy) }
            : updated;
    }

    private static ImmutableArray<PvpColosseumPrize> DrawPrizes(int wins, PvpColosseumRules policy) =>
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
