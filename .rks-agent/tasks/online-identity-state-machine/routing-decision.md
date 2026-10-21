# Routing Decision

- TaskContract: `.rks-agent/tasks/online-identity-state-machine/TaskContract.md`
- Contract revision: `2`（独立审查 `reviews/task-contract-review.md`，target_revision 2，verdict approved；r1 审查留档于 `reviews/task-contract-review-r1.md`）

## SDD Readiness

意图与范围已在对话中逐轮收敛并经用户认同（“大体方向我认同你的看法，细节你来把持吧”），结果可观察（平行存储消失、阶段判定集中、运行时状态单一归属、构建测试通过），需求可逐条列举（SC-1～SC-7）。中心方向已知：在线身份并入 `OnlineStateRegistry` 一把锁下的一条选中会话记录，角色在线状态迁入新的无锁运行时服务。剩余不确定只在实现层（各调用点原有接受范围、迁移顺序），交由 Spec 与 Plan 处理。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求写契约、按 SDD 推进，并把细节交给 Agent 把持，体现出重视精确、可接受多花时间。Direct 下的具体失败路径是：44 个生产文件、约 130 个测试文件要迁移，其中“已选中/在世界/连接已撤销”三种接受范围分散在各调用点，容易有一两处被悄悄放宽或收紧；冷却、灵药的代数隔离与无锁并发也容易漏掉竞态。Spec 先逐条钉住各判定的语义与验收，Plan 定迁移顺序，再做独立的完整结果审查，能直接拦住这类遗漏，值得这点时间。身份与运行时两部分共用选角/拆除流程、同一套测试夹具，拆开验收会互相阻塞，因此是一个连贯的 Single Spec，不存在独立的验收或回滚边界。
- Reroute conditions: 实施中发现某类调用点必须改变协议可见行为才能完成迁移（返回契约修订）；或发现运行时拆分需要改动跨角色聚合（城镇区域等）的不变量（返回契约，考虑与 #208 合并或拆为 Multi Spec）。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 不带 Plan，而本任务有几个耦合的早期决策：判定接口的形状、测试夹具的构造方式、四个运行时服务的迁移顺序。这些一旦选错，会在上百个测试文件里扩散，最终审查时才发现代价很高，所以显式 Plan 有实质价值，选 Standard。Governed 的独立挑战和漂移控制，对应的是安全、权限、数据丢失这类硬底线；本任务只改进程内存状态，不碰持久化结构或凭据，有基线测试和完整结果审查兜底，不需要 Governed。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `7fc8acdc`
- Gates: `implementation: auto`; `review: auto`; `verification: auto`（spec、plan 为 `none`）
- Bound methods: `implementation: test-driven-development`
- Artifact path overrides: `none`
