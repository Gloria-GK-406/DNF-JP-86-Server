# `0x00AB` `CLIENT_SPEC_STATISTIC`

## Identity and behavior

| Property | Value |
|---|---|
| Endpoint | Game |
| Direction | Client → Server |
| Command | `0x01` |
| Type | `0x00AB` |
| Role | Report（客户端硬件规格上报，fire-and-forget） |
| Trigger | 进入频道后的 Game 登录早期：LOGIN 响应之后、GET_USERINFO(`0x0008`) 之前，一次性发送（同会话仅一次，重复登录字节一致） |
| Expected response | None（Confirmed：发送后无等待/轮询逻辑，客户端用本地 reported 标志防重发） |
| Catalog status | ClientVerified |

客户端在登录握手阶段向服务端上报本机硬件画像：CPU / GPU / 操作系统 / 物理内存。
服务端已实现：解码为 typed report 并以语义化日志记录（暂无存储消费者）；无 ACK。

## Observed sequence

六次独立连接 + 一次 cdb 附加会话均观察到：

```text
Game login response
→ C2S 01/00AB (39-byte body)
→ C2S 01/01DD (16-byte body)
→ C2S 01/0008 account-character roster request
```

客户端在 `0x00AB` 后继续前进，没有等待、重试或因缺少响应中断。

## Wire layout

Game header 见 [Game TCP 帧组织](../../../patterns/game-frame.md)。Body 固定 39 字节，
由客户端序列化器 `0x01CD1120`（`__thiscall`, `ret 4`）逐字段写入，结构完全闭合、零剩余字节：

```c
struct Body00AB {
    u8  trigger;              // 触发路径标志（见下）
    u8  reported;             // 上报时本地已上报标志（恒 0，发送后置 1）
    u8  count;                // 常量 4
    struct { u8 tag; u32 v1; u32 v2; } slot[4];   // tag 恒为 0,1,2,3（定长槽位）
};
```

| Offset | Size | Encoding | Meaning | Confidence | Evidence |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | trigger：`0x80`=主路径（wrapper `0x1CD12C0`, `push 0`），`0x81`=条件路径（`0x1CD1360`, `push 1`，需 `0x10F9DA0`+`0xB61F70` 为真） | Confirmed | `E-006` |
| 1 | 1 | `u8` | reported 标志（序列化器末尾 `mov [ebx+0x34],1`） | Confirmed | `E-006` |
| 2 | 1 | `u8` | 槽位数 = 常量 4 | Confirmed | `E-006` |
| 3 | 1 | `u8` | slot0.tag = 0 | Confirmed | `E-006` |
| 4 | 4 | LE `u32` | slot0.v1 = CPU：`(逻辑处理器数 & 0x80FFFFFF) + 厂商桶×0x1000000`；桶 0=Intel / 1=AMD / 2=其他（cpuid 厂商串前缀比较，`0x21985A0`） | Confirmed | `E-006`,`E-007`,`E-008` |
| 8 | 4 | LE `u32` | slot0.v2 = CPU 主频桶（实测 MHz，注册表 `~MHz` 优先、否则 Sleep 校准循环）：0≤1050, 1≤1650, 2≤2450, 3>2450 | Confirmed | `E-007`,`E-008` |
| 12 | 1 | `u8` | slot1.tag = 1 | Confirmed | `E-006` |
| 13 | 4 | LE `u32` | slot1.v1 = GPU PCI Vendor ID（u16 扩展，SystemInfo `+0x7C`） | Confirmed | `E-006`,`E-008`,`E-009` |
| 17 | 4 | LE `u32` | slot1.v2 = GPU PCI Device ID（u16 扩展，SystemInfo `+0x7E`） | Confirmed | `E-006`,`E-008`,`E-009` |
| 21 | 1 | `u8` | slot2.tag = 2 | Confirmed | `E-006` |
| 22 | 4 | LE `u32` | slot2.v1 = OS 分类枚举（`0x2168ED0`）：1=Win98, 3=WinME, 4=Win95/NT4, 6=Win2000, 7=WinXP, 9=Vista, 0xA=Win7SP1, 0xC=Win8, 0xD=Win8.1, 0xE(14)=Win10/11 | Confirmed | `E-008` |
| 26 | 4 | LE `u32` | slot2.v2 = Service Pack 值（现代 Windows 恒 0） | Confirmed | `E-008` |
| 30 | 1 | `u8` | slot3.tag = 3 | Confirmed | `E-006` |
| 31 | 4 | LE `u32` | slot3.v1 = 常量 0 | Confirmed | `E-006` |
| 35 | 4 | LE `u32` | slot3.v2 = 物理内存桶（`ullTotalPhys>>20` 即 MB）：0≤136, 1≤200, 2≤264, 3≤392, 4≤520, 5≤1032, 6≤2056, 7>2056 | Confirmed | `E-006`,`E-008`,`E-009` |

> **旧布局假设（已推翻）**：本记录早期版本仅凭单样本字节把 body 切成
> `u16+u16+u32+u32+u8+u32+u32+…`。序列化器反汇编证明实际是
> `u8+u8+u8+4×(u8,u32,u32)` 循环写入（`esi` 以 0x0C 步进）。旧表中 offset 13/17
> 的 PCI Vendor/Device ID 猜测**方向正确**（与新布局的 slot1.v1/v2 重合），
> 其余 Unknown 字段边界全部失效。教训：无指令级证据前不得按字节猜边界。

## Samples

- [2026-08-13 Windows client sample 01](samples/2026-08-13-windows-client-01.hex)
  - Body length：39；同一客户端 6 次独立连接完全一致。
  - Body SHA-256：`C76FD00687666BB30214C1BB785F7A18104F8C20DAD63BD1916CC9083022A484`
  - 样本机画像（对应字段值）：Win11 / Intel 12 线程 @2611MHz / 16GB / Intel UHD 770
    → `(12,3) (0x8086,0xA7A8) (14,0) (0,7)`
- 2026-08-14 cdb 附加会话在序列化器入口 dump 的对象状态与上述值逐字段一致
  （见 `E-009`；未产生新 wire 样本，body 与 2026-08-13 样本相同）。

## Client reversing evidence

| Evidence | Kind | Location | Supports |
|---|---|---|---|
| `E-001` | Dynamic network observation | 2026-08-13 functional run, sample above | Exact body, length, ordering and repeatability |
| `E-002` | Static enum | `86JP-rewio/Server/DfoServer/Network/Core/PacketTypes.cs` | Type name `CLIENT_SPEC_STATISTIC` |
| `E-003` | Historical server behavior | `86JP-rewio/Server/DfoServer/Network/Protocol/GameProtocolHandler.cs` and Git history | No registered handler/response in available legacy implementation |
| `E-004` | Platform structure | Direct3D 9 `D3DADAPTER_IDENTIFIER9` | VendorId and DeviceId are adjacent DWORD adapter identifiers |
| `E-005` | Client send-chain reversing | `ClientHack/plugins/0100_network_hook/network_hook.c` | Header/body boundary and candidate trace point |
| `E-006` | Serializer disassembly (static) | DNF SHA `F78F0AEA…C04C`, RVA `0x1CD1120`（内存 dump，2026-08-14） | 精确 body 结构、reported 标志、两条触发路径 |
| `E-007` | Filler disassembly (static) | RVA `0x20D0E80`（上报对象填充器）、`0x21985A0`（CPU 检测） | slot0 语义：厂商桶编码、主频桶阈值、核心数来源 |
| `E-008` | SystemInfo collector disassembly | RVA `0x216B340` + `0x2168ED0` + `0x2169400/40/50` + IAT 注释（GetSystemInfo/cpuid/GlobalMemoryStatusEx） | slot1/2/3 数据来源与全部阈值 |
| `E-009` | Live debugger verification | cdb attach `DNF.unpacked-b1-debug.exe`，断点 `0x20D1120`，2026-08-14（现场日志见 workspace `analysis/cdb-00ab-live3.log`） | 运行时对象值、SystemInfo 全局、`"Intel"` 厂商串、15.93GB `ullTotalPhys` 全部吻合 |
| `E-010` | Real-client end-to-end | 2026-08-14 本地服务端（重构版）实机会话：客户端 NetworkHook 抓到出站帧与 E-001 逐字节一致，服务端解码并输出语义日志，客户端随即发出 GET_USERINFO | 完整 ClientVerified 证据：方向、触发、无响应兼容、解码正确性 |

The supported DNF executable is:

```text
SHA-256 F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C
```

### 数据对象架构（E-006~E-009 综合）

```text
facade [0x3091FB8]（0x28B，InitMain 期间 RVA 0xD88E98 创建）
  └ +0x10 → 00AB 上报对象（0x4CB）
        +0x00  → SystemInfo 全局 0x31FB498（指针）
        +0x04..+0x34 → 4 槽位 + reported 标志（序列化直接读这里）
        +0x38..+0x48 → 镜像缓存（MHz/核心数/厂商桶/PCI ID，画质决策 getter 用）
填充器 = 上报对象方法 0x20D0E80（facade 初始化 0x1CD13B0 时调用一次）
SystemInfo 全局 0x31FB498：
  +0x00 OS枚举  +0x04 SP值          （0x2168ED0 版本分类器）
  +0x18 ullTotalPhys（64 位）        （GlobalMemoryStatusEx）
  +0x38 CPU MHz  +0x3C 核心数  +0x40 内联 L"Intel"
  +0x7C GPU PCI VendorId(u16)  +0x7E GPU PCI DeviceId(u16)  +0x84 显卡名
```

### 序列化器伪代码（RVA `0x1CD1120`，指令级）

```c
int __thiscall Serialize00AB(Reporter* this, u8 path) {   // ret 4
    MakeHeader(0x00AB);                                   // 13 字节 Game 头
    WriteU8(path - 0x80);                                 // body[0]
    WriteU8(this->reported);                              // body[1]，此时为 0
    WriteU8(4);                                           // body[2] count
    for (int i = 0; i < 4; i++) {                         // esi 步进 0x0C
        WriteU8 (this->slots[i].tag);                     // [0x04+0x0C*i]
        WriteU32(this->slots[i].v1);                      // [0x08+0x0C*i]
        WriteU32(this->slots[i].v2);                      // [0x0C+0x0C*i]
    }
    Finalize();                                           // 0x208FDD0
    this->reported = 1;                                   // [0x34] 防重发
    return 1;
}
```

完整工作日志（含反汇编过程、facade 全方法簇、cdb 会话设计）：
[reverse-engineering-log.md](reverse-engineering-log.md)。

## Server and historical evidence

The latest rewio legacy server contains the enum but no `0x00AB` dispatch registration. Its fallback logs the body and sends no response. The client still reaches the roster screen, matching the current refactored server's observed behavior.

This proves compatibility with no response for the observed login path. It does not prove that every future client version treats the report identically.

## Implementation mapping

| Layer | Type/path | Responsibility |
|---|---|---|
| Data | `ClientSpecStatisticReportRequest` + `ClientSpecStatisticSlot`（`Dfo.Server.Data/Networking/NetworkMessages.cs`） | 中性槽位 DTO（tag/v1/v2），语义解读留在消费侧 |
| Networking | `ClientSpecStatisticPayloadCodec`（严格 39 字节 + count=4 + tag 顺序校验）、`GameProtocol.ClientSpecStatisticType`、`GameNetworkPlugin` 接线 | typed message；无出站路径 |
| Application | `ClientSpecStatisticHandler` + `ClientSpecStatisticSlotDescriptor`：语义化日志（含账号归属），**不经过命令总线**（纯遥测、无领域变更；见 AGENTS.md 偏差清单） | 无 `ServerMessageType`、无响应 |
| Domain/Infrastructure | 暂无消费者前不引入持久化 | — |

## Verification

- Parser/codec vectors：✅ `ClientSpecStatisticPayloadCodecTests` 用 E-001 真实样本逐字段断言，
  另覆盖厂商桶编码、长度/count/tag 异常拒绝
- Boundary cases：✅ 长度 ≠ 39、`count != 4`、tag 非连续均抛 `InvalidDataException`
- Handler：✅ `ClientSpecStatisticHandlerTests` 覆盖账号归属日志与无归属容错（不抛错断连）
- Response count/order：恒 0（E-003/E-006 双重证明 + 实现无发送路径）
- Real-client result：✅ **2026-08-14 本地服务端实机验证通过**：客户端连入本地服务端，
  进入频道后依次发出 LOGIN → 00AB → GET_USERINFO；服务端日志输出语义化报告
  （`cpu=cores=12 vendor=Intel clock=>2450MHz gpu=pci=8086:A7A8 os=Windows10/11 sp=0
  memory=bucket=7 (>2056MB)`，含账号归属），客户端未等待直接继续（`E-010`）

## Unknowns and next evidence

1. **`0x81` 条件路径的实际触发条件**（`0x1CD1360` 的 `0x10F9DA0`+`0xB61F70` 检查）——当前仅见 `0x80` 样本；需要不同登录状态采样。
2. **精确的"登录就绪→发送"路由**：静态 xref 只有 `0x8C7E7E` 一个直接调用者，但 cdb 实测返回地址链（`0x1CD12CA→0xD8566E→0xD89C36→0x1C90D3B→0xC601BA→DispatchMessageW`）落在发送管线内，疑有间接调用/延迟序列化。不影响协议实现。
3. **不同硬件的取值范围**：仅一台实机样本（Intel 核显/Win11）；AMD/独显/其他 OS 桶值需采样验证，尤其厂商桶 ×0x1000000 编码的 AMD 分支。
4. **客户端版本差异**：布局记录绑定 DNF SHA `F78F0AEA…`；版本变更须重新验证。
