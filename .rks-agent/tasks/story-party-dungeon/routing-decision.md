# Routing Decision

- TaskContract: `.rks-agent/tasks/story-party-dungeon/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标、首版范围、排除项和可观察验收均已稳定；已知实现方向是把当前按角色的副本运行态重构为共享局加逐角色参与者，并在既有剧情/普通模式分流之后编排队伍入场。双端真实帧仍待最终实机验证，但最小首发入场序列可在独立协议边界内实现并以实测校正，不改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求直接推进到可实机测试，同时此前持续强调领域边界与普通/剧情模式隔离。Direct 最可信的失败路径是只打通队长进本，却遗漏共享死亡幂等、逐角色任务/奖励和失败时整体收口，造成双端表面进入但运行态分裂。一个持久 Spec、显式依赖计划和完整结果审查能让这些耦合条件在实现与验证中保持一致，其精度收益值得本次额外时间；所有行为共同组成一个不可分割的双人副本验收结果，不存在独立可接受的子结果。
- Reroute conditions: 若实现证据表明 A21 首发入场必须依赖尚未知且无法通过现有最小序列触发的协议，或任务/结算必须拆成可独立上线与回滚的前置结果，则停止当前执行并重新评估任务语义或 Multi Spec 边界。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 的事后完整审查不能便宜地纠正共享局标识、参与者附着、协议投递、共享事实和逐角色副作用之间的错误依赖顺序；Standard 的显式 Plan 和依赖映射可以在编码前固定提交边界和迁移顺序。当前不涉及安全、权限、不可逆数据迁移或其他需要独立挑战/审批的高后果资产，因此 Governed 的额外控制没有相称收益。
