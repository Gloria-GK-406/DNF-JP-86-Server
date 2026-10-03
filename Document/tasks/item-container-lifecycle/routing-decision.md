# Routing Decision

- TaskContract: ./TaskContract.md
- Contract revision: 1

## SDD Readiness

用户已经明确 Take 交出完整 record、Get 返回容器内引用、Update 按引用更新、销毁独立处理，并授权回退后重新执行。玩法和协议兼容边界明确。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 上一尝试已实际出现“record 只是读写快照”的中心模型偏差；本任务通过生命周期 Spec、先确定存储结算与引用失效的计划及独立结果审查，在调用迁移扩散前固定边界。取出记录、引用和全部消费者必须一起成立，是一个验收结果。
- Reroute conditions: 仅当发现无法保持既有事务或协议行为、或者确有独立交付边界时重新判断；普通迁移问题就地修复。

## Controller Selection

- Controller: sdd-standard
- Profile source: planned-delivery
- Rationale: Take 不删除物品行、Put 单次接收、引用失效、材料数量和宠物附加状态共享同一结算，早期错误会传播到全部调用方；明确实施顺序比 Lite 的末尾审查更早控制返工。此次只改本地代码，事务回滚及恢复基线已有证据，无需更重审批。
