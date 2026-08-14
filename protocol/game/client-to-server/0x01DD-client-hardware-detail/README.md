# `0x01DD` `CLIENT_HARDWARE_DETAIL`

> 名称说明：`CLIENT_HARDWARE_DETAIL` 是**本资料库的描述性命名**，不是客户端或旧服枚举名。
> 与 `0x00AB`（名称来自旧服 `PacketTypes.cs`）不同，本 type 目前没有任何可用的
> 枚举证据；旧服源码树 `86JP-rewio` 当前不在工作区，无法核对。若日后取得枚举名，
> 应改名并保留此处的旧名映射。

## Identity and behavior

| Property | Value |
|---|---|
| Endpoint | Game |
| Direction | Client → Server |
| Command | `0x01` |
| Type | `0x01DD` |
| Role | Report（客户端硬件规格原始值上报，fire-and-forget） |
| Trigger | 紧接 `0x00AB` 之后、`0x0008` GET_USERINFO 之前，登录握手内一次性发送 |
| Expected response | None（Confirmed：抓包显示 01DD 与其后的 `0x0008` 之间没有任何入站帧，客户端不等待） |
| Catalog status | ClientVerified |

`0x00AB` 上报的是**分桶后**的硬件画像（主频桶、内存桶），`0x01DD` 紧随其后上报**同一批硬件的原始值**
（实测 MHz、物理内存 MB、显存 MB），并额外上报 `0x00AB` 完全没有的**系统位数**。
两者读取同一个 SystemInfo 提供者对象，但各自持有独立的记录对象与独立的字段布局。

## Observed sequence

2026-08-14 本地服务端实机会话（全量双向抓包，8 帧）：

```text
C2S 01/0001  LOGIN                     (73-byte body)
S2C    /0001 LOGIN response            (54-byte body)
C2S 01/00AB  CLIENT_SPEC_STATISTIC     (39-byte body)
C2S 01/01DD  本协议                     (16-byte body)   seq=2
C2S 01/0008  GET_USERINFO              (3-byte body)
S2C    /0002 角色列表                    (18-byte body，空名单)
C2S 01/02A8                            (2-byte body, FFFF)
C2S 01/0002                            (31-byte body)
```

顺序与 `0x00AB` 记录中"LOGIN → 00AB → 01DD → 0008"的既有观察一致。

> 附带修正：`0x00AB` 记录的内存扫描推断表把 `0x02A8` 与 `0x0002` 标为 S→C。本次抓包
> 直接观察到二者也作为 **C→S** 发送（`0x0002` 更是双向都出现）。type 不是全局身份，
> 需按方向分别建记录，详见 [协议目录](../../../catalog.md) 的约定。

## Wire layout

Game header 见 [Game TCP 帧组织](../../../patterns/game-frame.md)。Body 固定 **16 字节**，
由构包器 `0x00C375B0`（`__thiscall`，`esi = this`，`ret`）用共享帧写入原语逐字段写出，
宽度合计 `1+1+4+2+2+2+2+1+1 = 16`，与实测 body 长度精确闭合、零剩余字节：

```c
struct Body01DD {
    u8  cpu_vendor_bucket;   // this+0x04
    u8  cpu_logical_cores;   // this+0x05
    u32 cpu_clock_mhz;       // this+0x08
    u16 total_memory_mb;     // this+0x0C
    u16 gpu_pci_vendor_id;   // this+0x0E
    u16 gpu_pci_device_id;   // this+0x10
    u16 video_memory_mb;     // this+0x12
    u8  os_kind;             // this+0x14
    u8  os_bitness;          // this+0x15
};
```

| Offset | Size | Encoding | Meaning | Confidence | Evidence |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | CPU 厂商桶。填充器对 CPU 标识串做两次 token 比较：匹配 5 字符 token → `0`；否则匹配 3 字符 token → `1`；再否则 → `2`。编码与 `0x00AB` 已确认的 `0=Intel / 1=AMD / 2=其他` 一致 | 编码 Confirmed；字面量身份 High（字符串经 `0x1CF50A0` 解密，未取明文） | `E-002`,`E-003`,`E-007` |
| 1 | 1 | `u8` | CPU 逻辑处理器数 | Confirmed | `E-003`,`E-005` |
| 2 | 4 | LE `u32` | CPU 实测主频（MHz，原始值，非分桶） | Confirmed | `E-003`,`E-005` |
| 6 | 2 | LE `u16` | 物理内存 MB = `ullTotalPhys >> 20`（64 位右移，`shrd`/`shr 0x14`） | Confirmed | `E-003`,`E-005` |
| 8 | 2 | LE `u16` | GPU PCI Vendor ID | Confirmed | `E-003`,`E-005` |
| 10 | 2 | LE `u16` | GPU PCI Device ID | Confirmed | `E-003`,`E-005` |
| 12 | 2 | LE `u16` | `SystemInfo + 0xA0` 的字节数 `>> 20`（MB）。该字段位于结构体的 GPU/D3D 区块（紧邻 `+0x7C/+0x7E` PCI ID 与 `+0x84` 显卡名），实测 `0xFFD00000` = 4093 MiB，符合 D3D9 可用纹理内存的典型饱和值 | 取值与来源 Confirmed；"显存"语义 High | `E-003`,`E-005` |
| 14 | 1 | `u8` | OS 分类枚举，与 `0x00AB` slot2.v1 同源同值域（`0x0E` = Win10/11） | Confirmed | `E-003`,`E-005`,`E-007` |
| 15 | 1 | `u8` | 系统位数：`0` = 未查询/查询失败，`1` = 32 位 Windows，`2` = 64 位 Windows。经 `IsWow64Process(GetCurrentProcess(), &out)` 判定 | Confirmed | `E-003`,`E-006` |

### 发送条件（Confirmed，`E-002`,`E-003`）

构包器在写任何字节之前有两道门，任一不过即**完全不发送本帧**（返回 false，无空帧、无占位）：

1. `0x21216D9(0, 0x29C2D24)` 返回非 0 → 直接返回 false。该调用的参数是一个描述符结构而非字符串，
   语义未定（见 Unknowns）。
2. 填充器 `0x00C37490` 返回 false，发生在 `this+0x00`（SystemInfo 提供者指针）为空
   或 `this+0x18`（`IsWow64Process` 函数指针）为空时。后者意味着在缺少
   `IsWow64Process` 的老 Windows 上，本协议根本不会出现在 wire 上。

因此服务端**不得**假设每个会话必然收到 `0x01DD`：它比 `0x00AB` 多一层前置条件。

### 与 `0x00AB` 的字段对应（`E-007`）

同一台实机、同一次会话，两条协议的值互为交叉验证：

| 硬件项 | `0x00AB`（分桶） | `0x01DD`（原始） |
|---|---|---|
| CPU 厂商 | slot0.v1 高字节桶 = 0 | off 0 = 0 |
| CPU 核心数 | slot0.v1 低 24 位 = 12 | off 1 = 12 |
| CPU 主频 | slot0.v2 桶 = 3（>2450MHz） | off 2 = 2611 |
| 物理内存 | slot3.v2 桶 = 7（>2056MB） | off 6 = 16107 |
| GPU PCI | slot1 = `8086:A7A8` | off 8/10 = `8086:A7A8` |
| OS | slot2.v1 = 14 | off 14 = 14 |
| Service Pack | slot2.v2 = 0 | 未上报 |
| 显存 | 未上报 | off 12 = 4093 |
| 系统位数 | 未上报 | off 15 = 2 |

## Samples

- [2026-08-14 Windows client sample 01](samples/2026-08-14-windows-client-01.hex)
  - Body length：16
  - Body SHA-256：`8978BA4920ADEE4EBD4D1E82886EC020EA2455A60D02B47B1256444B2743F053`
  - 完整帧头（未入样本文件，仅记录）：`01 DD01 1D000000 92E5CFA9 0200`
    → cmd `0x01`、type `0x01DD`、totalLength 29 = 13 + 16、seq 2
  - 样本机画像：Win11 x64 / Intel 12 线程 @2611MHz / 16107MB / Intel UHD 770（`8086:A7A8`）/ 4093MB 显存
    → `(vendor=0, cores=12, mhz=2611, mem=16107, gpu=8086:A7A8, vram=4093, os=14, bitness=2)`
  - 同会话仅出现一次。**两次独立连接**（2026-08-14 抓包会话 1 与实机验证会话 2）的
    body、`seq`（恒 2）与 checksum 逐字节一致——checksum 亦相同说明它由内容派生、不含随机量

登录帧（`0x0001`）含账号凭证与启动参数 auth hash，**按安全边界未提交**。

## Client reversing evidence

| Evidence | Kind | Location | Supports |
|---|---|---|---|
| `E-001` | Dynamic network observation | 2026-08-14 全量抓包会话，`clienthack-frame-capture.log`（客户端目录） | body 精确字节、长度、方向、在登录握手中的位置、无响应 |
| `E-002` | Builder disassembly (static) | DNF SHA `F78F0AEA…C04C`，RVA `0x00C375B0`（内存 dump 2026-08-14） | 字段顺序/宽度/来源偏移、`MakeHeader(0x01DD)`、两道发送门 |
| `E-003` | Filler disassembly (static) | RVA `0x00C37490`（同一 dump） | 每个字段的数据来源与换算（`>>20`）、位数判定、厂商桶三分支 |
| `E-004` | Frame writer primitives | `0x1C8F5B0` / `0x1C90460` / `0x1C90550` / `0x1C90580` / `0x1C905B0` / `0x1C8FDD0`，与 `0x00AB` 已确认序列化器 `0x1CD1120` 交叉验证 | u8/u16/u32 写入宽度归属；见 [game-frame](../../../patterns/game-frame.md) |
| `E-005` | Live debugger values | `analysis/cdb-00ab-live3.log`（2026-08-14 00AB 会话所 dump 的 SystemInfo 全局 VA `0x031FB498`） | `ullTotalPhys = 0x3EEBDE000 >> 20 = 16107`、`[+0xA0] = 0xFFD00000 >> 20 = 4093`、`+0x38 = 2611`、`+0x3C = 12`、`+0x00 = 14` 与 wire 逐字段吻合 |
| `E-006` | IAT resolution | dump 内 `DNF-iat.csv`：slot VA `0x027D3120` = `kernel32!GetCurrentProcess` | 位数字段的判定路径为 `IsWow64Process(GetCurrentProcess(), &out)` |
| `E-007` | Cross-protocol consistency | 本记录"与 0x00AB 的字段对应"表 | 厂商桶编码、OS 枚举值域、核心数/PCI ID 与已确认语义一致 |
| `E-008` | Response absence | `E-001` 抓包中 01DD 与其后 `0x0008` 之间无入站帧；服务端当时未注册该 type，静默丢弃 | 无响应即可兼容 |
| `E-009` | Real-client end-to-end | 2026-08-14 第二次实机会话（服务端已实现 01DD）：`ClientHardwareDetailHandler` 输出 `cpu=cores=12 vendor=Intel clock=2611MHz gpu=pci=8086:A7A8 video=4093MB os=Windows10/11 bitness=64 memory=16107MB`（含账号归属），随后 `GetAccountCharacterRosterCommand` 正常执行 | 完整 ClientVerified 证据：解码正确性、无响应兼容、客户端不受影响地进入角色列表；同时给出第二个独立连接的重复性样本 |

The supported DNF executable is:

```text
SHA-256 F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C
```

### 构包器伪代码（RVA `0x00C375B0`，指令级）

```c
bool __thiscall Build01DD(Report01DD* this) {          // esi = this, ret
    if (Gate_0x21216D9(0, &desc_0x29C2D24)) return false;
    if (!Fill01DD(this)) return false;                 // 0x00C37490
    MakeHeader(0x01DD);                                // 0x1C90460, 13-byte Game header
    WriteU8 (this->cpu_vendor_bucket);                 // [esi+0x04]
    WriteU8 (this->cpu_logical_cores);                 // [esi+0x05]
    WriteU32(this->cpu_clock_mhz);                     // [esi+0x08]
    WriteU16(this->total_memory_mb);                   // [esi+0x0C]
    WriteU16(this->gpu_pci_vendor_id);                 // [esi+0x0E]
    WriteU16(this->gpu_pci_device_id);                 // [esi+0x10]
    WriteU16(this->video_memory_mb);                   // [esi+0x12]
    WriteU8 (this->os_kind);                           // [esi+0x14]
    WriteU8 (this->os_bitness);                        // [esi+0x15]
    Finalize();                                        // 0x1C8FDD0
    return true;
}
```

注意与 `0x00AB` 的差异：`0x00AB` 的序列化器末尾会写 `reported` 防重发标志
（`mov [ebx+0x34],1`），**`0x01DD` 没有等价标志**，也没有把标志值写进 body。

完整工作日志（含填充器逐指令、原语交叉验证、调用栈标定方法）：
[reverse-engineering-log.md](reverse-engineering-log.md)。

## Server and historical evidence

抓包当时服务端尚未注册 `0x01DD`：`CreateMessage` 对未知 type 返回 `null`，帧被静默丢弃且不断连，
客户端随后正常发出 `0x0008` 并进入角色列表——这直接证明"无响应"对观察到的登录路径是兼容的。

旧服 `86JP-rewio` 源码树当前不在工作区，因此**未能**核对旧服是否注册过该 type、
以及它的枚举名。这是与 `0x00AB`（有 `E-002`/`E-003` 旧服证据）相比的证据缺口。

## Implementation mapping

| Layer | Type/path | Responsibility |
|---|---|---|
| Data | `ClientHardwareDetailReportRequest`（`Dfo.Server.Data/Networking/NetworkMessages.cs`） | 九字段 typed DTO，保留 wire 原始宽度与命名，不做语义外推 |
| Networking | `ClientHardwareDetailPayloadCodec`（严格 16 字节）、`GameProtocol.ClientHardwareDetailType`、`GameNetworkPlugin` 接线 | typed message；无出站路径 |
| Application | `ClientHardwareDetailHandler` + `ClientHardwareDetailDescriptor`：语义化日志（含账号归属），**不经过命令总线**（纯遥测、无领域变更） | 无 `ServerMessageType`、无响应 |
| Domain/Infrastructure | 暂无消费者前不引入持久化 | — |

## Verification

- Parser/codec vectors：✅ `ClientHardwareDetailPayloadCodecTests` 用 `E-001` 真实样本逐字段断言
- Boundary cases：✅ 长度 ≠ 16（含 15/17/0）均抛 `InvalidDataException`；全 `0xFF` 与全 `0x00` 极值可解码
  （本协议无 count/tag 自洽字段，因此**没有**可拒绝的结构性非法值——这是与 `0x00AB` 的重要差别）
- Handler：✅ `ClientHardwareDetailHandlerTests` 覆盖账号归属日志与无归属容错（不抛错断连）
- Response count/order：恒 0（`E-008` + 实现无发送路径）
- Real-client result：✅ **2026-08-14 本地服务端实机验证通过**（`E-009`）：客户端连入已实现
  01DD 的服务端，登录后依次触发 `ClientSpecStatisticHandler`（00AB）与
  `ClientHardwareDetailHandler`（01DD），两条语义化日志的硬件值互相印证
  （00AB `clock=>2450MHz` / `bucket=7` ↔ 01DD `clock=2611MHz` / `16107MB`），
  服务端未发送任何响应，客户端随即完成角色列表请求。

## Unknowns and next evidence

1. **`0x21216D9(0, 0x29C2D24)` 门的语义**——参数是描述符结构（`0x29C2D24` 首 32 字节为指针/计数，
   非字符串）。最便宜的证据：反汇编 `0x21216D9` 并解析该描述符类型；或对该门下断点观察返回值。
2. **厂商桶两个 token 的明文**——字符串经 `0x1CF50A0` 解密，静态取不到明文。最便宜的证据：
   在 `0x00C374CA`/`0x00C374EF` 之后断点读取 `eax` 指向的解密结果。
3. **off 12 的确切来源 API**——已确认取自 `SystemInfo + 0xA0` 且值为 `0xFFD00000`，但写入方未定位。
   最便宜的证据：扫描 `+0xA0` 的写入 xref，或查 IAT/D3D9 `GetAvailableTextureMem` 的调用点。
4. **跨硬件的取值范围**——同机两次独立连接已逐字节一致（`E-001`,`E-009`），但仍只有**一台**
   实机（Intel 核显 / Win11 x64）。未采样：AMD（验证厂商桶 = 1 及其 3 字符 token）、
   独立显卡（off 12 在独显下是否变为真实显存而非 4093 的饱和值）、32 位 Windows（验证位数 = 1）、
   以及缺少 `IsWow64Process` 的老系统（验证本协议整帧不发送的分支）。
5. **是否存在防重发标志**——`0x00AB` 有 `reported` 标志，`0x01DD` 的构包器没有等价写入。
   同会话内是否可能重复发送未验证；需要长会话或频道切换采样。
6. **客户端版本差异**——布局绑定 DNF SHA `F78F0AEA…`；版本变更须重新验证。
