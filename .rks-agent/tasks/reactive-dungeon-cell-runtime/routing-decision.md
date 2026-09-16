# Routing Decision

- TaskContract: `.rks-agent/tasks/reactive-dungeon-cell-runtime/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标、范围和可观察验收条件已经稳定：首次访问原子创建完整 Cell，重访复用实例，运行期
Actor/地面物状态由 Cell 单一持有，现有进入、击杀、拾取和投影迁移到该真源。协议兼容、
背包事务补偿、非目标功能及实机验证边界均已明确；具体类形和迁移顺序可在 Spec 与 Plan 内
选择，不会改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求 SDD，且本次迁移若直接实施，最现实的失败是只移动数据结构却遗漏
  重访、拾取补偿或投影中的旧真源，最终形成第三套状态。持久 Spec、显式依赖顺序、完整结果
  审查和新鲜验证能直接降低这一风险，时间成本相对于返工整个地下城链路是值得的。所有变更
  共同服务于“Cell 成为唯一运行时真源”这一验收边界，不能独立接受或回滚，因此使用一个
  Spec。
- Reroute conditions: 如果发现现有线协议无法从 Cell 快照表达且必须新增独立协议能力，或
  建筑/玩家丢弃出现可单独验收、单独回滚的新功能边界，则停止当前实施并重新评估契约与
  Single/Multi 结构；若只需调整内部类形或迁移顺序，则保持当前路线。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 的最终审查不足以保护四条相互依赖的迁移顺序——Cell 创建、旧状态搬迁、
  拾取事务和协议投影；显式 Plan 能在修改前固定切换点和验证次序，避免前段结构决定把后段
  锁死。当前任务不涉及安全、凭据、不可逆外部写入或数据丢失硬门槛，普通独立完整结果审查
  与新鲜验证足够，因此不升级到 Governed。
