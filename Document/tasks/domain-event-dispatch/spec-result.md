---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# 结果：统一事件入口与订阅分流

## Delivered

- 所有原业务/协议/HTTP 的直接通知调用统一进入 IEventService；仅通知投影适配层接触低层总线，保留在线和精确会话、批次及同步回复顺序。
- 强类型事实与独立订阅持久化，事务一致性、后台唤醒/等待、重试、重启恢复和停服取消已实现；邮件每信保留事实，提醒合并；活动配置和马战生命周期走独立订阅。
- Spec 写明前后结构和职责，43 文件迁移矩阵与两份独立批准审查保留；权威架构指引更新。

## Verification

- [完成验证](verification/completion-evidence.md)：全部契约条件通过；构建零警告/错误，四个完整套件共 5236 通过、零失败/跳过。仅实现与自动回归证据，无 ClientVerified/部署声明。

## Repository Result

- 工作树：E:/Project/DNF/DNF-JP-86-Server/.worktrees/domain-event-dispatch；分支：task/domain-event-dispatch。
- 接受基线：ef00741dc3d4ee826c14b3d273042f38c3a1c31f；完整结果在一次本地接受提交成功后封存，提交身份由 Git 外部报告，不在本文件回写自身提交号。
- Changed paths：本任务 Server 生产/测试、AGENTS.md 和 Document/tasks/domain-event-dispatch；原深渊工作树及 main 保留。
- Parent rollback required：no。
- 分支/工作树保留；部署、集成和清理等待后续明确授权。

## Artifacts

- [职责 Spec](spec.md)、[调用迁移矩阵](migration-matrix.md)
- [完整结果独立审查](reviews/change-review.md)、[事务专项独立审查](reviews/transaction-review.md)
