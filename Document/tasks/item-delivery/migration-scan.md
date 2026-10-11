# 奖励路径反向扫描

扫描当前生产源码，分类依据见 [迁移矩阵](reward-migration.md)。文本命中含非物品创建和投影；不能将命中数当奖励点数。

命中文件 64；未归类文件 0。

| 文件 | 裁定 | 命中行 |
|---|---|---|
| [Server/src/Dfo.Server.Application/Accounts/QuestHelperPuppetDomainService.cs](../../../../Server/src/Dfo.Server.Application/Accounts/QuestHelperPuppetDomainService.cs) | 统一奖励/共享Effect | 20, 46 |
| [Server/src/Dfo.Server.Application/Achievements/TitleBookDomainService.cs](../../../../Server/src/Dfo.Server.Application/Achievements/TitleBookDomainService.cs) | 专属称号簿解锁与镜像 | 122, 123, 163, 165, 203, 242, 246, 252, 259 |
| [Server/src/Dfo.Server.Application/Administration/GmCeraCommands.cs](../../../../Server/src/Dfo.Server.Application/Administration/GmCeraCommands.cs) | 非物理资产/技能持久化 | 31 |
| [Server/src/Dfo.Server.Application/Administration/GmOfficialMailDomainService.cs](../../../../Server/src/Dfo.Server.Application/Administration/GmOfficialMailDomainService.cs) | 显式GM邮件/邮件发送领取 | 131, 137, 145 |
| [Server/src/Dfo.Server.Application/ApplicationModule.cs](../../../../Server/src/Dfo.Server.Application/ApplicationModule.cs) | 奖励计划/投影，不直接制造 | 278, 280, 567, 568, 578, 579 |
| [Server/src/Dfo.Server.Application/Characters/CharacterDomainService.cs](../../../../Server/src/Dfo.Server.Application/Characters/CharacterDomainService.cs) | 非物品创建/基础分发 | 171 |
| [Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs](../../../../Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs) | 初始资产 | 39, 283, 286 |
| [Server/src/Dfo.Server.Application/Characters/CharacterSkillStateDomainService.cs](../../../../Server/src/Dfo.Server.Application/Characters/CharacterSkillStateDomainService.cs) | 非物理资产/技能持久化 | 19 |
| [Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs](../../../../Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs) | 非物理资产/技能持久化 | 132 |
| [Server/src/Dfo.Server.Application/Characters/PlayingCharacter.Experience.cs](../../../../Server/src/Dfo.Server.Application/Characters/PlayingCharacter.Experience.cs) | 非物理资产/技能持久化 | 43 |
| [Server/src/Dfo.Server.Application/Effects/EffectOperationContext.cs](../../../../Server/src/Dfo.Server.Application/Effects/EffectOperationContext.cs) | 奖励计划/投影，不直接制造 | 142, 147, 152 |
| [Server/src/Dfo.Server.Application/Effects/InventoryEffectHandlers.cs](../../../../Server/src/Dfo.Server.Application/Effects/InventoryEffectHandlers.cs) | 统一奖励/共享Effect | 21, 22, 23, 25, 32, 36, 42 |
| [Server/src/Dfo.Server.Application/Effects/InventoryEffectPreflightDomainService.cs](../../../../Server/src/Dfo.Server.Application/Effects/InventoryEffectPreflightDomainService.cs) | 奖励计划/投影，不直接制造 | 67, 70, 87 |
| [Server/src/Dfo.Server.Application/Effects/OpeningEffectHandlers.cs](../../../../Server/src/Dfo.Server.Application/Effects/OpeningEffectHandlers.cs) | 统一奖励/共享Effect | 21, 22, 24, 69, 95, 101, 115, 141 |
| [Server/src/Dfo.Server.Application/Effects/QuestCompletionEffectHandlers.cs](../../../../Server/src/Dfo.Server.Application/Effects/QuestCompletionEffectHandlers.cs) | 统一奖励/共享Effect | 74, 75, 77, 83, 86, 87 |
| [Server/src/Dfo.Server.Application/Events/JoustDomainService.cs](../../../../Server/src/Dfo.Server.Application/Events/JoustDomainService.cs) | 统一奖励/共享Effect | 21, 199 |
| [Server/src/Dfo.Server.Application/Events/WeeklyAttendanceDomainService.cs](../../../../Server/src/Dfo.Server.Application/Events/WeeklyAttendanceDomainService.cs) | 统一奖励/共享Effect | 15, 115 |
| [Server/src/Dfo.Server.Application/Initialization/InitializationAssetGrants.cs](../../../../Server/src/Dfo.Server.Application/Initialization/InitializationAssetGrants.cs) | 初始资产 | 10, 14, 15, 16, 24 |
| [Server/src/Dfo.Server.Application/Inventory/AvatarCompoundDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/AvatarCompoundDomainService.cs) | 购买/制作 | 75, 82 |
| [Server/src/Dfo.Server.Application/Inventory/CeraShopDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/CeraShopDomainService.cs) | 购买/制作 | 24, 123 |
| [Server/src/Dfo.Server.Application/Inventory/CharacterBusinessInventoryDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/CharacterBusinessInventoryDomainService.cs) | 底层容器/生命周期 | 190, 256, 257 |
| [Server/src/Dfo.Server.Application/Inventory/DailyRefillDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/DailyRefillDomainService.cs) | 统一奖励/共享Effect | 16, 60 |
| [Server/src/Dfo.Server.Application/Inventory/EpicBookDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/EpicBookDomainService.cs) | 非物理资产/技能持久化 | 56 |
| [Server/src/Dfo.Server.Application/Inventory/EquipmentEffectPlanRuleService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/EquipmentEffectPlanRuleService.cs) | 奖励计划/投影，不直接制造 | 100 |
| [Server/src/Dfo.Server.Application/Inventory/InventorySortDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/InventorySortDomainService.cs) | 移动/排序/历史位置修正 | 63 |
| [Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs](../../../../Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs) | 底层容器/生命周期 | 135 |
| [Server/src/Dfo.Server.Application/Inventory/ItemDeliveryDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/ItemDeliveryDomainService.cs) | 统一奖励/共享Effect | 18, 74, 138, 157 |
| [Server/src/Dfo.Server.Application/Inventory/ItemGrantDelivery.cs](../../../../Server/src/Dfo.Server.Application/Inventory/ItemGrantDelivery.cs) | 底层容器/生命周期 | 49 |
| [Server/src/Dfo.Server.Application/Inventory/ItemGrantDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/ItemGrantDomainService.cs) | 底层容器/生命周期 | 14, 17, 19, 25, 33, 54, 70 |
| [Server/src/Dfo.Server.Application/Inventory/ItemLifecycleDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/ItemLifecycleDomainService.cs) | 底层容器/生命周期 | 23 |
| [Server/src/Dfo.Server.Application/Inventory/ItemMovementDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/ItemMovementDomainService.cs) | 移动/排序/历史位置修正 | 54, 62, 72, 84, 85 |
| [Server/src/Dfo.Server.Application/Inventory/LegacyWornItemNormalizer.cs](../../../../Server/src/Dfo.Server.Application/Inventory/LegacyWornItemNormalizer.cs) | 移动/排序/历史位置修正 | 53 |
| [Server/src/Dfo.Server.Application/Inventory/NpcShopDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/NpcShopDomainService.cs) | 购买/制作 | 24, 105 |
| [Server/src/Dfo.Server.Application/Inventory/OpeningEffectPlanRuleService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/OpeningEffectPlanRuleService.cs) | 共享制作/礼包计划，制作显式InventoryOnly | 68, 101 |
| [Server/src/Dfo.Server.Application/Inventory/OpeningInventoryDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/OpeningInventoryDomainService.cs) | 奖励计划/投影，不直接制造 | 99 |
| [Server/src/Dfo.Server.Application/Inventory/RecipeCraftDomainService.cs](../../../../Server/src/Dfo.Server.Application/Inventory/RecipeCraftDomainService.cs) | 共享制作/礼包计划，制作显式InventoryOnly | 68 |
| [Server/src/Dfo.Server.Application/Mail/MailboxDomainService.cs](../../../../Server/src/Dfo.Server.Application/Mail/MailboxDomainService.cs) | 显式GM邮件/邮件发送领取 | 124 |
| [Server/src/Dfo.Server.Application/Mail/MailItemTransfers.cs](../../../../Server/src/Dfo.Server.Application/Mail/MailItemTransfers.cs) | 显式GM邮件/邮件发送领取 | 27, 141 |
| [Server/src/Dfo.Server.Application/Quests/MirrorAradQuestRuleService.cs](../../../../Server/src/Dfo.Server.Application/Quests/MirrorAradQuestRuleService.cs) | 奖励计划/投影，不直接制造 | 123 |
| [Server/src/Dfo.Server.Application/Quests/QuestCompletionEffectRuleService.cs](../../../../Server/src/Dfo.Server.Application/Quests/QuestCompletionEffectRuleService.cs) | 奖励计划/投影，不直接制造 | 99 |
| [Server/src/Dfo.Server.Application/World/DespairTowerDomainService.cs](../../../../Server/src/Dfo.Server.Application/World/DespairTowerDomainService.cs) | 统一奖励/共享Effect | 24, 87 |
| [Server/src/Dfo.Server.Application/World/DungeonVisitDomainService.cs](../../../../Server/src/Dfo.Server.Application/World/DungeonVisitDomainService.cs) | 非物品创建/基础分发 | 98, 154 |
| [Server/src/Dfo.Server.Application/World/IllusionTower/IllusionTowerDomainService.cs](../../../../Server/src/Dfo.Server.Application/World/IllusionTower/IllusionTowerDomainService.cs) | 地面拾取/临时塔物品 | 164 |
| [Server/src/Dfo.Server.Application/World/IllusionTower/IllusionTowerRewardDomainService.cs](../../../../Server/src/Dfo.Server.Application/World/IllusionTower/IllusionTowerRewardDomainService.cs) | 统一奖励/共享Effect | 16, 82, 119 |
| [Server/src/Dfo.Server.Application/World/IllusionTower/TowerInventoryDomainService.cs](../../../../Server/src/Dfo.Server.Application/World/IllusionTower/TowerInventoryDomainService.cs) | 地面拾取/临时塔物品 | 13, 30 |
| [Server/src/Dfo.Server.Protocol/Game/CardReward/CardRewardHandlers.cs](../../../../Server/src/Dfo.Server.Protocol/Game/CardReward/CardRewardHandlers.cs) | 既有通知投影，不制造物品 | 154 |
| [Server/src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs](../../../../Server/src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs) | 既有通知投影，不制造物品 | 199 |
| [Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs](../../../../Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs) | 统一奖励/共享Effect | 163, 226, 234, 311 |
| [Server/src/Dfo.Server.Protocol/Game/CharacterSelectionDelivery.cs](../../../../Server/src/Dfo.Server.Protocol/Game/CharacterSelectionDelivery.cs) | 既有通知投影，不制造物品 | 15 |
| [Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs](../../../../Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs) | 既有通知投影，不制造物品 | 269 |
| [Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs](../../../../Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs) | 地面拾取/临时塔物品 | 296, 390, 669, 729, 747 |
| [Server/src/Dfo.Server.Protocol/Game/IllusionTowerPartyCoordination.cs](../../../../Server/src/Dfo.Server.Protocol/Game/IllusionTowerPartyCoordination.cs) | 既有通知投影，不制造物品 | 214 |
| [Server/src/Dfo.Server.Protocol/Game/MonsterDeath/ApplyMonsterKillCommand.cs](../../../../Server/src/Dfo.Server.Protocol/Game/MonsterDeath/ApplyMonsterKillCommand.cs) | 统一奖励/共享Effect | 49, 81 |
| [Server/src/Dfo.Server.Protocol/Game/MonsterDeath/ApplyObjectDestructionCommand.cs](../../../../Server/src/Dfo.Server.Protocol/Game/MonsterDeath/ApplyObjectDestructionCommand.cs) | 统一奖励/共享Effect | 31, 63 |
| [Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs](../../../../Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs) | 既有通知投影，不制造物品 | 752, 972 |
| [Server/src/Dfo.Server.Protocol/Game/MonsterDeath/PendingQuestItemGrants.cs](../../../../Server/src/Dfo.Server.Protocol/Game/MonsterDeath/PendingQuestItemGrants.cs) | 统一奖励/共享Effect | 22, 59 |
| [Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs](../../../../Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs) | 移动/排序/历史位置修正 | 625, 627, 717, 719, 803, 805 |
| [Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs](../../../../Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs) | 既有通知投影，不制造物品 | 141, 148 |
| [Server/src/Dfo.Server.Protocol/Game/Party/PartyCommandSlice.cs](../../../../Server/src/Dfo.Server.Protocol/Game/Party/PartyCommandSlice.cs) | 既有通知投影，不制造物品 | 647, 712, 797, 892, 1015, 1126 |
| [Server/src/Dfo.Server.Protocol/Game/QuestAcceptance/AcceptQuestCommand.cs](../../../../Server/src/Dfo.Server.Protocol/Game/QuestAcceptance/AcceptQuestCommand.cs) | 统一奖励/共享Effect | 90, 154 |
| [Server/src/Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestCommand.cs](../../../../Server/src/Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestCommand.cs) | 奖励计划/投影，不直接制造 | 131 |
| [Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs](../../../../Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs) | 既有通知投影，不制造物品 | 114, 173, 210, 216 |
| [Server/src/Dfo.Server.Protocol/Game/StateSynchronization/SupplementalSynchronizationMessages.cs](../../../../Server/src/Dfo.Server.Protocol/Game/StateSynchronization/SupplementalSynchronizationMessages.cs) | 既有通知投影，不制造物品 | 270 |
| [Server/src/Dfo.Server.Protocol/Game/TaggedIdReport/GrantTutorialRewardCommand.cs](../../../../Server/src/Dfo.Server.Protocol/Game/TaggedIdReport/GrantTutorialRewardCommand.cs) | 统一奖励/共享Effect | 43, 65 |
