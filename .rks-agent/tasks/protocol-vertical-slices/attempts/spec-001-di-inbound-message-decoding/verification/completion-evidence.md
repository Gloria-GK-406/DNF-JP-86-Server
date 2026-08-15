---
kind: rks-verification
decision: pass
---

# Completion Evidence: spec-001 入站解包由 DI 注册表驱动

## 被评估的主张

入站"线上帧键 → 类型化消息"的映射已由 DI 注册的解包器提供，两处 `CreateMessage` 分支消失；接入一个入站协议不再需要修改任何既有协议的解码或注册；已注册解包器与消息 handler 之间的接线缺口在启动时被发现，而只登记未接线的目录成员不再导致启动失败；且现有 14 个入站协议的解码结果与静默丢弃行为逐字不变。

评估对象为任务工作树 `codex/protocol-vertical-slices` 的未提交工作区状态，HEAD 仍为 `06b24e7`。全部证据在复核补救（`RequireDeclaredProductions` 前置校验、闭合性检查改为一次报告全部缺口）之后重新采集。

## 逐条核对

| ID | 条件 | 判定 | 证据 |
|---|---|---|---|
| CSC-1 | Networking 中不存在按消息号或 type 选择消息构造的分支 | pass | `grep -rn "CreateMessage" Server/src/` 返回 NONE。`NetworkPlugins.cs` 的两个 `Decode` 方法各自只做一次注册表查表，无按协议分支。`NetworkingModule.RegisterInboundDecoders` 中 `AddGameMessageDecoder<` 与 `AddChannelMessageDecoder<` 合计 14 条，与 `Framing/Decoders/` 下 14 个文件一一对应。 |
| CSC-2 | 14 个入站协议的解码结果与基线逐字段一致 | pass | `git diff --stat Server/src/Dfo.Server.Networking/Framing/` 为空——既有 `*PayloadCodec.cs` 零改动，解码字节逻辑未被触及；解包器仅转发。`Dfo.Server.Networking.Tests` 121 项全通过，含 GameLogin、ChannelConnect、ChannelControlMessages、GameExit 四组线级集成测试与全部 codec 向量测试，断言未修改。独立复核者另行逐条比对了旧 switch 的 14 个分支与新注册的 command/type 实参、产出消息与失败条件，结论为逐字等价。 |
| CSC-3 | 未注册线上帧键、command 非 `0x01` 的 Game 帧、解包器不产出消息三种情形均被静默丢弃 | pass | `UnknownInboundFrameIntegrationTests` 通过。该测试对三种情形各发一帧，随后发一个 EXIT 帧并逐字节断言其响应——任一被丢弃的帧若产生过任何出站字节，该断言都会读到错位数据而失败，故它是位置证明而非超时证明。 |
| CSC-4 | 接入新入站协议只需新增该协议自己的文件与注册 | pass | 实测探针：为线上 type `0x0117` 接入一个协议，全量测试通过后精确撤销。实际 footprint 为新增 `Probe0117MessageDecoder.cs`、`Probe0117Handler.cs` 两个文件，加上三处纯追加——目录枚举成员与消息记录（`NetworkMessages.cs`）、解包注册一行（`NetworkingModule.cs`）、处理器注册一行（`ApplicationModule.cs`）。无任何既有解包器、既有注册项或共享映射代码被修改；`NetworkPlugins.cs` 未被触及，而在基线上该文件必须新增一个 switch 分支。撤销后 `git diff` 对 `NetworkMessages.cs` 为空。 |
| CSC-5 | 只登记而未接线的目录成员不导致启动失败 | pass | `NetworkMessageDispatcherTests.BuildRegistry_AcceptsCataloguedMessageTypesThatAreNotWired` 通过：仅 `ChannelConnect` 有 handler 与产出登记，目录其余 13 个成员既无解包器也无 handler，注册表构造成功。 |
| CSC-6 | 已注册解包器产出的消息类型若无 handler，启动即失败并指明缺口 | pass | `NetworkMessageDispatcherTests.BuildRegistry_RejectsADecoderProductionThatNoHandlerConsumes` 通过：断言异常同时含消息类型名与申报它的解包器类型名。启动期触发由既有 `MessageHandlerRegistryValidationService`（`IHostedService`）保证，与 `Networking:Enabled` 无关；解包器注册位于 `NetworkingModule` 的 `Enabled` 判断之前，故监听关闭时该检查同样生效。 |
| CSC-7 | 编译期分层未放宽 | pass | `Dfo.Server.Networking.csproj` 的 `ProjectReference` 仍仅为 Abstractions 与 Data；`Dfo.Server.Abstractions.csproj` 仍仅为 Data。`git diff --name-only -- '*.csproj'` 为空——本成果未修改任何 csproj。全源码检索 `NoWarn`/`#pragma warning`/`SuppressMessage` 无匹配，`Directory.Build.props` 的 `TreatWarningsAsErrors` 仍为 `true`。 |
| CSC-8 | 无回归 | pass | `dotnet test 86JP.Server.slnx` 全量：572 项，507 通过、65 跳过、0 失败，0 警告。对齐 563 项基线并净增 9 项（Networking +8：7 项注册表测试与 1 项丢弃语义集成测试；Application +1：移除 1 项、新增 2 项）。跳过数 65 与基线一致。**唯一被移除的断言**是 `BuildRegistry_RejectsMissingMessageTypeRegistrations`，其断言的"枚举成员必须全部注册"正是本成果依 TaskContract 有意移除的行为，替代断言见 CSC-5 与 CSC-6。`BuildProvider` 中删除的按枚举回填 handler 的代码是脚手架而非断言。 |

## 实际运行的检查

- `cd /d/project/DNF && dotnet test DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx --nologo -nodereuse:false` — Pvf 60（51/9）、Application 266（256/10）、Networking 121（121/0）、Infrastructure 121（75/46）、Host 4（4/0）；0 失败。
- `git rev-parse HEAD` — `06b24e72acddc230a74f54f671eec416f7f3a15b`，与接受基线一致，未提交。
- `git diff --stat Server/src/Dfo.Server.Networking/Framing/` — 空。
- `git diff --name-only -- '*.csproj'` — 空。
- `grep -rn "CreateMessage" Server/src/` — 无匹配。
- `grep -rn "Enum.GetValues<ClientMessageType>" Server/` — 无匹配。
- 源码范围检索 `NoWarn|#pragma warning|SuppressMessage|TreatWarningsAsErrors` — 仅 `Directory.Build.props:6` 的 `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`。
- 独立子代理复核（`reviews/change-review.md`）：approved-with-findings，0 阻断；其自行复算了既有 codec 零改动、14 条注册等价、`InvokeNext()` 次数等价、`productions` 空集不抛出。

## 已知且被接受的局限

以下为复核提出、经评估不在本成果边界内修复的事项，记录以免后续误判为遗漏：

- 解包器"申报的产出类型"与其"实际产出类型"之间无强制一致性。一个申报 A 却返回 B 的解包器不会被闭合性检查发现；若 B 恰无 handler，失败仍会推迟到运行时派发。`GetUserInfoMessageDecoder` 是该风险最集中处（一个线上键条件性产出）。当前由代码复核而非机制保证。
- `RegisterInboundDecoders` 使用 `AddSingleton` 而非 `TryAdd`，故 `NetworkingModule.ConfigureServices` 若被重复调用会因重复线上键而抛出。这与 `ApplicationModule` 中命令与 handler 注册的既有写法一致，且模块在组合根只注册一次。
- 基线已知的 `Dfo.Server.Host.Tests` 瞬时失败在本次验证的多次运行中均未复现（每次 4/4 通过）。
