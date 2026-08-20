---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 人物持久化收敛到统一存储协议

## Context

### Source Request

用户在勘察人物与任务两块的持久层现状后判定：「那能改成类似 IItemStore 一样的抽象的接口吗？我们要把仓储层的抽象和 IItemStore 一样回收才对。」

Agent 曾提出人物不宜照抄物品的寻址词汇，用户在获知该顾虑后仍确认此方向，并在范围与流程两个选项中选定「1–4 步一次做完」与「SDD 治理流程」。

### Current Situation

上一个根任务 `domain-transaction-and-item-store` 已合并（main @ 712bdbc），物品侧拿到了四件事：载体无关的存储端口、持久与内存两种实现、作为参与者入事务、以及零 DDL。人物与任务两块一件也没有。

**写路径没有抽象。** `ICharacterEntryMutationRepository` 上挂的是业务动词——`UpdateSeekingQuestTriggersAsync`、`ReviveEquippedCreatureAsync`、`PersistPositionAsync`——而不是数据动词。每新增一种人物变更就要在接口上新增一个方法，正是上一轮从容器契约中删掉的 `ContainerMovePlan` 那种形状。

**规则住在持久实现里，三处：** `SqliteExpertSkillContractReconciliationRepository.ReconcileAsync` 读 `account_premiums` 判过期、按 `SkillPointCostDefinition` 算技能上限、再 UPDATE/DELETE `character_skills`，跨 `account_premiums` / `characters` / `character_skills` 三张表两个聚合；`SqliteDungeonPersistentEffectRecoveryRepository.RecoverAsync` 在仓储内实现 outbox + lease + dead-letter，并注入 `IDungeonExperienceProgressionPolicySource` 于仓储内部执行经验发放；`SqliteCharacterEntryStateRepository.ResolveGoldUpgradeLevel` 把规则写在读仓储里。这三处既无法脱离 SQLite 测试，也无法被任何其他载体复用。

**不参与事务。** 全仓库实现 `ITransactionParticipant` 的只有 `SqliteItemStore` 与 `MemoryItemStore`。人物侧 38 处 `new SqliteConnection`、13 处独立 `BeginTransactionAsync`，每个仓储方法自己开自己提交。因此「移动一件道具」与「更新一个任务触发值」今天无法原子完成。

**该缺口不能靠再加一个独立 store 补上，已验证。** `SqliteItemStore.StoreIdentity` 是连接字符串，而 `DomainTransaction.Enlist` 按 identity 去重并限定提交仍可能失败的参与者至多一个。一个独立的人物持久 store 若用同一连接串，会撞上 `Enlist` 的类型冲突；若用不同连接串，会撞上「至多一个可失败参与者」。两条路都不通。因此本任务要达成的原子性，要求同一个数据库在一次事务中只作为一个参与者出现，对外给出多个各自具类型的会话。

**建表是构造副作用，且定义互相冲突。** 各 `Sqlite*` 仓储构造函数末尾调用 `EnsureSchema()`。全仓库 58 处 `CREATE TABLE IF NOT EXISTS` 覆盖 51 张表，其中 6 张在多处定义：`accounts` 三处且列集互不相同（`SqliteAccountRepository` 7 列、`SqliteAccountEntryStateRepository` 12 列、`SqliteInventoryEntryRepository` 更多）。`IF NOT EXISTS` 意味着**先被构造的那一处胜出**，其余静默失效，于是又需要 5 处 `ALTER TABLE` 加 4 处 `PRAGMA table_info` 事后补列。`SqliteItemStore` 的零 DDL 因此是隐式依赖：它能工作只因为 `SqliteInventoryEntryRepository` 恰好先被构造。

**读路径已有一层可用的分层。** `ICharacterEntryStateRepository.LoadAsync` → `CharacterEntryStorageRecord` → 四个 `I*EntryProjection` → 领域态，领域确实不认识 SQLite。且存储行类型已行自带键（`CharacterActiveQuestStorageRow(short Slot, ...)`、`CharacterPetStorageRow(..., long CreatureKey, ...)`、`CharacterTitleBookStorageRow(byte Category, int SlotIndex, ...)`），与 `StoredItem(SlotIndex, Core)` 同形。

**改造面小于表数。** 建了 31 张人物表，但生产代码真正写过的只有 10 张：`characters`、`character_skills`、`character_active_quests`、`character_creatures`、`character_init_flags`、`character_subtype`、`character_pvp_skills`、`character_pvp_skill_state`、`accounts`、`dungeon_persistent_effect_outbox`。

**层次泄漏一处。** `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs:38` 把 `ICharacterEntryMutationRepository` 直接注入协议层命令处理器。

## Goal

人物的持久化与物品的持久化落在同一套存储协议上：业务代码不感知载体，人物侧的改动以数据动词而非业务动词表达，持久实现中不夹带业务规则，且同一数据库上人物与物品的改动能在一次用例事务内共同成败。

建表不再是仓储构造的副作用，同一张表在代码库中只有一处定义。

## Scope

### In

- 从物品存储中提取与聚合无关的载体协议，`IItemStore` 成为其一个实例，物品侧对外行为不变。
- 人物存储及其入事务的会话；同一数据库在一次事务中只作为一个参与者出现。
- 人物侧现有写入改由该会话表达，`ICharacterEntryMutationRepository` 上的业务动词随之消失。
- 三处业务规则迁出持久实现：过期专家契约的技能上限回收、地下城持久效果的恢复与经验发放、金币上限等级的解析。
- 建表归属集中化：人物与物品的存储实现均不自建表；重复且冲突的表定义收敛为每张表一处。
- 协议层直接注入人物仓储的那一处泄漏。

### Out

- 为任务立独立聚合。任务继续留在 `CharacterEntryStorageRecord` 之下，本任务只随人物存储改造其唯一写入点（`UPDATE character_active_quests SET trigger_value`）。用户在三个范围选项中选定了不含此项的一个。
- 账号侧（`SqliteAccountRepository`、`SqliteAccountEntryStateRepository`）的存储改造。其表定义参与集中化，但其读写路径不改。
- 那 21 张已建表但生产代码从未写入的人物表：纳入建表归属集中化，不为其建立写入会话。
- 世界、频道、在线用户、会话等其余领域的持久化。
- 读路径的 `Storage → Projection` 分层重做。
- 跨数据库的事务。
- 对外可观察行为的任何改变。

## Constraints

- 不得改变任何对外可观察行为：帧字节与时序均不变。
- 既有测试语义不得修改，只允许因类型或依赖变化做机械调整。
- 保证的边界必须明示，不得靠巧合成立：一次事务中提交阶段仍可能失败的参与者最多一个；无法共同settle的组合须以失败暴露，而不是降级为非原子。
- 在事务之外改动持久人物状态必须以失败暴露，不得静默完成。授权来自上一轮实测：`Microsoft.Data.Sqlite` 在 ambient transaction 下既不入伙也不报错，静默的非原子写入不可检测。
- 存储实现不得自建任何表。
- 开发阶段的策略是重建而非迁移：不得为既有数据编写回填、修复或迁移路径。
- 规则迁出前，被迁出行为须已有覆盖；覆盖不足时先补覆盖再迁。
- 不得改动 `Tool/` 与 `.worktrees/legacy-reference/`；`DNFClient-ClientHackLab-20260812` 只读；不得手工编辑客户端可执行文件与 DLL。
- `protocol/` 目录内不得出现凭据、令牌、聊天隐私或个人可识别信息。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 同一数据库上人物与物品的改动能在一次用例事务内共同成败 | 一个用例同时改动物品槽位与人物侧的一处持久状态；令其中一处在准备阶段失败，断言两者均无改动可见；令全部成功，断言两者均可见 |
| SC-2 | 人物侧的持久化以数据动词表达，业务动词从仓储契约上消失 | `UpdateSeekingQuestTriggersAsync`、`ReviveEquippedCreatureAsync`、`PersistPositionAsync` 逐条核对为零命中；其行为由 Application 层承载并保持既有覆盖通过 |
| SC-3 | 业务代码不因人物状态落在何种载体而分支，也不出现事务操作 | 相关用例源码不含提交、回滚或事务对象；同一段业务逻辑在两种载体上执行得到相同结果 |
| SC-4 | 三处业务规则不再位于持久实现中 | 过期专家契约的技能上限回收、地下城持久效果的恢复与经验发放、金币上限等级解析在持久实现中零命中；三者的既有行为覆盖仍然通过 |
| SC-5 | 在事务之外改动持久人物状态会失败，而不是静默成功 | 事务外调用持久人物存储的改动操作时失败，且该失败可被测试观察 |
| SC-6 | 建表不再是仓储构造的副作用，且每张表只有一处定义 | 存储与仓储实现中 `CREATE TABLE` 零命中；全库同名表定义处数等于表数（基线 58 处 / 51 表 / 6 张多定义）；`ALTER TABLE` 与 `PRAGMA table_info` 补列器随之消除或有保留理由的记录 |
| SC-7 | 协议层不再直接持有人物仓储 | `SelectCharacterPhaseCommands` 不再注入 `ICharacterEntryMutationRepository` |
| SC-8 | 对外可观察行为一字未变 | 相关协议的编码字节与改动前逐字一致；全套自动化测试保持全绿（基线 944 通过 / 2 跳过 / 0 失败，加新增用例）；实机走查角色选择、进入频道、拖拽与喝药表现与改动前一致 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「那能改成类似 IItemStore 一样的抽象的接口吗？我们要把仓储层的抽象和 IItemStore 一样回收才对。」随后在 Agent 明确列出范围与流程选项后选定「1–4 步一次做完」与「SDD 治理流程」 | 把人物持久化收敛到与物品相同的存储协议上，含载体协议提取、人物存储与入事务、三处业务规则外迁、建表归属集中化，以及协议层一处仓储泄漏的修正 |

## Requires Confirmation

- 合并、推送、创建 PR、分支与工作树清理。
- 若「按键寻址的行读写」被证明不足以表达本范围内的人物写入（见 Assumptions 第一条），须回到本契约重议，不得在实施阶段自行引入业务动词或旁路会话。
- 若建表归属集中化在本范围内被证明必须触及账号或世界侧的读写路径，而不只是其表定义，须回到本契约重议。

## Assumptions

- 「按键寻址的行读写」足以表达本范围内那 10 张表的全部写入，行自带键、写入携带整行。该前提已按现有写入点核对，但未被穷尽证明。
- 三处规则外迁后行为等价，且其现有覆盖足以证明等价。若覆盖不足，须先补齐覆盖再外迁。
- 那 21 张已建表但从无写入的人物表在本范围内确实无需写入会话；若实施中发现某张实际有写入路径，应记录并纳入。
- 集中化建表后，各仓储不再于构造时建表不会破坏任何既有测试夹具；上一轮已出现过夹具依赖构造副作用的先例（`SqliteItemStoreTests` 需同时构造两个仓储才拿到表），故此项须在实施早期验证。
