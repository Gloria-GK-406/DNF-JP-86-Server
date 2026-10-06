using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Server gameplay policy, not a reconstructed official probability algorithm.
internal static class JoustContestRules
{
    internal const int Version = 1;
    internal static JoustGameplayParameters DefaultParameters { get; } = new(1.15,1,.85,1.25,10,.4,1000);
    private const double NeutralProbability = 1d / 8;
    private const decimal MinimumOdds = 1.1m;
    private static readonly double ColdExponent = Math.Log(6d / 14) / Math.Log(.6);

    internal static double ConditionStrength(byte condition, JoustGameplayParameters? parameters = null) => condition switch
    {
        0 => (parameters ?? DefaultParameters).StrongMultiplier,
        1 => (parameters ?? DefaultParameters).NormalMultiplier,
        2 => (parameters ?? DefaultParameters).WeakMultiplier,
        _ => throw new InvalidDataException("未知的马战状态。")
    };

    internal static double AttackAdvantage(int left, int right, JoustGameplayParameters? parameters = null)
    {
        static int Position(int type) => type switch { 0 => 0, 1 => 1, 27 => 2, 28 => 3, _ => -1 };
        var a = Position(left); var b = Position(right);
        if (a < 0 || b < 0 || a == b) return 1;
        var advantage=(parameters ?? DefaultParameters).CounterMultiplier;
        return ((b - a + 4) % 4) switch { 1 => advantage, 3 => 1/advantage, _ => 1 };
    }

    internal static double MatchWinProbability(JoustKnight left, byte leftCondition, JoustKnight right, byte rightCondition,
        JoustGameplayParameters? parameters = null)
    {
        var a = ConditionStrength(leftCondition,parameters) * AttackAdvantage(left.AttackType, right.AttackType,parameters);
        var b = ConditionStrength(rightCondition,parameters);
        return a / (a + b);
    }

    internal static double[] ChampionshipProbabilities(IReadOnlyList<JoustKnight> knights, Func<int, int, double> matchup)
    {
        if (knights.Count != 8 || knights.Select(k => k.Index).Distinct().Count() != 8)
            throw new InvalidDataException("夺冠概率需要八位唯一骑士。");
        var reach = Enumerable.Repeat(1d, 8).ToArray();
        for (var groupSize = 2; groupSize <= 8; groupSize *= 2)
        {
            var next = new double[8]; var half = groupSize / 2;
            for (var i = 0; i < 8; i++)
            {
                var groupStart = i / groupSize * groupSize;
                var otherStart = i - groupStart < half ? groupStart + half : groupStart;
                for (var j = otherStart; j < otherStart + half; j++)
                    next[i] += reach[i] * reach[j] * matchup(i, j);
            }
            reach = next;
        }
        return reach;
    }

    internal static double[] PricingProbabilities(IReadOnlyList<double> historical, IReadOnlyList<double> current,
        JoustGameplayParameters? parameters = null)
    {
        if (historical.Count != 8 || current.Count != 8
            || historical.Concat(current).Any(p => !double.IsFinite(p) || p < 0 || p > 1)
            || Math.Abs(historical.Sum() - 1) > 1e-9 || Math.Abs(current.Sum() - 1) > 1e-9)
            throw new InvalidDataException("马战概率分布无效。");
        var weight = (parameters ?? DefaultParameters).ForecastWeight;
        for (var i = 0; i < 8; i++)
        {
            var delta = current[i] - NeutralProbability;
            if (delta > 0) weight = Math.Min(weight, (1 - historical[i]) / delta);
            if (delta < 0) weight = Math.Min(weight, historical[i] / -delta);
        }
        // Reduce the common weight instead of normalizing individual prices: a neutral
        // current prediction must leave that knight's historical baseline unchanged.
        return Enumerable.Range(0, 8).Select(i => Math.Clamp(historical[i] + weight * (current[i] - NeutralProbability), 0, 1)).ToArray();
    }

    internal static JoustKnightForecast[] Forecast(IReadOnlyList<JoustKnight> knights, IReadOnlyList<byte> conditions,
        IReadOnlyList<JoustKnightStatistics> statistics, JoustGameplayParameters? parameters = null)
    {
        if (conditions.Count != 8 || statistics.Any(s => s.Wins < 0 || s.Losses < 0))
            throw new InvalidDataException("马战预测输入无效。");
        var saved = statistics.ToDictionary(s => s.KnightIndex);
        var settings=parameters ?? DefaultParameters;
        var strengths = knights.Select(k => saved.TryGetValue(k.Index, out var s) ? (s.Wins + (double)settings.HistoryPrior) / (s.Losses + (double)settings.HistoryPrior) : 1d).ToArray();
        var historical = ChampionshipProbabilities(knights, (a, b) => strengths[a] / (strengths[a] + strengths[b]));
        var current = ChampionshipProbabilities(knights, (a, b) => MatchWinProbability(knights[a], conditions[a], knights[b], conditions[b],settings));
        var pricing = PricingProbabilities(historical, current,settings);
        return knights.Select((k, i) => new JoustKnightForecast(k.Index, conditions[i], historical[i], current[i], pricing[i])
            {CombatMultiplier=ConditionStrength(conditions[i],settings)}).ToArray();
    }

    internal static decimal InitialOdds(double probability)
    {
        if (!double.IsFinite(probability) || probability < 0 || probability > 1)
            throw new ArgumentOutOfRangeException(nameof(probability));
        return (decimal)(probability <= NeutralProbability
            ? 2 + 14 * Math.Pow(1 - probability / NeutralProbability, ColdExponent)
            : (double)MinimumOdds + (2 - (double)MinimumOdds) * (1 - probability) / (1 - NeutralProbability));
    }

    internal static decimal BettingOdds(double probability, long amount, JoustGameplayParameters? parameters = null)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var scale=(parameters ?? DefaultParameters).BettingScale;
        var value = MinimumOdds + (InitialOdds(probability) - MinimumOdds) * scale / (scale + (decimal)amount);
        return decimal.Round(Math.Clamp(value, MinimumOdds, 16m), 1, MidpointRounding.AwayFromZero);
    }
}
