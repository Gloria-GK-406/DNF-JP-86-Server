# A21 婚房、家具与夫妻树专项静态调查

日期：2026-10-18。范围：A21 客户端静态协议、当前服务端接缝与最小实机补证方案。本轮只读调查，不实施、不部署，不修改数据库、PVF、客户端或协议目录。

## 结论

**当前证据足以进入婚房子系统的预设计，尚不足以直接实施。第一个必须闭合的缺口是“玩家如何进入婚房”。**

PVF 列表确认 `town 101 -> CoupleRoom.twn`，而 A21 CMD 注册表把 C2S `0x03FC` 标为 `MOVE_TO_PARTNER`。两项证据各自成立，但没有 `WireCaptured` 或 `ClientVerified` 证据证明：点击原生婚房入口一定发送 `0x03FC`、`0x03FC` 一定把角色送到 town 101，或婚房沿普通换城协议进入。因此当前只能保留两个候选：

1. 普通城镇切换进入 `town 101`；
2. `0x03FC MOVE_TO_PARTNER` 触发专用移动，再由服务端或客户端落到婚房场景。

这两个候选不能在服务端设计里预先合并。第一次定向实机应只做原生 UI 入口与过渡抓包；入口闭合后，再确定准入、位置恢复和重登语义。

住房内部静态协议已经形成相对完整的功能簇：房间快照、夫妻仓库、家具移动/删除、夫妻树、戒指升级与爱心点更新都有 A21 handler 或 builder。字段宽度较多已闭合，**业务语义、状态归属、并发规则和持久化粒度仍是 `Unknown`**。当前不能把“客户端有本地窗口检查”升级成“服务器应允许/拒绝某项操作”的规则。

## 1. 证据身份与基线

本文严格区分以下证据：

| 标签 | 含义 |
|---|---|
| `ContentStatic` | PVF/内容表静态存在；不证明玩家可达或服务器执行 |
| `ClientStatic` | A21 客户端指令直接支持的收发形状或 UI 分支 |
| `CurrentImplementation` | 当前仓库代码行为；不证明 A21 原服行为 |
| `HistoricalCNReference` | 同时期或相近国服资料，仅提供业务候选，不等同日服 A21 |
| `WireCaptured` | 完整标注来源环境的线上原始字节；本轮没有新增原服/参考服样本 |
| `ProbeGenerated` | 本服隔离协议探针生成并由客户端接收的包；只证明探针和客户端反应，不证明原服行为 |
| `ClientVerified` | 真实客户端可见闭环；本轮没有新增 |
| `Unknown` | 当前证据不能裁定 |

本轮 A21 静态主工件为固定 EXE `DNF.unpacked-b1-debug.exe`，SHA-256：

```text
CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14
ImageBase = 0x00400000
```

逐指令输出见 [housing-a21-disassembly.txt](outputs/marriage-housing-a21-20261018/housing-a21-disassembly.txt)。该输出只证明 `ClientStatic`；没有因函数名、upstream 枚举名或静态 UI 分支新增实帧/实机结论。

仓库协议页来自另一已索引客户端 SHA `1F8244...B00DD`，可用作交叉校对，但不能替代当前固定 EXE 的重新闭合。尤其 [0x03FC 页面](../protocol/game/client-to-server/0x03FC.md) 的 H1 标题仍写成旧的 `AIM_FOR_THE_LEGENDARY`，机器身份区和 [protocol-inventory.tsv](../protocol/patterns/data/protocol-inventory.tsv) 才把 C2S `0x03FC` 登记为 `MOVE_TO_PARTNER`。本文采用机器身份，同时把旧标题视为待修正文档债务。

## 2. 婚房入口：已知与未知

### 已知

- **[ContentStatic / Confirmed]** [PVF town 列表](../protocol/patterns/data/probes/pvf-town-list-20260817.md) 第 35 行存在 `101 -> CoupleRoom.twn 婚房`。
- **[ClientStatic / Confirmed，旧索引 SHA]** C2S `0x03FC` 的注册身份是 `MOVE_TO_PARTNER`，构包点 RVA `0x0089443F`，已有页面记录 34 字节不透明正文。
- **[ClientStatic / Confirmed]** 多个婚房 handler 在客户端当前场景 ID 等于 `0x65`（十进制 101）时刷新房间/树 UI：NOTI `0x03DB` 路径见静态输出第 4092–4101 行，NOTI `0x03DC` 路径见第 5128–5143 行。这把婚房 UI 与 town 101 联系起来，但没有说明进入命令。

### 未知

- 原生 UI 的入口位置、菜单动作和前置条件；
- 入口走普通换城协议、`0x03FC`，还是两者组合；
- `0x03FC` 34 字节中 20 字节候选业务尾的字段边界与对象身份；
- 进入时由哪一方决定 spawn、朝向、返回点和断线恢复；
- 配偶离线、未婚、婚礼未完成、双方不同频道时的拒绝码；
- 主人房与访客房是否存在不同权限或不同快照。

因此，`town 101` 和 `0x03FC` 目前是**两项相互支持但尚未闭环的证据**，不能写成已确认入口。

## 3. 候选玩家流程

以下流程是由静态协议簇组合出的 **Hypothesis**，用于指导抓包，不是 A21 已验证时序：

```text
原生婚房入口
  -> [Unknown: 普通换城 / 03FC MOVE_TO_PARTNER / 二者组合]
  -> S2C 03D8 COUPLE_ROOM 房间快照
  -> S2C 03DC COUPLE_TREE 树快照

打开/关闭夫妻仓库
  -> C2S 03FD COUPLE_INVEN_OPEN_CLOSE
  -> CMD 03FD
  -> S2C 03D9 COUPLE_INVEN 仓库快照

移动家具
  -> C2S 03FE COUPLE_ROOM_MOVE_ITEM
  -> CMD 03FE
  -> S2C 03DA / 03DB 房间增量或刷新

删除夫妻仓库物品
  -> C2S 03FF COUPLE_INVEN_DELETE_ITEM
  -> CMD 03FF
  -> [Unknown: 03D9 / 03DA / 03DB 中哪条刷新]

夫妻树
  -> C2S 0400 种植 / 0401 浇水 / 0402 收获 / 0403 移除
  -> 同 type CMD 状态
  -> S2C 03DC 树快照
  -> 收获时可能另有 S2C 03DE 奖励列表

戒指与爱心点
  -> C2S 03F4 戒指升级 -> CMD 03F4 -> S2C 03D1 等级更新
  -> 既有 C2S 03F5 消耗心意通 -> S2C 03D3 角色爱心点更新
```

静态路径只证明这些消息适合组成上述流程，不证明每次操作的精确发送顺序、是否广播给配偶、失败时是否仍推快照，或重登时是否重放全部状态。

## 4. A21 协议结构表

### 4.1 长度口径

下表“客户端当前接收/构包总长”按相关 serializer/decoder 实际处理的连续字节计。若 C2S 构包器复制的对象包含 14 字节不透明前缀，则同时列出“业务尾”，避免把 14 字节再次误减成外层网络帧头。这个 14 字节区的业务身份仍未知；它与 Game 外层帧头不是同一项已证事实。

`Confirmed` 只表示宽度和控制流由指令直接支持；字段名称仍可能是 `Unknown`。

### 4.2 房间、仓库与家具

| 协议 | 方向 | 当前总长 | 不透明前缀 / 业务尾 | 指令可证布局 | 证据状态 |
|---|---|---:|---|---|---|
| `03FC MOVE_TO_PARTNER` | C2S | 34B | 候选 `14B + 20B` | 旧协议页只能闭合 34B 整块，字段未知 | `ClientStatic / High`；旧索引 SHA，当前固定 EXE 未重闭合 |
| `03D8 COUPLE_ROOM` | S2C NOTI | `17 + 6n` | 无 | `u32 count + n×(u16 + u32) + u32 + u32 + u32 + u8` | `ClientStatic / Confirmed`；[第 3289–3356 行](outputs/marriage-housing-a21-20261018/housing-a21-disassembly.txt#L3289-L3356) |
| `03D9 COUPLE_INVEN` | S2C NOTI | `8 + 10n` | 无 | `u32 count + n×bytes[10] + u32 tail`；10B 内至少有 `u16` 与 `u32` 读取，但完整语义未知 | `ClientStatic / Confirmed`；第 3600–3612、3720–3735 行 |
| `03DA COUPLE_ROOM_UPDATE` | S2C NOTI | 23B | 无 | `bytes[23]`；首字节控制更新分支，后续含多个 u8/u16/u32 值 | `ClientStatic / Confirmed`；第 3908–3975 行 |
| `03DB COUPLE_ROOM_REFRESH` | S2C NOTI | `4 + 11n` | 无 | `u32 count + n×(u8 tag + u32 + u16 + u32)` | `ClientStatic / Confirmed`；第 4055–4111 行 |
| `03FD COUPLE_INVEN_OPEN_CLOSE` | C2S | 15B | `14B + u8 state` | 两条 UI 路径发送相反的 0/1；真实开/关映射待实机 | `ClientStatic / Confirmed`；第 1667–1720、1832–1876 行 |
| `CMD 03FD` | S2C CMD | 5B | 无 | `i32 status + u8 state`，仅在 parser gate 成立时消费 | `ClientStatic / Confirmed`；第 1981–2001 行 |
| `03FE COUPLE_ROOM_MOVE_ITEM` | C2S | 28B | `14B + 14B` | 尾部为两组 `u8 containerKind + u16 slot + u32 value`；两条路径交换 source/destination kind | `ClientStatic / Confirmed`；第 2305–2368、2525–2546、2674–2696 行 |
| `CMD 03FE` | S2C CMD | 4B | 无 | `i32 status` | `ClientStatic / Confirmed`；第 2814–2826 行 |
| `03FF COUPLE_INVEN_DELETE_ITEM` | C2S | 24B | `14B + 10B` | 业务尾候选 `u16 slot + u32 valueA + u32 valueB`；三个值的身份未知 | `ClientStatic / High`；第 2997–3032 行 |
| `CMD 03FF` | S2C CMD | Unknown | 无 | 当前窗口未闭合 decoder 读值；不可从 handler 存在推断长度 | `Unknown` |

`03FE` 的 `value` 可能是物品 ID、序列号、格内实例标识或其他索引，静态指令不足以命名。它也不能证明非家具物品会被服务器拒绝。必须以“一件已知家具 + 一件普通物品”的差分实帧和 UI 结果闭合。

### 4.3 戒指、爱心点与夫妻摘要

| 协议 | 方向 | 当前总长 | 布局 | 证据状态 |
|---|---|---:|---|---|
| `03F4 WEDDING_UPGRADE_RING` | C2S | 19B | `14B opaque + u32 candidate + u8 targetLevel`；候选值的身份未知 | `ClientStatic / High`；第 163–182 行 |
| `CMD 03F4` | S2C CMD | 5B | `i32 status + u8 ringLevel`；成功调用与 `03D1` 相同的刷新函数 | `ClientStatic / Confirmed`；第 394–418 行 |
| `03D1 WEDDING_UPGRADE_RING` | S2C NOTI | 1B | `u8 ringLevel` | `ClientStatic / Confirmed`；第 698–706 行 |
| `03D3 CHARAC_LOVE_POINT` | S2C NOTI | 8B | `u32 characterId + u32 points`；仅当对象解析为本地角色时写入本地 manager | `ClientStatic / Confirmed`；实际函数入口 RVA `0x00892B80`，第 720–770 行 |
| `03DD COUPLE_SIMPLE_INFO` | S2C NOTI | 14B | `u32 + u32 + u8 + u8 + u32`；业务字段名未闭合 | `ClientStatic / Confirmed`；第 5171–5188 行 |

逐指令输出把 RVA `0x00892B80` 接在 `NOTI 03D1` 的较宽反汇编窗口内；它实际是独立的 `03D3` 函数。输出中后面的 `NOTI 03D3` 标题从 RVA `0x00893550` 开始，不是本文所用的 `03D3` decoder 入口，不能据该标题替换上述定位。

当前服务端已用 `03F5` 处理心意通并编码 `03D3`，且婚姻关系聚合中保存爱心点；这是 `CurrentImplementation`。`03D3` 静态 handler 只对匹配本地角色的值落 UI 状态，**不能单独证明爱心点在原服是个人持久值还是夫妻共享值**。当前服务端的关系级存储属于已实现设计，不应倒推为 A21 原服证据。

### 4.4 夫妻树

| 协议 | 方向 | 当前总长 | 不透明前缀 / 业务尾 | 指令可证布局 | 证据状态 |
|---|---|---:|---|---|---|
| `0400 COUPLE_TREE_PLANT` | C2S | Unknown | Unknown | 旧索引页记录 20B；当前固定 EXE 输出在该 builder 标题后为空，无法独立复核 | `Unknown`；不得按 20B 直接实施 |
| `CMD 0400` | S2C CMD | 4B candidate | 无 | parser gate 成立时读取 `i32 status` | `ClientStatic / High`；第 4278–4307 行 |
| `0401 COUPLE_TREE_WATER` | C2S | 20B | `14B + 6B` | `u16 candidate + u32 amount/value` | `ClientStatic / Confirmed`；第 4426–4462 行 |
| `CMD 0401` | S2C CMD | 4B | 无 | `i32 status` | `ClientStatic / Confirmed`；第 4552–4601 行 |
| `0402 COUPLE_TREE_HARVEST` | C2S | 0B | 无业务尾 | `MakeHeader` 后直接 `Finalize`，未追加字段 | `ClientStatic / Confirmed`；第 4684–4696 行 |
| `CMD 0402` | S2C CMD | 4B | 无 | `i32 status` | `ClientStatic / Confirmed`；第 4768–4806 行 |
| `0403 COUPLE_TREE_REMOVE` | C2S | 0B candidate | 无业务尾 | 当前固定 EXE 为 `MakeHeader` 后尾调用 `Finalize`；旧协议页因异常收尾仍将整体结构留为 Unknown | `ClientStatic / High`；第 4898–4903 行，需实帧定案 |
| `CMD 0403` | S2C CMD | 4B | 无 | `i32 status` | `ClientStatic / Confirmed`；第 5016–5056 行 |
| `03DC COUPLE_TREE` | S2C NOTI | 13B | 无 | `u32 + u32 + u32 + u8`；与 `03D8` 尾部写入同一组 manager 字段 | `ClientStatic / Confirmed`；第 5111–5143 行 |
| `03DE COUPLE_TREE_HARVEST_REWARD` | S2C NOTI | `2 + 8n` | 无 | `u16 count + n×(u32 + u32)` | `ClientStatic / Confirmed`；第 5529–5556 行 |

`03DC` 与 `03D8` 共享四个目标字段，是“03D8 尾部包含树状态”的强候选，但三个 u32 和一个 u8 的具体名称、单位、时间基准仍是 `Unknown`。不得先命名为成长值、浇水次数或成熟时间并固化到数据库。

## 5. 并发、共享与物品资格边界

客户端 `03FD` builder 在发送前检查本地窗口/状态，并对打开与关闭发送不同标志。它只证明客户端避免自身重复打开或在错误 UI 状态发送，**不证明服务器的跨配偶互斥规则**。两个客户端同时打开夫妻仓库时，是否只允许一人、后到者收到什么状态、先到者断线后锁多久，都需要双端实机。

此前找到的 2014 国服资料转引提到“爱心点个人积累、家具共享、夫妻仓库一次只允许一人操作”。本文把它标为 `HistoricalCNReference`：

- 可作为设计和实机用例来源；
- 不能证明日服 A21 的持久化粒度、字段布局或错误码；
- 在没有双端 `WireCaptured` 前，不能据此实现永久锁、租约时长或共享所有权。

同理，客户端能构造 `03FE`/`03FF` 不证明所有普通物品都可进入夫妻仓库，也不证明只有 PVF 家具类型可以移动。物品资格、绑定/期限物品、堆叠、过期、删除和返还规则全部保持 `Unknown`。

## 6. 当前服务端差距

### 已有

- **[CurrentImplementation]** 求婚、答复、婚礼、关系唯一性、婚姻持久化和心意通/爱心点已有领域与协议实现；当前范围见 [protocol/catalog.md](../protocol/catalog.md) 的 2026-10-16 婚姻段。
- **[CurrentImplementation]** 选角固定发送 `CoupleRoomEmptyCompatibilityMessage`：`0x03D8` 的 204 个零，见 [StateSynchronizationMessages.cs](../Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs#L410-L414) 和 [SelectedCharacterStateMessageFactory.cs](../Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs#L111)。
- **[CurrentImplementation]** 测试明确保证无论旧彩票进度有无数据都只发 204 个零，见 [SelectCharacterPayloadCodecTests.cs](../Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs#L211-L242)。这只是空婚房兼容，不是非空婚房 encoder。

### 缺失

当前源码查找未见 `03FC/03FD/03FE/03FF/0400..0403` 的住房 decoder/handler/encoder，也未见婚房、夫妻仓库、家具实例、夫妻树或住房会话锁的领域模型。现有 204 零 `03D8` 不能表达：

- 非空房间家具清单；
- 夫妻仓库内容与并发占用；
- 树状态与收获奖励；
- 主人/访客身份、权限和重登恢复。

这意味着当前实现差距不是“补几个 opcode”。至少需要入口准入、住房聚合、家具/仓库事务、树状态、双方广播与重放投影；其持久化边界必须在字段和实机规则闭合后设计。

## 7. 仍需闭合的 Unknown

1. **入口**：town 101 与 `03FC` 的真实关系、失败码和返回路径。
2. **03FC**：34B 字段边界、配偶 ID 类型、频道/位置参数。
3. **03D8**：每个 `(u16,u32)` 条目的业务身份；13B 尾的四字段含义。
4. **03D9**：10B 条目布局、尾部 u32、空仓库形状与最大条目数。
5. **03DA/03DB**：tag/分支与新增、移动、删除、广播动作的映射。
6. **03FD**：0/1 到打开/关闭的准确映射；双客户端互斥与断线释放。
7. **03FE/03FF**：slot/value 的身份、物品资格、数量/实例/所有权语义。
8. **03F4/03D1**：戒指当前级、目标级、费用、双方同步与失败码。
9. **03D3**：A21 原服的个人/共享持久化粒度；当前本服关系级设计不算原服证据。
10. **0400..0403/03DC/03DE**：种子/水/成长/时间/收获字段，奖励事务和重登恢复。
11. **共享权限**：主人、配偶、访客能否编辑或只读；非夫妻拒绝。
12. **容量与生命周期**：家具数量、仓库容量、堆叠、期限物品、离婚/关系解除后的资产处理。

## 8. 最小定向实机方案

本轮报告不改服务端行为。当前服务端尚无婚房、夫妻仓库、家具和夫妻树玩法，所以下表不是一组能直接在当前服连续完成的测试：

- 第 1 步可先在当前服从原生 UI 触发入口，即使服务器拒绝或没有成功回包，也能采集客户端实际发出的 C2S 与调用栈；
- 第 2–9 步涉及成功 S2C、非空房间、家具资产、树成长或收获，前置是存在可用的参考环境；如果没有参考环境，只能继续做定向静态补证，或在规格明确后使用隔离、可回滚的最小协议探针逐项驱动客户端；
- 参考环境采到的原始帧按其真实来源标记。由本服最小实现或协议探针生成的包只能算 `CurrentImplementation` / `ProbeGenerated`，不能标成原服 `WireCaptured`，也不能用来证明 A21 原服字段语义。

执行时保存双方 FrameCapture、出站调用栈和必要的 write trace；每个样本记录环境身份、角色、婚姻状态、当前 town/频道、操作前后 UI 与物品槽。

| 次序 | 操作 | 必须回答的问题 | 需要保留的证据 |
|---:|---|---|---|
| 1 | 从原生 UI 第一次进入婚房 | 普通换城还是 `03FC`；若有参考环境，再确认是否最终到 town 101 及完整回包/NOTI 顺序 | 当前服至少保存真实 C2S、调用栈和入口截图；参考环境另保存完整双端帧及前后 town/坐标 |
| 2 | 空房与至少一件家具的房间分别进入 | `03D8` 的 n、条目与 13B 尾差分；是否跟随 `03DC` | 原始 hex、decoder 消费长度、家具清单 |
| 3 | 空/非空夫妻仓库打开和关闭 | `03FD` 0/1 映射；`03D9` 10B 条目与尾值 | 双方帧序、槽位与物品实例对照 |
| 4 | 双客户端同时打开仓库，再让占用方正常关闭和断线 | 是否跨配偶互斥；错误码；锁释放条件 | 两端时间对齐日志、CMD 状态、UI 文案 |
| 5 | 移动一件确认家具，再尝试一件普通物品 | `03FE` 两个 7B endpoint 的 source/destination/value；资格拒绝 | 操作前后槽位、`03FE`/CMD/`03DA`/`03DB` |
| 6 | 删除或移除一件可恢复的测试家具 | `03FF` 10B 尾和资产去向 | 原始帧、库存/仓库/房间前后对照 |
| 7 | 戒指升级一次 | `03F4` 候选 u32、目标级、CMD 错误与双方 `03D1` | 双方帧和升级前后等级/材料 |
| 8 | 分别执行种植、浇水、收获、移除 | `0400..0403` 正文、CMD 状态、`03DC` 四字段和 `03DE` 奖励 | 每步单独样本、操作前后树状态与奖励 |
| 9 | 正常退出、重登，配偶离线后重登 | 哪些状态随选角/进房恢复；是否只发 204 零 | 重登完整帧序与持久状态对照 |

优先级不能倒置：**先闭合入口，再闭合空/非空快照，再做写操作。** 若入口仍未知，后续抓到的消息无法可靠区分选角重放、进房初始化和 UI 主动请求。

## 9. 实施与验收的迭代边界

未知字段不能先命名并固化成 A21 原服事实，但这不要求等待完整参考环境闭环后才开始实现。已经闭合的 identity、长度、方向、状态消费和当前婚姻聚合边界，可以在规格明确后用于受控的最小实现；尚未闭合的业务字段应保持中性命名、版本化投影或可替换接缝，避免数据库结构把猜测变成永久兼容负担。

最小实现应带隔离配置、可回滚存储迁移和明确的 `CurrentImplementation` 身份，用它逐步驱动客户端 UI、暴露下一项差分，再用参考环境或新增实帧校准。由本服生成的成功包只验证“当前实现能驱动客户端”，不自动升级为原服 wire 证据。本轮仍只交付调查报告，不实施这些改动。

建议按以下独立事务边界迭代推进：

1. 入口与只读房间快照；
2. 夫妻仓库打开/关闭与租约语义；
3. 家具移动/删除的资产事务与双方广播；
4. 夫妻树状态机与收获奖励；
5. 戒指升级和住房相关重放。

每一阶段分别保留 `ClientStatic`、`WireCaptured`、`CurrentImplementation` 和 `ClientVerified` 状态，避免自动化测试通过后把住房流程误记为真实客户端已验收。

## 10. 相关材料

- [A21 逐指令输出](outputs/marriage-housing-a21-20261018/housing-a21-disassembly.txt)
- [A21 xref 扫描输出](outputs/marriage-housing-a21-20261018/housing-a21-xrefs.txt)
- [一期婚姻调查](MarriagePhase1Research-20261016.md)
- [协议目录](../protocol/catalog.md)
- [协议 machine identity 表](../protocol/patterns/data/protocol-inventory.tsv)
- [town 列表 probe](../protocol/patterns/data/probes/pvf-town-list-20260817.md)

xref 扫描输出目前只有工件身份头、没有命中记录；本报告不据此推断任何函数或能力不存在。可引用的指令结论来自定向反汇编输出。
