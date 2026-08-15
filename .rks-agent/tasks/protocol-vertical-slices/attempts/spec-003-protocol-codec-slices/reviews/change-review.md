---
kind: rks-review
decision: approved
blocking_findings: 0
---

# 变更复核：Protocol Codec Slices（spec-003）

复核对象：工作树 `D:\project\DNF\DNF-JP-86-Server\.worktrees\protocol-vertical-slices`，基线 `feef1a5`（HEAD 仍为它），全部改动未提交。
判据：`TaskContract.md`（CSC-1..CSC-9）、`spec.md`（RQ-001..RQ-011 / AC-001..AC-012）。

## 结论

**核心主张成立。** 本次 diff 在 `Server/src` 下除文件位置、命名空间、using、以及两处迁移直接导致的失效文档 `cref` 修复之外，**不含任何行为改动**，也**不含任何可见性修饰的改动**（一处都没有，连"最小必要放宽"都未发生）。两张路由表逐字节等价迁移。引用图未放宽。全部 11 个迁移测试文件的断言逐字未改，6 个集成测试各只加 2 行。构建 0 警告 0 错误，全量测试 580 项 / 0 失败，与基线一致。

阻断级问题：**0 项**。非阻断观察：7 项，均为命名/粒度/后继整理性质。

---

## 逐条核对表

### 1. 零逻辑变更（最重要）

**判定：通过。**

对 `Server/src` 全量 diff 做机械过滤，剔除所有 `+/-` 的 `using` 行与 `namespace` 行后，剩余的净变更**只有以下 5 处**：

```
git diff HEAD -M -- Server/src \
  | grep -E '^[+-]' | grep -vE '^[+-]{3}' \
  | grep -viE '^[+-]\s*(using |namespace )'
```

| # | 文件 | 变更 | 性质 |
|---|---|---|---|
| a | `Dfo.Server.Host/Dfo.Server.Host.csproj` | `+ <ProjectReference ...Dfo.Server.Protocol.csproj />` | 组合根接线（Scope/In 第 6 条） |
| b | `Dfo.Server.Host/Program.cs` | `new NetworkingModule()` → `new NetworkingModule(), new ProtocolModule()` | 组合根接线 |
| c | `Dfo.Server.Networking/NetworkingModule.cs` | **纯删除**（3 个 hunk，全为 `-` 行，零 `+` 行） | 迁出 |
| d | `Dfo.Server.Networking/Runtime/NetworkPlugins.cs:103` | `<see cref="GameProtocol.Command"/>` → `<c>0x01</c>` | 失效 cref 修复（Scope/In 明列） |
| e | `Dfo.Server.Protocol/Channel/ChannelProtocol.cs:4` | `<see cref="GameProtocol"/>` → `<see cref="Game.GameProtocol"/>` | 失效 cref 修复（同上，跨命名空间限定） |

**除此之外，43 个被移动的源文件每一个的 diff 都只有 `namespace` 一行改写，外加 6 个文件各多一行 `using Dfo.Server.Networking.Framing;`**（原先靠父命名空间隐式可见、迁移后须显式化：`ChannelEncryptedFrame`、`ChannelConnectResponseEncoder`、`AccountCharacterRosterResponseEncoder`、`GameExitResponseEncoder`、`GameInitialConnectionPayloadCodec`、`GameLoginResponseEncoder`、`SelectCharacterResponseComposer`、`SelectCharacterSupplementalProjectionCodec`）。这正是 TaskContract Current Situation 第 2 条预判的那一类。

字节布局相关的重点抽查（`git diff` 对这些文件均只报 1 行 namespace 变更，similarity 99%/97%/95%/96%/95%/85%）：

- `SelectCharacterStaticBodyFamilies.cs`（similarity 99%）：唯一变更为第 7 行 namespace。数百行静态字节表未动。
- `SelectCharacterStaticPayloadCodec.cs`（99%）、`SelectCharacterSupplementalProjectionCodec.cs`（99%）、`SelectCharacterStaticSequence.cs`（98%）、`SelectCharacterResponseComposer.cs`（95%）：同上，仅 namespace（+ 后两者各一行 using）。
- `ChannelInfoResponseEncoder.cs`（96%）、`GameLoginResponseEncoder.cs`（95%）：仅 namespace（+ 后者一行 using）。
- `AccountCharacterRosterResponseEncoder.cs`（85%，因文件短）：直接读取当前文件确认 `command:` 实参**仍是字面量 `0x00`**，未被"顺手"改成 `GameProtocol.Command`：

```csharp
One(GameServerFrameEncoder.Encode(
    command: 0x00,
    GameProtocol.AccountCharacterRosterNotificationType,
    AccountCharacterRosterPayloadCodec.Encode(message.Roster)));
```

三个加密 Channel 编码器的 `compress` 标志与共用信封参数（`grep -rn "compress\|classification\|0x7C\|ack" Server/src/Dfo.Server.Protocol/Channel/`）：

| 消息号 | compress | 来源 |
|---|---|---|
| `0x06` | `compress: false` | `Channel/ScriptVersion/ChannelScriptVersionResponseEncoder.cs:22` |
| `0x0A` | `compress: true` | `Channel/Script/ChannelScriptResponseEncoder.cs:12` |
| `0x12` | `compress: true` | `Channel/Info/ChannelInfoResponseEncoder.cs:28` |

`ChannelEncryptedFrame.cs:19` 仍为 `ChannelFrameEncoder.Encode(0x7C, messageNumber, 0, 1, body)`（classification `0x7C`、ack `1`），`Channel/Connect/ChannelConnectResponseEncoder.cs` 的 `classification: 0x7C` / `acknowledgement: 1` 亦未变。**全部与主张一致。**

异常措辞：diff 中无任何字符串字面量的 `+/-` 行，故所有异常措辞逐字保留。

### 2. 两张路由表的迁移是否逐行等价

**判定：通过（逐字节等价）。**

机械比对，不依赖目视：

```
git show feef1a5:Server/src/Dfo.Server.Networking/NetworkingModule.cs > base-nm.cs
sed -n '/private static void RegisterInboundDecoders/,/^    }$/p'  base-nm.cs                    > base-in.txt
sed -n '/private static void RegisterInboundDecoders/,/^    }$/p'  .../ProtocolModule.cs         > new-in.txt
sed -n '/private static void RegisterOutboundEncoders/,/^    }$/p' base-nm.cs                    > base-out.txt
sed -n '/private static void RegisterOutboundEncoders/,/^    }$/p' .../ProtocolModule.cs         > new-out.txt
diff base-in.txt new-in.txt     →  (无输出，identical)
diff base-out.txt new-out.txt   →  (无输出，identical)
```

14 条入站 + 9 条出站，**方法体逐字节相同**：类型、`GameProtocol.*` / `ChannelProtocol.*` 线上键实参、`ClientMessageType` 成员、`NetworkEndpointKind` 端点实参、注册顺序全部一致。零错位。

**注册无条件生效**（CSC-4 / RQ-005 / AC-005）：`ProtocolModule.ConfigureServices` 全文只有 4 条语句——两个 `ArgumentNullException.ThrowIfNull`、`TryAddSingleton<ISelectCharacterProjectionComposer, ...>`、以及两次路由表调用。**该模块完全不读取 `NetworkingOptions`，不存在任何 `Enabled` 分支**，比基线"在 `if (!options.Enabled) return;` 之前"更强。注释也相应从"Registration happens before the `Enabled` check on purpose"改写为"Registration is unconditional on purpose"——这是对迁移后事实的如实描述，非行为改动。

### 3. `NetworkingModule` 是否只丢了该丢的

**判定：通过。**

`git diff HEAD -M -- .../NetworkingModule.cs` 只有 3 个 hunk，**全部为删除，零新增行**：

1. `-using Dfo.Server.Data.Networking;`、`-using ...Framing.Decoders;`、`-using ...Framing.Encoders;`（3 个失效 using）
2. `-services.TryAddSingleton<ISelectCharacterProjectionComposer, SelectCharacterProjectionComposer>();`、`-RegisterInboundDecoders(services);`、`-RegisterOutboundEncoders(services);`
3. 两个路由表方法及其 XML 文档整体删除

通读当前 `NetworkingModule.cs` 全文确认保留部分逐字未变：`NetworkingOptions` section 解析（`Enabled` / `ChannelPort` 默认 7001 / `GamePorts` 默认 `{10011}`）、`ValidatePort` 及其 1–65535 边界与异常措辞、`GameLoginOptions.FromConfiguration`、`TouchSocketConnectionRegistry`、`INetworkMessageSender` / `INetworkConnectionCloser`、两个插件、三个注册表（`GameMessageDecoderRegistry` / `ChannelMessageDecoderRegistry` / `ServerMessageEncoderRegistry`）、`if (!options.Enabled) return;` 位置、两处 `AddTcpService` 接线与适配器。**符合 CSC-3：Networking 只剩传输。**

`Dfo.Server.Networking` 残留 `.cs`（排除 obj）共 17 个，逐个检视均为传输：`Configuration/{GameLoginOptions,NetworkingOptions}`、`Framing/{ChannelFrameRequestInfo, ChannelPayloadCipher, DnfFrameProtocol, FrameDataHandlingAdapters, FrameEncoders, FrameProtocolException, GameClientFrameRequestInfo}`、`NetworkingModule`、`Runtime/{ClientMessageDecoderRegistries, NetworkPlugins, ServerMessageEncoderRegistry, TcpServices, TouchSocketConnectionRegistry, TouchSocketNetworkConnectionCloser, TouchSocketNetworkMessageSender}`。**无任何与具体消息绑定的编解码内容。**（AC-004 成立）

### 4. 引用图

**判定：通过，无循环。**

`Dfo.Server.Networking.csproj` 的 `git diff` 为空——**逐字未变**，仍只引用 Abstractions 与 Data：

| 项目 | ProjectReference |
|---|---|
| Dfo.Server.Data | （无） |
| Dfo.Server.Abstractions | Data |
| Dfo.Server.Application | Abstractions, Data |
| **Dfo.Server.Networking** | **Abstractions, Data**（未变） |
| **Dfo.Server.Protocol** | **Abstractions, Data, Networking** |
| Dfo.Server.Infrastructure | Pvf, Abstractions, Data |
| Dfo.Server.Host | Abstractions, Application, Infrastructure, Networking, **+ Protocol** |

方向为 Protocol → Networking，Networking 既不引用 Application 也不引用 Protocol，Application 亦不引用 Networking/Protocol。**无环。CSC-5 / RQ-006 / AC-006 成立。** TaskContract Assumptions 中"切片项目本成果不需引用 Application"的前提得到证实（Protocol 的引用列表中确无 Application）。

### 5. 可见性

**判定：通过——独立核实"零可见性放宽"主张成立，且强于主张。**

对 `git diff feef1a5 -M -- Server/src` 全量过滤后（见第 1 项的命令与结果表），**不存在任何一行带 `public` / `internal` / `protected` / `private` 修饰的 `+/-` 变更**。43 个被移动文件全部保留原修饰：`internal sealed class` / `internal static class` / `internal static partial class` / `internal enum` 原样；`GameInitialConnectionPayloadCodec` 在基线上即为 `public static class`，非本次放宽。

因此 CSC-8 / RQ-008 / AC-008 空过（no widening to justify）。跨程序集访问之所以无需放宽，是因为 TaskContract 勘察结论第 1 条成立：`ChannelFrameEncoder`、`GameServerFrameEncoder`、`DnfFrameProtocol`、`ChannelPayloadCipher`、`GameLoginOptions`、`ServerMessageEncoder<T>` 与两个 `Add*MessageDecoder` / `AddServerMessageEncoder` 扩展在基线上已是 `public`。

唯一新增的可见性**授予**是 `Dfo.Server.Protocol.csproj` 中的 `<InternalsVisibleTo Include="Dfo.Server.Protocol.Tests" />`。这不是类型修饰的放宽，且与基线上 `Dfo.Server.Networking.csproj` 对 `Dfo.Server.Networking.Tests` 的同名授予形态完全一致、覆盖的是同一批 internal 编解码类型——相对基线为**净持平**，非放宽。属必要（11 个迁入测试直接调用 internal codec）。

### 6. 测试断言未被削弱

**判定：通过。**

**11 个迁入 `Dfo.Server.Protocol.Tests` 的文件**：`git diff HEAD -M -- Server/tests` 显示每个文件**各只有 1 个 hunk，且该 hunk 全部位于文件头部（`@@ -1,x +1,y @@` 或 `@@ -2,8 +2,9 @@` 等）**，内容仅为 `using` 替换与 `namespace` 重写。文件正文（`[Fact]` / `[Theory]` / `Assert.*`）**零改动**。相似度 96%–99%，与"只改文件头"吻合。

**6 个集成测试文件**（留在 `Dfo.Server.Networking.Tests/Integration/`）：逐个确认恰好 **2 行新增、0 行删除、0 行修改**：

| 文件 | +1 | +2 |
|---|---|---|
| `ChannelConnectIntegrationTests.cs` | `using Dfo.Server.Protocol;` | `new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);` |
| `ChannelControlMessagesIntegrationTests.cs` | 同上 | 同上 |
| `GameExitIntegrationTests.cs` | 同上 | 同上 |
| `GameLoginIntegrationTests.cs` | 同上 | 同上 |
| `OutboundSendFailureTests.cs` | 同上 | 同上 |
| `UnknownInboundFrameIntegrationTests.cs` | 同上 | 同上 |

每处 `ProtocolModule` 调用均紧跟在既有 `new NetworkingModule().ConfigureServices(...)` 之后，模块顺序与 `Program.cs` 一致。断言零改动。（AC-010 / AC-011 / RQ-011 成立）

**无测试被删除**：迁走 11 个后 `Dfo.Server.Networking.Tests` 仍保留 12 个 `.cs`（4 个传输 Framing 测试 + 8 个 Integration/Runtime 文件），加上 Protocol.Tests 的 11 个，总数守恒；测试计数 45 + 84 = 129 与基线 Networking.Tests 的 129 吻合。

**无测试被 Skip**：`grep -rn "Skip *=" Server/tests/Dfo.Server.Protocol.Tests --include=*.cs` 无输出。全量结果中的 65 个 skipped 全部落在 Pvf(9)、Application(10)、Infrastructure(46)，与基线同分布，Networking/Protocol 两项均为 0 skipped。

### 7. `NetworkPlugins.cs` 的文档引用修复

**判定：通过。**

`git diff HEAD --numstat -- .../NetworkPlugins.cs` → `1  1`，即**恰好一增一删，全文件其余部分逐字未变**。改动内容：

```diff
-    /// <see cref="GameProtocol.Command"/>, so a frame carrying any other command cannot match a
+    /// <c>0x01</c>, so a frame carrying any other command cannot match a
```

值一致性核验：`Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs:5` → `public const byte Command = 0x01;`。**`0x01` 与 `GameProtocol.Command` 的实际值一致。** 该 cref 位于 XML 文档注释中，非可执行代码；`GameProtocol` 迁出程序集且为 `internal`，Networking 侧无法再解析该 cref，改写为纯文本是必需的。属 Scope/In 明列的"失效文档 cref 修复"。

### 8. 共用件未被复制

**判定：通过。**

- `ChannelEncryptedFrame`：全解决方案检索命中 4 个文件——**定义仅 1 处**（`Server/src/Dfo.Server.Protocol/Channel/ChannelEncryptedFrame.cs`），另 3 处为 Info/Script/ScriptVersion 三个编码器的**调用**。无副本。
- `SelectCharacter` 静态体系：`SelectCharacterStaticPayloadCodec` 以 `internal static partial class` 形式在同一切片目录内分布于 2 个文件（`SelectCharacterStaticPayloadCodec.cs` + `SelectCharacterStaticBodyFamilies.cs`），这是基线原有的 `partial` 形态，非复制；`SelectCharacterStaticCondition` 枚举 1 处；`SelectCharacterResponseComposer` 1 处。**均只一份定义。**（RQ-009 / AC-009 成立）

### 9. 切片形态

**判定：通过（CSC-1 / CSC-2 / RQ-002 / RQ-003 成立）。**

`Dfo.Server.Protocol` 目录树（排除 obj）：

```
Channel/
  ChannelProtocol.cs                (端点级线上标识，跨全部 Channel 协议)
  ChannelEncryptedFrame.cs          (共用信封，被下列 3 个切片调用)
  Connect/        ChannelConnectMessageDecoder.cs        + ChannelConnectResponseEncoder.cs
  Info/           ChannelInfoMessageDecoder.cs           + ChannelInfoResponseEncoder.cs
  Script/         ChannelScriptMessageDecoder.cs         + ChannelScriptResponseEncoder.cs
  ScriptVersion/  ChannelScriptVersionMessageDecoder.cs  + ChannelScriptVersionResponseEncoder.cs
Game/
  GameProtocol.cs                   (端点级线上标识，跨全部 Game 协议)
  AccountRoster/        GetUserInfoMessageDecoder.cs + GetUserInfoPayloadCodec.cs
                        + AccountCharacterRosterResponseEncoder.cs + AccountCharacterRosterPayloadCodec.cs
  ClientHardwareDetail/ Decoder + PayloadCodec                     (入站单侧)
  ClientSpecProfile/    Decoder + PayloadCodec                     (入站单侧)
  ClientSpecStatistic/  Decoder + PayloadCodec                     (入站单侧)
  Exit/                 GameExitMessageDecoder.cs + GameExitResponseEncoder.cs
  InitialConnection/    GameInitialConnectionNoticeEncoder.cs + GameInitialConnectionPayloadCodec.cs  (出站单侧)
  Login/                GameLoginMessageDecoder.cs + GameLoginPayloadCodec.cs + GameLoginResponseEncoder.cs
  RosterFollowUp/       Decoder + PayloadCodec                     (入站单侧)
  SelectCharacter/      Decoder + PayloadCodec + ResponseEncoder + ResponseComposer
                        + ProjectionComposer + StaticPayloadCodec + StaticBodyFamilies
                        + StaticSequence + SupplementalProjectionCodec
  SessionMetrics/       Decoder + PayloadCodec                     (入站单侧)
  TaggedIdReport/       Decoder + PayloadCodec                     (入站单侧)
ProtocolModule.cs
Dfo.Server.Protocol.csproj
```

**覆盖核对：**
- 14 个入站协议 → 14 个切片目录（Channel 4：Connect/Info/Script/ScriptVersion；Game 10：Login/Exit/SelectCharacter/AccountRoster(GET_USERINFO)/ClientSpecStatistic/ClientHardwareDetail/ClientSpecProfile/SessionMetrics/TaggedIdReport/RosterFollowUp）。✔
- 9 条出站消息 → 全部落在对应切片目录内（Connect/ScriptVersion/Script/Info/Exit/InitialConnection/Login/AccountRoster/SelectCharacter）。✔
- **两侧皆有内容的 8 个协议**（Connect、Info、Script、ScriptVersion、Exit、Login、SelectCharacter、AccountRoster）其解包与编码**同处一目录**，无须离开该目录即可读全。✔ 其中 `AccountRoster` 把入站 GET_USERINFO（0x0008）与出站 roster 通知配成一个切片，是正确配对——GET_USERINFO 正是该通知的触发消息。

**关于 `AccountRoster` 改名（CS0118）：判断为合理，且是同类选项中较优的一个。**

改名理由经独立证实：`Dfo.Server.Data.Characters.AccountCharacterRoster` 确为 `public sealed record`（`Server/src/Dfo.Server.Data/Characters/CharacterModels.cs:19`），而 `AccountCharacterRosterPayloadCodec.cs:8` 以裸名引用该类型：

```csharp
public static byte[] Encode(AccountCharacterRoster roster)
```

若目录/命名空间取名 `Dfo.Server.Protocol.Game.AccountCharacterRoster`，C# 名称查找中**外围命名空间名优先于 using 引入的类型名**，`AccountCharacterRoster` 会绑定到命名空间而非 record，触发 CS0118（"命名空间被当作类型使用"）。这不是可用 using alias 绕开的偶发问题，而是命名策略本身的结构性冲突。

备选方案评估：
- `GetUserInfo`（沿用线上名）——会把切片名钉在入站一侧，掩盖它同时承载出站 roster 通知；
- `AccountCharacterRoster` + 每处改用完全限定名 `Dfo.Server.Data.Characters.AccountCharacterRoster`——能编译（`NetworkMessages.cs:135` 正是这么写的），但代价是往迁移 diff 里塞入正文改动，直接违反本成果"零逻辑变更、只改 namespace/using"的核心约束；
- `Roster`——过短，与 `RosterFollowUp` 易混；
- `AccountRoster`（现选）——保留语义、无冲突、与同级 `RosterFollowUp` 可区分，且**使被移动文件的 diff 保持在只改一行 namespace**。

结论：现选方案是在"不污染迁移 diff"这一硬约束下的最佳解。**非阻断，无需变更。**

### 10. 自行执行的构建与全量测试

**判定：通过。**

均自仓库外 cwd（`/d/project/DNF`）执行，带 `-nodereuse:false`。

**构建**（原始输出尾部）：
```
Dfo.Server.Protocol -> ...\Server\src\Dfo.Server.Protocol\bin\Debug\net10.0\Dfo.Server.Protocol.dll
Dfo.Server.Protocol.Tests -> ...\Server\tests\Dfo.Server.Protocol.Tests\bin\Debug\net10.0\Dfo.Server.Protocol.Tests.dll
已成功生成。
    0 个警告
    0 个错误
已用时间 00:00:06.04
[exited with code 0]
```
14 个项目全部产出程序集，`TreatWarningsAsErrors` 未被关闭，无诊断抑制。

**全量测试**（`dotnet test ... --no-build`，exit code 0，原始逐程序集计数）：

| 程序集 | 失败 | 通过 | 已跳过 | 总计 | 基线预期 |
|---|---|---|---|---|---|
| Dfo.Pvf.Tests | 0 | 51 | 9 | **60** | 60 ✔ |
| Dfo.Server.Application.Tests | 0 | 256 | 10 | **266** | 266 ✔ |
| Dfo.Server.Networking.Tests | 0 | 45 | 0 | **45** | 45 ✔ |
| Dfo.Server.Protocol.Tests | 0 | 84 | 0 | **84** | 84 ✔ |
| Dfo.Server.Infrastructure.Tests | 0 | 75 | 46 | **121** | 121 ✔ |
| Dfo.Server.Host.Tests | 0 | 4 | 0 | **4** | 4 ✔ |
| **合计** | **0** | **515** | **65** | **580** | 580 / 515 / 65 ✔ |

**580 项 / 0 失败 / 515 通过 / 65 跳过，与基线 `feef1a5` 完全一致。** Networking 45 + Protocol 84 = 129，等于基线 Networking.Tests 的 129，测试无净增减。

**`Dfo.Server.Host.Tests` 瞬时失败：本次未发生**，4/4 一次通过（8 s）。无需重跑。

---

## 发现清单

全部为**非阻断**。零阻断级发现。

| # | 级别 | 内容 | 建议处置 |
|---|---|---|---|
| F-1 | 非阻断（事实更正） | 实施者描述称"Game 12 个切片"，实际 Game 下为 **11 个**切片目录（AccountRoster、ClientHardwareDetail、ClientSpecProfile、ClientSpecStatistic、Exit、InitialConnection、Login、RosterFollowUp、SelectCharacter、SessionMetrics、TaggedIdReport），Channel 4 个，合计 15 个。切片**覆盖本身完整无缺**（14 入站 + 9 出站全部有归属），仅计数表述有误。 | 在结果记录中把数字更正为 11，不需改代码。 |
| F-2 | 非阻断（粒度） | `SmallReportPayloadCodecTests.cs` 落在 `Protocol.Tests/Game/` 根而非某个切片目录下，因为它同时覆盖 `RosterFollowUp` 与 `TaggedIdReport` 两个协议的 codec（其 using 同时引入两个切片命名空间）。测试侧的"一协议一目录"形态在这一个文件上不成立。 | 接受现状；若后继成果要把测试形态也对齐切片，可拆成两个文件分别落入两个切片目录。本成果不宜动，因为拆分会改动测试文件结构、超出"只改位置/命名空间/using"的约束。 |
| F-3 | 非阻断（形态说明） | `Channel/ChannelProtocol.cs` 与 `Game/GameProtocol.cs` 位于端点根目录而非任一切片内。 | 判定合理并建议保留：二者是**端点级**线上标识表，被该端点下全部切片共享，放进任何单个切片都会制造错误的归属暗示。与 CSC-1"每个协议有自己的目录"不冲突（它们不属于任何单一协议）。 |
| F-4 | 非阻断（形态说明） | `ChannelEncryptedFrame.cs` 位于 `Channel/` 根，被 Info/Script/ScriptVersion 三个切片共用。 | 合规：TaskContract Scope/In 第 2 条明确允许"被多个协议共用者……落在一处明确的共享位置"。位置明确且唯一，无需变更。 |
| F-5 | 非阻断（引用图边缘） | `Dfo.Server.Networking.Tests.csproj` 新增了对 `Dfo.Server.Protocol.csproj` 的引用（因 6 个集成测试现需调用 `ProtocolModule`）。这意味着"传输的测试项目"依赖"协议项目"。 | 不影响生产分层（`Dfo.Server.Networking.csproj` 本身逐字未变，仍不引用 Protocol），且这 6 个测试本质是端到端线级测试而非纯传输测试。建议在后继成果中考虑把它们迁到独立的集成测试项目；本成果保持现状是正确的最小改动。 |
| F-6 | 非阻断（可读性） | `NetworkPlugins.cs` 的 cref 改写为 `<c>0x01</c>` 后，读者失去了到 `GameProtocol.Command` 的符号链接，日后若该常量变更，此处注释会静默过期。 | 现写法**事实正确**（已核验 `= 0x01`）且为跨程序集 internal 类型下的必然选择。可选微调：写成 `<c>GameProtocol.Command</c>（0x01）` 以保留符号线索。不构成变更要求。 |
| F-7 | 非阻断（记录完备性） | `Dfo.Server.Protocol.csproj` 新增 `<InternalsVisibleTo Include="Dfo.Server.Protocol.Tests" />`。这不是类型修饰的放宽（相对基线 Networking→Networking.Tests 的同型授予为净持平），但属于"可见性表面"的变动，应在成果的 CSC-8 证据里显式列出，而非被"零可见性放宽"一句带过。 | 在结果文档的可见性逐项列示中补记这一条及其必要性（11 个迁入测试直接调用 internal codec）。 |

---

## 对 CSC / RQ / AC 的最终判定

| ID | 判定 | 依据 |
|---|---|---|
| CSC-1 / RQ-002 / AC-002 | 通过 | 15 个切片目录，覆盖 14 入站 + 9 出站（见第 9 项） |
| CSC-2 / RQ-003 / AC-003 | 通过 | 8 个两侧协议的解包与编码同目录（见第 9 项） |
| CSC-3 / RQ-004 / AC-004 | 通过 | Networking 残留 17 个 `.cs` 逐个检视，全为传输（见第 3 项） |
| CSC-4 / RQ-005 / AC-005 | 通过 | 路由表 `diff` 逐字节 identical；`ProtocolModule` 无 `Enabled` 分支（见第 2 项） |
| CSC-5 / RQ-006 / AC-006 | 通过 | Networking.csproj 逐字未变；Protocol → Networking，无环（见第 4 项） |
| CSC-6 / RQ-007 / AC-007 | 通过 | 全量 diff 过滤后仅 5 处净变更，均在允许范畴（见第 1 项） |
| CSC-7 / RQ-010 / AC-010 | 通过 | 7 类线级集成测试全部通过，断言零改动（见第 6 项） |
| CSC-8 / RQ-008 / AC-008 | 通过（空过） | 零可见性修饰改动；唯一 IVT 授予为净持平（见第 5 项、F-7） |
| CSC-9 / RQ-011 / AC-011 / AC-012 | 通过 | 580 / 0 失败 / 515 通过 / 65 跳过，对齐基线（见第 10 项） |
| RQ-001 / AC-001 | 通过 | 协议与传输分属两个编译单元（见第 3、4 项） |
| RQ-009 / AC-009 | 通过 | 共用件各只一份定义（见第 8 项） |

## 判定

**approved（0 阻断）。**

这次改动确实干净。我按逐字节比对而非目视核对了两张路由表、按机械过滤而非抽样核对了 43 个被移动文件的全部净变更、独立验证了 `0x01` 与 `GameProtocol.Command` 的一致性和三个 `compress` 标志、并自行完成了构建与全量测试。"除文件位置、命名空间、using 与迁移直接导致的失效引用修复外不含任何行为改动"这一主张成立，且比主张更强——**本次 diff 连一处可见性修饰的改动都没有**。7 项非阻断发现中，F-1 是需要更正的表述错误，F-7 是需要补记的证据条目，其余 5 项为形态说明或后继整理建议，均不要求本成果做任何变更。
