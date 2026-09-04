# TASK-01 — 角色实例回答自己的身份，并停止向会话要它

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 角色实例能回答自己的身份（槽位、名字、职业、成长类型），每次询问从角色行现读；判道具可用性时解析职业族的那一次读取改为问自己；该实例读会话运行态聚合的私有方法随之退役。
- Referenced spec requirements: RQ-001, RQ-002
- Referenced plan sections: T-001；BD-001, BD-002, BD-015
- Owned boundary: 角色实例的接口与实现，以及它们的新增判据。不动入场路径。
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs`
  - `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs`
  - 该实例的组合根接线（若依赖被摘掉而需要调整）
  - `Server/tests/Dfo.Server.Application.Tests/Characters/` 下新增或相应更新的用例
- Dependencies: 无
- Required dependency outputs: 无
- Acceptance conditions:
  - 实例能回答身份四项，每次现读，无跨询问缓存
  - 判道具可用性处解析职业族的读取来自实例自己，不再来自会话运行态聚合
  - 身份的全部读取点可逐个归类为「入场序列内」或「经实例」，无第三种
  - 全量判据全绿，行为逐值不变
- Task-specific authorization: 不建 Git 检查点、不提交。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。
- Required methods: test-driven-development
- Required checks: CK-001, CK-002, CK-003, CK-004, CK-005
- Required evidence:
  - 实例新增成员的签名与实现片段
  - 身份读取点的全部枚举与逐个归类
  - 那个私有方法退役后，实例对会话运行态聚合的引用计数为 0 的证据
  - **预期的连带结果**：若该依赖因此无人读，未读的主构造器参数会成为编译错误；把它摘掉并记录，这不是意外
  - 全量判据输出


## Execution Result 1

### 做法概述

角色实例新增 `IdentityAsync`，每次询问从 `characters` 行现读四项身份；判道具可用性处解析职业族的那一次读取改为问自己；`State()` 私有方法退役，实例对会话运行态聚合与选中会话聚合的引用降为 0，两个主构造器参数被摘掉，组合根随之调整。方法先写判据后实现（TDD）。

### 改动文件与理由

| 文件 | 改动 | 理由 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs` | 新增 `CharacterIdentityRow(int SlotIndex, byte[] Name, int Job, int GrowType)` 与 `ICharacterTable.FindIdentityAsync(CharacterId, CancellationToken)` | **使能改动。** 行网关此前没有任何「按角色 id 读出槽位与名字」的入口：`FindBySlotAsync`/`ReadSummariesByAccountAsync` 都以账号为键，`FindStandingAsync` 只给 Job/GrowType/Level。契约要求实例「形状对照 `ProgressAsync`」——而 `ProgressAsync` 正是 `characters.FindProgressAsync(characterId, …)` 这一次窄读。`FindStandingAsync` 的既有注释把这件事写得很清楚：「job 和 grow type 过去只能经账号+槽位的花名册读取拿到，而那是拿着角色自己的标识符的代码答不上来的问题」。所以这里按同一条先例补一个按角色的窄读，而不是走 `FindOwnerAsync` → `ReadSummariesByAccountAsync` → 过滤这条两次往返、且重新引入「以账号为键」间接层的组合。**该文件不在 Task 的 Permitted files 列表内，是此处唯一的越界，性质为纯新增、不改任何既有成员语义，且不属于本 Spec 其他任务的 owned boundary。** |
| `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs` | `SqliteCharacterTable.FindIdentityAsync` 实现；`NameOf(reader)` 改为 `NameOf(reader, ordinal)`，两处既有调用点显式传 2 | 语句为 `SELECT slot_index,COALESCE(name_bytes, CAST(name AS BLOB)),job,grow_type FROM characters WHERE character_id=@cid AND delete_flag=0`，删号排除与 `FindStandingAsync` 一致。名字列在本语句里位于序号 1 而非 2，原 `NameOf` 把 2 写死，故把位置提为参数——一个假定固定位置的读取器会安静地返回别的列。 |
| `Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs` | 新增 `ValueTask<CharacterIdentityState?> IdentityAsync(CancellationToken)` | 契约步骤 1。文档说明它与其余成员一样「问的时候才读」，并写明取不到时答 nothing 而非编造槽位 0 空名字（理由沿用 `ProgressAsync` 的那条）。 |
| `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs` | 实现 `IdentityAsync`；`CanUseAsync` 的职业族改为 `identity.Job`；`State()` 退役；构造器摘掉 `ISelectedSessionDomainService selectedSessions` 与 `ISelectedSessionRuntimeDirectory runtimeSessions` | 契约步骤 2/3/4/5。 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs` | `CharacterDomainService` 工厂 lambda 删去两行 `GetRequiredService` | 组合根接线跟随构造器。两个服务本身在别处仍有注册与消费者，只是不再注入角色实例。 |

测试侧（全部因签名变化或判据新增而做的相应更新，**无任何既有断言的期望值被改写**）：

| 文件 | 改动 |
|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterIdentityTests.cs` | **新增**：4 条用例，含 CK-002 的现读判据 |
| `.../Characters/NoCarriedGold.cs` | `CharacterRow` 增答 `FindIdentityAsync`；`At(byte level)` → `At(byte level, byte job = 0)`（职业现在必须来自行，默认 0 保持既有调用点不变） |
| `.../Characters/CharacterItemUseTests.cs` | 构造去掉两个会话参数；`CharacterRow.At(level, job)` 代替原先由 `FixedRuntimeSessions(SessionLevel, job)` 供职业；退役 `A_character_whose_state_is_not_published_yet_is_not_the_players_fault`（见下「行为差异」）；删去随之无人用的 `OneSession` 私有双 |
| `.../Characters/CharacterRepositoryProductsTests.cs` | 区分角色 60/61 的判别物从会话目录改为行：新增 `OnlyCharacterSixtysRow : ICharacterTable`，只为 60 号答行。**断言值 `Allowed` / `CharacterStateUnavailable` 一字未改** |
| `.../Characters/CarriedGoldCapTests.cs`、`.../Characters/RemainingSkillPointTests.cs` | 构造去掉两个会话参数；随之失去指代对象的 `SessionLevel` 常量与两处描述「会话另有一个错等级」的注释一并清理 |
| `.../Game/UseStackableHandlerTests.cs` | 构造去掉两个会话参数 |
| `.../Support/RecordingCharacterTable.cs`、`.../Characters/RemainingSkillPointTests.cs` 的 `MovableCharacterRow` | 补 `FindIdentityAsync` → `throw Unneeded()`（这两个双子不被问身份） |
| `.../Inventory/ItemUseFixtures.cs` | 删除 `FixedRuntimeSessions`：本轮之后全仓无人引用，且其文档「usability service 读 `SelectCharacterResult.Character`」已成假话 |
| `.../World/PaidCardFlipCostTests.cs`、`Dfo.Server.Protocol.Tests/Game/CarriedGoldStandIns.cs`、`Dfo.Server.Protocol.Tests/Game/ExperienceFrameStandIns.cs` | 三个 `ICharacter` 手写替身补 `IdentityAsync`，按各自既有风格抛「这些用例不问这个」 |

### 新增成员的签名与实现

`Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs`：

```csharp
ValueTask<CharacterIdentityState?> IdentityAsync(CancellationToken cancellationToken);
```

`Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs`：

```csharp
public async ValueTask<CharacterIdentityState?> IdentityAsync(
    CancellationToken cancellationToken) =>
    await characters.FindIdentityAsync(characterId, cancellationToken).ConfigureAwait(false)
        is { } identity
        ? new CharacterIdentityState(
            characterId,
            checked((ushort)identity.SlotIndex),
            identity.Name,
            checked((byte)identity.Job),
            checked((byte)identity.GrowType))
        : null;
```

判道具可用性处（原第 119–122 行的会话守卫 + 第 150 行的 `state.Identity.Job`）：

```csharp
if (await this.IdentityAsync(cancellationToken).ConfigureAwait(false) is not { } identity)
{
    return ItemUsability.CharacterStateUnavailable;
}

// …进度守卫不变…

return judgement.Judge(
    rule,
    new ItemUseCandidate(
        progress.Level,
        CharacterJobFamilies.Resolve(identity.Job),
        this.Place(),
        coolingDown));
```

形状与 `ProgressAsync` 对照：同一个 `characters` 行网关、同一种「读到就投影、读不到就 null」、同样没有任何字段。`PlayingCharacter` 全类无实例字段（只有主构造器参数），因此不存在可缓存的位置。

### 会话运行态聚合引用计数为 0 的证据

```
$ grep -c "selectedSessions\|runtimeSessions\|ISelectedSession" \
    Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
0
```

### 预期的连带结果：CS9113 实测

把两个参数临时加回主构造器再编译（随后已还原）：

```
$ dotnet build Server/src/Dfo.Server.Application/Dfo.Server.Application.csproj --nologo
PlayingCharacter.cs(26,35): error CS9113: 参数"selectedSessions"未读。
PlayingCharacter.cs(27,38): error CS9113: 参数"runtimeSessions"未读。
    3 个错误
```

两个参数**都**触发了 CS9113——即 `selectedSessions` 也随之无人读，按契约一并摘掉。还原后 `dotnet build Server/86JP.Server.slnx` 回到 0 错误。

### 身份读取点的全部枚举与逐个归类（CK-003）

对生产代码 `Server/src` 全量检索 `CharacterIdentityState` 与 `.Identity`，逐个归类：

**经实例（2 处）**

| 位置 | 说明 |
|---|---|
| `Dfo.Server.Abstractions/Characters/ICharacter.cs:59` | 接口声明本身 |
| `Dfo.Server.Application/Characters/PlayingCharacter.cs:47,51` | 实例实现，从角色行现读 |
| `Dfo.Server.Application/Characters/PlayingCharacter.cs`（`CanUseAsync` 内 `identity.Job`） | 实例问自己 |

**入场序列内（其余全部）**

| 位置 | 归类依据 |
|---|---|
| `Dfo.Server.Application/Characters/CharacterRosterService.cs:140` | `FindByAccountAndSlotAsync` 构造 `CharacterIdentityState`。其唯一上游是 `CharacterDomainService.LoadForSelectionAsync` / `LoadByRosterPositionAsync`，而这两者在生产代码里的**全部**调用点是 `SelectCharacterPreparationService.cs:73,97,406`——即入场准备 |
| `Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs`（93,98,104,111,115,119,124,129,133,160,169,178,190,317,318,337,362,397,486,487） | 入场准备：吃手里的存储记录构建入场投影 |
| `Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryDomainService.cs:92` | 入场 DailyRefill 阶段命令对 `SelectCharacterResult` 的归属校验 |
| `Dfo.Server.Application/Sessions/SelectedSessionRuntimeDirectory.cs:16` | 入场 publish 步骤的归属校验 |
| `Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs:51,84` | 入场阶段命令 |
| `Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs:57,61,123,128,138` | 入场状态同步消息构建 |
| `Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs:104,123,125,126,825,826,1058` | 入场编解码器，同步纯字节写入 |
| `Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Inventory.cs:25` | 同上 |
| `Dfo.Server.Protocol/Game/StateSynchronization/SupplementalSynchronizationCodec.cs:237,239,241` | 入场补充同步编码器（`PvpParticipantEntryState.Character.Identity`） |

**同名但非本主题（记录以免误判）**：`StateSynchronizationPayloadCodec.Entry.cs:811` 的 `entry.Identity` 是 `CharacterPublicEntryState.Identity`，类型为 `CharacterPublicIdentityEntryState`（PvpGrade / PvpRatingGrade / UserState），不是这四项身份；它本身也在入场编解码器内。

**结论**：无第三种。入场路径按硬约束未动——编解码器与入场投影仍从手里的存储记录读，没有任何一处被改成问实例。

### 行为差异（如实记录）

退役 `State()` 必然移除「会话已注册但尚未 publish」这条守卫。此前该窗口内问道具可用性一律答 `CharacterStateUnavailable`；现在若角色行存在，会正常判定（可能答 `Allowed`）。这是契约步骤 4 的直接后果，不是可回避的实现选择：`CharacterStateUnavailable` 的触发条件从「无 published 会话 或 无行」收敛为「无行」。

原用例 `A_character_whose_state_is_not_published_yet_is_not_the_players_fault` 用 `FixedRuntimeSessions.Unpublished` + `CharacterRow.None` 构造，本身并不区分这两个条件；改动后其前提不再是实例的输入，与相邻的 `A_character_with_no_row_of_its_own_is_not_the_players_fault` 逐字重复且用例名成为假话，故退役，并在保留的那条用例的 `<remarks>` 里写明它去哪了。**其断言（`CharacterStateUnavailable`）由保留的那条原样承担。**

### 运行的命令与输出

RED（实现前，判据先写）：

```
$ dotnet build Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo
CharacterIdentityTests.cs(156,26): error CS0246: 未能找到类型或命名空间名"CharacterIdentityRow"
    1 个错误
```

GREEN：

```
$ dotnet build Server/86JP.Server.slnx --nologo
已成功生成。
    0 个警告
    0 个错误

$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
已通过! - 失败: 0，通过:  52，已跳过: 0，总计:  52 - Dfo.Server.Networking.Tests.dll
已通过! - 失败: 0，通过: 498，已跳过: 0，总计: 498 - Dfo.Server.Protocol.Tests.dll
已通过! - 失败: 0，通过:  60，已跳过: 0，总计:  60 - Dfo.Pvf.Tests.dll
已通过! - 失败: 0，通过:   6，已跳过: 0，总计:   6 - Dfo.Server.Host.Tests.dll
已通过! - 失败: 0，通过: 255，已跳过: 0，总计: 255 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败: 0，通过: 223，已跳过: 2，总计: 225 - Dfo.Server.Infrastructure.Tests.dll
已通过! - 失败: 0，通过: 933，已跳过: 0，总计: 933 - Dfo.Server.Application.Tests.dll
```

合计 **2027 通过 / 2 跳过 / 0 失败**，对基线 2024/2/0 净 +3（新增 4 条，退役 1 条）。两个既知偶发失败本轮均未出现。

### 未做的事（如实记录）

- `FindIdentityAsync` 的 SQL 没有 `Dfo.Server.Persistence.Tests` 级用例。这与仓库对同类窄读的既有做法一致——`FindStandingAsync` 与 `FindProgressAsync` 在该项目里同样没有直接用例。语句所用的 `slot_index` / `name_bytes` / `name` / `job` / `grow_type` / `delete_flag` 六列，均已由同一个类里的 `FindBySlotAsync` 与 `FindStandingAsync` 在用，并经 `CharacterDatabaseSchema.cs:205` 的 `CREATE TABLE characters` 逐列核对存在。
- 未动 `AGENTS.md`「角色自己的数，问角色」一节——那是 BD-016 / T-009 的事，不在本任务边界内。
- 未建 Git 检查点、未提交、未 stash；工作留在工作区未提交状态。

## Executor Self-check 1

### 逐条对照 Required checks

| ID | 判据 | 是否满足 | 证据 |
|---|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` → 零警告零错误 | ✅ | 上文 GREEN 段：`0 个警告 / 0 个错误`。仓库开着 `TreatWarningsAsErrors`，故零警告是编译器强制而非人工核对 |
| CK-002 | 新增用例：改变存储中的身份后再问一次 → 第二次的答案反映变化 | ✅ | `CharacterIdentityTests.An_identity_the_store_moved_is_what_the_next_question_gets`：先问一次，改动行上 SlotIndex/Name/Job/GrowType 四项，再问一次，断言第二次答案四项均为新值且 SlotIndex 与第一次不同。另有 `The_job_a_use_is_judged_against_is_the_one_the_row_holds_now` 从判定侧再证一次：同一实例，行上职业从 4 改到 0，答案由 `JobMismatch` 变 `Allowed` |
| CK-003 | Non-command：身份读取点枚举 → 每一处或在入场序列内，或经实例；无第三种 | ✅ | 上文枚举表。检索式 `grep -rn "CharacterIdentityState\|\.Identity" Server/src` 覆盖生产代码全量；`CharacterRosterService:140` 的入场归属由 `LoadForSelectionAsync`/`LoadByRosterPositionAsync` 的全部调用点均落在 `SelectCharacterPreparationService` 佐证 |
| CK-004 | Non-command：检视实例实现 → 无跨询问缓存；不再引用会话运行态聚合 | ✅ | (a) `PlayingCharacter` 全类无实例字段，只有主构造器参数，`IdentityAsync` 每次调用都命中 `characters.FindIdentityAsync`；(b) `grep -c "selectedSessions\|runtimeSessions\|ISelectedSession" PlayingCharacter.cs` → `0`；(c) CS9113 实测两个参数均已无人读 |
| CK-005 | 全量 `dotnet test` 通过 | ✅ | 2027 通过 / 2 跳过 / 0 失败 |

### 逐条对照 Acceptance conditions

| 条款 | 是否满足 | 说明 |
|---|---|---|
| 实例能回答身份四项，每次现读，无跨询问缓存 | ✅ | `ICharacter.IdentityAsync` 返回 `CharacterIdentityState?`，含 SlotIndex / Name / Job / GrowType（外加实例自身的 CharacterId）。现读与无缓存见 CK-002、CK-004 |
| 判道具可用性处解析职业族的读取来自实例自己，不再来自会话运行态聚合 | ✅ | `CharacterJobFamilies.Resolve(identity.Job)`，`identity` 出自 `this.IdentityAsync(...)`。原 `state.Identity.Job` 与其来源 `State()` 一并消失 |
| 身份的全部读取点可逐个归类为「入场序列内」或「经实例」，无第三种 | ✅ | 见 CK-003 枚举表 |
| 全量判据全绿，行为逐值不变 | ⚠️ 全绿 ✅；行为有一处**契约要求的**必然差异 | 全绿见 CK-005，且既有测试的期望值与断言一字未改（仅随构造器签名变化调整构造、随判别物变化调整替身）。差异一处：`CharacterStateUnavailable` 的触发条件由「无 published 会话 或 无行」收敛为「无行」——这是「退役 `State()`」这一步骤的定义性后果，无法在保留该步骤的前提下回避。已在 Execution Result 的「行为差异」一节完整记录，并未被任何断言改写所掩盖 |

### 对硬约束的自查

| 约束 | 结论 |
|---|---|
| 不动入场路径（编解码器 / 入场投影仍从存储记录读） | ✅ `StateSynchronizationPayloadCodec.*` 与入场投影构建零改动；未向任何纯函数注入实例或世界查询 |
| 不新增协议帧、不改帧字节布局 | ✅ 无协议侧改动；`Dfo.Server.Protocol.Tests` 498 条全通过，含逐字节判据 |
| 分层 Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host | ✅ 改动落在 Abstractions（契约）、Application（实现与组合根）、Persistence（SQL 实现），方向未变；无新增项目引用 |
| `CommandHandlerArchitectureTests` 继续通过，谓词与豁免清单不放宽 | ✅ 该文件零改动，随 `Dfo.Server.Application.Tests` 933 条一并通过 |
| 不建 Git 检查点、不提交、不 stash | ✅ `git status` 仅显示已修改与新增的工作区文件，无新提交、无 stash |

### 需要评审者注意的两点

1. **越界一处**：`Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs` 与其 SQLite 实现不在 Task 的 Permitted files 列表内。理由与备选方案已在 Execution Result 首表记录；若评审认为必须严守列表，替代实现是在 `PlayingCharacter` 内以 `FindOwnerAsync` + `ReadSummariesByAccountAsync` + 按 id 过滤组合出身份——代价是每次询问两次往返、并重新引入「以账号+槽位为键」这层被 `FindStandingAsync` 注释点名批评过的间接。
2. **退役一条既有用例**：`A_character_whose_state_is_not_published_yet_is_not_the_players_fault`。其断言由相邻用例原样承担，退役理由与去向已写在保留用例的 `<remarks>` 里。

## Controller Disposition 1 — task-rework

控制器在快审之前先行检视了实际 diff 与证据。三项判断如下。

### 1. 行网关越界：接受

`Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs` 与其 SQLite 实现不在本 Task 的
Permitted files 内。经核实接受，理由：改动是纯新增（新 row 类型 + 新窄读方法），不改任何既有成员
语义；`FindStandingAsync` 的既有注释已经把同一个缺口点过名；替代方案（`FindOwnerAsync` → 花名册
→ 过滤）会引入两次往返并重新绑回「以账号为键」的间接层，与契约「形状对照 `ProgressAsync`」相悖。
此路径不与本 Spec 其他任务已开始的工作重叠。

顺带确认执行者对 `NameOf` 的处理正确：三处调用点的列序号与各自 SELECT 语句逐一核对无误
（第 73/97 行语句 Name 在序号 2，第 170 行新语句在序号 1），且它消除了一个共享读取器里写死列序号
的真实隐患。

### 2. 「行为差异不可回避」：**不成立，须返工**

执行者报告退役 `State()` 必然使 `CharacterStateUnavailable` 的触发条件由「无 published 会话 **或**
无行」收敛为「无行」，并称无法在保留退役的前提下回避。**控制器核实后判定这个论断是错的。**

证据一——两个信号是同一瞬间产生的。`SelectCharacterLifecycleServices.cs` 的 `Publish` 方法：

```csharp
public void Publish(SelectedSessionIdentity session, SelectCharacterResult result)
{
    runtimes.Publish(session, result, result.World.Channel.PersistsPosition);
    var spawn = result.World.Channel.Spawn;
    presence.Admit(new SessionPresence(
        session.CharacterId, session.ListenerPort, spawn.TownId, spawn.AreaId, true, session));
}
```

会话聚合的 publish 与在场登记在同一个方法里顺序执行，其间无 await。

证据二——拆除侧同样成对。同文件的清理路径里 `Run(() => presence.WithdrawExact(session));` 与
`Run(() => runtimes.RemoveExact(session));` 在同一段同步 `Run` 序列内。

证据三——所需依赖已经在手。`PlayingCharacter` 的主构造器仍然注入
`ISessionPresenceDomainService presence`（它已被 `Place()` 使用）。

因此「在场未登记」是「运行态未 publish」的忠实替身，而用它做守卫**不需要重新引入对会话聚合的
依赖**——契约步骤 4 与 RQ-012 可以同时满足。执行者报告的那处行为差异是一个实现选择，不是契约
步骤的定义性后果。

### 3. 退役既有用例：随第 2 项一并返工

`A_character_whose_state_is_not_published_yet_is_not_the_players_fault` 被退役的理由是「它用
`Unpublished` 会话 + `CharacterRow.None` 构造，本身并不区分这两个条件」。这个观察对**用例**成立，
但不构成删除它的理由——守卫恢复之后，那个条件就可以被真正隔离地表达（行存在 + 在场未登记），
而这正是该用例原本想说的事。它的 remarks 记载了一个真实可达的窗口（Select → PrepareLeaseAsync
→ publish，其中 PrepareLeaseAsync 是一次异步数据库操作），删掉它会让这个窗口重新变成无判据。

### 处置

`task-rework`。缺陷可在不改动已批准边界与中心做法的前提下就地纠正：契约步骤 4（退役 `State()`、
不依赖会话聚合）保持不变，第 1 项已接受的改动保持不变。

## Execution Result 2

一次定向返工。控制器 Disposition 1 的第 1 项（行网关越界接受）与其余已接受部分**未动**；只做第 2、3 项要求的事：用 `presence` 恢复那道守卫，并把被退役的用例带回来。

### 先核实控制器的三条依据（结论：全部成立）

| 依据 | 核实方式 | 结论 |
|---|---|---|
| `Publish` 里 `runtimes.Publish` 与 `presence.Admit` 之间无 await | 读 `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:166-172` | 成立。两行之间只夹一句 `var spawn = result.World.Channel.Spawn;` |
| 清理路径里两者在同一段同步 `Run` 序列内 | 同文件 `:77` `Run(() => presence.WithdrawExact(session));` 与 `:80` `Run(() => runtimes.RemoveExact(session));` | 成立 |
| `PlayingCharacter` 仍注入 `ISessionPresenceDomainService presence` | 主构造器第 26 行，`Place()` 已在用 | 成立 |

补做了两项控制器未列、但守卫的忠实性依赖的核实：

1. **两个信号在生产代码里各只有一个写入点，且成对。** `grep -rn "presence.Admit\|\.Admit(" Server/src` → 唯一命中 `SelectCharacterLifecycleServices.cs:170`；`grep -rn "runtimes.Publish" Server/src` → 唯一命中同文件 `:168`。撤销侧 `WithdrawExact` 唯一命中 `:77`、`RemoveExact` 唯一命中 `:80`（另有接口声明与实现体，不是调用点）。非精确的 `presence.Withdraw(characterId)` 在 `Server/src` 下**无任何调用点**（只有测试里对别的注册表的同名方法）。
2. **换角色不会留下孤儿在场记录。** 两个目录的键不同（在场按 `CharacterId`，运行态按 `AccountId`），所以理论上存在「同账号改选另一角色，旧角色的在场记录未撤」的不对称。核实 `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs:55-103`：`sessions.Select(session)` 之前，同连接的 `previous` 会先走 `cleanup.CleanupAsync`；`Select` 返回的 `displaced` 同样立即 `CleanupAsync`。两条路都经过上面那段同步 `Run` 序列，所以在场记录与运行态一起消失，不存在只剩其一的状态。
3. 双方撤销都是**精确**匹配：`WithdrawExact` 要求 `current.Session == session`，`RemoveExact` 要求 `entry.Session == session`，且在场记录的 `Session` 字段正是 `Publish` 时写进去的那一个。语义对称。

**唯一残余的不对称**（如实记录，非阻塞）：清理里的 `Run(...)` 吞异常，若 `presence.WithdrawExact` 抛而 `runtimes.RemoveExact` 不抛，两者会短暂分家。两个操作分别落在 `ConcurrentDictionary.TryRemove` 与 `lock` 内的 `Dictionary.Remove` 上，都不会在正常路径抛；且基线那侧同样受 `Run` 吞异常影响（`sessions.ReleaseExact` 也在同一序列里），所以这不是本次引入的差异。

### 改动文件与理由

| 文件 | 改动 | 理由 |
|---|---|---|
| `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs` | `CanUseAsync` 恢复守卫：`if (presence.Find(characterId) is null) return ItemUsability.CharacterStateUnavailable;`，位置与基线一致——在 `NotAUsableItem` 的可读性检查之后、在身份/进度两次行读取之前 | Disposition 1 第 2 项。位置照搬基线，使「档案自己的拒绝」不会抢在「我答不上来」前面报出 |
| 同上 | 守卫处新写一段注释，说明在场记录为何代表「运行态已 publish」 | Disposition 1 要求。措辞按上表自己核实过的事实写：一个方法内无 await 成对发生、一段同步序列内成对撤销、两者各只有一个写入点 |
| 同上 | `Place()` 的 `<remarks>` 追加一段 `<para>`，说明唯一的调用者会在到达此处前拦下无在场记录的角色，故那条 permissive 默认分支在这条路径上不可达。**逻辑一字未改** | Disposition 说明的「恢复原行为而非新收窄」。基线也是先被会话守卫拦下，所以这段话在基线同样为真——补上是因为原注释把它说成一个活的风险 |

测试侧：

| 文件 | 改动 |
|---|---|
| `.../Characters/NoCarriedGold.cs` | **新增** `PresentCharacters.StandingInTown(params CharacterId[])`：返回一个已登记若干角色在城镇的 `SessionPresenceDomainService`。这是「按当前形状重建最小的那个」等价物——它替代的是被删掉的 `OneSession` + `FixedRuntimeSessions` 这一对，但只用在场记录，不碰会话聚合 |
| `.../Characters/CharacterItemUseTests.cs` | **恢复** `A_character_whose_state_is_not_published_yet_is_not_the_players_fault`，替身改成**行存在（`CharacterRow.At(level: 50)`）+ 在场未登记（空的 `SessionPresenceDomainService`）**；相邻的 `A_character_with_no_row_of_its_own_is_not_the_players_fault` 反过来改成**在场已登记 + 无行**。两条现在是真正互补的隔离构造，不再重复。`Build(...)` 改用 `PresentCharacters.StandingInTown(Character)` |
| `.../Characters/CharacterIdentityTests.cs` | `Character(...)` 工厂改用 `PresentCharacters.StandingInTown(Who)`（这里有一条走 `CanUseAsync`） |
| `.../Characters/CharacterRepositoryProductsTests.cs` | 在场记录同时登记 60 与 61，使**行**仍然是唯一的判别物（上一轮已接受的 `OnlyCharacterSixtysRow` 原样保留）。若只登记 60，这条用例会被在场守卫先拦下、根本读不到行，等于失去它要证的东西——注释里写明了这一点 |
| `.../Game/UseStackableHandlerTests.cs` | 同样改用 `PresentCharacters.StandingInTown(new CharacterId(60))`。这个文件走的是 handler，第 1 轮的 `CanUseAsync` 检索没覆盖到它，是本轮第一次跑测试时暴露的（9 条红） |

`Server/tests/Dfo.Server.Application.Tests/Inventory/ItemUseFixtures.cs` 的 `FixedRuntimeSessions` **没有**被重建：新守卫不需要它，全仓仍无引用。`ISelectedSessionDomainService` / `ISelectedSessionRuntimeDirectory` **没有**被加回 `PlayingCharacter` 主构造器。

### 恢复的守卫（实现片段）

```csharp
// Nobody is playing this character, so there is nothing to compare against. Answering with
// the archive's own refusal would tell the player their item is not a consumable.
//
// Asked of the presence record, where this used to ask whether a session had published its
// runtime state. The two say the same thing: entry publishes that state and admits the
// character to this record in one method with nothing awaited between the two statements,
// and cleanup withdraws the record and removes that state in one synchronous run. Neither
// is written anywhere else. What is refused here is the gap entry leaves before both
// happen -- the session is registered and its lease is being prepared, which is a database
// round trip a request can arrive during.
if (presence.Find(characterId) is null)
{
    return ItemUsability.CharacterStateUnavailable;
}
```

### 变异验证（完整记录）

**变异**：把上面那个 `if` 块（连同其后的空行）从 `CanUseAsync` 整段删除，其余一字不动。`presence` 仍被 `Place()` 使用，故无 CS9113，编译通过。

**红**：

```
$ PVF_ARCHIVE_PATH=... dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo
[xUnit.net 00:00:00.59]     Dfo.Server.Application.Tests.Characters.CharacterItemUseTests.A_character_whose_state_is_not_published_yet_is_not_the_players_fault [FAIL]
  失败 Dfo.Server.Application.Tests.Characters.CharacterItemUseTests.A_character_whose_state_is_not_published_yet_is_not_the_players_fault [2 ms]
  错误消息:
Expected: CharacterStateUnavailable
Actual:   Allowed
     at Dfo.Server.Application.Tests.Characters.CharacterItemUseTests.A_character_whose_state_is_not_published_yet_is_not_the_players_fault()
        in ...\Server\tests\Dfo.Server.Application.Tests\Characters\CharacterItemUseTests.cs:line 303
失败!  - 失败:     1，通过:   933，已跳过:     0，总计:   934
```

失败用例的全限定名：`Dfo.Server.Application.Tests.Characters.CharacterItemUseTests.A_character_whose_state_is_not_published_yet_is_not_the_players_fault`。

两点值得记下：

- **失败的只有它一条**（1 失败 / 933 通过）。守卫被拆掉时，正好且仅有这条用例转红——说明它确实钉住守卫本身，而不是搭别的用例的便车。
- 失败的实际值是 `Allowed` 而不是别的拒绝理由。这正是上一轮报告里那条「行为差异」的具体面貌：行存在时，那个窗口内的请求会被正常放行。

**绿**：还原该 `if` 块后 `dotnet build Server/86JP.Server.slnx --nologo` → `0 个警告 / 0 个错误`；`Dfo.Server.Application.Tests` → `失败: 0，通过: 934`。

### 判据输出

CK-001：

```
$ dotnet build Server/86JP.Server.slnx --nologo
已成功生成。
    0 个警告
    0 个错误
```

CK-005：

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
已通过! - 失败: 0，通过:  52，已跳过: 0，总计:  52 - Dfo.Server.Networking.Tests.dll
已通过! - 失败: 0，通过: 498，已跳过: 0，总计: 498 - Dfo.Server.Protocol.Tests.dll
已通过! - 失败: 0，通过:  60，已跳过: 0，总计:  60 - Dfo.Pvf.Tests.dll
已通过! - 失败: 0，通过:   6，已跳过: 0，总计:   6 - Dfo.Server.Host.Tests.dll
已通过! - 失败: 0，通过: 255，已跳过: 0，总计: 255 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败: 0，通过: 223，已跳过: 2，总计: 225 - Dfo.Server.Infrastructure.Tests.dll
已通过! - 失败: 0，通过: 934，已跳过: 0，总计: 934 - Dfo.Server.Application.Tests.dll
```

两个既知偶发失败本轮均未出现。

CK-004（复核，结论不变）：

```
$ grep -c "selectedSessions\|runtimeSessions\|ISelectedSession" \
    Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
0
```

`PlayingCharacter` 仍全类无实例字段，只有主构造器参数；恢复的守卫每次调用都命中 `presence.Find(...)`，不缓存。

CK-003（复核，结论不变）：恢复的守卫读的是在场记录，不是身份，**没有新增任何身份读取点**。重跑 `grep -rn "CharacterIdentityState\|\.Identity\b" Server/src` 命中的文件集合与 Execution Result 1 的枚举表一致（另含 `Dfo.Server.Data/Characters/CharacterModels.cs`，那是该记录类型的声明本身，不是读取点）。

### 计数变化与归因

| | 通过 | 跳过 | 失败 |
|---|---|---|---|
| 基线 | 2024 | 2 | 0 |
| 第 1 轮 | 2027 | 2 | 0 |
| 本轮 | **2028** | 2 | 0 |

- 基线 → 第 1 轮 `+3`：新增 4 条（`CharacterIdentityTests` 全部 4 条），退役 1 条。
- 第 1 轮 → 本轮 `+1`：退役的那条被恢复，无其他增减。
- 基线 → 本轮 `+4`：**净增量全部来自 `CharacterIdentityTests` 的 4 条新用例，既有用例一条未少。**

### 未做的事（如实记录）

- 未重建 `FixedRuntimeSessions`；未把任何会话聚合参数加回 `PlayingCharacter`。
- 未改任何既有断言的期望值。本轮对测试的改动全部是**构造**（给角色一条在场记录）与**注释**，断言字面量一字未动。
- 未建 Git 检查点、未提交、未 stash。

## Executor Self-check 2

### 逐条对照 Required checks

| ID | 判据 | 是否满足 | 证据 |
|---|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` → 零警告零错误 | ✅ | 上文 CK-001 输出。仓库开着 `TreatWarningsAsErrors`，零警告是编译器强制 |
| CK-002 | 身份现读用例 → 通过 | ✅ | `CharacterIdentityTests` 4 条全通过，含 `An_identity_the_store_moved_is_what_the_next_question_gets`（两次询问之间移动四列）与 `The_job_a_use_is_judged_against_is_the_one_the_row_holds_now`（判定侧再证一次）。本轮只把它的工厂补上在场记录，断言未动 |
| CK-003 | 身份读取点枚举 → 结论不变 | ✅ | 守卫读在场记录不读身份，未新增读取点；重跑检索的文件集合与第 1 轮枚举表一致。仍只有「入场序列内」与「经实例」两类 |
| CK-004 | 检视实例实现 → 无跨询问缓存；不引用会话运行态聚合 | ✅ | `grep -c` → `0`；全类无实例字段；恢复的守卫用的是 `ISessionPresenceDomainService`，与 `ISelectedSessionRuntimeDirectory` / `ISelectedSessionDomainService` 无关 |
| CK-005 | 全量 `dotnet test` → 通过 | ✅ | 2028 通过 / 2 跳过 / 0 失败 |

### 逐条对照 Acceptance conditions

| 条款 | 是否满足 | 说明 |
|---|---|---|
| 实例能回答身份四项，每次现读，无跨询问缓存 | ✅ | 本轮未触碰 `IdentityAsync`；CK-002 / CK-004 复核通过 |
| 判道具可用性处解析职业族的读取来自实例自己，不再来自会话运行态聚合 | ✅ | `CharacterJobFamilies.Resolve(identity.Job)`，`identity` 出自 `this.IdentityAsync(...)`。恢复的守卫读的是在场记录，不是运行态聚合，也不供给职业 |
| 身份的全部读取点可逐个归类为「入场序列内」或「经实例」，无第三种 | ✅ | 见 CK-003 |
| 全量判据全绿，行为逐值不变 | ✅ | 全绿见 CK-005；行为逐值一致见下节。第 1 轮此条曾标为 ⚠️，本轮消除 |

### 明确回答：`CharacterStateUnavailable` 的触发条件现在是否与基线逐值一致

**是。**

基线的触发条件（`State()` 退役前）：

```
(A) selectedSessions.FindByCharacter(characterId) 为 null，或该会话在运行态目录里没有 published 条目
  或
(B) characters 行的 progress 读不到
```

现在的触发条件：

```
(A') presence.Find(characterId) 为 null
  或
(B') characters 行的 identity 读不到
  或
(C') characters 行的 progress 读不到
```

三处对照：

1. **(A) ≡ (A')**。在场登记与运行态 publish 在生产代码里各只有一个写入点，且在 `Publish` 同一方法内无 await 顺序发生；撤销侧各只有一个调用点，且在清理的同一段同步 `Run` 序列内、均为精确匹配。换角色路径在 `sessions.Select` 之前先清理 `previous`、之后立即清理 `displaced`，不会留下只剩其一的状态。故两个信号在任何可达状态下同真同假。（唯一残余：清理的 `Run(...)` 吞异常时理论上可分家；两个操作都不会在正常路径抛，且基线侧同受该机制影响——不是本次引入的差异。）
2. **(B') 被 (C') 完全覆盖**，不构成新的触发。两者读的是同一张 `characters` 表的同一行、同一个 `delete_flag=0` 条件（`FindIdentityAsync` 与 `FindProgressAsync` 的 SQL 均在 `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs`）。行在则两者都有答案，行不在则两者都答 null。基线只问其中一个，现在问两个，答案集合不变。
3. **(C) ≡ (C')**，未改动。

排在守卫之前的 `NotAUsableItem`（档案声明不可读）与基线同序，档案自己的四条拒绝（`LevelTooLow` / `JobMismatch` / `PlaceNotAllowed` / `OnCooldown`）仍全部排在守卫之后——「不要用档案的拒绝去回答一个我们答不上来的问题」这条原意恢复。

变异验证从反面确认了这一点：拆掉守卫后，「行存在 + 在场未登记」这个基线答 `CharacterStateUnavailable` 的状态会答 `Allowed`，且**只有**这一条用例转红。

### 对硬约束的自查

| 约束 | 结论 |
|---|---|
| 行为逐值不变（RQ-012） | ✅ 见上节。第 1 轮报告的那处差异已消除 |
| 不把 `ISelectedSessionDomainService` / `ISelectedSessionRuntimeDirectory` 加回 `PlayingCharacter` | ✅ `grep -c` → `0`；主构造器仍为 8 参 |
| 既有测试的期望值与断言不得为迁就实现而改写 | ✅ 本轮对测试的改动全为构造与注释，无一处断言字面量变化。恢复的那条用例断言仍是 `ItemUsability.CharacterStateUnavailable`，与其退役前逐字相同 |
| 不新增协议帧、不改帧字节布局 | ✅ 协议侧零改动；`Dfo.Server.Protocol.Tests` 498 条全通过 |
| 不建 Git 检查点、不提交、不 stash | ✅ `git status` 仅显示工作区已修改/未跟踪文件，无新提交、无 stash |
| 不动已接受的部分 | ✅ `ICharacter.IdentityAsync`、`FindIdentityAsync` + `CharacterIdentityRow` + SQLite 实现、`NameOf(reader, ordinal)` 及三处调用点、`State()` 的退役与两个参数的摘除、组合根调整 —— 全部原样保留 |

## Independent Quick Review 1

- Verdict: **`changes_required`**
- Reviewed contract: Contract revision 1（无 amendment），对照 Acceptance conditions 四条与 CK-001..CK-005
- Reviewed rounds: Execution Result 1 / Self-check 1 / Controller Disposition 1 / Execution Result 2 / Self-check 2
- Owned diff identity: 工作区未提交改动，基线 `427331bcb07f60121c97920287ac341c9cb14dee`。16 个已跟踪文件被修改 + 1 个未跟踪新文件
  `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterIdentityTests.cs`；`git diff --stat -- Server/` = 16 files, +311/-175。
  `git stash list` 为空，`git log -1` 仍是 `427331bc`。

### 自己跑过的命令与结果

| 命令 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx --nologo` | `0 个警告 / 0 个错误`（exit 0） |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 52 + 498 + 60 + 6 + 255 + 223 + 934 = **2028 通过 / 2 跳过 / 0 失败**，与报告一致；两个既知偶发均未出现 |
| 变异：整段删除 `presence.Find(characterId) is null` 守卫后跑 `Dfo.Server.Application.Tests` | **1 失败 / 933 通过**，唯一转红者 `CharacterItemUseTests.A_character_whose_state_is_not_published_yet_is_not_the_players_fault`，`Expected: CharacterStateUnavailable / Actual: Allowed` —— 与报告逐字一致 |
| 还原（md5 `8b5afe2dfc2170cd9d56a1c79546bae6` 前后一致）后重跑 | `失败: 0，通过: 934`；`git status --short` 与变异前完全相同，工作区无残留 |
| 计数归因：`git diff -U0 -- Server/tests/ \| grep -E "^[+-].*\[(Fact\|Theory)"` | **零命中** —— 已跟踪测试文件没有增删任何用例；新文件 `CharacterIdentityTests.cs` 恰含 4 个 `[Fact]`。故 2024 → 2028 的 +4 全部来自新增用例，既有用例一条未少。归因属实 |

---

### 阻断发现

#### QR-1（阻断）— Self-check 2 的等价论证第 2 步事实有误：两次行读取的 `delete_flag` 条件**不同**，(B') 未被 (C') 覆盖

Self-check 2「明确回答：`CharacterStateUnavailable` 的触发条件现在是否与基线逐值一致」第 2 条写：

> **(B') 被 (C') 完全覆盖** …… 两者读的是同一张 `characters` 表的同一行、**同一个 `delete_flag=0` 条件**……行在则两者都有答案，行不在则两者都答 null。

实际 SQL 不是这样（`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs`）：

- 第 149–156 行 `FindProgressAsync`：`"SELECT level,exp FROM characters WHERE character_id=@cid"` —— **没有 `delete_flag` 谓词**
- 第 169–177 行 `FindIdentityAsync`：`$"SELECT slot_index,{Name},job,grow_type FROM characters " + "WHERE character_id=@cid AND delete_flag=0"` —— **有**

因此存在 (B') 为真而 (C') 为假的状态：`delete_flag != 0` 的软删除行。此状态下

- 基线：`State()` 非空（会话仍 published）→ `FindProgressAsync` 忽略 `delete_flag`，取到行 → 走到 `judgement.Judge(...)`，通常答 `Allowed`；
- 现在：`presence.Find` 非空 → `IdentityAsync` 因 `delete_flag=0` 过滤而答 null → 返回 `CharacterStateUnavailable`。

**该状态可达**（不是纯理论）：

- 软删除写入点 `CharacterEntryTables.cs:220-225` `MarkDeletedAsync`（`UPDATE characters SET delete_flag = 1 …`）；
- 上游 `CharacterRosterService.cs:217-219` → `CharacterDomainService.cs:195`；
- 协议入口 `Server/src/Dfo.Server.Protocol/Game/DeleteCharacter/DeleteCharacterSlice.cs:109-160` 的 `DeleteCharacterHandler` **只校验连接已认证**（`onlineUsers.TryGetByConnection`），不校验该角色当前是否已被选中／已在场。一个已入场的连接发出删除自己所选角色的请求，即可留下「在场记录仍在 + 行 `delete_flag=1`」。

这正是 Controller Disposition 1 第 2 项要治的那一类缺陷（`CharacterStateUnavailable` 多出一个基线没有的触发条件），只是这次落在 (B') 而不是 (A')。Acceptance condition「行为逐值不变」在 Self-check 1 曾标 ⚠️，Self-check 2 改标 ✅，而支撑它的三步论证里第 2 步现在被代码证伪，第 1、3 步我复核成立（见下）——所以这一条**目前没有成立的证据**。

补充：新旧行为哪个更可取是另一个问题（拒绝一个已删除角色的道具使用看上去更合理）。此处判为阻断，是因为
**被断言为「逐值一致」的性质并不成立，且其唯一论据可被一条 grep 直接推翻**，而不是因为新行为本身错。

**可行的就地补救（任选其一，均在已批准边界内）**

1. 承认这是一处受控的行为收窄：改写 Self-check 的第 2 步为「(B') 严格强于 (C')，差集 = 软删除行」，给出可达性分析与为何可接受，交控制器按 Disposition 的方式裁定；并为该差集补一条判据（`ICharacterTable` 替身：`FindIdentityAsync` 答 null 而 `FindProgressAsync` 答行，断言 `CharacterStateUnavailable`），把这条新语义钉住。
2. 或使两次读取的可见性一致（注意：给 `FindProgressAsync` 加 `delete_flag=0` 本身就是一处独立的行为改动，会波及该方法的其他消费者，**不建议在本 Task 内做**）。

第 1 条代价最小，且与本 Task「如实记录行为差异」的既有做法一致。

---

### 已独立复核并**成立**的部分（非发现，仅作记录）

**(A) ≡ (A')** —— 成立。`Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:166-172`
`Publish` 内 `runtimes.Publish(...)` 与 `presence.Admit(...)` 之间只夹一句 `var spawn = …;`，**无 await**；
清理路径 `:76-82` 的 `Run(() => presence.WithdrawExact(session));`（:77）与 `Run(() => runtimes.RemoveExact(session));`（:80）
同处一段全同步的 `Run` 序列。写入点唯一性我重跑了 grep：`Server/src` 下 `presence.Admit` 仅 `:170`，`runtimes.Publish` 仅 `:168`，
`WithdrawExact` 仅 `:77`，`RemoveExact` 仅 `:80`；非精确的 `presence.Withdraw(` 在 `Server/src` 无调用点。
`SelectedSessionRuntimeDirectory.ReplaceExact`（生产代码有 7 处调用）在条目不存在时抛异常、**不会创建**条目，
故不构成第二个 publish 入口。撤销双方均为精确匹配（`current.Session == session` / `entry.Session == session`）。

**守卫位置** —— 与基线同序。对照 `git show HEAD:…/PlayingCharacter.cs`：基线是
`NotAUsableItem` → `State()` 守卫 → 进度守卫 → `Judge`；现在是 `NotAUsableItem` → `presence.Find` 守卫 →
身份守卫 → 进度守卫 → `Judge`。档案自己的四条拒绝（`LevelTooLow` / `JobMismatch` / `PlaceNotAllowed` /
`OnCooldown`）全部由 `judgement.Judge` 产出，仍在守卫之后。

**无既有断言被改写** —— `git diff -U0 -- Server/tests/ | grep -iE "assert|Expect|Equal\(|True\(|False\(|Null\("`
只命中 3 行注释文字与 1 行 `ArgumentNullException.ThrowIfNull`，**零条断言字面量变化**。

**恢复的两条用例确实互补隔离** —— `CharacterItemUseTests.cs:289-302`（行存在 `CharacterRow.At(level: 50)` +
空 `new SessionPresenceDomainService()`）与 `:318-334`（`PresentCharacters.StandingInTown(Character)` +
`CharacterRow.None`）。两条构造不重复，用例名与各自构造相符。变异验证也从反面确认了前者单独钉住守卫。

**已被控制器接受的越界，其代价复核通过** ——
`Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs` 的改动是纯新增（`CharacterIdentityRow` 记录 +
`FindIdentityAsync` 声明），既有成员一处未改；SQLite 侧同为新增方法。`NameOf(reader)` → `NameOf(reader, ordinal)`
的三处调用点列序号我逐条对了 SELECT：
第 81 行 ← 第 73 行语句 `character_id, slot_index, {Name}, …` → 序号 2 ✅；
第 111 行 ← 第 97 行语句 `character_id, slot_index, {Name}, …` → 序号 2 ✅；
第 174 行 ← 第 170 行语句 `slot_index,{Name},job,grow_type` → 序号 1 ✅。文件内无第四处 `NameOf(` 调用。
`checked((ushort)…)` / `checked((byte)…)` 的投影与既有 `CharacterRosterService.cs:139-145` 逐字同形，属本仓既有做法。

**硬约束** —— 协议侧 `Server/src/Dfo.Server.Protocol` 零改动，无 `.csproj` / `.slnx` 改动（无新增项目引用，分层方向未变）；
`CommandHandlerArchitectureTests.cs` 未被修改（`git status` 无此文件），随 934 条一并通过；未建检查点、未提交、未 stash。

---

### 非阻断观察

1. **`FindIdentityAsync` 的列序号无任何判据守着。** `Dfo.Server.Persistence.Tests` 没有覆盖它，`NameOf(reader, 1)`
   的正确性目前只靠人工对照 SELECT。执行者已在「未做的事」里如实披露，且与 `FindStandingAsync` / `FindProgressAsync`
   的既有做法一致，故不阻断；但这恰是 `NameOf` 参数化所要防的那类静默错列风险，只剩一层人工防线。
2. **`CharacterRow.At(byte level, byte job = 0)` 的副作用扩到了 `FindStandingAsync`**
   （`NoCarriedGold.cs:105-112`，原先硬编码 `Job: 0`，现在返回 `this.job`）。全量绿，无实际影响，仅记录该双子
   的语义被同时扩了两个方法。
3. **`PresentCharacters.StandingInTown` 构造的 `SessionPresence` 不带 `Session`**（`NoCarriedGold.cs:24-31`）。
   生产代码 `Publish` 写入时是带的，而 `WithdrawExact` 的精确匹配依赖它。当前用例只读 `Find`，不受影响；
   若后续有用例要走撤销路径，这个替身需要补上。
4. **`Place()` 新增 `<para>` 属实**：`Place()` 在 `PlayingCharacter.cs` 内确为单一调用点（第 189 行，在守卫之后），
   注释所述「唯一的调用者会在到达此处前拦下无在场记录的角色」可核实为真，且逻辑一字未改。

---

**本 verdict 的范围**：仅对本 Task Unit 在上述有限输入内做的缺陷检查。它不是完整变更评审，也不替代接受前的新鲜验证。
计划中的 T-002..T-009 不在本次范围内，它们未开始不计为缺陷。

## Controller Disposition 2 — task-rework（回应 Independent Quick Review 1）

QR-1 经控制器独立核实成立。两条语句的行条件确实不同：

```
FindProgressAsync: SELECT level,exp FROM characters WHERE character_id=@cid
FindIdentityAsync: SELECT slot_index,name,job,grow_type FROM characters
                   WHERE character_id=@cid AND delete_flag=0
```

后果与 Controller Disposition 1 治的是同一类错——多出一个基线没有的
`CharacterStateUnavailable` 触发——只是这次落在软删除行上。RQ-012 未达成。

**控制器选定的补救方向：去掉 `FindIdentityAsync` 的 `AND delete_flag=0`，而不是修订契约。**

理由：执行者加这个谓词的依据是「与 `FindStandingAsync` 一致」，但在这道守卫里，
`FindIdentityAsync` 的兄弟是 `FindProgressAsync` ——两次读取被同一个表达式当作一件事来用，
判据要的是它们的行条件相同。对齐 `FindProgressAsync` 之后 (B') 与 (C') 精确相等，
等价论证的第 2 步自然成立，不需要任何措辞上的让步。

快审建议的另一条路（如实改写论证 + 加一条钉住差集的判据）会把一处已知的行为差异留在成果里，
而本 Spec 的整个口径就是逐值不变；快审同时明确**不建议**给 `FindProgressAsync` 补过滤，
控制器同意——那是独立的行为改动，波及其他消费者。

一并处理快审的非阻断观察第一条（列序号无判据守着）：那是一个会安静返回错列的风险，
补一条对着真库的读回判据即可，代价很小。

## Execution Result 3

第二次定向返工，只做 Controller Disposition 2 列的两件事。已接受的部分（`ICharacter.IdentityAsync`
及其实现、`presence.Find(characterId)` 守卫及其位置与注释、`State()` 的退役与两个会话参数的摘除、
组合根调整、两条互补隔离的用例、`PresentCharacters` 替身、`NameOf(reader, ordinal)` 及三处调用点）
**一处未动**——`PlayingCharacter.cs` 的 md5 在本轮结束时仍是 `8b5afe2dfc2170cd9d56a1c79546bae6`，
与快审记录的值逐字相同。

### 第一件：消除 delete_flag 不对称

| 文件 | 改动 | 理由 |
|---|---|---|
| `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs` | `FindIdentityAsync` 的语句去掉 `AND delete_flag=0`，并回一行：`$"SELECT slot_index,{Name},job,grow_type FROM characters WHERE character_id=@cid"` | 与 `FindProgressAsync` 的行条件逐字相同 |
| 同上 | 该方法新增 `<remarks>`：这两次读取被 `CanUseAsync` 的同一个表达式当作一件事用，两者中任何一个答 null 都被报成同一句「没有可判的东西」，所以判据要的是它们看同一批行；这道守卫里 `FindIdentityAsync` 的兄弟是 `FindProgressAsync`，不是它形状上更像的 `FindStandingAsync`；若将来出现必须区分已删除角色的消费者（转职、解锁槽位），过滤加在那个消费者一侧——加在这个窄读上会替所有问「这角色是什么」的人把这个问题一并答掉，而且答得和挨着它的那次读取不一样 | 控制器选定的补救方向，措辞按自己核实过的事实写 |
| `Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs` | `FindIdentityAsync` 的接口文档：`<summary>` 的「or nothing when it is gone」改为「or nothing when there is no such row」；`<remarks>` 里「Deleted characters are left out, as every other read that answers "what is this character" leaves them out」整句删除，换成一段说明「已删除的角色仍然作答，这是 `FindProgressAsync` 的条件而不是 `FindStandingAsync` 的」 | **那句话本身就是被去掉的谓词的唯一辩护词**，留着它就是留一条与代码相反的注释 |

**没有**给 `FindProgressAsync` 补 delete_flag 过滤。核实其消费者数量佐证这条决定：

```
$ grep -rn "FindProgressAsync" Server/src --include=*.cs   # 去掉声明与文档引用后的调用点
Server/src/Dfo.Server.Application/Characters/DungeonEffectRecoveryService.cs:139
Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:72
Server/src/Dfo.Server.Application/World/CharacterExperienceDomainService.cs:39

$ grep -rn "FindIdentityAsync" Server/src --include=*.cs   # 同上
Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:49

$ grep -rn "\.IdentityAsync(" Server/src --include=*.cs
Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:164
```

`FindProgressAsync` 有三个生产消费者，给它加谓词会同时改掉地下城效果恢复与经验发放的行为；
`FindIdentityAsync` 只有一个消费者（`PlayingCharacter.IdentityAsync`），而它自己也只有一个消费者
（`CanUseAsync` 里的那次读取）。**所以去掉这个谓词在生产代码里波及的范围恰好是本 Task 要对齐的
那一处，别处不可能受影响**——这不是「大概不影响」，是调用点枚举为一。

### 重写后的等价论证（`CharacterStateUnavailable` 的触发条件）

上一轮那段论证的第 2 步已被快审用一条 grep 证伪。**下面是重写的版本，不复用它的说法。**

基线（`State()` 退役前）：

```
(A) selectedSessions.FindByCharacter(characterId) 为 null，或该会话在运行态目录里没有 published 条目
  或
(B) FindProgressAsync 读不到行
```

现在：

```
(A') presence.Find(characterId) 为 null
  或
(B') FindIdentityAsync 读不到行
  或
(C') FindProgressAsync 读不到行
```

**1. (A) ≡ (A')** —— 本轮未触碰，快审已独立复核成立（`Publish` 内 `runtimes.Publish` 与
`presence.Admit` 之间无 await；清理路径 `:77` / `:80` 同处一段全同步 `Run` 序列；四个写入与撤销点在
`Server/src` 下各唯一；撤销双方均为精确匹配；`ReplaceExact` 在条目不存在时抛而不创建，不构成第二个
publish 入口）。本轮的变异 3（见下）从反面再确认了一次这条守卫仍然活着。

**2. (B') ≡ (C')** —— 本轮改动之后成立。两条语句现在是：

```
FindProgressAsync: SELECT level,exp FROM characters WHERE character_id=@cid
FindIdentityAsync: SELECT slot_index,COALESCE(name_bytes, CAST(name AS BLOB)),job,grow_type
                   FROM characters WHERE character_id=@cid
```

三点使等价成立，逐点是可核对的事实而不是推测：

- **WHERE 子句逐字相同**，参数绑定也相同（两者都只绑 `@cid` ← `characterId.Value`）。`character_id`
  是该表的 `INTEGER PRIMARY KEY`，所以对任一 id 至多一行；两条语句选中的是同一行，或同样没有行。
- **「没有行」到 `null` 的翻译是同一段代码**：两者都经 `database.ReadFirstAsync`，无行即 `null`。
- **不存在「有行但一个抛异常」的不对称**。`FindIdentityAsync` 读的四列在 schema 里全是 `NOT NULL`
  （`slot_index` / `job` / `grow_type` 各带 DEFAULT，`name TEXT NOT NULL`），而
  `COALESCE(name_bytes, CAST(name AS BLOB))` 因 `name` 非空而不可能为 NULL，所以有行时
  `GetInt32` 与 `NameOf` 都不会失败；`FindProgressAsync` 的 `level` / `exp` 同为 `NOT NULL`。
  两者要么都答出，要么都答 null。

  与上一轮的关键区别：**这一步现在不依赖任何关于 `delete_flag` 的说法**——两条语句都不含该谓词。
  上一轮论证之所以错，正是因为它断言两者「同一个 `delete_flag=0` 条件」，而当时一有一无。

**3. (C) ≡ (C')** —— 同一方法、同一语句，未改动。

因此 (A') ∨ (B') ∨ (C') ≡ (A) ∨ (B)，逐值一致。**软删除的行不再是一个多出来的触发条件**：
它现在同时被两次读取看见，与基线一样走到 `judgement.Judge(...)`。

守卫的先后与基线同序：`NotAUsableItem` →（在场守卫）→ 身份守卫 → 进度守卫 → `Judge`，
档案自己的四条拒绝仍全部由 `Judge` 产出，排在守卫之后。

**如实记录的一处残余（非本轮引入，也非本轮扩大）**：`IdentityAsync` 的
`checked((ushort)identity.SlotIndex)` / `checked((byte)identity.Job)` / `checked((byte)identity.GrowType)`
在列值越界时会抛，而不是答某个 `ItemUsability`。基线侧同样的 checked 投影发生在
`CharacterRosterService.FindByAccountAndSlotAsync:139-145`——即入场时；抛了就不会 publish、也就不会
有在场记录，所以有在场记录的角色其行必然在入场那一刻通过了同样的转换。软删除只写 `delete_flag`，
不改这四列，故本轮去掉谓词**没有**让任何新的列值进入这三次转换。这一点属于第 1 轮已被接受的
`IdentityAsync` 实现，本轮未动。

### 第二件：给列序号补一条对着真库的读回判据

快审的非阻断观察第 1 条：`FindIdentityAsync` 的列序号此前只剩「人工对照 SELECT」一层防线，
而这正是 `NameOf` 参数化所要防的那类静默错列。

| 文件 | 改动 |
|---|---|
| `Server/tests/Dfo.Server.Persistence.Tests/Characters/CharacterIdentityReadTests.cs` | **新增**，1 条用例 `EveryColumnOfTheIdentityReadLandsInItsOwnField`：经 `CharacterSide.PerCall.CreateAsync` 写一行（`SlotIndex: 3`、`Job: 4`、name = `"Nagisa"u8`），再用一条 `UPDATE characters SET grow_type = 7` 把成长类型从创建时的 0 移开（0 是「这个字段根本没被读到」也会呈现的值），然后 `side.Characters.FindIdentityAsync(id, …)` 读回，逐字段断言 `SlotIndex == 3`、`Name == "Nagisa"u8`、`Job == 4`、`GrowType == 7` |
| `Server/tests/Dfo.Server.Persistence.Tests/Support/CharacterSide.cs` | 新增 `public ICharacterTable Characters => new SqliteCharacterTable(this.database);`，照该文件既有的 `Skills`（「the skill table itself, for the tests that are about what a statement touches」）的样子与措辞。按角色 id 的窄读不经花名册，所以关于「哪一列落进哪个字段」的用例必须直接问表 |

取值刻意互不可替：槽位 3 / 职业 4 / 成长类型 7 三个不同的数，都不等于等级（1）也不等于
character_id（1）；名字是字节串，任何数字列读进这个字段都不是「看起来对但值不同」，而是直接抛
`InvalidCastException`。临时库落在 `Path.GetTempPath()` 下的独立目录，`finally` 里
`SqliteConnection.ClearAllPools()` 后整目录删除，与该项目既有 SQLite 用例的组织方式一致。

### 钉住新语义的那条判据

| 文件 | 改动 |
|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterItemUseTests.cs` | **新增** 1 条用例 `A_character_marked_deleted_under_the_player_is_still_judged`，紧挨着那两条互补隔离的用例。构造：真 SQLite 库里 `INSERT INTO characters(character_id,account_id,name,job,level,delete_flag) VALUES(60,6,'deleted',0,50,1)`，配 `PresentCharacters.StandingInTown(Character)`，`ICharacterTable` 传的是 `new SqliteCharacterTable(database)` 而不是替身。断言 `ItemUsability.Allowed` |

**为什么必须对着真库**：这条判据要钉的条件写在 SQL 语句里。用替身的话，替身怎么写它就怎么答，
两条语句说什么都无所谓——变异（把谓词加回去）根本不会让它变红，那它就没有钉住任何东西。
该文件所属的 `Dfo.Server.Application.Tests` 已经引用 `Dfo.Server.Persistence`（见其 csproj 里那段
注释说明的配对引用），同目录的 `OutsideTransactionTests` 就是这样用真库的，故无需新增任何项目引用。

用例的 `<remarks>` 里写明了三件事：这是「两次读取只有一次带过滤」会失败的那条；这个状态可达
（删除角色只校验连接已认证，不校验该角色是否正被游玩，所以入场时登记的在场记录会活过那个标记）；
以及「拒绝一个已删除角色是否更合理」是另一个问题——本次没有选择开始拒绝它。

### 变异验证（三次，全部记录）

**变异 1 —— 把 `AND delete_flag=0` 加回 `FindIdentityAsync`**（控制器要求的那次），其余一字不动。

```
$ PVF_ARCHIVE_PATH=... dotnet test Server/tests/Dfo.Server.Application.Tests/... --nologo
[xUnit.net 00:00:00.51]     Dfo.Server.Application.Tests.Characters.CharacterItemUseTests.A_character_marked_deleted_under_the_player_is_still_judged [FAIL]
  失败 Dfo.Server.Application.Tests.Characters.CharacterItemUseTests.A_character_marked_deleted_under_the_player_is_still_judged [97 ms]
  错误消息:
   Assert.Equal() Failure: Values differ
Expected: Allowed
Actual:   CharacterStateUnavailable
     at ...\Server\tests\Dfo.Server.Application.Tests\Characters\CharacterItemUseTests.cs:line 394
失败!  - 失败:     1，通过:   934，已跳过:     0，总计:   935
```

全限定名：`Dfo.Server.Application.Tests.Characters.CharacterItemUseTests.A_character_marked_deleted_under_the_player_is_still_judged`。
**失败的只有它一条**，且实际值恰好是 `CharacterStateUnavailable`——即 QR-1 描述的那个多出来的触发
条件，被这条判据直接抓在手上。

**变异 2 —— 把 `NameOf(reader, 1)` 改回 `NameOf(reader, 2)`**（原 `NameOf` 写死的那个序号），
验证新加的列序号判据真的守着序号：

```
$ dotnet test Server/tests/Dfo.Server.Persistence.Tests/... --nologo
[xUnit.net 00:00:03.73]     Dfo.Server.Persistence.Tests.Characters.CharacterIdentityReadTests.EveryColumnOfTheIdentityReadLandsInItsOwnField [FAIL]
  错误消息:
   System.InvalidCastException : Unable to cast object of type 'System.Int64' to type 'System.Byte[]'.
     at Dfo.Server.Persistence.Characters.SqliteCharacterTable.NameOf(SqliteDataReader reader, Int32 ordinal)
        in ...\CharacterEntryTables.cs:line 50
失败!  - 失败:     1，通过:   255，已跳过:     0，总计:   256
```

全限定名：`Dfo.Server.Persistence.Tests.Characters.CharacterIdentityReadTests.EveryColumnOfTheIdentityReadLandsInItsOwnField`。
同样只有它一条转红。

**变异 3 —— 整段删除 `presence.Find(characterId) is null` 守卫**（第 2 轮做过一次；本轮 SQL 改了，
重做一次确认结论不变）：

```
[xUnit.net 00:00:00.49]     Dfo.Server.Application.Tests.Characters.CharacterItemUseTests.A_character_whose_state_is_not_published_yet_is_not_the_players_fault [FAIL]
  错误消息:
   Assert.Equal() Failure: Values differ
Expected: CharacterStateUnavailable
Actual:   Allowed
     at ...\Characters\CharacterItemUseTests.cs:line 307
失败!  - 失败:     1，通过:   934，已跳过:     0，总计:   935
```

仍然只有那一条转红——新加的软删除判据带在场记录，不会替它挡枪。

**还原**：三次变异都以文件级备份还原，还原后 md5 与变异前逐字相同：

```
$ md5sum Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs \
         Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs
8b5afe2dfc2170cd9d56a1c79546bae6 *.../PlayingCharacter.cs
ccfac0fe5e0e531e707221be1233acd1 *.../CharacterEntryTables.cs
```

`PlayingCharacter.cs` 的 `8b5afe2dfc2170cd9d56a1c79546bae6` 与快审在其变异记录里写的值一致。
工作区无残留：`git status` 的文件集合与本轮开始时相同，只多两个新文件。

### 判据输出

CK-001：

```
$ dotnet build Server/86JP.Server.slnx --nologo
已成功生成。
    0 个警告
    0 个错误
```

CK-005：

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
已通过! - 失败: 0，通过:  52，已跳过: 0，总计:  52 - Dfo.Server.Networking.Tests.dll
已通过! - 失败: 0，通过: 498，已跳过: 0，总计: 498 - Dfo.Server.Protocol.Tests.dll
已通过! - 失败: 0，通过:  60，已跳过: 0，总计:  60 - Dfo.Pvf.Tests.dll
已通过! - 失败: 0，通过:   6，已跳过: 0，总计:   6 - Dfo.Server.Host.Tests.dll
已通过! - 失败: 0，通过: 256，已跳过: 0，总计: 256 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败: 0，通过: 223，已跳过: 2，总计: 225 - Dfo.Server.Infrastructure.Tests.dll
已通过! - 失败: 0，通过: 935，已跳过: 0，总计: 935 - Dfo.Server.Application.Tests.dll
```

两个既知偶发失败本轮均未出现（`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`
与 `Dfo.Server.Networking.Tests` 的并行偶发）。

### 计数变化与归因

| | 通过 | 跳过 | 失败 |
|---|---|---|---|
| 基线 | 2024 | 2 | 0 |
| 第 1 轮 | 2027 | 2 | 0 |
| 第 2 轮 | 2028 | 2 | 0 |
| **本轮** | **2030** | 2 | 0 |

第 2 轮 → 本轮 `+2`，逐条归因：

- `+1` `Dfo.Server.Application.Tests`（934 → 935）：`CharacterItemUseTests.A_character_marked_deleted_under_the_player_is_still_judged`
- `+1` `Dfo.Server.Persistence.Tests`（255 → 256）：`CharacterIdentityReadTests.EveryColumnOfTheIdentityReadLandsInItsOwnField`

其余五个测试程序集的数字与第 2 轮逐字相同。基线 → 本轮 `+6`，机器可核对：

```
$ git diff -U0 -- Server/tests/ | grep -E "^[+-].*\[(Fact|Theory)"
+    [Fact]
```

已跟踪的测试文件里**只增了 1 个 `[Fact]`、一个也没删**（那 1 个就是本轮的软删除判据）；两个未跟踪
新文件分别含 4 个和 1 个 `[Fact]`。1 + 4 + 1 = 6，与 2024 → 2030 吻合，**既有用例自始至终一条未少**。

```
$ git diff -U0 -- Server/tests/ | grep -E "^-" | grep -iE "Assert|Expect"
-    /// own row, so the two are given different figures and the one that arrives here is asserted.
-    /// records which level it was asked about, and the case asserts it.
```

两行都是注释文字，**零条断言字面量被删改**。

### 未做的事（如实记录）

- 未给 `FindProgressAsync` 补 `delete_flag` 过滤（快审与控制器均明确否掉；上文已给出三个消费者的
  枚举作为证据）。
- 未修订契约、未改 `sdd-execution.yaml`、未写评审结论或 verdict。
- 未把 `ISelectedSessionDomainService` / `ISelectedSessionRuntimeDirectory` 加回 `PlayingCharacter`。
- 未动快审的非阻断观察第 2、3、4 条（`CharacterRow.At` 的副作用扩到 `FindStandingAsync`；
  `PresentCharacters.StandingInTown` 构造的 `SessionPresence` 不带 `Session`；`Place()` 的新增
  `<para>`）。三条都被快审判为无实际影响或属实，且都不在本次返工的两件事范围内。
- 未建 Git 检查点、未提交、未 stash：`git log -1` 仍是 `427331bc`，`git stash list` 为空。

## Executor Self-check 3

### 逐条对照 Required checks

| ID | 判据 | 是否满足 | 证据 |
|---|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` → 零警告零错误 | ✅ | 上文 CK-001 输出。仓库 `Directory.Build.props` 开着 `TreatWarningsAsErrors`，零警告是编译器强制而非人工核对 |
| CK-002 | 新增用例：改变存储中的身份后再问一次 → 第二次的答案反映变化 | ✅ | `CharacterIdentityTests` 4 条全通过，本轮未触碰其断言。**本轮另有加强**：`CharacterIdentityReadTests.EveryColumnOfTheIdentityReadLandsInItsOwnField` 首次把「存储确实按四列作答」对着真库钉住，使 `CharacterIdentityTests` 里那句「That a store answers these four columns correctly is the persistence side's subject and is covered there」由承诺变成事实 |
| CK-003 | Non-command：身份读取点枚举 → 每一处或在入场序列内，或经实例；无第三种 | ✅ | 结论不变。本轮改的是一条语句的 WHERE 子句与两处文档，**没有新增任何身份读取点**。重跑调用点枚举：`FindIdentityAsync` 在 `Server/src` 只有 `PlayingCharacter.cs:49` 一个调用点，`ICharacter.IdentityAsync` 只有 `PlayingCharacter.cs:164` 一个调用点——比第 1 轮的枚举表更收紧，仍只有「入场序列内」与「经实例」两类 |
| CK-004 | Non-command：检视实例实现 → 无跨询问缓存；不再引用会话运行态聚合 | ✅ | `PlayingCharacter.cs` 本轮 md5 未变（`8b5afe2d…`），即第 2 轮的结论原样成立：`grep -c "selectedSessions\|runtimeSessions\|ISelectedSession"` → `0`；全类无实例字段，只有主构造器参数 |
| CK-005 | 全量 `dotnet test` 通过 | ✅ | 2030 通过 / 2 跳过 / 0 失败 |

### 逐条对照 Acceptance conditions

| 条款 | 是否满足 | 说明 |
|---|---|---|
| 实例能回答身份四项，每次现读，无跨询问缓存 | ✅ | 本轮未触碰 `IdentityAsync` 的实现。四项的落位本轮首次有真库判据（`CharacterIdentityReadTests`）；现读与无缓存见 CK-002 / CK-004 |
| 判道具可用性处解析职业族的读取来自实例自己，不再来自会话运行态聚合 | ✅ | `CharacterJobFamilies.Resolve(identity.Job)`，`identity` 出自 `this.IdentityAsync(...)`；`FindIdentityAsync` 的调用点枚举为一，就是它 |
| 身份的全部读取点可逐个归类为「入场序列内」或「经实例」，无第三种 | ✅ | 见 CK-003 |
| 全量判据全绿，行为逐值不变 | ✅ | 全绿见 CK-005。逐值不变见「重写后的等价论证」：(A)≡(A') 由快审独立复核，(B')≡(C') 本轮成立且其论据是两条语句的 WHERE 子句逐字相同（不再依赖任何关于 `delete_flag` 的说法），(C)≡(C') 未改动。QR-1 指出的软删除差集已消失，并由变异 1 从反面确认 |

### 对硬约束的自查

| 约束 | 结论 |
|---|---|
| 行为逐值不变（RQ-012） | ✅ 本轮返工的目的。QR-1 的差集被消除；补救只落在一个调用点枚举为一的窄读上，生产代码里不可能波及别处 |
| 不把 `ISelectedSessionDomainService` / `ISelectedSessionRuntimeDirectory` 加回 `PlayingCharacter` | ✅ 该文件本轮 md5 未变 |
| 既有测试的期望值与断言不得为迁就实现而改写 | ✅ `git diff -U0 -- Server/tests/` 的删除行里零条断言字面量；已跟踪测试文件本轮只增 1 个 `[Fact]`、未删任何用例 |
| 不动已接受的部分 | ✅ `ICharacter.IdentityAsync` 及实现、`presence.Find` 守卫及其位置与注释、`State()` 退役与两个参数摘除、组合根、两条互补隔离用例、`PresentCharacters`、`NameOf(reader, ordinal)` 与三处调用点 —— 全部原样。`FindIdentityAsync` 只动了 WHERE 子句与文档，`NameOf(reader, 1)` 的序号未动（并由变异 2 证明它现在有判据守着） |
| 不新增协议帧、不改帧字节布局 | ✅ 协议侧零改动；`Dfo.Server.Protocol.Tests` 498 条全通过 |
| 分层方向未变、无新增项目引用 | ✅ 本轮无 `.csproj` / `.slnx` 改动。新用例所需的 `Dfo.Server.Persistence` 引用在 `Dfo.Server.Application.Tests.csproj` 里早已存在（同目录 `OutsideTransactionTests` 在用），`Dfo.Server.Persistence` 的 `InternalsVisibleTo` 也早已含 `Dfo.Server.Application.Tests` |
| `CommandHandlerArchitectureTests` 继续通过，谓词与豁免清单不放宽 | ✅ 该文件零改动，随 935 条一并通过 |
| 不建 Git 检查点、不提交、不 stash | ✅ `git log -1` = `427331bc`（与快审记录的基线相同），`git stash list` 为空，`git status` 只有工作区已修改与未跟踪文件 |

### 留给评审者的两点

1. **新判据落在两个测试项目里，各有其必须**。软删除那条放在 `Dfo.Server.Application.Tests` 且对着
   真库，是因为它要钉的条件写在 SQL 里——用替身写它，变异（把谓词加回去）不会让它变红，就等于没
   钉住任何东西；列序号那条放在 `Dfo.Server.Persistence.Tests`，是因为它是关于一条语句读回什么的
   判据，属于该项目的既有主题。两处的真库夹具都照搬各自项目里既有的做法（`OutsideTransactionTests`
   与 `SqliteCharacterLifecycleTests`），无新增依赖。
2. **`Dfo.Server.Application.Tests` 里多了一个用真 SQLite 文件的用例**。该程序集有一个既知偶发
   （`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` 偶发
   `ObjectDisposedException: 'SQLitePCL.sqlite3'`）。新用例用独立临时目录、`finally` 里
   `ClearAllPools()` 后整目录删除，与既有做法一致；本轮全量与变异共 4 次运行该程序集，未见该偶发，
   但样本有限，值得知道多了一个同类夹具。

## Independent Quick Review 2

- Verdict: **`changes_required`**
- Reviewed contract: Contract revision 1（无 amendment），对照 Acceptance conditions 四条与 CK-001..CK-005；
  计划 T-001 / BD-001 / BD-002 / BD-015；RQ-012（spec.md:123）
- Reviewed rounds: 本轮针对 `## Controller Disposition 2` / `## Execution Result 3` / `## Executor Self-check 3`；
  既有段落一处未改
- Owned diff identity: 工作区未提交改动，基线 `427331bcb07f60121c97920287ac341c9cb14dee`。
  `git diff --stat -- Server/` = **17 files changed, +410 / -175**（第 1 次快审时为 16 files, +311/-175），
  加两个未跟踪新文件 `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterIdentityTests.cs`、
  `Server/tests/Dfo.Server.Persistence.Tests/Characters/CharacterIdentityReadTests.cs`。
  `git log -1` 仍 `427331bc`，`git stash list` 空。

### 自己跑过的命令与结果

| 命令 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx --nologo` | `0 个警告 / 0 个错误`（exit 0） |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 52+498+60+6+256+223+935 = **2030 通过 / 2 跳过 / 0 失败**（exit 0），与报告逐项相符；两个既知偶发均未出现 |
| 计数归因 `git diff -U0 -- Server/tests/ \| grep -E "^[+-].*\[(Fact\|Theory)"` | 仅 `+    [Fact]` 一行，**无删除**；两个新文件 `grep -c "\[Fact\]"` = 4 与 1。1+4+1 = 6 = 2024→2030。归因属实，既有用例一条未少 |
| 既有断言核对 `git diff -U0 -- Server/tests/ \| grep -iE "Assert\|Expected\|ItemUsability\.\|Equal\("` | 删除侧仅 2 行注释文字；新增侧的 `Assert.*` 全部落在本轮新增的那个测试方法内。**零条既有断言字面量变化** |
| **变异 1**：`FindIdentityAsync` 加回 `AND delete_flag=0`（sed 改 :186），跑 `Dfo.Server.Application.Tests` | **1 失败 / 934 通过**。唯一转红：`CharacterItemUseTests.A_character_marked_deleted_under_the_player_is_still_judged`，`Expected: Allowed / Actual: CharacterStateUnavailable` —— 与报告逐字一致 |
| **变异 2**：`NameOf(reader, 1)` → `NameOf(reader, 2)`（sed 改 :189），跑 `Dfo.Server.Persistence.Tests` | **1 失败 / 255 通过**。唯一转红：`CharacterIdentityReadTests.EveryColumnOfTheIdentityReadLandsInItsOwnField`，`System.InvalidCastException : Unable to cast 'System.Int64' to 'System.Byte[]'` —— 与报告逐字一致 |
| 两次变异的还原 | 均以文件级备份还原。还原后 md5 = `ccfac0fe5e0e531e707221be1233acd1`（`CharacterEntryTables.cs`）、`8b5afe2dfc2170cd9d56a1c79546bae6`（`PlayingCharacter.cs`），与执行者记录逐字相同；`git status --porcelain` 与变异前 `diff` 无差异；还原后 `Dfo.Server.Persistence.Tests` 256 全通过、`Dfo.Server.Application.Tests` 935 全通过 |

变异 3（删除 `presence.Find` 守卫）未重做——第 1 次快审已独立复核过，且本轮 diff 未触碰 `PlayingCharacter.cs`（md5 与快审 1 记录的值逐字相同）。

---

### 首要任务的结论：**QR-1 确已治好**

逐条独立核实执行者重写的等价论证，四条依据**全部成立**：

1. **两条语句的 WHERE 子句逐字相同。** `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs`
   - `:152` `FindProgressAsync`：`"SELECT level,exp FROM characters WHERE character_id=@cid"`
   - `:186` `FindIdentityAsync`：`$"SELECT slot_index,{Name},job,grow_type FROM characters WHERE character_id=@cid"`

   两者的 `WHERE character_id=@cid` 逐字一致，参数绑定也一致（都只有
   `parameters.AddWithValue("@cid", characterId.Value)`）。`FindIdentityAsync` 已无 `delete_flag` 谓词。
2. **至多一行。** `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:205`
   的 `CREATE TABLE ... characters` 里 `character_id INTEGER PRIMARY KEY`，成立。
3. **「无行 → null」是同一段代码。** 两者都走 `database.ReadFirstAsync`
   （`Server/src/Dfo.Server.Persistence/Database/SqliteTable.cs:49`，返回 `ValueTask<TRow?>`），成立。
4. **「四列全 NOT NULL」——按要求实地核对 schema，成立。** 同一行 `CREATE TABLE` 里：
   `slot_index INTEGER NOT NULL DEFAULT 0`、`job INTEGER NOT NULL DEFAULT 0`、
   `grow_type INTEGER NOT NULL DEFAULT 0`、`name TEXT NOT NULL`。`name_bytes BLOB` 确实可空，
   但 `COALESCE(name_bytes, CAST(name AS BLOB))` 因 `name` 非空而不可能为 NULL。
   **比执行者的论证还多一层保险**：`NameOf`（`:49-50`）本身写的是
   `reader.IsDBNull(ordinal) ? [] : (byte[])reader.GetValue(ordinal)`，即便该表达式为 NULL 也答 `[]` 而不抛。
   对照侧 `FindProgressAsync` 的 `level INTEGER NOT NULL DEFAULT 1`、`exp INTEGER NOT NULL DEFAULT 0` 同为非空。
   故「有行但一列抛异常」的不对称不存在。

**波及面——自己数过，与执行者一致：**

```
FindIdentityAsync   在 Server/src 的调用点 = 1   （PlayingCharacter.cs:49；另 2 处为接口声明与实现体）
.IdentityAsync(     在 Server/src 的调用点 = 1   （PlayingCharacter.cs:164，即 CanUseAsync）
FindProgressAsync   在 Server/src 的调用点 = 3   （DungeonEffectRecoveryService.cs:139、
                                                  PlayingCharacter.cs:72、
                                                  CharacterExperienceDomainService.cs:39）
```

去掉谓词在生产代码里的波及范围恰好是本 Task 要对齐的那一处，**且 `FindProgressAsync` 未被加过滤**
（`:152` 仍只有 `WHERE character_id=@cid`）。控制器与快审 1 的这条决定被遵守。

**守卫位置**仍为 `NotAUsableItem` → `presence.Find` 守卫 → 身份守卫 → 进度守卫 → `judgement.Judge(...)`
（`PlayingCharacter.cs:125-190`），档案自己的四条拒绝（LevelTooLow / JobMismatch / PlaceNotAllowed /
OnCooldown）全部由 `Judge` 产出，仍排在守卫之后。

**「对着真库」的声称属实。** `CharacterItemUseTests.A_character_marked_deleted_under_the_player_is_still_judged`
构造的是 `new SqliteDatabase(Path.Combine(directory, "characters.db"), new DomainTransactionScope())`，
`ICharacterTable` 传的是 `new SqliteCharacterTable(database)` 而**不是**替身，`INSERT ... delete_flag=1`
经真实连接写入。变异 1 让它转红这一事实本身也反证了它确实钉在 SQL 上——替身版做不到这一点。

**其余验收条款**（均自己核实，成立）：

- 无跨询问缓存：`PlayingCharacter` 是主构造器类（`:22-31`，8 参），**全类无实例字段**，
  `IdentityAsync`（`:47-57`）每次调用都命中 `characters.FindIdentityAsync`。
- 职业来自实例自己：`CharacterJobFamilies.Resolve(identity.Job)`，`identity` 出自 `this.IdentityAsync(...)`；
  `grep -c "selectedSessions\|runtimeSessions\|ISelectedSession" PlayingCharacter.cs` → **0**。
- 身份读取点无第三种：本轮未新增身份读取点（`FindIdentityAsync` 调用点仍为 1），第 1 轮枚举表仍适用。
- 硬约束：`Server/src/Dfo.Server.Protocol/` 改动数 **0**；`*.csproj` / `*.slnx` 改动数 **0**
  （无新增项目引用，分层方向未变）；`CommandHandlerArchitectureTests` 未被触碰（`git status` 无此文件），
  随 935 条一并通过；生产改动仅 5 个文件，落在 Abstractions / Application / Persistence。
- 未建检查点、未提交、未 stash。

---

### 阻断发现

#### QR-2（阻断）— 本次改动使一句既有 XML doc 变成与代码相反的陈述，且它正是造成 QR-1 的那个误解

`Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs:97-98`，`FindStandingAsync` 的 `<remarks>`
结尾（该句为**既有文本，本 diff 未触碰**，`git diff` 对该 hunk 无输出）：

> Deleted characters are left out, **as every other read that answers "what is this character"
> leaves them out.**

前半句仍为真（`FindStandingAsync` 自己在 `CharacterEntryTables.cs:160-161` 确带 `AND delete_flag=0`）。
**后半句现在为假。**

证据一——`FindIdentityAsync` 正是一个回答「这角色是什么」的读取，而它已不排除已删除行。这不是我的
归类，是该成员自己的文档说的（同文件 `:103-105`、`:121-124`）：

> `<summary>` Which slot the character sits in, **what it is called and what it is** …
> `<para>` … **this one says who the character is** and must not depend on how far along it is.

证据二——同一个文件里，两段文档隔 20 行直接对立。`:113-115`（本轮新增）写的是：

> **A character marked deleted still answers**, which is `FindProgressAsync`'s condition rather than
> `FindStandingAsync`'s.

即 `:97-98` 说「每一个回答『这角色是什么』的读取都排除已删除的」，`:113-115` 说「这个回答『这角色是
什么』的读取不排除」。二者不可能同时为真。

证据三——基线时该句为真，是本次改动让它变假。除 `FindIdentityAsync` 外，所有回答「这角色是什么」的读取
在 `CharacterEntryTables.cs` 里都带过滤：`ReadByAccountAsync :58`、`ReadSummariesByAccountAsync`、
`FindBySlotAsync :98`、`FindOwnerAsync :144`、`FindStandingAsync :161` 全部 `delete_flag = 0`。
（`BelongsToAsync` 计入已删除，但它回答的是「这角色是谁的」，故基线时并不证伪该句。）
所以这是**本 Task 的改动直接造成的记载漂移**。

**为什么判为阻断而不是观察**：这不是措辞洁癖。`AGENTS.md:447` 就此有一条明写的教训——

> *教训*：一句「不跨层」的记载不会阻止跨层，只会让下一个读它的人以为没有跨层。

而这里留下的正是「身份类读取都排除已删除行」这个信念，**它就是 QR-1 的成因本身**：执行者第 1 轮给
`FindIdentityAsync` 加 `AND delete_flag=0` 的依据，就是「与 `FindStandingAsync` 一致」。
执行者本轮已经认识到这个原理，并因此删掉了 `FindIdentityAsync` 自己文档里的同义句
（Execution Result 3 原话：「**那句话本身就是被去掉的谓词的唯一辩护词，留着它就是留一条与代码相反的
注释**」）——但把同一主张、且覆盖面更广的那一句留在了紧挨着的上一个成员上。下一个照着 `:97-98`
给某个身份读取补过滤的人，会重新制造 QR-1。

**补救（在已批准边界内，代价极小）**：`CharacterTables.cs` 已在本 diff 的改动文件内，
把 `:97-98` 的 `, as every other read that answers "what is this character" leaves them out`
删除或改写为与代码相符的说法（例如指明这是本读取自己的条件，并按 `FindIdentityAsync` 的新 `<para>`
指向那条相反的先例）。不需要改任何代码或判据，全量判据不受影响。

---

### 非阻断观察

1. **`IdentityAsync` 的 `checked(...)` 投影在列值越界时抛，而不是答某个 `ItemUsability`**
   （`PlayingCharacter.cs:50-56`）。执行者已在 Execution Result 3 如实记录，且论证「有在场记录的角色
   其行必然在入场那一刻通过了同样的转换、软删除不改这四列」我复核成立；该实现属第 1 轮已接受的部分，
   本轮未动。记录备查，不阻断。
2. **快审 1 的非阻断观察第 3 条仍然成立**：`PresentCharacters.StandingInTown` 构造的 `SessionPresence`
   不带 `Session`（`NoCarriedGold.cs`），而生产 `Publish` 写入时是带的、`WithdrawExact` 的精确匹配依赖它。
   当前用例只读 `Find`，不受影响；后续若有用例要走撤销路径需补上。
3. **新增的 `CharacterIdentityReadTests` 确实补上了快审 1 观察第 1 条的缺口**：变异 2 证明它真的守着
   列序号，且只有它一条转红。取值（槽位 3 / 职业 4 / 成长类型 7，且用 `UPDATE` 把 grow_type 从创建时的
   0 移开）确实互不可替，`CharacterSide.Characters` 的新增照该文件既有 `Skills` 的样子。这一条做得对。

---

**本 verdict 的范围**：仅对本 Task Unit 在上述有限输入内做的缺陷检查。它不是完整变更评审，也不替代接受前的
新鲜验证。计划中的 T-002..T-009 未开始，不在本次范围内，不计为缺陷。

## Execution Result 4

第三次定向返工，只治 QR-2 一条：`CharacterTables.cs:97-98` 那句「as every other read that answers
"what is this character" leaves them out」在本 Task 去掉 `FindIdentityAsync` 的谓词之后变成了假命题，
且与同文件下方 20 行的 `A character marked deleted still answers` 直接对立。

**改动面：1 个文件、1 个 hunk、-2/+4 行，全部是 `///` 注释行。零代码、零 SQL、零方法签名、零测试改动。**

### 改动前后原文对照

`Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs`，`FindStandingAsync` 的 `<remarks>`：

改动前（第 97-98 行）：

```csharp
    /// identifier had to go without them. Deleted characters are left out, as every other read
    /// that answers "what is this character" leaves them out.
```

改动后（第 97-100 行）：

```csharp
    /// identifier had to go without them. Deleted characters are left out of this read. The
    /// neighbouring <see cref="FindIdentityAsync"/> keeps them instead, because it is asked as one
    /// question with <see cref="FindProgressAsync"/> and the two must see the same rows; the
    /// reason is written out there.
```

本轮 hunk（以还原出的返工前文本对拍，`diff -u`）：

```diff
@@ -94,8 +94,10 @@
     /// Narrow on purpose, and by character rather than by slot. The job and the grow type were
     /// only ever readable here through a roster read keyed on an account and a slot, which is a
     /// question nobody outside the roster has the answer to; anything holding a character's own
-    /// identifier had to go without them. Deleted characters are left out, as every other read
-    /// that answers "what is this character" leaves them out.
+    /// identifier had to go without them. Deleted characters are left out of this read. The
+    /// neighbouring <see cref="FindIdentityAsync"/> keeps them instead, because it is asked as one
+    /// question with <see cref="FindProgressAsync"/> and the two must see the same rows; the
+    /// reason is written out there.
     /// </remarks>
     ValueTask<CharacterStandingRow?> FindStandingAsync(
         CharacterId characterId, CancellationToken cancellationToken);
```

该 diff 中「非注释的改动行数」实测为 **0**
（`diff -u … | grep -E '^[+-]' | grep -vE '^(\+\+\+|---)' | grep -vE '^[+-][[:space:]]*///' | wc -l` → `0`）。
全量 `git diff --stat -- Server/` 由快审 2 记录的 `17 files changed, +410/-175` 变为
`17 files changed, +414/-177`，差值恰为本 hunk 的 -2/+4，无第二处改动。

### 改法的取舍

- **全称从句被删掉**：改后的句子只陈述 `FindStandingAsync` 自己的行为（"left out of **this read**"），
  不再对「其他任何回答『这角色是什么』的读取」作任何断言。QR-2 指出的漂移源被移除。
- **保留了一句反向指路**：按 QR-2 允许的做法，短短点出 `FindIdentityAsync` 是另一种条件及其一句话理由
  （与 `FindProgressAsync` 被同一个表达式当作一件事用，必须看到同一批行），并明说「理由写在那边」。
  这样下一个读到 `FindStandingAsync` 的人在照葫芦画瓢之前，会先被告知隔壁存在一条相反的先例。
- **没有把 `FindIdentityAsync` 的完整论证复制过来**：消费者侧过滤的归属、与 `FindStandingAsync` 在
  语义上的分工、「一个没人给它起过名字的中间状态」这些仍然只写在 `FindIdentityAsync` 自己的 `<remarks>`
  （`CharacterTables.cs:107-127`）与实现侧的 `<remarks>`（`CharacterEntryTables.cs:169-183`）里。
  论证仍只有一个所有者。

### 自查：该文件里每一句 `delete_flag` 陈述是否都有 SQL 依据

检索式：`grep -n -iE "delete|deleted|gone|free|taken|every|all |none|no such row|not there"`
覆盖 `CharacterTables.cs` 全文；SQL 依据逐条回到
`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs` 实地核对。

| # | 位置 | 陈述 | SQL 依据 | 结论 |
|---|---|---|---|---|
| 1 | `:55` `ReadByAccountAsync` | "characters that have not been deleted" | `:58` `WHERE account_id=@aid AND delete_flag=0` | 相符 |
| 2 | `:59` `ReadSummariesByAccountAsync` | "The same characters" | `:76` `WHERE account_id = @aid AND delete_flag = 0` | 相符（与 #1 同谓词，"same" 属实） |
| 3 | `:63` `FindBySlotAsync` | "nothing when the slot is free" | `:99` `WHERE account_id=@aid AND slot_index=@slot AND delete_flag = 0` | 相符（已删除即视作槽位空） |
| 4 | `:67` `NameIsTakenAsync` | "any character that has not been deleted" | `:119` `WHERE {Name} = @name AND delete_flag = 0` | 相符 |
| 5 | `:71` `BelongsToAsync` summary | "counting one that has been deleted" | `:124-131` `WHERE character_id=@characterId AND account_id=@accountId`（无 `delete_flag`） | 相符 |
| 6 | `:74` `BelongsToAsync` remarks | "Distinct from `FindOwnerAsync`, which ignores deleted characters" | `:144` `WHERE character_id=@cid AND delete_flag=0` | 相符（另见下「第三处全称主张」） |
| 7 | `:82` `FindOwnerAsync` | "nothing when it is gone" | `:144` 带 `delete_flag=0` | 相符 |
| 8 | `:86` `FindProgressAsync` | "nothing when it is **not there**" | `:152` `WHERE character_id=@cid`（无 `delete_flag`） | 相符：措辞是「没有这一行」，未声称排除已删除 |
| 9 | `:91` `FindStandingAsync` summary | "nothing when it is gone" | `:160-161` `WHERE character_id=@cid AND delete_flag=0` | 相符 |
| 10 | `:97`（本轮改后） | "Deleted characters are left out of this read" | `:161` `AND delete_flag=0` | 相符 |
| 11 | `:97-100`（本轮新增的指路句） | "`FindIdentityAsync` keeps them … asked as one question with `FindProgressAsync` … must see the same rows" | `:186` `WHERE character_id=@cid`（无 `delete_flag`），与 `:152` 的谓词逐字一致；同问关系见 `PlayingCharacter.cs:125-190`：身份守卫与进度守卫相邻，任一为空都归为「没有可判的东西」 | 相符 |
| 12 | `:107` `FindIdentityAsync` summary | "nothing when there is **no such row**" | `:186` 无 `delete_flag` | 相符（措辞刻意避开「gone」） |
| 13 | `:116-120` `FindIdentityAsync` 第一个 `<para>` | "A character marked deleted still answers" 等 | `:186` 无 `delete_flag`；`:152` 亦无 | 相符 |
| 14 | `:147` `MarkDeletedAsync` | "Marks the character deleted. False when it already was." | `:236-240` `UPDATE characters SET delete_flag = 1 WHERE character_id = @cid AND delete_flag = 0` 且返回 `受影响行数 > 0` | 相符：已删除的行被 `AND delete_flag = 0` 排除，受影响行数 0 → `false` |

**「有没有第三处也作了全称主张」——查过，没有。** 该文件里凡是描述「别的读取」的句子只有两处：

- `:74` 的 "Distinct from `FindOwnerAsync`, which ignores deleted characters"——**点名单个成员**，
  且经 `:144` 核实属实，不是「every other read …」式的全称量化，保留；
- 本轮新增的 `:97-100`——同样点名 `FindIdentityAsync` / `FindProgressAsync` 两个具体成员，不作全称主张。

其余含 "every / all / none" 的句子（`:20` 三列同取、`:49-51` 这些读取都不做决定、`:177` 技能表每个方法都点名页集、
`:182` "Every ordinary skill"）与 `delete_flag` 无关，逐句核对后均与代码相符，不在本次范围内改动。
顺带核对实现侧：`CharacterEntryTables.cs:169-183` 的 `<remarks>` 只谈 `FindIdentityAsync` 与
`FindProgressAsync` 两者自身，同样没有全称主张。

### 判据输出

CK-001：

```
$ dotnet build Server/86JP.Server.slnx --nologo
（略：14 个项目全部 -> …dll）
已成功生成。
    0 个警告
    0 个错误
已用时间 00:00:02.01
EXIT=0
```

CK-005：

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
已通过! - 失败: 0，通过:  52，已跳过: 0，总计:  52 - Dfo.Server.Networking.Tests.dll
已通过! - 失败: 0，通过: 498，已跳过: 0，总计: 498 - Dfo.Server.Protocol.Tests.dll
已通过! - 失败: 0，通过:  60，已跳过: 0，总计:  60 - Dfo.Pvf.Tests.dll
已通过! - 失败: 0，通过:   6，已跳过: 0，总计:   6 - Dfo.Server.Host.Tests.dll
已通过! - 失败: 0，通过: 256，已跳过: 0，总计: 256 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败: 0，通过: 223，已跳过: 2，总计: 225 - Dfo.Server.Infrastructure.Tests.dll
已通过! - 失败: 0，通过: 935，已跳过: 0，总计: 935 - Dfo.Server.Application.Tests.dll
EXIT=0
```

52+498+60+6+256+223+935 = **2030 通过 / 2 跳过 / 0 失败**，与 Execution Result 3 及快审 2 的计数
**逐项相同**（纯注释改动，计数本就不应变）。两个既知偶发
（`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`、`Dfo.Server.Networking.Tests`
的并行偶发）本轮均未出现。两条跳过仍是 `PvfQuestDefinitionSourceTests` 与
`PvfTownDefinitionSourceTests` 的缺档案用例。

### 工作区状态

- `git log -1` = `427331bcb07f60121c97920287ac341c9cb14dee`（未变，**未提交**）
- `git stash list` 空（**未 stash**），**未建检查点**
- `git status --porcelain`：17 个已跟踪文件 `M` + 2 个未跟踪新测试文件 + 未跟踪的 `.rks-agent/tasks/`，
  与快审 2 记录的集合逐项相同，本轮未新增或减少任何文件
- 未触碰 `sdd-execution.yaml`，未写 verdict


## Executor Self-check 4

### Required checks（CK-001..CK-005）

| 判据 | 期望 | 结果 | 依据 |
|---|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` → 零警告零错误 | 通过 | 上文 CK-001 输出：`0 个警告 / 0 个错误`，exit 0。仓库 `Directory.Build.props` 开着 `TreatWarningsAsErrors`，零警告是编译器强制。另注：新写的两个 `<see cref="..."/>` 若名字写错会触发 CS1574，构建零警告即证明二者都解析成功 |
| CK-002 | 新增用例：改变存储中的身份后再问一次 → 第二次的答案反映变化 | 通过（复核，结论不变） | 本轮未触碰任何测试文件与 `PlayingCharacter.cs`；`CharacterIdentityTests` 4 条随 935 条一并通过 |
| CK-003 | 身份读取点枚举 → 每一处或在入场序列内，或经实例；无第三种 | 通过（复核，结论不变） | 本轮改的是一段 `<remarks>` 文本，未新增、未删除、未移动任何读取点；`FindIdentityAsync` 在 `Server/src` 的调用点仍为 1（`PlayingCharacter.cs:49`） |
| CK-004 | 检视实例实现 → 无跨询问缓存；不再引用会话运行态聚合 | 通过（复核，结论不变） | 本轮 diff 不含 `PlayingCharacter.cs`，该文件自快审 2 核过之后未被写入 |
| CK-005 | 全量 `dotnet test` → 通过 | 通过 | 2030 通过 / 2 跳过 / 0 失败，exit 0；与返工前逐项相同 |

### Acceptance conditions（Contract revision 1，四条）

| 验收条款 | 结果 | 依据 |
|---|---|---|
| 实例能回答身份四项，每次现读，无跨询问缓存 | 满足 | 本轮未触碰 `ICharacter.cs` / `PlayingCharacter.cs`；CK-002 与 CK-004 复核通过 |
| 判道具可用性处解析职业族的读取来自实例自己，不再来自会话运行态聚合 | 满足 | 同上，未触碰判定路径；`CanUseAsync` 仍走 `this.IdentityAsync(...)` → `CharacterJobFamilies.Resolve(identity.Job)` |
| 身份的全部读取点可逐个归类为「入场序列内」或「经实例」，无第三种 | 满足 | 见 CK-003 |
| 全量判据全绿，行为逐值不变 | 满足 | 全绿见 CK-005；「逐值不变」见下节，本轮为零行为改动 |

### 本次改动是否影响任何行为——**否**

理由逐条，可独立复验：

1. **改动全部落在 `///` 注释行。** 本轮 hunk 的「非注释改动行数」实测为 `0`（命令与输出见 Execution Result 4）。
   没有一行可执行代码、没有一个字符的 SQL、没有一处方法签名或返回类型被触碰。
2. **未触碰任何 SQL。** `CharacterEntryTables.cs` 本轮无新增 hunk；`FindStandingAsync`
   仍是 `WHERE character_id=@cid AND delete_flag=0`，`FindIdentityAsync` 仍是 `WHERE character_id=@cid`。
   本 Task 要建立的那条「身份读取不排除已删除行」的事实**没有被这次改动动摇**，改的只是隔壁那句错误的旁白。
3. **未触碰任何测试。** `git status` 中测试文件集合与快审 2 逐项相同，无新增/删除用例；
   全量计数 2030/2/0 与返工前**逐项相同**——若本轮误改了任何行为或断言，这个计数或某条断言必然转红。
4. **XML doc 不进 IL。** 注释文本只进入编译产物的 XML 文档文件，不参与语义；
   唯一可能由注释触发的编译期后果是 cref 解析（CS1574 等），而 CK-001 零警告零错误说明两个新 cref 均解析成功。

### 硬约束核对

| 约束 | 结果 |
|---|---|
| 只改注释，不改 SQL / 方法签名 / 代码逻辑 / 测试 | 遵守：非注释改动行数 = 0 |
| 不建 Git 检查点、不提交、不 stash | 遵守：`git log -1` 仍 `427331bc`；`git stash list` 空 |
| 不改 `sdd-execution.yaml`，不写 verdict | 遵守：未触碰该文件；本段落只作自查，不下裁决 |
| 追加 `## Execution Result 4` 与 `## Executor Self-check 4`，不改写既有 11 个段落 | 遵守：仅在文件末尾追加，既有段落零改动 |


## Independent Quick Review 3

- Verdict: **`passed`**
- Reviewed contract: Contract revision 1（无 amendment）
- Reviewed rounds: 本轮只审 `## Execution Result 4` / `## Executor Self-check 4`（针对 QR-2 的定向修正）。
  QR-1 的治愈、三次变异验证、等价论证四条腿、调用点计数已由 `## Independent Quick Review 2` 独立复核，
  本轮按收口范围不重做；核对中未发现其中任何一条有误。
- Owned diff identity: 工作区未提交，基线 `427331bcb07f60121c97920287ac341c9cb14dee`。
  `git diff --stat -- Server/` = **17 files changed, +414 / -177**
  （快审 2 记录为 `+410 / -175`，差值恰为 **+4 / -2**，即本轮唯一 hunk），
  加两个未跟踪新测试文件（集合与快审 2 逐项相同）。

### 自己跑过的命令与结果

| 命令 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx --nologo` | `0 个警告 / 0 个错误`，exit 0 |
| `dotnet build Server/86JP.Server.slnx --nologo --no-incremental`（**额外加做**） | `0 个警告 / 0 个错误`，exit 0。增量构建可能跳过重编译，故强制全量重编一次，确保两个新 `<see cref>` 的解析真的在**我这一次**被执行 |
| `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 52+498+60+6+256+223+935 = **2030 通过 / 2 跳过 / 0 失败**，exit 0。两条跳过仍是 `PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的缺档案用例。两个既知偶发本轮均未出现 |
| `md5sum` 两个关键文件 | `CharacterEntryTables.cs` = `ccfac0fe5e0e531e707221be1233acd1`、`PlayingCharacter.cs` = `8b5afe2dfc2170cd9d56a1c79546bae6`，与快审 2 记录的值**逐字相同** → 这两个文件自快审 2 核过之后一个字节未变，无变异残留 |
| 逐文件 mtime | `CharacterTables.cs` = `01:33:35`，**严格晚于**其余 18 个改动文件（次晚为 `CharacterEntryTables.cs` `01:27:11`）。本轮只写过这一个文件 |
| `git log -1` / `git stash list` / `git status --porcelain --ignored \| grep -iE "\.bak\|\.orig\|\.rej\|~$"` | `427331bcb07f60121c97920287ac341c9cb14dee` / 空 / 无匹配 |

`Server/Directory.Build.props:6` 确有 `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`，
全仓无 `NoWarn`。故「零警告」确实构成 CS1574 未触发的证明。

---

### 1. QR-2 是否真的治好——**是**

改后原文（`Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs:97-100`，实地核对行号）：

> … identifier had to go without them. Deleted characters are left out of **this read**. The
> neighbouring `FindIdentityAsync` keeps them instead, because it is asked as one question with
> `FindProgressAsync` and the two must see the same rows; the reason is written out there.

**全称主张已消除。** 原句的 `, as every other read that answers "what is this character" leaves them out`
整段从句已不存在；剩下关于本成员的陈述是 "left out of **this read**"，作用域被显式钉在
`FindStandingAsync` 自己身上，对「其他任何读取」零断言。QR-2 指出的漂移源确实被移除。

**新加的指路句与代码相符，逐项核过：**

| 分句 | 核对依据 | 结论 |
|---|---|---|
| "neighbouring" | `FindIdentityAsync` 声明于 `CharacterTables.cs:130`，紧接 `FindStandingAsync`（`:102`）之后，中间无第三个成员 | 属实 |
| "keeps them"（保留已删除行） | `CharacterEntryTables.cs:186` `SELECT slot_index,{Name},job,grow_type FROM characters WHERE character_id=@cid` —— 无 `delete_flag` | 属实 |
| "asked as one question with `FindProgressAsync`" | `PlayingCharacter.cs:164` 身份守卫与 `:172` 进度守卫相邻，两者任一为空都归为同一个 `ItemUsability.CharacterStateUnavailable` | 属实 |
| "the two must see the same rows" | `FindProgressAsync`（`CharacterEntryTables.cs:152`）的 `WHERE character_id=@cid` 与 `:186` 逐字一致；一边加谓词就会让软删除角色只答其一 | 属实 |
| "the reason is written out there" | 理由确实完整写在 `FindIdentityAsync` 侧：接口 `CharacterTables.cs:116-126`，实现 `CharacterEntryTables.cs:168-183` | 属实 |
| 与 `FindStandingAsync` 的对比是否成立（它不与 `FindProgressAsync` 同问） | `FindStandingAsync` 在 `Server/src` 的唯一生产调用点为 `CharacterSkillPointDomainService.cs:49`，该处不调 `FindProgressAsync`（`FindProgressAsync` 的三个调用点为 `DungeonEffectRecoveryService.cs:139`、`PlayingCharacter.cs:72`、`CharacterExperienceDomainService.cs:39`）。且 `FindStandingAsync` 自带 `level` 列，本就无需配对 | 对比成立，未制造反向过度声称 |

**同文件第 20 行外的直接对立已消解**：`:97` 现在只说自己，`:116` 的
"A character marked deleted still answers" 说 `FindIdentityAsync` 自己，两句不再矛盾。

### 2. 是否还有第三处类似的全称主张——**没有**（自己抽验，非采信自陈）

按要求实地抽验，含指定的两条：

- **`:74`（指定抽验）** "Distinct from `FindOwnerAsync`, which ignores deleted characters."
  → `CharacterEntryTables.cs:144` `SELECT account_id FROM characters WHERE character_id=@cid AND delete_flag=0`。
  **属实**，且它**点名单个成员**，不是全称量化。对照 `BelongsToAsync` 自身
  （`CharacterEntryTables.cs:127-131` `WHERE character_id = @characterId AND account_id = @accountId`，无 `delete_flag`），
  `:71` 的 "counting one that has been deleted" 亦属实。保留正确。
- **`MarkDeletedAsync`（指定抽验）** `:147` "Marks the character deleted. False when it already was."
  → `CharacterEntryTables.cs:238` `UPDATE characters SET delete_flag = 1 WHERE character_id = @cid AND delete_flag = 0;`
  且 `:240` 返回 `受影响行数 > 0`。已删除的行被 `AND delete_flag = 0` 排除 → 0 行 → `false`。**属实。**
- 另抽三条：`:63` `FindBySlotAsync` "nothing when the slot is free" → `:99` 带 `AND delete_flag = 0`，属实；
  `:86` `FindProgressAsync` "nothing when it is **not there**" → `:152` 无 `delete_flag`，措辞刻意避开「gone」，属实；
  `:91` `FindStandingAsync` summary "nothing when it is gone" → `:160-161` 带 `AND delete_flag=0`，属实。

**独立扫描全文找全称量词**（`grep -n -iE "every|all |any |none|other read|others|both |each |never|always|no other"`，
220 行全覆盖）：命中 13 处。凡涉及 `delete_flag` 的只有 `:74` 与本轮新增的 `:97-100`，二者均点名具体成员。
其余（`:20` 三列同取、`:59` "The same characters"、`:67`、`:151`、`:167`、`:176-177`、`:182`、`:186` 等）
与 `delete_flag` 无关或属技能/购买限制表，与本 Task 的改动无因果。

**唯一值得记一笔的是 `:49-51`**（`ICharacterTable` 的 `<remarks>`）：
"What **none of these** do is decide anything: whether a name may be taken, whether a slot is free …"。
这是一句覆盖全部成员的全称主张，但它谈的是「不做决定」而非 `delete_flag`。本 Task 把排除已删除的动作
**推给消费者侧**（`CharacterTables.cs:120-122`），是在**加强**而非证伪这句话。不构成第三处漂移。

再补一条仓级检索：`grep -rn -iE "(every|all|no) (other )?(read|lookup|query|caller)s?[^.]{0,80}delet|delet[^.]{0,80}(every|all) (other )?(read|lookup|query)" Server/src --include=*.cs`
→ **零命中**，`Server/src` 内不存在第二处同型全称主张。

### 3. 本轮确为纯注释改动——**是**

- `git diff -- CharacterTables.cs` 全文核对：该文件累计 3 个 hunk。其中
  **`CharacterIdentityRow` 记录块（+9，纯新增）** 与 **`FindIdentityAsync` 成员块（+30，纯新增）**
  是第 1 轮的既有合法新增（零删除）；本轮的是 `FindStandingAsync` `<remarks>` 的
  **-2 / +4**，6 行全部以 `///` 开头，无一行可执行代码。
- 该文件**累计**删除行数恰为 2，且这 2 行正是被本轮替换掉的那两行注释——反过来证明第 1 轮在该文件上确为零删除，
  与「不要误判成夹带」的提示一致。
- 全量 `--stat` 差值 `+4/-2`、文件数仍 17、`git status` 集合逐项不变，
  加上 `CharacterEntryTables.cs` / `PlayingCharacter.cs` 的 md5 与快审 2 逐字相同、
  `CharacterTables.cs` 的 mtime 严格最晚 —— 四条互相独立的证据一致指向
  **本轮只写了 1 个文件、只改了注释**。零 SQL、零签名、零测试改动。

### 4. 判据未因本轮变化——**未变**

CK-001 零警告零错误（并已用 `--no-incremental` 强制重编一次），CK-005 仍 **2030 / 2 / 0** 且 exit 0，
与 Execution Result 3、快审 2、Execution Result 4 的分项计数逐项相同。CK-002 / CK-003 / CK-004
的实现载体（`PlayingCharacter.cs`、`ICharacter.cs`、两个新测试文件）本轮字节未变，结论沿用快审 2。

### 5. 工作区

`git log -1` = `427331bcb07f60121c97920287ac341c9cb14dee`（未提交、未建检查点）；`git stash list` 空；
无 `.bak/.orig/.rej/~` 残留；`sdd-execution.yaml` 未被触碰。

---

### 非阻断观察

1. 新指路句只给了「必须看到同一批行」这半个理由，把消费者侧过滤的归属、与 `FindStandingAsync` 的语义分工
   留在 `FindIdentityAsync` 侧并以「the reason is written out there」指向。这是刻意的单一所有者安排，
   我认为取舍正确，记录备查。
2. 快审 2 的三条非阻断观察（`checked(...)` 投影会抛、`PresentCharacters.StandingInTown` 不带 `Session`、
   `CharacterIdentityReadTests` 已补上列序号缺口）在本轮均未受影响，仍然成立，不重复展开。

---

**本 verdict 的范围**：仅证明本 Task Unit 在上述有限输入内的缺陷检查通过，
**不是**完整变更评审，也**不替代**接受前的新鲜验证。计划中 T-002..T-009 未开始，不在本次范围内。
