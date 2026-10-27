---
kind: rks-review
target: tasks/TASK-06.md
verdict: approved
---

# TASK-06 结果快速审查（Independent Quick Review）

- 审查对象：`tasks/TASK-06.md` 契约第 2 版、Detailed Plan 与 Result（唯一一轮执行）。
- 改动身份：基准 HEAD `0223e646`，工作区未提交改动——`git diff HEAD` 的 95 个文件（90 个 `.cs`、5 个 csproj），以及未跟踪的 `Server/tests/Support/TestGameplay.cs`、`Server/tests/Dfo.Server.Application.Tests/Execution/TestGameplayTests.cs`、`verification/task-06-conflicts.md`、`verification/task-06-diff-check.md`、`verification/logs/task-06-*`。
- 适用准则：reviewing-work 的 Task Unit 快速审查，加上 Code 准则（只看局部的明显缺陷）。
- 依据：`plan.md` 的过渡期规则、「判据与测试的关系」「测试」两节；`reviews/plan-review-r2.md` R-1、R-2；`spec.md` 附录 F；`tasks/TASK-04.md` Result；`tasks/TASK-07.md` 第 1 版。
- 本审查**没有改动仓库文件**，只写了本文件。临时脚本都放在 scratchpad 的 `rv06/` 下，只读取仓库。我写完审查之后，`git status --short -- Server/src` 仍为空，`git diff --stat HEAD -- Server/src` 也为空。

## 结论

`approved`。没有发现阻断缺陷。

- 辅助确实把测试体交给真实的执行器实例运行。
- 抽查范围内没有误包生产入口。
- 冲突用例都没有包。
- 断言没有被改动。
- 数字与日志一致。

下面的非阻断项，主要是给 TASK-07 和 T-004b 的交接补全，以及几处记载不准确的地方。

## 决定性证据

1. **辅助语义**（`Server/tests/Support/TestGameplay.cs`，对照 `Server/src/Dfo.Server.Abstractions/Execution/GameplayJob.cs` 与 `Application/Execution/GameplayExecutor.cs`）
   - 三级来源的次序是：`GameplayJob.IsOnExecutor` 时内联 → 容器中的 `GameplayExecutor` → 新建执行器，用完 `DisposeAsync`/`Dispose`，再检查 `Completion.IsCompleted`。这与 §1 一致。
   - `IsOnExecutor` 判定的是「在任一执行器上」，所以嵌套调用一律内联在外层执行器上，外层的环境事务等 AsyncLocal 状态得以保留。
   - 同步守卫排在内联判断之前。`Run(async () => …)` 会绑定到 `Run<Task>`，被第二道守卫拦下。
   - 异常经 `TaskCompletionSource.SetException` 原样交还，`SkipException` 由用例 2 用 `Assert.Same` 判定。
   - 执行器的 RQ-003「程序错误粘在作业上」：被测体吞掉的自我等待仍会让作业失败。用例 9 正是依赖这一点，所以它证明的确实是「在容器执行器上运行」。
   - 我在审查时重跑了一次 `TestGameplayTests`，13/13 通过。
2. **不改生产代码**：`Server/src` 的 diff 为空，`grep -rn TestGameplay Server/src` 零命中。`Server/src` 中 `Execution/`、`Outbound/` 之外，没有任何地方读 `GameplayJob`/`GameplayExecutor`/`AssertNotOnExecutor`（检索退出码 1）。§4.1 的「作业内外走同一分支」成立。
3. **误包生产入口（R-2）**：我用自写脚本对每个包裹区域做括号匹配（`rv06/regions.py`），在区域内检索以下标记：真实 `ICommandBus`/`AddCommandDispatching`、dispatcher、`DomainEventDispatcher`、`DrainOnce`、`RefreshAsync`、`HttpClient`、观察者、hosted service、`StartAsync`、`GmTestHost`/`EndToEndHostHarness`。
   - 命中的都是假总线（`RecordingCommandBus`、`DetailCommandBus`、`OneCommandBus` 等）、直接调用的 Delivery 的 `SweepAsync`/`ProjectAsync`，或者 handler 内部持有的真实总线（`LeaveDungeonTests.Give_up_completes_through_real_command_locks…`）。
   - 最后这种情形里，测试体自己并不调用 `SendAsync`；按 plan 的过渡内联规则，它就是生产中 handler 在作业里的形态。我接受执行者的判断。
   - Host、Networking 的区域里没有 HTTP 调用、帧读写或轮询。
   - 4 条后台 worker 用例和 GM HTTP 用例都没有包入口调用。
4. **AsyncLocal 区域**
   - 偏离 1 的证据成立：`logs/task-06-fixture-dungeonrun-wrap.txt` 记有 38 处 `Participation changes require a transaction`，栈经 `TestGameplay.RunCore` → `PreparePartyEntry` → `DungeonParticipationDomainService.get_Pending`，正是「外部开事务、内部作业里用」的切法。还原该夹具、改由调用方包住含事务的整段区域，这是 §2.2 要求的方向。
   - 抽查 Protocol 中 `runs.Begin`/`BeginParty`/`PreparePartyEntry` 落在区域外的 8 处（`rv06/outside.py`），都在私有 `Build` 一类的构造辅助里，由已包裹的测试调用。
   - Networking 那一处在测试命令处理器里，经真实总线派发。
   - 所有 `TestGameplay.RunAsync` 调用都被 `await`/`return`/`=>` 接住。没有一个落在 `void` 方法里，也没有 `Run(async …)` 漏进生产测试。
5. **冲突清单**
   - 我自己检索了四个项目中的 TCS、`Task.WhenAll`、`ManualResetEvent`、`SemaphoreSlim`、`Acquire*Async`、`Task.Run`。命中的 33 个文件，要么已在 `task-06-conflicts.md` 中，要么属于 §2.4 入口用例或 TASK-05 的 Outbound 用例，要么经阅读确认不争用（`SqliteMailboxRepositoryTests` 的 `AcquireWriteAsync` 只是顺序调用）。
   - 清单中的 29 个冲突方法逐个核对（`rv06/conflicts_unwrapped.py`），方法体内都没有 `TestGameplay`。唯一的「包裹」是 `ItemUsageProjectionTests.Daily_worker…` 的最后一行直接投递，与记载一致。
   - research/05 列在这四个项目里的用例全部都有归处。计数出入（PartyCommandHandler 4、两塔各 1、TitleBook 3）我抽读后认为理由成立。
6. **机械核对**
   - 我独立用 `git diff -w -U0` 列出白名单形状以外的新增行和删除行：
     - 新增行只有 4 个出现在 lambda 末尾的提出返回值：`return anonymous`（2 处）、`rolledBack`、`replies`、`(run, afterDeath, clear, bossClear)`。我逐一确认，每个之后紧跟 `});`。
     - 其余的新增、删除行，都是原有语句被拆到区域内外的行。
   - `-w` 下全部改动只有 1412 行新增、20 行删除；原始 diff 中 8658/7266 的体量来自重缩进。
   - 换行符：三处 CRLF 文件里 CRLF 行数的增量等于新增行数。`--ignore-cr-at-eol` 与普通 numstat 只在 `CharacterDungeonDifficultyTests.cs` 上差 1 增 1 删，没有整文件换行改动。BOM 和末尾换行都没有变化。
7. **数字与日志**
   - `task-06-build-test.txt`：0 警告、0 错误；8 个项目的总数为 50/80/1694/475/138/969/4428/165；Application 通过 4410、失败 2、跳过 16；失败的只有 2 条 `DimensionDungeonTests`。
   - `task-06-helper-x20.txt`：「通过 13」20 次，exit 0 共 20 次。
   - `task-06-isolated.txt`：Networking 136 通过 + 2 跳过，Host 165，两次退出码都是 0。
   - HEAD 上 `.Begin()` 的复数：Persistence 60、Protocol 30、Host/Networking 0，强转写法 0。
   - 改动文件的最晚修改时间是 03:52:42，早于正式运行开始的 03:56（日志头 19:56:44Z），证据不是过期的。
8. **抽查 fork 改写的文件**（7 个）：`CardRewardTests`、`DungeonClearProjectionDeliveryTests`（经 Build）、`SetPlayResultHandlerTests`（`Settle` 辅助）、`PartyMoveMapHandlerTests`、`SelectCharacterProductionStateTests`、`TutorialLevelUpHandlerTests`、`ExpiredItemsProjectionTests`。形状都与 `decisions_a.md`/`decisions_b.md` 的记录相符。

## 对两处偏离的评估

- **偏离 1（`DungeonRunTestEntryExtensions` 不在内部包）**：接受。夹具内部包裹会违反 §2.2，有日志为证；处置交给调用方区域包裹，Application 侧交给 TASK-07。交接要写进 TASK-07 的契约，见 N-1。
- **偏离 2（两个同步容器重载）**：接受。它落实的是 §1「手里有容器的测试一律写 services 重载」，在同步调用场合的必要补充；语义与异步版同源（`RunCore` 共用），由用例 9 覆盖。

## 非阻断项

- **N-1　TASK-07 契约没有接住本单元的交接。** TASK-07 第 1 版只写了「规则沿用 TASK-06 §2」，没有提到以下几项：
  - `DungeonRunTestEntryExtensions` 的调用方必须包住**含环境事务的整段区域**，夹具内部不得包；
  - `Application.Tests/Support/` 下的适配器（`LegacyTownAreaTestAdapter`、`PartyServiceTestAdapter`、`TowerPresenceProxy` 等）也要处理，而它的 Owned boundary 只写了「Application.Tests 的测试文件」，适配器是否在内有歧义；
  - Remaining 第 3 条列出的、有意留在作业外的点。

  建议主控方在 TASK-07 开始前补一版修订，或在其 Detailed Plan 中显式引用 TASK-06 Result 的 Remaining 一节。
- **N-2　机械核对脚本的白名单过宽，TASK-07 复用之前应收紧。** `diffcheck.py` 的 `residue()` 会从任意插入片段中，逐个剔除单独的 `return x ;`、`await`、`)`、`}`、`;`。所以「在 lambda 中途插入一条提前返回」或「孤立加一个 `await`」都能通过核对，而断言多重集合不会变。本单元我已用行级 `-w` diff 补核，确认没有这种情况。TASK-07 要处理约 191 个文件，建议补两条约束：
  - 插入的 `return` 之后必须紧跟 lambda 的闭合（`});`、`})`）；
  - 插入的 `await` 必须紧邻 `TestGameplay.Run…`。
- **N-3　卡牌协调器用例在作业内轮询入口进度，与本单元自己定的规则不一致。** `DungeonCardPresentationCoordinatorTests` 的 7 个非冲突用例有 21 处 `WaitUntilAsync` 包在作业里，其中几处等待的是 `Delay.ReleaseAsync`（计时器入口）之后的发送结果。而 Result 对 Networking 的理由正是「轮询入口进度不包」。
  - 今天这些作业都在各自新建的执行器上，不会互等，所以不构成缺陷。
  - T-004b 迁卡牌计时器时，应复核：计时器作业与轮询作业是否可能落到同一个执行器上。
  - 建议把这一点补进 Remaining 第 3 条，或在 T-004b 的交接中点名。
- **N-4　T-004b 的「容器执行器生效后」核对清单不全。** Remaining 只点名了 `AuthenticationFixture.Consume` 与 Networking 的短作业。下面这些 Protocol 中写成 `RunAsync(provider/services, …)` 的区域没有列出，它们在 `AddGameplayExecution` 注册后会改用容器执行器：
  - `LeaveDungeonTests.Give_up_completes_through_real_command_locks…`
  - `PartySelectDungeonHandlerTests.The_leader_cancels_selection…`
  - `MonsterDeathHandlerTests` 的 1 个用例
  - `QuestClearVerticalTests` 的两段
  - `JoustQueryHandlerTests.QueryTwice`

  它们大多带超时，挂起会表现为失败而不是无声通过，所以只是交接不完整。
- **N-5　记载不准确（不影响结论）。**
  - Result「Why」写的是 `CardRewardTests.World`「只在已整体包裹的测试里被 `new`」。实际有 31 个未包裹的方法直接 `new World()`。它们之所以无害，是因为构造函数中没包的前半段只做 `new`，T2 调用都在已包裹的后半段，`Send`/`Handle` 也在内部包了。建议把措辞改成这个真实理由。
  - `task-06-diff-check.md` §4 称 `TitleBookProtocolTests.cs`「混用 CRLF 与 LF」，但 HEAD 上它是 170/170 全 CRLF，现在是 174/174 全 CRLF；恢复本身是完整的。
- **N-6　盲区已知、本单元不承担的残余风险**：经接口变量使用的 T2 类型、构造辅助被未包裹的方法调用等，脚本无法穷尽，按计划以 TASK-07 的探针为准。本审查的抽查没有发现这类漏包。
