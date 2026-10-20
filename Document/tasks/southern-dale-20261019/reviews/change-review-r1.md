---
kind: rks-review
verdict: changes_required
---

# 南部溪谷完整结果独立审核

## 目标与标准

- 工作区：`E:/Project/DNF/DNF-JP-86-Server`，分支 `task/southern-dale-20261019`。
- Spec baseline：`35d890662e987c85ff7e05786ae72c068f81c187`。
- 审核实现 HEAD：`7d44107ee173b2f9235d1db37010c1604272aab7`，审核时实际 HEAD 与此一致。
- 范围：上述 baseline..HEAD 的全部产品、测试和文档变更；结合现有准入、事务、击杀、补货、任务及通知接线检查交互与回归。
- 治理输入：本任务 `TaskContract.md` revision 1、`spec.md`、`plan.md`、`routing-decision.md`，以及工作区和仓库 `AGENTS.md`。
- 判据：`reviewing-work/SKILL.md`、`references/code.md`、`references/deliverable.md`。本审核为独立完整代码及混合交付审核，不替代完成验证或用户实机验收。未配置独立挑战集门禁。

## 阻断发现

### F-1 / P2：任务资格在活动启停与游戏日切换后没有主动更新到在线客户端

位置：`Server/src/Dfo.Server.Protocol/Game/WeeklyAttendance/WeeklyAttendanceSlice.cs:119-122`，以及 `Server/src/Dfo.Server.Application/World/SouthernDaleDailyRefill.cs:33`。新增过滤的来源为 `Server/src/Dfo.Server.Application/Quests/QuestAvailabilityRuleService.cs:74`。

本轮新增的 `SouthernDaleQuestAvailable` 同时依赖活动开放状态及本游戏日是否已完成20549。它会把不合格的20549从 `AvailableQuestListMessage`（NOTI `0x0015`）中移除。选角、正常任务完成以及显式Replay会正确重建该列表，但改变资格的另外两条生产路径没有重新投影该列表：

1. GM启停提交后，`EventsConfigurationNotificationSubscriber`（`ActivityNotificationSubscribers.cs:24-29`）发布 `EventsChanged`；其handler只发送006C、033F与其他活动状态，没有发送当前0015。角色在南部关闭时选角，随后GM开启，服务端已允许接取，客户端此前收到的可接列表仍没有20549。反向关闭同样留下过时的列表。
2. 当天完成20549后，`FinishQuestHandler.cs:276-277`发送的新0015已移除该任务。持续在线跨06:00时，`SouthernDaleDailyRefillJob`只执行补票命令；`RefillOnlineTicketAsync`最多产生背包变化通知，没有任务offers刷新。新游戏日 `CanAcceptAsync`已变为true，但客户端可接列表仍停留在昨日状态，直到重登或发生另一项碰巧重发0015的操作。

033F是调度状态的独立投影；当前实现没有证据证明它能够补入服务端已从0015省略的任务，也没有在06:00主动更新任务offers的接线。该缺口是服务端当前投影没有随其新增资格输入更新，不能以“原生UI待实机”代替修正。它影响合同SC-5的相关任务闭环及RQ-007/008的正常在线行为。

最小修正：在活动启停通知和06:00日切成功后，为仍为当前发布会话的角色重新读取任务资格并发送现有 `AvailableQuestListMessage`，复用现有任务查询/通知边界，不增加猜测协议；避免把读取到的新资格仅留在CurrentState或Replay中。回归应捕获实际通知输出：关闭时选角后开启、开启后关闭、当天完成后在线跨06:00，分别检查0015中的20549增加/移除，并核对033F仍独立正确。现有 `Southern_offer_stays_consistent_in_current_view_and_replay_after_activity_changes` 只验证显式查询与Replay，不覆盖这些自动发送路径。

## 已检查的其他关键边界

- 共享69由两个活动分别声明，聚合按EventId唯一化，无详情日期不参与线上内容冲突；活动主身份仍独立配置。组合启停及有效期测试符合实现。
- 南部入口拒绝不允许的模式、过低等级和重复run，复用全队Admission扣票及Visit事务发布；扣票失败/后续入场失败要求回滚。
- 两类活动票共用既有持久领取流程，南部采用06:00游戏日、补足目标2和待领邮件计数，旧零点领取记录保守处理。
- 固定钥匙奖励接在权威Boss房清空与actor提交的同一用例事务；run/角色唯一领取记录、队伍成员校验与失败回滚相互一致。现有普通房间构建保留Monster/APC独立掉落来源，不把APC强制转换为普通怪物经验。
- 20549完成次数限制进入同一任务结算事务；金币罐的材料消费、钱包上限预检/实际钱包写入及虚拟槽投影一致；魔罐沿用既有配方路径。
- CurrentState、选角、QuestFacts和Replay已补齐资格读取；这与F-1的“变化后自动发送缺口”是不同边界。
- 文档明确区分ClientStatic/High和CurrentImplementation，未将类别1任务关联、原生UI、特殊房间或四人联机升级为ClientVerified。

除F-1外，本轮未发现可证实的新增阻断缺陷。

## 检查证据与限制

直接读取了 `artifacts/southern-dale-20261019/no-pvf-final/` 与 `real-pvf-passed/` 的TRX结果，而非仅引用执行者汇报：

| 证据 | 实际结果 | 适用边界 |
|---|---|---|
| no-pvf-final，8份TRX | 总计7520，通过6653，跳过867，失败0 | 发生在最后CurrentState/Replay资格补齐之前；不能称为当前HEAD的fresh全量验证 |
| real-pvf-passed Application | 80/80通过 | 含南部准入、权威击杀、补票、任务/金币罐与CurrentView/Replay，并含此前有并行超时的Enchant重点回归 |
| real-pvf-passed Infrastructure | 44通过、1跳过、0失败 | 内容及目录相关重点回归 |
| real-pvf-passed Host | 11/11通过 | 活动与通知相关重点回归 |
| real-pvf-passed Protocol | 21/21通过 | 包含新增类别0/1的033F编码回归 |
| build-final.log | Release构建成功，0警告、0错误 | 仅作为已有构建证据，fresh完成验证由主执行者负责 |

未独立运行构建/测试，避免与主执行者同时写同一bin目录；未启动服务、未修改运行PVF/数据库、未进行Git变更。本轮唯一写入为本审核文件。

033F类别1任务映射仍为ClientStatic/High；原生入口、任务显示、特殊房间和四人联机保留用户实机验收，属于已声明的非阻断剩余边界。修复F-1后需对新HEAD独立复审，并执行fresh验证；本结论不批准当前实现门禁通过。
