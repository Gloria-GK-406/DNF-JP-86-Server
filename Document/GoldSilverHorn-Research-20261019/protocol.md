# 新·金角银角（DGN 4123）A21 协议静态证据

日期：2026-10-19
范围：只读客户端/PVF调查；本文不记录服务端实现已经通过实机验证。
证据分层：`ContentStatic`、`ClientStatic`、`CurrentImplementation` 分开陈述；目前没有 `WireCaptured` 或 `ClientVerified` 的 4123/042B 样本。

## 结论

- A21 S2C `NOTI 0x042B` 正文精确为 4 字节：`u16 goldUsed`、`u16 silverUsed`，均为 little-endian。
- 当前指定 PVF 只配置 `[silver horn dungeon]`，其中 `[dungeon index] = 4123`、`[max enter count] = 3`。加载后 `4123` 位于管理器第二槽，不在第一槽。
- 因此 DGN 4123 的已用次数必须写入 042B 正文 offset `2` 的第二个 `u16`。正文 offset `0` 是未配置的 gold 槽；当前实现应写 `0`，不得把 4123 计数对称复制到两个字段。
- 客户端按 `max - used` 计算显示剩余次数。服务器发送的是已用次数，不是剩余次数。
- 客户端静态代码只证明通知的接收、存储和消费条件，不能证明服务器原生发送时机。当前可实施时机是选角状态同步发送一次，并在成功提交 4123 入场扣次后刷新一次；这是 `CurrentImplementation` 策略，需通过实帧和 UI 验收提升证据等级。

## 工件身份

| 工件 | 用途 |
|---|---|
| `E:/Project/DNF/DNFClient-A21-protocol2/DNF.unpacked-b1-debug.exe` | A21 静态字符串解密；ImageBase `0x00400000` |
| `E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin` | A21 RVA 指令检查 |
| `artifacts/scheduled-notice-pvf-20261019/Script.server.pvf` | 当前服务端指定 PVF 内容检查 |
| `Tool/PvfProbe/bin/Release/net10.0/PvfProbe.exe` | 只读 PVF dump |

字符串使用仓库既有 `Document/abyss-research-20261007/evidence/client-scan.py` 所记录的 A21 literal 解密算法恢复：长度来自头部 byte1/word2，反馈 XOR 初始 key 为 `seed | 0x9A714CA0`。没有运行或修改客户端。

## PVF 内容

复现命令：

```powershell
$env:PVF_ARCHIVE_PATH = 'E:\Project\DNF\DNF-JP-86-Server\artifacts\scheduled-notice-pvf-20261019\Script.server.pvf'
& 'E:\Project\DNF\DNF-JP-86-Server\Tool\PvfProbe\bin\Release\net10.0\PvfProbe.exe' `
  dump 'etc/chn_goldsilverhorndungeon.etc' 400
```

输出：

```text
[world map index]
14
[/world map index]
[silver horn dungeon]
[dungeon index]
4123
[max enter count]
3
[silver horn dungeon]
```

该文件没有 `[gold horn dungeon]` 段。这是 `ContentStatic / Confirmed`。

## ETC loader 与双槽映射

### 解密后的实际 key

下列均为 A21 虚拟地址；RVA 等于 VA 减 `0x00400000`：

| VA | 解密值 |
|---:|---|
| `0x0311E058` | `Etc/chn_GoldSilverHornDungeon.etc` |
| `0x0362C908` | `[world map index]` |
| `0x0362C8D8` | `[gold horn dungeon]` |
| `0x0362C844` | `[/gold horn dungeon]` |
| `0x0362C810` | `[silver horn dungeon]` |
| `0x032CA288` | `[dungeon index]` |
| `0x0362C8AC` | `[max enter count]` |
| `0x0362C878` | `[dungeon enter cost]` |

### loader 写入位置

loader RVA `0x02512C60` 的实际分支如下：

| 配置段 | 匹配/写入指令 | 管理器字段 |
|---|---|---|
| world map | `0x02512D15` 匹配 key；`0x02512D60` 写值 | `+0x00 : u16 worldMapIndex` |
| gold | `0x02512D68` 匹配 `[gold horn dungeon]` | 第一槽 |
| gold dungeon index | `0x02512DC0` 匹配 key；`0x02512E0B` 写值 | `+0x04 : u32 dungeonId` |
| gold max enter | `0x02512E0E` 匹配 key；`0x02512E59` 写值 | `+0x08 : u16 maxEnterCount` |
| gold enter cost | `0x02512E62` 匹配 key；`0x02512ED5` 写容器 | `+0x0C` |
| silver | `0x02512F51` 匹配 `[silver horn dungeon]` | 第二槽 |
| silver dungeon index | `0x02512FA0` 匹配 key；`0x02512FEB` 写值 | `+0x20 : u32 dungeonId` |
| silver max enter | `0x02512FEE` 匹配 key；`0x02513039` 写值 | `+0x24 : u16 maxEnterCount` |
| silver enter cost | `0x02513042` 匹配 key；`0x025130B4` 写容器 | `+0x28` |

构造器 RVA `0x02512BD0` 将 `+0x04/+0x08` 与 `+0x20/+0x24` 初始化为零。当前 PVF 仅进入 silver 分支，因此加载结果是：

```text
manager + 0x04 = 0       # gold dungeon 未配置
manager + 0x08 = 0       # gold max 未配置
manager + 0x20 = 4123    # silver dungeon
manager + 0x24 = 3       # silver max enter count
```

这闭合了 4123 与第二槽的关系，等级为 `ClientStatic / Confirmed` 加 `ContentStatic / Confirmed`。

## NOTI 0x042B 布局

### 注册与读取

- RVA `0x00571813..0x00571825`：注册 S2C type `0x042B`，handler VA `0x00971730`，即 RVA `0x00571730`。
- RVA `0x00571736..0x00571745`：调用读取原语，长度立即数为 `4`。
- RVA `0x0057174C`：读取 body `+0` 的 `u16`。
- RVA `0x00571791`：存入全局 VA `0x039A94A4`。
- RVA `0x00571798`：读取 body `+2` 的 `u16`。
- RVA `0x005717D5`：存入全局 VA `0x039A94A8`。

精确布局：

| Offset | 长度 | 类型 | 语义 | 证据 |
|---:|---:|---|---|---|
| `0` | 2 | `u16 LE` | gold 槽已用次数 | `ClientStatic / Confirmed`（槽位）；`High`（“已用次数”语义） |
| `2` | 2 | `u16 LE` | silver 槽已用次数；当前即 DGN 4123 | `ClientStatic / Confirmed`（槽位）；`High`（“已用次数”语义） |

### “已用次数”而非“剩余次数”

RVA `0x0250FE90` 按 dungeonId 返回对应最大次数：

- 参数等于 manager `+0x04` 时返回 `+0x08`；
- 参数等于 manager `+0x20` 时返回 `+0x24`。

042B handler：

- RVA `0x00571753..0x00571773`：第一槽执行 `goldMax - bodyU16At0`；
- RVA `0x0057179C..0x005717BC`：第二槽执行 `silverMax - bodyU16At2`；
- 结果随后传给 UI 更新函数 RVA `0x01470AD0`。

因此正文值是当前/已用计数，剩余次数由客户端计算。由于尚无线级样本，语义保持 `ClientStatic / High`，不标为 `WireCaptured`。

## 4123 入口实际选择条件

入口 UI 消费点 RVA `0x0222493C..0x022249AC`：

1. `0x0222493E..0x02224949` 读取 manager `+0x04` 的 gold dungeonId；
2. `0x02224951..0x0222495C` 读取 manager `+0x20` 的 silver dungeonId；
3. `0x0222495D..0x0222496C` 将当前入口对象 `+0x34` 依次与两者比较；
4. 命中 gold 时，`0x0222498A` 调用 RVA `0x005716E0`，读取全局 `0x039A94A4`；
5. 命中 silver 时，`0x02224997` 调用 RVA `0x005716F0`，读取全局 `0x039A94A8`；
6. 两者均不命中则离开专属逻辑。

由于当前入口对象的 dungeonId 为 4123，而 manager `+0x20 = 4123`，它必然命中 silver 分支并消费第二个 `u16`。

## 可实施编码与发送时机

### 编码

```text
offset 0: u16 goldUsed   = 0
offset 2: u16 silverUsed = authorityUsedCountForDungeon4123
```

`silverUsed` 合法范围按本服业务规则为 `0..3`。不要把剩余次数 `3-used` 放在线上，也不要把 `silverUsed` 复制到 `goldUsed`。

### 发送时机的证据边界

A21 客户端只提供 S2C handler，没有从上述静态路径识别出金银角专属 C2S 查询。客户端同号 `CMD 0x042B` 在身份表中是另一条消息 `SENSE_OF_CHOICE`，不能据同号建立请求/响应关系。

现阶段采用以下 `CurrentImplementation` 时机：

1. **选角成功的状态同步阶段**：读取权威周期计数，发送一次 042B，使首次打开 4123 入口前已有正确值；
2. **4123 入场扣次事务成功提交后**：发送更新后的 042B；事务失败或回滚不得发送递增值；
3. 重连/再次选角仍以持久化权威计数重发，不依赖会话内缓存。

这些时机满足客户端消费前置条件和权威状态恢复，但不是原生日服发送顺序的证明。最终验收应记录：选角后的实帧顺序、第一次至第三次入场后的第二个 `u16` 递增，以及第四次被拒绝时计数不再变化。

## 最小实机验收清单

- 选角后捕获 S2C `0x042B`，正文长度恰为 4。
- 新周期未进入时正文为 `00 00 00 00`。
- 第一次成功入场提交后正文为 `00 00 01 00`；第二、三次依次为 `00 00 02 00`、`00 00 03 00`。
- 第四次拒绝后不得出现 `00 00 04 00`，持久化计数仍为 3。
- 入口 UI 的剩余次数依次显示 3、2、1、0；该项才可把静态语义提升为 `ClientVerified`。
- 重连/重新选角后第二个 `u16` 保持权威值；跨本服统一日切后恢复为 0。
- 任一事务失败/回滚场景不得提前广播递增值。
