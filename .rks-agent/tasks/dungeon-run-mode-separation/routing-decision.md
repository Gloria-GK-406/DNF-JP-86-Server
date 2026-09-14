# Routing Decision

- TaskContract: `.rks-agent/tasks/dungeon-run-mode-separation/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

目标与保护边界已稳定：共享副本运行机制，但分离普通与任务关联路线的入口、完成和结算；普通进入不得因持有任务而升级为剧情。任务可由现有实机日志、PVF 元数据、当前/旧实现和可重复测试验证。具体进入意图承载字段仍需在诊断阶段由证据选定，但不改变目标；证据不存在时契约要求停止而非猜测。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 直接修改最容易遗漏“意图与资格分离”，使修好的剧情选择反过来污染普通副本；一个持久 Spec、因果诊断、修复计划、完整结果审查和新鲜回归验证能把入口信号、运行目的、任务推进、结算与退出作为同一闭环检查。所有变化共同服务于一次地下城运行的单一验收边界，不具备可独立接受或回滚的多个结果。
- Reroute conditions: 若证据表明普通与剧情使用完全独立的协议族或持久运行系统，形成可独立验收/回滚的实现边界，则重新评估 Multi Spec；若无法从客户端或前置状态取得剧情进入意图，则在诊断阶段按契约阻塞，不以启发式继续。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `repair`
- Rationale: Lite 的最终审查无法低成本纠正一个已经贯穿内容模型和运行态的错误入口判据；Standard Repair 的先诊断后计划可在改动模型前闭合意图信号，并明确跨层迁移顺序。当前不涉及安全、授权、不可逆外部变更或数据丢失风险，普通独立审查与新鲜验证足够，无需 Governed。
