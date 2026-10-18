using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Abstractions.Marriage;

public interface IMarriageRelationDomainStore
{
    ValueTask<int> PointsAsync(Guid relation, CharacterId character, CancellationToken ct);
    ValueTask<bool> ChangePointsAsync(Guid relation, CharacterId character, int amount, CancellationToken ct);
    ValueTask<bool> DailyPointAsync(Guid relation, CharacterId character, long period, CancellationToken ct);
    ValueTask<byte> RingAsync(Guid relation, CancellationToken ct);
    ValueTask<bool> UpgradeRingAsync(Guid relation, byte expected, CancellationToken ct);
    ValueTask<MarriageRelation?> FindActiveAsync(CharacterId character, CancellationToken ct);
    ValueTask<MarriageRelation?> ReadAsync(Guid relation, CancellationToken ct);
    ValueTask CreateAsync(Guid relation, MarriageStage stage, DateTimeOffset at, CancellationToken ct);
    ValueTask<bool> TransitionAsync(MarriageRelation expected, MarriageStage stage, Guid? ceremony,
        DateTimeOffset at, DateTimeOffset? marriedAt, CancellationToken ct);
    /// <summary>Engaged relations still holding a Reserved ceremony whose wedding is missing or cancelled.</summary>
    ValueTask<int> CountUnsettledReservationsAsync(CancellationToken ct);
}
