# TASK-006 — 结构守护、文档与收尾

## Task Unit Contract

- Revision: 1
- Outcome and references：交付 Plan T-006（BD-005、BD-006），覆盖 Spec RQ-007、RQ-008、AC-001、AC-005、AC-006，并清理迁移后留下的失效路径。
  - 新增能拒绝反例的结构守护：协议层生产代码中，陈旧判定、副本归属查询只出现在允许的位置。
  - 在 `Document/ProtocolCommands.md` 中写入分发结构与陈旧帧规则，作为唯一归属。
  - 删除 `BossTowerPartyAttempt` / `IllusionTowerPartyAttempt` 中生产代码里恒为 true 的 `Handled` 字段。
- Owned boundary：
  - 测试：新增结构守护测试，放在 `Server/tests/Dfo.Server.Protocol.Tests/` 下。
  - 生产代码：
    - `Server/src/Dfo.Server.Protocol/Game/BossTowerPartyCoordination.cs`、`IllusionTowerPartyCoordination.cs`；
    - `GamePartyTransitionCommandHandler.Stages.cs` 中构造这两个记录的地方；
    - 守护发现的残留问题（如遗漏的顺序注释）。
  - 测试驱动 `Server/tests/Support/TowerAlgorithmDrivers.cs` 以及断言 `result.Handled` 的测试：
    - `BossTowerProjectionConcurrencyTests`、`IllusionTowerProjectionConcurrencyTests`；
    - `PartyTowerFootprintBoundaryTests`、`IllusionTowerDomainServiceTests`；
    - `ProtocolAlgorithmFixture` 中的引用。
  - 文档：`Document/ProtocolCommands.md`；其他提到旧顺序规则的现行文档只改为链接。
- 授权的断言改动：`Handled` 字段删除后，驱动在整队位置不是本塔时返回 null（或等价的“未进入通道”结果）。测试中的 `Assert.True(result.Handled)` 改为对应的“已进入通道”断言，例如 `Assert.NotNull(result)`。除此之外不改任何断言。
- Required checks：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。
  - 守护测试通过，且附带反例：至少各有一段违规源码文本被守护判定为违规。
    - 通道里出现陈旧判定；
    - 路由之外出现 `bossTowers.Find(` 路由查询；
    - 白名单之外出现 `participation.Find(`。
  - 变异检查：在真实生产文件里临时加回一处违规，例如在 `KillIllusionAsync` 中加回 `illusionTowers.Find`，或在 0x000F 的普通选图前再 `Locate` 一次。确认守护失败；还原后 `touch` 再构建，用 `cmp` 确认一致。
  - `git grep -n "TowerProtocol\|Handled" -- Server/src/Dfo.Server.Protocol/Game/*PartyCoordination.cs` 中，`Handled` 不再作为字段出现。
  - 文本检查：`Document/ProtocolCommands.md` 新节描述以下内容；`git grep` 确认三处旧顺序注释不再存在。
    - 入口判陈旧（先于会话查询）与陈旧帧定义；
    - 以占用登记定位与“对应服务不认可即为普通或不在副本”；
    - 整队位置；
    - 单次路由与 0x000F 例外；
    - 陈旧帧在任何位置丢弃；
    - 守护测试的位置。
  - 完整测试：`bash -c 'cd Server && dotnet test 86JP.Server.slnx 2>&1 | tee <log>; echo "exit=${PIPESTATUS[0]}" >> <log>'`，日志写到 `.rks-agent/tasks/dungeon-owner-routing/logs/task-006-full-test.log`，失败须归因。
- Dependencies：TASK-005
- Result review：required。理由：守护的白名单过宽，会让后续回退悄悄通过，而这一点守护自己的测试暴露不了；文档是规则输出，同样需要独立核对与代码一致。

## Detailed Plan

1. **结构守护（BD-005）**：
   - 扫描 `Server/src/Dfo.Server.Protocol/**/*.cs` 的生产源码，排除 `obj/`。实现方式自定：
     - 测试项目已引用 Roslyn 时，优先按语法树找方法；
     - 否则用基于文本的方法体提取。
   - 不扫描测试驱动：`TowerAlgorithmDrivers`、`BossTowerPublicHandlerTests` 的 `LoadingFlow` 中复刻的路由属测试夹具。
   - 规则一：`GameRequestFreshness.IsStale(` 只允许出现在白名单方法中。白名单是 13 个命令的路由方法，加上 0x009F 与 0x0010 的入口，每项注明命令。
   - 规则二：两塔服务的 `.Find(`、`.IsInTower(` 只允许出现在 `DungeonLocator` 与一份非路由白名单中，每项注明理由。白名单包括：
     - 投影内部查成员（`BossTowerActionProjection`、`IllusionTowerActionProjection`）；
     - 入场后的精确性校验（`BossEntryCommandPhases`、`IllusionEntryCommandPhases`）；
     - 计时器回城（`TowerReturnCommandHandler.Stages`）；
     - `*TowerExactSessions` 的精确会话校验；
     - 0x009F 的单通道。

     先 `git grep` 出现状，逐项分类，不能把路由用法放进白名单。
   - 规则三：`IDungeonParticipationDomainService` 的 `.Find(` 在协议层只允许出现在 `DungeonLocator`。
   - 规则四：`DungeonLocator.Locate(` / `LocateParty(` 只允许出现在路由方法与 `LocatePartyOf` 中。0x000F 的路由方法只调用一次，防止 TASK-004 变异 M2b 那类“释放后再定位”的回退。
   - 每条规则配一个反例用例：把一段违规源码文本交给同一个判定函数，断言报告违规。
2. **删除 `Handled`**：
   - 两个 `*PartyAttempt` 记录删除 `Handled` 字段，生产构造处同步调整。
   - `TowerAlgorithmDrivers` 在定位不到本塔时返回 null 或等价结果。
   - 测试按“授权的断言改动”调整。
   - `ProtocolAlgorithmFixture` 中若有引用，一并处理。
3. **文档（BD-006）**：
   - 在 `Document/ProtocolCommands.md` 的“CMD 的边界”之后，新增一节“副本归属分发与陈旧帧”。按 `Document/DocumentationGuidelines.md` 只写当前规则、设计理由与守护位置，不写演化过程。
   - `git grep` 现行文档中描述旧顺序规则的段落，改为链接到这一节：`Document/LordTower-*.md` 中“按当前玩法分流”、各入口行为表等。
   - `Document/tasks/` 下的历史任务记录不改。
4. **检查**：按 Required checks 执行，结果写入 Result。

## Result

- Outcome：done。
  - 新增协议层源码结构守护 `DungeonOwnerRoutingSourceTests`（8 个用例，含 6 个反例用例）。7 项真实生产文件变异全部被守护拒绝。
  - `Document/ProtocolCommands.md` 新增“副本归属分发与陈旧帧”一节，作为唯一归属；`LordTower-Design` 中的分流描述改为链接到该节。
  - 删除了 `BossTowerPartyAttempt` / `IllusionTowerPartyAttempt` 的 `Handled` 字段。测试驱动在整队位置不是本塔时返回 null。
  - 三处旧顺序注释在 `Server/`、`Document/` 中已不存在（TASK-003、TASK-004 已删除），`TowerProtocol` 已不存在。
  - 无 Blocker。

### Changed

| 文件 | 改动 |
|---|---|
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonOwnerRoutingSourceTests.cs`（新增） | 结构守护：4 条规则 + 路由方法内的顺序检查，含白名单（逐项注明理由与固定次数）与反例 |
| `Server/src/Dfo.Server.Protocol/Game/BossTowerPartyCoordination.cs`、`IllusionTowerPartyCoordination.cs` | 删除 `bool Handled` 字段 |
| `Server/src/Dfo.Server.Protocol/Game/GamePartyTransitionCommandHandler.Stages.cs` | 8 处 `new(true, …)` 改为 `new(…)`，即两塔各 4 处构造（成功、拒绝、两处结果返回） |
| `Server/tests/Support/TowerAlgorithmDrivers.cs` | `BossTowerPartyDriver` / `IllusionTowerPartyDriver` 的 `Try*Async` 改为返回 `*PartyAttempt?`。`CoordinateAsync` 改为 `async`：整队位置是本塔时 `await` 真实通道，否则返回 null，不再伪造 `(false, CallerNotCurrent)` |
| `BossTowerProjectionConcurrencyTests.cs`、`IllusionTowerProjectionConcurrencyTests.cs`、`PartyTowerFootprintBoundaryTests.cs`、`IllusionTowerDomainServiceTests.cs` | 授权改动：11 处 `Assert.True(x.Handled)` 改为 `Assert.NotNull(x)`（两个 Protocol 文件各 4 处，Footprint 2 处，IllusionTowerDomainServiceTests 1 处）。另有 13 处原本不断言 `Handled` 的解引用补了 `!`，见 Why |
| `Document/ProtocolCommands.md` | 在“CMD 的边界”之后新增“副本归属分发与陈旧帧” |
| `Document/LordTower-Design-20261019.md` | 第 63 行“按当前玩法分流”后补一句链接，指向新节；0010 的分流描述不变 |

`ProtocolAlgorithmFixture.cs` 中的 `Handled` 是 `DispatchProxy` 拦截决定的元组字段，与 `*PartyAttempt` 无关，未改。生产代码中也没有读取 `.Handled` 的地方。

### Why

**守护实现方式**
- 测试项目没有引用 Roslyn。新增包要改 `Directory.Packages.props`，超出边界。因此沿用 #200 `ItemLifetimeSourceTests` 与 `GameplayWriteEntryTableTests` 的文本扫描做法，只扫描 `Server/src/Dfo.Server.Protocol/**/*.cs`，排除 `bin/`、`obj/`。测试驱动不在扫描范围内。
- 先把注释替换为空格（保留换行与字符串），再归属命中：命中位置属于其上方最近的成员声明，声明须以访问修饰符开头。与先例的 `Declaration` 正则相同，另排除了 `static` 等修饰词被误当成方法名（`internal static (…) Resolve(` 的情形）。
  - 同一文件内的重名成员按参数类型区分，例如 `HandleAsync(CommandExecutionContext, GameFinishLoadingCommand, CancellationToken)`。
  - 命中位置正是被声明成员的名字时（`IsStale`、`LocatePartyOf` 的声明本身），不计为调用。
- 四条规则，每个允许位置都固定出现次数。因此违规有两种：命中出现在未列出的成员中；或者列出的成员命中次数不等于固定值，例如 0x000F 路由方法多一次 `Locate`。
  1. **Stale**：`IsStale(` 或 `SequenceIsFresh`。后者用来拦住内联写回判定式。
  2. **TowerLookup**：`<接收者>.Find(` / `.IsInTower(`。接收者取自源码中以 `IBossTowerDomainService` / `IIllusionTowerDomainService` 声明的名字，现状为 `bossTowers`、`illusionTowers`、`towers`。这样不靠固定变量名，也覆盖 `?.` 写法。
  3. **ParticipationLookup**：以 `IDungeonParticipationDomainService` 声明的接收者的 `.Find(`。
  4. **Locate**：`DungeonLocator.Locate(` / `LocateParty(` 与 `LocatePartyOf(`。
- 另加一项**路由顺序检查**：在 11 个路由方法体内，第一次 `FindByConnection(`、`DungeonLocator.` 或 `LocatePartyOf(` 不得早于陈旧判定。这条检查对应 RQ-002“先于会话查询、定位”。0x009F、0x0010 不参与，因为 0x0010 现行在取会话之后才判陈旧，不在本规格范围内。
- 每条规则配一个反例用例：把违规源码文本交给同一个 `Violations` 函数。另有：
  - `The_listed_sites_alone_are_clean`：只含白名单的合成源码，无违规；
  - `Comments_and_other_lookups_are_not_routing`：注释、`runs.Find`、`sessions.FindByConnection`、快照上的 `Find` 不计入。

**白名单分类**：分类前先用 `git grep` 列出现状（见 Checks“grep 检查”），逐项判定。没有任何路由用法进入白名单。
- **Stale（14 项，各 1 次）**：
  - `GameRequestFreshness.IsStale`：唯一定义，内含 `SequenceIsFresh`；
  - 11 个路由方法，覆盖 13 个命令：0x002B `GameGetItemCommandHandler.HandleAsync`；0x0028 `GameCharacterDeathCommandHandler.HandleAsync`；0x0027 `GameMonsterDeathCommandHandler.RunAsync`；0x0025、0x002D 为 `GameMapLoadingCommandHandler` 的两个 `HandleAsync` 重载；0x0048 `EplpAsync`；0x0084 `BackToVillageAsync`；0x000F `EnterSelectDungeonAsync`；0x0029 `GameUseRevivalCoinCommandHandler.RunAsync`；0x002A `GiveUpAsync`；0x000D/0x000E/0x0079 共用 `HandleMutationAsync`；
  - 0x009F `StartIllusionAsync`；
  - 0x0010 `SelectDungeonAsync`。
- **Locate（12 项，各 1 次）**：11 个路由方法，加上 `LocatePartyOf`（唯一一处 `LocateParty` 调用）。0x000F 固定为 1 次，因此 M2b 类“释放后再定位”会失败。
- **ParticipationLookup（1 项）**：`DungeonLocator.Locate`。
- **TowerLookup（28 项，共 32 次）**：
  - 定位器：`DungeonLocator.Locate` 2 次，只问登记指向的那座塔。
  - **精确会话校验**：`BossTowerExact` / `IllusionTowerExact`，核对定位所得会话仍在同一副本与房间版本；`BossTowerExactSessions` / `IllusionTowerExactSessions`，组队通道在写入前核对整局成员都精确在座。
  - **投影内部查成员**：`*TowerExactMembers`（收件人）、`*TowerCurrentRun`（写后经仍精确在座的成员重读副本）、`*TowerSendCommittedBatchAsync`（重试已提交批次前，核对结算回执不变）。
  - **离开投影**：`ProjectBossTowerPartyDepartureAsync` / `ProjectIllusionTowerPartyDepartureAsync`，确认塔已不再持有返回者后才投影回城。这三对方法分别位于 `GamePartyTransitionCommandHandler.Stages.cs`、`GameDungeonSelectionCommandHandler.Stages.cs`、`TowerReturnCommandHandler.Stages.cs`，共 6 项，是重复的投影代码。
  - **入场后精确性校验**：`BossEntryCurrent` / `IllusionEntryCurrent`。
  - **计时器回城**：`ReturnBossTowerRunAsync` / `ReturnIllusionTowerRunAsync`。
  - **0x009F 单通道**：`StartIllusionAsync`。
  - **其他命令自身的规则（拿不准，交审查判断）**：这些用法任务描述未预先列出，都只查幻影塔（变量名 `towers`）。它们不属于 13 个命令，也不在两条通道之间做选择：
    - `CreatureRuntimeDelivery.Resolve` 1 次、`VisibleAsync` 2 次、`RenameCreatureHandler.SendVisibleAsync` 2 次：宠物通知的收件人范围；
    - `GameFinishQuestCommand.HandleAsync` 2 次：任务完成外观发给同局塔友；
    - `GameSortItemCommand.HandleAsync` 1 次：在副本或幻影塔内拒绝整理仓库；
    - `RecoverStaminaSlice.RecoverAsync` 1 次：在副本或幻影塔内拒绝恢复疲劳。

    这几处只问幻影塔，不问 Boss 塔，可能是业务缺口，但不在本规格范围内，本 Task 未改。列入白名单后，守护会钉住现状次数，新增同类用法必须登记理由。
- **授权断言改动之外的 `!`**：驱动返回可空后，原本不断言 `Handled`、直接读 `x.Result` 的 13 处（Boss/幻影 Projection 测试各 6 处，`IllusionTowerDomainServiceTests:1094` 1 处）出现 CS8602（TreatWarningsAsErrors），所以在被断言对象上补了 null 容忍运算符，例如 `Assert.True(result!.Result.Succeeded)`。
  - 断言的比较对象与期望值都未改。
  - 若驱动返回 null，原先读到的是伪造的 `CallerNotCurrent` 拒绝，断言会失败；现在抛出 NullReferenceException，同样失败，严格程度不变。
  - 请审查确认这不算断言改动。
- **越界说明**：`Document/LordTower-Design-20261019.md` 属于合约允许的“其他现行文档只改为链接”。其余改动都在 Owned boundary 内。`SpecialSoloDungeonsExploration`、`Issue119` 中的“分流”只是功能描述，不是顺序规则，未改。

### Checks

- **构建**：`cd Server && dotnet build 86JP.Server.slnx` → 0 个警告，0 个错误。变异全部还原并 `touch` 后，又做了一次 `--no-incremental` 整解重建，仍为 0/0。
- **守护测试与反例**：`dotnet test tests/Dfo.Server.Protocol.Tests --filter FullyQualifiedName~DungeonOwnerRoutingSourceTests` → 8/8 通过。真实源码共扫描 506 个文件，无违规。
  - `A_stale_judgement_in_a_channel_is_named`：通道 `AdvanceBossAsync` 中的 `GameRequestFreshness.IsStale`，以及 `KillIllusionAsync` 中内联的 `InboundOrdinal is > 0 && !SequenceIsFresh`，都报违规。
  - `A_route_that_looks_up_before_dropping_a_stale_frame_is_named`：在两个路由方法中把 `FindByConnection` 提到陈旧判定之前，两处都报违规。
  - `A_tower_lookup_for_routing_outside_the_locator_is_named`：以下 4 处都报违规：
    - `KillIllusionAsync` 中的 `illusionTowers.Find(`；
    - 路由之外的 `bossTowers.Find(` 与 `bossTowers?.IsInTower(`；
    - 定位器中多出的第 3 次查询（“holds 3, pinned 2”）。
  - `A_registry_lookup_outside_the_locator_is_named`：白名单之外的 `participation.Find(`（`PickupBossAsync`）报违规；以另一名字声明的登记 `registry.Find(` 也报违规。
  - `A_second_locate_and_a_locate_outside_a_route_are_named`：0x000F 路由多一次 `Locate` 报“holds 2, pinned 1”；`EnterOrdinarySelectionAsync` 中的 `Locate` 报未列出。
- **变异检查**：脚本 `/private/tmp/claude-501/-Users-puxianguo-project-DNF-JP-86-Server/808579c3-b965-4b38-9388-8be538d33465/scratchpad/t6/mutate.py`，日志在同目录 `mutate.log`。每项流程：
  1. 备份；
  2. 断言替换锚点唯一，再改真实生产文件；
  3. 构建 Protocol.Tests（每项都是 0 警告 0 错误，变异可编译）；
  4. 运行守护测试；
  5. 用备份还原、`touch`、`cmp`。7 项均为 identical。

  每项都只有 `Dungeon_owner_routing_reads_staleness_and_ownership_only_where_listed` 失败（失败 1，通过 7）：

  | # | 变异 | 守护报告 |
  |---|---|---|
  | G1 | `KillIllusionAsync` 通道开头加回 `illusionTowers.Find(place.Session)`（TASK-003 M2） | `MonsterTowerCommandPhases.cs:48 TowerLookup in KillIllusionAsync is not listed` |
  | G2 | 0x000F 路由在普通选图前再 `DungeonLocator.Locate` 一次 | `DungeonSelectionCommandPhases.cs EnterSelectDungeonAsync holds 2 Locate, pinned 1` |
  | G2b | `EnterOrdinarySelectionAsync` 开头重新定位（TASK-004 M2b） | `DungeonSelectionCommandPhases.cs:209 Locate in EnterOrdinarySelectionAsync is not listed` |
  | G3 | `DieBossAsync` 通道内加 `GameRequestFreshness.IsStale(metadata)` | `GameCharacterDeathCommandHandler.Towers.cs:28 Stale in DieBossAsync is not listed` |
  | G4 | `PickupBossAsync` 通道内加 `participation.Find(...)` | `GameGetItemCommandHandler.Towers.cs:30 ParticipationLookup in PickupBossAsync is not listed` |
  | G5 | 0x0048 `EplpAsync` 先 `FindByConnection` 再判陈旧 | `GamePartyTransitionCommandHandler.cs EplpAsync looks up the session or place before dropping a stale frame` |
  | G6 | Boss 组队通道 `CoordinateBossTowerPartyAsync` 内按调用者 `bossTowers.Find(caller)` | `GamePartyTransitionCommandHandler.Stages.cs:469 TowerLookup in CoordinateBossTowerPartyAsync is not listed` |

  还原后核对：在 `git diff -- Server/src` 的新增行中 grep 各变异片段，无命中（exit=1）；`git status` 只含本 Task 的改动。
- **grep 检查**：
  - `git grep -n "TowerProtocol\|Handled" -- Server/src/Dfo.Server.Protocol/Game/*PartyCoordination.cs` → 无输出（exit=1）。
  - `git grep -n "TowerProtocol" -- Server` → 无输出。
  - 三处旧顺序注释（“Boss lookup precedes stale”“The stale gate precedes session/run lookup”“Selection checks for an attached run before consuming a stale request”）：`git grep -- Server Document` → 无输出（exit=1）。它们只残留在 `.rks-agent/` 的 TASK-003、TASK-004 记录中，属历史过程记录。
  - 分类前的现状盘点：`git grep` 列出了 `IsStale(` 14 行（13 处调用 + 定义）、两塔 `.Find(` / `.IsInTower(` 32 处（含 3 处 `towers?.` 写法）、`participation.Find` 1 处、`Locate` 系 14 行（含 `LocateParty`、`LocatePartyOf` 两处声明）。守护的真实源码输出与之一一对应：Stale 14（13 处调用 + 定义中的 `SequenceIsFresh`）、TowerLookup 32、ParticipationLookup 1、Locate 12，合计 59 个命中。
- **文档文本检查**：`Document/ProtocolCommands.md` 新节中以下关键句各出现 1 次：
  - “先于会话查询、定位和任何领域写入”；
  - “InboundOrdinal > 0”（陈旧帧定义）；
  - “对应服务不认可，即为“普通或不在副本””；
  - “整队位置”；
  - “单次路由”；
  - “0x000F 例外”；
  - “陈旧帧在任何位置一律丢弃”；
  - “DungeonOwnerRoutingSourceTests”（守护位置）。

  该节只写当前规则、设计约束和守护位置，不写演化过程。
- **完整测试**：`bash -c 'cd Server && dotnet test 86JP.Server.slnx 2>&1 | tee <log>; echo "exit=${PIPESTATUS[0]}" >> <log>'`，日志 `.rks-agent/tasks/dungeon-owner-routing/logs/task-006-full-test.log`，末行 `exit=0`，无失败。

  | 项目 | 通过 | 跳过 | 总计 | 对照 TASK-005 |
  |---|---|---|---|---|
  | Dfo.Pvf.Tests | 73 | 9 | 82 | 相同 |
  | Dfo.Server.HttpApi.Tests | 59 | 0 | 59 | 相同 |
  | Dfo.Server.Infrastructure.Tests | 655 | 348 | 1003 | 相同 |
  | Dfo.Server.Persistence.Tests | 484 | 0 | 484 | 相同 |
  | Dfo.Server.Protocol.Tests | 1990 | 60 | 2050 | +8（新增守护） |
  | Dfo.Server.Application.Tests | 5512 | 493 | 6005 | 相同 |
  | Dfo.Server.Networking.Tests | 189 | 20 | 209 | 相同 |
  | Dfo.Server.Host.Tests | 200 | 12 | 212 | 相同 |

  跳过的都是既有的 PVF 等条件跳过。依赖 `Handled` 改动的测试类都在完整运行中通过，包括 Boss/幻影 `ProjectionConcurrencyTests`、`BossTowerDomainServiceTests`（含 `PartyTowerFootprintBoundaryTests`）与 `IllusionTowerDomainServiceTests`。

### Remaining

- **守护的已知边界**：
  - 不认没有访问修饰符的成员（与先例相同），固定次数可以兜住命中被归到相邻成员的情况；
  - 字符串字面量不清除，含查询文本的字符串会被误报为违规，方向偏严；
  - 接收者只认以服务类型声明的名字，用 `var` 从别处取得的塔服务看不到。
- 白名单“其他命令自身的规则”6 个成员（宠物通知、任务完成外观、整理仓库、恢复疲劳）只查幻影塔，不查 Boss 塔，可能是独立业务缺口。不在本规格范围内，留给审查判断是否另立任务。
- 13 处 `!` 是否可接受，请审查确认。
- `IllusionTowerPublicFlowTests` 在本机因缺 PVF 跳过（与 TASK-003～005 相同）。
- 未提交，未推送。

## Result Review Round 1

- 审查对象：Task Unit Contract Revision 1；执行结果为上方 `## Result`（Outcome: done）；改动是 HEAD 802e9cf8 之上的未提交工作区：
  - 生产代码：3 个修改；
  - 测试：5 个修改，1 个新增；
  - 文档：2 个修改。
- 依据：`reviewing-work` 的 Task Unit、Code 与 Deliverable 准则（Task 局部范围）；spec.md RQ-007、RQ-008、AC-001、AC-005、AC-006；plan.md BD-005、BD-006；evidence/background-7adec858.md §1.2、§5；TASK-003～005 审查中交给本 Task 的事项；`Document/DocumentationGuidelines.md` 的“只写当前规则”。
- 核对结论：
  1. **守护判定逻辑**（`DungeonOwnerRoutingSourceTests.cs:308-493`）：
     - 注释清除跳过字符串与字符字面量，包括逐字串与原始串。
     - 命中归属到其上方最近的、带访问修饰符的成员声明；重名成员按参数类型区分。
     - `Violations` 同时检查两件事：未列出的命中，以及已列出成员的命中次数是否等于固定值。路由顺序检查取路由方法体内第一次 `FindByConnection` / `DungeonLocator.` / `LocatePartyOf`，要求不早于第一次陈旧判定。
     - 接收者名字从源码中以服务类型声明的名字收集。
     - 6 个反例用例与真实扫描都经由同一个 `Violations` 函数，`Listed()` 只是合成源码输入，没有走另一套判定。
     - 我另做了一个自拟变异 X6：在普通通道 `PickupOrdinaryAsync` 中加入 `bossTowers?.IsInTower(...)`。守护报 `GameGetItemCommandHandler.cs:111 TowerLookup in PickupOrdinaryAsync is not listed` 并失败，判定可信。
  2. **白名单逐项核对**：用 detailed logger 跑真实扫描，命中合计 59 个（Stale 14、Locate 12、ParticipationLookup 1、TowerLookup 32）。逐个与 `Allowed` 对照：
     - 定位器 2 次，只问登记的那座塔。
     - 精确与整队精确校验：`*TowerExact`、`*TowerExactSessions`。
     - 投影内部：`*ExactMembers`、`*CurrentRun`、`*SendCommittedBatchAsync`。
     - 离开投影 6 项：例如 `GamePartyTransitionCommandHandler.Stages.cs:82-84` 是写入后的“塔已不再持有返回者才回城”判断，不是选择通道。
     - 入场后校验、计时器回城、0x009F 单通道。
     - 没有发现任何为 13 个命令选择通道的用法混入。
     - 关于 6 个其他功能成员：抽查了 `CreatureRuntimeDelivery.Resolve:172-180`、`GameFinishQuestCommand.HandleAsync:252-261`、`GameSortItemCommand.HandleAsync:44-48`、`RecoverStaminaSlice.RecoverAsync:148-151`。它们是其他命令自身的门禁或投影受众，evidence §1.2 也已把它们归为“不属于本次路由范围”。放进白名单并固定次数是合适的：既不误伤，以后新增同类用法也必须登记。至于它们只问幻影塔、不问 Boss 塔是否构成业务缺口，那是本规格之外的独立问题，见非阻断 2。
  3. **`Handled` 删除与断言改动**：
     - `git diff 802e9cf8 -- Server/src Server/tests` 的全部改动行逐行核对。生产代码中只删除了字段，8 处构造去掉了首个 `true`。
     - 测试中 11 处 `Assert.True(x.Handled)` → `Assert.NotNull(x)`，在授权范围内。
     - 另外 13 处只在被断言对象上补了 `!`，比较对象与期望值都没变。驱动返回 null 时，测试仍会失败，原先因伪造的拒绝值而失败，现在因空引用而失败，严格程度不变。这不算断言语义改动，可以接受。
     - 没有其他断言变化。
     - `git grep Handled -- Server/src/Dfo.Server.Protocol/Game/*PartyCoordination.cs` 无输出。
  4. **文档**：
     - `Document/ProtocolCommands.md` 新节覆盖合约要求的每一项：入口判陈旧与陈旧帧定义、以登记定位且“对应服务不认可即普通或不在副本”、整队位置、单次路由与 0x000F 例外、任何位置丢弃、守护位置与规则。内容与实现一致，只写当前规则。
     - `LordTower-Design-20261019.md:63` 只补了一条链接。
     - `git grep` 中其余提到陈旧判定的现行位置都在 `Document/tasks/**` 的历史任务记录中，按文档规范不需要改为链接。
     - 有一处措辞不精确，见非阻断 1。
  5. **守护的已知边界**：
     - 无访问修饰符的成员：命中会归到相邻成员，固定次数能兜住大部分情况。
     - 字符串字面量：会误报，方向偏严。
     - 经 `var`、属性或其他对象取得的塔服务：看不到。仓内统一采用构造器注入字段，这个盲区可以接受。
     - 守护的职责是拦住“通道里补判陈旧、为路由查塔或登记、再次定位”这几种常见回退，上述边界不妨碍这一职责。
- 独立核查：
  - X6 变异之后，用备份还原并 `cmp` 一致，`touch` 后整解重建：0 警告，0 错误。
  - `Game.Dungeons` 与 `*ProjectionConcurrencyTests` 共 299 个用例全部通过；`git status` 与审查前相同。
  - `logs/task-006-full-test.log` 中 8 个测试项目都是“已通过”，没有“失败!”，末行为 `exit=0`。

```yaml
kind: rks-review
verdict: approved
```

- Verdict: approved
- Findings:
  - 阻断：无。
  - 非阻断：
    1. **新节第 1 步写“第一步调用 `GameRequestFreshness.IsStale`”，与组队变更不完全一致。** 组队变更的 `HandleMutationAsync` 在判陈旧之前先调用 `request.DemandCurrent()`（`GamePartyTransitionCommandHandler.Stages.cs:717-719`），这是现行既有顺序，本规格未改它。`DemandCurrent` 既不是会话查询、定位，也不是领域写入，所以“先于会话查询、定位和任何领域写入”成立，但“第一步”不准确。建议改为“在会话查询、定位和任何领域写入之前调用”，或注明组队变更先校验请求当前性。另外，守护的顺序检查只覆盖会话查询与定位，不覆盖领域写入；新节对守护的描述与此相符，无需改。
    2. **规格外的独立问题，建议报告给用户并另行立项，本 Task 不处理。** 白名单中整理仓库（`GameSortItemCommand.cs:47`）与恢复疲劳（`RecoverStaminaSlice.cs:150`）只拒绝普通副本与幻影塔，不拒绝 Boss 塔；宠物通知收件人与任务完成外观只按幻影塔同局成员扩展受众。这可能是 Boss 塔接入时遗漏的业务缺口，也可能是有意为之，需要对照客户端行为确认。
    3. **`IllusionTowerPublicFlowTests` 在本机因缺 PVF 跳过**（与 TASK-003～005 相同），整体收尾时需在有 PVF 的环境中补跑。
