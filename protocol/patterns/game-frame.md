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

## 地址约定：RVA 还是 VA（先读这一节）

内存 dump 按 **RVA** 索引，`manifest.json` 给出 `image_base = 0x00400000`，即

```text
RVA = VA - 0x00400000
```

两类地址混在同一段反汇编里，**必须分开处理**：

| 来源 | 得到的是 | 能否直接查 dump |
|---|---|---|
| `E8 rel32` / `jmp rel32` 的目标（相对寻址） | **RVA** | 能。`rel` 是位置无关量，`RVA_target = RVA_next + rel` |
| 指令里的**绝对立即数**（`push 0xCC62C0`、`mov eax,[0x319A114]`、`[edx*4+0x3173FC0]`） | **VA** | 不能，先减 `0x00400000` |
| 抓包插件记录的调用栈 | **RVA**（插件已减模块基址） | 能 |

**这个坑本次真实踩过，代价很大**：把接收 handler 表里的 `push 0x00CC62C0` 当成 RVA 去反汇编，
得到 `jmp dword ptr [eax*4+0x10C6464]`——一条看似合理、实则错位的指令，于是"handler 不像函数
入口"，进而怀疑 dump 不完整、怀疑代码被加密、怀疑指针不是函数指针，花了很多步才找到真因。
减掉基址后 `RVA 0x008C62C0` 是干净的 `push ebp; mov ebp,esp`。

**判据**：绝对立即数落在 `0x00400000` 以上且反汇编出无意义指令时，先减基址再看，
不要先怀疑 dump。

顺带说明：dump 中 `0x02BE51C4`–`0x04078000` 约 15MB 读出来全是 0，那是**真的**——
`pages.csv` 显示这些页已写入，只是内容为零（未初始化的 BSS 与运行时才填的表）。
`GetWriter` 读的全局 `[VA 0x0319A114]` 就落在这里，所以**任何运行时数据结构都不能从本 dump
静态读取**，只能靠运行时观测。诊断路径上的按 type 索引表（`VA 0x3173FC0` / `0x3174FF8`）
也是全零，取不到客户端自带的协议名。

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

因此 **body 长度 = 各 `WriteXX` 宽度之和**，可与实测 body 长度互相校验；两者不等**通常**说明
读漏了分支或循环。

### 但闭合失败也可能是正确结果

`0x0117` 是第一个反例：直线宽度 2 字节，实测 12 字节，原因是它的载荷由**虚函数**写出
（`mov edx,[ecx]; mov eax,[edx+0x1C]; push writer; call eax`），布局根本不是静态的。

**判别方法**：闭合失败时先在构包器里找**经 vtable 的间接 `call`**。

- 找不到间接 call → 确实读漏了分支/循环，继续读。
- 找到了 → 这是多态载荷，静态分析到此为止；必须先建立"kind → 载荷布局"表，
  而 vtable 是运行时数据（本 dump 里为零），只能靠运行时观测拿到实际被调用的函数地址。

顺带：这类协议往往**既没有条目计数也没有总长度**，接收方只能靠 kind 推长度。
服务端实现这种协议时，缺一个 kind 就会整帧错位——不能"跳过未知条目"。

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

已见但未确认的原语：`0x1C90710`，出现在被混淆区（见下）构包 C→S `0x0002` 的路径上，
紧邻 `Finalize`。该帧 body 尾部是 `0C000000` + 12 字节 ASCII 十六进制串，长度前缀与串长
吻合，因此它**很可能**是"写长度前缀字节串"。未确认，不得据此解析。

## 抓包的覆盖边界

抓包引擎挂在 `Cipher::Encrypt` / `Cipher::Decrypt` 上，所以它看到的是**加密路径上**的帧，
不是全部帧。实测证据：五次会话的发送 `seq` 恒为 `0…3, 5, 7, 8, …`，
**seq 4 与 seq 6 从未出现**。`seq` 只在真正发送时递增（第五次会话少发一帧，其后每个 seq
都整体下移一位），所以这两个号确实被发送消耗了，只是钩子没看到。

写记录时因此要区分两类断言：

- **可以**断言"某两帧之间没有入站帧"——入站有独立钩子，覆盖同样的路径。
- **不可以**断言"客户端只发了这些帧"或"发送序列完整"。用"抓包中观察到的顺序"这种措辞。

同理，`Trigger` 写"在 X 之后"时要意识到 X 与本帧之间可能还有没抓到的发送。想要因果而非
顺序，去查[接收 handler 普查](game-receive.md#接收-handler-注册表普查)：发送栈往下几层
通常落在某个接收 handler 里。

## 构包点普查：先枚举，再挑协议

`MakeHeader` 是所有出站帧的**唯一必经点**，且总有一个 `push <type>` 到达它。因此扫描
`MakeHeader` 的全部直接调用点就能得到"客户端到底有哪些 C→S 协议"的清单——不必先抓到包
才知道协议存在。

工具与结果已入库：

- [`data/builder-census.py`](data/builder-census.py)：普查脚本（capstone，跑一遍约 4 秒）
- [`data/client-to-server-builder-sites.tsv`](data/client-to-server-builder-sites.tsv)：
  `type` / 构包点数 / 质量标记 / 全部构包点 RVA

当前客户端（SHA `F78F0AEA…C04C`）的结果：

| 指标 | 值 |
|---|---|
| `MakeHeader` 直接调用点 | 1099 |
| type 可静态确定 | 1082 点 / **604 个不同 type**，范围 `0x0000`–`0x043D` |
| type 在寄存器里（运行时决定） | 16 点 |
| 块内无 push | 1 点 |
| 平均每 type 构包点数 | 1.79（最多一个 type 有 35 个） |

**一个 type 多个构包点是常态**，这直接改变了逆向单条协议的做法：

> 确定一条 C→S 协议的布局，必须看它**全部**构包点。只看抓到的那一个，会把"某个调用路径
> 写的常量"当成"字段的固定值"。`0x0008` 就是活例：抓到的那个点写 `0xFFFF`/`2` 两个立即数，
> 看起来完全闭合；实际另有三个点写运行时值和 `3`，字段是变量而非常量。

### 两种构包形状

```text
形状 A（自取 writer）          形状 B（writer 由参数传入）
push <type>                    push <type>
call GetWriter                 mov  ecx, <writer 参数>
mov  ecx, eax                  call MakeHeader
call MakeHeader
```

形状 B 的例子是 `0x0117` 的构包器 `0x01CD5300`（`mov ebx,[ebp+8]` 取 writer）。
只按形状 A 匹配会漏掉它，所以普查只以 `MakeHeader` 为锚点。

### 为什么普查要用"前向对齐投票"而不是反向反汇编

x86 无法无歧义地反向解码。脚本的做法是：在调用点前一段窗口内**逐个对齐前向反汇编**，
凡指令流能精确落在调用点上的对齐即视为"同步"，各同步对齐对"最后一个 push 是哪条"投票，
取多数。错位对齐必须与真对齐结论一致才会影响结果，比单次反向游走可靠得多。

三种更弱的规则都实测失败过，别再走：

- 要求 push **紧贴** `call GetWriter`：漏掉 252 个点（编译器在中间插了无关的 store）。
- 「前 32 字节内最后一个 push」不管对齐：把别的代码路径的 push 认成本次的——它在
  `0x00E40D75` 读出了不存在的 type `0x2AFF`（真值在 `ebx` 里）。
- 单次反向游走：会失同步后静默放弃，168 个点无解。

### 普查的已知盲区：被混淆的第二代码区

`.text`（< `0x01D10000`）之外还有一个代码区（`0x0439xxxx`–`0x048Exxxx`），普查在那里只
命中 9 个点。该区是**混淆/虚拟化过的代码**，典型指令形状：

```asm
push dword ptr [edi + 0x5a]
pop  dword ptr [esp + 4]        ; 纯栈搬运垃圾
mov  eax, dword ptr [esp + 4]
...
call 0x1C90710
call 0x1C8FDD0                  ; Finalize
```

**已观察到的 C→S `0x0002`（31 字节）就是在这里构包的**（抓包 stack[1] = `0x04876980`），
而它在普查里查不到。所以 604 是**下限**：被混淆搬走的协议对 `.text` 扫描不可见。
遇到"抓到了包但普查里没有这个 type"时，先看 stack[1] 是否落在该区。

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

**首选方法也会不可用**：外层帧的那条 `call` 可能是虚分发。`0x0008` 上就是
`0x00D8566C: call edx`，拿不到静态目标，而次选方法在同一处给出了又一个假阳性入口
`0x008C867D`（反汇编成 `cmp edi, 0x3AE8C88B`）。

此时**不要死磕入口**：入口地址是手段，不是结论。body 小、写入序列在同一基本块内时，
直接从 stack[1] 往前读二十几条指令就能看到完整的
`MakeHeader → WriteXX… → Finalize`，函数入口对布局结论没有贡献。
更省事的是查[构包点普查](#构包点普查先枚举再挑协议)：它按 type 直接给出全部
`MakeHeader` 调用点，跳过整个"从返回地址反推"的环节。

## Current patterns

- 当前观察到的客户端命令包使用 `Command = 0x01`。
- 相同 numeric Type 在不同 Command 或方向下可能代表不同含义；Type 不是全局唯一身份。
- Sequence 在同一客户端连接的连续命令中递增，但具体重置、回绕和响应关联规则尚未完成逆向。
- Checksum 的客户端计算规则尚未记录到本资料库；不要从单一样本猜测算法。
