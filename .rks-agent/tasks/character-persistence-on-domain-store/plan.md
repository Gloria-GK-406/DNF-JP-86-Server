---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 人物持久化收敛到统一存储协议

## Implementation Approach

Spec 的核心成果是「同一数据库上人物改动与物品改动在一次用例内共同成败，且业务代码既不书写事务也不因载体分支」。必要支撑是数据动词表达、三处规则外迁、事务外写抛出、建表归属明确；外围保证是零命中核对、全量绿、帧字节与实机走查。

最早的核心切片是 T-001：把载体协议从物品存储里提出来，并且**从提出的那一刻就按数据库而非按 store 归并参与者**。这条不能推迟。现有协调器按 identity 去重且只允许一个提交可失败的参与者，若协议按「一个 store 一个参与者」提取，人物存储接入时 RQ-001 直接不可达，而那时协议已被物品侧消费，返工面覆盖全部已改代码。T-001 因此在只有物品一个聚合时就用一个测试专用的第二种会话类型证明「同一数据库、两种具类型会话、一个参与者」。

T-002 是唯一排在核心工作之前的支撑任务，理由是安全而非便利：契约与 Spec 都要求规则迁出前先有覆盖，而覆盖是否足够今天并不确定（金币上限等级的解析没有以其名出现的用例）。在覆盖未定的情况下搬规则，行为改变是静默的，全量测试照样绿。T-002 只判定与补齐，不改任何生产行为。

其后按消费顺序推进：T-003 迁出三处规则（T-004 的会话化会消费「已无规则的仓储」），T-004 落地人物存储与会话，T-005 收掉协议层泄漏，T-006 集中建表归属，T-007 汇总行为保持证据。

T-004 是危险的一步，必须保持为一个任务。把「写入改走会话」与「用例边界提供原子性」拆成两次交付，会留下一个测试看不见的原子性空洞——中间态下写入已经分散成多次暂存却没有共同落定，而既有用例不覆盖原子性，照样全绿。本计划不提供把它们分开的排法。

T-006 同样自带一条硬次序：先用表征测试钉住「今天真正生效的列集」，再集中化。一张表今天的有效列集是「首个运行的建表语句的列集，并上所有运行过的补列」，取决于组件构造顺序；不先测出来就集中化，等于用六张多定义表中任取一处定义去替换一个未知的并集。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Transactions/` | 新增与聚合无关的载体协议；既有参与者协议仅在必要时补充 | 现有参与者协议 | 载体与会话的通用契约 |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Inventory/IItemStore.cs` | 改为载体协议的具名实例，对外形状不变 | BD-001 | 物品会话契约 |
| BD-003 | `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteItemStore.cs` | 参与者改按数据库归并，物品会话成为其一个面 | BD-001, BD-002 | 按数据库入伙的持久参与者 |
| BD-004 | `Server/src/Dfo.Server.Application/Inventory/MemoryItemStore.cs` | 随协议调整，易失语义不变 | BD-001, BD-002 | 易失物品会话 |
| BD-005 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryStateServices.cs` | 移除三个业务动词；加入人物载体与会话契约 | BD-001 | 人物持久化契约 |
| BD-006 | `Server/src/Dfo.Server.Infrastructure/Characters/` | 三处规则的实现外迁；人物持久会话并入数据库参与者；移除自建表 | BD-003, BD-005 | 人物持久实现 |
| BD-007 | `Server/src/Dfo.Server.Application/Characters/` | 承接外迁的三处规则；调用点改用会话 | BD-005, BD-006 | 规则与用例逻辑 |
| BD-008 | `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs` | 移除对人物持久化契约的直接依赖 | BD-007 | 协议层命令处理器 |
| BD-009 | `Server/src/Dfo.Server.Infrastructure/Schema/` | 新增唯一的建表归属者，按并集声明全部表 | T-006 的表征结论 | 集中的建表 |
| BD-010 | `Server/src/Dfo.Server.Infrastructure/Accounts/`, `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteInventoryEntryRepository.cs`, `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteEntryInventoryMutationRepository.cs` | 仅移除建表与补列副作用，读写路径不改 | BD-009 | 无建表语句的仓储 |
| BD-011 | `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`, `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 注册新契约与建表归属者 | BD-006, BD-007, BD-009 | 组合根 |
| BD-012 | `Server/tests/` | 新增与机械调整用例 | 各任务 | 自动化证据 |

## Ordered Tasks

### T-001: 载体协议按数据库归并参与者

- Covers: RQ-002, RQ-003
- Boundaries: BD-001, BD-002, BD-003, BD-004, BD-012
- Inputs: 现有物品存储与参与者协议
- Outputs: 与聚合无关的载体协议；物品侧成为其实例且对外行为不变

#### Steps

1. 先写失败用例：同一数据库上取两种不同的具类型会话，断言事务中的参与者恰为一个，且两者的写在同一次落定中共同生效。第二种会话类型只存在于测试中。
2. 先写失败用例：两个不同数据库各自的持久会话同时参与时以失败暴露。
3. 从物品存储中提取载体协议：载体身份、是否持久、以事务换取会话；会话的读写形状保持「按位置读、按位置写、按位置清空」。
4. 持久侧的参与者改按数据库身份归并，物品会话成为该参与者对外的一个面；易失侧同步调整，语义不变。
5. 全量回归，确认物品侧对外行为与既有用例语义未变。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~Transactions` | 新增的单参与者与多数据库用例通过 |
| CK-002 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~ItemStore` | 既有物品存储用例全部通过且语义未改 |
| CK-003 | `dotnet test Server/86JP.Server.slnx -m:1` | 全量不低于基线 944 通过 / 2 跳过 / 0 失败 |

### T-002: 判定并补齐待外迁行为的覆盖

- Depends on: T-001
- Covers: RQ-008
- Boundaries: BD-012
- Inputs: 三处规则的现有实现与现有用例
- Outputs: 覆盖充分性结论；不足处补齐的用例，断言当前行为

#### Steps

1. 逐处判定覆盖是否钉住了当前行为：过期专家契约的技能上限回收、地下城持久效果的恢复与经验发放、金币上限等级的解析。判定依据是「若把该规则改错，是否有用例会红」。
2. 对判定为不足的处，补齐用例，断言当前行为，不改任何生产代码。
3. 把逐处结论写入任务记录，包含判定为充分者的依据。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | Non-command: 逐处记录「改错该规则会红的用例」，三处均有指名 | 三处结论齐备，不足处已补齐 |
| CK-005 | `dotnet test Server/86JP.Server.slnx -m:1` | 全量绿，通过数不低于 T-001 结束时 |

### T-003: 三处规则迁出持久实现

- Depends on: T-002
- Covers: RQ-008
- Boundaries: BD-006, BD-007, BD-011, BD-012
- Inputs: T-002 的覆盖结论
- Outputs: 三处规则位于业务层，可在不涉及数据库的情况下检验

#### Steps

1. 把过期专家契约的技能上限判定迁出，持久侧只留读与写。
2. 把地下城持久效果的可提交判定与经验发放迁出，持久侧只留读、写与状态推进。
3. 把金币上限等级的解析迁出读仓储。
4. 为三处各补一组不触及数据库的规则用例。
5. 组合根随之调整。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~Characters` | 三处的无数据库规则用例通过 |
| CK-007 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~EntryLifecycleRecovery` | 两处的既有基础设施覆盖仍然通过 |
| CK-008 | `dotnet test Server/86JP.Server.slnx -m:1` | 全量绿 |

### T-004: 人物存储与会话，写入改走会话

- Depends on: T-001, T-003
- Covers: RQ-001, RQ-004, RQ-005, RQ-006, RQ-007, RQ-011
- Boundaries: BD-005, BD-006, BD-007, BD-011, BD-012
- Inputs: T-001 的载体协议；T-003 后无规则的人物持久实现
- Outputs: 人物持久会话并入数据库参与者；三个业务动词消失；事务外写抛出

#### Steps

1. 先写失败用例：一个用例同时改动物品槽位与人物状态，正常结束时两者可见，其中一处在准备阶段失败时两者均不可见。
2. 先写失败用例：事务边界之外改动持久人物状态抛出；边界之外读取正常完成。
3. 定义人物会话，动词只有按位置读、按位置写、按位置清空；位置由归属者、段与键确定。
4. 把那 10 处写入逐处改由会话表达，逐处记录表达结论；表达不出者记录事实，不引入业务动词或旁路。
5. 移除三个业务动词及其实现，调用点改用会话与业务层逻辑。
6. 补一组同一逻辑分别作用于持久与易失载体的对照用例。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-009 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~CharacterStore` | 跨人物与物品的共同成败用例通过 |
| CK-010 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~OutsideTransaction` | 边界外写抛出、边界外读成功 |
| CK-011 | `grep -rn "UpdateSeekingQuestTriggersAsync" Server/src --include=*.cs` | 零命中；另两个业务动词同样零命中 |
| CK-012 | Non-command: 10 处写入的表达结论逐处记录，含表达不出者的处置 | 结论齐备 |
| CK-013 | `dotnet test Server/86JP.Server.slnx -m:1` | 全量绿 |

### T-005: 协议层不再直接持有人物持久化契约

- Depends on: T-004
- Covers: RQ-010
- Boundaries: BD-008, BD-012
- Inputs: T-004 的业务层入口
- Outputs: 协议层依赖收敛

#### Steps

1. 把协议层直接注入的人物持久化契约替换为业务层入口。
2. 既有用例只作因依赖变化的机械调整。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-014 | `grep -rn "ICharacterEntryMutationRepository" Server/src/Dfo.Server.Protocol --include=*.cs` | 零命中 |
| CK-015 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests -m:1` | 协议用例全部通过 |

### T-006: 建表归属集中化

- Depends on: T-004
- Covers: RQ-009
- Boundaries: BD-009, BD-010, BD-011, BD-012
- Inputs: 现有全部建表与补列语句
- Outputs: 每张表一处定义；建表不再是读写组件的构造副作用

#### Steps

1. 先写表征用例：以今天的构造方式建库，导出每张表的实际列集，作为期望值钉住。这一步的产出是集中化的唯一依据。
2. 把全部建表按该并集集中到唯一的建表归属者，六张多定义表以并集为准而非任取一处。
3. 从各读写组件移除建表与补列副作用。
4. 补用例：只构造单个读写组件即可工作；表的列集与构造顺序无关。
5. 按表名统计定义处数。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-016 | Non-command: 集中化前后的表征列集逐表比对 | 每张表列集逐列一致 |
| CK-017 | `grep -rn "CREATE TABLE" Server/src/Dfo.Server.Infrastructure --include=*.cs` | 仅命中建表归属者所在路径；补列与表结构探查同样仅命中该路径 |
| CK-018 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1` | 含构造顺序无关性用例在内全部通过 |
| CK-019 | `dotnet test Server/86JP.Server.slnx -m:1` | 全量绿 |

### T-007: 行为保持证据

- Depends on: T-005, T-006
- Covers: RQ-012
- Boundaries: BD-012
- Inputs: 全部前序产出
- Outputs: 帧字节比对、全量结果与实机走查记录

#### Steps

1. 比对相关协议的编码字节与时序。
2. 跑全量并记录通过数与跳过数。
3. 实机走查角色选择、进入频道、拖拽道具、使用消耗品。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-020 | Non-command: 相关协议的帧字节与改动前逐字比对 | 逐字一致 |
| CK-021 | `dotnet test Server/86JP.Server.slnx -m:1` | 通过数不低于 944 加新增用例，跳过 2，失败 0 |
| CK-022 | Non-command: 实机走查四条路径 | 表现与改动前一致 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-004 | BD-005, BD-006 | CK-009, CK-013 |
| RQ-002 | T-001 | BD-001, BD-003 | CK-001, CK-002 |
| RQ-003 | T-001 | BD-001, BD-003 | CK-001 |
| RQ-004 | T-004 | BD-007 | CK-009 |
| RQ-005 | T-004 | BD-006, BD-007 | CK-009, CK-013 |
| RQ-006 | T-004 | BD-005, BD-006 | CK-011, CK-012 |
| RQ-007 | T-004 | BD-006 | CK-010 |
| RQ-008 | T-002, T-003 | BD-006, BD-007 | CK-004, CK-006, CK-007 |
| RQ-009 | T-006 | BD-009, BD-010 | CK-016, CK-017, CK-018 |
| RQ-010 | T-005 | BD-008 | CK-014, CK-015 |
| RQ-011 | T-004 | BD-005, BD-006 | CK-012 |
| RQ-012 | T-007 | BD-012 | CK-020, CK-021, CK-022 |

## Risks and Rollback

- Risk: 载体协议若按「一个 store 一个参与者」提取，人物存储接入时 RQ-001 不可达，而返工面已覆盖全部已改代码。缓解：T-001 在只有物品一个聚合时就以测试专用的第二种会话类型证明单参与者，失败即在最小面上暴露。
- Risk: T-004 若被拆成「写入会话化」与「用例提供原子性」两次交付，中间态存在测试看不见的原子性空洞。缓解：本计划不提供把它们分开的排法，且 T-004 的前两步是先写失败用例。
- Risk: 建表集中化以任一处定义替换未知的并集，后果是远离改动点的运行期失败。缓解：T-006 第一步先用表征用例钉住今天生效的列集，CK-016 逐表比对。
- Risk: 规则外迁在覆盖不足时进行，行为改变静默。缓解：T-002 前置判定，不足则补齐后再迁。
- Rollback: 全程不产生任务级提交。任一任务失败时，把该任务触及的路径恢复到接受基线 712bdbc，保留任务记录作为证据。

## Assumptions

- 「位置与值」足以表达那 10 处人物写入。T-004 步骤 4 逐处核对；若某处表达不出，按 Spec 的 RQ-011 记录事实并回契约层重议，不在实施阶段引入业务动词或旁路。
- 那 21 张已建表但无写入的人物表在本范围内无需写入能力，只参与 T-006。
- 集中化后读写组件不再于构造时建表，不会破坏既有测试夹具。上一轮已出现过夹具依赖构造副作用的先例，故 T-006 步骤 4 专门覆盖这一点。
- 三处规则的调用时序不因外迁而改变，故对外可观察行为不受影响。
