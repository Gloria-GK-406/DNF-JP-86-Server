# Routing Decision

- TaskContract: `Document/tasks/equipment-development/attempts/spec-002-equipment-enchantment/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

装备附魔的目标、材料、实例身份、资产原子性和客户端结果边界已由根 SC-2 与本子合同明确。具体 opcode、附魔源/目标适配、覆盖规则仍需从 70、上游、A21、PVF 取证，但不改变其独立可验收的功能边界。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 一条装备附魔闭环可独立审查和封存，宠物目标分支必须隔离验证；单一 Spec 可将证据、方案、实现与结果审查绑定，避免误改已接受普通强化。
- Reroute conditions: 若 A21 客户端证实装备与宠物附魔无法分离接受，或关键资产语义只能靠尚无的实帧才能判定，回根控制器评估边界/证据门；若工作树出现不属本任务的改动，先分类所有权。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 装备附魔材料/目标适配、宠物目标隔离与已接受实例持久化相互耦合，若先写 handler 再补证据，错误的列表类型或覆盖语义会传播到事务和投递。显式 Plan 可先排序 70/上游/A21/PVF 取证、领域结算和协议验证；Lite 仅末端审查不如早期方案收敛。没有无恢复的数据外部变更，故不升级 Governed。
