---
kind: rks-review
verdict: approved
review_round: 2
---

# 马战累计结果审查

当前判决为 Round2 `approved`；最小复核及当前输入身份见末节。以下 Round1 保留为未改变主体的独立审查证据。

## Round1：完整冻结结果

审查日期：2026-10-06。独立审查者在 fresh 上下文只读检查，收到主控明确 `target frozen` 后复核最终改动；仅写本报告，未运行 dotnet/npm、提交、部署或操作真实数据库。使用 reviewing-work 的 Code、Deliverable 标准，依据 TaskContract revision 1、spec、plan，以及工作区/主仓 AGENTS.md。未提供或要求额外 challenge set。

目标工作树：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`；分支 `task/warehouse-storage`；HEAD 与 accepted baseline 均为 `b4bf8d0d01113c39962c885b44002453dcb740c6`。范围为该基线到冻结工作树的全部累计 tracked diff 和未跟踪制品，非仅协议阶段；31 个 tracked 修改 + 36 个 untracked 文件 = 67 个输入文件。排除本报告与忽略目录内的 build/test 输出。

输入身份：按路径排序，对每个文件记录 `路径 + 空格 + 文件SHA256大写`，以 LF 连接、UTF-8 编码后取 SHA256。冻结初始集合为 `CCDAE4CFA589AC189204B504870CD713A87B6E1FF426BCD6191A27B277B3D6CE`；主控随后同步 implementation-result 总结和 sdd-execution 的 review 阶段，审查已读其最新内容，再取同一67路径得到 `51090C84E6EEE74BC43678470A2A3E623933C189CF572D047C08FF1BB9DBEA2C`。生产代码/spec/plan 未改变。本判决不自动覆盖后续生产变更；控制文档后续阶段同步不代表新的生产审批。

## 判决依据

| 范围 | 实际核对及结果 |
|---|---|
| 统一管理与内容边界 | ApplicationModule 将管理/玩法/只读投影绑定同一 JoustDomainService singleton；通用活动有效期/启停不包含赛程字段。IJoustDefinitionSource 仅由规则服务读取，typed PVF source 提供骑士、名称和物品模板；设置 API 不暴露 GrantTemplate。 |
| 赛程与快照 | schedule revision 独立落盘，默认仅作为缺失配置的回落；Current 优先已保存当前轮次。轮次保存 schedule、definition、名单、完整淘汰结果；绝对 UTC+8 起止、DailyRoundNo 均从保存快照计算。修改当前设置不会重算旧轮。 |
| 下注与资产 | Write 在读业务事实前取得 SQLite 写权限；同一 ambient 域事务内校验角色当前等级、个人限额、奖池范围及材料，再扣库存、插入投注。优先指定槽，同币跨槽，拒绝非法选中物品，稳定请求键碰撞核骑士/数量/槽。宽整数和 decimal 计算，下注前限制协议/奖品可表示范围。 |
| 结算与恢复 | Advance 对全部未结算轮次推进，关闭/到期不移除结算责任；仅 Result 结算，Final 虽揭示七胜者仍不发奖。prepared 附件、邮件和 Settled 标记同一 SQLite 事务；故障后回滚、重启和重复推进不会双发。已结算轮次在时钟回调后拒绝新下注。 |
| 物理请求与重放 | 原生投注实现 IEffectSettlementRequest/IRequestSettlementCommand，复用物理连接128帧 ledger 的 seq+完整 raw body 身份，正常 ushort 回绕保持新 ordinal。持久键使用物理 GUID/selection generation/transport ordinal；确认回滚可重试，提交后发送失败只投影当前状态。直接扣料经 EffectOperationContext 在 Freeze 前登记槽足迹，耗尽槽的直接响应和重复请求投影均能发000E清空。 |
| 原生字段与接线 | 与七条协议人工区、静态准备及 codec 对照：查询14B、投注23B/i32槽，3/90/46/31B通知，04BB七个四u8对局，历史成功/失败flag1路径均3505B。State采用参考策略0/1/3/4/2；Results阶段0/1/2/3对应累计4/6/7/7。特殊骑士隐藏flag、Aux0合法首项明确作为服务器策略；保留 LinkKey 两个斜杠参数。 |
| 通知与会话 | JoustChanged Compose 经只读 ReadAsync，不创建轮次/补推进；EventsChanged时钟及查询也收进只读领域投影。原生查询/下注持 selected transition exclusive lease 至实际投递，并校验精确物理连接和会话；只读 replay 不覆盖主命令的事务足迹。后台事务提交后按状态签名通知，无变化抑制。 |
| UI与回归 | 设置只提交六字段赛程和独立 revision；冲突/未知结果及重读失败保留草稿并锁写，读取外部新值后显式核对，普通400可继续编辑。tracked diff未覆盖基线经验/等级实现，也未修改初始化/物品目录/签到规则；原签到相关变化限于增列后的测试选择与只读通知接线。 |

## 预审发现与冻结后复核

本轮冻结目标没有未解决阻断项。以下预审问题已由唯一生产写入者修正，并在同一审查轮最终复核：

1. **补结算返回下一轮/Inactive时漏邮箱提示**：旧实现把刷新条件绑定返回 Phase.Result，实际补发旧轮邮件后可能返回 Betting/Inactive。最小修正已落地：JoustDomainService.StateAsync:154-155 保留瞬态 SettledDuringAdvance；成功 Bet 重投影仍保留它，拒绝返回原 State；JoustSlice:130/154 按真实补结算标志刷新邮箱。新增 Query_catchup_refreshes_mailbox_when_result_has_already_become_next_round_or_inactive 两个时间路径，冻结日志均通过。
2. **允许零 Result 窗口**：总阶段时间等于轮长时，Result 起点同时是 ends_at 排除边界，该轮 Result 永远无法经当前轮查询/推送，确定性缺失最终状态。最小修正为 JoustRuleService:35 与 JoustSettings 的总时长判断改为 `>=` 拒绝；领域、HTTP400、前端不写入判据一致，不新增字段。使用说明同步要求正时长窗口。此裁定来自状态可达性，不借未验证的 Stage3 文本猜测效果。
3. **故障判据表名错误**：原生回滚测试触发器已由 joust_bets 改为真实 event_joust_bet，最终日志证明真实 SQL 注入/回滚/同帧重试路径通过。

最终 delta 另核：物理槽转 BusinessSlot 已移至 message handler，CMD 仅接业务槽调用领域；新增 RecordInventorySlots 接缝仍满足主事务/会话/frozen 检查。两个旧未知枚举判据从1024迁至2048，是因为新增 Joust 占用1024，拒绝未知位的语义未删弱。

## 实际验证证据与边界

审查直接读取了代码、累计 diff、测试判据和日志末尾；阶段报告只用于定位证据，没有代替实现审查。冻结源码相关日志：protocol-application-check 105/105，protocol-codec-check 70/70，protocol-host-check 13/13，protocol-ui-check 49/49，均通过；protocol-ui-build 的 tsc/Vite 成功。审查自身重新执行 git diff --check，exit0。较早 backend-application-check 89/89 含经验/库存/邮件回归，backend-host-check 13/13；ui-result 记录全前端137/137，属于其阶段版本，不能当作最后新增拒绝条件后的全前端重跑。

本判决允许进入主控最终完成验证，不等于 SC-1..SC-7 已由本审查执行全部 fresh verification；当前主控仍需按契约核对初始化、目录、经验、签到及完整交付证据，再接受本地结果。任何后续生产整改必须复审明确的新 revision。

非阻断实机限制已在 JoustEvents/Human-Test-List 中保留：当前证据是 ClientStatic + ReferenceImplementation + ServerPolicy + CurrentImplementation，没有本次 WireCaptured/ClientVerified。原生日历窗口、黑骑士、State实际语义、Aux动画、Stage3最终文本、短赛程本地倒计时同步、真实领取仍由用户后续验收。线包不携带可编辑时长，不能承诺任意短 StageSeconds 的客户端展示精度。归档2016币的领域特许不阻止既有选角清理过期库存，测试须用有效实例；本活动奖励模板ExpireTime=0且没有放宽其他物品。跨重连的相同内容新请求不是旧业务ID，128帧重放边界不能升级为跨重连任意重复点击幂等。

## 完整路径范围

tracked（31）：

```text
Server/src/Dfo.Server.Abstractions/Effects/IReplayStateProjection.cs
Server/src/Dfo.Server.Abstractions/Events/EventServices.cs
Server/src/Dfo.Server.Application/ApplicationModule.cs
Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs
Server/src/Dfo.Server.Application/Effects/EffectOperationContext.cs
Server/src/Dfo.Server.Application/Effects/ReplayStateDomainService.cs
Server/src/Dfo.Server.Data/Events/EventModels.cs
Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs
Server/src/Dfo.Server.GmWeb/GmEventsEndpoints.cs
Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs
Server/src/Dfo.Server.Persistence/PersistenceModule.cs
Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs
Server/src/Dfo.Server.Protocol/Game/ReplayStateProjectionDelivery.cs
Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs
Server/src/Dfo.Server.Protocol/Game/WeeklyAttendance/WeeklyAttendanceSlice.cs
Server/src/Dfo.Server.Protocol/ProtocolModule.cs
Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationHandlerArchitectureTests.cs
Server/tests/Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs
Server/tests/Dfo.Server.Application.Tests/Sessions/ReplayStateSnapshotTests.cs
Server/tests/Dfo.Server.Host.Tests/GmEventsHttpTests.cs
Web/gm-admin/src/Events.test.tsx
Web/gm-admin/src/EventsView.tsx
Web/gm-admin/src/events/activityViews.ts
protocol/catalog.md
protocol/game/client-to-server/0x04E6.md
protocol/game/client-to-server/0x04E7.md
protocol/game/client-to-server/0x04E8.md
protocol/game/server-to-client/NOTI-0x04B8.md
protocol/game/server-to-client/NOTI-0x04B9.md
protocol/game/server-to-client/NOTI-0x04BA.md
protocol/game/server-to-client/NOTI-0x04BB.md
```

untracked（36；不含随后创建的本审查报告）：

```text
Document/JoustEvents.md
Document/tasks/event-joust/Human-Test-List.md
Document/tasks/event-joust/TaskContract.md
Document/tasks/event-joust/backend-application-check.log
Document/tasks/event-joust/backend-host-check.log
Document/tasks/event-joust/backend-result.md
Document/tasks/event-joust/implementation-result.md
Document/tasks/event-joust/plan.md
Document/tasks/event-joust/protocol-application-check.log
Document/tasks/event-joust/protocol-codec-check.log
Document/tasks/event-joust/protocol-host-check.log
Document/tasks/event-joust/protocol-preparation.md
Document/tasks/event-joust/protocol-result.md
Document/tasks/event-joust/protocol-ui-build.log
Document/tasks/event-joust/protocol-ui-check.log
Document/tasks/event-joust/recovery.md
Document/tasks/event-joust/routing-decision.md
Document/tasks/event-joust/sdd-execution.yaml
Document/tasks/event-joust/spec.md
Document/tasks/event-joust/ui-result.md
Document/tasks/event-joust/workspace.md
Server/src/Dfo.Server.Abstractions/Events/JoustServices.cs
Server/src/Dfo.Server.Application/Events/EventPlayerProjectionDomainService.cs
Server/src/Dfo.Server.Application/Events/GmJoustDomainService.cs
Server/src/Dfo.Server.Application/Events/JoustDomainService.cs
Server/src/Dfo.Server.Application/Events/JoustMaintenanceService.cs
Server/src/Dfo.Server.Application/Events/JoustRuleService.cs
Server/src/Dfo.Server.Data/Events/JoustModels.cs
Server/src/Dfo.Server.Infrastructure/Content/PvfJoustDefinitionSource.cs
Server/src/Dfo.Server.Persistence/Events/SqliteJoustPersistence.cs
Server/src/Dfo.Server.Protocol/Game/Joust/JoustSlice.cs
Server/tests/Dfo.Server.Application.Tests/Events/JoustIntegrationTests.cs
Server/tests/Dfo.Server.Host.Tests/GmJoustHttpTests.cs
Server/tests/Dfo.Server.Protocol.Tests/Game/Joust/JoustProtocolTests.cs
Web/gm-admin/src/events/JoustSettings.test.tsx
Web/gm-admin/src/events/JoustSettings.tsx
```

## Round2：格式规范化与最终证据最小复核

2026-10-06，同一独立审查者按明确再派发复核当前 revision。判决 `approved`：无新的生产逻辑变更或未解决阻断项；Round1 主体及实机限制继续有效。本轮仍只修改本报告，未运行测试/构建、提交、git add 或生产编辑。

目标 HEAD/基线仍 `b4bf8d0d01113c39962c885b44002453dcb740c6`。原始67路径当前 raw manifest SHA256 为 `6320C9F1D03D456FE87D1892D55F0AA11A406C752828A600DD185864622EBF0E`，算法与 Round1 相同；原始路径清单不变。当前已 staged 以供接受，但格式规范化仍是相对 index 的 unstaged delta；本判决覆盖当前工作树文件内容，不以旧 index 字节冒充当前候选。

独立执行读取 index 原字节与工作树字节的 Python 断言：四个源码/测试均满足 `old.replace(CRLF,LF).rstrip(LF) == new.rstrip(LF)`，逐个通过：

- `Server/src/Dfo.Server.Abstractions/Events/JoustServices.cs`
- `Server/src/Dfo.Server.Application/Events/EventPlayerProjectionDomainService.cs`
- `Server/src/Dfo.Server.Application/Events/JoustMaintenanceService.cs`
- `Server/tests/Dfo.Server.Application.Tests/Events/JoustIntegrationTests.cs`

规范化对照另确认16文件仅换行/行尾空白/末尾空行，按逐行去空格和制表符、统一LF比较内容相同；未删日志结果或失败细节。控制文档 sdd-execution 从 `verification/completed` 恢复为 `review/in_progress`，契约revision、阶段要求及引用未改；随后两个新增封存制品的证据措辞同步另见下文。生产、协议、前端、spec和plan没有其它语义 delta，UI文件字节未变。工作树 `git diff --check` exit0；旧index的 `--cached --check` 仍显示规范化前CRLF/行尾告警，主控需在接受前更新index并重新检查，不作为当前工作树格式失败。

最终证据实际读取：Application318/318、Protocol165/165、Host28/28、Persistence1/1、UI138/138，和 Host构建0警告/0错误及UI构建成功；`318+165+28+1+138=650`，加法与 completion-evidence 一致，全部是格式规范化前成功日志，不能称为格式后的新测试。格式后主控计划串行重跑Host build、受影响Application/Protocol回归；UI、Host HTTP、Persistence未有语义变化，不要求无条件重复全套。审查通过不替代该后续完成验证。

已读最新 completion-evidence/spec-result：它们现将旧raw身份限定为格式前输入，将代码/测试变化表述为语义保留，并把format delta纳入最终 Changed paths；没有把静态证据升级为ClientVerified，没有提前声称已有接受commit。随后cursor、fresh日志和验证记录更新由主控负责；任何新的生产语义变更须另行复审当前revision。

新增最终证据10路径，连同原67路径为77个当前审查输入（不含本报告）；这些均为验证/封存制品，无新生产路径：

```text
Document/tasks/event-joust/spec-result.md
Document/tasks/event-joust/verification/application.log
Document/tasks/event-joust/verification/artifact-identity.log
Document/tasks/event-joust/verification/completion-evidence.md
Document/tasks/event-joust/verification/host-build.log
Document/tasks/event-joust/verification/host-http.log
Document/tasks/event-joust/verification/persistence-experience.log
Document/tasks/event-joust/verification/protocol.log
Document/tasks/event-joust/verification/ui-build.log
Document/tasks/event-joust/verification/ui.log
```
