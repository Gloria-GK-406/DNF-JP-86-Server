# Routing Decision

- TaskContract: `TaskContract.md`
- Contract revision: `1`

## SDD Readiness

已授权普通学习首版；范围、增量语义、共享规则与整体一致性要求明确。响应附加段及尾部未知量作为有证据边界的实现研究项，不据此扩张到退点或全职业专有模式。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 单一接受边界是从普通学习请求到一致保存及响应。直接跨层接线容易遗漏批量失败回滚、复用点数真源或把参照猜测当协议事实；规格与显式顺序使这些要求在实现和审查时可核对，降低后续实机排错成本，值得本次成本。
- Reroute conditions: 若真实请求推翻增量语义或普通成功包边界，返回意图核对；若要求独立的退点/特殊职业玩法，另定接受边界。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-feature`
- Rationale: 比 Lite 增加的计划用于先收敛协议和槽位接口，再连接事务与响应，防止上下层各猜一份数据形状。普通功能可通过回归及独立完整审查保护，无需 Governed 的多重批准与对抗审查。
