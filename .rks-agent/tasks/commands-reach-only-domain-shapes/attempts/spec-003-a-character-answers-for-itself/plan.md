---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 角色自己回答自己的数

## Implementation Approach

要裁决的那处耦合是：**剩余 SP 的算术换位置之后由谁持有它的三个输入**。

算法今天住在 `SelectCharacterPreparationService.CompleteSkillProjectionAsync`，是一个私有静态
函数，吃 `character`（等级、职业、成长类型）、`entry.Skills`（已学技能页与加成）与规则服务，
吐出 `CharacterSkillProtocolMirrorState`。入场时它的输入全在手边；角色实例要问同一个问题时，
输入得从表里取。

两条容易走的路都不对：
- 让角色实例自己注入三张表，它就从「问什么答什么」变成了一个装配器；
- 让入场准备改去调一个会重新读表的服务，等于在入场时把已经在手的数据再读一遍。

**采用的形状是把算术本身上提到规则服务，让两个调用方各自供料。** 规则服务的定义正是
「内容 + 传入的 facts + 显式种子 → 值，无状态、不查世界」，而这段算术恰好如此：它拿档案给的
SP 表与成本表，加上传进来的等级、已学技能、加成，算出一个数。于是：

```
规则服务（纯算术，唯一实现）
  ├── 入场准备：用手边已有的 entry 供料           —— 行为逐值不变
  └── 角色技能点领域服务：从表取三个输入后供料      —— 角色实例问的就是它
```

一个算法，一处实现，两个供料方。这满足 RQ-002，也避免了入场重复读表。

任务顺序由此固定：先把算术上提（T-001，行为不变，可单独验证），再建供料的领域服务（T-002），
然后让角色实例回答（T-003），最后才改取数点（T-004）。反过来做会在中途留下「一半从实例问、
一半从镜像读」的状态，而那比现状更难说清。

**一处边界决定，明写出来供评审裁决**：入场序列的技能帧仍然从入场镜像取 SP——那份镜像就是
入场那一刻的投影，帧本身就是在报告入场时的状态，这是快照的正当用法。RQ-003 要禁的是**入场
之后**继续拿那份陈值当答案。T-004 的检查按这个读法执行；若评审认为 RQ-003 应按字面理解到
连入场帧也不许读镜像，那是一处边界收紧，届时按评审意见处理。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryPreparationRuleService.cs` | 新增一个纯算术成员：由定义集、等级、已学技能页与加成算出剩余 SP/TP | 现有 `SkillPointsAsync` 的产物 | 唯一的剩余点数算术 |
| BD-002 | `Server/src/Dfo.Server.Application/Characters/CharacterEntryPreparationRuleService.cs` | 实现该成员，逻辑逐行取自现有的私有投影 | BD-001 | 同上 |
| BD-003 | `Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs` | `CompleteSkillProjectionAsync` 改为调用规则服务的新成员，不再自带算术 | BD-002 | 入场结果逐值不变 |
| BD-004 | `Server/src/Dfo.Server.Abstractions/Characters/` 新增角色技能点领域服务接口 | 按角色标识回答剩余 SP/TP | — | 供角色实例委托 |
| BD-005 | `Server/src/Dfo.Server.Application/Characters/` 该服务的实现 | 从角色行、技能表、加成表取三个输入，调规则服务 | BD-002、BD-004 | 现读现算的剩余点数 |
| BD-006 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs` | 新增回答等级、经验、剩余 SP/TP 的成员 | BD-004 | 角色实例能回答自己的数 |
| BD-007 | `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs` | 实现之：等级与经验经既有的经验领域服务读，剩余点数委托 BD-005 | BD-005、BD-006 | 同上 |
| BD-008 | `Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs`、`.../DungeonClear/DungeonClearSlice.cs`、`.../QuestCompletion/FinishQuestHandler.cs`、`.../TutorialLevelUp/TutorialLevelUpSlice.cs` | 四个发经验帧处改向角色实例取剩余 SP/TP | BD-006 | 入场之后不再读陈值 |
| BD-009 | `Server/tests/Dfo.Server.Application.Tests/` 新增用例 | 一条能区分升级前后剩余 SP 的判据 | BD-005 | RQ-004 可证伪 |
| BD-010 | `AGENTS.md` | 记下角色自身的数从角色实例问 | T-001..T-005 | 记载与代码一致 |

## Ordered Tasks

### T-001: 剩余点数的算术只剩一处

- Covers: RQ-002, RQ-005
- Boundaries: BD-001, BD-002, BD-003
- Inputs: `CompleteSkillProjectionAsync` 现有实现
- Outputs: 算术在规则服务里；入场准备成为它的一个供料方

#### Steps

1. 在入场准备规则服务上新增纯算术成员，逻辑**逐行**取自现有私有投影，不做任何等价改写。
2. `CompleteSkillProjectionAsync` 改为供料并调用它。
3. 确认规则服务仍不查世界状态——新成员只吃传进来的 facts 与它已持有的内容源。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-002 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 与基线 2007 一致 |
| CK-003 | Non-command: 对比搬移前后的算术 | 逐行相同，无等价改写 |

### T-002: 一个能按角色标识回答剩余点数的领域服务

- Depends on: T-001
- Covers: RQ-001, RQ-002
- Boundaries: BD-004, BD-005
- Inputs: 角色行（等级、职业、成长类型）、技能表（已学技能页）、加成表
- Outputs: 现读现算的剩余 SP/TP

#### Steps

1. 定义接口：按角色标识回答剩余 SP/TP，不持有跨询问的缓存。
2. 实现：取三个输入，调 T-001 的算术。角色不存在时答不出来而不是答零——零和「没有这个角色」
   是两件事，混在一起会把后者报成前者。
3. 在组合根接线。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-005 | Non-command: 枚举该实现读取的输入 | 三个输入各自来自表，无一来自会话镜像 |

### T-003: 角色实例回答自己的等级、经验与剩余点数

- Depends on: T-002
- Covers: RQ-001
- Boundaries: BD-006, BD-007
- Inputs: 既有的经验领域服务、T-002 的服务
- Outputs: 角色实例的三个新答案

#### Steps

1. 在 `ICharacter` 上新增成员，文档说明它们像其余成员一样问的时候才读。
2. 实现：等级与经验经既有的经验领域服务从角色行读；剩余点数委托 T-002。
3. 不给角色实例增加任何跨询问的缓存字段。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-007 | Non-command: 检视实现 | 无缓存字段；每个成员都现读 |
| CK-008 | 新增用例：改变存储中的值后再问一次 | 第二次的答案反映变化 |

### T-004: 入场之后不再读那份陈值

- Depends on: T-003
- Covers: RQ-003, RQ-004, RQ-005
- Boundaries: BD-008
- Inputs: 四个发经验帧的位置
- Outputs: 经验帧的 SP/TP 来自角色实例

#### Steps

1. 四处改为向角色实例取剩余 SP/TP。
2. 保留各自原有的「取不到就不发/拒绝」语义——取不到的处理方式不变，只是取数来源变了。
3. 枚举 `Skills.ProtocolMirror` 与会话 `Progression` 的全部读取点，逐个说明去向：入场序列的
   帧仍读镜像（那是入场投影的正当用法，见实现路线一节的边界决定），其余必须已改。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-009 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-010 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过；改动的期望值逐条对应到剩余 SP 那一处变化 |
| CK-011 | Non-command: 镜像与会话进度字段的读取点枚举 | 镜像：除入场序列外无残留。会话等级：仍有读取点，但每一处都要说明它为何不陈（契约 revision 2 把 C-2 由「这三个数」重述为「会变陈的那个数」，理由见契约的修订一节） |
| CK-012 | `dotnet test --filter "FullyQualifiedName~CommandHandlerArchitectureTests"` | 通过；判定谓词与豁免清单 diff 为空 |

### T-005: 那条判据在实现被改坏时会红

- Depends on: T-004
- Covers: RQ-004
- Boundaries: BD-009
- Inputs: T-002 的服务
- Outputs: 一条区分升级前后的用例

#### Steps

1. 加一条用例：同一个角色在两个等级上问剩余 SP，断言两次答案不同且各自等于按该等级算出的值。
2. 做一次变异——把重算改成返回入场时的值——确认该用例失败；还原后确认恢复通过。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-013 | 新用例 | 通过 |
| CK-014 | Non-command: 变异验证 | 改坏时失败，还原后通过 |

### T-006: 仓库约定说出角色自身的数从哪里问

- Depends on: T-005
- Covers: RQ-007
- Boundaries: BD-010
- Inputs: 前五个任务的实际结果
- Outputs: 与代码一致的记载

#### Steps

1. 在 `AGENTS.md` 写入：角色自身的数从角色实例问，不从会话快照读；并说明会话快照此后对这三个
   数不再是取数来源。
2. 如实记下仍留在会话里的那些部分（坐标、世界内容、背包投影），以免读者以为聚合已经拆完。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-015 | Non-command: 记载与代码逐条核对 | 无与代码矛盾的陈述 |
| CK-016 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 与 CK-010 同一结果 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-002, T-003 | BD-004, BD-005, BD-006, BD-007 | CK-007, CK-008 |
| RQ-002 | T-001, T-002 | BD-001, BD-002, BD-003 | CK-003, CK-005 |
| RQ-003 | T-004 | BD-008 | CK-011 |
| RQ-004 | T-004, T-005 | BD-008, BD-009 | CK-013, CK-014 |
| RQ-005 | T-001, T-004 | BD-003, BD-008 | CK-002, CK-010 |
| RQ-006 | T-004 | BD-008 | CK-012 |
| RQ-007 | T-006 | BD-010 | CK-015 |

## Risks and Rollback

- Risk：这一批失去了「判据数字不变」这道护栏。缓解：CK-010 要求改动的期望值**逐条归因**到
  被授权的那一处变化，任何归不掉的改动都是夹带。
- Risk：剩余 SP 从三处输入现算，任何一处取错都会得到一个看起来合理的数字。缓解：CK-003 要求
  算术逐行搬移而非等价改写；CK-005 枚举三个输入的来源；CK-014 用变异证明判据会红。
- Rollback：未提交前可由根控制器按精确工作流回滚还原到 `68a12119`。

## Assumptions

- 剩余点数的三个输入都能从表取到：等级与职业成长类型在角色行，已学技能在技能表，加成在入场表
- 等级与经验目前是对的，归位是为消除「同一个数两处可读」，不是修现存缺陷
