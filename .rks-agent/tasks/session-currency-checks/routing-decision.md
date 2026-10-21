# Routing Decision

- TaskContract: `.rks-agent/tasks/session-currency-checks/TaskContract.md`
- Contract revision: `2`。独立审查结果见 `reviews/task-contract-review-r2.md`（target_revision 2，verdict approved）；r1 留档于 `reviews/task-contract-review.md`（changes_required）。

## SDD Readiness

意图和范围已经在对话中收敛：用户提出两类检查，认同两级口径、`TryGetInWorld`、先替换后收紧，并把命名和细节交给 Agent。结果可以观察：手写判定清零，具名判定集中，构建和测试通过。需求可以逐条列举（SC-1～SC-7）。中心方向已知：判定由 `OnlineStateRegistry` 实现，调用点按用途归级。剩下的不确定都在实现层，即各调用点归哪一级、测试夹具怎么调整，交给 Spec 和 Plan 处理。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求按契约推进，审查第一轮就查出两处会悄悄放宽检查、或漏掉主体范围的缺陷，说明这类改动在 Direct 下很容易出错。具体的失败路径是：约 180 个文件、约 400 处调用点里，有一两处被归错级，导致检查放宽，或者让正常路径失败（旁人状态让请求方抛异常）。Spec 先钉住定义、保底规则和例外清单，Plan 定下两步走的停靠点，最后再做独立的完整结果审查，能直接拦住这类问题。判定接口和调用点替换共用同一个编译边界和同一套测试夹具，只有一个验收边界，所以是一个 Single Spec。
- Reroute conditions: 实施中发现某类调用点只有放宽才能保持正常路径，需要回到契约修订；或者需要第三种具名语义，需要回到契约并向用户确认。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 有几个早期决策一旦出错，会在上百个文件里扩散：判定的定义、机械替换的规则、哪些调用点属于生命周期路径、测试替身怎么实现。所以显式 Plan 有实质价值，Lite 不够。不选 Governed，因为本任务只改进程内的判定，不碰持久化结构或凭据，有基线测试、保底规则和完整结果审查兜底。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `ea6a1544`
- Gates: `implementation: auto`; `review: auto`; `verification: auto`（spec、plan 为 `none`）
- Bound methods: `implementation: test-driven-development`
- Artifact path overrides: `none`
