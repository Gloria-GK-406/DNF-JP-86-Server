# Routing Decision

- TaskContract: `.rks-agent/tasks/chat-routing-first-batch/TaskContract.md`
- Contract revision: `3`（revision 3 复审 approved；改动只换私聊投递协议并补假设，结构选择与档位不变）

## SDD Readiness

意图、范围和验收已经稳定：第一批五类频道、拒绝集合、尽量可达的投递语义与范围模型都由用户逐条确认，TaskContract revision 2 已获独立评审 approved（`task-contract-review.md`）。执行方向已知：协议层解包与 CMD、Application 层范围领域服务、通知处理器按收件人组包。剩余不确定性是 `0x0076` 的 serverId／频道字节取值和 `0x01A9` 应答布局，属于任务内可查清的协议细节，不改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户说任务「蛮大」并要求走任务契约，表明愿意用流程时间换精度。直接执行最容易漏掉的是成批的拒绝路径（服务器专用 mode、不支持 mode、`0x01A9` 同 type 应答、喇叭失败、正文长度）和两种回推协议的分支；一个 Spec 加完整结果审查能逐条约束它们。五类频道共用同一入口、同一范围服务和同一投递链，需要一起验收、一起回滚，不构成独立的 Multi Spec 结果。
- Reroute conditions: 若查明某一频道必须先实现独立系统（例如喇叭回推要求先有好友或公会状态），或 A21 客户端证据推翻 `0x0011` 单入口假设（例如某频道走另一条 CMD），停止并重新对齐或改走 Multi Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `single-spec-workflow/assets/profiles/planned-delivery.yaml`
- Rationale: 范围服务的归属层、通知类型与处理器、库存扣除的事务点、编码器注册和架构测试之间相互依赖，前面放错一层（例如让 Application 认识 wire mode，或在 CMD 里自己查队员）会扩散到所有频道，显式 Plan 的依赖排序能在动码前固定这些选择，Lite 没有这项控制。任务不触及凭据、权限、不可逆外部变更或持久数据丢失；喇叭扣除走现有库存事务，普通完整审查加新鲜验证足够，Governed 的独立挑战与漂移审查没有额外价值。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `9712ecacc`（`origin/main`，规划提交之前）
- Gates: `spec: none`; `plan: none`; `implementation: auto`; `review: auto`; `verification: auto`
- Bound methods: `implementation: test-driven-development`
- Artifact path overrides: `none`
