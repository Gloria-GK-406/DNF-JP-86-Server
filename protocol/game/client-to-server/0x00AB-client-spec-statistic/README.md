# `0x00AB` `CLIENT_SPEC_STATISTIC`

## Identity and behavior

| Property | Value |
|---|---|
| Endpoint | Game |
| Direction | Client → Server |
| Command | `0x01` |
| Type | `0x00AB` |
| Role | Report |
| Trigger | Game 登录成功后、角色列表请求前 |
| Expected response | None（High） |
| Catalog status | Reversing |

当前结论：这是客户端硬件/运行环境统计上报。服务器应严格解析为 typed report；在没有新的客户端 reader 证据前，不发送 ACK。

## Observed sequence

六次独立连接均观察到：

```text
Game login response
→ C2S 01/00AB (39-byte body)
→ C2S 01/01DD (16-byte body)
→ C2S 01/0008 account-character roster request
```

客户端在 `0x00AB` 后继续前进，没有等待、重试或因缺少响应中断。

## Wire layout

Game header 见 [Game TCP 帧组织](../../../patterns/game-frame.md)。Body 固定观察为 39 字节。

下表是当前工作布局。字段边界必须由客户端 `ISocket::Write` trace 或构包函数确认后才能升级为 Confirmed。

| Offset | Size | Encoding | Meaning | Confidence | Evidence |
|---:|---:|---|---|---|---|
| `0` | 2 | LE `u16` | Unknown00 | Hypothesis | `E-001` |
| `2` | 2 | LE `u16` | Unknown02 | Hypothesis | `E-001` |
| `4` | 4 | LE `u32` | Unknown04 | Hypothesis | `E-001` |
| `8` | 4 | LE `u32` | Unknown08 | Hypothesis | `E-001` |
| `12` | 1 | `u8` | Unknown12 | Hypothesis | `E-001` |
| `13` | 4 | LE `u32` | Graphics PCI VendorId (`0x8086` in sample) | High | `E-001`, `E-004` |
| `17` | 4 | LE `u32` | Graphics PCI DeviceId (`0xA7A8` in sample) | High | `E-001`, `E-004` |
| `21` | 1 | `u8` | Unknown21 | Hypothesis | `E-001` |
| `22` | 4 | LE `u32` | Unknown22 | Hypothesis | `E-001` |
| `26` | 4 | LE `u32` | Unknown26 | Hypothesis | `E-001` |
| `30` | 4 | LE `u32` | Unknown30 | Hypothesis | `E-001` |
| `34` | 1 | `u8` | Unknown34 | Hypothesis | `E-001` |
| `35` | 4 | LE `u32` | Unknown35 | Hypothesis | `E-001` |

不要把未知值仅因数值相似而命名为 CPU 核心数、内存、DirectX 版本或显示设置。

## Samples

- [2026-08-13 Windows client sample 01](samples/2026-08-13-windows-client-01.hex)
- Body length：39。
- 在同一客户端的 6 次独立连接中完全一致。
- Body SHA-256：`C76FD00687666BB30214C1BB785F7A18104F8C20DAD63BD1916CC9083022A484`。

## Client reversing evidence

| Evidence | Kind | Location | Supports |
|---|---|---|---|
| `E-001` | Dynamic network observation | 2026-08-13 functional run, sample above | Exact body, length, ordering and repeatability |
| `E-002` | Static enum | `86JP-rewio/Server/DfoServer/Network/Core/PacketTypes.cs` | Type name `CLIENT_SPEC_STATISTIC` |
| `E-003` | Historical server behavior | `86JP-rewio/Server/DfoServer/Network/Protocol/GameProtocolHandler.cs` and Git history | No registered handler/response in available legacy implementation |
| `E-004` | Platform structure | Direct3D 9 `D3DADAPTER_IDENTIFIER9` | VendorId and DeviceId are adjacent DWORD adapter identifiers |
| `E-005` | Client send-chain reversing | `ClientHack/plugins/0100_network_hook/network_hook.c` | Header/body boundary and candidate trace point |

Direct constructor evidence is not yet captured. The supported DNF executable is:

```text
SHA-256 F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C
```

Known send-chain RVAs:

| Function | RVA |
|---|---:|
| `MakePacket` | `0x02090460` |
| `TCP_SEND` | `0x0208FDD0` |
| `Cipher::Encrypt` | `0x01C11360` |
| `ISocket::Write` | `0x048FF9AB` |

Next trace should filter at `Cipher::Encrypt` for `command == 1 && type == 0x00AB`, capture the normalized call stack, then record each `ISocket::Write` call made by the located constructor.

## Server and historical evidence

The latest rewio legacy server contains the enum but no `0x00AB` dispatch registration. Its fallback logs the body and sends no response. The client still reaches the roster screen, matching the current refactored server's observed behavior.

This proves compatibility with no response for the observed login path. It does not prove that every future client version treats the report identically.

## Intended implementation mapping

| Layer | Intended responsibility |
|---|---|
| Data | Neutral `ClientSpecStatisticReport`; unknown fields retain offset-based names |
| Networking | Require exact body length and decode little-endian fields |
| Application | Consume the report without issuing a response; optional structured diagnostics |
| Domain/Infrastructure | None until a real business consumer is identified |

No `ServerMessageType` should be added solely for this report.

## Verification still required

- Capture constructor call stack and `ISocket::Write` boundaries on Windows.
- Produce constructor pseudocode with source APIs/fields for all 39 bytes.
- Compare at least one client with a different graphics adapter or environment.
- Add exact-vector parser and malformed-length tests when implementation begins.
- Verify sender invocation count remains zero.
- Confirm the following `0x01DD` and roster request still progress in a real client run.

## Unknowns and next evidence

1. **Exact field boundaries before and after PCI IDs** — resolve with Write trace.
2. **Semantic source of Unknown fields** — resolve by tracing constructor operands to APIs or object fields.
3. **Whether the server ever persists/uses the report** — no evidence in available legacy server; do not introduce persistence without a consumer.
4. **Whether another client build changes length/layout** — key records by client SHA and add a separate variant if observed.
