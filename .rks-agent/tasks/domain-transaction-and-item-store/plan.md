---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 领域事务与物品存储抽象

## Implementation Approach

Spec 的核心成果是「写业务的人不再感知持久化」，其最早可独立成立的切片不是存储，而是**保证本身**：事务协调器加用例边界，可以只用测试用参与者证明「全有或全无」与「提交阶段最多一个可失败参与者」，此时一行存储代码都还没动。因此 T-001 是第一个核心任务，也没有任何任务排在它之前。

排序的第二个决定由一条真实的危险给出。`ApplyMainMoveAsync` 今天是一次原子写；改造后移动要拆成两次暂存写。若在事务可用之前先拆，中间态破坏原子性，而既有用例不覆盖原子性、全套测试照样全绿，评审也发现不了。因此**拆掉复合写与两条协议进入事务必须落在同一个任务里**（T-003），且严格晚于 T-001 与 T-002。任何把「容器改用存储」与「命令标记为事务型」分成两步的排法都被本计划显式排除。

其余次序按同一条判据：存储抽象先于容器改造（T-002 → T-003）；领域侧存储行标识的消除是一次跨文件的词汇替换，集中在 T-004 一步完成而不是分散；每日刷新与教学奖励的规则外迁触及行为敏感的入场路径，因此先有一个真正会产出结论的覆盖充分性任务（T-005）再动归属（T-006）。两条前提的处置各自落成会产出结论的任务：寻址充分性在 T-002 的检查中被证明或证伪（证伪即停并回契约层），`SlotsChanged` 的可达性在 T-007 单独结论。行为保持的整体证据（帧字节、全套测试、实机）放在 T-008，位于接受之前而不是核心工作之前。

实现取向取最小：不引入 `System.Transactions`（实测确认现用数据库客户端不参与环境事务且不报错），只照抄其「准备 / 提交 / 回滚」的形状；事务边界落在既有命令管道上，填掉既有的 `CommandExecutionKind.Transactional` 空槽，不新建调度机制；存储抽象只承担机制，寻址词汇为「容器与槽」。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Transactions/` | 新增：参与者契约与领域事务句柄 | Spec RQ-001/RQ-004/RQ-005 | 参与者接口、事务句柄、登记契约 |
| BD-002 | `Server/src/Dfo.Server.Application/Transactions/` | 新增：协调器与环境事务访问 | BD-001 | 三段提交协调器、登记去重与单一可失败参与者判定 |
| BD-003 | `Server/src/Dfo.Server.Application/Commands/Pipeline/` | 新增工作单元 behavior；`CommandExecutionPolicyBehavior.cs` 的 Transactional 分支由抛出改为放行 | BD-002 | 用例边界上的开启与提交，事务型命令可执行 |
| BD-004 | `Server/src/Dfo.Server.Abstractions/Inventory/IItemStore.cs` | 新增：物品存储抽象与会话，寻址为容器与槽 | BD-001 | 存储身份、持久性声明、会话读写契约 |
| BD-005 | `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteItemStore.cs` | 新增：持久实现，复用既有表，不建任何新表 | BD-004 | 持久参与者 |
| BD-006 | `Server/src/Dfo.Server.Application/Inventory/MemoryItemStore.cs` | 新增：仅存内存实现 | BD-004 | 仅存内存参与者 |
| BD-007 | `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` | 容器改由存储承载；复合写拆为基本读写；移除伪造标识与存储枚举映射 | BD-004, BD-005, BD-006 | 与载体无关的容器实现 |
| BD-008 | `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` | 移除存储行标识与移动计划类型，寻址改为容器与槽 | BD-007 | 不含行标识的容器契约 |
| BD-009 | `Server/src/Dfo.Server.Abstractions/Inventory/IItemUseEffects.cs`、`Server/src/Dfo.Server.Application/Inventory/ItemUseEffectDomainService.cs` | 消耗效果的寻址改为容器与槽 | BD-008 | 不含行标识的效果模型 |
| BD-010 | `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`、`Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs` | 两条命令改为事务型执行；移动的复合计划改为基本读写组合 | BD-003, BD-007 | 在事务保护下运行的两条协议 |
| BD-011 | `Server/src/Dfo.Server.Abstractions/Inventory/IInventoryEntryServices.cs` | 退役物品相关成员与并行类型 | BD-004, BD-013 | 收敛后的存储契约 |
| BD-012 | `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteEntryInventoryMutationRepository.cs` | 迁出业务规则，只留写行 | BD-013 | 不含业务规则的持久化实现 |
| BD-013 | `Server/src/Dfo.Server.Application/Inventory/` 内的每日刷新与放置规则服务 | 新增：日界、领取幂等、叠加或新开格、按段查找空位的归属 | `Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs` | 可脱离数据库检验的规则 |
| BD-014 | `Server/src/Dfo.Server.Protocol/Game/TaggedIdReport/TaggedIdReportHandler.cs` | 改用迁出后的放置规则与存储抽象 | BD-013 | 教学奖励的等价行为 |
| BD-015 | `Server/src/Dfo.Server.Application/ApplicationModule.cs`、`Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`、`Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 注册新构件；两条命令的执行方式改为事务型 | BD-002, BD-005, BD-006, BD-013 | 可运行的组合根 |
| BD-016 | `Server/tests/Dfo.Server.Application.Tests/`、`Server/tests/Dfo.Server.Infrastructure.Tests/`、`Server/tests/Dfo.Server.Protocol.Tests/` | 新增本 Spec 各条要求的用例；既有用例只作因类型或依赖变化的机械调整 | 各任务产出 | 可证伪的覆盖 |

## Ordered Tasks

### T-001: 用例边界提供全有或全无的保证

- Covers: RQ-001, RQ-002, RQ-004
- Boundaries: BD-001, BD-002, BD-003, BD-015, BD-016
- Inputs: 既有命令管道与其 `CommandExecutionKind.Transactional` 空槽
- Outputs: 参与者契约、三段提交协调器、用例边界上的工作单元 behavior

#### Steps

1. 定义参与者契约：准备允许失败且此刻可见状态不变；提交与回滚不允许失败。声明参与者是否持久（其提交是否仍可能失败）。
2. 实现协调器：按参与者身份登记去重；登记第二个持久参与者时抛出；提交按「全体准备 → 持久参与者提交 → 其余参与者提交」三段执行，任一前两段失败即全体回滚。
3. 新增工作单元 behavior，Order 取 60——须严格大于账号闸门的 55 且小于执行策略的 100，使事务整个待在账号粒度的锁内。非事务型命令原样放行。
4. 将 `CommandExecutionPolicyBehavior` 的 Transactional 分支由抛出改为放行。
5. 以两个测试用参与者（一个模拟持久、一个模拟仅存内存）覆盖成功、准备失败、持久提交失败三种走向，以及登记第二个持久参与者。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~Transaction` | 成功走向下两个参与者的改动都可见；准备失败与持久提交失败两种走向下一处也不可见 |
| CK-002 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~Transaction` | 登记第二个持久参与者时抛出，且抛出前未改动任何已登记参与者 |
| CK-003 | `Non-command:` 检查工作单元 behavior 的 Order 取值与既有 behavior 的相对次序 | 60 落在账号闸门 55 与执行策略 100 之间 |
| CK-004 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/86JP.Server.slnx -m:1` | 848 通过 / 2 跳过 / 0 失败的基线加本任务新增用例，无既有用例语义改动 |

### T-002: 物品存储抽象与两种实现，并对寻址充分性下结论

- Depends on: T-001
- Covers: RQ-010
- Boundaries: BD-004, BD-005, BD-006, BD-015, BD-016
- Inputs: T-001 的参与者契约；既有已建立的物品数据表
- Outputs: 以容器与槽寻址的存储会话契约、持久与仅存内存两种实现

#### Steps

1. 定义存储抽象：存储身份用于登记去重，声明自身是否持久，并按用例开出会话；会话是参与者。
2. 定义会话的读写词汇，只以容器与槽寻址，不含任何行标识。
3. 先写出「移动、交换、合并、拆堆、消耗」五种操作各自以该词汇表达的用例，再实现两种存储。任一种无法表达即停止本任务，记录该事实并回到契约层重议，不得引入替代的实例身份。
4. 持久实现复用既有已建立的表，不建任何新表；其读须能看见同一会话内尚未提交的写。
5. 仅存内存实现的提交为不可失败的状态替换，其准备阶段完成全部分配与校验。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~ItemStore` | 五种操作均以容器与槽表达并得到预期结果；同一会话内写后可读 |
| CK-006 | `Non-command:` 在持久实现的源码与其依赖中检索建表语句 | 零命中：本次未新增任何数据表定义 |
| CK-007 | `Non-command:` 记录寻址充分性结论 | 五种操作全部可表达；若有不可表达者，该事实与其操作名被记录且本计划停在此处 |

### T-003: 容器改由存储承载，两条协议进入事务

- Depends on: T-001, T-002
- Covers: RQ-002, RQ-003, RQ-005, RQ-008
- Boundaries: BD-007, BD-008, BD-010, BD-015, BD-016
- Inputs: T-001 的用例边界、T-002 的存储抽象
- Outputs: 与载体无关的容器、在事务保护下运行的两条协议

#### Steps

1. 容器改由存储会话承载读写；移动、交换、合并、拆堆由基本读写组合表达。
2. **在同一任务内**把两条命令的执行方式改为事务型。复合写的拆解与事务保护必须一起落地：先拆后保护会留下一个测试看不见的原子性空洞。
3. 移除容器契约中的移动计划类型与存储枚举映射；持久实现不再接受任何描述「移动」的输入。
4. 持久容器在用例边界之外被改动时抛出；仅存内存的容器不抛。
5. 移动与使用的业务逻辑中不书写任何开启、提交、回滚或事务对象。
6. 既有的移动与使用用例只作因类型或依赖变化的机械调整，语义不改。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~MoveItemSpaceHandlerTests` | 既有移动用例全部通过，且其断言未被改写 |
| CK-009 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~UseStackableHandlerTests` | 既有使用用例全部通过，且其断言未被改写 |
| CK-010 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~ContainerCarrier` | 同一段移动逻辑作用于持久容器与仅存内存的容器，走同一路径且可观察结果相同 |
| CK-011 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~OutsideTransaction` | 用例边界之外改动持久容器物品时抛出；同一操作对仅存内存的容器不抛 |
| CK-012 | `Non-command:` 检索两条命令处理器源码中的事务操作与事务对象 | 零命中 |
| CK-013 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/86JP.Server.slnx -m:1` | 全套通过，无既有用例语义改动 |

### T-004: 消除领域侧的存储行标识

- Depends on: T-003
- Covers: RQ-007
- Boundaries: BD-008, BD-009, BD-016
- Inputs: T-003 之后的容器与协议
- Outputs: 不携带行标识的容器契约与效果模型

#### Steps

1. 一次性完成跨文件的词汇替换：容器契约、效果模型、效果服务与受影响的既有用例中的存储行标识全部改为容器与槽寻址。
2. 仅存内存的容器不再发号任何伪造标识。
3. 核对既有用例只作机械调整。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-014 | `Non-command:` 在 `Server/src/Dfo.Server.Abstractions/Inventory/` 与 `Server/src/Dfo.Server.Application/Inventory/` 检索存储行标识与伪造标识发号 | 零命中 |
| CK-015 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~Inventory` | 全部通过，含仅存内存容器的完整操作用例 |
| CK-016 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/86JP.Server.slnx -m:1` | 全套通过 |

### T-005: 判定每日刷新与教学奖励的等价覆盖是否充分

- Depends on: T-004
- Covers: RQ-006
- Boundaries: BD-016
- Inputs: 每日刷新与教学奖励的既有覆盖
- Outputs: 充分性结论；不足时补齐的覆盖

#### Steps

1. 逐条核对既有覆盖是否分别锁住日界、领取幂等、叠加或新开格、按段查找空位四类判定的可观察结果。
2. 对未被锁住的判定，在归属改变之前补齐用例，用例断言的是当前行为。
3. 记录结论：哪些判定原本已被锁住，哪些是本任务新补的。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-017 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~DailyRefill` | 四类判定各有至少一条会因该判定改变而失败的用例 |
| CK-018 | `Non-command:` 记录充分性结论与补齐清单 | 结论落纸，且每一条新补用例在归属改变之前即为绿 |

### T-006: 业务规则迁出持久化实现

- Depends on: T-005
- Covers: RQ-006
- Boundaries: BD-011, BD-012, BD-013, BD-014, BD-015, BD-016
- Inputs: T-005 的覆盖结论
- Outputs: 可脱离数据库检验的规则；只留写行的持久化实现

#### Steps

1. 把日界、领取幂等、叠加或新开格、按段查找空位迁至规则服务，其判定不涉及数据库。
2. 持久化实现只保留写行，不再回调内容源。
3. 教学奖励改用迁出后的规则与存储抽象。
4. 退役物品相关的旧存储成员与并行类型。
5. T-005 的用例在归属改变后原样通过，不修改其断言。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-019 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~RefillRule` | 四类判定在不涉及数据库的情况下被检验 |
| CK-020 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~DailyRefill` | T-005 的用例断言未被改写且全部通过 |
| CK-021 | `Non-command:` 检索持久化实现中的日界、幂等、叠加决策与按段查找 | 零命中 |
| CK-022 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/86JP.Server.slnx -m:1` | 全套通过 |

### T-007: 对 `SlotsChanged` 的可达性下结论

- Depends on: T-003
- Covers: RQ-011
- Boundaries: BD-010, BD-016
- Inputs: T-003 之后读与写已进入同一用例边界、且用例被账号粒度闸门串行化
- Outputs: 可达性结论及其处置

#### Steps

1. 尝试构造使该拒绝原因触发的情形：在同一用例边界内于读之后改变槽位内容。
2. 若不可构造，移除该拒绝原因并保留一条断言，证明其余拒绝原因、触发条件与所选提示码均未变化。
3. 若可构造，保留该拒绝路径与其提示码，并记录构造方式——这不算失败。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-023 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Protocol.Tests -m:1 --filter FullyQualifiedName~ItemRefusalCodeTests` | 移动的对外拒绝原因集合除该项外逐条不变，所选提示码不变 |
| CK-024 | `Non-command:` 记录可达性结论与所采取的处置 | 结论落纸，含构造尝试的具体做法与其结果 |

### T-008: 行为保持的整体证据

- Depends on: T-004, T-006, T-007
- Covers: RQ-009
- Boundaries: BD-016
- Inputs: 前序全部任务的产出
- Outputs: 帧字节比对结果、全套测试结果、实机走查结果

#### Steps

1. 对一组既有的移动与使用请求，比对改动前后产生的帧字节。
2. 停止宿主进程后重建，运行全套测试。
3. 实机走查：教学关拖拽道具与使用药剂，核对表现与改动前一致，含服务端冷却拒绝仍然生效。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-025 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/tests/Dfo.Server.Protocol.Tests -m:1 --filter FullyQualifiedName~Codec` | 移动与使用的帧字节与改动前逐字一致 |
| CK-026 | `Get-CimInstance Win32_Process \| Where-Object { $_.CommandLine -like '*Dfo.Server.Host.dll*' } \| Stop-Process -Force` | 重建前无宿主进程占用产物 |
| CK-027 | `$env:PVF_ARCHIVE_PATH='D:/project/DNF/DNFClient-debug/Script.pvf'; $env:Pvf__ArchivePath=$env:PVF_ARCHIVE_PATH; dotnet test Server/86JP.Server.slnx -m:1` | 848 通过 / 2 跳过 / 0 失败的基线加全部新增用例；`ChannelHostStartupTests` 若因负载假红，单独重跑即绿，不计回归 |
| CK-028 | `Non-command:` 实机走查拖拽与使用道具 | 表现与改动前一致，服务端日志显示移动与消耗按预期发生 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001 | BD-001, BD-002, BD-003 | CK-001 |
| RQ-002 | T-001, T-003 | BD-003, BD-010 | CK-001, CK-012 |
| RQ-003 | T-003 | BD-007, BD-010 | CK-010 |
| RQ-004 | T-001 | BD-002 | CK-002 |
| RQ-005 | T-003 | BD-007 | CK-011 |
| RQ-006 | T-005, T-006 | BD-012, BD-013, BD-014 | CK-017, CK-019, CK-021 |
| RQ-007 | T-004 | BD-008, BD-009 | CK-014, CK-015 |
| RQ-008 | T-003 | BD-007, BD-008 | CK-008, CK-009 |
| RQ-009 | T-008 | BD-016 | CK-025, CK-027, CK-028 |
| RQ-010 | T-002 | BD-004, BD-005, BD-006 | CK-005, CK-007 |
| RQ-011 | T-007 | BD-010 | CK-023, CK-024 |

## Risks and Rollback

- Risk: 拆掉复合写而事务尚未保护时，会出现测试看不见的原子性空洞。缓解：T-003 把两者绑在同一任务内，且依赖 T-001 与 T-002；本计划不提供把它们分开的排法。
- Risk: 寻址充分性若被证伪，后续任务全部失去前提。缓解：T-002 在实现之前先写五种操作的表达用例，证伪即停在该任务并回契约层重议，此时已投入的改动仅限存储抽象本身。
- Risk: 规则外迁后每日刷新或教学奖励行为漂移，而漂移只在入场路径显现。缓解：T-005 先判定并补齐覆盖，其用例断言的是改动前的行为，T-006 不得修改这些断言。
- Rollback: 任一任务的产出未通过其 Validation 时，回退该任务自有的改动至上一任务结束时的状态；本任务全程不产生任务级提交，接受前的完整改动集是唯一回退单位。

## Assumptions

- 账号粒度的执行闸门覆盖两条道具协议，因此同一角色的用例被串行化。T-007 的结论依赖该前提；若不成立，`SlotsChanged` 仍可达并按该任务的第二种处置保留。
- 既有物品数据表足以承载存储抽象的读写，无需新增表结构。若不足，属数据库结构定义集中化那一刀，本次不做。
