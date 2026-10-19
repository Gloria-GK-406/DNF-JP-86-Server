# Routing Decision

- TaskContract: `.rks-agent/tasks/lord-tower/TaskContract.md`
- Contract revision: `1`；同级独立review已approved且路径/版本匹配。

## SDD Readiness

用户已接受Document/LordTower-Design-20261019.md并授权实现部署。八阶段和全队原子扣票、奖励、补票边界明确；UI剩余不确定性是实机验证边界，不改变完整候选交付。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求契约并在部署后真人验收。直接开发容易把无maze准入、普通塔奖励和专用协议混用，导致入口能点却扣费/结算不完整。一个完整塔闭环需共同验收，Spec覆盖和顺序计划比事后补错更省返工；没有独立可交付子成果。
- Reroute conditions: 原生证据推翻阶段/结算中心方案，出现独立验收或恢复边界，或发现未受保护的数据丢失/安全权限风险时重评。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 准入头部必须先于无maze入场，运行态/事务必须先于投影，之后才能安全部署；Lite不保留这些关键依赖。新交易检查属于游戏当前实例合法性，不修改身份认证或系统权限；部署有已验证备份和回退能力，无需更强控制。普通独立结果评审和 fresh verification足以保护本次候选交付。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `6f19c07b4cd2b5c2656198dbfb725d80544bcf13`
- Gates: spec:none; plan:none; implementation:auto; review:auto; verification:auto
- Bound methods: implementation:`test-driven-development`; review:`reviewing-work`; verification:`verification-before-completion`
- Artifact path overrides: none

## Workspace

workspace_root: `E:/Project/DNF/DNF-JP-86-Server`；workspace_kind: current-checkout；task_branch: `task/lord-tower-20261019`。
2026-10-19 git root/common dir均为主checkout，source next-round与新任务分支起点均6f19c07b；无已暂存/已跟踪修改。已有未跟踪路径以`artifacts/gold-silver-20261019/preexisting-files.json`及启动git status为边界，不属于本任务，不纳入提交/回滚。任务启动文件仅同级契约及评审。生产改动前规划提交；实施检查通过后实现提交；独立review和新鲜验证后seal。保留分支等待用户后续集成选择。
