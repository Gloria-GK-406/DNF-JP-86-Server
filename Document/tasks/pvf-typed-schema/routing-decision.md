# Routing Decision
- TaskContract: ./TaskContract.md
- Contract revision: 1

## SDD Readiness
用户已授权一次性迁移；结果兼容、错误可诊断、旧入口清零可验证。

## Structural Selection
- Selected workflow: single-spec-workflow
- Rationale: 用户要求契约和全覆盖。直接实施易漏字段比较、间接读取和复杂段落。共享接口计划、扫描和完整结果审查值得其时间成本。整个迁移依赖同一 API，统一验收。
- Reroute conditions: 合法数据无法兼容或需修改游戏/PVF 内容时重新评估。

## Controller Selection
- Controller: sdd-standard
- Profile source: planned-delivery
- Rationale: 先定类型与复杂形状，否则错误抽象会传播；明确依赖比 Lite 有价值。没有外部变更/持久化写入，无额外 Governed 需要。最终审查使用新鲜独立上下文。
