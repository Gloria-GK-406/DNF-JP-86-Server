---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result：活动管理与累计周签到

状态修正（2026-10-05）：下文CP936审查结论遗漏客户端兼容补丁，现按运行时65001统一UTF-8；历史测试通过不代表编码选择正确，见[006C记录](../../../protocol/game/server-to-client/NOTI-0x006C.md)。

## Delivered

- 活动管理从注册实例汇总领域信息，2208周签到的管理与玩法接口共享同一对象；支持配置、启停及显式新期次。
- 首例接通真实推荐通关、业务周进度、系统奖励邮件、SQLite去重及事务，入场/签到/通关/管理变化投影客户端日历和状态。
- `/gm/events`页面提供当前期次与PVF奖励列表、独立配置/启停/新期窗口、统一通知及故障重读恢复；Chrome隔离验证通过。
- 独立审查发现的CP936编码和说明目标次数问题已修正并复核通过。

## Verification

- [最终逐条件验证](verification/completion-evidence.md)为pass，范围是可供A21人工验收的开发实现。真实游戏GUI及bonus范围不冒充已验。

## Repository Result

- 改动集中于Server活动契约/领域/内容/持久化/GM与协议接线和测试、Web活动页、protocol人工记录及本任务Document证据。
- 接受前基线`af0715492d9b9850756afdd908f8a93614795ea6`；一个本地接受结果提交由控制器在审查和验证通过后创建，提交ID另行报告。
- 分支`task/warehouse-storage`、原任务工作区保留；不需要父级回滚。在线服务仍为之前版本，未部署本次改动。

## Artifacts

- [使用说明](../../WeeklyAttendanceEvents.md)
- [人工验收清单](Human-Test-List.md)
- [最终独立审查与修正历史](reviews/change-review.md)
