# Routing Decision

- TaskContract: `Document/tasks/epic-book-pickup/TaskContract.md`
- Contract revision: 1

## SDD Readiness

授权、账号归属、int.MaxValue 上限及拾取/查询语义已经明确。已有上游参照及当前拾取事务可以作为实现起点，客户端非空通知需核对并标注证据层级。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 图鉴到账、地面移除、旧数据迁移必须共同保护数量不丢失和不重复。用户要求任务契约，单份规格及独立结果审查可防止只改 UI 同步而遗漏持久化/满额语义；这一闭环使用一个验收边界即可。
- Reroute conditions: 客户端不支持参照通知，或发现无法保留旧碎片数量的存储歧义，需要重新确认对应边界。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- Rationale: 可在一个规格下直接实现，不需要独立任务单元或逐阶段人工审批；独立结果审查足以检查到账和事务一致性。
