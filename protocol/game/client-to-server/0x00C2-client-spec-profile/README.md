# `0x00C2` `CLIENT_SPEC_PROFILE`

> 名称说明：`CLIENT_SPEC_PROFILE` 是**本资料库的描述性命名**，不是客户端或旧服枚举名
> （旧服源码树 `86JP-rewio` 当前不在工作区）。与 `0x01DD` 同样的命名约定，见
> [协议目录](../../../catalog.md)。

## Identity and behavior

| Property | Value |
|---|---|
| Endpoint | Game |
| Direction | Client → Server |
| Command | `0x01` |
| Type | `0x00C2` |
| Role | Report（**客户端退出时的会话汇总**，内含一份硬件画像；fire-and-forget） |
| Trigger | **客户端正常退出**（Confirmed）：本帧是退出 flush 的第一帧，位于 EXIT(`0x0003`) 之前三帧。登录、进频道、停留在角色列表均**不会**触发；强制结束进程也不会（flush 不执行） |
| Expected response | None（Confirmed：本帧之后到 EXIT 之间无入站帧） |
| Catalog status | ClientVerified |

客户端一共发**三份**携带硬件画像的上报，全部读自同一个上报对象，但触发时机不同——
前两份属于登录握手，本协议属于退出流程：

| Type | 编码 | 触发 |
|---|---|---|
| `0x00AB` | 分桶值（主频桶、内存桶） | LOGIN 之后立即 |
| `0x01DD` | 原始值 + 系统位数 | 紧随 `0x00AB` |
| `0x00C2` | 归一化值 + 一张定长表 + 一个变长小列表 | **客户端退出时** |

### 退出序列（Confirmed，两次独立会话逐帧一致）

```text
C2S 01/0002 (31B, seq 7)     ← 登录流程的最后一帧
C2S 01/00C2 (361B, seq 8)    ← 本协议，退出 flush 的第一帧
C2S 01/00FA (50B, seq 9)
C2S 01/0117 (12B, seq 10)
C2S 01/0003 (1B, seq 11)     ← EXIT 请求
S2C    0003 (1B)             ← EXIT 响应
C2S 01/008F (5B, seq 12)
```

只进频道并停留在角色列表的会话到 seq 7 就结束，不会出现本帧。这解释了本记录早期版本
把 Trigger 误记为"进入角色列表之后"的原因：首个样本来自一次**关闭了客户端**的会话，
而后续两次只进频道的会话都没能复现，直到按退出流程重试才确认。它也解释了为什么
336 字节表全为 0——样本会话从未进入游戏，统计量没有累积。

## Wire layout

Game header 见 [Game TCP 帧组织](../../../patterns/game-frame.md)。由序列化器
`0x01CD6260`（`__thiscall`，`esi = this`，`push 0xC2` → `MakeHeader`）逐字段写出。
body 长度由结构决定并与实测精确闭合：

```text
BodyLength = 24 (header) + 336 (table) + 1 (count) + 3 * count
           = 361 + 3 * count      count ∈ 0..6
```

### 24 字节前缀

前 15 字节全部读自上报对象 `S = [esi+0x348]`，**该对象即 `0x00AB` 的上报对象**
（`0x00AB` 记录中 cdb dump 的 `0x25356700`），因此这些字段的语义直接继承已确认结论。

| Offset | Size | Encoding | Meaning | Confidence | Evidence |
|---:|---:|---|---|---|---|
| 0 | 4 | LE `u32` | CPU 实测主频 MHz ← `S+0x38` | Confirmed | `E-002`,`E-003`,`E-004` |
| 4 | 2 | LE `u16` | CPU 逻辑处理器数 ← `S+0x3C`。客户端分两次 `WriteU8` 写 `S+0x3C` 与 `S+0x3D`，在 wire 上等价于一个小端 u16 | Confirmed | `E-002`,`E-003`,`E-004` |
| 6 | 2 | LE `u16` | GPU PCI Vendor ID ← `S+0x14`（`0x00AB` slot1.v1 低 u16） | Confirmed | `E-002`,`E-003`,`E-004` |
| 8 | 2 | LE `u16` | GPU PCI Device ID ← `S+0x18`（`0x00AB` slot1.v2 低 u16） | Confirmed | `E-002`,`E-003`,`E-004` |
| 10 | 2 | LE `u16` | **归一化**显存 MB：`S+0x42`（原始 4093，`0x01DD` off 12 同源）经 `0x1CD2C80` 变换后为 4096 | 来源 Confirmed；变换规则 Hypothesis | `E-002`,`E-003`,`E-004` |
| 12 | 2 | LE `u16` | **归一化**物理内存 MB：`S+0x44`（原始 16107，`0x01DD` off 6 同源）经同一变换后为 8192。注意该变换**会向下截断**，不是四舍五入 | 来源 Confirmed；变换规则 Hypothesis | `E-002`,`E-003`,`E-004` |
| 14 | 1 | `u8` | OS 分类枚举 ← `S+0x46` 低字节（与 `0x00AB` slot2.v1 同源） | Confirmed | `E-002`,`E-003`,`E-004` |
| 15 | 4 | LE `u32` | **每会话一次性字段**：`[esi+0x380]` 标志为 0 时写 `[esi+0x384]` 的真值并置标志；此后同一对象再发本协议时恒写 `0xFFFFFFFF`。实测首帧为 `0x00090000` | 机制 Confirmed；数值含义 Hypothesis | `E-002` |
| 19 | 1 | `u8` | 序列化器第 1 个参数 `[ebp+8]`。写入前有一段读取两个解密字符串并调用 IAT 函数的分支（`[esi+0x388]` 标志同样只跑一次），该分支**可能覆写此参数**。实测 6 | 位置与宽度 Confirmed；语义未定 | `E-002` |
| 20 | 2 | LE `u16` | `0x1CD5980(esi, 0)` 的返回值。实测 `0xFFFF` | 位置与宽度 Confirmed；语义未定 | `E-002` |
| 22 | 2 | LE `u16` | `0x1CD5980(esi, 3)` 的返回值。实测 `0xFFFF` | 位置与宽度 Confirmed；语义未定 | `E-002` |

> 注意 offset 20..23 在样本里是连续四个 `FF`，很容易被当成一个 `u32 = -1`。反汇编证明
> 它是**两个独立 u16**，分别来自同一函数的不同参数。这正是"无指令级证据不得按字节切分"
> 的又一个实例。

### 336 字节定长表（offset 24..359）

双层循环写出，外层 6 次（`[ebp-0x60]` 以 `0x18` 步进直到 `0x90`），每次：

```c
struct Row {                       // 56 字节
    u16 word[4];                   // cvttss2si 截断自 [esi+0x34c]+0xC8+i*0xC0 的 float
    struct { u32 integer; f32 real; } pair[6];   // integer 取自 [esi+0x10] 起的数组；
};                                               // real = (float)integer_next * 常量
```

`6 × 56 = 336`。`pair.real` 的 4 字节经 `WriteBytes(&f, 4)`（`0x1C8FAC0`）原样写出，
是 IEEE-754 单精度。**实测样本该表 336 字节全为 0**，因此表内字段的语义无法从样本反推；
本记录只声明结构（指令级确认），不声明含义。

### 尾部变长列表（offset 360 起）

```c
u8 count;                                  // 见下方钳制规则
struct { u8 kind; u16 value; } entry[count];   // 每项 3 字节
```

- `count = ([esi+0x394] - [esi+0x390]) / 4`，**若 > 6 则写 0**（不是截断为 6）。
- 每项写入前钳制：`kind > 6` → 写 0；`value > 2000` → 写 0。
- 序列化器有两条路径都会到达这里：第 2 个参数 `[ebp+0xC]` 为 0 时直接 `WriteU8(0)`
  收尾（等价于 count=0）；非 0 时走上面的列表逻辑。两条路径在 count=0 时产生相同的 361 字节。
- **异常路径**：循环内对两个并行数组做边界检查，任一越界即跳出并**追加一个 0 字节**后收尾。
  此时 body 长度为 `361 + k*3 + 1`（k 为已写出的项数），与声明的 count 不符。
  服务端 codec 按长度不符拒绝，不把那个 0 字节当作数据。

## Samples

- [2026-08-14 Windows client sample 01](samples/2026-08-14-windows-client-01.hex)
  - Body length：361（count = 0）
  - Body SHA-256：`8783CF4537FD8FFB727234B2DA5D5F3A32293F6380527DC17B6DB24D1982747E`
  - 非零字节仅出现在 offset 0..23，其后 337 字节（含表与 count）全为 0
  - 完整帧头（未入样本文件）：`01 C200 76010000 75072BEA 0800`
    → totalLength `0x176` = 374 = 13 + 361，seq 8
  - 样本机画像：Intel 12 线程 @2611MHz / Intel UHD 770（`8086:A7A8`）/ 归一化 4096MB 显存、8192MB 内存 / Win10-11
  - **两次独立连接**（会话 1 与 `E-007` 的验证会话）的 body、`seq`（恒 8）与 checksum
    逐字节一致；两次的退出序列帧序也完全相同。注意 offset 15 的"每会话一次性"字段在
    新进程里又写回了真值 `0x00090000`，与"标志位于上报对象上、随进程重建"的机制一致

## Client reversing evidence

| Evidence | Kind | Location | Supports |
|---|---|---|---|
| `E-001` | Dynamic network observation | 2026-08-14 抓包会话 1，`clienthack-frame-capture.log` | body 精确字节、长度 361、方向、seq、在角色列表之后发送、无响应 |
| `E-002` | Serializer disassembly (static) | DNF SHA `F78F0AEA…C04C`，RVA `0x01CD6260`（内存 dump 2026-08-14） | 完整字段顺序/宽度、两条收尾路径、count 钳制、异常追加字节 |
| `E-003` | Getter disassembly (static) | `0x870A30`、`0x1CD10B0/C0/D0/E0/F0`、`0x1CD1100/1110` 均为 `mov reg,[ecx+off]; ret` | 每个前缀字段在上报对象内的确切来源偏移 |
| `E-004` | Cross-protocol identity | 来源对象 `S` 的 `+0x38/+0x3C/+0x44/+0x46` 与 `0x00AB` 记录 cdb dump（`0x25356700`）逐项吻合 | `S` 即 `0x00AB` 上报对象，故前缀字段语义继承已确认结论 |
| `E-005` | Frame writer primitives | `WriteU8/U16/U32`、`MakeHeader`、`Finalize`，新增 `WriteBytes 0x1C8FAC0` | 宽度归属与长度算术闭合（`361 + 3n`） |
| `E-006` | Entry resolution technique | 栈相邻帧：`call` 结束于 frame2 返回地址 `0x01CD6C77` → 目标 `0x01CD6260` | 定位序列化器入口；填充式启发法在此函数上给出假阳性，见工作日志 |
| `E-007` | Real-client end-to-end | 2026-08-14 第三次实机会话（服务端已实现 0x00C2）：只进频道时未发出本帧，**点击游戏内退出后**立即发出，`ClientSpecProfileHandler` 输出 `cpu=cores=12 clock=2611MHz gpu=pci=8086:A7A8 os=Windows10/11 normalized=video=4096MB total=8192MB off15=00090000 off19=6 off20=FFFF off22=FFFF table=6rows all-zero entries=none` | 触发条件（退出 flush）、解码正确性、无响应兼容；同时给出第二个独立连接的重复性样本 |

The supported DNF executable is:

```text
SHA-256 F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C
```

## Server and historical evidence

抓包当时服务端未注册 `0x00C2`，帧被 `CreateMessage` 静默丢弃且不断连，客户端继续前进。
旧服是否注册过该 type 无法核对（`86JP-rewio` 不在工作区）。

## Implementation mapping

| Layer | Type/path | Responsibility |
|---|---|---|
| Data | `ClientSpecProfileReportRequest` + `ClientSpecProfileRow` / `ClientSpecProfilePair` / `ClientSpecProfileEntry`（`Dfo.Server.Data/Networking/NetworkMessages.cs`） | typed DTO；未确认字段以 `Unknown<offset>` 命名，表内成员按 wire 类型命名（`Integer`/`Real`/`Words`），不外推语义 |
| Networking | `ClientSpecProfilePayloadCodec`（长度 = `361 + 3*count`，count ≤ 6 校验）、`GameProtocol.ClientSpecProfileType`、`GameNetworkPlugin` 接线 | typed message；无出站路径 |
| Application | `ClientSpecProfileHandler` + `ClientSpecProfileDescriptor`：语义化日志（含账号归属），**不经过命令总线** | 无 `ServerMessageType`、无响应 |
| Domain/Infrastructure | 暂无消费者前不引入持久化 | — |

日志刻意**不逐项打印那 336 字节表**（60 个数字/帧）：只报告有多少行非零，
表被填充时仍然可见，而样本全零时不制造噪音。

## Verification

- Parser/codec vectors：✅ `ClientSpecProfilePayloadCodecTests` 用 `E-001` 真实样本逐字段断言
- Boundary cases：✅ 长度 < 361、空 body、`count > 6`、长度与 count 不符（含异常路径那种多 1 字节的形状）均抛 `InvalidDataException`
- 条件分支/重复项：✅ count = 1/3/6 的尾部列表逐项断言；表按行/成员 stride 定点写值断言（错位即失败）
- 长度算术：✅ 断言 `TableRowLength = 56`、`TableLength = 336`、`MinimumBodyLength = 361`
- Handler：✅ `ClientSpecProfileHandlerTests` 覆盖账号归属、无归属容错、非零表与非空列表的日志呈现
- Response count/order：恒 0（`E-001` + 实现无发送路径）
- Real-client result：✅ **2026-08-14 本地服务端实机验证通过**（`E-007`）：进频道阶段确认不发出，
  点击游戏内退出后立即发出，服务端解码并输出语义化日志（含账号归属），未发送任何响应，
  客户端正常完成 EXIT 交换。

## Unknowns and next evidence

1. **`0x1CD2C80` 的归一化规则**——把 4093→4096、16107→8192。函数内有 `0x40` 字节局部缓冲与
   常量 `0x20`，疑似标准尺寸表查找且上限为 8192。最便宜的证据：反汇编该函数并读出表。
2. **336 字节表的含义**——结构已确认，但样本全零。既然本协议在退出时发送，表很可能是
   本次会话累积的运行统计。最便宜的证据：在真正进入过游戏的会话结束时抓一帧对照；
   或反汇编 `[esi+0x34c]` 与 `[esi+0x10]` 的写入方。
3. **offset 15 / 19 / 20 / 22 的语义**——机制清楚（一次性字段、参数、两次同函数调用），含义未定。
   最便宜的证据：`0x1CD5980` 反汇编；`[esi+0x384]` 的写入 xref；`[esi+0x388]` 分支里那两个
   解密字符串（同 `0x01DD` 的 `0x1CF50A0` 解密器，需运行时断点取明文）。
4. **尾部列表非空的样本**——count 恒 0 于当前样本，`kind`/`value` 的实际取值域未观察到。
   同上，进入过游戏的会话最可能产生非空列表。
5. **非正常退出的行为**——强制结束进程时 flush 不执行，本帧不出现（已观察）。崩溃、断线、
   切频道等路径是否走同一 flush 未验证。
6. **退出序列的其余三帧**——`0x00FA`/`0x0117`/`0x008F` 与本协议同属退出流程，尚未逆向，
   见 [协议目录](../../../catalog.md) 的候选清单。
7. **客户端版本差异**——布局绑定 DNF SHA `F78F0AEA…`；版本变更须重新验证。
