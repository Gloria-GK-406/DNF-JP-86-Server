using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using System.Collections.Immutable;

namespace Dfo.Server.Abstractions.Inventory;

public interface IMagicBoxOpeningDomainService
{
    ValueTask<MagicBoxOpeningResult> OpenAsync(SelectedSessionIdentity session, BusinessMagicBoxOpeningInput input,
        DateTimeOffset now, CancellationToken cancellationToken);
}
public interface IPackageOpeningDomainService
{
    ValueTask<PackageOpeningResult> OpenAsync(SelectedSessionIdentity session, BusinessPackageOpeningInput input,
        DateTimeOffset now, CancellationToken cancellationToken);
}
public interface IAvatarPackageOpeningDomainService
{
    ValueTask<PackageOpeningResult> OpenAsync(SelectedSessionIdentity session, BusinessAvatarPackageOpeningInput input,
        DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Shared ownership checks and inventory settlement, with no package-family decisions.</summary>
public interface IOpeningInventoryDomainService
{
    ValueTask<ContainerSlotItem> SourceAsync(SelectedSessionIdentity session, BusinessSlot slot, DateTimeOffset now, CancellationToken cancellationToken);
    ValueTask<OpeningInventoryReceipt> SettleAsync(SelectedSessionIdentity session,
        IReadOnlyList<OpeningSlotConsumption> consumed, ImmutableArray<OpeningPreparedGrant> grants,
        DateTimeOffset now, CancellationToken cancellationToken);
}
