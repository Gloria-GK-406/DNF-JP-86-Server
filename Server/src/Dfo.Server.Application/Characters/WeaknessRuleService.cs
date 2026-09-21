using Dfo.Server.Abstractions.Characters;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Characters;

internal sealed class WeaknessRuleService : IWeaknessRuleService
{
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
