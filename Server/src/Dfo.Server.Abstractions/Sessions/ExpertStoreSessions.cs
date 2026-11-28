using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions;

public enum ExpertStoreKind : byte
{
    DisjointMachine = 0,
    EnchantShop = 3,
}

public sealed record ExpertStorePlacement(
    ExpertStoreKind Kind, ImmutableArray<byte> Name, int Cost, short X, short Y, short Direction);

public sealed record ExpertStoreSnapshot(
    long Instance, SelectedSessionIdentity Owner, TownAreaKey Area, ExpertStorePlacement Placement)
{
    public ExpertStoreKind Kind => Placement.Kind;
    public int Cost => Placement.Cost;
}

public sealed record ExpertStoreVisit(SelectedSessionIdentity Visitor, ExpertStoreSnapshot Store);

public sealed record ExpertStoreClosure(ExpertStoreSnapshot Store, IReadOnlyList<SelectedSessionIdentity> Visitors);

public sealed record ExpertStoreView(
    ExpertStoreSnapshot Store, int Grade, int Endurance, IReadOnlyList<byte> CardQualifications);

public enum ExpertStoreRejection
{
    None,
    InvalidRequest,
    InvalidState,
    UnavailablePoint,
    RestrictedCommercialZone,
}

public sealed record ExpertStoreResult(ExpertStoreRejection Rejection, ExpertStoreView? View = null);

/// <summary>Validates expert-store gameplay against current exact sessions, town and profession state.</summary>
public interface IExpertStoreGameplayDomainService
{
    ValueTask<ExpertStoreResult> OpenAsync(SelectedSessionIdentity owner, ExpertStorePlacement placement, CancellationToken ct);

    ValueTask<ExpertStoreResult> EnterAsync(SelectedSessionIdentity visitor, ushort ownerUserId, CancellationToken ct);

    ValueTask<IReadOnlyList<ExpertStoreView>> VisibleToAsync(SelectedSessionIdentity viewer, CancellationToken ct);

    ExpertStoreVisit? FindUse(SelectedSessionIdentity visitor, ushort ownerUserId, ExpertStoreKind kind);

    /// <summary>Rechecks the exact visit and returns current owner profession/device state inside the settlement.</summary>
    ValueTask<ExpertJobState?> ValidateUseAsync(ExpertStoreVisit visit, CancellationToken ct);
}
