using Dfo.Server.Abstractions.Characters;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Characters;

internal sealed class WeaknessRuleService : IWeaknessRuleService
{
    // A21 runtime table, levels 1..99. Source and instruction evidence:
    // Document/weakness-recovery-price-rule.md. Values are prices at 10% stamina.
    private static readonly int[] RecoveryBasePrices =
    [
        0,0,0,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0,0,0,5000,
        5210,5420,5630,5840,6050,6260,6470,6680,6890,7100,
        7310,7680,8065,8465,8880,9310,9760,10230,10720,11230,
        11760,12310,12880,13470,14080,14710,15370,16060,16780,17530,
        18310,19120,19960,20830,21730,22670,23650,24670,25730,26830,
        27970,29150,30370,31630,32930,34270,35650,37070,38530,40030,
        41480,42880,44230,45530,46780,47980,49130,50230,51280,52280,
        53230,54130,54980,55780,56530,57230,57880,58480,59030,59530,
        59980,60380,60730,61030,61280,61480,61630,61730,61780,
    ];

    public int RecoveryGoldCost(byte level, byte value)
    {
        if (level == 0 || value > 100) throw new ArgumentOutOfRangeException();
        return RecoveryBasePrices[Math.Min((int)level, RecoveryBasePrices.Length) - 1]
            * (100 - value) / 90;
    }

    private readonly int minimumLevel;
    private readonly byte initialValue;
    private readonly int increment;

    public WeaknessRuleService(IConfiguration configuration)
    {
        // These are the selected server rules, not a claim about original A21 timing.
        minimumLevel = configuration.GetValue("Weakness:MinimumLevel", 20);
        var initial = configuration.GetValue("Weakness:InitialValue", 10);
        increment = configuration.GetValue("Weakness:RecoveryIncrement", 9);
        var seconds = configuration.GetValue("Weakness:RecoveryIntervalSeconds", 60);
        if (minimumLevel is < 1 or > 255 || initial is < 0 or > 100
            || increment is < 1 or > 100 || seconds is < 1 or > 86400)
            throw new InvalidOperationException("Invalid Weakness configuration.");
        initialValue = (byte)initial;
        RecoveryInterval = TimeSpan.FromSeconds(seconds);
    }

    public byte? InitialValue(byte level) => level >= minimumLevel ? initialValue : null;
    public byte RecoverStep(byte value) => (byte)Math.Min(100, value + increment);
    public TimeSpan RecoveryInterval { get; }
}
