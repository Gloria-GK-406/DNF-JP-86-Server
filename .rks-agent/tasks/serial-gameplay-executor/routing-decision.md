# Routing Decision

- TaskContract: `.rks-agent/tasks/serial-gameplay-executor/TaskContract.md`
- Contract revision: `2`（`reviews/task-contract-review-r2.md` 为 approved，target_revision 2）

## SDD Readiness

意图与范围稳定：执行模型由用户逐条提出，Agent 补充的细则也得到了用户的回复。结果可观察，11 条成功条件都有证据形式。中心方向已知：单执行器、协议级 CMD、提交后发件箱、删锁。

实施中仍要决定的，是局部设计：发件箱与发送端的结构、细粒度命令的去向、工作单元的划分、判据的具体写法。这些都在契约授权的范围内，不改变交付的是什么。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求走 governed，并点出「这个改动比较危险」。如果走 Direct，最可能的失败是：141 个 handler、204 个命令、52 个订阅、33 个后台服务、约 15 处依赖投递结果的行为里，漏掉一个入口或一项保护意图，而且直到线上才暴露。按 issue 逐个处理的模式也证明，跨会话很容易丢失全局目标。Spec 能把入口、执行器外部分、投递依赖、锁的去留和可见变化固定成对账表；task-units 的 Plan 加上执行期的偏离检查和独立审查，能针对性地降低这种失败。用户已经接受了相应的时间成本。验收边界只有一个，就是新执行模型整体成立。执行器、发件箱、协议级 CMD、删锁四件事互相依赖，单独交付任何一件都不构成用户要的结果，所以不拆成多个 Spec。
- Reroute conditions:
  - 实施中证明某一部分可以、也需要独立验收与回滚。例如执行器与删锁已经达标、可以单独封存，而协议级 CMD 的合并要暂停。这时转多 Spec。
  - RQ-091 的测量不达标，或用户在规划停点否决了默示接受的决定（单执行器、短锁暂留、持久化不变）。这时回到契约修订。

## Controller Selection

- Controller: `sdd-governed`
- Profile source: `long-running`
- Rationale:
  - **Standard 不够**：漏掉的入口或保护意图，在完整结果审查时很难被发现。Spec 与 Plan 都需要独立的对抗挑战和独立的审查批准，累积工作也需要一次偏离审查，这三项都只有 Governed 提供。
  - **不选 planned-delivery**：工作量要跨越多次会话和多次 compact，用户本人就会在规划后 compact。所以选 long-running，它带 task-units 和按需的偏离复盘。

## Execution Configuration

- Profile: `long-running` with `sdd-governed`; long-running: `yes`（Profile 自带 `attention_review: on-drift`）
- Spec baseline: `a02bce46`
- Gates:
  - spec: none
  - spec-adversarial: none
  - spec-review: auto
  - plan: none
  - plan-adversarial: none
  - plan-review: auto
  - implementation: auto，但开始实施前有一个由用户设定的停点（TaskContract「Requires Confirmation」）
  - drift-check: auto
  - change-adversarial: none
  - final-review: auto
  - verification: auto
- Bound methods: implementation: `test-driven-development`
- Artifact path overrides: none
