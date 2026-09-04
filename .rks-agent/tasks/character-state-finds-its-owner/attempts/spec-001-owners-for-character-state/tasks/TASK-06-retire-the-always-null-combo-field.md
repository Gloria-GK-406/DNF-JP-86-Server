# TASK-06 — 那个恒为 null 的单数连招字段退役

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 角色活动态上那个恒为 null 的单数连招字段及其类型从服务器里消失，写 null 的那一处一并去掉。
- Referenced spec requirements: RQ-007
- Referenced plan sections: T-006；BD-012
- Owned boundary: 该字段、它的类型、以及写 null 的那一处。
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs`
  - `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs`
  - 因该字段消失而需要更新符号的其他位置
- Dependencies: TASK-05
- Required dependency outputs: TASK-05 的连招实例已经是连招页的主人，旧字段才可以拆
- Acceptance conditions:
  - 该字段与其类型的标识符全仓计数为 0
  - 全量判据全绿，行为逐值不变
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: test-driven-development
- Required checks: CK-023, CK-024, CK-025
- Required evidence:
  - 两个标识符的全仓计数
  - 全量判据输出

## Contract Amendment 1 — 控制器追加的第二件（来自 TASK-05 的评审）

TASK-05 的独立快审在核实 `Job == 9` 那处披露时，发现新 store 里有一处**写读不对称**，评审判为
非阻断（今天没有写入方）。控制器把它折进本 Task 而不是单开一轮，理由是本 Task 体量小且同属
连招主题，且**这是新代码里能存进去却读不出来的数据**——不该靠文档记着它。

**追加交付物：消除连招 store 的写读长度不对称。**

已核实的事实：行网关的**读**路径对 body 长度有 64 KiB 上界（`EntryBlob.Combo`），而 TASK-05 新写的
`ComboPageSection.Write` **只校验页号、不校验长度**。于是一次超长的写入会被接受，随后的读必然被拒。
同一个类里页号有守卫、长度没有，两种同类问题处理方式不一致。

要求：
- 让写路径对 body 长度的态度与读路径一致
- 加一条判据钉住它（超长 body 的写入被拒，而不是写进去之后读不出来）
- 做一次变异验证证明该判据会红

**本 Task 原有的交付物不变**：那个恒为 null 的单数连招字段仍要退役。

## Execution Result 1

### 一、两件交付物各自改了什么

#### 交付物一：退役恒为 null 的单数连招字段

四处改动，全部是「类型消失」带来的符号更新，无一处改动既有期望值或断言：

| 文件 | 改了什么 |
|---|---|
| `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs` | 删掉 `public sealed record DarkKnightComboState(IReadOnlyList<uint> SkillIds);`（原 `:173`）；从 `ActiveCharacterState` 的位置参数表里删掉 `DarkKnightComboState? DarkKnightCombo`（原 `:181`）。该 record 由 6 个位置参数变为 5 个 |
| `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs` | 删掉 `FindByAccountAndSlotAsync` 里显式的 `DarkKnightCombo: null`（原 `:153`），右括号并到上一行 |
| `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs` | 构造 helper `Character(int, ushort)` 的目标类型 `new(...)` 末尾那个 `null` 实参删掉（原 `:642`） |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs` | `new ActiveCharacterState(...)` 末尾那个 `null` 实参删掉（原 `:803`） |

**构造点枚举的完整性**（不是从样本推全体）：`ActiveCharacterState` 在全工作区（排除 `.git`/`bin`/`obj`）
共 93 处提及，其中构造点用 `grep -rn "new ActiveCharacterState("` 得 2 处，加上目标类型 `new(...)`
的 1 处（`CharacterEntryInventoryTests.cs:637` 的 `private static ActiveCharacterState Character(...) => new(`），
共 3 处。这 3 处全部改了。**完整性由编译器兜底**：删掉一个 record 的位置参数会改变构造器 arity，
任何漏改的构造点都是 CS 错误；CK-023 零错误零警告即证明枚举无遗漏（见下方 CK-023 输出）。

原始命令与输出：

```
$ grep -rn "new ActiveCharacterState(" . --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj
./Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs:139:            : new ActiveCharacterState(
./Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs:797:        var character = new ActiveCharacterState(
```

四处的完整 `git diff`（原始输出，未裁剪）：

```
diff --git a/Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs b/Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
index 986e13bf..9c4a7dce 100644
--- a/Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
+++ b/Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
@@ -149,8 +149,7 @@ internal sealed class CharacterRosterService(
                 // The client reads this as the character's creation time and the entry ACK carries
                 // it; a working A21 session sends a real Unix timestamp here, so a fixed epoch is a
                 // zero on the wire rather than an unset field.
-                DateTimeOffset.FromUnixTimeSeconds(row.CreatedAtUnixSeconds),
-                DarkKnightCombo: null);
+                DateTimeOffset.FromUnixTimeSeconds(row.CreatedAtUnixSeconds));
     }
 
     public ValueTask<bool> NameExistsAsync(byte[] name, CancellationToken cancellationToken)
diff --git a/Server/src/Dfo.Server.Data/Characters/CharacterModels.cs b/Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
index 8d2997fd..1b52701c 100644
--- a/Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
+++ b/Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
@@ -170,15 +170,12 @@ public sealed record CharacterPositionState(
     short Y,
     byte Direction);
 
-public sealed record DarkKnightComboState(IReadOnlyList<uint> SkillIds);
-
 public sealed record ActiveCharacterState(
     CharacterIdentityState Identity,
     CharacterProgressionState Progression,
     CharacterFatigueState Fatigue,
     CharacterPositionState Position,
-    DateTimeOffset CreatedAt,
-    DarkKnightComboState? DarkKnightCombo);
+    DateTimeOffset CreatedAt);
 
 public sealed record CharacterAppearanceCompatibilityState(
     byte FormatVersion,
diff --git a/Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs b/Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs
index 7340f31a..7c04bca1 100644
--- a/Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs
+++ b/Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs
@@ -639,8 +639,7 @@ public sealed class CharacterEntryInventoryTests
         new CharacterProgressionState(10, 100),
         new CharacterFatigueState(156, 0),
         new CharacterPositionState(1, 1, 10, 20, 0),
-        DateTimeOffset.UnixEpoch,
-        null);
+        DateTimeOffset.UnixEpoch);
 
     private static TestInventoryEntryState EmptyInventory(AccountId accountId, CharacterId characterId) => new(
         new CharacterInventoryEntryState(
diff --git a/Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs b/Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs
index 89f3e10f..5020b440 100644
--- a/Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs
+++ b/Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs
@@ -799,8 +799,7 @@ internal static class SelectCharacterPayloadTestData
             new CharacterProgressionState(60, 100),
             new CharacterFatigueState(188, 5),
             new CharacterPositionState(1, 2, 3, 4, 5),
-            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
-            null);
+            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
         return new SelectCharacterResult(
             character,
             new CharacterEntryState(
```

#### 交付物二：消除连招 store 的写读长度不对称

两处改动（都在 TASK-05 新建的、Git 尚未跟踪的文件里，因此不出现在 `git diff --numstat`，
见下方 `git status` 里的 `??` 行）：

1. `Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs`
   —— `ComboPageSection.Write` 在既有页号守卫之后新增一条 body 长度守卫，形式与页号守卫同款
   （`ArgumentOutOfRangeException(nameof(value), 实际值, 说明)`）。最终代码：

```csharp
    public override void Write(OwnedCharacterId place, DarkKnightComboEntryState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.PageIndex > HighestPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.PageIndex,
                "A combo page the entry load will not carry is one no character could enter with.");
        }

        // The bound the read of these rows already applies: ReadRowsAsync below asks the gateway,
        // and the gateway refuses a body longer than EntryBlob.Combo where it reads the column. A
        // longer body would therefore settle as a row that every later read of this character's
        // pages throws on -- a page written and then not readable, rather than a page written
        // wrongly. Refused here for the same reason the page number is: raised while flushing, it
        // would take down the statements other kinds of state have already written.
        if (value.EncodedBody.Length > EntryBlob.Combo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.EncodedBody.Length,
                "A combo page body the read of these rows will not return is one no character could enter with.");
        }

        base.Write(place, value);
    }
```

   守卫**引用读路径的同一个常量 `EntryBlob.Combo`**，而不是抄一个 65536 的字面量——要钉的是
   「两端一致」，不是「两端都等于某个数」。

2. `Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs`
   —— 新增两条判据，放在既有的两条「页号越界被拒（写/读各一）」旁边：

```csharp
    /// <remarks>
    /// A body past what the read of these rows allows is the same kind of unusable page as a page
    /// number the entry load will not carry, and is turned away the same way. The read bound is
    /// <see cref="EntryBlob.Combo"/>, applied by the gateway this store reads its rows through; a
    /// write that took a longer body would settle a row that every later read of this character's
    /// pages then threw on.
    ///
    /// Named by the read's bound rather than by a number, because what is being pinned is that the
    /// two agree, not what they happen to agree on.
    /// </remarks>
    [Fact]
    public async Task ABodyTheReadWouldRefuseIsRefusedWhenWritten()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await fixture.Book().SetPageAsync(
                new DarkKnightComboEntryState(0, new byte[EntryBlob.Combo + 1]),
                CancellationToken.None));
        Assert.Empty(await fixture.StoredPagesAsync());
    }

    /// <remarks>
    /// And no tighter than the read either. A body of exactly the bound is one the read hands
    /// back, so refusing it on the way in would be the same asymmetry the other way round.
    /// </remarks>
    [Fact]
    public async Task ABodyExactlyAtTheReadsBoundIsSettledAndReadBack()
    {
        await using var fixture = await Fixture.CreateAsync();
        var body = new byte[EntryBlob.Combo];
        // Not all zeroes, and marked at the far end, so a body truncated anywhere fails.
        body[0] = 0x5A;
        body[^1] = 0xA5;

        await fixture.Book().SetPageAsync(
            new DarkKnightComboEntryState(0, body), CancellationToken.None);
        await fixture.SettleAsync();

        var page = await fixture.Book().FindPageAsync(0, CancellationToken.None);
        Assert.NotNull(page);
        Assert.Equal(body, page.EncodedBody);
    }
```

   第一条钉「超长 body 的写入被拒，且没有落任何行」；第二条钉「不比读更严」——恰好等于上界的
   body 写得进、结算得了、读得回来。少了第二条，把守卫写成 `>=` 就是把不对称换了个方向而无人发觉。

### 二、写读不对称的核实（先核实，再照做）

契约增补里的描述**逐项核实为真**，没有一处需要推翻，故未走阻塞路径：

| 断言 | 核实到的确切位置与形式 |
|---|---|
| 读路径对 body 长度有 64 KiB 上界 | `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs:17-21`：`internal static class EntryBlob { ... public const int Combo = 64 * 1024; ... }` |
| 上界叫 `EntryBlob.Combo` 且作用在连招页的读上 | 同文件 `:126`：`EntryBlob.Bounded(reader, 1, EntryBlob.Combo, "Dark Knight combo", allowNull: false)`，位于 `SqliteCharacterComboPageTable.ReadAsync`（`:115-128`），即 `ICharacterComboPageTable` 的唯一实现 |
| 超界的形式是拒绝而非截断 | 同文件 `:33-36`：`var value = (byte[])reader.GetValue(ordinal); return value.Length > maximum ? throw new InvalidDataException($"Character {name} exceeds {maximum} bytes.") : value;` |
| store 的读确实经过这个网关 | `SqliteDarkKnightComboDomainStore.cs` 的 `ComboPageSection.ReadRowsAsync`：`var rows = await gateway.ReadAsync(place.Character, cancellationToken)`，`gateway` 即 `ICharacterComboPageTable`；类注释 `:21-27` 明说「Its reads are the entry load's reads」 |
| `ComboPageSection.Write` 只校验页号、不校验长度 | 改动前的 `Write`（`:153-165`）只有 `ArgumentNullException.ThrowIfNull(value)` 与 `if (value.PageIndex > HighestPage) throw new ArgumentOutOfRangeException(...)`，此外直接 `base.Write(place, value)` |
| 「同一个类里页号有守卫、长度没有」 | 是。`HighestPage = 1`（`:149`）有守卫，`EncodedBody.Length` 无 |
| 后果是「存得进读不出」 | 是，且分两段：同一 use case 内 `SqliteOwnedCharacterSection.ReadAsync/ReadAllAsync` 先查 staged 覆盖层，读得到自己刚写的；`FlushAsync` 落库后，任何**后续** use case 的读都走 `ReadRowsAsync` → 网关 → `EntryBlob.Bounded`，必抛 `InvalidDataException` |

**取的是 `>` 而不是 `>=`**：读路径的判定是 `value.Length > maximum`，所以恰好 65536 字节是读**允许**的，
写路径若用 `>=` 就会比读更严。这一点由第二条新判据钉住。

### 三、变异验证记录（每条判据各由哪次变异证明非空转）

两条新判据，两次变异，**互不覆盖对方**（每次变异恰好杀死一条，另一条仍绿）：

**M1 —— 删掉整条长度守卫**（即回到本 Task 之前的 `Write`）。
这次变异等价于 TDD 的 red 阶段，实际发生过两次：一次是加守卫之前的首跑，一次是加完守卫后专门回退复现。
后者的原始输出：

```
$ python   # 把 if (value.EncodedBody.Length > EntryBlob.Combo) { ... } 整块删除
M1 applied: length guard deleted
$ dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter "FullyQualifiedName~DarkKnightComboBookTests"
[xUnit.net 00:00:00.47]     Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ABodyTheReadWouldRefuseIsRefusedWhenWritten [FAIL]
  失败 Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ABodyTheReadWouldRefuseIsRefusedWhenWritten [31 ms]
失败!  - 失败:     1，通过:    14，已跳过:     0，总计:    15，持续时间: 498 ms - Dfo.Server.Application.Tests.dll (net10.0)
```

首跑（守卫尚未写入时）的错误消息，证明它红在正确的原因上而非编译或环境：

```
  错误消息:
   Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.ArgumentOutOfRangeException)
```

→ **M1 证明 `ABodyTheReadWouldRefuseIsRefusedWhenWritten` 非空转**；此时
`ABodyExactlyAtTheReadsBoundIsSettledAndReadBack` 仍在那 14 条通过里。

**M2 —— 把 `>` 改成 `>=`**（守卫比读收紧一个字节）：

```
$ python   # if (value.EncodedBody.Length > EntryBlob.Combo) 改成 >=
M2 applied: > became >=
$ dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter "FullyQualifiedName~DarkKnightComboBookTests"
[xUnit.net 00:00:00.53]     Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ABodyExactlyAtTheReadsBoundIsSettledAndReadBack [FAIL]
  失败 Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ABodyExactlyAtTheReadsBoundIsSettledAndReadBack [27 ms]
失败!  - 失败:     1，通过:    14，已跳过:     0，总计:    15，持续时间: 524 ms - Dfo.Server.Application.Tests.dll (net10.0)
```

→ **M2 证明 `ABodyExactlyAtTheReadsBoundIsSettledAndReadBack` 非空转**；此时
`ABodyTheReadWouldRefuseIsRefusedWhenWritten` 仍绿（`>=` 依然拒绝 65537 字节）。

变异后恢复并做了字节级校验，确认变异没有残留：

```
$ python   # 比对恢复后的文件与开工时 cp 到 scratchpad 的副本
identical: True a3f44a31769f4852e1b8d351d70832f6ede2d726e70b7505b8e1dfa526db5eb8
BOM: False CRLF: 0 LF: 226 CR: 0
```

（该 sha256 是恢复点的文件；此后又做过一次注释精确化改写，最终文件 LF 数为 227，见第七节。）

**交付物一没有对应的变异**：它是删除，不引入新断言。它的「非空转」由编译器承担——
漏改任一构造点即 CS 错误，CK-023 零错误。

### 四、两个标识符的全仓计数证据

先说清楚**区分**，因为几个名字长得像但是不同的东西：

| 标识符 | 是什么 | 本 Task 怎么处置 |
|---|---|---|
| `DarkKnightComboState` | `Dfo.Server.Data` 里那个 `record ...(IReadOnlyList<uint> SkillIds)` | **删除**（本 Task 标的） |
| `DarkKnightCombo` | `ActiveCharacterState` 上那个恒为 null 的字段 | **删除**（本 Task 标的） |
| `DarkKnightComboEntryState` | 连招页的存值类型（页号 + body），入场投影与 TASK-05 的 store 都用它 | **不动** |
| `DarkKnightCombos` | `CharacterSkillsEntryState` 上那个分页列表，`Job == 9` 时喂给 `DarkKnightComboStateMessage` | **不动** |
| `DarkKnightComboStateMessage` | `Dfo.Server.Protocol` 里的 NOTI `00/01C0` 消息 record | **不动**（它含 `DarkKnightComboState` 作为前缀，但按词边界不是同一个标识符） |

用**词边界**正则做精确标识符计数（`\b` 在 C# 标识符字符集 `[A-Za-z0-9_]` 上正确区分
`DarkKnightCombo` 与 `DarkKnightComboEntryState`、`DarkKnightComboState` 与 `DarkKnightComboStateMessage`）。

**已跟踪文件（即仓库内容本身）计数 = 0，两个都是**：

```
$ git grep -nE "\bDarkKnightCombo\b|\bDarkKnightComboState\b" -- .
git grep exit=1
```

（`git grep` 无命中时无输出、退出码 1。上面是完整原始输出，没有被裁剪。）

**整个工作区（含未跟踪文件，仅排除 `.git`/`bin`/`obj`）**：

```
$ grep -rnE "\bDarkKnightCombo\b" . --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj
./.rks-agent/tasks/character-state-finds-its-owner/attempts/spec-001-owners-for-character-state/tasks/TASK-05-combo-pages-get-a-store-and-an-instance.md:653:  `ActiveCharacterState.DarkKnightCombo`），那是 TASK-06 的事。
./.rks-agent/tasks/character-state-finds-its-owner/attempts/spec-001-owners-for-character-state/tasks/TASK-05-combo-pages-get-a-store-and-an-instance.md:845:  `:181` `ActiveCharacterState.DarkKnightCombo` **均未删**，TASK-06 的标的完好。

$ grep -rnE "\bDarkKnightComboState\b" . --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj
./.rks-agent/tasks/character-state-finds-its-owner/attempts/spec-001-owners-for-character-state/tasks/TASK-05-combo-pages-get-a-store-and-an-instance.md:652:- **本 Task 没有删那个恒 null 的单数连招字段**（`DarkKnightComboState` 与
./.rks-agent/tasks/character-state-finds-its-owner/attempts/spec-001-owners-for-character-state/tasks/TASK-05-combo-pages-get-a-store-and-an-instance.md:844:- **恒 null 的单数连招字段仍在**：`CharacterModels.cs:173` `DarkKnightComboState` 与
```

（以上两条命令的输出均为完整原始输出。注意这两次 grep 是在**追加本节之前**跑的；本节与
本文件的契约段落此后也会出现这两个名字，那是记录本身在指称被删掉的东西。）

**如实说明这 4 条为什么不算「未清干净」**：它们全在 `.rks-agent/` 下 TASK-05 的**执行记录**里，
是那一轮如实交代「本 Task 没删这两样，留给 TASK-06」的历史记录。该目录是未跟踪的 SDD 过程档案
（见下方 `git status` 的 `?? .rks-agent/tasks/character-state-finds-its-owner/`），不是仓库产物；
改写已完成 Task 的记录等于篡改历史，故不动。**在源码与文档产物里（`Server/`、`Docs/`、
`protocol/`、`ClientHack/`、`Tool/`），两个标识符各 0 处** —— 由上面的 `git grep` 零命中直接给出，
因为这些目录全部已跟踪。

作为「没误删邻居」的正面证据，`Server/` 下所有以 `DarkKnightCombo` 开头的标识符及其出现次数：

```
$ grep -rhoE "DarkKnightCombo[A-Za-z0-9_]*" Server/ --include=*.cs --exclude-dir=bin --exclude-dir=obj | sort | uniq -c | sort -rn
     32 DarkKnightComboEntryState
     10 DarkKnightComboDomainStore
      8 DarkKnightCombos
      8 DarkKnightComboDomainStoreSession
      7 DarkKnightComboDomainService
      7 DarkKnightComboBook
      6 DarkKnightComboPages
      4 DarkKnightComboStateMessage
      1 DarkKnightComboBookTests
```

裸的 `DarkKnightCombo` 与 `DarkKnightComboState` 都不在这张表上；其余九个更长的标识符一个不少。

### 五、判据输出与计数归因

#### CK-023 `dotnet build Server/86JP.Server.slnx --nologo`

```
已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:02.56
```

#### CK-024 两个标识符的全仓计数 → 0

见第四节。已跟踪仓库内容计数 0 / 0（`git grep` 零命中）。

#### CK-025 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

最终一次全量运行的逐项结果行（用 `grep -E "已通过!|失败!|SKIP|FAIL"` 过滤。**该过滤式本身
包含 `FAIL` 与 `失败`，因此不可能滤掉任何失败行**；被滤掉的只有 restore/编译进度行与
「总共 1 个测试文件与指定模式相匹配。」这类噪声）：

```
已通过! - 失败:     0，通过:   505，已跳过:     0，总计:   505，持续时间: 2 s - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52，持续时间: 1 s - Dfo.Server.Networking.Tests.dll (net10.0)
[xUnit.net 00:00:04.96]     Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message [SKIP]
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60，持续时间: 8 s - Dfo.Pvf.Tests.dll (net10.0)
[xUnit.net 00:00:11.10]     Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog [SKIP]
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6，持续时间: 8 s - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   256，已跳过:     0，总计:   256，持续时间: 12 s - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   223，已跳过:     2，总计:   225，持续时间: 17 s - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   962，已跳过:     0，总计:   962，持续时间: 16 s - Dfo.Server.Application.Tests.dll (net10.0)
```

合计：505 + 52 + 60 + 6 + 256 + 223 + 962 = **2064 通过 / 2 跳过 / 0 失败**。

**计数归因**：基线 2062 / 2 / 0 → 现 2064 / 2 / 0。
**+2 全部来自本 Task 新增的两条判据**（`Dfo.Server.Application.Tests` 由 960 增至 962）：
`ABodyTheReadWouldRefuseIsRefusedWhenWritten` 与 `ABodyExactlyAtTheReadsBoundIsSettledAndReadBack`。
交付物一是纯删除，**没有删除任何测试**，也没有改任何既有测试的期望值或断言——四处测试改动
（`CharacterEntryInventoryTests.cs`、`SelectCharacterPayloadCodecTests.cs`）删的都是那个恒为 null
的实参本身，不是断言。跳过数 2 不变（两条都是 PVF 相关的 SkippableFact，与本 Task 无关）。
本轮未观察到既知的两类偶发（`OutsideTransactionTests` 的 `ObjectDisposedException`、
`Networking.Tests` 的并行偶发），因此没有做隔离复跑。

#### `CommandHandlerArchitectureTests` 单独复跑

```
$ dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter "FullyQualifiedName~CommandHandlerArchitectureTests"
已通过! - 失败:     0，通过:    12，已跳过:     0，总计:    12，持续时间: 33 ms - Dfo.Server.Application.Tests.dll (net10.0)
```

判定谓词与豁免清单未改：该文件不在 `git status` 里，见下一节。

### 六、`git status --short` 与 `git diff --numstat` 原始输出，逐文件归属

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
?? Server/src/Dfo.Server.Abstractions/Characters/ICharacterPositionDomainService.cs
?? Server/src/Dfo.Server.Abstractions/Characters/ICharacterSettingsDomainService.cs
?? Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboBook.cs
?? Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboDomainStore.cs
?? Server/src/Dfo.Server.Application/Characters/CharacterPositionDomainService.cs
?? Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs
?? Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs
?? Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterIdentityTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterPositionTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterSettingsTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs
?? Server/tests/Dfo.Server.Persistence.Tests/Characters/CharacterIdentityReadTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs
```

```
$ git diff --numstat
17	0	Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs
41	2	Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs
3	3	Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
12	2	Server/src/Dfo.Server.Application/ApplicationModule.cs
1	2	Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
57	24	Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
24	4	Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs
1	4	Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
34	4	Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs
6	0	Server/src/Dfo.Server.Persistence/PersistenceModule.cs
8	2	Server/src/Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs
13	6	Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs
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

（上面两段是完整原始输出，未裁剪。`git status --short` 里 `.rks-agent/` 显示为一条目录级
`??`，那是 Git 对未跟踪目录的默认折叠，不是我裁掉的。）

**逐文件归属**。开工前已快照过基线的 `git status --short` 与 `git diff --numstat`，
收工后再取一次并 `diff` 两者，得到本 Task 造成的**全部**增量：

```
$ diff baseline-status.txt final-status.txt
4a5
>  M Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
6a8
>  M Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
14a17
>  M Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs
27a31
>  M Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs

$ diff baseline-numstat.txt final-numstat.txt
4a5
> 1	2	Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
6a8
> 1	4	Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
14a17
> 1	2	Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs
27a31
> 1	2	Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs
```

`diff` 只有四条 `>`（新增行），**一条 `<` 都没有**——即基线里那 27 个已改文件的增删行数
逐个未变，TASK-01..05 在已跟踪文件上的成果一行都没被碰。

于是：

- **本 Task 改动的 6 个文件**：
  - `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs`（已跟踪，1+/4−）—— 交付物一，删类型与字段
  - `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs`（已跟踪，1+/2−）—— 交付物一，删写 null 那一处
  - `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs`（已跟踪，1+/2−）—— 交付物一，构造点符号更新
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs`（已跟踪，1+/2−）—— 交付物一，构造点符号更新
  - `Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs`（**未跟踪**，TASK-05 新建）—— 交付物二，加长度守卫
  - `Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs`（**未跟踪**，TASK-05 新建）—— 交付物二，加两条判据
- **`git status` 里其余 27 个 `M` 与 15 个具名 `??` 文件**：TASK-01..05 的成果，本 Task 未碰。
  对 27 个已跟踪文件，由上面两个 `diff` 的「零条 `<`」逐文件证明行数不变；对未跟踪文件，
  本 Task 除上述两个之外没有对任何未跟踪文件执行过写操作
- **`?? .rks-agent/tasks/character-state-finds-its-owner/`**：SDD 过程档案目录，本 Task 只往
  `tasks/TASK-06-....md` 末尾追加了本节所在的两段
- **未新建表**：`Server/src/Dfo.Server.Persistence/Schema/` 下无任何文件出现在 `git status` 里
- **未新增协议帧、未改任何帧字节布局**：`Dfo.Server.Protocol` 与 `Dfo.Server.Networking` 下
  没有任何文件因本 Task 进入改动集合（`Protocol` 侧那 5 个 `M` 与 `Networking` 侧的 0 个全部属于基线）
- **`CommandHandlerArchitectureTests.cs` 不在改动集合里**：判定谓词与豁免清单未动

### 七、BOM 与行尾（二进制手段）

用 Python 直接读 bytes，不经 MSYS 的 grep/sed/tail：

```
$ python   # 逐文件打印 first3bytes / hasBOM / CRLF / CR_total / LF_total / nonASCII / endsWithLF
Server/src/Dfo.Server.Data/Characters/CharacterModels.cs                                             first3=b'usi' hasBOM=False CRLF=0 CR_total=0 LF_total=554 nonASCII=0 endsWithLF=True
Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs                               first3=b'usi' hasBOM=False CRLF=0 CR_total=0 LF_total=276 nonASCII=0 endsWithLF=True
Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs                 first3=b'usi' hasBOM=False CRLF=0 CR_total=0 LF_total=999 nonASCII=0 endsWithLF=True
Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs      first3=b'usi' hasBOM=False CRLF=0 CR_total=0 LF_total=966 nonASCII=0 endsWithLF=True
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs                     first3=b'usi' hasBOM=False CRLF=0 CR_total=0 LF_total=227 nonASCII=0 endsWithLF=True
Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs                     first3=b'usi' hasBOM=False CRLF=0 CR_total=0 LF_total=429 nonASCII=0 endsWithLF=True
```

六个文件：**无 BOM**（首三字节是 `usi`，即 `using` 的前三字母，不是 `EF BB BF`）；
**纯 LF**（`CRLF=0` 且 `CR_total=0`，故既无 CRLF 也无孤立 CR）；
**纯 ASCII**（`nonASCII=0`，本 Task 写进源码的注释与标识符全是英文）；均以换行结尾。
改动**之前**对这六个文件跑过同一检查，结果同样是「无 BOM / CRLF=0 / CR_total=0」，
故本 Task 未改变任何文件的编码或行尾。

本 Task 文件（`TASK-06-....md`）追加前的检查为 `BOM=False CRLF=0 CR_total=0 LF=43`；
追加用 Git Bash 的 quoted heredoc 写入，不含 CR，追加后复查结果见下一节末尾。

### 八、被本次改动波及的注释与文档：核对结果

删掉一个类型时特别核对了还有没有别处在引用它。逐条如实说：

| 位置 | 说的是什么 | 是否因本改动变成假话 | 处置 |
|---|---|---|---|
| `Docs/NetworkingFrameProtocol.md:336-337` | "Optional character-only state such as Dark Knight combos is nullable only when the feature does not apply." | **否**。这句讲的是 `SelectCharacterResult` 里的可选角色态；今天承载它的是 `CharacterSkillsEntryState.DarkKnightCombos`（可空），由 `SelectedCharacterStateMessageFactory.cs:60-62` 按 `Job == 9` 且 `is { } combos` 双重判定后才发帧。被删的单数字段恒为 null，从来不是这句的支撑物 | 不改 |
| `Docs/Refactor/DomainInstanceDesign.md:182-183` | "`ActiveCharacterState` is already domain-shaped — identity, progression, fatigue, position." | **否**。这个枚举在改动前就未列 `CreatedAt` 与 `DarkKnightCombo`；删掉后该 record 只剩 identity/progression/fatigue/position/CreatedAt，比原先更贴近这句话，不会更假 | 不改 |
| `Docs/Refactor/CharacterEntryStateCoverage.md:40,47`、`SelectCharacterG6Coverage.md:22,105`、`SelectCharacterMigrationMatrix.md:113` | X-01 / L-06 的连招覆盖面，提到 `DarkKnightComboStateMessage` | **否**。全部讲入场投影侧的分页列表与 NOTI `00/01C0` 消息，与被删的单数字段无关 | 不改 |
| `Docs/领域结构重构设计.md:95` | 示意 `ActiveCharacterRegistration(..., ActiveCharacterState Character)` | **否**。只提类型名，不提字段 | 不改 |
| `Server/src/**` 里所有含 "combo" 的注释（用 `grep -rniE "^\s*(//\|///\|\*).*combo" Server/src` 枚举，共 24 行） | 全部讲连招页、`character_dark_knight_combo_skill_pages` 表、store / book / 网关 | **否**。无一行提及被删的单数字段或其类型 | 不改 |
| `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs:149-151` 的注释 | 讲 `CreatedAt` 为何用固定 epoch | **否**。它紧邻被删的那一行，但讲的是上一行的 `DateTimeOffset.FromUnixTimeSeconds(...)`；删掉 `DarkKnightCombo: null` 后该注释仍恰好对着它所解释的那一行 | 不改 |

新增的两处注释（守卫处与新判据处）在写完后又做过一次精确化改写：初稿写的是 "the read below"，
但 64 KiB 上界实际是在网关 `SqliteCharacterComboPageTable.ReadAsync` 里施加的，`ReadRowsAsync`
只是转调它；改为 "ReadRowsAsync below asks the gateway, and the gateway refuses..." 以免注释
一出生就漂移。改写后重跑了 CK-023 与 CK-025，即第五节贴出的那两次输出。

## Executor Self-check 1

### 对照 Required checks

| 判据 | 期望 | 实际 | 结论 |
|---|---|---|---|
| CK-023 `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 | `已成功生成。 0 个警告 0 个错误` | 通过 |
| CK-024 Non-command：`DarkKnightCombo` 字段与 `DarkKnightComboState` 类型的全仓计数 | 0 | 已跟踪仓库内容零命中（`git grep` 退出码 1、无输出）。整工作区仅剩 4 处，全在未跟踪的 TASK-05 执行记录里，理由已在第四节写明 | 通过 |
| CK-025 `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 | 2064 通过 / 2 跳过 / 0 失败 | 通过 |

### 对照 Contract Amendment 1 的四条要求

| 要求 | 做到没有 |
|---|---|
| 先自己核实写读不对称的事实 | 是。第二节逐项核实，七条断言全部为真，无一需要推翻，故未走阻塞路径。核实到的确切位置：读上界 `CharacterEntryStateTables.cs:17-21` 的 `Combo = 64 * 1024` + `:126` 的 `EntryBlob.Bounded(...)` + `:33-36` 的 `> maximum ? throw ...`；写路径原有页号守卫在 `SqliteDarkKnightComboDomainStore.cs:156-162` |
| 让写路径对 body 长度的态度与读路径一致，形式参照页号守卫 | 是。同一个 `Write` 方法内、紧接页号守卫之后，同样抛 `ArgumentOutOfRangeException(nameof(value), 实际值, 说明)`，并引用读路径的同一常量 `EntryBlob.Combo`，取同样的 `>` 而非 `>=` |
| 加一条判据钉住「超长 body 的写入被拒，而不是写进去之后读不出来」 | 是。`ABodyTheReadWouldRefuseIsRefusedWhenWritten` 断言 `SetPageAsync` 抛 `ArgumentOutOfRangeException`，**并断言 `StoredPagesAsync()` 为空**——后半句正是「不是写进去之后读不出来」。另加一条 `ABodyExactlyAtTheReadsBoundIsSettledAndReadBack` 防止守卫比读更严 |
| 做一次变异验证证明该判据会红 | 是，做了两次，一条判据对应一次，且每次只杀死对应的那一条：M1（删守卫）只让 `ABodyTheReadWouldRefuseIsRefusedWhenWritten` 红；M2（`>` 改 `>=`）只让 `ABodyExactlyAtTheReadsBoundIsSettledAndReadBack` 红。两次的原始输出都在第三节 |

### 对照 Task 契约的 Acceptance conditions

| 条件 | 结论 |
|---|---|
| 该字段与其类型的标识符全仓计数为 0 | 满足。见 CK-024 行与第四节 |
| 全量判据全绿 | 满足。2064 / 2 / 0，零失败 |
| 行为逐值不变 | 满足。交付物一是纯删除一个恒为 null 的字段与它唯一的类型，三个构造点删的都是 `null` 实参本身；无任何既有测试的期望值或断言被改写，四处已跟踪改动的完整 diff 在第一节可逐行核对。交付物二只在写入端**新增**一条拒绝，对合法长度（≤ 64 KiB）的 body 行为不变——这一点由 `ABodyExactlyAtTheReadsBoundIsSettledAndReadBack` 在边界上正面钉住，而 TASK-05 留下的既有连招判据全部继续通过 |

### 对照 Owned boundary 与 Permitted files

| 项 | 结论 |
|---|---|
| Owned boundary：该字段、它的类型、写 null 的那一处 | 三样各处理一次，无溢出 |
| Permitted files 前两条（`CharacterModels.cs`、`CharacterRosterService.cs`） | 都改了 |
| Permitted files 第三条「因该字段消失而需要更新符号的其他位置」 | 两个测试文件的构造点，各删一个 `null` 实参；这正是该条覆盖的情形 |
| 增补交付物涉及的两个文件（TASK-05 新建、未跟踪） | 由 Contract Amendment 1 授权，其中明确点名了 `ComboPageSection.Write` |

### 对照硬约束

| 约束 | 结论 |
|---|---|
| 不新增协议帧，不改任何帧的字节布局 | 满足。未碰 `Dfo.Server.Protocol` / `Dfo.Server.Networking` 的任何源码；被删的字段从未上过线（恒 null，且全仓无任何读点） |
| 分层 Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host | 满足。交付物一改 `Data` + `Application`，且是删减不是新增引用；交付物二只改 `Persistence` 内部（`EntryBlob` 与 `ComboPageSection` 同项目同命名空间 `Dfo.Server.Persistence.Characters`，无新增跨项目引用） |
| 不新建表 | 满足。`Server/src/Dfo.Server.Persistence/Schema/` 下无文件进入改动集合 |
| `CommandHandlerArchitectureTests` 继续通过，判定谓词与豁免清单不得放宽 | 满足。单独复跑 12 条全绿；该文件不在 `git status` 里 |
| TreatWarningsAsErrors 开启，CS9113 是错误 | 满足。CK-023 零警告零错误 |
| 不建 Git 检查点、不提交、不 stash | 满足。本轮跑过的 git 命令只有 `git status --short`、`git diff --numstat`、`git diff -- <四个文件>`、`git grep`，全部只读；变异的回滚用的是开工前 `cp` 到 scratchpad 的文件副本，不是 git |
| 不改 sdd-execution.yaml、不写 verdict、不预建空段落 | 满足。只往本文件末尾追加了 `## Execution Result 1` 与 `## Executor Self-check 1` 两段，未改写既有的 `## Task Unit Contract` 与 `## Contract Amendment 1` |
| Required methods: test-driven-development | 满足。交付物二先写判据、跑出红（错误消息为 `Assert.Throws() Failure: No exception was thrown`）、再加守卫、再跑绿。交付物一是删除，无新行为可先写判据，其完整性由编译器与全量判据承担 |

### 自查中如实交代的四处

1. **CK-024 的「全仓」不是字面全仓**。`.rks-agent/` 下 TASK-05 的执行记录里还有 4 处提及这两个
   标识符，本文件（含刚追加的这两段）也必然提及。判定按「已跟踪的仓库内容」为准，得 0 / 0；
   这个口径与理由写在第四节，没有靠裁剪 grep 输出把它藏掉。
2. **CK-025 的输出经过 `grep` 过滤**。过滤式是 `grep -E "已通过!|失败!|SKIP|FAIL"`，本身包含
   `FAIL` 与 `失败`，因此不可能滤掉任何失败行；被滤掉的是 restore / 编译进度与
   「总共 1 个测试文件与指定模式相匹配。」这类噪声。第五节已写明。
3. **第三节引用的那个 sha256 不是最终文件的**。它是两次变异之间回滚点的校验值；此后又做了一次
   注释精确化改写（LF 从 226 变为 227），最终文件的字节特征以第七节那张表为准。
4. **本 Task 只做了两次变异，不多不少**，分别对应两条新判据；交付物一没有变异，理由是它不引入
   新断言，其「非空转」由「漏改即编译错误」承担。没有把变异数量说成覆盖了更多东西。

## Independent Quick Review 1

- Verdict: **passed**
- 评审者：全新上下文的独立快审者，未参与实现
- 评审的契约修订：`Task Unit Contract`（Contract revision 1）+ `Contract Amendment 1`（两件交付物）
- 评审的 owned diff 身份：
  - 已跟踪 4 个文件，`git diff` 完整核对：`CharacterModels.cs`(1+/4−)、`CharacterRosterService.cs`(1+/2−)、
    `CharacterEntryInventoryTests.cs`(1+/2−)、`SelectCharacterPayloadCodecTests.cs`(1+/2−)
  - 未跟踪 2 个文件（TASK-05 新建）：`SqliteDarkKnightComboDomainStore.cs`、`DarkKnightComboBookTests.cs`
  - 工作区状态：`git log -1` = `427331bcb07f60121c97920287ac341c9cb14dee`，`git stash list` 空

### 一、独立重跑的命令与完整原始输出

#### 1. CK-024 标识符枚举（自己跑，未采信自陈）

```
$ git grep -nE "\bDarkKnightCombo\b|\bDarkKnightComboState\b" -- .
git grep exit=1
```

（无输出、退出码 1，即已跟踪仓库内容 0 / 0。上面是完整原始输出。）

整工作区 `grep -rnE "\bDarkKnightCombo\b" . --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj`
与同式的 `\bDarkKnightComboState\b`：命中**全部**落在 `.rks-agent/` 下，其中
`TASK-05-...md` 各 2 处（共 4 处），其余为本 TASK-06 文件自身的执行记录。`Server/`、`Docs/`、
`protocol/`、`ClientHack/`、`Tool/` 零命中。

**对「4 处留在 TASK-05 记录里」这一处置的判断：恰当。** 理由三条：(a) 那 4 处是 TASK-05 如实
交代「本 Task 没删、留给 TASK-06」的历史陈述，改写等于把一份已完成的执行记录改成假话；
(b) `.rks-agent/` 是未跟踪的 SDD 过程档案（`git status` 中为单条目录级 `??`），不是仓库产物，
CK-024 按「已跟踪的仓库内容」判定口径合理；(c) 执行者**没有靠裁剪 grep 输出把它藏掉**，
而是把这 4 处连同口径一并写在第四节与自查第 1 条里 —— 满足「不得从样本推到全体」的要求。

#### 2. 构造点枚举与「编译器兜底」论证（自己跑）

```
$ grep -rn "new ActiveCharacterState(" . --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj --exclude-dir=.rks-agent
./Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs:139:            : new ActiveCharacterState(
./Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs:797:        var character = new ActiveCharacterState(

$ grep -rn "ActiveCharacterState.*=> new(" . --include=*.cs --exclude-dir=bin --exclude-dir=obj
./Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs:637:    private static ActiveCharacterState Character(int id, ushort slot) => new(
```

共 3 处，与执行者一致，三处全部已更新。另查改动前的完整足迹：

```
$ git grep -nE "\bDarkKnightCombo\b|\bDarkKnightComboState\b" 427331bc -- .
427331bc:Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs:153:                DarkKnightCombo: null);
427331bc:Server/src/Dfo.Server.Data/Characters/CharacterModels.cs:173:public sealed record DarkKnightComboState(IReadOnlyList<uint> SkillIds);
427331bc:Server/src/Dfo.Server.Data/Characters/CharacterModels.cs:181:    DarkKnightComboState? DarkKnightCombo);
427331bc:Server/tests/Dfo.Server.Application.Tests/Inventory/ItemUseFixtures.cs:99:            DarkKnightCombo: null),
```

（第 4 处 `ItemUseFixtures.cs:99` 所在的整段在 TASK-01..05 期间已被删除，numstat 为 `0 47`，
不属本 Task。）该足迹**全部是写 `null`，没有任何读点**，这也独立印证了「行为逐值不变」。

**「完整性由编译器兜底」的论证成立。** 判断依据：(a) 删掉 record 的位置参数会改变构造器 arity，
任何漏改的构造点是 CS7036/CS1729；(b) 任何遗漏的**读点**（`.DarkKnightCombo`）在属性消失后同样
是 CS 错误；(c) 编译器覆盖不到的路径只有反射 / 按名字的序列化，而这类路径必然以字符串形式出现，
已由上面 `git grep` 的零命中排除；(d) `TreatWarningsAsErrors` 开启且 CK-023 零错误零警告（下面
本评审自己重跑）。四条合起来，该论证不是「从样本推全体」，是穷尽性论证。

#### 3. 邻居标识符未被误删（自己数）

```
$ grep -rhoE "DarkKnightCombo[A-Za-z0-9_]*" Server/ --include=*.cs --exclude-dir=bin --exclude-dir=obj | sort | uniq -c | sort -rn
     32 DarkKnightComboEntryState
     10 DarkKnightComboDomainStore
      8 DarkKnightCombos
      8 DarkKnightComboDomainStoreSession
      7 DarkKnightComboDomainService
      7 DarkKnightComboBook
      6 DarkKnightComboPages
      4 DarkKnightComboStateMessage
      1 DarkKnightComboBookTests
```

点名核查的三个：`DarkKnightComboEntryState` 32、`DarkKnightCombos` 8、`DarkKnightComboStateMessage` 4，
**都还在**；裸的 `DarkKnightCombo` 与 `DarkKnightComboState` 都不在表上。与执行者第四节的表逐行一致。

#### 4. 合同增补：七条断言的抽验（读上界的确切位置）

```
$ grep -n "public const int Combo" Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs
21:    public const int Combo = 64 * 1024;

$ grep -n "EntryBlob.Bounded(reader, 1, EntryBlob.Combo" Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs
126:                EntryBlob.Bounded(reader, 1, EntryBlob.Combo, "Dark Knight combo", allowNull: false)),
```

`:33-36` 亦逐字核对为 `var value = (byte[])reader.GetValue(ordinal); return value.Length > maximum ?
throw new InvalidDataException(...) : value;`，即**拒绝而非截断**，判定符号为 `>`。
`:126` 位于 `SqliteCharacterComboPageTable.ReadAsync`（`:114-128`），是 `ICharacterComboPageTable`
的唯一实现。执行者引用的三处位置全部为真，无一需推翻。

- 新守卫在 `SqliteDarkKnightComboDomainStore.cs:170`，形如
  `if (value.EncodedBody.Length > EntryBlob.Combo)` —— **引用读路径的同一常量 `EntryBlob.Combo`**，
  未抄 `65536` 字面量；**取 `>` 而非 `>=`**，与读路径方向一致。两点均核实为真。

#### 5. 两次变异，本评审**自己重做**

变异前备份到 scratchpad，sha256 = `36e193cfcb51a05b3e769d087c1c120d7ec1815965b3064e1686046c9bb52a14`。

**M1（删掉整条长度守卫 if 块）**：

```
M1 applied: length guard if-block deleted; new LF count 219
$ dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter "FullyQualifiedName~DarkKnightComboBookTests"
[xUnit.net 00:00:00.45]     Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ABodyTheReadWouldRefuseIsRefusedWhenWritten [FAIL]
  失败 Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ABodyTheReadWouldRefuseIsRefusedWhenWritten [29 ms]
  错误消息:
   Assert.Throws() Failure: No exception was thrown
失败!  - 失败:     1，通过:    14，已跳过:     0，总计:    15，持续时间: 492 ms - Dfo.Server.Application.Tests.dll (net10.0)
```

→ 红的恰是 `ABodyTheReadWouldRefuseIsRefusedWhenWritten`，**另一条在那 14 条通过里**。与执行者所述一致。

**M2（`>` 改 `>=`）**：

```
$ python   # restored sha256: 36e193cf...b52a14  → M2 applied: > became >=
$ dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter "FullyQualifiedName~DarkKnightComboBookTests"
[xUnit.net 00:00:00.51]     Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ABodyExactlyAtTheReadsBoundIsSettledAndReadBack [FAIL]
  失败 Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ABodyExactlyAtTheReadsBoundIsSettledAndReadBack [27 ms]
  错误消息:
失败!  - 失败:     1，通过:    14，已跳过:     0，总计:    15，持续时间: 503 ms - Dfo.Server.Application.Tests.dll (net10.0)
```

→ 红的恰是 `ABodyExactlyAtTheReadsBoundIsSettledAndReadBack`，另一条仍绿。与执行者所述一致。
**两条判据与两次变异确实一一对应，互不覆盖。**

还原后的字节级复核：

```
restored sha256: 36e193cfcb51a05b3e769d087c1c120d7ec1815965b3064e1686046c9bb52a14
matches pre-mutation: True
BOM= False CRLF= 0 CR= 0 LF= 227
$ diff <变异前 numstat> <还原后 numstat>
numstat IDENTICAL
$ git status --short | md5sum
023bc886fbc502d35ccf8146222bfb02   （与变异前一致）
```

**无残留。** 变异后的全量判据（下面第 7 项）也是在还原之后跑的。

另核实第二条判据不是空转的「只测写不测读」：`Fixture.SettleAsync()` 先
`Transactions.Open.CommitAsync(...)` 再 `Reopen()` 开新用例，因此其后的 `FindPageAsync` 必然走
`ReadRowsAsync` → gateway → `EntryBlob.Bounded`，**恰好 65536 字节确实穿过了读上界**，
钉住的是「两端一致」而非只是写端。第一条的 `Assert.Empty(await fixture.StoredPagesAsync())`
用独立连接直查表，确实证明「没落行」。

#### 6. 无夹带（自己复核 TASK-05 收口时的数字）

把 TASK-05 执行记录第 200-226 行贴出的 27 行 numstat 存为基线，与当前 `git diff --numstat` 对比：

```
$ diff t05-numstat.txt now-numstat.txt
4a5
> 1	2	Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs
6a8
> 1	4	Server/src/Dfo.Server.Data/Characters/CharacterModels.cs
14a17
> 1	2	Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs
27a31
> 1	2	Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs
```

**只有 4 条 `>`，一条 `<` 都没有** —— TASK-05 收口时那 27 个已改文件的增删行数逐个未变，
与执行者的声称一致。

对**未跟踪**文件另做了一道执行者没做的独立核对：TASK-05 记录第 603-609 行留有逐文件 LF 计数，
与当前值对比：

```
IDarkKnightComboDomainStore.cs        TASK05_LF=44    now_LF=44    delta=+0
IDarkKnightComboBook.cs               TASK05_LF=78    now_LF=78    delta=+0
DarkKnightComboBook.cs                TASK05_LF=72    now_LF=72    delta=+0
ApplicationModule.cs                  TASK05_LF=311   now_LF=311   delta=+0
PersistenceModule.cs                  TASK05_LF=174   now_LF=174   delta=+0
SqliteDarkKnightComboDomainStore.cs   TASK05_LF=213   now_LF=227   delta=+14
DarkKnightComboBookTests.cs           TASK05_LF=385   now_LF=429   delta=+44
```

`+14` 恰为守卫块（6 行注释 + 7 行代码 + 1 空行），`+44` 恰为两条新判据所在的 `:217-260`。
其余未跟踪文件 delta 全 0。**这独立证实了「除那两个之外没有对任何未跟踪文件执行过写操作」。**

#### 7. 判据（自己跑）

```
$ dotnet build Server/86JP.Server.slnx --nologo
已成功生成。
    0 个警告
    0 个错误
已用时间 00:00:03.19
```

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
（过滤式 grep -E "已通过!|失败!|SKIP|FAIL|error"，本身含 FAIL 与 失败，不可能滤掉失败行）
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52 - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   505，已跳过:     0，总计:   505 - Dfo.Server.Protocol.Tests.dll (net10.0)
[xUnit.net 00:00:07.27]     Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message [SKIP]
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll (net10.0)
[xUnit.net 00:00:11.24]     Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog [SKIP]
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6 - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   256，已跳过:     0，总计:   256 - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   223，已跳过:     2，总计:   225 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   962，已跳过:     0，总计:   962 - Dfo.Server.Application.Tests.dll (net10.0)
```

合计 52+505+60+6+256+223+962 = **2064 通过 / 2 跳过 / 0 失败**，与执行者报数一致。

**逐项目归因核对**：TASK-05 记录第 585、588 行给出的收口逐项目数为
`505 / 52 / 60 / 256 / 6 / 223 / 960 = 2062`。与本次逐项目对比，**只有
`Dfo.Server.Application.Tests` 由 960 变 962，其余六项一模一样**，故 +2 全部来自本轮两条新判据，
归因成立。本次未遇到两类既知偶发。

#### 8. BOM 与行尾（二进制手段，Python 读 bytes）

```
CharacterModels.cs                       first3=b'usi' BOM=False CRLF=0 CR=0 LF=554 nonASCII=0 endsLF=True
CharacterRosterService.cs                first3=b'usi' BOM=False CRLF=0 CR=0 LF=276 nonASCII=0 endsLF=True
CharacterEntryInventoryTests.cs          first3=b'usi' BOM=False CRLF=0 CR=0 LF=999 nonASCII=0 endsLF=True
SelectCharacterPayloadCodecTests.cs      first3=b'usi' BOM=False CRLF=0 CR=0 LF=966 nonASCII=0 endsLF=True
SqliteDarkKnightComboDomainStore.cs      first3=b'usi' BOM=False CRLF=0 CR=0 LF=227 nonASCII=0 endsLF=True
DarkKnightComboBookTests.cs              first3=b'usi' BOM=False CRLF=0 CR=0 LF=429 nonASCII=0 endsLF=True
```

六个文件全部：无 BOM、纯 LF、无孤立 CR、纯 ASCII、以换行结尾。LF 计数与执行者第七节逐个相同。

#### 9. 注释与文档未失真（抽验订正 + 找第三处）

- **订正到位**。`SqliteDarkKnightComboDomainStore.cs:164-165` 现为
  `// The bound the read of these rows already applies: ReadRowsAsync below asks the gateway,`
  `// and the gateway refuses a body longer than EntryBlob.Combo where it reads the column.`
  已无 "the read below"；判据侧 `DarkKnightComboBookTests.cs:220` 同样写作
  `applied by the gateway this store reads its rows through`。上界确在网关里施加，注释与代码一致。
- **找第三处：未发现。** 独立枚举 `Docs/ protocol/ ClientHack/ Tool/ AGENTS.md README.md` 中所有
  `ActiveCharacterState` 提及，共 3 处；执行者的表覆盖了其中 2 处
  （`DomainInstanceDesign.md:183`、`领域结构重构设计.md:95`），第三处 `DomainInstanceDesign.md:98`
  只在一张「快照从哪里来」的表里出现类型名、未提任何字段，删字段不使其失真。
- 另查被删 record 的唯一成员名 `SkillIds`：`git grep "SkillIds"` 的命中全部是
  `learnedSkillIds` / `ChildSkillIds`，与被删类型无关。再查「恒 null / always-null + combo」
  的散文式表述（`.rks-agent/` 之外）：零命中。
- 执行者表中未列的两处含 combo 的文档也抽查了：`Docs/Refactor/TableOwnershipRuling.md` 的 5 处
  全部讲 `character_dark_knight_combo_skill_pages` 这张表；`Docs/ClientEngine/ExtensionSurface.md`
  的命中是客户端控件 `CNComboBoxEx`。均与被删字段无关。
- `Docs/NetworkingFrameProtocol.md:335-337` 那句的判断经核实为真：
  `CharacterModels.cs:369` 仍声明 `IReadOnlyList<DarkKnightComboEntryState>? DarkKnightCombos`（可空），
  `SelectedCharacterStateMessageFactory.cs:62` 仍按 `Job == 9 && ... is { } combos` 双重判定，
  该句的支撑物完好。

#### 10. 既有测试的期望值与断言

已跟踪的两个测试文件的完整 `git diff` 逐行核对：**只删掉了那个 `null` 位置实参本身**，
没有一处期望值、断言或测试名被改。其余测试文件的增删行数由第 6 项的「零条 `<`」逐文件证明未变。

#### 11. 硬约束

| 约束 | 独立核实结论 |
|---|---|
| 行为逐值不变 | 满足。被删字段在 `427331bc` 的完整足迹是 4 处写 `null`、**零读点**，不可能影响任何值 |
| 不新增协议帧、不改帧字节布局 | 满足。owned diff 中 `Dfo.Server.Protocol` 侧只有一个**测试**数据构造点；`Dfo.Server.Networking` 零改动 |
| 不新建表 | 满足。`git status --short -- Server/src/Dfo.Server.Persistence/Schema/` 空 |
| `CommandHandlerArchitectureTests` 通过且未放宽 | 满足。`git diff --stat -- "*CommandHandlerArchitectureTests.cs"` 为空（文件未被修改），且它在本次 962/962 的 Application.Tests 全绿运行内 |
| 分层未违反 | 满足。交付物一是 `Data` + `Application` 的删减；交付物二只在 `Persistence` 项目内部（`EntryBlob` 与 `ComboPageSection` 同项目同命名空间）。判据文件用到 `EntryBlob` 走的是 AGENTS.md 明写的测试例外（`Dfo.Server.Application.Tests` 刻意引用 `Persistence`） |
| 未建检查点、未提交、未 stash | 满足。`git log -1` = `427331bc...`，`git stash list` 空 |

### 二、非阻断观察（不要求整改，供最终评审参考）

以下四条都是**记载质量**层面的，均不影响结论，且相关结论都已由本评审独立重跑证实为真：

1. 第八节第 6 行给出「`Server/src` 里含 combo 的注释共 24 行」这一**计数但未附原始输出**，
   与本 Spec「凡枚举、计数……必须附完整原始输出」的要求不严丝合缝。本评审重跑该命令：
   计数确为 24，且 24 行中无一行提及被删的单数字段或其类型 —— 结论正确，只是证据形态是裸计数。
2. 第七节称「改动**之前**对这六个文件跑过同一检查，结果同样是无 BOM / CRLF=0」，同样未附
   那一次的原始输出。
3. 第四节末句「……两个标识符各 0 处 —— 由上面的 `git grep` 零命中直接给出，**因为这些目录
   全部已跟踪**」的**理由不准确**：`Server/` 下有 15 个未跟踪文件，`git grep` 看不到它们。
   结论仍然为真，但真正给出它的是紧接其后那条整工作区 `grep -rnE`（其完整输出已贴出，
   命中全在 `.rks-agent/`），不是 `git grep`。属论证措辞问题，非结论问题。
4. 第六节「本 Task 除上述两个之外没有对任何未跟踪文件执行过写操作」在记录里是一条**无证据的
   否定声称**。本评审用 TASK-05 记录第 603-609 行的逐文件 LF 计数把它补成了可验证的对比
   （见第 6 项），结论成立；建议后续同类声称直接引用这种可比对的字节特征。

### 三、结论

两件交付物均按契约与增补完成，六个文件的 owned diff 未越界、未夹带；两条新判据与两次变异
一一对应且经本评审**独立重做**复现；CK-023 / CK-024 / CK-025 由本评审独立重跑，结果与执行者
报数逐项一致（2064 / 2 / 0，+2 全部归于 `Dfo.Server.Application.Tests`）；硬约束全部满足；
工作区在评审后仍与评审前逐字节一致。未发现可本地整改的阻断缺陷。

**Verdict: passed。** 该判定只证明这一个 Task Unit 的有限缺陷检查通过，不构成完整变更评审，
也不替代接受前的新鲜验证。
