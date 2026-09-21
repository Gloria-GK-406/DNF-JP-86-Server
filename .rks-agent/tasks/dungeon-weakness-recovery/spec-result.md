---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 地下城虚弱与自然恢复

## Delivered

- 服务端内存虚弱、失败退出施加、统一自然恢复、离线恢复和入本暂停。
- 登录/回城/恢复周期的当前值投影，替换临时诊断注入。
- 读取失败的可重试补偿、配置与行为回归测试。

## Verification

[当前验证证据](verification/completion-evidence.md)。完成范围为服务端实现与实机交接；完整客户端生命周期仍待用户实测。

## Repository Result

- 工作区：`.worktrees/dungeon-weakness-recovery`，分支 `feat/dungeon-weakness-recovery`。
- 基线：`88741631d4f104fe63d35945f896dd492d647c50`。
- Changed paths：Abstractions/Characters 与 World；Application/Characters、World、Sessions、注册；Protocol 的弱状态、登录、回城、退图与全灭切片；Host配置；相关测试；protocol人工记录及Document使用说明。
- 仅提交本次隔离工作区完整结果，主分支既有文档修改和外部调查保持原样；不自动合并或清理。
- Parent rollback required: no

## Artifacts

[功能与实机步骤](../../../Document/地下城虚弱与自然恢复.md)。
