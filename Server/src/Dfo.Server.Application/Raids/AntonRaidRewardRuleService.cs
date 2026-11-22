using Dfo.Server.Abstractions.Raids;

namespace Dfo.Server.Application.Raids;

internal sealed class AntonRaidRewardRuleService(IAntonRaidRewardContentSource source)
{
    internal static readonly TimeSpan PromptDelay = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MovieLimit = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan SelectionLimit = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan SquadDelay = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan DeliveryDelay = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DeliveryRetryDelay = TimeSpan.FromSeconds(2);

    internal AntonRaidRewardDefinition Current => source.Read();

    internal static byte Rank(AntonRaidPhaseRewardDefinition phase, int deaths)
    {
        foreach (var range in phase.Ranks)
            if (deaths <= range.Maximum)
                return range.Rank;

        return phase.Ranks[^1].Rank;
    }

    internal static (AntonRaidRewardItem Item, byte Flags) Draw(AntonRaidPhaseRewardDefinition phase,
        AntonRaidRewardKind kind, byte rank)
    {
        // Until the client mapping is confirmed, award higher nominal gold grades for fewer deaths.
        var goldState = rank switch { 0 => 3, 1 => 2, _ => -1 };
        var state = kind == AntonRaidRewardKind.Gold ? goldState : -1;
        var pools = phase.Pools.Where(pool => pool.Kind == kind && pool.State == state).ToArray();
        var pool = Weighted(pools, candidate => candidate.Weight);
        return (Weighted(pool.Choices, choice => choice.Weight).Item, pool.Flags);
    }

    private static T Weighted<T>(IReadOnlyList<T> choices, Func<T, int> weight)
    {
        var current = Random.Shared.NextInt64(choices.Sum(choice => (long)weight(choice)));
        foreach (var choice in choices)
        {
            current -= weight(choice);
            if (current < 0)
                return choice;
        }
        throw new InvalidDataException("Anton raid weighted choice did not resolve.");
    }
}
