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

## 改道记录（2026-10-23，已生效）

- 状态：**已生效（2026-10-23）**。自本节生效起，上面 Structural Selection 的 Single Spec 决定作废，只作为历史保留；Controller Selection 与 Execution Configuration 两节只作为 spec-001 子路由决定的来源保留。
- 审查门：`reviewing-task-contracts`。本机未安装该 Skill，沿用 r1、r2 的做法：新上下文的独立子代理，按 `reviewing-work` 交付物标准与 `modeling-task-contracts` 的 readiness 检查审查。三份审查都已通过：

| 审查对象 | revision | 审查记录 | 结论 |
|---|---|---|---|
| 根契约 `TaskContract.md` | `target_revision: 4` | `reviews/task-contract-review-r3.md` | Round 1（revision 3）changes_required；Round 2（revision 4）approved |
| 子契约 A `attempts/spec-001-executor-outbox-lock-removal/TaskContract.md` | `target_revision: 2`（`parent_revision: 4`） | `reviews/spec-001-contract-review.md` | Round 1（revision 1）changes_required；Round 2（revision 2）approved |
| `spec.md`、`plan.md` 修订记录 R3（含 `verification/follow-ups.md` 的标记） | 修订 R3 | `reviews/spec-plan-r3-review.md` | Round 1、Round 2 changes_required；Round 3 approved |

- 绑定：TaskContract `.rks-agent/tasks/serial-gameplay-executor/TaskContract.md`，revision `4`。
- 根控制器：`multi-spec-map.md`。spec-001 的子路由决定：`attempts/spec-001-executor-outbox-lock-removal/routing-decision.md`。
- 汇总与移交清单：`verification/reroute-t008.md`。

### 触发

上面 Structural Selection 预先登记的第一条改道条件成立：「实施中证明某一部分可以、也需要独立验收与回滚。例如执行器与删锁已经达标、可以单独封存，而协议级 CMD 的合并要暂停。这时转多 Spec。」

证据有三条：
- T-001～T-006 与 M-1、M-2 已提交（TASK-01～TASK-28，HEAD `06ca5e0f`）；
- 执行器、发件箱、删锁的验收证据集中在 `verification/t004-closure.md`、`t005-closure.md`、`t006-closure.md`；
- M-2 按用户修订后的接受线通过（`verification/measurement/m2-summary.md`）。

主控方依用户 2026-10-22 的授权（“你来做主吧，结束后我会找gpt astra单独做一遍审核。那边过了我们才收尾。”）提出改道。2026-10-23，用户本人决定：“继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出”。所以结构定为 Multi Spec：A、B 都在 `task/refactor-session-20261021` 上完成，A、B 都封存后一次收尾，A 不单独推送或开 PR。

### 理由

原决定不拆，理由是「执行器、发件箱、协议级 CMD、删锁四件事互相依赖，单独交付任何一件都不构成用户要的结果」。实施下来，依赖只有单向的一条：协议级 CMD 依赖执行器、发件箱与删锁，反过来不成立。前三件已经在过渡机制下构成一个可运行、可测试、可回滚的状态，过渡机制见根契约 SC-12。

两个结果性质不同：
- 结果 A 改变执行与投递语义；
- 结果 B 是按功能簇的结构归并，量最大（Plan T-008 一行就覆盖 141 个 handler 与 204 个命令处理器的去向）。

放在一个验收边界里，有两个后果：
- A 的验收与封存要等 B 全部做完，A 已经完成的部分长时间不能封存；B 一旦失败，回滚边界也只有整个 Spec 一个；
- 每一次审查的对象都过大，漏看的风险正是本任务选 governed 时要防的那种。

拆开之后，A 和 B 各有独立的 Spec、审查、验证与回滚边界。收尾不因拆分而提前：按用户 10-23 的决定，A、B 都封存后在同一分支上一次收尾，外部审核也在那时对整个分支一次进行。

### 新结构

- Selected workflow：`multi-spec-workflow`。
- 根控制器：`multi-spec-map.md`（`workspace.baseline` 为 `a02bce46`，即原 Spec 基准）。
- spec-001（结果 A）：子契约 `attempts/spec-001-executor-outbox-lock-removal/TaskContract.md`。它沿用原 Spec 的 `spec.md`、`plan.md`、`tasks/`、`reviews/`、`verification/`（原地，不搬），Spec 基准 `a02bce46`。原 Controller Selection（`sdd-governed` + `long-running`）与 Execution Configuration 移入 spec-001 的子路由决定，并加 `Parent controller` 与路径覆盖。子契约审查通过后，子路由决定已写在 `attempts/spec-001-executor-outbox-lock-removal/routing-decision.md`；路径覆盖（含排除项）照 `verification/reroute-t008.md`「spec-001 子路由决定的待落地内容」落地。
- spec-002（结果 B）：在 spec-001 封存、根评估之后才建模。草案见 `verification/reroute-t008-spec-002-draft.md`，不放在 `attempts/` 下。
- 改道后，上面的 Controller Selection 与 Execution Configuration 两节只作为 spec-001 的来源保留，不再是根级配置（Multi Spec 的根路由不选控制器）。

### 改道后的再改道条件

出现下面任一情形，回到根契约修订：
- spec-001 的 T-007 或 T-009 证明 A 的过渡状态不能独立验收（例如某项过渡机制必须与 B 一起删除，A 才成立）；
- 外部审核对 A 的意见，既不能作为新的修正子 Spec 承接，也不能在不跨边界的前提下并进 B（封存后的 A 不重开）；
- 用户否决本改道。

### 对已提交证据的影响

无。`a02bce46..06ca5e0f` 的 31 个提交都是本任务的检查点（规划 1、修订 2、单元 28），在 Plan 上都落在 T-001～T-006、M-1、M-2 之内，没有一个属于 T-008。它们记载中写作「交 T-008」「归 T-008」的去向，按 `verification/reroute-t008.md` §4 的对照读作「子 Spec B」或「留在 A」，已提交的文件不改。

### 与 `multi-spec-workflow` 常规流程的偏离

偏离有三条，已由主控方裁定第 2、3 条接受（`verification/reroute-t008.md` 文末）：
1. spec-001 是在执行中被收编的子 Spec，不是新建模的；
2. 它的 Spec 基准早于根映射提交；
3. 根级改道提交会落在 spec-001 的提交区间内。

详见 `verification/reroute-t008.md` §5。
