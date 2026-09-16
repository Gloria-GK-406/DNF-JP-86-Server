# Routing Decision

- TaskContract: `E:\Project\DNF\DNF-JP-86-Server\.worktrees\reactive-dungeon-lifecycle\.rks-agent\tasks\reactive-dungeon-lifecycle\attempts\spec-001-reactive-pickup-projection\TaskContract.md`
- Contract revision: `1`

## SDD Readiness

现象、正常边界和验收已明确：移动与权威拾取正常，仅远端拾取表现缺失；当前 Handler 直发路径与入场变更机制提供了确定的改造起点。A21 客户端静态证据或上游实现可在子任务内决定具体接收者编码，不改变目标语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 该子结果只有一个连贯验收边界：一次共享拾取产生一个实例变更并正确投影给拾取者和观察者。直接修改最容易遗漏金币、怪物掉落或旧连接边界；一份 Spec、测试驱动实现和独立完整代码复审足以控制风险，不需要再拆分子 Spec。
- Reroute conditions: 若静态与上游证据证明拾取动作完全不受服务器拾取通知控制，则停止生产修改并把证据返回根控制器缩减或重定向该结果。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `repair`
- Rationale: 远端表现缺失需要先从 A21 接收端或上游证明字段/消息语义，再为支持的根因规划修复；Standard repair 的独立诊断和修复计划能防止猜测 wire 字段后把错误固化进统一变更。Lite 没有这两个控制，Governed 的独立挑战与审计强度对本子结果没有额外实质收益。
