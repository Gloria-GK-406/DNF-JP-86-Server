# Routing Decision

- TaskContract: `.rks-agent/tasks/inventory-slot-boundary/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标、协议/业务边界、六条受影响 CMD、异常语义、持久化兼容与可观察验收均已明确。中心方向是单一双向映射与原子槽位值对象；具体命名和适配结构可在 Spec 与 Plan 中选择，不存在会改变任务语义的开放问题。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求先扫描、建契约并防止新旧坐标逻辑并存。Direct 最容易遗漏某条命令的输入或回包边界，留下能编译却拥有两个真源的半迁移状态；持久 Spec、显式迁移计划、完整结果审查和新鲜验证能直接降低该风险。所有变化共同服务于一个不可分割的槽位边界，不能独立接受或回滚，因此使用一个 Single Spec。
- Reroute conditions: 若扫描发现某一库存子系统必须进行独立数据库格式迁移、具有独立验收/回滚边界，或现有协议证据无法确定必须支持的槽位区段，则暂停并重新评估 Multi Spec 或任务语义。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 的最终审查不足以约束公共类型、六条协议链、领域容器访问与持久化适配的有序迁移；一个显式 Plan 能先建立值对象和映射，再迁移入口、领域和出口，避免中间状态反向固化。该任务不涉及安全、授权、不可逆外部写入或不可恢复数据迁移，普通独立完整结果审查已足够，不需要 Governed 的额外挑战层。
