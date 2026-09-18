# Routing Decision

- TaskContract: `.rks-agent/tasks/party-key-dungeon-membership-refactor/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已经明确内部 `PartyKey` 与客户端队伍 ID 的分工、运行中副本保持原实例、当前队伍成员为唯一资格真源，以及离队者的专属结果跳过而不重算 Cell。现有代码展示了真实/虚拟队伍身份、副本冻结成员、Cell 生成和通知的具体接缝；可观察的双客户端队长离队与副本延续结果已列入契约。剩余实现选择可在规格与计划中依据代码决定，不改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户特意要求任务契约来约束一次牵动组队、虚拟队伍、副本、Cell 与协议投影的重构。若直接逐处修改，最可能遗漏某个冻结成员消费者或把客户端 ID 再带回领域，最终形成新的双真源，而常规局部测试仍可能通过；一份贯穿所有接缝的 Spec、按依赖顺序实施的 Plan 和完整结果审查可以在较低返工成本下约束该风险。真实/虚拟身份、队长换代与副本连续性需要共同验收，尚无可独立接受和回滚的子目标，因此用一个 Spec。
- Reroute conditions: 若实际客户端协议证据推翻“仅更换客户端 ID 即可保留队伍显示”，或发现副本数据存在不能在此边界内安全迁移的独立持久化/回滚结果，则暂停并重评契约或拆分边界；若计划中证实无跨会话注意力风险，可移除长任务控制。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `long-running`
- Rationale: 需要先定内部身份与协议投影的先后，再迁移副本及 Cell 使用点；错误的早期接口决定会把返工扩散到所有消费者，Lite 的无计划直做不足以控制。该工作跨多个依赖单元，现有活体 Host 与实机验收也使其很可能跨会话，长任务的逐单元计划和偏航检查有实际价值。没有达到 Governed 必需的独立高后果挑战或强审计门槛；普通独立结果审查与新鲜验证已足够。
