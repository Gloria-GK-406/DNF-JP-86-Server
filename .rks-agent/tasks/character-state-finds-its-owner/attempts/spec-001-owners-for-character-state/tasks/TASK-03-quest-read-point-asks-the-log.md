# TASK-03 — 入场之后的那一处任务读点改问任务簿

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 进本切片里那一处入场之后的任务状态读取，改为向已有的任务簿实例提问；不为任务新增任何类型。
- Referenced spec requirements: RQ-008
- Referenced plan sections: T-003；BD-006
- Owned boundary: 进本切片里那一处读取及其判据。
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs`
  - 相应的判据更新
- Dependencies: TASK-02
- Required dependency outputs: 无
- Acceptance conditions:
  - 那一处向任务簿实例提问，取数来源不再是入场投影
  - 该处原有的取不到时的语义不变
  - 本任务新增的任务相关类型清单为空
  - 全量判据全绿
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: test-driven-development
- Required checks: CK-011, CK-012, CK-013
- Required evidence:
  - 该读取点改动前后的对照
  - 本任务新增类型清单（应为空）
  - 全量判据输出

## Execution Result 1

### 改了什么

生产代码两个文件，判据一个新文件。

| 文件 | 改动 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` | `SelectDungeonHandler` 增注 `ICharacterQuestLogDomainService questLogs`；入场之后那一处任务读取改为向任务簿实例提问；该处注释同步改写 |
| `Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs` | 只改一段 `<remarks>` 文字（见下「注释漂移」一节说明为何必须改） |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs` | 新增，5 条用例。这条路径此前**零判据**，见「变异验证」 |

不新增协议帧，不改任何帧的字节布局：下面 `git diff` 全文可见，没有任何 codec / encoder /
message record 被触及。

### `:440` 处原本用 `quests` 做什么

改动前（`git show HEAD:...DungeonEntrySlice.cs` 的 437-444 行）：

```csharp
        // Gathered here and handed over, so the rule stays a function of what it is given. Cleared
        // quests come from the entry batch the session already carries rather than a fresh read,
        // which keeps the facts consistent with the state the client was told about.
        var quests = result.CharacterEntry.Quests;
        var facts = new DungeonEntryFacts(
            message.Difficulty,
            quests.ActiveQuests.Select(value => value.QuestId).ToArray(),
            quests.ClearedQuestIds);
```

`quests` 是 `CharacterQuestEntryState`，四个部分里**只用了两个**：

- `ActiveQuests` —— 只取每项的 `QuestId`，丢掉 `Slot` 与 `TriggerValue`；
- `ClearedQuestIds` —— 整份直接交出去。

`NotificationQuestIds` 与 `VisibilityFlags` 这一处一次都没读。取到的两串数只用于构造
`DungeonEntryFacts`，别无他用。

### 换成问哪个成员

改动后（现 439-451 行）：

```csharp
        var questLog = questLogs.LogOf(session.AccountId, session.CharacterId);
        var carried = await questLog.CarriedAsync(cancellationToken).ConfigureAwait(false);
        var cleared = await questLog.ClearedAsync(cancellationToken).ConfigureAwait(false);
        var facts = new DungeonEntryFacts(
            message.Difficulty,
            carried.Select(quest => quest.QuestId).ToArray(),
            cleared.Select(quest => quest.QuestId).ToArray());
```

`ActiveQuests` → `IQuestLog.CarriedAsync`；`ClearedQuestIds` → `IQuestLog.ClearedAsync`。
账号与角色两半地址取自本就在手的 `session`（`SelectedSessionIdentity` 同时带
`AccountId` 与 `CharacterId`，见 `Server/src/Dfo.Server.Data/World/SelectedSessionModels.cs:8-16`）。

### 为何逐值相同

**不是「看起来一样」，是两条语句打在同两张表、同一个谓词上，投影也同序。**下面四段引文都是从
文件里原样抄出的。

**其一，在做的任务（`character_active_quests`）**

入场投影这一路：

```
Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs:151-152
    "SELECT slot, quest_id, trigger_value FROM character_active_quests "
    + "WHERE character_id=@cid ORDER BY slot"
```
```
Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs:64-67
        var activeQuests = quests.ActiveQuests.OrderBy(value => value.Slot)
            .Select(value => new CharacterQuestProgressState(
                value.Slot, value.QuestId, value.TriggerValue))
            .ToArray();
```

任务簿这一路：

```
Server/src/Dfo.Server.Persistence/Quests/SqliteQuestDomainStore.cs:134-135
    "SELECT slot, quest_id, trigger_value FROM character_active_quests WHERE character_id=@character;"
```
```
Server/src/Dfo.Server.Application/Quests/QuestLog.cs:41-43
            return (IReadOnlyList<CarriedQuest>)[.. rows
                .OrderBy(row => row.Slot)
                .Select(row => new CarriedQuest(row.Slot, row.QuestId, row.TriggerValue))];
```

同表、同三列、同 `character_id=?` 谓词，均按 `slot` 升序（一边在 SQL 里排一边在内存里排，
结果相同；`character_active_quests` 的主键是 `(character_id,slot)`，见
`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:177`，故 `slot` 无并列，
排序无歧义）。两边再各取 `QuestId`，得到同一串数。

**其二，已通关的任务（`character_invisible_flags`）**

入场投影这一路：

```
Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs:181-182
    "SELECT slot_index, flag_value FROM character_invisible_flags "
    + "WHERE character_id=@cid ORDER BY slot_index"
```
```
Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs:82-83
                quests.ClearFlags.Where(value => value.Value != 0)
                    .Select(value => value.SlotIndex).Order().ToArray(),
```

任务簿这一路：

```
Server/src/Dfo.Server.Persistence/Quests/SqliteQuestDomainStore.cs:238-239
    "SELECT slot_index, flag_value FROM character_invisible_flags WHERE character_id=@character;"
```
```
Server/src/Dfo.Server.Application/Quests/QuestLog.cs:61-64
            return (IReadOnlyList<ClearedQuest>)[.. rows
                .Where(row => row.Mark != 0)
                .OrderBy(row => row.QuestId)
                .Select(row => new ClearedQuest(row.QuestId, row.Mark))];
```

同表、同两列、同谓词；两边都**只留 flag 非零的行**（这一条 `SqliteQuestDomainStore.cs:196-200`
的 `<remarks>` 与 `QuestLog.cs:58-60` 的注释都写明是为了跟入场投影一致），都按
「quest 号」升序。`StoredClearedQuest.QuestId` 读的就是 `slot_index` 列
（`SqliteQuestDomainStore.cs:253`：`new StoredClearedQuest(reader.GetInt32(0), ...)`，
第 0 列即 `slot_index`），与入场投影取 `SlotIndex` 是同一个值。故两边 `QuestId` 串逐值相同。

**两处必须如实说出的差别（都不影响「同一批行 → 同一个答案」）：**

1. **读的时刻不同**，这正是本任务要的：入场投影是角色进世界那一刻的抄本，之后无人刷新；
   任务簿每次现读（`QuestLog` 的 `<remarks>` 原话：`Nothing is held between calls`）。
   同一批行下答案相同，行变了则任务簿对而抄本错。
2. **越界行的处置不同**。入场装配 `CharacterEntryAssembly.cs:274-305` 对
   `quest_id <= 0`、`trigger_value < 0`、`slot` 越 `short`、flag 槽位越 30000、flag 值越
   `byte` 一律抛 `InvalidDataException`；任务簿只校验 mark 的 byte 范围
   （`SqliteQuestDomainStore.cs:246-251`）与 slot 的 `checked((short))`。
   这不构成本处的行为差：这些行会让该角色**根本进不了世界**，而本读点位于已发布 session
   之后，那批行早已通过入场校验。
3. **新增了一条失败通路**：原先这一行读的是内存里的 record，不可能失败；现在是一次 SQLite
   读，I/O 出错会从 handler 抛出。这是「换取数来源」不可回避的代价，抛出点在任何一帧发出
   之前，与原先「未发布 session 就直接 return」的早退位置相邻，不会发出半截帧。

**另一件必须如实说的**：`DungeonEntryFacts` 今天**根本没被规则消费**。
`DungeonLayoutRuleService.ResolveAsync` 只做 `ArgumentNullException.ThrowIfNull(facts)`，
`SelectMaze(definition, seed)` 完全不看 facts —— 它自己的 `<remarks>`
（`Server/src/Dfo.Server.Application/World/DungeonLayoutRuleService.cs:61-67`）写明
「参考服务端按任务与难度筛 maze 的规则尚未逆向出来，facts 是刻意不用的」。
枚举证据：

```
$ grep -rn "facts" --include=*.cs Server/src | grep -v "///"
Server/src/Dfo.Server.Abstractions/World/IDungeonLayoutRuleService.cs:117:        DungeonEntryFacts facts,
Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs:70:        DungeonEntryFacts facts,
Server/src/Dfo.Server.Application/World/DungeonLayoutRuleService.cs:25:        DungeonEntryFacts facts,
Server/src/Dfo.Server.Application/World/DungeonLayoutRuleService.cs:29:        ArgumentNullException.ThrowIfNull(facts);
Server/src/Dfo.Server.Application/World/DungeonVisitDomainService.cs:30:        DungeonEntryFacts facts,
Server/src/Dfo.Server.Application/World/DungeonVisitDomainService.cs:33:        ArgumentNullException.ThrowIfNull(facts);
Server/src/Dfo.Server.Application/World/DungeonVisitDomainService.cs:38:            .ResolveAsync(dungeonId, facts, Random.Shared.Next(), cancellationToken)
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:448:        var facts = new DungeonEntryFacts(
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:454:            session.CharacterId, message.DungeonId, message.Difficulty, facts, cancellationToken)
```
（另有 `SqliteItemDomainStore.cs` 里同名的局部变量，与本主题无关。）

因此**本改动在今天的线上字节层面不可观测**；它把取数点摆正，等 maze 规则逆向出来时不必再改。
这也是新判据断在「handler 交给 `EnterAsync` 的 facts」这个接缝上、而不是断在帧字节上的原因。

### 该处「取不到时」的语义不变

这一处原本没有「任务取不到」的分支——`result.CharacterEntry.Quests` 恒非空，空角色得到两个空
列表。改后同样：`LogOf` 恒返回实例（接口 `<remarks>` 原话：`A character always has one`），
无行的角色得到两个空列表，而不是回落到 session 抄本。用例
`An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy` 就守这一条。

真正的「取不到」是**这一行之前**的守卫（现 427-437 行）：`FindByConnection` 为 null、
`TryGet` 为 false、或 `result` 为 null 时打 warning 并 return。这段**一个字符没动**
（见下面 `git diff`：该 hunk 起点在 `return;` 与空行之后），用例
`Without_a_published_session_nothing_is_entered` 守它，并且用一个「被问就抛」的假任务簿
证明守卫仍在读任务簿**之前**。

### 执行顺序

方法本来就是 `async ValueTask HandleMessageAsync`，第一个 `await` 原本在
`visits.EnterAsync`。改后新增两个 `await` 落在守卫之后、`EnterAsync` 之前，中间没有任何副作用
语句被跨越；`EnterAsync` 之后的一切原样。相对次序：守卫 → 取任务 → 进本 → 发帧，与改前一致
（改前是「守卫 → 读抄本 → 进本 → 发帧」）。

### 分层与切片

`Protocol` → `Abstractions` 是允许方向；`ICharacterQuestLogDomainService` 就在
`Dfo.Server.Abstractions/Quests/IQuestLog.cs:293`。AGENTS.md「handler 业务语义规则」允许的第 2
类是「只读查询与端点解析」，本处只调 `CarriedAsync` / `ClearedAsync` 两个只读成员，未碰
`AcceptAsync` / `CompleteAsync` 等可改行为。实例由领域服务交出（`logs.LogOf(...)`），正是
AGENTS.md「实例与快照不在那张表上，因为它们不是被注入的」所描述的形状。
`SelectDungeonHandler` 不是 `ICommandHandler`，`CommandHandlerArchitectureTests` 的判定谓词与
豁免清单未被触碰（Application.Tests 943 条全绿，含该判据）。

### 注释漂移：为什么动了第二个文件

`IDungeonVisitDomainService.EnterAsync` 的 `<remarks>` 原文是：

> The facts are handed in rather than found: which quests a character carries comes from the
> entry batch the session was published with, so that the layout is chosen against the state
> the client was told about rather than a fresher read of it.

改完之后这句的后半**变成假话**（现在恰恰就是 fresher read）。它不是远处的闲话，它描述的正是本
读点的取数来源。因此改写为只保留仍然为真的那一半（facts 由调用方收集、服务本身是入参的函数）。
这是超出 Task 契约 `Permitted files` 列表的一个文件，**据实声明**：改动仅限一段
`<remarks>` 文字，无签名、无行为、无类型变化，`git diff` 显示 3 增 3 删且全部是 `///` 行。
留着不改会复刻 TASK-01 里「改动使邻近注释变成假话」那一条。

反向也查过：`DungeonVisitDomainService.EnterAsync` 里「The layout was chosen from quest state as
it stood at this moment」与 `DungeonLayout` 的「quest state as it stood at entry」两处说的是
**进本那一刻**，改后只会更真，无需改。全仓 `grep -rn "entry batch"` 命中的其余各处都不描述本
读点。

### 变异验证

把 `:445-451` 退回从入场投影取（`_ = questLogs; var quests = result.CharacterEntry.Quests; ...`），
先跑本组，再跑全量：

```
$ dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --nologo --filter "FullyQualifiedName~SelectDungeonHandlerTests"
  失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_carried [11 ms]
  错误消息:
   Assert.Equal() Failure: Collections differ
                       ↓ (pos 0)
Expected: <generated> [910, 911]
Actual:   int[]       [55]
                       ↑ (pos 0)
  失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_cleared [< 1 ms]
  错误消息:
   Assert.Equal() Failure: Collections differ
                       ↓ (pos 0)
Expected: <generated> [920]
Actual:   int[]       []
  失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy [< 1 ms]
  错误消息:
   Assert.Empty() Failure: Collection was not empty
Collection: [55]

失败!  - 失败:     3，通过:     2，已跳过:     0，总计:     5，持续时间: 39 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
```

会红的三条判据，全限定名与 Expected/Actual 如上。

**这条路径原本零判据 —— 已实证，并已补上。** 变异态下的全量：

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
失败!  - 失败:     3，通过:   502，已跳过:     0，总计:   505 - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52 - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   256，已跳过:     0，总计:   256 - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6 - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   223，已跳过:     2，总计:   225 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   943，已跳过:     0，总计:   943 - Dfo.Server.Application.Tests.dll (net10.0)
```

**红的三条全是本任务新写的**，此前存在的 2040 条一条没红。改动前的枚举也指向同一结论：

```
$ grep -rn "SelectDungeonHandler\|DungeonEntryFacts" Server/tests --include=*.cs | grep -v "/bin/\|/obj/"
Server/tests/Dfo.Server.Application.Tests/World/DungeonLayoutRuleServiceTests.cs:41:        var layout = await service.ResolveAsync(10000, DungeonEntryFacts.None, seed: 12345, default);
Server/tests/Dfo.Server.Application.Tests/World/DungeonLayoutRuleServiceTests.cs:62:        var layout = await service.ResolveAsync(10000, DungeonEntryFacts.None, seed: -777, default);
Server/tests/Dfo.Server.Application.Tests/World/DungeonLayoutRuleServiceTests.cs:72:        var first = await service.ResolveAsync(1, DungeonEntryFacts.None, seed: 99, default);
Server/tests/Dfo.Server.Application.Tests/World/DungeonLayoutRuleServiceTests.cs:73:        var second = await service.ResolveAsync(1, DungeonEntryFacts.None, seed: 99, default);
Server/tests/Dfo.Server.Application.Tests/World/DungeonLayoutRuleServiceTests.cs:88:            cells.Add((await service.ResolveAsync(1, DungeonEntryFacts.None, seed, default))!.StartCell);
Server/tests/Dfo.Server.Application.Tests/World/DungeonLayoutRuleServiceTests.cs:98:        Assert.Null(await service.ResolveAsync(999_999, DungeonEntryFacts.None, seed: 1, default));
Server/tests/Dfo.Server.Application.Tests/World/DungeonLayoutRuleServiceTests.cs:116:            await service.ResolveAsync(1, DungeonEntryFacts.None, seed: 1, default));
Server/tests/Dfo.Server.Application.Tests/World/DungeonLayoutRuleServiceTests.cs:128:            var layout = await service.ResolveAsync(10000, DungeonEntryFacts.None, seed, default);
```
`SelectDungeonHandler` 一次都没被构造过，且上面 8 处一律传 `DungeonEntryFacts.None`。
这与上一轮 multi-spec 的实证同型：一个协议处理器整段回退到读陈值，全量判据仍全绿。

**还原**：把改动前另存的原文件拷回，随即核对：

```
$ grep -n "questLog\|questLogs\|CharacterEntry.Quests" Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
418:    ICharacterQuestLogDomainService questLogs,
445:        var questLog = questLogs.LogOf(session.AccountId, session.CharacterId);
446:        var carried = await questLog.CarriedAsync(cancellationToken).ConfigureAwait(false);
447:        var cleared = await questLog.ClearedAsync(cancellationToken).ConfigureAwait(false);
```
`CharacterEntry.Quests` 零命中，变异痕迹（`_ = questLogs;`）零残留；临时副本已删除，下面的
`git status --short` 里也不存在任何临时文件。还原后重跑 CK-011/CK-013 全绿（见下）。

### 判据输出

**CK-011**

```
$ dotnet build Server/86JP.Server.slnx --nologo
已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:02.56
```

**CK-012 —— 本任务新增的任务相关类型清单：生产侧为空**

```
$ git diff -U0 -- Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs \
  | grep -E '^\+[^+]' | grep -vE '^\+\s*///' | grep -E '\b(class|record|struct|interface|enum|delegate)\s+[A-Z]'
### CK-012 (a) production: added type declarations in the two modified src files ###
matches: 0
```

生产代码新增类型数 **0**（任务相关与否都为 0）。新增的只有一个已有 handler 的构造器参数。

**不含糊地把测试替身也报出来**，由控制器判定是否计入。新判据文件里声明的类型（全部是
`private` 嵌套测试替身，实现的都是**已存在**的接口，不进入产品程序集的公开面）：

```
$ grep -nE '^\s*(private|public|internal|sealed| )*\b(class|record|struct|interface|enum)\b' Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs
35:public sealed class SelectDungeonHandlerTests
237:    private sealed class RecordingVisits : IDungeonVisitDomainService
266:    private sealed class OneLog(IQuestLog log) : ICharacterQuestLogDomainService
281:    private class StandInLog(
331:    private sealed class UnaskedLog() : StandInLog([], [])
342:    private sealed class OneSession(SelectedSessionIdentity identity) : ISelectedSessionDomainService
359:    private sealed class NoSession : ISelectedSessionDomainService
376:    private sealed class OneRuntimeSession(SelectCharacterResult entry)
402:    private sealed class RecordingSender : IGameMessageSender
```

其中与任务相关的三个是 `OneLog`、`StandInLog`、`UnaskedLog`。它们是判据的替身，与既有的
`Server/tests/Dfo.Server.Application.Tests/Support/RecordingQuestLogs.cs:22`（同样实现
`ICharacterQuestLogDomainService`）同类。**产品里没有为任务新增任何类型**，这是 CK-012 与
契约「不为任务新增任何类型」的实质；替身在此如实列出而不藏。

**CK-013**

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52，持续时间: 1 s - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   505，已跳过:     0，总计:   505，持续时间: 2 s - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60，持续时间: 8 s - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6，持续时间: 8 s - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   256，已跳过:     0，总计:   256，持续时间: 13 s - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   223，已跳过:     2，总计:   225，持续时间: 17 s - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   943，已跳过:     0，总计:   943，持续时间: 16 s - Dfo.Server.Application.Tests.dll (net10.0)
```

跳过的两条原样列出（均为无 PVF 归档时的 SkippableFact，本次仍跳过）：

```
  已跳过 Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message [1 ms]
  已跳过 Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog [1 ms]
```

### 计数与归因

52 + 505 + 60 + 6 + 256 + 223 + 943 = **2045 通过 / 2 跳过 / 0 失败**。

基线 2040 / 2 / 0 → 现 2045 / 2 / 0，**+5 通过**，全部来自新文件
`Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs` 的
5 条用例：

| 用例 | 守什么 |
|---|---|
| `Entry_asks_the_quest_log_which_quests_are_carried` | 在做的任务来自任务簿，不是 session 抄本 |
| `Entry_asks_the_quest_log_which_quests_are_cleared` | 已通关的任务同上 |
| `The_answer_to_an_entry_is_what_it_was` | 难度与本号照旧到达 `EnterAsync`；四帧顺序不变 |
| `An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy` | 空日志给出空 facts，不回落抄本 |
| `Without_a_published_session_nothing_is_entered` | 原有守卫不变，且早于问任务簿 |

既有测试的期望值与断言**一处未改**：`git diff --numstat` 里没有任何既有测试文件是本任务改的
（下面逐文件归属）。既知偶发（`OutsideTransactionTests` 的 `ObjectDisposedException`、
`Networking.Tests` 并行偶发）本次**均未出现**。

### `git status --short` 原始输出

```
 M Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs
 M Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs
 M Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
 M Server/src/Dfo.Server.Application/ApplicationModule.cs
 M Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
 M Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs
 M Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs
 M Server/src/Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs
 M Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
 M Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs
 M Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
 M Server/src/Dfo.Server.Protocol/Game/TownProjection/TownReturnProjection.cs
 M Server/tests/Dfo.Server.Application.Tests/Characters/CarriedGoldCapTests.cs
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
?? .rks-agent/tasks/character-state-finds-its-owner/
?? Server/src/Dfo.Server.Abstractions/Characters/ICharacterPositionDomainService.cs
?? Server/src/Dfo.Server.Application/Characters/CharacterPositionDomainService.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterIdentityTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterPositionTests.cs
?? Server/tests/Dfo.Server.Persistence.Tests/Characters/CharacterIdentityReadTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs
```

### `git diff --numstat` 原始输出

```
17	0	Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs
41	2	Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs
3	3	Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
5	2	Server/src/Dfo.Server.Application/ApplicationModule.cs
57	24	Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
24	4	Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs
34	4	Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs
8	2	Server/src/Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs
13	6	Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
8	3	Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs
36	36	Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs
10	2	Server/src/Dfo.Server.Protocol/Game/TownProjection/TownReturnProjection.cs
3	14	Server/tests/Dfo.Server.Application.Tests/Characters/CarriedGoldCapTests.cs
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
```

### 逐个文件说明为何它在改动集合里

动手**之前**先跑过一次 `git status --short` 与 `git diff --numstat`，与上面逐行比对：

| 文件 | 归属 | 依据 |
|---|---|---|
| `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` | **本任务** | 基线 numstat 里**没有这一行**；现为 `13 6` |
| `Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs` | **本任务** | 基线 numstat 里**没有这一行**；现为 `3 3`，全部为 `///` 行 |
| `?? .../SelectDungeonHandlerTests.cs` | **本任务** | 基线 `git status` 的 6 个 `??` 里没有它，现为第 7 个 |
| 其余 24 个 ` M` 与 6 个 `??` | TASK-01 / TASK-02 | 基线 numstat 的 24 行与现 numstat 去掉上面两行后**逐行逐数字相同**（17/0、41/2、5/2、57/24、24/4、34/4、8/2、8/3、36/36、10/2、3/14、102/38、74/20、58/10、3/13、1/3、0/47、12/7、3/0、4/0、9/0、5/0、56/4、3/0）；基线 6 个 `??` 亦原样保留 |

本任务**没有**触碰 TASK-01/TASK-02 的任何成果文件。

### BOM 与行尾检查

```
$ for f in <本任务碰过的三个文件>; do printf '%s: ' "$f"; head -c 3 "$f" | xxd -p; done
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs: 757369
Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs: 757369
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs: 757369
```

`757369` 是 `usi`（三个文件都以 `using` 开头），**不是 `efbbbf`**；三个文件均无 BOM。

```
$ file <同三个文件>
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:                 ASCII text
Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs:                Unicode text, UTF-8 text
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs: ASCII text
```

三个都不是 `with CRLF line terminators`，行尾为 LF。中间那个报 `UTF-8` 而非 `ASCII` 是**改动前
就如此**——它第 38 行有一个既有的 em dash；已逐行比对证明是原有的，本次未新增任何非 ASCII 字节：

```
HEAD     non-ASCII lines: 1
   38 '/// character lands in \u2014 three things that have to happen together and in that order, because the'
worktree non-ASCII lines: 1
   38 '/// character lands in \u2014 three things that have to happen together and in that order, because the'
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs non-ASCII lines: 0
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs non-ASCII lines: 0
```

### 本任务两个生产文件的完整 diff

```diff
diff --git a/Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs b/Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
index 06760395..e90ac7fa 100644
--- a/Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
+++ b/Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
@@ -59,9 +59,9 @@ public interface IDungeonVisitDomainService
     /// dungeon under that identifier.
     /// </summary>
     /// <remarks>
-    /// The facts are handed in rather than found: which quests a character carries comes from the
-    /// entry batch the session was published with, so that the layout is chosen against the state
-    /// the client was told about rather than a fresher read of it.
+    /// The facts are handed in rather than found. Collecting them is the caller's job -- it is the
+    /// one holding the lease while it asks -- and this service stays a function of what it is
+    /// given, which is what keeps a run reproducible from its record.
     /// </remarks>
     ValueTask<DungeonEntry?> EnterAsync(
         CharacterId characterId,
diff --git a/Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs b/Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
index f037f999..7f94908b 100644
--- a/Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
+++ b/Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
@@ -3,6 +3,7 @@ using Dfo.Server.Protocol.Game.DungeonSelection;
 using Dfo.Server.Abstractions.Content;
 using Dfo.Server.Abstractions.Inventory;
 using Dfo.Server.Abstractions.Networking;
+using Dfo.Server.Abstractions.Quests;
 using Dfo.Server.Abstractions.Sessions;
 using Dfo.Server.Abstractions.World;
 using Dfo.Server.Data.Networking;
@@ -414,6 +415,7 @@ public sealed class SelectDungeonHandler(
     ISelectedSessionDomainService selectedSessions,
     ISelectedSessionRuntimeDirectory runtimeSessions,
     IDungeonVisitDomainService visits,
+    ICharacterQuestLogDomainService questLogs,
     IGameMessageSender sender,
     ILogger<SelectDungeonHandler> logger) : ClientMessageHandler<SelectDungeonRequest>
 {
@@ -434,14 +436,19 @@ public sealed class SelectDungeonHandler(
             return;
         }
 
-        // Gathered here and handed over, so the rule stays a function of what it is given. Cleared
-        // quests come from the entry batch the session already carries rather than a fresh read,
-        // which keeps the facts consistent with the state the client was told about.
-        var quests = result.CharacterEntry.Quests;
+        // Gathered here and handed over, so the rule stays a function of what it is given. They
+        // are asked of the character's own log rather than read out of the session, because the
+        // session's copy is whatever the entry batch put there and nothing refreshes it: a quest
+        // taken on or handed in since the character entered the world would leave the maze chosen
+        // against the state the player has already moved on from. Which quests they are is still
+        // decided once, here, and the layout stays fixed to the run it opens.
+        var questLog = questLogs.LogOf(session.AccountId, session.CharacterId);
+        var carried = await questLog.CarriedAsync(cancellationToken).ConfigureAwait(false);
+        var cleared = await questLog.ClearedAsync(cancellationToken).ConfigureAwait(false);
         var facts = new DungeonEntryFacts(
             message.Difficulty,
-            quests.ActiveQuests.Select(value => value.QuestId).ToArray(),
-            quests.ClearedQuestIds);
+            carried.Select(quest => quest.QuestId).ToArray(),
+            cleared.Select(quest => quest.QuestId).ToArray());
 
         var entered = await visits.EnterAsync(
             session.CharacterId, message.DungeonId, message.Difficulty, facts, cancellationToken)
```

## Executor Self-check 1

### Required checks

| ID | 判据 | 结果 | 证据 |
|---|---|---|---|
| CK-011 | `dotnet build Server/86JP.Server.slnx --nologo` 零警告零错误 | **通过** | 上文 CK-011 原始输出：`0 个警告 / 0 个错误`。TreatWarningsAsErrors 开启，故 CS9113（未读主构造器参数）若发生必为错误；新参数 `questLogs` 在 445 行被读，未触发 |
| CK-012 | 本任务新增的任务相关类型清单为空 | **通过（生产侧为空，并如实列出测试替身）** | 上文 CK-012：两个生产文件里新增类型声明匹配数 `0`；新判据文件里 3 个 `private` 任务替身已逐个列名，与既有 `RecordingQuestLogs` 同类 |
| CK-013 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` 通过 | **通过** | 上文 CK-013 七行原始输出；2045 通过 / 2 跳过 / 0 失败 |

### Acceptance conditions

| 条件 | 结果 | 证据 |
|---|---|---|
| 那一处向任务簿实例提问，取数来源不再是入场投影 | **满足** | `DungeonEntrySlice.cs:445-447` 经 `LogOf` → `CarriedAsync` / `ClearedAsync`；`grep -n "CharacterEntry.Quests"` 在该文件零命中 |
| 该处原有的取不到时的语义不变 | **满足** | 守卫段（427-437 行）diff 中未出现，一字未改；空日志给空 facts 而非回落抄本，两条用例分别守之 |
| 本任务新增的任务相关类型清单为空 | **满足（生产侧）** | 同 CK-012；测试替身已如实列出，不藏 |
| 全量判据全绿 | **满足** | 2045 / 2 / 0，无失败；两条既知偶发本次未出现 |

### 硬约束

| 约束 | 结果 | 证据 |
|---|---|---|
| 行为逐值不变 | **满足** | 「为何逐值相同」一节逐语句引文；三处差别（读的时刻、越界行处置、新增 I/O 失败通路）已如实列出，均不改变「同一批行 → 同一个答案」 |
| 既有测试的期望值与断言不得改写 | **满足** | numstat 逐行比对：本任务只新增一个测试文件，未改任何既有测试文件 |
| 不新增协议帧，不改帧字节布局 | **满足** | 两个生产文件 diff 全文如上，无 codec/encoder/message record 改动；Protocol.Tests 505 条（含 `DungeonEntryCodecTests` 的字节钉板）全绿 |
| 分层 | **满足** | 新依赖方向 `Protocol → Abstractions`，接口在 `Dfo.Server.Abstractions/Quests/IQuestLog.cs:293` |
| 切片内不跨协议引用 | **满足** | 未新增任何跨切片引用；`DungeonEntrySlice.cs` 原有的 `using ...Game.DungeonSelection;` 是改动前既存的第 2 行，本次未动 |
| `CommandHandlerArchitectureTests` 继续通过，谓词与豁免清单不得放宽 | **满足** | 该文件不在改动集合内（`git status` 无它）；`SelectDungeonHandler` 不是 `ICommandHandler`；Application.Tests 943 条全绿 |
| TreatWarningsAsErrors / CS9113 | **满足** | CK-011 零警告零错误 |
| 不建 Git 检查点、不提交、不 stash | **满足** | 全程只有 `git status` / `git diff` / `git show` 三个只读命令；`git status` 显示工作区仍未提交，无新分支无 stash |

### 自检里必须说清的三件事

1. **CK-012 的措辞**：契约要的是「不为任务新增任何类型」。产品里确实为零。但新判据文件里有 3
   个 `private` 嵌套的任务替身类型，我把它们**列出来交给控制器判定**，而不是用「清单为空」一句
   盖过去。
2. **越界了一个文件**：`IDungeonVisitDomainService.cs` 不在契约 `Permitted files` 里。改它是因为
   不改会让它的 `<remarks>` 变成假话。改动仅 3 行 `///`。
3. **这条路径原本零判据**，已实证（变异态全量只红本任务新写的 3 条），并已补齐会红的判据。
   同时如实说明：由于 `DungeonEntryFacts` 今天还没有任何规则消费，本改动在线上字节层面暂不可
   观测，判据因此断在 handler 与 `EnterAsync` 的接缝上。

## Independent Quick Review 1

- Verdict: **passed**
- Reviewed contract revision: 1（无修订案）
- Reviewer context: 全新上下文，未参与实现；独立复核，不采信自陈
- Owned diff identity: `HEAD=427331bcb07f60121c97920287ac341c9cb14dee`（未变）；本任务三个文件 ——
  `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs`（`13 6`）、
  `Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs`（`3 3`）、
  新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs`

### 我自己跑的命令

| 命令 | 结果 |
|---|---|
| `git log -1 --format='%H'` | `427331bcb07f60121c97920287ac341c9cb14dee`（变异前后两次均同） |
| `git stash list` | 空（两次） |
| `git status --short` 与 `git diff --numstat` | 26 个 ` M` + 8 个 `??`；逐行逐数字与执行者所载一致 |
| `dotnet build Server/86JP.Server.slnx --nologo` | `0 个警告 / 0 个错误` |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`（复核基线） | 52+505+60+6+256+223+943 = **2045 通过 / 2 跳过 / 0 失败** |
| 同上（我自己的变异态） | **3 失败 / 2042 通过**，见下 |
| 同上（我还原后） | **2045 通过 / 2 跳过 / 0 失败**，与基线逐项目相同 |
| `head -c 3 <三个文件> | xxd -p` | 三个均 `757369`，**非 `efbbbf`**；CRLF 计数均为 0 |

既知偶发（`OutsideTransactionTests` 的 `ObjectDisposedException`、`Networking.Tests` 并行偶发）
本次三轮全量均未出现。

### 一、逐值相同的论证：**成立**（引文逐条回文件核过）

九处引文我逐个回原文件核对，**文本全部属实、逐字相符**：

| 执行者引文 | 我的核对 |
|---|---|
| `CharacterEntryStateTables.cs:151-152` | 行号与文本均确 |
| `CharacterEntryStateServices.cs:64-67` | **文本确、行号差一**：该 4 行实为 **63-66**（`var activeQuests …` 在 63 行） |
| `SqliteQuestDomainStore.cs:134-135` | SQL 字面量在 135，134 为常量声明行，区间成立 |
| `QuestLog.cs:41-43` | 确 |
| `CharacterEntryStateTables.cs:181-182` | 确 |
| `CharacterEntryStateServices.cs:82-83` | 确 |
| `SqliteQuestDomainStore.cs:238-239` | 同上，239 为 SQL |
| `QuestLog.cs:61-64` | 确 |
| `SqliteQuestDomainStore.cs:253` | 确：`rows.Add(new StoredClearedQuest(reader.GetInt32(0), checked((byte)mark)));` |

**结论本身独立验成立**，而且我补验了执行者没有明说的两个环节：

1. **`StoredClearedQuest.QuestId` 确实就是 `slot_index`。** 只看构造调用不足以定论，还要看形参次序：
   `Server/src/Dfo.Server.Abstractions/Quests/IQuestDomainStore.cs:79` 是
   `public sealed record StoredClearedQuest(int QuestId, byte Mark);` —— 第一个位置形参即 `QuestId`，
   而它在 `:253` 由第 0 列 `slot_index` 填入。故 `ClearedAsync` 的 `.OrderBy(row => row.QuestId)`
   与入场投影的 `.Select(value => value.SlotIndex).Order()` 排的、取的是同一个量。**属实。**
2. **两边确实都只留 flag 非零的行，且过滤所比的是同一个量。** 入场侧
   `ICharacterEntryStateServices.cs:95` 的 `ClearFlags` 是 `IReadOnlyList<(int SlotIndex, byte Value)>`，
   过滤为 `Where(value => value.Value != 0)`；任务簿侧为 `Where(row => row.Mark != 0)`，`Mark` 亦为
   `byte`。两者都是同一列 `flag_value` 经各自 byte 校验后的值。**属实。**

关于「一个在 SQL 里排、一个在内存里排」这一点，我确认**不构成差异**，且执行者的理由还偏保守：

- 在做的任务：入场侧 SQL `ORDER BY slot` 之后，`CharacterEntryStateServices.cs:63` **又在内存里
  `OrderBy(value => value.Slot)` 排了一次**；任务簿侧 SQL 无 `ORDER BY`，由 `QuestLog.cs:42` 在
  内存里排。即**两边最终都由同一个内存排序定序**，SQL 端排序的有无根本不影响结果。
  `CharacterDatabaseSchema.cs:177` 的
  `CREATE TABLE … character_active_quests(… PRIMARY KEY(character_id,slot))` 我已核，`slot` 无并列，
  排序无歧义。**属实。**
- 已通关：入场侧 `.Order()`（对 int 升序）、任务簿侧 `.OrderBy(QuestId)`（同一个 int 升序），同序。

**行集合相同**这一环我也单独验了（这是等价性真正的支点，执行者只在「越界行处置」一节间接提到）：
`Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:277-307` 的 `LoadQuestsAsync`
对越界行是 **`throw new InvalidDataException`，不是过滤** —— 两个 `foreach` 里都没有
`continue` 或 `Where`。因此对任何**能进世界**的角色，两条路径读到的是同一批行，无一被丢弃。
等价性成立。

### 二、「原本零判据」与新判据会红：**属实，我独立重做了变异**

我未看执行者的变异脚本，自行把 `DungeonEntrySlice.cs` 的 445-451 替换为
`_ = questLogs; var quests = result.CharacterEntry.Quests; …`（退回读入场投影），跑全量：

```
失败!  - 失败:     3，通过:   502，已跳过:     0，总计:   505 - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败: 0，通过:  52 - Dfo.Server.Networking.Tests.dll
已通过! - 失败: 0，通过:  60 - Dfo.Pvf.Tests.dll
已通过! - 失败: 0，通过:   6 - Dfo.Server.Host.Tests.dll
已通过! - 失败: 0，通过: 256 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败: 0，通过: 223，已跳过: 2 - Dfo.Server.Infrastructure.Tests.dll
已通过! - 失败: 0，通过: 943 - Dfo.Server.Application.Tests.dll
```

报红的三条，全限定名与 Expected/Actual 与执行者所报**逐字相同**：

```
失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_carried
   Assert.Equal() Failure: Collections differ
Expected: <generated> [910, 911]
Actual:   int[]       [55]

失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_asks_the_quest_log_which_quests_are_cleared
   Assert.Equal() Failure: Collections differ
Expected: <generated> [920]
Actual:   int[]       []

失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy
   Assert.Empty() Failure: Collection was not empty
Collection: [55]
```

**红的三条全部出自本任务新写的文件；此前存在的 2040 条一条没红。**
「这条路径原本零判据」经实证成立。

判据设计我也看过，是真会红而非凑数：`Entry()`（测试文件 186-196 行）**先断言** session 抄本里
恰好只有 55 号、已通关为空，再交给 handler；日志一侧给 910/911 与已通关 920。读错来源必然得到
「可辨认的错值」而非与正解巧合。这一点站得住。

**还原核对**（变异前后我都记了 md5）：

```
$ md5sum Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
ca6fa3e3a727bfae33c07c832d5344e6   （与变异前逐字节相同）
$ grep -n "CharacterEntry.Quests\|_ = questLogs" …/DungeonEntrySlice.cs
（零命中）
$ head -c 3 …/DungeonEntrySlice.cs | xxd -p → 757369（未引入 BOM）
```

还原后 `git status --short`、`git diff --numstat`、`git log -1`、`git stash list` 与变异前完全一致，
全量 2045 / 2 / 0 全绿。工作区无任何残留（我的临时副本存在仓库外的 scratchpad，不是仓库内文件）。

### 三、超出 Permitted files 的那处注释改动：**核实通过**

`git diff -- Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs` 全文为
3 增 3 删，**全部是 `///` 行**，落在 `EnterAsync` 的 `<remarks>` 内；紧随其后的
`ValueTask<DungeonEntry?> EnterAsync(` 签名与参数一字未动，无行为、无类型变化。**是纯注释。**

原句「which quests a character carries comes from the entry batch the session was published with …
rather than a fresher read of it」在本任务改动后**确已变假** —— 现在这一处正是 fresher read。
改写后保留的两半（facts 由调用方收集；服务是入参的函数）与 `DungeonLayoutRuleService.ResolveAsync`
的实际形状相符，**为真**。不改则留下一句与代码相反的注释，越界一个文件改三行 `///` 是相称的处置。

### 四、`DungeonEntryFacts` 无消费方的披露：**属实，且我认为应当写进交付记载**

我独立跑了同一条 `grep`，并读了实现，确认论断成立：
`Server/src/Dfo.Server.Application/World/DungeonLayoutRuleService.cs:29` 对 `facts` 只做
`ArgumentNullException.ThrowIfNull(facts);`，此后 `facts` 在该文件再无出现；
`SelectMaze(definition, seed)`（`:69-72`）签名里根本没有 facts，只用 `definition` 与 `seed`。
其 `<remarks>`（`:61-67`）明写「the reference server filters them by the quests the character carries
and by difficulty; that rule has not been read out of it yet, so the choice here is made from the seed
alone and **the facts are deliberately unused**」，并记在 `Docs/DungeonEntryNotes.md`。
故**本改动在今天的线上字节层面确实不可观测**，新判据断在 handler 与 `EnterAsync` 的接缝上是**正确
的判据位置选择**，不是回避字节钉板。披露如实。

（附带核到的两件，均无碍：`DungeonVisitDomainService.cs:38` 确实把 facts 原样转交规则服务，
中途不加工；`ICharacterQuestLogDomainService` 的 DI 注册在 `HEAD:Server/src/Dfo.Server.Application/ApplicationModule.cs:255`
就已存在，本任务给 handler 加构造器参数不会造成解析不到 —— 该注册非本任务新增。）

### 五、CK-012 的判定：**测试替身不计入，CK-012 通过**

我的独立判断：**不算**。理由三条，按份量排列 ——

1. **契约禁的是「为任务重建第二条通路」，而这三个替身恰恰证明了没有第二条通路。**
   `OneLog` / `StandInLog` / `UnaskedLog` 实现的是**已存在**的 `ICharacterQuestLogDomainService`
   与 `IQuestLog`，没有引入任何新的任务概念、记录或接口；`StandInLog` 除
   `CarriedAsync` / `ClearedAsync` 外的**全部**动词一律 `throw new NotSupportedException`
   （测试文件 293-329 行），也就是说它**刻意拒绝**成为任务规则的第二份实现。契约要防的事本身被防住了。
2. **若把测试替身计入，契约会自相矛盾。** 同一份契约的 Required methods 写着
   `test-driven-development`，Acceptance 写着「全量判据全绿」，而 T-003 的改动是「换取数来源」——
   要写出一条会红的判据，就必须有一个能给出与 session 抄本不同答案的日志替身。禁替身等于禁判据。
3. **仓库已有同型先例。** `Server/tests/Dfo.Server.Application.Tests/Support/RecordingQuestLogs.cs:22`
   同样实现 `ICharacterQuestLogDomainService`，其 `<remarks>` 写的正是同一条纪律（只实现用到的，
   其余拒答，以免成为「a second, unchecked copy of the judgement」）。本次三个替身与之同类。

生产侧我独立核过：两个 src 文件的 diff 里**没有任何** `class` / `record` / `struct` / `interface`
/ `enum` / `delegate` 声明，新增的只有一个 `using` 和一个构造器参数。**生产侧新增类型 0，属实。**
执行者不用「清单为空」一句盖过、而是逐个列名交控制器判定，这个处理是恰当的。

### 六、无夹带：**通过**

- 本任务只碰了三个文件，与契约 Permitted files 加一处已声明的注释越界一致。
- 其余 24 个 ` M` 与 6 个 `??` 的数字，与执行者所载 TASK-02 收口值**逐行逐数字相同**；
  我另行抽验 `ApplicationModule.cs` 的 diff，内容是 TASK-01/02 的坐标领域服务注册，与本任务无关。
- **BOM：三个文件均无**（`757369` 即 `usi`，非 `efbbbf`）；CRLF 计数均为 0。上一个 Task 栽的那一处
  本次干净。
- 未建检查点、未提交、未 stash：`git log -1` 三次均 `427331bc`，`git stash list` 三次均空。

### 硬约束核对

| 约束 | 结论 | 我的依据 |
|---|---|---|
| 不新增协议帧、不改帧字节布局 | 通过 | 两个 src 文件 diff 全文无 codec / encoder / message record 触动；`Dfo.Server.Protocol.Tests` 505 条（含字节钉板）全绿 |
| `CommandHandlerArchitectureTests` 通过且未放宽 | 通过 | 该文件不在改动集合内；其谓词（`CommandHandlerArchitectureTests.cs:126`）只筛 `ICommandHandler<,>` 实现，而 `SelectDungeonHandler` 是 `ClientMessageHandler<SelectDungeonRequest>`，本就不在其射程内；`Exempt` 清单未被触碰；Application.Tests 943 全绿 |
| 分层未违反 | 通过 | 新依赖 `Protocol → Abstractions.Quests` 为允许方向；同程序集内 `AcceptQuestCommand.cs:84` 等早有同一引用 |
| 切片内不跨协议引用 | 通过 | 唯一新增 `using` 是 `Dfo.Server.Abstractions.Quests`；`using …Protocol.Game.DungeonSelection;` 在 diff 中是未变的上下文行，为改动前既存 |
| 「取不到时」语义不变 | 通过 | 守卫段（`return;` 之前）整段在 diff 中为未变上下文；`ICharacterQuestLogDomainService.LogOf` 的 `<remarks>`（`IQuestLog.cs:287-291`）明载「A character always has one … a character with no quests has an empty log rather than none」，恒返回实例、不新增 null 分支；空日志给空 facts 而非回落抄本，由 `An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy` 钉住 |

### 非阻断观察（不要求本轮修改）

1. **建议采纳并转交 TASK-09（对应上文第四条）。**「`DungeonEntryFacts` 今天无任何规则消费、
   本改动在线上字节层面不可观测」这一条我已独立验证为真，且它直接影响成果价值的读法 ——
   本 Task 交付的是**取数点的归位**，不是一处可观测的行为修正。控制器倾向于写进交付记载，
   我**赞同**：承接者 TASK-09 既然专记「每一项实际做到哪一步」，这正是该被所有者知悉的那类事实，
   否则日后读记载的人会误以为本项已带来线上可见的修正。建议连同
   `DungeonLayoutRuleService.cs:61-67` 的 `<remarks>` 与 `Docs/DungeonEntryNotes.md` 一并指过去。

2. **一处行号差一。** `CharacterEntryStateServices.cs:64-67` 应为 **63-66**（引文文本本身逐字无误，
   结论不受影响）。记在此处，供后续引用者不被带偏。

3. **一处出处不够严的记载（记载纪律项）。**「枚举证据」一节所载
   `grep -rn "facts" --include=*.cs Server/src | grep -v "///"` 的输出，我照原样重跑，实际还命中
   `ApplicationModule.cs:188`、`PlayingCharacter.cs:158`、`SelectCharacterPreparationService.cs:78`
   与 `:324`、`InventoryEntryAssembly.cs:151` 与 `:239` 共 6 行（均为 `//` 单行注释，`grep -v "///"`
   不会滤掉它们）。交付里只声明了裁掉 `SqliteItemDomainStore.cs` 的同名局部变量，未声明裁掉这 6 行，
   即**贴出的「原始输出」是经过删减的**。被删的 6 行与本主题确实无关，**结论不受影响**，故不阻断；
   但按本 Spec「枚举须附产生它的命令与原始输出」的要求，这属于应当改进的记载习惯：
   要么贴全，要么把裁剪范围一并写明。

4. **AGENTS.md「handler 业务语义规则」第 2 类的边界，供最终评审留意（非本任务引入）。**
   该条允许「只读查询与端点解析」，括注举的是 `IOnlineUserQueryDomainService`、`IChannelEntryPolicy`。
   本处新增的 `CarriedAsync` / `ClearedAsync` 是严格只读，符合该类的字面语义；且该 handler 在本任务
   之前**已经**注入 `ISelectedSessionDomainService`、`ISelectedSessionRuntimeDirectory`、
   `IDungeonVisitDomainService` 三个领域服务并调用 `EnterAsync`，其形状不是本任务确立的。
   本任务只是替换取数来源，**未使该形状变差**，故不作为本轮缺陷；是否要收紧那条括注的读法，
   属完整变更评审的范畴。

### 结论

六件逐条核实，无本地可修复的阻断缺陷。等价性论证经回文件核对成立（含我补验的两个支点），
变异实验由我独立重做且逐字复现，注释越界属实且相称，无消费方的披露如实，CK-012 判为通过，
无夹带、无 BOM、无提交与 stash。

**Verdict: `passed`。** 本判定只覆盖 TASK-03 这一个 Task Unit 的有限缺陷检查，不构成完整变更评审，
也不替代接受前的新鲜验证。
