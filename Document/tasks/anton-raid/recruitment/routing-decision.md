# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

契约第 1 版已独立评审通过。目标、业务边界和四项 A21 验收结果清楚；申请预检之后的客户端行为由双端取证确定。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求以契约交付完整招募。仅验证服务端名单会漏掉 A21 团号缓存、申请弹窗和双端编组同步。把入口、申请和投影放在同一份 Spec 下，所有环节共同通过最后的双客户端验收。
- Reroute conditions: 发现本次需要独立上线的功能或超出招募的玩家数据变更时，先重新对齐契约。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 先接通真实频道与选角，再核对申请预检及通知，避免错误的 A21 帧序扩散至审批和成员投影。显式计划可以保留依赖和取证入口；隔离开发验证和独立结果审核足以承接当前风险。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Workspace: `E:/Project/DNF/DNF-JP-86-Server-anton-raid`, linked worktree, `feature/anton-raid`; preexisting changes: none
- Spec baseline: `7f6f5ca673fffd3c82af2978e1ae8d23f352112f`
- Gates: implementation `auto`, review `auto`, verification `auto`
- Bound methods: implementation `test-driven-development`
- Artifact path overrides: none
