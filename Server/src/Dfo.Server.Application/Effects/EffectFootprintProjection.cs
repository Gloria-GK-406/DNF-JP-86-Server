using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

internal class EffectFootprintProjection(ReplayProjectionNeeds minimum)
{
    internal ReplayProjectionNeeds Needs { get; set; } = minimum;
    internal HashSet<BusinessSlot> Slots { get; } = [];
    internal HashSet<TownAreaKey> Areas { get; } = [];
    internal ReplayProjectionRequest Freeze()
    {
        var result = new ReplayProjectionRequest(Needs, Slots.ToImmutableArray()) { TouchedTownAreas = Areas.ToImmutableArray() };
        result.Validate();
        return result;
    }
    internal void RecordEffects(IReadOnlyList<AppliedEffect> applied)
    {
        foreach (var item in applied)
        {
            switch (item.Effect, item.Change)
            {
                case (ConsumeFromContainerEffect, ContainerConsumptionChange change): Physical(change.Slot); break;
                case (SetEquipmentUpgradeEffect or SetEquipmentEnchantmentEffect or SetEquipmentDimensionalEffect
                    or SetEquipmentMagicSealEffect or SetEquipmentForgingEffect or DestroyEquipmentEffect, EquipmentEffectChange change):
                    Physical(change.Slot); break;
                case (GrantStackableEffect, StackableGrantEffectChange change):
                    Needs |= ReplayProjectionNeeds.Inventory;
                    foreach (var addition in change.Additions) Physical(addition.Slot);
                    foreach (var slot in change.Slots) Physical(slot.Slot);
                    break;
                case (GrantPreparedItemEffect grant, PreparedItemGrantChange change):
                    Needs |= ReplayProjectionNeeds.Inventory;
                    foreach (var slot in change.Slots) Physical(slot);
                    if (grant.Grant.Item.Placement.ItemKind == 5) Needs |= ReplayProjectionNeeds.Pets;
                    break;
                case (GrantBasicCharacterItemEffect grant, BasicCharacterItemChange change):
                    Needs |= ReplayProjectionNeeds.Inventory;
                    foreach (var addition in change.Additions) Physical(addition.Slot);
                    if (grant.Placement.ItemKind == 5) Needs |= ReplayProjectionNeeds.Pets;
                    break;
                case (SpendGoldEffect, GoldEffectChange):
                case (SetQuestRewardGoldEffect, QuestRewardGoldChange):
                case (GrantOpeningGoldEffect, OpeningVirtualChange):
                case (GrantOpeningVirtualEffect, OpeningVirtualChange): Needs |= ReplayProjectionNeeds.Inventory; break;
                case (MoveToTownEffect move, TownPositionChange):
                    Needs |= ReplayProjectionNeeds.TownLocation;
                    Areas.Add(move.SourceArea);
                    Areas.Add(new(move.ChannelId, move.Destination.TownId, move.Destination.AreaId));
                    break;
                case (AddDungeonEntryAllowanceEffect, DungeonEntryAllowanceChange): Needs |= ReplayProjectionNeeds.HerasQuota; break;
                case (SetFatigueStateEffect, FatigueStateChange): Needs |= ReplayProjectionNeeds.Fatigue; break;
                case (SetItemUsageStateEffect, ItemUsageStateChange): Needs |= ReplayProjectionNeeds.ItemUsage; break;
                case (SetCarriedQuestEffect or RemoveTrackedQuestEffect or SetClearedQuestEffect, QuestRowChange): Needs |= ReplayProjectionNeeds.Quests; break;
                case (SetLearnedSkillEffect, LearnedSkillChange): Needs |= ReplayProjectionNeeds.Skills; break;
                case (RenamePetEffect, PetNameChange):
                case (AddPetStomachEffect, PetStomachChange): Needs |= ReplayProjectionNeeds.Pets; break;
                case (CharacterExperienceEffect, CharacterExperienceChange): Needs |= ReplayProjectionNeeds.Experience; break;
                case (SetCharacterLevelEffect, CharacterLevelChange): Needs |= ReplayProjectionNeeds.Experience | ReplayProjectionNeeds.Skills; break;
                case (SetCharacterGrowTypeEffect, CharacterGrowTypeChange):
                case (SetCharacterAwakeningEffect, CharacterGrowTypeChange):
                case (MaterializeSkillBuildsEffect, SkillBuildsMaterializedChange):
                    Needs |= ReplayProjectionNeeds.CharacterAppearance | ReplayProjectionNeeds.Skills; break;
                case (SetOpeningPremiumEffect, OpeningPremiumChange):
                case (SetDoublePotUsageEffect, DoublePotUsageChange): Needs |= ReplayProjectionNeeds.Premium; break;
                // These runtime changes have no safe absolute wire representation in the current protocol.
                case (StartItemCooldownEffect, ItemCooldownChange):
                case (StartSpiritPotionEffect, SpiritPotionChange):
                case (SetSeriaLuckEffect, SeriaLuckChange): break;
                default: throw new NotSupportedException($"No current-state footprint for {item.Effect.GetType().Name}/{item.Change.GetType().Name}.");
            }
        }
        void Physical(BusinessSlot slot) { Needs |= ReplayProjectionNeeds.Inventory; Slots.Add(slot); }
    }
}
