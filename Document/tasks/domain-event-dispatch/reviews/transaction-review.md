---
kind: rks-review
verdict: approved
---

# 事务与后台消费专项独立审查

日期：2026-10-08；基线：ef00741dc3d4ee826c14b3d273042f38c3a1c31f。
目标：当前完整交付及首轮修复；依据 TaskContract rev1、spec.md、plan.md、迁移矩阵及 reviewing-work 代码判据。
审查者：独立子代理 `/root/event_transaction_review`，没有参与实现；只读检查，不修改文件或执行构建测试。

## 问题与复核

首轮 changes_required：Task.WhenAll 的惰性枚举可能被首组持续同步完成的积压阻止，后续组无法启动。

修复前以 DomainEventSchedulingTests 的同步持久化和消费者、首组持续到期事实复现超时失败（scheduling-red.log）。RunGroupAsync 在入口和成功批次后 await Task.Yield；修复后相关 71 项通过（remediation-green.log）。

最终 approved：

- 初始 Yield 保证所有组任务能创建，每批 Yield 避免持续积压独占执行；让出在 drain 返回之后，组门、事务和数据库准入门已释放，没有延长持锁或增加每事件 Task.Run。
- 调度测试明确等待 BusyStarted 和 OtherStarted，再验证取消退出，覆盖首轮缺陷触发条件且没有降低断言。
- 可靠更新与完成标记同事务；失败事务先回滚再独立记录重试；失败退避、取消及恢复边界成立。
- LiveOnly 先提交消费标记再投影，旧实例、TTL 与批次合并保持；失败不会补播。
- 邮件/Joust 回归只增加显式 drain，原消息数量和类型断言保留。未发现修复引入新阻断问题。

## 证据边界

审查者亲自读取 RED/GREEN、全解决方案 build-reviewed.log（0 警告/错误）及 host-diagnosis.log（1/1）。这是专项审查批准；完整最终验收引用 verification/completion-evidence.md，不以旧首轮失败 TRX 宣称通过，不声明 ClientVerified。
