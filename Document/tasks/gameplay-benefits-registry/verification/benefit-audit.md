# 经验、掉落和强化加成复扫

从角色经验入账、房间掉落冻结、独立掉落、任务道具掉落和普通装备强化的实际入口回查了加成来源。三个领域当前已启用的额外加成都进入通用权益查询。

| 领域 | 已核对的来源及消费入口 |
|---|---|
| 角色经验 | 账号附加费率、成长契约、黑钻、公会、频道、精灵秘药、冒险团、幸运本人及队友、疲劳燃烧由来源按场景声明，杀怪、副本结算和适用的 Boss 塔沿用既有经验算法。幸运任务奖励由任务交付独立查询相同的权益组件。[副本来源](../../../../Server/src/Dfo.Server.Application/World/ExperienceBenefitSources.cs) · [任务来源](../../../../Server/src/Dfo.Server.Application/Quests/QuestExperienceBenefits.cs) · [结算](../../../../Server/src/Dfo.Server.Application/World/ExperienceCalculationDomainService.cs) |
| 装备和道具掉落 | 全服掉率活动、幸运队伍共享神器、成长契约 Boss 装备、任务助手的个人和共享任务材料已迁移。幸运来源一队声明一次，额外平均 1.5 轮，仅保留神器装备并使用共享归属。Boss 塔与深渊的独立掉落入口已取消外传成长加成参数。[全服倍率](../../../../Server/src/Dfo.Server.Application/GameplayParameters/DropDrawMultiplierBenefits.cs) · [掉落来源](../../../../Server/src/Dfo.Server.Application/World/DropBenefitSources.cs) · [怪物掉落](../../../../Server/src/Dfo.Server.Application/World/MonsterDropDomainService.cs) |
| 普通装备强化 | 读角色、账号或全局的强化成功率权益。药剂按角色存截止时间，消耗、冷却和计时状态在同一事务提交。秘药同组取最大值，水晶之光独立叠加。仍用原成功率判定的一次随机抽样。[药剂来源](../../../../Server/src/Dfo.Server.Application/Inventory/ReinforcementPotionBenefits.cs) · [强化结算](../../../../Server/src/Dfo.Server.Application/Inventory/EquipmentReinforcementDomainService.cs) |

权益表述包含来源、作用范围、名称、顺序和可选有效区间。已存状态继续由原领域读出完整声明。运行时发布允许较新版本替换整个声明，空声明撤回，同一次查询保留不可变快照。持久化药剂由定时表直接声明，进程内发布表无需存一份副本。[公共合同](../../../../Server/src/Dfo.Server.Abstractions/Benefits/GameplayBenefits.cs)

## 仍由各玩法处理的规则

- 基础经验、队伍规模及难度、评价、塔奖励上限、经验书固定经验、祭坛及大会固定奖励和宠物装备加成按各自规则处理。疲劳实际扣除仍在副本入场和房间账本。落盘结算按原值恢复。
- 关卡掉落表、翻牌基础概率、专属奖励、扣费和开盒次数仍由各自玩法处理。掉落补偿扩展点 `IDropRateAdjuster` 目前只注册了原样返回实现，仓库内无生效补偿提供者。[默认掉落调整](../../../../Server/src/Dfo.Server.Application/World/NoDropRateAdjustment.cs)
- 强化券、增幅、勋章、锻造、分解返还及各自的伤亡或消耗规则继续独立处理。限时强化药只影响普通装备强化的成功率。

## 道具口径

当前 dev PVF 的 `10004687` 有数值 3000 和时长 180 秒，`490001533` 有数值 1000 和时长 1800 秒。强化判定以 100000 为满值，两者分别加 3 和 1 个百分点。水晶之光 `10007343` 的说明明确加 10 个百分点、持续 30 秒。两个旧版强化秘药 `2600025`、`2749959` 的 PVF 只说明提高概率，采用 ServerS4A21 的兼容值加 1 个百分点。

v49 → v50 迁移新增独立时效表。旧道具效果数值继续透传。新时效行在选角同步中按剩余秒投影，A21 客户端的图标和显示时机尚未实机核对。[数据库迁移说明](../../../DatabaseSchemaMigration.md)
