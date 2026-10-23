# Routing Decision

- TaskContract: `.rks-agent/tasks/serial-gameplay-executor/attempts/spec-001-executor-outbox-lock-removal/TaskContract.md`
- Contract revision: `2`
- 子契约审查：`.rks-agent/tasks/serial-gameplay-executor/reviews/spec-001-contract-review.md`，`target_revision: 2`，`parent_revision: 4`，verdict `approved`（Round 2）。审查记录放在根任务的 `reviews/` 下、不在本目录旁，依据是主控方裁定第 8 条（`../../verification/reroute-t008.md` 文末）。
- 父契约：`../../TaskContract.md`，revision `4`；根契约审查 `../../reviews/task-contract-review-r3.md`，`target_revision: 4`，approved（Round 2）。

## SDD Readiness

本子 Spec 是收编的：改道前按 Single Spec 执行的那份 Spec，去掉协议级 CMD 归并（原 Plan T-008）之后，就是它。意图、范围与验收沿用已审查的 `../../spec.md`、`../../plan.md`，验收边界按修订记录 R3 收窄（`../../reviews/spec-plan-r3-review.md`，Round 3 approved）。

T-001～T-006、M-1、M-2 已经提交（TASK-01～TASK-28，HEAD `06ca5e0f`），剩下 T-007 与 T-009，执行方向已知。尚未定的只有这两个单元的局部设计，都在已授权的范围内。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale:
  - 结果 A 是根契约 revision 4 下一个有单一验收边界的结果：单执行器、提交后发件箱、删锁、调用链。
  - 它可以独立审查、封存与回滚，不依赖结果 B 中任何未封存的工作。
  - 按 `multi-spec-workflow`，每个子 Spec 都走 Single Spec。
  - 直接执行（Direct）时会出现的失败方式、所需控制及其时间代价，沿用根 `../../routing-decision.md` 原 Structural Selection 的论证。收窄之后，剩下的风险在于 T-009 的全局对账漏项与过渡机制清单不全，这正是该论证所防的那类。
- Reroute conditions:
  - T-007 或 T-009 证明 A 的过渡状态不能独立验收，例如某项过渡机制必须与 B 一起删除，A 才成立。这时回到根契约修订（根改道记录「改道后的再改道条件」）。
  - 剩余工作中，出现实测单条 CMD 超过 2 秒这类数量级的性能问题。这时回到用户（`../../spec.md` RQ-091 用户修订）。

## Controller Selection

- Controller: `sdd-governed`
- Profile source: `long-running`
- Rationale：沿用根 `../../routing-decision.md` 原 Controller Selection。
  - 漏掉的入口或保护意图，在完整结果审查时很难被发现，所以需要独立对抗挑战、独立审查批准，以及累积工作的偏离审查。这三项只有 Governed 提供。
  - 工作跨越多次会话与 compact，所以选 `long-running`。
  - 收编不改变这两条理由。剩余的 T-007、T-009 仍按 task-units 执行，并带按需的偏离复盘。

## Execution Configuration

- Profile: `long-running` with `sdd-governed`；long-running: `yes`（Profile 自带 `attention_review: on-drift`）。Plan 的模式由 Plan 记录，为 `task-units`（`../../plan.md` frontmatter）。
- Spec baseline: `a02bce469ec7fde2392592292bbdb9450939a02c`（`a02bce46`）。
  - 收编的子 Spec 沿用原 Spec 基准，不取「加了本行的根映射提交」。理由与登记见 `../../multi-spec-map.md`「Multi-Spec Boundary」（主控方裁定第 2 条）。
  - 回滚本子 Spec 时，回到这个基准；本区间内的根级改道提交按映射登记的 `foreign-commit` 例外处理（裁定第 3 条）。
- Gates:
  - spec: none
  - spec-adversarial: none
  - spec-review: auto
  - plan: none
  - plan-adversarial: none
  - plan-review: auto
  - implementation: auto（原停点「等用户 compact 后明确开始」已兑现）
  - drift-check: auto
  - change-adversarial: none
  - final-review: auto
  - verification: auto
- Bound methods: implementation: `test-driven-development`
- Artifact path overrides（主控方裁定第 2 条；子契约审查 N-1）：

| 键 | 路径 | 排除 |
|---|---|---|
| spec | `../../spec.md` | — |
| plan | `../../plan.md` | — |
| tasks | `../../tasks/` | — |
| reviews | `../../reviews/` | 根契约的审查：`task-contract-review.md`、`task-contract-review-r2.md`、`task-contract-review-r3.md`，以及之后的根契约审查 |
| verification | `../../verification/` | `reroute-t008.md`（根级改道汇总）、`reroute-t008-spec-002-draft.md`（B 的草案，A 封存后移出） |
| result | `./spec-result.md`（本目录内，封存时新建） | — |

  不属于本子 Spec 的根级文件，即使与它的产物同处一个目录，也不在覆盖范围内：
  - `../../TaskContract.md`、`../../routing-decision.md`、`../../multi-spec-map.md`；
  - `../../research/`，这是根级输入，只读引用。

  本子 Spec 封存之后：
  - B 新产生的跟进事项记在 `../spec-002-…/` 下，不改 `../../verification/follow-ups.md`；
  - 移交给 B 的条目，由 B 在自己的记录里引用编号并记录关闭；
  - `../../reviews/` 下本子 Spec 的审查文件，B 不改。
- Parent controller: `../../multi-spec-map.md`
