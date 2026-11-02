using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

/// <remarks>
/// Reads the store on every generation instead of caching: a cached value could outlive a
/// rolled-back write, and one small query per generation costs little beside the draws themselves.
/// </remarks>
internal sealed class DropDrawMultiplierDomainService(
    IDropDrawMultiplierPersistence store,
    ILogger<DropDrawMultiplierDomainService> logger) : IDropDrawMultiplierDomainService
{
    public async ValueTask<DropDrawMultipliers> CurrentAsync(CancellationToken cancellationToken)
    {
        var rows = await store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<DropDrawEntry, int>(rows.Count);
        foreach (var (key, multiplier) in rows)
        {
            if (DropDrawEntries.TryParse(key, out var entry) && DropDrawMultipliers.IsValid(multiplier))
                values[entry] = multiplier;
            else
                logger.LogWarning("Ignoring drop draw multiplier row {Key}={Multiplier}", key, multiplier);
        }
        return new DropDrawMultipliers(values);
    }

    public async ValueTask SetAsync(DropDrawEntry entry, int multiplier, CancellationToken cancellationToken)
    {
        if (!DropDrawMultipliers.IsValid(multiplier))
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        await store.SaveAsync(DropDrawEntries.KeyOf(entry), multiplier, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Drop draw multiplier {Key} set to {Multiplier}", DropDrawEntries.KeyOf(entry), multiplier);
    }
}
