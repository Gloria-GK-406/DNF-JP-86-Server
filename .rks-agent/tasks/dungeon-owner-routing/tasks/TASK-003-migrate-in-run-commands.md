# TASK-003 — 迁移副本内动作命令：拾取、角色死亡、怪物死亡、加载完成、过图

## Task Unit Contract

- Revision: 1
- Outcome and references：交付 Plan T-003（BD-003），覆盖 Spec RQ-002、RQ-004～RQ-006，AC-002、AC-004 中这 5 个命令的部分：0x002B 拾取、0x0028 角色死亡、0x0027 怪物死亡（含物件破坏）、0x0025 加载完成、0x002D 过图。每个命令改为：
  1. 入口判一次陈旧并丢弃；
  2. 取会话；
  3. `DungeonLocator.Locate`；
  4. `switch` 到唯一通道。
  
  通道不再判陈旧、不再为路由查塔归属。
- Owned boundary：
  - 生产代码：
    - `Server/src/Dfo.Server.Protocol/Game/GroundPickup/GameGetItemCommandHandler*.cs`；
    - `CharacterDeath/GameCharacterDeathCommandHandler*.cs`；
    - `GameMonsterDeathCommandHandler*.cs`、`MonsterTowerCommandPhases.cs`；
    - `GameMapLoadingCommandHandler*.cs`、`TowerLoadingCommandPhases.cs`、`OrdinaryLoadingCommandPhases.cs`；
    - 必要时可新增以塔命名的分部文件。
  - 0x009F（`TowerLoadingCommandPhases` 中的幻影开战）的分发不变，只保留已换名的陈旧判定。
  - 测试：
    - 这 5 个命令的陈旧帧矩阵测试；
    - 因处理器新增必填依赖而需要适配的夹具与手工构造。
- 约束（来自 TASK-001 与 TASK-002 的审查）：
  - 处理器以**必填**构造参数注入 `IDungeonParticipationDomainService`，不得写成可选参数。`ProtocolAlgorithmFixture` 只为必填参数提供默认登记替身，可选参数会得到 null。
  - **不得修改** TASK-001 表征测试（`DungeonOwnerRoutingCharacterizationTests*`）与 TASK-002 测试的断言。既有测试因夹具缺登记而失败时，修夹具而不改断言：
    - 让塔替身实现 `ICharacterTowerSeats`；
    - 或显式提供登记；
    - 或在真实容器中让替身塔登记。
  - **不得改**工作单元声明名（如 `"pick-up-boss-tower-item"`）、回包内容与顺序。
  - 通道内为处理本身做的校验（`BossTowerExact`、`IllusionTowerExact`、精确会话、读队员会话、普通流程按角色取普通副本）保留。
  - 陈旧判定在路由方法中是第一件事，先于会话查询。
- Required checks：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。
  - TASK-001 表征测试与 TASK-002 定位测试全部通过，断言零改动（`git diff` 中这些文件无断言改动）。
  - 新增陈旧帧矩阵：5 个命令 × 无会话 / 城镇 / 普通副本 / Boss 塔 / 幻影塔，断言无工作单元写入、无登记回包、未请求塔动作。0x0027 分非物件与物件破坏两种。Result 中逐格标出哪些是 Spec“陈旧帧行为变化清单”授权的变化格。
  - 变异检查至少三项，并在 Result 中列出失败用例：
    - 删除某命令入口的陈旧判定；
    - 在某个塔通道里重新加回 `bossTowers.Find` / `illusionTowers.Find` 路由；
    - 交换 0x0027 物件破坏的通道。
    还原后 `touch` 再构建。
  - 完整测试 `cd Server && dotnet test 86JP.Server.slnx`，输出（含退出码行）写入 `.rks-agent/tasks/dungeon-owner-routing/logs/task-003-full-test.log`，失败须归因。Result 中列出塔通道内部行为所依赖的既有测试类及其结果，例如：
    - Task44～49 real-wire；
    - `BossTowerPublicHandlerTests`；
    - `ExactTowerPhysicalClosureTests`；
    - `IllusionTowerPublicFlowTests`；
    - `BossTowerFrameIntegrationTests`。
- Dependencies：TASK-002
- Result review：required。理由：这次改动跨 7 个生产文件、5 个命令，并调整多处共享夹具。若通道搬迁时漏掉某个私有差异（如 0x0027 物件破坏的去向、无会话时的提前返回），会被后续 Task 当成既定写法复制；而陈旧帧变化格与保持格是否标对，执行者自检难以可靠判断。

## Detailed Plan

现行入口与分支位置见 `../evidence/background-7adec858.md` 第 1.3 节第 1～5 行；目标对照见 Spec“位置 → 通道对照”。

1. **路由方法**：每个命令的 `HandleAsync`／`RunAsync` 成为路由方法，结构如下。

   ```text
   if (GameRequestFreshness.IsStale(metadata)) return <现行“已处理”返回值>;
   var session = sessions.FindByConnection(connectionId);
   if (session is null) { <现行无会话处理> }
   switch (DungeonLocator.Locate(session, participation, bossTowers, illusionTowers))
   {
       case DungeonPlace.InBossTower boss: return await <Boss 通道>(..., boss, ...);
       case DungeonPlace.InIllusionTower illusion: return await <幻影通道>(..., illusion, ...);
       default: return await <普通通道>(..., session, ...);
   }
   ```

   各命令的特别处理：
   - **0x0028**：无会话时保持直接结束。
   - **0x002D**：无会话时保持记录后返回。
   - **0x0027**：无请求者时保持记录后返回。物件破坏在 Boss 塔被忽略，在幻影塔与普通或不在副本时走普通通道；非物件按三通道。
   - **0x0025**：普通通道包含城镇加载。
2. **通道**：
   - 把现行的 `PickupBossAsync` / `PickupIllusionAsync`、`DieBossAsync` / `DieIllusionAsync`、`KillBossAsync` / `KillIllusionAsync`、`CompleteBossLoadingAsync` / `CompleteIllusionLoadingAsync`、`AdvanceBossAsync` / `AdvanceIllusionAsync` 改为接收定位结果（会话 + 快照），删除其中的陈旧判定与 `FindByConnection` / `Find(session)` 路由查询，保留其后的精确校验与处理。
   - 返回值语义按需调整：原先“不在塔返回 false 交给下一分支”的分支，现在路由已确定，不再需要 false 分支。
   - 普通通道保留现行代码，不再依赖塔分支“顺带”丢弃陈旧帧。
   - 塔通道放在以塔命名的分部文件或既有塔相关文件中，与路由方法分开。
3. **注入**：受影响处理器构造器新增必填的 `IDungeonParticipationDomainService`。适配手工 `new` 这些处理器的测试，以及 Networking 测试中替身塔与真实登记不一致的容器（见 TASK-002 Result 的夹具盘点表）。
4. **删除过时注释**：删除 `GameGetItemCommandHandler.cs` 与 `MonsterTowerCommandPhases.cs` 中描述“Boss 先查后判、幻影先判后查”顺序差异的注释（Spec RQ-008）。
5. **测试**：陈旧帧矩阵按 Required checks 新增，优先放在 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/` 下，复用 TASK-001 的 `DungeonOwnerRoutingWorld`。
6. **检查**：按 Required checks 执行，结果写入 Result。

## Result

- Outcome：done。5 个命令（0x002B、0x0028、0x0027 含物件破坏、0x0025、0x002D）都改为“入口判陈旧 → 取会话 → `DungeonLocator.Locate` → `switch` 到唯一通道”。塔通道不再判陈旧、不再为路由查塔归属。工作单元声明名与回包未改。0x009F 分发未动。未发现无法在“定位一次、单通道”下保持现行行为的格，也未发现新的登记与现行查询不一致窗口，无 Blocker。

### Changed

路径前缀 `P/` = `Server/src/Dfo.Server.Protocol/Game/`。四个处理器的构造器都在两塔参数之后新增**必填**参数 `IDungeonParticipationDomainService participation`。

| 命令 | 路由方法 | 无会话 | Boss 塔 | 幻影塔 | 普通或不在副本 |
|---|---|---|---|---|---|
| 0x002B 拾取 | `P/GroundPickup/GameGetItemCommandHandler.cs` `HandleAsync` | 记录 Warning 后返回（原普通路径的同一条日志） | `PickupBossAsync(…, InBossTower, …)`，移到新文件 `GameGetItemCommandHandler.Towers.cs` | `PickupIllusionAsync(…, InIllusionTower, …)`，同上 | 新抽出的 `PickupOrdinaryAsync(…, session, …)`，主体为原普通代码 |
| 0x0028 角色死亡 | `P/CharacterDeath/GameCharacterDeathCommandHandler.cs` `HandleAsync` | 直接返回 true | `DieBossAsync(…, InBossTower, …)`，移到新文件 `GameCharacterDeathCommandHandler.Towers.cs` | `DieIllusionAsync(…, InIllusionTower, …)`，同上 | 新抽出的 `DieOrdinaryAsync(connectionId, session, message, …)` |
| 0x0027 怪物死亡 / 物件破坏 | `P/GameMonsterDeathCommandHandler.cs` `RunAsync` | Debug 日志后返回 | 非物件 → `KillBossAsync(…, InBossTower, …)`（`P/MonsterTowerCommandPhases.cs`）；物件破坏 → 忽略 | 非物件 → `KillIllusionAsync(…, InIllusionTower, …)`（同文件）；物件破坏用 `when message.ObjectCode is null` 落入普通通道 | 新抽出的 `ReportOrdinaryAsync(…, reporter, …)` |
| 0x0025 加载完成 | `P/GameMapLoadingCommandHandler.cs` `HandleAsync(GameFinishLoadingCommand)` | 进入普通通道（`CompleteOrdinaryLoadingAsync(id)` 返回 false 并记录 Warning） | `CompleteBossLoadingAsync(execution, InBossTower, token)`（`P/TowerLoadingCommandPhases.cs`） | `CompleteIllusionLoadingAsync(execution, InIllusionTower, token)`（同文件） | `CompleteOrdinaryLoadingAsync(id, token)`，签名与代码不变，含城镇加载 |
| 0x002D 过图 | `P/GameMapLoadingCommandHandler.cs` `HandleAsync(GameMoveMapCommand)` | Debug 日志后返回 | `AdvanceBossAsync(…, InBossTower, …)`（`P/TowerLoadingCommandPhases.cs`） | `AdvanceIllusionAsync(execution, InIllusionTower, token)`（同文件） | `MoveOrdinaryAsync(execution, context, message, session, token)`（`P/OrdinaryLoadingCommandPhases.cs`），删去原先的接收日志、会话查询与两塔试探，从 `runs.Find` 开始 |

- 塔通道：入参改为定位结果的强类型分支（会话 + 快照），返回值由 `bool` 改为无返回值。删除了其中的 `IsStale`、`FindByConnection` 与 `bossTowers.Find` / `illusionTowers.Find` 路由查询。`BossTowerExact` / `IllusionTowerExact`、Capture、工作单元与投影保持原样。
- 0x002D 的陈旧帧 Warning（`reason=StaleSequence`）从 `AdvanceBossAsync` 移到路由方法入口，日志文本不变。原先它在有会话的各位置都会输出（Boss 分支先判），现在无会话时也会输出。日志不在保护范围内。
- `StartIllusionAsync`（0x009F）未改：自身的 `GameRequestFreshness.IsStale` 与 `illusionTowers.Find` 仍保留，见 `TowerLoadingCommandPhases.cs` 文件头注释。
- 删除了过时注释：`GameGetItemCommandHandler.cs` 的“Boss lookup precedes stale; Illusion's stale gate below precedes lookup”，以及 `MonsterTowerCommandPhases.cs` 的“The stale gate precedes session/run lookup in the original helper”（RQ-008 的这两处）。新增的注释只说明通道前提，即“调用方已丢弃陈旧帧并完成定位”，以及物件破坏在两塔的去向。
- 新增测试 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/InRunCommandStaleFrameTests.cs`（`Category=Unit`，30 格）。

### Why

- **按 Detailed Plan 的结构实现**：路由方法第一行就是陈旧判定，先于会话查询与任何日志之外的动作（0x0027、0x002D 的 Debug 接收日志也挪到了陈旧判定之后）。普通通道不再依赖塔分支“顺带”去重。
- **保留 `connectionId` 入参**：0x002B 两塔通道与 0x002D Boss 通道仍接收 `connectionId`，因为原代码用它登记 NoRoom 回包或写日志。这样回包目标与原先逐字一致，不必论证 `session.ConnectionId` 恒等于查询用的连接。
- **0x0025 普通通道签名不变**：`CompleteOrdinaryLoadingAsync(id, token)` 被 `LoadingAlgorithmTestDriver` 直接调用，内部仍按连接取会话。这属于普通流程为处理本身做的精确会话校验，不是路由查询（RQ-005 允许）。
- **0x0027**：物件破坏的去向用 `case InIllusionTower when ObjectCode is null` 表达。这样幻影塔的物件破坏落入 `default` 普通通道；Boss 塔分支内对物件破坏直接返回。与 Spec“位置 → 通道对照”一致。
- **超出 Owned boundary 的测试改动**（都属于“为新必填依赖或通道签名适配夹具”，断言未改）：
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerPublicHandlerTests.cs` 的 `LoadingFlow` 辅助：原先直接调用 internal 的 `CompleteBossLoadingAsync(execution, id, metadata, token)`，并依赖“不在塔返回 false”。通道改收定位结果后，辅助改为先用同一登记替身 `DungeonLocator.Locate`：定位为 Boss 才调用通道并返回 true，否则返回 false。`Assert.False(...)` 与 `Starts == 0` 两条断言不变，仍表达“被替换的旧代次不能开始”。处理器改为显式传入同一个 `DerivedDungeonParticipation`，以便辅助与处理器看到同一份登记。
  - `Server/tests/Dfo.Server.Networking.Tests/Integration/BossTowerFrameIntegrationTests.cs`：与 TASK-002 盘点的预期一致，替身 Boss 塔 `Tower.Seed` 不会写入真实登记，迁移后 0x002D 落到普通通道，读帧超时失败（已复现）。修复方式：`Seed` 后在一个真实事务里调用真实 `IDungeonParticipationDomainService.TryClaim([角色], BossTower, RunId)`，模拟真实塔入场应写的登记。断言未改。

### Checks

- **构建**：`cd Server && dotnet build 86JP.Server.slnx` → 0 个警告，0 个错误。变异全部还原、`touch` 被改源文件后整解重建，仍为 0/0。
- **表征与定位测试**：`DungeonOwnerRoutingCharacterizationTests*`（155）、`DungeonLocatorTests`（17）、`DerivedDungeonParticipationTests`（2）全部通过。`git diff --stat -- Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/ Server/tests/Support/` 为空，TASK-001、TASK-002 的测试文件与夹具零改动。
- **陈旧帧矩阵** `InRunCommandStaleFrameTests.A_stale_in_run_request_is_dropped_wherever_the_requester_stands`：每格断言无回包、无工作单元、两塔都未被请求动作、未读当前视图，且请求者的普通副本（RoomVersion、死亡与就绪人数）和两塔座位保持原样。
  - RED（实现前，基准 78d2a17b 上的生产代码）：`dotnet test tests/Dfo.Server.Protocol.Tests --no-build --filter "FullyQualifiedName~InRunCommandStaleFrameTests"` → 失败 3，通过 27。失败的正是物件破坏 × 普通副本 / 幻影塔 / 城镇。
  - GREEN（实现后）：30/30 通过。

  “授权”指 Spec“陈旧帧行为变化清单”中授权的变化格；“保持”指现行已丢弃、继续丢弃的格。

  | 命令 | 无会话 | 城镇 | 普通副本 | Boss 塔 | 幻影塔 |
  |---|---|---|---|---|---|
  | 0x002B 拾取 | 通过（保持） | 通过（保持） | 通过（保持） | 通过（保持） | 通过（保持） |
  | 0x0028 角色死亡 | 通过（**授权**：原先入口直接 true，不判陈旧；两者都无写入、无回包） | 通过（保持） | 通过（保持） | 通过（保持） | 通过（保持） |
  | 0x0027 非物件 | 通过（**授权**：原先只写 Debug，不判陈旧） | 通过（保持） | 通过（保持） | 通过（保持） | 通过（保持） |
  | 0x0027 物件破坏 | 通过（**授权**：同上） | 通过（**授权**：原先回 Empty；RED 失败格） | 通过（**授权**：原先照常处理；RED 失败格） | 通过（保持：原先即忽略） | 通过（**授权**：原先经普通流程回 Empty；RED 失败格） |
  | 0x0025 加载完成 | 通过（保持） | 通过（保持） | 通过（保持） | 通过（保持） | 通过（保持） |
  | 0x002D 过图 | 通过（**授权**：原先 Debug 后返回，不判陈旧） | 通过（保持） | 通过（保持） | 通过（保持） | 通过（保持） |

  无会话的 3 个授权格（0x0028、0x0027 两种、0x002D）在基准上同样通过，差别只在日志与判定位置（Spec 已注明“这些情况下现行也无领域写入”）。可观察的变化只有物件破坏的 3 格。

- **变异检查**：脚本 `mutate.sh` 放在 scratchpad。每项先备份，再做唯一文本替换、构建并运行指定测试，然后用备份还原、`touch`，最后 `cmp` 确认与备份一致。最终 `git status` 只含本 Task 的改动；全部还原后整解重建，并重跑完整测试。

  | # | 变异 | 运行范围 | 失败用例 |
  |---|---|---|---|
  | M1 | 删除 0x002B 路由方法入口的陈旧判定 | `Game.Dungeons` 命名空间（215） | 8 个：`InRunCommandStaleFrameTests`(0x002B × Ordinary/Boss/Illusion/Town)；`A_stale_frame_is_dropped_where_it_is_dropped_today`(0x002B × Ordinary/Boss/Illusion/Town) |
  | M1b | 删除 0x0027 `RunAsync` 入口的陈旧判定 | 同上 | 11 个：新矩阵 0x0027 × Ordinary/Town/Boss/Illusion，物件破坏 × Ordinary/Town/Illusion；`A_stale_frame_is_dropped…`(0x0027 × Ordinary/Town/Boss/Illusion) |
  | M4 | 删除 0x0025 路由方法入口的陈旧判定 | 同上 | 8 个：新矩阵 0x0025 × Town/Illusion/Boss/Ordinary；`A_stale_frame_is_dropped…`(0x0025 × 同四格) |
  | M2 | 在 `KillIllusionAsync` 通道里重新加回路由查询：`sessions.FindByConnection` + `illusionTowers.Find(session)`，查不到即返回 | `Dfo.Server.Protocol.Tests` 全部 | **无失败（存活）**。原因：定位已确认该会话被幻影塔认可，且 RunId 一致；通道内再查同一座塔，在任何可达状态下结果都相同，只多一次查询，行为上无从区分。TASK-002 审查的非阻断 3 已指出，此类“通道内多余路由查询”只能由 T-006 的结构守护（AC-005）拒绝。本 Task 不为它造行为测试，以免把实现细节写成行为断言 |
  | M3a | 交换物件破坏通道：幻影塔去掉 `when ObjectCode is null`，物件破坏改进幻影击杀通道 | `Dfo.Server.Protocol.Tests` 全部 | `Object_destruction_is_ignored_in_a_boss_tower_and_otherwise_acknowledged_empty(Illusion)` |
  | M3b | 交换物件破坏通道：Boss 塔改为 `case InBossTower when ObjectCode is null`，物件破坏落入普通通道 | 同上 | `Object_destruction_is_ignored_in_a_boss_tower_and_otherwise_acknowledged_empty(Boss)` |

- **完整测试**：`cd Server && dotnet test 86JP.Server.slnx`，输出写入 `.rks-agent/tasks/dungeon-owner-routing/logs/task-003-full-test.log`，末行 `exit=0`。

  | 项目 | 通过 | 跳过 | 总计 | 对照 TASK-002 |
  |---|---|---|---|---|
  | Dfo.Pvf.Tests | 73 | 9 | 82 | 相同 |
  | Dfo.Server.HttpApi.Tests | 59 | 0 | 59 | 相同 |
  | Dfo.Server.Infrastructure.Tests | 655 | 348 | 1003 | 相同 |
  | Dfo.Server.Persistence.Tests | 484 | 0 | 484 | 相同 |
  | Dfo.Server.Protocol.Tests | 1951 | 60 | 2011 | +30（新增陈旧帧矩阵） |
  | Dfo.Server.Networking.Tests | 189 | 20 | 209 | 相同 |
  | Dfo.Server.Host.Tests | 200 | 12 | 212 | 相同 |
  | Dfo.Server.Application.Tests | 5512 | 493 | 6005 | 相同 |

  跳过均为既有的 PVF 等条件跳过，数量与 TASK-002 一致。

  失败归因：第一次完整运行时（变异检查前），`Networking.Tests` 中 `A21UdpDiscoveryServiceTests.Change_port_probe_is_answered_from_the_other_configured_port` 失败 1 个，`SocketException: Address already in use`。该用例先预留 UDP 端口、释放后再绑定，与并行测试存在端口竞争，与地下城路由无关。单独重跑 3/3 通过，最终完整运行通过。另外，第一次运行的日志里 `exit=` 为空，原因是 zsh 没有 `PIPESTATUS`。最终运行改用 `bash -c` 执行同一命令，日志已覆盖为最终结果。

- **塔通道依赖的既有测试类**（最终构建后单独运行）：

  | 测试类 / 用例组 | 结果 |
  |---|---|
  | Application.Tests `Task44_*`（加载、过图 real-wire，含 `DungeonLoadingFailureBoundaryTests`） | 20/20 通过 |
  | Application.Tests `Task45_*`（`MonsterDeathFailureBoundaryTests`） | 22/22 通过 |
  | Application.Tests `Task46_*`（`GroundPickupFailureBoundaryTests`） | 18/18 通过 |
  | Application.Tests `Task47_*`（`CharacterDeathFailureBoundaryTests`） | 24/24 通过 |
  | Application.Tests `Task49_*`、`DungeonExitLifecycleTests` | 16/16、8/8 通过（本 Task 未改 0x0048，作回归参考） |
  | Application.Tests `Task42_*`、`Task39_*` | 17/17、16/16 通过（同上） |
  | Application.Tests `BossTowerDomainServiceTests`（分部类，含上述 Task44/45/47 文件） | 122/122 通过 |
  | Application.Tests `IllusionTowerDomainServiceTests` | 97 通过，4 跳过 |
  | Application.Tests `IllusionTowerPublicFlowTests` | 0 通过，2 跳过（`[PvfFact]`，本机缺 PVF；本 Task 无法以它为证据） |
  | Protocol.Tests `BossTowerProjectionConcurrencyTests`（分部类，含 `BossTowerPublicHandlerTests.cs`、`ExactTowerPhysicalClosureTests.cs`） | 27/27 通过 |
  | Protocol.Tests `GetItemHandlerTests` / `CharacterDeathHandlerTests` / `MonsterDeathHandlerTests` | 12/12、3/3、30/30 通过 |
  | Networking.Tests `BossTowerFrameIntegrationTests` | 1/1 通过（适配前 30 秒读帧超时失败） |
  | Networking.Tests `IllusionTowerStageFrameIntegrationTests` | 2/2 通过 |

### 夹具适配清单

| 夹具 / 构造 | 原因 | 处理 | 断言 |
|---|---|---|---|
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CharacterDeath/CharacterDeathHandlerTests.cs` `DeathDriver` 手工 `new GameCharacterDeathCommandHandler(...)` | 新增必填参数，编译失败 | 在两塔参数后传 `new DerivedDungeonParticipation(默认 Boss 塔, 默认幻影塔, runs, selected)` | 未改 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerPublicHandlerTests.cs` `LoadingFlow` | 直接调用的 internal 通道 `CompleteBossLoadingAsync` 改签名，编译失败 | 显式构造登记替身并传给处理器；辅助方法先定位再调用通道，未定位到 Boss 返回 false | 未改 |
| `Server/tests/Dfo.Server.Networking.Tests/Integration/BossTowerFrameIntegrationTests.cs` | 真实容器中的登记看不到替身塔，运行期失败 | `Seed` 后在真实事务里 `TryClaim` Boss 登记 | 未改 |
| `ProtocolAlgorithmFixture.Create` 构造的驱动（`GetItemHandlerTests`、`PickupProjectionDriver`、`MonsterDeathHandlerTests`、`ObjectDestructionAlgorithmDriver`、`LoadingAlgorithmTestDriver`、`MoveMapAlgorithmDriver`、`TowerAlgorithmDrivers` 等） | 新增必填参数 | 无需改：夹具自动提供推出式登记替身（TASK-002） | — |
| Application.Tests 的 RouteDriver（`ActivatorUtilities`） | 新增必填参数 | 无需改：真实容器提供真实登记，真实塔与普通副本在事务内登记 | — |
| `DungeonOwnerRoutingWorld` | — | 无需改（TASK-002 已提供登记） | — |

### Remaining

- 变异 M2（通道内多余的塔路由查询）行为上不可区分，留给 T-006 的结构守护（BD-005 / AC-005）拒绝。
- 0x0048、0x0084、0x000F、0x0029 由 T-004 迁移；组队类命令由 T-005 迁移。TASK-002 盘点表中 `LeaveDungeonTests`、`DungeonSelectionTestDriver`、`DungeonRevivalTests` 的手工构造留给对应 Task。
- `IllusionTowerPublicFlowTests` 在本机因缺 PVF 跳过，幻影塔公开流程的这部分回归需要在有 PVF 的环境中确认。
- 未提交、未推送。

## Result Review Round 1

- 审查对象：Task Unit Contract Revision 1；执行结果为上方 `## Result`（Outcome: done）；改动是 HEAD 78d2a17b 之上的未提交工作区：
  - 生产代码：7 个修改，2 个新增 `.Towers.cs`；
  - 测试：3 个修改，1 个新增。
- 依据：`reviewing-work` 的 Task Unit 与 Code 准则（Task 局部范围）；spec.md“位置 → 通道对照”、陈旧帧行为变化清单、RQ-002、RQ-004～RQ-006；plan.md BD-003；evidence/background-7adec858.md §1.3 第 1～5 行、§1.4；TASK-001、TASK-002 审查约束。
- 逐命令对照 `git diff 78d2a17b -- Server/src`（新鲜帧）：

  | 命令 | 无会话 | 城镇 / 普通 | Boss 塔 | 幻影塔 | 结论 |
  |---|---|---|---|---|---|
  | 0x002B | 原先两塔分支在会话为空时返回 false，普通路径写 Warning 后返回；现在入口写同一条 Warning 后返回 | `PickupOrdinaryAsync`，主体逐行等同原普通代码 | 通道主体逐行搬移，仅删去 `FindByConnection`、`Find`、`IsStale`；Exact 先于 Capture 的顺序与原先一致 | 同左 | 一致 |
  | 0x0028 | 会话为空时直接返回 true，与原先一致 | `DieOrdinaryAsync` 等同原普通代码 | 逐行搬移 | 逐行搬移 | 一致 |
  | 0x0027 非物件 | Debug 日志后返回 | `ReportOrdinaryAsync` 等同原普通代码 | `KillBossAsync` 逐行搬移 | `KillIllusionAsync` 逐行搬移 | 一致 |
  | 0x0027 物件破坏 | 同上 | 普通通道（无副本时回 Empty） | `case InBossTower` 内只在 `ObjectCode is null` 时击杀，否则返回，即忽略 | `when ObjectCode is null` 不成立，落入 default 普通通道 | 与 Spec 对照表和原先外层 `bossTowers.Find`、幻影 `ObjectCode is null` 前置一致 |
  | 0x0025 | `session is null ? null : Locate` 不匹配任何 case，落入 `CompleteOrdinaryLoadingAsync`；该方法签名与代码未改，与原先两塔返回 false 后进入普通一致 | 普通通道含城镇加载 | 原先先 Capture 后 Exact，现在保持 | 同左 | 一致 |
  | 0x002D | Debug 日志后返回，与原先 `MoveOrdinaryAsync` 中会话为空的分支一致 | `MoveOrdinaryAsync` 从 `runs.Find` 起保持原代码 | `AdvanceBossAsync` 保留查后的 Info 日志、Capture、Exact 失败的 Warning，仅删去陈旧判定与查询 | 逐行搬移 | 一致 |

  - 被删除的代码：只有 `IsStale`、`FindByConnection`、`bossTowers.Find` / `illusionTowers.Find`、返回 false 的分支，以及两条顺序差异注释。没有夹带其他处理逻辑。
  - 0x002D 的陈旧帧 Warning 移到入口，无会话时也会写这条日志。日志不在保护范围内。
  - 工作单元声明名逐一比对，与原先相同。
  - 0x009F `StartIllusionAsync` 未改。
- 陈旧判定位置：5 个路由方法的第一条语句都是 `GameRequestFreshness.IsStale`，先于会话查询。0x0027 与 0x002D 的 Debug 接收日志排在它之后。
- 陈旧帧矩阵（`InRunCommandStaleFrameTests.cs`，6 × 5 = 30 格）：哪些格标为“授权变化”、哪些标为“保持”，与 Spec“陈旧帧行为变化清单”和 evidence §1.4 逐格一致。具体为：
  - 无会话下的 0x0028、0x0027、物件破坏、0x002D 为授权；
  - 物件破坏在普通、幻影、城镇为授权；
  - 物件破坏在 Boss、0x002B 与 0x0025 的全部格为保持。

  Result 声称实现前 RED 失败的 3 格正是物件破坏的授权格，可信。
- 约束遵守情况：
  - 四个处理器都以必填参数注入 `IDungeonParticipationDomainService`，grep 未发现可选写法。
  - `git diff --stat 78d2a17b -- Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons Server/tests/Support` 为空，TASK-001、TASK-002 的测试与夹具零改动。
  - 回包内容与顺序未改。
- 超出边界的测试改动：
  - `BossTowerFrameIntegrationTests.cs:97-104`：合约明确允许“在真实容器中让替身塔登记”。改法是在真实事务里 `TryClaim`，与真实塔入场时的登记一致，断言未改。合理。
  - `BossTowerPublicHandlerTests.cs`（`LoadingFlow`）：原先直接调用的 internal 通道改了签名，必须适配。`:104` 的 `Assert.False` 与 `Starts == 0` 两条断言未改，语义仍是“被替换的旧代次不能开始 Boss 加载”。合理，但有一点代价，见非阻断 1。
  - `CharacterDeathHandlerTests.cs:143`：只补了构造参数。
- 独立核查：
  - 重做变异 M3b（Boss 改为 `case InBossTower when ObjectCode is null`）：只有 `Object_destruction_is_ignored_in_a_boss_tower_and_otherwise_acknowledged_empty(Boss)` 失败，与 Result 一致。
  - 用备份还原后 `cmp` 一致，`touch` 后整解重建：0 警告，0 错误。
  - `Game.Dungeons`、`BossTowerProjectionConcurrencyTests`、`CharacterDeathHandlerTests` 共 245 个用例全部通过。
  - `git status` 与审查前相同。
  - `logs/task-003-full-test.log` 中 8 个测试项目都是“已通过”，没有“失败!”，末行为 `exit=0`。
- M2 存活的解释：成立。定位已确认幻影塔按本会话、本 RunId 认可，通道内再查同一座塔，在任何可达状态下都得到同一结果，行为上无法区分，应由 T-006 的结构守护拒绝。我没有找到其他应被行为测试抓到、却会存活的回退。
  - 改 0x0027 物件破坏去向，会被 TASK-001 用例抓到（M3a、M3b）。
  - 删入口陈旧判定，会被两份矩阵抓到。
  - 塔通道内部的 Exact、Capture 未动，仍由 Result 所列既有测试类保护。

```yaml
kind: rks-review
verdict: approved
```

- Verdict: approved
- Findings:
  - 阻断：无。
  - 非阻断：
    1. **`BossTowerPublicHandlerTests.cs:165-173` 的 `LoadingFlow.CompleteLoadingAsync` 改为在测试内先调 `DungeonLocator.Locate`，再决定是否调用通道。** 因此 `:104` 的 `Assert.False` 对被替换代次不再执行任何处理器代码，实际只是一条定位断言；这条断言已由 `DungeonLocatorTests` 与 TASK-001 代次窗口表征覆盖。
       - 另外，此夹具的塔替身不实现 `ICharacterTowerSeats`，推出式登记对旧代次给出“无登记”，而生产会给出“登记为 Boss”。结论（不进 Boss 通道）相同，但走的不是生产那条路。
       - 不影响正确性。后续若整理该测试，建议改为经公开入口 `HandleAsync(FinishLoading)` 断言 `Starts == 0`、没有 Boss 的 `LoadingCompleteNotification`，并让 `TowerDomain` 替身实现 `ICharacterTowerSeats`。
    2. **T-006 的结构守护要覆盖新增的 `GroundPickup/GameGetItemCommandHandler.Towers.cs` 与 `CharacterDeath/GameCharacterDeathCommandHandler.Towers.cs`。** 同时要允许 0x009F 的 `StartIllusionAsync` 保留自身的 `IsStale` 与 `illusionTowers.Find`（`TowerLoadingCommandPhases.cs`），不能按文件一刀切。
    3. **`IllusionTowerPublicFlowTests` 在本机因缺 PVF 跳过**（Result 已列入 Remaining）。幻影塔公开流程的回归需在有 PVF 的环境中补跑。
