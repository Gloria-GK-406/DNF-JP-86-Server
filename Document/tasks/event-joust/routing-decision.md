# Routing Decision

- TaskContract: `Document/tasks/event-joust/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已授权马战构建及恢复继续；配置归属和玩家竞猜闭环明确。协议和历史期限的细节在既定玩法内核对，不另猜玩法。基线已更新为包含经验修复的 b4bf8d0d。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求契约；下注已产生权益时修改赛程、错配客户端字段或重复补结算，会造成资产与玩法脱节。一个 Spec 固定闭环，依赖计划先闭合协议/规则，再冻结轮次状态，独立结果审查与故障恢复测试保护完整结果，各层共同形成一个验收边界。
- Reroute conditions: 客户端没有可用原生 JOUST 入口，或用户改变玩法/拆出独立交付时重新对齐。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 比 Lite 多出的依赖计划防止赛程、持久化权益与协议三套状态提前分叉；串行分工保持写入归属。仅隔离游戏开发，保留活服与数据库，无破坏性迁移或现实财务/权限变更，不需额外 HITL 审批。
