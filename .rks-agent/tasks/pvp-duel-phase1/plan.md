---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan: 决斗场一期

## Planning Choice

工作跨契约、内容、持久化、领域、协议五层，后面每层都消费前面层的接口；房间与结算的具体形状要等事务参与者写法落地后才好细化。执行配置要求 `attention_review: on-drift`，因此用 task-units：每个单元交付一段可检查的结果，后续单元按前序产出再细化。代码依据见 `research/code-map.md`。

## Approach and Shared Decisions

- **新领域目录 `Pvp/`**：`Abstractions/Pvp/` 放契约与能力表、权益记录；`Application/Pvp/` 放领域服务、规则服务、静态规则、调度与停服 drain；`Persistence/Pvp/` 放战绩 Store；`Infrastructure/Content/` 放两个 PVF 内容源；`Protocol/Game/Pvp*/` 放切片。
- **房间 owner**：`PvpRoomDomainService`（写面 `IPvpRoomDomainService`、读面 `IPvpRoomQueryDomainService`），内存态经事务参与者 `Changes`（`CommitCanFail=false`，Prepare 乐观校验，Commit 应用并唤醒调度）提交；无事务即抛。身份 `PvpRoomId`/`PvpMatchId` 用 Guid，线上 `u16` 房间号是 owner 分配的显示编号。
- **结算**：`PvpSettlementDomainService`（Abstractions 接口 `IPvpSettlementDomainService`）读冻结结果 → `IPvpRecordDomainStore` 读战绩与今日计数 → `IPvpSettlementRuleService` 决策 → Store `SettleAsync` 返回 `Created/AlreadySettled` → 首次才 `IEventService.EnqueueAsync(PvpMatchSettled)` → 房间阶段推进。CMD 只调用该服务。
- **规则**：`PvpRoomRuleService`（地图、分队、平衡、座位转移）与 `PvpSettlementRuleService`（经验、段位、上限、权益表、游戏日）各自注入内容源；静态 `PvpRoomRules`、`PvpGradeRules`；能力表 `PvpBattleModeCapabilities.Of(mode)` 只开 1、2。
- **调度**：独立 `PvpDeadlineScheduler : TypedGameplayTimerSource, IPvpDeadlineWake`（不扩展 `DungeonDeadlineKind`），来源扫描房间截止时间，提交内部 CMD 推进。
- **离场**：`SelectedSessionCleanupService` 新增一步 `rooms.Depart(session, reason)`，结果交给 `IPvpRoomDepartureSink`（Protocol 的离场投影队列实现），由扫描内部 CMD 在提交后认领投影；`AffectedSessions` 加入同房成员。
- **停服**：Application 注册 `PvpShutdownDrain : IGameplayProducerDrain`（排在传输 drain 之前），提交内部 CMD 结算已终局对局、取消其余对局。
- **位置**：`TownAreaDepartureReason` 加 `PvpRoom`；进房 `LeaveTown`，退房 `ReturnToTownAsync`。
- **邀请**：决斗邀请状态由房间 owner 保存；`GameRequestPeerCommandHandler` / `GameResponsePeerCommandHandler` 内按 type 分派。

## Work and Dependencies

| 包 | 交付结果 | 覆盖 | 依赖 | 拆分理由 |
|---|---|---|---|---|
| T-001 契约、内容与规则 | `Abstractions/Pvp` 记录与接口、能力表、权益记录；两个 PVF 内容源；两个规则服务与静态规则；对真实 A21 PVF 的规则测试（经验决策表、地图、时限、未登记环境启动失败）；模式 switch 源码扫描测试 | RQ-013（地图部分）、RQ-050–053、RQ-073 | — | 纯函数层，后续所有层都依赖它的接口与值 |
| T-002 持久化与迁移 | schema v11：战绩、对局、对局结果、迁移审计表；`character_pvp` 拆分（段位进战绩、`skills_initialized` 留技能侧）；全部消费方迁移；`IPvpRecordDomainStore`（含幂等 `SettleAsync`、今日计数、连胜）；DbMigrate 步骤与测试；迁移文档 | RQ-054（存储侧）、RQ-055（同源部分）、RQ-060 | T-001 | 独立可验证（迁移测试、往返测试），且旧列消费方迁移要一次到位 |
| T-003 大厅房间部分纯搬迁 | 把 `PvpLobbyDomainService` 的房间部分（`PvpRoom`、`Offer`/`Withdraw`/`OfferedOn`）搬到新 `PvpRoomDomainService` 的读写面，调用方改读新 owner；行为不变（单独提交） | RQ-002 | — | 项目要求纯搬迁与语义改动分开提交 |
| T-004 房间领域核心 | 房间 owner 改为事务参与者；建/进/退/踢/座位/模式/地图/准备/开局；邀请（创建、接受、过期）；与队伍互斥；离城/回城；状态普查与写入口表登记；领域测试 | RQ-010–015、RQ-020–021、RQ-071（房间部分）、RQ-074 | T-001、T-003 | 后续对局与协议都以它的结果类型为准 |
| T-005 对局、结算、离场、停服 | 阶段推进；死亡/超时判定与终局冻结；结算领域服务与事件；调度器与各截止来源；离场（Depart + sink 接缝 + `AffectedSessions`）；停服 drain；领域测试（事务回滚、幂等、断线各阶段、停服） | RQ-030–035（领域侧）、RQ-040–042、RQ-054、RQ-071 | T-002、T-004 | 规则最密集，单独验证后再接协议 |
| T-006 协议切片 | 消息目录成员；`0x00C3`、房间、对局切片（codec、decoder、encoder、薄 handler、CMD）；REQUEST/RESPONSE_PEER 按 type 分派；离场投影队列与扫描；大厅增量投影；选角 `0x0030` 与快照来源；长度拒收 | RQ-001、RQ-003、RQ-022、RQ-035（投影侧）、RQ-041、RQ-055、RQ-072、RQ-075 | T-004、T-005 | 协议形状依赖领域结果类型；可再按"房间/对局/选角与邀请"细分 |
| T-007 集成测试与文档 | TCP 集成：三客户端大厅一致性、完整一局、断线判负、确认超时；协议页人工区与 catalog；`Document/` 决斗场专项说明；全量测试与基线对比 | RQ-080，AC-003/034 集成证据 | T-006 | 需要完整链路 |
| T-008 用户测试准备 | 测试服（本分支、主干库一致备份副本迁移后、端口 41000–48999）；`Document/tasks/pvp-duel/` 就绪说明与编号实测步骤；用户反馈后修复复测并回写抓包结论 | RQ-081 | T-007 | 依赖用户，独立节奏 |

## Verification

每个单元带自己的测试与构建检查；T-007 在完整链路上补集成证据并跑全量测试，与 `verification/baseline-failed.txt` 对比无新增失败；评审阶段对照 TaskContract 与 Spec 做完整结果审查；验证阶段在 `verification/completion-evidence.md` 逐条对照 SC 与 AC。需要实机的项在 T-008 用户反馈前标为待验。

## Next Planning Boundary

下一个单元：T-003（纯搬迁，无依赖，最小）与 T-001（契约与规则）。先做 T-003 并单独提交，再做 T-001。T-004 的细化等 T-001 的接口与 T-003 的新 owner 落地后再写。

## Amendments

- A1（TASK-02 审查移交）：启动期校验与 FreeDuel 权益归属指派给 T-004。
  - 权益按频道身份取：`ChannelEntryKind.FreeDuel`（含按频道号配置的自由决斗频道，环境可能为空）一律取"不记录"行；`ChannelEntryKind.Pvp` 按登录环境取（8 不记录、24 记录），表中没有的环境为配置错误。`EntitlementsOf` 的输入随之改为频道身份（`ChannelEntryIdentity`）。
  - 显式启动检查：在开始接受连接之前，遍历频道目录中被判为决斗频道的每个频道调用 `EntitlementsOf`，并强制加载两份 PvP 内容源；任一失败即终止启动（不依赖只记 Warning 的内容预热）。挂接点沿用宿主现有的启动前检查/就绪机制，T-004 细化时确定。
