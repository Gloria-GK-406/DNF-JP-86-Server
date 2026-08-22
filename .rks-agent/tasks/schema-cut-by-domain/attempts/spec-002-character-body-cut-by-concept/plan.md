---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 角色本体按概念重切

## Implementation Approach

Spec 的核心成果是库里角色本体这一块的形状；必要支撑是六张每角色一行的表在建角色时各得一行、未被写入的列取值不变、九张源表的每一条约束在落点上存续；外围保证是下发帧字节与表征用例的可证伪性。

最小的做法建立在一个已经存在的事实上：**领域记录不变**。`CharacterEntryStorageRecord` 及其四个投影的形状、字段与取值都不改，改的只是每个字段从哪张表的哪一列来。这条把整件事变成一次可验证的搬运，并且直接给出最强的行为保持守卫——三份整记录表征用例的**期望输出一个字节都不用改**，只有播种的 SQL 改成写新表。期望值若被改动，就说明搬运改变了行为。

最早的核心任务是把库形状改成目标形状（T-003）。它前面有两个任务，两个都不可省，而且都只是「先立守卫」：

- T-001 先把目标形状与约束落点写成会失败的断言。不先写，T-003 之后就没有任何东西能证明九张源表的约束没被丢掉——SQL 层的约束缺失不产生编译错误，也不产生测试红灯。裁定评审中发现的那条即将静默丢失的级联外键，正是这一类。
- T-002 先把表征用例的播种改到目标形状、期望值原样不动。次序反过来（先改组装、后改用例）会让期望值照着改动后的行为被重写，用例从此谁也守不住。

T-003 到 T-006 之间全量套件是红的，这是意料之中：库形状先变、读写路径随后跟上。每个任务有自己那一条转绿的检查。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | 九张源表的声明改为六张目标表；每日挑战两表合一；拼写更正；约束、默认值与索引按裁定落位 | 裁定的目标形状与键约束两节 | 新的 `TableDeclarations`、`IndexDeclarations` 与可补列清单 |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs`、`CharacterEntryTables.cs` | 按表接口与其行类型随目标表改写 | 目标形状 | 每个接口只指名一张目标表 |
| BD-003 | `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs`、`CharacterEntryStateTables.cs`、`CharacterSkillTables.cs`、`SqliteCharacterStore.cs` | 读写语句改到目标表；建角色的写入供给六张表 | BD-001、BD-002 | 与目标形状一致的读写路径 |
| BD-004 | `Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs`、`CharacterEntryStateServices.cs`、`CharacterRosterService.cs` | 跨表组装改到新来源；建角色写六张表 | BD-003 | 领域记录不变的组装 |
| BD-005 | `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`、`Server/src/Dfo.Server.Persistence/Inventory/InventoryStateTables.cs`、`Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` | 仅把金币上限那一处读取改到 `character_stats`，不做物品域自身的重切 | BD-001 | 物品侧读取指向新位置 |
| BD-006 | `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs` 及同目录新增的形状与约束守卫 | 断言目标形状与九张源表约束的落点 | 裁定 | 会失败而后转绿的守卫 |
| BD-007 | `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`、`Accounts/AccountEntryLoadCharacterizationTests.cs`、`Inventory/InventoryLoadCharacterizationTests.cs` | 播种改到目标表，期望输出一个字节不改 | BD-001 | 期望值不变的表征守卫 |
| BD-008 | `Server/tests/Dfo.Server.Persistence.Tests/Characters/SqliteCharacterEntryStateRepositoryTests.cs`、`SqliteCharacterLifecycleTests.cs`、`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs`、`Server/tests/Dfo.Server.Persistence.Tests/Database/*.cs`、`Inventory/SqliteInventoryEntryRepositoryTests.cs` | 播种与断言随目标形状机械调整，语义不改 | BD-001 | 与新形状一致的既有用例 |
| BD-009 | `Server/tests/Dfo.Server.Persistence.Tests/Characters/` 下新增建角色行与默认值的用例 | 覆盖六张表各得一行与八个非零默认值 | 裁定的默认值表 | 新用例 |

## Ordered Tasks

### T-001: 目标形状与约束落点的守卫先行

- Covers: RQ-001, RQ-002, RQ-003
- Boundaries: BD-006
- Inputs: 裁定的「Target shape」与「Keys of the tables that change」两节；九张源表的现有声明
- Outputs: 一组会失败的断言，覆盖六张目标表的列、五张折入表的消失、以及九张源表每一条键/唯一性/检查/外键的落点

#### Steps

1. 从裁定抄出六张目标表应有的列名集合，写成对 `PRAGMA table_info` 的断言。
2. 断言五张折入的表（`character_init_flags`、`character_gold_limits`、`character_crystal_contract`、`character_dimension_flags`、`character_pvp_skill_state`）在库里不存在，`character_expert_job` 存在且含它吸收的两列。
3. 从九张源表的现有声明抄出每一条键、唯一性、检查与外键，逐条写成对新形状的断言——特别是 `character_pvp` 必须带 `FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE`。
4. 断言 `character_daily_challenges` 与 `character_invisible_flags` 存在，且旧名不存在。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1 --filter FullyQualifiedName~Schema` | 新增断言全部失败，失败原因是目标表不存在或约束缺失，而非断言本身写错 |

### T-002: 表征用例的播种改到目标形状，期望值原样不动

- Depends on: T-001
- Covers: RQ-007
- Boundaries: BD-007
- Inputs: 三份表征用例的现有播种与期望值；裁定的列来源表
- Outputs: 播种写入目标表、期望输出逐字节不变的三份用例

#### Steps

1. 把三份用例里对九张源表的 `INSERT` 改写为对六张目标表的 `INSERT`，逐列按裁定的来源对应。
2. **不改动任何一处期望输出。** 期望值若必须改，说明搬运改变了行为，停下并回到裁定。
3. 保留今天所有的边界种子（空值、上下界、职业专属行），不减少任何一条。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-002 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1 --filter FullyQualifiedName~Characterization` | 三份用例全部失败，失败原因是目标表不存在；期望值一处未改 |
| CK-003 | Non-command: 逐份 diff 用例文件，确认改动只落在播种语句上 | 期望输出的每一行与改动前相同 |

### T-003: 库形状改为目标形状

- Depends on: T-001, T-002
- Covers: RQ-001, RQ-002, RQ-003, RQ-008, RQ-009
- Boundaries: BD-001
- Inputs: 裁定的目标形状、键与约束、类型与可空的继承规则及其两条例外、索引六条结局
- Outputs: 新的库形状声明

#### Steps

1. 用六张目标表的声明替换 `characters`、`character_subtype0_fields`、`character_subtype1_fields` 与五张折入表的声明；`character_expert_job` 改为吸收两列后的形状。
2. 按裁定写约束：`character_pvp` 的级联外键；各表的键。
3. 按裁定写默认值：稀疏与窄表 `NOT NULL` 两条规则涉及的十列取 `INTEGER NOT NULL DEFAULT 0`；八个非零默认值逐一按裁定所列。
4. 合并每日挑战两表为 `character_daily_challenges(character_id, group_index, group_id, claimed_at)`，`claimed_at` 可空。
5. 把 `character_invisible_falgs` 改名为 `character_invisible_flags`。
6. 按裁定处置六条索引：一条原地、一条改建在 `owner_id` 上（属物品域，本任务不动）、两条合成一条或不建、两条随退役表消失（同属物品域，本任务不动）。本任务只动角色本体涉及的索引。
7. 同步可补列清单，删去指向已消失的表的条目。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1 --filter FullyQualifiedName~Schema` | T-001 的断言全部转绿 |
| CK-005 | Non-command: 把新声明的六张目标表的列与裁定的目标形状逐列比对 | 表名、列名、次序无关的集合相等 |

### T-004: 建角色写六张表，八个默认值就位

- Depends on: T-003
- Covers: RQ-004, RQ-005
- Boundaries: BD-003, BD-004, BD-009
- Inputs: 裁定的「哪些表每角色一行、谁建这一行」与八个非零默认值
- Outputs: 建角色后六张表各有一行；新增覆盖用例

#### Steps

1. 改写建角色的写入，使 `characters`、`character_stats`、`character_condition`、`character_settings`、`character_pvp`、`character_expert_job` 各得一行。
2. `character_condition` 的写入显式给 `pc_room_state`，因为该列可空无默认而读侧遇 NULL 即抛。
3. 新增用例：建一个角色后六张表各有一行；八个非零默认值逐一读回并与裁定所列比对。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1 --filter FullyQualifiedName~Lifecycle` | 建角色相关用例通过，新增的六行与八默认值用例通过 |
| CK-007 | Non-command: 对新增的默认值用例，逐一把期望值改成 0 再跑 | 每改一个都变红——证明该用例真的在守这个默认值 |

### T-005: 按表接口与读写路径改到新形状

- Depends on: T-004
- Covers: RQ-001, RQ-010
- Boundaries: BD-002, BD-003, BD-005
- Inputs: 目标形状；现有的按表接口与行类型
- Outputs: 每个持久化类只指名一张目标表；金币上限的物品侧读取指向 `character_stats`

#### Steps

1. 按目标表改写 `CharacterTables.cs` 与 `CharacterEntryTables.cs` 里的接口与行类型；合并或拆分行类型使其与目标表一一对应。
2. 改写 `CharacterEntryTables.cs`、`CharacterEntryStateTables.cs`、`CharacterSkillTables.cs`、`SqliteCharacterStore.cs` 的语句。
3. 只把金币上限那一处物品侧读取改到 `character_stats`，不动物品域其余部分。
4. 组合根随接口变化更新。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `dotnet build Server/86JP.Server.slnx` | 通过，无警告（警告即错误） |
| CK-009 | Non-command: 清点每个持久化类的 SQL 中出现的表名 | 每个类只出现一张表 |

### T-006: 领域组装改到新来源，领域记录不变

- Depends on: T-005
- Covers: RQ-006, RQ-007, RQ-010
- Boundaries: BD-004, BD-007, BD-008
- Inputs: 现有组装；目标形状
- Outputs: 领域记录逐字段不变的组装；三份表征用例转绿

#### Steps

1. 改写 `CharacterEntryAssembly` 与 `CharacterEntryStateServices`，使每个领域字段从它在目标形状里的新位置取值。
2. **不改动任何领域记录的形状或字段**。若某个字段取不到，回到裁定核对该列的落点。
3. 既有用例的播种与断言随形状机械调整，语义不改。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-010 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1` | 全部通过，含 T-002 那三份期望值未改的表征用例 |
| CK-011 | Non-command: diff 领域记录的定义 | `CharacterEntryStorageRecord` 与四个投影一处未改 |

### T-007: 行为保持证据

- Depends on: T-006
- Covers: RQ-006, RQ-007, RQ-010
- Boundaries: BD-007, BD-008
- Inputs: 全量套件；改动前的下发帧
- Outputs: 字节比对、全量结果、可证伪性演示、清点结果

#### Steps

1. 跑全量套件。
2. 取同一个角色在改动前后的选人下发帧，逐字节比对。
3. 对三份表征用例的每一处承载，构造一次列错位或列缺失，确认变红后还原。
4. 清点持久化类与其指名表、领域记录的定义处。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-012 | `dotnet test Server/86JP.Server.slnx -m:1` | 1032 通过 / 2 跳过 / 0 失败，或可解释的用例数变化；红色项仅限已知的满载超时且隔离复跑为绿 |
| CK-013 | Non-command: 选人下发帧的逐字节比对 | 与改动前完全相同 |
| CK-014 | Non-command: 表征用例的可证伪性演示 | 每一处承载都能被构造成红 |
| CK-015 | Non-command: 持久化类与记录定义处的清点 | 每个类只指名一张表；每条记录只有一个定义点 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-003, T-005 | BD-001, BD-002, BD-003, BD-006 | CK-004, CK-005, CK-008 |
| RQ-002 | T-001, T-003 | BD-001, BD-006 | CK-004, CK-005 |
| RQ-003 | T-001, T-003 | BD-001, BD-006 | CK-001, CK-004 |
| RQ-004 | T-004 | BD-003, BD-004, BD-009 | CK-006 |
| RQ-005 | T-004 | BD-009 | CK-006, CK-007 |
| RQ-006 | T-006, T-007 | BD-004, BD-008 | CK-010, CK-012, CK-013 |
| RQ-007 | T-002, T-006, T-007 | BD-007 | CK-002, CK-003, CK-010, CK-014 |
| RQ-008 | T-003 | BD-001 | CK-004, CK-010 |
| RQ-009 | T-003 | BD-001 | CK-004, CK-010 |
| RQ-010 | T-005, T-006, T-007 | BD-002, BD-003, BD-004 | CK-009, CK-011, CK-015 |

## Risks and Rollback

- Risk：T-003 到 T-006 之间全量套件是红的。缓解：每个任务有自己那一条转绿的检查（CK-004、CK-006、CK-008、CK-010），红的范围与原因在每一步都可指认；T-007 之前不得声称行为保持。
- Risk：表征用例的期望值被「顺手」改动，从而失去守卫作用。缓解：CK-003 要求逐份 diff 确认改动只落在播种上；T-002 明写期望值若必须改就停下回到裁定。
- Rollback：本成果的全部改动限于本任务工作树内的已知路径集合（BD-001..BD-009）。回滚为从 `workspace.baseline` 恢复这些路径，不触及其他内容。

## Assumptions

- 裁定中标为已核实的事实成立。若实施中发现某条不成立，先更新裁定再继续。
- 领域记录不需要改变。若某个领域字段在目标形状里无处可取，说明裁定漏了一列，回到裁定而不是就地改记录。
