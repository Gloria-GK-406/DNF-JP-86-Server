---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 单进程组件分层

## Delivered

- Host 缩为组合根；HttpApi 与 GameServer 各自管理装配和生命周期，共用一个根容器及业务/持久化。
- HTTP 可独立启停，游戏未就绪不签发票据；PVF 预热屏障、停止释放与既有配置/发布兼容得到验证。
- 环境配置文件发布复制回归已在独立审查后修复。

## Verification

- [完成验证](verification/completion-evidence.md)：双平台全量回归、真实PVF Host、双平台自包含入口探测、环境配置探针及隔离证据。
- [独立审查](reviews/change-review.md)：round 2 approved。

## Repository Result

- 基线：`d32ecfc9b4228a658c01efdfa36b3d41b23aba78`。
- 分支：`task/host-component-composition`；工作区：`.worktrees/host-component-composition`。
- 变更：新增GameServer、Abstractions就绪接口、Host配置辅助类；LauncherApi及测试更名HttpApi；迁移Program装配、增加Host测试、更新solution/架构/发布说明及发布文档复制。
- 本记录随唯一 accepted-result 本地提交封存；提交号由最终回复给出，不为写回自身哈希再提交。
- Parent rollback required: no。
- 保留工作区；未推送、PR、合并、打tag或替换现有服务。后续集成需用户选择。

## Artifacts

- [组件说明](../../host-component-composition.md)
- [TaskContract](TaskContract.md)
