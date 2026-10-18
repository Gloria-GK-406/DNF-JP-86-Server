# Routing Decision

- TaskContract: `.rks-agent/tasks/scheduled-task-dispatcher/TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户明确先交付调度器，再讨论清理接入；注册执行职责与在线清理边界明确，清理不在本次实际执行范围。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 用户指出偏离实现未被审查抓住的风险，调度、业务回调与未来清理的界限容易被混合。Spec与独立完整结果审查约束该边界；并发、补执行和scope生命周期需先固定协同语义，值得显式Plan成本。只有一个通用机制验收边界。
- Reroute conditions: 必须改业务数据、迁移后台任务或保证跨重启恰好一次时重审范围。

## Controller Selection

- Controller: sdd-standard
- Profile source: planned-delivery
- Rationale: 计划需先明确启动、时钟跃迁、慢回调及重试之间的耦合，避免以多个计时器或强制超时重试破坏不重叠约束；Lite的无计划实现不足以约束这个演化。无真实删除或不可逆操作，不需Governed。按持续用户约定复用当前主工作区及工作分支，不另开工作区。
