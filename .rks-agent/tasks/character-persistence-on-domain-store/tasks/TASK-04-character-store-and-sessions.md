# TASK-04 — 人物存储与会话，写入改走会话

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 人物持久会话并入按数据库归并的参与者；人物侧的写入改由「位置与值」表达；三个业务动词消失；事务边界之外改动持久人物状态抛出。
- Referenced spec requirements: RQ-001, RQ-004, RQ-005, RQ-006, RQ-007, RQ-011
- Referenced plan sections: `plan.md` 的 `### T-004`；`## Risks and Rollback` 第二条
- Owned boundary: BD-005, BD-006, BD-007, BD-011, BD-012
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryStateServices.cs`
  - `Server/src/Dfo.Server.Infrastructure/Characters/`
  - `Server/src/Dfo.Server.Application/Characters/`
  - `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`
  - `Server/src/Dfo.Server.Application/ApplicationModule.cs`
  - `Server/tests/`
- Dependencies: TASK-01, TASK-03
- Required dependency outputs: TASK-01 的载体协议与按数据库归并的参与者；TASK-03 之后已无业务规则的人物持久实现。
- Acceptance conditions:
  1. 一个用例同时改动物品槽位与人物状态时，正常结束两者可见，其中一处在准备阶段失败则两者均不可见。
  2. 人物状态加入后，同一数据库上提交仍可能失败的参与者仍恰为一个。
  3. 人物的持久化契约上不出现业务动词；`UpdateSeekingQuestTriggersAsync`、`ReviveEquippedCreatureAsync`、`PersistPositionAsync` 三者零命中。
  4. 新增一种人物状态改动无需在持久化契约上新增方法。
  5. 事务边界之外改动持久人物状态抛出；边界之外读取正常完成。
  6. 同一段人物状态操作逻辑作用于持久与易失载体时走同一条路径。
  7. 那 10 处写入逐处有表达结论；表达不出者记录事实，且未引入业务动词或旁路。
- Task-specific authorization: 属根 TaskContract `task` 授权范围内。**本单元不得再拆**：把「写入改走会话」与「用例边界提供原子性」分成两次交付会留下测试看不见的原子性空洞。若某处写入无法以「位置与值」表达，记录事实并回契约层重议，不得在本单元内引入业务动词或绕过会话的旁路。
- Required methods: `test-driven-development`。验收条件 1 与 5 的用例**必须先于实现写出并留有 RED 证据**。
- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-009 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~CharacterStore` | 跨人物与物品的共同成败用例通过 |
| CK-010 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~OutsideTransaction` | 边界外写抛出、边界外读成功 |
| CK-011 | `grep -rn "UpdateSeekingQuestTriggersAsync" Server/src --include=*.cs` | 零命中；另两个业务动词同样零命中 |
| CK-012 | Non-command: 10 处写入的表达结论逐处记录，含表达不出者的处置 | 结论齐备 |
| CK-013 | `dotnet test Server/86JP.Server.slnx -m:1` | 全量绿 |

- Required evidence: RED 证据、10 处写入的逐处表达结论、五条检查的实际输出。

### 共同约束

- 全程不产生任务级提交。工作保持未提交，直至完整评审与新鲜验证通过后由工作区控制器做唯一一次已接受结果提交。
- 对外可观察行为一字不变：帧字节与时序均不变。
- 既有测试语义不得修改，只允许因类型或依赖变化做机械调整。
- 存储实现不得自建任何表。
- 开发阶段的策略是重建而非迁移：不得为既有数据编写回填、修复或迁移路径。
- 不得改动 `Tool/` 与 `.worktrees/legacy-reference/`；客户端可执行文件与 DLL 不得手工编辑。
- C# 文件为 CRLF、无 BOM UTF-8；`net10.0`，`TreatWarningsAsErrors`。每次以工具写入 `.cs` 后须做一次行尾归一化。

### 验证环境

- `dotnet test Server/86JP.Server.slnx -m:1`
- `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`
- 基线：944 通过 / 2 跳过 / 0 失败。`ChannelHostStartupTests` 在负载下会假红，复跑确认。

## Contract Amendment A1

- Amends: Permitted files or interfaces
- 时点：实施开始，动手之前。

**增列 `Server/src/Dfo.Server.Abstractions/Characters/` 为本单元的 Permitted path，仅限新增人物载体与会话的契约文件。**

理由与 TASK-03 的 A1 同：`Dfo.Server.Infrastructure` 只引用 `Abstractions` 与 `Data`，人物存储的接口必须落在 Abstractions 才能被持久实现与业务层同时看见。本单元已获准修改 `Abstractions/Characters/ICharacterEntryStateServices.cs`（BD-005）以移除三个业务动词；新增的会话契约放在同目录的新文件里，而不是塞进那个已经很长的文件。

## Design Note 1 — 乐观并发如何在会话词汇里表达

`UpdateSeekingQuestTriggersAsync` 今天以 `UPDATE ... WHERE trigger_value = @before` 实现乐观并发，影响行数不为 1 即抛 `InvalidDataException`。它守的窗口是真实的：入场快照读到 trigger 值，到写回之间那一行可能已被改动。

会话化后若只发普通 UPDATE，这道守卫就没了——一次静默的行为收窄。三条路：

1. **让业务层先读后比再写**。窗口由「快照→写」缩为「会话读→落定」，更小但**不为零**：读与 prepare 之间仍有间隙。今天那道守卫连这个间隙也覆盖。属静默收窄，否决。
2. **把数据库事务提前到第一次读时开启**。守卫因此变得多余而非丢失。但这会让整个用例期间持有写锁，且波及物品侧的读路径，改动远超本单元，否决。
3. **让暂存的写携带调用方所见的前值，flush 时发带前值条件的 UPDATE。** 采用此条。

第三条为何仍在「位置与值」的词汇内：值本就承载「这个位置现在是什么」的全部——物品侧的 core 里带着附魔与随机属性，人物侧的行里带着调用方看见过的那个值。前置条件是存储概念（compare-and-set），不是业务动词；`Write` 的签名不变，条件随值走。

因此 `StoredActiveQuest` 带一个可空的 `ExpectedTriggerValue`：为 null 时无条件写，有值时以它为条件写，影响行数不为 1 则抛出与今天相同的异常。

## Contract Amendment A2

- Amends: Permitted files or interfaces
- 时点：实施开始，动手之前。

**增列 `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs`，仅限把被移除的业务动词的调用替换为业务层入口。**

为什么无法回避：本单元的验收条件 3 要求 `PersistPositionAsync` 零命中，而 `SelectCharacterPhaseCommands.cs:122` 正在调用它。移除动词而不改该调用点，交付的就是一棵编译不过的树；把动词留到 TASK-05 再删，则本单元的验收条件 3 不成立。二者不可得兼。

为什么不越出必要范围：只改该文件里那一处依赖与调用，**不动**其余任何阶段的时序与语义。TASK-05 的职责相应变为核验协议层对人物持久化契约的零命中，并处理其余残留——其验收条件不因本修正而降低。

授权：属根 TaskContract `task` 授权范围内的边界记录，不扩大对外可观察行为，留待 change review 确认。

## Design Note 2 — 归属校验如何留在「位置」里而不成为动词

今天三处写入各自校验归属：`UpdateSeekingQuestTriggersAsync` 与 `ReviveEquippedCreatureAsync` 先跑一次 `VerifyOwnerAsync`（`SELECT 1 FROM characters WHERE character_id AND account_id AND delete_flag=0`，不中则抛）；`PersistPositionAsync` 则把同样的条件直接写进 UPDATE 的 WHERE 里，影响行数不为 1 则抛。

会话化后若把归属校验搬到业务层，就等于把「这些行归谁」这件事从存储的寻址里拿走，而存储仍会照写不误——寻址反而变松了。

因此人物存储的**位置**是 `OwnedCharacterId(AccountId Account, CharacterId Character)`，而不是光一个角色号。位置本身就说明「哪个角色的行，且必须挂在这个账号下」，存储据此拒绝不成立的位置。这与物品侧 `OwnerOf` 拒绝它安放不了的容器是同一形状：存储校验的是自己的寻址是否成立，不是业务规则。

## Design Note 3 — 断线清理的位置写入与「边界之外必须抛」的冲突及其解法

实施中发现的冲突，记录其发现、判断与解法。

**冲突**：`ISelectedSessionPositionPersistence` 由 `SelectedSessionCleanupService.CleanupAsync` 调用，而后者的调用方之一是 `OnlineUserConnectionObserver`（TCP 断开）。`AGENTS.md` 明写「连接事件不建模为命令」，该路径**刻意**不经过命令总线，因此也不经过 `UnitOfWorkBehavior`，没有任何领域事务开启。于是这处持久人物写入天然落在用例边界之外——而验收条件 5 与 RQ-007 要求边界之外的持久人物改动必须抛。

**为什么不能靠豁免了事**：一旦为这一处开口子，「边界之外改动持久人物状态会失败」就从保证退化为约定，而这正是本任务要消灭的那种"靠巧合成立"的形状。

**为什么不能把断线清理改成命令**：那与 `AGENTS.md` 记载的既有设计直接冲突，且远超本单元范围。

**解法**：把「边界」与「命令」解耦。保证的真正内容是「持久人物改动发生在一个会共同落定的事务里」，而不是「发生在一条命令里」。清理路径**自己开一个领域事务**、写、提交——它就有了边界。这与物品侧既有做法同形：`StoreBackedItemContainer` 在没有环境事务时为读开一个一次性事务再丢弃。

**随之而来的归属变化**：`ISelectedSessionPositionPersistence` 的实现要能开领域事务，而 `DomainTransaction` 是 Application 的内部类型，Infrastructure 看不见。因此该实现由 Infrastructure 移至 Application，改用 `ICharacterStore`。`SqliteSelectedSessionPositionPersistence` 与 `SqliteCharacterEntryMutationRepository` 上那份**从未被注册、因而是死代码**的同名实现一并消失。接口本身（`Abstractions/Sessions/ISessionPresence.cs`）不改，故不触碰该文件。

## Contract Amendment A3

- Amends: Permitted files or interfaces
- 时点：实施中发现，动手改该文件之前。

**增列 `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`，仅限把改动人物持久状态的两条阶段命令的执行方式由 `Workflow` 改为 `Transactional`。**

发现：SelectCharacter 的七条阶段命令全部注册为 `CommandExecutionKind.Workflow`（`ProtocolModule.cs:113-131`）。`UnitOfWorkBehavior` 只对 `Transactional` 开领域事务，其余原样放行。因此任务同步与宠物复活这两条命令执行期间**没有环境事务**，`IDomainTransactionAccessor.Current` 为 null。

后果：若不改，人物会话在这两条命令里取不到边界，只剩两条路——要么让业务代码自己开事务（直接违反 RQ-004「业务代码不表达事务」），要么让写入落在边界之外（违反 RQ-007）。二者都不可接受。

为什么这是本单元该做的：RQ-004 要求的正是「业务代码不书写任何事务操作却获得 RQ-001 的保证」，而提供该保证的机制就是 `UnitOfWorkBehavior`，开关就是这个执行方式标记。不翻这个开关，本单元的核心成果无处落地。

影响面为何有限：这两条命令原本每次仓储调用各自开一个数据库事务并各自提交；改为 `Transactional` 后，一条命令内的写改为共同落定。粒度由「每次调用一个事务」收敛为「每条命令一个事务」，不跨命令合并，故 SelectCharacter 工作流的阶段边界与时序不变。另五条阶段命令不改。

授权：属根 TaskContract `task` 授权范围内的边界记录，不改变对外可观察行为，留待 change review 确认。
## Execution Result 1

### 交付

人物持久会话已并入按数据库归并的参与者；三个业务动词消失；事务边界之外改动持久人物状态抛出。

| 文件 | 变化 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/Characters/ICharacterStore.cs` | 新增（A1）。`OwnedCharacterId`、`ICharacterStore`、`ICharacterStoreSession`（`Quests`、`Creatures` 两段 + 位置 + `HoldsAsync`）、`StoredActiveQuest`、`StoredCreature` |
| `Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterStore.cs` | 新增。`StoreIdentity` 为连接字符串，与物品存储相同，故同库同参与者。会话为面，含通用的 `SqliteCharacterSection<TKey,TValue>` 与两个具体段 |
| `Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs` | 新增。`AmbientStoreAccess`（读放行、持久写在边界外拒绝、易失写放行）与 `SelectedSessionPositionPersistence` |
| `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryStateServices.cs` | 移除 `ICharacterEntryMutationRepository` 整个接口 |
| `Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterEntryMutationRepository.cs` | **删除** |
| `Server/src/Dfo.Server.Infrastructure/Characters/SqliteSelectedSessionPositionPersistence.cs` | **删除** |
| `Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterRepository.cs` | 接收原属被删仓储的位置六列声明 |
| `Server/src/Dfo.Server.Application/Characters/SelectCharacterPostEntryServices.cs` | 任务同步与宠物复活改走会话 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 任务与宠物两条阶段命令改为 `Transactional`（A3） |
| `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs` | 改注入 `ISelectedSessionPositionPersistence`（A2） |
| 两个组合根 | 注册 `ICharacterStore`、`AmbientStoreAccess`、位置持久化接缝 |
| `Server/tests/` | 新增 7 条；三个测试替身因依赖变化机械改造 |

### 跨聚合原子性：本任务的核心成果

宠物复活是最好的例子。它今天读装备栏第 24 格的物品核心取出生物号，再读写 `character_creatures`——**两个聚合、一次用例**。改造前这是仓储自己手搓的一个事务；改造后物品会话与人物会话同属一个数据库参与者，共同落定。

`CharacterStoreSettlementTests` 直接钉住这一点：一个用例既改物品槽位又改任务触发值，正常结束两者可见；令任务写携带一个该行从未持有过的前值（这正是陈旧入场快照的表现），落定在写出阶段失败，**两者均不可见**。

### 实施中发现并处置的三件事

**1. `Workflow` 与 `Transactional` 的差别（A3）。** SelectCharacter 七条阶段命令全为 `Workflow`，而 `UnitOfWorkBehavior` 只对 `Transactional` 开事务。不翻这个开关，人物会话在这两条命令里取不到边界。已按 A3 只改动改人物状态的那两条。

**2. 断线清理的位置写入落在边界之外（Design Note 3）。** 解法是让该路径自己开一个领域事务，而非为它开豁免口子。随之实现由 Infrastructure 移至 Application。顺带发现 `SqliteCharacterEntryMutationRepository` 上那份 `ISelectedSessionPositionPersistence` 实现**从未被注册，是死代码**。

**3. 位置六列的归属（一处真实回归及其修复）。** 删掉 `SqliteCharacterEntryMutationRepository` 时，把它构造函数里给 `characters` 补 `town_id`/`area_id`/`pos_x`/`pos_y`/`direction`/`area_state` 六列的 `EnsurePositionColumns` 一并删掉了，全库再无人声明这六列。由新增的 `OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` 抓住（位置读回 null）。已把该声明移入 `SqliteCharacterRepository`——`characters` 表的归属者。这正是 TASK-06 要根治的那类问题：表的形状取决于哪个组件恰好被构造。

### 注册顺序的一次自我纠正

`ISelectedSessionPositionPersistence` 由 Application 注册后，几个只注册 Application + Networking + Protocol、**不注册 Infrastructure** 的 Networking 集成测试宿主启动失败——它要求的 `ICharacterStore` 不存在。

我第一版的处置是「仅当 `ICharacterStore` 已注册时才注册本接缝」，依据是 Host 中 Infrastructure 先于 Application。这个做法被 `CommandRegistrationTests` 打脸：那里的注册顺序恰好相反（Application 在前）。依赖注册顺序的守卫站不住。

改为顺序无关：以 `GetService<ICharacterStore>()` 可空查找，构造恒成功，真要保存位置而又无处可存时才响亮失败。较原先「接缝未注册因而静默不保存」更严格，且不依赖任何模块次序。

### 10 处人物写入的逐处表达结论（CK-012）

| 写入点 | 结论 |
|---|---|
| `character_active_quests` | **已以位置与值表达**。位置 `(账号, 角色, 槽)`，值为整行；乐观并发的前值随值走 |
| `character_creatures` | **已表达**。位置 `(账号, 角色, 生物号)`，值为该生物的状态字段；`field04<=0` 前置条件随值走 |
| `characters`（位置） | **已表达**。一角色一行的段，无键 |
| `characters`（创建与软删） | 未改。属角色生命周期，由 `ICharacterRepository` 承载，不在本单元移除的三个动词之内。可以位置与值表达，但转换它不属本单元 |
| `character_skills` | 未改。TASK-03 已判定其编排留在持久侧符合 plan `### T-003` 的措辞，且该判断经复评确认。会话化它即是被推迟的「编排上提」 |
| `character_init_flags`、`character_subtype`、`character_pvp_skills`、`character_pvp_skill_state` | 未改。由入场状态仓储写入，不经本单元移除的任何动词 |
| `accounts` | 未改。账号状态，非人物行；根 TaskContract 已把账号侧读写路径列为范围外 |
| `dungeon_persistent_effect_outbox` | 未改。outbox 的状态推进，plan `### T-003` 明确留在持久侧 |

**没有一处表达不出**：三处已转换的都以位置与值表达，未引入任何业务动词或绕过会话的旁路；七处未转换的原因是不属本单元的范围，而非表达不出。这一区分很重要——验收条件 7 关心的是前者。

变更评审指出上表对未转换的六处给的是**范围**理由而非**表达性**结论，属实。补上：

| 写入点 | 位置 | 值 | 表达性 |
|---|---|---|---|
| `characters`（创建与软删） | `(账号, 角色)` | 整行 | 可表达。创建是写一个此前不存在的位置，软删是写 `delete_flag` 一列——后者与位置分区同形 |
| `character_skills` | `(账号, 角色, 页, 槽)` | 整行（技能号、等级、extra_values） | 可表达。行自带键，`ExpertSkillCap` 已经是「这个位置现在是什么」的形状 |
| `character_init_flags` | `(账号, 角色)` | 整行 | 可表达。一角色一行，与位置分区同形 |
| `character_subtype` | `(账号, 角色, 子类型号)` | 整行 | 可表达。行自带键 |
| `character_pvp_skills` / `character_pvp_skill_state` | `(账号, 角色, 页, 槽)` / `(账号, 角色)` | 整行 | 可表达。与 `character_skills` 和 `character_init_flags` 各自同形 |
| `dungeon_persistent_effect_outbox` | `(角色, 事件号, 种类, 范围, 目标)` | 整行 | **可表达但不宜**。它是 outbox，其写入是租约认领与状态推进——`state`、`lease_id`、`lease_expires_at`、`attempt_count` 四者的原子推进本就是一条带条件的 UPDATE。以「位置与值」表达需要把这四列一起当作值并携带前置条件，形式上成立，但会把一个专门的并发协议压进通用词汇里。plan `### T-003` 把它留在持久侧是对的 |

`accounts` 不在此表：它是账号行而非人物行，根 TaskContract 已把账号侧读写路径列为范围外。

### 检查

| ID | 结果 |
|---|---|
| CK-009 | `dotnet test ... --filter FullyQualifiedName~CharacterStore` → `已通过! - 失败: 0，通过: 4` |
| CK-010 | `dotnet test ... --filter FullyQualifiedName~OutsideTransaction` → `已通过! - 失败: 0，通过: 7` |
| CK-011 | `grep -rn "UpdateSeekingQuestTriggersAsync\|ReviveEquippedCreatureAsync\|PersistPositionAsync" Server/src` → **零命中** |
| CK-012 | 上表 10 行 |
| CK-013 | 全量 `1003 通过 / 2 跳过 / 0 失败` = 944 基线 + 4 + 14 + 34 + 7 |

`dotnet build Server/86JP.Server.slnx`：0 警告 0 错误。

附带：`grep -rn "ICharacterEntryMutationRepository" Server/src` 亦为零命中，TASK-05 的 CK-014 已提前成立。

## Executor Self-check 1

| 验收条件 | 证据 |
|---|---|
| 1 一个用例同时改物品与人物，成则两者可见、准备阶段失败则两者均不可见 | `CharacterStoreSettlementTests` 前两条 |
| 2 人物加入后同库仍恰一个可失败参与者 | `TheCharacterAndItsItemsTakePartAsOneParticipant`：两个存储的 `StoreIdentity` 相等且均为持久 |
| 3 三个业务动词零命中 | CK-011 |
| 4 新增一种人物状态改动无需在契约上新增方法 | 新增一段是加一个属性，段上可做什么由 `IStoreSession` 固定；`Quests` 与 `Creatures` 即以同一套词汇表达两种完全不同的行 |
| 5 边界外改动抛出、边界外读取正常 | `OutsideTransactionTests` 三条 |
| 6 同一逻辑作用于持久与易失载体走同一条路径 | `ChangingSomethingThatOutlivesNothingIsAllowedOutsideAUseCase`：同一个 `AmbientStoreAccess.Changing` 对两种载体给出不同答案，而调用方代码相同 |
| 7 10 处写入逐处有表达结论 | CK-012 表 |

### 自认的薄弱处

- **验收条件 7 的达成度需要评审判断。** 10 处中转换了 3 处；其余 7 处的理由是「不属本单元范围」，不是「表达不出」。若评审认为本单元应当转换更多，这是要返工的地方。我的判断是：本单元的边界由被移除的三个业务动词界定，而那三个正对应已转换的三处。
- 位置段没有走 `IStoreSession`，因为一角色只有一行、没有键。这是首评在 TASK-01 就前瞻过的形状问题，当时判断「等有真实消费者再定形」，现在的定形是「一角色一行的段直接给两个方法，不硬套按键寻址」。
- `SqliteCharacterSection.ReadAsync` 逐行扫描 `ReadRowsAsync` 的结果而非按键查询。行数极小（任务槽、生物数均为个位数），但这是为复用而非为效率写的。
- 位置六列现由 `SqliteCharacterRepository` 以 `PRAGMA` + `ALTER` 补列声明，形状与 TASK-06 要根治的一致。它是被搬动而非新引入的，且 TASK-06 会一并收编。

## Independent Quick Review 1

- Verdict: `changes_required`
- Reviewed contract revision: 1，含 Contract Amendment A1 / A2 / A3 与 Design Note 1 / 2 / 3
- Reviewed result: Execution Result 1 与 Executor Self-check 1
- Owned diff identity: 工作树未提交状态 vs `HEAD` = `712bdbc`（`git status --porcelain` 22 改 + 12 新增路径）。该工作树同时驻留 TASK-01 与 TASK-03 的未提交产出（`Abstractions/Transactions/DomainStores.cs`、`Infrastructure/Persistence/`、`Inventory/IItemStore.cs`、`SqliteItemStore.cs`、三处规则外迁及其组合根改动、`SelectCharacterPayloadCodecTests.cs` 的 `GoldLimitRuleService` 机械调整），已按归属剔除，不计入本单元判定。本单元自身的改动全部落在 Permitted path 加 A1/A2/A3 增列之内，未见越界。
- Evidence inspected（未执行任何测试，按约定采信执行者记录）：
  - `git status --porcelain`、`git diff -- Server/`、`git show HEAD:` 两个被删文件全文
  - 新增文件全文：`ICharacterStore.cs`、`SqliteCharacterStore.cs`、`CharacterStateAccess.cs`、`CharacterStoreSettlementTests.cs`、`OutsideTransactionTests.cs`、`RecordingCharacterStore.cs`
  - 对照读取：`DomainStores.cs`、`DomainTransaction.cs`、`DomainTransactionScope.cs`、`SqliteDatabaseParticipant.cs`、`MemoryItemStore.cs`、`ItemContainers.cs`、`UnitOfWorkBehavior.cs`、`CommandExecutionPolicyBehavior.cs`、`CommandContracts.cs`、`SqliteItemStore.cs`、`SqliteInventoryEntryRepository.DecodeItemCore`、`SqliteCharacterEntryStateRepository` 建表段
  - `plan.md` 的 `### T-004`、`spec.md` 的 RQ-005 / RQ-006 / RQ-007 / RQ-011、AC-006 / AC-017、Non-Goals
  - 独立复跑 CK-011：`UpdateSeekingQuestTriggersAsync|ReviveEquippedCreatureAsync|PersistPositionAsync|ICharacterEntryMutationRepository` 在 `Server/` 全域（含 tests）零命中，与执行者记录一致
  - 新增六个 `.cs` 均为 CRLF、无 BOM，符合共同约束

### 阻断发现

| ID | 文件与行 | 发现 |
|---|---|---|
| QB-1 | `Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs:60-63` | **易失载体上的写入被静默丢弃。** `Changing` 对 `IsDurable == false` 的存储返回 `store.SessionIn(new DomainTransaction())`，而这个 `DomainTransaction` 是方法内的局部对象，调用方拿到的只有 session，无人也无从对它 `CommitAsync`。`MemoryItemStoreSession.Write` 只暂存（`Server/src/Dfo.Server.Application/Inventory/MemoryItemStore.cs:109-121`），内容要到 `PrepareAsync` 之后的 `CommitAsync` 才落到 `containers`（同文件 `134-194`）。于是边界之外经 `Changing` 写易失载体：不抛、不报、也不生效。<br>物品侧本单元援引的同形做法恰恰相反——`TransientItemContainer.WriteAsync`（`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:350-361`）先写再 `await alone.CommitAsync(...)`，注释明写「a transaction of one is settled the moment it is filled」。<br>与交付陈述冲突：Execution Result 1 表格称 `AmbientStoreAccess`「易失写放行」（本文件第 141 行），类型注释亦称 volatile 存储「is not refused, because there is nothing to be left half done」——但实际被落下的正是那次写入本身。<br>与自检证据冲突：Executor Self-check 1 第 6 行以 `ChangingSomethingThatOutlivesNothingIsAllowedOutsideAUseCase` 作为验收条件 6 的证据，而该用例（`Server/tests/Dfo.Server.Application.Tests/Characters/OutsideTransactionTests.cs:56-61`）只断言 `Assert.NotNull`，看不见丢失，因此这条检查不覆盖它所声称的行为。<br>根因在 API 形状：`Changing` 返回 session 而不接收写动作，因而没有任何位置可以了结那个一次性事务。 |

**要求的整改**：让 `Changing` 在无环境事务且存储为易失时真正了结自己开的一次性事务（例如改为接收写动作后 `CommitAsync`，与 `TransientItemContainer.WriteAsync` 同形），或明确改为同样拒绝；并把 `ChangingSomethingThatOutlivesNothingIsAllowedOutsideAUseCase` 加强为「写入后可被读回」，使该用例先红。整改范围限于 `CharacterStateAccess.cs`、其两处调用点与该用例，属本单元可就地修复。

### 非阻断发现

| ID | 文件与行 | 发现 |
|---|---|---|
| QN-1 | `Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterStore.cs:402` 与 `412-427` | `character_creatures.field04` 可为 NULL（建表见 `SqliteCharacterEntryStateRepository.cs:935`，该列无 NOT NULL）。原实现读到 NULL 时 `before is null` → 直接返回 null，不复活也不报错；新实现 `IsDBNull(1) ? 0` 把 NULL 读成 0，于是 `Field04 > 0` 不成立、照常暂存写入，flush 时 `field04<=0` 在 SQL 里对 NULL 求值为非真、影响行数 0，抛 `InvalidDataException("Equipped creature revival changed concurrently.")`。「静默跳过」变成「命令失败」。可达性很低（`Server/src` 内无任何 `INSERT INTO character_creatures`，行只可能来自外部数据或夹具），故不阻断，但属行为不等价。 |
| QN-2 | `SqliteCharacterStore.cs:123-136`；对照 `SelectCharacterPostEntryServices.cs:118-120` | 归属校验的失败时机在两条路径上不一致。宠物路径以 `HoldsAsync` 先问，与原 `VerifyOwnerAsync` 位置相同；任务路径则推迟到 flush 时按触及角色逐个校验，原实现是在任何 UPDATE 之前必抛。因 `UnitOfWorkBehavior`（Order 60）在派发内部提交，命令仍在任何帧写出之前失败，对外不可观察；且 `planned.Count > 0` 时必有暂存，校验必然发生。记录于此是因为 Design Note 2 对两条路径给出的是同一个说法。 |
| QN-3 | `SqliteDatabaseParticipant.cs:74-80` 与 `102-120` | 宠物路径的两次读不再与写共处同一个 SQLite 事务：`pending` 只在 `PrepareAsync` 里才建立，`CommandAsync` 在此之前发出的语句 `Transaction` 为 null。原实现的 SELECT 与 UPDATE 同在一个 `BeginTransaction` 内。隔离级别收窄，但 `OnlyWhenAtMostZero` 的 compare-and-set 仍守住不变量，故不构成正确性缺陷。 |
| QN-4 | `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs`（宠物与任务两个 handler 体） | A3 之后，`sessionEntry.ReplaceRuntimeExact(...)` 在事务提交之前就改了内存会话；原先是仓储提交成功之后才改。准备或提交失败时，内存会话会领先于数据库。阶段失败有补偿路径拆会话兜底，故不阻断；记录是因为 A3 的「影响面为何有限」一段未提及这项时序变化。 |
| QN-5 | `CharacterStateAccess.cs:100-111` | `SelectedSessionPositionPersistence.PersistAsync` 无条件自开 `DomainTransaction`，从不询问 `IDomainTransactionAccessor`。今天两个调用点（断线清理与 `Workflow` 的 `PrepareSelectedCharacterSessionCommandHandler`）都不在环境事务内，故正确；但只要其中之一改为 `Transactional`，位置写就会静默逃出环境边界——正是 Design Note 3 要堵的那类洞。与 `AmbientStoreAccess` 相比缺一条「有环境事务就并入」的分支。 |
| QN-6 | `SqliteCharacterStore.cs:264-280` | `SqliteCharacterSection.FlushAsync` 依赖 `Dictionary` 的枚举顺序发语句，而同一层的 `SqliteDatabaseParticipant` 恰恰特意为 facet 另存一个 `List`，理由写着枚举顺序「is not part of its contract」（`SqliteDatabaseParticipant.cs:25-31`）。只影响多槽冲突时先抛出哪一条异常。 |
| QN-7 | `SqliteCharacterStore.cs:340-345` | `QuestSection.FillAsync` 的 `ExpectedTriggerValue is null` 分支（INSERT … ON CONFLICT）无生产调用方、无用例覆盖。对着 `character_active_quests` 的 `PRIMARY KEY(character_id,slot)` 语法成立，但未被执行过。 |
| QN-8 | 本文件 CK-012 表 | 表列出 11 张表的结论，而契约称「那 10 处」。只有把 `accounts`（spec Non-Goals 明确排除）剔除、并把 `characters` 的创建与软删算作一处，才恰好凑成 10。这一折算未被写出，使 CK-012 的「结论齐备」无法直接核对。 |
| QN-9 | 验收条件 7 的范围界定 | 独立判断：**成立，不构成阻断，但须由最终评审确认。** 依据是——(a) 本单元契约的验收条件 7 只要求「逐处有表达结论 + 表达不出者记录事实 + 未引入业务动词或旁路」，三项均达成，我逐条核对未发现任何新增业务动词或绕过会话的旁路；(b) plan `### T-004` 的 Outputs 恰为「人物持久会话并入数据库参与者；三个业务动词消失；事务外写抛出」，与已转换的三处一一对应；(c) `accounts`、「已建表但从无写入的人物表」在 spec Non-Goals 中被排除，`character_skills` 与 `dungeon_persistent_effect_outbox` 由 T-003 判定留在持久侧并经复评确认；(d) 未转换的七处各自仍在原有事务里，未因此产生任何新的原子性空洞。<br>但张力是真实的：plan `### T-004` 步骤 4 的字面要求是「把那 10 处写入逐处**改由会话表达**」，spec AC-017 的 Then 是「每一处都表达得出」。执行者交付的是「每一处都**能**表达」而非「每一处都**已**表达」。这属于计划措辞与交付范围之间的判定，归 drift 检查与最终评审所有，超出快速评审可决的范围。 |

### 已独立复核并确认无误的几处

- **乐观并发等价**：`QuestSection.FillAsync` 带前值分支的 WHERE 子句（`character_id AND slot AND quest_id AND trigger_value=@expected`）、影响行数判定与异常文本 `$"Seeking quest {value.QuestId} changed concurrently."`，与被删实现逐字一致。
- **宠物生物号等价**：`InventoryItemCoreState` 的 `ItemKind` 为 `core[0]`、`Value` 为 `BinaryPrimitives.ReadInt32LittleEndian(core[5..])`（`SqliteInventoryEntryRepository.cs:833-836`），故新实现的 `Core.ItemKind == 5` 与 `Core.Value` 等价于原先的 `core[0]==5` 与 `core.AsSpan(5,4)`。`InventoryContainerType.Equipment == 3` 与 `list_type=3` 相符；物品存储多出的 `owner_scope='character' AND owner_id=character_id` 谓词与装备行的实际写入方式相符。唯一残留差异是原先的 `core.Length == 82` 守卫，现由 `DecodeItemCore` 的索引越界代替，属物品存储既有形状。
- **位置写入等价**：`FillPositionAsync` 的 SET/WHERE（含 `area_state=3`、`delete_flag=0`）、影响行数判定与 `"Entry position owner is unavailable."` 与被删实现逐字一致；归属校验文本 `"Entry mutation character ownership does not match."` 亦一致。
- **A3 的影响面陈述属实**：`CommandExecutionPolicyBehavior`（`:12-21`）对 `Workflow` 与 `Transactional` 都是 `next()`，两者唯一差别就是 `UnitOfWorkBehavior`（`:27-30`）是否开事务。故「阶段时序不变、粒度由每次仓储调用收敛为每条命令」成立；另五条阶段命令未动。更小的做法（只为这两条命令在业务层自开事务）会直接违反 RQ-004，A3 的取舍正确。
- **注册顺序确实无关**：`ISelectedSessionPositionPersistence` 现仅由 `ApplicationModule` 一处 `TryAddSingleton` 注册（Infrastructure 侧两处注册已随文件删除），`ICharacterStore` 以 `provider.GetService<...>()` 在解析时惰性查找，容器构建完成后才发生，与模块登记次序无关。行为变化（无处可存时抛，而非「接缝未注册因而静默不保存」）方向正确：`SelectedSessionCleanupService` 收的是 `IEnumerable<ISelectedSessionPositionPersistence>`（`SelectCharacterLifecycleServices.cs:76`），原先零注册即静默跳过。
- **`EnsurePositionColumns` 的搬迁完整**：六列与默认值（`town_id`/`area_id`/`pos_x`/`pos_y` 默认 0，`direction` 默认 5，`area_state` 默认 3）与被删实现逐字一致，且由 `SqliteCharacterRepository` 构造函数经 `EnsureSchema()` 调用，时机等价。被删的 `SqliteSelectedSessionPositionPersistence` 除转调外无其他逻辑；`SqliteCharacterEntryMutationRepository` 上那份 `ISelectedSessionPositionPersistence` 实现确为死代码（组合根注册的是前者）。除 QB-1 外未发现随两个被删文件一同消失的行为。
- **`AmbientStoreAccess` 三条路径中的两条正确**：无边界读开一次性 `DomainTransaction` 并在 `finally` 回滚，经 `SqliteDatabaseParticipant.RollbackAsync → SettleAsync → CloseAsync` 释放连接，无泄漏；持久写在无边界时抛 `InvalidOperationException`，文本被 `OutsideTransactionTests` 钉住。第三条（易失写）即 QB-1。
- **既有测试语义未被削弱**：`SelectCharacterLifecycleTests.CharacterMutations`、`SessionGenerationOwnershipTests.NoopMutations`、`SelectCharacterEntryLifecycleServiceTests.RecordingMutations` 三处替身的改动均为依赖变化所迫的机械调整；宠物用例由「替身返回预置结果」改为「真实读改状态后第二次自然返回 null」，断言不变而覆盖变强。

### 交给最终评审的问题

- QN-9 所述的 plan 步骤 4 字面要求与交付范围之间的差距。
- QN-5 所述的位置持久化接缝与环境事务的关系，是否应在本轮补上「有环境事务就并入」的分支。
## Execution Result 2 (post-verdict)

首评判 `changes_required`：1 项阻断、9 项非阻断。处置如下。

### 阻断项 QB-1：易失载体上的写入被静默丢弃

评审是对的，而且这一条格外该被抓住——**它正是本任务要消灭的那类失败**：不抛、不报、也不生效。

原因在 API 形状。`Changing` 把 session 交出去，而边界之外那个一次性事务是方法内的局部对象，调用方既不知道它存在也无从提交；`MemoryItemStoreSession.Write` 只暂存，于是写入被暂存后随对象一起消失。物品侧同形做法（`TransientItemContainer.WriteAsync`）恰恰是写完当场 `CommitAsync`，我援引了它却没照它做。

我自己的用例也遮蔽了这一点：`ChangingSomethingThatOutlivesNothingIsAllowedOutsideAUseCase` 只断言 `Assert.NotNull(access.Changing(...))`——断言的是「拿得到 session」，而丢的是那次写。**能通过的断言不等于有效的断言。**

修法是改形状而非补一行提交：`Changing` 换成 `ChangeAsync(store, change, ct)`，把变更交进来，于是一次性边界有了被了结的位置。环境事务存在时不提交（用例自己会提交），持久载体在边界外仍拒绝，易失载体开一次性边界并当场落定。

用例改为断言**值确实还在**：写完之后读回并比对。证伪确认：把 `ChangeAsync` 里那句 `alone.CommitAsync` 去掉，该用例立刻转红（`失败: 1，通过: 6`），恢复后复绿。

### 已修正的行为不等价 QN-1

`character_creatures.field04` 为 NULL 时，原实现读作 `null` 并直接返回（不写、不抛）；我的读把 NULL 折成 0，于是继续走到写，而 `field04<=0` 对 NULL 不成立，最终抛出。方向错了。

改为：`ReadRowsAsync` 跳过 `field04` 为 NULL 的行——没有记录条件的行不是这个段持有的行。于是读回 null、调用方返回 null，与原实现逐条一致。

### 已补的覆盖 QN-7

`ExpectedTriggerValue` 为 null 的无条件写此前无调用方也无用例。新增 `AQuestWrittenWithNothingToCompareAgainstLandsRegardless`（覆盖既存槽的改写与新槽的创建）与 `ASessionSeesItsOwnUnsettledWrites`（会话读得到自己尚未落定的写）。

### 记录在案、不予修改的六项

| findings | 判断 |
|---|---|
| QN-2 任务路径的归属校验推迟到 flush（宠物路径与原来一致） | 保持。评审自陈对外不可观察：两条路径最终都在同一次落定中失败，异常类型与文本不变。把校验提前会多一次无谓查询 |
| QN-3 宠物的读与写不再同处一个 SQLite 事务 | 保持。compare-and-set 前置条件 `field04<=0` 随写走，不变量仍由数据库守住，这正是 Design Note 1 选定第三条路的理由 |
| QN-4 A3 之后 `ReplaceRuntimeExact` 早于提交，影响面陈述未提及 | 补记于此。该动作是运行时注册表的内存操作，不属事务参与者；A3 改变的是数据库写何时落定，不改变内存注册表的时机 |
| QN-5 位置持久化接缝无条件自开事务、从不并入环境事务 | 保持，且是刻意的。Design Note 3 说明了它存在的理由就是为没有边界的清理路径开一个边界。若它并入环境事务，在有环境事务时行为反而不可预期 |
| QN-6 flush 依赖 Dictionary 枚举顺序 | 保持。同一角色的不同槽、不同生物之间互不依赖，顺序不可观察。跨面的顺序已由 TASK-01 的 `order` 列表固定 |
| QN-8 CK-012 列 11 行而契约称「10 处」 | 折算写明：`characters` 在契约里算一处，我按「位置」与「创建/软删」拆成两行陈述，故 11 行对应 10 处 |

### 复验

| ID | 结果 |
|---|---|
| CK-009 | `已通过! - 失败: 0，通过: 6`（原 4 条 + QN-7 补的 2 条） |
| CK-010 | `已通过! - 失败: 0，通过: 7` |
| CK-011 | 零命中 |
| CK-013 | 全量 `1005 通过 / 2 跳过 / 0 失败` |

`dotnet build`：0 警告 0 错误。

## Executor Self-check 2

| 首评发现 | 状态 | 证据 |
|---|---|---|
| QB-1（阻断）易失写被静默丢弃 | 已修 | `ChangeAsync` 接收变更并了结一次性边界；用例改为断言值仍在；去掉提交即转红 |
| QN-1 NULL 条件的行为不等价 | 已修 | 无条件记录的行不被本段持有，读回 null，与原实现一致 |
| QN-7 无前值分支无覆盖 | 已补 | 新增 2 条 |
| QN-2、QN-3、QN-4、QN-5、QN-6、QN-8 | 记录未改 | 见上表逐条理由 |
| QN-9 验收条件 7 的达成度 | 评审判为不阻断，留待 drift 检查与最终评审 | CK-012 表连同范围界定理由一并留存 |

七条验收条件的证据与 Self-check 1 相同；QB-1 与 QN-1 的修正均未改变任何验收条件所断言的行为，CK-011 仍零命中，CK-013 由 1003 增至 1005（QN-7 的两条）。

## Independent Quick Review 2

- Verdict: `passed`
- Reviewed contract revision: 1，含 Contract Amendment A1 / A2 / A3 与 Design Note 1 / 2 / 3
- Reviewed result: `## Execution Result 2 (post-verdict)` 与 `## Executor Self-check 2`（第 1 轮已核清的部分未重复核）
- Reviewer context: 全新独立上下文，与执行者及第 1 轮评审者均不同
- Owned diff identity（本轮增量）：工作树未提交状态，`HEAD` 仍为 `712bdbc`（无任务级提交，符合共同约束）。按文件修改时间界定本轮改动恰为五个文件——`Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs`、`Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterStore.cs`、`Server/src/Dfo.Server.Application/Characters/SelectCharacterPostEntryServices.cs`、`Server/tests/Dfo.Server.Application.Tests/Characters/OutsideTransactionTests.cs`、`Server/tests/Dfo.Server.Application.Tests/Characters/CharacterStoreSettlementTests.cs`（均为 14:00–14:01），与执行者声明的范围一致，**未见范围扩大**。
- Evidence inspected（**未执行任何测试**，按约定采信执行者记录的 CK-009 6 通过 / CK-010 7 通过 / CK-011 零命中 / CK-013 1005 通过 2 跳过 0 失败 / build 0 警告 0 错误）：
  - 全文：`CharacterStateAccess.cs`、`SqliteCharacterStore.cs`、`SelectCharacterPostEntryServices.cs`、`OutsideTransactionTests.cs`、`CharacterStoreSettlementTests.cs`、`RecordingCharacterStore.cs`
  - 对照：`DomainTransaction.cs`、`SqliteDatabaseParticipant.cs`、`MemoryItemStore.cs`、`ICharacterStore.cs`、`SelectCharacterPhaseCommands.cs`、`ProtocolModule.cs:105-144`、`SelectCharacterEntryLifecycleServiceTests.cs:95-162`、`SqliteCharacterEntryStateRepository.cs:414-428` 与 `:935`
  - `git show HEAD:...SqliteCharacterEntryMutationRepository.cs` 全文（QN-1 等价性比对）
  - `git status --porcelain`、`git rev-parse HEAD`；五个改动文件的行尾与 BOM（均 CRLF、无 BOM）

### 逐项核验

| 首评发现 | 判定 | 依据 |
|---|---|---|
| QB-1 | **已闭合** | `CharacterStateAccess.cs:58-92` 三条路径均正确。①有环境事务：`ChangeAsync` 只 `await change(store.SessionIn(ambient))` 后返回，不提交（`:66-71`），由 `UnitOfWorkBehavior` 提交；②持久载体且无环境事务：在调用 `change` **之前**抛出（`:73-79`），委托不会被执行；③易失载体：开 `alone`、写、当场 `alone.CommitAsync`（`:81-86`），异常路径 `alone.RollbackAsync` 后重抛（`:87-91`）。`DomainTransaction.CommitAsync` 会先对全部参与者 `PrepareAsync` 再提交，故 `MemoryItemStoreSession` 的 `prepared is null` 守卫不会误触发；`CommitAsync` 失败时其内部已 `RollbackEnlistedAsync` 并置 `settled`，`catch` 里的再次 `RollbackAsync` 直接返回，不会双重回滚。`Changing` 已在全仓零残留。 |
| QB-1 的用例强度 | **断言确实依赖提交** | `OutsideTransactionTests.cs:71-84`：`ChangeAsync` 之后另起一次 `access.ReadAsync`，而该读会 `new DomainTransaction()` → `MemoryItemStore.SessionIn` 的 `Enlist` 按事务建表，故拿到的是**全新** `MemoryItemStoreSession`（`staged` 为空），只能从共享的 `containers` 读。`containers` 仅在 `MemoryItemStoreSession.CommitAsync`（`MemoryItemStore.cs:184-190`）被赋值。因此去掉 `alone.CommitAsync` 后 `kept` 为 null，`kept!.Core` 转红——执行者的证伪记录成立。 |
| QN-1 | **修法正确** | 原实现 `before = value is null || value is DBNull ? null : ...`，随后 `if (before is null || before > 0) return null;`（`SqliteCharacterEntryMutationRepository.ReviveEquippedCreatureAsync`）。新实现 `CreatureSection.ReadRowsAsync` 在 `reader.IsDBNull(1)` 时 `continue`（`SqliteCharacterStore.cs:404`），使「无行」与「field04 为 NULL」都落到 `ReadAsync` 返回 null，调用方 `SelectCharacterPostEntryServices.cs:137` 的 `creature is null` 分支直接 return null——与原实现逐条一致，且不再抛。 |
| QN-1 的波及面 | **无其他调用方** | 全仓 `Creatures.ReadAllAsync` / `CreatureSection.ReadAllAsync` 无任何生产调用方（仅 `RecordingCharacterStore` 的替身实现同名方法），故跳过 NULL 行只影响宠物路径的 `ReadAsync`。 |
| QN-7 的两条新用例 | **确实覆盖所声称的分支** | `AQuestWrittenWithNothingToCompareAgainstLandsRegardless`（`CharacterStoreSettlementTests.cs:107-120`）以三参构造 `StoredActiveQuest`，`ExpectedTriggerValue` 为 null，走 `QuestSection.FillAsync` 的 `INSERT … ON CONFLICT` 分支；槽 2 命中夹具既存行（改写）、槽 3 为新行（创建），两侧影响行数均为 1，断言直接查库。`ASessionSeesItsOwnUnsettledWrites`（`:126-137`）确实证明「读得到自己未落定的写」：`ReadAsync` 先命中 `SqliteCharacterSection.staged`（`SqliteCharacterStore.cs:240-243`）返回 42，而另一条连接查库仍为 6——42 只可能来自暂存层。附带确认该用例不泄漏资源：`Write` 与命中暂存的 `ReadAsync` 都不发语句，`SqliteDatabaseParticipant` 的连接是 `CommandAsync` 时才惰性打开的，故那个未落定的 `DomainTransaction` 未持有任何连接。 |
| 两处调用点改为 `ChangeAsync` | **语义不变** | 任务同步（`SelectCharacterPostEntryServices.cs:55-67`）：lambda 同步执行且返回 `ValueTask.CompletedTask`，`committed` 在 `ChangeAsync` 返回时必已填满，故「先填后判」与原顺序等价；`planned` 的每一项都已满足 `After != Before`（`:47-48`），`committed.Count == 0` 与改造前同样不可达；持久载体在无边界时于 lambda 之前抛出，`committed` 保持为空且异常上抛，也与原 `Changing` 的时机一致。宠物路径（`:122-142`）在有环境事务时，`ReadAsync` 与 `ChangeAsync` 都经 `SqliteCharacterStore.SessionIn` → `transaction.Enlist(连接串, …)` → `participant.Facet(FacetKey, …)`，两者按同一 key 归并，**确为同一个 `SqliteCharacterStoreSession`**（`SqliteDatabaseParticipant.Facet` 注释明写此意），因而读得到自己的暂存；该命令已由 A3 置为 `Transactional`，环境事务必然存在。 |
| QN-2 / QN-6 / QN-8 记录未改 | **理由成立** | QN-2：两条路径最终都在同一次落定中失败，异常类型与文本不变，帧未写出前失败，对外不可观察。QN-6：同一段内不同槽、不同生物互不依赖，顺序只决定多处冲突时先抛哪一条，两者均为 `InvalidDataException` 且都不产生帧；跨面顺序已由 `SqliteDatabaseParticipant.order` 固定。QN-8：折算说明已写明（`characters` 契约算一处、陈述拆两行），CK-012 现可直接核对。 |
| QN-3 记录未改 | **恰当** | 不变量由 SQL 的 `field04<=0` 前置条件守住（`SqliteCharacterStore.cs:415-419`），读写不同处一个 SQLite 事务只收窄隔离窗口：并发抢先复活时新实现抛 `InvalidDataException` 而非静默改写，方向安全（更严不更松），且不产生帧。与 Design Note 1 选定「条件随值走」的理由自洽。 |
| QN-5 记录未改 | **可接受，但仍属最终评审的开放项** | 复核确认该洞**今日不活跃**：`ProtocolModule.cs:112-114` 的 `PrepareSelectedCharacterSessionCommand` 与 `:134-136` 的 `CompensateSelectedCharacterSessionCommand` 均仍为 `Workflow`，A3 改为 `Transactional` 的两条（`:128-133`）既不调用 `positions.PersistAsync` 也不调用 `cleanup.CleanupAsync`，故当前不存在「环境事务内再自开一个同库事务」的嵌套。执行者的理由（「并入环境事务反而不可预期」）是六项中最弱的一条——真正的约束是断线清理路径无环境事务可并，`ChangeAsync` 对持久载体在边界外会拒绝，因而该接缝无法直接改用 `ChangeAsync`；这一点执行者未写出。首评已将其列为「交给最终评审的问题」，本轮维持该归属，不构成阻断。 |
| QN-4 记录未改 | **可接受，但陈述略有回避** | 「A3 改变的是数据库写何时落定，不改变内存注册表的时机」对注册表的绝对时机成立，但首评指出的是**相对次序**变了（原为提交成功后改内存，现为提交前改内存），执行者以「补记于此」承认而未正面复述。阶段失败有补偿路径拆会话兜底，不阻断。 |
| QN-9 | **维持首评归属** | 属计划措辞与交付范围之间的判定，归 drift 检查与最终评审。 |

### 补救本身未引入新问题

- 范围未扩大：本轮恰为五个文件，与声明一致；`Tool/`、`.worktrees/legacy-reference/`、客户端二进制未触及；无任务级提交。
- 五个改动文件均 CRLF、无 BOM，符合共同约束。
- 验收条件 5 仍成立：`ChangeAsync` 对持久载体在边界外抛出，`OutsideTransactionTests.ChangingACharacterOutsideAUseCaseIsRefused` 钉住文本；边界外读取路径未改。
- 验收条件 6 的证据由「只断言拿得到 session」升级为「写入后可被读回」，覆盖强于首评时。
- 检查计数自洽：CK-009 由 4 增至 6（`CharacterStoreSettlementTests` 现 6 条），CK-010 的 7 条 = 本文件 3 条 + `ItemContainerOutsideTransactionTests` 4 条，CK-013 由 1003 增至 1005，与新增两条吻合。

### 非阻断观察（供最终评审参考，不要求本单元处置）

- `character_creatures` 的主键是 `(character_id, sort_order)` 而非 `creature_key`（`SqliteCharacterEntryStateRepository.cs:935`）。若同一 `creature_key` 出现在多行上，QN-1 的跳过逻辑会让新实现取到第一条**非 NULL** 的行，而原实现的 `LIMIT 1` 可能取到 NULL 那条并返回 null。此情形需要数据本身已损坏（且入场加载路径 `:414-428` 会先一步炸开），实际不可达，方向亦为更宽容而非更危险，仅作记录。
- QN-5 与 QN-4 如上，保留为最终评审的开放项。

## Remediation 2 (post-verdict)

复评判 `passed`，无阻断。三项记述缺陷予以补正。

| findings | 补正 |
|---|---|
| QN-5 我给的理由（「并入环境事务反而不可预期」）是六项中最弱的 | 复评给出了真正的约束，采用之：断线清理**没有环境事务可并**，而 `ChangeAsync` 对持久载体在边界之外会拒绝——所以该接缝根本无法改用 `ChangeAsync`，它必须自己开边界。这不是取舍，是唯一解。复评并已复核该接缝今日不与任何同库事务嵌套：`Prepare` 与 `Compensate` 两条命令仍是 `Workflow`，A3 改动的两条既不调用位置持久化也不调用清理 |
| QN-4 我答的是「绝对时机不变」，而首评问的是**相对次序**变了 | 正面承认：A3 之后 `ReplaceRuntimeExact` 由「数据库提交之后」变为「提交之前」。它是内存注册表操作、不是事务参与者，故提交失败时不会随之回滚。今日不可观察（该命令失败即走补偿路径重置会话），但这是 A3 影响面陈述里我漏掉的一条，记在此处交最终评审 |
| 复评新增观察：`character_creatures` 主键是 `(character_id, sort_order)` 而非 `creature_key`，同一 `creature_key` 若出现在多行，跳过 NULL 会取到第一条非 NULL 行，而原 `LIMIT 1` 可能取到 NULL 那条并返回 null | 记录。需数据已损坏才可达（入场加载路径会先失败），且新行为更宽容而非更严。不改 |
