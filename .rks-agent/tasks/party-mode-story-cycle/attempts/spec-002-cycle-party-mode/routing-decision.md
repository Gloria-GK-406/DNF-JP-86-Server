# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

Cycle 是根任务当前唯一活动候选，类别边界、排除项、证据顺序与客户端可观察验收已稳定；target/default
与无区域 sentinel 是本子规格需要闭合的证据问题，不改变 Cycle 设置与生命周期这一单一结果。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: Cycle 的静态构包、区域规则、通知和实机验收必须共同成立；若直接从 Story 或 UI 文本类推，
  最容易把未知区域 ID/sentinel 固化为错误规则。一个持久 Spec/Plan、完整结果审查与新鲜验证能实质阻止
  该失败，且一次完整 Cycle 结果的精度收益值得流程成本；拆成多个可接受 Spec 会留下不可用半模式。
- Reroute conditions: 若证据证明 Cycle 基本创建/进入依赖未授权快速匹配，返回根控制器；若 Cycle 与
  Story 共享不可分割的原子状态机并会改写已封存边界，停止并重新评估根结构。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 相比 Lite，Cycle 的区域资料定位、无区域 sentinel 裁决、规则变更与实机验收有严格顺序依赖；
  显式 Plan 能把证据门放在产品修改之前，并在静态结论不足时形成可恢复停止点。普通完整结果审查和新鲜
  验证足以保护可逆代码/客户端实验；不存在需要 Governed 独立挑战或高风险资产控制的事实。
