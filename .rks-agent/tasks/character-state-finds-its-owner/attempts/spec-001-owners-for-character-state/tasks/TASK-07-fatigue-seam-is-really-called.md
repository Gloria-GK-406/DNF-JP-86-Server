# TASK-07 — 疲劳有一个接缝，进本处真的向它报告

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 一个能回答总量、已用量、剩余量并接受「用掉若干」与「退回若干」两种报告的疲劳接缝；一个**不记账**的实现；进本处向它报告本次消耗。
- Referenced spec requirements: RQ-009, RQ-010
- Referenced plan sections: T-007；BD-013, BD-006
- Owned boundary: 疲劳接缝、其占位实现、进本处的调用，以及相应判据。
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Abstractions/Characters/` 下新增的疲劳接缝
  - `Server/src/Dfo.Server.Application/Characters/` 下其不记账的实现与组合根接线
  - `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs`
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/` 下的新增判据
- Dependencies: TASK-06
- Required dependency outputs: 无
- Acceptance conditions:
  - 接缝能答三个数并接受两种报告
  - **实现不记账**：报告一次消耗之后再问已用量，答案未变；继续报出协议今天报出的那两个值
  - **不记账这一点可从代码本身读出**（类型名与文档直说），不必从行为反推
  - **不建 store，不建表**
  - 进本处真的调用，报告的数取自该副本的消耗
  - 全量判据全绿；疲劳两个字段在帧上的值与基线相同
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: test-driven-development
- Required checks: CK-026, CK-027, CK-028, CK-029, CK-030
- Required evidence:
  - 接缝的成员与占位实现全文
  - 进本处调用的位置与所报的数的来源
  - 对着记录式替身断言调用发生且数对的判据
  - 全量判据输出，含疲劳两个字段的值与基线相同的证据

## Execution Result 1

本 Task 只做占位接缝，不做记账。下面每一条声称都附产生它的命令与**未裁剪**的原始输出；确有裁剪
的地方写明裁掉了什么、为何不影响结论。

### 一、接缝的成员与占位实现全文

#### 1.1 接缝（新建）：`Server/src/Dfo.Server.Abstractions/Characters/ICharacterFatigueDomainService.cs`

成员：`BalanceOfAsync`（一次答出总量 / 已用量 / 剩余量三个数）、`SpendAsync`（用掉若干）、
`RefundAsync`（退回若干，为抗疲劳药剂准备）。全文：

```csharp
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// One character's fatigue: what a day gives them, what is spent of it, and what is left.
/// </summary>
/// <param name="Total">
/// Everything the character may spend before the daily reset. The pair of this and
/// <paramref name="Used"/> is what the entry acknowledgement carries -- a live A21 session was
/// measured sending 188 there
/// (<c>protocol/game/client-to-server/0x0004.md</c>, offsets 13 and 15).
/// </param>
/// <param name="Used">How much of the total is already spent.</param>
/// <remarks>
/// Not <see cref="CharacterFatigueState"/>, and deliberately beside it rather than instead of it:
/// that record is the shape the entry frame carries and lives in the data layer with the rest of
/// the character's entry state. This is the answer a caller asks a service for, and it answers the
/// third number -- what is left -- which the frame has no field for and every spender needs.
/// </remarks>
public readonly record struct CharacterFatigueBalance(ushort Total, ushort Used)
{
    /// <summary>What is left to spend.</summary>
    /// <remarks>
    /// Floored at zero. Both fields are unsigned, so a used count past the total would otherwise
    /// wrap to a very large remainder -- the one arithmetic mistake this number can make, and the
    /// one that would read as an unlimited allowance rather than as an exhausted one.
    /// </remarks>
    public ushort Remaining =>
        this.Used >= this.Total ? (ushort)0 : (ushort)(this.Total - this.Used);
}

/// <summary>
/// A character's fatigue, and the two directions a day's play moves it.
/// </summary>
/// <remarks>
/// <b>A seam ahead of the system behind it: fatigue is not implemented.</b> The only implementation
/// registered today is <c>UnaccountedCharacterFatigueDomainService</c>, which answers a fixed pair
/// and keeps no account of anything reported to it. It is here so that the places that spend
/// fatigue and the places that give it back can be found and called while the system that would
/// record them does not exist, and so that building that system is one implementation rather than
/// a search for the callers it never had.
/// <para>
/// The rules it will keep are written down and none of them are kept here: a point charged for
/// each newly entered room that holds monsters, nothing charged for walking back into a cleared
/// one, a floor of zero, and no entry at all once it reaches zero
/// (<c>Docs/DungeonEntryNotes.md</c> section 8, dictated 2026-08-27). The owner gives the day's
/// total as 156 with a further 32 for a black diamond, recovered by a fatigue potion and reset
/// daily; 156 + 32 is the 188 the measured session sends.
/// </para>
/// <para>
/// Neither verb asks permission. Whether a character has enough to enter is a separate question
/// with its own refusal on the wire (<c>SelectDungeonRefusal.InsufficientFatigue</c>), and nothing
/// asks it yet; a report that could be refused would be a gate, and a gate that always says yes is
/// the kind of thing that is mistaken for a working one.
/// </para>
/// </remarks>
public interface ICharacterFatigueDomainService
{
    /// <summary>What this character has, has spent, and has left.</summary>
    ValueTask<CharacterFatigueBalance> BalanceOfAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Reports that this character spent that many points.</summary>
    ValueTask SpendAsync(
        CharacterId characterId, ushort points, CancellationToken cancellationToken);

    /// <summary>Reports that this character got that many points back.</summary>
    /// <remarks>
    /// The other direction has a caller waiting rather than a hypothetical one: a fatigue potion
    /// returns points already spent. Nothing drinks one yet, which is why it is declared now --
    /// a seam that only counted downwards would have to be reopened for the first drink.
    /// </remarks>
    ValueTask RefundAsync(
        CharacterId characterId, ushort points, CancellationToken cancellationToken);
}
```

#### 1.2 占位实现（新建）：`Server/src/Dfo.Server.Application/Characters/UnaccountedCharacterFatigueDomainService.cs`

全文：

```csharp
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// The fatigue seam with no fatigue behind it: it answers a fixed pair and keeps no account.
/// </summary>
/// <remarks>
/// <b>Nothing reported to this is recorded anywhere, by design.</b> <see cref="SpendAsync"/> and
/// <see cref="RefundAsync"/> return having done nothing at all; <see cref="BalanceOfAsync"/>
/// answers the same two numbers before and after either of them, for every character, for the
/// server's whole lifetime. There is no store behind it and no table under it, because a store
/// that holds nothing is worse than none: it reads as a persistence path that already exists.
/// <para>
/// It is a placeholder and it is named one. What it stands in for is the fatigue system, which is
/// its own piece of work: a day's allowance, a charge per newly entered room, a floor, a refusal at
/// zero, a potion that gives points back and a daily reset. Putting the seam in first lets the
/// place that spends fatigue call it now -- so the call site is written, wired and tested against
/// the shape it will keep -- while the numbers on the wire stay exactly where they are. That
/// matters for more than tidiness: a real accounting would count a test session's fatigue down to
/// zero and lock the character out of the dungeon that the rest of this work is checked against.
/// </para>
/// <para>
/// The pair it answers is the pair the server already sends. <c>CharacterRosterService</c> builds
/// every entering character's <see cref="CharacterFatigueState"/> as a hardcoded
/// <c>(188, 0)</c> and the entry acknowledgement writes those two numbers straight out, so
/// answering anything else here would describe a character the client is not being told about.
/// The two literals are restated rather than shared because the roster's belongs to the frame it
/// fills and this one to the seam; the fatigue system replaces both at once.
/// </para>
/// </remarks>
internal sealed class UnaccountedCharacterFatigueDomainService : ICharacterFatigueDomainService
{
    /// <summary>The day's allowance every character is told they have.</summary>
    /// <remarks>
    /// The number a live A21 session was measured sending, and the number this server hardcodes:
    /// 156 as standard plus 32 for a black diamond, as the owner gives the rule.
    /// </remarks>
    public const ushort UnvaryingTotal = 188;

    /// <summary>How much every character is told they have spent, whatever they have done.</summary>
    public const ushort UnvaryingUsed = 0;

    /// <summary>Answers the fixed pair, ignoring which character is asked about.</summary>
    public ValueTask<CharacterFatigueBalance> BalanceOfAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new CharacterFatigueBalance(UnvaryingTotal, UnvaryingUsed));

    /// <summary>Accepts the report and discards it: nothing is spent and nothing is written.</summary>
    public ValueTask SpendAsync(
        CharacterId characterId, ushort points, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <summary>Accepts the report and discards it: nothing is returned and nothing is written.</summary>
    public ValueTask RefundAsync(
        CharacterId characterId, ushort points, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
```

#### 1.3 组合根接线：`Server/src/Dfo.Server.Application/ApplicationModule.cs`

`git diff -U2 -- Server/src/Dfo.Server.Application/ApplicationModule.cs` 原始输出（未裁剪。其中
`ICharacterPositionDomainService`、`ICharacterSettingsDomainService`、`CharacterDomainService` 的
参数变化、`ICharacterDarkKnightComboDomainService` 四处是 TASK-02/04/05/06 的成果，**不是本 Task
的改动**，机械证据见第七节）：

```diff
diff --git a/Server/src/Dfo.Server.Application/ApplicationModule.cs b/Server/src/Dfo.Server.Application/ApplicationModule.cs
index 75268dae..cd461076 100644
--- a/Server/src/Dfo.Server.Application/ApplicationModule.cs
+++ b/Server/src/Dfo.Server.Application/ApplicationModule.cs
@@ -217,4 +217,19 @@ public sealed class ApplicationModule : IServerModule
         services.TryAddSingleton<
             ICharacterSkillPointDomainService, CharacterSkillPointDomainService>();
+        // Where a character being played is standing, asked by identifier. It used to be a field
+        // on the session's snapshot of the character, which meant every step rewrote the snapshot
+        // and every reader had to find a session before it could ask.
+        services.TryAddSingleton<
+            ICharacterPositionDomainService, CharacterPositionDomainService>();
+        // A character's keys and options, asked by identifier. The entry load is the only other
+        // reader of those two tables and it is left alone: it builds its frames from rows it has
+        // already read, and this reads the same rows for anyone who is not entry.
+        services.TryAddSingleton<
+            ICharacterSettingsDomainService, CharacterSettingsDomainService>();
+        // A character's fatigue -- the seam only. The implementation answers a fixed pair and
+        // records nothing, which is what its name says; the system that would record it is its own
+        // piece of work and this exists so that the places that spend fatigue call it now.
+        services.TryAddSingleton<
+            ICharacterFatigueDomainService, UnaccountedCharacterFatigueDomainService>();
         services.TryAddSingleton(
             provider => new CharacterDomainService(
@@ -225,6 +240,4 @@ public sealed class ApplicationModule : IServerModule
                     provider.GetRequiredService<IItemUseJudgementService>(),
                     provider.GetRequiredService<ICharacterItemCooldownDomainService>(),
-                    provider.GetRequiredService<ISelectedSessionDomainService>(),
-                    provider.GetRequiredService<ISelectedSessionRuntimeDirectory>(),
                     provider.GetRequiredService<ISessionPresenceDomainService>(),
                     provider.GetRequiredService<ICharacterCarriedGoldTable>(),
@@ -254,4 +267,6 @@ public sealed class ApplicationModule : IServerModule
         services.TryAddSingleton<ICharacterContainerDomainService, CharacterContainerDomainService>();
         services.TryAddSingleton<ICharacterQuestLogDomainService, CharacterQuestLogDomainService>();
+        services.TryAddSingleton<
+            ICharacterDarkKnightComboDomainService, CharacterDarkKnightComboDomainService>();
         services.TryAddSingleton<ICharacterItemCooldownDomainService, CharacterItemCooldownDomainService>();
         services.TryAddSingleton<IItemUseJudgementService, ItemUseJudgementService>();
```

本 Task 在这个文件里只加了 5 行：3 行注释 + 2 行 `TryAddSingleton`。

> 关于 permitted files：契约写的是「`Server/src/Dfo.Server.Application/Characters/` 下其不记账的
> 实现与**组合根接线**」。仓库里领域服务的注册点只有 `ApplicationModule.cs`（同一个项目，但在
> `Characters/` 目录之外），没有第二处。如实记在这里，不假装它落在目录内。

### 二、「不记账」这一点如何从代码本身读出

不必从行为反推，六处直说：

1. **类型名**：`UnaccountedCharacterFatigueDomainService`——"Unaccounted"（未记账）就是名字本身。
2. **类摘要第一句**：`The fatigue seam with no fatigue behind it: it answers a fixed pair and keeps no account.`
3. **类 remarks 第一句加粗**：`<b>Nothing reported to this is recorded anywhere, by design.</b>`，
   紧接着逐个方法点名：两个报告方法返回时什么都没做，`BalanceOfAsync` 在它们前后答同一对数。
4. **两个报告方法各自的 summary**：`Accepts the report and discards it: nothing is spent and
   nothing is written.` / `...: nothing is returned and nothing is written.`——不是一个"看起来在
   工作的空方法"，方法名旁边就写着它把报告丢掉了。
5. **接缝 remarks 第一句加粗**：`<b>A seam ahead of the system behind it: fatigue is not
   implemented.</b>`，并点名今天注册的唯一实现是哪个类。
6. **组合根注释**：`The implementation answers a fixed pair and records nothing, which is what its
   name says`。

**不建 store、不建表**——枚举证据（完整输出，7 行）：

```
$ grep -rn "ICharacterFatigueDomainService" --include=*.cs Server/src Server/tests
Server/src/Dfo.Server.Abstractions/Characters/ICharacterFatigueDomainService.cs:58:public interface ICharacterFatigueDomainService
Server/src/Dfo.Server.Application/ApplicationModule.cs:233:            ICharacterFatigueDomainService, UnaccountedCharacterFatigueDomainService>();
Server/src/Dfo.Server.Application/Characters/UnaccountedCharacterFatigueDomainService.cs:33:internal sealed class UnaccountedCharacterFatigueDomainService : ICharacterFatigueDomainService
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:78:    /// Nothing sends it. Entry reports what it costs to <see cref="ICharacterFatigueDomainService"/>
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:426:    ICharacterFatigueDomainService fatigue,
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:505:        // ICharacterFatigueDomainService -- which is why this is the report and not a check: there
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:305:    private sealed class RecordingFatigue : ICharacterFatigueDomainService
```

唯一实现是那个占位类，唯一另一个实现者是判据里的记录式替身。新增文件里没有任何 `*DomainStore` /
`*Repository` / `*Table` / `*Persistence` 类型；`Server/src/Dfo.Server.Persistence/Schema/` 下一个
文件都不在本 Task 的改动集合里（第七节机械比对）。

### 三、进本处的调用：位置、所报的数，以及这个数的来源

**位置**：`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` 的
`SelectDungeonHandler.HandleMessageAsync`，在 `var (layout, visit) = entered;` 之后、日志与发帧之前
（当前行号 503-511）。刻意放在 `entered is null` 的拒绝分支**之后**，所以被拒的选本一次报告都不发。

```csharp
        var (layout, visit) = entered;

        // Reported once the entry has actually happened, so that a refused selection reports
        // nothing. What the seam does with it is nothing at all today -- see
        // ICharacterFatigueDomainService -- which is why this is the report and not a check: there
        // is no balance to be short of.
        await fatigue
            .SpendAsync(session.CharacterId, FatigueAnEntryCosts, cancellationToken)
            .ConfigureAwait(false);
```

**所报的数**：`SelectDungeonHandler.FatigueAnEntryCosts = 1`（`ushort`）。

**这个数从哪来，为什么不是从副本档案里取的**——如实说：

- **今天全仓没有任何「每个副本消耗多少点」的值可取。** 依据是
  `grep -rni "fatigue" --include=*.cs --include=*.sql --include=*.json Server/src` 的**完整** 84 行
  输出（很长，此处不重复粘贴；与"副本消耗量"沾边的条目**全部**列在下面，其余条目分别属于
  `fatigue_penalty` / `fatigue_update` / `fatigue_display_update` / `stamina` 这四个与当前疲劳无关
  的库列、经验加成枚举 `FatigueBuff` / `FatigueBurn`、疲劳加速帧 `FatigueAcceleration*`，以及本
  Task 自己新增的文本）：
  - `Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs:85: bool ConsumesFatigue,`
    ——**只是一个布尔**（档案 `[no fatigue]` 标记），不是数量；且契约明令本 Task 不消费它。
  - `PvfDungeonDefinitionSource.cs:90 / 125 / 127 / 236 / 329 / 351`——解析那个布尔的地方，同样没有
    数量。
  - `CharacterRosterService.cs:147: new CharacterFatigueState(188, 0)`——角色侧写死的总量与已用量，
    不是副本消耗。
  - `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:180` 的
    `character_condition` 建表语句（我读了整条 `CREATE TABLE`，不是抽样）：只有
    `fatigue_display_update` / `fatigue_penalty` / `fatigue_update` / `stamina`，没有当前疲劳、
    没有上限、更没有副本消耗。
  - `DungeonDefinition`（`Abstractions/Content/DungeonDefinitionContent.cs:81-89`）与
    `DungeonLayout`（`Abstractions/World/IDungeonLayoutRuleService.cs:52-62`）两个 record 我逐字段
    读完：没有任何消耗点数字段。
- **有的是「一次扣多少」这条规则本身。** `Docs/DungeonEntryNotes.md` 第八节（用户 2026-08-27 口述）
  写着"何时扣：**进入一间「新房间」时扣 1**"。进本就是走进这次运行的第一间房，所以一次进本的
  消耗就是那 1 点。**这个数取自写下来的规则，不是编的。**
- **三件会改变这个数的事情本 Task 刻意不问**，因为每一件都属于疲劳系统那个 Spec：第一间房里有没有
  怪、这间房是不是走过、这个副本 `[no fatigue]` 到底扣不扣。常量的 XML doc 把这三条逐条写明，并
  说明疲劳系统落地时要变的是"这个数怎么算出来"，而不是"这里发不发报告"。

### 四、变异验证记录

按 Task 指令，**本 Task 不做进本调用点（production 侧）的变异**——那是 TASK-08 的专职。这里做的是
"我自己新加的判据各自非空转"的证明：CK-028 的两条用例各由一次**接缝实现**的变异证伪，CK-029 由
两次**判据自身期望**的变异证伪（不动 production 代码，以免抢 TASK-08 的活）。

| 变异 | 改了什么 | 命令 | 结果 |
|---|---|---|---|
| M1 | 占位实现改成**真记账**：加 `private ushort mutantUsed`，`SpendAsync` 累加它，`BalanceOfAsync` 答它 | `dotnet test .../Dfo.Server.Protocol.Tests.csproj --nologo --filter "FullyQualifiedName~UnaccountedFatigueTests"` | **失败 2 / 通过 0** |
| M2 | `UnvaryingTotal` 188 → 189 | 同上 | **失败 1 / 通过 1**（只有第二条红） |
| M3a | CK-029 判据里期望值 `(ushort)1` → `(ushort)2` | `... --filter "FullyQualifiedName~SelectDungeonHandlerTests"` | **失败 1 / 通过 5** |
| M3b | CK-029 判据里 `Assert.Single(...)` + 两条 `Assert.Equal` → `Assert.Empty(fatigue.Spent)` | 同上 | **失败 1 / 通过 5** |

M1 原始输出：

```
[xUnit.net 00:00:00.22]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries [FAIL]
[xUnit.net 00:00:00.23]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.A_reported_spend_moves_no_number_the_seam_answers [FAIL]
  失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries [5 ms]
  错误消息:
   Assert.Equal() Failure: Values differ
Expected: 0
Actual:   7
  堆栈跟踪:
     at ...UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries() in ...\UnaccountedFatigueTests.cs:line 60
  失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.A_reported_spend_moves_no_number_the_seam_answers [< 1 ms]
  错误消息:
   Assert.Equal() Failure: Values differ
Expected: 0
Actual:   7
  堆栈跟踪:
     at ...UnaccountedFatigueTests.A_reported_spend_moves_no_number_the_seam_answers() in ...\UnaccountedFatigueTests.cs:line 34
失败!  - 失败:     2，通过:     0，已跳过:     0，总计:     2，持续时间: 25 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
```

（裁剪说明：只把堆栈行里的绝对路径缩成 `...`，并删掉两行 `--- End of stack trace from previous
location ---`。断言消息、行号、计数一字未动。）

M2 原始输出（同样只缩了路径）：

```
[xUnit.net 00:00:00.27]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries [FAIL]
  失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries [5 ms]
  错误消息:
   Assert.Equal() Failure: Values differ
Expected: 188
Actual:   189
  堆栈跟踪:
     at ...UnaccountedFatigueTests.cs:line 59
失败!  - 失败:     1，通过:     1，已跳过:     0，总计:     2，持续时间: 24 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
```

M3a 原始输出：

```
[xUnit.net 00:00:00.28]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [FAIL]
  失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [2 ms]
  错误消息:
   Assert.Equal() Failure: Values differ
Expected: 2
Actual:   1
  堆栈跟踪:
     at ...SelectDungeonHandlerTests.cs:line 142
失败!  - 失败:     1，通过:     5，已跳过:     0，总计:     6，持续时间: 42 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
```

M3b 原始输出：

```
[xUnit.net 00:00:00.20]     Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [FAIL]
  失败 Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam [2 ms]
  错误消息:
   Assert.Empty() Failure: Collection was not empty
Collection: [Tuple (4660, 1)]
  堆栈跟踪:
     at ...SelectDungeonHandlerTests.cs:line 140
失败!  - 失败:     1，通过:     5，已跳过:     0，总计:     6，持续时间: 43 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
```

M3b 顺带把报告的内容原样打了出来：`Tuple (4660, 1)`——4660 = 0x1234 = 判据里那个角色的 id，
1 = 报告的点数。

**每条判据分别由哪次变异证明非空转**：

- CK-028 用例一 `A_reported_spend_moves_no_number_the_seam_answers`：由 **M1** 证伪（占位一旦真
  记账它就红）。
- CK-028 用例二 `The_pair_it_answers_is_the_pair_the_entry_frame_carries`：由 **M2** 单独证伪
  （M1 也会让它红，但 M2 只让它红、另一条仍绿，所以它是被**单独**证明的）。
- CK-029 `Entry_reports_what_it_costs_to_the_fatigue_seam`：**M3a** 证明"数对"那半不是空转，
  **M3b** 证明"报告发生"那半不是空转。

**还原证据**：四次变异全部还原。还原后

```
$ grep -n "mutant\|189\|ushort)2\|Assert.Empty(fatigue" <占位实现> <SelectDungeonHandlerTests.cs>
grep exit=1 (1 means no residue found)
$ dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --nologo --filter "FullyQualifiedName~DungeonEntry"
已通过! - 失败:     0，通过:    34，已跳过:     0，总计:    34，持续时间: 51 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
```

### 五、判据输出

#### CK-026 `dotnet build Server/86JP.Server.slnx --nologo`

```
已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:02.61
```

#### CK-027 Non-command：检视接缝实现

见第二节。不记账从**类型名**（`Unaccounted...`）与**文档首句**即可读出，六处直说，无需从行为反推。

#### CK-028 新增用例：报告一次消耗后再问已用量 → 答案未变

新建 `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/UnaccountedFatigueTests.cs`，全文：

```csharp
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

/// <summary>
/// The fatigue seam's only implementation keeps no account, which is the whole of what it does.
/// </summary>
/// <remarks>
/// Beside the test of the call site rather than among the application tests, because the two
/// halves of this placeholder only mean anything together: entry reports what it costs, and
/// nothing whatever comes of the report. Read either one alone and the other is what you would
/// have to assume.
/// </remarks>
public sealed class UnaccountedFatigueTests
{
    /// <summary>
    /// A reported spend leaves every number the seam can be asked for exactly where it was.
    /// </summary>
    /// <remarks>
    /// The used count is the one this is about -- it is what a real accounting would move, and it
    /// is what the entry acknowledgement carries -- but the total and the remainder are asserted
    /// with it so that "unchanged" is the whole answer rather than one field of it.
    /// </remarks>
    [Fact]
    public async Task A_reported_spend_moves_no_number_the_seam_answers()
    {
        var fatigue = new UnaccountedCharacterFatigueDomainService();

        var before = await fatigue.BalanceOfAsync(Character, CancellationToken.None);
        await fatigue.SpendAsync(Character, 7, CancellationToken.None);
        var after = await fatigue.BalanceOfAsync(Character, CancellationToken.None);

        Assert.Equal(before.Used, after.Used);
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.Remaining, after.Remaining);
    }

    /// <summary>
    /// And what it answers, after reports in both directions, is the pair the entry frame carries.
    /// </summary>
    /// <remarks>
    /// The two numbers are written out rather than read off the implementation's own constants, for
    /// the reason the call site's test writes its number out: an expectation taken from the thing
    /// under test agrees with it about whatever it becomes. 188 and 0 are what
    /// <c>CharacterRosterService</c> hardcodes into every entering character's fatigue state and
    /// what the acknowledgement therefore puts on the wire, so they are the pair this has to keep
    /// answering while fatigue is unimplemented.
    /// </remarks>
    [Fact]
    public async Task The_pair_it_answers_is_the_pair_the_entry_frame_carries()
    {
        var fatigue = new UnaccountedCharacterFatigueDomainService();

        await fatigue.SpendAsync(Character, 7, CancellationToken.None);
        await fatigue.RefundAsync(Character, 3, CancellationToken.None);
        var balance = await fatigue.BalanceOfAsync(Character, CancellationToken.None);

        Assert.Equal((ushort)188, balance.Total);
        Assert.Equal((ushort)0, balance.Used);
        Assert.Equal((ushort)188, balance.Remaining);
    }

    /// <summary>Whoever is asked about; the seam does not distinguish them and does not pretend to.</summary>
    private static readonly CharacterId Character = new(0x1234);
}
```

（放在 `Protocol.Tests/Game/DungeonEntry/` 是因为契约的 permitted files 只给了这个测试目录；文件头
的 remarks 说明了为什么它挨着调用点的判据放。`Dfo.Server.Application.csproj` 早已有
`<InternalsVisibleTo Include="Dfo.Server.Protocol.Tests" />`，所以 `internal` 的占位实现在这里可见，
**没有为此改任何 csproj**。）

#### CK-029 新增用例：进本时对着记录式替身断言报告发生且数对

判据本体（`SelectDungeonHandlerTests.cs:118-143`）：

```csharp
    /// <summary>
    /// Entering reports what the entry costs to the fatigue seam, against this character.
    /// </summary>
    /// <remarks>
    /// The number is written out here rather than read from
    /// <see cref="SelectDungeonHandler.FatigueAnEntryCosts"/>. A test that takes its expectation
    /// from the constant it is checking agrees with the handler about whatever that constant
    /// becomes, which is the one change this exists to catch.
    /// <para>
    /// What it does not check is any effect of the report, because there is none: the seam's only
    /// implementation keeps no account. That half is checked next door, in
    /// <see cref="UnaccountedFatigueTests"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Entry_reports_what_it_costs_to_the_fatigue_seam()
    {
        var fatigue = new RecordingFatigue();
        var handler = Handler(new RecordingVisits(), fatigue: fatigue);

        await handler.HandleAsync(Dispatch(), Request(), CancellationToken.None);

        var spent = Assert.Single(fatigue.Spent);
        Assert.Equal(Identity.CharacterId, spent.CharacterId);
        Assert.Equal((ushort)1, spent.Points);
    }
```

记录式替身（`SelectDungeonHandlerTests.cs:296-323`）：

```csharp

    /// <summary>
    /// The fatigue seam, remembering what it is told and refusing everything else.
    /// </summary>
    /// <remarks>
    /// Entry neither reads a balance nor gives points back, so both of those are defects this
    /// fixture reports rather than absorbs: a balance read would mean something here had started
    /// gating entry on fatigue, which is not this seam's doing and not this frame's.
    /// </remarks>
    private sealed class RecordingFatigue : ICharacterFatigueDomainService
    {
        public List<(CharacterId CharacterId, ushort Points)> Spent { get; } = [];

        public ValueTask SpendAsync(
            CharacterId characterId, ushort points, CancellationToken cancellationToken)
        {
            this.Spent.Add((characterId, points));
            return ValueTask.CompletedTask;
        }

        public ValueTask<CharacterFatigueBalance> BalanceOfAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon asks for no balance; nothing gates it.");

        public ValueTask RefundAsync(
            CharacterId characterId, ushort points, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Entering a dungeon gives no fatigue back.");
    }
```

构造器工厂的改动（`SelectDungeonHandlerTests.cs:191-208`）：

```csharp
    private static SelectDungeonHandler Handler(
        RecordingVisits visits,
        RecordingSender? sender = null,
        OneLog? logs = null,
        RecordingFatigue? fatigue = null) =>
        new(
            new OneSession(Identity),
            new OneRuntimeSession(Entry()),
            visits,
            logs ?? new OneLog(new StandInLog(
                [
                    new CarriedQuest(0, CarriedInLog, 4),
                    new CarriedQuest(1, AlsoCarriedInLog, 0),
                ],
                [new ClearedQuest(ClearedInLog, IQuestLog.ClearedMark)])),
            fatigue ?? new RecordingFatigue(),
            sender ?? new RecordingSender(),
            NullLogger<SelectDungeonHandler>.Instance);

```

#### CK-030 全量 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

下面是同一条命令的输出经 `| grep -E "已通过!|失败!|已跳过 "` 过滤后的汇总行。**裁掉的是各项目的
编译输出行与"总共 1 个测试文件与指定模式相匹配"这类噪声，不含任何失败信息**；这条命令的
**未过滤**版本在本 Task 里也完整跑过一次（在最后一次收尾改注释之前），汇总数字与下面完全相同。

```
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52，持续时间: 1 s - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   508，已跳过:     0，总计:   508，持续时间: 3 s - Dfo.Server.Protocol.Tests.dll (net10.0)
  已跳过 Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message [1 ms]
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60，持续时间: 9 s - Dfo.Pvf.Tests.dll (net10.0)
  已跳过 Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog [1 ms]
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6，持续时间: 8 s - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   256，已跳过:     0，总计:   256，持续时间: 14 s - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   223，已跳过:     2，总计:   225，持续时间: 17 s - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   962，已跳过:     0，总计:   962，持续时间: 17 s - Dfo.Server.Application.Tests.dll (net10.0)
```

**计数与归因**：52 + 508 + 60 + 6 + 256 + 223 + 962 = **2067 通过 / 2 跳过 / 0 失败**。
基线 2064 / 2 / 0，**净 +3 通过**，跳过数不变。三条新增用例是：

- `Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam`（CK-029）
- `Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.A_reported_spend_moves_no_number_the_seam_answers`（CK-028）
- `Dfo.Server.Protocol.Tests.Game.DungeonEntry.UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries`（CK-028）

归因不是从数字反推：本 Task 只碰过两个判据文件，两者的用例计数是

```
$ grep -c "^\s*\[Fact\]\|^\s*\[Theory\]\|^\s*\[SkippableFact\]" <两个文件>
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:6
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/UnaccountedFatigueTests.cs:2
```

`SelectDungeonHandlerTests` 原有 5 条（TASK-06 交付），本 Task 加 1 条 → 6；`UnaccountedFatigueTests`
是新文件 2 条。1 + 2 = 3，与净增量一致；第七节的机械比对证明**没有别的判据文件被本 Task 触碰**。

既知偶发（`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` 的
`ObjectDisposedException`、`Networking.Tests` 的并行偶发）在本 Task 的两轮全量里都**没有出现**。

另外单独复跑了两条与本改动相邻的守卫，均通过：

```
$ dotnet test .../Dfo.Server.Protocol.Tests.csproj --nologo --filter "FullyQualifiedName~SelectCharacterAcceptedTranscriptTests"
已通过! - 失败:     0，通过:    11，已跳过:     0，总计:    11，持续时间: 127 ms - Dfo.Server.Protocol.Tests.dll (net10.0)
$ dotnet test .../Dfo.Server.Application.Tests.csproj --nologo --filter "FullyQualifiedName~CommandHandlerArchitectureTests"
已通过! - 失败:     0，通过:    12，已跳过:     0，总计:    12，持续时间: 35 ms - Dfo.Server.Application.Tests.dll (net10.0)
```

### 六、疲劳两个字段在帧上的值与基线相同的证据

帧上那两个数走的是一条**只有一个生产者、两个消费者**的通路，本 Task 三处都没碰。

1. **唯一生产者**（完整输出）：

```
$ grep -rn "new CharacterFatigueState" --include=*.cs Server/src/
Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs:147:                new CharacterFatigueState(188, 0),
```

2. **唯一消费者**（完整输出）：

```
$ grep -rn "\.Fatigue\." --include=*.cs Server/src/
Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Inventory.cs:47:        writer.Write(character.Fatigue.Total);
Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Inventory.cs:48:        writer.Write(character.Fatigue.Used);
```

3. 补一条不依赖点号的词边界枚举，确认没有第二种触达方式（例如 `with { Fatigue = ... }`）。完整
   输出：

```
$ grep -rn "Fatigue\b" --include=*.cs Server/src Server/tests | grep -v "FatiguePenalty\|FatigueUpdate\|FatigueDisplayUpdate\|FatigueAcceleration\|FatigueBuff\|FatigueBurn\|ConsumesFatigue\|NoFatigue\|InsufficientFatigue\|consumesFatigue"
Server/src/Dfo.Server.Data/Characters/CharacterModels.cs:176:    CharacterFatigueState Fatigue,
Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Inventory.cs:47:        writer.Write(character.Fatigue.Total);
Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Inventory.cs:48:        writer.Write(character.Fatigue.Used);
Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs:55:    /// separately; "目前无法使用物品" is the closest true thing it can say. Fatigue's <c>95</c> is
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:135:        var fatigue = new RecordingFatigue();
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:160:            new RecordingFatigue(),
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:195:        RecordingFatigue? fatigue = null) =>
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:206:            fatigue ?? new RecordingFatigue(),
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:305:    private sealed class RecordingFatigue : ICharacterFatigueDomainService
```

  这条 grep 的模式 `Fatigue\b` 按定义匹配不到 `CharacterFatigueState` / `CharacterFatigueBalance` /
  `ICharacterFatigueDomainService` 这类后面还跟字母的标识符，所以它证明的是**成员访问点**只有那
  两处；类型的构造点由第 1 条单独枚举。**两条合起来才是完整的**，任一条单独都不是。

4. **三个决定这两个数的文件，改动前后 sha256 逐字节相同**（前一次在本 Task 动手之前采集，后一次
   在全量判据通过之后采集，两次输出完全一致）：

```
ed0a0cef03634b64faa3a3079f39535e8d6ababc7475713626674d774db642e5  Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
f58c0c03de4be39ce3db6b14b649d88b0a07c423ccd06117ef4f5cb0b254ece8  Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Inventory.cs
04c14804358a6640c8d35dc0194f64037aae99861be15e8b18822a7374870bbe  Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
d86075dd5a9e478e75bc75abf00736d60676e8f1a92246ca1b2b200b655d4bf6  Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs
```

5. **帧字节本身有金标守着，且通过**：`SelectCharacterAcceptedTranscriptTests` 对入场批 62 帧逐帧
   比对 SHA256，其中第 1 帧就是携带这两个字段的 CMD `01/0004`。编码器
   `StateSynchronizationPayloadCodec.EncodeEntryAccepted` 的写入顺序是
   `byte 1` + `int 0` + `int createdAt` + `ushort characterId` + `ushort 0`，此后偏移 13 写
   `Fatigue.Total`、偏移 15 写 `Fatigue.Used`，与 `protocol/game/client-to-server/0x0004.md` 记的
   偏移 13 / 15 一致。该测试类 11 条全绿（第五节）。

**这份证据证明到哪一步，没证明什么**：第 5 条用的是判据夹具的 `(188, 5)`，证明的是"这两个字段在
帧上的位置与编码方式没变"；第 1 条证明"生产路径喂进去的仍是 `(188, 0)`"。两条合起来才是"帧上的
值与基线相同"。我**没有**跑真实客户端会话，也不声称跑过。

### 七、`git status --short` 与 `git diff --numstat` 原始输出，逐文件说明

```
$ git status --short
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

```
$ git diff --numstat
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

**本 Task 的改动集合只有 6 个文件**（3 个新建 + 3 个修改）：

| 文件 | 为何在改动集合里 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/Characters/ICharacterFatigueDomainService.cs`（新建，`??`） | 疲劳接缝本体：三个数 + 两种报告 |
| `Server/src/Dfo.Server.Application/Characters/UnaccountedCharacterFatigueDomainService.cs`（新建，`??`） | 不记账的占位实现 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/UnaccountedFatigueTests.cs`（新建，`??`） | CK-028 的两条用例 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs`（修改，numstat 由 `12 2` 变为 `17 2`） | 组合根接线，+5 行 |
| `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs`（修改，numstat 由 `13 6` 变为 `55 8`） | 进本处的调用、消耗常量、构造器参数，以及被本改动波及的两处既有注释（第九节） |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs`（修改，**但 numstat 里看不见它**） | CK-029 判据 + 记录式替身 + 构造器工厂。该文件是 TASK-06 新建、尚未 `git add`，因此在 `git status` 里是 `??`、在 `git diff --numstat` 里**根本不出现**。不说明这一点，numstat 会被误读成"本 Task 没改判据" |

**"其余文件不是我改的"这句话的机械证据**（不是靠印象）：本 Task 动手之前先采过一次
`git diff --numstat`，与上面这份逐行比对，**只有两行不同**，正是我改的那两个 tracked 文件：

```
rows only in current: set()
rows only in baseline: set()
DIFFERS: 12	2	Server/src/Dfo.Server.Application/ApplicationModule.cs -> 17	2	Server/src/Dfo.Server.Application/ApplicationModule.cs
DIFFERS: 13	6	Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs -> 55	8	Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
```

`git status` 里其余 29 个 `M` 与 13 个 `??` 全部是 TASK-01..06 的成果，本 Task 一个都没动；
`?? .rks-agent/tasks/character-state-finds-its-owner/` 是本 Spec 的 SDD 制品目录（spec / plan /
tasks），本 Task 只往其中的 TASK-07 文件末尾追加这两段。

`DungeonEntrySlice.cs` 的 `git diff -U2` 原始输出（含 TASK-06 的任务簿改动）：

```diff
diff --git a/Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs b/Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
index f037f999..192da8e5 100644
--- a/Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
+++ b/Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
@@ -1,7 +1,9 @@
 using System.Buffers.Binary;
 using Dfo.Server.Protocol.Game.DungeonSelection;
+using Dfo.Server.Abstractions.Characters;
 using Dfo.Server.Abstractions.Content;
 using Dfo.Server.Abstractions.Inventory;
 using Dfo.Server.Abstractions.Networking;
+using Dfo.Server.Abstractions.Quests;
 using Dfo.Server.Abstractions.Sessions;
 using Dfo.Server.Abstractions.World;
@@ -73,4 +75,10 @@ public sealed record SelectDungeonRefusal(byte ErrorCode, byte Context) : IGameC
 
     /// <summary>Not enough fatigue left to enter.</summary>
+    /// <remarks>
+    /// Nothing sends it. Entry reports what it costs to <see cref="ICharacterFatigueDomainService"/>
+    /// but nobody keeps that account and so nobody can find it short, which is a different state of
+    /// affairs from <see cref="DungeonUnavailable"/>: there the rule is known and unwritten, here
+    /// the balance this would be measured against does not exist yet.
+    /// </remarks>
     public const byte InsufficientFatigue = 0x16;
 
@@ -404,6 +412,6 @@ internal sealed class SelectDungeonDecoder : IGameMessageDecoder
 /// exists to learn whether the client accepts these three frames, which is the question every
 /// later piece depends on and none of which can be settled by reading the reference server. The
-/// run aggregate, the party that owns entry, and the cost a run charges all arrive after that
-/// answer.
+/// run aggregate and the party that owns entry arrive after that answer. So does the charging of
+/// what an entry costs: the cost is reported here, to a fatigue seam that keeps no account of it.
 ///
 /// No CMD reply. The reference server's success path produces none -- it delegates straight to the
@@ -415,7 +423,33 @@ public sealed class SelectDungeonHandler(
     ISelectedSessionRuntimeDirectory runtimeSessions,
     IDungeonVisitDomainService visits,
+    ICharacterQuestLogDomainService questLogs,
+    ICharacterFatigueDomainService fatigue,
     IGameMessageSender sender,
     ILogger<SelectDungeonHandler> logger) : ClientMessageHandler<SelectDungeonRequest>
 {
+    /// <summary>What one entry reports as spent.</summary>
+    /// <remarks>
+    /// <b>No dungeon declares a cost, so this is not read off one.</b> The archive says only
+    /// whether a dungeon charges fatigue at all -- its <c>[no fatigue]</c> tag, which reaches this
+    /// server as <see cref="DungeonDefinition.ConsumesFatigue"/> and which nothing in the server
+    /// reads: the only reader is the test pinning the parse. Neither
+    /// <see cref="DungeonDefinition"/> nor <see cref="DungeonLayout"/> carries a number of points,
+    /// and no other content or table holds one either.
+    /// <para>
+    /// What there is, is the charge itself: a point for each newly entered room that holds
+    /// monsters (<c>Docs/DungeonEntryNotes.md</c> section 8, dictated 2026-08-27). An entry walks
+    /// into exactly one room, the run's first, so an entry's cost is that one point.
+    /// </para>
+    /// <para>
+    /// Three things that would change it are deliberately not asked here, because each belongs to
+    /// the fatigue system rather than to this frame: whether that first room holds any monsters,
+    /// whether the character has stood in it before, and whether this dungeon charges at all. Until
+    /// then this is the ordinary case stated plainly rather than a computation, and it is a
+    /// placeholder in the same sense as the seam it is reported to -- what changes when fatigue is
+    /// built is what this number is worked out from, not that a report happens here.
+    /// </para>
+    /// </remarks>
+    public const ushort FatigueAnEntryCosts = 1;
+
     protected override async ValueTask HandleMessageAsync(
         NetworkDispatchContext context,
@@ -435,12 +469,17 @@ public sealed class SelectDungeonHandler(
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
@@ -464,4 +503,12 @@ public sealed class SelectDungeonHandler(
         var (layout, visit) = entered;
 
+        // Reported once the entry has actually happened, so that a refused selection reports
+        // nothing. What the seam does with it is nothing at all today -- see
+        // ICharacterFatigueDomainService -- which is why this is the report and not a check: there
+        // is no balance to be short of.
+        await fatigue
+            .SpendAsync(session.CharacterId, FatigueAnEntryCosts, cancellationToken)
+            .ConfigureAwait(false);
+
         logger.LogInformation(
             "Select dungeon {ConnectionId} user={WireUserId}: dungeon={DungeonId} difficulty={Difficulty} "
```

本 Task 在这个文件里的四段是：`using Dfo.Server.Abstractions.Characters;`、`InsufficientFatigue`
的 remarks、handler 的 remarks 一句 + 构造器参数 + `FatigueAnEntryCosts` 常量、以及 `SpendAsync`
调用。`using Dfo.Server.Abstractions.Quests;` 与 `questLogs` / `questLog.CarriedAsync` 那一整段是
**TASK-06** 的，不是本 Task 的。

### 八、BOM 与行尾检查（Python 读 bytes，不用 grep / sed / tail）

对本 Task 碰过或新建的**每一个**文件（6 个，不是抽样）：

```
$ python -c "<按字节统计 BOM / CRLF / LF / 孤立 CR / 末尾换行 / 非 ASCII 字节数>"
Server/src/Dfo.Server.Abstractions/Characters/ICharacterFatigueDomainService.cs           bytes=4127   BOM=False CRLF=0 LF=76  loneCR=0 trailingNL=True nonASCIIbytes=0
Server/src/Dfo.Server.Application/Characters/UnaccountedCharacterFatigueDomainService.cs  bytes=3461   BOM=False CRLF=0 LF=59  loneCR=0 trailingNL=True nonASCIIbytes=0
Server/src/Dfo.Server.Application/ApplicationModule.cs                                    bytes=22009  BOM=False CRLF=0 LF=316 loneCR=0 trailingNL=True nonASCIIbytes=0
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs                     bytes=34428  BOM=False CRLF=0 LF=716 loneCR=0 trailingNL=True nonASCIIbytes=0
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs     bytes=20579  BOM=False CRLF=0 LF=484 loneCR=0 trailingNL=True nonASCIIbytes=0
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/UnaccountedFatigueTests.cs       bytes=3089   BOM=False CRLF=0 LF=66  loneCR=0 trailingNL=True nonASCIIbytes=0
```

结论：**无 BOM、纯 LF、无孤立 CR、末尾有换行、全 ASCII**。

（`DungeonEntrySlice.cs` 的 bytes / LF 两个数字是在第九节那次收尾改注释**之前**采的；之后只增删了
注释文本，字符全部是 ASCII，BOM / 孤立 CR / 末尾换行三项结论不受影响。此处如实标注，不假装是
最终字节数。）

与仓库口径的对照：全仓 714 个 `.cs`（含 `obj/`、`bin/` 生成物）里只有 5 个带 BOM、36 个含 CRLF，
且都不是本 Task 碰的文件。`.editorconfig` 写的是 `end_of_line = crlf`，但工作区实际检出与既有源码
都是 LF（`git config core.autocrlf` = `false`），所以跟工作区实际口径走。这是既有分歧，本 Task 不
改它，如实记在这里。

本文件（TASK-07 的 md）本身：追加前为无 BOM、纯 LF；追加的内容同样是 LF。

### 九、被本改动波及的既有注释

1. `DungeonEntrySlice.cs` 的 `SelectDungeonHandler` remarks 原本写着 "The run aggregate, the party
   that owns entry, and **the cost a run charges** all arrive after that answer."——本 Task 之后进本
   处就报告消耗了，这句话变成半真。已改成："… So does the charging of what an entry costs: the cost
   is reported here, to a fatigue seam that keeps no account of it."（消耗**被报告**了；**被记账**
   这件事仍然在后头。）
2. `SelectDungeonRefusal.InsufficientFatigue` 原本只有一句 "Not enough fatigue left to enter."。新增
   了一段 remarks 说明它**仍然没有任何人发**，以及为什么：报告发生了但没人记账，所以没人能发现
   余额不足。不加这段，读代码的人看到新的 `SpendAsync` 调用会误以为入场已经被疲劳把门了。

**检查过、确认仍然成立、因而没有改的两处**：

- `Docs/DungeonEntryNotes.md:137`「现状：`CharacterRosterService.cs` 里写死
  `new CharacterFatigueState(188, 0)`，全仓没有任何地方递减它。」——两句都仍然为真：写死的值一个字
  没动，占位实现也不递减任何东西。（`Docs/` 也不在本 Task 的 permitted files 内。）
- `UseStackableSlice.cs:55-57`「this server has no fatigue model, and a code for a rule that is not
  enforced would be a lie in the other direction.」——仍然为真：本 Task 加的是接缝，不是模型；没有
  任何规则被执行。

### 十、诚实清单

- **`SelectDungeonHandler` 是消息 handler，不是命令处理器**，本 Task 往它的构造器里加了第五个领域
  服务。AGENTS.md「handler 业务语义规则」的四类允许清单里没有"注入领域服务"这一条；但这个 handler
  在本 Task 之前就已经注入了 `IDungeonVisitDomainService` 与 `ICharacterQuestLogDomainService`
  （后者是 TASK-06 加的），本次沿用同一形状。契约与 plan 的 BD-006 明确要求改动落在
  `DungeonEntrySlice.cs`，所以没有把它挪进命令。`CommandHandlerArchitectureTests` 不覆盖消息
  handler，也未被放宽，12 条全绿。
- **CK-028 的判据放在 `Protocol.Tests` 而不是 `Application.Tests`**，因为契约的 permitted files 只
  给了 `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/`。它测的是 Application 里的占位
  实现，靠既有的 `InternalsVisibleTo` 看见它，没有为此改 csproj。
- **接缝新引入了一个值类型 `CharacterFatigueBalance`**，两个字段与 `Dfo.Server.Data` 里既有的
  `CharacterFatigueState` 相同。没有复用后者，是因为接缝要答第三个数（剩余量），而 Data 里那个
  record 是帧的形状、且 `Data/` 不在 permitted files 内。两者的关系写在 `CharacterFatigueBalance`
  的 remarks 里，没有藏着。
- **`188` 与 `0` 现在在仓库里有两处字面量**（`CharacterRosterService.cs:147` 与占位实现的两个
  常量）。没有把 roster 那处改成引用常量，因为 `CharacterRosterService.cs` 不属于"其不记账的实现与
  组合根接线"。占位实现的 remarks 写明了这两处同源、疲劳系统落地时一起换掉。
- **没有建 Git 检查点、没有 commit、没有 stash。** 变异验证全部用原地改写 + 原地还原完成，还原由
  grep（exit code 1，无残留）与复跑（34 条全绿）双重确认。

## Executor Self-check 1

### 逐条对照 Required checks

| 判据 | 要求 | 结论 | 证据 |
|---|---|---|---|
| CK-026 | `dotnet build Server/86JP.Server.slnx --nologo` 零警告零错误 | **通过**：`0 个警告 / 0 个错误` | 第五节 |
| CK-027 | Non-command：不记账从类型名与文档即可读出 | **通过**：类型名 `UnaccountedCharacterFatigueDomainService`；类摘要、类 remarks 加粗句、两个方法 summary、接缝 remarks、组合根注释共六处直说 | 第二节 |
| CK-028 | 新增用例：报告一次消耗后再问已用量，答案未变 | **通过**：`A_reported_spend_moves_no_number_the_seam_answers` 与 `The_pair_it_answers_is_the_pair_the_entry_frame_carries`，分别由 M1 / M2 证明非空转 | 第四、五节 |
| CK-029 | 新增用例：进本时对着记录式替身断言报告发生且数对 | **通过**：`Entry_reports_what_it_costs_to_the_fatigue_seam` 对 `RecordingFatigue` 断言恰好一次报告、角色为该会话的角色、点数为 1；由 M3a（数对）与 M3b（发生）分别证明非空转 | 第四、五节 |
| CK-030 | 全量判据通过；疲劳两字段在帧上的值与基线相同 | **通过**：2067 / 2 / 0（基线 2064 / 2 / 0，+3 为本 Task 三条新用例）；两字段的唯一生产者与唯一消费者本 Task 未触碰（sha256 逐字节相同），入场批 62 帧金标 SHA256 全绿 | 第五、六节 |

### 逐条对照 Acceptance conditions

| 条件 | 结论 |
|---|---|
| 接缝能答三个数并接受两种报告 | **满足**：`BalanceOfAsync` 一次答出 `Total` / `Used` / `Remaining`；`SpendAsync`（用掉若干）与 `RefundAsync`（退回若干，为抗疲劳药剂准备）两种报告 |
| 实现不记账：报告一次消耗之后再问已用量，答案未变；继续报出协议今天报出的那两个值 | **满足**：两条用例分别断言"前后未变"与"答的是 188 / 0"，见第五节 CK-028 |
| 不记账这一点可从代码本身读出（类型名与文档直说），不必从行为反推 | **满足**：见第二节六处；两个报告方法的 summary 直接写 "Accepts the report and discards it" |
| 不建 store，不建表 | **满足**：新增文件里无 `*DomainStore` / `*Repository` / `*Table` / `*Persistence` 类型；`Persistence/Schema/` 未出现在本 Task 的改动集合里（第七节机械比对）；接缝的全部实现者只有占位类与判据替身（第二节 7 行完整枚举） |
| 进本处真的调用，报告的数取自该副本的消耗 | **前半满足；后半如实说明并偏离**。调用真的发生在 `SelectDungeonHandler` 成功进本之后（第三节，CK-029 守着）。报告的数是常量 1，**不是从该副本的档案里读出来的**：今天全仓不存在"每个副本消耗多少点"的值（第三节完整枚举——档案只有 `[no fatigue]` 布尔，`DungeonDefinition` / `DungeonLayout` / `character_condition` 都没有点数字段），而硬约束禁止本 Task 消费那个布尔。1 取自写下来的规则"进入一间新房间扣 1"（`Docs/DungeonEntryNotes.md` 第八节，用户 2026-08-27 口述），进本即走进第一间房。三件会改变这个数的事刻意留给疲劳系统，常量的 XML doc 逐条写明 |
| 全量判据全绿；疲劳两个字段在帧上的值与基线相同 | **满足**：2067 / 2 / 0；证据与其边界见第六节 |
| （硬约束）不启用 `InsufficientFatigue`、不消费 `ConsumesFatigue` | **满足**：`grep -rn "ConsumesFatigue\|consumesFatigue" --include=*.cs Server/src Server/tests` 的完整输出里，读它的只有解析它的内容源与钉住那次解析的 Infrastructure 判据，本 Task 一处没加；`InsufficientFatigue` 仍无任何发送点，只加了一段说明它为何仍不发的 remarks |
| （硬约束）不新增协议帧、不改帧字节布局 | **满足**：改动集合里没有 `Messages.cs` / codec / encoder / decoder / `ProtocolModule`；`DungeonEntrySlice.cs` 的改动全在 handler 与文档上（第七节 diff 可逐段核对） |
| （硬约束）分层 | **满足**：接缝在 `Abstractions`（只引用 `Data`），实现在 `Application`（只引用 `Abstractions` + `Data`），调用在 `Protocol`；方向仍是 `Protocol → Application`，无反向引用 |
| （硬约束）切片架构：切片内不跨协议引用、命令处理器禁止嵌套 dispatch | **满足**：`DungeonEntry` 切片新增的只是一个 `Abstractions` 接缝的注入，没有引用别的切片；本 Task 没有新增或修改任何 `ICommandHandler` |
| （硬约束）`CommandHandlerArchitectureTests` 继续通过、判定谓词与豁免清单不得放宽 | **满足**：该文件不在改动集合里；单独复跑 12 条全绿 |
| （硬约束）TreatWarningsAsErrors / CS9113 | **满足**：占位实现不用主构造器，不存在未读的主构造器参数；全量构建 0 警告 |
| （硬约束）不建 Git 检查点、不提交、不 stash | **满足**：全程未执行任何 `git commit` / `git stash` / `git checkout` |
| Required methods: test-driven-development | **部分满足，如实说**：两条新判据是随实现一起写的，判据先红后绿这一点由第四节的四次变异**事后**证明（每条判据都被证明会因它该抓的改动而红），而不是靠先写出一个红的判据。若审查要求严格的先红后绿顺序证据，我没有 |

### 我认为审查时最该盯的两处

1. **报告的数是常量 1，不是从副本档案取的。** 契约字面是"报告的数取自该副本的消耗"，Task 指令
   同时授权"若消耗值今天无处可取，就如实说明你报的是什么数、为什么"。我选了后者并给了完整枚举。
   若审查认为必须依赖 `ConsumesFatigue` 才算"取自该副本"，那与"不消费 `ConsumesFatigue`"这条硬
   约束直接冲突，需要所有者裁决。
2. **`ApplicationModule.cs` 严格说落在 permitted files 的 `Characters/` 目录之外。** 它是仓库里
   唯一的领域服务注册点，契约的"组合根接线"四个字指的就是它；但路径不在目录内，我没有掩饰。

## Independent Quick Review 1

- Verdict: **passed**
- Reviewed contract: Contract revision 1（无 amendment）；对照 Execution Result 1 与 Executor Self-check 1
- Owned diff identity: 6 个文件 —— 新建 `ICharacterFatigueDomainService.cs`、
  `UnaccountedCharacterFatigueDomainService.cs`、`UnaccountedFatigueTests.cs`；修改
  `ApplicationModule.cs`、`DungeonEntrySlice.cs`、`SelectDungeonHandlerTests.cs`（后者未跟踪）
- 评审者上下文：全新独立上下文，未参与实现；下列所有结论由本人重跑命令得出，未采信自陈

### 一、判据复跑（本人执行）

```
$ dotnet build Server/86JP.Server.slnx --nologo
已成功生成。
    0 个警告
    0 个错误
```

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
已通过! - 失败: 0，通过: 508，已跳过: 0，总计: 508 - Dfo.Server.Protocol.Tests.dll
已通过! - 失败: 0，通过:  52，已跳过: 0，总计:  52 - Dfo.Server.Networking.Tests.dll
已通过! - 失败: 0，通过:  60，已跳过: 0，总计:  60 - Dfo.Pvf.Tests.dll
已通过! - 失败: 0，通过:   6，已跳过: 0，总计:   6 - Dfo.Server.Host.Tests.dll
已通过! - 失败: 0，通过: 256，已跳过: 0，总计: 256 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败: 0，通过: 223，已跳过: 2，总计: 225 - Dfo.Server.Infrastructure.Tests.dll
已通过! - 失败: 0，通过: 962，已跳过: 0，总计: 962 - Dfo.Server.Application.Tests.dll
```

508+52+60+6+256+223+962 = **2067 通过 / 2 跳过 / 0 失败**，与执行者报告的计数及**逐项目归因**
完全一致。两个既知偶发本轮均未出现。

### 二、逐条核实（八件）

**1. `1` 的来源如实。核实通过。**

(a) `Docs/DungeonEntryNotes.md:135` 标题确为「八、疲劳值规则（用户 2026-08-27 口述，尚未实现）」，
表内第一行确为「何时扣 | **进入一间「新房间」时扣 1**」。文档确实这么写。

(b) 本人重跑 `grep -rni "fatigue" --include=*.cs --include=*.sql --include=*.json Server/src`，
输出 **84 行**，与执行者所称行数一致；逐行读完，无任何「每副本消耗点数」的值。另外独立读完
`DungeonDefinition`（8 个字段）与 `DungeonLayout`（11 个字段）两个 record 全文，均无消耗字段；
`character_condition` 建表语句只有 `fatigue_display_update` / `fatigue_penalty` /
`fatigue_update` / `stamina`。仓库顶层只有 `ClientHack/ Docs/ Server/ Tool/ protocol/`，
`find . -name DungeonEntryHandler.cs` 无结果——**仓内不存在上游参考服务端源码**，故也不存在
「从上游取值」这条更好来源。

披露恰当：唯一的替代来源是 `[no fatigue]` 布尔，而它被硬约束明令不得消费；执行者正是把这个
冲突原样摆出来请所有者裁决，没有掩饰，也没有假装 1 是从档案读来的。**未发现被漏掉的更好来源。**

**2. CK-027「不记账可从代码读出」。核实通过。**

本人读完两个新建生产文件全文，六处直说全部属实：类型名 `Unaccounted...`；类摘要首句
`keeps no account`；类 remarks 加粗 `<b>Nothing reported to this is recorded anywhere, by
design.</b>`；两个报告方法各自 `Accepts the report and discards it: nothing is ... and nothing
is written.`；接缝 remarks 加粗 `<b>A seam ahead of the system behind it: fatigue is not
implemented.</b>`；组合根注释 `records nothing, which is what its name says`。
按「读代码的人不必从行为反推」这个标准衡量：**够**——只读类型名一项即已足够，其余五处是冗余。

**3. 不建 store、不建表。核实通过。**

本人重跑接缝枚举，**7 行，与执行者所报逐字一致**（唯一差异：`DungeonEntrySlice.cs` 的 505→507，
源于执行者自己披露的收尾改注释，不影响结论）：

```
$ grep -rn "ICharacterFatigueDomainService" --include=*.cs Server/src Server/tests
Server/src/Dfo.Server.Abstractions/Characters/ICharacterFatigueDomainService.cs:58:public interface ICharacterFatigueDomainService
Server/src/Dfo.Server.Application/ApplicationModule.cs:233:            ICharacterFatigueDomainService, UnaccountedCharacterFatigueDomainService>();
Server/src/Dfo.Server.Application/Characters/UnaccountedCharacterFatigueDomainService.cs:33:internal sealed class UnaccountedCharacterFatigueDomainService : ICharacterFatigueDomainService
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:78:    /// Nothing sends it. Entry reports what it costs to <see cref="ICharacterFatigueDomainService"/>
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:426:    ICharacterFatigueDomainService fatigue,
Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:507:        // ICharacterFatigueDomainService -- which is why this is the report and not a check: there
Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:305:    private sealed class RecordingFatigue : ICharacterFatigueDomainService
```

实现者只有占位类与判据替身。事务形状与建表检查（本人执行）：

```
$ grep -n "DomainStore\|Repository\|Table\|Persistence\|IDomainStoreSession\|CREATE TABLE\|Sqlite\|Connection\|Transaction" <3 个新建文件>
grep exit=1        （无任何匹配）

$ git status --short -- Server/src/Dfo.Server.Persistence/Schema/
（空）

$ git status --short | grep -i csproj
（空）
```

无新表、无事务形状、无 csproj 改动。

**4. 疲劳两字段在帧上与基线相同。核实通过，且已把 TASK-06 的干扰区分开。**

- 唯一消费者 `StateSynchronizationPayloadCodec.Inventory.cs` **根本不出现在 `git status --short`
  里**——与 HEAD 逐字节相同，本 Task 未触碰。
- 唯一生产者 `CharacterRosterService.cs` 的 numstat `1 2`，本人读其**完整 diff**：只有一个 hunk
  `@@ -149,8 +149,7 @@`，内容是删 `DarkKnightCombo: null)`、把 `...CreatedAtUnixSeconds(),`
  改成 `...CreatedAtUnixSeconds());`——**确为 TASK-06 的成果**。
  `new CharacterFatigueState(188, 0)` 在第 147 行，落在该 hunk 之外，未被改动。
- 附带确认 `SelectCharacterPayloadCodecTests.cs:800` 的 `new CharacterFatigueState(188, 5)` 在其
  （同属 TASK-06 的）diff 里同样是上下文行。
- 入场批 62 帧金标 SHA256 随 Protocol.Tests 508 条全绿一并通过。

**5. 变异复现。M1 与 M3a 均由本人重做，报红的正是执行者所说的那几条。**

M1（`private ushort mutantUsed;`，`SpendAsync` 累加它，`BalanceOfAsync` 答它）：

```
[FAIL] UnaccountedFatigueTests.The_pair_it_answers_is_the_pair_the_entry_frame_carries
[FAIL] UnaccountedFatigueTests.A_reported_spend_moves_no_number_the_seam_answers
   Assert.Equal() Failure: Values differ
Expected: 0
Actual:   7
     at ...UnaccountedFatigueTests.cs:line 60
     at ...UnaccountedFatigueTests.cs:line 34
失败!  - 失败:     2，通过:     0，已跳过:     0，总计:     2 - Dfo.Server.Protocol.Tests.dll (net10.0)
```

M3a（`Assert.Equal((ushort)1, spent.Points)` → `(ushort)2`）：

```
[FAIL] SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam
   Assert.Equal() Failure: Values differ
Expected: 2
Actual:   1
     at ...SelectDungeonHandlerTests.cs:line 142
失败!  - 失败:     1，通过:     5，已跳过:     0，总计:     6 - Dfo.Server.Protocol.Tests.dll (net10.0)
```

两次均与执行者所报的**失败条目、断言消息、行号、计数**完全一致。

还原验证（本人执行）：

```
$ sha256sum <两个待变异文件>            # 变异前采集
8bb91ab93bd7909a4995e8a2fff05a3e15ebe9303c048b9c1701f42a22305d16  UnaccountedCharacterFatigueDomainService.cs
2df9f9fa0ad66acdee872003365c0eefb960db84df2a1471ff88c49e6573b948  SelectDungeonHandlerTests.cs

$ sha256sum <同两个文件>                # 全部还原后：两值一字未变
8bb91ab93bd7909a4995e8a2fff05a3e15ebe9303c048b9c1701f42a22305d16  UnaccountedCharacterFatigueDomainService.cs
2df9f9fa0ad66acdee872003365c0eefb960db84df2a1471ff88c49e6573b948  SelectDungeonHandlerTests.cs

$ grep -n "mutant\|ushort)2, spent" <同两个文件>
grep exit=1        （无残留）

$ dotnet test .../Dfo.Server.Protocol.Tests.csproj --nologo --filter "FullyQualifiedName~DungeonEntry"
已通过! - 失败:     0，通过:    34，已跳过:     0，总计:    34 - Dfo.Server.Protocol.Tests.dll (net10.0)
```

`git status --short` 与 `git diff --numstat` 的 md5 在变异前后一致（`4a19c3e8…` / `85ec28ab…`）。

**未抢做 TASK-08 的活**：四次变异中 M1/M2 改的是**接缝实现**，M3a/M3b 改的是**判据自身期望**，
无一改动 `DungeonEntrySlice.cs` 里那处 `SpendAsync` 调用。TASK-08 的专职未被侵占。

**6. 边界：拒绝码未启用、档案标签未消费。核实通过。**

```
$ git show HEAD:.../DungeonEntrySlice.cs | grep -n "InsufficientFatigue"
75:    public const byte InsufficientFatigue = 0x16;

$ grep -n "InsufficientFatigue" .../DungeonEntrySlice.cs        # 工作区
83:    public const byte InsufficientFatigue = 0x16;

$ git diff -U0 -- .../DungeonEntrySlice.cs | grep -E "^[+-]" | grep -i fatigue
（11 条全部为 + 行，无一条 - 行；无一条含 "InsufficientFatigue = 0x16"）
```

`const byte` 那行 HEAD 与工作区**逐字节相同**，新增的只是 remarks。全仓 `InsufficientFatigue`
枚举：一处声明 + 一处文档交叉引用 + 两份 protocol `.md` 表格，**零发送点**。
`ConsumesFatigue` 全量枚举：生产方 `PvfDungeonDefinitionSource` 四处，读者只有
`PvfDungeonDefinitionSourceTests.cs:32`（钉住解析）与一处新增文档注释——**仍是生产零消费**。

**7. 证据局限披露是否成立。成立；并已由本人用独立手段补上其缺口。**

本人确认 `SelectDungeonHandlerTests.cs` 在 `git status --short` 中为 `??`、在
`git diff --numstat` 中**确实不出现**，所以该披露必要且属实——不写会让 numstat 被误读成
「本 Task 没改判据」。numstat 前后逐行比对作为替代证据**对 tracked 文件成立**，但按其构造
**无法约束另外 12 个未跟踪源文件**。本人用 mtime 独立封住这个缺口：

```
$ git status --short | sed 's/^...//' | grep -v '^\.rks-agent' | while read f; do stat -c "%y  %n" "$f"; done | sort     （尾部）
2026-09-04 05:31:50  Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs
2026-09-04 05:49:29  Server/src/Dfo.Server.Abstractions/Characters/ICharacterFatigueDomainService.cs
2026-09-04 05:49:57  Server/src/Dfo.Server.Application/ApplicationModule.cs
2026-09-04 05:52:53  Server/tests/.../DungeonEntry/UnaccountedFatigueTests.cs
2026-09-04 05:54:12  Server/src/Dfo.Server.Application/Characters/UnaccountedCharacterFatigueDomainService.cs
2026-09-04 05:54:41  Server/tests/.../DungeonEntry/SelectDungeonHandlerTests.cs
2026-09-04 05:57:03  Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
```

全改动集合里**最新的 6 个文件恰是本 Task 声称的那 6 个**（05:49:29 – 05:57:03），与前一个文件
（05:31:50，TASK-05/06 的 combo store）之间有干净断层。tracked 与 untracked 两侧的边界主张
因此都成立。

**8. 常规。核实通过。**

- **无夹带**：`git status --short`（31 个 `M` + 19 个 `??`）与 `git diff --numstat`（31 行）本人
  重跑，与执行者记录的两份原始输出**逐行一致**，也与其所述 TASK-06 收口状态一致。
- **BOM 与行尾**（Python 读 bytes，未用 grep / sed / tail）：

```
ICharacterFatigueDomainService.cs            bytes=4127  BOM=False CRLF=0 LF=76   loneCR=0 trailingNL=True nonASCII=0
UnaccountedCharacterFatigueDomainService.cs  bytes=3461  BOM=False CRLF=0 LF=59   loneCR=0 trailingNL=True nonASCII=0
ApplicationModule.cs                         bytes=22009 BOM=False CRLF=0 LF=316  loneCR=0 trailingNL=True nonASCII=0
DungeonEntrySlice.cs                         bytes=34598 BOM=False CRLF=0 LF=718  loneCR=0 trailingNL=True nonASCII=0
SelectDungeonHandlerTests.cs                 bytes=20579 BOM=False CRLF=0 LF=484  loneCR=0 trailingNL=True nonASCII=0
UnaccountedFatigueTests.cs                   bytes=3089  BOM=False CRLF=0 LF=66   loneCR=0 trailingNL=True nonASCII=0
```

  六个文件全部：无 BOM、纯 LF、无孤立 CR、末尾有换行、全 ASCII。`DungeonEntrySlice.cs` 的
  34598 / 718 与执行者记的 34428 / 716 不同，**正是执行者自己声明过的**「该数字采于收尾改注释
  之前」——四项结论不受影响，其标注属实。
- **既有判据期望值**：5 条既有用例按名全部在位（`Entry_asks_the_quest_log_which_quests_are_carried`
  / `..._cleared` / `The_answer_to_an_entry_is_what_it_was` /
  `An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy` /
  `Without_a_published_session_nothing_is_entered`），本 Task 只做了执行者已披露的构造器实参与
  工厂机械改动；全量 2067 = 2064 + 3、两个被碰判据文件 `[Fact]` 计数 6 与 2，算术上与「无用例被
  删或改名」自洽。该文件未跟踪，无法做字节级证明；未发现任何相反证据。
- **两处既有注释改得对；第三处本人另行搜索后未发现**——
  `Docs/DungeonEntryNotes.md:137`「全仓没有任何地方递减它」仍真（占位是丢弃而非递减）；
  `UseStackableSlice.cs:55-57`「this server has no fatigue model」仍真（接缝不是模型，无规则被
  执行）；另查 `Docs/ProtocolScaffoldDissonances.md:230`「判定没有：等级、疲劳、入场道具、每日
  次数一条都不查」亦仍真。

### 三、硬约束

| 约束 | 结论 | 证据 |
|---|---|---|
| 不新增协议帧、不改帧字节布局 | 通过 | 改动集合中无 codec / `Messages.cs` / Encoder / Decoder / `ProtocolModule`；Protocol.Tests 508 条（含入场批 62 帧金标 SHA256）全绿 |
| `CommandHandlerArchitectureTests` 通过且未放宽 | 通过 | 该文件不在 `git status` 中，mtime `2026-09-03 23:56`，早于本 Spec 全部工作窗口（00:26–05:57）；Application.Tests 962 条全绿 |
| 分层未违反 | 通过 | `Dfo.Server.Abstractions.csproj:8` **本就**引用 `Dfo.Server.Data`；无任何 csproj 改动 |
| 切片内不跨协议引用、命令处理器未嵌套 dispatch | 通过 | `DungeonEntry` 只新注入一个 `Abstractions` 接缝；无新增或修改的 `ICommandHandler` |
| 未建检查点、未提交、未 stash | 通过 | `git log -1` = `427331bcb07f60121c97920287ac341c9cb14dee`；`git stash list` 为空 |

### 四、非阻断观察（不影响 verdict，供执行者与最终评审参考）

- **O-1（记载准确性）**：第七节表格与 Self-check 均称 `SelectDungeonHandlerTests.cs`「是
  **TASK-06** 新建」「原有 5 条（**TASK-06** 交付）」。实为 **TASK-03** 新建——
  `TASK-03-quest-read-point-asks-the-log.md:38`「新增，5 条用例。这条路径此前**零判据**」，
  同文件 :481「`?? .../SelectDungeonHandlerTests.cs` | **本任务** | 基线 `git status` 的 6 个
  `??` 里没有它，现为第 7 个」；TASK-04 / 05 / 06 仅在各自粘贴的 `git status` 输出里出现该文件名。
  承重事实（未跟踪、既有 5 条）均属实且已由本人独立核对，仅归属写错，建议订正这两句。
- **O-2（判据覆盖）**：调用点注释称 "Reported once the entry has actually happened, so that a
  refused selection reports nothing"，但**没有任何判据钉住后半句**。
  `SelectDungeonHandlerTests.cs:151-169` 的 `Without_a_published_session_nothing_is_entered`
  把 `new RecordingFatigue()` 内联进构造器、未持有引用，因此无法断言 `Assert.Empty(fatigue.Spent)`；
  `entered is null` 那条拒绝分支则完全没有判据。本契约的 CK-029 只要求「报告发生且数对」，故不
  构成缺陷，但这是 TASK-08 或最终评审最该看的一处。
- **O-3（未标源的旁证）**：`ICharacterFatigueDomainService.cs:46-50` 称「所有者给出日总量 156
  外加黑钻 32，由疲劳药剂恢复、每日重置；156 + 32 即实测的 188」。本人在 `Docs/` 与 `protocol/`
  下搜 `156` / `黑钻` / `black diamond` / `抗疲劳` / `疲劳药`，**`DungeonEntryNotes.md` 第八节
  未记载其中任何一项**；仓内有出处的只有 188 本身（`protocol/game/client-to-server/0x0004.md:73`
  与 `:209`「疲劳上限（实测 **188**）」）。该分解式在仓内无可引证的来源。
- **O-4（把已披露的偏离说到最锐处）**：CK-029 的夹具副本是 `TutorialDungeonId = 10000`，而
  `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfDungeonDefinitionSourceTests.cs:32`
  正钉着该副本 `Assert.False(dungeon.ConsumesFatigue)`——按写下来的规则（`DungeonEntryNotes.md:152`
  「教学本就是不耗疲劳的」），**教程本一次进本的消耗是 0 而不是 1**。今天无可观察后果（不记账），
  且常量的 XML doc 已把「这个副本到底扣不扣」列入刻意不问的三件事，但所有者裁决时应当看到这个最
  具体的形态。

### 五、结论

八件核实项全部通过；两条判据变异（M1、M3a）由本人独立复现，报红条目、断言消息、行号与计数与执行者
所报完全一致，且已确认全部还原、无残留、工作区状态未变；全量判据由本人重跑得 2067 / 2 / 0，逐项目
归因吻合。未发现可局部修复的阻断性缺陷。

**Verdict: `passed`。**

本 verdict 仅证明这一个 Task Unit 的有限缺陷检查通过，不代表完整变更评审，也不替代接受前的新鲜验证。
第二节第 1 条那处偏离（报告的数是常量 `1`，非取自副本档案）**仍需所有者裁决**——本人核实其披露属实
且完整，并确认在「不消费 `ConsumesFatigue`」这条硬约束下，仓内不存在更好的来源。
