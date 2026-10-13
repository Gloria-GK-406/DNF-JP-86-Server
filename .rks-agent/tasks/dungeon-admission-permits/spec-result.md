---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 地下城入场资格与费用凭证

## Delivered

- 已实现每参与者凭证数组、强类型材料/次数费用、消费后再创建房间与运行实例的共同入场骨架。
- 普通、循环、主动深渊、镜像、异界接线完成；免费凭证有效，疲劳仅作资格检查，出生房/新房间仍各扣一次。
- 全队扣费/创建失败以及正常拒绝均整体回滚；凭证及收据一次性，消费复核资源，保留既有错误类别、再挑战及镜像会话检查。

## Verification

- [完成验证](verification/completion-evidence.md)：实际PVF完整构建与测试通过，独立结果审查approved。

## Repository Result

- Changed paths：Abstractions资格/成本契约、Application入场/材料/事务及DI、对应测试、Document说明、AGENTS职责与本任务证据。
- Baseline：67ffa14f82e8e789e53e7a22fe1af1b85627d57e；完整结果以 task/dungeon-admission-permits 上一次本地接受提交封存。
- Parent rollback required：no。
- 工作区及分支保留；未合并、未推送、未部署。后续整合/清理待用户选择。

## Artifacts

- [结构说明](../../../Document/DungeonAdmission.md)
- [独立审查](reviews/change-review.md)
