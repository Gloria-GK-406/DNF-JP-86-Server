# TASK-02 — 坐标有一个专门的主人

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 一个专门的领域服务持有并回答角色坐标；坐标的 6 处读与 2 处整体写回全部改经它；角色实例不回答坐标。
- Referenced spec requirements: RQ-003, RQ-004
- Referenced plan sections: T-002；BD-003, BD-004, BD-005, BD-015
- Owned boundary: 新的坐标领域服务及其接线，以及那 8 处调用点。
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Abstractions/Characters/` 或 `.../Sessions/` 下新增的坐标领域服务接口
  - `Server/src/Dfo.Server.Application/` 下该服务的实现与组合根接线
  - `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs`
  - `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownReturnProjection.cs`
  - `Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs`
  - 相应的新增判据
- Dependencies: TASK-01
- Required dependency outputs: TASK-01 已把角色实例对会话运行态聚合的依赖摘干净，本任务不得把它加回来
- Acceptance conditions:
  - 坐标可从该领域服务问出；角色实例没有回答坐标的成员
  - 坐标的全部读取点与全部改变点中，无一处从会话运行态聚合取坐标或靠整体写回它来改坐标
  - **落库时机不变**：仍挂在会话生命周期上
  - 全量判据全绿；换地图与走动的既有用例期望值未改
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: test-driven-development
- Required checks: CK-006, CK-007, CK-008, CK-009, CK-010
- Required evidence:
  - 坐标领域服务的成员清单
  - 角色实例成员清单（证明没有坐标成员）
  - 坐标读取点与改变点的全部枚举，逐个说明去向
  - 落库接缝未被改动的证据
  - 全量判据输出

## Execution Result 1

### 1. 新增的坐标领域服务

**接口** `Server/src/Dfo.Server.Abstractions/Characters/ICharacterPositionDomainService.cs`（新增，BD-003）

成员清单（5 个）：

| 成员 | 签名 | 语义 |
|---|---|---|
| `Place` | `void Place(SelectedSessionIdentity session, CharacterPositionState position)` | 入场时记下角色站在哪，覆盖此前任何记录；带会话身份，使被顶掉的一代不能拆掉顶替它的那一代 |
| `Find` | `CharacterPositionState? Find(CharacterId characterId)` | 按角色回答坐标；没人在玩这个角色时答 null |
| `CrossInto` | `CharacterPositionState CrossInto(SelectedSessionIdentity session, byte townId, byte areaId, short x, short y)` | 报告一次跨区（也是跨城）移动，答移动后的坐标；朝向保留——该命令不带朝向 |
| `StepTo` | `CharacterPositionState StepTo(SelectedSessionIdentity session, short x, short y, byte direction)` | 报告一次区内走动，答移动后的坐标；城/区保留 |
| `ForgetExact` | `bool ForgetExact(SelectedSessionIdentity session)` | 精确遗忘本会话的坐标；记录不属于本会话时返回 false |

`CrossInto` / `StepTo` 在「站在那里的不是这个会话」时抛 `InvalidOperationException`。这**不是新增的收窄**：它就是原先 `ISelectedSessionRuntimeDirectory.ReplaceExact` 的既有语义（`SelectedSessionRuntimeDirectory.cs:22-29`，代不匹配或条目不存在即 `throw new InvalidOperationException("Selected runtime generation is no longer current.")`）。整体写回改成报告移动之后，那条拒绝分支必须跟着搬过来，否则一条被顶掉的会话发来的走动帧会去移动顶替它的角色。

**实现** `Server/src/Dfo.Server.Application/Characters/CharacterPositionDomainService.cs`（新增，BD-004）

- `internal sealed`，私有 `Dictionary<CharacterId, Standing>` 加单把 `object sync` 锁，形状对齐 `SessionPresenceDomainService` 那一族（对外只回答领域问题，字典是实现细节；没有任何「取出来—改一改—塞回去」的成员）。
- 用整锁而不是 `ConcurrentDictionary`：报告移动要「读—改—存」三步合一，两条同时到达的报告若各自基于自己读到的值，第二条会存回第一条已经离开的位置。这正是 `SelectedSessionRuntimeDirectory` 的整锁在为整份快照做的事。
- 放在 `Application/Characters/` 而非 `Application/Sessions/`：BD-004 两处都允许，选前者是因为 AGENTS.md「架构设计 §1」把 `Sessions/` 写死为「**四个**领域服务」，把第五个塞进那个目录会让那句话当场变成假话。放 `Characters/` 则落在该目录既有的「入场后修正」这一类里，不使任何既有文字失效。

**接线** `Server/src/Dfo.Server.Application/ApplicationModule.cs:219-223`：`TryAddSingleton<ICharacterPositionDomainService, CharacterPositionDomainService>()`，紧邻 `ICharacterSkillPointDomainService`（同为「按标识符问一个角色自己的数」）。

### 2. 逐文件改动与理由

| 文件 | 改动 | 理由 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/Characters/ICharacterPositionDomainService.cs` | **新增** | BD-003：坐标的主人 |
| `Server/src/Dfo.Server.Application/Characters/CharacterPositionDomainService.cs` | **新增** | BD-004：持有活坐标 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 加 5 行注册 | 组合根接线 |
| `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs` | `SelectCharacterSessionEntryService` 加 `positions` 参数，`Publish` 里 `positions.Place(session, result.Character.Position)`（:188）；`SelectedSessionCleanupService` 加 `positions` 参数，落库读源改为 `positions.Find(session.CharacterId)`（:74 与 :82），清理链加 `Run(() => positions.ForgetExact(session))`（:89） | 服务要有人喂、有人清。放在与 `runtimes.Publish` 和 `presence.Admit` **同一段语句**里，使三者的存活区间完全相同；清理里 `ForgetExact` 紧挨 `runtimes.RemoveExact` |
| `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs` | `SetUserAreaHandler` 与 `SetUserPositionHandler` 各加 `ICharacterPositionDomainService positions`；4 处读改经服务、2 处整体写回改为报告移动；两段 XML remarks 改写 | BD-005 主体 |
| `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownReturnProjection.cs` | `Frames` 增加 `CharacterPositionState position` 形参，删掉 `var position = result.Character.Position` | 它是 `*Projection`（无依赖、无 I/O），不能自己去问服务，只能由调用方把答案递进来 |
| `Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs` | `BackToVillageHandler` 加 `positions`，`position` 改由 `positions.Find` 在门里取出（:190），`Frames(...)` 多传一个实参 | 6 处读之一 |
| `Server/src/Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs` | `EplpCommandHandler` 同上（:111） | **超出 Permitted files 清单的唯一一处，且不可回避**：`TownReturnProjection.Frames` 有两个调用方，另一个就在这里。改了投影签名而不改它，编译不过；不改投影签名，`TownReturnProjection.cs:34` 那处读就无法脱离聚合。Task 的 Owned boundary 写的是「那 8 处调用点」，这一处正是其中 `TownReturnProjection` 那处读的第二个供料方 |
| `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterPositionTests.cs` | **新增**，8 条用例 | CK-009 与 BD-015 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/SessionGenerationOwnershipTests.cs` | 7 处构造多传一个 `CharacterPositionDomainService`；`Cleanup_PersistsRuntimePositionOnlyWhenEntryPolicyAllowsIt` 的 arrange 里补 `positions.Place(session, result.Character.Position)` | 构造器多了一个参数。**期望值一个字未改**：`[InlineData(true, 1)]` 与 `[InlineData(false, 0)]` 以及 `Assert.Equal(expectedWrites, persistence.Writes)` 原样保留，补的是 arrange（生产路径上入场把两者放在同一段语句里，测试里要照做） |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs` | 3 处构造多传一个坐标服务替身（`StandingWhereTheyAre` 与 `StandingNowhere`）；`Entry()` 的快照坐标改成第三个明显不同的值 | **期望值一个字未改**：`StandingTownId=3`、`StandingAreaId=6`、`StandingX=902`、`StandingY=388`、`SpawnAreaState=7` 与全部 `Assert` 原样。`Entry()` 快照坐标改为 `(9,8,77,66,4)` 是**加强**而非迁就：现在 spawn、快照副本、真实站位三者互不相同，投影若退回去读快照会像读 spawn 一样全盘失败，而不是碰巧对上 |

### 3. 坐标的全部读取点与全部改变点（CK-007）

先说枚举方法与差异：任务给的锚点是「6 处读 / 2 处整体写回」，我用 `grep -rn "\.Position" --include=*.cs Server/` 与 `grep -rn "CharacterPositionState" --include=*.cs Server/src/` 自己复核了一遍。**Protocol 层的 6 处读与 2 处写回与给定锚点完全一致，一处不多一处不少。** 差异只有一点：`Server/` 全域下还有 3 处生产侧坐标接触点不在那 8 处里（落库读、入场解析读、入场构造写），任务把落库通路单列了，另两处属于入场序列。下面全部列出，一个不漏。

#### 3.1 读取点

| # | 改前位置 | 改前取自 | 改后去向 |
|---|---|---|---|
| R1 | `Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs:192` `var position = result.Character.Position` | 会话运行态聚合 | `positions.Find(session.CharacterId)`，见 `:190` |
| R2 | `Protocol/Game/TownProjection/TownProjectionSlice.cs:246` `result.Character.Position.TownId`（拒绝日志） | 会话运行态聚合 | `standing.TownId`，`standing` 来自 `positions.Find`（`:235`），见 `:257` |
| R3 | 同上 `:247` `.AreaId` | 会话运行态聚合 | `standing.AreaId`，见 `:258` |
| R4 | 同上 `:264` `var previous = result.Character.Position` | 会话运行态聚合 | 并入同一个 `standing`（一次 `Find` 同时供拒绝日志与成功日志的 From），见 `:285-286` |
| R5 | 同上 `:351` `var moved = result.Character.Position with {...}` | 会话运行态聚合 | **整处消失**。`SetUserPositionHandler` 现在完全不读坐标：它只报告一次走动（`:359`），城/区与其余字段由服务保留 |
| R6 | `Protocol/Game/TownProjection/TownReturnProjection.cs:34` `var position = result.Character.Position` | 会话运行态聚合 | 改为形参 `CharacterPositionState position`（`:37`）。两个供料方各自问服务：`DungeonSelectionSlice.cs:190` 与 `LeaveDungeonSlice.cs:111` |
| R7 | `Application/Sessions/SelectCharacterLifecycleServices.cs:73` `runtime.Character.Position`（落库） | 会话运行态聚合 | `positions.Find(session.CharacterId)`（`:74`），落库时机与位置一字未动，见第 4 节 |
| R8 | `Application/Characters/SelectCharacterPreparationService.cs:149` `character.Position.TownId` | **不是**会话运行态聚合，是刚从仓储载入的 `ActiveCharacterState` | **未改**。这是入场序列内部解析落点用的、对已载入角色记录的读。（如实记：今天这条路上 `CharacterRosterService.FindByAccountAndSlotAsync` 给 `ActiveCharacterState` 填的是占位 `CharacterPositionState(0,0,0,0,5)`（`CharacterRosterService.cs:148`），所以它实际恒为 0——这是先于本任务存在的缺口，且在本任务边界之外，不动） |

**没有第九处。** 改后 `Server/src/` 下 `.Position` 成员访问只剩 4 处：`CharacterPositionDomainService.cs:44` 与 `:86`（服务自身内部）、`SelectCharacterPreparationService.cs:149`（R8）、`SelectCharacterLifecycleServices.cs:188`（入场交接，见 W3）。`Protocol/` 下一处不剩。

#### 3.2 改变点

| # | 改前位置 | 改前做法 | 改后去向 |
|---|---|---|---|
| W1 | `TownProjectionSlice.cs:274` `runtimeSessions.ReplaceExact(session, result with { Character = result.Character with { Position = moved } })`（换地图） | 整体写回聚合 | `positions.CrossInto(session, message.TownId, message.AreaId, message.X, message.Y)`（`:277`） |
| W2 | `TownProjectionSlice.cs:359` 同形状（走动） | 整体写回聚合 | `positions.StepTo(session, message.X, message.Y, message.Direction)`（`:359`） |
| W3 | — | — | **新增** `positions.Place(session, result.Character.Position)`（`SelectCharacterLifecycleServices.cs:188`）：入场把落点交给服务。交出去的是 placement 已经定好、入场写库已经落过的那个值，不是从运行态目录里读回来的 |
| W4 | — | — | **新增** `positions.ForgetExact(session)`（`SelectCharacterLifecycleServices.cs:89`）：会话拆除时精确遗忘，紧挨 `runtimes.RemoveExact` |
| W5 | `SelectCharacterPreparationService.cs:54` `Character = prepared.Character with { Position = resolved }` | 入场构造 | **未改**。它写的是**尚未发布**的 `SelectCharacterResult`，不是「整体写回运行态聚合」；它的产物经 W3 流入服务 |
| W6 | `CharacterStateAccess.cs:134` 与 `:147` `store.SessionIn(...).WritePosition(owned, position)` | 落库 | **未改**，见第 4 节 |
| W7 | `CharacterRosterService.cs:148` `new CharacterPositionState(0,0,0,0,5)` | 行到 `ActiveCharacterState` 映射时的占位构造 | **未改**，先于本任务存在，不是活坐标的改变 |

**结论（CK-007）**：W1 与 W2 两处整体写回已全部消失；R1 至 R7 七处读全部改经领域服务；`ReplaceExact` 在 `Server/src/` 里只剩 `SynchronizeSelectedCharacterQuestsCommandHandler` 与 `ReviveSelectedCharacterPetCommandHandler` 两个与坐标无关的调用方。无一处从会话运行态聚合取坐标，也无一处靠整体写回它来改坐标。

#### 3.3 顺带修正的行为（如实记，不是收窄）

改前 `SetUserAreaHandler` 在三次 `await` 之前抓下 `result` 快照，之后用**那个快照**拼出 `moved` 再整体写回——期间若有一帧 0x0023 改了朝向，这次写回会把它盖掉（丢失更新）。改后 `CrossInto` 作用在服务当下持有的记录上，朝向保留的是真实的当下值。单线程与全部既有用例下逐值相同；只有在这个竞态窗口里结果不同，且新行为是把原来的丢失更新修掉。

### 4. 落库接缝未被改动的证据

`git diff --stat -- Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs Server/src/Dfo.Server.Application/Sessions/SelectedSessionRuntimeDirectory.cs Server/src/Dfo.Server.Data/Characters/CharacterModels.cs`
→ **输出为空**。即 `ISelectedSessionPositionPersistence` 契约与其 remarks、`SelectedSessionPositionPersistence` 实现、两个消费方之一的 `SelectCharacterPreparationService`、运行态目录、`CharacterPositionState` 记录，全部零改动。

唯一被碰的消费方是 `SelectedSessionCleanupService`，改的只有**取哪个值**，没改**什么时候取**：

- 落库仍在 `CleanupAsync` 内、仍在 `expertStores.CloseExact` 等一串 `Run(...)` 之前、仍用 `CancellationToken.None`、仍经 `positionPersistence.SingleOrDefault()` 且为 `null` 时仍是 `failures.Add(...)`。
- 触发条件仍由 `runtimes.TryGet(...) && runtime is not null && shouldPersistPosition` 决定，`shouldPersistPosition` 标志仍挂在 `ISelectedSessionRuntimeDirectory` 上，一字未动。
- 新增的第四个合取项 `positions.Find(session.CharacterId) is { } standing` 不改变判定集合：坐标记录与运行态条目由 `SelectCharacterSessionEntryService.Publish` 在同一段语句里一起放入，由 `CleanupAsync` 同一次运行一起移除，两者的存在性同真同假。
- 这条时机之所以不能动，理由在 `ISelectedSessionPositionPersistence` 的 remarks 里：会话结束可能发生在别的用例正在跑的时候（新选角把旧的顶掉，旧的拆除就发生在新选角里面），它必须**加入**那个用例而不是在旁边另起一个数据库连接——第一个连接正持着视图，第二个写下去，第一个就再也写不进了。

CK-010 的证据面：`Cleanup_PersistsRuntimePositionOnlyWhenEntryPolicyAllowsIt` 两条 InlineData 期望值未改且全绿。

### 5. 变异验证

**做法**：把 `CharacterPositionDomainService.Move` 改成丢弃报告（`_ = report; return standing.Position;`），即「服务忽略移动」。

**命令**：`PVF_ARCHIVE_PATH=... dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter "FullyQualifiedName~CharacterPositionTests"`

**结果**：`失败 3，通过 5，总计 8`。CK-009 那条用例的全限定名与失败输出：

```
Dfo.Server.Application.Tests.Characters.CharacterPositionTests.A_step_reported_is_what_the_next_question_answers [FAIL]
  Assert.Equal() Failure: Values differ
  Expected: CharacterPositionState { TownId = 3, AreaId = 6, X = 410, Y = 275, Direction = 5 }
  Actual:   CharacterPositionState { TownId = 3, AreaId = 6, X = 902, Y = 388, Direction = 1 }
  at ...\Server\tests\Dfo.Server.Application.Tests\Characters\CharacterPositionTests.cs:line 60
```

同一变异另外拖红两条（一并记名，说明判据不是只有一条独苗）：

```
Dfo.Server.Application.Tests.Characters.CharacterPositionTests.A_crossing_reported_is_what_the_next_question_answers [FAIL]
  Expected: CharacterPositionState { TownId = 5, AreaId = 2, X = 100, Y = 200, Direction = 1 }
  Actual:   CharacterPositionState { TownId = 3, AreaId = 6, X = 902, Y = 388, Direction = 1 }

Dfo.Server.Application.Tests.Characters.CharacterPositionTests.A_step_after_a_crossing_is_taken_in_the_area_crossed_into [FAIL]
  Expected: CharacterPositionState { TownId = 5, AreaId = 2, X = 150, Y = 210, Direction = 3 }
  Actual:   CharacterPositionState { TownId = 3, AreaId = 6, X = 902, Y = 388, Direction = 1 }
```

**还原**：从备份覆盖回原文件；`grep -rn "MUTATION" Server/src Server/tests --include=*.cs` 无输出；全量重跑 `2038 通过 / 2 跳过 / 0 失败`。工作区无残留（变异期间只动过这一个文件，备份放在 scratchpad，不在仓库内）。

### 6. 判据输出与计数归因

**CK-006** `dotnet build Server/86JP.Server.slnx --nologo`

```
已成功生成。
    0 个警告
    0 个错误
```

**CK-007** 见第 3 节。无一处从会话运行态聚合取坐标或靠整体写回它来改坐标。

**CK-008** 角色实例成员清单：

`ICharacter`（`Abstractions/Characters/ICharacter.cs`）共 8 个成员——`Id`、`CanUseAsync`、`IdentityAsync`、`ProgressAsync`、`RemainingSkillPointsAsync`、`GoldAsync`、`AddGoldAsync`、`TrySpendGoldAsync`。

`PlayingCharacter`（`Application/Characters/PlayingCharacter.cs`）公开成员与上表一一对应，另有一个私有 `ItemUsePlace Place()`（`:212`）——它答的是「村里还是地下城」这一枚举，来源是 `presence.Find(characterId)?.StandingInTown`，**不是坐标**，且不在 `ICharacter` 上。

**没有任何成员回答坐标。** 构造器参数也未回退：`characterId, judgement, cooldowns, presence, carriedGold, goldRules, characters, skillPoints`，TASK-01 摘掉的 `ISelectedSessionDomainService` 与 `ISelectedSessionRuntimeDirectory` 两个会话聚合依赖没有被加回来（见 `ApplicationModule.cs` diff 中那两行仍为删除态）。

**CK-009** 新增 `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterPositionTests.cs`，8 条：

- `A_character_nobody_is_playing_is_standing_nowhere`
- `A_character_is_standing_where_entry_placed_them`
- `A_step_reported_is_what_the_next_question_answers`（**CK-009 本体**）
- `A_step_leaves_the_town_and_the_area_where_they_were`
- `A_crossing_reported_is_what_the_next_question_answers`
- `A_step_after_a_crossing_is_taken_in_the_area_crossed_into`
- `A_displaced_session_cannot_move_the_one_that_replaced_it`
- `A_displaced_session_forgets_nothing`

全绿。

**CK-010** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

```
Dfo.Server.Networking.Tests      失败 0，通过  52，跳过 0
Dfo.Server.Protocol.Tests        失败 0，通过 498，跳过 0
Dfo.Pvf.Tests                    失败 0，通过  60，跳过 0
Dfo.Server.Host.Tests            失败 0，通过   6，跳过 0
Dfo.Server.Persistence.Tests     失败 0，通过 256，跳过 0
Dfo.Server.Infrastructure.Tests  失败 0，通过 223，跳过 2
Dfo.Server.Application.Tests     失败 0，通过 943，跳过 0
```

合计 **2038 通过 / 2 跳过 / 0 失败**。

**计数归因**：基线 2030 到 2038，**加 8，全部来自新增的 `CharacterPositionTests` 那 8 条**。没有任何既有用例被删除、跳过或改写期望值。两个既知偶发失败本轮未出现（`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` 与 `Dfo.Server.Networking.Tests` 均全绿）。

`CommandHandlerArchitectureTests` 全绿且未被触碰：本任务没有改动任何 `ICommandHandler` 实现的构造器——坐标服务只注入到四个消息 handler（`SetUserAreaHandler`、`SetUserPositionHandler`、`BackToVillageHandler`、`EplpCommandHandler`）与两个领域服务里，而且它本身就是判据允许的 `*DomainService` 形状。

### 7. 一处诚实记录：超出 Permitted files 的改动

`Server/src/Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs` 不在 Task 的 Permitted files 清单上，但被改了。理由已在第 2 节表格里写明：`TownReturnProjection.Frames` 有两个调用方，清单只列了其中一个；要让 `TownReturnProjection.cs:34` 那处读脱离聚合，投影签名必须变，两个调用方就都必须跟着变。改动限于「加一个构造器参数、门里多一个合取项、调用多传一个实参」，没有任何别的行为改动。Owned boundary 写的是「那 8 处调用点」，这一处正是其中一处读的第二个供料方。

`AGENTS.md` 未改动，并且核对过它没有被本次改动证伪：

- 「会话快照里仍然留着坐标、在场、世界内容、背包投影与账号侧状态」——`SelectCharacterResult.Character.Position` 字段仍在快照里（只是不再被读作活坐标），且这句话里「在场」早已有 `ISessionPresenceDomainService` 作主人却仍列在其中，说明这句话说的是「快照里还带着副本」而不是「这些还没主人」。仍成立。
- 「`Sessions/`（在场、选中会话、PvP 大厅、专家商店**四个**领域服务）」——正是为了不把这个数字改成假的，新服务放进了 `Application/Characters/`。

## Executor Self-check 1

### 对照 Required checks

| 判据 | 期望 | 实测 | 结论 |
|---|---|---|---|
| CK-006 | `dotnet build Server/86JP.Server.slnx --nologo` 零警告零错误 | `0 个警告 / 0 个错误` | 通过 |
| CK-007 | 坐标读取点与改变点枚举：无一处从会话运行态聚合取坐标或靠整体写回它来改坐标 | 第 3 节全量枚举：R1 至 R7 全部改经 `ICharacterPositionDomainService`；W1 与 W2 两处整体写回消失；`Protocol/` 下 `.Position` 成员访问 0 处；R8 是入场对已载入角色记录的读、非聚合 | 通过 |
| CK-008 | 角色实例成员清单：没有回答坐标的成员 | `ICharacter` 8 个成员逐个列出，无坐标成员；`PlayingCharacter` 唯一涉「地点」的是私有 `Place()`，答 `ItemUsePlace` 枚举、取自在场记录 | 通过 |
| CK-009 | 新增用例：报告一次移动后再问坐标，答案是移动后的值 | `CharacterPositionTests.A_step_reported_is_what_the_next_question_answers` 等 8 条全绿；变异（服务忽略移动）下该条按名报红，输出已引 | 通过 |
| CK-010 | 全量 `dotnet test` 通过；换地图与走动的既有用例期望值未改 | 2038 / 2 / 0；换地图与走动**没有既有 handler 级用例**（只有 `TownProjectionCodecTests` 的解包器用例，本次零改动），相邻的 `LeaveDungeonTests` 与 `Cleanup_PersistsRuntimePositionOnlyWhenEntryPolicyAllowsIt` 期望值逐条保留 | 通过 |

关于 CK-010 后半句的如实说明：我核过 `grep -rn "SetUserAreaHandler\|SetUserPositionHandler" Server/tests/` **零命中**——换地图与走动这两个 handler 今天没有任何用例。所以「期望值未改」在这两个协议上是空真；实际受本次改动波及并被我逐条核对过期望值未改的，是 `LeaveDungeonTests`（断言常量全保留）与 `SessionGenerationOwnershipTests` 的落库那条 Theory（两组 InlineData 原样）。

### 对照 Task 契约的 Acceptance conditions

| Acceptance condition | 结论 | 证据 |
|---|---|---|
| 坐标可从该领域服务问出；角色实例没有回答坐标的成员 | 满足 | `ICharacterPositionDomainService.Find(CharacterId)`；第 6 节 CK-008 的成员清单 |
| 坐标的全部读取点与全部改变点中，无一处从会话运行态聚合取坐标或靠整体写回它来改坐标 | 满足 | 第 3.1 与 3.2 节全量枚举加改后 grep 复核 |
| **落库时机不变**：仍挂在会话生命周期上 | 满足 | 第 4 节：接缝五个文件 `git diff` 为空；`CleanupAsync` 里位置、条件、取消令牌、失败收集方式全部原样，只换了取值来源 |
| 全量判据全绿；换地图与走动的既有用例期望值未改 | 满足 | 第 6 节 CK-010 与上面的空真说明 |

### 对照其他硬约束

| 约束 | 结论 | 说明 |
|---|---|---|
| 行为逐值不变 | 满足（一处已披露的竞态修正） | 唯一非逐值相同处见第 3.3 节：换地图在三次 await 期间与走动帧竞态时，旧代码会用陈旧快照盖掉朝向（丢失更新），新代码作用在当下记录上。单线程与全部既有用例下逐值相同 |
| 既有测试期望值与断言不得为迁就实现而改写 | 满足 | 只改了 arrange 与构造器实参；`LeaveDungeonTests` 的 `Entry()` 快照坐标改为第三个值是**加强判别力**（让「退回读快照」也全盘失败），断言常量与断言本身一字未动 |
| 不新增协议帧，不改任何帧的字节布局 | 满足 | 未碰任何 `*PayloadCodec`、`*Encoder`、`*Decoder`、`Messages.cs`；`ProtocolModule` 零改动 |
| 分层 Data 到 Abstractions 到 Application 到 Protocol 到 Host | 满足 | 新接口在 Abstractions（只依赖 Data 的 `CharacterId`、`CharacterPositionState`、`SelectedSessionIdentity`），实现在 Application，消费在 Protocol。方向单向 |
| 切片架构：切片内不跨协议引用 | 满足 | 未新增任何跨切片引用。`TownReturnProjection` 被 `DungeonSelection` 与 `DungeonClear` 引用是先于本任务的既有形态；本次只是给它多传一个值 |
| `CommandHandlerArchitectureTests` 继续通过，谓词与豁免清单不得放宽 | 满足 | 该文件零改动，全量绿；本任务未改任何命令处理器的构造器 |
| TreatWarningsAsErrors 与 CS9113 | 满足 | 0 警告 0 错误；新加的 `positions` 参数在每个类里都被读到 |
| 不建 Git 检查点、不提交、不 stash | 满足 | 全程无 `git commit`、`git stash`、`git tag`；仅用 `git status` 与 `git diff` 只读查看 |
| 不动 TASK-01 成果，不把会话聚合依赖加回角色实例 | 满足 | `PlayingCharacter.cs`、`ICharacter.cs`、`CharacterTables.cs`、`CharacterEntryTables.cs` 本次零改动；`ApplicationModule.cs` 里 TASK-01 删掉的那两行依赖仍为删除态 |

### 已知的、留在边界之外的事实（如实说）

1. `SelectCharacterPreparationService.cs:149` 读的 `character.Position.TownId` 恒为 0，因为 `CharacterRosterService.FindByAccountAndSlotAsync` 给 `ActiveCharacterState` 填的是占位坐标（`CharacterRosterService.cs:148`），而 `ICharacterDomainStore.ReadPositionAsync` 在 `Server/src/` 下**没有任何生产调用方**。也就是说「回到你离开的那座城」的**还原半边**今天仍然接不上——但这先于本任务存在，且落在本任务边界之外（Permitted files 不含 `CharacterRosterService` 与 `CharacterDomainService`）。本次改动没有让它变好也没有让它变坏。
2. `SelectCharacterResult.Character.Position` 字段仍在（`Dfo.Server.Data` 不在允许改动范围内）。它现在只在一个地方被读：入场发布时把值交给坐标服务（W3）。此后不再被任何人读作活坐标。彻底摘除该字段属于「连着会话快照其余部分一起拆」的后续工作。

## Independent Quick Review 1

- **Verdict: `changes_required`**
- 审查的契约版本：Contract revision 1，无修订（amendment）。
- Owned diff 身份：`git log -1` = `427331bcb07f60121c97920287ac341c9cb14dee`，stash 空，无提交、无检查点。工作区同时含已被接受的 TASK-01 成果；本轮只审 TASK-02 归属的 10 个文件：
  - 新增 `Server/src/Dfo.Server.Abstractions/Characters/ICharacterPositionDomainService.cs`、`Server/src/Dfo.Server.Application/Characters/CharacterPositionDomainService.cs`、`Server/tests/Dfo.Server.Application.Tests/Characters/CharacterPositionTests.cs`
  - 修改 `ApplicationModule.cs`、`Sessions/SelectCharacterLifecycleServices.cs`、`Protocol/Game/TownProjection/TownProjectionSlice.cs`、`Protocol/Game/TownProjection/TownReturnProjection.cs`、`Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs`、`Protocol/Game/DungeonClear/LeaveDungeonSlice.cs`、`tests/.../SessionGenerationOwnershipTests.cs`、`tests/.../LeaveDungeonTests.cs`

### 本轮自行执行的证据

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| CK-006 | `dotnet build Server/86JP.Server.slnx --nologo` | `0 个警告 / 0 个错误` |
| CK-010 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 52+498+60+6+256+223+943 = **2038 通过 / 2 跳过 / 0 失败**，与执行者报数逐项一致；两个既知偶发本轮未出现 |
| 计数归因 | 检视测试侧 diff | 除新增 `CharacterPositionTests`（8 个 `[Fact]`）外，无 `[Fact]` / `[Theory]` / `[InlineData]` 的增删；+8 成立 |
| 变异验证 | 备份后把 `CharacterPositionDomainService.Move` 的写回改为丢弃报告，跑 `--filter "FullyQualifiedName~CharacterPositionTests"` | **失败 3，通过 5，总计 8**，三条报红的全限定名与 Expected/Actual 与执行者所引**逐字一致**（含 `X=410,Y=275,Direction=5` / `X=902,Y=388,Direction=1`）。还原后文件 md5 与变异前相同（`484563ff…`），`git status` 与变异前逐行一致，`Dfo.Server.Application.Tests` 943 全绿，无残留 |
| 落库接缝 | `git diff --stat --` 五个接缝文件 | 输出为空，执行者所述属实 |
| `Protocol/` 坐标读点 | `grep -rn "\.Position" Server/src/Dfo.Server.Protocol/` | **0 处**，属实。`Server/src/` 全域仅剩 4 处与坐标有关（服务内 2 处、`SelectCharacterPreparationService.cs:149`、`SelectCharacterLifecycleServices.cs:188`），其余命中为 `stream.Position` 与 `PositionOperands` |
| 角色实例 | 读 `ICharacter.cs` 全部成员 | 无回答坐标的成员 |
| 命名与仓规 | `AGENTS.md:93, 124-125, 420-425` | `*DomainService` 用法正确，未误用 `store`；`Sessions/`「四个领域服务」因新服务落在 `Application/Characters/` 而仍然成立；`AGENTS.md` 本身零改动 |
| 硬约束 | diff 逐行 | 未触任何 `*Codec` / `Encoder` / `Decoder` / `Messages.cs` / `ProtocolModule`；`TownProjectionSlice.cs` 的改动全在 handler 构造器与方法体，解包器段落零改动；`CommandHandlerArchitectureTests.cs` diff 为空且全绿；分层方向单向；未新增跨切片引用 |

### 首要问题的独立判定：披露的行为差异不构成 RQ-012 违反

**结论：该交错不可达，因此新旧两版在任何可达执行下逐值相同，不落在 RQ-012 的禁止范围内。**

1. **可达性——已实测否定。** `NetworkPlugins.cs` 的两处 `OnTcpReceived` 是 `ITcpReceivedPlugin` 回调。我在 scratchpad 搭了独立探针（TouchSocket 4.3.1，与本仓中央包管理同版本），用**与本仓相同的 `ITcpReceivedPlugin.OnTcpReceived` 插件形状**起 `TcpService`，处理器内 `await Task.Delay(1000)`，客户端在处理中途分两次继续发送：

   ```
     115ms ENTER 'A' inFlight=1
    1119ms EXIT  'A'
    1121ms ENTER 'BC' inFlight=1
    2123ms EXIT  'BC'
   EVENTS=2 OVERLAP_COUNT=0   RESULT=SERIAL
   ```

   处理中途到达的 B、C 被**扣住**，直到 A 的 `Task` 完成才投递。即：同一连接上，TouchSocket 在上一帧的 `OnTcpReceived` 返回的 `Task` 完成之前不会投递下一帧。本仓的适配器 `ChannelFrameDataHandlingAdapter` / `GameClientFrameDataHandlingAdapter` 均派生自 `CustomFixedHeaderDataHandlingAdapter`（单流适配器，一个缓冲区内多帧逐个 `await`），不改变这一点，与 `docs/NetworkingFrameProtocol.md:353`「按顺序逐帧投递」相符。**单连接下两帧不可能并发进入处理。**

2. **跨连接也不可达。** 两个连接必然是两个 `SelectedSessionIdentity`（`ConnectionId` 不同），而 `SelectedSessionRuntimeDirectory` 按 `AccountId` 索引并做 `entry.Session == session` 的代次相等判定（`SelectedSessionRuntimeDirectory.cs:36-52`；`SelectedSessionIdentity` 是 `record`，`==` 为值相等）。同一账号只有一代能通过 `TryGet`，被顶掉的那代在两个 handler 的门口就被挡下。同一角色被两个账号持有在花名册模型下不成立。

3. **旧行为在假想交错下确实是覆盖。** 走动帧的写回走 `ReplaceExact`，它只替换 `Entry.Result`，`Entry.Session` 原样保留（`SelectedSessionRuntimeDirectory.cs:26`），**代次不变**，所以换地图那次基于陈旧快照的写回不会被代次校验挡住——执行者对旧行为的描述正确，只是这个交错不可达。

4. **新旧差异只此一点。** 逐点核对了坐标的状态转移：旧世界里快照 `Position` 的改变者只有入场发布、`SetUserAreaHandler`（保留 Direction）、`SetUserPositionHandler`（保留 Town/Area）；其余五个 `ReplaceExact` 调用点（见发现 F2）都只改任务/经验字段，`Character.Position` 原样带过。新世界里 `Place` / `CrossInto` / `StepTo` 的转移函数与之逐字对应。`CrossInto` / `StepTo` 的 `InvalidOperationException` 确是 `ReplaceExact` 既有语义的搬迁，不是新增收窄。

5. **可观察性备注（不影响结论）。** 若该交错可达，差异**会被客户端观察到**：`SetUserAreaHandler` 发出的 `AreaUserPresence` 携带 `moved.Direction`（`TownProjectionSlice.cs:310`）。所以「旧行为只是内部丢失更新、不是承诺给客户端的值」这条辩护路线**不成立**；判可接受的唯一依据是上面的不可达性。

**是否需要写进交付记载：需要，但要改写。** 现在 §3.3 与 Self-check 里「只有在这个竞态窗口里结果不同」的写法，会让所有者以为存在一个真实的行为差异窗口。应改为「该窗口在 TouchSocket 的每连接串行投递下不可达，故新旧逐值相同；`CrossInto` 顺带使这处写回不再依赖陈旧快照」，并附上串行性的依据。

### 第二个问题的独立判定：超出 Permitted files 的改动理由成立

- `TownReturnProjection.Frames` 确有且仅有两个调用方：`DungeonSelectionSlice.cs:217` 与 `LeaveDungeonSlice.cs:128`。
- `TownReturnProjection` 是 `internal static class`，无构造器、无依赖；注入服务需要把它变成实例服务，并推翻它「投影不查任何东西」的既定形态（该形态写在它自己的 remarks 里，并被本次新增的第三段 `<para>` 明说）。改签名是最小改法。
- 该文件的改动**纯属签名跟随，无夹带**：`using` 一行、构造器加一个 `ICharacterPositionDomainService`、门里加一个合取项与三行注释、`Frames(...)` 多传一个实参。日志格式串、`LeaveState`、run 结束逻辑全部原样。
- 结论：理由成立，不作为发现。

### 阻断发现

**F1 — `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs:194-196` 的 `<summary>` 被本次改动变成了假话，且与它自己的 `<remarks>` 自相矛盾。**

```
/// <summary>
/// Moves the published session to the requested area and tells the client where it now is.
/// </summary>
```

改后 `SetUserAreaHandler` **不再移动 published session**：`runtimeSessions.ReplaceExact(...)` 已从该 handler 删除，运行态快照在这条路径上不再被写。同一段注释里本次**新加**的段落已经把这件事说清楚了——「The runtime session is still consulted, for the channel's spawn template and to establish that this connection has a published session at all. **Where the character stands is no longer part of what it is asked.**」（`:205-207`）。两句直接对立。

这不是苛求：执行者在同一文件里为**同一个理由**把姊妹 handler 的 summary 从「Keeps the runtime session's position current」改成了「Keeps the character's live position current」（`:321-322`），也把本段 remarks 的首句「The new position is written into the runtime session」改掉了——唯独漏了这一行 summary。属于「改动使既有注释失真」，且落在 Permitted files 之内。

**修法**：把 summary 改成陈述实际发生的事，例如「Moves the character to the requested area and tells the client where they now are.」（一行）。

**F2 — Execution Result §3.2 的结论段包含一条可证伪的枚举。**

原文：

> `ReplaceExact` 在 `Server/src/` 里只剩 `SynchronizeSelectedCharacterQuestsCommandHandler` 与 `ReviveSelectedCharacterPetCommandHandler` 两个与坐标无关的调用方。

实测 `grep -rn "ReplaceExact(" Server/src --include=*.cs`：改后 `Server/src/` 下的**生产调用点有六处**：

```
Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:195  (ReplaceRuntimeExact 包装)
Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs:752
Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs:490
Server/src/Dfo.Server.Protocol/Game/QuestAcceptance/AcceptQuestHandler.cs:106
Server/src/Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestHandler.cs:144
Server/src/Dfo.Server.Protocol/Game/TutorialLevelUp/TutorialLevelUpSlice.cs:241
```

而被点名的那两个 handler **一次都没有直接调用 `ReplaceExact`**：它们经 `sessionEntry.ReplaceRuntimeExact`（`SelectCharacterPhaseCommands.cs:257` 与 `:285`）间接抵达。这句话既漏了五个直接调用方，又把两个间接调用方说成了全部。

**这一条有实质后果，不只是措辞**：那五处 Protocol 侧的整体写回，在本次改动后会把一个**永久陈旧**的 `Character.Position` 原样带回运行态快照——旧世界里它至少还被走动/换地图刷新，新世界里它自入场起再不更新。执行者在「已知的事实 2」里只说该字段「此后不再被任何人读作活坐标」，而这句错误的枚举恰好掩盖了「谁还在把它写回去」。这正是本 Spec 前一个 Task 反复出现的「一句比代码强的陈述」，且是交付记载中所有者会直接依赖的一句。

（说明：CK-007 的验收本身**仍然成立**——那五处都不改变坐标，只是把它原样搬运，所以「无一处靠整体写回聚合来改坐标」为真。F2 要求订正的是记载，不是代码。）

**修法**：把该句改为对六个调用点的如实枚举，并点明它们都只搬运不改变坐标，以及快照 `Position` 自此永久陈旧这一后果。

### 非阻断观察（不要求本轮处理）

1. `SetUserAreaHandler` / `SetUserPositionHandler` 今天**没有任何 handler 级用例**（`grep -rn "SetUserAreaHandler\|SetUserPositionHandler" Server/tests/` 零命中），执行者已如实声明 CK-010 后半句在这两个协议上是空真。后果是：改动最大的这两个 handler 的 RQ-012 等价性完全靠阅读而非判据支撑。本 Task 的 Required checks 未要求补，记录以供最终评审。
2. `SessionGenerationOwnershipTests.Cleanup_PersistsRuntimePositionOnlyWhenEntryPolicyAllowsIt` 的 arrange 补了 `positions.Place(...)` 之后，该用例不再单独守住「落库与否只由 `shouldPersistPosition` 决定」——现在还额外要求坐标记录存在。这是设计的真实后果，arrange 忠实于生产路径（`runtimes.Publish` 只有 `SelectCharacterSessionEntryService.Publish` 一个调用方，与 `positions.Place` 同段），断言与两组 `InlineData` 一字未改，可接受。
3. `ISessionPresence.cs:102` 的 `ISelectedSessionPositionPersistence` summary 仍写「save the last exact **runtime** position」。落库取值来源已改为坐标领域服务，这句轻微漂移。该文件在 Permitted files 之外且其 diff 必须保持为空（落库时机不变的证据），故不要求本轮动它，留给后续 Task。
4. `LeaveDungeonTests.Entry()` 的快照坐标改为第三个互异值确属**加强判别力**（spawn / 陈旧快照 / 真实站位三者互不相同），断言常量与断言本身一字未改，配套改写的 XML remarks 与新事实相符。
5. 新服务按 `CharacterId` 索引，而运行态目录按 `AccountId` 索引；`Find` 自身不做代次校验。在本仓「一账号一选中角色、角色不跨账号」的模型下两者一一对应，且三个消费点都在 `runtimes.TryGet` 的代次门之后才问，故不构成缺陷。记录以供最终评审关注该不对称。

### 要求的整改

- **F1**：修正 `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs:195` 的 summary（一行）。
- **F2**：订正 Execution Result §3.2 结论段中 `ReplaceExact` 调用方的枚举，并把「陈旧快照字段仍被五处整体写回搬运」这一后果写进记载。
- **附带**：按上文「首要问题」第 5 点改写 §3.3 与 Self-check 中关于竞态窗口的表述，把「存在一个结果不同的窗口」改为「该窗口不可达」，并给出串行投递的依据。

本 verdict 只证明这一个 Task Unit 的有限缺陷检查结果，不构成完整变更评审，也不替代接受前的新鲜验证。

## Controller Disposition 1 — task-rework（回应 Independent Quick Review 1）

评审的两条阻断成立，控制器另把它的一条非阻断观察升为必办项，共三件。

### 竞态的判定：采纳评审的结论与依据，不采纳执行者的辩护

评审用独立探针实测了本仓插件形状下的帧分发为串行，并以会话代次门排除了跨连接路线，据此判定
该交错不可达、新旧逐值相同。控制器采纳这个结论。

但**执行者给出的辩护理由是错的**，必须一并订正：它称旧行为「只是内部丢失更新」，而评审指出
`AreaUserPresence` 携带 `moved.Direction`，那次覆盖若真发生就是客户端可见的。可接受的唯一依据是
不可达性，不是「差异不可见」。记载里凡依赖后一种说法的地方都要改写——一份用来支撑验收判断的
记录里出现一句比事实强的论断，正是本 Spec 反复在治的那类错。

### F1、F2 按评审所述整改

### 升为必办：两个改动最大的 handler 至今零 handler 级判据

评审把它列为非阻断观察。控制器升为必办，理由是上一轮 multi-spec 任务里同型的缺口被实证过后果：
`MonsterDeathHandler` 整段回退到读陈值，**全量判据仍然全绿**，因为那条路径零判据。

本任务的处境同构且更值得担心——F2 已经查明会话聚合里的 `Character.Position` 自入场起**永久陈旧**，
所以把 handler 退回去读它不会抛异常、不会崩，只会安静地报出一个入场时的坐标。CK-009 那条变异
证明的是**服务**会红，不是 **handler 接对了**。

补两条 handler 级判据，使「把 handler 退回从会话运行态聚合取坐标」这一变异会红。

## Execution Result 2 — 定向返工（回应 Independent Quick Review 1 与 Controller Disposition 1）

本轮只做控制器裁定的三件事与一处措辞订正。Execution Result 1、Executor Self-check 1、Independent
Quick Review 1、Controller Disposition 1 四个既有段落一字未改；订正写在本段落里并逐条指名它订正的
是旧段落的哪一处。已被接受的代码改动（领域服务及其接线、8 处调用点、投影签名与两个调用方、
既有测试的构造跟随、落库时机）本轮零改动。

代码侧本轮只动两个文件：`TownProjectionSlice.cs` 改一行注释，新增一个测试文件。

### 1. F1 — 自相矛盾的那行 `<summary>`

`Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs:194-196`

改前（本次改动已使其为假，且与同段 remarks 对立）：

```
/// Moves the published session to the requested area and tells the client where it now is.
```

改后：

```
/// Moves the character to the requested area and tells the client where they now are.
```

`ReplaceExact` 已从该 handler 删除，published session 不再被这条路径写；被移动的是角色（其位置由
`ICharacterPositionDomainService` 持有）。同段 remarks 的 `:205-207`「Where the character stands is no
longer part of what it is asked」现在与 summary 一致。该文件本轮 diff 恰为 1 行增 1 行删（`git diff
--stat` 由 35/35 变为 37/37），无夹带。

### 2. 订正 Execution Result 1 的 §3.2 — `ReplaceExact` 调用方的枚举

**原文（撤回）**：

> `ReplaceExact` 在 `Server/src/` 里只剩 `SynchronizeSelectedCharacterQuestsCommandHandler` 与
> `ReviveSelectedCharacterPetCommandHandler` 两个与坐标无关的调用方。

**这句话两处都错**，评审的实测成立。我用 `grep -rn "ReplaceExact(" Server/src --include=*.cs` 与
`grep -rn "ReplaceRuntimeExact(" Server/src --include=*.cs` 重新枚举，逐个读了上下文：

改后 `Server/src/` 下 `ReplaceExact` 的**生产调用点是六处**（另有两处不是调用：`ISessionPresence.cs:93`
的接口声明、`SelectedSessionRuntimeDirectory.cs:21` 的实现本体）：

| # | 位置 | 这一次写回携带的改变 | 是否改变 `Character.Position` |
|---|---|---|---|
| 1 | `Application/Sessions/SelectCharacterLifecycleServices.cs:195` | `ReplaceRuntimeExact` 的实现本体 | 否 |
| 2 | `Protocol/Game/DungeonClear/DungeonClearSlice.cs:752` | 清关奖励后的等级/经验/技能点镜像与刷新过的任务提供列表 | 否 |
| 3 | `Protocol/Game/MonsterDeath/MonsterDeathSlice.cs:490` | 击杀奖励后的同一组字段 | 否 |
| 4 | `Protocol/Game/QuestAcceptance/AcceptQuestHandler.cs:106` | 接下任务后刷新的提供列表 | 否 |
| 5 | `Protocol/Game/QuestCompletion/FinishQuestHandler.cs:144` | 交任务后的经验与刷新的提供列表 | 否 |
| 6 | `Protocol/Game/TutorialLevelUp/TutorialLevelUpSlice.cs:241` | 教程升级后的等级/经验与刷新的提供列表 | 否 |

被我点名的那两个 handler **一次都没有直接调用 `ReplaceExact`**：
`SynchronizeSelectedCharacterQuestsCommandHandler`（`SelectCharacterPhaseCommands.cs:257`）与
`ReviveSelectedCharacterPetCommandHandler`（`:285`）调用的是 `sessionEntry.ReplaceRuntimeExact`，经上表
第 1 行间接抵达。原文既漏了五个直接调用方，又把两个间接调用方写成了全部。

#### 这条错误掩盖的实质后果（评审要求写进记载，如实写）

改动之后，会话运行态快照里的 `Character.Position` **自入场起对该会话永久陈旧**：

- 它只被写入一次——`SelectCharacterPreparationService.cs:54` 把 placement 解析出的落点写进**尚未
  发布**的结果，随后 `SelectCharacterSessionEntryService.Publish` 发布它并在同一段语句里
  `positions.Place(session, result.Character.Position)`（`SelectCharacterLifecycleServices.cs:188`）。
- 此后再无写入方。本轮复核：`grep -rn "Position\s*=" Server/src --include=*.cs` 的生产侧命中只剩
  `CharacterPositionDomainService.cs:87`（服务自己的记录）与 `SelectCharacterPreparationService.cs:54`
  （上面那次入场构造），其余命中是 `stream.Position`、`*Position` 常量名与枚举值。
- 旧世界里这个字段至少还被 0x0023 与 0x0024 每帧刷新；新世界里两条路径都改成了向服务报告，
  于是它停在入场那一刻。

**上表 2 到 6 那五处，每次运行都会把这个永久陈旧的值原样搬回新的快照。** 我在「已知的事实 2」里
只说它「此后不再被任何人读作活坐标」，而那句错误的枚举恰好挡住了「谁还在把它写回去」这个问题。

**CK-007 的验收本身仍然成立**，理由是这六处**只搬运不改变坐标**，而不是「它们不存在」：

- `Server/src/Dfo.Server.Protocol/` 下 `.Position` 成员访问本轮复核为 **0 处**——这五处 Protocol 侧写回
  的表达式里没有任何一处读或写 `Character.Position`，它们改的是 `Character.Progression`、
  `CharacterEntry.Public.Extras`、`Session.Adjacent` 一类字段，`Character.Position` 由 `with` 表达式原样带过。
- 因此「坐标的全部读取点与全部改变点中，无一处从会话运行态聚合取坐标或靠整体写回它来改坐标」
  为真：这六处既不取坐标，也不改坐标。

彻底摘掉这个字段属于「连着会话快照其余部分一起拆」的后续工作，落在本 Task 边界之外
（`Dfo.Server.Data` 不在 Permitted files 内）。本段的作用是让所有者看见它现在是什么状态。

### 3. 订正 Execution Result 1 的 §3.3 与 Executor Self-check 1「行为逐值不变」那一行

**撤回的说法**：§3.3 的「期间若有一帧 0x0023 改了朝向，这次写回会把它盖掉（丢失更新）」「只有在
这个竞态窗口里结果不同，且新行为是把原来的丢失更新修掉」，以及 Self-check 1 表格里「唯一非逐值
相同处……旧代码会用陈旧快照盖掉朝向（丢失更新）」。**这条辩护不成立。**

不成立的理由，我复核并确认评审是对的：`SetUserAreaHandler` 发出的 `AreaUserPresence` 第四个字段
就是 `moved.Direction`（`TownProjectionSlice.cs:311-313`），`TownProjectionPayloadCodec.EncodeAreaUsers`
把它写进 NOTI 0x0018 每个用户记录的第 7 个字节。那次覆盖若真发生，被盖掉的朝向会直接出现在
客户端收到的帧里。它**不是**「内部丢失更新」，把它说成不可见是一句比事实强的论断。

**订正后的表述**：新旧两版在任何**可达**执行下逐值相同，因为那个交错不可达。

不可达性的依据来自 Independent Quick Review 1，**不是我做的**：评审在 scratchpad 搭了独立探针
（TouchSocket 4.3.1，与本仓中央包管理同版本，与本仓相同的 `ITcpReceivedPlugin.OnTcpReceived` 插件
形状），实测同一连接上一帧的 `OnTcpReceived` 返回的 `Task` 完成之前不会投递下一帧
（`EVENTS=2 OVERLAP_COUNT=0 RESULT=SERIAL`），并以会话代次门排除了跨连接路线。我没有重做这个
实验，本轮也不声称做过。

我复核的是该结论所依赖的、可以在仓内查证的三处事实：

1. 两个适配器 `ChannelFrameDataHandlingAdapter` 与 `GameClientFrameDataHandlingAdapter` 确实都派生自
   `CustomFixedHeaderDataHandlingAdapter<...>`（`Server/src/Dfo.Server.Networking/Framing/FrameDataHandlingAdapters.cs:5`
   与 `:17`），即单流逐帧适配器，不会把一个缓冲区里的多帧并发投递。
2. 跨连接路线被代次门挡住：`SelectedSessionRuntimeDirectory` 按 `AccountId` 索引并在
   `TryGet`（`:44`）与 `ReplaceExact`（`:24`）两处都做 `entry.Session == session` 判定，`SelectedSessionIdentity`
   是 `record`（值相等），所以同一账号只有一代能通过两个 handler 门口的 `runtimes.TryGet`。
3. 旧行为在那个假想交错下**确实**是覆盖：走动帧的 `ReplaceExact` 只替换 `Entry.Result` 而保留
   `Entry.Session`（`SelectedSessionRuntimeDirectory.cs:26`），代次不变，所以基于陈旧快照的换地图写回
   不会被代次校验挡下。这一点我原来描述正确，错的只是「不可见」那半句。

顺带说明一处：`CrossInto` 使这次写回不再依赖三次 `await` 之前抓下的快照，朝向取自服务当下持有的
记录。这是设计的自然结果，不作为「修掉了一个可观察缺陷」记账——按上面的依据，那个缺陷不可达。

### 4. 新增两条 handler 级判据（控制器升级项）

`Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs`（新增）

放在 `Game/TownProjection/` 下与既有 `TownProjectionCodecTests.cs` 并列，按该项目既有的组织方式：
解包器判据一个文件、handler 判据一个文件，stand-in 以嵌套 `private sealed class` 写在类内，形状照
`Game/DungeonClear/LeaveDungeonTests.cs`（同项目里最近的 handler 判据）。

两条用例：

| 用例 | 守住的是什么 |
|---|---|
| `A_crossing_moves_the_character_the_service_owns` | `SetUserAreaHandler`：报告一次跨区之后，服务答的是移动后的坐标，且发给客户端的 `AreaUserPresence` 带的是**服务持有的朝向**而不是快照里入场时的朝向 |
| `A_step_moves_the_character_the_service_owns` | `SetUserPositionHandler`：报告一次走动之后，服务答的是移动后的坐标，且城/区是**服务当下所在**的那一对而不是快照里入场时的那一对；0x0016 仍原样回抄客户端报的 x/y/朝向/trailer |

**判别力从哪来（评审与控制器指名要求的那一点）**：arrange 让「服务里的当下坐标」与「聚合里的入场
陈值」在**每一个字段上都不同**，并且用的是真的 `CharacterPositionDomainService` 而不是替身——被测的
正是 handler 有没有接到真正持有坐标的那个东西。

- 聚合（`Entry()` 里 `SelectCharacterResult.Character.Position`）：`(TownId 9, AreaId 8, X 77, Y 66, Direction 4)`
- 服务（`Standing()`）：先 `Place` 上面那个入场值，再 `CrossInto(3, 6, 500, 400)`、`StepTo(902, 388, 7)`，
  于是服务当下持有 `(3, 6, 902, 388, 7)`——这也正是生产里两者分叉的方式：入场放一次，之后每帧报告
  移动，而快照那份副本再没人回头改。
- 跨区请求要去 `(5, 2, 100, 200)`；走动请求报 `(410, 275, 朝向 5, trailer 104)`。

三组值互不相同，所以 handler 若退回读聚合，报出来的是可辨认的错值，不会碰巧对上。

`OneRuntimeSession` 这个 stand-in 的 `ReplaceExact` **接受**写回而不是抛 `NotSupportedException`，是刻意的：
抛异常的替身会让退回读聚合的 handler 以「替身抛了」而不是「坐标错了」的方式变红，那证明不了
handler 用的是哪一份坐标。这一点写在该 stand-in 的 remarks 里。

### 5. 变异验证（两条判据分别报红）

变异对象只有 `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs` 一个文件，
备份放在 scratchpad（仓库外）。两次变异**分别**施加、分别验证、各自还原。

**变异 A**：`SetUserAreaHandler` 退回从会话运行态聚合取坐标——把
`var moved = positions.CrossInto(session, message.TownId, message.AreaId, message.X, message.Y);` 换回
`var moved = result.Character.Position with { TownId = ..., AreaId = ..., X = ..., Y = ... };` 加
`runtimeSessions.ReplaceExact(session, result with { Character = result.Character with { Position = moved } });`
（`positions` 仍被拒绝日志那处 `Find` 读到，所以不触发 CS9113，变异版照样零警告编译）。

命令：`PVF_ARCHIVE_PATH=... dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --nologo --filter "FullyQualifiedName~TownProjectionHandlerTests"`

结果 `失败 1，通过 1，总计 2`：

```
Dfo.Server.Protocol.Tests.Game.TownProjection.TownProjectionHandlerTests.A_crossing_moves_the_character_the_service_owns [FAIL]
  Assert.Equal() Failure: Values differ
  Expected: 7
  Actual:   4
  at ...\Server\tests\Dfo.Server.Protocol.Tests\Game\TownProjection\TownProjectionHandlerTests.cs:line 79
```

`7` 是服务持有的当下朝向，`4` 是聚合里入场时的朝向——正是「安静地报出入场时的值」这个后果本身，
出现在发给客户端的 `AreaUserPresence` 里。`A_step_...` 在此变异下仍通过，说明两条判据各守各的。

**变异 B**（先从备份还原变异 A）：`SetUserPositionHandler` 退回从会话运行态聚合取坐标——把
`_ = positions.StepTo(session, message.X, message.Y, message.Direction);` 换回
`var moved = result.Character.Position with { X = ..., Y = ..., Direction = ... };` 加同样的 `ReplaceExact`
（加 `_ = positions;` 保持零警告）。

结果 `失败 1，通过 1，总计 2`：

```
Dfo.Server.Protocol.Tests.Game.TownProjection.TownProjectionHandlerTests.A_step_moves_the_character_the_service_owns [FAIL]
  Assert.Equal() Failure: Values differ
  Expected: CharacterPositionState { TownId = 3, AreaId = 6, X = 410, Y = 275, Direction = 5 }
  Actual:   CharacterPositionState { TownId = 3, AreaId = 6, X = 902, Y = 388, Direction = 7 }
  at ...\Server\tests\Dfo.Server.Protocol.Tests\Game\TownProjection\TownProjectionHandlerTests.cs:line 114
```

服务完全没被告知这次走动，答的还是走动之前的位置——不抛异常、不崩，只是安静地答旧值。
`A_crossing_...` 在此变异下仍通过。

**还原与残留检查**：从备份覆盖回 `TownProjectionSlice.cs`，md5 与变异前一致
（`741c1bf51cfa4285c20f96ec82a6ae20`）；`grep -rn "MUTATION" Server/src Server/tests --include=*.cs` 无输出；
`git status --short` 与变异前逐行一致（唯一新增的未跟踪文件是本轮的 `TownProjectionHandlerTests.cs`）。
备份文件在 scratchpad，不在仓库内。

### 6. 判据全部重跑

**CK-006** `dotnet build Server/86JP.Server.slnx --nologo`

```
已成功生成。
    0 个警告
    0 个错误
```

**CK-010** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

```
Dfo.Server.Networking.Tests      失败 0，通过  52，跳过 0
Dfo.Server.Protocol.Tests        失败 0，通过 500，跳过 0
Dfo.Pvf.Tests                    失败 0，通过  60，跳过 0
Dfo.Server.Host.Tests            失败 0，通过   6，跳过 0
Dfo.Server.Persistence.Tests     失败 0，通过 256，跳过 0
Dfo.Server.Infrastructure.Tests  失败 0，通过 223，跳过 2
Dfo.Server.Application.Tests     失败 0，通过 943，跳过 0
```

合计 **2040 通过 / 2 跳过 / 0 失败**。

**计数归因**：上一轮 2038 到本轮 2040，**加 2，全部来自新增的 `TownProjectionHandlerTests` 两条**
（`Dfo.Server.Protocol.Tests` 由 498 变 500，其余六个项目逐项不变）。没有任何既有用例被删除、跳过
或改写期望值；本轮测试侧只新增一个文件，既有测试文件零改动。

两个既知偶发本轮均未出现：`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` 所在的
`Dfo.Server.Persistence.Tests` 256 全绿；`Dfo.Server.Networking.Tests` 52 全绿。

**CK-007 / CK-008 / CK-009** 的证据面本轮未被改动（代码侧只动了一行注释与新增一个测试文件）；
CK-007 的枚举按第 2 节订正后仍然成立，理由见该节。

## Executor Self-check 2

### 对照控制器裁定的三件事与一处措辞订正

| 要求 | 结论 | 证据 |
|---|---|---|
| F1 改掉 `TownProjectionSlice.cs:195` 自相矛盾的 summary | 完成 | 第 1 节；该文件本轮 diff 恰 1 增 1 删 |
| F2 重新枚举 `ReplaceExact` 并写出实质后果 | 完成 | 第 2 节：六处直接调用点逐个列出并读过上下文；两个被误点名的 handler 更正为经 `ReplaceRuntimeExact` 间接抵达；快照 `Position` 自入场起永久陈旧、被那五处原样搬运，如实写出；CK-007 仍成立的理由（只搬运不改变）一并说明 |
| 升级项：补两条 handler 级判据，使「退回读聚合」变异会红 | 完成 | 第 4 节与第 5 节：两条用例、两次分别的变异、两条各自按全限定名报红并附 Expected/Actual |
| 措辞订正：凡依赖「差异不可见 / 只是内部丢失更新」的地方改写为依据不可达性 | 完成 | 第 3 节：撤回原辩护并说明它为何不成立（`AreaUserPresence` 带 `moved.Direction`），改述为「该交错不可达」；不可达性的探针实验明确记为评审所做，我只复核了三处仓内可查证事实 |

### 对照本轮硬约束

| 约束 | 结论 | 说明 |
|---|---|---|
| 不推倒重来，不动已被接受的部分 | 满足 | 代码侧本轮只改 `TownProjectionSlice.cs` 一行注释 + 新增一个测试文件。领域服务、接线、8 处调用点、投影签名与两个调用方、`CharacterPositionTests`、`SessionGenerationOwnershipTests`、`LeaveDungeonTests`、落库接缝五个文件本轮全部零改动 |
| 行为逐值不变 | 满足 | 本轮唯一的生产文件改动是一行 XML 注释，不参与编译产物的行为 |
| 既有测试的期望值与断言不得为迁就实现而改写 | 满足 | 本轮测试侧只新增一个文件，既有测试文件一个字节未改（`git diff` 中测试文件的改动与上一轮完全相同） |
| 不新增协议帧，不改任何帧的字节布局 | 满足 | 未碰任何 `*PayloadCodec` / `*Encoder` / `*Decoder` / `Messages.cs` / `ProtocolModule`；新判据只读现成的通知记录 |
| 不建 Git 检查点、不提交、不 stash | 满足 | 全程只用 `git status` / `git diff` / `git show HEAD:<path>` 只读查看；无 `commit`、`stash`、`tag`、`checkout` |
| 变异后还原、工作区无残留 | 满足 | 第 5 节：md5 复原一致、`MUTATION` grep 无输出、`git status --short` 与变异前逐行一致 |
| 不改 `sdd-execution.yaml`，不写 verdict | 满足 | 未打开该文件；本段不含任何 verdict |
| 分层与切片架构 | 满足 | 新判据在 `Dfo.Server.Protocol.Tests` 内，用该项目已有的 `InternalsVisibleTo`（`Dfo.Server.Application.csproj:14`）取用 `CharacterPositionDomainService`；未新增任何项目引用，未新增跨切片生产引用 |
| `TreatWarningsAsErrors` 与 CS9113 | 满足 | 全量构建 0 警告 0 错误；两次变异版本也各自零警告编译（否则变异跑不起来） |

### 本轮如实记录的两件事

1. 评审 F2 指出的「五处整体写回把永久陈旧的 `Character.Position` 原样搬回快照」是**代码的真实状态**，
   本轮只订正了记载，**没有改代码**。控制器裁定的三件事里没有要求改它，且改它要碰 `Dfo.Server.Data`
   与五个切片文件，全部在本 Task 的 Permitted files 之外。它是留给「拆会话快照」那类后续工作的。
2. 新增的两条 handler 判据用了真的 `CharacterPositionDomainService`（经 `InternalsVisibleTo`）而不是替身。
   好处是它们证明 handler 接到了真正持有坐标的那个东西；代价是这两条判据同时依赖服务的实现，
   服务变红时它们也会红。这是有意的取舍：控制器要求的判据是「handler 接对了」，而不是「服务对了」，
   用真服务反而让「接错了」这件事无处可藏——CK-009 的那 8 条仍然单独守着服务本身。

## Independent Quick Review 2

- **Verdict: `changes_required`**
- 审查的契约版本：Contract revision 1，无修订（amendment）。裁定依据为 Independent Quick Review 1
  的 F1/F2 与 Controller Disposition 1 的三件必办项。
- 审查者身份：全新上下文，未参与实现，也不是 Independent Quick Review 1 的作者。未采信任何自陈结论，
  下列每一项均自行执行。
- Owned diff 身份：`git log -1` = `427331bcb07f60121c97920287ac341c9cb14dee`，stash 空，无提交、无检查点。
- 范围：按控制器指定，只核本轮返工的四件事与判据计数。上一轮已独立复核通过的部分（落库时机五文件
  diff 为空、`Protocol/` 下 `.Position` 归零、既有测试期望值未改、命名、分层、CK-009 变异、竞态可达性探针）
  未重审；其中偶然复核到的两项在下文如实记录，均无异常。工作区内 TASK-01 的成果不在本次范围。

### 本轮自行执行的证据

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| CK-006 | `dotnet build Server/86JP.Server.slnx --nologo` | `0 个警告 / 0 个错误`（变异还原后复跑同样 0/0） |
| CK-010 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 52+500+60+256+6+223+943 = **2040 通过 / 2 跳过 / 0 失败**，与执行者报数**逐项目一致**；两个既知偶发本轮均未出现 |
| 计数归因 | 新文件 `grep -c "\[Fact\]"` = **2**；`Dfo.Server.Protocol.Tests` 498→500，其余六项目（52 / 60 / 256 / 6 / 223 / 943）与 Independent Quick Review 1 独立复核过的数字**逐项相同** | +2 全部来自两条新 handler 判据，属实 |
| 既有测试本轮零改动 | 全部变更文件 `stat` mtime | 既有测试文件 mtime 全部 ≤ `02:00:39`，新判据文件为 `02:33:45`，Task 文件为 `02:39:40`。本轮工作窗口内无既有测试文件被触碰，声称属实 |
| 未建检查点 / 未提交 / 未 stash | `git log -1`、`git stash list` | HEAD 仍 `427331bc`，stash 空 |
| 未改 `sdd-execution.yaml` | `stat` mtime + 读 TASK-02 条目 | mtime `01:45:47`，早于本轮工作窗口（02:33 起）；TASK-02 `state: in_progress`，执行者未写 verdict、未改任务控制状态 |
| Task 文件既有段落未被改写 | 通读七个标题段 | Contract / Result 1 / Self-check 1 / Review 1 / Disposition 1 五个既有段落内容与控制器转述一致，无改写痕迹；订正一律以「撤回 + 指名旧段落哪一处」的方式写在 Execution Result 2 内 |

### 1. F1 已治好，且该文件其余注释未失真——但夹带了一处未申报的改动（见 F3）

- `TownProjectionSlice.cs:194-196` 现为「Moves the character to the requested area and tells the client
  where they now are.」，不再声称移动 published session，与同段 `:205-207`「Where the character stands is
  no longer part of what it is asked」一致。**F1 已消除。**
- 我复核了 `:205-207` 那句本身是否为真：`runtimeSessions` 在该 handler 里确实仍被用于 `TryGet` 门与
  `result.World.Channel.Spawn`（`:292`），且不再被问坐标。属实。
- 该文件**其余**注释逐条核过，无因本次改动失真者：
  - `:322`「Keeps the character's live position current so the write at cleanup records where they
    actually stood」——落库读源已改为 `positions.Find`，`StepTo` 更新的正是该记录，为真。
  - `:326-328`「town/area 由服务保留，both reports get it」——`CharacterPositionDomainService.StepTo`
    的 `with` 只改 X/Y/Direction，`CrossInto` 的 `with` 只改 Town/Area/X/Y 而保留 Direction，两条规则
    确实都落在服务里，为真。
  - `:275-276`、`:357-358`、`:228-231` 三处新增行内注释与代码一致。
  - `:38-44`、`:57`、`:66-70`、`:111` 为协议事实描述，不受本次改动影响。
- **但**：该文件本轮的实际改动不是执行者声称的「恰 1 增 1 删 / 无夹带」。见下方 **F3**。

### 2. F2 的订正准确无误

- 自行 `grep -rn "ReplaceExact(" Server/src --include=*.cs`：命中 8 条，其中**生产调用点恰为六处**，
  与执行者第 2 节表格**逐行一致**（`SelectCharacterLifecycleServices.cs:195`、`DungeonClearSlice.cs:752`、
  `MonsterDeathSlice.cs:490`、`AcceptQuestHandler.cs:106`、`FinishQuestHandler.cs:144`、
  `TutorialLevelUpSlice.cs:241`）；另两条确非调用（`ISessionPresence.cs:93` 接口声明、
  `SelectedSessionRuntimeDirectory.cs:21` 实现本体）。
- 被原文误点名的两个 handler 确经 `sessionEntry.ReplaceRuntimeExact` 间接抵达
  （`SelectCharacterPhaseCommands.cs:257` 与 `:285`）。订正属实。
- **「快照 `Character.Position` 自入场起永久陈旧」这一最强论断，我自行验证为真**，且执行者的论证方式
  （穷举写入方而非逐点阅读）比逐点阅读更强：
  - `grep -rn "Position\s*=" Server/src --include=*.cs` 的生产侧命中只有
    `CharacterPositionDomainService.cs:87`（服务自己的记录，非快照）与
    `SelectCharacterPreparationService.cs:54`（入场构造），其余全是 `stream.Position`、常量名与枚举值。
  - `SelectCharacterPreparationService.cs:52-54` 写的是**尚未发布**的 `placed`；
    `SelectCharacterLifecycleServices.cs:187-188` 先 `runtimes.Publish` 再 `positions.Place`，此后
    `SelectCharacterResult.Character.Position` 再无写入方。
  - `QuestOfferRefreshDomainService` 内无任何 `Position =`，故那五处经 `RefreshAsync` 的写回不可能改动它。
  - 因此那五处每次都把入场那一刻的值原样搬回，论断成立。**这不是一句比代码强的陈述。**
- **CK-007 仍成立的理由已说清且为真**：`grep -rn "\.Position" Server/src/Dfo.Server.Protocol --include=*.cs`
  **零命中**（exit 1），六处写回既不取坐标也不改坐标，只是搬运。验收条款「无一处从会话运行态聚合
  取坐标或靠整体写回它来改坐标」为真。

### 3. 两条新 handler 判据确实挡住了它们声称挡住的东西

- **arrange 逐字段互异，属实**：聚合陈值 `(9, 8, 77, 66, 4)`（`Entry()`，:185-186）对服务当下值
  `(3, 6, 902, 388, 7)`（`Standing()`，:163-173），五个字段**无一相同**；跨区请求 `(5, 2, 100, 200)` 与
  走动请求 `(410, 275, 5, trailer 104)` 与前两者也都不同。三组值互不相同，退回读聚合会产生可辨认的
  错值而非碰巧对上。
- **`OneRuntimeSession.ReplaceExact` 接受写回而非抛异常，这个设计成立。** 判断理由：本轮两个变异是
  「读聚合 + 写回聚合」成对退回（这正是改前的真实代码形状）。若替身在 `ReplaceExact` 抛异常，变异版
  会在写回那一步先炸，测试以替身的异常变红，而**读的是哪一份坐标这个问题被掩盖**。
  改成接受写回之后，判别力完全来自对可观察输出的断言——`AreaUserPresence.Direction` 与
  `positions.Find(...)`——这两者都不受替身是否接受写回影响。执行者的理由正确，且这是更强而非更宽松的
  选择。该理由已写在替身的 remarks（:232-236）里。
- **两次变异我自行重做，分别施加、分别验证、各自还原**（备份在 scratchpad，仓库外）：

  **变异 A** — `SetUserAreaHandler` 退回 `var moved = result.Character.Position with { TownId, AreaId, X, Y }`
  加 `runtimeSessions.ReplaceExact(...)`。结果 `失败 1，通过 1，总计 2`：

  ```
  Dfo.Server.Protocol.Tests.Game.TownProjection.TownProjectionHandlerTests.A_crossing_moves_the_character_the_service_owns [FAIL]
    Assert.Equal() Failure: Values differ
    Expected: 7
    Actual:   4
    at ...\Server\tests\Dfo.Server.Protocol.Tests\Game\TownProjection\TownProjectionHandlerTests.cs:line 79
  ```

  `A_step_moves_the_character_the_service_owns` 在此变异下**仍通过**。

  **变异 B** — `SetUserPositionHandler` 退回同一形状。结果 `失败 1，通过 1，总计 2`：

  ```
  Dfo.Server.Protocol.Tests.Game.TownProjection.TownProjectionHandlerTests.A_step_moves_the_character_the_service_owns [FAIL]
    Assert.Equal() Failure: Values differ
    Expected: CharacterPositionState { TownId = 3, AreaId = 6, X = 410, Y = 275, Direction = 5 }
    Actual:   CharacterPositionState { TownId = 3, AreaId = 6, X = 902, Y = 388, Direction = 7 }
    at ...\Server\tests\Dfo.Server.Protocol.Tests\Game\TownProjection\TownProjectionHandlerTests.cs:line 114
  ```

  `A_crossing_moves_the_character_the_service_owns` 在此变异下**仍通过**。

  两条全限定名与 Expected/Actual 与执行者所报**逐字一致**；两条判据各守各的，控制器要求的
  「分别报红」成立。变异版均零警告编译（`TreatWarningsAsErrors` 下能跑起来即证）。
- **还原后校验**：`md5sum` = `741c1bf51cfa4285c20f96ec82a6ae20`（与变异前一致，也与执行者所报一致）；
  `git diff --numstat` 回到 `37 37`；`git status --porcelain` 与变异前**逐行相同**（`diff` 输出为空）；
  `grep -rn "MUTATION" Server/src Server/tests --include=*.cs` 无输出；全量判据复跑 2040/2/0 全绿。

### 4. 措辞订正到位，且探针实验的归属正确

- 全文 `grep` 依赖「差异不可见 / 只是内部丢失更新」的说法，只有两处：`§3.3`（:111）与
  `Executor Self-check 1` 表格「行为逐值不变」那一行（:244）。Execution Result 2 §3（:484-486）
  **逐处指名撤回**，并改述为「新旧两版在任何**可达**执行下逐值相同，因为那个交错不可达」。既有段落
  按规则未被改写，订正以撤回方式写在新段落内，处理方式正确。
- 撤回的理由为真：`AreaUserPresence` 的第四个字段确为 `moved.Direction`（`TownProjectionSlice.cs:313`；
  执行者引「:311-313」，区间含真实行，无实质偏差）。
- **归属正确，未把评审的实验写成自己做的**：:495 明写「不可达性的依据来自 Independent Quick Review 1，
  **不是我做的**」，:498-500 明写「我没有重做这个实验，本轮也不声称做过」。
- 执行者声称自行复核的三处仓内事实，我逐条验证**全部为真**：
  1. `ChannelFrameDataHandlingAdapter`（`FrameDataHandlingAdapters.cs:5`）与
     `GameClientFrameDataHandlingAdapter`（`:17`）确实都派生自 `CustomFixedHeaderDataHandlingAdapter<...>`。
  2. 代次门：`ReplaceExact`（`SelectedSessionRuntimeDirectory.cs:24`）与 `TryGet`（`:44`）都做
     `entry.Session` 相等判定。
  3. `ReplaceExact` 的 `entry with { Result = result }`（`:26`）确实保留 `Entry.Session`，代次不变。

### 阻断发现

**F3 — `TownProjectionSlice.cs` 被夹带了一处未申报的改动：文件头新增 UTF-8 BOM，直接证伪本轮
「恰 1 增 1 删 / 无夹带」与「本轮唯一的生产文件改动是一行 XML 注释」两处声称。**

证据：

```
$ git show HEAD:Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs | head -c 8 | xxd
00000000: 7573 696e 6720 5379                      using Sy
$ head -c 8 Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs | xxd
00000000: efbb bf75 7369 6e67                      ...using
```

`git diff` 的首个 hunk 即为此（`-using System.Buffers.Binary;` / `+<BOM>using System.Buffers.Binary;`）。

- 该文件在 HEAD 处**没有** BOM，工作区版本**有**。这一对增删与 summary 那一行的增删无关。
- 实测 `git diff --numstat` = **`37  37`**。执行者自己在 Execution Result 2 §1 里写的
  「`git diff --stat` 由 35/35 变为 37/37」已经是 **+2/-2**，与紧邻的「该文件本轮 diff **恰为 1 行增 1 行删**
  ……**无夹带**」自相矛盾——**否证所需的数字就在同一句话里**。Executor Self-check 2「行为逐值不变」一行
  的「本轮唯一的生产文件改动是一行 XML 注释」同样为假。
- 这不是仓库惯例：本次全部 30 个变更/新增 `.cs` 文件中，**只有这一个**带 BOM；`Server/src` 下 433 个
  受版本控制的 `.cs` 里只有 5 个带 BOM。该 BOM 是编辑动作的产物，不是有意的格式决定，也未在任何
  段落里申报。
- **为何判为阻断而非观察**：本 Task 的返工轮次正是为治「一句比代码强的陈述」而设（F2 即此类），
  控制器在 Disposition 1 里写明「一份用来支撑验收判断的记录里出现一句比事实强的论断，正是本 Spec
  反复在治的那类错」。本轮在修 F2 的同一份记载里出现了同类的第二例，且执行者手里已有否证数字。
  按快审判据「self-check 或 result 与实际 diff / 证据矛盾」「owned diff 含无关改动」，该条成立。
- 行为影响：**无**。C# 编译器接受 BOM，构建 0 警告 0 错误，判据 2040 全绿。这是记载准确性与 diff
  清洁度的缺陷，不是功能缺陷。

**修法**（三字节，可当场验证）：去掉该文件头部的 `EF BB BF`，随后 `git diff --numstat` 应变为
**`36  36`**——恰好比上一轮的 `35 35` 多一对，届时「本轮恰 1 增 1 删」这句声称才真正成立。同时把
Execution Result 2 §1 与 Executor Self-check 2 中相应的两句改为与该数字相符的表述。

### 非阻断观察

1. **对「五处搬运永久陈旧 `Character.Position`、本轮只订正记载不改代码」这一处置的意见：同意留到后续，
   且不影响本 Task 的验收。** 理由：
   - **验收层面**：本 Task 的验收条款是「全部读取点与全部改变点中，无一处从会话运行态聚合取坐标或
     靠整体写回它来改坐标」。我已独立验证该条为真（Protocol 下 `.Position` 零命中；六处写回既不取也
     不改坐标）。陈旧字段被**搬运**不落在该条款的禁止范围内，验收不受影响。
   - **边界层面**：摘掉该字段要改 `Dfo.Server.Data` 的快照记录定义与五个切片文件，全部在 Permitted
     files 之外。本 Task 已有一处**有充分理由**的越界（`LeaveDungeonSlice.cs`，签名跟随，Review 1 已判理由
     成立）；为一个无行为影响的清理再越界六个文件，代价与风险都高于收益，也与本 Spec 的边界纪律相悖。
   - **风险层面**：陈旧字段的真实危险是「有人把它当活坐标读」。该风险本轮已被显著压低——生产侧读者
     归零，且控制器要求补的两条 handler 判据经我实测确实能让「退回去读它」变红（第 3 节），这正是
     Disposition 1 引用的 `MonsterDeathHandler` 前例所缺的那道防线。
   - **但建议加一道账**：请所有者不要让这件事只以 TASK-02 交付记载里一段文字的形式存在。它应当被登记
     为一条明确的技术债并指名承接者——本 Spec 的 TASK-03 至 TASK-06 正在逐块拆这个会话快照，是它的
     天然归宿。同一处还可以一并了结 Review 1 非阻断观察 3 提到的 `ISessionPresence.cs:102`
     「save the last exact **runtime** position」那句已漂移的 summary。留在记载里而不入债务台账，
     下一个读到这个字段的人仍会以为它是活的。
2. Review 1 的非阻断观察 5（新服务按 `CharacterId` 索引而运行态目录按 `AccountId` 索引，`Find` 自身
   不做代次校验）本轮未处理，控制器也未要求。我复核了 `CharacterPositionDomainService.cs:40-46`：
   `Find` 确实只按 `CharacterId` 查而不校验代次，`Move` 与 `ForgetExact` 则校验。三个消费点都在
   `runtimes.TryGet` 的代次门之后才问，故仍不构成缺陷，维持「记录以供最终评审」。
3. 新判据用真的 `CharacterPositionDomainService` 而非替身（经 `InternalsVisibleTo`），执行者已在
   Self-check 2 里如实记下其代价（服务变红时这两条也会红）。我同意这个取舍：控制器要的是「handler
   接对了」，用真服务让「接错了」无处可藏，且 CK-009 的 8 条仍单独守着服务本身。不作为发现。

### 要求的整改

- **F3**：去掉 `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs` 头部的 UTF-8 BOM
  （`EF BB BF`），使该文件 `git diff --numstat` 由 `37 37` 变为 `36 36`；并订正 Execution Result 2 §1
  与 Executor Self-check 2 中「恰 1 增 1 删」「本轮唯一的生产文件改动是一行 XML 注释」两处与实际
  diff 不符的表述。除此之外不得改动该文件的任何字节。

本轮 F1、F2、两条新 handler 判据、措辞订正与归属、判据计数与归因，均已独立复核通过；唯一未通过项为
F3。本 verdict 只证明这一个 Task Unit 的有限缺陷检查结果，不构成完整变更评审，也不替代接受前的
新鲜验证。

## Controller Disposition 2 — task-rework（回应 Independent Quick Review 2）

F3 经控制器独立核实成立：工作区该文件头为 `ef bb bf`，HEAD 版本为 `75 73 69`（`using` 的前三字节），
`git diff --numstat` 为 `37 37`。

**判为必办而非可忽略**，理由不是那三个字节，而是评审指出的那一点：这轮返工的全部意义就是治
「一句比代码强的陈述」（F2 正是此类），却在同一份记载里出了同类第二例，而且**否证的数字就写在
同一句话里**——「由 35/35 变为 37/37」与「恰为 1 行增 1 行删……无夹带」互相矛盾。记载的可信度
是本 Spec 唯一的验收凭据，这条不能放过。

### 对评审所提技术债的处置

评审同意「五处搬运永久陈旧的 `Character.Position`」留到后续、不影响本 Task 验收，理由控制器采纳：
验收条款禁的是「取坐标 / 靠整体写回改坐标」，被搬运不落在禁止范围内；且新增的两条 handler 判据
经评审实测确能让「退回去读它」变红，正是上一轮 `MonsterDeathHandler` 前例缺的那道防线。

采纳评审的追加建议：**不要让它只以交付记载里的一段文字存在。** 指定承接者为 **TASK-09**（仓库约定
如实记下每一项做到哪一步）——它本就是记载没做到的部分的那个任务。一并在那里了结
`ISessionPresence.cs:102` 那句已漂移的 "runtime position" summary。

控制器已将这两项登记为 TASK-09 的必办输入，见该 Task 文件。

## Execution Result 3 — 极窄返工（回应 Independent Quick Review 2 的 F3 与 Controller Disposition 2）

本轮只做两件事：**删掉三个字节**，**订正两句假话**。除 `TownProjectionSlice.cs` 文件头的
`EF BB BF` 外，没有改动任何代码、测试、注释或文档。既有九个标题段（Task Unit Contract、Execution
Result 1、Executor Self-check 1、Independent Quick Review 1、Controller Disposition 1、Execution
Result 2、Executor Self-check 2、Independent Quick Review 2、Controller Disposition 2）一字未改；
订正一律写在本段落内，并逐条指名它订正的是哪个旧段落的哪一处。

**本段落的记载纪律**：按 Controller Disposition 2 的特别要求，下文凡出现枚举或计数，一律附上产生它的
那条命令与它的原始输出，不写没有输出支撑的结论。

### 1. F3 — 去掉 `TownProjectionSlice.cs` 的 UTF-8 BOM

改动前的字节与指纹：

```
$ xxd -l 16 Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
00000000: efbb bf75 7369 6e67 2053 7973 7465 6d2e  ...using System.

$ stat -c%s Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
15740

$ md5sum Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
741c1bf51cfa4285c20f96ec82a6ae20 *Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
```

执行的操作（纯字节裁剪，不经任何文本编辑器、不重写行尾）：

```
$ tail -c +4 <file> > <file>.nobom && mv -f <file>.nobom <file>
```

改动后：

```
$ stat -c%s Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
15737

$ md5sum Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
1d7650ebf4b29adf2f007f5a68d44eec *Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs

$ xxd -l 16 Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
00000000: 7573 696e 6720 5379 7374 656d 2e42 7566  using System.Buf
```

15740 − 15737 = **恰 3 字节**，首三字节由 `ef bb bf` 变为 `75 73 69`（`usi`），与 HEAD 版本一致。

**「其余内容一字节未变」不是自陈，而是可复算的**：把三字节 BOM 重新前置到当前文件上，指纹应当回到
改动前的值。实测：

```
$ printf '\xef\xbb\xbf' > rebuilt.cs && cat <file> >> rebuilt.cs && md5sum rebuilt.cs
741c1bf51cfa4285c20f96ec82a6ae20 *rebuilt.cs
```

与改动前的 `741c1bf51cfa4285c20f96ec82a6ae20` **逐位相同**。故本轮对该文件的全部改动恰为「删除前三字节」，
不多不少。（`rebuilt.cs` 建在仓库外的 scratchpad，用后已删。）

`numstat` 复核：

```
$ git diff --numstat -- Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
36      36      Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
```

由 `37 37` 变为 **`36 36`**，与 Controller Disposition 2 的预期一致。`git diff` 的首个 hunk 现在是：

```
@@ -1,5 +1,6 @@
 using System.Buffers.Binary;
 using Dfo.Server.Abstractions.Channel;
+using Dfo.Server.Abstractions.Characters;
```

不再是 `-using System.Buffers.Binary;` / `+<BOM>using System.Buffers.Binary;` 那一对。评审 F3 指认的
夹带已消除。

### 2. 订正 Execution Result 2 的 §1 —— 「恰 1 行增 1 行删……无夹带」

**原话（撤回）**：

> 该文件本轮 diff 恰为 1 行增 1 行删（`git diff --stat` 由 35/35 变为 37/37），无夹带。

**为何为假**：括号里的数字自己否证了括号外的结论。`35/35 → 37/37` 是 **+2 行增、+2 行删**，不可能
同时是「恰 1 行增 1 行删」。多出来的那一对不是注释造成的，而是文件头新增的 UTF-8 BOM 让首行整行
被判为增删。评审 F3 的措辞「否证所需的数字就在同一句话里」准确，我接受。

**现在的实际数字**（命令与输出见上一节）：该文件 `git diff --numstat` = **`36 36`**。

**改述为**（两个口径分开写，避免再造一句比事实强的话）：

- **该文件对 HEAD 的全部差异**是 **36 行增 / 36 行删**——这是本 Task 各轮累积的结果，包含早先已被
  Independent Quick Review 1 复核通过的构造函数参数、门判条件、remarks 重写等改动，不是任何单轮的量。
- **Execution Result 2 那一轮相对上一轮基线的增量**，真实值是 **2 行增 / 2 行删**（`35/35 → 37/37`）：
  一对是 `:194-196` 的 `<summary>` 替换，另一对是未申报的文件头 BOM。
- **本轮（Execution Result 3）删掉 BOM 之后**，该增量收敛为 **1 行增 / 1 行删**（`35/35 → 36/36`），
  也就是只剩那一行 `<summary>`。原话所声称的形状，到本轮才真正成立；它在写下的当时不成立。

### 3. 订正 Executor Self-check 2 的「行为逐值不变」一行 —— 「本轮唯一的生产文件改动是一行 XML 注释」

**原话（撤回）**：

> 本轮唯一的生产文件改动是一行 XML 注释，不参与编译产物的行为

**为何为假**：Execution Result 2 那一轮对 `TownProjectionSlice.cs` 的字节改动有**两处**——`<summary>`
那一行的替换，以及文件头新增的三字节 UTF-8 BOM。「唯一」这个词把后者排除掉了，而后者当时确实存在，
且未在任何段落里申报。这与 F2 被判的毛病是同一类：一句比代码强的陈述。

**需要与之分开的一点**：被这句话拿来支撑的**结论**（「行为逐值不变」）本身仍然成立——C# 编译器接受
BOM，构建 0 警告 0 错误，全量判据 2040 全绿，编译产物行为不受影响。假的是**理由**，不是结论。我不把
「结论碰巧对」当作理由不必订正的借口：一份用来支撑验收判断的记载里，理由为假就是缺陷。

**改述为**：Execution Result 2 那一轮对生产文件的字节改动共两处（一行 XML 注释 + 未申报的文件头 BOM），
两处都不改变编译产物的行为；本轮（Execution Result 3）删除 BOM 之后，该生产文件对 HEAD 的差异中
不再含任何未申报的字节。

### 4. 判据复跑（命令与原始输出）

**构建**：

```
$ dotnet build Server/86JP.Server.slnx --nologo
...
已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:03.59
```

**全量测试**：

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo

已通过! - 失败:     0，通过:   500，已跳过:     0，总计:   500，持续时间: 1 s - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52，持续时间: 912 ms - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60，持续时间: 7 s - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6，持续时间: 8 s - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   256，已跳过:     0，总计:   256，持续时间: 12 s - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   223，已跳过:     2，总计:   225，持续时间: 16 s - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   943，已跳过:     0，总计:   943，持续时间: 15 s - Dfo.Server.Application.Tests.dll (net10.0)
```

求和（这七行就是全部被加数，别处无来源）：500 + 52 + 60 + 6 + 256 + 223 + 943 = **2040 通过**；
已跳过合计 **2**（两条都在 `Dfo.Server.Infrastructure.Tests`，同一次运行的输出里逐条列出了名字：
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`）；
失败合计 **0**。与上一轮的 **2040 / 2 / 0 逐项目相同**，纯字节改动未使任何计数变化，符合预期。

两个既知偶发（`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` 的
`ObjectDisposedException`、`Dfo.Server.Networking.Tests` 的并行偶发）本轮均未出现。

### 5. 全仓 BOM 复查（命令与原始输出）

**（a）本次改动涉及的全部文件——有没有第二个被意外加了 BOM 的**。枚举源是 `git status --porcelain`
（30 个 `.cs` 改动/新增文件；另有一个未跟踪目录 `.rks-agent/tasks/character-state-finds-its-owner/`，
它被 `[ -f ]` 跳过，单独在 (b) 处理）：

```
$ git status --porcelain | sed 's/^...//' | tr -d '"' | while IFS= read -r p; do
    if [ -f "$p" ]; then
      b=$(head -c 3 "$p" | xxd -p)
      if [ "$b" = "efbbbf" ]; then echo "BOM     $p"; else echo "no-BOM  $p"; fi
    fi
  done

no-BOM  Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs
no-BOM  Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs
no-BOM  Server/src/Dfo.Server.Application/ApplicationModule.cs
no-BOM  Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
no-BOM  Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs
no-BOM  Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs
no-BOM  Server/src/Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs
no-BOM  Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs
no-BOM  Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
no-BOM  Server/src/Dfo.Server.Protocol/Game/TownProjection/TownReturnProjection.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Characters/CarriedGoldCapTests.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Characters/CharacterItemUseTests.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Characters/CharacterRepositoryProductsTests.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Characters/NoCarriedGold.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Characters/RemainingSkillPointTests.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Game/UseStackableHandlerTests.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Inventory/ItemUseFixtures.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Sessions/SessionGenerationOwnershipTests.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Support/RecordingCharacterTable.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs
no-BOM  Server/tests/Dfo.Server.Persistence.Tests/Support/CharacterSide.cs
no-BOM  Server/tests/Dfo.Server.Protocol.Tests/Game/CarriedGoldStandIns.cs
no-BOM  Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs
no-BOM  Server/tests/Dfo.Server.Protocol.Tests/Game/ExperienceFrameStandIns.cs
no-BOM  Server/src/Dfo.Server.Abstractions/Characters/ICharacterPositionDomainService.cs
no-BOM  Server/src/Dfo.Server.Application/Characters/CharacterPositionDomainService.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Characters/CharacterIdentityTests.cs
no-BOM  Server/tests/Dfo.Server.Application.Tests/Characters/CharacterPositionTests.cs
no-BOM  Server/tests/Dfo.Server.Persistence.Tests/Characters/CharacterIdentityReadTests.cs
no-BOM  Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs
```

**30 行全部 `no-BOM`，零个 `BOM`。第二个被意外加了 BOM 的文件不存在。**

**（b）未跟踪的 Task 目录下的全部文件**（记载本身也是本次改动涉及的文件，一并查）：

```
$ find .rks-agent/tasks/character-state-finds-its-owner -type f | while IFS= read -r p; do
    b=$(head -c 3 "$p" | xxd -p)
    if [ "$b" = "efbbbf" ]; then echo "BOM     $p"; else echo "no-BOM  $p"; fi
  done

no-BOM  .../spec-001-owners-for-character-state/plan.md
no-BOM  .../spec-001-owners-for-character-state/routing-decision.md
no-BOM  .../spec-001-owners-for-character-state/sdd-execution.yaml
no-BOM  .../spec-001-owners-for-character-state/spec.md
no-BOM  .../spec-001-owners-for-character-state/TaskContract.md
no-BOM  .../spec-001-owners-for-character-state/tasks/TASK-01-identity-answers-from-the-character.md
no-BOM  .../spec-001-owners-for-character-state/tasks/TASK-02-position-gets-an-owner.md
no-BOM  .../spec-001-owners-for-character-state/tasks/TASK-03-quest-read-point-asks-the-log.md
no-BOM  .../spec-001-owners-for-character-state/tasks/TASK-04-settings-get-a-domain-service.md
no-BOM  .../spec-001-owners-for-character-state/tasks/TASK-05-combo-pages-get-a-store-and-an-instance.md
no-BOM  .../spec-001-owners-for-character-state/tasks/TASK-06-retire-the-always-null-combo-field.md
no-BOM  .../spec-001-owners-for-character-state/tasks/TASK-07-fatigue-seam-is-really-called.md
no-BOM  .../spec-001-owners-for-character-state/tasks/TASK-08-prove-the-fatigue-call-goes-red.md
no-BOM  .../spec-001-owners-for-character-state/tasks/TASK-09-repository-conventions-tell-the-truth.md
no-BOM  .../character-state-finds-its-owner/multi-spec-map.md
no-BOM  .../character-state-finds-its-owner/routing-decision.md
no-BOM  .../character-state-finds-its-owner/TaskContract.md
```

（原始输出中每行都是完整相对路径；此处仅为排版把公共前缀 `.rks-agent/tasks/character-state-finds-its-owner/`
折叠为 `.../`，条目数与逐条判定照录未改。）**17 个文件全部 `no-BOM`。**

**（c）工作区侧 `Server/src` 全量**：

```
$ git ls-files 'Server/src/*.cs' | wc -l
433

$ git ls-files 'Server/src/*.cs' | while IFS= read -r p; do
    b=$(head -c 3 "$p" | xxd -p); if [ "$b" = "efbbbf" ]; then echo "BOM  $p"; fi
  done

BOM  Server/src/Dfo.Server.Protocol/Game/AccountRoster/GetAccountCharacterRosterHandler.cs
BOM  Server/src/Dfo.Server.Protocol/Game/CreateCharacter/CreateCharacterSlice.cs
BOM  Server/src/Dfo.Server.Protocol/Game/DeleteCharacter/DeleteCharacterSlice.cs
BOM  Server/src/Dfo.Server.Protocol/Game/HiddenCharacterInfo/HiddenCharacterInfoSlice.cs
```

**（d）HEAD 侧同一集合（即「本来就带 BOM」的基线）**：

```
$ git ls-files 'Server/src/*.cs' | while IFS= read -r p; do
    b=$(git show "HEAD:$p" | head -c 3 | xxd -p)
    if [ "$b" = "efbbbf" ]; then echo "BOM-at-HEAD  $p"; fi
  done

BOM-at-HEAD  Server/src/Dfo.Server.Protocol/Game/AccountRoster/GetAccountCharacterRosterHandler.cs
BOM-at-HEAD  Server/src/Dfo.Server.Protocol/Game/CreateCharacter/CreateCharacterSlice.cs
BOM-at-HEAD  Server/src/Dfo.Server.Protocol/Game/DeleteCharacter/DeleteCharacterSlice.cs
BOM-at-HEAD  Server/src/Dfo.Server.Protocol/Game/HiddenCharacterInfo/HiddenCharacterInfoSlice.cs

$ (同一循环，把 echo 换成计数 | wc -l)
4
```

(c) 与 (d) **逐行相同**：工作区现存的 4 个带 BOM 的文件，HEAD 侧同样带 BOM，且都不在本 Task 的改动
集合里（与 (a) 的 30 行比对可见，无一重合）。**这 4 个我一个字节都没动。**

**顺带订正一个计数（订正 Independent Quick Review 2 的 F3 与 Controller Disposition 2 转述的
「5 个本来就带 BOM」）**：Review 2 写的是「`Server/src` 下 433 个受版本控制的 `.cs` 里只有 5 个带 BOM」，
本轮任务书据此说「有 5 个**本来就**带 BOM」。按 (d) 的 HEAD 侧实测，**本来就带 BOM 的是 4 个，不是
5 个**——第 5 个正是我加了 BOM 的 `TownProjectionSlice.cs`。评审是在**工作区**上数的（那正是它用来
论证「本次 30 个改动文件中只有这一个带 BOM」的同一份数据，其结论方向没错），把当时含我那个 BOM 的
工作区计数当作「本来就有」的基线转述时多算了一个。这不影响 F3 的成立，也不影响处置；我照数字如实
记下，以免这个 4/5 之差在后续任务里被读成「我少改了一个」或「多改了一个」。

### 6. 本轮未做的事（命令与原始输出）

```
$ git log -1 --format=%H
427331bcb07f60121c97920287ac341c9cb14dee

$ git stash list
（无输出）
```

HEAD 仍是 `427331bc`，与 Independent Quick Review 2 记录的 owned diff 身份一致；stash 为空。
**未建检查点、未提交、未 stash。**

```
$ stat -c '%y %n' .../spec-001-owners-for-character-state/sdd-execution.yaml
2026-09-04 01:45:47.451880300 +0800 .../sdd-execution.yaml
```

mtime `01:45:47`，早于本轮工作窗口（见下）。**未改 `sdd-execution.yaml`；本段与 Executor Self-check 3
均不含 verdict。**

**本轮只碰了一个代码文件，可由 mtime 证**。本轮窗口的起点是 Controller Disposition 2 写入 Task 文件
的时刻（该文件在我追加本段之前的 mtime 为 `2026-09-04 02:54:39`）。下面这条命令列出全部 30 个
改动/新增 `.cs` 的 mtime：

```
$ git status --porcelain | sed 's/^...//' | tr -d '"' | grep '\.cs$' \
    | while IFS= read -r p; do stat -c '%y  %n' "$p"; done | sort
```

原始输出共 30 行，最早 `2026-09-04 00:26:38`（`ICharacter.cs`），**只有最后一行落在 `02:54:39`
之后**；这里照录首尾相邻的两行以示分界：

```
2026-09-04 02:33:45.906411200 +0800  Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs
2026-09-04 02:55:54.608184100 +0800  Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
```

其余 29 个 `.cs` 的 mtime 全部 **≤ `02:33:45`**，都落在上一轮及更早。**本轮唯一被触碰的代码文件是
`TownProjectionSlice.cs`（`02:55:54`），而对它的改动已由第 1 节的 md5 复算限定为「恰删前三字节」。**

## Executor Self-check 3

### 对照 Controller Disposition 2 与本轮任务书

| 要求 | 结论 | 证据（均附命令与原始输出，见 Execution Result 3 对应节） |
|---|---|---|
| 删掉 `TownProjectionSlice.cs` 头部的 `EF BB BF`，其余字节不变 | 完成 | §1：`stat -c%s` 15740→15737（差恰 3）；`xxd -l 16` 首三字节 `efbbbf`→`757369`；**BOM 回贴后 md5 复算回到 `741c1bf5…`，证明改动恰为删前三字节，不多不少** |
| 该文件 `git diff --numstat` 应为 `36 36` | 完成 | §1：`git diff --numstat -- <file>` 原始输出 `36      36`；首个 hunk 已不含 BOM 那一对 |
| 订正 Execution Result 2 §1 的「恰 1 增 1 删……无夹带」 | 完成 | §2：引原话、指出「35/35→37/37 即 +2/−2」自证其伪、给出两个口径的真实数字（对 HEAD 36/36；那一轮增量真值 2/2；删 BOM 后增量 1/1） |
| 订正 Executor Self-check 2 的「本轮唯一的生产文件改动是一行 XML 注释」 | 完成 | §3：引原话、指出那一轮实为两处字节改动（注释 + 未申报 BOM）、区分「结论仍真」与「理由为假」并照样订正 |
| 不回头改旧段落，订正写在新段落并指名 | 满足 | §2、§3 的小标题即「订正 Execution Result 2 的 §1」「订正 Executor Self-check 2 的……」；既有九个标题段一字未改 |
| 除三字节外不改任何代码/测试/注释/文档 | 满足 | §6 的 mtime 枚举：30 个 `.cs` 中只有 `TownProjectionSlice.cs`（`02:55:54`）落在本轮窗口内，其余 29 个 ≤ `02:33:45`；而对它的改动已由 §1 的 md5 复算限定为「恰删前三字节」 |
| 不建 Git 检查点、不提交、不 stash | 满足 | §6：`git log -1` = `427331bc…`（与 Review 2 记录一致）；`git stash list` 无输出。全程只用 `git status` / `git diff` / `git show HEAD:<path>` / `git ls-files` 只读查看 |
| 不改 `sdd-execution.yaml`，不写 verdict | 满足 | §6：该文件 mtime `01:45:47`，早于本轮窗口；本段与 Execution Result 3 均不含 verdict |

### 对照判据

| 判据 | 结论 | 证据 |
|---|---|---|
| `dotnet build Server/86JP.Server.slnx --nologo` 零警告零错误 | 满足 | §4：原始输出 `0 个警告` / `0 个错误` |
| 全量测试 2040 通过 / 2 跳过 / 0 失败 | 满足 | §4：七个项目的结果行逐条照录，求和 500+52+60+6+256+223+943 = 2040；跳过 2 条并列出其全名；失败 0。与上一轮**逐项目相同**，纯字节改动未改变任何计数 |
| 该文件 `git diff --numstat` = `36 36` | 满足 | §1 |
| 全仓复查有无第二个被意外加 BOM 的文件 | 满足，**无** | §5(a) 30 行全 `no-BOM`；§5(b) Task 目录 17 个文件全 `no-BOM`；§5(c)/(d) 工作区与 HEAD 的 `Server/src` BOM 集合**逐行相同**（同样那 4 个），且与本 Task 改动集合无一重合 |
| 既知偶发未出现 | 是 | §4：`OutsideTransactionTests` 与 `Dfo.Server.Networking.Tests` 本轮均全绿 |

### 本轮如实记录的两件事

1. **我顺带订正了评审与任务书的一个计数**（§5 末）：「`Server/src` 下 5 个**本来就**带 BOM」经 HEAD 侧
   实测为 **4 个**，第 5 个是我加的那个。评审的结论方向没错（工作区当时确实数得 5 个，且它据此正确
   论证了「本次 30 个改动文件中只有这一个带 BOM」），错的只是把工作区计数当成了「本来就有」的基线。
   我按本轮的记载纪律把数字连同命令一并写出，而不是沉默地照抄 5。**我没有动那 4 个文件中的任何一个。**
2. **我没有借「结论碰巧仍为真」免掉 §3 的订正。** BOM 确实不改变编译产物行为，「行为逐值不变」这个
   结论现在仍成立；但支撑它的那句「本轮唯一的生产文件改动是一行 XML 注释」在写下的当时是假的。本
   Spec 反复在治的正是「一句比代码强的陈述」，理由为假就是缺陷，与结论是否碰巧对无关。

## Independent Quick Review 3

- **Verdict: `passed`**
- 审查的契约版本：Contract revision 1，无修订（amendment）。裁定依据为 Independent Quick Review 2 的
  F3 与 Controller Disposition 2 指定的极窄返工范围。
- 审查者身份：全新上下文，未参与实现，也不是 Independent Quick Review 1 / 2 的作者。下列每一项均自行
  执行命令取证，未采信任何自陈结论。
- Owned diff 身份：`git log -1` = `427331bcb07f60121c97920287ac341c9cb14dee`，`git stash list` 无输出，
  无提交、无检查点。与 Independent Quick Review 2 记录的身份一致。
- 范围：按控制器指定，只核 F3 的返工与本轮收口（四件事 + 判据 + 记载纪律）。Independent Quick Review 2
  已独立复核通过的部分（F1 已治、F2 订正、两条 handler 判据的货真价实、措辞订正、2040/2/0）未重审。
  工作区内 TASK-01 的成果不在本次范围。

### 本轮自行执行的证据

| 项 | 我执行的命令 | 我看到的输出 |
|---|---|---|
| 文件头字节 | `head -c 3 <TownProjectionSlice.cs> \| xxd -p` | `757369`（`usi`），BOM 已不在 |
| HEAD 侧对照 | `git show HEAD:<file> \| head -c 16 \| xxd` | `using System.Buf`，与工作区首字节一致 |
| 文件大小 | `stat -c%s <file>` | `15737`（= 执行者报的改后值） |
| numstat | `git diff --numstat -- <file>` | `36	36` |
| **md5 复算（关键）** | `printf '\xef\xbb\xbf' > rebuilt.cs && cat <file> >> rebuilt.cs && md5sum rebuilt.cs` | `741c1bf51cfa4285c20f96ec82a6ae20`，大小 `15740`——与执行者记的改动前指纹**逐位相同** |
| 该文件全量 diff 通读 | `git diff -- <file>` | 6 个 hunk 全部是坐标改动（`using` 增行、summary/remarks 重写、两个构造函数参数、门判 `positions.Find`、`CrossInto`/`StepTo` 替换整体写回）；**无 BOM 那一对**，无任何与坐标无关的行 |
| 空白/行尾 | `git diff --check` | 退出码 0，无输出 |
| 本轮触碰面 | 对 30 个改动/新增 `.cs` 逐个 `stat -c '%y  %n'` 并排序 | 只有 `TownProjectionSlice.cs`（`02:55:54`）落在 Controller Disposition 2 写入之后；其余 29 个 ≤ `02:33:45` |
| 全仓 BOM（工作区） | `git ls-files 'Server/src/*.cs' \| while read p; do head -c 3 ...` | 4 个：`GetAccountCharacterRosterHandler.cs` / `CreateCharacterSlice.cs` / `DeleteCharacterSlice.cs` / `HiddenCharacterInfoSlice.cs` |
| 全仓 BOM（HEAD 侧） | 同一集合改走 `git show "HEAD:$p" \| head -c 3` | **同样这 4 个，逐行相同**；计数 `wc -l` = **4** |
| 改动集合内 BOM | `git status --porcelain` 逐个查头三字节 | **零个**；`Server/src` 全量 `git ls-files ... \| wc -l` = `433` |
| Task 目录 | `find .rks-agent/tasks/character-state-finds-its-owner -type f` | 17 个文件，**零个带 BOM** |
| CK-006 构建 | `dotnet build Server/86JP.Server.slnx --nologo` | `0 个警告` / `0 个错误` |
| CK-010 全量测试 | `PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo` | 52+500+60+6+256+223+943 = **2040 通过 / 2 跳过 / 0 失败**；跳过两条即 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`；两个既知偶发均未出现 |
| 未提交 / 未 stash / 未改控制状态 | `git log -1`、`git stash list`、`stat` + 读 `sdd-execution.yaml` | HEAD 仍 `427331bc`；stash 空；yaml mtime `01:45:47`（早于本轮窗口），`TASK-02 state: in_progress`，执行者未写 verdict |
| 无残留临时文件 | 全仓 `find` `*nobom*` / `rebuilt*` | 无匹配（`rebuilt.cs` 确在仓库外，已不在仓库内留痕） |

### 1. F3 已消除，且「其余字节一位未动」经我自己复算成立

BOM 三字节已去（`efbbbf` → `757369`），大小 `15740 → 15737` 恰差 3，`numstat` 由 `37 37` 收敛为
`36 36`，`git diff` 首个 hunk 已是 `+using Dfo.Server.Abstractions.Characters;` 而非 BOM 那一对。

执行者给出的证明**不是自陈，我自己跑了一遍**：把三字节 BOM 重新前置回当前文件，md5 回到
`741c1bf51cfa4285c20f96ec82a6ae20`、大小回到 `15740`。md5 逐位相同意味着「当前文件 = 改动前文件去掉
前三字节」在全部 15737 个字节上成立，不留任何「顺手改了别处又刚好抵消」的余地。**这条证明我判为
真正可复算，采信。**

我另外通读了该文件对 HEAD 的全部 36/36 差异：6 个 hunk 全是坐标改动，无一行与坐标无关，
`git diff --check` 也是干净的。

### 2. 本轮没有第二处夹带

30 个改动/新增 `.cs` 的 mtime 我自己排了一遍：唯一落在 Controller Disposition 2 写入之后（`02:54:39`）
的是 `TownProjectionSlice.cs`（`02:55:54`），其余 29 个最晚 `02:33:45`。而对这唯一一个文件的改动已被
第 1 节的 md5 复算限死为「恰删前三字节」。两者合起来构成闭环：**本轮除三字节外没有改动任何代码、
测试、注释或文档。** `sdd-execution.yaml`（`01:45:47`）与九个既有标题段亦未动——我按行位置核对了被
订正的两句原话仍原样留在旧段落（`:426` 与 `:646`），未被回头改写，符合「订正写新段、不改旧段」的要求。

（`TASK-09-repository-conventions-tell-the-truth.md` mtime 为 `02:54:55`，即 Controller Disposition 2
落笔的同一时刻，是控制器登记技术债的动作，不属执行者本轮改动，也不在本 Task 的 owned diff 内。）

### 3. 两句记载订正准确到位，未产生第三句假话

两处引文我逐字比对过原文，**引得准确**：

- `:425-426` 原文「该文件本轮 diff 恰为 1 行增 1 行删（`git diff --stat` 由 35/35 变为 37/37），无夹带。」
  与 §2 的引用逐字相同。
- `:646` 原文「本轮唯一的生产文件改动是一行 XML 注释，不参与编译产物的行为」与 §3 的引用逐字相同。

订正内容本身经核对为真：

- §2 给出的三个口径互不混淆，且与实测吻合——对 HEAD 的累积差异 **36/36**（我实测同值）；那一轮相对
  上一轮基线的增量真值 **2/2**（`35/35 → 37/37` 的算术必然）；删 BOM 后收敛为 **1/1**（`35/35 → 36/36`，
  与我实测的 36/36 自洽）。把「累积量」与「单轮增量」分开写，正是上一轮出错的那个混淆点，改法对症。
- §3 区分了「结论仍真」（BOM 不改变编译产物行为——我实测构建 0/0、判据 2040 全绿，成立）与「理由为假」
  （那一轮实为两处字节改动），并且**没有拿结论碰巧对当作免于订正的理由**。这一区分我判为正确且必要。

我特意找过第三句假话：§1–§6 的每个数字我都对照了自己的实测，**未发现新的不实陈述**。

### 4. 对上一轮评审的计数订正成立

我在 HEAD 侧自己数了一遍：`Server/src` 下 433 个受版本控制的 `.cs` 中，HEAD 侧带 BOM 的是
**4 个**（`GetAccountCharacterRosterHandler.cs`、`CreateCharacterSlice.cs`、`DeleteCharacterSlice.cs`、
`HiddenCharacterInfoSlice.cs`），不是 5 个。工作区侧现在也正好是同样这 4 个，**逐行相同**，且都不在本
Task 的 30 个改动文件里——这 4 个执行者一字节未动。

**订正成立。** 第 5 个确是当时含 BOM 的 `TownProjectionSlice.cs`。需要为上一轮评审说明的一点是：
Independent Quick Review 2 的 F3 原文写的是「`Server/src` 下 433 个受版本控制的 `.cs` 里只有 5 个带
BOM」，**未用「本来就」三字**，在当时的工作区上这句话字面为真；「本来就」是转述环节添上的时间限定，
它把工作区计数读成了基线，这才产生 4/5 之差。执行者在正文里正是这么说的（「评审的结论方向没错……
错的只是把工作区计数当成了「本来就有」的基线」），措辞把握准确。

唯一一处措辞可再紧：§5 末的小标题写作「订正 Independent Quick Review 2 的 F3 与 Controller
Disposition 2 转述的「5 个本来就带 BOM」」，而 Task 文件内的 Controller Disposition 2 段落里并无这句
话（该限定出现在文件外的返工任务书中）。属指名对象的粒度偏松，正文已把归属说清，不构成不实陈述，
**不列为必办**。

### 5. 记载纪律（枚举/计数须附命令与原始输出）的执行情况

逐节核过：§1、§4、§5(a)(b)(c)(d)、§6 的每一处枚举与计数都附了产生它的命令。**没有发现只写结论、
不给出处的枚举。** 两处对输出做了压缩，但都当场声明、且我复跑后结论为真，故不列为缺陷：

- §5(b) 把 17 行的公共路径前缀折叠为 `.../`（已声明「条目数与逐条判定照录未改」）——我实测 17 个文件、
  零 BOM，属实。
- §6 的 30 行 mtime 只照录首尾相邻两行以示分界（已声明「原始输出共 30 行」）——我完整跑了同一条命令，
  「其余 29 个 ≤ `02:33:45`」属实。
- §5(d) 的计数一行写作「（同一循环，把 echo 换成计数 | wc -l）」，是描述而非可粘贴的命令；我用等价
  循环实测得 `4`，与其所报一致。

以上三点均为可复现性上的轻微粗糙，不影响任何结论的真伪，记录备考。

### 结论与遗留

F3 的三字节已去且改动纯净（md5 复算是硬证据，不是自陈）；本轮无第二处夹带；两句假话已在新段落内
逐条引原话撤回、给出真实数字，且未生第三句假话；对上轮评审的 4/5 计数订正经 HEAD 侧实测成立。判据
自跑为构建 0 警告 0 错误、全量 **2040 通过 / 2 跳过 / 0 失败**，与前轮逐项目相同——纯字节改动未撼动任
何计数，符合预期。未建检查点、未提交、未 stash，`sdd-execution.yaml` 未改。

**无必办项。**

本 verdict 只表示：在上述有限输入内，本 Task Unit 未被证明存在明显缺陷。它不是完整变更评审，也不替
代接受前的新鲜验证；Controller Disposition 2 已承接给 TASK-09 的两项（五处搬运永久陈旧的
`Character.Position`、`ISessionPresence.cs:102` 的 "runtime position" 漂移 summary）仍按原处置留在
TASK-09，本轮未动。
