# Routing Decision

- TaskContract: `.rks-agent/tasks/pvp-duel-phase1/TaskContract.md`
- Contract revision: `2`（`task-contract-review.md` approved）

## SDD Readiness

范围、规则与架构已由 #81 修订版 r1（经 Astra 审核）与用户「可以」稳定下来；17 条规则全部有确定值；执行方向（房间 owner + 事务参与者、结算领域服务、离场投影队列、规则服务与能力/权益表）已知。剩余不确定性是若干协议细节（`0x0037` 长度、`0x0039` 后解除等待的帧、含密码进房、建房/准备是否回 CMD），契约已给出缺省实现与实机取证的处置，不改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求先写任务契约，愿意用流程时间换精度。直接执行最可能的失败是：结算让 CMD 直接调规则服务或在未提交事务内投影（Astra 三条阻断的复发）、房间内存态与写库不同进退、离场四条路径漏掉一条或在终局后改结果、`character_pvp` 拆分漏掉技能初始化标记、大厅增量漏发——这些都是容易在长时间实现中遗忘的显式要求，需要 Spec 固定验收、Plan 固定顺序、完整结果审查逐条对照。房间、对战、结算、战绩共用同一房间 owner 与同一次结算事务，不能独立验收或回滚，是一个 Single Spec。
- Reroute conditions: 实机证明客户端对战流程与上游布局根本不同（例如结算顺序不同到需要改规则含义）则回到用户对齐；若库迁移需要独立发布与回滚，或协议取证需先行一个独立的逆向任务，改走 Multi Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `single-spec-workflow/assets/profiles/long-running.yaml`
- Rationale: 契约与内容源 → 持久化与迁移 → 房间 owner 与事务参与者 → 结算领域服务 → 离场与停服 → 协议切片 → 大厅增量之间有严格顺序依赖，早期放错（例如房间不做事务参与者）会扩散到后续每个切片，Lite 没有 Plan 控制。工作量大、跨多次上下文，需要 task-units 与目标检查防止注意力漂移，因此用 long-running。不触及凭据、权限、不可逆外部变更；库迁移走现有 DbMigrate 并在用户测试前一致备份，普通完整审查加新鲜验证足够，不需要 Governed 的独立挑战。

## Execution Configuration

- Profile: `long-running` with `sdd-standard`; long-running: `yes`
- Spec baseline: `3da40eab1`（契约提交，位于 `origin/main@ef20398a3` 之上；工作区就绪时的 HEAD）
- Gates: `spec: none`; `plan: none`; `implementation: auto`; `review: auto`; `verification: auto`
- Bound methods: `implementation: test-driven-development`
- Artifact path overrides: `none`
