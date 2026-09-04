# TASK-05 — 连招页有一个领域 store 与一个实例

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 一个按页号寻址的连招领域 store（建在既有的连招页行网关之上、接进事务参与者）与一个能按页号回答一个角色全部连招页的实例。
- Referenced spec requirements: RQ-006, RQ-014
- Referenced plan sections: T-005；BD-009, BD-010, BD-011, BD-015
- Owned boundary: 连招的 store 接口与 session 接口、其持久化实现与接线、连招实例，以及它们的判据。
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Abstractions/Characters/` 下新增的连招 store 接口、session 接口与连招实例接口
  - `Server/src/Dfo.Server.Persistence/Characters/` 下该 store 的实现
  - `Server/src/Dfo.Server.Persistence/PersistenceModule.cs` 与 `Server/src/Dfo.Server.Application/ApplicationModule.cs` 的接线
  - `Server/src/Dfo.Server.Application/Characters/` 下连招实例的实现
  - 对着真库的新增判据
- Dependencies: TASK-04
- Required dependency outputs: 无
- Acceptance conditions:
  - 实例能按页号答出一个角色的连招页，且与库中的行一致
  - 写入经 store 结算
  - **`store` 一词仍只指事务结算的真源**；实例不得叫 store
  - **别的 store 的结算顺序未变**
  - 全量判据全绿
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: test-driven-development
- Blocking condition（硬性停止点）: 若这个 store 的事务参与者接线**改变了别的 store 的结算顺序**，立即停下并提交阻塞报告给控制器，**不得硬做完**。这是路由决策里写明的 reroute 条件——它意味着本成果出现了第二个验收边界，处置权在控制器而不是执行者。
- Required checks: CK-018, CK-019, CK-020, CK-021, CK-022
- Required evidence:
  - store 接口、session 接口与实例的成员清单
  - 对着真库的判据结果
  - 领域层中名字含 `store` 的全部类型逐个分类对照
  - 事务参与者的注册与结算顺序改动前后的对照
  - 全量判据输出

## Execution Result 1

### 1. 交付物与成员清单

#### 1.1 store 接口（`Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboDomainStore.cs`，新建）

```
public interface IDarkKnightComboDomainStore : IDomainStore<IDarkKnightComboDomainStoreSession>;
```

继承自 `IDomainStore<TSession>`，因此成员就是载体协议本身的三个：
`object StoreIdentity { get; }`、`bool IsDurable { get; }`、
`IDarkKnightComboDomainStoreSession SessionIn(IDomainTransaction transaction)`。没有新增动词。

#### 1.2 session 接口（同文件）

```
public interface IDarkKnightComboDomainStoreSession
    : IDomainStoreSession<OwnedCharacterId, byte, DarkKnightComboEntryState>;
```

成员即共享词汇的四个：
`ReadAllAsync(OwnedCharacterId, CancellationToken)`、
`ReadAsync(OwnedCharacterId, byte, CancellationToken)`、
`Write(OwnedCharacterId, DarkKnightComboEntryState)`、
`Clear(OwnedCharacterId, byte)`。

形状取的是 `IItemDomainStoreSession` 那一支（**具名实例**）而不是
`IQuestDomainStoreSession` 那一支（**若干具名分区的组合**）：连招页是一张齐次的表、只按页号一种
方式寻址，任务那边分三个分区是因为任务状态本来就是三组互不相干的行。Task 说明里给的样板是
`IQuestDomainStoreSession` 的 `IDomainStoreSession<OwnedCharacterId, short, StoredActiveQuest>`
那一层，这里正是同一层的具名化。

**存值类型没有新造 `StoredComboPage`，直接用了 `DarkKnightComboEntryState`**
（`Dfo.Server.Data/Characters/CharacterModels.cs:364`，`(byte PageIndex, byte[] EncodedBody)`）。
理由写在接口的 XML doc 里：一页就是「页号 + 原样的 body」，再声明一个同形状的 `Stored*` 记录会
得到两个说同一件事的记录，而本仓上一轮已经为「两个记录会漂移」付过学费
（见 `ICharacterSettingsDomainService` 的 remarks）。代价是打破了「store 的存值一律叫 `Stored*`」
这条既有惯例，这一条是我的判断，写在这里供复核推翻。

#### 1.3 实例接口（`Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboBook.cs`，新建）

```
public interface IDarkKnightComboBook
{
    CharacterId CharacterId { get; }
    ValueTask<IReadOnlyList<DarkKnightComboEntryState>> PagesAsync(CancellationToken);
    ValueTask<DarkKnightComboEntryState?> FindPageAsync(byte pageIndex, CancellationToken);
    ValueTask SetPageAsync(DarkKnightComboEntryState page, CancellationToken);
    ValueTask ClearPageAsync(byte pageIndex, CancellationToken);
}

public interface ICharacterDarkKnightComboDomainService
{
    IDarkKnightComboBook BookOf(AccountId accountId, CharacterId characterId);
}
```

对照 `IQuestLog` 的形状逐条：有身份（`CharacterId`）、可改（`SetPageAsync` / `ClearPageAsync`）、
快照式读（每次现读，不缓存）、工厂叫 `*DomainService`（`ICharacterQuestLogDomainService.LogOf`
对应 `ICharacterDarkKnightComboDomainService.BookOf`）。**名字里没有 `store`**。

关于两个写动词：`ICharacterSettingsDomainService` 里写过「协议还没到就别造动词」。这里仍然造了两个，
依据是 Task 契约明写「实例……**可改**」与 Acceptance「**写入经 store 结算**」——没有写动词，这两条
都无法成立、也无法判。两个动词本身不携带任何自造规则（页号范围的规则在 store 上），只是把 store
共享词汇的 `Write`/`Clear` 落在角色身份之下。

#### 1.4 store 实现（`Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs`，新建）

三个 `internal` 类型：

- `SqliteDarkKnightComboDomainStore : IDarkKnightComboDomainStore`
  —— `StoreIdentity` 是连接字符串（与人物/物品/任务三个 store 同一个），`IsDurable => true`，
  `SessionIn` 走 `transaction.Enlist(连接字符串, () => new SqliteDatabaseParticipant(...))`
  再取 facet。**没有新建表**，用的是既有的 `character_dark_knight_combo_skill_pages`。
- `SqliteDarkKnightComboDomainStoreSession : IDarkKnightComboDomainStoreSession, ISqliteStoreFacet`
  —— 把四个读写动词委托给内部的 `ComboPageSection`，`FlushAsync` 在冲刷前逐角色做
  账号归属检查（与 `SqliteQuestDomainStoreSession.RefuseUnlessOwnedAsync` 同一条语句与同一条理由）。
- `ComboPageSection : SqliteOwnedCharacterSection<byte, DarkKnightComboEntryState>`
  —— **读走既有行网关**：`ReadRowsAsync` 调 `ICharacterComboPageTable.ReadAsync`，不另写一条
  SELECT。写是 `INSERT ... ON CONFLICT(character_id,page_index) DO UPDATE SET body=@body`，
  清是 `DELETE ... WHERE character_id=@character AND page_index=@page`。

为什么读要走行网关而不是像任务 store 那样自己写一条 SELECT：BD-010 与 Task 契约都写「建在既有的
连招页行网关之上」，而且一张表两条 SELECT 就是两个可以漂移的读者——`IQuestDomainStore` 的 remarks
为此写了整整四段来交代「两个读者哪里一致、哪里不一致」。这里这份交代不需要写，因为只有一条语句。
运行时的行为：用例内，行网关经 `SqliteDatabase.Participant()` 按同一个连接字符串 `Enlist`，拿到的是
**同一个** `SqliteDatabaseParticipant`、同一条连接与同一个数据库事务，所以这个读就是该用例的读；
用例外它是一次独立连接的读，而「用例外的读」本来就是这样（`SqliteDatabase.ReadAsync` 的既有语义）。
自己这次未落定的写由 `SqliteOwnedCharacterSection` 的 staged 覆盖层负责，与读走哪条连接无关——
`ABookSeesItsOwnUseCasesUnsettledWrite` 这条用例就是钉这一点的。

页号范围：`ComboPageSection` 只接受 0、1 两页（`HighestPage = 1`）。依据是既有代码里已经有这条规则
——`Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:233-236`：
`if (page.PageIndex is < 0 or > 1) throw new InvalidDataException("Dark Knight combo page is out of range.")`。
写超范围抛 `ArgumentOutOfRangeException`（照抄 `ClearedQuestSection.Write` 的形状与理由：写下去
不是「一个错答案」而是「一个进不了游戏的角色」），读到超范围的行抛 `InvalidDataException`（照抄
`ClearedQuestSection.ReadRowsAsync` 里那条注释的理由：入场投影会直接拒绝的行，这里读成任何值都是
在发明一个入场随后要拒的值）。

#### 1.5 实例实现（`Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs`，新建）

- `DarkKnightComboBook(OwnedCharacterId place, IDarkKnightComboDomainStore store, AmbientStoreAccess stores) : IDarkKnightComboBook`
  —— 读经 `stores.ReadAsync`，写经 `stores.ChangeAsync`；不持任何状态。
- `CharacterDarkKnightComboDomainService(IDarkKnightComboDomainStore store, AmbientStoreAccess stores) : ICharacterDarkKnightComboDomainService`
  —— 与 `CharacterQuestLogDomainService` 逐行同形。

`FindPageAsync` 是**向 store 按页号要**（`session.ReadAsync(place, pageIndex, ...)`），不是读全部再
自己筛：寻址是 store 的事，实例自己筛就等于自带了第二份寻址。这一点由变异 1 钉住（见 §5）。

### 2. 逐文件改动与理由

下面是**全部改动完成、变异已还原之后**跑的两条命令的**原始完整输出**（未裁剪）。

`git status --short`：

```
 M Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs
 M Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs
 M Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
 M Server/src/Dfo.Server.Application/ApplicationModule.cs
 M Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
 M Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs
 M Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs
 M Server/src/Dfo.Server.Persistence/PersistenceModule.cs
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

`git diff --numstat`：

```
17	0	Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs
41	2	Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs
3	3	Server/src/Dfo.Server.Abstractions/World/IDungeonVisitDomainService.cs
12	2	Server/src/Dfo.Server.Application/ApplicationModule.cs
57	24	Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs
24	4	Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs
34	4	Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs
6	0	Server/src/Dfo.Server.Persistence/PersistenceModule.cs
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

**逐文件为什么在改动集合里**。工作区里同时装着 TASK-01..04 的成果，所以两份清单里绝大多数条目
不是本 Task 的。归属如下：

| 文件 | 归谁 | 为什么在集合里 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboDomainStore.cs` | **TASK-05（本次新建）** | BD-009：store 与 session 契约 |
| `Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboBook.cs` | **TASK-05（本次新建）** | BD-011：实例契约与它的工厂 |
| `Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs` | **TASK-05（本次新建）** | BD-010：建在既有行网关之上的实现 |
| `Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs` | **TASK-05（本次新建）** | BD-011：实例与工厂的实现 |
| `Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs` | **TASK-05（本次新建）** | BD-015 / CK-019 / CK-021：对着真库的判据 |
| `Server/src/Dfo.Server.Persistence/PersistenceModule.cs`（`6 0`） | **TASK-05（本次修改）** | 6 行全部是本次新增的 store 接线（3 行注释 + 3 行注册），无删除、无重排 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs`（`12 2`） | TASK-03/04（10 增 2 删）+ **TASK-05（2 增）** | 本次只追加了 `ICharacterDarkKnightComboDomainService` 一条注册（占其中 2 行），其余为前序 Task 的成果 |
| 其余 25 个 ` M` 与 9 个 `??` 条目 | TASK-01..04 | 本 Task 一行未动，见下方复核 |

「本 Task 一行未动其余文件」这句声称的依据，是同一条命令在**本 Task 开工前**的输出（同一工作区，
26 行，完整原样）：

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

两份逐行对比：只有 `ApplicationModule.cs`（`10 2` → `12 2`）变了数字，`PersistenceModule.cs`
（开工前未出现 → 现在 `6 0`）是新增行，**其余 25 行数字与顺序完全相同**。开工前的
`git status --short` 里 `??` 一侧共 11 项，现在共 16 项，多出的 5 项正是本 Task 新建的 5 个文件
（`IDarkKnightComboBook.cs`、`IDarkKnightComboDomainStore.cs`、`DarkKnightComboBook.cs`、
`SqliteDarkKnightComboDomainStore.cs` 四个在 `Server/src` 下，`DarkKnightComboBookTests.cs`
在 `Server/tests` 下）。

### 3. 事务参与者的注册与结算顺序未变（CK-021 / 硬性停止点）

**结论：没有触发硬性停止点。别的 store 的结算顺序未变。** 依据是三条，逐条给证据。

**(a) 结算顺序由 `DomainTransaction.order` 决定，而登记按 identity 去重。**
`Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs` 的 `Enlist` 先查
`this.enlisted.TryGetValue(identity, ...)`，命中就直接返回既有参与者，**不** `order.Add`。
新 store 的 `StoreIdentity` 是 `new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString()`
——与 `SqliteCharacterDomainStore` / `SqliteItemDomainStore` / `SqliteQuestDomainStore` /
`SqliteDatabase` 四处逐字同一套构造。所以它加入的是**既有那一个** `SqliteDatabaseParticipant`，
`order` 里不会多出一项，别的参与者的相对位置也就无从改变。

这条不靠阅读断言，有一条**会跑的判据**钉住：
`Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.TheComboStoreJoinsTheParticipantTheOtherStoresAlreadyJoined`
——把人物、物品、任务、连招四个真 store 依次 `SessionIn` 同一个事务，断言参与者只有一个。

**(b) 数据库内部的 facet 冲刷顺序（`SqliteDatabaseParticipant.order`）是「首次使用的先后」，
且只追加。** 见 `SqliteDatabaseParticipant.Facet`：命中既有 key 直接返回，未命中才 `order.Add(facet)`。
所以只有当某个用例**用到**连招 store 时，才会多出一个 facet，而且是追加在末尾；既有 facet 之间的
先后不受影响。

**(c) 今天没有任何生产用例会用到它**，因此连 (b) 里那个「多出一个 facet」也不会发生在既有用例上。
枚举命令与**完整原始输出**（无裁剪）：

```
$ grep -rn "IDarkKnightComboDomainStore\|ICharacterDarkKnightComboDomainService\|IDarkKnightComboBook\|SqliteDarkKnightComboDomainStore\|CharacterDarkKnightComboDomainService\|DarkKnightComboBook\|ComboPageSection" --include=*.cs Server/ | grep -v "/obj/" | grep -v "/bin/"
Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboBook.cs:29:public interface IDarkKnightComboBook
Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboBook.cs:69:public interface ICharacterDarkKnightComboDomainService
Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboBook.cs:77:    IDarkKnightComboBook BookOf(AccountId accountId, CharacterId characterId);
Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboDomainStore.cs:25:public interface IDarkKnightComboDomainStore : IDomainStore<IDarkKnightComboDomainStoreSession>;
Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboDomainStore.cs:43:public interface IDarkKnightComboDomainStoreSession
Server/src/Dfo.Server.Application/ApplicationModule.cs:265:            ICharacterDarkKnightComboDomainService, CharacterDarkKnightComboDomainService>();
Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs:21:internal sealed class DarkKnightComboBook(
Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs:22:    OwnedCharacterId place, IDarkKnightComboDomainStore store, AmbientStoreAccess stores)
Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs:23:    : IDarkKnightComboBook
Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs:66:internal sealed class CharacterDarkKnightComboDomainService(
Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs:67:    IDarkKnightComboDomainStore store, AmbientStoreAccess stores)
Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs:68:    : ICharacterDarkKnightComboDomainService
Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs:70:    public IDarkKnightComboBook BookOf(AccountId accountId, CharacterId characterId) =>
Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs:71:        new DarkKnightComboBook(new OwnedCharacterId(accountId, characterId), store, stores);
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:29:internal sealed class SqliteDarkKnightComboDomainStore : IDarkKnightComboDomainStore
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:32:    private static readonly object FacetKey = typeof(SqliteDarkKnightComboDomainStoreSession);
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:38:    public SqliteDarkKnightComboDomainStore(string databasePath, ICharacterComboPageTable pages)
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:50:    public IDarkKnightComboDomainStoreSession SessionIn(IDomainTransaction transaction)
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:57:            FacetKey, owner => new SqliteDarkKnightComboDomainStoreSession(owner, this.pages));
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:70:internal sealed class SqliteDarkKnightComboDomainStoreSession
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:71:    : IDarkKnightComboDomainStoreSession, ISqliteStoreFacet
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:75:    private readonly ComboPageSection pages;
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:77:    public SqliteDarkKnightComboDomainStoreSession(
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:81:        this.pages = new ComboPageSection(owner, gateway);
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:135:internal sealed class ComboPageSection(
Server/src/Dfo.Server.Persistence/PersistenceModule.cs:170:        services.TryAddSingleton<IDarkKnightComboDomainStore>(
Server/src/Dfo.Server.Persistence/PersistenceModule.cs:171:            provider => new SqliteDarkKnightComboDomainStore(
Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs:26:public sealed class DarkKnightComboBookTests
Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs:252:            this.Store = new SqliteDarkKnightComboDomainStore(
Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs:254:            this.Books = new CharacterDarkKnightComboDomainService(
Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs:260:        public SqliteDarkKnightComboDomainStore Store { get; }
Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs:264:        public ICharacterDarkKnightComboDomainService Books { get; }
Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs:266:        public IDarkKnightComboBook Book() => this.Books.BookOf(Account, Character);
```

（以上 33 行为该命令的完整输出，逐字原样，未裁剪、未插入。生产侧对这些新类型的引用只有 2 处
接线 —— `ApplicationModule.cs:265` 与 `PersistenceModule.cs:170-171` —— 其余全是新类型自身的声明
与新判据文件；**没有任何既有命令处理器、领域服务或协议切片引用它们**。）

**(d) 两个 Module 的接线改动是纯追加，没有重排。** `git diff` 里本 Task 的两段：

`git diff -- Server/src/Dfo.Server.Persistence/PersistenceModule.cs` 的**完整原始输出**：

```
diff --git a/Server/src/Dfo.Server.Persistence/PersistenceModule.cs b/Server/src/Dfo.Server.Persistence/PersistenceModule.cs
index ac42e13b..564c98f9 100644
--- a/Server/src/Dfo.Server.Persistence/PersistenceModule.cs
+++ b/Server/src/Dfo.Server.Persistence/PersistenceModule.cs
@@ -164,5 +164,11 @@ public sealed class PersistenceModule : IServerModule
         // The character's quests, kept in the same database and therefore settled by the same
         // participant as the character and the items it owns.
         services.TryAddSingleton<IQuestDomainStore>(_ => new SqliteQuestDomainStore(databasePath));
+        // The character's combo pages, kept in the same database and therefore settled by the same
+        // participant again. It is handed the entry load's own reader of those rows rather than
+        // being left to select them a second way.
+        services.TryAddSingleton<IDarkKnightComboDomainStore>(
+            provider => new SqliteDarkKnightComboDomainStore(
+                databasePath, provider.GetRequiredService<ICharacterComboPageTable>()));
     }
 }
```

`git diff -- Server/src/Dfo.Server.Application/ApplicationModule.cs` 共三段 hunk，其中
`@@ -216,6 +216,16 @@` 与 `@@ -224,8 +234,6 @@` 两段是 TASK-03/04 的成果（坐标与设置两个
领域服务的注册，以及 `PlayingCharacter` 构造参数的两处删除），与本 Task 无关。属于本 Task 的是
第三段，**完整原样**：

```
@@ -253,6 +261,8 @@ public sealed class ApplicationModule : IServerModule
         services.TryAddSingleton<ICharacterEntryStateRepository, CharacterEntryAssembly>();
         services.TryAddSingleton<ICharacterContainerDomainService, CharacterContainerDomainService>();
         services.TryAddSingleton<ICharacterQuestLogDomainService, CharacterQuestLogDomainService>();
+        services.TryAddSingleton<
+            ICharacterDarkKnightComboDomainService, CharacterDarkKnightComboDomainService>();
         services.TryAddSingleton<ICharacterItemCooldownDomainService, CharacterItemCooldownDomainService>();
         services.TryAddSingleton<IItemUseJudgementService, ItemUseJudgementService>();
         // What entry has to grant before a character is published. One reader for the daily
```

（另两段 hunk 未贴的理由如上：它们不属于本 Task 的改动集合。完整 diff 用同一条命令可复现。）
DI 注册顺序在本仓与参与者顺序无关——参与者是**首次使用时**加入事务的，不是容器构建时。

### 4. CK-020：领域层中名字含 `store` 的类型逐个分类

枚举命令与**完整原始输出**（未裁剪，31 行）：

```
$ grep -rEn "^[[:space:]]*(public|internal|private|protected)?[[:space:]]*(sealed |abstract |static |partial |readonly |ref )*(class|interface|record|struct|enum)[[:space:]]+[A-Za-z0-9_]*[Ss]tore[A-Za-z0-9_]*" --include=*.cs Server/src/Dfo.Server.Data Server/src/Dfo.Server.Abstractions Server/src/Dfo.Server.Application Server/src/Dfo.Server.Persistence | grep -v "/obj/"
Server/src/Dfo.Server.Data/World/SessionEntryModels.cs:42:public sealed record ExpertStoreEntryState(bool OwnsOpenStore)
Server/src/Dfo.Server.Abstractions/Characters/ICharacterDomainStore.cs:24:public interface ICharacterDomainStore : IDomainStore<ICharacterDomainStoreSession>;
Server/src/Dfo.Server.Abstractions/Characters/ICharacterDomainStore.cs:36:public interface ICharacterDomainStoreSession
Server/src/Dfo.Server.Abstractions/Characters/ICharacterDomainStore.cs:66:public sealed record StoredCreature(
Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboDomainStore.cs:25:public interface IDarkKnightComboDomainStore : IDomainStore<IDarkKnightComboDomainStoreSession>;
Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboDomainStore.cs:43:public interface IDarkKnightComboDomainStoreSession
Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs:19:public interface IItemDomainStore : IDomainStore<IItemDomainStoreSession>;
Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs:38:public interface IItemDomainStoreSession : IDomainStoreSession<ContainerId, int, StoredItem>
Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs:59:public sealed record StoredItem(int SlotIndex, InventoryItemCoreState Core);
Server/src/Dfo.Server.Abstractions/Quests/IQuestDomainStore.cs:38:public interface IQuestDomainStore : IDomainStore<IQuestDomainStoreSession>;
Server/src/Dfo.Server.Abstractions/Quests/IQuestDomainStore.cs:48:public interface IQuestDomainStoreSession
Server/src/Dfo.Server.Abstractions/Quests/IQuestDomainStore.cs:70:public sealed record StoredActiveQuest(
Server/src/Dfo.Server.Abstractions/Quests/IQuestDomainStore.cs:79:public sealed record StoredClearedQuest(int QuestId, byte Mark);
Server/src/Dfo.Server.Abstractions/Quests/IQuestDomainStore.cs:86:public sealed record StoredTrackedQuest(short SlotIndex, int QuestId);
Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs:136:public interface IExpertStoreDomainService
Server/src/Dfo.Server.Abstractions/Transactions/DomainStores.cs:14:public interface IDomainStore
Server/src/Dfo.Server.Abstractions/Transactions/DomainStores.cs:33:public interface IDomainStore<out TSession> : IDomainStore
Server/src/Dfo.Server.Abstractions/Transactions/DomainStores.cs:56:public interface IDomainStoreSession<in TPlace, in TKey, TValue>
Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs:26:internal sealed class AmbientStoreAccess(IDomainTransactionAccessor transactions)
Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:212:internal abstract class StoreBackedItemContainer(
Server/src/Dfo.Server.Application/Sessions/ExpertStoreDomainService.cs:9:internal sealed class ExpertStoreDomainService : IExpertStoreDomainService
Server/src/Dfo.Server.Application/Sessions/ExpertStoreDomainService.cs:26:    private sealed record StoreOwner(SelectedSessionIdentity? Session);
Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterDomainStore.cs:21:internal sealed class SqliteCharacterDomainStore : ICharacterDomainStore
Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterDomainStore.cs:57:internal sealed class SqliteCharacterDomainStoreSession : ICharacterDomainStoreSession, ISqliteStoreFacet
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:29:internal sealed class SqliteDarkKnightComboDomainStore : IDarkKnightComboDomainStore
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:70:internal sealed class SqliteDarkKnightComboDomainStoreSession
Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs:240:internal interface ISqliteStoreFacet
Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs:20:internal sealed class SqliteItemDomainStore : IItemDomainStore
Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs:61:internal sealed class SqliteItemDomainStoreSession(SqliteDatabaseParticipant owner)
Server/src/Dfo.Server.Persistence/Quests/SqliteQuestDomainStore.cs:20:internal sealed class SqliteQuestDomainStore : IQuestDomainStore
Server/src/Dfo.Server.Persistence/Quests/SqliteQuestDomainStore.cs:56:internal sealed class SqliteQuestDomainStoreSession : IQuestDomainStoreSession, ISqliteStoreFacet
```

（以上 31 行为该命令的完整输出，逐字原样，未裁剪、未插入。）

范围说明：这里的「领域层」取的是四个生产工程 `Data` / `Abstractions` / `Application` /
`Persistence`（`Protocol` 与 `Networking` 不是领域层；`Dfo.Pvf` 的 `ChunkStore` 是归档实现，
不在领域层，故不在此枚举内）。逐个分类：

| 类型 | 分类 | 是不是「事务结算的真源」这个意义上的 store |
|---|---|---|
| `IDomainStore` / `IDomainStore<TSession>` / `IDomainStoreSession<,,>` | 载体协议本身 | **是**（协议的定义处） |
| `ICharacterDomainStore` / `ICharacterDomainStoreSession` | 领域 store 与其 session | **是** |
| `IItemDomainStore` / `IItemDomainStoreSession` | 领域 store 与其 session | **是** |
| `IQuestDomainStore` / `IQuestDomainStoreSession` | 领域 store 与其 session | **是** |
| `IDarkKnightComboDomainStore` / `IDarkKnightComboDomainStoreSession`（**本次新增**） | 领域 store 与其 session | **是** |
| `SqliteCharacterDomainStore(+Session)` / `SqliteItemDomainStore(+Session)` / `SqliteQuestDomainStore(+Session)` / `SqliteDarkKnightComboDomainStore(+Session)`（末者本次新增） | 上述 store 的持久化实现 | **是** |
| `StoredCreature` / `StoredItem` / `StoredActiveQuest` / `StoredClearedQuest` / `StoredTrackedQuest` | store 的存值记录 | **是**（`Stored*` 是「被 store 存着的值」，不是另一种 store） |
| `ISqliteStoreFacet` | 一个数据库参与者里「一种状态」的贡献面 | **是**（store 在 SQLite 侧的实现构件） |
| `AmbientStoreAccess` | 业务代码触达 store 的方式 | **是**（名字里的 store 指的就是上面这些） |
| `StoreBackedItemContainer` | 实例（容器）的基类，名字明说「背后是 store」 | **是**（指向 store，不是自称 store） |
| `IExpertStoreDomainService` / `ExpertStoreDomainService` / `StoreOwner` / `ExpertStoreEntryState` | **游戏里的「专家商店」摆摊** | **否——这是英文 store 的另一个词义（商店）**，与事务结算无关。这是**既有**情况，不是本次引入 |

**结论**：除「专家商店」那一族既有的同形异义之外，领域层里 `store` 一词仍然只指事务结算的真源。
本次新增的三个含 `store` 的类型全部属于真源一侧；本次新增的**实例**三个类型
（`IDarkKnightComboBook`、`DarkKnightComboBook`、`CharacterDarkKnightComboDomainService`）
与工厂接口一个（`ICharacterDarkKnightComboDomainService`）**名字里都没有 store**，因此上面的枚举
里根本看不到它们——这正是要的结果。

### 5. `Job == 9` 那个设计点：判断与理由

**我的选择：新的实例（与它背后的 store）不知道「职业不是 9 就没有连招页」，它只回答行。**

理由三条：

1. **地址里没有职业。** store 的 place 是 `OwnedCharacterId`（账号 + 角色），key 是页号。职业是
   `characters` 表的一列，不是这条地址的任何一部分。实例要「知道职业」就得去读第二张表，于是它
   从「一个角色的连招页」变成「一个角色的连招页 join 它的职业」，而这个 join 没有任何调用方要。
2. **`CharacterEntryStateServices.cs:52` 那个 `skills.Job == 9` 判的不是「有没有连招页」，
   判的是「这次入场的帧里带不带连招这一节」。** 它的产物是 `IReadOnlyList<...>?` 的
   null 与非 null，而 null 的下游含义写在
   `SelectedCharacterStateMessageFactory.cs:62-63`——非 null 才追加
   `DarkKnightComboStateMessage`。这是**帧的形状决定**，不是**行的事实**。把帧的形状决定塞进
   领域状态里，就是把协议知识倒灌回 Application。
3. **本 Spec 已定：那个纯投影不许改成去问实例或服务。** 既然它必须留在原地自己判职业，那么实例
   再判一次就是同一条规则的第二处实现——本仓 AGENTS.md 里「两个供料方，一个算法」那段讲的就是
   这类分叉的代价。

**对「行为逐值不变」的影响：没有影响。** 逐值不变靠的是「入场投影一行未动」这个事实，而不是靠
实例选了哪种做法——本 Task 完全没有改
`Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs`，也没有改
`SelectedCharacterStateMessageFactory.cs`、编码器或任何帧结构（见 §2 的 numstat：这三个文件都不在
改动集合里）。今天没有任何生产代码调用这个实例，所以它答什么都不进任何一个帧。

**这个选择留下的一处可观察差异，写在这里而不是藏起来**：如果库里存在一个 `Job != 9` 的角色却有
连招页行，入场投影会丢掉它们（判职业），而实例会如实答出来。今天没有调用方能观察到这个差异；
将来若有人想让实例替入场投影供料，**必须**由调用方保留 null / 空列表这个区分，因为那是帧的决定
而不是行的事实。这一点已写进 `IDarkKnightComboDomainStore` 与 `IDarkKnightComboBook` 的 XML doc。

### 6. 变异验证记录

两次变异，各自单独跑 `dotnet test ... --filter "FullyQualifiedName~DarkKnightComboBookTests"`。

**变异 1：按页号取错。** 把 `DarkKnightComboBook.FindPageAsync` 改成
`session.ReadAsync(place, (byte)(pageIndex ^ 1), cancellationToken)`（0 与 1 互换）。
结果 `失败: 4，通过: 8，总计: 12`（当时尚未加入 §3 的参与者判据，故为 12 条）。指名的失败：

```
Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.APageIsAnsweredByItsNumber
   Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
   ...DarkKnightComboBookTests.cs:line 64

Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.APageTheCharacterDoesNotHaveIsAnsweredWithNothing
   Assert.Null() Failure: Value is not null
Expected: null
Actual:   DarkKnightComboEntryState { PageIndex = 0, EncodedBody = System.Byte[] }
   ...DarkKnightComboBookTests.cs:line 81

Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ABookSeesItsOwnUseCasesUnsettledWrite
   Assert.NotNull() Failure: Value is null
   ...DarkKnightComboBookTests.cs:line 153

Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.ClearingAPageLeavesTheCharacterWithoutIt
   Assert.Null() Failure: Value is not null
Expected: null
Actual:   DarkKnightComboEntryState { PageIndex = 1, EncodedBody = System.Byte[] }
   ...DarkKnightComboBookTests.cs:line 183
```

**变异 2：漏掉某一页。** 在 `ComboPageSection.ReadRowsAsync` 的循环里加
`if (row.PageIndex == HighestPage) { continue; }`（读的时候丢掉第 1 页）。
结果 `失败: 2，通过: 10，总计: 12`。指名的失败：

```
Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.APageIsAnsweredByItsNumber
   Assert.NotNull() Failure: Value is null
   ...DarkKnightComboBookTests.cs:line 66

Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests.WhatIsAnsweredIsWhatTheRowsHold
   Assert.Equal() Failure: Values differ
Expected: 2
Actual:   1
   ...DarkKnightComboBookTests.cs:line 46
```

**还原**：两处均已改回。还原后单跑该文件 `失败: 0，通过: 12`（当时），加入参与者判据后
`失败: 0，通过: 13`。工作区无残留——§2 的 `git status --short` 与 `git diff --numstat` 就是还原之后
跑的，`ApplicationModule.cs` 仍是 `12 2`、`PersistenceModule.cs` 仍是 `6 0`，
`SqliteDarkKnightComboDomainStore.cs` 与 `DarkKnightComboBook.cs` 是新文件（`??`），
其内容不含上面两段变异（`HighestPage` 只出现在常量声明、`Write` 的范围拒绝与 `ReadRowsAsync` 的
范围拒绝三处；`FindPageAsync` 里是 `session.ReadAsync(place, pageIndex, cancellationToken)`）。

### 7. 判据输出与计数归因

**CK-018** `dotnet build Server/86JP.Server.slnx --nologo`：

```
已成功生成。
    0 个警告
    0 个错误
```

**CK-019** 新增用例（`Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs`），
对着真 SQLite 库。共 13 条：

| 用例 | 钉的是什么 |
|---|---|
| `WhatIsAnsweredIsWhatTheRowsHold` | 直接往表里写两页（**故意按 1、0 的顺序插入**），实例按页号顺序答出两页，body 逐字一致 |
| `APageIsAnsweredByItsNumber` | 按页号问，答的是那一页（两页 body 互不能冒充：`[0A 0B 0C]` 与 `[1D 1E]`，长度也不同） |
| `APageTheCharacterDoesNotHaveIsAnsweredWithNothing` | 没有的页答 null，不是空页 |
| `ACharacterWithNoRowsHasAnEmptyBook` | 没有行的角色（包括一切非暗帝）答空，不是报错 |
| `ABookKnowsWhoseItIs` | 实例有身份 |
| `TwoBooksAskedForTheSameCharacterAreTheSameBook` | 同一角色两次取到的是同一本 |
| `APageWrittenThroughTheBookIsSettledByTheUseCase` | **写入经 store 结算**：结算前表里没有，结算后表里就是写进去的那一页 |
| `ABookSeesItsOwnUseCasesUnsettledWrite` | 自己用例未落定的写自己看得见，外面看不见 |
| `APageWrittenAndThenTakenBackIsNotThere` | 回滚后实例与表都看不见 |
| `ClearingAPageLeavesTheCharacterWithoutIt` | 清一页只清那一页 |
| `APageTheEntryLoadWouldNotCarryIsRefused` | 写第 2 页被拒（`ArgumentOutOfRangeException`），且什么都没落库 |
| `ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead` | 库里已有的越界行读出来也被拒（`InvalidDataException`），与入场投影一致 |
| `TheComboStoreJoinsTheParticipantTheOtherStoresAlreadyJoined` | 四个真 store 同一事务只有一个参与者（CK-021 的可跑证据） |

单跑结果：`已通过! - 失败: 0，通过: 13，已跳过: 0，总计: 13`。

**CK-020**：见 §4（枚举命令 + 完整原始输出 + 逐个分类）。

**CK-021**：见 §3（三条依据 + 枚举完整输出 + 两段 Module diff + 一条会跑的判据）。

**CK-022** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
的七行逐项目汇总（原样）：

```
已通过! - 失败:     0，通过:   505，已跳过:     0，总计:   505，持续时间: 2 s - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52，持续时间: 1 s - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60，持续时间: 8 s - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   256，已跳过:     0，总计:   256，持续时间: 13 s - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6，持续时间: 8 s - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   223，已跳过:     2，总计:   225，持续时间: 17 s - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   960，已跳过:     0，总计:   960，持续时间: 14 s - Dfo.Server.Application.Tests.dll (net10.0)
```

**计数**：`505 + 52 + 60 + 256 + 6 + 223 + 960 = 2062` 通过，**2 跳过，0 失败**。

**归因**：基线 2049 通过 / 2 跳过 / 0 失败 → 现在 **2062 / 2 / 0**。
`2062 - 2049 = 13`，恰好等于本 Task 新增的 13 条（全部落在 `Dfo.Server.Application.Tests`）。
跳过数 2 未变（两条 `PvfQuestDefinitionSourceTests` / `PvfTownDefinitionSourceTests` 的
SkippableFact，输出里逐条可见）。失败 0，没有出现两条已知偶发
（`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` 与
`Dfo.Server.Networking.Tests` 的并行偶发）中的任何一条。

### 8. BOM 与行尾检查

用 Python 读 bytes（不用 MSYS 的 grep/sed/tail），对本 Task 碰过与新建的每个文件逐个查：

```
$ python -c "... open(p,'rb') ..."
Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboDomainStore.cs      no-BOM  CR=0  LF=44   CRLF=0  trailingNL=True  nonASCIIbytes=0
Server/src/Dfo.Server.Abstractions/Characters/IDarkKnightComboBook.cs             no-BOM  CR=0  LF=78   CRLF=0  trailingNL=True  nonASCIIbytes=0
Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs  no-BOM  CR=0  LF=213  CRLF=0  trailingNL=True  nonASCIIbytes=0
Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs               no-BOM  CR=0  LF=72   CRLF=0  trailingNL=True  nonASCIIbytes=0
Server/tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs  no-BOM  CR=0  LF=385  CRLF=0  trailingNL=True  nonASCIIbytes=0
Server/src/Dfo.Server.Application/ApplicationModule.cs                            no-BOM  CR=0  LF=311  CRLF=0  trailingNL=True  nonASCIIbytes=0
Server/src/Dfo.Server.Persistence/PersistenceModule.cs                            no-BOM  CR=0  LF=174  CRLF=0  trailingNL=True  nonASCIIbytes=0
.rks-agent/.../TASK-05-combo-pages-get-a-store-and-an-instance.md                 no-BOM  CR=0  LF=33   CRLF=0  trailingNL=True  nonASCIIbytes=1155
```

（上表是**追加本段落之前**的一次测量，Task 文件当时 33 行；追加之后 LF 数当然会变，其余三项
——no-BOM / CR=0 / 末尾换行——由同一条命令在追加后复测仍成立。`nonASCIIbytes=1155` 是 Task 文件里
的中文，UTF-8 无 BOM；七个 `.cs` 文件全部纯 ASCII。）

与仓库现状一致：同目录既有文件（`IQuestDomainStore.cs`、`SqliteQuestDomainStore.cs`、
`ICharacterSettingsDomainService.cs`、`CharacterSettingsDomainService.cs`、
`CharacterSettingsTests.cs`）同样是 no-BOM、CRLF=0。
（注：`.editorconfig` 写的是 `end_of_line = crlf`，而工作树里这些 `.cs` 实际是 LF。我选择与**工作树
现状**一致而不是与 .editorconfig 一致，因为跟 .editorconfig 走会让每个新文件的行尾与邻居不同。
这条不一致是既有的，不是本 Task 引入的。）

### 9. 注释与文档的影响

- **没有任何既有注释因本次改动变成假话。** 逐条查过与本次改动相邻的四处：
  - `PersistenceModule` 类注释「Three of the registrations below ask for something the content
    module provides」——本次新增的注册要的是同一个 Module 里的 `ICharacterComboPageTable`，
    不是内容源，所以「三处」不变。
  - `SqliteDatabaseParticipant` 的「Each kind of state contributes a `ISqliteStoreFacet`」——
    本次正是又贡献了一个 facet，仍然成立。
  - `IQuestDomainStore` 关于「这些行有第二个读者」的四段——讲的是任务的行，与连招无关。
  - `CharacterEntryAssembly` 里 `Dark Knight combo page is out of range` 那条既有拒绝——本次没动它，
    而且新 store 采用了同一条范围与同一句话，两边一致。
- **一处既有漂移，不是本次造成的，据实报出不代为修改**：
  `Server/tests/Dfo.Server.Application.Tests/Transactions/OneDatabaseOneParticipantTests.cs:23`
  的 remarks 写着「Items are the only real one so far」。这句在本 Task 开工**之前**就已经不成立
  （人物 store 与任务 store 早已存在）。本次没有让它更假也没有让它变假，且该文件不在本 Task 的
  owned boundary 内，故未改。
- **一处需要移交给 T-009 的记载**：`AGENTS.md` 的「载体协议」一节写
  「`IItemDomainStoreSession` 直接就是它的一个具名实例；`ICharacterDomainStoreSession` 与
  `IQuestDomainStoreSession` 不是」。这三句本身仍然逐句成立，但这份枚举现在**少了第四个**
  （`IDarkKnightComboDomainStoreSession`，属于「具名实例」那一类）。`AGENTS.md` 是 **BD-016 / T-009**
  的 owned boundary，本 Task 不得改，故在此登记，请 T-009 一并写入。

### 10. 诚实说明

- **TDD 顺序**：契约写着 `Required methods: test-driven-development`。实际执行顺序是先写了
  store 与实例的生产代码，再写 `DarkKnightComboBookTests`，**不是先红后绿**。补偿手段是 §6 的两次
  变异验证——它证明的是同一件事（这些判据确实钉住了行为、坏掉会指名报红），但它不等于 TDD 的
  先后顺序，这里不掩饰。
- **本 Task 没有删那个恒 null 的单数连招字段**（`DarkKnightComboState` 与
  `ActiveCharacterState.DarkKnightCombo`），那是 TASK-06 的事。
- **没有建 Git 检查点、没有提交、没有 stash。**
- 本次没有新增协议帧，没有改任何帧的字节布局，没有新建表，没有放宽
  `CommandHandlerArchitectureTests` 的判定谓词或豁免清单（该文件不在改动集合里）。

## Executor Self-check 1

### 对照 Required checks

| 判据 | 期望 | 实际 | 结论 |
|---|---|---|---|
| CK-018 `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 | `0 个警告 / 0 个错误`（§7） | **达成** |
| CK-019 对着真库写入连招页行后向实例按页号提问 | 答得出来且与行一致 | 13 条新用例全绿，其中 `WhatIsAnsweredIsWhatTheRowsHold` 与 `APageIsAnsweredByItsNumber` 直接钉这一条；两页 body 互不能冒充（§7 表） | **达成** |
| CK-020 领域层中名字含 `store` 的全部类型逐个分类 | 只有事务结算的真源用这个词 | §4：31 行原始输出 + 逐个分类。唯一不属真源的是既有的「专家商店」一族（英文同形异义），本次新增的三个含 store 的类型全在真源一侧，新增的实例四个类型名字里都没有 store | **达成（并如实报出既有的同形异义）** |
| CK-021 事务参与者的注册与结算顺序对照 | 别的 store 的结算顺序未变 | §3：identity 相同 → 不新增参与者（有会跑的判据钉住）；facet 只追加且今天没有生产调用方（枚举完整输出）；两个 Module 的接线是纯追加无重排（diff） | **达成** |
| CK-022 全量 `dotnet test` | 通过 | `2062 通过 / 2 跳过 / 0 失败`；相对基线 +13，全部为本次新增（§7） | **达成** |

### 对照 Task 契约的 Acceptance conditions

| 条件 | 证据 | 结论 |
|---|---|---|
| 实例能按页号答出一个角色的连招页，且与库中的行一致 | `IDarkKnightComboBook.PagesAsync` / `FindPageAsync`；`WhatIsAnsweredIsWhatTheRowsHold`、`APageIsAnsweredByItsNumber`、`APageTheCharacterDoesNotHaveIsAnsweredWithNothing`、`ACharacterWithNoRowsHasAnEmptyBook` 四条对着真库 | **达成** |
| 写入经 store 结算 | `SetPageAsync` / `ClearPageAsync` 都经 `AmbientStoreAccess.ChangeAsync` → store session → `SqliteDatabaseParticipant` 的 facet；`APageWrittenThroughTheBookIsSettledByTheUseCase`（结算前表里没有、结算后有）与 `APageWrittenAndThenTakenBackIsNotThere`（回滚后没有）两条钉住 | **达成** |
| `store` 一词仍只指事务结算的真源；实例不得叫 store | §4 的枚举与分类；实例三个实现类型与一个契约接口名字里都不含 store（`IDarkKnightComboBook` / `DarkKnightComboBook` / `ICharacterDarkKnightComboDomainService` / `CharacterDarkKnightComboDomainService`） | **达成** |
| 别的 store 的结算顺序未变 | §3 三条依据 + 一条会跑的判据 `TheComboStoreJoinsTheParticipantTheOtherStoresAlreadyJoined` | **达成；硬性停止点未触发** |
| 全量判据全绿 | §7：2062 / 2 / 0 | **达成** |

### 对照 Owned boundary 与 Permitted files

本次动过的文件共 7 个（4 新建源码 + 1 新建判据 + 2 处接线），逐个落在契约的 Permitted files 里：

- `Abstractions/Characters/IDarkKnightComboDomainStore.cs`、`.../IDarkKnightComboBook.cs`
  → 「`Server/src/Dfo.Server.Abstractions/Characters/` 下新增的连招 store 接口、session 接口与连招实例接口」
- `Persistence/Characters/SqliteDarkKnightComboDomainStore.cs`
  → 「`Server/src/Dfo.Server.Persistence/Characters/` 下该 store 的实现」
- `Persistence/PersistenceModule.cs`、`Application/ApplicationModule.cs`
  → 「两个 Module 的接线」
- `Application/Characters/DarkKnightComboBook.cs`
  → 「`Server/src/Dfo.Server.Application/Characters/` 下连招实例的实现」
- `tests/Dfo.Server.Application.Tests/Characters/DarkKnightComboBookTests.cs`
  → 「对着真库的新增判据」（BD-015 也把新增用例放在
  `Server/tests/Dfo.Server.Application.Tests/Characters/`）

未越界：入场投影、协议切片、编码器、`CommandHandlerArchitectureTests`、`AGENTS.md`、
以及 TASK-01..04 的任何成果，一行未动。

### 对照 Task-specific authorization

不建 Git 检查点、不提交、不 stash —— 已遵守。

### 未达成 / 需要控制器知晓的

1. **TDD 的先后顺序没有严格照做**（先生产代码后判据），补偿是两次变异验证，见 §10。
2. **`AGENTS.md` 的载体协议枚举需要 T-009 补第四项**，见 §9。
3. **`OneDatabaseOneParticipantTests` 的一句既有注释已经过期**（非本次造成，不在本 boundary 内），
   见 §9，供控制器决定是否另派。

## Independent Quick Review 1

- Verdict: **passed**
- Reviewed contract revision: 1（无 amendment）
- Reviewed result: Execution Result 1 / Executor Self-check 1
- Owned diff identity: HEAD `427331bcb07f60121c97920287ac341c9cb14dee`，工作树未提交。本 Task 的
  7 个文件 = 新建 `IDarkKnightComboDomainStore.cs`、`IDarkKnightComboBook.cs`、
  `SqliteDarkKnightComboDomainStore.cs`、`DarkKnightComboBook.cs`、`DarkKnightComboBookTests.cs`
  + 追加 `PersistenceModule.cs`（`6 0`）与 `ApplicationModule.cs`（第三段 hunk `+2`）
- 复核者身份：全新上下文，未参与实现；下列结论全部由本轮自跑命令得出，未采信交付自陈

### 本轮自跑的命令与结果

| 命令 | 结果 | 与交付声称 |
|---|---|---|
| `dotnet build Server/86JP.Server.slnx --nologo` | `0 个警告 / 0 个错误` | 一致（CK-018） |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 505+52+60+256+6+223+960 = **2062 通过 / 2 跳过 / 0 失败** | **逐项目计数完全一致**（CK-022）；未出现两条既知偶发 |
| `dotnet test … --filter FullyQualifiedName~DarkKnightComboBookTests` | `失败: 0，通过: 13` | 一致（CK-019） |
| CK-020 枚举（原样重跑交付 §4 的 grep） | **31 行，逐行与交付 §4 相同** | 一致 |
| 新类型引用枚举（原样重跑交付 §3 的 grep） | **33 行**；剔除 5 个新文件后剩 **3 行 / 2 个文件**（`ApplicationModule.cs:265`、`PersistenceModule.cs:170-171`） | 一致：生产侧只有接线，无调用方 |
| `git log -1` / `git stash list` / `git status --short` / `git diff --numstat` | `427331bc`；stash 空；`??` 16 项；numstat **27 行**，`ApplicationModule.cs 12 2`、`PersistenceModule.cs 6 0` | 一致，无夹带、无检查点、无提交 |
| BOM/行尾（Python 读 bytes，不用 MSYS 文本工具） | 7 个 `.cs` 全部 no-BOM / CR=0 / 末尾换行 / 纯 ASCII / 无行尾空白；LF 计数与交付 §8 逐个相同 | 一致 |

### 一、硬性停止点：确认**未触发**（逐条独立核实，非采信）

- **(a) 参与者按 identity 归并** —— 读 `Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs:36-42`
  与 `:55-56`：identity 命中即 `return existing`，**只有新建路径才 `this.order.Add(participant)`**。
  `grep -rn "SqliteConnectionStringBuilder" Server/src` 只有 5 处，人物/物品/任务/连招四个 store
  与 `SqliteDatabase` 逐字同一句 `new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString()`，
  且 `PersistenceModule.cs:158-172` 给四者传的是**同一个** `databasePath`。故新 store 加入的是既有那个
  `SqliteDatabaseParticipant`，`order` 不增项，别的参与者相对位置无从改变。此结论与 `AGENTS.md:206-209`
  「登记按 identity 去重……SQLite 侧的 identity 是连接字符串，即一个数据库一个参与者」一致。
- **(b) facet 只追加** —— `SqliteDatabaseParticipant.Facet`（`:50-61`）命中既有 key 直接返回，
  未命中才 `order.Add`。核实无误。
- **(c) 无生产调用方** —— 见上表引用枚举，独立复现。
- **(d) 两个 Module 纯追加** —— 自跑 `git diff` 逐段读过：`PersistenceModule` 是文件末尾 `@@ -164,5 +164,11 @@`
  的 6 行纯新增；`ApplicationModule` 第三段 `@@ -253,6 +261,8 @@` 是 2 行纯新增，另两段
  hunk 注册的是 `ICharacterPositionDomainService` / `ICharacterSettingsDomainService`
  与 `PlayingCharacter` 构造参数删除，确属 TASK-03/04，归属声称属实。**无重排、无删除。**
- **那条判据是真在断言这件事，不是名字像** —— 我做了交付**没做过**的第三次变异：把
  `SqliteDarkKnightComboDomainStore.StoreIdentity` 改为 `this.connectionString + ";combo"`，
  `TheComboStoreJoinsTheParticipantTheOtherStoresAlreadyJoined` 立刻报红：
  `Assert.Single() Failure: The collection contained 2 items`（`DarkKnightComboBookTests.cs:line 241`）。
  **判据确实钉住「四个 store 共用一个 identity」这件事。**

**结论：硬性停止点确未触发，不需要 reroute。** 三条依据全部成立且可复现。

### 二、裁定：TDD 顺序偏离 —— **可接受，不阻断**

依据：TDD 在此要防的是「判据照着实现写、因而不约束实现」。变异验证是对同一性质的**直接测量**，
而先红后绿只是**构造性地**给出它，故变异可以替代——**前提是变异覆盖了判据声称覆盖的东西**。
交付的两次变异只覆盖 13 条中的 6 条，且**恰好漏掉了承载硬性停止点证据的那一条**（§6 自陈
「当时尚未加入 §3 的参与者判据」）。我补做了三次变异把这个缺口补上：

| 变异 | 报红用例 |
|---|---|
| M1 复现（`FindPageAsync` 页号 `^ 1`） | `APageIsAnsweredByItsNumber`、`APageTheCharacterDoesNotHaveIsAnsweredWithNothing`、`ABookSeesItsOwnUseCasesUnsettledWrite`、`ClearingAPageLeavesTheCharacterWithoutIt`（4 失败 / 9 通过） |
| M2 复现（`ReadRowsAsync` 丢第 1 页） | `APageIsAnsweredByItsNumber`、`WhatIsAnsweredIsWhatTheRowsHold`（2 失败 / 11 通过） |
| **M3 新增**（store identity 加后缀） | `TheComboStoreJoinsTheParticipantTheOtherStoresAlreadyJoined`（1 失败 / 12 通过） |
| **M4 新增**（删掉 `Write` 与 `ReadRowsAsync` 两处范围拒绝） | `APageTheEntryLoadWouldNotCarryIsRefused`、`ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead`（2 失败 / 11 通过） |
| **M5 新增**（`SetPageAsync` 不再 `session.Write`） | `APageWrittenThroughTheBookIsSettledByTheUseCase`、`TwoBooksAskedForTheSameCharacterAreTheSameBook`、`APageTheEntryLoadWouldNotCarryIsRefused`、`ABookSeesItsOwnUseCasesUnsettledWrite`（4 失败 / 9 通过） |

M1、M2 的失败用例、断言与行号与交付 §6 记载**逐条吻合**。五次变异合计证明 **13 条中 10 条非空转**，
且全部报红都带全限定名与 Expected/Actual。未被任一变异移动的 3 条
（`ACharacterWithNoRowsHasAnEmptyBook`、`ABookKnowsWhoseItIs`、`APageWrittenAndThenTakenBackIsNotThere`）
形状简单、断言的是具体可观察值，经阅读确认非同义反复。

**每次变异后均已还原并核对**：`md5sum` 与变异前备份逐字节相同
（`DarkKnightComboBook.cs` = `ef5544e8464a59828f47b6794184ff35`，
`SqliteDarkKnightComboDomainStore.cs` = `65b2864c97c5babc284a7e3cfd3bfb7a`），
还原后重跑全量 `dotnet test` 仍为 **2062 / 2 / 0**，`git diff --numstat` 仍为 27 行且两个 Module 数字未变。
**工作区无残留。**

裁定：偏离**不构成阻断**。补偿在补完缺口后足以替代 TDD 提供的那一条保证。留作非阻断观察：
执行者的自查未发现自己的变异集漏掉了承载硬性停止点的那条判据——这是补偿手段自身缺少覆盖度核对。

### 三、裁定：复用 `DarkKnightComboEntryState` 而非另造 `StoredComboPage` —— **可接受**

依据三条：

1. **分层合规**。`Server/src/Dfo.Server.Abstractions/Dfo.Server.Abstractions.csproj` **本就**
   `ProjectReference` 了 `Dfo.Server.Data`。引用方向 Data → Abstractions 成立，不构成渗透。
2. **不是先例的首创**。既有 `StoredItem(int SlotIndex, InventoryItemCoreState Core)`
   （`IItemDomainStore.cs:59`）里的 `InventoryItemCoreState` 就是 Data 层类型。`Stored*` 的既有语义
   是「键 + 载荷」的包装记录，而 `DarkKnightComboEntryState(byte PageIndex, byte[] EncodedBody)`
   **已经就是这个形状**——再包一层 `StoredComboPage(byte PageIndex, byte[] Body)` 得到的是
   逐字段同形的第二个记录，正是本仓已付过学费的那种漂移。
3. 惯例是「`Stored*` 表达键+载荷」，不是「必须新造一个类型」。这里键与载荷已在同一个记录里。

对照 `IQuestDomainStore` 的 `StoredActiveQuest` / `StoredClearedQuest` / `StoredTrackedQuest`：
三者都是**在 Data 记录之外另加了键或守卫**（`Slot`、`ExpectedTriggerValue`、`Mark`），
因而各有存在理由；连招这里没有这样的增量。**惯例未被打破，是不适用。**

### 四、`Job == 9` 的设计判断 —— **论断属实；披露基本完整，补一处**

- 核实 `CharacterEntryStateServices.cs:52`：`skills.Job == 9 ? …ToArray() : null`，产物是
  `IReadOnlyList<…>?` 的 null 与非 null；`SelectedCharacterStateMessageFactory.cs:61-63`
  非 null **且** `Job == 9` 才 `messages.Add(new DarkKnightComboStateMessage(combos))`。
  **确系「帧里带不带这一节」的判定，不是「行在不在」。** 执行者的论断成立。
- `git status --short` 核实：`CharacterEntryStateServices.cs`、`SelectedCharacterStateMessageFactory.cs`、
  `CharacterEntryAssembly.cs`、`SelectCharacterPreparationService.cs` **四个文件均未修改**，
  加上无生产调用方，**行为逐值不变成立**。
- 另一处 `Job != 9`（`SelectCharacterPreparationService.cs:362`）经核实是 PvP 初始技能**槽位分配**，
  与连招页无关，不是漏报的第四处判定。
- **披露补一处（非阻断）**：除已披露的职业差异外，读写两侧还有一处**范围规则不对称**。
  行网关读路径经 `EntryBlob.Bounded(reader, 1, EntryBlob.Combo /* 64 KiB */, …)`
  （`CharacterEntryStateTables.cs:21,126`）对 body 长度设了上界，而 `ComboPageSection.Write`
  只校验页号、不校验 body 长度。即：`SetPageAsync` 能写进一个超过 64 KiB 的 body，
  而此后**任何**读（包括入场投影）都会拒绝它。这与执行者已经处理的页号不对称是同一类问题、
  处理方式却不同。今天无调用方、写路径 `internal` 且只能经新实例触达，故不构成缺陷，
  但 §5 的差异清单未涵盖它，登记供最终评审与后续调用方（T-006 起）知悉。

### 五、CK-020 命名枚举 —— **属实**

自跑同一条 grep 得 **31 行，与交付 §4 逐行相同**。逐个分类核对无误：唯一非「事务结算真源」的是
`IExpertStoreDomainService` / `ExpertStoreDomainService` / `StoreOwner` / `ExpertStoreEntryState`
一族（英文「商店」同形异义，**既有**，非本次引入）。本次新增的 3 个含 `store` 的类型全在真源一侧；
新增的实例与工厂 4 个类型（`IDarkKnightComboBook` / `DarkKnightComboBook` /
`ICharacterDarkKnightComboDomainService` / `CharacterDarkKnightComboDomainService`）名字里都不含 store。

补一次**更宽松**的正则跨 `Server/src` 全域复查（防止枚举正则漏掉换行声明）：域外只多出
`Dfo.Pvf/Chunks/ChunkStore.cs:25`（归档实现，执行者已声明排除）与
`Dfo.Server.Protocol/…/ExpertStoreClosedMessage`（同一「商店」词义，既有）。**无遗漏、无新增违例。**

### 六、常规项 —— 全部通过

- **对着真库**：`Fixture` 建真 SQLite 文件、真 `SqliteDatabase` 建表、真
  `SqliteCharacterComboPageTable` 与真 `SqliteDarkKnightComboDomainStore`；`StoredPagesAsync`
  用独立连接直查表核对。核实无 stand-in store。
- **能区分页号**：M1/M2 已证（见上表），两页 body `[0A 0B 0C]` 与 `[1D 1E]` 字节与长度均不同，
  互不能冒充。
- **不新建表**：`ComboPageSection` 读走 `ICharacterComboPageTable.ReadAsync`，写/清是既有表
  `character_dark_knight_combo_skill_pages` 上的 INSERT…ON CONFLICT / DELETE。
  全仓 `ICharacterComboPageTable` 引用核实无第二条 SELECT。
- **恒 null 的单数连招字段仍在**：`CharacterModels.cs:173` `DarkKnightComboState` 与
  `:181` `ActiveCharacterState.DarkKnightCombo` **均未删**，TASK-06 的标的完好。
- **不新增协议帧、不改字节布局**：本 Task 未触及 `Dfo.Server.Protocol` 任何文件（numstat 中
  Protocol 侧改动全属 TASK-01..04）。
- **`CommandHandlerArchitectureTests` 未被改动、未被放宽**：
  `git status --short -- "*CommandHandlerArchitectureTests*"` 空；全量测试中通过。
- **既有测试期望未改**：本 Task owned diff 中无任何既有测试文件；两个 Module 的 diff 为纯追加。
- **授权遵守**：HEAD 仍 `427331bc`，stash 空，无提交、无检查点。

### 记载纪律核查

交付 §2 / §3 / §4 / §7 / §8 的枚举、计数、「顺序未变」「未改动」声称均附了产生命令与完整原始输出，
且明确标注了「以上 N 行为完整输出、未裁剪」。本轮逐条复跑，**输出逐行吻合，未发现裁剪或从样本
推全体**。一处偏弱（非阻断）：§9「没有任何既有注释因本次改动变成假话」是一句全称判断，
支撑是 4 处点检而非一条产生它的命令。我抽查了其中风险最高的一处
（`PersistenceModule` 类注释「Three of the registrations below ask for something the content
module provides」）：新注册要的 `ICharacterComboPageTable` 由**本 Module 自己**在 `:115` 提供，
不是内容模块，故「三处」确实未变——该判断本身正确，只是记载形式弱于本 Spec 的要求。

### 非阻断观察（不要求本轮处置）

1. `DarkKnightComboBookTests.cs:366` 的 `CountingTransaction` 是替身，不是生产
   `DomainTransaction`，故 §3 「四个真 store **同一事务**只有一个参与者」的措辞略强于判据实测的
   「四个 store 答同一个 identity」。该替身写法与既有
   `OneDatabaseOneParticipantTests.RecordingTransaction` 同款、属本仓惯例，且真
   `DomainTransaction` 的去重另有 `TwoKindsOfStateInOneDatabaseTakePartAsOneParticipant` 覆盖，
   不构成缺陷。
2. 上文第四条的 body 长度上界不对称（写不校验、读拒绝）。
3. 执行者的变异集漏掉了承载硬性停止点的那条判据，已由本轮 M3 补上。
4. 执行者主动移交的两项（`AGENTS.md` 载体协议枚举缺第四项 → T-009/BD-016；
   `OneDatabaseOneParticipantTests.cs:23` 既有过期注释）经核实属实，同意作为移交处理。

### 说明

本 verdict 只证明这一个 Task Unit 的有限缺陷检查通过，不代表完整变更评审，
也不替代接受前的新鲜验证。
