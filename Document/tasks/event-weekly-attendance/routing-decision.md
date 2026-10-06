# Routing Decision

- TaskContract: `Document/tasks/event-weekly-attendance/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已确认双接口活动模型、以累计周签到完成首例闭环，并授权契约及实施。周期、奖励与客户端协议的细节可在既定活动语义内通过原始内容核对，不需另猜玩法。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求任务契约；若直接分散实施，日历/执行两套生效状态、结算重复计数和客户端 ID/窗口错配会在多层接线后才暴露。一个 Spec 固定闭环，并由独立结果审查和真实持久化验证保护它；各层共同形成一个验收边界，不拆多个 Spec。
- Reroute conditions: 当前客户端或 PVF 无法支持原生周签到首例，或用户改变活动类型/要求独立验收另一活动时重新对齐范围。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 的直接实施不能承载这里需要的串行代理交付与共享接口依赖；先固定领域数据和协议，再接线结算/HTTP/UI的计划能避免跨层返工。没有现实资产、破坏性迁移或新的权限体系变更，不需要 Governed 的额外审批/挑战层。
