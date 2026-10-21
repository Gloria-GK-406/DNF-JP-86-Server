# TASK-07 全量机械差异核对与迁移点对账

- 日期 2026-10-22。基准提交 `0223e646`（TASK-06 开始之前），对象是工作区，覆盖 TASK-06 与 TASK-07 两个单元的全部测试改动。
- 脚本都是一次性的，放在 scratchpad，不进仓库，目录 `/private/tmp/claude-501/-Users-puxianguo-project/19adbc1e-6ba1-4530-bca0-dcab5d727d8a/scratchpad/t07/`：
  - `diffcheck7.py`：TASK-06 §5.1 的 `diffcheck.py`，按 TASK-07 §6 收紧了白名单；
  - `lines7.py`：`git diff -w` 行级补核；
  - `eol.py`：行尾、BOM、末尾换行；
  - `begins.py`、`outside.py`、`splitbegin.py`：迁移点检索与归属。
- 命令：`python3 diffcheck7.py 0223e646 --json dc7.json`、`python3 lines7.py 0223e646`、`python3 eol.py 0223e646`、`python3 begins.py 0223e646 <五个项目> --list | python3 outside.py`。

## 1. 口径

沿用 TASK-06 §5.1 的三项比较，对 `git diff --name-only 0223e646 -- Server/tests` 中基准里就有的每个 `.cs` 文件，比较基准版与工作区版：

1. **测试方法**：特性加方法名，按多重集合比较。
   - 改了一处：特性行到声明行的扫描距离从 14 行放宽到 80 行。原脚本会漏掉 `InlineData` 很多的 Theory，例如 `EquipmentReinforcementDomainTests.Invalid_ticket_attempts…` 有 12 行 `InlineData`。
   - 两侧用同一口径，所以放宽只会多认出方法，不会让两侧不等。
2. **断言行**：含 `Assert.` 或 `Skip.` 的物理行，去掉全部空白后按多重集合比较。
3. **白名单外差异**：两版各自切成 C# 记号，用 `difflib` 对齐，再从每个插入片段和删除片段里剔除白名单形状。

按 TASK-07 §6 收紧的地方：

- **插入的 `return`**：只认 `return x ;` 或 `return ( a , b ) ;`，而且后面必须紧跟 lambda 的闭合 `} )`。孤立的 `return x ;` 不再放行。
- **插入的 `await`**：只认两个位置：
  - 紧挨在 `TestGameplay.Run…` 之前；
  - 紧接在它的 lambda 头 `( ) =>` 之后，即 `async () => await <原表达式>`。
  - 孤立的 `await` 不再放行。
- **对齐歧义**：只对纯插入片段使用。当插入片段两端的记号与相邻记号重复时，同一份记号流可以有几种对齐。脚本在左右各 4 个记号内尝试等价的轮换，只要有一种轮换整体落在白名单内就放行。
  - 这样不会多放行任何东西：轮换前后的记号串逐字相同，它只撤销 `difflib` 对对齐位置的随意选择。
  - 它主要消掉 `{ await using …` 这类形状：原有的 `await` 和包裹头里的 `await` 被对到了一起。
- 删除片段只允许 `=>`、`;`、`async`，这一条没变。

## 2. 汇总

| 项目 | 改动文件 | 方法 基准 | 方法 工作区 | 断言行 基准 | 断言行 工作区 | 白名单外片段 | 集合不等的文件 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Application.Tests | 202 | 1819 | 1819 | 10142 | 10142 | 4 | 0 |
| Host.Tests | 4 | 43 | 43 | 336 | 336 | 0 | 0 |
| Networking.Tests | 4 | 8 | 8 | 100 | 100 | 0 | 0 |
| Persistence.Tests | 28 | 129 | 129 | 586 | 586 | 0 | 0 |
| Protocol.Tests | 60 | 553 | 553 | 2327 | 2327 | 0 | 0 |
| tests/Support（`TestSessions.cs`） | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 合计 | 299 | 2552 | 2552 | 13491 | 13491 | 4 | 0 |

- 加法，改动文件：202 ＋ 4 ＋ 4 ＋ 28 ＋ 60 ＋ 1 ＝ 299。
- 加法，方法：1819 ＋ 43 ＋ 8 ＋ 129 ＋ 553 ＋ 0 ＝ 2552，两侧相等。
- 加法，断言行：10142 ＋ 336 ＋ 100 ＋ 586 ＋ 2327 ＋ 0 ＝ 13491，两侧相等。
- 299 个文件逐个比较，方法多重集合与断言行多重集合**全部相等**。
- 另有 2 个基准中不存在的新文件，不参加比较：`Server/tests/Support/TestGameplay.cs`、`Server/tests/Dfo.Server.Application.Tests/Execution/TestGameplayTests.cs`。
- csproj 改动 5 个，都是 TASK-06 的那一行 `Compile Include … Link`。TASK-07 没有改 csproj。
- 与 TASK-06 的表比，Host 从 2 个文件变成 4 个，Networking 从 3 个变成 4 个，Protocol 从 56 个变成 60 个。多出来的是本单元按探针 (B) 补包的文件，见 §7。

## 3. 白名单外的 4 个片段（逐条）

| 文件 | 片段 | 理由 |
|---|---|---|
| `Application.Tests/Inventory/WarehouseMoveSynchronizationTests.cs`（辅助 `Send`） | 删除了一个 `return` | 原文是 `return handler.HandleAsync(…);`，方法返回 `ValueTask`。现在是 `private static async ValueTask Send(…) { host.Sender.Messages.Clear(); await TestGameplay.RunAsync(host.Services, async () => await …HandleAsync(…)); }`。<br>`ValueTask` 只能这样接进 `Func<Task>`。`Clear()` 仍然在调用时同步执行：async 方法在第一个 `await` 之前同步运行。调用方都是 `await Send(…)`，行为相同 |
| `Application.Tests/Parties/PartySettingsDomainTests.cs`（`ConcurrentUpdatesAreLinearizedIntoUniqueRevisionsAndDetachedResults`，2 处） | 残留 `. Run ( ( => TestGameplay` | 实际形状是在 `Task.Run(() =>` 的 `=>` 之后插入 `TestGameplay.Run(() =>`，闭合处 `});` 变成 `}));`。<br>`difflib` 把新插入的 `() =>` 对到了原来 `Task.Run` 的 lambda 头上，超出了 4 个记号的轮换范围。人工核对 `git diff -w`，只有这两处插入 |
| `Application.Tests/World/DungeonRunDomainServiceTests.cs`（`ACharacterInNoRunHasNothingToSettle`） | 残留 `TestGameplay . Run ( (` | 原方法是表达式体：`public void X() =>` 加一行断言。整行就是断言，不能在断言行上直接加包裹的头尾，所以改成了块体：`{ TestGameplay.Run(() => { 原断言; }); }`。<br>这里删去 `=>`，加上 `{` 和 `}`；断言行一字未改 |

## 4. `git diff -w` 行级补核（TASK-07 §6）

`lines7.py` 对 `git diff -w -U0 0223e646 -- Server/tests` 的每个 hunk 做两步：

1. 先去掉单独成立的白名单行：包裹头、`{`、`}`、`});`、`return x;`、`using Dfo.Server.Testing;`；
2. 再从两侧剩余的文本里去掉 `TestGameplay.Run…( [容器,] [async] () => [await]`、`async`、`await` 和括号、分号，比较两侧是否相等。

| 项 | 数 |
|---|---:|
| hunk（只计基准中已有的 `.cs` 文件） | 3656 |
| 其中逐字通过 | 3646 |
| 需人工复核 | 10 |
| 新增行 | 5417 |
| 其中单独成立的白名单行 | 5320 |
| 删除行 | 92 |

需人工复核的 10 个 hunk，逐条判定：

| hunk | 判定 |
|---|---|
| `AchievementTitleBookTests` 的 `Fixture.WriteAsync` 两个重载（同一 hunk） | 包裹头前缀是 `return await`，`lines7` 的去头规则不去 `return`。形状是白名单的「`return` ＋ 包裹头」。主控改，见 TASK-07 Result |
| `JoustIntegrationTests` 的 `Fixture.Run<T>` | 同上（`{return await TestGameplay.RunAsync(provider,async()=>{…});}`）。fork-4 改 |
| `WarehouseMoveSynchronizationTests.Send` | §3 第 1 条 |
| `DungeonRunDomainServiceTests.ACharacterInNoRunHasNothingToSettle` | §3 第 3 条 |
| `Host.Tests/GmPlayerDirectoryHttpTests.cs` 4 处 | TASK-06 的提出返回值形状：`var x = TestGameplay.Run(host.Services, () =>` 单独一行，原表达式去掉 `var x =` 后留在下一行。TASK-06 审查已核过 |
| `Networking.Tests/Integration/BossTowerFrameIntegrationTests.cs`、`IllusionTowerStageFrameIntegrationTests.cs` | 同上，TASK-06 |

**结论**：行级补核没有发现记号级核对之外的问题。§3 的两条实际例外（删 `return`、表达式体改块体）在两种核对里都出现，均已说明。

## 5. 行尾、BOM、末尾换行

`eol.py 0223e646`：306 个改动文件（含 csproj），行尾类别（LF、CRLF、混用）、BOM、末尾换行与基准**全部一致**，差异 0。

两个原本就混用 CRLF 和 LF 的文件仍然只混用原来那几行：

- `AchievementTitleBookTests.cs`：基准 440 行中 439 行是 CRLF，工作区 449 行中 448 行是 CRLF；新增的 9 行都是 CRLF。fork-1 发现第 3 行被主控用 Edit 改成了 CRLF，已改回 LF；
- `CharacterDungeonDifficultyTests.cs`：TASK-06 处理过，本单元没动。

## 6. 迁移点对账（Required check 4）

检索（`begins.py`）把注释和字符串去掉后，用三条正则统计：

- 写事务：`\.Begin\(\s*\)`，以及 `Begin(readOnly: false)`、`Begin(false)`；
- 只读：`readOnly: true` 或 `true`；
- 运行态入口：`x.Begin(参数…)`。

`splitbegin.py` 另查了 `.` 与 `Begin` 被拆到两行的写法，基准上 0 处。

| 项目 | 基准写 `.Begin()`（文件数） | 其中强转写法 | 只读 | 运行态 `Begin(…)` | 工作区写事务：在作业区域内 / 外 | 运行态：内 / 外 |
|---|---:|---:|---:|---:|---|---|
| Application | 604（127） | 26 | 6 | 90 | 555 / 49 | 81 / 9 |
| Persistence | 60（27） | 0 | 2 | 0 | 59 / 1 | 0 / 0 |
| Protocol | 30（8） | 0 | 0 | 12 | 29 / 1 | 11 / 1 |
| Host | 0 | 0 | 0 | 3 | — | 2 / 1 |
| Networking | 0 | 0 | 0 | 1 | — | 0 / 1 |

- **与 Plan 的 694 对账**：604 ＋ 60 ＋ 30 ＝ 694，与 Plan 相等。
- **强转写法**：`((DomainTransactionScope)…).Begin()` 在 Application 有 26 处，都写在同一行，所以已经计入 604，没有被漏计。TASK-07 契约担心的那种差异在本仓没有出现。
- **运行态入口与 Plan 的「约 97」**：
  - Plan 的数是约数。本表 Application 90 处，口径是「`x.Begin(参数)`，不含写事务和只读」；
  - Host 3 处比 TASK-06 的 4 处少 1：本表的正则不计表达式体转发里的 `inner.Begin` 那一种。
- **「作业区域内」**按括号匹配判断：该处是否落在某个 `TestGameplay.Run…(` 的实参括号之内。落在外面的 63 处由 `outside.py` 逐处找到所在方法后归属：
  - **冲突用例里的 5 处**（写事务）：
    - Application：`UsageQuotaTests:148`（T-004b 冲突，只涉及第 1 个事务块）、`DomainEventDispatchTests:172`、`DungeonObjectQueueTests:91,102`；
    - Persistence：`AuthenticationConsumptionTests:19`。
    - 这些按 TASK-06 §0 保持原样。
  - **辅助或夹具方法体里的 58 处**（写 46，运行态 12），调用方都在作业里，运行时内联执行：
    - 例如 `TournamentDungeonTests.Enter`、`EquipmentReinforcementDomainTests.Reinforce`、`MailFixture.SeedStackAsync`、`TestCreatureContainer` 构造函数、`MirrorDungeonEntryTests.Create`；
    - 其中 3 处是被生产代码调用的替身或转发：`ThrowingRuntime.Begin` 2 处、`InterleavedPosition.PositionOf` 1 处；
    - 另有 Networking 的 `SeedReconnectRunCommandHandler`，经真实总线派发，TASK-06 已说明。
    - **证据是探针**：最终一轮中，这些辅助都没有作为 (B) 记录的包裹落点出现。
  - 加法：5 ＋ 58 ＝ 63；与表中「外」一栏相等：49 ＋ 1 ＋ 1 ＋ 9 ＋ 1 ＋ 1 ＋ 1 ＝ 63。
- **最终以探针为准**：P1（写事务开启）的 (B) 记录只出现在冲突总清单所列的用例里，见 `task-07-probe.md` §4。

## 7. 本单元的改动规模（相对 `c19b3b7b`，即 TASK-06 提交）

`sites.py c19b3b7b`。口径：新增行中含 `TestGameplay.Run` 的行数；一行里有两个调用只算一行。

| 项目 | 改动 `.cs` 文件 | 新增包裹调用行 |
|---|---:|---:|
| Application.Tests | 202 | 1263 |
| Persistence.Tests | 1（`EpicBookTests`） | 3 |
| Protocol.Tests | 6 | 10 |
| Host.Tests | 2（`GmCeraHttpTests`、`GmOfficialMailHttpTests`） | 5 |
| Networking.Tests | 1（`GameLoginIntegrationTests`） | 6 |
| 合计 | 212 | 1287 |

- 加法：202 ＋ 1 ＋ 6 ＋ 2 ＋ 1 ＝ 212；1263 ＋ 3 ＋ 10 ＋ 5 ＋ 6 ＝ 1287。
- Protocol 的 6 个文件：`BuyItemCodecTests`、`NpcShopGainProjectionTests`、`DeleteItemCodecTests`、`DisjointItemCodecTests`、`MarriageCapsuleCodecTests`、`MarriageHousingCodecTests`。都是通知 handler 的 `ComposeAsync` 读玩法状态，属 TASK-06 Remaining 第 3 条最后一项，按 TASK-07 §6.1 补包。
- 两个单元累计（相对 `0223e646`）：301 个 `.cs` 文件（299 个已有文件加 2 个新文件），1753 行。

## 8. 计数核对（正式全量，`logs/task-07-build-test.txt`）

- 构建：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
- 测试：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"`。
- 日志开头记了运行时的 `git diff --stat -- Server/src` 与 `git status --short -- Server/src`，两者都为空。

| 项目 | TASK-06 计数 | 本次总数 | 差 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|---:|---:|---:|
| HttpApi | 50 | 50 | 0 | 50 | 0 | 0 |
| Protocol | 1694 | 1694 | 0 | 1694 | 0 | 0 |
| Pvf | 80 | 80 | 0 | 80 | 0 | 0 |
| Persistence | 475 | 475 | 0 | 475 | 0 | 0 |
| Infrastructure | 969 | 969 | 0 | 967 | 0 | 2 |
| Networking | 138 | 138 | 0 | 136 | 0 | 2 |
| Application | 4428 | 4428 | 0 | 4410 | 2 | 16 |
| Host | 165 | 165 | 0 | 165 | 0 | 0 |

- 总数：50 ＋ 1694 ＋ 80 ＋ 475 ＋ 969 ＋ 138 ＋ 4428 ＋ 165 ＝ 7999 ＝ 7986 ＋ 13（N ＝ 13）。
- 通过：50 ＋ 1694 ＋ 80 ＋ 475 ＋ 967 ＋ 136 ＋ 4410 ＋ 165 ＝ 7977 ＝ 7964 ＋ 13。
- 失败 2，跳过 2 ＋ 2 ＋ 16 ＝ 20；7977 ＋ 2 ＋ 20 ＝ 7999。
- 失败的只有 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`。
  - 后者本单元整方法包了：它有一处写事务 `.Begin()`，没有入口。
  - 它在解析迷宫时抛 `InvalidDataException`，那一步在事务之前，失败形状不变。
- 表征测试（`Networking.Tests.Characterization`）：8 条通过，2 条跳过。
- `TestGameplayTests` 13 条全部通过。
- 隔离复跑（`logs/task-07-isolated.txt`）：
  - Application 4428 条：4410 通过，2 失败（同上两条），16 跳过；
  - Networking 138 条：136 通过，2 跳过。
