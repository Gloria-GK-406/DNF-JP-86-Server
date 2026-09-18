# A21 普查锚点

`census-identity.py` 的六个锚点全是 **A12 专有 RVA**，所以对 A21 重跑普查的门槛不是改一行路径，
而是把这些锚点在 A21 镜像里重新找出来。本页是找出来的结果。

**输入**

| 项 | 值 |
|---|---|
| 客户端 | `DNF.unpacked-b1-debug.exe` SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` |
| 内存转储 | `DNFClient-A21-protocol/dumps/dnf-dump-20260822-232754-263-26888/DNF-memory-rva.bin`（122 085 376 B，按 RVA 索引） |
| 镜像基址 | `0x00400000` |
| 枚举表 | ServerS4A21 的 `PacketTypesA21.cs`（NOTI 1219、CMD 1272 条） |

转储的 RVA 索引已用三处已知字节核对过：`0x01147BB0`（自杀桩 `c7 05 00…c3`）、
`0x00D7D48E`、`0x0123C142`，与 exe 逐字节相同。

## 锚点

| 角色 | A21 RVA | A12 对应 |
|---|---|---|
| `Register` | **`0x0123C4A0`** | `0x00D89FC0` |
| CMD 注册表 getter | **`0x0123C410`** → 全局 `[0x03A5BE68]`，初始化标志 `[0x03A5BE6C]` | — |
| NOTI 注册表 getter | **`0x0123C550`** → 全局 `[0x03A5BE74]`，初始化标志 `[0x03A5BE78]` | — |
| CMD forwarder | **`0x01237660`** | `0x00D855C0` |
| NOTI forwarder | **`0x01237720`** | `0x00D85680` |
| CMD 编排器 | **`0x0123C190`** | — |
| NOTI 编排器 | **`0x0123C4F0`** | — |
| CMD 批次 ×3 | **`0x00D0C2C0`、`0x00D18BC0`、`0x00D44AF0`** | `0x008C5920`、`0x008D04C0`、`0x008F3C70` |
| NOTI 批次 ×3 | **`0x00D5F540`、`0x00D885F0`、`0x00D9A780`** | `0x0090BD20`、`0x009301C0`、`0x0093EAD0` |
| GetWriter（需声明为透明） | **`0x0234CA50`** | `0x01C8F5B0` |
| forwarder 路径上的插入调用（需声明为透明） | **`0x00D02C30`** | `0x008BC420`（0 参 thunk） |
| `MakeHeader` | **`0x0234D1E0`** | `0x01C90460` |

## 序列化原语

`extract-layout.py` 要的不止身份锚点，还要读写原语——**宽度由调用了哪个原语决定，不靠猜**。
A21 的这一组同样是从指令流读出来的。

### 写（C→S 构建器内）

| 原语 | A21 RVA | 判据 | A12 对应 |
|---|---|---|---|
| `WriteU8` | **`0x0234D2E0`** | `inc [ecx+0x2BCC2C]`、`push 1` | `0x01C90550` |
| `WriteU16` | **`0x0234D310`** | `add [ecx+0x2BCC2C], 2`、`push 2` | `0x01C90580` |
| `WriteU32` | **`0x0234D340`** | `add [ecx+0x2BCC2C], 4`、`push 4` | `0x01C905B0` |
| `WriteBytes` | **`0x0234CED0`** | `ret 8`（两参：指针 + 长度） | `0x01C8FAC0` |
| `WriteDString` | **`0x0234D4A0`** | 先求串长再走 `0x0234D410` 逐字节 | `0x01C90710` |
| `Finalize` | **`0x0234E6D0`** | 922 个构建器收口于它 | `0x01C8FDD0` |

`[ecx+0x2BCC2C]` 是写出器自己的字节计数器，三个定宽原语各把它推进自己的宽度——
这就是宽度的直接证据。

### 读（S→C handler 内）

流状态是两个全局：**指针 `[0x03BAAE38]`**、**剩余字节 `[0x03BAAE3C]`**。

| 原语 | A21 RVA | 判据 | A12 对应 |
|---|---|---|---|
| `ReadU8` | **`0x023C5230`** | `mov cl, byte [eax]`、`inc eax`、`dec [剩余]` | `0x01D00420` |
| `ReadU16` | **`0x023C52A0`** | `mov cx, word [eax]`、`add eax,2`、`sub [剩余],2` | `0x01D00490` |
| `ReadU32` | **`0x023C5310`** | `mov ecx, dword [eax]`、`add eax,4`、`sub [剩余],4` | `0x01D00500` |
| `ReadBytes` | **`0x023C5380`** | 按 `[ebp+0xc]` 长度 memcpy 后推进指针 | `0x01D00570` |

四个的地址间隔 `0x70`，与 A12 的 `0x01D00420/0490/0500/0570` 同构。

**没有独立的 dstring 读原语。** A21 的 handler 把「读长度 + 读字节」内联，`ReadU32` 之后
0x60 字节内跟 `ReadBytes` 的配对散布在 58 处 handler 里，不集中于某个 helper。
提取器缺这一项不影响正确性，只是把字符串报成 `u32 + bytes`——那反而更贴近指令流。

### 一条意外的印证

`ReadBytes` 里 memcpy 之后那条指令是 **`0x023C5399`**，而它正是本仓客户端第二次崩溃栈上的
第一个非 ntdll 帧（`stack: …, 0x023C5399, 0x053526CF, 0x00D82D91`，写 `0x001B0000`、
`ecx = 0x000A252E`）。

也就是说那次崩溃是**从流里读到一个假长度、照它 memcpy 进栈缓冲**。这与当时按帧长比对得出的
判断（`NOTI 0x0025` 的 A21 布局与本仓所写不是一回事）互相独立而结论一致，
崩溃栈到此可以逐帧解读。

## 怎么找到的

**不靠猜锚点，靠已知的枚举值反推。** 全镜像扫 `push imm32` 且立即数落在 type 取值域
（`0x000..0x4F7`）内，按邻近聚簇，取值递增且互不重复的簇就是注册表。第一个命中的簇
`0x00D889C2` 给出注册形状：

```asm
push 0                 ; ctx
push 0x1160ba0         ; handler
push 0x8a              ; type
mov  ecx, esi          ; 注册表
call 0x163c4a0         ; Register
```

反查 `Register` 的直接调用点得 **943 个**，按邻近聚成 **6 个批次**（941 个被覆盖），
与 A12 的三 CMD + 三 NOTI 同构。落在批次之外的 **2 个**正是两个 forwarder——
它们各自 `call` 一个不同的 getter 再 `call Register`，所以**forwarder 身份即族别**，
这一点与 A12 相同。

批次的注册表是 `ecx` 传进来的参数（每处都是 `mov esi, ecx`），所以族别不能从批次本身读出。
启动代码 `0x0123ABD8` 处一次给全了：

```asm
call 0x163c410      ; getter A
mov  ecx, eax
call 0x163c190      ; 编排器 1 → 三个批次
call 0x163c550      ; getter B
mov  ecx, eax
call 0x163c4f0      ; 编排器 2 → 另三个批次
```

`MakeHeader` 由定点法得到：`push 0x4DD`（客户端确定会发的 `CHECK_USER_CONNECTION`）
全镜像只有一处，其现场是

```asm
push 0
push 0x4dd
call 0x274ca50      ; GetWriter
mov  ecx, eax
call 0x274d1e0      ; MakeHeader
call 0x274e6d0      ; 发送
```

`GetWriter` 插在 push 与 anchor 之间，正是 A12 注释里说的必须声明为透明的那种调用。

## 哪个注册表是 CMD、哪个是 NOTI

**判据不是地址顺序。** 两个注册表的全局只在各自 getter 内被引用，派发点不直接读它们，
所以走的是统计判据，用参照会话的 458 帧做真值——每帧自带 `cmd=0x00`（客户端收作 NOTI）
或 `cmd=0x01`（收作 CMD）。

只见于 NOTI 的 58 种里：

| | 覆盖 NOTI-only | 覆盖 CMD-only |
|---|---|---|
| registry A | 24/58（41%） | 10/15（67%） |
| registry B | **45/58（78%）** | 9/15（60%） |

决定性的是**独占**：只见于 NOTI 且**只在 B 里**的有 `0x0023 0x0026 0x0061 0x0069 0x007C
0x00AA 0x00AC 0x00AD 0x00AE 0x0117 0x0158 0x0164…`；只在 A 里的**只有 `0x0002` 一个**，
而 `0x0002` 恰好是客户端会发、且[已知注册了响应 handler](../../../game/client-to-server/0x0002.md)
的那一条，落在 CMD 表里正合适。

另一条独立佐证：A 独有的 148 个号里 **83%** 也出现在客户端发出的集合中，B 独有的 287 个里只有
54%——CMD 的请求与响应共用 type，NOTI 不共用。

结论：**A = CMD 接收表，B = NOTI 接收表。** 证据级别 `High`——两条独立统计一致，
缺一条直接指令级证据（派发点按 selector 选表的那一处尚未定位）。

## forwarder 注册的那批

A12 有 439 条注册走运行时 forwarder 而非启动批次。A21 当前找到 574 个调用点，形状是：

```asm
push ebx           ; ctx
push 0x43c5e0      ; handler
push 0x352         ; type
call 0x1102c30     ; 插入调用——必须声明为透明，否则这条路一条都解不出
mov  ecx, eax
call 0x1637660     ; forwarder（族别由它决定）
```

CMD forwarder 272 个调用点全部解出，对应 271 个 type；NOTI forwarder 302 个调用点全部解出，
对应 300 个 type。重复调用点由同表覆盖语义归并，不是未解析。

## 这次静态产出

| 项 | 数 |
|---|---|
| `Register` 直接调用点 | 943（批次内 941） |
| forwarder 调用点 | 574（CMD 272 + NOTI 302），全部解出 |
| **CMD 接收 type** | **671**（批次 400 + forwarder 271） |
| **NOTI 接收 type** | **839**（批次 539 + forwarder 300） |
| 两表都注册的 type | 460 |
| **C→S 发送 type** | 1411 个 `MakeHeader` 调用点；1391 点解析为 **762 type**，20 点保留未解析分类 |

作个尺度对照：枚举表里 NOTI 1219 条、CMD 1272 条。普查解出的是**实际注册了 handler 的**
子集，不是枚举全集——枚举里有大量号客户端根本不接收。

## 仍未参数化的边界

- **客户端 profile 仍在脚本内。** 当前流水线已经完成 A21 identity、inventory、layout 与文档生成；
  外部 dump/枚举路径分别通过 `DNF_A21_DUMP_PATH`、`DNF_A21_ENUMS_PATH` 注入。但 RVA 锚点、
  原语和代码区边界仍是脚本内的 A21 常量；切到另一客户端版本前，仍需把这组常量抽成受校验的
  profile，不能只换文件路径。
- **派发点**：按 selector 选表的那处代码没找到。找到即可把族别判定从 `High` 提到指令级
  （两个注册表的全局只在各自 getter 内被引用，派发走的是 getter 返回值或对象字段）。
- **dstring 读原语**：A21 没有独立形态，见上文。
- A21 镜像 33% 是打包/虚拟化区（`.vmp0`/`.vmp1`/`.mackt` 等 40.3 MB / 122.1 MB），
  handler 体落在其中的，静态取字段会和 A12 一样止步——那不是这条流水线能解决的，
  要运行时手段。
