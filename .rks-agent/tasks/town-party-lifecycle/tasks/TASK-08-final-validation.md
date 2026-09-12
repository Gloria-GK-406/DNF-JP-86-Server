# TASK-08 — 扩围结果完整验证与 A21 实机验收

## Task Unit

- Outcome and references: 对完整城镇组队、名册后 HP 与角色详情闭环执行 Spec RQ-001..RQ-015、AC-001..AC-013 和 Plan T-008 的最终验证。
- Scope boundary: 仅做新鲜自动化、Release Host 双客户端实测、证据归档、协议状态同步、独立结果审查与完成记录；校准仅限已授权 Scope In。
- Inputs and dependencies: TASK-07 完成；全部当前实现、测试、协议资料和 Round 5 服务端/UI 证据。
- Required checks: `dotnet build Server/86JP.Server.slnx`；全量测试生成 TRX 并逐项确认无失败/跳过；双客户端在共享区域完成邀请、接受、双端初始 HP、查看目标详情与普通成员离队；服务端日志证明对应收发顺序和当前会话边界。
- Rollback note: 若实机暴露 wire 偏差，只在捕获证据支持的最小编码/顺序边界校准并重跑自动化；无法证明客户端成功时保持 `Implemented` 和任务未完成，不提前标记 `ClientVerified`。

## Detailed Plan

待 TASK-07 完成后补写新鲜验证命令、日志关联窗口、用户操作脚本和预期帧序；执行完整结果审查后再进入 completion verification。任何自动化或实机失败先回到对应实现单元诊断，不以进程退出码、单端 UI 或旧日志替代当前证据。

## Automated Readiness — 2026-09-12

- **Release build**：最新完整源码 `dotnet build Server/86JP.Server.slnx -c Release --no-restore` 成功，0 warning、0 error。
- **Fresh full tests**：`TestResults/task08-final` 共 10 份 TRX。Infrastructure 按 PVF present 301 项与 archive-missing 2 项互斥执行，其余八个测试项目完整执行；逐条解析 3050 个 `UnitTestResult`，全部 Passed，0 Failed、0 Other/NotExecuted。
- **Recovered test exception**：首次 Application 全套运行有 1 个既有 `CharacterSkillLearningTests` 数据行在 schema 初始化时遇到已释放 SQLite handle；该理论 7 个数据行隔离重跑为 7/7，通过后同一 Application 全套重跑为 1369/1369。`TestResults/task08-diagnostics` 保留的是成功的 7/7 隔离复跑 TRX；首次失败的同名 final TRX 已被完整通过批次覆盖，只保留在本次执行输出中。未修改生产代码，最终证据目录只包含完整通过批次。
- **Static integrity**：`git diff --check` 通过。完整变更审查与 A21 双客户端 live oracle 仍是完成前置，当前不判定 TASK-08 或根任务完成。

## Live Oracle Script

1. 两个当前 A21 客户端分别登录不同账号，先确认同处赛丽亚 `[gate]` 房间时互不可见。
2. 两端进入同一 Game listener 的共享 `[normal]` 区域；一方向另一方发起组队邀请，核对邀请者、复活币、剩余疲劳与两个当前占位零值。
3. 目标接受；确认两端都显示同一支两人队伍，并且双方头像在名册建立后立即显示 HP，不要求手工触发 `0x00A6`。
4. 任一端对在线队友点击“查看角色详情”；确认窗口打开并显示的是目标角色信息，同时关联服务端 `GET_USERINFO mode=3 accepted=True` 与 S2C `NOTI 0x0002`。
5. 普通成员主动离队；确认本人恢复无队、队长侧保留单人队并收到 refresh，两个客户端进程都保持存活。

## Result — 2026-09-12

- **Live oracle passed**：用户完成上述双客户端脚本并确认“测试 ok”。`[gate]` 私有、`[normal]` 共享、
  邀请/接受、同一两人名册、名册后双方初始 HP、mode-3 详情窗口与普通成员主动离队均成功。
- **Session correlation**：同一 Release Host 窗口记录两端 `SET_UDP_IP_PORT`、有效 `REQUEST_PEER` /
  `RESPONSE_PEER`、`GET_USERINFO mode=3 accepted=True`、`LEAVE_PARTY accepted=True`，以及幸存队长的
  `CALL_PARTY_MEMBER_REALTIME_INFO inParty=True`。完整证据边界见
  [`a21-town-party-detail-20260912.md`](../../../protocol/patterns/data/traces/a21-town-party-detail-20260912.md)。
- **Automated evidence rechecked**：`TestResults/task08-final` 的 10 份 TRX 共 3050 个 `UnitTestResult`，
  3050 Passed、0 Failed、0 Other/NotExecuted；Release build 0 warning、0 error；最终 `git diff --check` 通过。
- **Protocol status synchronized**：C2S 与可由具体 UI 直接归因的 NOTI 只在证据边界内升级；
  `NOTI 0x0008` 接受回执和 `NOTI 0x000B` 端点投影无法由聚合组队 UI 单独证明，维持
  `Implemented`。缺失端点回落、实际战斗 P2P 和单人“移除队伍”仍不冒充已实机覆盖。
- **Task result**：功能/UI live oracle 已通过，自动化 `3050/3050 Passed`；但本轮未部署
  `0100_NetworkHook`，缺少 TaskContract SC-1/SC-2 要求的双端 S2C 抓包及 SC-9/SC-10 要求的
  对应逐帧记录。证据状态修正后的独立复审为 `approved`，但 TASK-08 保持 `in_progress`，等待带抓包的
  最小复测。

## Integration Readiness — 2026-09-12

- 用户在知悉逐帧证据缺口后明确授权先合并当前工作分支并清理任务 worktree；该授权接受当前可运行结果，
  不把缺失的协议证据改写为已完成。
- 当前源码重新执行 Release build，结果为 0 warning、0 error。
- `.rks-agent/tasks/town-party-lifecycle/verification/premerge-20260912` 中 10 份新鲜 TRX 经逐项统计为
  3050 Passed、0 Failed、0 Other/NotExecuted；Infrastructure 的 PVF present 301 项与 archive-missing
  2 项仍按互斥环境分别执行。
- `git diff --check` 通过。TASK-08 与根任务继续保持 `in_progress/manual_required`；本次合并属于用户明确
  接受当前实现并开始后续队伍模式任务，不属于协议证据补全。
