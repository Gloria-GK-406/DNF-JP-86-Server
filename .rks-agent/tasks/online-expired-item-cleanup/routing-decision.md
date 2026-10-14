# Routing Decision
- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness
登录清理已验收，在线范围与六点时点明确，使用既有调度器和删除服务。

## Structural Selection
- Selected workflow: single-spec-workflow
- Rationale: 单一验收边界；Direct易遗漏离线竞态和提交后同步，Spec及独立完整结果审查可以约束这些要求，符合用户对清理流程防偏移的要求。
- Reroute conditions: 若需全库离线扫描或物品期限迁移则重新定界。

## Controller Selection
- Controller: sdd-lite
- Profile source: focused-delivery
- Rationale: 既有删除逻辑和调度器均已实现，内部编排可直接确定；没有需要另立Plan的未决事项。SQLite备份证明恢复边界，无需更强治理。
