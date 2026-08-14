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
| `0x1C8FAC0` | `WriteBytes(ptr, len)` | `push <len>` → `push <ptr>` → `GetWriter` → call；用于浮点等非整型字段 |
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
- `0x1C90460` / `0x1C8FDD0`：三条协议共用，分别传入各自 type 并在末尾收尾。
- `0x1C8FAC0`（`WriteBytes`）：由 `0x00C2` 确定（`push 4; push &float` 写 IEEE-754 单精度），
  长度由第二个参数显式给出，因此宽度不必推断。
- 支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`。

尚未识别宽度的原语（字符串、数组、u64、变长）出现时，必须先在新协议记录里确定其宽度，
再回写本页；不要按调用序号推断宽度。

## 调用栈标定构包器

抓包插件（`[FrameCapture] Stack=`）对出站帧记录 16 层调用栈 RVA。

```text
00AB: 01C9007C  01CD11B0  01CD12CA  00D8566E  ...   ← 序列化器 + wrapper 两层
01DD: 01C9007C  00C37683            00D8566E  ...   ← 构包器一层（序列化内联）
00C2: 01C9007C  01CD65D7  01CD6C77  01CD138F  ...   ← 另一条派发路径
```

- **第 1 帧恒为 `Finalize` 调用的返回地址**（`0x00AB` 的 `0x01CD11AB → 0x01CD11B0`、
  `0x00C2` 的 `0x01CD65D2 → 0x01CD65D7`）。它落在序列化器内，但通常不是函数入口。
- 第 0 帧是共享写入 helper 的返回地址，对所有协议相同，不含信息。
- 层数差异反映"wrapper + 序列化器"还是"单函数内联"。
- `0x00D8566E → 0x00D89C36 → 0x01C90D3B → 0x00C601BA` 是 `0x00AB`/`0x01DD` 共用的登录期
  派发链；`0x00C2` 走的是另一条（`0x01CD6C77 → 0x01CD138F → …`），与它较晚发送相符。

### 从返回地址求函数入口

**首选方法**：栈上相邻两帧即可直接得到入口——结束于外层帧返回地址的那条 `call`，
其目标就是内层函数的入口。

```text
call 结束于 0x01CD6C77 (frame2)  →  目标 0x01CD6260  =  frame1 所在函数入口
```

**次选方法**（只有一帧可用时）：向后扫描 `CC`/`90` 填充后的第一个 prologue，并要求从该
候选开始的线性反汇编能精确落在目标地址上。使用时注意两个坑，两者本次都真实发生过：

- **不要把函数内部的 `ret` 当作函数结尾**。提前返回（守卫失败即 `ret`）很常见，
  在第一个 `ret` 处停止扫描会让正确的入口被判为"到不了目标"。
- **该启发法会给出假阳性**。`0x00C2` 的序列化器上它返回了 `0x01CD65AC`，从那里反汇编
  出的是错位垃圾（`sti`、`dec [ebx-0x60341738]`）。**判据**：真入口应以正常 prologue
  开头（`push ebp; mov ebp,esp` 或 `push esi/ebx; mov reg,ecx`），且很快能看到
  `push <type>` → `GetWriter` → `MakeHeader`。看到无意义指令就说明对齐错了，换首选方法。

## Current patterns

- 当前观察到的客户端命令包使用 `Command = 0x01`。
- 相同 numeric Type 在不同 Command 或方向下可能代表不同含义；Type 不是全局唯一身份。
- Sequence 在同一客户端连接的连续命令中递增，但具体重置、回绕和响应关联规则尚未完成逆向。
- Checksum 的客户端计算规则尚未记录到本资料库；不要从单一样本猜测算法。
