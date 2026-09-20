---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 地下城难度类型与角色最高可选难度

## Delivered

- Pack校验并转换DungeonDifficulty，业务实例和房间/奖励/任务上下文使用枚举。
- 角色每图最高难度缺省Normal，支持持久化、只升不降、幂等及旧重复记录兼容。
- 单人超限入场不创建实例；权限通知正确编码，选图重读进度。
- 普通、冒险实例与客户端一致，换房及重登权限实机验证通过。

## Verification

[完成证据](verification/completion-evidence.md)；[独立审查](reviews/change-review.md)。

## Repository Result

- Changed paths: Server/src领域、持久化、协议链；Server/tests对应回归；Document/tasks/dungeon-difficulty契约、规格和证据。
- Accepted baseline before result: 026c4f9f50baa8e47d05260b45593d07f546d2e8。
- Task branch: task/dungeon-difficulty；源main保持不变，集成待用户选择。
- Parent rollback required: no。
- 测试服务仍运行于本工作树runtime下，使用数据库副本；清理工作树前须结束该测试运行。
