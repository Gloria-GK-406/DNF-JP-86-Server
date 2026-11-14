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
