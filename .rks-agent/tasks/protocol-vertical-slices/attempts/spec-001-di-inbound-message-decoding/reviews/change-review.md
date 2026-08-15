---
kind: rks-change-review
contract:
  path: ../TaskContract.md
  revision: 1
spec:
  path: ../spec.md
plan:
  path: ../plan.md
baseline: 06b24e7
verdict: approved-with-findings
---

# 变更复核：DI 驱动的入站消息解包

## 结论

**approved-with-findings**

**阻断性发现：0 项。**

逐协议比对结果：14 个入站协议的「注册键 + 解包器」组合与基线两处 `CreateMessage` 的分支**逐字等价**——相同的产出消息、相同的构造参数与顺序、相同的失败/不产出条件、相同的异常传播路径。静默丢弃语义未变，两个 plugin 的 `e.InvokeNext()` 调用次数在全部路径上与旧代码一致。四条硬性约束全部通过独立验证。全量测试 572 项 507 通过 / 65 跳过 / 0 失败 / 0 警告，相对 563 项基线净增 9 项新测试且跳过数不变。

判定为 `approved-with-findings` 而非 `approved`，是因为存在 6 项非阻断发现，其中 F-1（CSC-4 / RQ-002 / AC-002 / CK-011 的探针证据缺失）与 F-2（实现阶段的 `spec-result.md` 未产出，导致「须在结果中说明的断言改动」无落点）属**证据条款未被覆盖**，需要在进入 verification 阶段前补齐，而不是代码缺陷。

---

## 阻断性发现

无。

以下是被重点怀疑、经核实**不成立**的几项，列出以说明复核已实际触及：

| 怀疑 | 核实结论 |
|---|---|
| 删除 `frame.Command != GameProtocol.Command` 后，非 `0x01` command 的帧可能被接受 | 不成立。10 条 Game 注册的 command 实参全部是 `GameProtocol.Command`（`NetworkingModule.cs:87-116`），无一例外；`GameProtocol.Command = 0x01`。查表键为 `(byte Command, ushort Type)` 元组，非 `0x01` 的 command 必然未命中。且已通过线级测试实证（见下）。 |
| Channel plugin 删除 `if (message is null) { await e.InvokeNext(); return; }` 早返回后，`InvokeNext()` 次数可能改变 | 不成立。旧代码三条路径各调用一次：非 Channel 帧 → 行 69 一次；message 为 null → 行 57 一次后 return；message 非 null → dispatch 后落到行 69 一次。新代码所有路径统一在 `NetworkPlugins.cs:63` 调用一次。旧早返回本就是冗余写法。 |
| 帧适配器可能在 command 非 `0x01` 时就丢弃帧，使集成测试的 AC-008 断言形同虚设 | 不成立。`GameClientFrameRequestInfo.OnParsingHeader` 只读取 command 不校验（`GameClientFrameRequestInfo.cs:24-37`），`GameClientFrameDataHandlingAdapter` 亦无校验。command `0x02` 的帧确实到达 plugin，测试确实穿过了查表未命中路径。 |
| 各解包器申报的产出类型可能与其实际产出的消息类型不符，使闭合性检查检错对象 | 不成立。14 条申报逐一比对了目标消息记录的 `MessageType` 实现，全部一致（比对表见下）。 |
| `GameLoginPayloadCodec.TryDecode` 归一时可能把「返回 false 即丢弃」改成抛出 | 不成立。codec 零改动，`GameLoginMessageDecoder.cs:16-18` 原样调用 `TryDecode` 并在 false 时返回 null，与旧分支体逐字相同。 |
| 解包器为 singleton，可能持有状态 | 不成立。14 个解包器全部无字段、无可变状态，均为纯转发或纯构造。 |
| 测试断言可能被越界削弱 | 不成立。仅 `NetworkMessageDispatcherTests.cs` 被修改；被移除的断言只有 `BuildRegistry_RejectsMissingMessageTypeRegistrations` 一处（属允许项）。`BuildProvider` 中删除的是枚举回填脚手架，非断言，且其删除反而收紧了两项既有测试的隔离度。既有 codec 向量测试与四组线级集成测试文件零改动。 |

---

## 非阻断性发现

### F-1（证据缺口，中）CSC-4 / RQ-002 / AC-002 / CK-011 无探针证据

TaskContract CSC-4 要求的证据是「一个探针协议的接入 diff，仅含新增文件与注册行」；plan T-005 步骤 3 明确要求在临时分支状态下做一次探针接入并记录触及文件集合。当前 attempts 目录下不存在任何记录该探针的制品：

```
.rks-agent/tasks/protocol-vertical-slices/attempts/spec-001-di-inbound-message-decoding/
  TaskContract.md  spec.md  plan.md  routing-decision.md  sdd-execution.yaml
```

代码结构本身支持该主张（新增一个协议 = 新增一个 `Framing/Decoders/*.cs` + `RegisterInboundDecoders` 一行），但 CSC-4 要求的是 diff 证据而非结构论证。**建议**：进入 verification 前补做一次探针并记录，或由控制器显式将该条证据降级并说明理由。

顺带一个精确性说明：Channel 侧新增协议实际需要**两处**共享文件改动——`ChannelProtocol.cs` 加一个常量 + `NetworkingModule.cs` 加一行注册。Game 侧同理需要 `GameProtocol.cs` 加常量。这仍满足 RQ-002 的「不修改任何既有协议的解码单元、既有注册项」，但与 CSC-4 字面的「一行注册」略有出入，补证据时应如实记录。

### F-2（证据缺口，中）实现阶段的 `spec-result.md` 未产出

`sdd-execution.yaml` 声明 `artifacts.result: "./spec-result.md"`，该文件不存在。TaskContract Constraints 明确要求「仅当某测试断言的行为本身被本成果有意移除时方可修改其断言，**且须在结果中说明**」，CSC-8 的证据也要求「说明任何断言改动」。断言改动本身合规（见上），但其书面说明目前没有落点。同样，Assumptions 中「若归一过程中发现 codec 依赖 `Networking.Configuration` 的反例，须在结果中记录」——本次未归一 codec，`GameLoginPayloadCodec` 仍是唯一依赖 `Networking.Configuration` 的入站 codec，该事实也应在结果中记录以支撑根任务第三个成果的切片落点。

### F-3（设计残余风险，中）产出申报与实际产出之间无强制一致性

`ClientMessageProductionRegistration` 是**手写申报**，没有任何机制保证它与解包器实际返回的消息类型一致。当前 14 条申报全部正确（已逐条核对），但闭合性检查的强度上限就是申报的诚实度。最典型的隐患是 `GetUserInfoMessageDecoder`——它是唯一一个「一个线上帧键按 body 分流」的解包器，目前只申报 `GameGetAccountCharacterRoster`；若将来为其他 mode 增加产出而忘记补申报，闭合性检查会静默漏过，退化回「运行时收到该消息才暴露」——恰是本任务要消除的那类缺口。

这是所选方案的固有取舍（`ClientMessageDecoding.cs:41-49` 的注释已阐明为何必须是扁平申报），不构成本次变更的缺陷。**建议**记入根任务的已知风险，供第三个成果考虑是否补一层运行时校验（例如 dispatcher 在派发时断言 `message.MessageType` 属于该解包器的申报集合）。

### F-4（健壮性，低）`RegisterInboundDecoders` 破坏了 `NetworkingModule` 原有的幂等性

`NetworkingModule.ConfigureServices` 其余全部登记都用 `TryAddSingleton`（`NetworkingModule.cs:44-53`），即对同一 `IServiceCollection` 重复调用是安全的。新增的 `AddGameMessageDecoder` / `AddChannelMessageDecoder` 内部用的是 `AddSingleton`（`ClientMessageDecoding.cs:66-67, 82-83`），不幂等：重复调用 `ConfigureServices` 会让 `GameMessageDecoderRegistry` 在启动时抛出「Game wire key 0x01/0x0001 was registered more than once」。

当前不构成缺陷——`ServerModuleRegistrationExtensions.AddServerModules` 按模块名查重，Host 路径不会重复调用；5 个集成测试各自只调用一次。但这条隐含不变量从「模块自身保证」下沉成了「调用方保证」，且失败信息会指向一个看起来像配置错误的重复注册，排查成本不低。**建议**在结果中记录，或让注册扩展改用 `TryAddSingleton` + `TryAddEnumerable`。

### F-5（健壮性，低）`AddProductions` 在抛出前已修改了 `IServiceCollection`

`ClientMessageDecodingExtensions.AddGameMessageDecoder`（`ClientMessageDecoding.cs:66-68`）先执行 `AddSingleton<TDecoder>()` 与 `AddSingleton(new GameMessageDecoderRegistration(...))`，再调用 `AddProductions`；后者在 `produces` 为空时抛 `ArgumentException`（`ClientMessageDecoding.cs:95-101`）。异常抛出时 `services` 已被部分改写：解包器与线上帧键登记已进入集合，产出登记没有。若调用方捕获该异常继续构建容器，会得到一个「已占用该线上帧键但对闭合性检查不可见」的解包器——正是这条检查存在的目的所要防止的状态。

现有测试 `Registration_RejectsADecoderThatDeclaresNoProducedMessageType` 用完即弃 `ServiceCollection`，未暴露该问题。**建议**把空集校验提到任何 `AddSingleton` 之前。

### F-6（一致性，低）两处措辞与排序小瑕疵

- `NetworkingModule.cs:4` 的 `using Dfo.Server.Data.Networking;` 插在 `Dfo.Server.Networking.Configuration` 之后，未按字母序排列（应在其之前）。编译 0 警告，仓库无相关分析器强制，纯风格不一致。
- `MessageHandlerRegistry` 的闭合性检查用 `FirstOrDefault` 只报告**第一条**缺口（`MessageHandlerRegistry.cs:44-45`），而被替换的旧检查用 `string.Join` 一次列出全部缺失项。CSC-6 只要求「指明缺口」，故满足；但在一次接入多个协议时会退化成逐条修复。

---

## 逐条契约核对表

| ID | 判定 | 证据 |
|---|---|---|
| **CSC-1** `Dfo.Server.Networking` 中不存在按消息号或 type 选择消息构造的分支 | **满足** | 全仓 `grep -rn "CreateMessage" --include=*.cs`（排除 `obj/`）**零匹配**。代码审阅：`NetworkPlugins.cs:66-76`（Channel）与 `:107-117`（Game）已退化为「查表 → 若命中则解码」，两处 `Decode` 方法体内无任何 `switch`、无任何按帧键的条件分支。全部 14 个解包器（`Framing/Decoders/*.cs`）各自只有一条表达式体，无分支。唯一残留的按值分流在 `GetUserInfoPayloadCodec.cs:41-45`，那是**帧体 mode 分流**而非帧键分流，属 Non-Goals（codec 字节语义）明确排除的范围。 |
| **CSC-2** 14 个入站协议的解码结果与基线逐字段一致 | **满足** | ① `git diff --stat -- Server/src/Dfo.Server.Networking/Framing/` 输出为**空**，`git status --short` 该目录下只有两个 `??`（新增的 `ChannelProtocol.cs` 与 `Decoders/`）——既有 `*PayloadCodec.cs` **零改动**，独立验证通过。② 逐协议比对表见下，14/14 逐字等价。③ 既有 8 组 codec 向量测试与 4 组线级集成测试文件均未出现在 `git status` 中，断言未改，全部通过（`Dfo.Server.Networking.Tests` 121/121）。 |
| **CSC-3** 三种情形均被静默丢弃 | **满足** | `UnknownInboundFrameIntegrationTests.FramesWithNoDecoder_AreDroppedSilentlyAndLeaveTheConnectionUsable` 在同一条真实 TCP 连接上依次覆盖三种情形：未注册帧键（`cmd=0x01, type=0x7FFF`）、非 `0x01` command（`cmd=0x02, type=0x0003`）、解包器不产出（`cmd=0x01, type=0x0008, body=[0xFF,0xFF,0x01]`，target=`0xFFFF` 但 mode=`0x01`≠`0x02`）。**证明方式是位置性的而非超时性的**：每个被丢弃的帧后紧跟一个 EXIT 帧并逐字节比对其已知应答，若前序帧回送过任何字节，该字节会先到达而使 `Assert.Equal` 失败。同时断言无异常（测试不吞异常）、连接保持可用（后续请求仍得应答）。这是一个比「等超时」强得多的断言设计。 |
| **CSC-4** 接入新协议只需新增该协议自己的文件与一行注册 | **部分满足（证据缺口）** | 结构上成立：`Framing/Decoders/` 一协议一文件，`NetworkingModule.RegisterInboundDecoders` 一协议一行，注册表按帧键查表，新增互不影响。但 TaskContract 要求的证据是「一个探针协议的接入 diff」，该制品**不存在**。另见 F-1 关于 Channel/Game 常量文件也需一行的精确性说明。 |
| **CSC-5** 只登记而未接线的 `ClientMessageType` 成员不导致启动失败 | **满足** | `MessageHandlerRegistry` 已删除 `Enum.GetValues<ClientMessageType>()` 完备性判定，改为只对 `productions` 求闭包（`MessageHandlerRegistry.cs:43-51`）。`productions` 为空集合时 `FirstOrDefault` 返回 null，不抛出——空集行为已核实正确。测试 `BuildRegistry_AcceptsCataloguedMessageTypesThatAreNotWired` 只接线 `ChannelConnect` 一个成员、其余 13 个目录成员既无解包器也无处理器，注册表构造成功。 |
| **CSC-6** 已注册解包器申报的产出若无 handler，启动即失败并指明缺口 | **满足** | `MessageHandlerRegistry.cs:44-51` 抛出的 `InvalidOperationException` 同时指名**产出的消息类型**与**申报它的解包器类型**。测试 `BuildRegistry_RejectsADecoderProductionThatNoHandlerConsumes` 双重断言两者都在消息中。「启动即失败」这一时序由 `MessageHandlerRegistryValidationService`（`NetworkMessageDispatchingExtensions.cs:33-41`，既有设施）保证：它是 `IHostedService`，构造函数依赖 `MessageHandlerRegistry`，因此注册表在 host 启动时被强制构造，**与 `Networking:Enabled` 是否为 true 无关**——这正好兑现了 `RegisterInboundDecoders` 置于 `Enabled` 判断之前的设计意图。 |
| **CSC-7** 编译期分层未放宽 | **满足** | `Dfo.Server.Networking.csproj` 的 `ProjectReference` 仅 `Dfo.Server.Abstractions` 与 `Dfo.Server.Data`，**无** `Dfo.Server.Application`。`Dfo.Server.Abstractions.csproj` 未新增任何 `ProjectReference`（仍只有 `Dfo.Server.Data`），新增的 DI 扩展依赖既有的 `Microsoft.Extensions.DependencyInjection.Abstractions` 包引用。接缝落在 Abstractions 是两侧唯一共同引用层，未引入新边。全仓 `git status` 中**无任何 `.csproj` 被修改**。 |
| **CSC-8** 无回归 | **满足** | 全量 572 项 / 507 通过 / 65 跳过 / **0 失败** / **0 警告**，退出码 0。相对基线 563 项（498 通过 / 65 跳过）净增 9 项，与新增测试数吻合（`ClientMessageDecoderRegistryTests` 7 项 + `UnknownInboundFrameIntegrationTests` 1 项 + `NetworkMessageDispatcherTests` 净增 1 项：1 项被替换为 2 项）。跳过数 65 与基线完全一致，说明无测试被静默禁用。`Dfo.Server.Host.Tests` 4/4 通过，**两次运行均未出现已知的端口竞争瞬时失败**。断言改动已在上文说明，但按 Constraints 还需落在 `spec-result.md` 中（见 F-2）。 |

### 逐协议等价性比对（14/14）

`GameProtocol.Command = 0x01`；元数据构造在两侧均为 `new GameMessageMetadata(frame.Command, frame.Type, frame.Checksum, frame.Sequence)` 与 `new ChannelMessageMetadata(frame.Classification, frame.MessageNumber, frame.Checksum, frame.Acknowledgement)`，与记录的位置参数定义顺序一致（`NetworkMessages.cs:94-98` / `:43-47`），**字段顺序两侧逐字相同**。

| # | 旧 switch 分支 | 新注册键 | 解包器 | 产出/申报 | 等价 |
|---|---|---|---|---|---|
| 1 | `GameProtocol.LoginType`(0x0001) → `TryDecode` ? `new GameLoginRequest(m, id, hash)` : null | `(0x01, 0x0001)` | `GameLoginMessageDecoder` | `GameLoginRequest` → `GameLogin` | ✓ 逐字，含 false→null 失败路径 |
| 2 | `GetUserInfoType`(0x0008) → `GetUserInfoPayloadCodec.Decode` | `(0x01, 0x0008)` | `GetUserInfoMessageDecoder` | `GetAccountCharacterRosterRequest` → `GameGetAccountCharacterRoster` | ✓ 整体转发，mode`0x02`+target`0xFFFF` 双条件与长度≠3 的判定全在未改动的 codec 内 |
| 3 | `SelectCharacterType`(0x0004) → codec | `(0x01, 0x0004)` | `SelectCharacterMessageDecoder` | `SelectCharacterRequest` → `GameSelectCharacter` | ✓（codec 返回具体类型，协变到接口，与旧 switch 相同） |
| 4 | `ClientSpecStatisticType`(0x00AB) → codec | `(0x01, 0x00AB)` | `ClientSpecStatisticMessageDecoder` | `ClientSpecStatisticReportRequest` → `GameClientSpecStatistic` | ✓ |
| 5 | `ClientHardwareDetailType`(0x01DD) → codec | `(0x01, 0x01DD)` | `ClientHardwareDetailMessageDecoder` | `ClientHardwareDetailReportRequest` → `GameClientHardwareDetail` | ✓ |
| 6 | `ClientSpecProfileType`(0x00C2) → codec | `(0x01, 0x00C2)` | `ClientSpecProfileMessageDecoder` | `ClientSpecProfileReportRequest` → `GameClientSpecProfile` | ✓ |
| 7 | `SessionMetricsType`(0x00FA) → codec | `(0x01, 0x00FA)` | `SessionMetricsMessageDecoder` | `SessionMetricsReportRequest` → `GameSessionMetrics` | ✓ |
| 8 | `TaggedIdReportType`(0x008F) → codec | `(0x01, 0x008F)` | `TaggedIdReportMessageDecoder` | `TaggedIdReportRequest` → `GameTaggedIdReport` | ✓ |
| 9 | `RosterFollowUpType`(0x02A8) → codec | `(0x01, 0x02A8)` | `RosterFollowUpMessageDecoder` | `RosterFollowUpRequest` → `GameRosterFollowUp` | ✓ |
| 10 | `ExitType`(0x0003) → `new GameExitRequest(m)`（不经 codec） | `(0x01, 0x0003)` | `GameExitMessageDecoder` | `GameExitRequest` → `GameExit` | ✓ 直接构造，逐字 |
| 11 | `ClientMessageType.ChannelScriptVersion`(0x05) → `new ChannelScriptVersionRequest(m)` | `0x05` | `ChannelScriptVersionMessageDecoder` | → `ChannelScriptVersion` | ✓ |
| 12 | `ChannelScript`(0x09) | `0x09` | `ChannelScriptMessageDecoder` | → `ChannelScript` | ✓ |
| 13 | `ChannelConnect`(0x0B) | `0x0B` | `ChannelConnectMessageDecoder` | → `ChannelConnect` | ✓ |
| 14 | `ChannelInfo`(0x11) | `0x11` | `ChannelInfoMessageDecoder` | → `ChannelInfo` | ✓ |

补充核实：
- 旧 Channel 分支的键来自 `(ClientMessageType)frame.MessageNumber` 强转，对应枚举值为 `ChannelScriptVersion=0x05, ChannelScript=0x09, ChannelConnect=0x0B, ChannelInfo=0x11`（`NetworkMessages.cs:7-10`）；`ChannelProtocol.cs` 的四个常量取值**完全相同**。`frame.MessageNumber` 是 `byte`，其余 252 个取值在两侧均不产出消息。
- 10 个 Game type 值**两两互异**，4 个 Channel 消息号**两两互异**，注册表构造期查重不会误报；无错配、无遗漏、无多余注册（`GameProtocol.AccountCharacterRosterNotificationType = 0x0002` 是出站类型，两侧均未注册，与基线一致）。
- **异常语义等价**：codec 抛出的 `FrameProtocolException` 等异常，旧路径从 `CreateMessage` 传出 `OnTcpReceived`；新路径从 `Decode` 在同一个 `if` 条件内传出 `OnTcpReceived`。传播点与调用栈深度不同，但对外可观察行为（异常类型、措辞、逃逸位置）相同。
- **空引用**：`decoder?.Decode(new …Metadata(…), frame.Body.Span)` 的 null 条件运算符会短路整个实参列表，未命中时元数据记录都不会被构造，无解引用风险。

### RQ / AC 核对摘要

| RQ | 判定 | 说明 |
|---|---|---|
| RQ-001 | 满足 | 见 CSC-1（AC-001） |
| RQ-002 | 部分满足 | 结构成立，AC-002 的探针证据缺失（F-1） |
| RQ-003 | 满足 | codec 零改动 + 向量测试断言未改（AC-003 / AC-004） |
| RQ-004 | 满足 | `GetUserInfoPayloadCodec` 零改动，mode+target 双条件与长度校验原封不动；AC-006 另有线级实证 |
| RQ-005 | 满足 | 三种情形均有线级实证（AC-007 / AC-008 / AC-009） |
| RQ-006 | 满足 | 消息自带 `MessageType`，dispatcher 依此选 handler；`DispatchAsync_ExecutesTheRegisteredHandlerExactlyOnce` 覆盖 AC-010 |
| RQ-007 | 满足 | AC-011，见 CSC-5 |
| RQ-008 | 满足 | AC-012，见 CSC-6 |
| RQ-009 | 满足 | AC-013，见 CSC-7 |

---

## 实际运行过的验证及其结果

| # | 验证 | 结果 |
|---|---|---|
| V-1 | `git status --short` + `git diff`（全量改动获取） | 4 个已跟踪文件被修改、5 组未跟踪新增；`.rks-agent/` 已按要求排除出代码复核范围。**无任何 `.csproj` / `Directory.Build.props` / `.editorconfig` 被修改。** |
| V-2 | `git diff --stat -- Server/src/Dfo.Server.Networking/Framing/` **（独立验证硬性约束，未采信任务描述）** | 输出**为空**。追加 `git status --short -- <同路径>` 只显示 `?? ChannelProtocol.cs` 与 `?? Decoders/`。既有 `*PayloadCodec.cs` **零改动**，约束通过。 |
| V-3 | `git show HEAD:Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs` 取旧版本，与新版本逐行比对 | 完成 14 条分支的逐字等价核对（表见上）与 `InvokeNext()` 控制流等价核对。 |
| V-4 | `grep -rn "CreateMessage" --include=*.cs .`（排除 `obj/`） | **零匹配** → CSC-1 / CK-006。 |
| V-5 | 检视 `Dfo.Server.Networking.csproj`、`Dfo.Server.Abstractions.csproj`、`Dfo.Server.Networking.Tests.csproj` 的引用图 | Networking → {Abstractions, Data}，**无 Application**；Abstractions → {Data} 未新增；`InternalsVisibleTo Include="Dfo.Server.Networking.Tests"` 为既有条目，未新增可见性放宽 → CSC-7 / CK-002 / CK-007。 |
| V-6 | `grep -rn "TreatWarningsAsErrors\|NoWarn\|WarningLevel"` + `grep -rn "#pragma warning\|SuppressMessage" Server/src Server/tests` | `Server/Directory.Build.props:6` 仍为 `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`，**未见任何 `NoWarn`**；全仓 `#pragma warning` / `SuppressMessage` **零匹配** → 未以抑制诊断换取编译通过。 |
| V-7 | `cd /d/project/DNF && dotnet test DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx --nologo -nodereuse:false`（仓库外 cwd，禁用节点复用） | 退出码 0。首次运行的输出被我的 `tail` 截断，故**完整重跑一次**取全量日志。 |
| V-8 | 全量日志逐项统计 | `Dfo.Pvf.Tests` 51/9/60、`Dfo.Server.Application.Tests` 256/10/266、`Dfo.Server.Networking.Tests` 121/0/121、`Dfo.Server.Infrastructure.Tests` 75/46/121、`Dfo.Server.Host.Tests` 4/0/4 → 合计 **通过 507 / 跳过 65 / 总计 572 / 失败 0**。对齐 563 基线并净增 9 → CK-010 / CSC-8。 |
| V-9 | 全量日志的 `warning` / `警告` / `错误` 检索 | **零匹配**，构建 0 警告 0 错误 → CK-001 / CK-003。 |
| V-10 | 两次全量运行中 `Dfo.Server.Host.Tests` 的稳定性 | 两次均 4/4 通过。**未复现**基线已知的端口竞争瞬时失败，无需按 Risks 一节做单套件重跑。 |
| V-11 | 逐条比对 14 条产出申报与目标消息记录的 `MessageType` 实现（`NetworkMessages.cs` 中 14 处 `MessageType => ClientMessageType.*`） | 14/14 一致，闭合性检查的检查对象正确 → 见 F-3 关于其强度上限的说明。 |
| V-12 | 检视 `MessageHandlerRegistry` 在 `productions` 为空集合时的行为 | `productions.FirstOrDefault(...)` 返回 null，不抛出；`registrations` 侧仍先 `.ToArray()` 且查重保留。空集行为正确 → CSC-5。 |
| V-13 | 追查「启动即失败」的时序保证 | `MessageHandlerRegistryValidationService`（既有 `IHostedService`）强制在 host 启动时构造 `MessageHandlerRegistry`，与 `Networking:Enabled` 无关 → CSC-6 的「启动即」成立。 |
| V-14 | 检视 `GameClientFrameRequestInfo` / `GameClientFrameDataHandlingAdapter` 是否在帧层校验 command | 均不校验，command `0x02` 的帧确实抵达 plugin → AC-008 的集成测试断言是真实覆盖，非空转。 |
| V-15 | 检视 `ServerModuleRegistrationExtensions.AddServerModules` 与 5 处 `NetworkingModule().ConfigureServices` 调用点 | 按模块名查重，Host 与各集成测试均只调用一次 → F-4 的不幂等性当前不构成缺陷。 |
| V-16 | 通读 `NetworkMessageDispatcherTests.cs` 全文，核对断言增删 | 唯一被移除的断言属允许项；`OtherHandler` 在第 48 行仍被使用（无未使用私有类型）；其余 3 项既有测试断言原样保留。 |
