# Routing Decision

- TaskContract: ./TaskContract.md
- Contract revision: 1

## SDD Readiness

已授权实现与部署，范围限定现有固定条件的事件触发，验收可观察且中央执行方向明确。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 用户要求契约；统一物品入口容易漏掉共享材料和孵化更新，完整 Spec 与独立结果审查防止仅接通少数 CMD。一个玩家解锁边界，无独立子任务验收。
- Reroute conditions: 若发现物品写入无法共享事务或必须改造客户端协议，重新界定受影响范围。

## Controller Selection

- Controller: sdd-lite
- Profile source: focused-delivery
- Rationale: 采用固定入口与现有可靠事件机制，无需独立 Plan；最终审查和回归保护遗漏、回滚与重复奖励边界。

## Workspace

- Root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/achievement-state-events
- Branch: task/achievement-state-events
- Accepted baseline: d0427ce3ecc987ab76050f0e714fe919c7188fcf
- 初始工作区 clean；源 main 不变；原生工具因父目录不是仓库失败，Git fallback 已创建并核验。
