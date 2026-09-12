# Routing Decision

- TaskContract: `.rks-agent/tasks/town-party-lifecycle/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标、范围和可观察验收已经稳定：当前任务只交付 A21 城镇邀请、接受、组队显示、离队与掉线清理，并同步协议证据；聊天组、好友、交易、PvP 和多人副本均明确排除。协议与旧服证据已经给出可执行方向，剩余的 A21 非空队伍字节不确定性可通过任务内分阶段实机校准解决，不改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求实现与协议文档同步，直接执行最容易让跨会话邀请代际、掉线清理或某个通知协议被遗漏。一个统一 Spec 的完整结果审查可以持续约束这些共同服务于同一“城镇组队闭环”的要求，收益足以覆盖流程成本；它们需要一起验收和回滚，不构成独立的 Multi Spec 结果。
- Reroute conditions: 若证据表明当前 A21 必须先实现独立的好友/聊天/交易系统才能组队，或城镇组队必须同时交付多人副本运行时，任务语义和独立验收边界将发生变化，应停止并重新对齐或改走 Multi Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `sdd-standard/assets/profiles/code-feature.yaml`
- Rationale: 本任务的状态模型、协议投影顺序、会话生命周期和验证步骤相互依赖，错误的前置选择会扩散到多个消费者，显式 Plan 的依赖排序能显著减少返工；Lite 缺少这项前置控制。任务不涉及安全、权限、持久数据损失或不可逆外部变更，普通完整代码审查和新鲜验证足够，Governed 的独立挑战与漂移控制没有额外价值。
