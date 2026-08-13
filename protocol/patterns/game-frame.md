# Game TCP 帧组织

本页只记录多条 Game 协议共享的帧级事实。各 type 的 body 布局位于对应协议记录。

## Header

当前客户端与服务端使用 13 字节小端 header：

| Offset | Size | Field | Encoding |
|---:|---:|---|---|
| `0` | 1 | Command | `u8` |
| `1` | 2 | Type | little-endian `u16` |
| `3` | 4 | TotalLength | little-endian `u32`，包含 13 字节 header |
| `7` | 4 | Checksum | little-endian `u32` |
| `11` | 2 | Sequence | little-endian `u16` |

因此：

```text
BodyLength = TotalLength - 13
```

## Evidence

- 当前服务端 parser 按上述 offset 解码：`Server/src/Dfo.Server.Networking/Framing/GameClientFrameRequestInfo.cs`。
- 客户端 `Cipher::Encrypt` 的 body 指针前 13 字节是 header，并在发送前写入 `bodyLength + 13`：`ClientHack/plugins/0100_network_hook/network_hook.c`。
- 客户端发送链的当前逆向结论：`MakePacket → ISocket::Write* → TCP_SEND → Cipher::Encrypt → flush/send`。
- 支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`。

## Current patterns

- 当前观察到的客户端命令包使用 `Command = 0x01`。
- 相同 numeric Type 在不同 Command 或方向下可能代表不同含义；Type 不是全局唯一身份。
- Sequence 在同一客户端连接的连续命令中递增，但具体重置、回绕和响应关联规则尚未完成逆向。
- Checksum 的客户端计算规则尚未记录到本资料库；不要从单一样本猜测算法。
