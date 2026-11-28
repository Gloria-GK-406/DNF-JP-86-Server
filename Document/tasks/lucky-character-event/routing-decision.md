# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `2`

## SDD Readiness

GM 启停、有效期与职业条件已经确认。各项收益、入场资格和客户端显示都有可验收条件。原生刷新和角色技能表现需要实机校验。[独立契约审阅](task-contract-review.md)

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: GM 启用一项活动时，原生客户端会显示完整权益。可以分段实现和测试，但玩家侧接受的是一次完整开关。先规划活动时间、入场状态和结算送达的边界，避免跨期时疲劳与经验按不同资格计算。
- Reroute conditions: 用户要求单独发布或独立启用福利时，重新确认验收边界。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 先安排入场数据、事务和协议的共同依赖，再实现各奖励边界。隔离开发与独立结果复核满足当前控制需要，玩家库操作保留明确授权边界。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `818cbeed7391a9fb6079270a06766fecac2a5ff6`
- Gates: `implementation: auto`, `review: auto`, `verification: auto`
- Bound methods: none
- Artifact path overrides: none
