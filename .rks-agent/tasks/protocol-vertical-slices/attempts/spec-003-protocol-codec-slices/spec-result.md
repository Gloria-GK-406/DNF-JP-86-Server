---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Protocol Codec Slices

## Delivered

- 新建 `Dfo.Server.Protocol` 编译单元，协议编解码与传输自此分属两个项目。协议内容按端点分层、端点内一协议一目录，共 **15 个切片**（Channel 4 + Game 11），覆盖 14 个入站协议与 9 条出站消息。
- 43 个协议文件自 `Dfo.Server.Networking` 迁入其所属切片。`Dfo.Server.Networking` 现存 17 个文件，逐个判定均为传输：帧头拆装、TouchSocket 适配、连接注册表、三个路由注册表、sender 与端点级载荷保护。
- 两张 DI 路由表与 `ISelectCharacterProjectionComposer` 注册迁入新的 `ProtocolModule`。**方法体逐字节未变**（复核者独立 diff 无输出），且注册仍无条件生效，不受 `Enabled` 开关影响。
- 11 个编解码向量测试迁入新建的 `Dfo.Server.Protocol.Tests`，按其被测协议归入对应切片目录；传输测试留在 `Dfo.Server.Networking.Tests`。两套件项数之和 45 + 84 = 129，与迁移前 Networking 的 129 项精确吻合。
- **零可见性放宽。** 被迁移的 `internal` 编解码类型迁移后仍同处一个编译单元，故全部保持原可见性；传输原语本已是 `public`。唯一的可见性表面变动是新项目对其测试项目的 `InternalsVisibleTo` friend 授予，与基线上 Networking 的同型授予净持平。
- **零逻辑变更。** 过滤 `using`/`namespace` 行后，`src` 下的净变更只剩 5 处，其中 3 处是新项目接线（csproj 引用、组合根模块、`NetworkingModule` 的纯删除），2 处是迁移导致的失效文档引用修复。

## Success Evidence

| Condition | Evidence |
|---|---|
| CSC-1 | `verification/completion-evidence.md`：切片目录树列示，15 个切片 |
| CSC-2 | 同上：逐切片核对解包与编码的同目录性 |
| CSC-3 | 同上：Networking 现存 17 个文件的逐个职责判定 |
| CSC-4 | 同上：复核者对两个路由表方法体的逐字节 diff，两次均无输出；`ProtocolModule` 无条件注册 |
| CSC-5 | 同上：`Dfo.Server.Networking.csproj` 的 diff 为空；引用图无环 |
| CSC-6 | 同上：过滤后净变更仅 5 处；高危字节常量逐项独立确认 |
| CSC-7 | 同上：七组线级集成测试通过，六个被触碰文件各 +2/-0，断言零改动 |
| CSC-8 | 同上：类型可见性净变更为零；`InternalsVisibleTo` 按复核发现 F-7 显式列出 |
| CSC-9 | 同上：全量 580 项，515 通过 65 跳过 0 失败；45 + 84 = 129 |

### 被修改的既有测试

无断言改动。6 个集成测试文件各 **+2 行 / -0 行**（一行 `using Dfo.Server.Protocol;`、一行 `new ProtocolModule().ConfigureServices(...)`）——其宿主须注册协议模块才能获得解包器与编码器。11 个迁入测试文件各只有一个文件头 hunk（namespace 与 using）。无测试被删除或被 `Skip`。

## Repository Result

- Changed paths：新增 `Server/src/Dfo.Server.Protocol/`（44 个 `.cs` + csproj）、`Server/tests/Dfo.Server.Protocol.Tests/`（12 个 `.cs` + csproj）；改 `Server/src/Dfo.Server.Networking/NetworkingModule.cs`、`Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs`、`Server/src/Dfo.Server.Host/Program.cs` 与其 csproj、`Server/86JP.Server.slnx`、`Server/tests/Dfo.Server.Networking.Tests/` 的 csproj 与 6 个集成测试；删除 `Server/src/Dfo.Server.Networking/Framing/` 下 33 个协议文件与 `Decoders/`、`Encoders/` 两个目录，以及 `Framing/` 下 11 个 codec；以及本尝试的流程制品。
- Baseline or accepted result：基于 `feef1a5`，由父控制器接受为一次已验证的本地结果提交。
- Parent rollback required：no

## Successor Guidance

- **切片骨架已就位，后继只需往已有目录里填。** 15 个切片目录已建立并被两张路由表接线。后继成果（DTO、消息 handler、命令与命令 handler 的迁移）的落点是确定的：每个 handler 与其命令进入同名切片。`InitialConnection` 是纯出站切片，无 handler；`AccountRoster` 同时承载 GET_USERINFO 入站与花名册出站，其 handler 是 `GetAccountCharacterRosterHandler`。
- **后继必须同时迁 DTO 与 handler，不可再分。** 本成果刻意把消息 DTO 留在 `Dfo.Server.Data`，因为若单独迁 DTO，仍留在 Application 的 14 个 handler 会反向依赖 Protocol，与 Protocol 对 Application 的依赖（`SelectCharacterCommand` 需要 `Application.Characters`/`Sessions`/`Inventory`/`OnlineUsers`，命令契约在 `Application.Commands`）构成循环。两者必须在同一个成果内一起移动。
- **`Dfo.Server.Protocol.csproj` 届时须新增对 `Dfo.Server.Application` 的引用。** 这是后继成果唯一需要的引用图变更，方向为 Protocol → Application，不触及 `Dfo.Server.Networking` 的引用图（其 csproj 在本成果中逐字未变，应继续保持）。
- **`Data/Networking/NetworkMessages.cs` 拆分时须留下接口与枚举。** `IClientNetworkMessage`、`IServerNetworkMessage`、`ClientMessageType`、`ServerMessageType`、`NetworkEndpointKind` 与两个 `*MessageMetadata` 被 Abstractions 的两个接缝、Networking 的传输侧与 Application 的派发器共同依赖，必须留在 `Dfo.Server.Data`；只有具体消息 record 随切片走。
- **`ApplicationModule` 的 14 条 `RegisterMessageHandler` 与 8 条 `AddCommandHandler` 中属协议的部分应迁入 `ProtocolModule`。** 迁移后须保持 `MessageHandlerRegistry` 的接线闭合性检查仍能同时看到解包器产出声明与 handler 注册——两者届时都在 `ProtocolModule`，比现状更近。
- **切片名 `AccountRoster` 是被迫的。** `AccountCharacterRoster` 与 `Data.Characters` 的同名 record 冲突（CS0118）。后继若迁入更多类型，须留意同类命名空间/类型名冲突，尤其 `SelectCharacter`、`SessionMetrics` 这类与 Data 类型同名风险较高的切片名。
- **AGENTS.md 尚未更新。** 项目职责表、"新增一个协议的固定动作"与切片边界规则仍描述迁移前的形态，须由后继成果一并修订。

## Artifacts

- `spec.md`（RQ-001..RQ-011 / AC-001..AC-012 / EV-001..EV-007）
- `plan.md`（BD-001..BD-012 / T-001..T-005 / CK-001..CK-019）
- `reviews/change-review.md`（独立子代理复核，approved，0 阻断；7 项非阻断中 2 项已落实，其余记入验证证据的已知偏差）
- `verification/completion-evidence.md`（decision: pass）
