# PVF 固定读取覆盖清单

由 verification/generate-field-coverage.py 从当前源码生成。复合 Tokens 字段由各主题的专业解析器完整验证；字段族和动态边界见下方说明。

统计：53 个内容主题，476 个具名字段声明（不含 ProbeFields.Name 和 PvfFieldFamily）。

## PvfCeraShopDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `AvatarField` | `[avatar]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CoinField` | `[coin]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `MaterialField` | `[material]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `RecoveryItemField` | `[recovery item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `RecoveryitemField` | `[recoveryitem]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `PremiumField` | `[premium]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CreatureField` | `[creature]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CommunityItemField` | `[community item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `SelectableCharacterPremiumField` | `[selectable character premium]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CharacPremiumPackageField` | `[charac premium package]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `PackageField` | `[package]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `RegularPackageField` | `[regular package]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CommunityPackageField` | `[community package]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `VisualField` | `[visual]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `EtcField` | `[etc]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseEtcField` | `[/etc]` | `bool` / `Marker` |
| `EquipmentTypeField` | `[equipment type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `StackableTypeField` | `[stackable type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `RestrictBuyLevelCashshopField` | `[restrict buy level cashshop]` | `int` / `Int32` |
| `DurabilityField` | `[durability]` | `int` / `Int32` |
| `AbilityCaseIndexField` | `[ability case index]` | `int` / `Int32` |
| `CreatureSpeciesField` | `[creature species]` | `int` / `Int32` |
| `AttachTypeField` | `[attach type]` | `string` / `String` |
| `BuyOnlyCeraField` | `[buy only cera]` | `IReadOnlyList<int>` / `Int32List` |
| `BuyOnlyCeraPointField` | `[buy only cera point]` | `IReadOnlyList<int>` / `Int32List` |
| `NotStackableBuyField` | `[not stackable buy]` | `IReadOnlyList<int>` / `Int32List` |
| `BuyRestrictItemField` | `[buy restrict item]` | `IReadOnlyList<int>` / `Int32List` |
| `CommonCharacterPremiumDetailField` | `[common character premium detail]` | `IReadOnlyList<int>` / `Int32List` |
| `UpgradeInfoField` | `[upgrade info]` | `IReadOnlyList<int>` / `Int32List` |
| `BoosterInfoField` | `[booster info]` | `bool` / `Marker` |
| `CloseBoosterInfoField` | `[/booster info]` | `bool` / `Marker` |
| `PackageDataIncludeUsablePeriodField` | `[package data include usable period]` | `IReadOnlyList<int>` / `Int32List` |
| `PackageDataSelectionField` | `[package data selection]` | `IReadOnlyList<int>` / `Int32List` |
| `AvatarTypeSelectField` | `[avatar type select]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `AvatarSelectAbilityField` | `[avatar select ability]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `StartLevelField` | `[start level]` | `int` / `Int32` |
| `AbilityCaseField` | `[ability case]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseAbilityCaseField` | `[/ability case]` | `bool` / `Marker` |
| `TargetItemField` | `[target item]` | `IReadOnlyList<int>` / `Int32List` |
| `CatalogItemField` | `[item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ItemField` | `[item]` | `IReadOnlyList<int>` / `Int32List` |
| `PackageDataField` | `[package data]` | `IReadOnlyList<int>` / `Int32List` |
| `ExpirationDateField` | `[expiration date]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `UsablePeriodField` | `[usable period]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `TradeLimitField` | `[trade limit]` | `int` / `Int32` |

## PvfCharacterStatDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `InitialValueField` | `[initial value]` | `bool` / `Marker` |
| `HPMAXField` | `[HP MAX]` | `double` / `Number` |
| `MPMAXField` | `[MP MAX]` | `double` / `Number` |
| `PhysicalAttackField` | `[physical attack]` | `double` / `Number` |
| `PhysicalDefenseField` | `[physical defense]` | `double` / `Number` |
| `MagicalAttackField` | `[magical attack]` | `double` / `Number` |
| `MagicalDefenseField` | `[magical defense]` | `double` / `Number` |
| `FireResistanceField` | `[fire resistance]` | `double` / `Number` |
| `WaterResistanceField` | `[water resistance]` | `double` / `Number` |
| `DarkResistanceField` | `[dark resistance]` | `double` / `Number` |
| `LightResistanceField` | `[light resistance]` | `double` / `Number` |
| `InventoryLimitField` | `[inventory limit]` | `double` / `Number` |
| `HPRegenSpeedField` | `[HP regen speed]` | `double` / `Number` |
| `MPRegenSpeedField` | `[MP regen speed]` | `double` / `Number` |
| `MoveSpeedField` | `[move speed]` | `double` / `Number` |
| `AttackSpeedField` | `[attack speed]` | `double` / `Number` |
| `CastSpeedField` | `[cast speed]` | `double` / `Number` |
| `HitRecoveryField` | `[hit recovery]` | `double` / `Number` |
| `JumpPowerField` | `[jump power]` | `double` / `Number` |
| `WeightField` | `[weight]` | `double` / `Number` |

## PvfClearRewardRateSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `BandCountSection` | `[drop prob count]` | `int` / `Int32` |
| `ProfileSection` | `[drop prob]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `DropKindSection` | `[drop kind prob]` | `IReadOnlyList<int>` / `Int32List` |
| `ItemTypeSection` | `[drop item type prob]` | `IReadOnlyList<int>` / `Int32List` |
| `DifficultySection` | `[dungeon difficulty drop bonusrate]` | `IReadOnlyList<int>` / `Int32List` |
| `RaritySection` | `[basis of rarity dicision]` | `IReadOnlyList<int>` / `Int32List` |
| `DifficultyGoldSection` | `[dungeon difficulty gold drop bonusrate]` | `IReadOnlyList<double>` / `Numbers` |
| `PartySection` | `[party member drop bonusrate]` | `IReadOnlyList<double>` / `Numbers` |
| `MapCountSection` | `[reward item rate per map max count]` | `IReadOnlyList<int>` / `Int32List` |
| `CostSection` | `[gold card cost table]` | `IReadOnlyList<int>` / `Int32List` |
| `CreateRateSection` | `[gold card create rate]` | `double` / `Single` |
| `GoldBlankItemSection` | `[gold card blank item]` | `IReadOnlyList<int>` / `Int32List` |
| `PcRoomBlankItemSection` | `[pcroom card blank item]` | `IReadOnlyList<int>` / `Int32List` |
| `GradeBandSection` | `[item drop ref table]` | `IReadOnlyList<int>` / `Int32List` |
| `RarityControl` | `[item drop rarity control]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ChronicleSetRewardRate` | `[chronicle set reward rate]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfCollectionBoxDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `CollectBoxInfoField` | `[Collect Box Info]` | `bool` / `Marker` |
| `CloseCollectBoxInfoField` | `[/Collect Box Info]` | `bool` / `Marker` |
| `IndexField` | `[Index]` | `int` / `Int32` |
| `BaseExpirationDateField` | `[base expiration date]` | `string` / `String` |
| `MaxExpirationDateField` | `[max expiration date]` | `string` / `String` |
| `ExtendItemField` | `[Extend Item]` | `IReadOnlyList<int>` / `Int32List` |
| `ItemPositionInfoField` | `[Item Position Info]` | `IReadOnlyList<int>` / `Int32List` |

## PvfCreatureDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `NameField` | `[name]` | `string` / `String` |
| `StartLevelField` | `[start level]` | `int` / `Int32` |
| `PermissionLevelField` | `[permission level]` | `int` / `Int32` |
| `MaxLevelField` | `[max level]` | `int` / `Int32` |
| `ArtifactSlotField` | `[artifact slot]` | `IReadOnlyList<string>` / `Strings` |
| `EvolutionLevelField` | `[evolution level]` | `IReadOnlyList<int>` / `Int32List` |
| `EvolutionQuestField` | `[evolution quest]` | `IReadOnlyList<int>` / `Int32List` |
| `EvolutionCreatureIdField` | `[evolution creature id]` | `IReadOnlyList<int>` / `Int32List` |
| `AmbientField` | `[ambient]` | `bool` / `Marker` |
| `EquipmentTypeField` | `[equipment type]` | `string` / `Custom` |
| `OutputIndexField` | `[output index]` | `int` / `Int32` |
| `CreatureSpeciesField` | `[creature species]` | `int` / `Int32` |
| `RoomListField` | `[room list]` | `IReadOnlyList<string>` / `Strings` |

## PvfCreatureEquipmentLifetimeSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `ExpirationDateField` | `[expiration date]` | `string` / `Custom` |
| `UsablePeriodField` | `[usable period]` | `string` / `Custom` |

## PvfCreatureRuleSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `EquipmentTypeField` | `[equipment type]` | `string` / `Custom` |
| `SubTypeField` | `[sub type]` | `int` / `Int32` |
| `CreatureSpeciesField` | `[creature species]` | `int` / `Int32` |
| `OutputIndexField` | `[output index]` | `int` / `Int32` |
| `CreatureMinimumLevelField` | `[creature minimum level]` | `int` / `Int32` |
| `CreatureExperienceAmountRateField` | `[creature experience amount rate]` | `double` / `Number` |
| `SelectiveGrowCreatureField` | `[selective grow creature]` | `bool` / `Marker` |
| `UsingExpItemField` | `[using exp item]` | `int` / `Int32` |
| `GrowAtypeItemIndexField` | `[grow Atype item index]` | `int` / `Int32` |
| `GrowBtypeItemIndexField` | `[grow Btype item index]` | `int` / `Int32` |

## PvfDailyActivityDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `GroupField` | `[group]` | `int` / `Int32` |
| `CloseGroupField` | `[/group]` | `bool` / `Marker` |
| `LevelField` | `[level]` | `IReadOnlyList<int>` / `Int32List` |
| `SlotNumTableField` | `[slot num table]` | `IReadOnlyList<int>` / `Int32List` |
| `SlotField` | `[slot]` | `IReadOnlyList<int>` / `Int32List` |
| `CloseSlotField` | `[/slot]` | `bool` / `Marker` |
| `RewardTableField` | `[reward table]` | `IReadOnlyList<int>` / `Int32List` |

## PvfDropItemPoolSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `GradeSection` | `[grade]` | `int` / `Int32` |
| `RaritySection` | `[rarity]` | `int` / `Int32` |
| `CreationRateSection` | `[creation rate]` | `int` / `Int32` |
| `DurabilitySection` | `[durability]` | `int` / `Int32` |

## PvfDropItemReader.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `RarityField` | `[rarity]` | `int` / `Int32` |
| `DurabilityField` | `[durability]` | `int` / `Int32` |
| `ItemCategoryField` | `[item category]` | `IReadOnlyList<string>` / `Strings` |

## PvfDropRateTableSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `BandCountSection` | `[drop prob count]` | `int` / `Int32` |
| `BandSection` | `[drop prob]` | `IReadOnlyList<int>` / `Int32List` |
| `RaritySection` | `[basis of rarity dicision]` | `IReadOnlyList<int>` / `Int32List` |
| `MonsterRankSection` | `[monster type drop bonusrate]` | `IReadOnlyList<double>` / `Numbers` |
| `DifficultySection` | `[dungeon difficulty drop bonusrate]` | `IReadOnlyList<double>` / `Numbers` |
| `PartySection` | `[party member drop bonusrate]` | `IReadOnlyList<double>` / `Numbers` |
| `GradeBandSection` | `[item drop ref table]` | `IReadOnlyList<int>` / `Int32List` |
| `GoldBaseSection` | `[gold drop ref table]` | `IReadOnlyList<int>` / `Int32List` |
| `GoldQuantitySection` | `[gold quantity]` | `IReadOnlyList<int>` / `Int32List` |
| `GoldVolumeSection` | `[gold volume]` | `IReadOnlyList<int>` / `Int32List` |

## PvfDungeonDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `MazeInfoField` | `[maze info]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseMazeInfoField` | `[/maze info]` | `bool` / `Marker` |
| `SizeField` | `[size]` | `IReadOnlyList<int>` / `Int32List` |
| `MapSpecificationField` | `[map specification]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `StartMapField` | `[start map]` | `IReadOnlyList<int>` / `Int32List` |
| `BossMapField` | `[boss map]` | `IReadOnlyList<int>` / `Int32List` |
| `MinimumRequiredLevelField` | `[minimum required level]` | `int` / `Int32` |
| `BasisLevelField` | `[basis level]` | `int` / `Int32` |
| `ExperienceIncreasingPointField` | `[experience increasing point]` | `double` / `Number` |
| `NoFatigueField` | `[no fatigue]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `GoldCardUseField` | `[gold card use]` | `int` / `Int32` |
| `QuestConnectionField` | `[quest connection]` | `IReadOnlyList<int>` / `Int32List` |

## PvfDungeonExperienceProgressionPolicySource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `MaxGageExpField` | `[max gage exp]` | `int` / `Int32` |
| `ExpGainRateField` | `[exp gain rate]` | `int` / `Int32` |
| `GradeField` | `[grade]` | `IReadOnlyList<int>` / `Custom` |
| `CloseGradeField` | `[/grade]` | `bool` / `Marker` |
| `MaxexpOnMaxlevelField` | `[maxexp on maxlevel]` | `int` / `Int32` |

## PvfDungeonRankGradeSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `GradeSection` | `[rank grade]` | `IReadOnlyList<int>` / `Int32List` |

## PvfDungeonRoomActorSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `MonsterField` | `[monster]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseMonsterField` | `[/monster]` | `bool` / `Marker` |
| `AiCharacterField` | `[ai character]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseAiCharacterField` | `[/ai character]` | `bool` / `Marker` |
| `MinimumInfoField` | `[minimum info]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfDungeonRoomDropSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `HellpartyBlock` | `[hellparty]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `HellpartyClose` | `[/hellparty]` | `bool` / `Marker` |
| `SpecialPassiveObjectField` | `[special passive object]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `SpecialPassiveObjectItemField` | `[special passive object item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `PassiveObjectField` | `[passive object]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfEntryLifecycleContentSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `RefillItemField` | `[refill item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `StackLimitField` | `[stack limit]` | `int` / `Int32` |
| `StackableTypeField` | `[stackable type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `EquipmentTypeTag` | `[equipment type]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfEquipmentDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `MinimumLevelField` | `[minimum level]` | `int` / `Int32` |
| `GradeField` | `[grade]` | `int` / `Int32` |
| `RarityField` | `[rarity]` | `int` / `Int32` |
| `DurabilityField` | `[durability]` | `int` / `Int32` |
| `EquipmentTypeField` | `[equipment type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ImpossibleContentsField` | `[impossible contents]` | `IReadOnlyList<string>` / `Strings` |

## PvfEquipmentDisjointDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `AllSkillItemContainerField` | `[all skill item container]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `AllSkillItemField` | `[all skill item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `SkillApplyConditionField` | `[skill apply condition]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ExtraConditionField` | `[extra condition]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ValueField` | `[value]` | `int` / `Int32` |
| `GradeField` | `[grade]` | `int` / `Int32` |
| `RarityField` | `[rarity]` | `int` / `Int32` |
| `RandomOptionField` | `[random option]` | `int` / `Int32` |
| `MinimumLevelField` | `[minimum level]` | `int` / `Int32` |
| `PortableDisjointField` | `[portable disjoint]` | `int` / `Int32` |
| `PortableDisjointInfoField` | `[portable disjoint info]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CubeCreationConstField` | `[cube creation const]` | `IReadOnlyList<double>` / `Numbers` |
| `AdditionalResultField` | `[additional result]` | `IReadOnlyList<int>` / `Int32List` |
| `AdditionalResultConstField` | `[additional result const]` | `IReadOnlyList<double>` / `Numbers` |
| `AdditionalResultExpandField` | `[additional result expand]` | `IReadOnlyList<int>` / `Int32List` |
| `AdditionalResultExpandConstField` | `[additional result expand const]` | `IReadOnlyList<double>` / `Numbers` |
| `ImpossibleContentsField` | `[impossible contents]` | `IReadOnlyList<string>` / `Strings` |
| `AttachTypeField` | `[attach type]` | `string` / `String` |
| `CubeIndexField` | `[cube index]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfEquipmentDurability.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `DurabilityField` | `[durability]` | `int` / `Int32` |

## PvfEquipmentEnchantContentSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `TargetItemIdField` | `[target item id]` | `IReadOnlyList<int>` / `Int32List` |
| `BeadLimitedUsableItemField` | `[bead limited usable item]` | `IReadOnlyList<int>` / `Int32List` |
| `StackableTypeField` | `[stackable type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `MonsterCardIdField` | `[monster card id]` | `int` / `Int32` |
| `ItemCategoryField` | `[item category]` | `string` / `String` |
| `EquipmentTypeField` | `[equipment type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `StringDataField` | `[string data]` | `IReadOnlyList<string>` / `Strings` |
| `EnchantIndexField` | `[enchant index]` | `int` / `Int32` |

## PvfEquipmentForgingContentSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `SeparateUpgradeMaxField` | `[separate upgrade max]` | `int` / `Int32` |
| `TableField` | `[table]` | `IReadOnlyList<double>` / `Numbers` |
| `ItemWeightsByGradeField` | `[item weights by grade]` | `IReadOnlyList<int>` / `Int32List` |
| `ItemWeightsByRarityField` | `[item weights by rarity]` | `IReadOnlyList<double>` / `Numbers` |

## PvfEquipmentMagicSealContentSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `UniqueQuantityField` | `[unique]` | `IReadOnlyList<int>` / `Int32List` |
| `CommonQuantityField` | `[common]` | `IReadOnlyList<int>` / `Int32List` |
| `RandomOptionField` | `[random option]` | `int` / `Int32` |
| `RarityField` | `[rarity]` | `int` / `Int32` |
| `MinimumLevelField` | `[minimum level]` | `int` / `Int32` |
| `EquipmentTypeField` | `[equipment type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ChooseOptionGroupField` | `[choose option group]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ModifiedOptionSelectionField` | `[modified option selection]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `UnsealScrollLevelLimitField` | `[unseal scroll level limit]` | `int` / `Int32` |
| `BreakSealCostField` | `[break seal cost]` | `IReadOnlyList<int>` / `Int32List` |
| `OptionModificationField` | `[option modification]` | `IReadOnlyList<int>` / `Int32List` |
| `OptionGroupField` | `[option group]` | `IReadOnlyList<int>` / `Int32List` |
| `LevelField` | `[level]` | `int` / `Int32` |
| `DungeonScopeField` | `[dungeon]` | `bool` / `Marker` |
| `PvpScopeField` | `[pvp]` | `bool` / `Marker` |
| `OptionField` | `[option]` | `int` / `Int32` |
| `CloseLevelField` | `[/level]` | `bool` / `Marker` |
| `StuckField` | `[stuck]` | `IReadOnlyList<double>` / `Numbers` |

## PvfEquipmentOutworldContentSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `PurifyOnlyCeraMaterialField` | `[purify only cera material]` | `IReadOnlyList<int>` / `Int32List` |
| `EquipLevelConstField` | `[equip level const]` | `int` / `Int32` |
| `PurifyMaterialField` | `[purify material]` | `IReadOnlyList<int>` / `Int32List` |
| `PurifyOnlyMaterialField` | `[purify only material]` | `IReadOnlyList<int>` / `Int32List` |
| `InvestOptionField` | `[invest option]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ReinvestOptionField` | `[reinvest option]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `OptionDataField` | `[option data]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `RarityWeightField` | `[rarity weight]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfEquipmentReinforcementDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `ActionTypeField` | `[action type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `NoticeField` | `[notice]` | `int` / `Int32` |
| `CostField` | `[cost]` | `IReadOnlyList<double>` / `Numbers` |
| `CostWeightsByRarityField` | `[cost weights by rarity]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `TypeField` | `[type]` | `IReadOnlyList<double>` / `Numbers` |
| `CostWeightByUpgradeLevelField` | `[cost weight by upgrade level]` | `IReadOnlyList<double>` / `Numbers` |
| `DestroyLevelByRarityField` | `[destroy level by rarity]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `MaxUpgradeLevelByRarityField` | `[max upgrade level by rarity]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `DisjointBonusItemField` | `[disjoint bonus item]` | `int` / `Int32` |
| `EquipLevelConstField` | `[equip level const]` | `int` / `Int32` |
| `UpgradeConstForBonusItemCountField` | `[upgrade const for bonus item count]` | `int` / `Int32` |
| `UpgradeFailedBonusWeightByRarityField` | `[upgrade failed bonus weight by rarity]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CorrectionGradeByRarityField` | `[correction grade by rarity]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseActionTypeField` | `[/action type]` | `bool` / `Marker` |
| `TableField` | `[table]` | `IReadOnlyList<double>` / `Numbers` |
| `TableTypeField` | `[table type]` | `string` / `String` |
| `CloseTableField` | `[/table]` | `bool` / `Marker` |
| `DisjointField` | `[disjoint]` | `IReadOnlyList<int>` / `Int32List` |
| `CloseDisjointField` | `[/disjoint]` | `bool` / `Marker` |

## PvfEquipmentRepairDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `RepairPriceField` | `[repair price]` | `int` / `Int32` |
| `GradeField` | `[grade]` | `int` / `Int32` |
| `DurabilityField` | `[durability]` | `int` / `Int32` |
| `RepairCostField` | `[repair cost]` | `double` / `Number` |
| `RepairCostRateByUpgradeLevelField` | `[repair cost rate by upgrade level]` | `IReadOnlyList<double>` / `Numbers` |

## PvfEquipmentRequirementDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `MinimumLevelField` | `[minimum level]` | `int` / `Int32` |

## PvfEquipmentSlotDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `EquipmentTypeField` | `[equipment type]` | `string` / `Custom` |

## PvfExperienceParameterSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `DifficultySection` | `[dungeon difficulty exp bonusrate]` | `IReadOnlyList<double>` / `Numbers` |
| `PartySection` | `[party user number exp bonusrate]` | `IReadOnlyList<double>` / `Numbers` |
| `MonsterSection` | `[monster exp bonusrate]` | `double` / `Number` |
| `ClearSection` | `[clear exp bonusrate]` | `double` / `Number` |
| `ClearRankSection` | `[clear rank exp bonusrate]` | `IReadOnlyList<double>` / `Numbers` |

## PvfExpertSkillContractDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `TypeField` | `[type]` | `int` / `Int32` |
| `OverSkillField` | `[over skill]` | `int` / `Int32` |

## PvfExtraDropContentSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `SpecialMonsterDropField` | `[special monster drop]` | `int` / `Int32` |
| `CreationRateField` | `[creation rate]` | `int` / `Int32` |
| `GradeField` | `[grade]` | `int` / `Int32` |
| `DifficultyForExtraDropField` | `[difficulty for extra drop]` | `IReadOnlyList<string>` / `Strings` |
| `DungeonTypeForExtraDropField` | `[dungeon type for extra drop]` | `IReadOnlyList<string>` / `Strings` |
| `EquipmentTypeField` | `[equipment type]` | `string` / `Custom` |
| `AncientDungeonField` | `[ancient dungeon]` | `int` / `Int32` |
| `DimensionDungeonField` | `[dimension dungeon]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfGoldCarryLimitSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `GoldLimitPairs` | `[gold limit from level]` | `IReadOnlyList<int>` / `Int32List` |

## PvfIndependentDropTableSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `IndependentDropField` | `[independent drop]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseIndependentDropField` | `[/independent drop]` | `bool` / `Marker` |
| `ListField` | `[list]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseListField` | `[/list]` | `bool` / `Marker` |
| `AncientDungeonField` | `[ancient dungeon]` | `int` / `Int32` |
| `DungeonTypeField` | `[dungeon type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `DimensionDungeonField` | `[dimension dungeon]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfInitialEquipmentDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `CreateEquipmentListField` | `[create equipment list]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseCreateEquipmentListField` | `[/create equipment list]` | `bool` / `Marker` |

## PvfInitialSkillDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `TypeField` | `[type]` | `string` / `String` |
| `InitialValueField` | `[initial value]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseInitialValueField` | `[/initial value]` | `bool` / `Marker` |
| `SkillField` | `[skill]` | `IReadOnlyList<int>` / `Int32List` |
| `CloseSkillField` | `[/skill]` | `bool` / `Marker` |

## PvfInventorySortKeySource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `RarityField` | `[rarity]` | `int` / `Int32` |
| `MinimumLevelField` | `[minimum level]` | `int` / `Int32` |
| `StackableTypeField` | `[stackable type]` | `string` / `Custom` |

## PvfItemSaleDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `BoosterInfoField` | `[booster info]` | `bool` / `Marker` |
| `CloseBoosterInfoField` | `[/booster info]` | `bool` / `Marker` |
| `ValueField` | `[value]` | `int` / `Int32` |
| `DurabilityField` | `[durability]` | `int` / `Int32` |
| `ImpossibleContentsField` | `[impossible contents]` | `IReadOnlyList<string>` / `Strings` |
| `NotSellField` | `[not sell]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CannotSellField` | `[cannot sell]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfItemUsageRuleSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `TotalUsableCountField` | `[total usable count]` | `int` / `Int32` |
| `CoolTimeField` | `[cool time]` | `int` / `Int32` |

## PvfItemUseRuleDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `StackableTypeField` | `[stackable type]` | `IReadOnlyList<PvfToken>` / `Custom` |
| `CoolTimeField` | `[cool time]` | `int` / `Int32` |
| `CooltimeGroupField` | `[cooltime group]` | `int` / `Int32` |
| `MinimumLevelField` | `[minimum level]` | `int` / `Int32` |
| `ExpBonusRateField` | `[exp bonus rate]` | `double` / `Single` |
| `StatChangeDurationField` | `[stat change duration]` | `int` / `Custom` |
| `UsableJobField` | `[usable job]` | `IReadOnlyList<string>` / `Strings` |
| `ActionUsablePlaceField` | `[action usable place]` | `IReadOnlyList<string>` / `Strings` |

## PvfMailItemDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `AttachTypeField` | `[attach type]` | `string` / `String` |
| `TradeLimitMaxField` | `[trade limit max]` | `int` / `Int32` |

## PvfMonsterDeclaredDropTableSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `ItemField` | `[item]` | `IReadOnlyList<int>` / `Int32List` |

## PvfNpcShopDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `NPCField` | `[NPC]` | `int` / `Int32` |
| `PriceField` | `[price]` | `int` / `Int32` |
| `AddPriceField` | `[add price]` | `int` / `Int32` |
| `DurabilityField` | `[durability]` | `int` / `Int32` |
| `RoleField` | `[role]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `BoosterInfoField` | `[booster info]` | `bool` / `Marker` |
| `CloseBoosterInfoField` | `[/booster info]` | `bool` / `Marker` |
| `NeedMaterialField` | `[need material]` | `IReadOnlyList<int>` / `Int32List` |
| `DailyPurchaseLimitField` | `[daily purchase limit]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ResetDailyPurchaseItemField` | `[reset daily purchase item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `AttachTypeField` | `[attach type]` | `string` / `String` |
| `CoinField` | `[coin]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `BuyCoinField` | `[buy coin]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CeraField` | `[cera]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `SeraField` | `[sera]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CashField` | `[cash]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `PriceTypeField` | `[price type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `SellInfoField` | `[sell info]` | `bool` / `Marker` |
| `CloseSellInfoField` | `[/sell info]` | `bool` / `Marker` |
| `UseCategoryField` | `[use category]` | `string` / `String` |
| `CategoryEntryField` | `[category entry]` | `bool` / `Marker` |
| `CloseCategoryEntryField` | `[/category entry]` | `bool` / `Marker` |
| `IdField` | `[id]` | `int` / `Int32` |
| `ItemListField` | `[item list]` | `IReadOnlyList<int>` / `Int32List` |
| `TabField` | `[tab]` | `bool` / `Marker` |
| `CloseTabField` | `[/tab]` | `bool` / `Marker` |
| `CloseItemListField` | `[/item list]` | `bool` / `Marker` |

## PvfPackageOpeningDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `LotteryUseCostField` | `[lottery use cost]` | `int` / `Int32` |
| `LotteryUseNeedItemField` | `[lottery use need item]` | `IReadOnlyList<int>` / `Int32List` |
| `IntDataField` | `[int data]` | `IReadOnlyList<int>` / `Int32List` |
| `RandomboxField` | `[randombox]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseRandomboxField` | `[/randombox]` | `bool` / `Marker` |
| `BoosterInfoField` | `[booster info]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseBoosterInfoField` | `[/booster info]` | `bool` / `Marker` |
| `BoosterSelectCategoryField` | `[booster select category]` | `IReadOnlyList<int>` / `Int32List` |
| `CloseBoosterSelectCategoryField` | `[/booster select category]` | `bool` / `Marker` |
| `BoosterSelectionNumField` | `[booster selection num]` | `int` / `Int32` |
| `NeedMaterialField` | `[need material]` | `IReadOnlyList<int>` / `Int32List` |
| `SealingRemovalItemField` | `[sealing removal item]` | `IReadOnlyList<int>` / `Int32List` |
| `AvatarField` | `[avatar]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `PackageDataIncludeUsablePeriodField` | `[package data include usable period]` | `IReadOnlyList<int>` / `Int32List` |
| `BroadcastField` | `[broadcast]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `UsableCountLimitField` | `[usable count limit]` | `int` / `Int32` |
| `UsableCountField` | `[usable count]` | `int` / `Int32` |
| `UseCountLimitField` | `[use count limit]` | `int` / `Int32` |
| `PackageDataField` | `[package data]` | `IReadOnlyList<int>` / `Int32List` |
| `EquipmentField` | `[equipment]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `EtcField` | `[etc]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CreatureField` | `[creature]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `SpecialAvatarField` | `[special avatar]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `UsablePeriodField` | `[usable period]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ExpirationDateField` | `[expiration date]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `TypeField` | `[type]` | `int` / `Int32` |
| `ItemField` | `[item]` | `int` / `Int32` |
| `TermField` | `[term]` | `int` / `Int32` |
| `IsTermUnitMinuteField` | `[is term unit minute]` | `int` / `Int32` |
| `StackableTypeField` | `[stackable type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseItemField` | `[/item]` | `bool` / `Marker` |

## PvfPremiumServiceDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `FreeStaminaField` | `[free_stamina]` | `IReadOnlyList<int>` / `Int32List` |
| `PlusGoldcardField` | `[plus_goldcard]` | `IReadOnlyList<int>` / `Int32List` |
| `BuffServiceField` | `[buff_service]` | `IReadOnlyList<int>` / `Int32List` |
| `AutoRepairField` | `[auto_repair]` | `IReadOnlyList<int>` / `Int32List` |
| `BonusExpField` | `[bonus exp]` | `int` / `Int32` |
| `ExpField` | `[exp]` | `int` / `Int32` |
| `QuestHelperField` | `[quest_helper]` | `IReadOnlyList<int>` / `Int32List` |
| `LotteryOnemoreField` | `[lottery_onemore]` | `IReadOnlyList<int>` / `Int32List` |
| `TypeField` | `[type]` | `int` / `Int32` |
| `IndependentDropRateField` | `[independent drop rate]` | `IReadOnlyList<int>` / `Int32List` |
| `OverSkillField` | `[over skill]` | `int` / `Int32` |
| `OverEquipinfoField` | `[over equipinfo]` | `IReadOnlyList<int>` / `Int32List` |

## PvfQuestDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `NameField` | `[name]` | `string` / `String` |
| `GradeField` | `[grade]` | `string` / `String` |
| `JobField` | `[job]` | `IReadOnlyList<string>` / `Strings` |
| `LevelField` | `[level]` | `IReadOnlyList<int>` / `Int32List` |
| `GrowTypeField` | `[grow type]` | `int` / `Int32` |
| `NpcIndexField` | `[npc index]` | `int` / `Int32` |
| `CompleteNpcIndexField` | `[complete npc index]` | `int` / `Int32` |
| `TypeField` | `[type]` | `string` / `String` |
| `SubTypeField` | `[sub type]` | `int` / `Int32` |
| `IntDataField` | `[int data]` | `IReadOnlyList<int>` / `Int32List` |
| `MonsterRewardItemField` | `[monster reward item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `EnemyRewardItemField` | `[enemy reward item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `DependGiveItemField` | `[depend give item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `DifficultyField` | `[difficulty]` | `string` / `String` |
| `RewardTypeField` | `[reward type]` | `string` / `String` |
| `RewardIntDataField` | `[reward int data]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `RewardSelectionIntDataField` | `[reward selection int data]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `GoldMultipleField` | `[gold multiple]` | `int` / `Int32` |
| `ExposedByNpcField` | `[exposed by npc]` | `int` / `Int32` |
| `EventField` | `[event]` | `int` / `Int32` |
| `CreatureKindField` | `[creature kind]` | `int` / `Int32` |
| `CreatureLevelField` | `[creature level]` | `int` / `Int32` |
| `ExpertjobLevelField` | `[expertjob level]` | `IReadOnlyList<int>` / `Int32List` |
| `TargetCharacterField` | `[target character]` | `IReadOnlyList<string>` / `Custom` |
| `JobChangeQuestField` | `[job change quest]` | `int` / `Int32` |
| `PreRequiredQuestField` | `[pre required quest]` | `IReadOnlyList<int>` / `Int32List` |
| `PreRequiredQuestAnswerField` | `[pre required quest answer]` | `IReadOnlyList<int>` / `Int32List` |
| `CollisionQuestField` | `[collision quest]` | `IReadOnlyList<int>` / `Int32List` |
| `AccountCollisionQuestField` | `[account collision quest]` | `IReadOnlyList<int>` / `Int32List` |
| `SpecialRewardStatusField` | `[special reward status]` | `bool` / `Custom` |
| `IgnoreQuestLevel4ExpField` | `[ignore quest level 4 exp]` | `int` / `Int32` |

## PvfQuestParameterSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `DifficultyField` | `[difficulty]` | `IReadOnlyDictionary<char, int>` / `Custom` |
| `ExpRewardTableField` | `[exp reward table]` | `IReadOnlyList<int>` / `Custom` |
| `GoldRewardTableField` | `[gold reward table]` | `IReadOnlyList<int>` / `Custom` |
| `GreenLevelPenaltyField` | `[green level penalty]` | `int` / `Int32` |
| `GreyLevelPenaltyField` | `[grey level penalty]` | `int` / `Int32` |
| `EpicGreenLevelPenaltyField` | `[epic green level penalty]` | `int` / `Int32` |
| `EpicGreyLevelPenaltyField` | `[epic grey level penalty]` | `int` / `Int32` |

## PvfRecipeContentSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `InputItemField` | `[input item]` | `IReadOnlyList<int>` / `Int32List` |
| `OutputItemField` | `[output item]` | `IReadOnlyList<int>` / `Int32List` |
| `StackableTypeField` | `[stackable type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `NeedSkillField` | `[need skill]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `IntDataField` | `[int data]` | `IReadOnlyList<int>` / `Int32List` |
| `SuccessRateField` | `[success rate]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `FailureItemField` | `[failure item]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `FailItemField` | `[fail item]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfSkillPointDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `SpecialPurchaseCostField` | `[special purchase cost]` | `IReadOnlyList<int>` / `Int32List` |
| `PurchaseCostField` | `[purchase cost]` | `IReadOnlyList<int>` / `Int32List` |
| `SpTableField` | `[sp table]` | `IReadOnlyList<int>` / `Int32List` |
| `FeatureSkillTypeField` | `[feature skill type]` | `IReadOnlyList<int>` / `Int32List` |
| `PreRequiredSkillField` | `[pre required skill]` | `IReadOnlyList<int>` / `Int32List` |
| `NameField` | `[name]` | `string` / `String` |
| `ExplainField` | `[explain]` | `string` / `String` |
| `MaximumLevelField` | `[maximum level]` | `IReadOnlyList<int>` / `Int32List` |
| `GrowtypeMaximumLevelField` | `[growtype maximum level]` | `IReadOnlyList<int>` / `Int32List` |
| `SecondGrowtypeMaximumLevelField` | `[second growtype maximum level]` | `IReadOnlyList<int>` / `Int32List` |
| `RequiredLevelField` | `[required level]` | `int` / `Int32` |
| `RequiredLevelRangeField` | `[required level range]` | `int` / `Int32` |
| `FixedLevelSkillField` | `[fixed level skill]` | `int` / `Custom` |
| `FixedLevelSkillEndField` | `[/fixed level skill]` | `bool` / `Marker` |
| `ApplyContractSkillmasterField` | `[is apply contract skillmaster]` | `int` / `Int32` |
| `IntervalLevelField` | `[interval level]` | `int` / `Int32` |
| `AddLevelPerIntervalField` | `[add level per interval]` | `int` / `Int32` |
| `TypeField` | `[type]` | `string` / `String` |
| `SkillClassField` | `[skill class]` | `int` / `Int32` |
| `SkillFitnessGrowtypeField` | `[skill fitness growtype]` | `IReadOnlyList<int>` / `Int32List` |
| `InitialValueField` | `[initial value]` | `bool` / `Marker` |
| `SkillField` | `[skill]` | `IReadOnlyList<int>` / `Int32List` |
| `AwakeningSkillField` | `[awakening skill]` | `IReadOnlyList<int>` / `Int32List` |

## PvfStackableActionDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `ActionTypeField` | `[action type]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `MinimumLevelField` | `[minimum level]` | `int` / `Int32` |
| `MaximumLevelField` | `[maximum level]` | `int` / `Int32` |
| `ExpirationDateField` | `[expiration date]` | `IReadOnlyList<PvfToken>` / `Tokens` |

## PvfTownDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `TownMovableAreaField` | `[town movable area]` | `IReadOnlyList<int>` / `Int32List` |
| `CloseTownMovableAreaField` | `[/town movable area]` | `bool` / `Marker` |
| `VirtualMovableAreaField` | `[virtual movable area]` | `IReadOnlyList<int>` / `Int32List` |
| `CloseVirtualMovableAreaField` | `[/virtual movable area]` | `bool` / `Marker` |
| `AreaField` | `[area]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `CloseAreaField` | `[/area]` | `bool` / `Marker` |
| `PermissionField` | `[permission]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `ClosePermissionField` | `[/permission]` | `bool` / `Marker` |
| `NameField` | `[name]` | `string` / `String` |
| `MinimapOpen` | `[minimap rect]` | `IReadOnlyList<PvfToken>` / `Tokens` |
| `MinimapClose` | `[/minimap rect]` | `bool` / `Marker` |

## PvfTutorialRewardDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `EscaladeTutorialRewardField` | `[escalade tutorial reward]` | `IReadOnlyList<int>` / `Int32List` |
| `CloseEscaladeTutorialRewardField` | `[/escalade tutorial reward]` | `bool` / `Marker` |

## PvfVendingMachineDefinitionSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `ItemGroupField` | `[item group]` | `bool` / `Marker` |
| `GroupNumField` | `[group num]` | `int` / `Int32` |
| `MaterialField` | `[material]` | `IReadOnlyList<int>` / `Int32List` |
| `OutputField` | `[output]` | `IReadOnlyList<int>` / `Int32List` |
| `CloseOutputField` | `[/output]` | `bool` / `Marker` |
| `CloseItemGroupField` | `[/item group]` | `bool` / `Marker` |

## PvfWorldDropTableSource.cs

| 声明 | PVF 名称 | 结果类型 / 形状 |
|---|---|---|
| `WorldDropField` | `[world drop]` | `IReadOnlyList<int>` / `Int32List` |
| `CloseWorldDropField` | `[/world drop]` | `bool` / `Marker` |

## 无标签或按位置的固定序列

| 入口 / 具名形状 | 固定形状及约束 |
|---|---|
| `PvfListFile.Read` / list-entry pair | 无外围字段的 Int32 ID 与 string 相对路径配对；保留原有 pending-ID、空路径及空结果处理 |
| `PvfEquipmentDefinitionSource.ReadPaths` / equipment index pair | 严格正 Int32 ID + QuotedString 相对路径；重复 ID、非法路径、截断脚本仍带 item/path 诊断 |
| `PvfDungeonExperienceProgressionPolicySource.ReadAllInts` / character experience threshold sequence | `character/ExpTable.tbl` 全文件整数投影；等级计数与既有 uint 转换约束 |
| `PvfCreatureRuleSource.Build` / creature experience threshold sequence | `creature/ExpTable.tbl` 全文件整数投影；首值非负且后续严格递增 |
| `PvfEquipmentDisjointDefinitionSource.ReadEquipmentSaleRate` / positional price triple | `equipment/pricetable.tbl` 第一节（本地化名称）三个非负 Int32，普通出售取首列，随机属性破坏取第三列 |
| `PvfItemSaleDefinitionSource.Read` / positional price triple | 同一第一节三列；保留重复/负数/类型失败拒绝 |

GoldCarryLimit 原来全文件整数投影，现已绑定真实固定 `[gold limit from level]` 的 Int32List；后续百级配对和非负限额投影保留。

## 明确保留的动态及值入口

- `Tool/PvfProbe/Program.cs` 的 `values`、`where`：`args[2]` 是使用者运行时请求的任意字段；固定 `name` 查询已用 `ProbeFields.Name`。
- `PvfNpcShopDefinitionSource.Sections`、equipment 字典与 stream scanners：归档中的未知 Identifier 字段名作为字典键或有序记录保留；固定读取侧参数均为声明。
- 通用 `[/` 判断和按未知 opening name 构造 closing name 是结构作用域算法，支持未知合法块。
- `ArchiveJobFamilyTokens`、`PvfEquipmentSlots`、物品种类、NPC role、Package type、任务等级/奖励种类、掉落 kind/mask 都是字段值枚举，不是字段声明。
- `ChannelScriptExperienceSource` 是频道文本配置，不属于 PVF。



## 固定投影读取补齐

- CharacterStat 的 Number 标量按 initial / growtype / awakening 当前块 Decode 完整字段；同块重复拒绝，跨块同名合法；未知字段保留原首数值投影。
- ExperienceParameter 的5个固定字段逐节 Decode；合法重复列表按序追加，重复标量均验证并保留第一节投影；未知字段继续跳过。
- InventorySortKey 的 rarity/minimum level 以及混合 stackable type 通过 PvfDocument.TryRead；缺失仍保留0/空类型，存在但类型、数量或重复损坏则拒绝。
- EquipmentDurability 通过 PvfDocument.TryRead，缺失仍0，存在时完整 Int32 与 UInt16 范围校验。
- DungeonExperience capsule 标量通过 TryRead；honor 每个 grade 块独立 Custom Decode，合法重复 grade 保留并累加旧整数投影；关闭标记和 maxlevel 标量同样 Decode。
- `PvfTypedProjectionTests` 新增22项无外部档案合成用例，覆盖上述合法投影、不同作用域重复和损坏值拒绝。本补齐轮未执行dotnet，交由父工作流串行验证。
