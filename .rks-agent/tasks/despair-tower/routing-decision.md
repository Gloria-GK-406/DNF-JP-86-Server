# Routing Decision
- TaskContract: ./TaskContract.md
- Contract revision: 1

## SDD Readiness
单人塔的入口、战斗、持久化和测试部署目标明确，楼层表已有内容证据。版本细节以spec显式规则约束。

## Structural Selection
- Selected workflow: single-spec-workflow
- Rationale: 每日资格、票费和奖励推进容易各自正确却时序不一致；简短Spec和完整独立评审防止漏掉失败退款、重复奖励及重登状态，成本低于实机反复排障。整体是一条可玩的闭环。
- Reroute conditions: 若发现必须改客户端或无法按已知包布局完成入场/结算，则先重定边界。

## Controller Selection
- Controller: sdd-lite
- Profile source: focused-delivery
- Rationale: 复用既有入场/战斗/事务机制即可，暂不需要单独Plan或强审批。
