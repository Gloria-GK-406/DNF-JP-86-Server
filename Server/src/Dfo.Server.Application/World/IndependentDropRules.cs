using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal static class IndependentDropRules
{
    public static int SuccessfulAttempts(int attempts, int cap, int probability, int basis, DropRandom random)
    {
        if (attempts < 0 || cap < 0 || probability < 0 || basis <= 0)
            throw new ArgumentOutOfRangeException(nameof(probability), "Unsupported independent-drop trial parameters.");
        var successes = 0;
        // Complete the declared trials before applying the cap, keeping draw consumption stable.
        for (var attempt = 0; attempt < attempts; attempt++)
            if (random.Hits(Math.Min(probability, basis), basis)) successes++;
        return Math.Min(successes, cap);
    }

    public static int Select(ImmutableArray<int> ends, int ticket)
    {
        if (ends.IsDefaultOrEmpty || ticket < 0 || ticket >= ends[^1])
            throw new ArgumentOutOfRangeException(nameof(ticket));
        var low = 0;
        var high = ends.Length - 1;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (ticket < ends[middle]) high = middle;
            else low = middle + 1;
        }
        return low;
    }
}
