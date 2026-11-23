# 实施方式

- TaskContract: `./TaskContract.md`
- Contract revision: `3`
- 独立评审：[已批准的工作区契约](task-contract-review.md)

## SDD Readiness

四职业范围、角色入口和存档边界已由用户确认。任务与部分类别的 A21 包形仍需实机样本，当前以对照证据实现，待四职业齐备再做 A21 验收。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 四职业共用授职与放弃事务、状态源、配方和摊位。四职业一起达到验收边界；用同一规范和依赖计划，防止实现中重复保存职业状态或将双人结算拆开。
- Reroute conditions: 出现须独立交付和回滚的结果，或已确认的资产结算边界无法通过现有事务机制承接。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 角色状态、任务入口、摊位两个会话和协议投影存在实际先后依赖。按单元分配接口与所有权，避免在完成后才发现早期决定需要四处改动。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `cada01a0ec33ad9e3ec34fcad1ce2ca475244fd4`
- Gates: spec `none`, plan `none`, implementation `auto`, review `auto`, verification `auto`
- Bound methods: none
- Artifact path overrides: none
