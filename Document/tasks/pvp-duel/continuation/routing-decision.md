# Routing Decision

- TaskContract: `Document/tasks/pvp-duel/continuation/TaskContract.md`
- Contract revision: `2`

## SDD Readiness

用户已确认先恢复 CH50 自由房并启动实现。契约第 2 版独立审核通过。技能编辑和另外两种频道环境以 A21 证据限定，已证的技能读取与 CH50 流程可以先做。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 决斗页投影、房间阶段和结算都要在同一条玩家流程中验收。显式规格及分段计划可以防止丢掉普通页隔离、离场时的阶段语义和旧库兼容，也给实机证据留下可核查的落点。
- Reroute conditions: 若客户端取证要求切出另有独立交付和启用方式的新玩法，重新核对是否另立契约。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery` 加长程选项
- Rationale: 技能接入、A21 渠道取证和房间收敛存在明确先后，同一分支内逐段实现和复核可降低返工。已确认的产品范围与隔离验证允许自动推进各内部阶段。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `yes`
- Spec baseline: `bcb547cf73ffa377d7876bd02b7a12c14ac98ea2`
- Gates: implementation `auto`, review `auto`, verification `auto`
- Bound methods: implementation 依据配置使用 `executing-tasks` 并在出现偏离时使用 `reviewing-work`
- Artifact path overrides: none
- Workspace: `E:/Project/DNF/DNF-JP-86-Server-pvp`，`feature/pvp-duel-phase1`
- Preexisting changes: `.rks-agent/tasks/pvp-duel-phase1/reviews/change-review.md`
