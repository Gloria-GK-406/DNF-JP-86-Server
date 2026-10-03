using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Accounts;

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
    ValueTask<ItemReference> SourceAsync(SelectedSessionIdentity session, BusinessSlot slot, DateTimeOffset now, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<AccountPremiumEntryState>> ReadPremiumsAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);
    ValueTask<OpeningEffectsApplication> ApplyAsync(SelectedSessionIdentity session, IReadOnlyList<DomainEffect> effects,
        DateTimeOffset now, CancellationToken cancellationToken);
}
public sealed record OpeningEffectsApplication(OpeningInventoryReceipt Inventory, ApplyEffectsResult Applied);
