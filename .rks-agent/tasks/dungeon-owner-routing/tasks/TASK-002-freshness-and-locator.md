# TASK-002 — 中性陈旧定义、定位能力与夹具补登记

## Task Unit Contract

- Revision: 1
- Outcome and references：交付 Plan T-002（BD-001、BD-002、BD-004），覆盖 Spec RQ-001、RQ-003、AC-001、AC-003。
  - 陈旧判定改为单一中性定义，删除 `TowerProtocol`；
  - 新增定位能力及其单元测试；
  - 让协议层测试夹具能为定位能力提供副本占用登记。

  本 Task 不改变任何命令的分发：处理器暂不调用定位能力。
- Owned boundary：
  - 生产代码：
    - 新增 `Server/src/Dfo.Server.Protocol/Game/GameRequestFreshness.cs`；
    - 删除 `Server/src/Dfo.Server.Protocol/Game/TowerProtocol.cs`；
    - 替换全部 `TowerProtocol.IsStale` 调用（只改调用名，不移动位置）；
    - 新增 `Server/src/Dfo.Server.Protocol/Game/Dungeons/DungeonLocator.cs`（含位置结果类型）。
  - 测试：
    - 定位能力单元测试；
    - `Server/tests/Support/TowerAlgorithmDrivers.cs` 等驱动中复制的 `TowerProtocol.IsStale` 写法同步替换；
    - 共享的“按服务状态推出”的占用登记替身；
    - 协议层夹具默认提供该替身。
  - 不改任何命令处理器的分支结构，不改 `Server/src/Dfo.Server.Application/`。
- Required checks：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。
  - `git grep -n "TowerProtocol" -- Server` 无结果。
  - 定位能力单元测试通过；TASK-001 的表征测试（`DungeonOwnerRoutingCharacterizationTests*`）不改断言、全部通过。
  - 变异检查：至少两项。例如让 `Locate` 在塔不认会话时仍返回塔，或在整队位置中让幻影优先；确认对应单元测试失败。还原后 `touch` 再构建，`git diff` 中不留变异。
  - 完整测试 `cd Server && dotnet test 86JP.Server.slnx`，输出写入 `.rks-agent/tasks/dungeon-owner-routing/logs/task-002-full-test.log`，失败须归因。
- Dependencies：TASK-001
- Result review：required。理由：T-003～T-005 的全部命令迁移都建立在定位能力的语义与结果类型之上。若定位规则或整队优先级有偏差，会被复制进 13 个命令，直到最终审查才暴露；单元测试是否覆盖了 Spec 定义的每种情形，需要独立核对。

## Detailed Plan

1. **陈旧定义（BD-001）**：
   - `internal static class GameRequestFreshness { internal static bool IsStale(GameMessageMetadata metadata) => metadata.InboundOrdinal is > 0 && !metadata.SequenceIsFresh; }`。
   - 注释说明判定含义：传输层赋予了入站序号、但序号未在连接计数上前进一位；不带序号的帧永远不陈旧。
   - 用 `git grep -l "TowerProtocol.IsStale"` 找出全部调用（生产约 19 处，测试驱动 2 处），逐一替换，删除 `TowerProtocol.cs`。
2. **定位能力（BD-002）**：在 `Dfo.Server.Protocol.Game.Dungeons` 命名空间新增：
   - 单人位置：封闭的结果类型，三种取值。
     - `InBossTower(SelectedSessionIdentity Session, BossTowerSnapshot Run)`；
     - `InIllusionTower(SelectedSessionIdentity Session, IllusionTowerSnapshot Run)`；
     - `OrdinaryOrNone(SelectedSessionIdentity Session)`。

     可用抽象 record 加 sealed 子类，便于 `switch` 穷举。
   - `DungeonLocator.Locate(SelectedSessionIdentity session, IDungeonParticipationDomainService participation, IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers)`，规则如下：
     - 登记 `Mode == BossTower` 且 `bossTowers.Find(session)` 非空、`RunId` 与登记一致 → `InBossTower`；
     - 幻影塔同理 → `InIllusionTower`；
     - 其余一律 `OrdinaryOrNone`。

     核对 RunId 时，注意两塔快照的 RunId 类型与 `DungeonParticipationOwner.RunId`（Guid）的对应关系，按实际类型比较。
   - 整队位置：结果三种取值。
     - `PartyInBossTower(IReadOnlyList<BossTowerSnapshot> Runs)`；
     - `PartyInIllusionTower(IReadOnlyList<IllusionTowerSnapshot> Runs)`；
     - `PartyOrdinaryOrNone`。

     `DungeonLocator.LocateParty(IEnumerable<SelectedSessionIdentity> candidates, ...)` 规则：
     - 对候选去重后逐个 `Locate`；
     - 有任一 Boss 塔则返回 Boss，副本按候选顺序按 RunId 去重；
     - 否则有幻影塔则返回幻影，同样处理；
     - 否则返回 `PartyOrdinaryOrNone`。

     现行 `BossTowerCapture` / `IllusionTowerCapture` 的候选顺序是调用者、目标、在线队员，迁移时（T-005）沿用。
   - 只读，不写登记，不开事务。
3. **单元测试（AC-003）**：放在 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonLocatorTests.cs`，用真实 `DungeonParticipationDomainService` 或简单替身，以及塔服务替身。覆盖：
   - 无登记 → 普通或不在副本；
   - 登记普通 → 普通或不在副本；
   - Boss 塔与幻影塔各自被认可；
   - 登记为塔但塔服务不认这个会话（会话代次窗口）→ 普通或不在副本；
   - 登记为塔、塔服务认可，但 RunId 不一致 → 普通或不在副本；
   - 整队位置：只有队员在 Boss 塔 → Boss；Boss 与幻影并存 → Boss；只有幻影 → 幻影；多个不同 Boss 副本 → 按候选顺序全部附带、按 RunId 去重；候选全在城镇 → 普通或不在副本。
4. **夹具补登记（BD-004）**：
   - 把 TASK-001 中 `DungeonOwnerRoutingWorld.cs` 使用的“按角色推出”登记替身提升为共享测试辅助，放在 `Server/tests/Support/` 或协议测试的 Support 目录，按现有链接方式引用。推出规则：
     - 角色在 Boss 塔服务中（按角色）→ Boss 塔；
     - 否则在幻影塔服务中 → 幻影塔；
     - 否则普通副本服务能按角色找到 → 普通；
     - 否则无登记。
   - 让 `ProtocolAlgorithmFixture` 在测试未显式提供 `IDungeonParticipationDomainService` 时默认提供它。
   - 盘点其他会构造协议命令处理器、但不经 `ProtocolAlgorithmFixture` 的夹具（如 `TowerAlgorithmDrivers`、`DungeonExitDrivers`、`LoadingAlgorithmTestDriver`、`EntryHandlerDriver`、`TowerDomainStub` 的使用方，以及 Networking 测试中的手工容器），列入 Result。本 Task 不强制全部改造，迁移 Task 按实际失败再补；能低成本补的，现在就补。
   - 测试断言不得因补登记而修改。
5. **检查**：按 Required checks 执行，结果写入 Result。

## Result

- Outcome：done。未改任何命令处理器的分支结构，未改 `Server/src/Dfo.Server.Application/`；处理器尚未调用定位能力。

### Changed

- 生产代码
  - 新增 `Server/src/Dfo.Server.Protocol/Game/GameRequestFreshness.cs`：`internal static bool IsStale(GameMessageMetadata)`，判定式与原 `TowerProtocol.IsStale` 逐字一致；注释写明“传输层编号但未前进一位才陈旧，未编号的帧永不陈旧”。
  - 删除 `Server/src/Dfo.Server.Protocol/Game/TowerProtocol.cs`（工作区删除，未暂存）。
  - 生产 19 处调用原地改名（`GameCharacterDeathCommandHandler` 2、`SelectDungeonCommandPhases` 1、`GameDungeonSelectionCommands` 3、`GamePartyTransitionCommandHandler.Exit` 2、`.Stages` 1、`GamePartyTransitionCommandHandler` 1、`GameGetItemCommandHandler` 2、`MonsterTowerCommandPhases` 2、`TowerLoadingCommandPhases` 5），只换调用名，位置不动。
  - 新增 `Server/src/Dfo.Server.Protocol/Game/Dungeons/DungeonLocator.cs`：
    - `DungeonPlace`：封闭的抽象 record，嵌套 `InBossTower(Session, BossTowerSnapshot Run)`、`InIllusionTower(Session, IllusionTowerSnapshot Run)`、`OrdinaryOrNone(Session)`；
    - `DungeonPartyPlace`：嵌套 `PartyInBossTower(Runs)`、`PartyInIllusionTower(Runs)`、`PartyOrdinaryOrNone`；
    - `DungeonLocator.Locate` / `LocateParty`，规则按 Detailed Plan 第 2 步。只读，不写登记、不开事务。
- 测试
  - `Server/tests/Support/TowerAlgorithmDrivers.cs`：2 处复制写法同步改名。
  - 新增 `Server/tests/Support/DerivedDungeonParticipation.cs`：共享的“按服务状态推出”登记替身 `DerivedDungeonParticipation`，以及测试接口 `ICharacterTowerSeats`（`Guid? RunOf(CharacterId)`）。推出顺序：Boss 塔 → 幻影塔 → 普通副本 `runs.Find(character)` → `Residual`。写操作抛 `NotSupportedException`。
  - `Server/tests/Support/ProtocolAlgorithmFixture.cs`：`Create<T>` 解析参数时，若参数类型正是 `IDungeonParticipationDomainService`、调用方未提供且该参数没有默认值，就用已提供的 Boss 塔、幻影塔、普通副本服务和会话目录（未提供的取夹具默认值）构造上述替身。
  - 三个链接 `ProtocolAlgorithmFixture.cs` 的测试项目（Application.Tests、Host.Tests、Protocol.Tests）的 csproj 按现有方式各加一行 `<Compile Include="../Support/DerivedDungeonParticipation.cs" .../>`。
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonOwnerRoutingWorld.cs`：删除私有 `Registrations`，改用共享替身；两个塔替身实现 `ICharacterTowerSeats`（`RunOf` 改为返回 `Guid?`）。表征测试文件 `DungeonOwnerRoutingCharacterizationTests*.cs` 零改动（`git diff` 为空）。
  - 新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonLocatorTests.cs`（17 个用例，`Category=Unit`）。
  - 新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DerivedDungeonParticipationTests.cs`（2 个用例，`Category=Unit`）：夹具默认提供替身，且能经会话回退识别 `TowerDomainStub`；显式提供的登记优先；可选参数仍为 null。

### Why

- **结果类型形状**：用抽象 record 加私有构造器、嵌套 sealed record。外部无法新增分支，迁移 Task 可以 `switch` 穷举，通道方法直接收强类型分支。`Session` 放在基类，三种单人结果都带会话。普通或不在副本不带普通副本快照，与 BD-002 一致。
- **RunId 比较**：`BossTowerSnapshot.RunId`、`IllusionTowerSnapshot.RunId` 与 `DungeonParticipationOwner.RunId` 都是 `Guid`，直接用 `==` 比较，无需转换。普通副本的 `DungeonRunId.Value` 只在测试替身里用到。
- **只问登记的那座塔**：`Locate` 按登记模式只调用对应塔的 `Find(session)`，另一座塔不会被查询（Boss 塔的 `Find` 在可写事务里有观察副作用）。单元测试 `Only_the_registered_tower_is_asked` 钉住这一点。
- **`LocateParty`**：候选先 `Distinct()`（与现行 `BossTowerCapture` 一致，按 record 值相等），再逐个 `Locate`；按候选顺序取塔快照，并按 `RunId` 去重。候选收集仍留给组队代码。
- **登记替身放在 `tests/Support/`**：`ProtocolAlgorithmFixture` 本身在 `tests/Support/`，并链接进三个项目，默认值必须能在这三个项目中编译，因此替身与夹具放在同一目录，按同样方式链接。
- **按角色查塔的两种途径**：生产登记按角色，塔按精确会话。要在替身里复现会话代次窗口，塔替身得能按角色说出所在副本。为此设了可选接口 `ICharacterTowerSeats`，表征世界的两个塔替身实现了它。其余只实现 `Find(session)` 的替身（`TowerDomainStub`、`DispatchProxy` 包装的塔、真实塔服务等），回退为“取该角色的当前会话再调用 `Find`”。这种回退表达不了代次窗口；这类替身本来也不持有旧代次座位，所以不影响结论。没有提供会话目录时不登记塔。
- **默认登记只给必填参数**：Application 层的真实服务把登记作为可选参数（默认 null）。如果把替身直接塞进 `Complete` 的值列表，这些服务会拿到它，入场时调用 `TryClaim` 就会抛异常。因此只在参数无默认值时提供；迁移 Task 给处理器新增的必填依赖会自动得到它。

### Checks

- 构建：`cd Server && dotnet build 86JP.Server.slnx` → 0 个警告，0 个错误（变异还原并 `touch` 后重新构建，仍为 0/0）。
- `git grep -n "TowerProtocol" -- Server` 无结果；另用 `grep -rn TowerProtocol src tests --include='*.cs'` 检查了未跟踪的新文件，同样无结果。
- 定位能力单元测试 `DungeonLocatorTests`：17/17 通过。覆盖如下：
  - 无登记；登记普通；登记普通但普通副本已找不到（离队后尚未结束本局）；
  - Boss 塔、幻影塔各自被认可（附会话与快照；另一座塔未被查询）；
  - 会话代次窗口 ×2 塔；RunId 不一致 ×2 塔；登记 Boss、但只有幻影塔认这个会话；
  - 整队位置：只有队员在 Boss 塔 → Boss；Boss 与幻影并存 → Boss；只有幻影（含一名普通候选）→ 幻影；多个 Boss 副本按候选顺序附带、按 RunId 去重，重复候选只定位一次；多个幻影副本同理；全员城镇或普通 → 普通或不在副本；队员的塔座位只被旧代次持有 → 普通或不在副本。
- `DerivedDungeonParticipationTests`：2/2 通过。
- TASK-001 表征测试 `DungeonOwnerRoutingCharacterizationTests*`：155/155 通过，断言未改。
- 变异检查：每项都先备份，变异后构建并运行 `DungeonLocatorTests|DerivedDungeonParticipationTests`（共 19 个），再还原、`touch`、重新构建；最后用 `cmp` 确认文件与备份一致，`git diff` 中无变异。

  | # | 变异 | 失败用例 |
  |---|---|---|
  | M1 | `Locate` 的 Boss 分支去掉 `run.RunId == runId` | `Tower_run_other_than_the_registered_one_is_ordinary_or_none(BossTower)` |
  | M2 | 登记为 Boss 时，塔不认这个会话也返回 `InBossTower` | `Tower_seat_held_by_an_older_generation_is_ordinary_or_none(BossTower)`、`Tower_run_other_than_the_registered_one…(BossTower)`、`Only_the_registered_tower_is_asked`、`Party_member_whose_tower_seat_is_held_by_an_older_generation_does_not_place_the_party`（4 个失败） |
  | M3 | `LocateParty` 先判幻影塔，后判 Boss 塔 | `Party_prefers_the_boss_tower_over_the_illusion_tower` |
  | M4 | `LocateParty` 的 Boss 副本去掉按 RunId 去重 | `Party_carries_every_distinct_boss_run_in_candidate_order` |
  | M5 | `ProtocolAlgorithmFixture` 不再默认提供替身，退回 `Unused` 代理 | `Fixture_registers_a_session_seated_by_a_supplied_tower_stand_in` |

- 完整测试：`cd Server && dotnet test 86JP.Server.slnx`，退出码 0，输出见 `.rks-agent/tasks/dungeon-owner-routing/logs/task-002-full-test.log`。

  | 项目 | 通过 | 跳过 | 总计 | 对照 TASK-001 round2 |
  |---|---|---|---|---|
  | Dfo.Pvf.Tests | 73 | 9 | 82 | 相同 |
  | Dfo.Server.Persistence.Tests | 484 | 0 | 484 | 相同 |
  | Dfo.Server.HttpApi.Tests | 59 | 0 | 59 | 相同 |
  | Dfo.Server.Infrastructure.Tests | 655 | 348 | 1003 | 相同 |
  | Dfo.Server.Protocol.Tests | 1921 | 60 | 1981 | +19（本 Task 新增 17+2） |
  | Dfo.Server.Networking.Tests | 189 | 20 | 209 | 相同 |
  | Dfo.Server.Application.Tests | 5512 | 493 | 6005 | 相同 |
  | Dfo.Server.Host.Tests | 200 | 12 | 212 | 相同 |

  无失败，无需归因。跳过均为既有的 PVF 依赖条件跳过（本机缺 PVF），各项目的跳过数与 TASK-001 基准完全一致。

### 夹具盘点表

“已补”指本 Task 已让它在定位能力接入后能看到副本占用；“迁移 Task”指留待 T-003～T-005 按实际失败处理。

| 夹具 / 构造方式 | 涉及处理器 | 状态 | 说明 |
|---|---|---|---|
| `DungeonOwnerRoutingWorld`（显式提供登记） | 13 个命令的全部处理器 | 已补 | 改用共享替身，塔替身实现 `ICharacterTowerSeats`，能复现会话代次窗口与 `Residual` |
| 经 `ProtocolAlgorithmFixture.Create` 构造的驱动：`TowerAlgorithmDrivers`（Dependencies）、`DungeonExitDrivers`、`LoadingAlgorithmTestDriver`、`PartyMutationTestDriver`、`ExactTowerPhysicalClosureTests`、`BossTowerPublicHandlerTests`、`GetItemHandlerTests`、`PickupProjectionDriver`、两个 `GroundPickupAlgorithmDriver`、`MonsterDeathHandlerTests`、`ObjectDestructionAlgorithmDriver` | 拾取、怪物死亡、加载、组队类 | 已补（默认值） | 处理器新增必填的登记参数后自动得到推出式替身；按 `ICharacterTowerSeats` 或“当前会话 + `Find`”识别塔，按 `runs.Find` 识别普通副本。前提是测试提供了会话目录；未提供会话目录的用例无法识别塔，迁移时若失败，补传会话目录即可 |
| `TowerDomainStub` 的使用方（`FinishQuestHandlerTests`、`CreatureProjectionDeliveryTests`） | 非 13 个命令 | 不受影响 | 这些处理器不调用定位能力；`TowerDomainStub` 经会话回退已验证可被识别（`DerivedDungeonParticipationTests`） |
| `CharacterDeathHandlerTests.cs:143`，`new GameCharacterDeathCommandHandler(...)` 手工构造 | 0x0028 | 迁移 Task（T-003） | 构造器尚无登记参数，现在无法补；新增参数后会编译失败，届时传 `DerivedDungeonParticipation` |
| `LeaveDungeonTests.cs:515`，`new GamePartyTransitionCommandHandler(...)` 手工构造 | 0x0048 等 | 迁移 Task（T-004/T-005） | 同上 |
| `DungeonSelectionTestDriver.cs:39`，`new GameDungeonSelectionCommandHandler(...)` 手工构造 | 0x0084、0x000F | 迁移 Task（T-004） | 同上 |
| `DungeonRevivalTests.cs:194`（Application.Tests），`new GameUseRevivalCoinCommandHandler(...)` 手工构造 | 0x0029 | 迁移 Task（T-004） | 同上；该项目已链接共享替身 |
| Application.Tests 的 RouteDriver（`GroundPickupRouteDriver`、`CharacterDeathRouteDriver`、`MonsterDeathRouteDriver`、`MapLoadingRouteDriver`）用 `ActivatorUtilities.CreateInstance(services)` | 拾取、角色死亡、怪物死亡、加载 | 无需补（预期） | 服务来自真实 `ApplicationModule` 容器，登记是真实的 `DungeonParticipationDomainService`，由真实服务在事务内登记，与生产一致；迁移后若失败再查 |
| `BossTowerFrameIntegrationTests`（Networking.Tests）：真实 `ApplicationModule` 加替身 Boss 塔 `Tower` | 经网络入口的 Boss 塔命令 | 迁移 Task | 容器中的登记是真实服务，替身塔不会调用 `TryClaim`，定位会落到普通。不能整体换成推出式替身：同一容器里的真实普通副本服务需要 `TryClaim`。迁移时可在替身塔所在测试中预置登记，或为该替身包一层登记；Networking.Tests 目前未链接共享替身 |
| `IllusionTowerStageFrameIntegrationTests`（Networking.Tests）：真实幻影塔服务 | 幻影塔命令 | 无需补（预期） | 真实服务入场时在事务内登记 |
| `EntryHandlerDriver`（`new GameSelectDungeonCommandHandler`） | 0x0010 | 不受影响 | 0x0010 只改用新的陈旧定义，不接定位能力 |
| Host.Tests 的完整宿主 | 全部 | 无需补 | 生产组合 |

### Remaining

- 处理器接入 `DungeonLocator` 及上表“迁移 Task”各行，由 T-003～T-005 完成。
- 本 Task 未发现新的“登记与现行查询不一致”的窗口。
- 未提交、未推送；`TowerProtocol.cs` 的删除留在工作区，未暂存。

## Result Review Round 1

- 审查对象：Task Unit Contract Revision 1；执行结果为上方 `## Result`（Outcome: done）。改动是基准 HEAD aee80420 之上的未提交工作区，包括 `Server/src` 中 9 个修改、1 个删除、2 个新增，以及 `Server/tests` 中 6 个修改、3 个新增。
- 依据：`reviewing-work` 的 Task Unit 与 Code 准则（Task 局部范围）；spec.md Definitions（位置、整队位置）、RQ-001、RQ-003、AC-001、AC-003；plan.md BD-001、BD-002、BD-004；evidence/background-7adec858.md §1.3、§2.1～§2.4。
- 独立核查：
  - **改名**：`git diff -- Server/src` 中 19 处都是同一行内 `TowerProtocol.IsStale` → `GameRequestFreshness.IsStale` 的替换，没有移动任何行，也没有改动周边逻辑。`GameRequestFreshness.IsStale` 的判定式与被删的 `TowerProtocol.cs` 逐字一致。`git grep TowerProtocol -- Server` 与对未跟踪文件的 grep 都无结果。测试驱动 `TowerAlgorithmDrivers.cs` 中的 2 处同样只是原地改名。
  - **`Locate`**（`DungeonLocator.cs:48-65`）：登记为 Boss 时只问 Boss 塔的 `Find(session)` 并比较 `RunId`（两边都是 `Guid`，用 `==`），幻影塔同理，其余一律 `OrdinaryOrNone`。与 Spec“位置”定义逐条一致。
  - **只问登记那座塔，与现行“依次查 Find”何时不同**：只有三种组合，生产中都没有窗口：
    - 登记为空、但塔 `Find(session)` 非空；
    - 登记为一座塔、但另一座塔认这个会话；
    - 登记的 RunId 与塔快照不一致。

    依据：evidence §2.1、§2.3 指出两塔的登记与运行变更在同一事务内，执行器串行；`DungeonParticipationMode` 只有 Ordinary、IllusionTower、BossTower 三个值。唯一真实存在的差异窗口是 W1（会话代次），Spec 已规定其结果，TASK-001 也有表征。
  - **`LocateParty`**（`:73-96`）：
    - 候选 `Distinct()` 用的是 record 值相等，与现行 `BossTowerCapture` 一致；
    - Boss 优先，按候选顺序 `DistinctBy(RunId)`；
    - 现行 `Coordinate*PartyAsync` 只在 `Runs.Count == 0` 时返回 NotHandled（`GamePartyTransitionCommandHandler.Stages.cs:503-506`、`:222-225`），因此“有任一 Boss 副本就由 Boss 通道处理”与现行等价。`Runs.Count > 1` 时的拒绝仍由通道负责。
  - **`DungeonLocatorTests`**（17 个）：AC-003 的每种情形都有对应用例，即普通、城镇、Boss、幻影、两塔各自的代次窗口与 RunId 不一致、离队后尚未结束本局、整队 Boss 优先、幻影次之、多副本按候选顺序附带并去重、整队全在城镇或普通、队员只由旧代次占座。另有“只问登记那座塔”的用例。
  - **重做变异 M3**（`LocateParty` 改为先判幻影）：只有 `Party_prefers_the_boss_tower_over_the_illusion_tower` 失败，与 Result 一致。用备份还原后 `cmp` 一致，`touch` 后整解重建。
  - **最终状态**：构建 0 警告、0 错误；`DungeonLocatorTests`、`DerivedDungeonParticipationTests` 与 TASK-001 表征测试合计 174 个全部通过。表征测试文件的 `git diff` 为空，断言未改。`logs/task-002-full-test.log` 中 8 个测试项目都是“已通过”，没有“失败!”。

```yaml
kind: rks-review
verdict: approved
```

- Verdict: approved
- Findings:
  - 阻断：无。
  - 非阻断（交 T-003～T-005 注意）：
    1. **推出式登记替身遇到不实现 `ICharacterTowerSeats` 的塔替身时，按“角色的当前会话”反查塔**（`Server/tests/Support/DerivedDungeonParticipation.cs:49-53`）。生产登记的含义是“角色的任一座位”。两者只在一种情况下分歧：夹具中请求者的会话不是目录里该角色的当前会话，例如同一角色两代会话并存、旧连接仍能被查到。这时替身不登记，定位落到普通，而生产会进塔。这种分歧只会表现为迁移后测试失败，不会把错误的定位放过。迁移 Task 遇到这类失败时，应当让该塔替身实现 `ICharacterTowerSeats` 或显式提供登记，不应修改断言。
    2. **`ProtocolAlgorithmFixture` 只为没有默认值的 `IDungeonParticipationDomainService` 参数提供替身**（`ProtocolAlgorithmFixture.cs:33-34`）。处理器接入时，这个参数必须声明为必填；若写成 `= null`，夹具会传入 null，定位时直接抛 NRE。建议在 T-003 合约中写明“处理器以必填参数注入登记服务”。
    3. **推出式替身按构造与塔的 `Find` 一致**，所以经该夹具的既有用例无法发现“绕过定位、直接查塔 `Find`”的回退。这一类回退由 TASK-001 的代次窗口表征（它使用 `ICharacterTowerSeats`）和 T-006 的结构守护负责，不需要本 Task 处理。
    4. **Result 写“完整测试退出码 0”**，但 `task-002-full-test.log` 末尾没有记录退出码（TASK-001 round2 日志有 `exit=0` 行）。8 个项目全部“已通过”，结论成立，建议补记以保持证据格式一致。
