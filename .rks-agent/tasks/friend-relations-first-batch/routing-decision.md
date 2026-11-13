# Routing Decision

- TaskContract: `.rks-agent/tasks/friend-relations-first-batch/TaskContract.md`
- Contract revision: `1`（`task-contract-review.md` approved）

## SDD Readiness

意图、范围与验收已稳定：统一关系领域服务的定位（只存角色 ID、含业务规则、不主动推送）、好友与黑名单同批、师徒与最近联系人后置、婚姻独立，均由用户逐条确认；三方参照（70、上游、第三方）在好友语义上一致，执行方向已知。剩余不确定性是客户端自身上限、好友条目末尾字段、全量列表是否清掉黑名单显示，以及各协议的客户端读取边界；这些属于任务内可查清的协议细节，契约已给出缺省取值与「以客户端为准」的处置，不改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求走 SDD，愿意用流程时间换精度。直接执行最容易出错的是：把规则散落到协议切片里而破坏「关系服务唯一 owner、不推送」的约束；漏掉上下线推送的某个出口（断线、回选人、换频道）或推送顺序；排队的旧包复活或误删关系；黑名单在聊天各频道漏接。一个 Spec 加完整结果审查能逐条约束这些点。好友与黑名单共用同一关系服务、同一张关系存储、同一条在线推送链，且全量好友列表与黑名单显示相互依赖，必须一起验收、一起回滚，不构成独立的 Multi Spec 结果。
- Reroute conditions: 客户端证据显示 A21 好友需要对方确认、或好友走另一套协议（推翻统一服好友假设），回到用户对齐；若查明黑名单生效必须先改造聊天单入口的结构，或数据库迁移需要独立的发布与回滚，改走 Multi Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `single-spec-workflow/assets/profiles/planned-delivery.yaml`
- Rationale: 客户端证据 → 关系服务与存储 → 结构迁移 → 在线推送 → 协议切片 → 聊天黑名单接入之间有顺序依赖，前面放错一层（例如让关系服务依赖通知，或按名字建键）会扩散到所有后续切片；显式 Plan 能在动码前固定这些选择与先后，Lite 没有这项控制。任务不触及凭据、权限、不可逆外部变更；数据库升级走现有 DbMigrate 步骤并自动备份，普通完整审查加新鲜验证足够，Governed 的独立挑战与漂移审查没有额外价值。持续时间长但已有 task-units 的 Task 文件保持目标，不另加 long-running 控制。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `17eb734d48d4698f466285df002430b19db1f0ae`（`origin/main@17eb734d4`，规划提交之前）
- Gates: `spec: none`; `plan: none`; `implementation: auto`; `review: auto`; `verification: auto`
- Bound methods: `implementation: test-driven-development`
- Artifact path overrides: `none`
