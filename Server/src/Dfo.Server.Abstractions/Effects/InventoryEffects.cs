using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Accounts;

namespace Dfo.Server.Abstractions.Effects;

public abstract record InventoryEffect : DomainEffect;
public sealed record ConsumeAccountMaterialEffect(AccountMaterialKind Kind, int ExpectedItemId, int Count) : InventoryEffect;
public sealed record SpendGoldEffect(int Amount) : InventoryEffect;
public sealed record GrantStackableEffect(int ItemId, int Count) : InventoryEffect;
public sealed record GrantAccountMaterialEffect(AccountMaterialKind Kind, int ExpectedItemId, int Count) : InventoryEffect;
public sealed record EquipmentEffectTarget(ItemIdentity Identity, int ExpectedItemId);
public abstract record EquipmentEffect(EquipmentEffectTarget Equipment) : InventoryEffect;
public sealed record SetEquipmentUpgradeEffect(EquipmentEffectTarget Equipment, byte Level) : EquipmentEffect(Equipment);
public sealed record SetEquipmentEnchantmentEffect(EquipmentEffectTarget Equipment, int CardId) : EquipmentEffect(Equipment);
public sealed record SetEquipmentDimensionalEffect(EquipmentEffectTarget Equipment, byte Type, ushort Value) : EquipmentEffect(Equipment);
public sealed record SetEquipmentMagicSealEffect(EquipmentEffectTarget Equipment, IReadOnlyList<RandomOptionState> Options) : EquipmentEffect(Equipment);
public sealed record SetEquipmentForgingEffect(EquipmentEffectTarget Equipment, byte Level) : EquipmentEffect(Equipment);
public sealed record DestroyEquipmentEffect(EquipmentEffectTarget Equipment) : EquipmentEffect(Equipment);

public sealed record InventorySlotEffectChange(BusinessSlot Slot, InventoryItemCoreState? Core);
public sealed record EquipmentEffectChange(EquipmentInstanceFacts Before, EquipmentInstanceFacts? After) : EffectChange;
public sealed record GoldEffectChange(int Remaining) : EffectChange;
public sealed record AccountMaterialEffectChange(AccountMaterialChange Change) : EffectChange;
public sealed record StackableGrantAddition(BusinessSlot Slot, int ItemId, int Count);
public sealed record StackableGrantEffectChange(IReadOnlyList<StackableGrantAddition> Additions,
    IReadOnlyList<InventorySlotEffectChange> Slots) : EffectChange;

public enum InventoryEffectRejection { None, InvalidTarget, InsufficientAssets, UnsupportedItem, NoRoom }
public sealed record InventoryEffectPreviewResult(InventoryEffectRejection Rejection, int PhysicalRewardEntries = 0,
    int AccountRewardKinds = 0, InventoryEffectHoldingsSnapshot? Holdings = null);
public sealed record InventoryEffectHoldingsSnapshot(System.Collections.Immutable.ImmutableArray<ContainerSlotItem> Main, int Gold);
public sealed class InventoryEffectException(InventoryEffectRejection rejection)
    : InvalidOperationException($"Inventory effect plan refused: {rejection}.")
{
    public InventoryEffectRejection Rejection { get; } = rejection;
}
public interface IInventoryEffectPreflightDomainService
{
    ValueTask<InventoryEffectPreviewResult> PreviewAsync(EffectExecutionContext context,
        IReadOnlyList<DomainEffect> effects, CancellationToken ct);
    ValueTask<InventoryEffectPreviewResult> PreviewWithHoldingsAsync(EffectExecutionContext context,
        IReadOnlyList<DomainEffect> effects, CancellationToken ct) => throw new NotSupportedException("Holdings preview is unavailable.");
}

// Prepared grants can extend this same standing inventory without flattening creation facts.
public interface IInventoryEffectPreviewHandler
{
    Type EffectType { get; }
    ValueTask PreviewAsync(EffectExecutionContext context, DomainEffect effect, IInventoryEffectPreviewState state,
        CancellationToken ct);
}
public interface IInventoryEffectPreviewState
{
    ValueTask<int> OpenSlotCountAsync(BusinessInventorySpace space, CancellationToken ct) => ValueTask.FromResult(int.MaxValue);
    ValueTask<ContainerSlotItem?> ReadSlotAsync(BusinessSlot slot, CancellationToken ct);
    void SetSlot(BusinessSlot slot, ContainerSlotItem? item);
    ValueTask<int> AccountCountAsync(AccountMaterialKind kind, CancellationToken ct);
    void SetAccountCount(AccountMaterialKind kind, int count);
    ValueTask<int> GoldAsync(CancellationToken ct);
    void SetGold(int amount);
    ValueTask<ContainerSlotItem?> VirtualSlotAsync(int index, CancellationToken ct);
    void SetVirtualSlot(int index, ContainerSlotItem? item);
    ValueTask<long> PremiumExpiryAsync(PremiumServiceType type, CancellationToken ct);
    void SetPremiumExpiry(PremiumServiceType type, long expiry);
    ValueTask<int> SeriaLuckAsync(CancellationToken ct);
    void SetSeriaLuck(int value);
    ValueTask<DoublePotCounterState> DoublePotCountersAsync(CancellationToken ct);
    void SetDoublePotCounters(DoublePotCounterState counters);
    void AddedPhysicalRewardEntries(int count);
    void AddedAccountReward(AccountMaterialKind kind);
}
