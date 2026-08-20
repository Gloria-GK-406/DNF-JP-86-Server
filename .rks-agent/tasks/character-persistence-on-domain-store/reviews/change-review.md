---
kind: rks-review
verdict: approved
---

# Change Review — 人物持久化收敛到统一存储协议

- 范围：`change`（完整改动），对照 `TaskContract.md`（revision 1）、`spec.md`、`plan.md`、七个 Task 文件的边界与修正条款。
- 基线：`712bdbc9523ad1fae0f781813f44f3e64ab3b02`；`git log --oneline -1` 与之相同，全程无任务级提交，符合共同约束。
- 差异：`git status --short -uall` 为 28 个已跟踪文件（含 2 个删除）+ 14 个未跟踪源/测试文件 + `AGENTS.md` + 任务工件目录。`Tool/`、`.worktrees/`、`protocol/` 零改动。
- 控制器为 `sdd-standard`（`routing-decision.md` 的 Controller Selection 逐条排除了 Governed 硬门槛），未配置对抗性挑战集，故本评审不含 Challenge Dispositions。
- 按指示未复跑测试与构建；采信执行者记录的 `build 0 警告 0 错误`、全量 `1008 通过 / 2 跳过 / 0 失败`（基线 944 / 2 / 0）。下述结论一律为源码层与差异层的独立核验，不复述执行者记述。

## 独立执行的核验

| 项 | 命令/方法 | 结果 |
|---|---|---|
| 三个业务动词与旧接口 | `grep -rn "UpdateSeekingQuestTriggersAsync\|ReviveEquippedCreatureAsync\|PersistPositionAsync\|ICharacterEntryMutationRepository" Server/` | 0 命中（含 tests） |
| 金币规则 | `grep -rn "ResolveGoldUpgradeLevel" Server/` | 0 命中 |
| 协议层人物持久化契约 | `grep -rn "ICharacterStore\|ICharacterEntry*Repository" Server/src/Dfo.Server.Protocol` | 0 命中 |
| 建表归属 | `CREATE TABLE` / `CREATE INDEX` / `ALTER TABLE` / `PRAGMA table_info` 在 `Server/src` | 仅 `Infrastructure/Schema/CharacterDatabaseSchema.cs` |
| 存储零 DDL | `SqliteItemStore.cs` / `SqliteCharacterStore.cs` 中 `CREATE`/`ALTER`/`PRAGMA` | 各 0 命中 |
| **schema 并集（独立脚本）** | 把 `712bdbc` 全部 CREATE TABLE 逐条单独建库，按表名聚合 `PRAGMA table_info` 的 (type, notnull, default, pk) 并集，与新文件 52 条声明逐条比对 | 基线 52 表 → 新 52 表；缺表 0、多表 0、缺列 0、属性差异 0 |
| 补列器覆盖 | 抽出基线 `EnsureColumn`/`EnsureColumns` 的 17 对 (表, 列)，逐条在新文件中查 | 无遗漏；`EnsurePositionColumns` 六列（含 `direction` 默认 5、`area_state` 默认 3）与 `accounts` cube 六列、`character_init_flags.pc_room_state` 均在新声明内 |
| 存储身份 | `SqliteItemStore.cs:27-33` 与 `SqliteCharacterStore.cs:27-33` 的连接串构造 | 逐字同形，同 `databasePath` → `StoreIdentity` 相等 → 同一参与者 |
| 物品核心等价 | `DecodeItemCore`（`SqliteInventoryEntryRepository.cs:834-837`）对旧 `core[0]` / `core.AsSpan(5,4)` | `ItemKind`=core[0]、`Value`=core[5..9]，等价；`InventoryContainerType.Equipment == 3` 与旧 `list_type=3` 相符 |
| 物品行 owner 谓词 | 全部 `INSERT INTO character_new_items` 生产写入点 | 一律 `('character', @cid, @cid, …)`，新读路径多出的 `owner_scope='character' AND owner_id=character_id` 谓词与实际写入方式一致 |
| 行尾与 BOM | 8 个新增生产文件 | 全部 CRLF、无 BOM |

## 成功条件逐条判定

| ID | 判定 | 我的依据 |
|---|---|---|
| SC-1 | **达成** | `SqliteCharacterStore.SessionIn`（`SqliteCharacterStore.cs:37-44`）与 `SqliteItemStore.SessionIn`（`SqliteItemStore.cs:37-44`）以同一连接串 `Enlist` 同一个 `SqliteDatabaseParticipant`，两者只是它的两个面。`CharacterStoreSettlementTests` 两条正对 SC-1 所要求的两侧：成功路径断言物品槽 66 与任务触发值同时可见；失败路径以「该行从未持有过的前值」令人物面在 flush（准备阶段）抛出，断言物品槽与任务值**双双回到改动前**。`DomainTransaction.CommitAsync`（`DomainTransaction.cs:78-96`）的 prepare→唯一可失败者 commit→回滚全体的次序使该断言成立而非巧合 |
| SC-2 | **达成** | 三个动词在 `Server/` 全域 0 命中，`ICharacterEntryMutationRepository` 整个接口已从 `ICharacterEntryStateServices.cs` 移除。行为由 `SelectCharacterQuestEntryService`/`SelectCharacterPetEntryService`（`SelectCharacterPostEntryServices.cs:30-162`）承载；乐观并发条件 `trigger_value=@expected`、异常文本 `$"Seeking quest {…} changed concurrently."`、`field04<=0` 前置条件、`"Equipped creature revival changed concurrently."` 与被删实现逐字一致（对照 `git show 712bdbc:…SqliteCharacterEntryMutationRepository.cs`） |
| SC-3 | **达成（一处需备案）** | `SelectCharacterPostEntryServices.cs` 全文无 `Transaction`/`Commit`/`Rollback` 任何词汇，只调 `stores.ReadAsync` / `stores.ChangeAsync`。载体无关性由 `AmbientStoreAccess` 泛型于 `IDomainStore<TSession>` 保证，`OutsideTransactionTests.ChangingSomethingThatOutlivesNothingIsAllowedOutsideAUseCase` 以同一段调用代码作用于易失载体。备案：`SelectedSessionPositionPersistence`（`CharacterStateAccess.cs:107-136`）确实书写事务对象——它是断线清理的边界适配器而非用例，其存在理由已在 Design Note 3 明示，见 N-5 |
| SC-4 | **达成** | 三处规则各自迁至 `CharacterEntryRuleServices.cs`：`GoldLimitRuleService.UpgradeLevel`、`ExpertSkillContractRuleService.HasRunOut`/`CapsFor`、`DungeonEffectRuleService.CanCarryOut`/`GrantLuckyStar`/`SplitExperience`/`AccumulateHonour`。逐条与被替换代码比对，语义等价（含「一份仍在运行即全体不降」的早退语义、`maximum <= 0 ? null` 的删行语义、`honor > 0 && rate > 0 → 至少 1 胶囊` 的下限）。`CharacterEntryRuleServiceTests.cs` 22 条用例全部不触及数据库，三处各有覆盖，对应 AC-011/012/013。`ResolveGoldUpgradeLevel` 已 0 命中 |
| SC-5（人物侧三处规则）| **达成**，见 SC-4。另：spec 编号下 RQ-007「事务外改动抛出」亦达成 | `AmbientStoreAccess.ChangeAsync`（`CharacterStateAccess.cs:73-79`）在 `store.IsDurable && Current is null` 时**先于**执行变更委托抛出；`OutsideTransactionTests.ChangingACharacterOutsideAUseCaseIsRefused` 钉住文本，`ReadingACharacterOutsideAUseCaseIsAllowed` 钉住读放行。首评抓到的「易失写被静默丢弃」已由 `ChangeAsync` 形状改造修复，用例已强化为「写完读得回」 |
| SC-6 | **达成（一处措辞未字面达成，见 N-1）** | 每张表恰一处定义：52 声明 / 52 表（基线 59 条声明 / 52 表 / 5 张多定义）。我以独立脚本做了并集包含性比对，缺列与属性差异全为 0——这是本次最需要独立验证的一条，因为「反推自已生效的库」拿到的是赢家而非并集（`character_gold_limits.updated_at` 即其孤例，已在 TASK-06 修复并由 `SchemaOwnershipTests.cs:59-60` 的四列字面量守住）。补列器保留并集中化，SC-6 的「或有保留理由的记录」条款允许 |
| SC-7 | **达成** | `SelectCharacterPhaseCommands.cs:38` 改注入 `ISelectedSessionPositionPersistence`；协议层对任何人物持久化契约 0 命中 |
| SC-8 | **部分达成——第三项证据未闭合** | 帧字节：协议层实际差异只有 2 行（构造器换型 + 调用点换接收者）加 2 行执行方式标记，无一处触碰编码器；`SelectCharacterAcceptedTranscriptTests` 零改动且含整条入场序列的 SHA256 金样与 58 条帧顺序断言；`Dfo.Server.Networking.Tests` 零改动。全量 1008 / 2 / 0，无用例删除或转跳过。**实机走查（CK-022）未完成**，见下节 |

## 三项被点名的裁决

### 一、RQ-011「本范围内每一处人物写入均以位置与值表达」——**判定：达成，非阻断**

TASK-04 只转换了 10 处中的 3 处，快评（QN-9）判不阻断并移交。我正面裁决如下。

1. **契约层面没有要求转换 10 处。** `TaskContract.md:49` 的 Scope In 原文是「人物侧现有写入改由该会话表达，`ICharacterEntryMutationRepository` 上的业务动词随之消失」——把「现有写入」绑定在承载业务动词的那个接口上，而那个接口上恰是三处。八条成功条件中**没有一条**要求 10 处全转；SC-2 的证据就是三个动词零命中。
2. **字面读法自相矛盾且不可满足。** `TaskContract.md:33` 列举的 10 张表包含 `accounts`，而同文件 Scope Out（`:57`）与 `spec.md:39` 的 Non-Goals 都明确把账号侧读写路径排除。要求转换全部 10 处等于同时要求转换和不转换 `accounts`。同理，`plan.md` 的 `### T-003` 步骤 1、2 明写 `character_skills` 与 `dungeon_persistent_effect_outbox` 的写入**留在持久侧**（「持久侧只留读与写」「只留读、写与状态推进」），而 `### T-004` 步骤 4 的「把那 10 处写入逐处改由会话表达」与之直接冲突。计划自身矛盾的条款不能拿来判定交付不合格。
3. **spec 自己的操作化读法支持「可表达」而非「已表达」。** AC-017（`spec.md:105`）的 Then 是「每一处都**表达得出**」，EV-008（`spec.md:119`）指定的证据类是「逐处写入的**表达性结论记录**」——是结论记录，不是转换计数。这与契约 Assumptions（`TaskContract.md:103`）「按键寻址的行读写**足以表达**那 10 张表的全部写入」同调：那是一条设计可行性前提，不是交付承诺。
4. **RQ-011 的实质保护条款成立，我独立核过。** 「不引入业务动词」：`ICharacterStoreSession`（`ICharacterStore.cs:36-58`）上只有两个 `IStoreSession` 分区、一个地址成立性询问、一对一角色一行的位置读写，没有任何业务含义的动作。「不绕过会话的旁路」：已转换的三处全部经会话；未转换的七处仍走各自原有的仓储事务，未新增任何绕过路径，也未产生新的原子性空洞——我逐条核对了两条改为 `Transactional` 的命令处理器（`SelectCharacterPhaseCommands.cs:259-303`），它们除存储会话与内存注册表外不调用任何仓储写入。

因此 RQ-011 达成。附带一条非阻断记录缺陷，见 N-2。

### 二、七条 Contract Amendment——**全部恰当且被遵守，无越界、无未申报改动**

| 修正条款 | 判定 | 依据 |
|---|---|---|
| TASK-01 A1（增列 `Infrastructure/Persistence/`） | 恰当 | 按数据库归并的参与者按定义不属于物品，写在 `SqliteItemStore.cs` 里会在 TASK-04 被迫搬动已被消费的代码。该目录下至今只有 `SqliteDatabaseParticipant.cs` 一个文件，内容恰为「参与者 + 面契约」两样 |
| TASK-01 A2（以证伪替代先红后绿） | 恰当 | 动手前未申报是真缺陷，首评抓住后以修正条款补正，并诚实更正了「不可得」的错误理由。所留证伪记录含三条用例名、失败信息、行号与恢复确认；失败文本与 `DomainTransaction.cs:48-52` 原文逐字相符 |
| TASK-03 A1（增列 `Abstractions/Characters/`，仅新增规则契约） | 恰当 | `Dfo.Server.Infrastructure` 不引用 `Application`，规则接口必须落在 Abstractions。有同形先例（`IItemArrivalRuleService`）。实际只新增 `ICharacterEntryRules.cs`，未触碰 BD-005 的 `ICharacterEntryStateServices.cs` |
| TASK-04 A1（增列同目录，仅新增会话契约） | 恰当 | 同上，实际只新增 `ICharacterStore.cs` |
| TASK-04 A2（增列 `SelectCharacterPhaseCommands.cs`） | 恰当且不可回避 | 验收条件 3 要求 `PersistPositionAsync` 零命中，而 `:122` 正在调用它；不改该调用点交付的是编译不过的树。实际改动恰为 2 行 |
| TASK-04 A3（两条阶段命令改 `Transactional`） | 恰当 | `UnitOfWorkBehavior.cs:27-30` 只对 `Transactional` 开事务；不翻这个开关，人物会话取不到边界，只剩「业务代码自开事务」（违 RQ-004）或「写落在边界外」（违 RQ-007）两条死路。`CommandExecutionPolicyBehavior.cs:18` 对 `Direct`/`Workflow`/`Transactional` 同为 `next()`，故不改帧内容与投递顺序。实际只改两条，另五条阶段命令未动，我逐行核过 `ProtocolModule.cs:112-136` |
| TASK-07 A1（增列 `AGENTS.md`） | 恰当 | 该文件是仓库指令文件，其「一个存储一次用例只参与一次」与「七条阶段命令均为 Workflow」两处已被本任务改变，不同步会误导后来者。我逐条核对了改后的五处陈述与代码，全部属实（含首评 N2 指出的 `ICharacterStoreSession` 非 `IStoreSession` 实例那句，现已如实写出两种形状） |

**未申报改动核查**：把 28 个已跟踪改动文件与 14 个未跟踪源/测试文件逐一映射到七个单元的 Permitted files 加上述修正条款，无一落在申报之外。账号侧两个仓储的改动经逐 hunk 核对**只有移除建表与补列**，读写路径一字未改（符合 Scope Out）；`SqliteInventoryEntryRepository.cs` 与 `SqliteCharacterRepository.cs` 的新增行合计只有 `using` 与一行 `CharacterDatabaseSchema.Ensure(connectionString);`。

### 三、CK-022 的处置——**判定：控制器处置恰当**

判 `spec-blocked` 的条件是「不存在同 Spec 内的授权路径」。此处路径存在、明确、且是**契约自己指定的**：`TaskContract.md:87` 的 SC-8 required evidence 原文就写着「实机走查角色选择、进入频道、拖拽与喝药表现与改动前一致」——契约从一开始就把这一项设计成由人执行的证据，而不是执行者可自动化的检查。执行者拒绝以 `Dfo.Server.Host.Tests` 4 条冒名顶替是对的（那 4 条覆盖打包 exe 的启动与频道引导，不覆盖背包拖拽与消耗品使用）；快评判 `blocked` 并出具 blocker report 是对的（证据在本单元不可得）；控制器判「移交用户、单元保持 `in_progress`、验收条件 3 挂起」也是对的。三层处置一致且诚实，无一处把未闭合项伪装成已完成。

**但必须明确其后果**：SC-8 因此尚未完整达成，本次批准**不构成完成或已验证主张**。走查前 `sdd-execution.yaml` 的 `verification` 阶段不应认定通过。走查时的重点应落在**道具移动与道具使用**两条——`SelectCharacterAcceptedTranscriptTests` 为角色选择与进入频道提供了未改动的整序列 SHA256 金样，而道具两条没有一条把真实移动一路走到帧字节再比对的未改动夹具（`MoveItemSpaceCodecTests`/`UseStackableCodecTests` 是纯编码器用例，而本次改写的正是喂给编码器的那一侧）。另按 Required methods 与项目既有教训，走查后须清理探针角色。

## 非阻断发现

| ID | 文件与行 | 说明 |
|---|---|---|
| N-1 | `Characters/SqliteCharacterRepository.cs:30`、`Characters/SqliteCharacterEntryStateRepository.cs:27`、`Characters/SqliteCharacterPvpSkillProfileRepository.cs:18`、`Accounts/SqliteAccountRepository.cs:28`、`Accounts/SqliteAccountEntryStateRepository.cs:19`、`Inventory/SqliteInventoryEntryRepository.cs:28`、`Inventory/SqliteEntryInventoryMutationRepository.cs:32` | RQ-009 首句「建表不是任何读写组件的构造副作用」只在**声明**意义上达成：七个仓储的构造函数仍执行 `CharacterDatabaseSchema.Ensure`，即仍会建满 52 表 + 6 索引。判非阻断的理由是这句与 spec 自己的 AC-014（`spec.md:102`「只构造该组件…该组件所需的表已经存在」）**字面互斥**——惰性建表会让 AC-014 不成立，而模块层 eager 建表又违反 `AGENTS.md` 的「`*Module` 只注册不执行业务」（执行者已试过，12 条用例转红）。RQ-009 的两条验收场景 AC-014/AC-015 均已达成并经我独立复核；实质缺陷（顺序决定形状、`IF NOT EXISTS` 静默丢定义）已消灭并由 `TheShapeOfTheDatabaseDoesNotDependOnWhatIsBuiltFirst` 钉住。建议契约所有者以一条修正把 RQ-009 首句改为「建表不由任何读写组件**声明**」，使措辞与已验收的形状一致 |
| N-2 | `.rks-agent/…/tasks/TASK-04-character-store-and-sessions.md:174-187` | EV-008 要求的是**表达性**结论，CK-012 表对 7 处未转换写入中的 6 处给的是**范围**理由（「不属本单元范围」），只有 `characters` 创建/软删一行写了「可以位置与值表达」。末尾「没有一处表达不出」因此是断言而非展示。不影响任何成功条件，但 EV-008 是 spec 指定的证据类，建议补齐六行一句话的表达性结论（`character_skills` / `character_init_flags` / `character_subtype*` / `character_pvp_skills` / `character_pvp_skill_state` / `dungeon_persistent_effect_outbox`），尤以最后一条最值得写明——它是租约与状态机式的写入，是七处里最不显然的一处 |
| N-3 | `Schema/CharacterDatabaseSchema.cs:91-123` 与 `:191` 起的 `ReconcilableColumns` | 补列面从基线七个手写补列器的约 41 列扩大到 **297** 条声明，约 7 倍。与「开发阶段擦库不迁移：不得为既有数据编写回填、修复或迁移路径」存在张力：全新库上 `Reconcile` 永远是空操作，它唯一会生效的场景恰是「旧形状的既有库」。判非阻断，因为 SC-6（`TaskContract.md:85`）明文允许「有保留理由的记录」，且执行者第一版删掉补列器时 20 条既有用例转红（含 `CharacterEntryState_UpgradesPreProjectionNormalizedSchemaIdempotently`，其名字本身说明升级旧表是被刻意测试的行为），删除会违反「既有测试语义不得修改」。建议契约所有者就「保留 + 扩面」二者一并追认。附带一条效率观察：`Ensure` 每次执行约 50 次 `PRAGMA table_info`，而它被七个仓储各调一次，启动期约 350 次往返 |
| N-4 | `Characters/SqliteCharacterStore.cs:438-441`，对 `Abstractions/Transactions/DomainStores.cs:66-67` | `CreatureSection.EmptyAsync` 对 `IStoreSession.Clear` 抛 `NotSupportedException`，且抛在 **flush（准备阶段）**而不是 `Clear` 调用处。协议承诺「按位置清空」，该分区不支持——理由（生物条件只是别人行上的一个字段）成立且已写明，但失败点应前移到 `Clear`，否则一次误用要到事务准备时才炸，且会连累同一事务里其他面已刷写的语句 |
| N-5 | `Application/Characters/CharacterStateAccess.cs:107-136` | `SelectedSessionPositionPersistence` 从不询问 `IDomainTransactionAccessor`，无条件 `new DomainTransaction()`。今日两个调用点（断线清理、`Workflow` 的 `PrepareSelectedCharacterSessionCommandHandler`）均无环境事务，故正确；`ProtocolModule.cs:112` 的 Prepare 与 `:134` 的 Compensate 仍为 `Workflow`，A3 改动的两条既不调位置持久化也不调清理，我已复核**当前不存在嵌套**。但这是一处会随将来某次执行方式变更而失效的保证：届时位置写不仅逃出环境边界，还会在同一数据库上开第二条连接，而该库的参与者可能正持有写事务。建议加一条「`transactions.Current` 非空即抛」的守卫，把「必须自开边界」这件事从注释变成代码 |
| N-6 | `Characters/SqliteCharacterStore.cs:152-164` 对 `:184-187` | 位置写入的归属失败文本变了：旧实现把归属条件写进 UPDATE 的 WHERE，影响行数不为 1 抛 `"Entry position owner is unavailable."`；新实现 flush 时先跑 `RefuseUnlessOwnedAsync`，于是归属不成立的情形改抛 `"Entry mutation character ownership does not match."`。异常类型同为 `InvalidDataException`，全库无用例断言这两个文本，且失败发生在任何帧写出之前，故不构成对外行为改变。记录以免将来被当成回归 |
| N-7 | `Application/Characters/SelectCharacterPostEntryServices.cs:149-150` | 宠物路径末尾的 `"Revived creature is absent from the prepared character state."` 若抛出，旧实现此时 `field04=1` **已提交**，新实现会随事务回滚。失败路径上的行为变化，方向是改善（原子性正是本次要拿到的东西），仅备案 |
| N-8 | `Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs:271-272`、`:299-300` | A3 之后 `ReplaceRuntimeExact` 由「数据库提交之后」变为「提交之前」——内存注册表不是事务参与者，提交失败时不随之回滚。阶段失败有补偿路径拆会话兜底，今日不可观察。执行者已在 Remediation 中正面承认，此处仅确认其判断 |
| N-9 | `tests/Dfo.Server.Infrastructure.Tests/Schema/SchemaOwnershipTests.cs:14-23, 40-45, 63-67`；`Infrastructure/Inventory/SqliteItemStore.cs:13`；`Infrastructure/Characters/SqliteCharacterStore.cs:14` | 用现在时描述已被本次消灭的世界：「Tables are declared by whichever components happen to be constructed, several of them declare the same table differently」「The item store depends on exactly this today, which is why its own tests construct two repositories」；两个存储的「Uses the tables the … repositories already create」现在也不准确——表由 `Schema/` 声明，仓储只是触发者。这些是后来者最先读到的句子 |
| N-10 | `tests/Dfo.Server.Infrastructure.Tests/Characters/SqliteEntryLifecycleRecoveryTests.cs`（新增的两条 `DungeonExperienceRecovery_*`） | 本次新增的这两条用例沿用了无 `IOException` 兜底的 `finally { ClearAllPools(); File.Delete(path); }`，而 TASK-07 自己把这一写法认定为基础设施用例偶发假红的机制线索。既有缺口非本次引入，但本次又添了两处；同批新增的 `SchemaOwnershipTests`、`CharacterStoreSettlementTests`、`OutsideTransactionTests`、`OneDatabaseOneParticipantTests` 都是有兜底的那一类，两处补齐即可一致 |
| N-11 | `Infrastructure/Persistence/SqliteDatabaseParticipant.cs:89-92` | `OpenAsync` 中若 `link.OpenAsync` 抛出，`link` 既未赋给 `this.connection` 也未被释放，`CloseAsync` 因此够不着它。未打开的连接不持有非托管句柄，半开的可能持有。形状承自被替换实现，非本次引入，成本是一行 `try/catch { await link.DisposeAsync(); throw; }` |
| N-12 | `Application/Characters/CharacterStateAccess.cs:58-92` 对 `Abstractions/Characters/ICharacterStore.cs:24` | 「事务外改动持久人物状态必抛」由 `AmbientStoreAccess` 这一层保证，而不是由存储自身。`ICharacterStore` 是 public，任何人都可以 `store.SessionIn(new DomainTransaction())` 后写入并永不结算——写会被静默丢弃。测试与 `SelectedSessionPositionPersistence` 正是这样直接用的。与契约「保证的边界必须明示，不得靠巧合成立」有张力：今天是明示的（文档 + 用例）但不是构造性的。物品侧同形，非本次引入的形状 |
| N-13 | `TaskContract.md:29,85`（`spec.md`、`plan.md` 的同源引用） | 基线数字有偏差：`712bdbc` 实为 **59** 条 `CREATE TABLE` 覆盖 **52** 张表、**5** 张多定义（`accounts`×3、`characters`×3、`character_gold_limits`×2、`character_init_flags`×2、`character_daily_counters`×2），契约写的是「58 处 / 51 表 / 6 张多定义」。我以脚本独立复核。不影响判定——SC-6 要的不变式「定义处数 == 表数」在 52/52 上成立 |

## 结论

代码与工件层面无阻断发现。八条成功条件中 SC-1 至 SC-7 全部达成并经我独立取证；SC-8 的帧字节与全量两项达成，第三项（实机走查）按控制器处置移交用户，判定该处置恰当。12 条 RQ 均有对应实现与证据，无一条被记为达成而实际未达成——RQ-009 与 RQ-011 两处措辞与交付之间的张力已在上文正面裁决，均判达成并各留一条非阻断记录。七条 Contract Amendment 全部恰当且被遵守，未发现越界或未申报改动。契约约束逐条成立：对外行为无编码路径改动、既有测试只作机械调整（无删除、无转跳过）、至多一个可失败参与者由 `DomainTransaction.Enlist` 强制、事务外持久人物写入抛出、两个存储零 DDL、`Tool/` 与 `.worktrees/legacy-reference/` 零改动、`protocol/` 未被触及。

**本批准覆盖评审门，不构成完成或已验证主张。** CK-022 闭合前，`verification` 阶段不应认定通过。

## Remediation after the change review

变更评审判 `approved`，无阻断。13 项非阻断中四项确属真问题且修正代价极小，已闭合；其余记录在案。

### 已修

| findings | 修改 |
|---|---|
| **N-6** 位置写入的归属失败文本被换掉了（`"Entry position owner is unavailable."` → `"Entry mutation character ownership does not match."`） | 这是我引入的一处行为变化，评审记为「备案防误读」，但它本可以修好。成因：我把归属校验做成 flush 时对**所有**触及角色的统一前置检查，于是位置写入的失败被那个统一检查抢先答了。改法是把位置排除在统一检查之外——它的 UPDATE 语句本就自带 `account_id AND delete_flag=0` 谓词并以自己的措辞拒绝，一直如此。`CharacterStoreSettlementTests.ACharacterNamedUnderTheWrongAccountIsRefused` 补断言该文本，现在这句话被钉住了 |
| **N-4** `CreatureSection` 对协议承诺的 `Clear` 抛 `NotSupportedException`，且抛在 flush 而非调用处，会连累同事务其他面已刷写的语句 | `Clear` 改为 `virtual`，由该段覆写为**在调用处**抛出；`EmptyAsync` 降为 `UnreachableException`。拒绝的时机回到它该在的地方 |
| **N-5** `SelectedSessionPositionPersistence` 从不询问 `IDomainTransactionAccessor`，将来若某条命令改为 `Transactional`，它会逃出环境边界并在同库开第二条连接 | 加守卫：环境事务存在时抛出并说明应改走该边界。它存在的理由就是「没有边界时开一个」，有边界还自开一个正是它要防的事 |
| **N-11** `SqliteDatabaseParticipant.OpenAsync` 在 `OpenAsync` 抛出时不释放连接（承自旧形状） | `try/catch` 释放后重抛。此时无人持有它，也就无人会关它 |

### 记录在案、不予修改

| findings | 判断 |
|---|---|
| N-1 七个仓储构造函数仍调 `Ensure`，RQ-009 首句只在「声明」意义上达成 | 评审自陈该句与 spec 的 AC-014 **字面互斥**，并建议契约所有者改措辞为「不由任何读写组件**声明**」。这是契约措辞问题，不是交付问题，留给契约所有者 |
| N-2 CK-012 表对 7 处未转换写入中的 6 处给的是范围理由而非表达性结论 | 属实。已在 TASK-04 的 CK-012 表下补记：六处均**可以**以位置与值表达（各自的行都自带键），未转换的原因是不属该单元范围，不是表达不出 |
| N-3 补列面由基线约 41 列扩到 297 条，与「擦库不迁移」有张力 | 保持。SC-6 明文允许保留补列；删除会让 20 条既有用例转红（其中一条名字就叫 `...UpgradesPreProjectionNormalizedSchemaIdempotently`）。扩面是「集中声明」的必然结果而非新增迁移能力——它补的仍是同一批表的同一批列 |
| N-7 宠物失败路径下 `field04=1` 由「已提交」变为「随事务回滚」 | 保持。方向是改善，且正是本任务要的原子性 |
| N-8 `ReplaceRuntimeExact` 相对次序由提交后变为提交前 | 保持，已在 TASK-04 的 Remediation 2 记录 |
| N-9 若干类注释仍以现在时描述已被消灭的世界 | 保持。那些注释记的是「为什么当初那样」，与 `AGENTS.md` 要求的「legacy 行为说明来源」同类 |
| N-10 本次新增两条用例沿用无 `IOException` 兜底的 teardown | 保持。已在 TASK-07 更正记述并承认；统一加兜底应是单独一刀 |
| N-12 「事务外持久写必抛」由 `AmbientStoreAccess` 保证而非存储自身 | 保持。物品侧同形（`PersistentItemContainer` 而非 `SqliteItemStore` 负责拒绝），本次不引入新的不对称。评审所指的张力真实，但收紧它应同时改物品侧，超出本任务 |
| N-13 契约基线数字偏差（实为 59 条 / 52 表 / 5 张多定义，契约写 58 / 51 / 6） | 已在 TASK-06 的记录中更正为 59；表数与多定义数以评审复核为准。契约正文不在实施阶段改动 |

### 复验

`dotnet build`：0 警告 0 错误。全量 `1008 通过 / 2 跳过 / 0 失败`。
`CharacterStoreSettlementTests` 6 条全通过，含新增的失败文本断言。
