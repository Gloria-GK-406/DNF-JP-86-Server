```yaml
kind: rks-review
verdict: approved
```

# 完整结果审查：决斗场一期（代码 + 协议文档 + 专项文档）

## 目标与范围

- 工作区 `E:\Project\DNF\DNF-JP-86-Server-pvp`，分支 `feature/pvp-duel-phase1`，HEAD 已核对为 `b56b9359008077c434b4a4c16dd7b5069c321e0d`，工作树无未提交改动。
- 审查范围 `git diff 3da40eab1..b56b93590`（契约提交之后的 13 个提交，202 个文件，+14096/−389）。
- 治理依据：TaskContract revision 2、spec.md、plan.md（含 A1、A2）、TASK-01..08（Result 与 Result Review 只作证据指针，结论以实际代码为准）、`Document/tasks/pvp-duel/design-r1.md`。
- 仓库规则：`AGENTS.md`、`Document/ArchitectureReview.md`（裁定表与阻断形状）、`Architecture.md`、`RefactorPreferences.md`、`GameplayExecution.md`、`EventsAndNotifications.md`、`ProtocolCommands.md`、`DatabaseSchemaMigration.md`、`DocumentationGuidelines.md`、`protocol/README.md`。
- 审查准则：reviewing-work 的 Code 与 Deliverable 两份准则（混合交付，另查两者一致性）。
- SC-12、SC-13（测试服与用户实测）属后续单元（plan A2：TASK-09），不作本次阻断；只检查其所需实测项是否已进入待实机清单。

## 本审查亲自执行的检查

均为 `-m:1`，测试逐项目、`MaxParallelThreads=1`，`PVF_ARCHIVE_PATH=E:\Project\DNF\PvfBuilds\revised-20261113\Script.pvf`（登记的修订版）：

| 检查 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx -m:1` | 0 警告 0 错误 |
| Application.Tests `~Pvp` | 152/152（含对真实 PVF 的规则测试，0 跳过） |
| Infrastructure.Tests `~Pvp` | 15/15 |
| Persistence.Tests `~Pvp` | 10/10 |
| Protocol.Tests `~Pvp`/`~CommandRegistration`/`~SelectCharacterPvpRecord` | 188/188 |
| DbMigrate.Tests `~Pvp` | 2/2 |
| Host.Tests `~Pvp` | 3/3（含真实停服顺序） |
| Networking.Tests `~Pvp` | 5/5（含 4 个真实 TCP 用例） |
| Application.Tests 架构类（`~Architecture`/`~Census`/`~Boundary`/`~GameplayWriteEntryTable`/`~TimerSourceReconciliation`） | 330/331，唯一失败 `DarkElfRuinsTests.Monthly_quota…` 在 `baseline-failed.txt` 内 |
| Host.Tests `~Architecture` | 19/19 |
| `protocol/patterns/data/check-server-documentation-sync.py` | 退出 1、534 项失败；失败主要是"cannot resolve inbound type"/"no resolvable wire type"，覆盖全仓旧协议，属检查器本身与当前代码不兼容的既有问题，不能作为本分支的证据，也不归咎本分支 |

全量对比未重跑（按要求，沿用 `verification/full-test-summary.md`）。

## 覆盖矩阵摘要

| Spec | 实现落点 | 自动证据 | 状态 |
|---|---|---|---|
| RQ-001 / AC-001 | `PvpChannelInfoSlice` | `PvpChannelInfoTests` | 满足（客户端表现待实机） |
| RQ-002 / AC-002 | 选角 `PlanPvpAsync`、`SessionAdjacentEntryDomainService` 均读 `RoomsOn`；`PvpLobbyDomainService` 已无房间 | `PvpLobbyEntryTests`、写入口表完整性测试（方法级） | 满足；"源码不含房间状态"的结构断言缺失（见 F-5） |
| RQ-003 / AC-003 | `PvpRoomLobbyFrames.For` 唯一增量映射 | `PvpDuelWireTests` 用例 1（三客户端每步对比新会话快照，覆盖建/进/换队/改图/开局/结算/回等待/退房/销毁）+ `Lobby_increments_add_up…` | 满足 |
| RQ-010..014 / AC-010..012 | `PvpRoomDomainService` + `PvpRoomRules` | `PvpRoomDomainServiceTests` 各项、`PvpRoomCommandTests` 帧序 | 满足 |
| RQ-015 / AC-013 | 领域 `InParty`；组队 CMD `IsSeated` | 领域测试 + 真实 TCP 用例 4 | 满足（组队侧检查在写单元外，单执行器串行下不可达，已写入 `PvpDuel.md`） |
| RQ-020..022 / AC-020 | `InviteAsync`/`AcceptInviteAsync`；REQUEST/RESPONSE_PEER 按 type 分派 | 邀请领域测试（过期、房满、已开局、会话替换、在队、黑名单、token）、`PvpRoomInviteTests`、`Peer_and_duel_room_messages_each_bind_one…` | 满足 |
| RQ-030 / AC-030 | `Battle()` 阶段闸门 | `A_report_that_does_not_belong_to_the_phase…`、`Loading_or_linking_past_its_deadline…` | 满足 |
| RQ-031 / AC-031 | `ReportDeathAsync`；CMD 校验死者=发送者 | 领域与 CMD 测试 | 满足 |
| RQ-032 / AC-032 | `ReportTimeoutAsync`（时限−5s、首份、判平） | `Only_the_first_time_out…`、32/31/33 字节 | 满足 |
| RQ-033/040 / AC-033 | `DepartAsync`、冻结结论 `PvpMatchConclusion` | `The_winner_leaving_after_the_end…`（三时点 theory）、`Leaving_a_fight…`、`Leaving_before_the_fight…` | 满足 |
| RQ-034 / AC-034 | `Stage`/`CompleteSettlement` | 全员确认与超时两路径、帧序 `0x002F→0x0030` | 满足（"回等待后立即再开一局"无集成例，领域层有） |
| RQ-035 / AC-035 | `PvpResultScreenDeferrals` + `PvpRoomProjector` | `PvpBattleProjectionTests`/`PvpBattleCommandTests`（释放、超时不释放、下一局释放、迟到 0x0039、房间销毁丢弃） | 满足（客户端表现待实机） |
| RQ-041 / AC-041 | `DepartRoomAsync` 外层事务 `RegisterAfterCommit`；自开事务先提交后交 sink | `A_cleanup_departure_reaches_its_sinks_only_when…`、`The_departure_queue_holds…only once its transaction commits` | 满足 |
| RQ-042 / AC-042 | `PvpShutdownService.StoppingAsync`（非 producer drain） | `Shutdown_cancels…`、`PvpShutdownServiceTests`、`PvpShutdownHostTests` | 满足 |
| RQ-050..053 / AC-050..051 | `PvpSettlementRuleService`、`PvpGradeRules` | 真实 PVF 规则测试覆盖决策表每行、50/51 胜、10/11 负、跨日、满级、下限 0；跨多段与下限用合成表（真实表一局不可能跨段，研究记录已说明） | 满足 |
| RQ-054 / AC-054 | Store `ON CONFLICT(match_id) DO NOTHING` + 首次才发事件；房间参与者 | 幂等、`AlreadySettled`、注入写库失败反例 | 满足 |
| RQ-055 / AC-055 | 选角 `WithPvpRecord`（HOTKEY 之后）；USERINFO 段位读 `character_pvp_record` | `SelectCharacterPvpRecordTests`、51 字节基线 | 满足 |
| RQ-060 / AC-060 | `PvpRecordSplitUpgrade`、Steps[10] | 迁移测试（各列去向、审计表、结构与新建库一致、Store 可读） | 满足；"经验初值为段位下限"以 NULL 表达，已在 design-r1 修正案与 DatabaseSchemaMigration 中写明 |
| RQ-070..075 / AC-070, AC-075 | 见下节 | 架构测试、模式 switch 扫描含反例、长度拒收测试 | 满足 |
| RQ-080 | 协议页人工区、catalog 两节、`Document/PvpDuel.md` | 文档核对 | 基本满足（见 F-4） |
| RQ-081 / AC-081 | TASK-09 | — | 后续（SC-12/13） |

"需实机、属 SC-12/13"的项：`0x0037` 实际长度、`0x0039` 后解除等待的帧、欠帧合帧与无 USER_LEAVE、团队 `0x002F` 胜方座位、选角 `0x0030` 位置、建房成功无 CMD、拒绝 CMD 表现、USERINFO1 routing uid、`0x0007` type 2、换模式/换队不清准备、截止类路径的真实 TCP 表现（测试主机时钟冻结）、回选角/选角替换的真实 TCP 离场、环境 8 不改战绩的集成例。

"本该自动测试却缺失"：只有 F-5（AC-002 结构断言），影响很小。

## 架构不变量（RQ-070..075，按 ArchitectureReview.md）

结论：**通过：附建议**。

- CMD 注入面：房间/对局/邀请 CMD 只依赖领域服务（`IPvpRoomDomainService`、`IPvpSettlementDomainService`、只读 `IPvpRecordDomainService`）、工作单元与投影；不注入规则服务、内容源或 `IEventService`；`0x0030` 所需段位上下界经只读领域查询取得。结构测试通过。
- 结算在领域层：`PvpSettlementDomainService` 读冻结结论 → 规则决策 → Store 幂等写 → 首次才 `EnqueueAsync(PvpMatchSettled)` → `CompleteSettlement` 推进房间，全部在调用方写单元内。
- 房间唯一 owner 与事务参与者：`PvpRoomDomainService` 内存态经 `Changes : ITransactionParticipant`（`CommitCanFail=false`，Prepare 乐观校验房间/号码/座位/会话/邀请/结论）提交；无事务或只读事务即抛；进/退房的城镇移动在 Commit 内执行；唤醒只在提交后、截止提前时发生。
- 投影不在未提交写事务内：各 CMD 在 `units.WriteAsync` 返回后投影；清理链外层事务用 `RegisterAfterCommit`，自开事务先提交再交 sink；sink 只入队、由调度轮认领投影。
- 领域无协议号与字节布局：Application/Abstractions 的 Pvp 代码只有注释提到 `0x0030`；线上 seat state、拒绝码映射都在 Protocol 的 codec。
- 能力表/权益表：`PvpBattleModeCapabilities.Of` 与按模式的 switch 列全成员、无兜底（`CS8524` 为仓库级 NoWarn，扫描测试带反例）；权益按频道身份取，13 归 FreeDuel，表外环境抛出并由启动校验（A1）拦截。
- 状态普查：`PvpRoomDomainService`、`PvpDepartureProjectionQueue`、`PvpResultScreenDeferrals` 登记写入口表，`PvpDeadlineScheduler` 登记普查，测试通过。
- 单一长度拒收：`0x0036` 1B、`0x0037` 5B、`0x0038` 32B，其他长度在 CMD 前拒收、记日志、不进领域。
- 周边先例：离场投影队列与 `BossTowerDepartureProjectionQueue` 同形；调度器仿 `DungeonDeadlineScheduler`；`GameServerStartupService` 内的启动检查已有进度表约束先例。未发现第二真源、旁路规则服务或专项逻辑进入共享机制。

## 17 条已定规则落点

| # | 落点与值 | 一致 |
|---|---|---|
| 1 | `PvpBattleModeCapabilities.Of`：只有 1、2 `Supported`；建房固定模式 2（0x0032 无模式字段，special=1 练习房拒收） | 是 |
| 2 | `EntitlementsOf`：Pvp 环境 8 不记、24 记；13 判为 FreeDuel 不记 | 是 |
| 3 | `PvpGradeRules.Change`：0 段首胜到 1 段下限、+W、`GradeOf` 可跨段、平 0、逃离按负、取消不结算 | 是 |
| 4 | Ranked 行 `(50, 10)`；`GameDayOf(StartedAt)` 用每日重置规则；`DailyCountsAsync` 按开局游戏日 | 是 |
| 5a/5b | 满级胜 0；负照扣 `LossExperience` | 是 |
| 6 | Fighting 离场 `Flee`；Loading/PeerLinking 离场 `Waiting`（取消）；AwaitRank/AwaitResult 只标 `Departed` | 是 |
| 7 | 心跳 30s 只在 Fighting 计（`DueOf`/`AdvanceFight`）；加载 60s、连通 30s | 是 |
| 8 | 超时上报与 `PhaseDeadline = PVF 时限 + 30s` 兜底一律 Draw | 是 |
| 9 | Rank/Result 各 10s；超时只推进阶段，欠帧不释放 | 是 |
| 10 | 只写胜负平、经验、段位、连胜；无道具/胜点 | 是 |
| 11 | 领域 `InParty`；组队 CMD `IsSeated` 双向 | 是 |
| 12 | 无装备限制 | 是 |
| 13 | 密码 ≤8 字节、建/进房支持；**实机抓包门槛未进入待实机清单**（F-1） | 实现是；清单缺 |
| 14 | 座位态 3/4（观战）拒收 | 是 |
| 15 | 停服先取消未终局（置 closing）再结算已终局；崩溃丢弃 | 是 |
| 16 | 无每日次数限制 | 是 |

## 问题清单

均非阻断。

**F-1（中，治理/后续单元）待实机清单缺两项 SC-12 必测项**
- 位置：`tasks/TASK-06.md` 第 82 行、`TASK-07.md` 第 91 行、`TASK-08.md` 第 53–55 行的"仍需实机"清单。
- 事实：TaskContract SC-12 与已定规则 13 要求实测步骤覆盖"含密码进房（须抓包才算完成）"和"准备成功是否回 CMD"。三份清单都列了"建房成功无 CMD"，但没有"准备成功（`0x0035`）是否回 CMD"，也没有"带密码进房 `0x0033` 抓包"。`0x0033` 页写了"密码分支来自上游（Hypothesis）"，但没有作为待抓包项列出。另外"重登、服务端重启后战绩不变"（SC-7）也只在契约里，各单元清单没有。
- 后果：TASK-09 若按单元清单写实测步骤，规则 13 的完成门槛和 SC-12 两项会漏测。
- 最小修正：TASK-09 写 `Document/tasks/pvp-duel/` 实测步骤时，直接以 TaskContract SC-12 列表为准，补上带密码进房抓包、准备成功回包抓包、重登与重启后战绩核对。

**F-2（中，数据风险，需在迁移玩家库前确认）旧段位超过 PVF 满级会锁死角色**
- 位置：`PvpRecordDomainService.ViewAsync` → `PvpSettlementRuleService.LowerBound` → `PvpGradeRules.Step`（`grade > MaximumGrade` 抛 `ArgumentOutOfRangeException`）；选角 `GameSelectCharacterCommand.ReadPvpRecordAsync` 对每个频道都读取。
- 事实：v10 以前选角只按 `checked((byte)pvp_grade)` 读段位，0–255 都能进；现在 A21 PVF 满级为 20，段位 >20 的角色在任何频道选角都会失败。迁移按要求原样保留段位，不做截断。
- 后果：若玩家库有 `pvp_grade > 20` 的行，迁移后这些角色无法进入游戏。目前没有证据表明这种数据存在（默认值 0）。
- 最小修正：TASK-09 迁移主干库副本前执行 `SELECT COUNT(*) FROM character_pvp WHERE pvp_grade > 20 OR pvp_grade < 0`，结果记入就绪说明；不为 0 时回到用户决定（截断或在 `ViewAsync` 容错），不要自行改数据。

**F-3（低，遗留/重复）`PvpLobbyEntryState.Rooms` 已无生产读取方**
- 位置：`SessionAdjacentEntryDomainService.ResolveLobby` 仍计算 `RoomsOn(...).Select(PvpRoomLobbyView.Of)`；选角 `SelectCharacterSessionEntryService.PlanPvpAsync` 改为重新读 `RoomsOn`，注释说明"不取相邻状态"。生产代码不再读 `PvpLobbyEntryState.Rooms`，只有测试断言它。
- 后果：同一份大厅房间视图被算两次、只用一次。它不是第二真源（同源派生），但违背"一次迁移到位、不留兼容层"，后来者可能误以为相邻状态里的房间是选角快照的来源。
- 最小修正：从 `PvpLobbyEntryState` 删除 `Rooms`（连同对应测试断言），或改为由选角消费它并删掉重读；二选一。

**F-4（低，死分支）`PvpDepartureReason.HeartbeatLost` 没有生产调用方**
- 位置：`PvpRoomDomainService.DepartAsync` 的 `keepsSeat` 分支；`IPvpRoomDomainService.DepartAsync` 的注释"a fighter that only stopped sending heartbeats keeps it"。
- 事实：生产中的心跳超时只走 `AdvanceDueAsync → AdvanceFight` 的 `Flee`。`HeartbeatLost` 只被两个领域测试用来"让某人逃离"的捷径调用（`PvpBattleDomainTests` 第 221、533 行）。
- 后果：同一事实（心跳丢失＝逃离、保留座位）有两条写路径，真实路径和测试捷径可能在后续修改时分叉。
- 最小修正：删除 `HeartbeatLost` 与 `keepsSeat` 分支，测试改用推进时钟加 `AdvanceDueAsync`；或让 `AdvanceFight` 调用同一私有方法并在注释里写明唯一调用方。

**F-5（低，测试缺口）AC-002 的"`PvpLobbyDomainService` 不含房间状态"没有结构断言或源码扫描**
- 现状：只有写入口表完整性测试间接约束其公开方法，字段没有约束。
- 最小修正：加一个反射断言，检查该类型没有引用 `PvpRoom*` 类型的字段；或在 completion-evidence 中说明以写入口表测试替代。

**F-6（低，文档）catalog 机器生成的 server-coverage 区未更新**
- 位置：`protocol/catalog.md` 中 `rks:server-coverage` 区（"已注册 decoder""encoder-backed"表）。
- 事实：新增的 `0x0032`–`0x003B`、`0x00C3`、`0x012A`/`0x012B`/`0x006E` decoder 与 `0x0029`–`0x0031` NOTI encoder 不在该区；人工两节已完整记录。生成器 `check-server-documentation-sync.py` 当前对全仓失败 534 项（既有问题），无法据此重新生成。
- 最小修正：在两节人工说明里注明"生成区待检查器修复后重生成"，或另开 issue 跟踪检查器；不阻断。

**F-7（极低，风格）14 个文件新增 UTF-8 BOM**
- 位置：`ApplicationModule.cs`、`ISessionPresence.cs`、`PvpLobbyDomainService.cs`、`SelectCharacterLifecycleServices.cs`、`SessionAdjacentEntryDomainService.cs`、若干测试文件，以及新文件 `PvpRooms.cs`、`PvpRoomDomainService.cs`、`PvpRegistration.cs`。`.editorconfig` 规定 `charset = utf-8`（无 BOM）。`ApplicationModule.cs` 另有一行 `services.AddSessions();` 的空白/行尾噪音。
- 最小修正：建 PR 前去掉 BOM，避免无关 diff。

## 文档一致性

- `Document/PvpDuel.md` 与代码逐节核对一致：owner 与事务、阶段与截止表、`0x0038` 提前 5s、退避 1/4 夹 1–30s 与 5min 放弃、权益与经验规则、离场四类与两种事务路径、欠帧入账/释放/丢弃、停服挂点与坑、启动校验、投影要点、证据边界。
- `Architecture.md`、`AGENTS.md` 阅读入口、`DatabaseSchemaMigration.md` 10→11、`TableOwnershipRuling.md` 修正案、design-r1 修正案都与实现一致。修正案只更正记载、不改要求，符合跨界修正记载约定。
- 协议页人工区证据标签如实：都标 CurrentImplementation、待实机，上游来源标 Hypothesis，没有新增 ClientVerified/WireCaptured。`0x000A`/`0x000B` 主表只把 type 0 标为 ClientVerified，type 2 注明待实机。

## 数据迁移安全（v10→v11）

- 迁移在单个事务内建表、复制、写审计、删 `character_pvp`；`CREATE IF NOT EXISTS` 兼容 legacy 路径；结构与新建库逐表一致（测试断言 `ShapeOf(fresh)==ShapeOf(path)`），二次运行无变化。
- 段位、评级、`skills_initialized` 原样保留；四个语义不明列只在非 0 时进审计表；没有 `character_pvp` 行的角色不补造（与迁移前同样进不了）。
- 新库不能被 v10 服务端打开，文档已写明，需要切换前一致备份（SC-12 范围）。
- 风险见 F-2（段位超过满级）。

## 非阻断残余风险

1. 全量对比（`verification/full-test-summary.md`）所用 PVF 是 21:00 被替换的未登记版本（`A97132DD…`），与基线所用 PVF 不同。基线外的那一项已用登记 PVF 单独复跑通过。本审查用登记 PVF 复跑了全部 Pvp 子集和架构测试，均通过；但非 Pvp 的基线失败项与新增失败的对照仍建立在两份不同 PVF 上。
2. 选角录制转写测试（`SelectCharacterSuccessCharacterizationTests`）在基线里本就失败，新增的 `0x0030` 帧因此不受该转写守护；帧序由 `SelectCharacterPvpRecordTests` 单独固定。
3. 截止类路径（取消、判平、心跳、确认超时、结算重试/放弃）只有 Application/Protocol 层证据；真实 TCP 主机时钟冻结，没有覆盖。
4. 进/退房城镇移动在参与者 Commit 内执行，失败只记 Error；回城落点若为需读存储的家园城镇，会在 Commit 期读存储（TASK-04 移交项，决斗频道基本不会出现）。
5. `0x0037` 三方长度冲突、`0x0039` 解除帧等协议假设待 SC-13 抓包定案；不符时需按契约回写协议页并可能调整长度。
6. 组队侧互斥检查（`IsSeated`）在组队写单元之外，依赖单执行器串行；并行化前须收进同一写单元（已写入 `PvpDuel.md`）。
7. `EV-003 verification/completion-evidence.md` 尚未生成，属后续验证阶段。

## 复审：补救提交 90c6fdc84（F-2..F-7）

```yaml
kind: rks-review
verdict: approved
```

- 目标：HEAD 已核对为 `90c6fdc840ba4f3039e1a1959815073bdc0e0c57`，范围 `b56b93590..90c6fdc84`（25 个文件，+229/−134）。只复审这次补救及其直接回归；F-1 属 TASK-09 范围，F-6 是检查器的既有问题，这两项不在本轮。
- 检查（`-m:1`，单项目单线程，登记修订版 PVF）：
  - 构建：0 错误。
  - Application `~Pvp|~Sessions|~GameplayWriteEntryTable|~GameplayWriteAssertion|~Census|~Architecture`：523/523。
  - Protocol 同一过滤：172/172。
  - Networking `~Pvp`：6/6（含新增线上用例）。
  - Host `~Pvp|~Architecture|~Registry`：22/22。
  - Persistence 同一过滤：11/11。

| 项 | 处理 | 判定 |
|---|---|---|
| F-2 段位高于满级 | `PvpRecordDomainService.ViewAsync` 按 `min(grade, MaximumGrade)` 取上下界、记 Warning、不写库，显示段位保持库值。`PvpGradeRules.Decide` 在空经验时按 `Within` 取起点，之后按经验重算段位，结算写回合法段位。`IPvpSettlementRuleService.MaximumGrade` 由 PVF 提供。`LowerBound`/`UpperBound` 的生产调用方只剩已截断的这两处和启动校验，没有其他会抛异常的路径。测试：视图带反例、结算、规则单测，以及 Networking 线上用例（库内段位 25 照常进入，0x0030 用满级段的上下界，库值不变，连接不断）。迁移与库数据都没有改动。 | 已解决 |
| F-3 `PvpLobbyEntryState.Rooms` | 记录字段、相邻入场的计算与对房间 owner 的依赖都已删除；记录注释写明房间唯一来源是 owner、由选角快照读取。只依附在该字段上的测试已删；房间快照内容仍由 Protocol 的 `PvpRoomLobbyView`/大厅增量测试与 `PvpDuelWireTests` 覆盖。`RegistryExistenceTests` 去掉了为解析房间 owner 而引入的模块，回到原样。 | 已解决 |
| F-4 `HeartbeatLost` 死分支 | 枚举成员与 `keepsSeat` 分支已删，`DepartAsync` 一律让座，未定义的原因值直接抛出；接口注释改为"心跳丢失归截止处理，不是离场"。两个测试改为 `FallSilentAsync`：推进时钟、其余人继续心跳，再走 `Deadlines`+`AdvanceDueAsync` 的生产路径，并断言 `Fled`。仓内已无 `HeartbeatLost` 引用。 | 已解决 |
| F-5 AC-002 结构断言 | `The_lobby_keeps_no_room_state` 用反射检查字段类型（含泛型参数与元素类型）以及接口成员名和签名里有没有 `PvpRoom*`，并用一个持有房间表的反例类证明该检查会失败。 | 已解决 |
| F-7 BOM 与换行噪音 | `3da40eab1..HEAD` 范围内已没有任何新增 BOM；`ApplicationModule.cs` 只剩两行实质新增；`--ignore-cr-at-eol` 与普通 diff 的统计一致，没有 CR 噪音。 | 已解决 |

- 直接回归：未发现。说明两点：
  - 显示段位与库值一致而上下界取满级段，这是有意的取舍，已写入 `Document/PvpDuel.md`。
  - 结算按经验重算后会把超范围段位改写为合法值，这是结算的正常写入，不是迁移改库。
- 仍然有效：首轮的 F-1（TASK-09 实测步骤以 SC-12 为准）、F-6，以及首轮列出的残余风险。迁移前仍建议用 F-2 的查询统计超范围段位的行数，并记入就绪说明；这些行现在不会再锁死角色，只用于知情。

## 复审：实测修复 06db29b5d（SA-1、SA-2）

```yaml
kind: rks-review
verdict: approved
```

- 目标：HEAD `06db29b5df9f12b8b0ff0db435e2a202de7d6d16` 相对父提交 `5065692fe` 的 diff（29 个文件，+253/−228）。依据：spec.md Amendments SA-1、SA-2，`Document/PvpDuel.md`，`Document/Architecture.md`。审查标准：reviewing-work 的 Code 与 Deliverable 两部分。审查者是独立审查者，未参与实现，只读。
- 本审查实际跑过的检查（`-m:1`，一次只测一个项目，单线程，PVF 用 revised-20261114）：
  - 构建：0 警告，0 错误。
  - Application `~Pvp|~Execution`：547/547。
  - Protocol `~PvpBattle|~PvpRoom|~SupplementalSynchronization`：166/166。
  - Networking `~PvpDuelWireTests`：6/6，无跳过，含新增的 SA-1 线上回归。
  - 测试服进程与 `artifacts/pvp-duel` 未动；只用 Select-String 读了 server.log。

| 检查项 | 结论 |
|---|---|
| SA-2：所有写座位 uid 的路径 | 仓内写座位 uid 的地方只有两处：`SupplementalSynchronizationCodec` 第 241 行写 `0x0029`，`PvpRoomPayloadCodec.Encode(PvpSeatStateMessage)` 写 `0x002B`。两处都调用 `PvpSeatUserId`，非 Taken 座位一律返回 `0xFFFF`。大厅增量、欠帧补发的 8 座位帧、销毁前的空座帧都经 `PvpSeatStateMessage` 走这同一个编码器；投影侧的 `Empty` 和 `PvpRoomLobbyView` 在领域里存的 uid 是 0，编码时被统一映射，没有其他路径绕开。字节测试用例里空座和关闭座位故意带非零 uid（5、6），断言的是 `0xFF 0xFF`，所以无论改回写 0 还是改回透传 `seat.UserId`，测试都会失败。线上测试的 `PvpLobbyWireView.Seat` 对每个解析出的座位断言"空或关闭 ⇔ uid 为 0xFFFF"。 | 通过 |
| SA-1：开局即 Fighting | `SetReadyAsync` 开局时直接进入 Fighting：`PhaseDeadline = 开局 + BattleTimeLimit + FightOverrun`，每个战斗者的 `LastHeartbeat = 开局`。`0x0038` 的 TooEarly 改为以 `StartedAt` 计。以下成员都已删除：`Loading`、`PeerLinking`、`FightingSince`、`LoadingTimeout`、`PeerLinkTimeout`、`ReportLoaded/PeerLinkedAsync`、`ChangeKind.Loaded/PeerLinked`、`PvpFighterReports.Loaded/PeerLinked`，以及 `Report` 的 `whenAll` 参数。`git grep` 在 `Server/src`、`Server/tests` 中没有残留；BossTower 的 `LoadingTimeout` 与本次无关。`DepartAsync`、`AdvanceDueAsync`、`DueOf` 三个 switch 都把剩余阶段逐一列出，没有 `_` 兜底分支，构建在 TreatWarningsAsErrors 下通过。`0x012A`/`0x012B` 的 handler 只记 Information 日志后返回 false，不调用 `units.WriteAsync`，不进入领域；命令测试断言 `Rooms.Calls`、帧、结算三者都为空。停服用的 `CancelForShutdownAsync` 按 `Terminal: null` 判定，不依赖具体阶段，所以不受影响。结算和确认流程中，`Report` 去掉 `whenAll` 后，Rank/Result 两处原本就传 null，行为不变。延后释放座位以 `Phase: not Waiting` 判定，现在从开局就开始生效，与文档一致。 | 通过 |
| `PvpFighterReports` 重编号 | 只在 Application 内存里的房间 owner 和 Protocol 投影中用 `HasFlag` 读取，Persistence、HttpApi、GmWeb 都不引用，也不落库、不上线路。 | 没有依赖 |
| 回归能否抓住原缺陷 | 领域测试 `A_death_right_after_the_start_...`：开局 20 s 后直接 KO，接着 AwaitRank、两人确认、结算，战绩为 1/0 和 0/1。在旧实现下，fixture 的 `FightAsync` 断言 Fighting 就会失败，`ReportDeathAsync` 也会以 WrongPhase 被拒。线上测试 `A_ko_without_any_loading_or_linking_report_...` 不发 `0x012A`/`0x012B`，直接发 `0x0037` 和 `0x003A`，断言帧序 `0x002E→0x0031→0x002F→0x0030`、战绩、`pvp_matches` 只有 1 行、房间进入 AwaitResult。测试主机的时钟是冻结的，所以旧实现在这里一定停在 Loading，KO 被拒，测试会失败。实测日志也符合原缺陷：server.log 里有 `ReportDeathAsync ... ignored: WrongPhase` 和多条在错误阶段被忽略的心跳，`ReportLoaded`、`ReportPeerLinked`、`COMPLETE_LOAD`、`CONNECT_P2P` 出现 0 次。 | 能捕获 |
| 文档、协议页、catalog | `PvpDuel.md` 的阶段表、离场表、投影要点、测试段都与代码一致。catalog 已把 `0x012A`/`0x012B` 拆成单独一行，写明"仍解码，只记日志"。四个协议页都追加了 2026-11-15 修订节，标签是 CurrentImplementation，"实测"指服务端日志未收到，b1 地址标为 Static/High，并列入待实机复核。 | 基本一致，见下方非阻断项 |

非阻断项（不影响本轮通过，建议顺手处理）：

- N-1：协议页的修订用的是追加方式，旧正文没有改。`NOTI-0x0029.md` 第 74 行字段表仍写"uid 空座为 0"；`0x012A.md`、`0x012B.md` 第 56–63 行仍写 `ReportLoadedAsync`/`ReportPeerLinkedAsync` 推进阶段；`NOTI-0x002B.md` 第 70 行仍写"Loading 之后"。修订节都明确写了"上一节不再成立"，不会误导细读的人，但只扫字段表的人会读到错的值。建议直接改正文中的字段表这一行，或在旧句后加删除线。
- N-2：SA-2 引用的 b1 VA 中，`0x01A6B970`（PVPRoomInfo 构造）在 ClientHack 的 `Docs/a21-units/unit-033.md` 里有记录，但"空座初始化为 0xFFFF"这一点，以及 `0x01A0573C`、`0x01A070A0` 这两个地址，在工作区任何文档或数据里都查不到。Static/High 目前没有可复查的走图记录。建议把这几处判断写进 unit-033 或地址台账；修复后的实机复核本身已列为待验。
- N-3：spec 正文的 RQ-030 阶段表、RQ-040、AC-030，以及 TASK-04/05/07/08 计划文件仍按旧阶段描述。SA-1 已经声明它们失效，并写明"已定规则 6、7 随之失效，待用户确认"。这项由控制方等用户确认后收口，不在代码范围内。
- N-4：`PvpBattleProjectionTests` 中 `A_battle_cancelled_or_dropped_...` 的 InlineData 由 Loading 改成了 AwaitResult+DeadlinePassed。现在测的是"结果界面截止回等待"，不再是"取消"，测试名有些名不副实，覆盖没有损失。
