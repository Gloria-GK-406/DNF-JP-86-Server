using Dfo.Server.Abstractions.UsageQuotas;

namespace Dfo.Server.Abstractions.Effects;

public sealed record AddDungeonEntryAllowanceEffect(int DungeonId, int Increase, DateOnly Period,
    UsageQuotaValue? ExpectedBonus, int ExpectedUsed) : DomainEffect;
public sealed record DungeonEntryAllowanceChange(int DungeonId) : EffectChange;
