# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

Story 是根任务当前唯一活动结果，类别边界、排除项、证据顺序和客户端可观察验收已稳定；target/default
属于本子规格要闭合的证据问题，不改变 Story 设置与生命周期这一单一结果。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: Story 的静态构包、规则实现、通知与实机验收必须共同成立，拆成多个可接受 Spec 会产生半成品
  模式；持久 Spec/Plan、完整结果独立审查和新鲜验证可防止把 Normal 假设或 parser-safe 值误当 Story
  规则，精度收益值得该模式的一次流程成本。因此它是一个 Single Spec。
- Reroute conditions: 若证据证明 Story 基本流程依赖尚未授权的快速匹配系统，返回根控制器评估范围；若
  Story 与 Cycle 实际共享不可分割的原子状态机，停止并重新评估根结构。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 相比 Lite，Story 的证据裁决、规则变更、协议投影与实机验收存在顺序依赖；显式 Plan 能在
  target/default 尚未知时阻止实现先行并固定运行时停止条件。普通完整结果审查和新鲜验证足以保护当前
  可恢复代码/客户端实验，不存在需要 Governed 独立挑战或高风险资产控制的额外事实。
