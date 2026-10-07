using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>Random entry is deliberately disabled until its eligibility/probability rules exist.</summary>
public sealed class DungeonEntryDecisionDomainService : IDungeonEntryDecisionDomainService
{
    public DungeonEntryKind ResolveEntryKind(DungeonEntryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.RequestMode == 1 ? DungeonEntryKind.ManualHell : DungeonEntryKind.Normal;
    }
}
