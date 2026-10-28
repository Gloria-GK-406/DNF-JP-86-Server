# TASK-005 — 迁移组队类命令：放弃、退队、踢人、转让队长

## Task Unit Contract

- Revision: 1
- Outcome and references：交付 Plan T-005（BD-002 的整队位置、BD-003），覆盖 Spec RQ-002～RQ-006，以及 AC-002、AC-004 中组队类命令的部分。
  - 0x002A 放弃与 0x000D/0x000E/0x0079 组队变更，在入口陈旧判定之后，用一次 `DungeonLocator.LocateParty` 得出整队位置，再进入唯一通道：Boss 塔协调、幻影塔协调或普通流程。
  - 两塔各自重复的候选收集（`BossTowerCapture` / `IllusionTowerCapture` 中“调用者、目标、在线且身份匹配的队员”）合并为一处，候选顺序不变。
  - 塔协调通道不再自己按候选查塔归属，改用整队位置附带的副本列表。
- Owned boundary：
  - 生产代码：
    - `Server/src/Dfo.Server.Protocol/Game/GamePartyTransitionCommandHandler.cs`（`GiveUpAsync`）；
    - `GamePartyTransitionCommandHandler.Exit.cs`（`TryGiveUpTowersAsync` 等）；
    - `GamePartyTransitionCommandHandler.Stages.cs`（`HandleMutationAsync`、`Coordinate*TowerPartyAsync`、`*TowerCapture`、`*TowerExactSessions` 等）；
    - 必要时新增以塔命名的分部文件。
  - 测试：
    - 组队类命令在各位置的陈旧帧覆盖（核对 TASK-001 已有的 15 格，补缺口）；
    - 因通道签名变化而需要适配的驱动，例如 `Server/tests/Support/TowerAlgorithmDrivers.cs` 中直接调用 `Coordinate*TowerPartyAsync` 的地方。
- 约束：沿用 TASK-003 合约的全部约束。另有本 Task 专属约束：
  - 入口顺序保持现行：
    - 组队变更为 `DemandCurrent()` → 陈旧判定（返回 false）→ 取调用者；
    - 放弃为陈旧判定 → 取会话。
  - 两塔协调的全部分支语义保持现行：
    - 副本数大于 1 时以 `PartyGenerationChanged` 拒绝；
    - `*TowerExactSessions` 全员精确校验；
    - `allowVirtual` 在放弃中为 true、在组队变更中为 false；
    - 0x000E 幻影塔失败发 `RemovePartyMemberRefusal`，Boss 塔不发；
    - 写组队日志的位置不变。
  - `Coordinate*TowerPartyAsync` 若还有组队命令之外的调用方（例如断线或会话清理），先列出。这些调用方的行为不得改变：可以保留一个按候选自行收集的入口给它们用，也可以让它们同样走 `LocateParty`，但必须证明结果不变。不确定时写 `## Blocker`。
  - 不改 TASK-001～TASK-004 测试的断言；驱动调用的 internal 方法签名变化时，适配驱动而不改断言，并在 Result 中说明该断言实际覆盖了什么。
- Required checks：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。
  - TASK-001～004 测试全部通过，断言零改动；`PartyCommandHandlerTests`、`PartyTowerFootprintBoundaryTests`、`LeaveDungeonTests` 以及两塔组队相关测试全部通过。
  - 组队类命令的陈旧帧覆盖：4 个命令 × 城镇 / 普通副本 / Boss 塔 / 幻影塔（整队位置口径），断言无工作单元写入、无登记回包、未请求塔动作。这些格都是现行已丢弃、保持丢弃，没有授权变化格。已有的格引用 TASK-001 用例即可，只补缺口。
  - 变异检查至少三项，并在 Result 中列出失败用例：
    - `LocateParty` 改为只看调用者；
    - 整队位置中幻影塔优先；
    - 0x000E 幻影塔失败时不发拒绝帧。

    还原后 `touch` 再构建，用 `cmp` 确认一致。
  - 完整测试：`bash -c 'cd Server && dotnet test 86JP.Server.slnx 2>&1 | tee <log>; echo "exit=${PIPESTATUS[0]}" >> <log>'`，日志写到 `.rks-agent/tasks/dungeon-owner-routing/logs/task-005-full-test.log`，失败须归因。
- Dependencies：TASK-004
- Result review：required。理由：组队协调同时决定队伍状态变更与塔成员离开。合并两塔的候选收集、改用整队位置后，若候选顺序、去重、`allowVirtual` 或拒绝帧差异有偏差，就会改变队伍与塔的结果。而这些分支在测试中多经内部方法驱动，自检难以确认覆盖。

## Detailed Plan

现行逻辑见 `GamePartyTransitionCommandHandler.Stages.cs` 中的 `HandleMutationAsync`、`CoordinateBossTowerPartyAsync` / `CoordinateIllusionTowerPartyAsync`、`BossTowerCapture` / `IllusionTowerCapture`，以及 `.Exit.cs` 中的 `TryGiveUpTowersAsync`。背景见 `../evidence/background-7adec858.md` 第 1.3 节第 8～11 行与 1.5 节第 9、10 条。

1. **候选收集合并**：抽出一个方法，按现行顺序收集调用者、目标（若有）和在线且身份匹配的队员，同时返回队伍与目标。两塔的 `*TowerCapture` 改为使用它，或由路由方法收集一次后传入。
2. **路由**：放弃与组队变更在现有入口判定之后，调用一次 `LocateParty(candidates, ...)`，再按结果分派：
   - `PartyInBossTower` → Boss 协调，传入副本列表与队伍、目标；
   - `PartyInIllusionTower` → 幻影协调，同样传入；
   - `PartyOrdinaryOrNone` → 现行普通流程。

   协调通道删除按候选查塔的代码，保留副本数大于 1 的拒绝、精确校验与其后的处理。原先“协调返回未处理即交给下一座塔”的 `Handled == false` 语义，现在由路由决定；若协调内部仍有返回“未处理”的情形（例如副本数为 0），说明它在新结构下不可达，或保留其现行去向。
3. **其他调用方**：按约束处理 `Coordinate*TowerPartyAsync` 的非命令调用方。
4. **测试与夹具**：核对并补齐陈旧帧覆盖；适配 `TowerAlgorithmDrivers` 等直接调用协调方法的驱动。
5. **检查**：按 Required checks 执行，结果写入 Result。

## Result

- Outcome：done。0x002A 放弃与 0x000D/0x000E/0x0079 组队变更都改为“现行入口（陈旧判定）→ 一次收集候选 → 一次 `DungeonLocator.LocateParty` → 唯一通道”。两塔各自的候选收集合并为一处，协调通道改收整队位置附带的副本列表，不再按候选查塔。未发现整队位置无法保持现行结果的情形；`Coordinate*TowerPartyAsync` 没有组队命令之外的生产调用方，无 Blocker。

### Changed

- **候选收集（合并为一处）**：`GamePartyTransitionCommandHandler.Stages.cs` 新增
  - `internal sealed record PartyCandidates(PartySnapshot Party, SelectedSessionIdentity? Target, IReadOnlyList<SelectedSessionIdentity> Candidates)`；
  - `internal (PartyCandidates Candidates, DungeonPartyPlace Place) LocatePartyOf(caller, targetSlot)`：主体逐行取自原 `BossTowerCapture` / `IllusionTowerCapture`（`parties.FindParty` → 按 slot 取目标并 `TryGetInWorld` → 候选顺序“调用者、目标、在线且账号与 WireUserId 匹配的队员”），末尾改为调用一次 `DungeonLocator.LocateParty(candidates, participation, bossTowers, illusionTowers)`。`Distinct()` 与按 `RunId` 去重由 `LocateParty` 完成，与原 Capture 一致。
  - 删除 `BossTowerCapture`、`IllusionTowerCapture`、`BossTowerCharacter`、`IllusionTowerCharacter`、`BossTowerNotHandled`、`IllusionTowerNotHandled`。
- **路由**：
  - 组队变更 `HandleMutationAsync`：`DemandCurrent()` → `IsStale` 返回 false → `FindByConnection` 为空返回 false（三者顺序未动）→ `LocatePartyOf` → `switch`：
    - `PartyInBossTower` → Boss 协调，返回 `Result.Succeeded`，不发拒绝帧、不写组队日志（同现行）；
    - `PartyInIllusionTower` → 幻影协调；0x000E 失败发 `RemovePartyMemberRefusal`，写 `LogPartyMutation`（同现行）；
    - 其余 → 新拆出的普通通道 `MutatePartyOrdinaryAsync`，代码为原“两塔都未处理”之后的部分，逐行未改（仍自行读 roster 取目标）。
  - 放弃 `GiveUpAsync`：`IsStale` → `FindByConnection`（为空写 Warning 返回）不变 → `LocatePartyOf(session, null)` → `switch`：Boss / 幻影协调（`PartyChange.Leave`、`allowVirtual: true`、`GiveUpGameResponse`）后返回；其余 → 新拆出的 `GiveUpOrdinaryAsync`，代码为原 `TryGiveUpTowersAsync` 之后的部分，逐行未改。
  - 删除 `.Exit.cs` 中的 `TryGiveUpTowersAsync`。
- **两塔协调通道**：`CoordinateBossTowerPartyAsync` / `CoordinateIllusionTowerPartyAsync` 的参数由 `byte? targetSlot` 改为 `PartyCandidates candidates, DungeonPartyPlace.PartyIn{Boss,Illusion}Tower place`；开头用 `new *TowerCaptureState(candidates.Party, candidates.Target, place.Runs)` 代替原 Capture。
  - 删除 `Runs.Count == 0 → NotHandled` 分支：`LocateParty` 只在至少一个副本时给出塔位置，该分支在新结构下不可达。
  - 保留：副本数不为 1（现等价于大于 1）以 `PartyGenerationChanged` 拒绝、`*TowerExactSessions` 全员精确校验、`*TowerValidate` 中的 `allowVirtual`、离开／同步顺序、二阶段失败隔离、投影与 `closeTargets.Capture` 位置，均未改动。
  - `*TowerCaptureState`、`*TowerPartyAttempt` 类型未改（文件在边界外）。生产通道返回的 `Handled` 现在恒为 true；字段保留给测试驱动表达“未定位到该塔”。
- **非命令调用方**：`git grep` 全仓（`Server/`、`Tool/`）确认 `Coordinate*TowerPartyAsync` 的生产调用方只有本类的放弃与组队变更。断线与会话清理（`SelectCharacterLifecycleServices`）直接调塔服务，不经这两个方法。其余调用方只有测试驱动 `TowerAlgorithmDrivers.cs`，处理见夹具清单。没有为非命令调用方保留按候选自行查塔的入口。

### Why

- **整队位置与现行结果等价**：原 Boss Capture 对每个候选调 `bossTowers.Find(session)`，取非空、按 RunId 去重；没有 Boss 副本时，幻影 Capture 同样处理幻影塔。`LocateParty` 对每个候选先看登记，再向登记的那座塔按会话核对 RunId；按候选顺序得到 Boss 副本列表，Boss 优先、幻影次之。两者只在“塔认会话、登记却不指向该塔该局”时不同，TASK-002 审查已论证生产中无此窗口（evidence §2.3 末段）。会话代次窗口（W1）两者都判为不在塔，由 TASK-001 `A_party_whose_only_tower_seat_is_held_by_an_older_generation_takes_the_ordinary_flow`（16 格）钉住，仍通过。
- **候选只收集一次**：原先 Boss、幻影各读一次组队快照与会话，中间无写入，两次读到相同事实；合并后只读一次，再按整队位置分派。
- **越界改动**：
  1. `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs`，**改了两处断言**，需审查确认：
     - `Commands_are_the_only_route_from_protocol_to_party_writes`：期望的 `IPartyService` 调用序列中，0x000D 的 `query:2` 由 2 次改为 1 次；0x0079、0x000E 的 `query:1` 由 3 次改为 2 次。原注释“Boss and illusion domains each capture current party facts.”恰好描述本 Task 要消除的重复收集，已改写。
     - `Removing_an_empty_slot_returns_the_native_error_without_a_domain_write`：`["query:1","query:1","query:1"]` → `["query:1","query:1"]`。
     - 改的只是只读查询 `FindParty` 的次数。写操作（`invite`/`accept`/`leave`/`lead`/`remove`/`settings`）的种类与顺序、拒绝帧、命令序列都未改。合约要求合并候选收集，读次数减少是这一要求的直接结果。该文件不属于 TASK-001～004，后者的断言零改动。
  2. `Server/tests/Support/TowerAlgorithmDrivers.cs`：合约点名的驱动适配，见夹具清单。

### Checks

- **构建**：`cd Server && dotnet build 86JP.Server.slnx` → 0 个警告，0 个错误。全部变异还原后 `touch` 相关源文件再整解构建，仍为 0/0。
- **TASK-001～004 测试**：`Game.Dungeons` 命名空间 246/246 通过（其中 `DungeonOwnerRoutingCharacterizationTests` 155/155，新增 `PartyCommandStaleFrameTests` 10/10）。`git diff --quiet -- Server/src/Dfo.Server.Protocol/Game/Dungeons Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons` 为真，即定位器与 TASK-001～004 测试文件零改动。
- **组队相关既有测试**：

  | 测试类 | 结果 |
  |---|---|
  | Protocol `PartyCommandHandlerTests` | 25/25（含上述两处读次数断言调整） |
  | Protocol `LeaveDungeonTests` | 31/31 |
  | Protocol `BossTowerProjectionConcurrencyTests`（分部类） | 27/27 |
  | Protocol `IllusionTowerProjectionConcurrencyTests` | 18/18 |
  | Application `BossTowerDomainServiceTests`（分部类，含 `PartyTowerFootprintBoundaryTests.cs` 的 Task39 用例） | 122/122 |
  | Application `IllusionTowerDomainServiceTests` | 97 通过，4 跳过（既有 PVF 条件跳过） |
  | Application 过滤 `Party\|Tower\|GiveUp\|Leave` | 504 通过，40 跳过 |

- **陈旧帧覆盖表**（4 命令 × 整队位置；全部是“保持”：现行入口已丢弃，继续丢弃；无授权变化格）：

  | 命令 | 城镇 | 普通副本 | Boss 塔 | 幻影塔 | 无会话 |
  |---|---|---|---|---|---|
  | 0x000D/0x000E/0x0079，全队同处 | 既有 A | 既有 A | 既有 A | 既有 A | 既有 A |
  | 0x000D/0x000E/0x0079，调用者在城镇、队员在塔 | — | — | **新 N** | **新 N** | — |
  | 0x002A，单人（虚拟队伍，整队位置即本人位置） | 既有 B | 既有 B | 既有 B | 既有 B | 既有 B |
  | 0x002A，真实两人队（塔格只有队员在塔） | **新 N** | **新 N** | **新 N** | **新 N** | — |

  - A = `DungeonOwnerRoutingCharacterizationTests.A_stale_party_mutation_is_dropped_wherever_the_party_stands`（15 格）；
  - B = `DungeonOwnerRoutingCharacterizationTests.A_stale_frame_is_dropped_where_it_is_dropped_today` 中 0x002A 的 5 格；
  - N = 新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/PartyCommandStaleFrameTests.cs` 的 `A_stale_party_command_is_dropped_wherever_the_whole_party_stands`（10 格，`Category=Unit`）。每格断言：`AssertUntouched`（无登记回包、无工作单元、两塔未被请求动作、未读当前视图）、无组队投影、队长与队员的组队、普通副本、两塔座位快照不变。
  - 新用例在基准 179aa343 的生产代码上同样 10/10 通过（`git stash` 本 Task 生产与驱动改动后运行，再恢复），与“保持”一致；其检测力由变异 M4、M5 证明。
- **变异检查**：脚本 `/private/tmp/claude-501/-Users-puxianguo-project-DNF-JP-86-Server/808579c3-b965-4b38-9388-8be538d33465/scratchpad/t5/mutate.py`。每项先备份，做唯一文本替换，整解构建，运行 `Dfo.Server.Protocol.Tests` 全部用例与 Application 过滤 `Party|Tower|GiveUp|Leave`；然后用备份还原、`touch`、`cmp` 确认一致（8 项均为 True）。最终 `git diff` 中 grep 不到任何变异片段。Application 过滤集在各项变异下均为 504 通过，无失败。

  | # | 变异 | Protocol.Tests 失败用例 |
  |---|---|---|
  | M1（必做） | `LocateParty` 只看调用者：`candidates.Distinct().Take(1)` | 20 个：表征 `A_member_leaving_from_town_is_owned_by_the_tower_the_leader_sits_in`、`Leadership_given_from_town_to_a_seated_member_is_owned_by_that_members_tower`、`Removing_a_seated_member_from_town_is_owned_by_that_members_tower`、`Giving_up_from_town_is_owned_by_the_tower_a_member_sits_in`（各 Boss／Illusion）及 `A_boss_seat_in_the_party_wins_over_an_illusion_seat`；`DungeonLocatorTests` 5 个 Party 用例；Boss／幻影 `ProjectionConcurrencyTests` 各 3 个（`A_caller_whose_session_is_not_current…`、`External_party_leader_departure…`、`Leadership_transfer_to_a_returned_member…`） |
  | M2（必做） | 整队位置幻影塔优先（`LocateParty` 先判幻影） | `DungeonLocatorTests.Party_prefers_the_boss_tower_over_the_illusion_tower`、`A_boss_seat_in_the_party_wins_over_an_illusion_seat` |
  | M3（必做） | 0x000E 幻影塔失败不发拒绝帧（删除幻影分支的 `RemovePartyMemberRefusal`） | `A_non_leaders_removal_is_refused_by_the_owner_of_the_whole_partys_place(Illusion)`、`Removing_a_seated_member_from_town_is_owned_by_that_members_tower(Illusion)` |
  | M4 | 删除 `HandleMutationAsync` 入口陈旧判定 | 20 个：`A_stale_party_mutation_is_dropped…` 的 12 个有会话格；新 `PartyCommandStaleFrameTests` 的 6 个组队变更格；`PartyCommandHandlerTests.A_stale_leave_frame_cannot_bind_to_the_current_party`、`Stale_targeted_party_frames_stop_before_roster_or_session_capture` |
  | M5 | 删除 `GiveUpAsync` 入口陈旧判定 | 8 个：`A_stale_frame_is_dropped_where_it_is_dropped_today(42, Boss/Illusion/Ordinary/Town)`；新 `PartyCommandStaleFrameTests(42, Town/Ordinary/Boss/Illusion)` |
  | M6 | 放弃的 Boss 通道 `allowVirtual` 改为 false | `Give_up_alone_is_owned_by_the_requesters_place(Boss)` |
  | M7 | 放弃的幻影通道 `allowVirtual` 改为 false | `Give_up_alone_is_owned_by_the_requesters_place(Illusion)` |
  | M8 | Boss 通道 0x000E 失败也发拒绝帧 | `A_non_leaders_removal…(Boss)`、`Removing_a_seated_member_from_town…(Boss)` |

  - M5 中无会话的 0x002A 格未失败：去掉判定后，无会话仍走 Warning 后返回，结果相同，属预期。
- **完整测试**：`bash -c 'cd Server && dotnet test 86JP.Server.slnx 2>&1 | tee <log>; echo "exit=${PIPESTATUS[0]}" >> <log>'`，日志 `.rks-agent/tasks/dungeon-owner-routing/logs/task-005-full-test.log`，末行 `exit=0`，无失败。

  | 项目 | 通过 | 跳过 | 总计 | 对照基准 |
  |---|---|---|---|---|
  | Dfo.Pvf.Tests | 73 | 9 | 82 | 相同 |
  | Dfo.Server.HttpApi.Tests | 59 | 0 | 59 | 相同 |
  | Dfo.Server.Infrastructure.Tests | 655 | 348 | 1003 | 相同 |
  | Dfo.Server.Protocol.Tests | 1982 | 60 | 2042 | +10（新增陈旧帧用例；基准 2032 含 TASK-004 Round 1 补的 1 个） |
  | Dfo.Server.Networking.Tests | 189 | 20 | 209 | 相同 |
  | Dfo.Server.Persistence.Tests | 484 | 0 | 484 | 相同 |
  | Dfo.Server.Host.Tests | 200 | 12 | 212 | 相同 |
  | Dfo.Server.Application.Tests | 5512 | 493 | 6005 | 相同 |

  跳过均为既有的 PVF 等条件跳过。本次没有出现端口竞争类失败。

### 夹具与驱动适配清单

| 夹具 / 驱动 | 原因 | 处理 | 断言 |
|---|---|---|---|
| `Server/tests/Support/TowerAlgorithmDrivers.cs` `BossTowerPartyDriver` / `IllusionTowerPartyDriver` 的 `TryLeaveAsync` / `TryRemoveAsync` / `TryTransferAsync` | 直接调用的 internal 通道改了签名，编译失败 | 新增私有 `CoordinateAsync`：先调处理器自己的 `exit.LocatePartyOf(caller, slot)`（处理器的登记由 `ProtocolAlgorithmFixture.Create` 按驱动所持的塔、普通副本与会话推出），定位为本塔才调用通道；否则驱动自行返回 `Handled=false`（与原 `NotHandled` 相同的 `CallerNotCurrent` 拒绝） | 未改 |
| 使用上述驱动的测试：Protocol `BossTowerProjectionConcurrencyTests`、`IllusionTowerProjectionConcurrencyTests`；Application `PartyTowerFootprintBoundaryTests.cs`（`BossTowerDomainServiceTests` 分部）、`IllusionTowerDomainServiceTests.cs:1013,1028,1092` | 经驱动间接受影响 | 无需改 | 未改。其中 `Assert.True(result.Handled)` 原先表示“该塔按候选找到副本并处理”，现在表示“整队定位到该塔并进入该塔通道”。其后对结果、塔动作、投影与关闭范围的断言，仍由真实通道代码执行 |
| `PartyCommandHandlerTests.cs` | 读次数断言钉住了被消除的重复收集 | 见 Why 第 1 条 | **改了两处只读查询次数** |
| `LeaveDungeonTests` `ExitBusHandler`、`DungeonOwnerRoutingWorld` | TASK-004、TASK-002 已提供登记替身 | 无需改 | 未改 |

### Remaining

- `BossTowerPartyAttempt.Handled` / `IllusionTowerPartyAttempt.Handled` 在生产中恒为 true，只有测试驱动会给出 false。类型文件不在本 Task 边界内，是否删去该字段（连同驱动与测试中的 `Assert.True(result.Handled)`）留给 T-006 或后续整理决定。
- T-006 的结构守护需覆盖 `LocatePartyOf`（`DungeonLocator.LocateParty` 的唯一组队调用点），并确认两塔组队通道里不再出现 `bossTowers.Find(` / `illusionTowers.Find(` 用于路由。`*TowerExactSessions` 中的 `Find(member.Session)` 属于精确会话校验，需进白名单。
- 未提交，未推送。

## Result Review Round 1

- 审查对象：Task Unit Contract Revision 1；执行结果为上方 `## Result`（Outcome: done）；改动是 HEAD 179aa343 之上的未提交工作区：
  - 生产代码：`GamePartyTransitionCommandHandler{,.Exit,.Stages}.cs`；
  - 测试：`TowerAlgorithmDrivers.cs`、`PartyCommandHandlerTests.cs` 两个修改，新增 `PartyCommandStaleFrameTests.cs`。
- 依据：`reviewing-work` 的 Task Unit 与 Code 准则（Task 局部范围）；spec.md“整队位置”定义、RQ-002～RQ-006；plan.md BD-002（`LocateParty`）、BD-003；evidence/background-7adec858.md §1.2 第 43～45 行、§1.3 第 8～11 行、§1.5 #9 与 #10；TASK-001～004 审查约束。
- 核对结论：
  1. **`LocatePartyOf` 与原两份 Capture 逐行等价**（`GamePartyTransitionCommandHandler.Stages.cs:688-712`，对照被删的 `BossTowerCapture` / `IllusionTowerCapture`）。
     - 组队快照：原 `*TowerCharacter(caller)` 与现在内联的 `new(caller.AccountId, caller.CharacterId, caller.WireUserId, null)` 相同。
     - 目标解析：`targetSlot is null || party.IsVirtual` 则无目标；按 slot 取成员，再 `TryGetInWorld`。与原先相同。
     - 候选顺序：调用者、目标、在线且 `AccountId` 与 `WireUserId` 匹配的队员。与原先相同。
     - 候选 `Distinct()` 与按 `RunId` 去重移入 `DungeonLocator.LocateParty`，Boss 优先（TASK-002 已审）。
     - 原先幻影 Capture 只在 Boss Capture 为空时才执行，并重读一次快照；两次读取之间没有写入，合并为一次读取不改变事实。
  2. **两塔协调通道只删了按候选查塔与 `Runs.Count == 0 → NotHandled`**。
     - 该分支是原先唯一的 NotHandled 出口（TASK-002 审查已核对 `Stages.cs:503-504` 与 `:222-223`）。`LocateParty` 只在至少有一个副本时才给出塔位置，所以该分支在新结构下确实不可达。
     - 以下内容在 diff 中均未改动：`Runs.Count != 1` 以 `PartyGenerationChanged` 拒绝、`*TowerExactSessions`、`allowVirtual`（放弃仍传 true，组队变更传 false）、离开与同步顺序、二阶段失败隔离、`closeTargets.Capture`、投影。
     - 拒绝帧差异保持：Boss 不发；幻影在 0x000E 失败时发 `RemovePartyMemberRefusal`。
     - 日志位置保持：只有幻影分支写 `LogPartyMutation`。
  3. **入口顺序与普通流程**。
     - `HandleMutationAsync`：`DemandCurrent()` → `IsStale` 返回 false → `FindByConnection` 为空返回 false，这三行未动。
     - `GiveUpAsync`：`IsStale` → `FindByConnection`（为空写 Warning）未动，只把 `TryGiveUpTowersAsync` 换成定位加 `switch`。
     - `MutatePartyOrdinaryAsync`、`GiveUpOrdinaryAsync` 是从原方法原地截断而来，diff 中只有方法边界变化，主体逐行未改。
  4. **`PartyCommandHandlerTests` 的期望改动可以接受**。两处只删除了只读 `query:*` 记录：0x000D 由 2 次变为 1 次，0x0079 与 0x000E 由 3 次变为 2 次，空槽踢人由 3 次变为 2 次。减少量正好是“Boss 与幻影各收集一次”合并为一次。写操作（`invite`/`accept`/`leave`/`lead`/`remove`/`settings`）的种类与顺序、命令序列与拒绝帧断言都未改，没有掩盖行为变化。该文件不属于 TASK-001～004 的受保护测试。
  5. **`TowerAlgorithmDrivers` 的适配没有让既有断言退化成只测定位**。驱动先调处理器自己的 `LocatePartyOf`，定位到本塔才进入真实通道，否则返回与原 `NotHandled` 相同的 `(false, CallerNotCurrent)`。依赖它的 11 处都是 `Assert.True(result.Handled)` 之后继续断言结果、塔动作、投影与关闭范围，这些仍由真实通道代码执行。没有任何测试断言 `Handled == false`。
  6. **生产调用方**：`git grep` 显示 `Coordinate*TowerPartyAsync` 与 `LocatePartyOf` 在 `Server/src`、`Tool` 中只被本类的 0x002A 与组队变更调用，结论属实。生产中 `*TowerPartyAttempt.Handled` 现在恒为 true，见非阻断 1。
- 陈旧帧：`PartyCommandStaleFrameTests` 的 10 格都属于“保持”，入口原本已丢弃。补的正是 TASK-001 矩阵没覆盖的“调用者在城镇、队员在塔”与真实两人队 0x002A；Result 中没有错标授权格。
- 约束遵守情况：`git diff --stat 179aa343 -- Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons Server/src/Dfo.Server.Protocol/Game/Dungeons` 为空，TASK-001～004 测试与定位器零改动。工作单元声明名与回包未改。
- 独立核查：
  - 重做变异 M8（Boss 通道在 0x000E 失败时也发拒绝帧）：失败的正是 `Removing_a_seated_member_from_town…(Boss)` 与 `A_non_leaders_removal…(Boss)` 两个，与 Result 一致。
  - 用备份还原后 `cmp` 一致，`touch` 后整解重建：0 警告，0 错误。`Game.Dungeons`、`PartyCommandHandlerTests`、`*ProjectionConcurrencyTests` 共 316 个用例全部通过。`git status` 与审查前相同。
  - `logs/task-005-full-test.log` 中 8 个测试项目都是“已通过”，没有“失败!”，末行为 `exit=0`。

```yaml
kind: rks-review
verdict: approved
```

- Verdict: approved
- Findings:
  - 阻断：无。
  - 非阻断：
    1. **`BossTowerPartyAttempt.Handled` / `IllusionTowerPartyAttempt.Handled` 在生产中恒为 true**，只剩测试驱动用它表达“未定位到本塔”。这个字段已成为误导性遗留：读者会以为通道仍可能“未处理”。
       - 建议在 T-006 二选一：删除该字段，驱动在未定位时返回可空结果或直接断言定位，同步去掉 11 处 `Assert.True(result.Handled)`（这属于断言改动，需主代理认可）；或者至少在类型上注释“生产恒为 true，仅测试驱动使用 false”。
       - 不影响本 Task 的正确性。
    2. **`TowerAlgorithmDrivers.CoordinateAsync` 在测试中复刻了路由**（先 `LocatePartyOf`，再按位置选择通道），与 TASK-003 `BossTowerPublicHandlerTests.LoadingFlow` 是同一种写法。目前可以接受，因为通道本体仍真实执行。但 T-006 的结构守护只应扫描生产代码，不要把测试驱动纳入“通道外不得定位”的规则。
    3. **T-006 结构守护的白名单**需包含 `LocatePartyOf`（`DungeonLocator.LocateParty` 唯一的组队调用点），以及 `*TowerExactSessions` 中用于精确会话校验的 `Find(member.Session)`。Result 的 Remaining 已列出，确认无误。
