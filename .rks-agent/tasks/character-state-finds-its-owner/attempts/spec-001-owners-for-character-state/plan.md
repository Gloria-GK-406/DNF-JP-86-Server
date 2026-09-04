---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 角色状态各自找到主人

## Implementation Approach

### 一处必须先说清的事实：哪几样有生产消费方，哪几样没有

这四样状态今天的读取点分布很不一样，而这直接决定了每一项的实际价值。**先摆出来，不要让评审
自己去发现**：

| 状态 | 入场序列之外有生产消费方吗 | 因此本计划做出的东西 |
|---|---|---|
| 身份 | **有，而且它正在违反 RQ-002** | 实例回答身份，那一处改为问自己 |
| 坐标 | **有，8 处**（6 读 + 2 改） | 领域服务持有并回答，8 处全部改指 |
| 任务 | **有，1 处** | 那一处改问已有的任务簿实例 |
| 设置 | **没有**。只有入场序列读，且全仓无写回通路 | 领域服务能答，判据对着真库校验 |
| 连招 | **没有**。只有入场序列读，且全仓无写入方 | 领域 store + 实例，判据对着真库校验 |

后两样是所有者明确要求的归位（设置用领域服务、连招用 store），它们今天没有生产调用方是事实，
不是本计划的疏漏。**它们不是「加了接口没接线」**：两者都有真实的表与真实的行，实例能把行读出来，
判据对着真库验证它答得对。它们缺的是调用方，而调用方要等改键与连招编辑那两个协议做进来。
这一点写在这里供评审裁决，若评审认为无消费方的归位应当推迟，那是一次范围收窄，按评审流程处理。

### 身份这一项的实际内容，比「加几个成员」大

`PlayingCharacter` 对会话运行态聚合的依赖收敛在一个私有方法上，而那个方法只服务于一件事——
判断道具可用性时解析角色的职业族。也就是说**角色实例正在向会话要自己的职业**。同一个表达式里
的等级，上一轮已经改成问自己了，职业被留了下来。

所以身份归位会连带产生一个结构结果：那个私有方法退役之后，角色实例对会话运行态聚合的依赖整个
消失。仓库把未读的主构造器参数当错误，所以如果那个依赖因此无人读，编译器会强制把它摘掉——
这不需要本计划额外安排，但**必须预期到**，否则会被当成意外。

### 入场路径一律不动，这是一条贯穿全篇的决定

入场编解码器是一组同步的纯字节写入函数；入场投影的构建也是同步的纯函数，吃存储记录吐快照。
让它们中的任何一个去问一个会读世界的实例，就是把纯函数变成世界查询——那比现状差，而且会给
入场路径增加每帧一次的数据库往返。

因此：**新的主人从表里读，入场路径继续从它手里已有的存储记录读。** 两者读的是同一批行，值相同。
Spec 的 Definitions 已经把「入场序列读入场投影」定为正当用法，这条决定与之一致。

### 任务顺序的依据

先做有真实消费方、且能消除真实违规的三样（身份、坐标、任务读点）——它们是核心成果里最先能
安全交付的切片，且各自独立。再做两样所有者要求但今天无消费方的归位（设置、连招），把连招那个
最大的一件放在有真实价值的工作之后，万一它在事务接线上触发了路由决策里写明的 reroute 条件，
前面的成果已经落地。删残留紧跟连招之后——先有新家再拆旧的。疲劳的接缝与它的变异验证放在核心
搬迁之后，因为核心工作不消费它。约定记载最后，它要如实记下前面每一项**实际**做到哪一步。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs` | 新增回答身份的成员 | 既有的 `ProgressAsync` 形状 | 实例能答身份 |
| BD-002 | `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs` | 实现身份成员从角色行现读；道具可用性改为问自己；退役那个读会话聚合的私有方法及随之无人读的依赖 | BD-001 | 实例不再读会话聚合 |
| BD-003 | `Server/src/Dfo.Server.Abstractions/Characters/` 新增坐标领域服务接口 | 按角色回答与更新坐标 | — | 坐标的主人 |
| BD-004 | `Server/src/Dfo.Server.Application/Characters/` 或 `Sessions/` 该服务的实现 | 持有活坐标并回答；落库时机仍挂在会话生命周期上 | BD-003 | 同上 |
| BD-005 | `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs`、`.../TownProjection/TownReturnProjection.cs`、`.../DungeonSelection/DungeonSelectionSlice.cs` | 6 处读与 2 处整体写回改为经坐标领域服务 | BD-004 | 聚合不再是坐标的取数与写回位置 |
| BD-006 | `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` | 入场之后那一处任务读点改问任务簿实例；进本处新增疲劳消耗报告 | 既有 `ICharacterQuestLogDomainService`；BD-013 | 任务读点归位；疲劳被真实调用 |
| BD-007 | `Server/src/Dfo.Server.Abstractions/Characters/` 新增设置领域服务接口 | 按角色回答按键与选项设置 | — | 设置的主人 |
| BD-008 | `Server/src/Dfo.Server.Application/Characters/` 该服务的实现 | 从既有的热键表与设置表读 | BD-007 | 同上 |
| BD-009 | `Server/src/Dfo.Server.Abstractions/Characters/` 新增连招领域 store 接口与其 session 接口 | 按页号寻址的事务结算真源 | 既有 `IQuestDomainStore` 的形状 | 连招的 store |
| BD-010 | `Server/src/Dfo.Server.Persistence/Characters/` 该 store 的实现与组合根接线 | 建在既有的连招页行网关之上；接进事务参与者 | BD-009 | 同上 |
| BD-011 | `Server/src/Dfo.Server.Abstractions/` 与 `Application/` 的连招实例 | 按页号回答一个角色的全部连招页 | BD-009, BD-010 | 连招的实例 |
| BD-012 | `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs`、`Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs` | 退役角色活动态上那个恒 null 的单数连招字段及其类型 | BD-011 | 残留消失 |
| BD-013 | `Server/src/Dfo.Server.Abstractions/Characters/` 新增疲劳接缝；`Application/Characters/` 其不记账的实现 | 回答总量、已用量、剩余量；接受用掉与退回两种报告 | — | 疲劳的接缝 |
| BD-014 | `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonEntry/` 新增用例 | 断言进本处向疲劳接缝报告了消耗且数对 | BD-006, BD-013 | 会红的判据 |
| BD-015 | `Server/tests/Dfo.Server.Application.Tests/Characters/` 新增用例 | 身份现读、坐标归属、设置答案、连招按页回答 | BD-002, BD-004, BD-008, BD-011 | 各项的判据 |
| BD-016 | `AGENTS.md` | 记下这几样状态各自归谁，并如实写下聚合仍在、疲劳未记账 | T-001..T-008 的实际结果 | 记载与代码一致 |

## Ordered Tasks

### T-001: 角色实例回答自己的身份，并停止向会话要它

- Covers: RQ-001, RQ-002
- Boundaries: BD-001, BD-002, BD-015
- Inputs: 既有 `ProgressAsync` 的现读形状；道具可用性判定处那一次职业读取
- Outputs: 实例能答身份；实例不再读会话运行态聚合

#### Steps

1. 在角色实例接口上新增回答身份的成员，文档说明它像其余成员一样问的时候才读。
2. 实现从角色行现读，形状对照既有的进度成员，不加任何跨询问的缓存。
3. 把道具可用性判定处解析职业族的那一次读取改为问自己。
4. 退役那个从会话运行态聚合取角色活动态的私有方法。**预期连带结果**：该实例对会话运行态聚合的
   依赖若因此无人读，未读的主构造器参数会成为编译错误，届时把它一并摘掉。
5. 枚举身份的全部读取点，逐个归类为「入场序列内」或「经实例」。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-002 | 新增用例：改变存储中的身份后再问一次 | 第二次的答案反映变化 |
| CK-003 | Non-command: 身份读取点枚举 | 每一处或在入场序列内，或经实例；无第三种 |
| CK-004 | Non-command: 检视实例实现 | 无跨询问缓存；不再引用会话运行态聚合 |
| CK-005 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-002: 坐标有一个专门的主人

- Depends on: T-001
- Covers: RQ-003, RQ-004
- Boundaries: BD-003, BD-004, BD-005, BD-015
- Inputs: 坐标的 6 处读与 2 处整体写回；既有的坐标落库通路
- Outputs: 坐标经领域服务取与改；聚合不再是它的位置

#### Steps

1. 定义坐标领域服务接口：按角色回答坐标，并接受一次移动。
2. 实现之。落库时机**不改**——仍挂在会话生命周期上，理由见既有落库接缝的说明。
3. 6 处读改为向该服务提问；2 处整体写回改为向该服务报告移动。
4. 确认角色实例上没有回答坐标的成员。
5. 枚举坐标的全部读取点与全部改变点。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-007 | Non-command: 坐标读取点与改变点枚举 | 无一处从会话运行态聚合取坐标或靠整体写回它来改坐标 |
| CK-008 | Non-command: 角色实例成员清单 | 没有回答坐标的成员 |
| CK-009 | 新增用例：报告一次移动后再问坐标 | 答案是移动后的值 |
| CK-010 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过；换地图与走动的既有用例期望值未改 |

### T-003: 入场之后的那一处任务读点改问任务簿

- Depends on: T-002
- Covers: RQ-008
- Boundaries: BD-006
- Inputs: 既有的任务簿领域服务与实例
- Outputs: 那一处不再读入场投影

#### Steps

1. 把进本切片里那一处入场之后的任务状态读取改为向任务簿实例提问。
2. 保留该处原有的取不到时的语义不变——只换取数来源。
3. 确认本成果没有为任务新增任何类型。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-011 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-012 | Non-command: 本成果新增的任务相关类型清单 | 为空 |
| CK-013 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-004: 设置有一个能回答它的领域服务

- Depends on: T-003
- Covers: RQ-005
- Boundaries: BD-007, BD-008, BD-015
- Inputs: 既有的热键表与设置表
- Outputs: 设置的主人

#### Steps

1. 定义设置领域服务接口：按角色回答按键与选项设置。
2. 实现之，从既有的两张表读。
3. **不改入场路径**——入场序列继续从它手里已有的存储记录读，理由见实现路线一节。
4. 在组合根接线。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-014 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-015 | 新增用例：对着真库写入设置行后向该服务提问 | 答案与库中的行一致 |
| CK-016 | Non-command: 该服务今天的生产调用方 | 记录实际数目（预期为零），并在结果里如实报出 |
| CK-017 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-005: 连招页有一个领域 store 与一个实例

- Depends on: T-004
- Covers: RQ-006, RQ-014
- Boundaries: BD-009, BD-010, BD-011, BD-015
- Inputs: 既有的连招页行网关；既有任务领域 store 的形状
- Outputs: 连招的 store 与实例

#### Steps

1. 定义连招领域 store 接口与其 session 接口，按页号寻址，形状对照既有的任务领域 store。
2. 在既有的连招页行网关之上实现它，并接进事务参与者。
3. 定义并实现连招实例：按页号回答一个角色的全部连招页。
4. 命名必须让 `store` 一词仍只指事务结算的真源；实例不得叫 store。
5. **确认事务参与者的结算顺序未因此改变。** 若它改变了别的 store 的结算顺序，这是路由决策里写明
   的 reroute 条件，停下并向父控制器报告，不要硬做完。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-018 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-019 | 新增用例：对着真库写入连招页行后向实例按页号提问 | 答得出来且与行一致 |
| CK-020 | Non-command: 领域层中名字含 `store` 的全部类型逐个分类 | 只有事务结算的真源用这个词 |
| CK-021 | Non-command: 事务参与者的注册与结算顺序对照 | 别的 store 的结算顺序未变 |
| CK-022 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-006: 那个恒为 null 的单数连招字段退役

- Depends on: T-005
- Covers: RQ-007
- Boundaries: BD-012
- Inputs: T-005 的实例
- Outputs: 残留消失

#### Steps

1. 从角色活动态上摘掉那个单数连招字段，并删掉它的类型。
2. 把显式写 `null` 的那一处一并去掉。
3. 全仓查找该标识符确认零处。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-023 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-024 | Non-command: 该标识符的全仓计数 | 0 |
| CK-025 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-007: 疲劳有一个接缝，进本处真的向它报告

- Depends on: T-006
- Covers: RQ-009, RQ-010
- Boundaries: BD-013, BD-006
- Inputs: 进本处；协议上今天报出的固定总量与恒零已用量
- Outputs: 被真实调用的占位接缝

#### Steps

1. 定义疲劳接缝：回答总量、已用量、剩余量；接受「用掉若干」与「退回若干」两种报告。
2. 实现它，**不记账**：回答的值不随报告改变，继续报出协议今天报出的那两个值。类型名与文档必须
   直说它不记账，让读代码的人不必从行为反推。
3. 进本处向它报告本次消耗，报告的数取自该副本的消耗。
4. 不建 store，不建表。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-026 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-027 | Non-command: 检视接缝实现 | 不记账这一点从类型名与文档即可读出 |
| CK-028 | 新增用例：报告一次消耗后再问已用量 | 答案未变 |
| CK-029 | 新增用例：进本时对着记录式替身断言报告发生且数对 | 通过 |
| CK-030 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过；疲劳两个字段在帧上的值与基线相同 |

### T-008: 证明那次疲劳报告被改坏时判据会红

- Depends on: T-007
- Covers: RQ-011
- Boundaries: BD-014
- Inputs: T-007 的调用点与判据
- Outputs: 变异验证记录

#### Steps

1. 做两次变异：一次移除进本处那次报告，一次把报告的数改错。
2. 每次都记下**哪一条判据**失败，而不只是「判据红了」。
3. 还原后确认恢复通过。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-031 | Non-command: 变异一（移除调用） | 指名的判据失败 |
| CK-032 | Non-command: 变异二（数改错） | 指名的判据失败 |
| CK-033 | Non-command: 还原后复跑 | 恢复通过 |

### T-009: 仓库约定如实记下每一项做到哪一步

- Depends on: T-008
- Covers: RQ-012, RQ-013, RQ-015
- Boundaries: BD-016
- Inputs: T-001..T-008 的实际结果
- Outputs: 与代码一致的记载；收口证据

#### Steps

1. 在仓库约定里写下这几样状态各自归谁。
2. **如实写下没做到的部分**：会话运行态聚合仍然存在及其原因；疲劳有接缝但不记账；设置与连招
   今天没有生产调用方。
3. 收口：跑全量判据；复核注入面判据的判定规则与豁免清单未被放宽；把测试改动中每一处改动的
   期望值逐条归因。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-034 | Non-command: 记载与代码逐条核对 | 无一句比代码强 |
| CK-035 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |
| CK-036 | `dotnet test --filter "FullyQualifiedName~CommandHandlerArchitectureTests"` | 通过；判定谓词与豁免清单 diff 为空 |
| CK-037 | Non-command: `Server/tests` 的 diff 审查 | 凡改动的期望值都能归因到类型改名，无一处是行为差异 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001 | BD-001, BD-002 | CK-002, CK-004 |
| RQ-002 | T-001 | BD-002 | CK-003, CK-004 |
| RQ-003 | T-002 | BD-003, BD-004 | CK-008, CK-009 |
| RQ-004 | T-002 | BD-005 | CK-007 |
| RQ-005 | T-004 | BD-007, BD-008 | CK-015, CK-016 |
| RQ-006 | T-005 | BD-009, BD-010, BD-011 | CK-019 |
| RQ-007 | T-006 | BD-012 | CK-024 |
| RQ-008 | T-003 | BD-006 | CK-012 |
| RQ-009 | T-007 | BD-013, BD-006 | CK-029 |
| RQ-010 | T-007 | BD-013 | CK-027, CK-028 |
| RQ-011 | T-008 | BD-014 | CK-031, CK-032, CK-033 |
| RQ-012 | T-009 | BD-016 | CK-035, CK-037 |
| RQ-013 | T-009 | BD-016 | CK-036 |
| RQ-014 | T-005 | BD-009, BD-011 | CK-020 |
| RQ-015 | T-009 | BD-016 | CK-034 |

## Risks and Rollback

- Risk：连招 store 的事务参与者接线改变别的 store 的结算顺序。缓解：CK-021 专门对照它；若确实
  改变，那是路由决策写明的 reroute 条件，停下报告而不是硬做完。
- Risk：设置与连招今天没有生产调用方，评审可能认为归位应当推迟。缓解：CK-016 要求把实际数目
  如实报出而不是掩盖；两者都有对着真库的判据，不是无判据的接口。
- Risk：身份归位连带摘掉实例对会话聚合的依赖，可能牵出未预期的编译错误链。缓解：T-001 的步骤 4
  已把它写成预期结果而不是意外；仓库把未读的主构造器参数当错误，正好把范围逼出来。
- Risk：坐标的落库时机被无意改动。缓解：T-002 步骤 2 明写不改；CK-010 要求换地图与走动的既有
  用例期望值不动。
- Rollback：未提交前由父控制器按精确工作流回滚还原到 `427331bc`。

## Assumptions

- 判据命令必须带档案路径环境变量，否则读档案的判据静默跳过而不是报红
- 基线自带两个偶发失败，见 Spec 的 Assumptions
- 连招页在库里已有按页号寻址的行网关，新 store 建在它之上，不新建表
- 入场编解码器与入场投影都是同步纯函数，本计划一律不动它们
