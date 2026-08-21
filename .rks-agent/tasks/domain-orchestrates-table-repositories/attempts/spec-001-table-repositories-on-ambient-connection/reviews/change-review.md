---
kind: rks-change-review
spec: ./../spec.md
plan: ./../plan.md
contract: ./../TaskContract.md
scope: 完整变更集（相对接受基线 ea95d49）
state: approved
---

# spec-001 变更评审

## 一、评审方式

八个任务单在实施期间各自做过独立快评，那些快评做的是「新代码 vs 被删旧代码」的逐语句对照。
本次评审不重做那件事，而是看整体结果，分三个维度并行进行，每个维度由一名独立评审者只读核实，
不采信任何执行记录里的数：

1. 分层与契约的一致性（RQ-001 / RQ-002 / RQ-005）
2. 事务保证与装配正确性（RQ-002 / RQ-003 / RQ-004 / RQ-006 / RQ-007）
3. 测试套件的完整性与可证伪性（RQ-008）

## 二、规格要求的独立核实

| RQ | 要求 | 核实结论 | 证据 |
|---|---|---|---|
| RQ-001 | 每个仓储的全部语句只提及一张表 | 成立 | `Persistence/{Accounts,Characters,Inventory}` 全部 65 个类，剔注释后按 52 张表名匹配 FROM/INTO/UPDATE/JOIN：提及 >1 张表的类为 0 |
| RQ-002 | 仓储不自开连接、不自开或结算事务 | 成立 | `new SqliteConnection(` 恰 3 处（`SqliteDatabase.cs:70`、`SqliteDatabaseParticipant.cs:100`、`CharacterDatabaseSchema.cs:53`）；`BeginTransaction` 恰 2 处，均在参与者内且为 `??=` deferred。58 个表仓储中 0 个 |
| RQ-003 | 一次用例内的多次读看到同一份快照 | 成立 | 一个用例一条连接一个 deferred 事务；三家参与者的 Enlist 键逐字相同故去重成立；全部 5 处 `new DomainTransaction()` 都在「当前无用例」分支内，用例内不会开第二条连接 |
| RQ-004 | 一次用例内对多张表的改动全有或全无 | 成立 | ProtocolModule 19 条命令逐条追到底：Direct 6 条全部只读或纯计算；Workflow 3 条中两条只动内存目录，Compensate 的唯一持久写有写下的理由；其余 10 条 Transactional |
| RQ-005 | 跨表组装在领域层且只有一处定义 | 成立 | 三个记录各 1 处构造，全在 Application：`AccountEntryAssembly.cs:58`、`CharacterEntryAssembly.cs:91`、`InventoryEntryAssembly.cs:75` |
| RQ-006 | 持久化实现不查询表结构元数据 | 成立 | `sqlite_master` 0 处；`PRAGMA table_info` 仅 `CharacterDatabaseSchema.cs:130` 一处，属唯一归属者 |
| RQ-007 | 拆分不引入新的表或列声明 | 成立（含一处经授权的例外，见下） | 表 52 → 53、列 404 → 423；差异只有 `dungeon_persistent_effect_outbox` 一张，它是补上一处既有的空洞而非新表 |
| RQ-008 | 对外可观察行为不变 | 成立 | Protocol.Tests 203/203；改动行中 `Assert.` 条数 0；全树 260 种、910 处字节与十六进制字面量与基线一字节不差；三份整记录金样本原样通过 |

装配完整性：声明 58 个 `I*Table`，注册 58 个，双向差集均为空；9 个领域装配的每一个构造函数参数
都能从容器解析；`SqliteDatabase` 是单例且不持有任何每用例状态（每次调用现取参与者）。

## 三、必须改的三条，均已处理

### CR-1 入场装载在全新库上必然抛错（真实回归，本次引入）

`dungeon_persistent_effect_outbox` 是全库唯一一张**生产代码读写、却无人声明**的表。
基线上这不致命，因为被删的 `SqliteDungeonPersistentEffectRecoveryRepository` 先问一句
`sqlite_master` 里有没有它，没有就安静返回空。T-005 删掉了那一问——理由是「建表已集中到唯一归属者
且创建全部表，该判定恒为真」——而这张表恰恰是那个前提不成立的一张。

后果：`SelectCharacterPreparationService` 每次选角都调 `RecoverAsync`，于是**任何全新启动的服务器
第一次选角就会抛 `no such table`**。既有用例发现不了，因为它们全都自己先把这张表建出来。

已用新增用例
`Persistence.Tests/Characters/DungeonRecoveryOnAFreshDatabaseTests.ACharacterEntersOnADatabaseThatHasOnlyEverBeenBroughtIntoShape`
复现（修前抛 `SqliteException: no such table`），修法是在唯一归属者里声明这张表。
见下方合同修订。

### CR-2 唯一一处绕开「无用例不许写」守卫的写

`CharacterEntryTables.cs` 的建角用 `INSERT … RETURNING`，为了读回 id 走的是
`ReadFirstAsync` → `SqliteDatabase.ReadAsync`——而读路径允许在没有用例时开一次性连接。
它是全仓唯一一条能在边界外落地的持久写，且没有任何用例能发现
（夹具的 `PerCall.CreateAsync` 总是套着 lease，既有的「边界外写要被拒绝」用例打的是 `ICharacterStore`）。

已加 `SqliteTable.WriteReturningAsync`：改库的语句无论怎么措辞都是写，走写路径、边界外照样被拒。
新增用例 `DungeonRecoveryOnAFreshDatabaseTests.BringingACharacterIntoBeingOutsideAUseCaseIsRefused`
钉住它。

### CR-3 契约注释错挂

`Abstractions/Persistence/AccountTables.cs`：`SetLuckyStarAsync` 的 `<summary>` 落到了
`FindCubeFragmentsAsync` 头上，后者两个 summary、前者一个没有。契约注释是这次重构的设计依据，
指错成员比没有更坏，而 0 警告的构建抓不到它。已各归各位。

## 四、评审提出、判定为不改的两条

### CR-4 并发「先读后写」的失败模式（真实、已文档化、超出本 Spec 边界）

「一次用例一个 deferred 事务」让用例内的多次读看到同一时刻（RQ-003），也让长读不挡写。
代价是：用例 A 与 B 都先读、A 先提交之后，B 再写时它手里的视图已经过期，数据库拒绝在其上写。
等待帮不上忙——过期的是时刻不是锁——但从连接看这就是「库忙」，于是它按
`Microsoft.Data.Sqlite` 的默认 30 秒等满才抛。全仓没有任何地方设过这个超时，也没有在 BUSY 上重试。

评审者要求「恢复并发用例里被放宽的断言，或另加用例证明读后写并发不会被打回」。
判定：**两者都不做，改为写下来并上报**。理由：
- 「两侧都必须成功」在今天是假的，恢复它只会得到一条必红的用例；
- 任何直接证明这一失败模式的用例都要等满 30 秒（实测如此），在一个别处只有 10 秒预算的套件里
  这是真实代价，而缩短它需要给连接串加超时——那是一个生产决策；
- 真正的解法是「整条用例重来」，属于派发用例的那一层，不属于数据库，也不在本 Spec 的边界内。

已写入 `AGENTS.md`（与已知假红同一节），并作为本次唯一需要用户裁决的事项上报。

### CR-5 Prepare 失败时的补偿写会被自己的边界收回

`PrepareSelectedCharacterSessionCommand` 在 T-002 由 Workflow 改为 Transactional 之后，
其 catch 里的 cleanup 写进 ambient 事务，随后异常上抛被整体回滚——补偿写等于没写。
这与 `ProtocolModule.cs` 给 Compensate 免边界的理由字面上冲突。

判定：**这是 T-005 已经识别、写下并接受的同一处取舍**，见
`Abstractions/Sessions/ISessionPresence.cs` 的 `PersistAsync` 注释与
`Application.Tests/Characters/OutsideTransactionTests.ASessionEndingInsideAnotherUseCaseSettlesWithIt`：
一次失败的选角会带走被顶掉角色的最后站位，留下更早存下的那个；代价小且有界，
另一种做法是选角根本无法进行。不改，此处备案以便与 Compensate 的注释对读。

## 五、评审提出、批为后续清理的若干条

这些都不动摇任何 RQ，也都不是缺陷，是这次拆分留下的一致性欠账。批为一个后续清理单，不阻塞本次：

1. 五对接口描述同一张表（`characters`、两张 subtype、`character_init_flags`、
   `character_daily_counters`），与 `CharacterTables.cs` 自己写下的「一张表一个仓储」不一致；
   其中 `character_daily_counters` 的两个契约分处两个文件两个文件夹。
2. 契约归属错位三处：`IDungeonEffectOutboxTable` 在 `AccountTables.cs`；
   `CubeFragmentRow` 在 `InventoryTables.cs` 而唯一使用者在 `AccountTables.cs`；
   `ICharacterDailyCounterTable` 在 `InventoryTables.cs` 而表属 character。
3. 5 张表直接返回领域记录、其余 28 张返回裸 `*Row`，同一方法两种约定且签名上看不出。
4. `CharacterRosterService` 让领域合成 `name` 列（UTF8 失败回退 hex），
   把「这张表拿两列存名字」推给了领域。
5. `ICharacterDimensionFlagTable.ReadAsync` 返回无名元组，调用方只能写 `Item1..Item4`。
6. 三个装配不同构：`InventoryEntryAssembly` 一类实现三接口且含写路径，另两个各只实现一个只读接口。
7. 三份金样本合计覆盖 48/52 张表，未覆盖的 4 张各有别的用例覆盖，没有裸露的表；
   但人物金样本有 7 对同级失盲（集中在 subtype0 的布尔位）、其集合顺序未被检验。
8. `<Compile Include … Link>` 让三个测试程序集各自编出一份同名 `InventorySide`；
   将来任一程序集 ProjectReference 到 Persistence.Tests 会 CS0436。宜改为独立 Support 类库。
9. N+1 读三处（每封信一次附件读、每件时装一次明细读、每条规则读两次主背包），
   是拆 JOIN 的必然代价、入场量级也小，但契约里没有批量读法。

## 六、顺带修掉的文档陈旧

`Docs/Refactor/CharacterEntryStateCoverage.md` 与 `Docs/DungeonEntryNotes.md` 各有一句指向已删除的类。
两处都是「这件事住在哪」的事实陈述，已改指现在的住处。

## 七、合同修订（本次评审授权）

**声明 `dungeon_persistent_effect_outbox`。** RQ-007 的字面是「拆分不引入任何新的表或列声明」，
本次为修 CR-1 增加了一张表的声明，表数 52 → 53。

理由：RQ-006（不查表结构元数据）之所以可满足，前提正是「唯一归属者声明全部表」。
这张表是那个前提唯一的空洞，而生产代码一直在读写它。补上声明是让前提成真，
不是引入一张新表——它没有引入任何新的持久状态，只是让一张一直被使用的表真正存在。
不补的话，RQ-006 与「选角能成功」二者只能取一。

随之更新：`SchemaOwnershipTests` 的表数断言 52 → 53；T-008 记录的表与列计数。

## 八、结论

三个维度的独立核实一致：八条 RQ 全部成立。三条必须改的已改并各自有新增用例钉住，
两条判定不改的已写下理由与去处，其余批为后续清理单。

**本次评审结论：approved，无未解决的阻塞发现。**

一件需要用户裁决的事：CR-4 的并发失败模式。它不阻塞本次接受，但会在多人并发时真实发生。
