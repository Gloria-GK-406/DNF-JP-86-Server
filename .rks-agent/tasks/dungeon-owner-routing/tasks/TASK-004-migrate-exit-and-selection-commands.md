# TASK-004 — 迁移离开与选图命令：Eplp、回城、进入选图、复活币

## Task Unit Contract

- Revision: 1
- Outcome and references：交付 Plan T-004（BD-003），覆盖 Spec RQ-002、RQ-004～RQ-006，以及 AC-002、AC-004 中下列 4 个命令的部分：
  - 0x0048 Eplp；
  - 0x0084 回城；
  - 0x000F 进入选图（含唯一具名例外）；
  - 0x0029 复活币。

  每个命令改为：入口判一次陈旧并丢弃 → 取会话 → `DungeonLocator.Locate` → `switch` 到唯一通道。0x000F 的 Boss 塔通道只负责释放副本，返回“是否已释放”；路由方法据此决定继续普通通道的城镇选图，还是结束，不重新定位、不再判陈旧。
- Owned boundary：
  - 生产代码：
    - `Server/src/Dfo.Server.Protocol/Game/GamePartyTransitionCommandHandler.cs`（`EplpAsync`）与 `GamePartyTransitionCommandHandler.Exit.cs`（`SelectBossReturnAsync` / `SelectIllusionReturnAsync`）；
    - `DungeonSelection/DungeonSelectionCommandPhases.cs`、`DungeonSelection/GameDungeonSelectionCommands.cs`；
    - `CharacterDeath/GameUseRevivalCoinCommandHandler*.cs`；
    - 必要时新增以塔命名的分部文件。
  - 不改放弃（0x002A）与组队变更命令，它们属于 TASK-005。
  - 测试：
    - 这 4 个命令的陈旧帧矩阵；
    - 因新增必填依赖、通道签名变化而需要适配的夹具与手工构造。
- 约束：沿用 TASK-003 合约的全部约束：
  - 必填注入 `IDungeonParticipationDomainService`；
  - 不改 TASK-001、TASK-002 测试的断言，也不改 TASK-003 新增测试的断言；
  - 不改工作单元声明名与回包；
  - 保留通道内精确校验与现行副作用顺序。例如 Eplp Boss 通道的 `closeTargets.Capture` 在 Exact 之前；幻影通道 `state != LeaveState` 的早退。
  - 陈旧判定是路由方法的第一件事。
  - 夹具失败时修夹具，不改断言。
  - 测试中直接调用被改签名的 internal 通道时，优先改为经公开入口断言；做不到时，可以像 TASK-003 那样适配，但须在 Result 中说明断言实际覆盖了什么。
- Required checks：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。
  - TASK-001～TASK-003 的测试全部通过，断言零改动。
  - 新增陈旧帧矩阵：4 个命令 × 无会话 / 城镇 / 普通副本 / Boss 塔 / 幻影塔，断言无工作单元写入、无登记回包、未请求塔动作。Result 中逐格标出 Spec“陈旧帧行为变化清单”授权的变化格：
    - 0x000F：普通、幻影、城镇、无会话；
    - 0x0029：全部格；
    - 0x0048、0x0084：无会话。
  - 变异检查至少三项，在 Result 中列出失败用例：
    - 删除 0x0029 或 0x000F 入口的陈旧判定；
    - 0x000F 在 Boss 塔释放后不再接普通选图，或接上后又重新定位；
    - 0x0029 在幻影塔改走 Boss 拒绝。

    还原后 `touch` 再构建。
  - 完整测试：
    - 命令：`bash -c 'cd Server && dotnet test 86JP.Server.slnx 2>&1 | tee <log>; echo "exit=${PIPESTATUS[0]}" >> <log>'`；
    - 日志：`.rks-agent/tasks/dungeon-owner-routing/logs/task-004-full-test.log`；
    - 失败须归因。
  - Result 中列出这些命令的塔通道所依赖的既有测试类及结果，例如：
    - `Task42_*`、`Task44_*`～`Task49_*`；
    - `DungeonSelectionTowerBoundaryTests`、`DungeonExitLifecycleTests`、`DungeonRevivalTests`；
    - 两塔领域测试。
- Dependencies：TASK-003
- Result review：required。理由：本 Task 包含唯一的具名例外（0x000F 释放后续选图），以及 0x0029、0x000F 的大部分授权变化格。例外的边界若写错，或在通道搬迁时丢失现行副作用（Eplp 的连接捕获、回城与选图的城镇投影），会一直被带进最终交付；这些格的新鲜帧证据有一部分只钉到“读取了当前视图”，自检难以可靠判断。

## Detailed Plan

现行入口与分支见 `../evidence/background-7adec858.md` 第 1.3 节第 7、12、13、14 行；目标对照见 Spec“位置 → 通道对照”。

1. **0x0048 Eplp**：`EplpAsync` 成为路由方法。
   - 无会话时保持现行：走普通流程，记录后返回。
   - Boss 塔 → `SelectBossReturnAsync`；幻影塔 → `SelectIllusionReturnAsync`。两者改为接收定位结果，删除各自的陈旧判定与 `Find` 路由查询，其余保留。
   - 普通或不在副本 → 现行普通代码。
2. **0x0084 回城**：`BackToVillageAsync` 成为路由方法。
   - 无会话保持现行（普通流程，记录后返回）。
   - Boss 塔 → `SelectionLeaveBossAsync`；幻影塔 → `SelectionLeaveIllusionAsync`，同样改为接收定位结果。
   - 普通或不在副本 → 现行普通代码。
3. **0x000F 进入选图**：`EnterSelectDungeonAsync` 成为路由方法。
   - Boss 塔 → `SelectionPrepareBossAsync` 改为接收定位结果，返回是否已释放。保留队长与 Exact 校验：未释放时路由方法结束；已释放时直接执行普通选图代码。
   - 幻影塔、普通或不在副本、无会话 → 普通选图代码。
   - 删除 `GameDungeonSelectionCommands.cs` 中“选图先查归属，回城相反”的顺序注释。
4. **0x0029 复活币**：路由方法注入登记服务。
   - Boss 塔 → 回拒绝帧的 Boss 通道；
   - 其余 → 普通通道；
   - 现行 `bossTower.Find(payer)` 路由查询删除。
5. **注入与夹具**：受影响处理器新增必填依赖。适配 `DungeonSelectionTestDriver`、`DungeonRevivalTests` 等手工构造（见 TASK-002 Result 的夹具盘点），以及任何因真实容器中替身塔不登记而失败的集成测试。
6. **测试与检查**：陈旧帧矩阵新增到 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/`，复用 `DungeonOwnerRoutingWorld`；按 Required checks 执行，结果写入 Result。

## Result

- Outcome：done。4 个命令（0x0048 Eplp、0x0084 回城、0x000F 进入选图、0x0029 复活币）都改为“入口判陈旧 → 取会话 → `DungeonLocator.Locate` → 唯一通道”。0x000F 的 Boss 通道只负责释放并返回“是否已释放”，路由方法据此直接接普通选图或结束，不重新定位、不再判陈旧。塔通道不再判陈旧、不再为路由查塔归属；Exact 校验、队长校验、`closeTargets.Capture` 的位置、幻影 `state != LeaveState` 早退均保留。工作单元声明名与回包未改。未发现无法在“定位一次、单通道（含 0x000F 例外）”下保持现行行为的格，也未发现新的登记与现行查询不一致窗口，无 Blocker。

### Changed

路径前缀 `P/` = `Server/src/Dfo.Server.Protocol/Game/`。三个处理器都以**必填**构造参数注入 `IDungeonParticipationDomainService participation`：`GamePartyTransitionCommandHandler`、`GameDungeonSelectionCommandHandler` 放在两塔参数之后；`GameUseRevivalCoinCommandHandler`（主构造器）在末尾新增 `IIllusionTowerDomainService illusionTowers` 与 `participation`，原 `bossTower` 参数改名 `bossTowers`。

| 命令 | 路由方法 | 无会话 | Boss 塔 | 幻影塔 | 普通或不在副本 |
|---|---|---|---|---|---|
| 0x0048 Eplp | `P/GamePartyTransitionCommandHandler.cs` `EplpAsync` | 进入普通通道（原代码在会话为空时写 Warning 后返回） | `SelectBossReturnAsync(execution, InBossTower, state, option, token)`（`P/GamePartyTransitionCommandHandler.Exit.cs`）：先 `closeTargets.Capture`，再 `BossTowerExact`，其余原样 | `SelectIllusionReturnAsync(execution, InIllusionTower, …)`（同文件）：先 Capture，`state != LeaveState` 早退，再 `IllusionTowerExact` | 新抽出的 `EplpOrdinaryAsync(execution, context, message, session?, token)`，主体为原普通代码 |
| 0x0084 回城 | `P/DungeonSelection/DungeonSelectionCommandPhases.cs` `BackToVillageAsync` | 进入普通通道（Warning 后返回 false） | `SelectionLeaveBossAsync(execution, InBossTower, response, token)`（`P/DungeonSelection/GameDungeonSelectionCommands.cs`）：Exact 之后 Capture，与原先一致 | `SelectionLeaveIllusionAsync(execution, InIllusionTower, response, token)`（同文件） | 新抽出的 `BackToVillageOrdinaryAsync(context, message, session?, token)`，主体为原普通代码 |
| 0x000F 进入选图 | `P/DungeonSelection/DungeonSelectionCommandPhases.cs` `EnterSelectDungeonAsync` | 进入普通选图（Warning 后返回 false） | `SelectionPrepareBossAsync(execution, InBossTower, dungeon, token)` 返回 `released.Changed`；Exact 或队长校验失败返回 false。路由：未释放 → 返回 true 结束；已释放 → 直接调用普通选图 | 普通选图 | 新抽出的 `EnterOrdinarySelectionAsync(context, message, session?, token)`，主体为原普通代码 |
| 0x0029 复活币 | `P/CharacterDeath/GameUseRevivalCoinCommandHandler.cs` `RunAsync` | 直接返回（与原先一致） | `RefuseAsync`（拒绝帧即 Boss 通道全部内容） | 普通通道 | 新抽出的 `ReviveOrdinaryAsync(execution, payer, message, token)`，主体为原普通代码 |

- 塔通道：入参改为定位结果的强类型分支，返回值由 `bool` 改为无返回值（0x000F Boss 通道改为“是否已释放”）。删除了其中的 `GameRequestFreshness.IsStale`、`FindByConnection` 与 `bossTowers.Find` / `illusionTowers.Find` 路由查询，以及“不在塔返回 false”的分支。0x0029 的 `bossTower.Find(payer)` 路由查询删除。
- 删除了 `GameDungeonSelectionCommands.cs` 中“Selection checks for an attached run before consuming a stale request; return does the reverse.”（RQ-008 的第三处）。新增注释只说明通道前提（调用方已丢弃陈旧帧并完成定位）、0x000F 唯一交接的原因，以及 0x0029 为何 Boss 通道只有拒绝、幻影走普通。
- 新增测试 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/ExitAndSelectionStaleFrameTests.cs`（`Category=Unit`，4 × 5 = 20 格）。
- 未新增 `.Towers.cs` 分部文件：Eplp 的塔通道原本就在 `Exit.cs`，回城与选图的塔通道原本就在 `GameDungeonSelectionCommands.cs`，都已与路由方法分文件；0x0029 的 Boss 通道只有一次 `RefuseAsync` 调用，不值得单独成文件。

### Why

- **0x000F 例外用 `if` 而非 `switch`**：只有 Boss 一个塔通道，其余位置都进普通选图。`session is not null && Locate(...) is InBossTower boss && !await SelectionPrepareBossAsync(...)` 为真时结束；否则（含已释放）落到同一条 `return await EnterOrdinarySelectionAsync(...)`。这样“释放后续选图”只在路由方法里出现一次，普通选图不感知它来自哪里，也不会再定位。释放成功时 Boss 塔在同一写入内释放登记（`BossTowerDomainService.ReleaseForSelectionAsync` 中的 `participation?.Release`），所以领导者此时确实已在城镇。
- **普通通道签名带 `SelectedSessionIdentity?`**：三个普通流程原本就在会话为空时自己写 Warning 并返回。保留这一分支，无会话的现行行为（新鲜帧）逐字不变。
- **返回值**：陈旧帧在路由入口返回原先 Boss 分支的“已处理”值（0x0084、0x000F 为 true；Eplp、0x0029 无返回值）。消息 handler 丢弃该值（evidence §0）。
- **0x0029 定位口径变化**：原先只看 `bossTower.Find(payer)`，现在需要登记为 Boss 且 RunId 一致。差异只在“塔认会话但登记不符”，TASK-002 审查已论证生产无此窗口；会话代次窗口由 TASK-001 `A_tower_seat_held_by_an_older_generation…(Boss, 0x0029)` 钉住，仍通过。
- **超出 Owned boundary 的测试改动**（都属于“为新必填依赖适配手工构造”，断言未改，TASK-002 夹具盘点已列出）：
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/DungeonSelectionTestDriver.cs`：手工 `new GameDungeonSelectionCommandHandler(...)`，新增 `participation:` 实参，为 `new DerivedDungeonParticipation(同一 Boss 塔, 同一幻影塔, runs ?? 默认, sessions)`。为了让登记与处理器持有同一份塔实例，把两塔提为字段，`Command` 改为首次访问时构造（构造器无副作用，初始化时机变化不可观察）。`Enter` 路径不提供 `runs`，用夹具默认的空世界副本服务。
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs`：`ExitBusHandler` 手工 `new GamePartyTransitionCommandHandler(...)`，按位置补 `new DerivedDungeonParticipation(默认 Boss, 默认幻影, runs, selected)`。该驱动只发 0x002A（TASK-005 才接定位），目前不读登记。
  - `Server/tests/Dfo.Server.Application.Tests/World/DungeonRevivalTests.cs`：`RevivalDriver` 手工 `new GameUseRevivalCoinCommandHandler(...)`，补默认幻影塔与 `ProtocolAlgorithmFixture.Default<IDungeonParticipationDomainService>()`（未提供即抛异常的代理）。该驱动只直接调用 internal 的 `ReviveAsync`，不经路由，所以不需要可用的登记；若以后改为经路由，这个代理会显式失败而不是静默放行。
- 没有测试直接调用被改签名的 internal 通道，无需 TASK-003 那种“测试内先定位再调通道”的适配。

### Checks

- **构建**：`cd Server && dotnet build 86JP.Server.slnx` → 0 个警告，0 个错误。全部变异还原、`touch` 后重新构建，并另做一次 `--no-incremental` 整解重建，仍为 0/0。
- **TASK-001～003 测试**：`FullyQualifiedName~Game.Dungeons`（含 `DungeonOwnerRoutingCharacterizationTests*` 155、`DungeonLocatorTests` 17、`DerivedDungeonParticipationTests` 2、`InRunCommandStaleFrameTests` 30、本 Task 新增 20 及该命名空间其余用例）共 235 个全部通过。`git diff --stat -- Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/ Server/tests/Support/` 为空，TASK-001～003 的测试文件与共享夹具零改动。
- **陈旧帧矩阵** `ExitAndSelectionStaleFrameTests.A_stale_leave_or_selection_request_is_dropped_wherever_the_requester_stands`：每格断言无回包、无工作单元、两塔都未被请求动作、未读当前视图、无投影，且请求者的组队、普通副本与两塔座位保持原样。世界设为 Boss 塔会释放选图，使 0x000F × Boss 格在漏判时会走到释放与普通选图。
  - RED（实现前）：在 HEAD 2855c824 的临时工作树上只加入该测试文件，`--filter FullyQualifiedName~ExitAndSelectionStaleFrameTests` → 失败 5，通过 15。失败的是 0x0029 × 普通 / Boss，0x000F × 普通 / 城镇 / 幻影，正是现行会产生可观察处理的授权格。临时工作树已移除。
  - GREEN（实现后）：20/20 通过。

  “授权”指 Spec“陈旧帧行为变化清单”授权的变化格；“保持”指现行已丢弃、继续丢弃的格。

  | 命令 | 无会话 | 城镇 | 普通副本 | Boss 塔 | 幻影塔 |
  |---|---|---|---|---|---|
  | 0x0048 Eplp | 通过（**授权**：原先走普通流程写 Warning 后返回，无写入、无回包） | 通过（保持） | 通过（保持） | 通过（保持） | 通过（保持） |
  | 0x0084 回城 | 通过（**授权**：同上） | 通过（保持） | 通过（保持） | 通过（保持） | 通过（保持） |
  | 0x000F 进入选图 | 通过（**授权**：同上） | 通过（**授权**：原先打开普通选图；RED 失败格） | 通过（**授权**：同左；RED 失败格） | 通过（保持：原先 Boss 分支查后判陈旧即丢弃） | 通过（**授权**：原先无幻影分支，打开普通选图；RED 失败格） |
  | 0x0029 复活币 | 通过（**授权**：原先无会话即返回，不判陈旧） | 通过（**授权**：原先普通流程找不到普通副本静默返回） | 通过（**授权**：原先照常处理，本例回拒绝帧；RED 失败格） | 通过（**授权**：原先回拒绝帧；RED 失败格） | 通过（**授权**：原先静默返回） |

  无会话的 4 个授权格以及 0x0029 × 城镇 / 幻影在基准上同样通过：这些情况下现行也无写入、无回包，差别只在判定位置与日志（Spec 已注明）。可观察的变化只有 RED 失败的 5 格。

- **变异检查**：脚本 `mutate.py` 放在 scratchpad。每项先备份，再做唯一文本替换、构建 `Dfo.Server.Protocol.Tests` 并运行指定范围，然后用备份还原、`touch`，最后 `cmp` 确认与备份一致（7 项均为 identical）。全部还原后整解重建并重跑完整测试；最终 `git diff` 不含变异。

  | # | 变异 | 运行范围 | 失败用例 |
  |---|---|---|---|
  | M1 | 删除 0x0029 `RunAsync` 入口的陈旧判定 | `Game.Dungeons`（235） | 2 个：新矩阵 0x0029 × Ordinary / Boss。其余 3 格（无会话、城镇、幻影）陈旧帧也无可观察效果，行为上无法区分 |
  | M1b | 删除 0x000F `EnterSelectDungeonAsync` 入口的陈旧判定 | 同上 | 5 个：新矩阵 0x000F × Boss / Ordinary / Town / Illusion；`A_stale_frame_is_dropped_where_it_is_dropped_today(15, Boss)` |
  | M2a | 0x000F 在 Boss 塔释放后不再接普通选图（`!await Prepare…` 改为 `(await Prepare… \|\| true)`） | `Game.Dungeons` + `DungeonSelection` + `BossTower`（282） | `Enter_selection_continues_into_the_ordinary_selection_once_the_boss_run_is_released` |
  | M2b | 接上普通选图后又重新定位：`EnterOrdinarySelectionAsync` 开头加 `Locate(...) is InBossTower` 则返回 | `Dfo.Server.Protocol.Tests` 全部（2030） | **无失败（存活）**。原因：释放成功时 Boss 塔在同一写入内释放登记并移除座位，再定位在任何可达状态下都得到“普通或不在副本”，只多一次查询，行为上无法区分。与 TASK-003 的 M2 同类，留给 T-006 的结构守护（AC-005）拒绝；本 Task 不为它构造不可达状态的行为测试 |
  | M3 | 0x0029 在幻影塔改走 Boss 拒绝（`case InBossTower or InIllusionTower`） | `Dfo.Server.Protocol.Tests` 全部 | `Revival_coin_is_refused_only_by_a_boss_tower(Illusion)` |
  | M4 | 删除 0x0048 `EplpAsync` 入口的陈旧判定 | `Game.Dungeons` | 8 个：新矩阵 0x0048 × Ordinary / Boss / Illusion / Town；`A_stale_frame_is_dropped_where_it_is_dropped_today(72, 同四格)` |
  | M5 | 删除 0x0084 `BackToVillageAsync` 入口的陈旧判定 | 同上 | 8 个：新矩阵 0x0084 × 同四格；`A_stale_frame_is_dropped_where_it_is_dropped_today(132, 同四格)` |

- **完整测试**：`bash -c 'cd Server && dotnet test 86JP.Server.slnx 2>&1 | tee <log>; echo "exit=${PIPESTATUS[0]}" >> <log>'`，日志 `.rks-agent/tasks/dungeon-owner-routing/logs/task-004-full-test.log`，末行 `exit=0`。

  | 项目 | 通过 | 跳过 | 总计 | 对照 TASK-003 |
  |---|---|---|---|---|
  | Dfo.Pvf.Tests | 73 | 9 | 82 | 相同 |
  | Dfo.Server.HttpApi.Tests | 59 | 0 | 59 | 相同 |
  | Dfo.Server.Infrastructure.Tests | 655 | 348 | 1003 | 相同 |
  | Dfo.Server.Protocol.Tests | 1971 | 60 | 2031 | +20（新增陈旧帧矩阵） |
  | Dfo.Server.Networking.Tests | 189 | 20 | 209 | 相同 |
  | Dfo.Server.Persistence.Tests | 484 | 0 | 484 | 相同 |
  | Dfo.Server.Host.Tests | 200 | 12 | 212 | 相同 |
  | Dfo.Server.Application.Tests | 5512 | 493 | 6005 | 相同 |

  无失败，无需归因。跳过均为既有的 PVF 等条件跳过，数量与 TASK-003 一致。

- **塔通道依赖的既有测试类**（最终构建后单独运行，`--no-build`）：

  | 测试类 / 用例组 | 结果 |
  |---|---|
  | Application.Tests `Task42_*`（`DungeonSelectionTowerBoundaryTests`、`DungeonSelectionFailureBoundaryTests`、`DungeonSelectionProtocolFlowTests` 等：0x000F / 0x0084 两塔边界） | 17/17 通过 |
  | Application.Tests `Task43_*`、`Task44_*`、`Task45_*`、`Task46_*`、`Task47_*`、`Task48_*` | 11/11、20/20、22/22、18/18、24/24、21/21 通过 |
  | Application.Tests `Task49_*`（`DungeonExitLifecycleTests`：0x0048 两塔与普通退出） | 16/16 通过；`DungeonExitLifecycleTests` 8/8 |
  | Application.Tests `DungeonSelectionProtocolFlowTests` | 2/2 通过 |
  | Application.Tests `DungeonRevivalTests`（0x0029 结算，经适配的 `RevivalDriver`） | 8/8 通过 |
  | Application.Tests `BossTowerDomainServiceTests`（分部类，含 Task42/44/45/47 等文件） | 122/122 通过 |
  | Application.Tests `IllusionTowerDomainServiceTests` | 97 通过，4 跳过 |
  | Application.Tests `IllusionTowerPublicFlowTests` | 0 通过，2 跳过（`[PvfFact]`，本机缺 PVF；本 Task 无法以它为证据） |
  | Protocol.Tests `BossTowerProjectionConcurrencyTests`（分部类，含 `BossTowerPublicHandlerTests.cs` 中经 `DungeonSelectionTestDriver.Enter` 的 Boss 选图与 Eplp 用例、`ExactTowerPhysicalClosureTests.cs`） | 27/27 通过 |
  | Protocol.Tests `LeaveDungeonTests`（含适配的 `ExitBusHandler`） | 31/31 通过 |
  | Protocol.Tests `DungeonSelection*`、`*Eplp*`、`*EnterSelect*` | 16/16、4/4、6/6 通过 |
  | Protocol.Tests `PartySelectDungeonHandlerTests`、`TheCharacterChangesAsYouDressItTests`（`DungeonSelectionTestDriver` 使用方） | 26 通过，19 跳过（既有 PVF 跳过） |
  | Networking.Tests `BossTowerFrameIntegrationTests`、`IllusionTowerStageFrameIntegrationTests` | 3/3 通过 |

### 夹具适配清单

| 夹具 / 构造 | 原因 | 处理 | 断言 |
|---|---|---|---|
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/DungeonSelectionTestDriver.cs` 手工 `new GameDungeonSelectionCommandHandler(...)` | 新增必填参数，编译失败 | 传 `DerivedDungeonParticipation(同一 Boss 塔, 同一幻影塔, runs ?? 默认, sessions)`；两塔提为字段，`Command` 改为首次访问时构造 | 未改 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs` `ExitBusHandler` 手工 `new GamePartyTransitionCommandHandler(...)` | 新增必填参数，编译失败 | 按位置补 `DerivedDungeonParticipation(默认 Boss, 默认幻影, runs, selected)` | 未改 |
| `Server/tests/Dfo.Server.Application.Tests/World/DungeonRevivalTests.cs` `RevivalDriver` 手工 `new GameUseRevivalCoinCommandHandler(...)` | 新增两个必填参数，编译失败 | 补默认幻影塔与未提供即抛异常的登记代理（驱动不经路由） | 未改 |
| `ProtocolAlgorithmFixture.Create` 构造的驱动（`DungeonOwnerRoutingWorld`、`DungeonExitDrivers`、`TowerAlgorithmDrivers`、`PartyMutationTestDriver`、`ExactTowerPhysicalClosureTests` 等） | 新增必填参数 | 无需改：夹具自动提供推出式登记替身（TASK-002） | — |
| Application.Tests / Host.Tests 的真实容器 | 新增必填参数 | 无需改：真实登记由真实塔与普通副本在事务内写入 | — |
| `BossTowerFrameIntegrationTests` | — | 无需改（TASK-003 已在真实事务里 `TryClaim`） | — |

### Remaining

- 变异 M2b（0x000F 接普通选图后重新定位）与 M1 中 0x0029 无会话 / 城镇 / 幻影三格的入口判定，行为上不可区分，留给 T-006 的结构守护（BD-005 / AC-005）。守护需覆盖 `GameUseRevivalCoinCommandHandler.cs`、`DungeonSelectionCommandPhases.cs`、`GameDungeonSelectionCommands.cs`、`GamePartyTransitionCommandHandler.cs` / `.Exit.cs`，并只允许 0x000F 路由方法中的那一次 `Locate`。
- `GamePartyTransitionCommandHandler` 已注入登记服务，0x002A 与组队变更命令的 `LocateParty` 迁移由 T-005 完成；`LeaveDungeonTests` 的 `ExitBusHandler` 已提供登记替身，T-005 可直接使用。
- `IllusionTowerPublicFlowTests` 在本机因缺 PVF 跳过，幻影塔公开流程的这部分回归需在有 PVF 的环境中确认。
- 未提交、未推送。

### Round 1 后补充

针对审查非阻断第 1 条：0x000F 中“Exact 或队长校验失败即结束”这一支原先没有测试保护，现已补上。

- **新用例**：新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/EnterSelectionBossHandOverTests.cs`（`Category=Unit`），用例为 `A_boss_tower_member_who_is_not_the_leader_ends_the_selection_with_the_tower`。
  - 构造：用 `DungeonOwnerRoutingWorld`，设 `Boss = { ReleasesForSelection = true }`，然后 `Boss.Enter(leader, member)`（slot 0 为队长）。
  - 动作：非队长 `member` 发送新鲜 0x000F（`GameMessageMetadata.Empty`，选择 3505）。
  - 断言：
    - `world.AssertUntouched()`：无回包、无工作单元、两塔都未被请求动作（含释放）、未读取当前视图；
    - `member` 仍坐在原 Boss 塔副本中。
  - 没有修改其他测试文件，也没有修改任何既有断言。
- **变异 X5**：把 `GameDungeonSelectionCommands.cs` `SelectionPrepareBossAsync` 中 Exact 或队长校验失败分支的 `return false` 改为 `return true`，运行 `Dfo.Server.Protocol.Tests` 全部用例。
  - 结果：只有 `EnterSelectionBossHandOverTests.A_boss_tower_member_who_is_not_the_leader_ends_the_selection_with_the_tower` 失败。原因是变异后请求转入普通选图，读取了当前视图。
  - 还原：用备份还原、`touch`，`cmp` 确认与备份一致，`grep` 确认 `return false` 已恢复。
- **还原后结果**：
  - 整解构建：0 个警告，0 个错误。
  - `EnterSelectionBossHandOverTests`：1/1 通过。
  - `Game.Dungeons` 命名空间：236/236 通过。
  - `Dfo.Server.Protocol.Tests` 全部：通过 1972，跳过 60，总计 2032，比上方完整测试多 1 个，即新用例。
- 变异运行时，Protocol.Tests 报告的总数为 2031（失败 1、通过 1970、跳过 60），比还原后的 2032 少 1 个。尚未查明原因。还原后的全量运行计数正确，且只有新用例失败，结论不受影响。完整解决方案测试未重跑，上方 `task-004-full-test.log` 仍是补充前的结果。

## Result Review Round 1

- 审查对象：Task Unit Contract Revision 1；执行结果为上方 `## Result`（Outcome: done）；改动是 HEAD 2855c824 之上的未提交工作区：
  - 生产代码：5 个修改；
  - 测试：3 个修改，1 个新增。
- 依据：`reviewing-work` 的 Task Unit 与 Code 准则（Task 局部范围）；spec.md“位置 → 通道对照”、陈旧帧行为变化清单、RQ-002、RQ-004（0x000F 例外）、RQ-005、RQ-006；plan.md BD-003；evidence/background-7adec858.md §1.3 第 7、12～14 行、§1.4、§1.5；TASK-001～003 审查约束。
- 逐命令对照 `git diff 2855c824 -- Server/src`（新鲜帧）：

  | 命令 | 无会话 | 城镇 / 普通 | Boss 塔 | 幻影塔 | 结论 |
  |---|---|---|---|---|---|
  | 0x0048 | `session is null ? null : Locate` 落入 default，进入 `EplpOrdinaryAsync`，Warning 后返回；与原先“会话为空跳过两塔进普通”一致 | `EplpOrdinaryAsync` 主体逐字为原普通代码 | 先 `closeTargets.Capture`、再 Exact、再 `state == 2` 回包，原顺序保持；只删去 `Find` 与陈旧判定（原先陈旧判定夹在 Capture 与 Exact 之间） | 先 Capture、`state != LeaveState` 早退、再 Exact，原顺序保持 | 一致 |
  | 0x0084 | 同上，进入 `BackToVillageOrdinaryAsync` | 普通代码不变 | 先 Exact 后 Capture，与原先一致 | 同左 | 一致 |
  | 0x000F | `session is not null &&` 不成立，进入普通选图 | 普通选图 | 见下文“0x000F 例外” | `Locate` 不是 `InBossTower`，进入普通选图，与原先“无幻影分支”一致 | 一致 |
  | 0x0029 | 会话为空时直接返回 | `ReviveOrdinaryAsync` 主体逐字为原普通代码 | `RefuseAsync`，与原先 `bossTower.Find(payer)` 命中后拒绝一致 | default 普通通道，无普通副本时静默 | 一致 |

  - **0x000F 例外逐一对应**。原先 `SelectionPrepareBossAsync` 返回 true 表示结束、false 表示继续；现在返回“是否已释放”，路由用 `!released` 决定是否结束。三种情形：
    - Exact 或队长校验失败：原先返回 true，结束；现在返回 false，经 `!` 后结束。
    - 未释放：原先 `!Changed` 为 true，结束；现在 `Changed` 为 false，结束。
    - 已释放：原先返回 false，继续；现在返回 true，继续进入 `EnterOrdinarySelectionAsync`，不重新定位、不再判陈旧。

    三种情形与基准语义逐一对应。
  - **删掉的代码**只有 `IsStale`、`FindByConnection`、`bossTowers.Find`、`illusionTowers.Find`、`bossTower.Find(payer)`、返回 false 的分支，以及 RQ-008 第三处顺序注释。没有夹带其他处理逻辑。
  - 0x0048 Boss 原先在判陈旧前已 Capture，现在陈旧帧在入口丢弃、不再 Capture。`closeTargets` 是 Scoped 状态，evidence §1.5 #5 已判定这一差异不可观察。
  - 工作单元声明名（`selection-release-boss-tower`、`select-illusion-tower-return` 等）与回包未改。
- 陈旧判定位置：4 个路由方法的第一条语句都是 `GameRequestFreshness.IsStale`，先于 `FindByConnection`。
- 陈旧帧矩阵（`ExitAndSelectionStaleFrameTests.cs`，4 × 5 = 20 格）：标注与 Spec 清单和 evidence §1.4 逐格一致。
  - 0x0048、0x0084：无会话为授权，其余四格为保持。
  - 0x000F：Boss 为保持，其余四格为授权。
  - 0x0029：全部五格为授权。
  - RED 失败的 5 格（0x0029 × 普通 / Boss；0x000F × 普通 / 城镇 / 幻影）正是现行会产生可观察处理的授权格，可信。
  - 世界设为 `ReleasesForSelection = true`，使 0x000F × Boss 格在漏判陈旧时会走到释放。设计合理。
- M2b 存活的解释成立。`BossTowerDomainService.ReleaseForSelectionAsync`（`Server/src/Dfo.Server.Application/World/BossTower/BossTowerDomainService.cs:248-254`）在同一个 `Execute` 写入内，对每个成员调用 `participation?.Release` 并 `next.Leave`。释放成功后，登记与座位同时消失，再定位必然得到“普通或不在副本”，行为上无法区分。
- 依赖与夹具：
  - **复活币处理器**：新增的 `IIllusionTowerDomainService`、`IDungeonParticipationDomainService` 都以 `DomainService` 结尾，符合 `CommandHandlerArchitectureTests` 的后缀规则（完整测试通过）。`bossTower` → `bossTowers` 是主构造器参数改名；grep 显示除 `DungeonRevivalTests` 的位置参数构造外没有其他构造方，生产经 DI 构造，不受影响。
  - **`DungeonRevivalTests`**：传入“未提供即抛异常”的登记代理是合适的。该驱动只调用 internal 的 `ReviveAsync`，不经路由；以后若改为经路由，会显式失败而不是静默落到普通流程。
  - **`DungeonSelectionTestDriver`**：`Command` 改为首次访问时构造。处理器构造器只做赋值，参数都来自主构造器捕获的同一组值，惰性化不改变既有用例的语义。两塔提为字段，保证登记替身与处理器持有同一份塔实例。
  - **`LeaveDungeonTests`**：只按位置补了构造参数。
  - 三处适配的断言均未改。
- 约束遵守情况：
  - 三个处理器都以必填参数注入登记服务。
  - `git diff --stat 2855c824 -- Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons Server/tests/Support` 为空，TASK-001～003 的测试与共享夹具零改动。
- 独立核查：
  - 自拟变异 X5：在 `GameDungeonSelectionCommands.cs` 的 `SelectionPrepareBossAsync` 中，把 Exact 或队长校验失败的 `return false` 改为 `return true`，即非队长或不精确的 Boss 塔成员也会接着打开普通选图。结果 Protocol.Tests 全部 2031 个、Application.Tests 中 `Task42*` / `*BossTower*` / `*DungeonSelection*` 共 139 个**全部通过，变异存活**。见非阻断 1。
  - 用备份还原后 `cmp` 一致，`touch` 后整解重建：0 警告，0 错误。`Game.Dungeons` 235 个用例全部通过；`git status` 与审查前相同。
  - `logs/task-004-full-test.log` 中 8 个测试项目都是“已通过”，没有“失败!”，末行为 `exit=0`。

```yaml
kind: rks-review
verdict: approved
```

- Verdict: approved
- Findings:
  - 阻断：无。实现与基准逐格一致；变异 X5 暴露的是测试网缺口，不是实现缺陷。
  - 非阻断：
    1. **0x000F 例外中“Exact 或队长校验失败即结束”这一支没有任何行为测试保护**（变异 X5 存活）。本 Task 恰好把这一支的返回值语义从“true 结束”反转为“false 结束”，以后若有人误改，非队长或不精确的 Boss 塔成员就会在仍坐在塔里时打开普通选图，现有测试不会报警。
       - 建议补一个用例，可以放在新文件，或放在 `ExitAndSelectionStaleFrameTests` 旁，不改 TASK-001 文件。
       - 写法：用 `DungeonOwnerRoutingWorld` 构造 `Boss.Enter(leader, member)` 且 `ReleasesForSelection = true`，由非队长 `member` 发新鲜 0x000F。
       - 断言：Boss 塔未被请求动作、无工作单元、未读当前视图、无回包。补完后用 X5 确认它会失败。
       - 可在 T-005 或 T-006 一并补上。
    2. **T-006 的结构守护需要放行 0x000F 路由方法中唯一的那次 `Locate`**，并覆盖 Result Remaining 所列 5 个文件（`GameUseRevivalCoinCommandHandler.cs`、`DungeonSelectionCommandPhases.cs`、`GameDungeonSelectionCommands.cs`、`GamePartyTransitionCommandHandler.cs`、`GamePartyTransitionCommandHandler.Exit.cs`）。0x0029 无会话、城镇、幻影三格的入口陈旧判定行为上不可区分（M1），只能由守护保证。
    3. **`IllusionTowerPublicFlowTests` 在本机因缺 PVF 跳过**（Result 已列入 Remaining），需在有 PVF 的环境中补跑。
