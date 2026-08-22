---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 技能按概念重切

## Implementation Approach

Spec 的核心成果是库里技能这一块的形状；必要支撑是四条只在 PvP 一侧声明的保证逐条存续、以及它们加到 PvE 行上不改变任何现行写入的结果；外围保证是下发帧字节与表征用例的可证伪性。

领域记录不变：`CharacterSkillsEntryState` 与选人下发那一段的技能字段都不改，改的只是每一行从哪张表来。三份整记录表征用例的期望输出因此一个字节都不用改。

这一块的失效来源比前两块窄，但方向相反——前两块怕的是**丢**，这一块还怕**加**：

- **丢**：四条保证只在 `character_pvp_skills` 上声明，`character_skills` 一条都没有。按「以宽的为准」搬运会一条不剩地丢掉，而今天**没有任何用例断言这四条**。所以 T-001 先把它们写成会失败的断言。
- **加**：把四条保证加到 PvE 行上是一次收紧。`NOT NULL` 与级联外键是安全的（建角色的初始技能提供全部五个值，角色行本就存在），但 `UNIQUE (character_id, page_index, skill_id)` 是裁定**没有讨论过**的一处收紧：若某个职业的初始技能清单在同一页出现两次同一个 `skill_id`，改动后建角色会失败——而这只在实机建那个职业时才显形。所以 T-002 先把这件事证明掉，证不掉就先更新裁定。

T-003 到 T-005 之间全量套件是红的，这是意料之中。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/tests/Dfo.Server.Persistence.Tests/Schema/` 下新增的技能形状与约束守卫 | 断言目标形状与四条保证 | 裁定 | 会失败而后转绿的守卫 |
| BD-002 | `Docs/Refactor/TableOwnershipRuling.md` | 仅在 T-002 证不掉那处收紧时更新 | 初始技能清单的事实 | 可能的一段追加 |
| BD-003 | `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | 两张源表的声明换成一张目标表；索引与可补列清单随之调整 | 裁定 | 新的声明 |
| BD-004 | `Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs` | 按表接口与其行类型随目标表改写；`ICharacterPvpSkillTable` 退役 | BD-003 | 每个接口只指名一张目标表 |
| BD-005 | `Server/src/Dfo.Server.Persistence/Characters/CharacterSkillTables.cs` | 读写语句改到目标表并按 `mode` 限定 | BD-003、BD-004 | 一表一类的读写路径 |
| BD-006 | `Server/src/Dfo.Server.Application/Characters/SelectCharacterPersistenceServices.cs`、`CharacterRosterService.cs`、`CharacterEntryAssembly.cs` | 播种序列与建角色的技能写入改到新接口 | BD-005 | 领域记录不变的组装 |
| BD-007 | `Server/src/Dfo.Server.Persistence/PersistenceModule.cs` | 注册随接口增减更新 | BD-004 | 与新接口一致的注册 |
| BD-008 | `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs` 及其他播种技能的用例 | 播种改到目标表，期望输出不改 | BD-003 | 期望值不变的表征守卫 |
| BD-009 | `Server/tests/Dfo.Server.Persistence.Tests/Characters/` 下新增的收紧与播种序列守卫 | 覆盖四条保证的拒绝与接受、以及播种序列 | 裁定 | 新用例 |

## Ordered Tasks

### T-001: 四条保证的守卫先行

- Covers: RQ-001, RQ-004, RQ-005, RQ-006, RQ-007
- Boundaries: BD-001, BD-009
- Inputs: 两张源表的现有声明；裁定的 `character_skills` 目标列表与键
- Outputs: 一组会失败的断言

#### Steps

1. 从裁定抄出 `character_skills` 应有的七列，写成对 `PRAGMA table_info` 的断言；断言 `character_pvp_skills` 不存在。
2. 从 `character_pvp_skills` 的现有声明抄出四条保证，逐条写成对新形状的断言：五个非 BLOB 列的 `NOT NULL`、`CHECK (mode <> 'pvp' OR page_index BETWEEN 0 AND 1)`、级联外键、`UNIQUE(character_id, mode, page_index, skill_id)`，以及新键 `(character_id, mode, page_index, slot)` 与 `mode` 自身的 `CHECK (mode IN ('pve','pvp'))`。
3. 写行为层的断言：写入五列中任一为空被拒；`pvp` 行 `page_index=2` 被拒而 `pve` 行不被拒；删角色级联清除两组；同 mode 同页同 `skill_id` 的第二行被拒而另一 mode 的不被拒；同一角色两个 mode 的行互不干扰。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test ... --filter FullyQualifiedName~Schema` 与技能守卫的过滤 | 新增断言全部失败，失败原因是目标形状不存在或约束缺失，而非断言本身写错 |

### T-002: 证明收紧无害，或先更新裁定

- Depends on: T-001
- Covers: RQ-008
- Boundaries: BD-002
- Inputs: `NewCharacterSpec.InitialSkills` 的每一个供给点
- Outputs: 一条结论

#### Steps

1. 清点所有构造 `NewCharacterSpec.InitialSkills` 的地方（生产代码与用例）。
2. 对每一处判断：同一页里会不会出现两次同一个 `skill_id`。
3. 若不会，写下依据并继续；**若会或无法排除，停下**，把 `UNIQUE` 改为只按 `mode='pvp'` 限定（部分唯一索引），先更新裁定说明理由，再继续。
4. 无论哪种结论，补一条建角色带初始技能的用例，证明改动前后都写入成功。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-002 | 初始技能清单的清点 | 每一处都有结论 |
| CK-003 | 建角色带初始技能的用例 | 在**当前形状下**先跑绿 |

### T-003: 库形状改为目标形状

- Depends on: T-002
- Covers: RQ-001
- Boundaries: BD-003
- Inputs: 裁定（含 T-002 可能的更新）
- Outputs: 新的声明

#### Steps

1. 按裁定写出 `character_skills` 七列的声明，四条保证逐条落位，`mode` 加 `CHECK (mode IN ('pve','pvp'))`。
2. 删去 `character_pvp_skills` 的声明。
3. 可补列清单删去指向已消失表的条目；`character_skills` 因新增 `mode` 而需要一条补列条目——**但补列无法给一张既有表加上 `NOT NULL` 且无默认值的列**，所以 `mode` 的补列条目要么带默认值、要么这张表不进补列清单。按裁定与既有机制择一并写明理由。
4. 表数清点 44 → 43。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test ... --filter` 技能形状守卫 | T-001 的形状与约束断言全部转绿 |
| CK-005 | 表数清点 | 43 |

### T-004: 按表接口与读写路径

- Depends on: T-003
- Covers: RQ-002, RQ-003, RQ-013
- Boundaries: BD-004, BD-005, BD-007
- Inputs: 目标形状
- Outputs: 一表一类的读写路径

#### Steps

1. `ICharacterSkillTable` 的每个方法按 `mode` 限定：`ReadLevelsAsync`、`ReadEntryAsync`、`InsertAsync`、`SetLevelAsync`、`RemoveAsync` 全部只作用于 `mode='pve'` 的行。
2. `ICharacterPvpSkillTable` 的两个方法并入同一个接口或另立一个只指名同一张表的类，其读写只作用于 `mode='pvp'` 的行；PvP 读取的排序 `ORDER BY page_index, slot` 保留。
3. `extra_values` 两侧的空值语义统一：两边今天都把 NULL 读成空数组、把空数组写成 NULL，保持不变。
4. 删除被取代的接口与注册。删除任何测试文件前逐条核对其断言证的是不是即将消失的东西。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet build Server/86JP.Server.slnx` | 零错误零警告 |
| CK-007 | 脚本清点 `Server/src/Dfo.Server.Persistence` 下每个类 SQL 中的表名 | 零个类指名超过一张表 |

### T-005: 领域组装与播种序列

- Depends on: T-004
- Covers: RQ-009, RQ-010
- Boundaries: BD-006, BD-008, BD-009
- Inputs: 新的读写路径
- Outputs: 领域记录不变的组装

#### Steps

1. `PvpSkillProfileService` 的读标记—写技能—写标记序列改到新接口，判据仍是 `character_pvp.skills_initialized`。
2. `CharacterRosterService` 的初始技能写入改到新接口，仍写 `mode='pve'`、`page_index=0`。
3. `CharacterEntryAssembly` 的技能读取改到新接口。
4. 两张职业专属页的读取与「非本职业持有即抛」不动，并补一条用例确认它仍然抛。
5. 表征用例的播种改到目标表，**期望输出一字不改**。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | 领域记录文件的 `git diff` | 为空 |
| CK-009 | 表征用例的 `git diff` | 只落在播种语句上，期望值零改动 |
| CK-010 | `dotnet test Server/86JP.Server.slnx -m:1` | 全绿（已知假红除外） |

### T-006: 行为保持证据

- Depends on: T-005
- Covers: RQ-011, RQ-012, RQ-013
- Boundaries: 无新增
- Inputs: 完成后的工作区
- Outputs: 验证阶段可引用的证据

#### Steps

1. 全量套件，记录通过/跳过/失败与用例数变化，逐条解释增减。
2. 可证伪性演示两次：把技能读取的 `mode` 条件去掉（PvE 读取会读到 PvP 的行）→ 表征立刻红；把 `CHECK (mode <> 'pvp' OR ...)` 去掉 → 对应守卫立刻红。两次都还原并确认转绿。
3. 一表一类清点；领域记录定义点清点。
4. **逐条回读本 plan 的每一个 Step，确认没有漏做**——上一块的唯一 A 级缺陷正是漏做了计划里点名的一步。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-011 | `dotnet test Server/86JP.Server.slnx -m:1` | 全绿；红色项仅限已知满载假红且隔离复跑为绿 |
| CK-012 | 两次可证伪性演示 | 均先红后绿 |
| CK-013 | 逐条回读计划的 Step | 每一步都有对应的产物或证据 |
| CK-014 | `git rev-parse HEAD` | 仍为 `1a77ef8a1de23ebb0d6ba283f44ca178b2496953`，未产生任务级提交 |

## Requirement Coverage

| RQ | Tasks |
|---|---|
| RQ-001 | T-001, T-003 |
| RQ-002 | T-001, T-004 |
| RQ-003 | T-001, T-004 |
| RQ-004 | T-001, T-003 |
| RQ-005 | T-001, T-003 |
| RQ-006 | T-001, T-003 |
| RQ-007 | T-001, T-003 |
| RQ-008 | T-002 |
| RQ-009 | T-005 |
| RQ-010 | T-005 |
| RQ-011 | T-006 |
| RQ-012 | T-005, T-006 |
| RQ-013 | T-004, T-006 |

## Risks

| Risk | Mitigation |
|---|---|
| 四条保证在搬运中静默丢失 | T-001 先把它们写成会失败的断言，T-003 的 CK-004 逐条转绿 |
| `UNIQUE` 加到 PvE 行上是一处裁定未讨论的收紧，会在实机建某个职业时才显形 | T-002 先证明掉，证不掉就改成只按 `mode='pvp'` 限定并更新裁定 |
| `mode` 是 `NOT NULL` 且无默认值，可补列清单加不上这样的列 | T-003 第 3 步明确择一并写明理由 |
| 读取漏掉 `mode` 条件，PvE 读到 PvP 的行 | T-006 第 2 步的可证伪性演示正是这一条 |
| 漏做计划里点名的某一步 | T-006 第 4 步：逐条回读 |
