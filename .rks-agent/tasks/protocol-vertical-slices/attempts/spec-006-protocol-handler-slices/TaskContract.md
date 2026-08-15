---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-7
  - SC-9
  - SC-11
depends_on: []
---

# TaskContract: Protocol Handler Slices

## Context

### Source Request

根任务的最后一个有界成果，由根控制器在接受 spec-005 后于 `safe-successor` 时确定：把消息 DTO、消息 handler、命令与命令 handler 迁入 spec-003 已建立的切片目录，并完成 AGENTS.md 更新与 handler 业务语义边界审计。用户授权见根 TaskContract 的 Authorization 行与下方 Authorization 表。

本 Spec 承接已封存的 spec-004——后者以相同成果建模，但其边界建立在"投递 DTO 是钉死类型"这一被证伪的前提上，故以 `failed` 封存且未改动任何代码。spec-005 已消除该前提。

用户另在 spec-003 交付后确认了一处附带处置：`NetworkPlugins.cs` 中因跨程序集而失效、被降级为硬编码 `<c>0x01</c>` 的文档引用，应按"断言归位"处理而非保留字面量。用户原话：「一个东西依赖另一个包的定义，但是另一个包又不被引用。那就说明这个机制或者功能放错位置了。留下这个硬编码才更刺眼。新方案一并处理了吧」。

### Current Situation

spec-003 与 spec-005 之后，`Dfo.Server.Protocol` 已有 15 个切片目录（Channel 4 + Game 11），每个协议的入站解包与出站编码已同处一目录。六类制品中仍有三类在别处：

- **消息 DTO** 在 `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`（单文件承载全部消息 record 与协议身份契约）。
- **消息 handler** 在 `Server/src/Dfo.Server.Application/Game/`（10 个）与 `Application/Channel/`（`ChannelConnectHandler` 加 `ChannelControlMessageHandlers` 中的 3 个），共 14 个。
- **命令与命令 handler** 在 `Application/Commands/Game/`（4 个）与 `Commands/Channel/`（2 个文件承载 4 条命令）。

`ApplicationModule` 中 14 条 `RegisterMessageHandler` 与 8 条 `AddCommandHandler` 是它们的注册点。

四项勘察结论决定了本成果的形态：

1. **不再存在任何钉死的消息 DTO。** spec-005 已查明 `SelectCharacterProjectionDelivery` 与 `ExplicitProjectionTarget` 本非消息（无线上字段、自带复数收件人），并已将其迁入 `Dfo.Server.Abstractions.Networking`。`NetworkMessages.cs` 现在只剩协议身份契约与纯粹的消息 record。
2. **消息 DTO 在 Protocol 之外零引用。** 已对 Networking 传输侧与留驻 Application 的服务逐一检索具体消息类型名，无命中。协议身份契约（两个枚举、两个消息接口、两个 `*MessageMetadata`）被 Abstractions 的两个接缝、Networking 传输侧与 Application 的派发器共同依赖，须留在 `Dfo.Server.Data`；除此之外全部消息 record 均可迁入切片。
3. **切片项目须新增对 `Dfo.Server.Application` 的引用。** `SelectCharacterCommand` 依赖 `Application.Characters`、`Application.Sessions`、`Application.Inventory`、`Application.OnlineUsers`，命令契约在 `Application.Commands`。方向为 Protocol → Application，不触及 `Dfo.Server.Networking` 的引用图（其 csproj 在 spec-003 中逐字未变）。
4. **`NetworkPlugins.cs` 的注释同时声明了两件归属不同的事。** "这里不检查 command 字节 / 未注册的线上键由查表本身丢弃"是传输事实，在本文件可验证；"全部 Game 解包器都注册在 command 0x01 下"是协议事实，只在 `ProtocolModule` 可验证。后者在 spec-003 中被降级为字面量 `0x01`，成为 `GameProtocol.Command` 的一份脱钩副本——该常量若变更，无任何机制发现该注释已失真。

接受基线 `7545921`：编译 0 警告 0 错误，全量测试 580 项 515 通过 / 65 跳过 / 0 失败。

## Goal

每个已实现协议的消息 DTO、入站解包、出站编码、消息 handler、命令与命令 handler 位于同一个切片目录中，可一处通览；协议接线集中在切片项目的模块内；消息 handler 不直接实施业务语义或状态迁移这一边界经审计并被记入项目文档；服务端的线级可观察行为与全部既有断言不变。

## Scope

### In

- 将全部消息 DTO 自 `NetworkMessages.cs` 拆入各自的切片；只有协议身份契约（两个枚举、两个消息接口、两个 `*MessageMetadata`）留在 `Dfo.Server.Data`。
- 将 14 个消息 handler、8 条命令与其 handler 迁入各自的切片目录。
- 将 `ApplicationModule` 中属协议的 14 条消息 handler 注册与 8 条命令注册迁入切片项目的模块；命令总线本身、管线行为与领域服务注册留在 `ApplicationModule`。
- 切片项目新增对 `Dfo.Server.Application` 的引用。
- 迁移受影响的测试至对应位置。
- 审计全部 14 个消息 handler 是否直接实施业务语义或状态迁移，记录结论；若发现违反，在本成果内纠正或明确记录为待处理。
- 更新 AGENTS.md：新的项目职责与引用图、「新增一个协议的固定动作」、切片可拥有与不可拥有内容的边界规则、以及 handler 不得承载业务语义的规则。
- 拆分 `NetworkPlugins.cs` 中归属错位的注释：传输侧断言留在本文件且不含具体线上号，协议侧断言迁至 `ProtocolModule` 的入站路由表处并恢复对 `GameProtocol.Command` 的符号引用。
- 排查并修正因本次搬迁而失真的其余注释：凡描述"另一个编译单元如何工作"的散文，跨程序集后不再被编译器看管，须逐处核对。
- 在 `Docs/` 下新增一份架构不和谐点记录，供用户后续复审。该文档只陈述观察与理由，不实施任何改动。

### Out

- 任何字节布局、加密参数、组帧规则、投递次序、解包判定与丢弃语义的改变。
- 协议身份契约的迁移。理由见 Current Situation 第 2 条。
- 命令总线、管线行为、执行闸门与 access mode 取值的改动。
- 领域服务、仓储、PVF 内容源的迁移或改写。
- 抽取共享的字段读写原语。根 Scope/Out 已排除。
- 新增或删除任何协议、消息、命令或注册项。

## Constraints

- 迁移部分不得夹带逻辑变更：除文件位置、命名空间、using、可见性修饰，以及迁移直接导致的失效引用修复外，其 diff 不得包含行为改动。注释归位属本成果明确授权的例外，须单独列出。
- 编译期分层不得放宽：`Dfo.Server.Networking` 不得引用 `Dfo.Server.Application` 或 `Dfo.Server.Protocol`；`Dfo.Server.Abstractions` 与 `Dfo.Server.Data` 不得引用 `Dfo.Server.Protocol`。
- 线级可观察行为必须不变：未知线上帧键静默丢弃、出站按端点校验、Channel 载荷的加密与压缩参数、Game 出站帧头、选择角色的静态帧流与补充投影的有序投递及其失败补偿次序、登录失败体仍不可编码。
- 消息 handler 不得直接实施业务语义或状态迁移，须经命令与领域服务到达。此为用户明确要求，理由是 handler 私改状态而领域服务不知情构成双写。
- 不得以放宽类型、抑制诊断或关闭 `TreatWarningsAsErrors` 的方式换取编译通过。
- 既有 580 项测试须全部继续通过，且断言内容不得改动；迁移可改变测试的位置、命名空间与 using。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| CSC-1 | 每个已实现协议的消息 DTO、入站解包、出站编码、消息 handler、命令与命令 handler 位于同一目录 | 逐协议的切片目录内容列示，对全部 14 个入站协议成立 |
| CSC-2 | 留在 `Dfo.Server.Data` 的网络类型仅限协议身份契约 | 该文件剩余内容的列示与逐项归属说明 |
| CSC-3 | 协议接线集中在切片项目的模块内 | 该模块的代码审阅；`ApplicationModule` 中不再有消息 handler 与协议命令的注册 |
| CSC-4 | 编译期分层未放宽 | 各 csproj 引用图；`Dfo.Server.Networking` 不引用 Application 与 Protocol，`Abstractions` 与 `Data` 不引用 Protocol |
| CSC-5 | 迁移部分不含逻辑变更 | 逐文件 diff 审阅结论，配合内容比对；注释归位的改动单独列示 |
| CSC-6 | 线级可观察行为不变 | 既有集成测试通过且断言未改 |
| CSC-7 | 消息 handler 不直接实施业务语义或状态迁移 | 全部 14 个 handler 的逐个审计结论，含每个 handler 所调用的命令或服务 |
| CSC-8 | AGENTS.md 记录新的项目职责、新增协议的固定动作、切片边界规则与 handler 业务语义规则 | 该文件相应章节 |
| CSC-9 | `NetworkPlugins.cs` 不再含协议侧断言的硬编码副本，该断言在能验证它的位置并带符号引用 | 两处注释的代码审阅 |
| CSC-10 | 无回归 | 全量测试对齐 580 项基线，0 失败，并说明测试的位置改动与任何断言改动 |
| CSC-11 | `Docs/` 下存在一份架构不和谐点记录，逐项给出观察、理由与影响，且不实施任何改动 | 该文档本身，以及其每一条与代码现状的可核对性 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 复用根 TaskContract 的 `task` 行（「按ssd跑完吧……」），并经用户后续授权：就注释归位答「可以，这是对的。……新方案一并处理了吧」；就自主跑完余下全部工作答「你先按你的想法完成整个任务。然后我再做一次复审处理。……直接按你想的跑吧。我后面会处理掉不和谐的地方。不过你要在docs下留个文档说明一下哪些感觉不和谐，要注意。」 | 在任务工作树 `codex/protocol-vertical-slices` 内完成 DTO、消息 handler、命令与命令 handler 的切片归位、相应测试迁移、AGENTS.md 更新、handler 业务语义审计、注释归位，以及在 `Docs/` 下交付一份架构不和谐点记录，产出一次已审阅并已验证的本地结果提交。该成果完整落在根 Scope/In 第 5、6、7 条之内；架构文档为用户明确追加的交付物，不改变代码范围。 |

## Requires Confirmation

- 合并、推送、发起 PR，或对 `main` 与其他分支的写入。
- 若审计发现某个 handler 确实承载业务语义，对其行为的实质性改写（而非记录）。
- 迁移协议身份契约。
- `protocol/` 知识库与协议结论的任何改动。

## Assumptions

- 消息 DTO 在 `Dfo.Server.Protocol` 之外零引用（已对 Networking 传输侧与留驻 Application 的服务逐一检索确认；此前唯一的例外已由 spec-005 查明为归类错误并移出消息集合）。若迁移中发现反例，该 DTO 的归属须重新评估而非放宽引用图。
- 消息 record 的成分类型（`ClientSpecStatisticSlot`、`ClientSpecProfilePair`/`Row`/`Entry`、`SessionMetricMean`/`Record`）随其所属消息进入同一切片。
- 命令与其 handler 同处一个文件是既有写法，迁移保持该形态而不拆分。
- 架构不和谐点记录是观察性文档，其每一条须可由当前代码核对；它不承诺任何后续改动，处置由用户决定。
- 基线中 `Dfo.Server.Host.Tests` 有一次未复现的瞬时失败（见根 map 的 `baseline_evidence`）。同类瞬时失败不得计为本成果的回归，但须在结果中如实记录其发生。
