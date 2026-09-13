# Routing Decision
- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

证据依赖已由 spec-001 接受；结果边界固定为一个不新增新 wire 的 town/party 投影闭环，受众、生命周期、
并发和排除项清楚。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: domain observer capture、protocol delivery 与 entry/move/cleanup 接入必须共同满足因果顺序；拆成
  可独立接受 Spec 会留下只在一半生命周期工作的公共列表。
- Reroute conditions: 若测试或当前代码证明需要新的客户端消息、持久目录或快速匹配状态，停止并回父控制器。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 多个生命周期和并发顺序需要持久 Plan、分 Task Unit 快速审查、完整结果审查和新鲜验证；
  改动可恢复且不触及高风险外部资产，无需 Governed。
