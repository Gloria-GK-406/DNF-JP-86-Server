# TASK-002 — 普通强化规则与原子结算

## Task Unit Contract

- Revision: 2（重复报文的序列去重改由拥有 `GameMessageMetadata.Sequence` 的 TASK-003 负责；领域层不接触线协议序列）
- Outcome and references: 让普通强化有真实 A21 PVF 驱动的规则输入，并在角色拥有的装备实例上原子结算成功、失败、材料、金币、保护和破坏产物；对应 Plan T-002、Spec RQ-002—RQ-004 / AC-002—AC-004。[证据与设计](../evidence-and-design.md)是本单元的规则边界。
- Owned boundary: `Server/src/Dfo.Server.Abstractions/{Content,Inventory}/`、`Server/src/Dfo.Server.Infrastructure/Content/`、`Server/src/Dfo.Server.Application/Inventory/`、相关 `Server/tests/`。本单元不接 `0x0050` 网络切片，不修改客户端或 70/上游样本；新增依赖在各自组合根注册。
- Required checks: PVF 真实样本解析和无效数据拒绝；目标/材料/金币/锁定/耐久校验；确定性成功、保留、降级、保护和破坏；库存/金币/产物原子性，模拟提交失败及 SQLite 重读；解决方案构建与对应测试。模式解析、同序列重放与命令总线账户串行检查归 TASK-003。
- Dependencies: TASK-001 已完成；使用同一干净基线和现有宠物变更，不破坏其功能。

## Detailed Plan

1. 先沿 `EquipmentDisjointDomainService`、`EquipmentRepairDomainService`、`MaterialConsumptionDomainService` 与 `DomainTransaction` 确定容器、金币、材料、产物在同一个命令事务下的实际提交接缝；写下不可跨越的原子性边界，并设定可控随机输入。不要把 70 `use_money` 的先后顺序机械转成多个独立提交。
2. 在内容层增加普通强化定义源。解析真实 A21 `etc/upgrade.etc` 的 `normal` 19 列行、费用权重/等级、破坏阈值及 `[disjoint]`；从目标装备 PVF 读取等级、稀有度、类型、满耐久和不可强化标记，从材料 PVF 识别普通/超级保护脚本。缺失、错行和非有限数值均拒绝。用最小构造脚本和真实 PVF 样本测试格式及 +1/+5/破坏阈值。
3. 先写领域拒绝与随机边界的失败测试：非本角色/错槽和模板、错模式、脏/锁定/增幅目标、材料或金币不足、无容量破坏、重复及并发；再实现纯规则决策与领域结算。复制原装备实例，只更新强化等级；保护券仅在适用失败分支消耗，成功不消耗保护券。未证实的活动/炼金加成保持禁用。
4. 在同一事务中预计划破坏产物及放置，扣金/材料/保护券和更新/删除目标，提交失败回滚全部。若破坏产物无法精确实现，明确返回 UnsupportedOutcome，保留全部资产，绝不暗改成其他惩罚。对非破坏路径也验证目标无关字段不变。
5. 只在业务结果里返回已提交状态和受影响槽位/金币供 TASK-003 投递；不在领域层编造 A21 回包。执行目标测试、SQLite 重读和 `dotnet build Server/86JP.Server.slnx --no-restore`，记录真实结果与剩余阻断。

## Implementation Notes

- 当前服务的 `InventoryItemCoreState.EnchantUpgradeCount` 是持久化等级，`IBusinessInventorySlotDomainService` 给角色绑定槽位，`EquipmentDisjointDomainService` 有破坏产物预计划样例。复用要受真实事务约束，不能仅因为使用同一接口就假定跨多个容器自动原子。
- 保护券标签的 PVF 证据：`[protect equipment]` 与 `[new protect equipment] 10`；自动选券优先级尚非 ClientVerified。候选策略须记录为 UpstreamReference/PVF 推断，并通过显式槽位或保守拒绝避开误消耗。
- 如真实 PVF 某些等级/稀有度配置缺位，返回可定位拒绝，不以 0 金币、100% 成功或空产物悄悄兜底。

## Result

- Changed: 新增 A21 PVF 普通强化表、装备和保护券定义源及解析测试；新增普通强化领域契约、随机源、规则决策、资产结算与应用/基础设施注册；没有改客户端/70/上游，也没有接 `0x0050` 报文。
- Why: `normal` 表的 19 列、金币权重和保护券 action 由真实 A21 PVF 提供；领域结算在现有命令事务内更新装备实例、角色金币、账户/物理材料与破坏产物。破坏产物先规划容量，不能交付时在任何资产写入前拒绝。
- Checks: 真实 A21 PVF 指定后 `PvfEquipmentReinforcementDefinitionSourceTests` 12/12；`EquipmentReinforcement*` Application 指定测试 23/23，覆盖边界权重、奖惩、保护、账户虚拟材料、无空间、金币提交失败回滚；Infrastructure 全量 403 通过/2 跳过；Application 全量隔离复跑 2011 通过/171 跳过；解决方案构建 0 警告、0 错误。最初并行跑两个测试套件时，一条未改动的疲劳药测试失败，随后单测及单独全量均通过，原因未证实，未改其代码。
- Remaining: 真实 A21 `0x0050` 命令、回包和库存/金币投影尚未接入；序列重放的幂等与到账可见性由 TASK-003 验证。用户 GUI 验收仍未执行。PVF 成本公式及产物数量依上游解释和 A21 表联合推断，标记为非 ClientVerified。
