# TASK-06 机械差异核对与迁移点对账

- 日期 2026-10-22，基准 HEAD `0223e646`，比对对象是工作区。
- 脚本是一次性的，放在 scratchpad，不进仓库：
  `/private/tmp/claude-501/-Users-puxianguo-project/19adbc1e-6ba1-4530-bca0-dcab5d727d8a/scratchpad/diffcheck.py`
- 命令：`python3 diffcheck.py HEAD --json diffcheck.json`

## 1. 口径

`git diff --name-only HEAD -- Server/tests` 里的每个 `.cs` 文件，取 HEAD 版与工作区版比较三件事：

1. **测试方法**：用正则抽出特性加方法名（`Fact`、`Theory`、`SkippableFact`、`SkippableTheory`、`PvfFact`、`PvfTheory`，包括 `[Dfo.Server.Testing.PvfFact]` 这种限定写法），按多重集合比较。
2. **断言行**：含 `Assert.` 或 `Skip.` 的物理行，去掉全部空白后按多重集合比较。
3. **其余差异**：
   - 两版各自切成 C# 记号（标识符、数字、字符串字面量、运算符、标点），空白与换行因此全部不计；
   - 用 `difflib.SequenceMatcher` 对齐两个记号序列；
   - 每个插入片段和删除片段，反复剔除 §2.6 白名单形状之后，剩下的就是「白名单外」。
   - 白名单形状（记号级）：
     - `using Dfo.Server.Testing;`
     - `[var x = | var (a, b) = | x = | return] [await] TestGameplay.Run[Async]( [容器表达式,] [async] () => [{]`
     - `return x;`、`return (a, b);`
     - `});`、`);`、`)`、`{`、`}`、`;`、`async`、`await`
   - 删除片段只允许 `=>`、`;`、`async`。
   - 一行拆成两行、整段缩进 4 格，在记号层面不留差异。
   - 换行符：有两个原本混用 CRLF/LF 的文件被文本模式重写成 LF，已按 HEAD 逐行恢复，见 §4。

新文件（HEAD 中不存在）不参加比较，单独列出：`Server/tests/Support/TestGameplay.cs`、`Server/tests/Dfo.Server.Application.Tests/Execution/TestGameplayTests.cs`。

## 2. 汇总

| 项目 | 改动文件 | 方法 HEAD | 方法 工作区 | 断言行 HEAD | 断言行 工作区 | 白名单外片段 | 集合不等的文件 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Dfo.Server.Host.Tests | 2 | 7 | 7 | 86 | 86 | 0 | 0 |
| Dfo.Server.Networking.Tests | 3 | 3 | 3 | 43 | 43 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 28 | 129 | 129 | 586 | 586 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 56 | 535 | 535 | 2253 | 2253 | 1 | 0 |
| tests/Support（`TestSessions.cs`） | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 合计 | 90 | 674 | 674 | 2968 | 2968 | 1 | 0 |

- 加法，改动文件：2 ＋ 3 ＋ 28 ＋ 56 ＋ 1 ＝ 90。
- 加法，方法：7 ＋ 3 ＋ 129 ＋ 535 ＋ 0 ＝ 674，两侧相等。
- 加法，断言行：86 ＋ 43 ＋ 586 ＋ 2253 ＋ 0 ＝ 2968，两侧相等。
- 90 个文件逐个比较，方法多重集合与断言行多重集合**全部相等**。所以没有任何断言行被改写、拆分或合并，只有缩进变化。
- 另有 5 个 csproj 改动，每个只新增一行 `<Compile Include="…/Support/TestGameplay.cs" Link=… />`，属白名单。Persistence 的那一行放在新建的 `ItemGroup` 里。
- Application.Tests 下没有改动的测试文件：只有新增的 `Execution/TestGameplayTests.cs` 与 csproj 链接（见 §4 的 `DungeonRunTestEntryExtensions` 说明）。

## 3. 白名单外的例外（逐条）

| 文件 | 片段 | 理由 |
|---|---|---|
| `Protocol.Tests/Game/Joust/JoustQueryHandlerTests.cs`（`QueryTwice`） | 插入片段剔除白名单后剩一个 `var` | 记号对齐造成的假象。实际改动是标准的提出返回值形状：`var replies = await TestGameplay.RunAsync(services, async () => { …原来的 var handler、var replies 与循环… return replies; });`，外层保留原来的 `return [.. replies];`。`difflib` 把原来的 `var handler` 与新加的 `var replies` 头部对在了一起，所以剩下一个孤立的 `var`。人工核对 diff，没有白名单外的改动 |

## 4. 其他说明

- **换行符**：
  - `Persistence.Tests/Characters/CharacterDungeonDifficultyTests.cs`（HEAD 149 行里 78 行 CRLF）和 `Protocol.Tests/Game/StateSynchronization/TitleBookProtocolTests.cs` 原本混用 CRLF 与 LF，被包裹脚本整份写成了 LF。
  - 已用对齐脚本按 HEAD 逐行恢复，新加的行沿用前一行的行尾。
  - 恢复后，`git diff --stat` 与 `git diff --ignore-cr-at-eol --stat` 对这两个文件只差 2 行和 4 行，都落在包裹新增或重缩进的行上。
  - fork B 另外发现 `MarriageHousingCodecTests.cs` 是 CRLF，自行恢复过。
- **`DungeonRunTestEntryExtensions` 没有在内部包**（偏离 §3.3，主控方需确认）：
  - 第一次在 `PreparePartyEntry` 内部包上 `TestGameplay.Run` 后，全量运行有 36 条失败，全部是 `InvalidOperationException: Participation changes require a transaction.`：
    - Application 35 条：DungeonBuffUsage 3、DungeonRevival 7、FreeGoldCard 8、ItemSale 11、TeleportConsumable 1、TournamentRewardSweep 5；
    - Networking 1 条：CardRewardReconnect；
    - 加法：3 ＋ 7 ＋ 8 ＋ 11 ＋ 1 ＋ 5 ＝ 35，35 ＋ 1 ＝ 36。同一次运行另有已知的 2 条 `DimensionDungeonTests` 失败，不在此列。
    - 这是修改共享夹具后的第一次全量运行。
  - 原因：`runs.Begin` / `PreparePartyEntry` 要求环境事务，调用方是在执行器外持有事务时调用它的，例如命令处理器（经总线），或 `ShopFixture.InTransaction`。新作业在 `SuppressFlow` 下运行，看不到那个事务。这正是 §2.2 AsyncLocal 边界规则所禁止的切法。
  - 处理：还原该文件，改为由调用方按 T2（`runs.Begin` 类运行态入口）包裹调用区域。
    - Protocol 的调用方已按此处理。
    - Networking 的调用方在一个测试专用命令处理器里，经真实总线派发，留给 T-004c 翻转后自然进入执行器。
    - Application 的调用方交给 TASK-07。
  - 证据：`logs/task-06-fixture-dungeonrun-wrap.txt`。

## 5. 计数核对（全量运行）

见 §7。

## 6. 迁移点对账（Required check 5）

检索命令（HEAD 上，四个项目）：

```
git grep -cE "\.Begin\(\)" HEAD -- 'Server/tests/Dfo.Server.<项目>.Tests/*.cs'
git grep -nE "\(DomainTransactionScope\)|IDomainTransactionAccessor>\(\)\)\.Begin" HEAD -- <四个项目>
git grep -nE "\.Begin\(" HEAD -- <四个项目>      # 再按 readOnly / 运行态入口 / 写事务分类
```

| 项目 | 写事务 `.Begin()` 处数（文件数） | 强转写法 | 只读 `Begin(readOnly: true)` | 运行态入口 `.Begin(…)`（T2，不计入 694） |
|---|---:|---:|---:|---:|
| Persistence | 60（27） | 0 | 2 | 0 |
| Protocol | 30（8） | 0 | 0 | 12（`positions.Begin` 11 处，分布在 TownProjectionHandler 6、SelectCharacterProductionState 3、TownAreaProjectionDelivery 2；另有 CardRewardTests 的 `this._runs.Begin` 1 处） |
| Host | 0 | 0 | 0 | 4（GmPlayerDirectoryHttpTests 3；GmPlayerSamplingHttpTests 1，是替身里的转发 `inner.Begin`） |
| Networking | 0 | 0 | 0 | 1（CardRewardReconnect 的测试命令处理器里的 `runs.Begin`） |

- 与 Plan 694 对账：Plan 的 694 ＝ Application 604 ＋ Persistence 60 ＋ Protocol 30。
  - 本单元负责的 Persistence 60、Protocol 30 与检索**完全相等**。
  - 顺带复核 Application：`.Begin()` 604 处、127 个文件，加法 604 ＋ 60 ＋ 30 ＝ 694。
- 强转写法 `((DomainTransactionScope)…).Begin()` 在这四个项目里**零处**。§3.2 担心的这个盲区只可能出现在 Application.Tests，交给 TASK-07 的探针。
- 与 TASK-06 §3.2 的文件清单出入：
  - §3.2 把 Protocol 的 30 处记成「9 个测试文件和 1 个夹具」，其中列了 `SelectCharacterProductionStateTests`、`TownAreaProjectionDeliveryTests`、`TownProjectionHandlerTests`。这三个文件里只有运行态入口 `positions.Begin(…)`，没有写事务 `.Begin()`。
  - 漏列了 `GroundPickupTests`（3 处）。
  - 实际是 7 个测试文件加 `MoveItemFixture`：CardReward 2、GroundPickup 3、ItemPathFrame 2、AdvanceHuntEnemyQuest 9、MonsterDeath 1、AcceptQuestCommandHandler 11、CreatureIdentityFitsTheWire 1、MoveItemFixture 1。
  - 加法：2 ＋ 3 ＋ 2 ＋ 9 ＋ 1 ＋ 11 ＋ 1 ＋ 1 ＝ 30。处数不变，只是文件归属不同。
- **覆盖核对**：在工作区对这四个项目的每一处 `.Begin()` 检查它是否在某个 `TestGameplay.Run…` 的 lambda 里（`coverage.py`，向上找缩进更浅的 `TestGameplay.Run`）：
  - 87 处在内，3 处报「在外」，逐条如下：
    1. `AuthenticationConsumptionTests.cs` 的冲突用例 `ExactCredentialBindsAgainButOverlappingUncommittedBindsAreRejected`：按 §0 不包，见 `task-06-conflicts.md`。
    2. `SqliteMailboxRepositoryTests.AlreadyReceivedLegacyGoldMigratesAsClaimed`：实际在内。方法体含原始字符串字面量，包裹时没有重新缩进，所以与 `TestGameplay` 行同一缩进，脚本判断失灵。
    3. `MonsterDeathHandlerTests` 的替身总线 `DeathBus.SendAsync`：它是测试替身，由被测 handler 在已整体包裹的测试作业内调用，运行时在作业内。
  - 加法：87 ＋ 3 ＝ 90 ＝ 60 ＋ 30。
- 最终以 TASK-07 的探针为准（Required check 5）。

## 7. 全量计数（`logs/task-06-build-test.txt`）

- 构建：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
- 测试：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"`。

| 项目 | TASK-05 基线 | 本次总数 | 差 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|---:|---:|---:|
| HttpApi | 50 | 50 | 0 | 50 | 0 | 0 |
| Pvf | 80 | 80 | 0 | 80 | 0 | 0 |
| Protocol | 1694 | 1694 | 0 | 1694 | 0 | 0 |
| Persistence | 475 | 475 | 0 | 475 | 0 | 0 |
| Networking | 138 | 138 | 0 | 136 | 0 | 2 |
| Infrastructure | 969 | 969 | 0 | 967 | 0 | 2 |
| Application | 4415 | 4428 | ＋13 | 4410 | 2 | 16 |
| Host | 165 | 165 | 0 | 165 | 0 | 0 |

- 总数：50 ＋ 80 ＋ 1694 ＋ 475 ＋ 138 ＋ 969 ＋ 4428 ＋ 165 ＝ 7999 ＝ 7986 ＋ 13，N ＝ 13。
- 通过：50 ＋ 80 ＋ 1694 ＋ 475 ＋ 136 ＋ 967 ＋ 4410 ＋ 165 ＝ 7977 ＝ 7964 ＋ 13。
- 失败 2，跳过：2 ＋ 2 ＋ 16 ＝ 20；7977 ＋ 2 ＋ 20 ＝ 7999。
- Application 的 ＋13 全部是 `TestGameplayTests`（日志里 13 行「已通过 …TestGameplayTests…」）。4415 ＋ 13 ＝ 4428，4397 ＋ 13 ＝ 4410。
- 失败的只有 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`。
- 表征测试（`Networking.Tests.Characterization`）：8 条通过，2 条跳过（`SelectCharacterCompensationCharacterizationTests`）。
- 隔离复跑（`logs/task-06-isolated.txt`）：Networking 138 条，136 通过、2 跳过；Host 165 条全部通过；两项退出码均为 0。
- 辅助测试连跑 20 次（`logs/task-06-helper-x20.txt`）：20 次全部为「通过 13，失败 0」，退出码 0 共 20 次。
- 红灯记录（`logs/task-06-helper-red.txt`）：把辅助换成「直接调用委托」的桩之后，12 条里 11 条失败，1 条通过。通过的是「返回值与异常原样交还」，直接调用也满足它。当时辅助还没有同步容器重载，所以只有 12 条，第 13 条是之后加的。
