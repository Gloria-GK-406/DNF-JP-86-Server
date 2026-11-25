# TASK-08 — 真实 TCP 集成测试、文档与全量测试对比

## Task Unit Contract

- Revision: 1
- Outcome and references: 在真实 TCP 主机上证明完整链路；补齐专项文档与已证伪记载的修正；全量测试相对基线无新增失败。Plan T-007（由 A2 改为 TASK-08）；Spec RQ-003（AC-003 集成证据）、RQ-034（AC-034 集成证据）、RQ-040（断线集成证据）、RQ-080；TaskContract SC-2、SC-4、SC-5、SC-8、SC-11 的自动化部分。
- Owned boundary: 测试 `Server/tests/Dfo.Server.Networking.Tests/Integration/`（或 `Characterization/`）下新增决斗场集成测试及其测试支撑；文档 `Document/` 新增决斗场专项说明并在 `AGENTS.md`「按任务读取」表加一行入口、`Document/Architecture.md` 中 PvP 大厅相关描述更新；`Docs/Refactor/TableOwnershipRuling.md` 补 v11 修正案（TASK-03 审查 N1）；`Document/tasks/pvp-duel/` 任务说明；`verification/full-test-*.log` 与对比结果。生产代码只允许修集成测试暴露的缺陷（每处在 Result 的 Why 说明，若改变语义写 Blocker）。
- Required checks: 新集成测试通过；全量测试**逐项目顺序执行**（`dotnet test <项目> --no-build -m:1 -- xUnit.MaxParallelThreads=1`，一次一个），失败集合 ⊆ `verification/baseline-failed.txt`；文档链接有效。
- Dependencies: TASK-07
- Result review: required — 集成测试是实机前唯一的端到端证据，测试写错会给出虚假的完成信号；全量对比决定能否进入用户测试。

## Detailed Plan

### 集成测试（照 `Networking.Tests/Integration/PartyEstablishmentWireTests.cs`、`Characterization/CharacterizationSteps.cs`、`SelectionLifecycleWholeCommandTests.cs:60-93`）
- 主机：`CharacterizationHost`，用 `Decorate<IChannelEntryDomainService>` 把频道判为 `Pvp` 且 `LoginEnvironment = 24`（记录频道）；如需 8 再加一例确认不改战绩。时钟冻结——超时类不在这里测。
- 用例 1（完整一局 + 大厅一致）：A、B、C 三人进决斗频道；A 建房（含密码）、B 带密码进房、C 留在大厅；B 换队、A 改地图；双方准备开局；双方 `0x012A`、`0x012B`；B 发 `0x0037`（5 字节，victim=B、killer=A）；双方 `0x003A`；断言双方依次收到 `0x002E`、`0x0031`、`0x002F`（长度 26+19×2）、`0x0030`（51B）；双方 `0x0039`；房内与 C 收到 `0x002A`(state 1)；每一步 C 累积的大厅视图等于新会话此刻入大厅的 `0x0029` 快照（或用第四个连接在结尾进大厅比对）。最后检查数据库战绩：A 胜 1、B 负 1、段位/经验按规则。
- 用例 2（断线判负）：开局到 Fighting 后断开 B 的 TCP；A 收到 `0x002E(B, 0xFF)`、`0x0031`；A 确认后结算 B 记负且逃离；A 回等待后房间状态正确。
- 用例 3（邀请）：A 在房间中邀请大厅里的 C（REQUEST_PEER type 2），C 收到 USERINFO + `0x0007` type 2，C 接受后进房且不收 `0x0033` 成功体。
- 用例 4（互斥与拒绝）：错误密码回 `00 06`；房满 `00 04`；在房者被邀请组队时被拒。
- 若某处帧序与 design-r1 / 协议页不一致，以实现与上游为准修正测试断言前先确认不是实现缺陷。

### 文档
- `Document/PvpDuel.md`（或按 `Document/DocumentationGuidelines.md` 命名）：房间 owner 与事务参与者、对局阶段表与截止时间、结算与幂等、离场四条路径与延后投影、停服、权益表与启动校验、协议切片位置、已知待实机项。只写当前规则与坑，不写演化过程；协议事实引用 `protocol/`，不复制。
- `AGENTS.md`「按任务读取」表加"修改决斗场"一行；`Document/Architecture.md` 中关于 PvP 大厅的描述（Sessions 领域"PvP 大厅"、四处内存运行态之一）按现状更新（房间已迁到 `Pvp/`）。
- `Docs/Refactor/TableOwnershipRuling.md`：补一条修正案，说明 `character_pvp_skill_state` 并入 `character_pvp` 的裁定已被 v11 取代（`character_pvp` 拆为战绩与技能状态），按"跨界修正记载"写法（保留原文、加修正案）。

### 全量测试
逐项目顺序跑全部测试项目（含 `Tool/DbMigrate.Tests`），日志存 `verification/full-test-<项目>.log`，汇总失败集合与基线对比写入 `verification/full-test-summary.md`。新增失败必须修复或证明与本任务无关（例如偶发的 SQLite 并行释放，按 `Document/BuildAndTest.md` 的判别依据复跑）。

## Result

- Outcome: done（无生产代码改动；集成测试未暴露生产缺陷）
- Changed:
  - 新测试 `Server/tests/Dfo.Server.Networking.Tests/Integration/PvpDuelWireTests.cs`（4 例，`CharacterizationHost` 真实 TCP，`Decorate<IChannelEntryDomainService>` 把频道判为 `Pvp`：用例 1、2 环境 24，用例 3、4 环境 8）与支撑 `PvpLobbyWireView.cs`（只用客户端收到的 `0x0029`/`0x002A`/`0x002B` 累积大厅视图，渲染成可比文本）。
    1. `A_whole_battle_settles_once_and_the_lobby_matches_a_fresh_snapshot_after_every_step`：A/B/C 进决斗频道；A 建房（含密码）、B 带密码进房、B 换到 1 队再换回 2 队、A 改地图、B 准备 A 开局、双方 `0x012A`/`0x012B`、B 发 5 字节 `0x0037`（victim=B、killer=A）、双方 `0x003A`、双方 `0x0039`、B 退房、A 退房销毁。断言：开局帧 `0x002C(B,1)`→`0x002C(A,1)`→`0x002D(map 2, mode 2)`→`0x002A(2)`；双方 `0x002E(Bseat,Aseat)`→`0x0031`→`0x002F`（64 B = 26+19×2，胜方座位 A）→`0x0030`（51 B）；`0x002F` 本人段位/经验变化与 `0x0030` 胜负/经验/段位等于库内值；库内 A 胜 1、B 负 1，段位与经验等于 `IPvpSettlementRuleService.Decide`（按开局前库内记录、`EntitlementsOf` 环境 24）的结果；`pvp_matches` 只 1 行，`0x0039` 后仍 1 行；回等待时 A、B、C 都收到本房 `0x002A` state 1，领域房间 Waiting 且无对局；B 退房的本人帧序 USER_AREA 在 `0x002B` 之前、无本人 USER_STATE，C 收到 USER_STATE(B)。**每一步**（共 9 次）新开一个会话进大厅，其入场快照累积视图与 C 的增量累积视图逐字符相等（AC-003）。
    2. `A_fighter_who_disconnects_loses_as_fled_and_the_survivor_settles_and_waits_again`：开局到 Fighting 后断开 B 的 TCP；A 收到 `0x002E(Bseat,0xFF)`→`0x0031` 且**不**收座位移除（欠帧），C 立即收到座位移除，C 视图与新会话快照一致；A `0x003A` 后 `0x002F`(64 B，胜方 A)→`0x0030`；库内 A 胜 1、B 负 1，`pvp_match_results` 中 B fled=1、A fled=0；A `0x0039` 后先收欠帧 8 座位 `0x002B`(36 B)+`0x002A`，再收回等待 `0x002A` state 1；领域房间 Waiting、只剩 A 且 A 为房主；A 的累积大厅视图与 C 一致并与新会话快照一致。
    3. `A_room_invitation_reaches_a_lobby_member_who_accepts_into_the_room_without_the_enter_reply`：A 房内发 REQUEST_PEER type 2（7 字节，peer 0x1234）；C 依次收到 USERINFO `0x0002`、NOTI `0x0007` 体 `uid(A) 02 34 12 00 00`；A 无 CMD 回包；C 回 RESPONSE_PEER type 2 后进房，无 CMD `0x0033`/`0x000B`，收到含自己 uid 的 `0x002B`；领域两人同房；大厅视图与快照一致。
    4. `Rooms_refuse_a_wrong_password_and_a_full_room_and_a_seated_character_refuses_parties`：A 建带密码房并关 6 座（无拒绝回包）；B 错密码 `0x0033` 回 `00 06`，正确密码进房；C 进满房回 `00 04`；C 邀请在房的 A 组队、A 邀请 C 组队都回 CMD `0x000A` `00 13 00`，双方无 NOTI `0x0007`；房内仍 2 人；大厅视图与快照一致。
  - 文档：新 `Document/PvpDuel.md`（归属与代码位置、房间 owner 与事务、阶段与截止表、结算与幂等/退避放弃、权益表、离场四类与延后投影、结果界面欠帧、停服挂点与坑、启动校验、投影要点、测试与证据边界、实机待验清单；协议事实只链接 `protocol/catalog.md`）；`AGENTS.md`「按任务读取」加"修改决斗场"一行；`Document/Architecture.md` 的 Sessions 说明改为"PvP 大厅只保存就绪名单、不保存房间"，新增 `Pvp/` 领域目录说明，内存运行态一段的"哪些房间已公告"改为"谁在决斗大厅就绪"并说明房间 owner 是事务参与者；`Docs/Refactor/TableOwnershipRuling.md` 在 `character_pvp — 8 columns` 下加指向，文末新增 "Amendments / Amendment A-1"（v11 拆为 `character_pvp_record`/`character_pvp_skill_state`/`character_pvp_legacy` 及理由，原文保留）；`Document/tasks/pvp-duel/design-r1.md` 文末加修正案（§6 第 347 行：无 `updated_at`、迁移经验写 NULL 而非预算下限；§4 e 补实现挂点链接）。
  - 验证产物：`verification/full-test-<项目>.log`（9 个）与 `verification/full-test-summary.md`。
- Why:
  - 越界修正记载（按"跨界修正记载"规则）：`design-r1.md` 不在本单元边界内的那一处记载（§6 第 347 行）被 TASK-03 实现证伪，只改记载、不改要求，修正案写在该文件文末；`TableOwnershipRuling.md` 的修正案是本单元边界内的指定项（TASK-03 审查 N1）。
  - 离场投影在冻结时钟下照常发出：`PvpDeadlineScheduler.WaitAsync` 由 `IPvpDeadlineWake.Wake()` 写入的信号通道唤醒，离场 sink 入队即唤醒；时钟只用于计算截止等待（`CancellationTokenSource(delay, clock)`，冻结时钟下截止永不到期但等待用真实计时器，不会误推进）。测试用 `ReadUntilAsync` 等待 `0x0031`/`0x002B`，未改生产延迟，也未需要测试推进设施。
  - 集成测试与 design-r1、协议页的帧序一致，未发现需修正的断言或实现缺陷。
- Checks（最终源码，`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，`-m:1`，单项目、`MaxParallelThreads=1`）：
  - `dotnet build Server/86JP.Server.slnx -m:1`：0 警告 0 错误。
  - Networking.Tests `PvpDuelWireTests`：4 通过（单独运行 34 s；全量运行中同样通过）。
  - 反例（均已还原，哈希核对一致）：去掉 `PvpRoomLobbyFrames.For` 中"地图变化发 `0x002A`"，用例 1 在"map 2"一步大厅视图比对失败（map=1 vs 2）；去掉 `PvpRoomProjector` 大厅受众对欠帧成员的排除，用例 2 失败（A 在离场时多收 `0x002B`）。
  - 全量（9 个项目逐个顺序执行，22:14–22:54）：DbMigrate 33/33；Pvf 82/82；Infrastructure 1066 中 17 失败 2 跳过；Persistence 489/489；HttpApi 146/146；Host 327 中 1 失败 1 跳过；Protocol 2488/2488；Networking 226 中 14 失败；Application 6995 中 27 失败 8 跳过。失败 59 项中 58 项在 `baseline-failed.txt`；基线清单每行仍失败。
  - 基线外 1 项：`PackageOpeningTests.Archive_package_referencing_missing_equipment_is_refused_without_consumption`（期望 `UnsupportedContent` 实得 `ItemUnusable`，单独复跑确定性失败）。判为与本任务无关：测试 PVF 于 21:00 被替换（SHA-256 `A97132DD…`，非登记的修订版 `e1f3a25c…`，晚于 16:15 的基线），本分支未改任何开盒/库存/礼包代码。未在 `ef20398a3` 上用当前 PVF 直接复跑确认。
  - 文档链接：五个改动的 Markdown 文件内相对链接逐一 `Test-Path` 通过；锚点按 GitHub 规则手工核对。
  - 换行：`git diff --numstat` 与 `--ignore-cr-at-eol` 一致；新文件 LF。
- Remaining / 仍需实机：
  - 测试主机时钟冻结，截止类（Loading/PeerLinking 取消、判平、心跳超时、确认超时、结算重试与放弃）只有 Application/Protocol 层证据，无真实 TCP 证据。
  - 真实 TCP 只证明服务端帧序与字节；以下仍须 A21 客户端实机：`0x0037` 实际长度、`0x0039` 后 `0x002A` 是否解除结果等待、欠帧合成一帧 8 座位且无 USER_LEAVE 是否足够、团队 `0x002F` 胜方座位、选角 `0x0030` 位置、建房成功无 CMD、拒绝 CMD 的客户端表现、房内 USERINFO1 routing uid、邀请 NOTI `0x0007` type 2 与拒绝体、换模式/换队不清准备、离开者回城帧序的客户端表现。
  - 回城帧序已有真实 TCP 证据（用例 1 B 退房）；多人销毁时被送回者互收 state 0 的差异在当前领域（每次只空一个座）不可达，已写入 `PvpDuel.md`。
  - 组队侧互斥读 `IsSeated` 在组队写单元外：单执行器内同一作业串行，不可插入；已写入 `PvpDuel.md` 作为并行化前提。
  - 未覆盖：回选角/选角替换路径的真实 TCP 离场（只有断线）、环境 8 不改战绩的集成例（Application 层已有）、回等待后立即再开一局的集成例。
## Result Review

- Verdict: changes_required → 主控修正后通过。审查者用登记修订版 PVF 复跑 `PvpDuelWireTests` 4/4、Infrastructure `~Pvp` 15/15；脚本比对 9 个日志失败名单，基线外仅 `PackageOpeningTests.Archive_package_referencing_missing_equipment…`，主控已用登记 PVF 单独复跑通过，归因客户端目录 PVF 于 21:00 被替换。
- 主控修正（只改文档）：`TableOwnershipRuling.md` 修正案中 `character_pvp_legacy` 列名改为实际的 `rank_point`/`stat_a`/`win_streak`/`lose_streak`；`Document/PvpDuel.md` 去掉依赖 `.rks-agent/` 的 Spec 编号，崩溃边界直接写明理由。
- 说明：新文件在工作区为 CRLF，入库由 autocrlf 转为 LF；diff 无换行噪音。
