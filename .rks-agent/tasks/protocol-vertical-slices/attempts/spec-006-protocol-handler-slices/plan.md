---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Protocol Handler Slices

## Implementation Approach

Spec 的核心成果是"每个协议的六类制品同处一目录，且迁移未改变任何行为"。必要支撑是协议身份契约留在数据契约单元与引用方向只增 Protocol → Application；周边保证是零变更比对、逐个 handler 审计、断言未改与全量测试。

最小可行路径基于两项已勘察的项目事实：

1. **`ApplicationModule` 是 Application 内唯一引用待迁 handler 与命令的文件**，而它引用的正是要随之迁走的 22 条注册。除它之外，Application 的领域服务、会话服务与派发器均不引用任何待迁类型。因此不存在 Application ↔ Protocol 的环，整批迁移可一次成立。
2. **13 个测试文件引用待迁类型，且其中的 `Support/EntryServices.cs` 是与领域测试共享的脚手架。** `SelectCharacterLifecycleTests` 同时验证命令 handler 与领域生命周期，`ExitFlushHandlerTests` 横跨三个协议。故**测试整体留在 `Dfo.Server.Application.Tests`**，只为该项目增加对 Protocol 的引用与必要 using。这是一个有意的取舍：Spec 的核心成果是源码切片，测试迁移买到的收益小而拆分共享脚手架的风险大。该不对称将写入架构不和谐点文档。

搬迁分两批（Channel 先、Game 后），与 spec-003 同构。每批的原子单位是"一个协议的 DTO + handler + 命令 + 其两条注册"——少搬其中任何一样都会让 `ApplicationModule` 或切片项目引用不到对方。批次末尾构建与全量测试须绿。

DTO 的落点规则：消息 record 及其成分类型进入其协议的切片，每切片一个 `Messages.cs`；`ClientMessageType`、`ServerMessageType`、`IClientNetworkMessage`、`IServerNetworkMessage`、`ChannelMessageMetadata`、`GameMessageMetadata` 六项留在 `NetworkMessages.cs`——它们被 Abstractions 的两个接缝、Networking 传输侧与 Application 的派发器共同依赖。

`ChannelControlMessageHandlers.cs`（一文件三 handler）与 `ChannelControlCommands.cs`（一文件三命令）按协议拆开进入 `Script`、`ScriptVersion`、`Info` 三个切片，因为切片是本成果的组织单位；拆开只移动既有类型声明，不改其内容。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Protocol/Dfo.Server.Protocol.csproj` | 新增对 `Dfo.Server.Application` 的引用 | 现文件 | 可承载 handler 与命令的切片单元 |
| BD-002 | `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs` | 删除全部消息 record 与成分类型，只留六项协议身份契约 | 现文件 | 只含身份契约的数据契约文件 |
| BD-003 | `Server/src/Dfo.Server.Protocol/Channel/*/` | 每切片接收其 DTO、handler、命令与命令 handler | Application 的 Channel handler 与命令、`NetworkMessages.cs` 的 Channel 消息 | 四个完整的 Channel 切片 |
| BD-004 | `Server/src/Dfo.Server.Protocol/Game/*/` | 同上，覆盖 11 个 Game 切片 | Application 的 Game handler 与命令、`NetworkMessages.cs` 的 Game 消息 | 十一个完整的 Game 切片 |
| BD-005 | `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 删除 14 条消息 handler 注册与 8 条协议命令注册及失效 using；保留命令总线、管线与领域服务注册 | 现文件 | 只剩应用层自身接线的模块 |
| BD-006 | `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 接收上述 22 条注册；接收自 `NetworkPlugins.cs` 归位的协议侧断言 | BD-005 移出的注册 | 协议接线的唯一注册点 |
| BD-007 | `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs` | 移除协议侧断言的硬编码副本，只留传输侧能自证的部分 | 现第 101-106 行 | 只陈述本处事实的注释 |
| BD-008 | `Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj` 与其 13 个受影响测试文件 | 增加对 Protocol 的引用；补 using。断言不动 | 现文件 | 可编译且断言未改的测试项目 |
| BD-009 | `AGENTS.md` | 更新项目职责与引用图、新增协议的固定动作、切片边界规则、handler 业务语义规则 | 现文件 | 与迁移后形态一致的项目文档 |
| BD-010 | `Docs/ProtocolSliceArchitectureNotes.md` | 新建；逐条记录架构不和谐点，只陈述不改动 | 本次重构全过程的观察 | 供用户复审的观察性文档 |
| BD-011 | `.rks-agent/.../spec-006-protocol-handler-slices/verification/completion-evidence.md` | 新建 | T-001..T-005 的检查输出 | 验证证据 |

## Ordered Tasks

### T-001: Channel 四个切片补齐为完整切片

- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-010, RQ-011
- Boundaries: BD-001, BD-002, BD-003, BD-005, BD-006, BD-008
- Inputs: `NetworkMessages.cs` 的 8 个 Channel 消息、`Application/Channel/` 的 4 个 handler、`Commands/Channel/` 的 4 条命令、`ApplicationModule` 的 8 条相关注册
- Outputs: 四个含六类制品的 Channel 切片

#### Steps

1. `Dfo.Server.Protocol.csproj` 增加 `ProjectReference` 指向 `Dfo.Server.Application`；`Dfo.Server.Application.Tests.csproj` 增加 `ProjectReference` 指向 `Dfo.Server.Protocol`。
2. 每个 Channel 切片新建 `Messages.cs`，自 `NetworkMessages.cs` 移入其请求与响应记录（`Connect`、`ScriptVersion`、`Script`、`Info` 各一对），命名空间为该切片的命名空间，记录内容与其 XML 注释逐字不动。
3. 移入 handler：`ChannelConnectHandler.cs` 进 `Connect/`；`ChannelControlMessageHandlers.cs` 的三个 handler 按协议拆入 `ScriptVersion/`、`Script/`、`Info/` 各自的 `*Handler.cs`，类型声明与方法体逐字不动。
4. 移入命令：`EstablishChannelConnectionCommand.cs` 进 `Connect/`；`ChannelControlCommands.cs` 的三条命令与其 handler 按协议拆入对应切片。
5. `ApplicationModule` 删除 4 条 `RegisterMessageHandler` 与 4 条 Channel `AddCommandHandler` 及失效 using；`ProtocolModule` 按原顺序接收，并补 `using Dfo.Server.Application.Commands;` 与各切片 using。
6. 为受影响的测试文件补 using，断言与其余内容不动。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx -nodereuse:false` | 已成功生成，0 警告 0 错误 |
| CK-002 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 580 项，0 失败 |
| CK-003 | Non-command: 被移动的类型逐个与 `7545921` 版本比对 | 差异仅为命名空间与 using |

### T-002: Game 十一个切片补齐为完整切片

- Depends on: T-001
- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-010, RQ-011
- Boundaries: BD-002, BD-004, BD-005, BD-006, BD-008
- Inputs: `NetworkMessages.cs` 余下的 Game 消息与成分类型、`Application/Game/` 的 10 个 handler、`Commands/Game/` 的 4 条命令、`ApplicationModule` 的 14 条相关注册
- Outputs: 十一个含其应有制品的 Game 切片；`NetworkMessages.cs` 只剩六项身份契约

#### Steps

1. 每个 Game 切片新建 `Messages.cs`，移入其消息记录与成分类型：`Login`（请求、响应）、`Exit`（请求、响应）、`SelectCharacter`（请求、响应）、`AccountRoster`（请求、响应）、`InitialConnection`（通知）、`ClientSpecStatistic`（`Slot` 与请求）、`ClientHardwareDetail`（请求）、`ClientSpecProfile`（`Pair`、`Row`、`Entry` 与请求）、`SessionMetrics`（`Mean`、`Record` 与请求）、`TaggedIdReport`（请求）、`RosterFollowUp`（请求）。
2. 移入 10 个 handler 至其切片，文件内容除命名空间与 using 外逐字不动。
3. 移入 4 条命令与其 handler 至 `Exit`、`AccountRoster`、`Login`、`SelectCharacter` 四个切片。
4. `ApplicationModule` 删除余下 10 条 `RegisterMessageHandler` 与 4 条 Game `AddCommandHandler` 及失效 using；`ProtocolModule` 按原顺序接收。
5. 确认 `NetworkMessages.cs` 只剩 `ClientMessageType`、`ServerMessageType`、`IClientNetworkMessage`、`IServerNetworkMessage`、`ChannelMessageMetadata`、`GameMessageMetadata`。
6. 为受影响的测试文件补 using，断言不动。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet build Server/86JP.Server.slnx -nodereuse:false` | 已成功生成，0 警告 0 错误 |
| CK-005 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 580 项，0 失败 |
| CK-006 | Non-command: 被移动的类型逐个与 `7545921` 版本比对 | 差异仅为命名空间与 using |
| CK-007 | Non-command: `NetworkMessages.cs` 剩余内容列示 | 恰为六项协议身份契约 |
| CK-008 | `grep -o 'Include="[^"]*csproj"' Server/src/Dfo.Server.Networking/Dfo.Server.Networking.csproj Server/src/Dfo.Server.Abstractions/Dfo.Server.Abstractions.csproj Server/src/Dfo.Server.Data/Dfo.Server.Data.csproj` | 三者均不含 Protocol；Networking 亦不含 Application |

### T-003: 注释归位与失真排查

- Depends on: T-002
- Covers: RQ-009, RQ-012
- Boundaries: BD-006, BD-007
- Inputs: `NetworkPlugins.cs` 现有 `<remarks>`、迁移后各文件的注释
- Outputs: 每条注释均可在其所在编译单元内核实

#### Steps

1. `NetworkPlugins.cs` 的 `<remarks>` 只保留传输侧能自证的部分：不检查 command 字节，未注册的线上键由查表本身丢弃。删去"全部 Game 解包器注册在 0x01 下"这一硬编码副本。
2. 在 `ProtocolModule.RegisterInboundDecoders` 的 `<remarks>` 中加入该协议侧断言，并以 `<see cref="Game.GameProtocol.Command"/>` 指向该常量。
3. 逐个检视本次及前三次成果中被跨编译单元搬迁的文件的注释，找出仍在描述另一编译单元事实且无法在本处核实者，逐处修正或记录。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-009 | Non-command: 两处注释的代码审阅 | 协议侧断言在 `ProtocolModule` 且带符号引用；`NetworkPlugins.cs` 无 `0x01` 副本 |
| CK-010 | `dotnet build Server/86JP.Server.slnx -nodereuse:false` | 已成功生成，0 警告 0 错误（`cref` 可解析） |
| CK-011 | Non-command: 跨单元注释排查清单 | 每条被检视的注释给出判定；修正项逐处列示 |

### T-004: handler 审计、项目文档与架构不和谐点记录

- Depends on: T-002
- Covers: RQ-007, RQ-008, RQ-013
- Boundaries: BD-009, BD-010
- Inputs: 迁移后的 14 个 handler、`AGENTS.md`、本次重构全过程的观察
- Outputs: 审计表、更新后的项目文档、供用户复审的观察性文档

#### Steps

1. 逐个读完 14 个消息 handler 的方法体，为每个记录：它调用了哪些命令或服务、该调用是读取还是状态变更、以及是否存在不经命令的状态迁移。产出审计表。
2. 若发现违反，记录为待处理并写入架构文档，**不在本成果内改写其行为**（契约 Scope/Out）。
3. 更新 `AGENTS.md`：编译单元职责与引用图（新增 `Dfo.Server.Protocol` 与其测试项目）、「新增一个协议的固定动作」（改为：在切片目录内新增六类制品并在 `ProtocolModule` 加两行注册）、切片可拥有与不可拥有内容的边界规则（协议内容归切片，传输与协议身份契约不归）、handler 不得承载业务语义的规则及其理由。
4. 新建 `Docs/ProtocolSliceArchitectureNotes.md`，逐条记录架构不和谐点，每条含观察、可核对的代码位置、为何值得注意、以及可选处置方向。已知应收录者：出站 `ServerMessageType` 无机械用途；解包器声明产出与实际产出无一致性检查；`Dfo.Server.Utilities` 为空占位项目；`SelectCharacterStaticBodyFamilies` 的巨型静态表；跨编译单元注释不被编译器看管；测试仍按功能组织而源码已按协议组织的不对称；以及 handler 审计中发现的任何待处理项。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-012 | Non-command: 14 行 handler 审计表 | 每个 handler 一行，含协作者与调用性质，无整体推定 |
| CK-013 | Non-command: `AGENTS.md` 四项内容的审阅 | 四项均已更新且与迁移后实际形态一致 |
| CK-014 | Non-command: `Docs/ProtocolSliceArchitectureNotes.md` 逐条对照代码 | 每条可定位到具体代码或结构事实；文档本身未改动任何代码 |

### T-005: 结构、引用图与零变更的成果证据

- Depends on: T-003, T-004
- Covers: RQ-001, RQ-002, RQ-003, RQ-004, RQ-005, RQ-006, RQ-010, RQ-011
- Boundaries: BD-011
- Inputs: T-001..T-004 的产物
- Outputs: 可供复核与验证引用的证据

#### Steps

1. 列示 `Dfo.Server.Protocol` 完整目录树，逐协议核对六类制品是否齐备（纯入站与纯出站切片按其应有制品核对）。
2. 列示 `NetworkMessages.cs` 剩余内容并逐项说明归属。
3. 检视 `ProtocolModule` 与 `ApplicationModule` 的注册内容。
4. 取全部 csproj 生成引用图。
5. 全量 `git diff 7545921` 过滤 `using`/`namespace` 行后列出净变更，逐项归类。
6. 运行全量测试并记录分项计数。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-015 | Non-command: 切片目录树与逐协议制品核对 | 每个协议的应有制品齐备且同处一目录 |
| CK-016 | Non-command: `ApplicationModule` 审阅 | 无消息 handler 与协议命令注册；命令总线、管线与领域服务注册仍在 |
| CK-017 | Non-command: 过滤后的净变更清单 | 每项可归入位置/命名空间/using/可见性/失效引用修复/注释归位/文档之一 |
| CK-018 | `dotnet test Server/86JP.Server.slnx -nodereuse:false` | 580 项，0 失败，与基线一致 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-002, T-005 | BD-003, BD-004 | CK-015 |
| RQ-002 | T-002, T-005 | BD-002 | CK-007 |
| RQ-003 | T-001, T-002, T-005 | BD-005, BD-006 | CK-016 |
| RQ-004 | T-001, T-002, T-005 | BD-005 | CK-016 |
| RQ-005 | T-001, T-002 | BD-001 | CK-008 |
| RQ-006 | T-001, T-002, T-005 | BD-003, BD-004 | CK-003, CK-006, CK-017 |
| RQ-007 | T-004 | BD-010 | CK-012 |
| RQ-008 | T-004 | BD-009 | CK-013 |
| RQ-009 | T-003 | BD-006, BD-007 | CK-009, CK-010 |
| RQ-010 | T-001, T-002, T-005 | BD-003, BD-004 | CK-002, CK-005, CK-018 |
| RQ-011 | T-001, T-002 | BD-008 | CK-002, CK-005 |
| RQ-012 | T-003 | BD-007 | CK-011 |
| RQ-013 | T-004 | BD-010 | CK-014 |

## Risks and Rollback

- Risk: 某处编译不过时以放宽引用图作为最省事的修复。缓解：CK-008 把三个不得依赖 Protocol 的单元列为独立检查项；本成果唯一应有的引用图变更是 Protocol → Application 与测试项目对 Protocol 的引用。
- Risk: 拆分 `ChannelControlMessageHandlers.cs` 与 `ChannelControlCommands.cs` 时改动类型内容。缓解：CK-003 以逐类型内容比对为准，拆分只移动声明。
- Risk: handler 审计流于形式。缓解：CK-012 要求每个 handler 一行且写明协作者与调用性质，不接受整体结论。
- Rollback: 本尝试未接受前，回滚边界是回到 `7545921`。

## Assumptions

- 测试整体留在 `Dfo.Server.Application.Tests`，只补引用与 using。该取舍写入架构不和谐点文档。
- `Application/Channel/ChannelEncryptionKeyProvider.cs` 与 `ChannelEntryPolicy.cs` 是领域服务而非协议内容，留在 Application。
- `AcknowledgeGameExitCommand.cs` 无 using，迁移后按需补齐。
- 基线中 `Dfo.Server.Host.Tests` 有一次未复现的瞬时失败。同类瞬时失败不计为回归，但须如实记录。
