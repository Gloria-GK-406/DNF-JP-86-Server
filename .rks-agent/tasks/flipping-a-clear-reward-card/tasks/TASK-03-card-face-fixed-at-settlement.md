# TASK-03 — 一局的牌面在结算时定下，此后不变

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

在 `DungeonRunState` 上增牌面状态（四个牌位各自的内容与是否已翻开），
给 `IDungeonRunDomainService` 增「定下牌面」与「认领牌位」两个动作，
并新增一个编排用例的业务服务。

- Referenced spec requirements: RQ-002, RQ-004
- Referenced plan sections: `plan.md` 的 T-003；边界 BD-008, BD-009, BD-010, BD-007, BD-021
- Owned boundary: BD-008, BD-009, BD-010, BD-007, BD-021
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs`（扩展）
- `Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs`（扩展）
- `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`（新建）
- `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`（新建）
- `Server/src/Dfo.Server.Application/ApplicationModule.cs`（增注册行）
- `Server/tests/`（新增判据）

- Dependencies: TASK-02
- Required dependency outputs: TASK-02 的规则服务及其牌面记录类型
- Acceptance conditions:

1. 牌面住在 `DungeonRunState` 上——计划已定死，不得另建注册表。沿用既有的比较交换写法。
2. 定下牌面是幂等的：已定下的本局再次要求定牌不改变已有内容。
3. 认领牌位：成功返回该牌位内容，重复认领返回空。
4. 不新增清理路径；本局结束的三个既有出口自然带走牌面。
5. 业务服务自行准备事实与种子；规则服务不查任何世界状态。
6. 既有的本局判据仍全部通过。
7. 全量判据 不低于 1690 通过、0 失败、2 跳过。

- Task-specific authorization: 根契约（revision 2）的授权覆盖本任务，无收窄。合并、推送、建 PR、删分支或清理 worktree 不在其内。
- Required methods:

- `test-driven-development`：判据先行，再让它变绿。
- 分层按 `AGENTS.md` 与 `Docs/新功能接入规范.md`：内容源只被规则服务注入；
  规则服务不持有仓储、注册表、目录、存储或领域服务；handler 只做四类动作；
  命令处理器禁止嵌套 dispatch；能触达持久写的命令一律 Transactional。
- 归档读法在 XML doc remarks 里写明段落出处；读不出来的记为未查明，不填看似合理的数。
- `System.*` using 在前，本仓不做字母序。`TreatWarningsAsErrors`。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-007 | `dotnet test Server/86JP.Server.slnx --nologo --filter DungeonRun` | 重复定牌不改内容；重复认领第二次失败；既有本局判据仍通过 |
| CK-008 | `Non-command:` 检查业务服务与规则服务的构造器 | 规则服务依赖中无仓储、注册表、目录、存储或领域服务 |
| CK-S | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于 1690 通过、0 失败、2 跳过 |

- Required evidence: CK-007 的判据输出；CK-008 的构造器枚举；全量判据输出。

## Contract Amendment 1 — 免费脸与付费脸必须拿到不同的种子

**触发**：TASK-02 完成时报告，其规则服务**不据牌型分岔**——`ClearRewardOccasion` 带
`Kind`，但档案对两种牌用的是同一个 profile、同一条阶梯、同一个品级带、同一个池子，
所以同一个种子喂进去，免费脸与付费脸**逐位完全相同**。TASK-02 已用判据钉住这个行为，
并在接口注释里写明「要让两张脸不同，调用方必须给不同的种子」。

**控制器裁定：TASK-02 的选择正确，但保障不能停在注释上。**

选择正确的理由有二：其一，据牌型分岔就得发明一个没有档案依据的推导，
而本 Spec 的约束正禁止这件事；其二分层上「本局有两张脸」是**世界的事实**不是规则的事实——
规则服务回答「这个场合这个种子的脸长什么样」，「有几张脸、各用什么种子」属于本 Task。

但「接口注释里嘱咐调用方」是靠自律的保障，而本仓刚在上一个重构里为同类形状吃过亏
（签名允许传入不匹配的组合，只靠调用方守规矩）。

**因此本 Task 的 Acceptance conditions 增补两条**：

1. 一局的免费脸与付费脸由**不同的种子**产生，且两个种子都由本局已有的事实派生
   （不得取时钟、不得取共享生成器）——否则重连或重放会给出不同的东西，
   违反 RQ-002。
2. **有一条判据钉住「同一局的免费脸与付费脸不相同」**，且该判据是在本 Task 这一层写的
   （规则服务那一层已有判据钉住的是相反的事实：同种子必然相同）。
   仅在注释里写明不算满足本条。

判据设计提示：两张脸「不相同」这件事本身是概率性的——理论上两个不同种子可能抽出同样的东西。
判据应当钉的是**种子确实不同且确实各自派生**，而不是靠比较结果碰运气。

Spec 的验收边界（RQ-002）未变；本条是把它在本层落实的方式写死。

## Contract Amendment 2 — 断言规矩（扩宽版）与行尾

**一、可验证断言必须有判据或删去。** 本条自 TASK-01 起演进，在 TASK-02 的 round 2 扩宽，
本 Task 按扩宽后的版本执行：

> 注释里凡对**本文件之外的任何东西**作出的可验证事实断言——归档文件、仓库里的其他代码、
> 或参照服务端的行为——都必须要么有判据钉住，要么删去。

**「本服务器没有 X」这类全称否定尤其危险**：它读起来像常识，而验证它需要搜遍全仓。
TASK-02 就栽在这里——一句「本服务器没有 PC 房概念」被仓库里六处 `PcRoom*` 定义证伪。

原版规矩只框住了「档案文件」，那句关于**本仓代码**的断言因此从缝里漏了过去。

**二、行尾一律 LF。** TASK-02 有一个文件被整文件 CRLF 改写，`git diff` 从真实的 16/0
膨胀成 294/278，把改动埋进噪声里。本仓基线是 LF，`core.autocrlf=false`，且**没有
`.gitattributes` 兜底**——写出 CRLF 不会有任何东西提醒你。

交付前自查：`git diff --numstat` 与 `git diff --numstat --ignore-cr-at-eol` 两个数应当一致；
不一致说明有整文件行尾改写。

理由不是格式洁癖：本 Spec 没有行为 oracle，**完整变更评审就是唯一的 oracle**，
让评审看不见真正改了什么，比改错一行更贵。

## Execution Result — round 1

### 改了哪些文件、每处做了什么

| 文件 | 改动 | 净增删 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs` | `DungeonRunState` 增可空位置参数 `ClearRewardCardSet? ClearRewardCards`；新增 `ClearRewardCardId(Kind, Index)` 与 `ClearRewardCardSet(Free, Paid, Claimed)`（后者带 `FaceOf` / `SlotAt` / `IsClaimed` 三个只读读法）；接口增 `FixClearRewardCards` 与 `TryClaimClearRewardCard` | +95 / -1 |
| `Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs` | 实现上面两个动作，两处都沿用本文件既有的 `TryGetValue` → `with` → `TryUpdate` 比较交换循环 | +61 / -0 |
| `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs` | 新建：`ClearRewardSettlement(CharacterId, CharacterLevel, PartyMemberCount = 1)` 与 `ICardRewardDomainService`（`SettleAsync` / `ClaimAsync`，均 `ValueTask` + `CancellationToken`） | 新文件 64 行 |
| `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs` | 新建：`internal sealed` + 主构造器，只吃 `IClearRewardRuleService` 与 `IDungeonRunDomainService`；备好两个场合与两个种子、向规则服务各要一次脸、交给本局定下；已定下的本局直接返回既有牌面且不再问规则 | 新文件 83 行 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 增 1 行注册 + 3 行注释，紧挨 `IClearRewardRuleService` 那段 | +4（本任务） |
| `Server/tests/Dfo.Server.Application.Tests/World/DungeonRunClearRewardCardTests.cs` | 新建：本局层 11 条判据 | 新文件 263 行 |
| `Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs` | 新建：业务服务层 10 条判据 | 新文件 333 行 |

**另有三处非本任务清单内的改动，全部落在 Permitted files 的 `Server/tests/` 内**：
`Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/` 下的 `LeaveDungeonTests.cs`、
`ResultScreenTests.cs`、`SetPlayResultHandlerTests.cs` 各有一个手写的 `OneRun`
假实现 `IDungeonRunDomainService`；接口加了两个成员，它们不实现就编译不过（CS0535）。
三处各补 8 行，全部是 `throw new NotSupportedException(...)`，与该 fixture 里既有的其余
未用成员写法完全一致。**没有改动任何既有断言或期望值**，所以没有按越界处理；
若评审认为这仍算越界，改动本身可原样撤回并另行裁定。

### 三个已定死的决定

1. 牌面住在 `DungeonRunState` 上，无新注册表。我自己读了代码确认了三条依据：
   `TrySettle` 只 `current with { IsSettled = true }` 不调 `TryRemove`；
   `End` 是一行 `_runs.TryRemove`，且 `grep -rn "\.End(" --include=*.cs Server/src`
   实测正好三个调用点——`Protocol/Game/DungeonClear/DungeonClearSlice.cs:143`、
   `Protocol/Game/DungeonClear/LeaveDungeonSlice.cs:119`、
   `Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs:202`；
   `MonsterDrops` 确是同一条记录上的 `init` 属性。
2. `FaceFor(occasion, seed)` 一次给整个牌面，业务服务对每种牌型各调一次，共两次。
3. 免费牌与付费牌共用 `IClearRewardRuleService`，牌型是 `ClearRewardOccasion.Kind` 传入。

### 两个种子各自怎么派生

```
seed(kind) = ((ulong)(uint)run.Layout.Seed << 32) | ((uint)run.Layout.DungeonId ^ (uint)kind)
```

- 高 32 位是 `DungeonLayout.Seed`——本局入场时抽定并随 layout 一路带着的那个数，
  是本局已有的事实。
- 低 32 位是 `DungeonLayout.DungeonId` 与牌型作异或。异或而非混合，是为了让两种牌型
  在任何副本号下都不可能得出同一个种子。
- **没有读时钟，没有读共享生成器**：`SeedFor` 是静态纯函数，只吃 `DungeonRunState` 与
  `ClearRewardCardKind`。变异检查 M7（换成常量）与 M8（换成 `DateTime.UtcNow.Ticks`）
  都会红。

### 判据真实输出

**CK-S 全量（`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`）**

改动前基线（我自己先跑了一遍）：

```
Dfo.Server.Protocol.Tests      失败 0，通过 376，已跳过 0
Dfo.Server.Networking.Tests    失败 0，通过  51，已跳过 0
Dfo.Pvf.Tests                  失败 0，通过  60，已跳过 0
Dfo.Server.Application.Tests   失败 0，通过 822，已跳过 0
Dfo.Server.Persistence.Tests   失败 0，通过 244，已跳过 0
Dfo.Server.Host.Tests          失败 0，通过   4，已跳过 0
Dfo.Server.Infrastructure.Tests 失败 0，通过 204，已跳过 2
合计：1761 通过 / 0 失败 / 2 跳过
```

改动后：

```
Dfo.Server.Networking.Tests    失败 0，通过  51，已跳过 0
Dfo.Server.Protocol.Tests      失败 0，通过 376，已跳过 0
Dfo.Pvf.Tests                  失败 0，通过  60，已跳过 0
Dfo.Server.Application.Tests   失败 0，通过 843，已跳过 0
Dfo.Server.Persistence.Tests   失败 0，通过 244，已跳过 0
Dfo.Server.Host.Tests          失败 0，通过   4，已跳过 0
Dfo.Server.Infrastructure.Tests 失败 0，通过 204，已跳过 2
合计：1782 通过 / 0 失败 / 2 跳过
```

净增 21 条，全部在 `Dfo.Server.Application.Tests`（822 → 843）：本局层 11 条
（含一条三行 `[Theory]`）、业务服务层 10 条（含一条两行 `[Theory]`）。
跳过数仍为 2，与基线一致，且是同样那两条（运行输出里逐条列出的
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`）。
无假红出现，不需要隔离复跑。

**CK-007（`dotnet test Server/86JP.Server.slnx --nologo --filter DungeonRun`）**

```
已通过! - 失败: 0，通过: 25，已跳过: 0，总计: 25 - Dfo.Server.Application.Tests.dll
```

基线该 filter 是 14 条（`DungeonRunDomainServiceTests` 全部），现为 25 条：
既有 14 条一条不改、全部仍通过，新增 11 条来自 `DungeonRunClearRewardCardTests`。
业务服务那 10 条名字里没有 `DungeonRun`，用
`--filter "CardReward|DungeonRun"` 跑得 35 条全过。

**CK-008（构造器枚举，非命令）**

```
ClearRewardRuleService(IClearRewardRateSource, IDropItemPoolSource, ClearRewardRateOptions)
CardRewardDomainService(IClearRewardRuleService, IDungeonRunDomainService)
```

规则服务的依赖里没有 repository / registry / directory / store / 领域服务——这条由
TASK-02 已有的 `ClearRewardRuleServiceTests.The_rule_service_is_handed_no_reader_of_world_state`
逐项钉住，本任务未改动它，仍通过。业务服务这一侧由本任务新增的
`CardRewardDomainServiceTests.The_settlement_service_reaches_the_archive_only_through_the_rules`
钉住：它枚举出的两个参数**都不是内容源**，业务服务只经规则服务触达归档。

### 变异检查结果

逐条改坏一处、跑 `--filter "CardReward|DungeonRun"`、再还原。14 条全部转红，
**且第 11 条第一次跑时是绿的，暴露了一个真实的判据缺口，已补**（见下）。

| # | 改坏什么 | 结果 | 抓住它的判据 |
|---|---|---|---|
| M1 | 删掉 `FixClearRewardCards` 的"已定下就返回既有"分支 | RED | `Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing` |
| M2 | 删掉认领的 `claimed.Contains(id)` 去重 | RED | `A_position_is_claimed_once` 等 4 条 |
| M3 | `SlotAt` 不再检查下标范围 | RED | `A_position_the_face_does_not_have_is_refused` 三行全红 |
| M4 | 认领的键忘掉牌型（恒用 `Free`） | RED | `The_two_faces_are_claimed_independently`、`A_position_is_handed_over_once` |
| M5 | 空牌位改为拒绝认领而非认领成功 | RED | `A_position_that_holds_nothing_is_still_claimed_once` |
| M6 | 两张脸用同一个种子（`^ 0u`） | RED | `A_clear_fixes_two_faces_drawn_from_different_seeds`、`Both_seeds_are_derived_from_the_run_and_from_nothing_else` |
| M7 | 种子改成常量，不再来自本局 | RED | `Both_seeds_are_derived_from_the_run_and_from_nothing_else` |
| M8 | 种子改成读 `DateTime.UtcNow.Ticks` | RED | `Both_seeds_are_derived_from_the_run_and_from_nothing_else` |
| M9 | 删掉 `SettleAsync` 里"已定下就不再问规则"的早返回 | RED | `A_second_settlement_keeps_the_cards_already_fixed` |
| M10 | 场合的难度写死为 0 | RED | `The_occasion_is_assembled_from_the_run_and_the_settlement` 两行 |
| M11 | 场合的地图数写死为 3 | RED（**第一次为 GREEN**） | 同上 |
| M12 | 场合丢掉传入的队伍人数（写死 1） | RED | 同上 |
| M13 | `End` 不再移除本局 | RED | `Ending_the_run_takes_its_cards_with_it`、既有的 `Ending_a_run_leaves_the_character_in_none` |
| M14 | 删掉 `ApplicationModule` 的注册行 | RED | `The_registration_wires_the_settlement_service` |

**M11 的缺口与修补**：第一轮 M11 是绿的。原因是当时判据只用了一个 3 个房间的 layout，
而我为变异特意选的常量恰好也是 3——一个从常量来的地图数与它一致。修法不是改常量
（那只是换个数继续赌），而是让判据自己排除全部常量：新增第二个 5 个房间的 layout，
把该判据改成两行 `[Theory]`，并在判据内断言两个 layout 的房间数不相等。任何来自
本局之外的地图数都无法同时满足两行。补完后 M11 转红。

### 行尾自查

- 本仓 `core.autocrlf` = `false`（在本 worktree 实测）。
- **本仓根目录确实有 `.gitattributes`**——与本任务简报里"无 `.gitattributes` 兜底"的
  说法不符，我实测后按实测记。它只有一条规则 `protocol/** text eol=lf`，注释写明
  刻意只圈 `protocol/`。**`Server/` 树不在它的范围内**，所以就本任务改到的文件而言，
  "写出 CRLF 不会有东西提醒你"这个实质结论仍然成立。
- 本任务改到或新建的 10 个文件，逐个用 `awk '/\r/{n++} END{print n+0}'` 数含 CR 的行，
  全部为 **0**。
- `git diff --numstat -- Server/` 与 `git diff --numstat --ignore-cr-at-eol -- Server/`
  两份输出用 `diff` 逐行比对，**完全一致**：

```
95      1       Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs
20      0       Server/src/Dfo.Server.Application/ApplicationModule.cs
61      0       Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs
8       0       Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs
8       0       Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/ResultScreenTests.cs
8       0       Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/SetPlayResultHandlerTests.cs
```
（`ApplicationModule.cs` 的 20 行里只有 4 行是本任务的，其余 16 行是 TASK-02 的未提交改动；
`DropRateContent.cs` 与 `InfrastructureModule.cs` 是 TASK-01/02 的，本任务一个字未动。）

### 意外与处理

1. **接口加成员打断了三个既有测试 fixture**（CS0535×6）。处理见上表下方那段说明：
   补最小实现，不动任何断言。
2. **M11 一开始不红**，说明判据在赌一个巧合。已按上面写的方式补强，不是调常量绕过。
3. **简报里关于 `.gitattributes` 的说法与实测不符**，按实测记录，未照抄。
4. `.editorconfig` 里写的是 `end_of_line = crlf`，与本仓 `Server/` 树的实际基线（LF）
   相反。我没有改它——那不在本任务的 Permitted files 里，也不在本任务的范围里。
   只在此记录：任何按 `.editorconfig` 重排行尾的编辑器都会把本仓源码整文件改成 CRLF，
   这大概就是 TASK-02 那次整文件行尾改写的来源。是否要修 `.editorconfig` 交控制器裁定。
5. 判据不是严格意义上的"先红后绿"：接口与实现和判据是一起写的。作为替代，
   我对每一条新判据都做了变异检查（上表 14 条），证明它们各自都盯着一处真实行为。

### 拿不准的地方

1. **`MapMaxCount` 我传的是 `run.Layout.Rooms.Count`**（本局迷宫的房间数）。
   `ClearRewardTable.MapCountRates` 是按"地图数"作键的字典，而本局手上唯一与"地图数"
   同形状的事实就是它的房间数。我**没有**去断言归档那一段说的就是这个量——注释里
   只说"本局迷宫有多少个房间"，出处问题留给评审。若判定应当换成别的事实，
   改的是 `CardRewardDomainService.FaceFor` 里的一个表达式。
2. **`SettleAsync` 没有校验 `run.IsSettled`**。契约没有要求"必须先结算才能定牌"，
   加一道这样的门会把两个动作的先后顺序钉死在本层，而 T-004 才知道协议侧的真实顺序。
   现在的形状是：谁调它谁负责在结算时调。
3. **`ICardRewardDomainService` 是 `ValueTask` 的，但两个方法今天都是同步返回**
   （`ValueTask.FromResult`）。依据是架构约束里的"`ValueTask` 全覆盖"，
   同仓已有同形状的先例（`SessionAdjacentEntryDomainService`）。若评审更认可
   `IDungeonRunDomainService` 那样的全同步领域服务，这是两处签名的改动。
4. **`PartyMemberCount` 由调用方传入（默认 1）而不是本层写死 1。**
   写死就得在注释里说"本服务器没有队伍"，那正是 Amendment 2 点名的那种全称否定。
   改成参数，就既不必断言也不必验证。

## Executor Self-check — round 1

### Task Unit Contract 的 Acceptance conditions

| # | 条款 | 结论 |
|---|---|---|
| 1 | 牌面住在 `DungeonRunState` 上，不另建注册表，沿用既有比较交换 | ✅ `ClearRewardCardSet?` 是 `DungeonRunState` 的一个位置参数；`DungeonRunDomainService` 里没有任何新字段，两个新方法都是 `TryGetValue` → `with` → `TryUpdate` 循环，与 `TryMove`/`TryTakeGroundDrop`/`TryReportActorDead` 同形 |
| 2 | 定下牌面幂等 | ✅ `FixClearRewardCards` 已定下则原样返回；判据 `Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing` + 业务层 `A_second_settlement_keeps_the_cards_already_fixed`；变异 M1、M9 均红 |
| 3 | 认领成功返回内容，重复认领返回空 | ✅ `TryClaimClearRewardCard` 返回 `ClearRewardCardSlot?`；判据 `A_position_is_claimed_once`、`A_position_is_handed_over_once`；空牌位认领成功仍返回槽位（`A_position_that_holds_nothing_is_still_claimed_once`），与"拒绝"区分得开；变异 M2、M5 均红 |
| 4 | 不新增清理路径，本局结束的既有出口自然带走牌面 | ✅ 牌面是 `DungeonRunState` 的一部分，`End` 仍是那一行 `_runs.TryRemove`，本任务一个字未改；判据 `Ending_the_run_takes_its_cards_with_it`；变异 M13 红 |
| 5 | 业务服务自行准备事实与种子；规则服务不查世界状态 | ✅ `CardRewardDomainService` 组装 `ClearRewardOccasion`（难度与地图数取自本局，等级与队伍人数取自 `ClearRewardSettlement`）并算出两个种子；判据 `The_occasion_is_assembled_from_the_run_and_the_settlement` 两行；规则服务侧由 TASK-02 既有的构造器枚举判据钉住，本任务未改它 |
| 6 | 既有的本局判据仍全部通过 | ✅ `--filter DungeonRun` 里既有 14 条全通过，断言与期望值一个字未改；全量 0 失败 |
| 7 | 全量不低于 1690 通过、0 失败、2 跳过 | ✅ 1782 / 0 / 2 |

### Required checks

| ID | 结论 |
|---|---|
| CK-007 | ✅ 25 通过 / 0 失败（基线该 filter 14 条，新增 11 条）；重复定牌不改内容、重复认领第二次失败、既有本局判据仍通过，三点各有具名判据 |
| CK-008 | ✅ 两个构造器均已枚举（见上）；规则服务依赖中无仓储、注册表、目录、存储或领域服务 |
| CK-S | ✅ 1782 通过 / 0 失败 / 2 跳过 |

### Required methods

- **判据先行**：部分做到。判据与实现同批写出，未走严格的先红后绿；作为等价保障，
  14 条变异检查逐条证明判据会红，并因此发现并补上了 M11 的缺口。已如实记在上面。
- **分层**：内容源只被规则服务注入（业务服务的构造器只有规则服务与领域服务两项，有判据）；
  规则服务未获任何仓储/注册表/目录/存储/领域服务（TASK-02 判据仍绿）；
  本任务不含 handler、不含命令、不触达持久写，因此"handler 四类动作"、
  "禁止嵌套 dispatch"、"能触达持久写的命令一律 Transactional"三条本轮不适用。
- **归档读法写明出处**：本任务不读归档，无新的归档读数，故无出处可写；
  也没有在任何地方填入"看似合理"的数。
- **`System.*` using 在前、不做字母序**：本任务新建的两个源文件都不需要 `System.*`
  using（`ValueTask`/`CancellationToken`/`ArgumentNullException` 走 ImplicitUsings）；
  既有文件的 using 块一行未动。
- **`TreatWarningsAsErrors`**：`dotnet build Server/86JP.Server.slnx` → **0 个警告，0 个错误**。

### Contract Amendment 1 —— 两张脸必须拿到不同的种子

| 增补条款 | 结论 |
|---|---|
| 1. 两张脸由不同的种子产生，且两个种子都由本局已有的事实派生（不取时钟、不取共享生成器） | ✅ 派生式见上；只吃 `Layout.Seed` 与 `Layout.DungeonId` 与牌型，`SeedFor` 是静态纯函数 |
| 2. 有一条判据钉住这件事，且写在本层；仅注释不算 | ✅ `CardRewardDomainServiceTests` 用一个记录型规则服务把两次 `FaceFor` 的 `(occasion, seed)` 全部记下：`A_clear_fixes_two_faces_drawn_from_different_seeds` 断言两个种子不等；`Both_seeds_are_derived_from_the_run_and_from_nothing_else` 断言同一 layout 再settle 一次得到同一对种子（排除时钟与共享生成器），且换 `Layout.Seed` 或换 `DungeonId` 两个种子都跟着变（排除常量） |
| 判据设计提示：钉种子而不是碰运气比结果 | ✅ 断言的对象是传给规则服务的两个 `ulong` 种子本身，**从头到尾没有比较两张脸的内容**；变异 M6/M7/M8 分别对应"同种子"、"常量种子"、"时钟种子"，三条全红 |

### Contract Amendment 2 —— 断言规矩与行尾

| 条款 | 结论 |
|---|---|
| 一、对本文件之外任何东西的可验证事实断言，要么有判据钉住，要么删去 | ✅ 逐句过了一遍新写的注释。保留下来的跨文件断言只有两类：**compiler 能验的 `<see cref="..."/>` 交叉引用**（`ClearRewardOccasion`、`IClearRewardRuleService.FaceFor`、`MonsterKill.PartyMemberCount`、以及判据文件里指向 `ClearRewardRuleServiceTests` 的两处），和**本任务判据钉住的对本层自身行为的陈述**（幂等、认领去重、种子派生、场合取自本局、构造器依赖）。**主动删掉的**：初稿里"nothing outside this record says what a clear's cards were"（对全仓的全称否定，改写成对 `ClearRewardOccasion` 的 `cref`）、"the two content sources behind the rules are the rules' own"（对 TASK-02 构造器的断言，改写成对本文件自身的陈述）、"The two kinds are zero and one"（对枚举取值的断言，改写成"两个互不相同的牌型"）、"the two faces are two separate faces of four positions each"（对 `CardSlotCount` 的断言，删掉"of four positions each"）。**没有写下任何"本服务器没有 X"式的全称否定。** |
| 二、行尾一律 LF；`git diff --numstat` 与 `--ignore-cr-at-eol` 应一致 | ✅ 两份 numstat 用 `diff` 比对完全一致（输出见上）；10 个文件含 CR 的行数全为 0。另：实测发现本仓根目录**有** `.gitattributes`（只圈 `protocol/`），已在"意外与处理"里按实测更正简报的说法 |

### 未越界声明

本轮没有改动 Permitted files 之外的任何生产代码。唯一超出任务清单逐条列举的是
`Server/tests/Dfo.Server.Protocol.Tests/` 下三个文件的 fixture 补齐——它落在 Permitted
files 明列的 `Server/tests/` 内，只补接口新成员、不改任何断言与期望值，因此按未越界
处理并在上面写明；若评审另有判定，撤回这三处即可。
未建任何 git 提交，未合并、未推送、未建 PR、未删分支、未清理 worktree。

## Controller Observation — 行尾事故的根因已查明（TASK-03 执行者发现，控制器实测复核）

TASK-03 的执行者更正了控制器简报里的两处说法，控制器逐条实测复核如下。

**一、`.gitattributes` 存在，但只管 `protocol/`。**
仓库根有 `.gitattributes`，内容只有一条 `protocol/** text eol=lf`，并附注释说明是**刻意只
限于该目录**（那是唯一有机器生成内容的树）。所以「`Server/` 没有行尾兜底」这个实质结论成立，
但「本仓没有 `.gitattributes`」这句话本身是错的——控制器此前在三份派工简报里都这么写过。

**二、真正的根因是 `.editorconfig`。** 它的 `[*]` 段写着 `end_of_line = crlf`，
而 `Server/` 全树在 index 与工作区都是 LF（`git ls-files --eol` 显示 `i/lf w/lf`）。
**任何遵守 `.editorconfig` 的编辑器或工具写文件时都会产出 CRLF**，于是 diff 爆炸。
TASK-02 那次 `ApplicationModule.cs` 从真实的 16/0 膨胀成 294/278，来源就在这里。

**三、`core.autocrlf` 实测为 `false`**（worktree 与 global 皆是），控制器简报里这一条是对的。
但 `.gitattributes` 的注释写着「This repository sets core.autocrlf=true」——该项是**每个克隆
各自的本地设置，不可能由提交的文件决定**，所以那句注释描述的是作者当时的环境而非仓库属性。
不改它（不在本 Task 边界内，且其实质规则正确），记在此处供后续。

**给后续任务与仓库的分开处置**：

- **本 Spec 内**：继续按现行做法，交付前自查
  `git diff --numstat` 与 `--ignore-cr-at-eol` 是否逐行一致。这条已在 TASK-03 生效并通过。
- **仓库级**：`.editorconfig` 与 `Server/` 的实际行尾相反，是一个会反复咬人的缺陷，
  且不会有任何东西提醒。**修它超出本 Spec 范围**（改动影响全仓工具行为），
  留作后续任务或单独决定。记在这里是为了让下一个撞上它的人不必再查一遍。

**记这一条的另一层意思**：控制器在三份简报里重复了一个未经实测的说法，
由执行者实测更正。这与本 Spec 反复出现的形状相同——**说得顺口的事实没有防线**，
无论说的人是谁。

## Independent Quick Review — round 1 (reviewer QR-03-1)

- **Verdict: `passed`**
- Reviewed contract revision: 1，含 Contract Amendment 1 与 Contract Amendment 2（扩宽版）
- Owned diff identity: worktree `flipping-a-clear-reward-card`，基线 `c0cb6c99`，工作未提交。
  本轮核的是 TASK-03 拥有的 8 个路径（3 个已跟踪修改 + 2 个新建源 + 2 个新建判据 + 3 个
  Protocol.Tests fixture）。`DropRateContent.cs`、`InfrastructureModule.cs`、`ClearReward*`
  那批为 TASK-01/02 产出，已核实其内容与本 Task 无关（`InfrastructureModule.cs` 的 3 行是
  `PvfClearRewardRateSource` 注册），不计入本轮。

### 自跑的证据

| 命令 | 结果 |
|---|---|
| `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | **1782 通过 / 0 失败 / 2 跳过**，exit 0，输出中 0 处 warning/error。分项 51+376+60+244+843+4+204。两条跳过即 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。无假红出现 |
| `dotnet test ... --filter DungeonRun` | 25 通过 / 0 失败（CK-007 达成） |
| `dotnet test ... --filter "CardReward\|DungeonRun"` | 35 通过 / 0 失败 |
| `git diff --numstat` vs `git diff --numstat --ignore-cr-at-eol` | 8 行输出**逐行完全一致** |
| 10 个改动/新建文件 `awk '/\r/{n++} END{print n+0}'` | 全部为 **0** |

### 逐项核对

**一、Amendment 1（种子）—— 钉住了，不是碰运气。**

`CardRewardDomainService.cs:81-82` 的 `SeedFor` 是 `private static`，形参只有
`DungeonRunState` 与 `ClearRewardCardKind`，函数体是纯表达式
`((ulong)(uint)run.Layout.Seed << 32) | ((uint)run.Layout.DungeonId ^ (uint)kind)`。
`Layout.Seed` 与 `DungeonId` 均为 `int`（`IDungeonLayoutRuleService.cs:53,60`），
在 `Begin` 时随 layout 固定。`ClearRewardCardKind.Free = 0 / Paid = 1`
（`IClearRewardRuleService.cs:13-14`），异或对第二参数是单射，故两个种子**必然不等**，
且与副本号无关——注释里那句「两种牌型在任何副本号下都不可能得出同一个种子」为真。
无时钟、无共享生成器、无实例状态。

判据钉的确实是**种子本身**：`CardRewardDomainServiceTests.cs` 的 `RecordingRules`
记下每次 `FaceFor` 的 `(occasion, seed)`，
`Both_seeds_are_derived_from_the_run_and_from_nothing_else`（:165-185）四条断言分别封死四种偏离：
:169 `NotEqual(first.Free, first.Paid)` 封「同种子/常量种子」；
:174 `Equal(first, await SeedsFor(Tutorial))`（另起一个 `DungeonRunDomainService` 与
另一个服务实例）封「时钟/共享生成器/实例计数器」；
:178-180 换 `Layout.Seed` 封「不读本局种子」；:182-184 换 `DungeonId`（`+2`，避开与牌型位的
异或碰撞）封「不读副本号」。
全文件**没有一处比较两张脸的内容**来证明「不同」——`ItemIds` 的比较只出现在幂等判据
（:205-206, :212-213）里，用途相反。Amendment 1 的「判据设计提示」满足。

**二、幂等与去重。** `FixClearRewardCards`（`DungeonRunDomainService.cs:125-146`）与
`TryClaimClearRewardCard`（:153-180）都是 `TryGetValue` → `with` → `TryUpdate` 循环，
与同文件 `TrySettle`/`TryTakeGroundDrop` 同形；类里仍只有 `_runs` 一个字段（:18）。
已定下则原样返回 `current`（:138-139），认领命中 `Claimed` 则返回 null（:170-171）。
判据：`Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing`（:71）、
`A_position_is_claimed_once`（:105）、`The_two_faces_are_claimed_independently`（:137）、
`A_position_that_holds_nothing_is_still_claimed_once`（:164）、
`A_position_the_face_does_not_have_is_refused`（:186，三行 Theory，含 `-1`）。
`ImmutableHashSet.Add` 在已含时返回同实例这一坑被 :170 的早返回挡在前面。

**三、清理路径。** 实测 `grep -rn "TryRemove" DungeonRunDomainService.cs` 只有
`:221 public bool End(...) => _runs.TryRemove(...)` 一处；`grep -rn "\.End(" Server/src`
正好三个调用点（`DungeonClearSlice.cs:143`、`LeaveDungeonSlice.cs:119`、
`DungeonSelectionSlice.cs:202`）。牌面是 `DungeonRunState` 的位置参数，未新增第四个出口，
也不会残留——`Ending_the_run_takes_its_cards_with_it`（:227）连「下一局重开不继承」都断言了。

**四、M11 的修补属实。** `CardRewardDomainServiceTests.cs:60-72` 确有第二个 layout
`Sprawling`（5 个房间，Tutorial 3 个），:225-228 该判据确为两行 `[Theory]`，
:248 `Assert.NotEqual(Tutorial.Rooms.Count, Sprawling.Rooms.Count)` 把「任何常量都无法
同时满足两行」写死。自查属实且有价值。
其余判据未见同类「单样本换个值也绿」的问题：等级/队伍/难度三个常量（77/3/2）都不是任何
默认值；越界判据用 `-1/4/99` 三行；种子判据用两个变体 layout 而非一个。

**五、越界（24 行）核实通过。** 三处各 8 行，逐行读过：只有
`FixClearRewardCards` 与 `TryClaimClearRewardCard` 两个 `=> throw new NotSupportedException(...)`
表达式体，异常消息沿用各 fixture 既有的那一句，**没有夹带任何断言、期望值或其他成员**。
落在 BD-021（`Server/tests/`）内，且 BD-021 的限制是「不改既有用例的断言与期望值」——
未违反。判为未越界。

**六、分层。** `CardRewardDomainService`（`:15-17`）`internal sealed` + 主构造器，
只吃 `IClearRewardRuleService` 与 `IDungeonRunDomainService`，由
`The_settlement_service_reaches_the_archive_only_through_the_rules`（:305）逐项枚举钉住。
规则服务 `ClearRewardRuleService(IClearRewardRateSource, IDropItemPoolSource, ClearRewardRateOptions)`
（`ClearRewardRuleService.cs:33-36`）本任务一字未动，仍不持世界状态。CK-008 达成。

**七、漏网断言（Amendment 2 一）。** 扫了本轮全部改动/新建文件的注释。
**未发现「本服务器没有 X」式全称否定**。逐条核过的跨文件断言：
- `IDungeonRunDomainService.cs:352` 「`TrySettle` … answers null on a repeat」——`TrySettle`
  在**同一文件**声明（:322），其自身 doc（:312-313）即如此写，且既有判据
  `DungeonRunDomainServiceTests.cs:240` `Assert.Null(runs.TrySettle(Character))` 钉住。合规。
- `ICardRewardDomainService.cs:19` 「for the reason `MonsterKill.PartyMemberCount` is one」——
  实测 `IMonsterDropService.cs:48-52` 确为参数且给出理由，断言为真，cref 由编译器验证。
- `IDungeonRunDomainService.cs:174` 「facts this record does not carry -- see `ClearRewardOccasion`」——
  实测为真（Occasion 带 CharacterLevel/Difficulty/MapMaxCount/PartyMemberCount，CardSet 不带），
  cref 编译器验证。
- 其余保留下来的断言均是对本层自身行为的陈述，各自有具名判据。
行尾（Amendment 2 二）见上表，两份 numstat 逐行一致。

**八、Acceptance conditions 1-7 与 CK-007/CK-008/CK-S 全部达成**（证据见上）。
`ApplicationModule.cs` 的 +20 行实测拆分为 4（`IClearRewardRuleService`，TASK-02）
+ 12（`ClearRewardRateOptions`，TASK-02）+ 4（`ICardRewardDomainService` 注册与其 3 行注释，
本任务），与执行者所报一致。

### 非阻塞观察（不要求本轮整改，供最终评审与控制器裁定）

1. **`DungeonRunDomainService.cs:168` 的硬向下转型**
   `var claimed = (ImmutableHashSet<ClearRewardCardId>)cards.Claimed;`。
   `ClearRewardCardSet.Claimed` 的声明类型是 `IReadOnlySet<ClearRewardCardId>`，而该记录是
   `Server.Abstractions` 里的 **public** 记录。实测全仓只有一个构造点
   （`DungeonRunDomainService.cs:143`，传 `ImmutableHashSet.Empty`），故今天不可触发；
   但 T-004 或任何判据一旦用 `HashSet<>` 构造该记录，认领时会 `InvalidCastException`。
   收窄成员类型或改用 `ToImmutableHashSet()` 可根除。
2. **`MapMaxCount ← run.Layout.Rooms.Count`**（`CardRewardDomainService.cs:62`）是一个
   已披露的解释性映射，无归档出处。执行者的注释**正确地没有**断言归档那一段说的就是房间数
   （只说「本局迷宫有多少个房间」），故不违反 Amendment 2；但 `ClearRewardOccasion.MapMaxCount`
   的 doc（TASK-02 文件）写的是「How many maps the run's dungeon holds」，两者是否同一个量
   留给最终评审／控制器。
3. **跨任务陈旧断言（不在本轮 diff 内，无法在此整改）**：
   `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs:69`
   「This server has no party model and passes one.」——正是 Amendment 2 点名的全称否定形状，
   且 TASK-03 之后调用方已可传入非 1 的队伍人数（判据里传 3），该句同时变得陈旧。
   属 TASK-02 文件，提请控制器决定何时处置。
4. 微瑕：`CardRewardDomainServiceTests.cs:60-63` 的 `Tutorial with { Width = 3, Height = 2, ... }`
   把 `Width`/`Height` 覆写成与 `Tutorial` 相同的值，是空操作。纯观感，无行为影响。

### 要求的整改

无。本 Task Unit 可按执行者的验收逻辑继续推进。本 `passed` 仅表示未在受限输入内发现
明显阻塞缺陷，不构成最终质量批准。

## Contract Amendment 3 — 边界扩至 IClearRewardRuleService.cs 一句（**编辑之前**写的）

**触发**：TASK-03 的评审给了 `passed`，但附了四条非阻塞观察，其中两条控制器判为应当就地处理。

**这条修订写在动手之前。** TASK-02 的 `Contract Amendment 4` 记着：上一次控制器写完边界修订
之后又越出了它，只补自述，被评审记为 F-5，并写下「越出 Permitted files 之前先写修订，
在同一次编辑之前，不要留到交付时补」。本条即照此执行。

### 要改的两处

**其一（在本 Task 边界内，不需扩界）**：
`Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs` 有一处
`(ImmutableHashSet<ClearRewardCardId>)cards.Claimed` 硬向下转型，
而 `Claimed` 在 **public** 记录 `ClearRewardCardSet` 上声明为 `IReadOnlySet<>`。
今天不可触发，但**下一个任务 T-004 构造该记录时传一个普通 `HashSet` 就是运行时
`InvalidCastException`**。

**处置：不加防御，改为让错误不可能**——把记录成员的类型收紧到实际需要的类型，
转型随之消失，编译器接管这件事。留一个「今天不会崩、下个任务会崩」的东西给后来的人，
正是本 Spec 一直在防的那种交接。

**其二（需扩界）**：
`Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` 的
`PartyMemberCount` 参数注释写着「**This server has no party model and passes one**」。

这是一句**无判据的全称否定**，正是 Amendment 2 明令禁止的形状，也正是 TASK-02
栽在「本服务器没有 PC 房概念」上的同一句式。该文件是 TASK-02 的产出、已收工。

**处置**：按 TASK-01 `Amendment 3` 与 TASK-02 `Amendment 3` 的先例，
由当前在办的 Task 做有界扩界，不重开已收工的 Task。

改法**不是**换成另一句需要搜全仓才能验证的话，而是按 TASK-02 round 3 立下的通则
去掉验证负担：说明这个参数**由调用方传入**、以及本 Task 的调用方今天传什么，
这两件事在本文件与调用点即可核。

Permitted files or interfaces 增补：
`Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
（仅 `PartyMemberCount` 一段 param doc）。

**不动**评审另记的两条观察：`MapMaxCount ← Rooms.Count` 的解释性映射（注释已正确地
未断言归档出处，留最终评审）与判据里一处空操作 `with`。

Spec 的验收边界未变；两处均不改可执行语义（其一是收紧类型使既有行为不变且更早报错，
其二是注释）。

## Execution Result — round 2（控制器处理评审的两条非阻塞观察）

评审给了 `passed` 并附四条非阻塞观察。控制器判其中两条应当就地处理，两条留下。
**`Contract Amendment 3` 在动手之前写好**（见上），这一点是本轮刻意做对的事——
TASK-02 正是在写完修订后又越出它才被记了 F-5。

### 处理了：硬向下转型（观察 ①）

`DungeonRunDomainService` 原有一行
`var claimed = (ImmutableHashSet<ClearRewardCardId>)cards.Claimed;`，
而 `Claimed` 在 **public** 记录 `ClearRewardCardSet` 上声明为 `IReadOnlySet<>`。

今天不可触发（唯一构造点就在同一个方法附近），但**下一个任务构造该记录时传一个普通
`HashSet` 就是运行时 `InvalidCastException`**——编译通过、第一次翻牌才炸。

**改法不是加防御，是让错误不可能**：把记录成员的类型收紧为 `ImmutableHashSet<>`，
转型随之消失，编译器接管。参数注释写明为什么要具体类型而不是接口：
认领一个牌位要由上一个集合产出下一个，需要 `Add`；声明成接口就只能靠转型拿回来。

**收紧之后一条判据都没红**，说明今天没有任何地方在传别的集合类型——
这是一次纯粹的「把将来的运行时错变成现在的编译错」。

留一个「今天不会崩、下个任务会崩」的东西给后来的人，正是本 Spec 一直在防的那种交接。

### 处理了：又一句无判据的全称否定（观察 ③，需扩界）

`IClearRewardRuleService.cs` 的 `PartyMemberCount` 参数注释写着
「**This server has no party model and passes one**」——与 TASK-02 栽过的
「本服务器没有 PC 房概念」是同一句式，无判据钉住。该文件是 TASK-02 的产出、已收工，
故按前两个 Task 的先例由本 Task 有界扩界，不重开已收工的 Task。

改法按 TASK-02 round 3 立下的通则**去掉验证负担**，而不是换成另一句需要搜全仓的话：
现在只说这个参数**由调用方传入**（与本文件其余每个事实一样）、以及调用方传什么在调用点可见。
两件事都在本文件与调用点即可核，不需要断言全仓有没有队伍模型。

### 留下未动的两条

- **观察 ②**：`MapMaxCount ← Rooms.Count` 是解释性映射。注释**正确地没有断言**归档那一段
  说的就是这个量，评审也据此判为非阻塞。留给最终评审与验证阶段。
- **观察 ④**：判据里一处空操作 `with`。无害，不值一次改动与一轮评审。

## Executor Self-check — round 2

- Amendment 3 写在编辑之前，不是事后补记。
- 观察 ①：转型已消失（grep 零命中），类型收紧后全量判据未红，行为不变。
- 观察 ③：已改，改后该段不含任何需要搜仓验证的断言。
- 未改任何判据的期望值；本轮产品改动为三处，两处注释一处类型声明。
- 行尾：`git diff --numstat` 与 `--ignore-cr-at-eol` 逐行一致。
- 全量判据：**1782 通过 / 0 失败 / 2 跳过**，退出码 0，一次跑成，无假红。
  分项 60 + 51 + 376 + 244 + 843 + 204 + 4 = 1782。

## Independent Quick Review — round 2 (reviewer QR-03-2)

- **Verdict: `passed`**
- Reviewed contract revision: 1，含 Contract Amendment 1、2（扩宽版）、3
- Owned diff identity: worktree `flipping-a-clear-reward-card`，基线 `c0cb6c99`，工作未提交，
  无新提交（`git log --oneline -1` = `c0cb6c99`）。
- 评审范围：**仅 round 2 的三处改动**。round 1 已核过的结论未重核，且 round 2 未动摇任何一条
  （下面「未被动摇」一节给出理由）。

### 自跑的证据

| 命令 | 结果 |
|---|---|
| `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | **1782 通过 / 0 失败 / 2 跳过**，exit 0，一次跑成、无假红。分项 376+51+60+244+843+4+204 = 1782。整份输出 grep `warn\|警告\|error\|错误` **零命中**（`TreatWarningsAsErrors` 达成）。两条跳过即 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，输出中逐条列出 `[SKIP]` |
| `dotnet test ... --filter "CardReward\|DungeonRun"` | **35 通过 / 0 失败 / 0 跳过** |
| `git diff --numstat` vs `git diff --numstat --ignore-cr-at-eol` | 8 行输出**逐行完全一致** |
| 三个 round-2 文件 `awk '/\r/{n++} END{print n+0}'` | 全部为 **0**（含未跟踪的 `IClearRewardRuleService.cs`，numstat 覆盖不到，单独数过） |
| `stat -c %y` 全部改动/新建 `.cs` 文件 | 最后三个 mtime 恰为 `IDungeonRunDomainService.cs`(11:25:51)、`DungeonRunDomainService.cs`(11:25:57)、`IClearRewardRuleService.cs`(11:26:04)，其余全部 ≤ 11:08。**round 2 确实只碰了这三个文件** |

### 一、硬向下转型：已消除，且确实不改可执行语义

- **转型确已消失**：`grep -rn "(ImmutableHashSet<" --include=*.cs Server/` 现只剩三处，
  全是基线既有的兄弟成员——`DungeonRunDomainService.cs:56`（`VisitedCells`）、
  `:75`（`TakenGroundDrops`）、`:94`（`DeadActors`）。round 1 点名的 `:168` 现为
  `var claimed = cards.Claimed;`，**正是它被点的那一行**。
- **记录成员类型确已改**：`IDungeonRunDomainService.cs:193`
  `ImmutableHashSet<ClearRewardCardId> Claimed)`；`:182-188` 新增 `<para>` 写明为什么要具体类型。
- **不改可执行语义，逐条核过**：
  - 相等性：`IReadOnlySet<X>` 与 `ImmutableHashSet<X>` 都不提供值相等的 `IEquatable<>`，
    `EqualityComparer<T>.Default` 收紧前后同样落到引用相等，`ClearRewardCardSet` 的
    `Equals`/`GetHashCode` 行为不变。
  - `ToString`、`with`、`Contains`、`Add` 全部解析到同一个运行时实例的同一批成员。
  - `Deconstruct` 的 out 形参类型确实变了，但**全仓没有一处解构该记录**
    （`grep -rn "ClearRewardCardSet"` 的全部命中里，只有 `:190` 的声明与
    `DungeonRunDomainService.cs:143` 的 `new`，无位置模式、无解构）。
  - 唯一变化在编译期：谁能构造它。**这正是改动的目的。**
- **「收紧之后一条判据都没红」成立**：`ClearRewardCardSet` 全仓只有一个构造点
  （`DungeonRunDomainService.cs:143`，传 `ImmutableHashSet<ClearRewardCardId>.Empty`）与一个
  `with`（`:175`，`claimed.Add(id)`，本身就返回 `ImmutableHashSet<>`）；判据文件里**一处也没有**
  构造该记录（`CardRewardDomainServiceTests.cs`、`DungeonRunClearRewardCardTests.cs`
  只读 `.ClearRewardCards.Free/.Paid/.Claimed/.IsClaimed/.SlotAt`）。所以今天确实没有任何地方
  在传别的集合类型。
- **numstat 佐证改动之小**：该文件由 round 1 自述的 95/1 变为 **103/1**，净 +8 行、删除数不变，
  与「一个 `<para>` 文档块 + 一个类型 token」相符；`DungeonRunDomainService.cs` 仍是 **61/0**
  单一连续 hunk，未触及任何基线行（转型行是本任务自己新增的行，改它不产生新的删除）。

**用词上的一点不精确（非发现，仅记）**：自查写的是「收紧之后一条判据都没红」。
传错集合类型会是**编译错**而不是红判据；真正的证据是**整个解决方案 0 警告 0 错误地编译通过**
且构造点唯一。实质结论无误，措辞可更准。

### 二、`PartyMemberCount` 参数注释：改后不含需搜全仓的断言

现文（`IClearRewardRuleService.cs:68-72`）：

> How many were in the party, counted from one. Handed in like every other fact here rather than
> worked out: what a clear was fought by is the caller's to know, not these rules'. What this
> server's callers pass today is visible at their call sites.

逐句核：
- 「counted from one」——本参数自身的约定，本文件即是权威。
- 「Handed in like every other fact here」——`here` 指同一记录 `ClearRewardOccasion`
  （`:73-78`），五个成员全部是形参，**本文件可核**。
- 「the caller's to know, not these rules'」——关于本文件所声明的规则服务的规范性陈述，
  且由 TASK-02 既有判据 `ClearRewardRuleServiceTests.The_rule_service_is_handed_no_reader_of_world_state`
  钉住（本轮仍绿）。
- 「What this server's callers pass today is visible at their call sites」——**指路，不是断言**：
  它没有给出任何会过期的具体值，也没有作任何全称否定。真要核也只需看调用点
  （`CardRewardDomainService.cs:63` 传 `settlement.PartyMemberCount`；
  `ICardRewardDomainService.cs:22` 默认 1，判据 `CardRewardDomainServiceTests.cs:241` 传 3）。

**原句「This server has no party model and passes one」的两处毛病都消掉了**：全称否定没了，
「passes one」这个会随 TASK-03 变陈旧的具体值也没了。合规。

另外扫了三个 round-2 文件全部注释找「本X没有Y」式全称否定：
`IDungeonRunDomainService.cs:254`「There is no reconnect in this game」确实是这种句式，
但 `git diff -U0` 的五个 hunk 落在新文件行 1、14-17、28-29、161-217、351-389，**:254 不在其中**，
是基线既有文本，不属本任务（也不属本 Spec）。其余命中（`:16`、`:35`、`:42`、`:71`、`:200`、
`DungeonRunDomainService.cs:12`）要么是基线行，要么是对本文件自身空值约定的陈述。**无漏网。**

### 三、授权齐备：逐一对上，无一对不上

| 改动路径 | 授权来源 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs` | 原始 Permitted files 第 1 项（扩展） |
| `Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs` | 原始 Permitted files 第 2 项（扩展） |
| `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` | **Amendment 3** 的增补项，且限定「仅 `PartyMemberCount` 一段 param doc」 |

- Amendment 3 **确实授权了** `IClearRewardRuleService.cs`，原文逐字：
  「Permitted files or interfaces 增补：`Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`（仅 `PartyMemberCount` 一段 param doc）」。
- **改动确在该 scope 内**：round 1 评审引用过本文件的 `:13-14`（`Free = 0 / Paid = 1`），
  现仍在 `:13-14` **未移位**，说明第 68 行之上一个字未动；改动区就是 `:68-72` 的那一段 param doc，
  记录声明 `:73-78` 结构完好。
- **本轮 mtime 证据**（见上表）显示 round 2 只碰三个文件，没有第四个文件需要对账。
- 其余仍在工作区的路径（`DropRateContent.cs`、`InfrastructureModule.cs`、`ClearReward*` 那批新文件、
  三个 `Protocol.Tests` fixture、两个新判据文件、`ApplicationModule.cs`）mtime 全部 ≤ 11:08，
  round 1 已各自定过归属，本轮未动。
- 未建提交、未合并、未推送、未建 PR、未删分支、未清理 worktree（`git log --oneline -1` 仍是基线）。

**时序**（Amendment 3 写在编辑之前）按简报要求不作强判：文件本身看不出先后。
但**授权齐备这一点是可核的，且已核实齐备**——这正是 TASK-02 F-5 想防的实质后果，本轮没有发生。

### 四、留下未动的两条：**同意留**

- **观察 ②（`MapMaxCount ← run.Layout.Rooms.Count`）：同意留，且认为不该在本轮改。**
  `CardRewardDomainService.cs:50-53` 的 remarks 写的是「how many rooms the maze it was given
  holds」——**只陈述取了什么，没有断言归档那一段说的就是这个量**，故不违反 Amendment 2。
  「房间数是不是档案的 map count」是一个**需要读档案才能回答的问题**，属验证阶段与最终评审，
  不是 quick review 能在受限输入内定的；把它明写在 Task 记录里等人查，比在本轮凭猜「修」掉更安全。
- **观察 ④（判据里的空操作 `with`）：同意留。**
  实测 `Tutorial` 是 `Width: 3, Height: 2`（`CardRewardDomainServiceTests.cs:40-41`），
  `Sprawling` 重写同值（`:62-63`），确为空操作。但它**读起来是自述**：把新 `Rooms`
  （5 个格子，含 `(2,1)`、`(1,0)`）所处的迷宫尺寸摆在旁边，读者不必回头翻 `Tutorial`。
  纯观感、零行为影响，不值一次改动加一轮评审。

### 未被动摇（round 1 结论仍成立）

round 2 只改了「一个记录成员的类型 token + 两处注释」。三条本轮复跑的硬证据说明 round 1 的结论
未受影响：全量仍 1782/0/2 且分项与 round 1 完全一致；`CardReward|DungeonRun` 仍 35/0；
两份 numstat 仍逐行一致。M11 的加固（`:57-58` 的 remarks、`:60-72` 的第二个 layout）原样健在。

### 非阻塞观察（不要求本轮整改，供最终评审与控制器裁定）

1. **同一条记录上的兄弟成员未跟进。** `DungeonRunState` 的 `VisitedCells`、`TakenGroundDrops`、
   `DeadActors`（`IDungeonRunDomainService.cs:22-24`）仍声明为 `IReadOnlySet<>`，
   实现里仍各带一处硬向下转型（`DungeonRunDomainService.cs:56`、`:75`、`:94`）。
   本轮新增的 `:182-188` 文档正好解释了「为什么要具体类型」，读者会立刻问三个兄弟为何不同。
   **这三处是基线代码，不在 TASK-03 的 Owned boundary 内，本轮不动是对的**；
   记在此处供最终评审或后续任务决定。
2. 措辞：见「一」末尾那条「编译错而非红判据」的记录。

### 要求的整改

**无。** round 2 的三处改动干净：转型消除到位且语义中性，注释改后无需搜仓即可核，
三个文件的授权逐一对得上，行尾与全量判据均自跑复现。
本 Task Unit 可按执行者的验收逻辑继续推进。本 `passed` 仅表示未在受限输入内发现明显阻塞缺陷，
不构成最终质量批准。
