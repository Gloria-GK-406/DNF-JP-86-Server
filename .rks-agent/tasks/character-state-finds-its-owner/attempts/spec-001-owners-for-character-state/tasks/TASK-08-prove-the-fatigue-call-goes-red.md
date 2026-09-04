# TASK-08 — 证明那次疲劳报告被改坏时判据会红

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 两次变异验证的记录，证明进本处那次疲劳报告被移除或被改错时，指名的判据会失败，还原后恢复通过。
- Referenced spec requirements: RQ-011
- Referenced plan sections: T-008；BD-014
- Owned boundary: 变异验证本身及其记录。若变异发现判据其实不会红，则补强判据也属本任务。
- Permitted files or interfaces:
  - 临时改动 `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs`（**必须还原**）
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/` 下判据的补强
- Dependencies: TASK-07
- Required dependency outputs: TASK-07 的调用点与判据
- Acceptance conditions:
  - 变异一（移除调用）使**指名的**判据失败
  - 变异二（把报告的数改错）使**指名的**判据失败
  - 两次还原之后判据恢复通过，工作区无变异残留
  - **只说「判据红了」不算**，必须指名是哪一条
- Task-specific authorization: 不建 Git 检查点、不提交。变异改动必须在本任务结束前完全还原。
- Required methods: test-driven-development
- Required checks: CK-031, CK-032, CK-033
- Required evidence:
  - 变异一的改动内容、失败判据的全限定名与失败输出
  - 变异二的改动内容、失败判据的全限定名与失败输出
  - 还原后的复跑结果
  - 工作区无变异残留的证据

## Contract Amendment 1 — 控制器追加（来自 TASK-07 的评审观察 O-2）

TASK-07 的独立快审指出一处判据缺口，本 Task 承接：

`DungeonEntrySlice` 里那段注释声称「被拒绝的选本一次报告都不发」，但**没有任何判据钉住它**。
评审的具体证据：`Without_a_published_session_nothing_is_entered` 把 `RecordingFatigue` 内联进构造器
而未持引用，因此 `entered is null` 那条分支**完全无判据**。

**追加要求**：本 Task 的变异验证不止要证明「报告发生了、数对」，还要证明**「不该报告时确实没报告」**
——即被拒绝的进本路径上，疲劳接缝一次都没被调用。若现有判据挡不住这一点，补一条能挡住的。

## Execution Result 1

### 0. 判据环境与开工基线

命令与完整原始输出（本节所有全量数跑的都是这一条）：

```
$ cd E:/Project/DNF/DNF-JP-86-Server/.worktrees/character-state-finds-its-owner
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
exit=0
Passed! - Failed: 0, Passed:  52, Skipped: 0, Total:  52 - Dfo.Server.Networking.Tests.dll (net10.0)
Passed! - Failed: 0, Passed: 508, Skipped: 0, Total: 508 - Dfo.Server.Protocol.Tests.dll (net10.0)
Passed! - Failed: 0, Passed:  60, Skipped: 0, Total:  60 - Dfo.Pvf.Tests.dll (net10.0)
Passed! - Failed: 0, Passed:   6, Skipped: 0, Total:   6 - Dfo.Server.Host.Tests.dll (net10.0)
Passed! - Failed: 0, Passed: 256, Skipped: 0, Total: 256 - Dfo.Server.Persistence.Tests.dll (net10.0)
Passed! - Failed: 0, Passed: 223, Skipped: 2, Total: 225 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
Passed! - Failed: 0, Passed: 962, Skipped: 0, Total: 962 - Dfo.Server.Application.Tests.dll (net10.0)
```

52+508+60+6+256+223+962 = **2067 通过 / 2 跳过 / 0 失败**，与控制器给的基线逐位相符。
那 2 个跳过是档案在场才跳过的 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。
本 Task 全程未见 AGENTS.md 记的两类偶发假红。

被变异文件的开工指纹（Python 读 bytes）：

```
$ python -c "import hashlib; b=open('.../DungeonEntrySlice.cs','rb').read(); print(len(b), hashlib.sha256(b).hexdigest())"
bytes= 34598
sha256= 50e3a5bc4fc4775fa72796fd08439745e8f35858d33b6696c29f152215399b7d
crlf_count= 0 lf_only= 718
```

全文副本存 scratchpad `task08/DungeonEntrySlice.cs.orig`，回滚一律用它覆盖，不用 git stash。
开工 `git status --short` / `git diff --numstat` 原样存 `task08/baseline-git-status.txt` /
`task08/baseline-git-numstat.txt`，收工逐行比对见第 6 节。

### 1. 变异一：移除进本处那次疲劳报告

**改动内容**：删掉 `SelectDungeonHandler.HandleAsync` 里下面这 8 行（含尾随空行，共 457 字节），
位置在 `var (layout, visit) = entered;` 与 `logger.LogInformation(` 之间：

```csharp
        // Reported once the entry has actually happened, so that a refused selection reports
        // nothing. What the seam does with it is nothing at all today -- see
        // ICharacterFatigueDomainService -- which is why this is the report and not a check: there
        // is no balance to be short of.
        await fatigue
            .SpendAsync(session.CharacterId, FatigueAnEntryCosts, cancellationToken)
            .ConfigureAwait(false);
```

**第一次结果不是判据失败，是编译失败**，如实记下：

```
E:\...\Server\src\Dfo.Server.Protocol\Game\DungeonEntry\DungeonEntrySlice.cs(426,36):
error CS9113: Parameter 'fatigue' is unread. [E:\...\Dfo.Server.Protocol.csproj]
```

`fatigue` 是主构造器参数，删掉唯一读它的地方就触发 CS9113，`TreatWarningsAsErrors` 把它升成错误，
整个解决方案编译不过，**一条判据都没跑到**。这是一道真实的护栏，但它不是「指名的判据失败」，
所以做了变异一的最小可编译形式：

**变异一 b（实际用于 CK-031 的那次）**：同样删掉上面 8 行，只补一行 `_ = fatigue;` 让参数仍被读，
其余一字不改。这一行只让编译通过，不向接缝报告任何东西。

**失败的判据（全限定名）**：

```
Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam
```

**失败输出原文**（`DOTNET_CLI_UI_LANGUAGE=en` 复跑 `Server/tests/Dfo.Server.Protocol.Tests`，未裁剪）：

```
[xUnit.net 00:00:00.33]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [FAIL]
  Failed Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [2 ms]
  Error Message:
   Assert.Single() Failure: The collection was empty
  Stack Trace:
     at Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam() in E:\Project\DNF\DNF-JP-86-Server\.worktrees\character-state-finds-its-owner\Server\tests\Dfo.Server.Protocol.Tests\Game\DungeonEntry\SelectDungeonHandlerTests.cs:line 140
--- End of stack trace from previous location ---

Failed!  - Failed:     1, Passed:   509, Skipped:     0, Total:   510, Duration: 820 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
```

Expected / Actual：断言是 `Assert.Single(fatigue.Spent)`，期望恰好一条报告，实得空集合。

**同时仍然通过的判据**（全量跑，`task08/mut1b-full.txt`，未裁剪的项目行）：

```
Failed!  - Failed:     1, Passed:   507, Skipped:     0, Total:   508 - Dfo.Server.Protocol.Tests.dll
Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52 - Dfo.Server.Networking.Tests.dll
Passed!  - Failed:     0, Passed:    60, Skipped:     0, Total:    60 - Dfo.Pvf.Tests.dll
Passed!  - Failed:     0, Passed:   256, Skipped:     0, Total:   256 - Dfo.Server.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6 - Dfo.Server.Host.Tests.dll
Passed!  - Failed:     0, Passed:   223, Skipped:     2, Total:   225 - Dfo.Server.Infrastructure.Tests.dll
Passed!  - Failed:     0, Passed:   962, Skipped:     0, Total:   962 - Dfo.Server.Application.Tests.dll
```

（该次全量跑在补判据之前，故 Protocol.Tests 总数是 508。补判据后同一变异的定向复跑是 510 总数、
509 通过、1 失败，见上。）

DungeonEntry 命名空间下的逐条结果（`--logger "console;verbosity=detailed"`，
`task08/en-mut1b-detailed.txt`；此处贴与疲劳有关的两个 fixture 共 10 条，另 26 条
`DungeonEntryCodecTests` 全部 Passed，命令 `grep -c "^\s\+Passed\s\+Dfo...DungeonEntry\."`
输出 `DungeonEntry passed count: 35`，即 36−1）：

```
Failed Dfo...SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [1 ms]
Passed Dfo...SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam [11 ms]
Passed Dfo...SelectDungeonHandlerTests.An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy [< 1 ms]
Passed Dfo...SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_carried [3 ms]
Passed Dfo...SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_cleared [< 1 ms]
Passed Dfo...SelectDungeonHandlerTests.The_answer_to_an_entry_is_what_it_was [1 ms]
Passed Dfo...SelectDungeonHandlerTests.Without_a_published_session_nothing_is_entered [15 ms]
Passed Dfo...SelectDungeonHandlerTests.Without_a_published_session_nothing_is_reported_to_the_fatigue_seam [< 1 ms]
Passed Dfo...UnaccountedFatigueTests.A_reported_spend_moves_no_number_the_seam_answers [< 1 ms]
Passed Dfo...UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries [5 ms]
```

（此份逐条是在补判据之后重放变异一 b 取得的，所以两条新判据也在列并且是绿的——它们钉的是被拒绝
的路径，删掉成功路径上的报告碰不到它们。）

变异是定向的：整个仓库只有这一条红。

**还原**：用 `task08/DungeonEntrySlice.cs.orig` 覆盖，随即校验

```
bytes= 34598
sha256= 50e3a5bc4fc4775fa72796fd08439745e8f35858d33b6696c29f152215399b7d
MATCH_BASELINE= True
```

### 2. 变异二：把报告的数改错

**改动内容**：唯一一行，`SelectDungeonHandler` 的常量

```diff
-    public const ushort FatigueAnEntryCosts = 1;
+    public const ushort FatigueAnEntryCosts = 2;
```

**失败的判据（全限定名）**：

```
Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam
```

**失败输出原文**（未裁剪）：

```
[xUnit.net 00:00:00.36]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [FAIL]
  Failed Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [2 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 1
Actual:   2
  Stack Trace:
     at Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam() in E:\Project\DNF\DNF-JP-86-Server\.worktrees\character-state-finds-its-owner\Server\tests\Dfo.Server.Protocol.Tests\Game\DungeonEntry\SelectDungeonHandlerTests.cs:line 142
--- End of stack trace from previous location ---

Failed!  - Failed:     1, Passed:   509, Skipped:     0, Total:   510, Duration: 786 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
```

Expected: 1 / Actual: 2。这条之所以能红，是因为判据把 1 写死在测试里而没有去读
`SelectDungeonHandler.FatigueAnEntryCosts`——它自己的 remarks 就是这么写的。

**同时仍然通过的判据**（全量跑 `task08/mut2-full.txt`，未裁剪的项目行）：

```
Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52 - Dfo.Server.Networking.Tests.dll
Passed!  - Failed:     0, Passed:    60, Skipped:     0, Total:    60 - Dfo.Pvf.Tests.dll
Failed!  - Failed:     1, Passed:   507, Skipped:     0, Total:   508 - Dfo.Server.Protocol.Tests.dll
Passed!  - Failed:     0, Passed:   256, Skipped:     0, Total:   256 - Dfo.Server.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6 - Dfo.Server.Host.Tests.dll
Passed!  - Failed:     0, Passed:   223, Skipped:     2, Total:   225 - Dfo.Server.Infrastructure.Tests.dll
Passed!  - Failed:     0, Passed:   962, Skipped:     0, Total:   962 - Dfo.Server.Application.Tests.dll
```

DungeonEntry 逐条（`task08/en-mut2-detailed.txt`）与变异一 b 完全同形：同一条红，
另 35 条绿（含两条 `UnaccountedFatigueTests` 与两条新判据）。

**特别记一笔**：`UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries`
在变异二下**仍然通过**。它断言接缝答的是 188/0，而报告多少点数不影响那个答案——这正是「接缝空转」
的直接体现，也说明变异二证明的只有调用点报的数，不是任何账。

### 3. CK-033：还原后复跑

还原变异二之后立刻全量复跑（`task08/ck033-restored.txt`）：

```
exit=0
Passed! - Failed: 0, Passed:  52, Skipped: 0, Total:  52 - Dfo.Server.Networking.Tests.dll
Passed! - Failed: 0, Passed:  60, Skipped: 0, Total:  60 - Dfo.Pvf.Tests.dll
Passed! - Failed: 0, Passed: 508, Skipped: 0, Total: 508 - Dfo.Server.Protocol.Tests.dll
Passed! - Failed: 0, Passed: 256, Skipped: 0, Total: 256 - Dfo.Server.Persistence.Tests.dll
Passed! - Failed: 0, Passed:   6, Skipped: 0, Total:   6 - Dfo.Server.Host.Tests.dll
Passed! - Failed: 0, Passed: 223, Skipped: 2, Total: 225 - Dfo.Server.Infrastructure.Tests.dll
Passed! - Failed: 0, Passed: 962, Skipped: 0, Total: 962 - Dfo.Server.Application.Tests.dll
```

**2067 / 2 / 0**，与开工基线逐位相同。

### 4. 变异三（合同增补）：让被拒绝的进本路径也报告

#### 4.1 先核实缺口是否属实——属实

核实手段与结论：

1. `grep -rln "SelectDungeonHandler" Server/tests/` 的源文件命中只有一个：
   `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs`
   （其余命中全是 `bin/` `obj/` 下的编译产物）。`SelectDungeonRefusal` / `DungeonNotFound`
   在测试源码里**零命中**，只命中 4 个 `Dfo.Server.Protocol.dll` 二进制。
2. 该文件里唯一的 `IDungeonVisitDomainService` 替身是 `RecordingVisits`，它的 `EnterAsync`
   **无条件**返回非 null。因此没有任何判据能走到 `entered is null` 那条分支，
   更谈不上对它断言——**这条分支当时完全无判据**，评审的论断成立。
3. `Without_a_published_session_nothing_is_entered` 确实把 `new RecordingFatigue()` 直接写进
   构造器实参而未持引用，事后无从断言，对疲劳接缝**什么也没说**。

#### 4.2 变异内容

一次变异同时打两条被拒绝的出路（两条都是「没进本」，各有各的 `return`）：

```diff
             logger.LogWarning(
                 "Select dungeon {ConnectionId}: no published session, ignoring dungeon {DungeonId}",
                 context.Connection.ConnectionId,
                 message.DungeonId);
+            await fatigue
+                .SpendAsync(
+                    session?.CharacterId ?? default, FatigueAnEntryCosts, cancellationToken)
+                .ConfigureAwait(false);
             return;
```

```diff
                 context.Connection.ConnectionId,
                 message.DungeonId);
+            await fatigue
+                .SpendAsync(session.CharacterId, FatigueAnEntryCosts, cancellationToken)
+                .ConfigureAwait(false);
             await sender.SendExplicitAsync(
```

（第一处 `session` 可能为 null——`session is null` 与 `TryGet` 失败共用同一个门，
所以取 `session?.CharacterId ?? default`；`CharacterId` 是 `readonly record struct`，`default` 合法。）

#### 4.3 补判据之前跑一次：**全量零红**

```
$ PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo   （task08/mut3-before-newtests.txt）
exit=0
Passed! - Failed: 0, Passed:  52, Skipped: 0, Total:  52 - Dfo.Server.Networking.Tests.dll
Passed! - Failed: 0, Passed: 508, Skipped: 0, Total: 508 - Dfo.Server.Protocol.Tests.dll
Passed! - Failed: 0, Passed:  60, Skipped: 0, Total:  60 - Dfo.Pvf.Tests.dll
Passed! - Failed: 0, Passed: 256, Skipped: 0, Total: 256 - Dfo.Server.Persistence.Tests.dll
Passed! - Failed: 0, Passed:   6, Skipped: 0, Total:   6 - Dfo.Server.Host.Tests.dll
Passed! - Failed: 0, Passed: 223, Skipped: 2, Total: 225 - Dfo.Server.Infrastructure.Tests.dll
Passed! - Failed: 0, Passed: 962, Skipped: 0, Total: 962 - Dfo.Server.Application.Tests.dll
```

**2067 / 2 / 0，与基线一字不差。** 被拒绝的两条路径都多发了一次疲劳报告，
`DungeonEntrySlice` 那段「so that a refused selection reports nothing」的注释被代码直接推翻，
而**全仓 2067 条判据一条都没红**。这就是本 Task 找到的真实缺口，如实记在此处，
不是「补完就当无事发生」。

#### 4.4 补了哪两条判据，以及为什么

还原源码后，在 `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs`
**追加**（不改任何既有用例的期望值与断言；`diff` 计数为 added 93 行 / removed 0 行）：

- `A_refused_selection_reports_nothing_to_the_fatigue_seam`
  —— 新增替身 `RefusingVisits`（`EnterAsync` 返回 null）驱动 `entered is null` 那条从无判据的分支，
  断言 `Assert.Empty(fatigue.Spent)`；**同时**断言送出的是 `SelectDungeonRefusal` 且
  `ErrorCode == DungeonNotFound`。后半句是防空转的：不钉住拒绝帧的话，空报告表也可能只是
  「handler 更早就停了」，判据会因为与它名字无关的理由长绿。
- `Without_a_published_session_nothing_is_reported_to_the_fatigue_seam`
  —— 持 `RecordingFatigue` 的引用走无会话的门，断言 `Assert.Empty(visits.Facts)` 与
  `Assert.Empty(fatigue.Spent)`。**另起一条而不是改写
  `Without_a_published_session_nothing_is_entered`**，既有用例一字未动。

补完在**干净源码**上先跑一次确认是绿的（`task08/newtests-clean.txt`）：Protocol.Tests
508 → **510** 通过，全量 **2069 / 2 / 0**。

#### 4.5 重做变异三，证明新判据会红

**失败的判据（全限定名）**：

```
Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam
Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Without_a_published_session_nothing_is_reported_to_the_fatigue_seam
```

**失败输出原文**（未裁剪）：

```
[xUnit.net 00:00:00.32]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam [FAIL]
  Failed Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam [13 ms]
  Error Message:
   Assert.Empty() Failure: Collection was not empty
Collection: [Tuple (4660, 1)]
  Stack Trace:
     at Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam() in E:\Project\DNF\DNF-JP-86-Server\.worktrees\character-state-finds-its-owner\Server\tests\Dfo.Server.Protocol.Tests\Game\DungeonEntry\SelectDungeonHandlerTests.cs:line 176
--- End of stack trace from previous location ---
[xUnit.net 00:00:00.34]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Without_a_published_session_nothing_is_reported_to_the_fatigue_seam [FAIL]
  Failed Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Without_a_published_session_nothing_is_reported_to_the_fatigue_seam [1 ms]
  Error Message:
   Assert.Empty() Failure: Collection was not empty
Collection: [Tuple (0, 1)]
  Stack Trace:
     at Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Without_a_published_session_nothing_is_reported_to_the_fatigue_seam() in E:\Project\DNF\DNF-JP-86-Server\.worktrees\character-state-finds-its-owner\Server\tests\Dfo.Server.Protocol.Tests\Game\DungeonEntry\SelectDungeonHandlerTests.cs:line 207
--- End of stack trace from previous location ---

Failed!  - Failed:     2, Passed:   508, Skipped:     0, Total:   510, Duration: 804 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
```

Expected / Actual：两条都期望空报告表，实得一条报告。`Tuple (4660, 1)` 里 4660 = 0x1234 =
`Identity.CharacterId`，对应被拒绝的选本那条路径；`Tuple (0, 1)` 里 0 是无会话时的
`default(CharacterId)`，对应无发布会话那条路径。**两个数各自指向一条被打的路径，互不混淆。**

**同时仍然通过的判据**——全量跑（`task08/mut3-after-newtests.txt`，未裁剪的项目行）：

```
Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52 - Dfo.Server.Networking.Tests.dll
Failed!  - Failed:     2, Passed:   508, Skipped:     0, Total:   510 - Dfo.Server.Protocol.Tests.dll
Passed!  - Failed:     0, Passed:    60, Skipped:     0, Total:    60 - Dfo.Pvf.Tests.dll
Passed!  - Failed:     0, Passed:   256, Skipped:     0, Total:   256 - Dfo.Server.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6 - Dfo.Server.Host.Tests.dll
Passed!  - Failed:     0, Passed:   223, Skipped:     2, Total:   225 - Dfo.Server.Infrastructure.Tests.dll
Passed!  - Failed:     0, Passed:   962, Skipped:     0, Total:   962 - Dfo.Server.Application.Tests.dll
```

DungeonEntry 逐条（`task08/en-mut3-detailed.txt`，两个 fixture 全 10 条，未裁剪；另 26 条
`DungeonEntryCodecTests` 全 Passed）：

```
Failed Dfo...SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam [11 ms]
Failed Dfo...SelectDungeonHandlerTests.Without_a_published_session_nothing_is_reported_to_the_fatigue_seam [< 1 ms]
Passed Dfo...SelectDungeonHandlerTests.An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy [< 1 ms]
Passed Dfo...SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_carried [2 ms]
Passed Dfo...SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_cleared [< 1 ms]
Passed Dfo...SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [< 1 ms]
Passed Dfo...SelectDungeonHandlerTests.The_answer_to_an_entry_is_what_it_was [1 ms]
Passed Dfo...SelectDungeonHandlerTests.Without_a_published_session_nothing_is_entered [16 ms]
Passed Dfo...UnaccountedFatigueTests.A_reported_spend_moves_no_number_the_seam_answers [< 1 ms]
Passed Dfo...UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries [5 ms]
```

这份逐条把增补的论断钉死了两次：

- `Without_a_published_session_nothing_is_entered` 在**它自己那条路径被多打了一次报告的情况下
  仍然 Passed**——它对疲劳接缝确实什么也没说；
- `Entry_reports_what_it_costs_to_the_fatigue_seam` 也 **Passed**——变异一/二覆盖的那条判据
  管不到被拒绝的路径。两条新判据不是既有判据的重复。

**还原**：`sha256= 50e3a5bc...399b7d`，`MATCH_BASELINE= True`；`cmp` 与开工副本 0 字节差异。

### 5. 这三次变异各证明了什么、又没触及什么

**证明了「非空转」的判据（逐条对应）**

| 变异 | 被证明非空转的判据（全限定名） | 证明了它钉住什么 |
|---|---|---|
| 一 b（移除调用） | `...SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam` | 进本成功路径上**那次报告确实发生** |
| 二（数改错） | 同上一条 | 报告的**点数是 1**，且不是从被测常量抄来的 |
| 三（拒绝路径也报告） | `...SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam` | `entered is null` 的拒绝路径**一次都不报告**，且确实走到了拒绝帧 |
| 三（同一次变异，另一处） | `...SelectDungeonHandlerTests.Without_a_published_session_nothing_is_reported_to_the_fatigue_seam` | 无发布会话的门后**一次都不报告** |

变异一还附带证明了一道非判据护栏：CS9113 + `TreatWarningsAsErrors` 使「悄悄删掉调用点」
连编译都过不去。这是护栏不是判据，单列在此，不计入上表。

**这三次变异都没有触及的判据（如实列全）**

1. `Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.A_reported_spend_moves_no_number_the_seam_answers`
2. `Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries`
   —— 这两条测的是 `UnaccountedCharacterFatigueDomainService` 这个实现本身，不是调用点；
   任何调用点变异都不可能让它们红，本 Task 三次变异下它们全程 Passed。**接缝空转这一半仍然只由
   它们自己担保，未经变异验证。**
3. `...SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_carried`
4. `...SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_cleared`
5. `...SelectDungeonHandlerTests.The_answer_to_an_entry_is_what_it_was`
6. `...SelectDungeonHandlerTests.An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy`
7. `...SelectDungeonHandlerTests.Without_a_published_session_nothing_is_entered`
   —— 同一个 fixture 里的另 5 条，属 TASK-06 的题目（问日志而非会话副本）与既有守卫，
   本 Task 三次变异全程 Passed，**本 Task 对它们不作任何非空转声称**。
8. `Dfo.Server.Protocol.Tests.Game.DungeonEntry.DungeonEntryCodecTests` 的 **26 条**
   （`A_body_shorter_than_its_evidenced_fields_is_refused` … `The_support_roster_is_a_count_of_zero`，
   全名单见 `task08/dungeonentry-tests.txt`）
   —— 编解码向量，三次变异全程 Passed，未触及。
9. 其余 **2033** 条（收口总数 2069 − DungeonEntry 命名空间 36 条 = 2033）——分布在
   Networking / Pvf / Persistence / Host / Infrastructure / Application 与 Protocol 其余部分，
   三次变异全程 Passed，未触及。

**本 Task 明确不声称的事**（不从样本推全体）：

- 不声称疲劳这条线以外的任何判据非空转；
- 不声称疲劳「记账」有判据——接缝本来就不记账，`RefundAsync` 至今**没有任何调用方**，
  这条方向的调用点无判据可言，也不在本 Task 的题目里；
- 不声称进本确认帧上那对 188/0 与本次报告有关——那对数来自
  `CharacterRosterService` 的硬编码，变异二下 `The_pair_it_answers_...` 仍绿即是证据；
- 不声称档案的 `[no fatigue]` / `DungeonDefinition.ConsumesFatigue` 有任何消费方判据。

### 6. 还原证据与工作区状态

**被变异文件逐字节相同**（Python 读 bytes；本机 MSYS 的 grep/sed/tail 对 CR 不可靠，故不用它们）：

```
$ python -c "import hashlib; b=open('Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs','rb').read(); print('bytes=',len(b)); print('sha256=',hashlib.sha256(b).hexdigest()); print('md5=',hashlib.md5(b).hexdigest())"
bytes= 34598
sha256= 50e3a5bc4fc4775fa72796fd08439745e8f35858d33b6696c29f152215399b7d
md5= fb10b7957964380984f7e97745e2125b

$ cmp Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs <scratchpad>/task08/DungeonEntrySlice.cs.orig
cmp vs pre-mutation copy: IDENTICAL (0 differing bytes)
```

开工指纹是 `bytes=34598 / sha256=50e3a5bc...399b7d`，收工完全一致——三次变异（含变异一的原始形
与一 b、以及为取英文输出做的四次定向重放，共七次落盘改动）**全部还原**，工作区无变异残留。
变异体只存在于 scratchpad（`task08/DungeonEntrySlice.cs.mut3`），不在工作区。

**收工 `git status --short`**（原样，未裁剪）：

```
 M Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs
 M Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs
 M Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
 M Server/src/Dfo.Server.Application/ApplicationModule.cs
 M Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
 M Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
 M Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs
 M Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
 M Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs
 M Server/src/Dfo.Server.Persistence/PersistenceModule.cs
 M Server/src/Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs
 M Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
 M Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs
 M Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
 M Server/src/Dfo.Server.Protocol/Game/TownProjection/TownReturnProjection.cs
 M Server/tests/Dfo.Server.Application.Tests/Characters/CarriedGoldCapTests.cs
 M Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs
 M Server/tests/Dfo.Server.Application.Tests/Characters/CharacterItemUseTests.cs
 M Server/tests/Dfo.Server.Application.Tests/Characters/CharacterRepositoryProductsTests.cs
 M Server/tests/Dfo.Server.Application.Tests/Characters/NoCarriedGold.cs
 M Server/tests/Dfo.Server.Application.Tests/Characters/RemainingSkillPointTests.cs
 M Server/tests/Dfo.Server.Application.Tests/Game/UseStackableHandlerTests.cs
 M Server/tests/Dfo.Server.Application.Tests/Inventory/ItemUseFixtures.cs
 M Server/tests/Dfo.Server.Application.Tests/Sessions/SessionGenerationOwnershipTests.cs
 M Server/tests/Dfo.Server.Application.Tests/Support/RecordingCharacterTable.cs
 M Server/tests/Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs
 M Server/tests/Dfo.Server.Persistence.Tests/Support/CharacterSide.cs
 M Server/tests/Dfo.Server.Protocol.Tests/Game/CarriedGoldStandIns.cs
 M Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs
 M Server/tests/Dfo.Server.Protocol.Tests/Game/ExperienceFrameStandIns.cs
 M Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs
?? .rks-agent/tasks/character-state-finds-its-owner/
?? Server/src/Dfo.Server.Abstractions/Characters/ICharacterFatigueDomainService.cs
?? Server/src/Dfo.Server.Abstractions/Characters/ICharacterPositionDomainService.cs
?? Server/src/Dfo.Server.Abstractions/Characters/ICharacterSettingsDomainService.cs
?? Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboBook.cs
?? Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboDomainStore.cs
?? Server/src/Dfo.Server.Application/Characters/CharacterPositionDomainService.cs
?? Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs
?? Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs
?? Server/src/Dfo.Server.Application/Characters/UnaccountedCharacterFatigueDomainService.cs
?? Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterIdentityTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterPositionTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterSettingsTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs
?? Server/tests/Dfo.Server.Persistence.Tests/Characters/CharacterIdentityReadTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/UnaccountedFatigueTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs
```

**收工 `git diff --numstat`**（原样，未裁剪）：

```
17	0	Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs
41	2	Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs
3	3	Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
17	2	Server/src/Dfo.Server.Application/ApplicationModule.cs
1	2	Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
57	24	Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
24	4	Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs
1	4	Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
34	4	Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs
6	0	Server/src/Dfo.Server.Persistence/PersistenceModule.cs
8	2	Server/src/Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs
55	8	Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
8	3	Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs
36	36	Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
10	2	Server/src/Dfo.Server.Protocol/Game/TownProjection/TownReturnProjection.cs
3	14	Server/tests/Dfo.Server.Application.Tests/Characters/CarriedGoldCapTests.cs
1	2	Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs
102	38	Server/tests/Dfo.Server.Application.Tests/Characters/CharacterItemUseTests.cs
74	20	Server/tests/Dfo.Server.Application.Tests/Characters/CharacterRepositoryProductsTests.cs
58	10	Server/tests/Dfo.Server.Application.Tests/Characters/NoCarriedGold.cs
3	13	Server/tests/Dfo.Server.Application.Tests/Characters/RemainingSkillPointTests.cs
1	3	Server/tests/Dfo.Server.Application.Tests/Game/UseStackableHandlerTests.cs
0	47	Server/tests/Dfo.Server.Application.Tests/Inventory/ItemUseFixtures.cs
12	7	Server/tests/Dfo.Server.Application.Tests/Sessions/SessionGenerationOwnershipTests.cs
3	0	Server/tests/Dfo.Server.Application.Tests/Support/RecordingCharacterTable.cs
4	0	Server/tests/Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs
9	0	Server/tests/Dfo.Server.Persistence.Tests/Support/CharacterSide.cs
5	0	Server/tests/Dfo.Server.Protocol.Tests/Game/CarriedGoldStandIns.cs
56	4	Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs
3	0	Server/tests/Dfo.Server.Protocol.Tests/Game/ExperienceFrameStandIns.cs
1	2	Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs
```

**与开工逐行比对**：

```
$ diff baseline-git-status.txt final2-status.txt
（无输出）  -> status: IDENTICAL to opening baseline
$ diff baseline-git-numstat.txt final2-numstat.txt
（无输出）  -> numstat: IDENTICAL to opening baseline
```

**一处必须点破，免得被误读**：两份 numstat 完全相同，**并不等于本 Task 一行判据都没加**。
`SelectDungeonHandlerTests.cs` 在 git 眼里是 **untracked（`??`）**，`git diff --numstat`
按定义不列它。该文件的实际变化用文件指纹给出：

```
开工  bytes= 20579  sha256= 2df9f9fa0ad66acdee872003365c0eefb960db84df2a1471ff88c49e6573b948
收工  bytes= 24791  sha256= c7b0f7ce24d901f3932d57c8cbf11b240ea4d814d68b7ea12f76ec0abc05892b
$ diff <scratchpad>/task08/SelectDungeonHandlerTests.cs.orig <该文件> | grep -c "^>"  -> added lines: 93
$ diff <scratchpad>/task08/SelectDungeonHandlerTests.cs.orig <该文件> | grep -c "^<"  -> removed lines: 0
```

**removed = 0**，即既有用例的期望值与断言一字未改。

### 7. 收口判据与计数归因

```
$ DOTNET_CLI_UI_LANGUAGE=en PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
exit=0
Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52, Duration: 697 ms - Dfo.Server.Networking.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    60, Skipped:     0, Total:    60, Duration: 7 s - Dfo.Pvf.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   510, Skipped:     0, Total:   510, Duration: 1 s - Dfo.Server.Protocol.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   256, Skipped:     0, Total:   256, Duration: 13 s - Dfo.Server.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 8 s - Dfo.Server.Host.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   223, Skipped:     2, Total:   225, Duration: 17 s - Dfo.Server.Infrastructure.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   962, Skipped:     0, Total:   962, Duration: 14 s - Dfo.Server.Application.Tests.dll (net10.0)

TOTAL passed=2069 skipped=2 failed=0
```

**新计数 2069 通过 / 2 跳过 / 0 失败。归因**：2067 → 2069，`+2` 全部落在
`Dfo.Server.Protocol.Tests`（508 → 510），就是 §4.4 补的那两条：
`A_refused_selection_reports_nothing_to_the_fatigue_seam` 与
`Without_a_published_session_nothing_is_reported_to_the_fatigue_seam`。
跳过数仍是 2，仍是那两条「因为档案在才跳过」的，未变。其余六个项目的数与开工基线逐位相同。

本 Task 全程未观察到 AGENTS.md 列的两类已知偶发（`OutsideTransactionTests` 的
`ObjectDisposedException`、`Dfo.Server.Networking.Tests` 的并行偶发），无需隔离复跑。

### 8. 证据文件

全部原始输出留在 scratchpad `.../scratchpad/task08/`：
`baseline-full.txt`、`baseline-git-status.txt`、`baseline-git-numstat.txt`、
`dungeonentry-tests.txt`（DungeonEntry 判据全名单）、
`mut1-full.txt`（CS9113）、`mut1b-full.txt`、`mut2-full.txt`、`ck033-restored.txt`、
`mut3-before-newtests.txt`（缺口证据：全绿）、`newtests-clean.txt`、`mut3-after-newtests.txt`、
`en-mut1.txt`、`en-mut1b.txt`、`en-mut2.txt`、`en-mut3.txt`、
`en-mut1b-detailed.txt`、`en-mut2-detailed.txt`、`en-mut3-detailed.txt`、
`en-final-full.txt`、`final2-status.txt`、`final2-numstat.txt`、
`DungeonEntrySlice.cs.orig`、`DungeonEntrySlice.cs.mut3`、`SelectDungeonHandlerTests.cs.orig`。

（说明：最早三次全量跑是在中文 console 下捕获的，文件里的中文标签是编码 mojibake；
判据全限定名、Expected/Actual 与计数行都是 ASCII，未受影响。为免争议，三次变异又用
`DOTNET_CLI_UI_LANGUAGE=en` 各定向重放了一次，本交付引用的失败原文取自英文那批。）

## Executor Self-check 1

### 对照 Required checks

| 判据 | 要求 | 结果 | 证据位置 |
|---|---|---|---|
| CK-031 | 变异一（移除调用）→ 指名的判据失败 | **达成**。指名 `...SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam`，`Assert.Single() Failure: The collection was empty`。附带如实记录：原始形态的删除先撞 CS9113 编译错误（`TreatWarningsAsErrors`），故用最小可编译形式（补 `_ = fatigue;`）完成本判据 | §1 |
| CK-032 | 变异二（数改错）→ 指名的判据失败 | **达成**。同一条判据，`Expected: 1 / Actual: 2` | §2 |
| CK-033 | 还原后复跑 → 恢复通过 | **达成**。变异二还原后立即全量 2067/2/0，与开工基线逐位相同；末尾收口 2069/2/0（含新增两条） | §3、§7 |
| 增补项 | 变异三（被拒绝路径也报告）→ 有判据失败；若原本没有，补一条并证明它会红 | **达成，且原本确实没有**。变异三在补判据前全量 **2067/2/0 零红**；补两条后同一变异下两条均红（`Collection: [Tuple (4660, 1)]` / `[Tuple (0, 1)]`） | §4 |

### 对照 Task 契约 Acceptance conditions

| 条件 | 结果 |
|---|---|
| 变异一（移除调用）使**指名的**判据失败 | 达成，全限定名见 §1；非仅「判据红了」 |
| 变异二（把报告的数改错）使**指名的**判据失败 | 达成，全限定名见 §2 |
| 两次还原之后判据恢复通过，工作区无变异残留 | 达成。每次还原都以 sha256 校验 `MATCH_BASELINE= True`；收口 `cmp` 0 字节差异；`git status` / `numstat` 与开工逐行相同 |
| **只说「判据红了」不算**，必须指名是哪一条 | 达成。四条失败判据全部给出全限定名、Expected/Actual、原始输出与源码行号 |

### 对照 Required evidence

| 项 | 结果 |
|---|---|
| 变异一的改动内容、失败判据全限定名与失败输出 | §1，改动逐行列出（含 CS9113 分支） |
| 变异二的改动内容、失败判据全限定名与失败输出 | §2，改动是单行 diff |
| 还原后的复跑结果 | §3（2067/2/0）与 §7（2069/2/0） |
| 工作区无变异残留的证据 | §6，sha256 + md5 + `cmp` + `git status`/`numstat` 与开工 `diff` 全等 |

### 对照 Task-specific authorization 与硬约束

| 约束 | 结果 |
|---|---|
| 不建 Git 检查点、不提交、不 stash | 遵守。全程无 `git commit` / `git stash` / `git tag`；回滚一律用 scratchpad 文件副本覆盖 |
| 变异改动必须在本 Task 结束前完全还原 | 遵守，见 §6 逐字节证据 |
| Permitted files：临时改 `DungeonEntrySlice.cs`（须还原）+ `tests/.../Game/DungeonEntry/` 下补判据 | 遵守。源码文件已逐字节还原；唯一持久改动是 `SelectDungeonHandlerTests.cs` 追加 93 行、删 0 行，正落在允许目录内 |
| 既有测试的期望值与断言不得为迁就而改写 | 遵守。`removed lines: 0`；`Without_a_published_session_nothing_is_entered` 原样保留，新判据另起一条 |
| TreatWarningsAsErrors 开启 | 遵守，且它本身在变异一里成为一条被记录的发现 |
| 不改 `sdd-execution.yaml`、不写 verdict、不预建空段落 | 遵守。本次只向本 Task 文件**追加** `## Execution Result 1` 与 `## Executor Self-check 1` 两段，未改写既有两段 |
| 不动 TASK-01..07 的成果 | 遵守。除本 Task 文件与上述测试文件外无其他改动，`git status`/`numstat` 与开工全等可证 |
| 只执行 TASK-08 | 遵守。只读了 Task 契约、plan 的 T-008 与 BD-014、AGENTS.md 三份 |

### 自评的诚实边界

- 三次变异只覆盖**疲劳报告这一条线上的 4 条判据**；同 fixture 另 5 条、`UnaccountedFatigueTests`
  2 条、`DungeonEntryCodecTests` 26 条与全仓其余 2033 条**均未被触及**，已在 §5 逐项列出，
  本 Task 对它们不作任何非空转声称。
- 「接缝空转」这一半（`UnaccountedCharacterFatigueDomainService` 不记账）仍只由它自己的两条判据
  担保，未经变异验证——因为任何调用点变异都够不到它。
- `ICharacterFatigueDomainService.RefundAsync` 至今**无任何调用方**，因此无调用点可变异、
  也无调用点判据可言；这不在本 Task 题目内，如实记下。
- 变异一的原始形态未能产出「指名的判据失败」而是产出编译错误，已如实记录，未把 CS9113 冒充为判据。
- §5 第 9 条的 2033 是算出来的（2069 − 36），不是逐条点过的；DungeonEntry 那 36 条是逐条列过的。

## Independent Quick Review 1

- Verdict: **passed**
- 评审的契约修订：Contract revision 1 + Contract Amendment 1（控制器追加，承接 TASK-07 评审观察 O-2）
- Owned diff identity：唯一持久改动
  `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs`
  （未跟踪文件，+93 行 / −0 行，20579 → 24791 字节，
  sha256 `2df9f9fa…73b948` → `c7b0f7ce…05892b`）。
  `DungeonEntrySlice.cs` 无净改动。
- 评审者上下文：全新，未参与实现。所有结论均由本评审自行重做变异、自行跑判据得出，
  不采信执行者自陈。本评审的临时改动（三次变异 + 一次测试文件回退）已全部还原，见第 5 节。

### 1. 变异一：区分「构建失败」与「判据变红」——执行者的处理诚实且正确

**原始形态**（删掉 `SelectDungeonHandler.HandleAsync` 里那 8 行报告块）。本评审用 Python 按字节
删除，块长与执行者所记一致：

```
$ python -c "... print('block bytes=',len(block),'occurrences=',b.count(block)) ..."
block bytes= 457 occurrences= 1
after bytes= 34141
```

跑全量：

```
$ DOTNET_CLI_UI_LANGUAGE=en PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
    dotnet test Server/86JP.Server.slnx --nologo
exit=1
E:\...\Server\src\Dfo.Server.Protocol\Game\DungeonEntry\DungeonEntrySlice.cs(426,36): error CS9113:
Parameter 'fatigue' is unread. [E:\...\Dfo.Server.Protocol.csproj]

（该次跑到的项目，未裁剪）
Passed!  - Failed: 0, Passed:  60, Skipped: 0, Total:  60 - Dfo.Pvf.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 223, Skipped: 2, Total: 225 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 256, Skipped: 0, Total: 256 - Dfo.Server.Persistence.Tests.dll (net10.0)
```

**这个区分成立，且本评审补一条更硬的证据**：

```
$ grep -c "Dfo.Server.Protocol.Tests.dll" mut1-raw.txt                              -> 0
$ grep -c "Entry_reports_what_it_costs_to_the_fatigue_seam" mut1-raw.txt            -> 0
$ grep -E "error " mut1-raw.txt | sort -u   -> 只有上面那一条 CS9113
```

`Dfo.Server.Protocol.Tests.dll` **一次都没被执行**，指名的那条判据**连名字都没出现在输出里**。
「构建挂了」确实不等于「判据变红」——执行者拒绝把 CS9113 冒充成判据失败，这个判断经本评审
独立复现，正确。

**变异一 b**（同样删 8 行 + 补一行 `_ = fatigue;`，插在 `var (layout, visit) = entered;` 之后）：

```
exit=1
Passed!  - Failed: 0, Passed:  52, Skipped: 0, Total:  52 - Dfo.Server.Networking.Tests.dll (net10.0)
Failed!  - Failed: 1, Passed: 509, Skipped: 0, Total: 510 - Dfo.Server.Protocol.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  60, Skipped: 0, Total:  60 - Dfo.Pvf.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 256, Skipped: 0, Total: 256 - Dfo.Server.Persistence.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:   6, Skipped: 0, Total:   6 - Dfo.Server.Host.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 223, Skipped: 2, Total: 225 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 962, Skipped: 0, Total: 962 - Dfo.Server.Application.Tests.dll (net10.0)

[xUnit.net 00:00:00.64]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [FAIL]
  Failed Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [8 ms]
  Error Message:
   Assert.Single() Failure: The collection was empty
  Stack Trace:
     at ...SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam() in
     E:\...\SelectDungeonHandlerTests.cs:line 140
```

`grep -c "FAIL]"` = **1**：全仓只有这一条红，与执行者所记逐字相符（同一全限定名、同一断言、同一行号 140）。

### 2. 变异二：独立复现，结论一致

改 `SelectDungeonHandler` 的 `FatigueAnEntryCosts = 1` → `2`（`occurrences= 1`，单行替换）：

```
exit=1
Passed!  - Failed: 0, Passed:  52, Skipped: 0, Total:  52 - Dfo.Server.Networking.Tests.dll (net10.0)
Failed!  - Failed: 1, Passed: 509, Skipped: 0, Total: 510 - Dfo.Server.Protocol.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  60, Skipped: 0, Total:  60 - Dfo.Pvf.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 256, Skipped: 0, Total: 256 - Dfo.Server.Persistence.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:   6, Skipped: 0, Total:   6 - Dfo.Server.Host.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 223, Skipped: 2, Total: 225 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 962, Skipped: 0, Total: 962 - Dfo.Server.Application.Tests.dll (net10.0)

[xUnit.net 00:00:00.53]     ...SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [FAIL]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 1
Actual:   2
  Stack Trace:
     at ...SelectDungeonHandlerTests.cs:line 142
```

`grep -c "FAIL]"` = **1**。全限定名、Expected/Actual、行号 142 全部与执行者所记一致。

### 3. 变异三与那个缺口：缺口属实，新判据确实会红，且不是既有判据的重复

**先证缺口原本存在。** 本评审把 `SelectDungeonHandlerTests.cs` 换成 TASK-08 开工前的形态
（下方第 5 节说明该形态如何被独立佐证），在其上打变异三——两条被拒绝的出路各加一次
`fatigue.SpendAsync`（`guard anchor occurrences= 1`、`refusal anchor occurrences= 1`）：

```
$ DOTNET_CLI_UI_LANGUAGE=en PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo
exit=0
Passed!  - Failed: 0, Passed:  52, Skipped: 0, Total:  52 - Dfo.Server.Networking.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  60, Skipped: 0, Total:  60 - Dfo.Pvf.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 508, Skipped: 0, Total: 508 - Dfo.Server.Protocol.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 256, Skipped: 0, Total: 256 - Dfo.Server.Persistence.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:   6, Skipped: 0, Total:   6 - Dfo.Server.Host.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 223, Skipped: 2, Total: 225 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 962, Skipped: 0, Total: 962 - Dfo.Server.Application.Tests.dll (net10.0)

$ grep -c "FAIL]"  -> 0
```

52+508+60+256+6+223+962 = **2067 / 2 / 0，零红**。**缺口属实**：TASK-08 之前，把疲劳报告
挪到两条被拒绝的路径上，全仓 2067 条判据一条都拦不住。执行者 §4.3 的发现经独立复现成立。

**再证新判据会红、旧判据不红。** 保留同一变异，把测试文件换回交付形态：

```
（每项目 Test Run 摘要，未裁剪）
Test Run Failed.     Total tests: 510   Passed: 508   Failed: 2      （Protocol.Tests）
Test Run Successful. Total tests:  60   Passed:  60                  （Pvf.Tests）
Test Run Successful. Total tests:  52   Passed:  52                  （Networking.Tests）
Test Run Successful. Total tests:   6   Passed:   6                  （Host.Tests）
Test Run Successful. Total tests: 256   Passed: 256                  （Persistence.Tests）
Test Run Successful. Total tests: 225   Passed: 223   Skipped: 2     （Infrastructure.Tests）
Test Run Successful. Total tests: 962   Passed: 962                  （Application.Tests）

[xUnit.net] ...SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam [FAIL]
              Assert.Empty() Failure: Collection was not empty
              Collection: [Tuple (4660, 1)]
[xUnit.net] ...SelectDungeonHandlerTests.Without_a_published_session_nothing_is_reported_to_the_fatigue_seam [FAIL]
              Assert.Empty() Failure: Collection was not empty
              Collection: [Tuple (0, 1)]
```

DungeonEntry 命名空间逐条（`--logger "console;verbosity=detailed"`，两个 fixture 全 10 条，未裁剪）：

```
  Failed SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam [7 ms]
  Failed SelectDungeonHandlerTests.Without_a_published_session_nothing_is_reported_to_the_fatigue_seam [< 1 ms]
  Passed SelectDungeonHandlerTests.An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy [< 1 ms]
  Passed SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_carried [4 ms]
  Passed SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_cleared [< 1 ms]
  Passed SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [1 ms]
  Passed SelectDungeonHandlerTests.The_answer_to_an_entry_is_what_it_was [1 ms]
  Passed SelectDungeonHandlerTests.Without_a_published_session_nothing_is_entered [6 ms]
  Passed UnaccountedFatigueTests.A_reported_spend_moves_no_number_the_seam_answers [< 1 ms]
  Passed UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries [7 ms]

$ grep -cE "^\s+Passed\s+...DungeonEntry\.DungeonEntryCodecTests\."   -> 26
$ grep -cE "^\s+Failed\s+...DungeonEntry\.DungeonEntryCodecTests\."   -> 0
```

三条论断经独立复现全部成立：

1. 两条新判据在变异三下**都红**，`Tuple (4660, 1)`（4660 = 0x1234 = `Identity.CharacterId`，
   拒绝路径）与 `Tuple (0, 1)`（`default(CharacterId)`，无会话路径）各指一条路径，互不混淆；
2. `Entry_reports_what_it_costs_to_the_fatigue_seam` 在同一变异下 **Passed**——变异一/二覆盖的
   那条判据管不到被拒绝的路径，**新判据不是它的重复**；
3. `Without_a_published_session_nothing_is_entered` 在**它自己那条路径被多打一次报告的情况下
   仍 Passed**——它对疲劳接缝确实什么也没说，TASK-07 评审 O-2 的论断成立。

另核：`A_refused_selection_...` 的失败落在方法末尾那句 `Assert.Empty(fatigue.Spent)`，说明它
前面两句拒绝帧断言先通过了——该判据确实走到了拒绝分支，不是「更早停下」的空转绿。

### 4. 覆盖缺口的列举：如实，且算术自洽

本评审对交付态全量跑做逐条点名，核对 §5 的清单：

```
$ grep -oE "Dfo\.Server\.Protocol\.Tests\.Game\.DungeonEntry\.[A-Za-z]+Tests" final-test.txt | sort | uniq -c
     26 ...DungeonEntry.DungeonEntryCodecTests
      8 ...DungeonEntry.SelectDungeonHandlerTests
      2 ...DungeonEntry.UnaccountedFatigueTests
```

DungeonEntry 命名空间共 **36** 条，与 §5 一致。清单核对：

- 被证明非空转的**互异判据是 3 条**：`Entry_reports_what_it_costs_to_the_fatigue_seam`、
  `A_refused_selection_reports_nothing_to_the_fatigue_seam`、
  `Without_a_published_session_nothing_is_reported_to_the_fatigue_seam`；
- §5 列为「未触及」的：`UnaccountedFatigueTests` 2 条 + 同 fixture 另 5 条 + `DungeonEntryCodecTests`
  26 条 + 其余 2033 条 = **2066**；
- **2066 + 3 = 2069**，与收口总数逐位吻合。

`SelectDungeonHandlerTests` 那 8 条逐条对号：5 条在「未触及」清单里、3 条在「已证明」表里，
**不重不漏**。执行者没有把任何一条只是「跟着绿」的判据说成被变异覆盖，也没有漏掉它其实
声称覆盖了的东西。TASK-05 那类「声称覆盖 13 条实际只覆盖 6 条」的问题，此处不存在。

三条「明确不声称」也逐条核实属实：

```
$ grep -rn "RefundAsync" Server/src Server/tests --include="*.cs"
Server/src/Dfo.Server.Abstractions/Characters/ICharacterFatigueDomainService.cs:74              （声明）
Server/src/Dfo.Server.Application/Characters/UnaccountedCharacterFatigueDomainService.cs:11,56  （实现与注释）
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:413       （替身桩，抛异常）
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/UnaccountedFatigueTests.cs:56          （直接测实现）
```

—— **生产侧零调用方**，执行者「`RefundAsync` 没有调用方」属实。

```
$ grep -rn "188" Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
147:                new CharacterFatigueState(188, 0),
```

—— 188/0 确实是 `CharacterRosterService` 的硬编码，与本次报告无关，属实。

```
$ grep -rn "ConsumesFatigue" Server/src Server/tests --include="*.cs"
Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs:85                   （字段声明）
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:434                   （注释提及）
Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfDungeonDefinitionSourceTests.cs:32  （钉解析的那条）
```

—— 唯一读者就是钉解析的那条测试，无消费方判据，属实。

### 5. 还原与证据局限

**被变异文件逐字节还原**（Python 读 bytes + `cmp`，未用对 CR 不可靠的 MSYS 工具）：

```
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
  bytes= 34598 match= True
  sha256= 50e3a5bc4fc4775fa72796fd08439745e8f35858d33b6696c29f152215399b7d match= True
Server/tests/.../SelectDungeonHandlerTests.cs
  bytes= 24791 match= True
  sha256= c7b0f7ce24d901f3932d57c8cbf11b240ea4d814d68b7ea12f76ec0abc05892b match= True

$ cmp <工作区> <本评审开工副本>
slice IDENTICAL (0 differing bytes)
tests IDENTICAL (0 differing bytes)
```

执行者所记 34598 / `50e3a5bc…399b7d` 与本评审开工独立测得的完全一致。

**无变异残留**：

```
$ grep -rn "_ = fatigue\|MUTATION\|FatigueAnEntryCosts = 2" Server/src Server/tests --include="*.cs"
grep exit=1 (1 = no matches)
```

**证据局限那处披露：成立且恰当。** `SelectDungeonHandlerTests.cs` 确为未跟踪（`??`），
`git diff --numstat` 按定义不列它，所以「numstat 与开工相同」确实不能推出「没加判据」——
执行者主动点破这一点，避免了一处会误导读者的证据。替代证据（文件大小/哈希）本评审判为**成立**，
并另加三条独立佐证，使它不再只依赖执行者自己的副本：

1. **行级纯追加**（本评审自算）：

   ```
   pre  bytes= 20579  lines= 484
   post bytes= 24791  lines= 577
   delta bytes= 4212  delta lines= 93
   every pre-task line still present in order: True (485/485)
   $ diff <pre> <post>: added(>)= 93   removed(<)= 0
   ```

   开工形态的**每一行都原封不动地按原顺序留在交付形态里**，93 行全部是新增，
   内容就是两个新测试方法 + 一个新替身 `RefusingVisits`。**既有用例的期望值与断言一字未改**
   由此得证，不依赖任何自陈。
2. **计数三方吻合**：TASK-07 记录 Protocol.Tests = 508；本评审把开工形态放回工作区实测 = **508**；
   交付形态实测 = **510**。508 + 2 = 510。
3. **方法数**：开工形态 `[Fact]` 计数 6，交付形态 8。

**工作区状态**（本评审收工，与本评审开工逐行比对）：

```
$ git log -1 --format="%H"   -> 427331bcb07f60121c97920287ac341c9cb14dee
$ git stash list             -> （空）
$ diff <开工 git status --short> <收工>    -> 无输出，status: IDENTICAL
$ diff <开工 git diff --numstat> <收工>    -> 无输出，numstat: IDENTICAL
```

未建检查点、未提交、未 stash；`sdd-execution.yaml` 里 TASK-08 仍是 `state: in_progress`，
执行者未擅自改动任务控制状态；Task 文件在本评审追加之前只有 4 个二级段落
（Contract / Amendment 1 / Execution Result 1 / Executor Self-check 1），未预建空评审段。

### 6. 本评审自己的判据

```
$ DOTNET_CLI_UI_LANGUAGE=en dotnet build Server/86JP.Server.slnx --nologo
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ DOTNET_CLI_UI_LANGUAGE=en PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
    dotnet test Server/86JP.Server.slnx --nologo
exit=0
Test Run Successful. Total tests:  52   Passed:  52                 （Networking.Tests）
Test Run Successful. Total tests: 510   Passed: 510                 （Protocol.Tests）
Test Run Successful. Total tests:  60   Passed:  60                 （Pvf.Tests）
Test Run Successful. Total tests:   6   Passed:   6                 （Host.Tests）
Test Run Successful. Total tests: 256   Passed: 256                 （Persistence.Tests）
Test Run Successful. Total tests: 225   Passed: 223   Skipped: 2    （Infrastructure.Tests）
Test Run Successful. Total tests: 962   Passed: 962                 （Application.Tests）
$ grep -c "FAIL]"  -> 0
```

52+510+60+6+256+223+962 = **2069 通过 / 2 跳过 / 0 失败**，逐项目归因与执行者所报一致：
2067 → 2069 的 `+2` 全部落在 `Dfo.Server.Protocol.Tests`（508 → 510），就是新补的那两条。
两条跳过经点名核实仍是那两条档案相关的：

```
Skipped Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message
Skipped Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog
```

`CommandHandlerArchitectureTests` 12 条全部 Passed（含
`ProductionCommandHandlers_OnlyTakeDomainShapes`、`TheSweepFindsTheCommandHandlers`）。

本评审六次全量跑（开工基线、变异一原始、一 b、二、三前、三后、收口）全程**未观察到** AGENTS.md
记的两类偶发假红，也未观察到 `OutsideTransactionTests` 的 `ObjectDisposedException`，无需隔离复跑。

不新增协议帧、不改帧字节布局：本 Task 的持久改动只有一个测试文件，新增代码只引用既有的
`SelectDungeonRefusal` / `SelectDungeonRefusal.DungeonNotFound`（生产侧 `DungeonEntrySlice.cs`
逐字节未变），无 codec / Encoder / Decoder / `Messages.cs` / `ProtocolModule` 改动；
`DungeonEntryCodecTests` 26 条含字节金标全绿。

### 7. findings

**阻断缺陷：无。** 五件核查项逐条独立复现，结论与执行者自陈一致；未发现越界改动、未发现
把「跟着绿」说成「被变异覆盖」的夸大、未发现残留或未还原。记载纪律（命令 + 完整原始输出 +
不从样本推全体）经抽查照做：§5 的 2033 条明标是算出来的而非逐条点过，§1 明标 CS9113 是护栏
不是判据，§7 明标 numstat 相同不等于没加判据——三处都是主动降低自身证据强度的披露。

**非阻断观察**（不要求整改）：

- **O-1（措辞）**：Executor Self-check 的「诚实边界」写「三次变异只覆盖疲劳报告这一条线上的
  **4 条判据**」，Acceptance conditions 表写「**四条失败判据**」。实际互异的测试方法是 **3 条**
  ——`Entry_reports_what_it_costs_to_the_fatigue_seam` 在变异一 b 与变异二下各红一次，被计成两行。
  §5 的表格本身标注了「同上一条」，未触及清单的算术（2066 + 3 = 2069）也按 3 条自洽，
  所以没有任何判据被多算或漏算，只是「4」这个数指的是「(变异, 判据) 对」而非互异判据数。
  措辞可更精确，与结论无关。
- **O-2（证据链条）**：开工指纹 20579 / `2df9f9fa…` 的原始出处是执行者自己的 scratchpad 副本，
  git 无从佐证（文件未跟踪）。本评审已用三条互相独立的方式佐证该副本为真（第 5 节），
  结论可靠；但后续读者应知这一环是**被佐证**而非**被 git 背书**的。
- **O-3（覆盖形状）**：无会话那道门把 `session is null`、`TryGet` 返回 false、`result is null`
  三种情形并成一个 `return`。新判据 `Without_a_published_session_nothing_is_reported_to_the_fatigue_seam`
  用 `NoSession` 走的是第一种，钉住的是「这个 return 之前不报告」。三者共用一个报告位置，
  一条判据足够；但若日后把这道门拆开，另两种情形将不再有判据护着。不属本 Task 契约范围，
  记此备考。

本 verdict 仅表示这一个 Task Unit 的有限缺陷检查通过，不构成完整变更评审，
也不替代接受前的新鲜验证。
