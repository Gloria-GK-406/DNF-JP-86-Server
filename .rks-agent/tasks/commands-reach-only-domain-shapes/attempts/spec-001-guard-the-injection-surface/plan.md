---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 守住注入面

## Implementation Approach

Spec 的核心成果是「命令处理器的每个依赖都能仅凭类型说清自己是什么，且判据守着它」。判据是核心
成果本身，但它必须最后落地——它识别类别的规则依赖于名字，而名字要先改到位。所以顺序是先让名字
说话，再让判据守话。

识别规则定为：**构造器参数类型是接口、声明在 `Dfo.Server.Abstractions` 程序集、名字以
`DomainService` 或 `DomainStore` 结尾**；否则必须出现在具名豁免清单里。这条规则满足 RQ-002：
新增一个符合词汇表的依赖无需改判据。它成立的前提已经核实——当前被注入的 15 个领域服务无一例外
声明在 `Dfo.Server.Abstractions`。

Spec 里的「交出实例的工厂」不需要单独一类：`ICharacterDomainService.Get(id)`、
`ICharacterQuestLogDomainService.LogOf(...)`、`ICharacterContainerDomainService.FindContainer(...)`
本身都是领域服务，实例是它们交出来的返回值。三类因此收敛为两类加豁免。

最早的一批工作是退役三个没有消费方的接缝（T-001）。它排在最前不是因为重要，而是因为它**减少
后面要分类与改名的类型数**，且与其余工作零耦合——晚做只会让每一步都多背三个类型。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacterServices.cs` | 删除 `IActiveCharacterAccessDomainService` 与 `IActiveCharacterWriteScope`；`IActiveCharacterDomainService` 收窄到仍被调用的两个成员 | 消费方枚举 | 只剩有消费方的接缝 |
| BD-002 | `Server/src/Dfo.Server.Application/Users/ActiveCharacterAccessDomainService.cs` | 整文件删除 | BD-001 | 文件不再存在 |
| BD-003 | `Server/src/Dfo.Server.Application/Users/OnlineStateStore.cs` | 删除随之失去调用方的成员；注册项不再保存无人读出的完整角色状态，只保留存活路径实际用到的字段 | 现有读写点枚举 | 无只写不读的角色状态副本 |
| BD-004 | `Server/src/Dfo.Server.Application/Users/UserDomainServices.cs` | 门面随接口收窄 | BD-001、BD-003 | 门面与接口一致 |
| BD-005 | `Server/src/Dfo.Server.Abstractions/Inventory/IAccountCargoGoldService.cs`、`Server/src/Dfo.Server.Application/Inventory/AccountCargoGoldService.cs` | 两个文件删除 | 消费方枚举 | 接缝不再存在 |
| BD-006 | `Server/src/Dfo.Server.Abstractions/Inventory/IInventoryEntryServices.cs` 中的 `IEquipmentEligibilityCharacterReader`、`Server/src/Dfo.Server.Application/Characters/CharacterDomainService.cs` 中它的实现 | 删除接口与实现成员 | 消费方枚举 | 接缝不再存在 |
| BD-007 | `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 删除随之失效的注册 | BD-001..BD-006 | 容器不再注册已删类型 |
| BD-008 | `Server/tests/Dfo.Server.Application.Tests/Users/ActiveCharacterAccessTests.cs`、`.../Inventory/AccountCargoGoldServiceTests.cs`、`.../Characters/EquipmentEligibilityCharacterFactsTests.cs`、`Server/tests/Dfo.Server.Host.Tests/CarriedGoldCompositionTests.cs` | 前三个整删；第四个去掉对已删接缝的解析断言 | BD-001..BD-006 | 判据不再引用已删类型 |
| BD-009 | `Server/src/Dfo.Server.Abstractions/Transactions/DomainStores.cs` | `IStoreSession<,,>` 改名为 `IDomainStoreSession<,,>` | — | 会话词汇与 store 词汇同族 |
| BD-010 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacterStore.cs`、`.../Inventory/IItemStore.cs`、`.../Quests/IQuestStore.cs` | 三个 store 接口与其会话接口改名为 `*DomainStore` / `*DomainStoreSession`，文件名随之 | BD-009 | 领域 store 可由名字辨认 |
| BD-011 | `Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterStore.cs`、`.../Inventory/SqliteItemStore.cs`、`.../Quests/SqliteQuestStore.cs` | 实现类与文件名随接口改名 | BD-010 | 实现与契约同名族 |
| BD-012 | `Server/src/Dfo.Server.Abstractions/World/ICharacterExperienceGrant.cs`、`Server/src/Dfo.Server.Application/World/CharacterExperienceGrant.cs` | 改名为 `ICharacterExperienceDomainService`；新增承接教学等级落库的成员 | — | 类别可从名字读出；行网关不再外露 |
| BD-013 | `Server/src/Dfo.Server.Abstractions/World/IExperienceBonusService.cs`、`Server/src/Dfo.Server.Application/World/ExperienceBonusService.cs` | 改名为 `IExperienceBonusDomainService` | — | 同上 |
| BD-014 | `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs`、`.../Characters/SelectCharacterPreparationService.cs`、`.../Characters/SelectCharacterPostEntryServices.cs` 中的五个接口 | 接口上提到 `Dfo.Server.Abstractions` 并补 `DomainService` 后缀，实现留在原处 | — | 跨层接缝住在接缝层 |
| BD-015 | `SelectedSessionCleanupNotificationPlan`、`PvpParticipantPublicationTarget`、`SelectCharacterQuestSynchronization`、`SelectCharacterPetRevival` 四个 record | 移到 `Dfo.Server.Data` | BD-014 | 上提后的接口签名可解析 |
| BD-016 | `Server/src/Dfo.Server.Protocol/Game/TutorialLevelUp/TutorialLevelUpSlice.cs` | 命令处理器不再注入行网关，改经 BD-012 的新成员 | BD-012 | 硬违规消除 |
| BD-017 | `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs` | 命令处理器不再注入持久化接缝；出生点的落位与落库折进准备用领域服务 | BD-014 | 硬违规消除 |
| BD-018 | `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs` | 黑名单改为按类型判定的白名单，附具名豁免清单；判定逻辑可被单独喂入假类型验证 | T-001..T-005 | 违规会红 |
| BD-019 | `AGENTS.md` | 记入词汇表与其判据；裁决五个内部服务的跨层状态；更新「分层测试不存在」的自陈 | T-001..T-006 | 记载与代码一致 |

## Ordered Tasks

### T-001: 三个没有消费方的接缝与只写不读的角色副本不再存在

- Covers: RQ-006, RQ-007
- Boundaries: BD-001, BD-002, BD-003, BD-004, BD-005, BD-006, BD-007, BD-008
- Inputs: 三个接缝及其实现、注册、测试的位置；`OnlineStateStore` 的读写点枚举
- Outputs: 接缝与其测试移除；注册项只保留存活路径读取的字段

#### Steps

1. 删除 `IActiveCharacterAccessDomainService` 与 `IActiveCharacterWriteScope` 及其实现文件，
   并把 `IActiveCharacterDomainService` 收窄到 `Publish(SelectedSessionIdentity, …)` 与
   `ClearExact` 这两个仍有调用方的成员。
2. 删除 `IAccountCargoGoldService` 与其实现；删除 `IEquipmentEligibilityCharacterReader` 与
   `CharacterDomainService` 上对应的实现成员。
3. 在 `OnlineStateStore` 中删除随之失去调用方的成员（角色读取与批量应用一族），并把注册项
   改为只保留存活路径实际读取的内容——已核实存活读取点只用到角色标识，完整角色状态无人读出。
4. 同步删除 `ApplicationModule` 里失效的注册与三个测试文件，并移除组合根测试里对已删接缝的
   解析断言。
5. 记录被删测试的逐条清单与其用例数，供 CK-003 对账。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误（仓库启用 TreatWarningsAsErrors） |
| CK-002 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 全部通过；跳过仍为 2 |
| CK-003 | Non-command: 用例数与基线 2015 的差额，逐条对上第 5 步记录的被删测试 | 差额与清单完全吻合，无未交代的减少 |
| CK-004 | Non-command: 枚举 `OnlineStateStore` 注册项各字段的读出点 | 每个保留字段至少有一个存活读出点 |

### T-002: 领域 store 与其会话类型可由名字辨认

- Depends on: T-001
- Covers: RQ-004, RQ-007
- Boundaries: BD-009, BD-010, BD-011
- Inputs: store 家族的九个类型声明位置
- Outputs: `*DomainStore` / `*DomainStoreSession` 名族

#### Steps

1. 把 `IStoreSession<,,>` 改名为 `IDomainStoreSession<,,>`。
2. 把三个 store 接口及其会话接口改名为 `*DomainStore` / `*DomainStoreSession`，文件名随之。
3. 把三个 Sqlite 实现类与文件名随接口改名。
4. 逐个更新引用点。**按类型改名，不做全局字符串替换**：`IExpertStoreRegistry` 的 `Store` 指
   玩家摆的摊，`OnlineStateStore` 是内存容器，两者都不属于本任务的改名对象。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-006 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 与 CK-002 同一结果 |
| CK-007 | Non-command: 列出仓库中名字含 `Store` 的全部领域类型及其含义 | 领域 store 与摆摊、内存容器三者可仅凭名字区分 |

### T-003: 两个形状正确但缺后缀的接缝改用能表明类别的名字

- Depends on: T-001
- Covers: RQ-001, RQ-007
- Boundaries: BD-012, BD-013
- Inputs: 两个接口及其实现与注入点
- Outputs: `ICharacterExperienceDomainService`、`IExperienceBonusDomainService`

#### Steps

1. 把 `ICharacterExperienceGrant` 改名为 `ICharacterExperienceDomainService`，实现与文件名随之；
   四个注入点同步更新。
2. 给它新增一个承接「把角色置于某个等级并落库」的成员，供 T-005 使用——该实现已持有行网关，
   落库归它是恰当的归属而非搬家。
3. 把 `IExperienceBonusService` 改名为 `IExperienceBonusDomainService`，实现与注入点同步更新。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-009 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 与 CK-002 同一结果 |

### T-004: 五个跨层使用的内部服务住进接缝层

- Depends on: T-001
- Covers: RQ-001, RQ-007, RQ-008
- Boundaries: BD-014, BD-015
- Inputs: 五个接口的签名；四个支撑 record 的当前位置
- Outputs: 接口在 `Abstractions` 且带 `DomainService` 后缀；支撑 record 在 `Data`

#### Steps

1. 把四个支撑 record 移到 `Dfo.Server.Data` 中与其同类的位置。
2. 把五个接口移到 `Dfo.Server.Abstractions` 的对应目录并补 `DomainService` 后缀；实现类留在
   `Application` 原处，只更新其声明的接口名。
3. 更新全部注入点与注册。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-010 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-011 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 与 CK-002 同一结果 |
| CK-012 | Non-command: 检查 `Dfo.Server.Application` 中是否仍有被 `Dfo.Server.Protocol` 注入的接口 | 无；跨层接缝全部住在 `Abstractions` |

### T-005: 命令处理器不再够到 store 以下与容器

- Depends on: T-003, T-004
- Covers: RQ-003, RQ-007
- Boundaries: BD-016, BD-017
- Inputs: 相关处理器的现有依赖与调用序列
- Outputs: 命令处理器不再依赖行网关、持久化接缝或会话容器

> **计划修订（执行期补入）。** 本任务原先只列两处硬违规。推演白名单的最终依赖面时发现，
> `ISelectedSessionDirectory` 另有 **2 次**注入落在命令处理器上
> （`PrepareSelectedCharacterSessionCommandHandler`、`CompensateSelectedCharacterSessionCommandHandler`）。
> 它在普查中被归为「取决于 store 边界怎么定」，而所有者随后裁定「store 只有实现 `IDomainStore`
> 的那一种，其余都是问题」——按该裁定它是违规。它既不在本成果原先的两处硬违规里，也不在后续
> 成果的三个注册表清单里，属于落在批次之间的漏项。Spec 的 RQ-001 与 RQ-003 本就覆盖它，故按
> 计划修订补入本任务，不扩大 Spec 边界。
> 该容器在 socket 消息处理器上另有 25 次注入，那属于 Spec 明列的 Non-Goal，不在本次范围。

#### Steps

1. 教学升级处理器改为经 T-003 新增的成员完成落库，不再注入行网关；保留它对「行未写入」的
   拒绝语义。
2. 把出生点的解析、写入会话结果、以及按频道策略落库这三步，折进准备用领域服务的一个成员；
   处理器改为调用它。
3. 证明折叠后的副作用次序变化不可观察：落库与租约准备同属一个 `Transactional` 命令，且租约
   准备既不读也不写角色位置。
4. 把那两个处理器对会话容器的使用（查找、替换登记、精确移除）移到拥有会话入场的领域服务后面，
   使容器不再出现在任何命令处理器的构造器上。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-013 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-014 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 与 CK-002 同一结果 |
| CK-015 | Non-command: 检视租约准备的实现，确认它不读也不写角色位置 | 确认，折叠后的次序变化不可观察 |
| CK-021 | Non-command: 枚举命令处理器的构造器，查找会话容器 | `ISelectedSessionDirectory` 不再出现在任何命令处理器上 |

### T-006: 判据守住注入面，且违反会红

- Depends on: T-002, T-003, T-004, T-005
- Covers: RQ-001, RQ-002, RQ-003, RQ-005
- Boundaries: BD-018
- Inputs: 全部命令处理器的构造器依赖
- Outputs: 白名单判据与具名豁免清单

#### Steps

1. 把判定写成一个可以单独喂入任意类型的函数：接口 + 声明在 `Dfo.Server.Abstractions` +
   名字以 `DomainService` 或 `DomainStore` 结尾，否则须命中具名豁免清单。
2. 用它遍历全部命令处理器的构造器参数并断言无违规；违规信息要指出是哪个处理器的哪个参数。
3. 具名豁免清单目前只有 `TimeProvider`，每项在代码旁写明理由。
4. 补一个用例，把一个不符合词汇表的类型喂给该判定函数，断言它被判为违规——这样 RQ-005 的
   可证伪性有判据覆盖，而不必往生产代码里塞真违规。
5. 保留原有「不许注入命令总线」的断言：白名单虽已蕴含它，但它记录的是一条独立的架构禁令。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-016 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 全部通过，含新增用例 |
| CK-017 | Non-command: 枚举全部命令处理器的构造器参数，逐项标注归类与依据 | 每一项要么命中两类后缀之一，要么在豁免清单内且写明理由 |
| CK-018 | Non-command: 检视判据实现 | 判定不含逐个类型名的人工清单；豁免清单是唯一的具名例外 |

### T-007: 仓库约定说出这套词汇表

- Depends on: T-006
- Covers: RQ-008
- Boundaries: BD-019
- Inputs: 前六个任务的实际结果
- Outputs: 与代码一致的约定记载

#### Steps

1. 在 `AGENTS.md` 写入词汇表：命令处理器可注入的两类形状、实例与快照各自的来路、以及违反时
   会失败的那条判据。
2. 裁决并改写关于五个内部服务「不跨层暴露」的记载——它们现在住在接缝层，记载据实更新。
3. 更新「分层测试不存在」的自陈，指向新落地的判据，并说明它覆盖的是命令处理器而非普通消息
   处理器。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-019 | Non-command: 把改动的记载逐条对照代码事实 | 无与代码矛盾的陈述 |
| CK-020 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 与 CK-016 同一结果 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-003, T-004, T-006 | BD-012, BD-013, BD-014, BD-015, BD-018 | CK-017 |
| RQ-002 | T-006 | BD-018 | CK-018 |
| RQ-003 | T-005, T-006 | BD-016, BD-017, BD-018 | CK-017, CK-021 |
| RQ-004 | T-002 | BD-009, BD-010, BD-011 | CK-007 |
| RQ-005 | T-006 | BD-018 | CK-016 |
| RQ-006 | T-001 | BD-001, BD-002, BD-003, BD-004, BD-005, BD-006, BD-007, BD-008 | CK-004 |
| RQ-007 | T-001, T-002, T-003, T-004, T-005 | BD-003, BD-010, BD-011, BD-012, BD-014, BD-016, BD-017 | CK-003, CK-014, CK-015 |
| RQ-008 | T-004, T-007 | BD-014, BD-019 | CK-012, CK-019 |

## Risks and Rollback

- Risk：按字符串改名会把 `IExpertStoreRegistry`（玩家摆摊）与 `OnlineStateStore`（内存容器）
  一起改掉，改完仍然编译、仍然全绿，只是名字从此说谎。缓解：T-002 逐类型改名，并以 CK-007
  的名称含义对照表作为验收证据。
- Risk：退役死件时只删接口或只删注册，判据不会有任何反应。缓解：CK-003 的逐条对账与 CK-004
  的读出点枚举都是人工核对而非判据。
- Rollback：全部工作未提交前，可由根控制器按精确工作流回滚还原到 `9c0de6c6`。

## Assumptions

- 判定规则依赖「跨层领域接缝都声明在 `Dfo.Server.Abstractions`」这一事实。当前 15 个被注入的
  领域服务全部满足；T-004 把余下五个也搬进来之后该前提对本 Spec 范围内成立。
- 基线自带一个偶发失败（跨事务读取用例抛已释放的 SQLite 句柄），单独复跑其所在项目即全绿。
  CK-002 及其后续同类检查以复跑结果为准。
