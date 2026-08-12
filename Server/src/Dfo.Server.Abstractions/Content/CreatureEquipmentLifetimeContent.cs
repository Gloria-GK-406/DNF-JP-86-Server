namespace Dfo.Server.Abstractions.Content;

/// <summary>Both legacy PVF lifetime probes for one creature equipment definition.</summary>
public readonly record struct CreatureEquipmentLifetime(
    uint DirectRemainingSeconds,
    uint DetailFallbackRemainingSeconds);

/// <summary>PVF lifetime inputs for one equipped creature item, resolved at one preparation time.</summary>
public interface ICreatureEquipmentLifetimeSource
{
    ValueTask<CreatureEquipmentLifetime> GetAsync(
        int itemTemplateId,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken);
}
