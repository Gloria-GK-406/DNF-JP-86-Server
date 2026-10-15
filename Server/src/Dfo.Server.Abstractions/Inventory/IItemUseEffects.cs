using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public readonly record struct ItemCooldownKey(bool IsShared, int Value)
{
    public static ItemCooldownKey ForGroup(int cooltimeGroup) => new(true, cooltimeGroup);
    public static ItemCooldownKey ForItem(int itemId) => new(false, itemId);
}

public sealed record ConsumeFromContainerEffect(BusinessSlot Slot, int ExpectedItemId, int Count,
    byte ExpectedEquipmentLockId = 0) : InventoryEffect;
public sealed record StartItemCooldownEffect(ItemCooldownKey Key, TimeSpan Duration) : DomainEffect;
public sealed record StartSpiritPotionEffect(double BonusRate, TimeSpan Duration) : DomainEffect;
public sealed record AddPetStomachEffect(int ItemUid, int Amount) : DomainEffect;

public sealed record ContainerConsumptionChange(BusinessSlot Slot, int ItemId, int Consumed, int RemainingCount) : EffectChange;
public sealed record ItemCooldownChange(ItemCooldownKey Key, TimeSpan Duration) : EffectChange;
public sealed record SpiritPotionChange(double BonusRate, TimeSpan Duration) : EffectChange;
public sealed record PetStomachChange(Dfo.Server.Abstractions.Creatures.CreatureRuntimeChange Change) : EffectChange;

public sealed record ItemUsePlan(ItemUsability Status, IReadOnlyList<DomainEffect> Effects);
public sealed record ItemUseApplication(ItemUsability Status, int ItemId, int RemainingCount, ApplyEffectsResult? Applied = null)
{
    public Dfo.Server.Abstractions.World.ExperienceAward? ExperienceAward { get; init; }
    public CharacterSkillProtocolMirrorState? SkillPoints { get; init; }
}

public interface IItemUseEffectRuleService
{
    ValueTask<ItemUseRule?> RuleForAsync(int itemId, CancellationToken cancellationToken);
    ItemCooldownKey CooldownKeyFor(int itemId, ItemUseRule rule);
    ItemUsePlan Build(ItemUseRule? rule, BusinessSlot source, ItemReference item,
        ItemUseCandidate? character, int? currentPetUid, DateTimeOffset at);
}

public interface IItemUseEffectDomainService
{
    ValueTask<ItemUseApplication> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        ItemReference item, CancellationToken cancellationToken);
}

public interface ICharacterItemCooldownDomainService
{
    bool IsCoolingDown(CharacterId characterId, ItemCooldownKey key);
    void Start(CharacterId characterId, ItemCooldownKey key, TimeSpan duration);
    void Clear(CharacterId characterId);
}
