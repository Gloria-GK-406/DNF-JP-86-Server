using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Accounts;

namespace Dfo.Server.Abstractions.Effects;

public abstract record InventoryEffect : DomainEffect;
public sealed record SpendGoldEffect(int Amount) : InventoryEffect;
public sealed record GrantStackableEffect(int ItemId, int Count) : InventoryEffect;
public sealed record EquipmentEffectTarget(ItemReference Reference, int ExpectedItemId);
public abstract record EquipmentEffect(EquipmentEffectTarget Equipment) : InventoryEffect;
public sealed record SetEquipmentUpgradeEffect(EquipmentEffectTarget Equipment, byte Level) : EquipmentEffect(Equipment);
public sealed record SetEquipmentEnchantmentEffect(EquipmentEffectTarget Equipment, int CardId) : EquipmentEffect(Equipment);
public sealed record SetEquipmentDimensionalEffect(EquipmentEffectTarget Equipment, byte Type, ushort Value) : EquipmentEffect(Equipment);
public sealed record SetEquipmentMagicSealEffect(EquipmentEffectTarget Equipment, IReadOnlyList<RandomOptionState> Options) : EquipmentEffect(Equipment);
public sealed record SetEquipmentForgingEffect(EquipmentEffectTarget Equipment, byte Level) : EquipmentEffect(Equipment);
public sealed record DestroyEquipmentEffect(EquipmentEffectTarget Equipment) : EquipmentEffect(Equipment);

public sealed record InventorySlotEffectChange(BusinessSlot Slot, ItemSnapshot? Core);
public sealed record EquipmentEffectChange(BusinessSlot Slot, ItemSnapshot Before, ItemSnapshot? After) : EffectChange;
public sealed record GoldEffectChange(int Remaining) : EffectChange;
public sealed record StackableGrantAddition(BusinessSlot Slot, int ItemId, int Count);
public sealed record StackableGrantEffectChange(IReadOnlyList<StackableGrantAddition> Additions,
    IReadOnlyList<InventorySlotEffectChange> Slots) : EffectChange;

public enum InventoryEffectRejection { None, InvalidTarget, InsufficientAssets, UnsupportedItem, NoRoom }
public sealed record InventoryEffectPreviewResult(InventoryEffectRejection Rejection, int RewardEntries = 0,
    InventoryEffectHoldingsSnapshot? Holdings = null);
public sealed record InventoryEffectHoldingsSnapshot(System.Collections.Immutable.ImmutableDictionary<int, ItemSnapshot> Main, int Gold);
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
    ValueTask<ItemSnapshot?> GetAsync(BusinessSlot slot, CancellationToken ct);
    void SetSlot(BusinessSlot slot, ItemSnapshot? item);
    ValueTask<int> GoldAsync(CancellationToken ct);
    void SetGold(int amount);
    ValueTask<ItemSnapshot?> VirtualSlotAsync(int index, CancellationToken ct);
    void SetVirtualSlot(int index, ItemSnapshot? item);
    ValueTask<long> PremiumExpiryAsync(PremiumServiceType type, CancellationToken ct);
    void SetPremiumExpiry(PremiumServiceType type, long expiry);
    ValueTask<int> SeriaLuckAsync(CancellationToken ct);
    void SetSeriaLuck(int value);
    ValueTask<DoublePotCounterState> DoublePotCountersAsync(CancellationToken ct);
    void SetDoublePotCounters(DoublePotCounterState counters);
    void AddedRewardEntries(int count);
}
