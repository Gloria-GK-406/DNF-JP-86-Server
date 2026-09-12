# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

该子结果边界稳定：只交付保守共享设置模型、规则服务、严格 `0x000C` 基础闭环和默认普通兼容；模式
特有玩法明确留给后续子规格。客户端取证可在授权范围内决定具体 wire 形状，但不会改变本子结果语义；
每项验收均有可观察证据。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 直接实现最可能遗漏的是把上游原样字节或静态类别线索提升成未经 A21 证明的共享规则，
  造成后续三个模式被错误抽象锁住。一个持久 Spec、完整结果审查和新鲜验证能保护保守建模与协议证据
  边界，精度收益值得额外时间。该子结果共享同一原子设置变更和通知闭环，只有一个接受/回退边界，
  不存在再拆为多个独立 Spec 的结构理由。
- Reroute conditions: 若当前 A21 证明 `0x000C` 创建与编辑并非同一设置状态转移，或基础模型无法在不
  引入某一模式完整语义的情况下成立，则返回根控制器重定后继结果；若只出现可在当前设置闭环内修正的
  字段差异，则保留 Single Spec。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 该子结果需要先固定客户端取证停止条件，再协调设置模型、严格 codec、领域原子性和通知
  顺序；显式 Plan 能阻止一个错误 wire 判断传播到所有层，这是 Lite 的最终审查无法同样低成本修正的。
  当前变更不触及安全、凭据、不可逆外部状态或数据迁移，普通独立结果审查和新鲜验证足以控制风险，
  因而不需要 Governed。
