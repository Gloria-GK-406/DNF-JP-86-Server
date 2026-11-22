namespace Dfo.Server.Data.Raids;

public sealed record AntonRaidRules(
    int MinimumMembers,
    int MaximumMembers,
    TimeSpan PreparationTime,
    TimeSpan IntermissionTime,
    TimeSpan RejoinTime,
    int TicketItemId,
    int TicketCount,
    int RetryItemId,
    int RetryItemCount,
    IReadOnlyList<TimeSpan> PhaseLimits)
{
    public IReadOnlyList<AntonRaidPhaseRule> Phases { get; init; } = [];
    public IReadOnlyDictionary<string, uint> Symbols { get; init; } = new Dictionary<string, uint>();
    public IReadOnlyList<int> Hatcheries { get; init; } = [];
    public int OpenHatcheries { get; init; }
    public int ShieldChargeRate { get; init; }
    public int InfectedShieldChargeRate { get; init; }
    public IReadOnlyList<AntonRaidBuffRule> Buffs { get; init; } = [];
}
