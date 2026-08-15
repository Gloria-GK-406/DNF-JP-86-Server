---
kind: rks-verification
decision: pass
---

# Completion Evidence: spec-003 协议编解码迁入 Dfo.Server.Protocol 切片

## 被评估的主张

协议编解码内容已迁入新建的 `Dfo.Server.Protocol`，按端点分层、端点内一协议一目录；`Dfo.Server.Networking` 只剩传输；两张 DI 路由表随之迁入 `ProtocolModule` 且注册无条件生效；编译期依赖方向为 Protocol → Networking 且传输单元未反向依赖；本次 diff 除文件位置、命名空间、using 与迁移直接导致的失效引用修复外不含任何行为改动；线级可观察行为与既有断言逐字不变。

评估对象为任务工作树 `codex/protocol-vertical-slices` 的未提交工作区状态，HEAD 仍为接受基线 `feef1a5`。全部证据在复核补救（切片计数更正、`AccountRoster` 切片名同步）之后重新采集。

## 逐条核对

| ID | 条件 | 判定 | 证据 |
|---|---|---|---|
| CSC-1 | 存在 `Dfo.Server.Protocol`，每个已实现协议有自己的目录 | pass | 目录树列示 44 个 `.cs`：`Channel/` 下 4 个切片（`Connect`、`ScriptVersion`、`Script`、`Info`）与端点级 `ChannelProtocol.cs`、`ChannelEncryptedFrame.cs`；`Game/` 下 11 个切片（`Login`、`Exit`、`SelectCharacter`、`AccountRoster`、`RosterFollowUp`、`ClientSpecStatistic`、`ClientHardwareDetail`、`ClientSpecProfile`、`SessionMetrics`、`TaggedIdReport`、`InitialConnection`）与端点级 `GameProtocol.cs`；加 `ProtocolModule.cs`。合计 15 个切片，覆盖 14 个入站协议与 9 条出站消息（`InitialConnection` 为纯出站，`AccountRoster` 合并 GET_USERINFO 入站与花名册出站）。 |
| CSC-2 | 两侧皆有内容的协议，其解包与编码同处一目录 | pass | 逐切片核对：4 个 Channel 切片各含 1 解包器 + 1 编码器；`Login` 含解包器、codec、编码器；`Exit` 含解包器与编码器；`SelectCharacter` 含解包器、入站 codec、编码器与 5 个组帧/投影件；`AccountRoster` 含 GET_USERINFO 解包器与 codec、花名册 codec 与编码器。纯入站的六个切片（ClientSpec*、SessionMetrics、TaggedIdReport、RosterFollowUp、ClientHardwareDetail）各含解包器与其 codec。 |
| CSC-3 | `Dfo.Server.Networking` 中不再有协议编解码内容 | pass | 该项目现存 17 个 `.cs`，逐个判定均为传输：`Configuration/`（2 个选项类）、`Framing/`（`ChannelFrameRequestInfo`、`ChannelPayloadCipher`、`DnfFrameProtocol`、`FrameDataHandlingAdapters`、`FrameEncoders`、`FrameProtocolException`、`GameClientFrameRequestInfo`）、`Runtime/`（三个注册表、插件、sender、closer、连接注册表、TCP 服务）、`NetworkingModule`。无任何与具体消息绑定的编解码。`Framing/Decoders/` 与 `Framing/Encoders/` 两个目录已删除。 |
| CSC-4 | 两张路由表随之迁入且注册无条件生效 | pass | 复核者把基线与新文件的两个方法体各自切出后 `diff`，**两次均无输出（逐字节 identical）**：14 条入站与 9 条出站注册的类型、线上键实参、`ClientMessageType` 成员、端点实参与顺序全部一致。`ProtocolModule.ConfigureServices` 全文仅 4 条语句，不读 `NetworkingOptions`，无任何 `Enabled` 分支——注册无条件生效这一既有性质得以保持。 |
| CSC-5 | 编译期分层未放宽 | pass | `Dfo.Server.Networking.csproj` 的 diff **为空（逐字未变）**，仍只引用 Abstractions 与 Data，不引用 Application 与 Protocol。`Dfo.Server.Protocol` → {Abstractions, Data, Networking}，无环。`TreatWarningsAsErrors` 仍为 true，零抑制。 |
| CSC-6 | 本次 diff 不含逻辑变更 | pass | 复核者对 `git diff -M -- Server/src` 全量过滤掉 `using`/`namespace` 行后，净变更**只剩 5 处**：`Dfo.Server.Host.csproj` 加一行 `ProjectReference`、`Program.cs` 加 `new ProtocolModule()`、`NetworkingModule.cs` 纯删除（3 个 hunk 全为 `-` 行、零 `+` 行）、`NetworkPlugins.cs` 的 cref → `<c>0x01</c>`、`ChannelProtocol.cs` 的 cref 加命名空间限定。43 个被移动文件每个只改一行 namespace，其中 8 个各多一行 `using Dfo.Server.Networking.Framing;`。高危项独立确认：`AccountCharacterRosterResponseEncoder` 的 `command:` 仍为字面量 `0x00`；三个加密 Channel 编码器 `0x06→false / 0x0A→true / 0x12→true`；`ChannelEncryptedFrame` 仍为 `Encode(0x7C, messageNumber, 0, 1, body)`；`SelectCharacterStaticBodyFamilies.cs` 的 rename similarity 99%，唯一变更是 namespace。 |
| CSC-7 | 线级可观察行为不变 | pass | 全量测试 0 失败。`ChannelConnectIntegrationTests`、`ChannelControlMessagesIntegrationTests`、`GameLoginIntegrationTests`、`GameExitIntegrationTests`、`UnknownInboundFrameIntegrationTests`、`OutboundSendFailureTests`、`TouchSocketNetworkMessageSenderProjectionTests` 全部通过；六个被触碰的集成测试文件各恰好 **+2 行 / -0 行**（一行 using、一行注册 `ProtocolModule`），断言零改动。 |
| CSC-8 | 可见性放宽取最小必要范围 | pass | **类型修饰的可见性放宽为零**：`git diff -M -- src` 中不存在任何 `public`/`internal`/`protected`/`private` 修饰的净变更（唯二匹配是 `NetworkingModule` 中两个已空私有方法的整体删除，属路由表迁移而非可见性变动）。唯一的可见性表面变动是 `Dfo.Server.Protocol.csproj` 新增 `InternalsVisibleTo Include="Dfo.Server.Protocol.Tests"`——与基线上 `Dfo.Server.Networking` → `Dfo.Server.Networking.Tests` 的同型授予净持平，其存在是因为 11 个测试所验证的 codec 在迁移后仍保持 `internal`（即以 friend 授予替代了放宽类型可见性，是更小的选择）。此项按复核发现 F-7 显式列出。 |
| CSC-9 | 无回归 | pass | 全量 580 项，515 通过、65 跳过、0 失败、0 警告。分布 Pvf 60、Application 266、Networking 45、Protocol 84、Infrastructure 121、Host 4。**Networking 45 + Protocol 84 = 129**，与基线 Networking 的 129 项精确吻合，总数与跳过数均与基线一致。无测试被删除或被 `Skip`；11 个迁入 `Dfo.Server.Protocol.Tests` 的文件每个只有一个文件头 hunk，断言逐字未改。 |

## 实际运行的检查

- `dotnet build 86JP.Server.slnx -nodereuse:false` — 已成功生成，0 警告 0 错误。
- `dotnet test 86JP.Server.slnx -nodereuse:false` — 580 项，0 失败（分布见 CSC-9）。
- `git rev-parse HEAD` — `feef1a5`，与接受基线一致，未提交。
- `git diff feef1a5 -M --stat` — 67 个跟踪文件变动；重命名配对完整识别。
- Networking 项目文件列示与逐个职责判定；`Dfo.Server.Protocol` 目录树列示。
- 共用件唯一性检索：`ChannelEncryptedFrame` 与 `SelectCharacterResponseComposer` 各命中一处；`SelectCharacterStaticBodyFamilies.cs` 无同名类型（该文件是 `SelectCharacterStaticPayloadCodec` 的 partial 部分），故不存在被复制的共用件。
- 独立子代理复核（`reviews/change-review.md`）：**approved，0 阻断**；其自行取基线版本逐文件比对 43 个被移动文件、独立切出两个路由表方法体做逐字节 diff、独立验证引用图与断言未削弱，并自行完成一次构建与全量测试。

## 已知且被接受的偏差与局限

- **切片名 `AccountRoster` 而非 `AccountCharacterRoster`。** 后者会与 `Data.Characters` 的同名 record 冲突（CS0118：C# 名称查找中外围命名空间名优先于 using 引入的类型名），实测编译失败。复核者独立验证了该必然性并评估了四个备选，判定现选方案是"不往迁移 diff 里塞正文改动"这一硬约束下的最佳解。
- **`NetworkPlugins.cs` 的 `<see cref="GameProtocol.Command"/>` 降级为 `<c>0x01</c>`。** `GameProtocol` 迁入 Protocol 后 Networking 无法引用它，保留 cref 会产生 CS1574 而 `TreatWarningsAsErrors` 为 true。已确认 `GameProtocol.Command == 0x01` 与改写值一致，该文件 `--numstat` 为 `1 1`，确为唯一改动。代价是丢失一个符号跳转链接。
- **`ChannelProtocol.cs` 的 cref 经历两次改动、净结果是加命名空间限定。** T-002 时 `GameProtocol` 尚在 Networking 且不可见，该 cref 被暂改为纯文本；T-003 后恢复为 `<see cref="Game.GameProtocol"/>`。最终 diff 只体现净变化。
- **`Dfo.Server.Networking.Tests` 现引用 `Dfo.Server.Protocol`。** 其集成测试拉起真实宿主，须注册 `ProtocolModule` 才能获得解包器与编码器。这是测试项目的引用，不影响生产分层（复核者独立确认）。
- **`SmallReportPayloadCodecTests` 落在 `Game/` 根而非某个切片内。** 它同时覆盖 `RosterFollowUpPayloadCodec` 与 `TaggedIdReportPayloadCodec` 两个切片的 codec，无单一归属。
- **两个 `*Protocol.cs` 与 `ChannelEncryptedFrame.cs` 位于端点根而非切片内。** 前者是该端点线上号的全量登记（与用户保留消息枚举的理由同构），后者被三个加密 Channel 编码器共用。契约的 Scope/In 明确允许共用件落在"一处明确的共享位置"。
- **Plan 曾把 Game 切片数写作 12，实际为 11。** 复核发现 F-1，已更正 Plan 文本；切片覆盖本身完整无缺，仅为计数笔误。
- 基线已知的 `Dfo.Server.Host.Tests` 瞬时失败在本次验证与复核者的运行中均未复现（两次均 4/4）。
