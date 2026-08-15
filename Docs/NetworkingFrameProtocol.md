# Networking TCP 帧协议（代码反推草案）

## 文档状态

本文档不是经过客户端抓包或原始协议资料确认的正式协议说明。

当前内容由旧主干中的服务器实现反向整理，目的是为 Networking 重构、TouchSocket
适配器实现和自动化测试提供一个明确且可修订的基线。字段含义、方向差异、校验规则和
长度上限都必须在后续真实客户端抓包后再次确认。

确认状态约定：

- **代码事实**：可以从旧服务器实现直接观察到。
- **实现推断**：根据代码行为推断，但尚未由抓包确认。
- **临时策略**：重构期间为防止死循环、无限缓存或过量分配而增加，不代表原协议要求。
- **待确认**：必须通过客户端抓包、客户端反编译结果或其他可信资料确认。

## 反推来源

以下路径指旧主干中的实现；重构分支不会继续依赖这些类型：

- `Server/DfoServer/Network/Core/Packet.cs`
  - `ChannelPacketHeader`
  - `GamePacketHeader`
- `Server/DfoServer/Network/Core/FlexiblePacketProcessor.cs`
  - 使用包头中的长度字段从 TCP 字节流拆帧。
- `Server/DfoServer/Network/Core/GamePacketEnvelopeBuilder.cs`
  - 构造服务端发往 Game 客户端的帧。
- `Server/DfoServer/Network/Protocol/ChannelProtocolHandler.cs`
  - 构造服务端发往 Channel 客户端的帧。

仓库中未发现已经提交的 `.pcap` 或 `.pcapng` 文件。根目录的 `packet-capture.bat`
和 `Tool/PvfProxy` 可以生成后续核对所需的捕获日志，但代理目前按 TCP read 记录数据，
日志中的一次记录不能直接等同于一个协议帧。

## 通用约定

### 字节序

多字节整数按 Little Endian 处理。

状态：**实现推断**。旧代码使用 `BinaryReader.ReadUInt16/ReadUInt32`、`BitConverter`
和 Windows/x86 环境下的结构体封送，行为均表现为 Little Endian；仍需用真实数据确认。

### 长度语义

Channel 的 `sLength` 和 Game 的 `length` 都按“包头长度 + Body 长度”的总帧长度处理：

```text
BodyLength = TotalLength - HeaderLength
```

状态：**代码事实**。

### TCP 流语义

一次 TCP read 可能包含：

- 不完整包头；
- 完整包头和不完整 Body；
- 一个完整帧；
- 多个连续帧；
- 一个或多个完整帧以及下一个帧的前半部分。

Networking 必须缓存半帧，并且只在完整帧形成后向后续解码层投递。

## Channel 帧

### 客户端到服务端

旧实现使用 11 字节固定包头：

| Offset | 长度 | 类型 | 旧字段名 | 当前解释 | 状态 |
|---:|---:|---|---|---|---|
| 0 | 1 | `byte` | `classification` | 分类标识 | 待确认 |
| 1 | 1 | `byte` | `msg_no` | 消息编号 | 代码事实 |
| 2 | 4 | `uint32 LE` | `sLength` | 包含 11 字节包头的总帧长度 | 代码事实 |
| 6 | 4 | `uint32 LE` | `check_sum` | 校验值 | 待确认 |
| 10 | 1 | `byte` | `ack` | 应答标识 | 待确认 |
| 11 | `sLength - 11` | bytes | Body | 消息负载 | 代码事实 |

### 服务端到客户端

旧服务端使用相同的 11 字节布局，并在观察到的发送路径中设置：

```text
classification = 0x7C
check_sum       = 0
ack             = 1
```

状态：发送值是**代码事实**；这些值是否对所有命令都固定仍然**待确认**。

### 当前已反推的 Channel Message

下表来自旧 `ChannelProtocolHandler` 的实现，并非抓包确认结果。当前重构实现已经为这些
Message 建立强类型请求、Command 和响应编码：

| 客户端 `msg_no` | 服务端 `msg_no` | 语义 | 响应 Body |
|---:|---:|---|---|
| `0x05` | `0x06` | 检查脚本版本 | 4 字节零前缀、最长 16 字节 ASCII 版本字段，补零到 AES 块后加密，不压缩 |
| `0x09` | `0x0A` | 获取频道脚本 | 文件原始字节补零到 AES 块、AES-128-ECB 加密、zlib 压缩 |
| `0x0B` | `0x0C` | 建立频道连接 | 4 字节零前缀、32 字节 ASCII 密钥字段，不加密 |
| `0x11` | `0x12` | 获取新版频道列表 | 列表明文补零到 AES 块、AES-128-ECB 加密、zlib 压缩 |

当前代码反推的 AES 密钥为本地日期的 `yyyyMMdd000006`，按 UTF-8 写入 16 字节密钥区并
在末尾补零。AES 模式为 ECB、无内建 Padding；明文使用零字节手动对齐到 16 字节。
这些密码学选择只是在兼容旧客户端协议，不代表推荐的通用安全设计。

频道列表加密前的明文布局为：

| Offset | 长度 | 内容 |
|---:|---:|---|
| 0 | 2 | 固定零 |
| 2 | 4 | `int32 LE` 频道数量 |
| 6 | `count × 48` | 频道条目 |

每个 48 字节频道条目依次为：20 字节零结尾 ASCII 名称、`int32 LE` 最大人数、
`int32 LE` 当前人数、16 字节零结尾 ASCII 地址、`int32 LE` Game 端口。旧实现忽略上述
四种请求的 Body；这一行为也仍需抓包确认。

## Game 帧

### 客户端到服务端

旧接收实现使用 13 字节固定包头：

| Offset | 长度 | 类型 | 旧字段名 | 当前解释 | 状态 |
|---:|---:|---|---|---|---|
| 0 | 1 | `byte` | `cmd` | 不透明 selector；Protocol 解释为 CMD=`0x01` / NOTI=`0x00` | 客户端反编译确认 |
| 1 | 2 | `uint16 LE` | `type` | 业务消息类型 | 代码事实 |
| 3 | 4 | `uint32 LE` | `length` | 包含 13 字节包头的总帧长度 | 代码事实 |
| 7 | 4 | `uint32 LE` | `checksum` | 校验值 | 待确认 |
| 11 | 2 | `uint16 LE` | `seq` | 序号 | 待确认 |
| 13 | `length - 13` | bytes | Body | 消息负载 | 代码事实 |

### Game 初始连接通知 `NOTI 0x00/type=0x0001`

Game TCP 连接建立后，服务端必须先发送一次初始连接通知；客户端在收到它之前不需要先发送
LOGIN 或其他 Game 帧。当前实现先登记匿名在线连接，再根据接受连接的精确监听端口解析频道，
最后发送通知。Channel 端点不会发送该通知；监听端口无法解析为 `1..255` 的频道编号时，连接
握手明确失败并撤销刚登记的在线身份。

通知使用下文所述的 15 字节服务端 Game 包头，`cmd=0x00/type=0x0001`。Body 布局如下：

| 顺序 | 长度 | 内容 | 状态 |
|---:|---:|---|---|
| 1 | 1 | 成功值 `0x01` | 代码事实 |
| 2 | 4 + N | ASCII DString `ch.<频道编号>` | 代码事实 |
| 3 | 4 | `int32 LE` 零 | 待确认 |
| 4 | 4 | `int32 LE` 零 | 待确认 |
| 5 | 1 | 服务编号 `0x01` | 待确认 |
| 6 | 1 | 当前监听端口对应的频道编号 | 代码事实 |
| 7 | 1 | 固定零 | 待确认 |
| 8 | 4 | `int32 LE` Unix 秒时间戳；每份通知只取值一次 | 实现推断 |
| 9 | 4 | `int32 LE` 值 `1` | 待确认 |
| 10 | 4 + M | ASCII DString 配置的对外 IPv4 地址 | 实现推断 |
| 11 | 4 | `int32 LE` 值 `12311` | 待确认 |
| 12 | 4 | `int32 LE` 值 `12312` | 待确认 |
| 13 | 1 | 固定零 | 待确认 |
| 14 | 1 | 固定零 | 待确认 |
| 15 | 4 | `int32 LE` 值 `1086` | 待确认 |
| 16 | 4 | `int32 LE` 值 `1036` | 待确认 |

DString 仍是 `int32 LE` 字节长度后紧跟 ASCII 字节，不带零终止符。上述布局和常量由旧服务端
`BuildInitialLoginNotice` 路径反推；尚未由客户端抓包确认其业务含义。当前实机验证首先应确认：
该帧确实是 Game 方向的第一帧、客户端收到后开始发送 LOGIN，以及不同监听端口收到各自的
`ch.<频道编号>`。若对外地址不是可用 IPv4 或监听端口没有精确频道映射，当前实现不会伪造
回环地址或默认频道通知。

> **TODO(protocol-capture): Game 初始连接通知**
>
> 需要记录真实客户端收到该帧后的首个请求，确认时间戳、UDP 端口、`1086/1036`、服务编号
> 与两个零字段的真实语义，并比较多个频道的通知内容。本文只声明代码兼容布局。

### Game CMD `0x0001`（LOGIN）

旧实现将客户端 Body 解析为两个连续的 ASCII DString：

| 顺序 | 类型 | 内容 | 状态 |
|---:|---|---|---|
| 1 | `int32 LE length + byte[length]` | `m_id` 登录名 | 代码事实 |
| 2 | `int32 LE length + byte[length]` | 密码 Hash 文本 | 代码事实 |

每个字符串的旧代码上限为 256 字节。旧登录逻辑按 `m_id` 查询账号；不存在时使用收到的
密码 Hash 创建账号，存在时直接复用旧账号，并未在该路径验证新收到的密码 Hash。当前
迁移仅兼容这项旧行为，不把它解释为推荐的认证策略。

基础成功响应使用 `cmd=0x01/type=0x0001`，Body 为：

| Offset | 长度 | 内容 | 状态 |
|---:|---:|---|---|
| 0 | 1 | 成功值 `0x01` | 代码事实 |
| 1 | 1 | 固定值 `18` | 待确认 |
| 2 | 1 | 固定零 | 待确认 |
| 3 | 1 | 频道环境值 | 实现推断 |
| 4 | 1 | 固定零 | 待确认 |
| 5 | 4 | `int32 LE` 登录频道端口 | 实现推断 |
| 9 | 4 + N | ASCII DString 服务端 IP | 实现推断 |
| 13 + N | 4 | `int32 LE` 未知端口，旧默认 `17200` | 待确认 |
| 17 + N | 4 | 再次写入相同未知端口 | 待确认 |
| 21 + N | 24 | 固定零 | 待确认 |

当前阶段在 Game 连接建立时创建匿名 `OnlineUserId` 索引；LOGIN Command 以排他方式查询或
创建账号，并把账号 ID 绑定到同一个在线用户聚合。`OnlineUserId` 只用于登录前连接生命周期；
登录后的业务 Message Handler 必须先解析出 `AccountId`，Command 和领域服务不再传递
`OnlineUserId`。内部索引和 generation 不写入协议帧。
账号设置、拍卖开放、荣誉等级、成长胶囊以及其他旧登录后通知尚未迁移，后续仍应作为各自
的强类型 Message 处理，不能把它们误称为当前基础 LOGIN ACK 的一部分。

> **TODO(protocol-capture): Game CMD `0x0001` (`LOGIN`)**
>
> 需要确认两个 DString 的真实字符集和长度边界、客户端是否接受空密码 Hash、响应常量与
> 两个 `17200` 字段的含义、不同 Game 频道的环境值，以及基础 ACK 后各通知的真实顺序和
> 必需性。本文所有 LOGIN 布局仍来自代码反推，不是抓包结论。

> **TODO(protocol-capture): Game CMD `0x0003` (`EXIT`)**
>
> 旧枚举将它命名为 `EXIT`，旧处理器忽略请求 Body，只返回同类型、Body 为 `0x01` 的
> 成功 ACK。仓库中没有真实客户端抓包，尚不能确认它是否由“退出整个游戏”触发、ACK 后
> 由哪一端关闭连接、请求 Body 是否始终为空，以及服务端是否还应执行退出副作用。
> 在证据补齐前，迁移实现只能保持 ACK 行为；Session 保存和清理必须继续以连接断开
> 生命周期为可靠入口，不得仅依赖该命令。

### Game CMD `0x0008`（GET_USERINFO）

> 请求侧的完整记录见 `protocol/game/client-to-server/0x0008.md`（5 个构包点全部反汇编，
> 布局与 mode 值域已确认）。

请求 Body `{ uint16 LE target, byte mode }`——旧实现的反推，现已由客户端指令**独立确认**。
`mode` 是同一线路 opcode 的协议判别字段，Networking 解码后按 mode 生成不同的强类型
Message，不把 mode 继续传给业务 Handler。

已确认的修正：客户端只存在 `mode=2` 与 `mode=3`，**不存在 `mode=0/1`**；`target` 的
`0xFFFF` 是"无目标"哨兵（客户端把合法目标限制在 `0..0xFFFE`），`mode=2` 固定写该哨兵，
因此该模式下它确实不参与账号定位。`mode=3` 携带运行时目标 id，尚未迁移。

`mode=2` 当前发送 `cmd=0x00/type=0x0002` 的角色列表通知。列表头和单角色基础字段沿用旧
构造顺序，外观、荣誉和未确认尾部字段暂以保守默认值编码。旧实现随后还会发送
`0x0286`、荣誉等级和成长胶囊通知，这些后续通知尚未迁移。

> **TODO(protocol-capture): Game CMD `0x0008` 的响应侧**
>
> 请求侧已确认。仍未确认的是：`mode=2` 的完整通知顺序、角色列表默认字段和外观数据格式
> （即 S→C `0x0002` 的 body 编码，当前仍是旧代码反推）、以及 `mode=3` 的响应身份。

### Game CMD `0x0001`/type `0x00AB`（HARDWARE_REPORT，客户端硬件规格上报）

> 完整记录见 `protocol/game/client-to-server/0x00AB.md`
>（反汇编 + cdb 动态双重验证，含详细工作日志）。

客户端→服务端，`cmd=0x01/type=0x00AB`。登录早期一次性上报（实测在 LOGIN 之后、
GET_USERINFO 之前；触发于进入频道），fire-and-forget，**服务端无需 ACK**，
客户端以本地 `[0x34]` 标志防重发。Body 39 字节，结构闭合：

```
u8  trigger      ; 序列化路径标志：0x80=主路径（push 0 wrapper），0x81=条件路径
u8  reported     ; 上报时的已上报标志（样本为 0，发送后置 1）
u8  count        ; 常量 4
4 × {
    u8  tag      ; 0..3 槽位编号（定长）
    u32 v1
    u32 v2
}
```

| 槽位 | v1 | v2 |
|---|---|---|
| tag=0 CPU | 逻辑处理器数 & 0x80FFFFFF + 厂商桶×0x1000000（0=Intel/1=AMD/2=其他） | 主频桶：0≤1050MHz，1≤1650，2≤2450，3>2450 |
| tag=1 GPU | PCI Vendor ID（u16 扩展） | PCI Device ID（u16 扩展） |
| tag=2 OS | OS 枚举：1=98,3=ME,4=95/NT4,6=2000,7=XP,9=Vista,0xA=7SP1,0xC=8,0xD=8.1,0xE=10/11 | Service Pack 值 |
| tag=3 RAM | 常量 0 | 物理内存桶（ullTotalPhys>>20）：0≤136MB … 6≤2056MB，7>2056MB |

实测样本（Win11/12 线程/16GB/Intel 核显）：`(12,3) (0x8086,0xA7A8) (14,0) (0,7)`。
服务端实现建议：按结构解析落库即可。

### 服务端到客户端

旧发送实现构造 15 字节包头，而不是复用 13 字节入站包头：

| Offset | 长度 | 类型 | 当前解释 | 状态 |
|---:|---:|---|---|---|
| 0 | 1 | `byte` | 不透明 selector；Protocol 解释为 CMD/NOTI | 客户端反编译确认 |
| 1 | 2 | `uint16 LE` | 业务消息类型 | 代码事实 |
| 3 | 4 | `uint32 LE` | 包含 15 字节包头的总帧长度 | 代码事实 |
| 7 | 4 | bytes | 通常为零；补充角色列表 `00/0002` 写 `2`，字段暂称 checksum | 代码事实，语义待确认 |
| 11 | 4 | bytes | 旧实现固定写零，语义未知 | 待确认 |
| 15 | `length - 15` | bytes | Body | 代码事实 |

13/15 字节的方向差异必须保留为两个独立的 codec，不得在抓包确认前合并成一个
对称包头。尤其需要确认出站最后 4 字节是否包含 2 字节序号、两个独立字段，或者确实是
一个 4 字节保留字段。

## 当前重构实现

Networking 当前提供：

- `ChannelFrameRequestInfo`：解析 11 字节 Channel 包头及 Body。
- `GameClientFrameRequestInfo`：解析 13 字节 Game 客户端包头及 Body。
- `ChannelFrameDataHandlingAdapter`：TouchSocket Channel 流适配器。
- `GameClientFrameDataHandlingAdapter`：TouchSocket Game 入站流适配器。
- `ChannelFrameEncoder`：生成观察到的 11 字节 Channel 帧。
- `GameServerFrameEncoder`：生成观察到的 15 字节 Game 服务端帧。
- `GameServerFrameEncoder` 以不透明数值写入 offset 7 checksum 与 offset 11 reserved；
  默认均为零，Protocol 可保留已确认的非零既有值（当前为补充角色列表的 checksum `2`）。
- Game 拆帧、解码注册表和帧编码器只把首字节命名为 `Selector` 并按原值搬运；
  Networking 不定义也不解释 CMD/NOTI。
- `ChannelPayloadCipher`：兼容旧实现的 Channel AES-128-ECB、零填充和 zlib 顺序。
- Channel `0x05/0x09/0x0B/0x11` 到强类型 Message 的解码，以及对应响应编码。
- Protocol 为每个 Game 出站 message 固定且唯一地标记 `IGameCmdMessage` 或
  `IGameNotiMessage`，由对应编码器基类写入 selector；`IGameMessageSender` 的
  `SendCmdAsync` / `SendNotiAsync` 在调用点约束类别，再委托通用 Networking sender。
- Game 连接建立时先登记匿名在线连接，根据精确 Game 监听端口产生强类型 NOTI 初始通知，
  再发送 `00/0001`；发送失败会撤销该在线身份。
- 所有 C→S Game 解码注册通过 Protocol 的 CMD helper 固定为 selector `0x01`，Networking
  仍以 `(selector,type)` 为不透明路由键；不存在客户端 NOTI 注册路径。
- Game `CMD 0x01/type=0x0001` LOGIN DString 解码、强类型 Message、账号绑定以及基础
  成功 ACK 编码；身份变更通过按在线用户分区的排他 Command 执行。
- Game `cmd=0x01/type=0x0003` 到 `GameExitRequest` 的解码，以及 Body 为 `0x01` 的
  `GameExitResponse` ACK 编码；当前实现不因该消息主动关闭连接或清理 Session。
- Game `cmd=0x01/type=0x0008` 在 Networking 按 mode 分流；当前 `mode=2` 解码为独立的
  `GetAccountCharacterRosterRequest`，并通过只携带 `AccountId` 的 Command 查询角色列表。

### `SELECT_CHARACTER` (`CMD 0x0004`) migration status

> This section is reverse-engineered from the legacy server code. It has not
> yet been confirmed by packet capture.

- The request body begins with a little-endian `u16` character slot index.
- The refactored path resolves the exact `AccountId + SlotIndex`; it does not
  preserve the legacy fallback to the first account character or seed data.
- The application command use case returns a composed `SelectCharacterResult` whose
  character, account, world, and session views remain separate. Optional
  character-only state such as Dark Knight combos is nullable only when the
  feature does not apply.
- Protocol 的 `SelectCharacterHandler` 协调响应顺序和条件状态投影；Networking 只执行
  通用传输及显式收件人失败隔离。当前接受与状态同步序列均已迁移，并按消息固有类别编码：
  接受、PremiumService、CloneTitle 为 CMD，其余已实现状态推送为 NOTI。拒绝路径保持
  相邻的 CMD `01/0004` 与 NOTI `00/01F7`，但由两个独立强类型消息表达。

这些类型属于 Networking 的传输协议层。完整帧形成后，还应由消息 decoder 将
`msg_no/type + Body` 转换为共享业务数据类型，再通过 Abstractions 中的分发接口进入业务层。

## 非法数据策略

### 当前已经执行的策略

- `TotalLength < HeaderLength`：拒绝该帧。
- `TotalLength > 1 MiB`：拒绝该帧。
- Body 未完整到达：由每连接独立的 TouchSocket adapter 缓存，不提前投递。
- 一个接收缓冲区包含多个帧：按顺序逐帧投递。
- checksum、classification、cmd、seq：目前只解析和保留，不做语义校验。

1 MiB 是**临时策略**，用于避免恶意长度导致无限缓存或过量内存占用，不是已确认的
协议上限。后续应根据真实最大业务包、客户端行为和部署配置调整，并考虑改为显式配置项。

### 连接处理建议

当 adapter 报告非法总长度时，主机层应记录协议错误并关闭对应连接。当前包头没有已经
确认的 magic/signature，无法可靠地从任意垃圾字节中重新定位下一帧；在没有证据前不建议
实现扫描式重同步。

半帧不是协议错误，但还需要在主机接入阶段配置缓存超时。连接关闭时仍有缓存数据，应记录
为 truncated frame，不得投递到业务层。

## 自动化测试基线

`Server/tests/Dfo.Server.Networking.Tests` 当前覆盖：

- Channel 和 Game 入站字段偏移、Little Endian 解析和 Body 提取；
- Channel 11 字节、Game 出站 15 字节的精确编码结果；
- 在不同接收 buffer 长度下的半包、逐字节输入和粘包；
- 长度为 0、1、小于包头、超过临时上限和 `uint.MaxValue` 的非法帧；
- 一个输入流连续产生多个帧且保持顺序。

测试中的字节样本目前是根据旧代码构造的 executable examples，不应称为真实客户端
golden packets。抓包完成后应增加独立的 `Fixtures/GoldenPackets`，保存脱敏后的真实帧样本，
并让 codec 对这些样本进行精确回归测试。

## 抓包确认清单

后续至少需要用真实客户端确认：

1. Channel 和 Game 的多字节字段端序。
2. 两个长度字段是否始终包含包头。
3. Channel `classification`、`ack` 的有效值范围。
4. checksum 的算法、覆盖范围以及值为零时的含义。
5. Game `cmd` 的有效值范围。
6. Game `seq` 是否单调、按连接重置或参与请求响应关联。
7. Game 服务端出站 15 字节包头最后 4 字节的真实结构。
8. 空 Body 帧是否真实存在。
9. 真实最大帧长度和合理缓存超时。
10. 客户端遇到非法帧、未知 type 或 checksum 错误时的行为。
11. Game CMD `0x0003` 的真实触发场景、请求 Body、ACK 后关闭方和预期副作用。
12. Game CMD `0x0001` 的字符集、响应常量/端口字段，以及登录后通知的必要集合与顺序。

每次确认后，应同时更新本文档、golden packet fixture 和相应自动化测试，并注明证据来源。
