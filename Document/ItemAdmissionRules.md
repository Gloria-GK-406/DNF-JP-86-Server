# 物品 flag 准入规则

日期：2026-10-05；迁移基线：`b3cd7909`。这里描述服务端容器准入，不是客户端协议结论。

`ItemTraits` 描述物品的模板特征；`ItemAdmissionRule` 描述容器接受哪些特征组合。
`ItemContainerAdmission.For(space)` 给出业务空间的规则，业务容器通过 `Admission` 暴露它。

| 掩码 | 条件 | `None` 的含义 |
|---|---|---|
| `Allowed` | 物品所有标志都必须包含在允许集合中 | 拒绝全部物品 |
| `All` | 物品必须具备全部指定标志 | 没有必需标志 |
| `Any` | 物品至少具备一个指定标志 | 没有候选组要求 |

三项条件同时成立才准入，物品自身为 `None` 时始终拒绝。`default(ItemAdmissionRule)` 同样拒绝全部。
构造时拒绝 `All` 不属于 `Allowed`、或非空 `Any` 与 `Allowed` 没有交集的规则。
`Any` 可以包含不允许的候选，只要其中至少一个被允许；命中候选不豁免 `Allowed` 检查。
规则属性只读，避免构造后绕过校验修改掩码。

例如允许宠物、消耗品及账号共享特征，并要求“宠物 + 消耗品”：

```csharp
var rule = new ItemAdmissionRule(
    allowed: ItemTraits.Creature | ItemTraits.Consumable | ItemTraits.AccountShared,
    all: ItemTraits.Creature | ItemTraits.Consumable);
```

普通容器保留原允许集合，`All` 和 `Any` 都为空。2026-10-05 增加 Tower 特征与独立塔快捷栏，其准入要求如下。
所有 flag 准入调用统一使用 `Matches`；现有容量、槽位、堆叠、所有权和 PVF 身份校验继续独立执行。
装备查找中的 `Allowed == None` 仅用于跳过没有物品准入的业务空间，不用于判断某件物品是否可放入。

塔快捷栏使用 `Allowed = Tower | Consumable | Material`、`All = Tower`、`Any = Consumable | Material`。普通容器的 Allowed 不含 Tower，因此塔物品不能流入普通背包。
实现与验收边界见[死亡塔快捷栏](TowerQuickSlots-20261005.md)。没有增加规则 OR 组合或表达式引擎。
