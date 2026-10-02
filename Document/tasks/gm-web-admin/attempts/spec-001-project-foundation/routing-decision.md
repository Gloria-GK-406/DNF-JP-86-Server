# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

启动组合和页面构建边界清晰，新增功能没有玩家数据或权限写入。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 新模块易遗漏 GM-only 启动和监听边界；一个 Spec 加真实启动验证和独立完整审查覆盖该稳定结果。
- Reroute conditions: 如开始开放认证或私有数据，转入后续已规划子结果，不在骨架范围内扩张。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- Rationale: 当前不触达玩家资料、权限或资产，具体挂载方式可局部解决，独立结果审查足够，无需增加 Plan 和挑战审批。
