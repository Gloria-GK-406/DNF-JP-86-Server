namespace Dfo.Server.Abstractions.Characters;

/// <summary>Owned by fatigue; only the unlimited-fatigue activity subscriber writes this setting.</summary>
public interface IFatigueConsumptionControlDomainService
{
    ValueTask<bool> IsSuspendedAsync(CancellationToken ct);
    ValueTask SetSuspendedAsync(bool suspended, CancellationToken ct);
}

public interface IFatigueConsumptionSettingsPersistence
{
    ValueTask<bool> IsSuspendedAsync(CancellationToken ct);
    ValueTask SetSuspendedAsync(bool suspended, CancellationToken ct);
}
