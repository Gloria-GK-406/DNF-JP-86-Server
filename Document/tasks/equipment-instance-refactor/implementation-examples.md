# 装备实例和规则服务：典型操作

这些例子说明当前代码（CurrentImplementation），不证明新协议或实际客户端效果。实例接口见 `Server/src/Dfo.Server.Abstractions/Inventory/EquipmentInstances.cs`，规则接口见同目录 `EquipmentCultivationRules.cs`。

## 拿到一件装备并修改基本状态

在CommandBus开启的事务内取得装备；owner是角色，UID是装备身份，slot仅是当前所在位置。

```csharp
var equipment = await equipments.Of(owner).FindAsync(itemUid, ct)
    ?? throw new InvalidOperationException("装备不存在或不属于该角色");

await equipment.SetReinforcementLevelAsync(3, ct);
await equipment.SetEnchantmentAsync(cardId, ct);
await equipment.DecreaseDurabilityAsync(1, ct);
var latest = await equipment.ReadAsync(ct);
```

每次操作都按UID读取当前暂存状态；第二个句柄修改附魔后，第一个句柄再改耐久也能保留附魔。移动装备后句柄跟随身份；同模板另一件装备不会被替换。事务提交/回滚后需要重新取实例；新建但尚未分配持久化UID的装备不会提前交出实例。

其他基本操作为 `SetForgingLevelAsync`、`SetDimensionalAttributeAsync(type, value)`、`SetMagicSealOptionsAsync(options)`、`SetDurabilityAsync`、`DestroyAsync`。魔法封印一次设置整个选项组，同时清理原转换临时状态；销毁后所有旧句柄失效。耐久归零和销毁是两个操作。

## 强化：报价、决定、结算、应用

强化领域服务实际使用如下顺序，省略原会话、材料、金币、保护券、产物空间和receipt代码：

```csharp
var equipment = await equipments.Of(session.CharacterId).FindAtAsync(targetSlot, ct);
var facts = await equipment!.ReadAsync(ct);
var quote = await rules.QuoteAsync(facts.Core, ct);
if (quote.Rejection != EquipmentReinforcementRejection.None)
    return new(quote.Rejection);
var rule = quote.Rule!;

// 使用rule.GoldCost、MaterialItemId、MaterialCount检查资源。
// 保护券库存/期限由领域检查；适用范围和优先级由rule决定。
// 原资源准入通过后才抽取随机值。
var succeeded = rule.TryReinforce(rolls.Next(100_000));
var decision = succeeded
    ? new EquipmentReinforcementFailureResult(
        EquipmentReinforcementRejection.None,
        EquipmentReinforcementOutcome.Succeeded,
        (byte)(facts.Core.ReinforcementLevel + 1), false, [])
    : await failureRules.GetResultAsync(equipment,
        new(protectionDefinition, rolls.Next(10_000), rolls.Next(10_000)), ct);
if (decision.Rejection != EquipmentReinforcementRejection.None)
    return new(decision.Rejection);

// 先检查产物空间，再在同一事务中结算材料、保护券和金币。
if (decision.Outcome == EquipmentReinforcementOutcome.Destroyed)
    await equipment.DestroyAsync(ct);
else
    await equipment.SetReinforcementLevelAsync(decision.NewLevel, ct);

// decision.Rewards 包含本次完整产物，不只有强化痕迹。
// 同事务添加这些产物、返回receipt；提交以后发送通知。
```

强化规则判断此次是否成功；失败规则独立决定保留/降级/销毁、保护券消耗及全部材料。实例执行等级修改或销毁，领域把扣费和发奖统一结算。普通强化等级为 `ReinforcementLevel`，读取 `Attribute` 低5位；实例写低5位并保留高3位密封次数。`EnchantUpgradeCount` 是独立的附魔升级次数，强化不修改它；82字节存储和wire布局保持。此处根据2026-09-30实机失败修正此前错误字段解释。

实际代码：`Application/Inventory/EquipmentReinforcementDomainService.cs`、`EquipmentReinforcementRuleService.cs` 和 `EquipmentReinforcementFailureRuleService.cs`。`TryReinforce`接受显式draw并返回bool。失败服务的instance重载只读取并冻结facts；计算只依赖facts、静态内容、显式保护和奖励draw，不写实例、不读玩家库存。

例如稀有装备原+11尝试+12失败且无保护，`decision.Rewards`包含基础无色、品质分解产物和1个强烈之痕迹；普通白装有基础无色但没有痕迹。保护生效时产物列表为空。数量采用[70静态证据](verification/failure-reference.md)及实际A21内容，新的GUI与重登结果按[失败验收清单](Failure-Human-Test-List.md)验证。

## 其他已有业务的落点

| 业务 | 规则决定 | 实例行为 |
|---|---|---|
| 普通强化 | ReinforcementRule报价/成败，FailureRule.GetResult处罚/完整奖励 | SetReinforcementLevel / Destroy |
| 珠子附魔 | EnchantRule.CardAsync，含升级珠子拒绝 | SetEnchantment |
| 异次元处理 | 既有OutworldRule.DecideAsync | SetDimensionalAttribute |
| 魔法封印解封/转换 | 既有MagicSealRule.DecideAsync | SetMagicSealOptions |
| 锻造 | 既有ForgingRule.DecideAsync | 成功时SetForgingLevel |
| 普通修理 | RepairRule报价：费用/耐久/是否写 | SetDurability |
| 自动修理 | RepairRule目标耐久；领域检查资格/额度 | SetDurability |
| 耐久消耗 | 既有允许装备槽准入 | DecreaseDurability |
| 既有分解适配 | 原DisjointRule与内容准入 | Destroy |

异次元字段仍是 `AmplifyType/AmplifyValue`，本次没有新增尚未实现的增幅玩法。分解只统一装备facts/销毁，原工具内容准入旁支没有扩张为另一次重构。剩余通用容器写入属于工具、材料产物或空间模拟；五项养成及修理/耐久的装备字段写入已经统一到实例。

## 最后验收

当前失败服务已部署ready，按[失败验收清单](Failure-Human-Test-List.md)执行新损毁材料及重登验证；原[Human-Test-List](Human-Test-List.md)未确认条目继续保留。本任务在用户明确反馈前保留为待验；其他四项完整GUI仍按原养成任务的清单接续。
