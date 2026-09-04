# TASK-04 — 设置有一个能回答它的领域服务

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 一个领域服务能按角色回答按键与选项设置，从既有的热键表与设置表读。
- Referenced spec requirements: RQ-005
- Referenced plan sections: T-004；BD-007, BD-008, BD-015
- Owned boundary: 新的设置领域服务及其接线与判据。**不动入场路径。**
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Abstractions/Characters/` 下新增的设置领域服务接口
  - `Server/src/Dfo.Server.Application/Characters/` 下该服务的实现与组合根接线
  - 对着真库的新增判据
- Dependencies: TASK-03
- Required dependency outputs: 无
- Acceptance conditions:
  - 该服务能按角色答出按键与选项设置，且答案与库中的行一致
  - 入场路径未被改动
  - 全量判据全绿
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: test-driven-development
- Required checks: CK-014, CK-015, CK-016, CK-017
- Required evidence:
  - 该服务的成员与其答案来源
  - 对着真库的判据结果
  - **如实报告义务（CK-016）**：把该服务今天的生产调用方**实际数目**报出来。预期为零——设置今天只有入场序列读，且全仓无写回通路。**不得掩盖这个数，也不得为了让它显得有人用而去改入场路径。** 这一点已写进计划的实现路线并交由评审裁决。
  - 全量判据输出

## Execution Result 1

### 服务的成员与答案来源

新接口 `Server/src/Dfo.Server.Abstractions/Characters/ICharacterSettingsDomainService.cs`，**一个成员**：

```csharp
ValueTask<CharacterSettingsEntryState?> FindAsync(
    CharacterId characterId, CancellationToken cancellationToken);
```

实现 `Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs`，
主构造器注入既有的两张表 `ICharacterSettingsTable settings` 与 `ICharacterHotkeyTable hotkeys`。

答案逐字段的来源：

| 答案字段 | 来自哪张表 / 哪一列 | 经由 |
|---|---|---|
| `HotkeySlots` | `character_hotkey_slots.hotkey_value`，`ORDER BY slot_index` | `ICharacterHotkeyTable.ReadAsync` |
| `OptionBitmap` | `character_settings.character_option_blob` | `ICharacterSettingsTable.FindAsync` |
| `VisibilityBits` | `character_settings.user_state_bits`（与上一行同一行） | 同上 |

答案类型复用 `CharacterSettingsEntryState`（`Server/src/Dfo.Server.Data/Characters/CharacterModels.cs:430`），
不新建同形状的第二个 record，也没有向 `Data` 加任何类型。

不回答（null）的条件：`character_settings` 里没有这个角色的行。有设置行但没有热键行的角色答一个
空的槽位列表——那是「从没存过按键」，不是「没有这个角色」。两者各有一条用例。

热键行的两条拒绝条件按入场装配里的同一形状写：

```
CharacterEntryAssembly.cs:178              if (slots.Any(value => value is < 0 or > ushort.MaxValue) || slots.Count > 256)
CharacterSettingsDomainService.cs:43-44    if (slots.Any(value => value is < 0 or > ushort.MaxValue)
                                               || slots.Count > MaximumHotkeySlots)   // MaximumHotkeySlots = 256
```

**不声称两者「refuse 同一批行」**：入场装配另有 `RequireProfileInRange` 对
`settings.UserStateBits is < 0 or > uint.MaxValue` 的拒绝，本服务不重复它——那是它在组装整个
profile record 时对该 record 的检查，本服务不组装那个 record。这一点写在实现的 `<remarks>` 里。

### 逐文件改动与理由

我这一轮碰过的文件**只有四个**：

| 文件 | 状态 | 为何在改动集合里 | 边界 |
|---|---|---|---|
| `Server/src/Dfo.Server.Abstractions/Characters/ICharacterSettingsDomainService.cs` | 新建 | 设置领域服务的接口 | BD-007 |
| `Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs` | 新建 | 该服务的实现，从既有两张表读 | BD-008 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 修改 +5 行 | 组合根接线（3 行注释 + 2 行 `TryAddSingleton`） | BD-008 |
| `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterSettingsTests.cs` | 新建 | 对着真库的判据（CK-015） | BD-015 |

`ApplicationModule.cs` 我这一轮加的整段（`git diff` 里该文件另有 5 加 2 删属于 TASK-01/TASK-02，
见下面 numstat 一节的说明）：

```csharp
        // A character's keys and options, asked by identifier. The entry load is the only other
        // reader of those two tables and it is left alone: it builds its frames from rows it has
        // already read, and this reads the same rows for anyone who is not entry.
        services.TryAddSingleton<
            ICharacterSettingsDomainService, CharacterSettingsDomainService>();
```

上面那句「entry load is the only other reader of those two tables」是**读取方**的论断，命令与完整输出：

```
$ grep -rn "settings\.FindAsync\|hotkeys\.ReadAsync" --include=*.cs Server/src/
Server/src/Dfo.Server.Application/Accounts/AccountEntryAssembly.cs:60:            await settings.FindAsync(accountId, cancellationToken).ConfigureAwait(false)
Server/src/Dfo.Server.Application/Accounts/AccountEntryAssembly.cs:89:        return await settings.FindAsync(accountId, cancellationToken).ConfigureAwait(false)
Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:145:            : await settings.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:177:        var slots = await hotkeys.ReadAsync(characterId, cancellationToken).ConfigureAwait(false);
Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs:36:        var row = await settings.FindAsync(characterId, cancellationToken).ConfigureAwait(false);
Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs:42:        var slots = await hotkeys.ReadAsync(characterId, cancellationToken).ConfigureAwait(false);

$ grep -rn "ICharacterSettingsTable\|ICharacterHotkeyTable" --include=*.cs Server/src/
Server/src/Dfo.Server.Abstractions/Persistence/CharacterBodyTables.cs:178:public interface ICharacterSettingsTable
Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs:107:public interface ICharacterHotkeyTable
Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:31:    ICharacterSettingsTable settings,
Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:33:    ICharacterHotkeyTable hotkeys,
Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs:29:    ICharacterSettingsTable settings,
Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs:27:    ICharacterSettingsTable settings,
Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs:28:    ICharacterHotkeyTable hotkeys) : ICharacterSettingsDomainService
Server/src/Dfo.Server.Persistence/Characters/CharacterBodyTables.cs:101:internal sealed partial class SqliteCharacterSettingsTable(SqliteDatabase database) : ICharacterSettingsTable
Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs:87:internal sealed class SqliteCharacterHotkeyTable(SqliteDatabase database) : ICharacterHotkeyTable
Server/src/Dfo.Server.Persistence/PersistenceModule.cs:88:        services.TryAddSingleton<ICharacterSettingsTable>(
Server/src/Dfo.Server.Persistence/PersistenceModule.cs:111:        services.TryAddSingleton<ICharacterHotkeyTable>(
```

读这两条输出要注意两点，都不影响结论：

1. `AccountEntryAssembly.cs:60/89` 的 `settings.FindAsync(accountId, ...)` 是**账号**设置表，
   不是本任务这两张表——同名局部变量而已（参数是 `accountId`）。
2. `CharacterRosterService.cs:29` 注入了 `ICharacterSettingsTable`，但它只在建号时写：
   `CharacterRosterService.cs:209` 调 `settings.InsertIfAbsentAsync(...)`，**不读**。

所以在我加这个服务之前，这两张表的读取方只有 `CharacterEntryAssembly` 一个；现在是它加本服务。

### 未改入场路径（硬性要求其一）

入场序列相关的六个路径一处未动。命令与输出（无输出即这些路径都不在改动集合里）：

```
$ git status --short -- Server/src/Dfo.Server.Protocol/Game/SelectCharacter/ \
    Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs \
    Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs \
    Server/src/Dfo.Server.Protocol/Game/StateSynchronization/ \
    Server/src/Dfo.Server.Data/Characters/CharacterModels.cs \
    Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryStateServices.cs
(空)
```

`SelectedCharacterStateMessageFactory.cs:77`（`HotkeyStateMessage`）与 `:79`
（`CharacterOptionStateMessage`）继续吃 `result.CharacterEntry.Settings`，不问本服务。

### CK-016：该服务今天的生产调用方 = **0 个**

**如实报出：零个。** 枚举命令与完整原始输出（未裁剪）：

```
$ grep -rn "CharacterSettingsDomainService" . --exclude-dir=bin --exclude-dir=obj --exclude-dir=.git
./Server/src/Dfo.Server.Abstractions/Characters/ICharacterSettingsDomainService.cs:28:public interface ICharacterSettingsDomainService
./Server/src/Dfo.Server.Application/ApplicationModule.cs:228:            ICharacterSettingsDomainService, CharacterSettingsDomainService>();
./Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs:26:internal sealed class CharacterSettingsDomainService(
./Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs:28:    ICharacterHotkeyTable hotkeys) : ICharacterSettingsDomainService
./Server/tests/Dfo.Server.Application.Tests/Characters/CharacterSettingsTests.cs:117:            this.Settings = new CharacterSettingsDomainService(
./Server/tests/Dfo.Server.Application.Tests/Characters/CharacterSettingsTests.cs:124:        public ICharacterSettingsDomainService Settings { get; }
```

`Server/src` 下的四处依次是：接口声明、组合根注册、实现的类头、实现的 `: ICharacterSettingsDomainService`。
**没有任何一处是调用方**——没有生产代码注入它，`FindAsync` 在 `Server/src` 里零次被调用。
`Server/tests` 下两处是本轮新增的判据自己。

这与计划实现路线的预期一致，原因也在仓库里可查：设置今天没有写回通路。

- `character_hotkey_slots` 在 `Server/src` 里只有 `CREATE TABLE` 与一条 `SELECT`（见前一节 grep）；
- `character_settings` 在 `Server/src` 里只有 `CREATE TABLE`、一条 `SELECT`、以及建号时的
  `INSERT ... ON CONFLICT(character_id) DO NOTHING`
  （`Server/src/Dfo.Server.Persistence/Characters/CharacterBodyTables.cs:193`），**没有任何 `UPDATE`**；
- 唯一一条携带 option blob 的入站协议 `SAVE_GAME_OPTION_1`
  （`Server/src/Dfo.Server.Protocol/Game/SaveGameOption/SaveGameOption1Slice.cs`）的 handler 只打日志，
  其 `<summary>` 自己写着 "Nothing is persisted yet"，日志文本也是 "not persisted"。

若评审认为无消费方的归位应当推迟，那是一次范围收窄，按评审流程处理。

### CK-015：对着真库的判据

新增 `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterSettingsTests.cs`，
四条用例，真 SQLite 文件库（`new SqliteDatabase(path, new DomainTransactionScope())` 建表，
测试用裸连接直接写行——因为服务器里没有任何 verb 会写这些行），服务由
`new CharacterSettingsDomainService(new SqliteCharacterSettingsTable(db), new SqliteCharacterHotkeyTable(db))`
按组合根同样的形状装配。组织方式参照 `Server/tests/Dfo.Server.Persistence.Tests/` 下既有的
SQLite 判据（一次性临时库 + `IAsyncDisposable` fixture + `SqliteConnection.ClearAllPools()`）。

**三个字段的值互不可混淆**（这是判据能区分「读错列 / 错位」的前提）：

- 热键槽 `[4113, 8226, 12339]`——三个互不相同的数，且行是按 `slot_index` 乱序插入的
  （先 2 再 0 再 1），所以答案的顺序来自 `ORDER BY slot_index` 而不是插入顺序；
- 选项位图 `[0x5B, 0x6C, 0x7D, 0x8E]`——字节，任何数字列读进这个字段都不会「碰巧对上」；
- 可见位 `1234567`——大于 `ushort.MaxValue`，因此它不可能冒充一个热键槽。

四条用例：

| 用例 | 断言什么 |
|---|---|
| `WhatIsAnsweredIsWhatTheRowsHold` | 三个字段逐一等于库里的行 |
| `ACharacterWithNoSettingsRowIsAnsweredWithNothing` | 没有设置行的角色答 null |
| `ACharacterWithNoHotkeyRowsIsStillAnsweredForWithNoSlots` | 有设置行没有热键行 → 答空槽位，不是 null |
| `AnOptionBlockNeverStoredAndAnEmptyOneAreDifferentAnswers` | NULL blob 答 null，空 blob 答空数组（底层 reader 刻意分开的两件事没有被本服务合并） |

单独跑：

```
$ dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter "FullyQualifiedName~CharacterSettingsTests"
已通过! - 失败:     0，通过:     4，已跳过:     0，总计:     4，持续时间: 162 ms - Dfo.Server.Application.Tests.dll (net10.0)
```

### 变异验证

两次变异，各自只改实现里的一行，改完跑上面那条 filter，跑完还原并用 `diff` 对拍备份确认逐字节回到原样。

**变异 A —— 读错同一张表的另一列**：把 `VisibilityBits` 的来源从 `row.UserStateBits`
改成 `row.OptionBlob?.Length ?? 0`。结果：

```
[xUnit.net 00:00:00.42]     Dfo.Server.Application.Tests.Characters.CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold [FAIL]
  失败 Dfo.Server.Application.Tests.Characters.CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold [36 ms]
  错误消息:
   Assert.Equal() Failure: Values differ
Expected: 1234567
Actual:   4
  堆栈跟踪:
     at Dfo.Server.Application.Tests.Characters.CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold() in ...\CharacterSettingsTests.cs:line 55
失败!  - 失败:     1，通过:     3，已跳过:     0，总计:     4
```

**变异 B —— 向热键表问错角色**：把 `hotkeys.ReadAsync(characterId, ...)` 改成
`hotkeys.ReadAsync(new CharacterId(characterId.Value + 1), ...)`。结果：

```
[xUnit.net 00:00:00.42]     Dfo.Server.Application.Tests.Characters.CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold [FAIL]
  失败 Dfo.Server.Application.Tests.Characters.CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold [35 ms]
  错误消息:
   Assert.Equal() Failure: Collections differ
                       ↓ (pos 0)
Expected: ushort[]    [4113, 8226, 12339]
Actual:   <generated> []
  堆栈跟踪:
     at Dfo.Server.Application.Tests.Characters.CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold() in ...\CharacterSettingsTests.cs:line 53
失败!  - 失败:     1，通过:     3，已跳过:     0，总计:     4
```

两条都**指名了全限定名与 Expected/Actual**。还原证据（对拍备份文件）：

```
$ diff <备份> Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs && echo "RESTORED IDENTICAL"
RESTORED IDENTICAL
```

还原后 filter 复跑 `已通过! - 失败: 0，通过: 4`。备份文件放在会话 scratchpad 目录，不在工作区内
（下面的 `git status --short` 里没有它）。

时序说明：两次变异都在**还原并逐字节对拍通过之后**才继续。此后我又改过实现一次，改的是那段 `<remarks>` 的措辞（把「两者 refuse 同一批行」这句不成立的等价论断改掉，见本文第一节），**不涉及任何可执行语句**；上面「判据输出」里的 CK-014 与 CK-017 两次结果都是在这次措辞修改**之后**跑的。

裁剪声明：上面两段失败输出里，我把堆栈跟踪中的绝对路径
`E:\Project\DNF\DNF-JP-86-Server\.worktrees\character-state-finds-its-owner\Server\tests\Dfo.Server.Application.Tests\Characters\`
缩成了 `...\`，并省掉了 xUnit 重复输出的第二行同名堆栈帧与
`--- End of stack trace from previous location ---`。裁掉的都是路径与重复帧，
FQN、Expected/Actual、行号、计数一字未动。

### 判据输出

**CK-014** `dotnet build Server/86JP.Server.slnx --nologo`：

```
已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:02.28
```

**CK-017** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`：

```
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52，持续时间: 1 s - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   505，已跳过:     0，总计:   505，持续时间: 2 s - Dfo.Server.Protocol.Tests.dll (net10.0)
  已跳过 Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message [1 ms]
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60，持续时间: 8 s - Dfo.Pvf.Tests.dll (net10.0)
  已跳过 Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog [1 ms]
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6，持续时间: 8 s - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   256，已跳过:     0，总计:   256，持续时间: 12 s - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   223，已跳过:     2，总计:   225，持续时间: 17 s - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   947，已跳过:     0，总计:   947，持续时间: 17 s - Dfo.Server.Application.Tests.dll (net10.0)
```

裁剪声明：这段是把该次运行输出里的**编译日志行与 `总共 1 个测试文件与指定模式相匹配。` 这类噪声行**
用 `grep -E "已通过!|失败!|已跳过 "` 过滤掉的结果。八个项目的结果行**一条不缺**；`失败!` 这个词
在过滤模式里，若有失败项目一定会出现——它没有出现。

计数：52 + 505 + 60 + 6 + 256 + 223 + 947 = **2049 通过 / 2 跳过 / 0 失败**。

**归因**：基线 2045 / 2 / 0 → 2049 / 2 / 0，**+4 通过**，正好是本轮新增的
`CharacterSettingsTests` 四条 `[Fact]`（上面的 filter 单跑显示 `总计: 4`）。跳过数与失败数不变。
本轮**没有修改任何既有用例**——`git status --short` 里 `Server/tests` 下唯一属于我的条目是
新建的 `CharacterSettingsTests.cs`，其余修改项全部属于 TASK-01/02/03。

基线的两个已知偶发（`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` 的
`ObjectDisposedException`、`Networking.Tests` 的并行偶发）本轮两次全量运行都没有出现。

**架构判据 `CommandHandlerArchitectureTests`** 单独复跑并确认文件未被改：

```
$ dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter "FullyQualifiedName~CommandHandlerArchitectureTests"
已通过! - 失败:     0，通过:    12，已跳过:     0，总计:    12，持续时间: 36 ms - Dfo.Server.Application.Tests.dll (net10.0)

$ git status --short -- Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs
(空)
```

判定谓词与豁免清单未被触碰（该文件不在改动集合里）。本服务也不是命令处理器的注入面变更——
没有任何命令处理器注入它（CK-016 的枚举即证）。

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
?? Server/src/Dfo.Server.Abstractions/Characters/ICharacterSettingsDomainService.cs
?? Server/src/Dfo.Server.Application/Characters/CharacterPositionDomainService.cs
?? Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterIdentityTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterPositionTests.cs
?? Server/tests/Dfo.Server.Application.Tests/Characters/CharacterSettingsTests.cs
?? Server/tests/Dfo.Server.Persistence.Tests/Characters/CharacterIdentityReadTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs
?? Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs
```

逐条归属：

- **本轮（TASK-04）产生的只有 4 条**：`?? .../ICharacterSettingsDomainService.cs`、
  `?? .../CharacterSettingsDomainService.cs`、`?? .../CharacterSettingsTests.cs`，
  以及 ` M .../ApplicationModule.cs` 里我加的那 5 行。
- **`?? .rks-agent/tasks/character-state-finds-its-owner/`**：本 Spec 的 SDD 制品树（spec/plan/tasks），
  我进场前就已经是未跟踪目录；本轮我只往本 Task 文件末尾追加了这两段交付。
- 其余 25 条 ` M` 与 5 条 `??` 全部是 TASK-01（身份）、TASK-02（坐标）、TASK-03（任务读点）
  的成果，本轮一处未动。

### `git diff --numstat` 原始输出

```
17	0	Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs
41	2	Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs
3	3	Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
10	2	Server/src/Dfo.Server.Application/ApplicationModule.cs
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

`numstat` 里唯一有我份额的是 `ApplicationModule.cs` 的 `10 2`：其中 **5 加**是我这一轮的设置服务注册
（3 行注释 + 2 行 `TryAddSingleton`），另 **5 加 2 删**是 TASK-02 的坐标服务注册与 TASK-01 摘掉
`PlayingCharacter` 两个会话依赖留下的。完整 diff（行尾的 `<-- TASK-0x` 是我为归属加的标注，
**不在文件里**；除此之外与 `git diff -- Server/src/Dfo.Server.Application/ApplicationModule.cs`
的输出逐字一致）：

```diff
@@ -216,6 +216,16 @@ public sealed class ApplicationModule : IServerModule
         // arithmetic is the entry rule service's; this is the way to reach it without being entry.
         services.TryAddSingleton<
             ICharacterSkillPointDomainService, CharacterSkillPointDomainService>();
+        // Where a character being played is standing, asked by identifier. It used to be a field   <-- TASK-02
+        // on the session's snapshot of the character, which meant every step rewrote the snapshot  <-- TASK-02
+        // and every reader had to find a session before it could ask.                              <-- TASK-02
+        services.TryAddSingleton<                                                                   <-- TASK-02
+            ICharacterPositionDomainService, CharacterPositionDomainService>();                     <-- TASK-02
+        // A character's keys and options, asked by identifier. The entry load is the only other    <-- TASK-04
+        // reader of those two tables and it is left alone: it builds its frames from rows it has   <-- TASK-04
+        // already read, and this reads the same rows for anyone who is not entry.                  <-- TASK-04
+        services.TryAddSingleton<                                                                   <-- TASK-04
+            ICharacterSettingsDomainService, CharacterSettingsDomainService>();                     <-- TASK-04
         services.TryAddSingleton(
             provider => new CharacterDomainService(
                 () => provider.GetRequiredService<ICharacterRepository>(),
@@ -224,8 +234,6 @@ public sealed class ApplicationModule : IServerModule
                     characterId,
                     provider.GetRequiredService<IItemUseJudgementService>(),
                     provider.GetRequiredService<ICharacterItemCooldownDomainService>(),
-                    provider.GetRequiredService<ISelectedSessionDomainService>(),                   <-- TASK-01
-                    provider.GetRequiredService<ISelectedSessionRuntimeDirectory>(),                <-- TASK-01
                     provider.GetRequiredService<ISessionPresenceDomainService>(),
                     provider.GetRequiredService<ICharacterCarriedGoldTable>(),
```

三个新建文件不出现在 `git diff --numstat` 里，因为它们未跟踪；`git status --short` 已把它们列为 `??`。

### BOM 与行尾检查

**方法说明（先说，因为我第一次用错了工具）**：我最初用 `head -c 3 | xxd` 与 `grep -c` 数 CR
来做这两项检查，随后发现在本机的 MSYS 环境里 `grep` / `sed` / `tail` 对 CR 的处理不可靠——
数 CR 的那条 `grep -c` 在一个**完全不含 CR** 的文件上照样报满行数。因此下面的结论一律改用
Python 以二进制方式重数，脚本与完整输出照录。

```
$ python -c "<下面这段>"
files = [ ...本轮碰过的四个文件，外加三个既有对照... ]
for path in files:
    data = open(path, 'rb').read()
    crlf = data.count(CRLF); lone_lf = data.count(LF) - crlf
    print(name, len(data), crlf, lone_lf, data[:3] == UTF8_BOM)

ICharacterSettingsDomainService.cs                 bytes=2513    crlf=0     lone_lf=47    bom=False
CharacterSettingsDomainService.cs                  bytes=2838    crlf=0     lone_lf=59    bom=False
ApplicationModule.cs                               bytes=21471   crlf=0     lone_lf=309   bom=False
CharacterSettingsTests.cs                          bytes=8630    crlf=0     lone_lf=196   bom=False
CharacterPositionDomainService.cs                  bytes=4046    crlf=0     lone_lf=94    bom=False   <- 既有对照（TASK-02）
CharacterEntryAssembly.cs                          bytes=32837   crlf=0     lone_lf=646   bom=False   <- 既有对照（未改动）
AGENTS.md                                          bytes=33853   crlf=0     lone_lf=459   bom=False   <- 既有对照（未改动）
```

（`CRLF` / `LF` / `UTF8_BOM` 分别是 `b"\r\n"`、`b"\n"`、`b"\xef\xbb\xbf"` 三个字节串常量；
这里写成名字是因为把转义直接写进本文档会被处理链吃掉，上一版就是这么错过一次。）

**BOM：全部 `bom=False`**——我碰过的四个文件一个都没有 UTF-8 BOM，
没有一个以 `ef bb bf` 开头。

**行尾：全部 `crlf=0`，即整树 LF**，与既有的未改动文件（`CharacterEntryAssembly.cs`、
`AGENTS.md`）一致。本工作区 `git config core.autocrlf` 的实际值是 `false`
（不是 `.gitattributes` 注释里描述的 `true`），所以工作区里的文件本来就是 LF；
我新建的三个文件与之一致，没有引入混合行尾。

本 Task 文件自身同样核过：追加交付之后 `crlf=0`、`bom=False`，
且开头 1505 字节（原有的 `## Task Unit Contract` 一段，27 行）与追加前的备份**逐字节相同**
（`cur[:len(old)] == old` 为 `True`）。

### 我的改动是否让某句既有注释/文档不再成立

逐条查过，**没有需要一并改的**：

- `ICharacterSettingsTable` / `ICharacterHotkeyTable` 的 `<summary>` 只说「这是哪张表」，
  不含「只有谁读它」之类的论断，新增一个读取方不影响；
- `CharacterEntryAssembly` 的 `<summary>`（"Thirty-three tables become one record"）说的是它自己
  装配了什么，我没有改它读的表数，也没有改它；
- `SaveGameOption1Handler` 的 "Nothing is persisted yet" 仍然成立——本轮没有加任何写入通路；
- `AGENTS.md` 的分层与接口后缀约定：新接口落在 `Dfo.Server.Abstractions`、以 `DomainService` 结尾、
  实现落在 `Dfo.Server.Application`、只被组合根注册，与约定一致，无需改动。
  （AGENTS.md 记下这几样状态各自归谁是 T-009 / BD-016 的事，不在本 Task 边界内。）
- 命名：服务叫 `*DomainService`，**没有**用 `store`——本仓 `store` 只指事务结算的真源
  （`IDomainStoreSession`、能进事务、暂存后一次性落地），设置这里是单点读，够不上那个门槛。

## Executor Self-check 1

### 对照 Required checks

| Check | 要求 | 结果 | 证据位置 |
|---|---|---|---|
| CK-014 | `dotnet build Server/86JP.Server.slnx --nologo` 零警告零错误 | **通过**：`0 个警告 / 0 个错误` | 上文「判据输出」 |
| CK-015 | 对着真库写入设置行后向该服务提问，答案与库中的行一致 | **通过**：`CharacterSettingsTests` 四条全绿；三个字段值互不可混淆；两次变异各自把它打红并指名 FQN 与 Expected/Actual | 上文「CK-015」「变异验证」 |
| CK-016 | 记录该服务今天的生产调用方**实际数目**并如实报出 | **0 个**，已如实报出，附全仓 grep 的完整未裁剪输出与逐处说明 | 上文「CK-016」 |
| CK-017 | 全量 `dotnet test` 通过 | **通过**：2049 / 2 跳过 / 0 失败（基线 2045 / 2 / 0，+4 = 本轮四条新用例） | 上文「判据输出」 |

### 对照 Task 契约的 Acceptance conditions

| 条件 | 结果 | 依据 |
|---|---|---|
| 该服务能按角色答出按键与选项设置，且答案与库中的行一致 | 满足 | `ICharacterSettingsDomainService.FindAsync(CharacterId, ...)` 一个成员答出三个字段；`WhatIsAnsweredIsWhatTheRowsHold` 对着真库逐字段断言 |
| 入场路径未被改动 | 满足 | `git status --short` 对入场序列六个路径的查询为空；`SelectedCharacterStateMessageFactory.cs:77/79` 仍吃 `result.CharacterEntry.Settings` |
| 全量判据全绿 | 满足 | 2049 / 2 / 0，0 失败 |

### 对照 Owned boundary 与 Permitted files

| 允许的位置 | 我动了什么 | 越界？ |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/Characters/` 下新增的设置领域服务接口 | 新建 `ICharacterSettingsDomainService.cs` | 否 |
| `Server/src/Dfo.Server.Application/Characters/` 下该服务的实现与组合根接线 | 新建 `CharacterSettingsDomainService.cs`；组合根接线落在 `Server/src/Dfo.Server.Application/ApplicationModule.cs`（该文件即本项目的组合根接线点，TASK-02 的坐标服务注册也在同一处，已经过独立快审） | 否 |
| 对着真库的新增判据 | 新建 `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterSettingsTests.cs`（对应 BD-015 指定的目录） | 否 |

### 对照本轮的硬约束

- **行为逐值不变**：本任务是纯新增，零处生产调用方，因此对客户端的可观察行为一字节未变；
  既有测试的期望值与断言**一处未改**（`Server/tests` 下我只新建了一个文件）。
- **不新增协议帧，不改任何帧的字节布局**：`Server/src/Dfo.Server.Protocol/` 与
  `Server/src/Dfo.Server.Data/Networking/` 都不在我的改动集合里。
- **分层**：`Abstractions`（接口，只引 `Data`）→ `Application`（实现 + 注册，引 `Abstractions` / `Data`）。
  未引入任何反向或跨层引用；新服务只注入 `Abstractions/Persistence` 里的两个表契约。
- **`CommandHandlerArchitectureTests`**：12 条通过，文件未改，判定谓词与豁免清单未动。
- **TreatWarningsAsErrors / CS9113**：构建零警告；实现的两个主构造器参数都被读到。
- **不建 Git 检查点、不提交、不 stash**：未执行任何 `git commit` / `git stash` / `git tag` /
  `git checkout`；只用了 `git status` 与 `git diff` 两个只读命令。

### 已知的、留给评审裁决的一点

该服务**今天没有生产调用方**（CK-016 = 0）。这是计划实现路线预先写明并交由评审裁决的事项，
不是接线遗漏：两张表是真表、行是真行、判据对着真库验证它答得对，缺的是调用方，而调用方要等
改键协议做进来。若评审认为无消费方的归位应当推迟，那是一次范围收窄，按评审流程处理。

## Independent Quick Review 1

- Verdict: **passed**
- Reviewed contract revision: 1（无修正案）
- Owned diff identity: 新建 `Server/src/Dfo.Server.Abstractions/Characters/ICharacterSettingsDomainService.cs`、新建 `Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs`、新建 `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterSettingsTests.cs`、修改 `Server/src/Dfo.Server.Application/ApplicationModule.cs`（本轮 +5 行）
- Baseline: `git log -1` = `427331bc`（`Record where the three batches left the root map`），`git stash list` 空
- 评审者身份：全新上下文，未参与实现；下列结论一律自行执行命令取得，不采信自陈。

### 我自己跑过的命令

| 命令 | 我得到的结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx --nologo`（CK-014） | `0 个警告 / 0 个错误`（变异前后各跑一次，均如此） |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`（CK-017） | 52 + 505 + 60 + 6 + 256 + 223 + 947 = **2049 通过 / 2 跳过 / 0 失败**（跑两次，逐项目计数两次一致） |
| `dotnet test … --filter "FullyQualifiedName~CharacterSettingsTests"`（CK-015） | `失败: 0，通过: 4` |
| 全仓 `grep -rn "CharacterSettingsDomainService"`（CK-016） | `Server/src` 4 处、`Server/tests` 2 处，**生产调用方 0 个** |
| `git status --short` / `git diff --numstat` / `git log -1` / `git stash list` | 与交付逐字一致；变异后再查一次仍逐字一致 |
| Python 二进制重数 BOM 与行尾 | 四个文件全部 `crlf=0, lone_cr=0, bom=False` |

两次全量运行都没有出现既知偶发（`OutsideTransactionTests` 的 `ObjectDisposedException`、
`Networking.Tests` 的并行偶发）。

### 六项核对

**1. 服务答的是不是库里那一行 —— 逐条属实。**

| 答案字段 | 我核到的 SQL / 实现 |
|---|---|
| `HotkeySlots` | `CharacterEntryStateTables.cs:92-93` `SELECT hotkey_value FROM character_hotkey_slots WHERE character_id=@cid ORDER BY slot_index` |
| `OptionBitmap` | `CharacterBodyTables.cs:106-113` `SELECT character_option_blob, user_state_bits FROM character_settings WHERE character_id=@cid`，第 0 列经 `EntryBlob.Optional` → `row.OptionBlob` |
| `VisibilityBits` | 同一条 SELECT 的第 1 列 `reader.GetInt64(1)` → `row.UserStateBits`，**确为同一行** |

两种「没有」确实分开：`CharacterSettingsDomainService.cs:36-40` 在 `settings.FindAsync` 返回 null 时
**先行返回 null，根本不去问热键表**；有设置行时才读热键，空列表原样进入 `HotkeySlots`。
用例也确实各有一条（`ACharacterWithNoSettingsRowIsAnsweredWithNothing` /
`ACharacterWithNoHotkeyRowsIsStillAnsweredForWithNoSlots`）。

答案类型复用属实：`CharacterSettingsEntryState`（`CharacterModels.cs:430`）正是入场投影在
`CharacterEntryStateServices.cs:39-42` 用同样三个来源（`storage.HotkeySlots`、
`storage.CharacterOptionBlob`、`storage.Settings.UserStateBits`）构造的那一个，没有第二个同形状 record。

热键拒绝条件与 `CharacterEntryAssembly.cs:177-181` 逐字同形；交付里「不声称两者 refuse 同一批行」
（入场另有 `RequireProfileInRange`，本服务不重复）的自我限定**属实**，不是虚饰。

**2. 判据判别力 —— 两次变异逐字复现；另发现一处非阻断的过度论断。**

- **变异 A**（`row.UserStateBits` → `row.OptionBlob?.Length ?? 0`）：
  `WhatIsAnsweredIsWhatTheRowsHold [FAIL]`，`Expected: 1234567 / Actual: 4`，`失败: 1，通过: 3`。**与报告一字不差。**
- **变异 B**（`hotkeys.ReadAsync(characterId,…)` → `ReadAsync(new CharacterId(characterId.Value + 1),…)`）：
  同一条用例红，`Expected: ushort[] [4113, 8226, 12339] / Actual: <generated> []`，`失败: 1，通过: 3`。**与报告一字不差。**
- 还原：`sha256` 回到 `104bb766…2f75eb`，`cmp` 逐字节相同；还原后全量重跑 `0 警告 / 0 错误` 与
  `2049 / 2 / 0`；`git status --short` 与 `git diff --numstat` 与变异前逐字相同，无残留，
  备份文件在会话 scratchpad、不在工作区。

关于这组值本身：判断为**确实互不可混淆**。`VisibilityBits = 1234567 > ushort.MaxValue`，
不可能冒充槽位；选项块是字节且字段类型是 `byte[]?`，数字列进不来；三个槽位值两两不同，
错位必然移动某条断言。

**非阻断发现（不影响本 Task 的接受）**：交付称「行是按 slot_index 乱序插入的（先 2 再 0 再 1），
所以答案的顺序来自 `ORDER BY slot_index` 而不是插入顺序」。我做了第三次变异——把
`CharacterEntryStateTables.cs:92-93` 的 `ORDER BY slot_index` 整个删掉——**四条用例仍然全绿**。
原因是 `character_hotkey_slots` 的主键是 `PRIMARY KEY(character_id, slot_index)`
（`CharacterDatabaseSchema.cs:194`），SQLite 走那个自动索引本来就按 slot_index 出行。
所以这条用例真正证到的是较弱但已足够的那半句：**答案是 slot 序、不是插入序**（插入序会是
`12339, 4113, 8226`，必红）；它证不到「这个顺序是那句 `ORDER BY` 给的」。
该 `ORDER BY` 属既有生产代码、不在本 Task 的 owned diff 内，故不构成本 Task 的缺陷。
变异已还原，该文件 `git diff --numstat` 为空。

**3. CK-016 = 0 —— 属实，三条佐证我逐条验过。**

- 复跑枚举：`Server/src` 四处依次是接口声明（`ICharacterSettingsDomainService.cs:28`）、
  组合根注册（`ApplicationModule.cs:228`）、实现类头（`:28`）、`: ICharacterSettingsDomainService`（`:30`）。
  **无任何注入点，`FindAsync` 在 `Server/src` 里零次被调用。**
- `character_hotkey_slots`：全仓 `Server/src` 只有 `CREATE TABLE`（`CharacterDatabaseSchema.cs:194`）
  与一条 `SELECT`（`CharacterEntryStateTables.cs:92`）。属实。
- `character_settings`：只有 `CREATE TABLE`（`:203`）、一条 `SELECT`（`CharacterBodyTables.cs:107-108`）、
  建号时的 `INSERT … ON CONFLICT(character_id) DO NOTHING`（`:193`）。
  我另跑 `grep -rni "UPDATE character_settings|UPDATE character_hotkey" Server/` → **输出为空**。属实。
- `SaveGameOption1Slice.cs:69` 原话我读到了：
  `/// Accepts the option blob and logs it. Nothing is persisted yet: the blob's internal layout is`
  `/// undecoded, …`；`:87` 的日志文本是 `"… {Length} option bytes, not persisted"`。属实。
- 两条「易误读」说明**均成立**：`AccountEntryAssembly` 的主构造器参数是
  `IAccountSettingsTable settings`（账号设置表，同名局部变量，参数确为 `accountId`）；
  `CharacterRosterService.cs:29` 注入的 `ICharacterSettingsTable` 全文件唯一用处是
  `:209` 的 `InsertIfAbsentAsync`，**只写不读**。

**4. 入场路径确实未动。** 六路径 `git status --short` 复跑为空。
`SelectedCharacterStateMessageFactory.cs:77` 仍是
`new HotkeyStateMessage(result.Account.Settings, result.CharacterEntry.Settings)`、
`:79` 仍是 `new CharacterOptionStateMessage(result.CharacterEntry.Settings.OptionBitmap)`，
都吃入场记录、不问新服务；该文件不在改动集合里。
（记一笔：该文件实际在 `Game/SelectCharacter/` 而非评审说明里写的 `Game/StateSynchronization/`；
两个目录都在被查询的路径清单内，且该文件无论如何未被改动，结论不变。）

**5. 命名与分类正确。** 接口 `ICharacterSettingsDomainService` 落在
`Dfo.Server.Abstractions/Characters/`、以 `DomainService` 结尾，符合 AGENTS.md:124 与 :420。
**没有**用 `DomainStore` 后缀，**也没有**加 `IDomainStoreSession` 之类的事务形状——接口仅一个
`FindAsync` 成员，无会话、无按位置读写、无事务收口。AGENTS.md:125/:425 把 `store` 保留给
「事务结算真源」，一个跨两张表的只读接缝留在领域服务是对的。
分层：`Dfo.Server.Abstractions.csproj` 只引用 `Dfo.Server.Data`，接口暴露 `Data` 的
`CharacterSettingsEntryState` 与 TASK-02 的 `ICharacterPositionDomainService` 是同一先例。

**6. 无夹带 + 记载纪律 —— 属实。**

- `git status --short`（26 ` M` + 11 `??`）与 `git diff --numstat`（26 行）我复跑，与交付**逐字一致**。
- `ApplicationModule.cs` 的 `10 2` 拆分经 `git diff` 核对属实：新增 10 行中 5 行是 TASK-02 的坐标服务注册、
  5 行是本轮的设置服务注册；2 行删除是 TASK-01 摘掉 `ISelectedSessionDomainService` /
  `ISelectedSessionRuntimeDirectory` 留下的。本轮在该文件的份额确为 5 行。
- `git log -1` = `427331bc`，`git stash list` 空。未建检查点、未提交、未 stash。
- **BOM / 行尾**：用 Python 读原始字节重数，四个文件全部
  `crlf=0, lone_cr=0, bom=False`——**无 BOM、纯 LF**。本 Task 文件自身亦然。上一个 Task 栽的那种 BOM
  问题这里不存在。

**关于执行者对自身错误的订正（非阻断）**：订正是**诚实且实质完整**的——它主动交代了用错工具、
交代了错工具给出了错数、交代了自己一度写下「全文件 CRLF」的错误论断、并说明改用 Python
二进制重数。承载结论的那部分（自己四个文件无 BOM、纯 LF）我独立复现，**完全正确**。
唯一残留是一处从 7 个样本推到全体的措辞：它写「整个工作区都是 LF」。我对全树做了二进制扫描
（3092 个 `.cs`/`.md`/`.csproj`/`.json`/`.slnx`，排除 `bin`/`obj`/`.git`），实际有 **2 个文件含 CRLF**
（`Server/src/Dfo.Server.Abstractions/IServerModule.cs`、
`Server/src/Dfo.Server.Host/Modules/ServerModuleRegistrationExtensions.cs`）、
**7 个文件带 UTF-8 BOM**（`GetAccountCharacterRosterHandler.cs`、`CreateCharacterSlice.cs`、
`DeleteCharacterSlice.cs`、`HiddenCharacterInfoSlice.cs`、`CharacterLifecyclePayloadCodecTests.cs`、
`Tool/PvfProxy/Program.cs`、`Tool/PvfProxy/PvfProxy.csproj`）。这 9 个全部是既有文件、
一个都不在 `git status --short` 里，与本 Task 的改动集合无交集。属于**证据措辞过宽**，不是缺陷。

### 硬约束核对

| 约束 | 结论 |
|---|---|
| 行为逐值不变 | 满足。纯新增、零生产调用方，可观察行为无变化 |
| 既有测试期望与断言一字未改 | 满足。`Server/tests` 下本轮唯一条目是新建的 `CharacterSettingsTests.cs` |
| 不新增协议帧、不改帧字节布局 | 满足。`Dfo.Server.Protocol/` 与 `Dfo.Server.Data/Networking/` 不在本轮改动集合 |
| `CommandHandlerArchitectureTests` 通过且未放宽 | 满足。全量运行内通过；该文件不在改动集合，无从放宽 |
| 分层未违反 | 满足。`Abstractions`（仅引 `Data`）→ `Application`；新服务只注入 `Abstractions/Persistence` 的两个表契约 |
| 未建检查点 / 未提交 / 未 stash | 满足。`427331bc`，stash 空 |

### 非阻断观察

1. **（评审被要求给意见）无消费方的归位是否应推迟到改键协议做进来时——我的意见是：不应推迟，现在建立接缝是对的。** 四条理由：
   (a) CK-016 的设计本身就是**如实报数**而非「必须非零」的门槛，计划的实现路线已预先写明预期为零。
   执行者如实报了 0、且**没有**为凑出调用方去动入场路径——这正是契约想要的行为，此时推迟等于反过来惩罚合规。
   (b) 成本小且有界：1 接口 + 1 实现 + 2 行注册 + 4 条用例，生产代码约 5.3KB，零调用方意味着**零行为风险、零回归面**，今天它不可能弄坏任何东西。
   (c) 即使没有调用方，价值也不为零：那四条用例是这两张表语义的第一份可执行记载——特别是
   option blob「从没存过（null）」与「存了个空的」的区别，底层 reader 刻意保留了它
   （`CharacterBodyTables.cs:109-111` 的注释），而在此之前**没有任何用例断言过它**。
   将来改键协议最容易悄悄压平的就是这类区别。
   (d) RQ-005 要的是「设置的主人」——那是结构性归属陈述。推到改键协议时做，等于让那一次改动
   同时承担接缝与协议两件事，恰是计划想避免的耦合。
   **风险面我也看了**：无消费方的接缝的典型风险是「为想象中的消费者设计错形状」。这里风险很低——
   答案类型不是新发明的，就是入场投影已经在造的 `CharacterSettingsEntryState`；且执行者
   刻意**拒绝**在协议存在前加写入动词，并把这条克制写进了接口 `<remarks>`。这是对的取舍。
   **结论：建议不做范围收窄，保留该接缝。**
2. 承上，唯一值得控制器留意的是：没有任何架构判据会注意到这个接缝长期无人调用。
   若需保险，最省的办法是在 T-009 / BD-016 的 `AGENTS.md` 记载里写明「此接缝存在且刻意暂无消费方，
   待改键协议」——那已在计划范围内，**不需要**额外的范围变更。
3. 判据强度的一点补充（见第 2 项）：若日后有人删掉 `ORDER BY slot_index`，现有用例不会报红。
   这属于既有持久化层的判据覆盖，不在本 Task 边界内，仅作记录。
4. `ICharacterSettingsDomainService.FindAsync` 的 XML 文档未提及该方法在热键值越界时会抛
   `InvalidDataException`（`CharacterSettingsDomainService.cs:45-49`）。措辞层面的小事，非缺陷。

本 verdict 只证明这一个 Task Unit 的有限缺陷检查通过，不代表完整变更评审，
也不替代接受前的新鲜验证。
