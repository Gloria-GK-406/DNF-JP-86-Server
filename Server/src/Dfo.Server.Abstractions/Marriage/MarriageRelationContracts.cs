using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Abstractions.Marriage;

public interface IMarriageRelationDomainStore
{
    ValueTask<MarriageRelation?> FindActiveAsync(CharacterId character, CancellationToken ct);
    ValueTask<MarriageRelation?> ReadAsync(Guid relation, CancellationToken ct);
    ValueTask CreateAsync(Guid relation, MarriageStage stage, DateTimeOffset at, CancellationToken ct);
    ValueTask<bool> TransitionAsync(MarriageRelation expected, MarriageStage stage, Guid? ceremony,
        DateTimeOffset at, DateTimeOffset? marriedAt, CancellationToken ct);
    /// <summary>Also writes the legacy wedding column, so an older binary still reads the same points.</summary>
    ValueTask<bool> AddLovePointsAsync(Guid relation, int amount, int maximum, CancellationToken ct);
    /// <summary>Engaged relations still holding a Reserved ceremony whose wedding is missing or cancelled.</summary>
    ValueTask<int> CountUnsettledReservationsAsync(CancellationToken ct);
}
