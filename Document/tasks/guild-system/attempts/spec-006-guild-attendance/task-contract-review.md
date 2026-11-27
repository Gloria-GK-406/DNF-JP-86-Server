---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.worktrees/guild-system/Document/tasks/guild-system/attempts/spec-006-guild-attendance/TaskContract.md
contract_revision: 1
reviewer: /root/guild_attendance_contract_review
selection_basis: 独立审查者，未参与本子契约建模、意图对齐或路由
verdict: approved
---

# 公会自动签到契约审查

依据为用户给出的 A21 截图、对自动签到与两处显示的提问及后续授权「你来实现吧」，[根契约](../../TaskContract.md) SC-05、SC-09、SC-10，以及 [0x02E9](../../../../../protocol/game/client-to-server/0x02E9.md) 和 [NOTI 0x0046](../../../../../protocol/game/server-to-client/NOTI-0x0046.md) 的本地证据。

契约纳入角色入场自动签到、同日去重、跨日计数、会员隔离、进度和名单共用数据以及旧库保留。服务端本地日历日取自 A21 参照，原服重置时刻仍待证实。奖励及相关成长功能由根契约承接。线上部署和代用户操作实机处于当前授权之外，契约保留用户实机验收。三个验收条件分别给出持久性、协议表现和升级后的可检查结果。

| 维度 | 证据类型 | 依据 | 实际影响 |
| --- | --- | --- | --- |
| quality | observed | 原生计数与明细证据对应 AC-1 和 AC-2，AC-3 验证旧库保留 | 可分别发现签到写入、界面读数及迁移的偏差 |
| efficiency | proxy | 两处显示共用同一签到语义与验证场景 | 预期一次双连接验证即可对照计数和成员名单 |
| cost | proxy | 复用已建立的测试公会及隔离旧库 | 预期避免另建验收客户端和准备生产数据 |
| cognitive burden | observed | 可观察结果给出 1/2、2/2 和下线后继续列出 | 界面验收可直接对照画面 |
| actual human participation | observed | 用户提供两处显示的实机截图并明确授权本期实现 | 人工实机确认仍需后续发生，当前只能确认功能意图与授权 |
