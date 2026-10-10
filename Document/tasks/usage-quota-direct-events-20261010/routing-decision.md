# Routing Decision

- TaskContract: ./TaskContract.md
- Contract revision: 1

## SDD Readiness

用户已确定同步直接投递、次数归拢及部署目标；范围及观测结果明确，可保持原规则和原数据兼容实施。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 直接改动容易漏掉Effect中的次数写入，或让队列重复扣次、通知提前于提交。用户明确要求任务契约，Spec固定职责边界、Plan固定迁移顺序与接口，独立审查检查完整收口；比最终仅看UI更能减少返工。三类计数共同组成一次可验收的重构，不拆独立Spec。
- Reroute conditions: 若需要改变客户端或不可兼容迁移数据，应重新对齐范围；普通实现与回归发现属于本Spec修复。

## Controller Selection

- Controller: sdd-standard
- Profile source: planned-delivery
- Rationale: 需要先固定同步扣次与提交后投影的交界及兼容方案，并允许有边界的实施委派以保留控制上下文。Lite缺少显式Plan与实施委派；Governed的额外审批对此本地可回滚改动收益不足。
