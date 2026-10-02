using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public enum InventoryExpansionRejection { None, NoSession, InvalidRequest, InvalidStage, InsufficientAssets, GoldLimit, StateChanged }
public sealed record InventoryExpansionChange(InventoryExpansionKind Kind, int ExpectedParameter, int Parameter);
public sealed record InventoryExpansionReceipt(SelectedSessionIdentity Session, CharacterInventoryEntryState Character,
    AccountInventoryEntryState Account, DateTimeOffset At);
public sealed record InventoryExpansionResult(InventoryExpansionRejection Rejection, InventoryExpansionReceipt? Receipt = null);
public sealed class InventoryExpansionException(InventoryExpansionRejection rejection) : Exception(rejection.ToString())
{ public InventoryExpansionRejection Rejection { get; } = rejection; }
public interface IInventoryExpansionDomainService
{
    ValueTask<InventoryExpansionResult> UseItemAsync(SelectedSessionIdentity session, BusinessSlot slot, InventoryExpansionKind kind, DateTimeOffset at, CancellationToken ct);
    ValueTask<InventoryExpansionResult> UpgradeAccountAsync(SelectedSessionIdentity session, bool create, DateTimeOffset at, CancellationToken ct);
    ValueTask<InventoryExpansionResult> TransferGoldAsync(SelectedSessionIdentity session, bool deposit, int amount, DateTimeOffset at, CancellationToken ct);
    ValueTask<IReadOnlyList<InventoryExpansionChange>?> PlanPurchaseAsync(SelectedSessionIdentity session, IReadOnlyList<InventoryExpansionGrant> grants, CancellationToken ct);
    ValueTask ApplyPurchaseAsync(SelectedSessionIdentity session, IReadOnlyList<InventoryExpansionChange> changes, CancellationToken ct);
    ValueTask<InventoryExpansionReceipt> SnapshotAsync(SelectedSessionIdentity session, DateTimeOffset at, CancellationToken ct);
}
public interface IInventoryExpansionRuleService
{
    ValueTask<AccountCargoDefinition> AccountCargoAsync(CancellationToken ct);
    ValueTask<InventoryExpansionGrant?> ItemAsync(int itemId, CancellationToken ct);
    AccountCargoTier? NextTier(AccountCargoDefinition definition, int current);
    bool CanUpgrade(InventoryExpansionGrant grant, int current, AccountCargoDefinition account, bool purchase);
}
