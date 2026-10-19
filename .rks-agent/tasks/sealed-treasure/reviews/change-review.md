---
kind: rks-review
verdict: approved
target: 28f6aba6e02d0868fc5e65ab6a0a2d0f48531c9f
baseline: 9907b3b39648bb275a169b9c6e5855b85753d6f3
scope: complete-mixed-result
contract_revision: 1
---

# 封印的藏宝地完整结果独立审查

## 审查绑定

- 审查对象：`task/sealed-treasure-20261019` 的目标提交 `28f6aba6`，相对 planning 提交 `9907b3b3` 的完整 17 文件差异。
- 治理来源：同目录 `TaskContract.md` revision 1、`spec.md`、`routing-decision.md`，以及工作区与仓库 `AGENTS.md`。
- 适用标准：`reviewing-work` 的 Code 与 Deliverable 混合结果标准。
- 独立性：直接审读实际 diff、当前邻接源码、研究来源、实现文档和 TRX；生产报告只作为证据定位，不替代代码检查。

## 结论

`approved`。在本轮已授权的服务端实现、自动验证和验收文档范围内，没有未解决的阻断性缺陷。该结论不包含部署，也不把原生 UI、ACT 表现或 `0x033F` 实机行为提升为 `WireCaptured` 或 `ClientVerified`。

## 关键要求核查

| 要求 | 实际结果与决定性证据 | 判定 |
|---|---|---|
| 全类 `[spicies dungeon]` 免疲劳 | `PvfDungeonDefinitionSource` 在通用定义解析处读取标记并统一令 `ConsumesFatigue=false`；`DungeonStructureRuleService` 把该值带入运行布局，准入、首房和首次新房消费均读取同一布局值，重访本来不消费。真实 PVF 测试同时确认 3557 免疲劳、普通 70 仍耗疲劳。 | 满足 RQ-001 / AC-001。 |
| 3557 活动、准入与 `0x033F` 接线 | 2331 作为独立可管理活动注册，玩家投影以 dungeon 3557 生成独立 `ScheduledDungeonState`；既有 encoder 发送 category 0、state 1/0、content id 3557。专用入口检查活动 active、正常请求、非练习、非 cycle/mirror、单人、无既有运行；内容规则固定 70 级下限、唯一难度 2、单人及禁练习。 | 满足 RQ-002..003 / AC-002..003；客户端可见性正确保留人工验收。 |
| 双票整组替代、冻结与事务 | 通用准入先以完整活动材料组做可满足性检查，成功时整组替代普通组；组内仍由材料服务逐项 AND 校验。显式 `RequiredItemsOverride` 优先，保护南部溪谷、牛头怪乐园等专用入口。所选组写入 admission permit，消费时只重新核验和扣除该冻结组；批次只能消费一次。建局回调拒绝或异常会把环境事务标记为回滚，材料扣除不能单独提交。四种持票组合、活动组不完整回退、普通多材料 AND、重复消费及回调拒绝回滚均有通过测试。 | 满足 RQ-004 / AC-004。 |
| 元素状态和四路门禁 | 暗/火/光/水的怪物、封印石及中央相邻格映射与静态内容一致。运行聚合没有第二份元素布尔状态，而是从 `CommitActorDeathChange` 成功后写入的 `DungeonKillRecord` 派生；预留或释放死亡不会解锁。移动与封印石销毁都读取该已提交 kills 集合；状态随运行跨房保存，新运行自然清空。四组逐项、其他三路保持关闭、回房、新局与未提交死亡均有通过测试。 | 满足 RQ-005 / AC-005。 |
| 15150 声明掉落时序与幂等 | 15150 不附加四元素齐备条件。声明物品仍在建房时冻结并保留 object index 关联；拾取前检查对应 SpecialPassive 物件是否已提交消费。仅有销毁 reservation 或销毁回滚时物件仍未消费，因此拒领；销毁提交后才允许既有拾取事务继续。物件队列、reservation 和地面物品 claim/commit 共同阻止重复销毁、重复领取和重复产出。销毁前、回滚后、提交后一次领取及重试拒绝均有通过测试。 | 满足 RQ-006 / AC-006。 |
| PVF 生产内容与兼容回归 | 真实 PVF 相关的 Application 回归 149 项与 Infrastructure 解析回归 88 项共同覆盖本要求：前者的 `SealedTreasureTests` 由生产工厂物化 25 图，并核对 257 个怪物放置、47 种怪物代码、四精灵、四封印石、15150 以及两种白金箱各 147 项、总权重 100000；后者确认 spicies、活动票、人数、练习字段及 DGN3700 重复声明的解析兼容。`[prohibit practice]` 采用最后声明，兼容 DGN3700 的重复 0/1；南部/牛头专用 `RequiredItemsOverride` 不会被活动票逻辑改写。无 PVF 的 8 个 TRX 合计 7537 项，其中 6664 passed、873 skipped、0 failed；末次聚焦复验另有 `sealed-final.trx` 28 passed 和 `sealed-pvf-final.trx` 88 passed。 | 满足 RQ-007 / AC-007，并保护普通图及既有活动路径。 |
| 实现文档 | `Document/SealedTreasure-20261019.md` 与代码一致地记录当前策略、证据等级、兼容处理、自动验证和人工验收矩阵；明确未部署，且原生入口 UI、`0x033F`、ACT、声明物件显示、完整路线、放弃/断线均待实机，不含未经支持的 ClientVerified 声明。 | 交付完整、可用于下一轮部署与人工验收。 |

## 证据矛盾处置

`artifacts/sealed-treasure-tests/sealed-focused.trx` 是 15:46 的较早测试运行，包含一个“无两票”参数用例的夹具清理失败：断言辅助函数读取了未注册的 Material 容器，失败发生在测试 `Count`，不是生产入口逻辑。15:47 的后续 `sealed-related.trx` 已在目标代码上覆盖同一理论的四组参数并全部通过，随后真实 PVF 与完整无 PVF 回归也均为零失败。因此该早期工件不构成当前未解决缺陷。

## 保留风险与后续边界

- `0x033F` 对 3557 的原生客户端可见效果、PVF ACT 自动销毁/提示、声明掉落的客户端显示时机、完整路线、死亡/放弃/断线行为仍需部署后按文档人工矩阵验证。
- 自动测试和静态 PVF 证明的是 `CurrentImplementation` 与内容兼容性；本审查不授予部署权限，也不证明 A21 原服行为。
