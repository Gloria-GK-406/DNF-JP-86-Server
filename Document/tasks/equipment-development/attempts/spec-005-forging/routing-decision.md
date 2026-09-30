# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

根任务 SC-5 已给定锻造闭环和自动/实机分界；本子项的目标、资产边界和证据要求稳定。A21/PVF 的具体规则作为 Spec 前取证决定，不在契约中猜定。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求每一内聚装备功能独立接受；锻造的装备字段、材料、成功/失败及刷新构成一个资产边界。直接编码易将普通强化规则带入锻造，Spec 与独立结果审查有明确收益；本子项不含另一个独立接受结果。
- Reroute conditions: 若 A21 证实存在多个独立可接受且相互不依赖的锻造玩家入口，返回父控制器重新划界。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: PVF 规则、状态字段、概率决策及协议响应相互耦合，显式 Plan 可在扣料代码前锁定依赖次序和未闭合路径。普通独立审查与新鲜验证足以控制当前局部事务风险；未触及需要更强治理的外部不可逆操作。
