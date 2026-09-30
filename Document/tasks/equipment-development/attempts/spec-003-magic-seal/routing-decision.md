# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

根任务 SC-3、现有装备实例、自动接受边界及用户排除的附魔师范围稳定；具体 opcode 和 PVF 规则可在已授权的证据阶段确定，未知资产语义设为停门。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户偏向快速迭代，但随机词条与材料结算若仅靠直接边做边猜，容易遗漏失败/重试下的资产守恒；一个短证据设计阶段、完整结果审查及新鲜验证足以降低返工。魔法封印生成、解封和词条操作共享同一装备状态与验收边界，属于一个内聚 Spec，且根 Multi Spec 已隔离其他装备功能。
- Reroute conditions: A21 证据证明该功能需要独立可接受的跨玩家流程，或关键操作的资产语义无法安全确定且不存在同 Spec 可实现路径。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 解封、随机属性生成与协议投递有先后依赖；在实现前用短 Plan 固定证据门和事务/投递顺序，比 Lite 最终审查才发现早期误判更省返工。没有不可逆外部操作或高后果权限/安全边界，不采用 Governed 的额外挑战门。
