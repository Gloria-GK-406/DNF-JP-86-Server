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

## 客户端构包原语

客户端的出站构包函数不用 memcpy 结构体，而是逐字段调用一组共享写入原语。识别出这组
原语后，任何构包函数的 body 布局都可以**机械读出**，不必再靠样本猜边界：

| RVA | 作用 | 调用形态 |
|---|---|---|
| `0x1C8F5B0` | `GetWriter()` → `eax` | 每个字段写入前都调一次 |
| `0x1C90460` | `MakeHeader(u16 type)` | `push <type>` → `GetWriter` → `mov ecx,eax` → call |
| `0x1C90550` | `WriteU8` | `movzx reg, byte [this+X]` → push → `GetWriter` → call |
| `0x1C90580` | `WriteU16` | `movzx reg, word [this+X]` → push → `GetWriter` → call |
| `0x1C905B0` | `WriteU32` | `mov reg, dword [this+X]` → push → `GetWriter` → call |
| `0x1C8FDD0` | `Finalize()` | 所有字段写完后调一次，随后函数返回 |

典型形状（`GetWriter` 的返回值即 `WriteXX` 的 `this`）：

```text
push <value>
call 0x1C8F5B0          ; GetWriter
mov  ecx, eax
call 0x1C90550/80/B0    ; WriteU8 / WriteU16 / WriteU32
```

因此 **body 长度 = 各 `WriteXX` 宽度之和**，可与实测 body 长度互相校验；两者不等即说明
读漏了分支或循环。

### 构包原语的证据

- `0x1C90550`（u8）与 `0x1C905B0`（u32）：由 `0x00AB` 已确认布局反证
  （`u8+u8+u8 + 4×(u8,u32,u32)` = 39，序列化器 `0x1CD1120`）。
- `0x1C90580`（u16）：由 `0x01DD` 确定（四次调用对应四个 u16，长度算术与实测 16 字节唯一
  自洽，其中两值为已知 u16 量 PCI `8086`/`A7A8`），构包器 `0x00C375B0`。
- `0x1C90460` / `0x1C8FDD0`：两条协议共用，分别传入各自 type 并在末尾收尾。
- 支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`。

尚未识别宽度的原语（字符串、数组、u64、变长）出现时，必须先在新协议记录里确定其宽度，
再回写本页；不要按调用序号推断宽度。

## 调用栈标定构包器

抓包插件（`[FrameCapture] Stack=`）对出站帧记录 16 层调用栈 RVA。用一条**已确认**的协议做
标定，即可读出未知协议的构包器位置：

```text
00AB: 01C9007C  01CD11B0  01CD12CA  00D8566E  ...   ← 序列化器 + wrapper 两层
01DD: 01C9007C  00C37683            00D8566E  ...   ← 构包器一层（序列化内联）
```

- 第 0 帧是共享写入 helper 的返回地址，对所有协议相同，不含信息。
- 第 1 帧起才是业务构包代码；层数差异反映"wrapper + 序列化器"还是"单函数内联"。
- `0x00D8566E → 0x00D89C36 → 0x01C90D3B → 0x00C601BA` 是登录握手期两条协议共用的上层
  派发链，可作为"这是登录阶段自动上报"的旁证。

## Current patterns

- 当前观察到的客户端命令包使用 `Command = 0x01`。
- 相同 numeric Type 在不同 Command 或方向下可能代表不同含义；Type 不是全局唯一身份。
- Sequence 在同一客户端连接的连续命令中递增，但具体重置、回绕和响应关联规则尚未完成逆向。
- Checksum 的客户端计算规则尚未记录到本资料库；不要从单一样本猜测算法。
