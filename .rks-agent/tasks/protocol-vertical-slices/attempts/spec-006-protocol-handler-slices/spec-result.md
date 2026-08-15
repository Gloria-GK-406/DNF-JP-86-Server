---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Protocol Handler Slices

## Delivered

- **15 个协议切片补齐为完整切片**（Channel 4 + Game 11）。每个已实现协议的消息
  record、payload codec、入站解包器、出站编码器、消息 handler、命令与命令 handler
  同处一个目录。打开 `Protocol/Game/Login/` 即可一屏看完该协议的全部制品。
- **`Dfo.Server.Data/Networking/NetworkMessages.cs` 从 274 行降至 60 行**，
  只剩六项协议身份契约：两个目录枚举、两个消息接口、两个帧头元数据 record。
  29 个消息 record（含成分类型）全部迁入其所属切片。
- **协议接线收敛到单一位置**。`ProtocolModule` 现持 4 类注册：入站解包器 14 条、
  出站编码器 9 条、命令 8 条、消息 handler 14 条，另加投影组合器与连接观察者。
  `ApplicationModule.ConfigureServices` 只剩领域服务注册加两行机制注册
  （`AddCommandDispatching()` / `AddNetworkMessageDispatching()`）——
  **机制在 Application，具体协议在切片**。
- **发现并解决了一处真实的分层冲突**：`OnlineUserConnectionObserver` 在
  Application 中构造 `GameInitialConnectionNotice`。DTO 一进切片，Application 就
  再也够不着它，而 `Application` 不能引用 `Protocol`（会成环）。观察者整体迁入
  `Protocol/Game/`，可见性由 `internal` 放宽为 `public`，注册随迁。
  这不是搬迁噪音，而是原有归属就错了——已作为最刺眼的不和谐点记入文档第 3 条。
- **`SelectCharacterCommand.cs` 按归属拆分**：命令 record 与
  `SelectCharacterCommandHandler` 进切片；`ISelectCharacterPreparationService` 与
  `SelectCharacterPreparationService` 留在 `Application/Characters/`。接口必须
  留下——实现留在 Application，而 Application 不能实现 Protocol 的接口。
  两半合并后与基线逐行相等。
- **14 个消息 handler 的逐个业务语义审计，零违规**。没有任何 handler 自行改变
  状态或直接调用会改变状态的服务；全部状态迁移都经 `commandBus.SendAsync`。
  关键支撑：唯一被注入 handler 的在线用户协作者是只读的
  `IOnlineUserQueryService`，会改状态的成员在另一个接口上。
- **5 处注释归位**。`NetworkPlugins.cs` 中协议侧断言的硬编码副本 `0x01` 已删，
  该断言移到 `ProtocolModule.RegisterInboundDecoders` 并以
  `<see cref="Game.GameProtocol.Command"/>` 符号引用取代常量；另 4 处描述另一
  编译单元事实的散文改写为本处可核实的陈述。
- **`AGENTS.md` 更新四项**：项目职责与引用图（新增 `Dfo.Server.Protocol` 行，
  引用方向逐条写明）、请求生命周期图、"新增一个协议的固定动作"（现在只需改
  `ProtocolModule` 一处）、新增"切片边界规则"与"handler 业务语义规则"两节。
- **`Docs/ProtocolSliceArchitectureNotes.md`**：12 条架构不和谐点，
  每条含现象、可核对位置、为什么现在没动、可选处置。该文档只陈述，不改动代码。

## Success Evidence

| Condition | Evidence |
|---|---|
| CSC-1 | `verification/completion-evidence.md` 的"切片目录列示"：15 个切片逐目录内容 |
| CSC-2 | 同上"`NetworkMessages.cs` 剩余内容"：6 项，逐项归属说明，无第七项 |
| CSC-3 | 同上；复核件独立核对 22 条注册与 `ApplicationModule` 的删除结果逐字节一致 |
| CSC-4 | 同上"CK-008 引用图"：三个下游单元均不引用 Protocol，Networking 亦不引用 Application；复核件另在源码中 grep 验证，零命中 |
| CSC-5 | 同上"CK-006 逐项内容比对"：15 个整文件 + 29 个 record 逐字一致；注释改动单独成表 |
| CSC-6 | 同上"CK-011"：测试非 using 类改动 3 行，断言零改动；集成测试全通过 |
| CSC-7 | 同上"14 个 handler 逐个审计"：全部合规，违规 0 |
| CSC-8 | `AGENTS.md` 的项目表与引用图、"### 3) Socket 层"、"### 切片边界规则"、"### handler 业务语义规则"、"## 快速开发：新增一个协议的固定动作" |
| CSC-9 | 同上"注释改动清单"第 1、2 条；复核件确认 RQ-009 满足 |
| CSC-10 | 同上"构建与测试"：580 项 0 失败，对齐基线 |
| CSC-11 | `Docs/ProtocolSliceArchitectureNotes.md` 共 12 条；复核件逐条核对现状，指出 2 条不准确，已订正 |

### 被修改的既有测试

`Dfo.Server.Application.Tests` 17 个 `.cs` 文件被改动，其中 16 个需补
`using Dfo.Server.Protocol.*`；另有 `Networking.Tests` 与 `Protocol.Tests` 各若干。
排除 using 与空行后，全部测试改动共 3 行：一处全限定名跟随类型迁移
（`SelectCharacterPreparationService`）、一条 `ProjectReference`。**断言零改动，
无测试被删除或 `Skip`。** 测试文件位置未动。

## Repository Result

- Changed paths：新增 `Server/src/Dfo.Server.Protocol/` 下 24 个文件与
  `Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs`；
  移动 15 个源文件；改 `ApplicationModule.cs`、`ProtocolModule.cs`、
  `NetworkMessages.cs`、`NetworkPlugins.cs`、`INetworkServices.cs`、
  `SelectCharacterSupplementalModels.cs`、`Dfo.Server.Protocol.csproj`、
  `AGENTS.md` 与 20 个测试文件；新增
  `Docs/ProtocolSliceArchitectureNotes.md`；加本尝试的流程制品。
- Baseline or accepted result：基于 `7545921`，待父控制器接受为一次已验证的
  本地结果提交。
- Parent rollback required：no

## Successor Guidance

- **根任务的三个验收边界至此全部落地。** 入站解包 DI 化（spec-001）、出站编码
  DI 化（spec-002）、切片重排（spec-003 编解码 + spec-006 handler 与命令）已完成，
  spec-005 顺带修正了投影投递的归类。根 TaskContract 的成功条件应可全部转为
  satisfied，后继工作是根级复核与验证，而非再开子 spec。
- **`Docs/ProtocolSliceArchitectureNotes.md` 是用户点名要的复审输入**，
  用户明确表示会在重构完成后自行处理其中的不和谐点。**后继不得代为处置**——
  该文档的价值在于把选择权留给用户。
- **文档中第 3 条（连接观察者带业务语义住进协议层）与第 9 条（9 个 handler 在
  总线之外持有应用层协作者）是彼此相关的一对。** 若用户决定收紧"handler 只认识
  `ICommandBus` 与 sender"，两条须一并考虑：身份解析会需要由 dispatcher 预先解好
  并放进 `NetworkDispatchContext`，那是一次接缝改动而非局部修补。
- **测试仍按功能组织，源码已按协议切片**（文档第 8 条）。若要重排测试，
  阻碍是具体的且已勘定：`Support/EntryServices.cs` 是与领域测试共享的脚手架、
  `SelectCharacterLifecycleTests` 横跨命令 handler 与领域生命周期、
  `ExitFlushHandlerTests` 横跨三个协议。须先拆共享脚手架。
- **注释归位仍是每次跨编译单元搬迁都要主动排查的类别。** 本成果又发现 4 处
  spec-002/003 遗留的失真（`SelectCharacterSupplementalModels.cs` 两处、
  `INetworkServices.cs` 两处）。只有 `cref` 会被编译器发现，散文不会——
  文档第 7 条给出的可选约定是"跨单元的事实一律用 `cref` 表达"。
- **`Dfo.Server.Host.Tests` 与 `Dfo.Server.Networking.Tests` 存在并行端口争用**，
  整套 `dotnet test` 偶发单项失败，重跑即绿。非本次改动引入，但后继若把测试计入
  门禁需要处理，否则会产生无意义的红。

## Artifacts

- `spec.md`（RQ-001..RQ-013 / AC-001..AC-014 / EV-001..EV-009）
- `plan.md`（BD-001..BD-011 / T-001..T-005 / CK-001..CK-018）
- `reviews/change-review.md`（独立子代理复核，对抗性取向，approved，0 阻断；
  F-1..F-4 已修复，F-5 经论证保留，非阻断观察逐项处置）
- `verification/completion-evidence.md`（decision: pass）
