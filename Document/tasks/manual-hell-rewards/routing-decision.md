# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户已明确冻结奖励、末怪释放和部署；现有 Cell 事务可承载，不存在改变交付含义的开放问题。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 将组归属及一次性结算写入规格并独立审查，防止遗漏导致重复发奖；规则、冻结与死亡提交共同形成一个验收边界。
- Reroute conditions: 现有事务无法原子提交，或发现奖励必须改变已经接受的流程。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- Rationale: 实现路线已经明确，完整结果独立审查足以保护组归属和普通掉落；额外 Plan 不增加实质控制。
