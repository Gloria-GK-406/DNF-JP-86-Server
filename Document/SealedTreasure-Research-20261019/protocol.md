# 封印的藏宝地（DGN 3557）协议与准入研究快照

日期：2026-10-19
范围：A21 客户端静态证据、A21 PVF、70 级服务端通用参考、当前服务端实现
目标：为 GitHub #154 的可实现方案划清证据边界；本报告不修改服务端、客户端、PVF、数据库或运行环境。

## 结论

封印的藏宝地可以沿用现有普通副本入场和房间循环，不应先发明专用 opcode。当前最小可行实现仍有两个必须补齐的领域能力：

1. 准入层要把 `[required item]` 与 `[event required item]` 建模为两种候选门票。当前解析器只读取前者，导致每日补到 1 个的活动票 `10003235` 无法用于 DGN 3557。
2. 一局内需要保存四个元素状态。PVF 明确写出“元素精灵死亡写参数，四个封印石用相同参数检查后销毁”的跨房间链；当前运行聚合只有房间内物件队列和 Elevator 机关，没有这类 run-scoped 状态。

现有证据没有闭合 A21 的双票选择优先级、缺票拒绝码、`PARTY SYMBOL` 的服务端同步包，也没有 DGN 3557 的真实网络会话或 UI 验收。因此：

- 可以把 70 服务端的“活动票优先，失败后回退普通票，只消费被选中的票”作为实现参考；它仍是 `70Reference`，不是 A21 原服事实。
- 可以把 `NOTI 0x033F(category=0, state=1/2, contentId=3557, opaque=0)`列为开放入口显示的高置信候选；`schedule 2331` 保留为 PVF 配置身份，不写入 `contentId`。
- 四元素状态应先在服务端运行聚合中建模，再用既有 `START_MAP`、`DIE_MONSTER` 与房间物件投影承载；只有抓包证明客户端还需要独立同步时，才新增协议。
- 本轮没有新增 `WireCaptured` 或 `ClientVerified` 证据。

## 证据等级

| 标签 | 本报告中的含义 |
|---|---|
| `70Reference` | 70 级有符号服务端或 70 PVF 的行为，仅供语义参考 |
| `ContentStatic` | A21 PVF、地图、ACT 与物件脚本的静态内容证据 |
| `ClientStatic` | A21 EXE / 内存镜像中的客户端指令、描述符与 handler 证据 |
| `WireCaptured` | DGN 3557 真实会话中观察到的线级帧 |
| `ClientVerified` | 原生 A21 客户端完成了相应可见行为的实机验收 |
| `CurrentImplementation` | 当前仓库代码和当前服务端 PVF 的行为 |
| `Unknown` | 现有证据不能确定 |

置信度使用 `Confirmed`、`High`、`Hypothesis`。`ContentStatic / Confirmed` 或 `ClientStatic / Confirmed` 只表示内容资源或客户端指令本身明确，不自动证明网络权威、原服策略或实机可见结果。

## 输入身份

| 输入 | 身份 |
|---|---|
| A21 RVA 内存镜像 | `DNF-memory-rva.bin`，大小 `122085376`，SHA-256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，ImageBase `0x00400000` |
| 对应原始 A21 EXE | SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` |
| 70 服务端 ELF | `df_game_r`，大小 `36360954`，SHA-256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7` |
| 当前服务端 PVF | `artifacts/scheduled-notice-pvf-20261019/Script.server.pvf`，SHA-256 `C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06` |
| 冻结内容清单 | [content/inventory.json](content/inventory.json)，25 张地图、47 个被引用怪物、34 个被引用被动物件 |

## A21 内容与入口门槛

### 地下城定义

`ContentStatic / Confirmed`：

| 字段 | A21 PVF 值 |
|---|---|
| 路径 | `dungeon/Event/chn_Species_Dungeon/chn_Species_dungeon.dgn` |
| 名称 | 封印的藏宝地 |
| 专用标记 | `[spicies dungeon]`（PVF 原拼写） |
| 最低 / 基准 / 推荐等级 | `70 / 73 / 70..86` |
| 普通门票 | `[required item] 10003196 1 1` |
| 活动门票 | `[event required item] 10003235 1 1` |
| 固定难度 | `[designate dungeon difficulty] 2` |
| 队伍限制 | `[limit party count] 1` |
| 练习模式 | `[prohibit practice] 1` |
| 地图 | `5 × 5`，25 张地图 `25313..25337` |
| 起点 / Boss | `(0,0)` / `(4,4)` |
| 中心房 | `(2,2)`，map `25325` |
| 路径门物件 | `621..630` |
| 调度 | `[schedule] 2331`；`etc/chn_schedule.etc` 的地下城记录包含 `3557` |
| 特殊物件掉落原始元组 | `[special passive object item] 0 -1 1 10003717 3500 1 -1 0` |

最后一行只记录原始值。`3500` 的量纲、概率或其他含义尚未闭合，不能写成“3500 个奖励”。完整 map、monster、passive object 和引用路径保存在 [content/inventory.json](content/inventory.json)。

### 两种导航人偶

`ContentStatic / Confirmed`：

| item | 名称 | 栈上限 | 静态来源 |
|---:|---|---:|---|
| `10003196` | 导航人偶 | 2 | DGN 的 `[required item]`；`etc/cerashop.etc` 与 event-character 商城表存在引用 |
| `10003235` | 导航人偶 | 1 | DGN 的 `[event required item]`；`etc/chn_server_limititemusageinfo.etc` 存在 `[refill item]` 记录 |

活动票的 refill 原始四元组是：

```text
10003235  1  "2030-01-01 06:00:00"  0
```

`CurrentImplementation / Confirmed`：`PvfEntryLifecycleContentSource.Parse` 将四个值解释为 `itemId, quantity, expiresAt, mode`；`mode=0` 是 `RefillToTarget`。`2030-01-01 06:00:00` 是这条 PVF 规则的**到期时间**，解析为 UTC+8，不能当作每天的刷新时刻。

当前通用补给规则按 UTC+8 自然日，即 `00:00` 切换领取周期；只有代码中特列的 `690001556` 与 `2680738` 才使用可配置的 `Game:DailyResetTime`（默认 `06:00`）。`10003235` 不在特列中，所以它在当前实现里是零点周期，并在选角/世界进入准备时补到 1；没有证据说明它会在角色持续在线跨零点时立即主动推送。

GM 每日补给配置还可禁用这条 PVF 规则，或用相同 item id 的自定义数量、模式和到期时间覆盖它；配置变更保留当天已有处理记录，不重新发放。因此“当前会补到 1”只成立于 PVF 规则有效、未被 GM 禁用或覆盖且本周期尚未处理的有效配置状态。

静态资源还支持“普通票可在商城购买”的文本与商城引用。活动票究竟在原服何时补发、是否每天仅补缺额、是否与账号或角色绑定，除了当前实现的泛化规则外没有 A21 线级或原服服务端证据。因此原服来源和刷新策略仍是 `Unknown`。

### A21 双票语义的边界

下列事实是 `Unknown`：

- 两字段在 A21 原服是 OR、AND，还是按特定活动状态切换；
- 两种票同时存在时优先消耗哪一种；
- 缺票拒绝使用哪个 CMD `0x0010` 状态码；
- 选图 UI 是否显示两种票、是否显示剩余次数；
- 重试是否再次扣票，异常断线是否退票。

仅从两个 PVF 字段并列出现，不能推导 AND 收费。

## 70 服务端参考：双票是候选替代关系

70 PVF `Script.20130704.pvf` 中没有检出 `chn_species`、`species_dungeon`、map `25313`、item `10003196` 或 `10003235`。该活动晚于这份 PVF，因此没有地图级 `70Reference`。

70 ELF 的通用 event-dungeon 代码提供了可复用语义，但不能升级为 A21 事实。

### 准入检查

`70Reference / Confirmed`：`CParty::CheckEnterEventDungeon(CDungeon const*, byte&, Inven_Item*, int*)` 位于 `0x0859EE66`。

- event required item 位于 `CDungeon + 0x860`，数量位于 `+0x864`。
- regular required item 位于 `CDungeon + 0x7F4`，数量位于 `+0x7F8`。
- `0x0859EF1B..0x0859F04B` 先检查 event item。
- event item 缺失或数量不足后，`0x0859F05A..0x0859F199` 再检查 regular item。
- 只有两条候选都失败时才拒绝。因此 70 的通用语义是 OR，不是 AND。

### 开跑后扣除

`70Reference / Confirmed`：`CParty::do_after_dungeon_start` 位于 `0x0859FCFC`。

- `0x085A0642..0x085A066A` 先把 event required item 交给删除助手。
- `0x085A0672..0x085A068E` 在 event 删除成功时立即返回，不再处理 regular item。
- event 删除失败才在 `0x085A0693..0x085A06BB` 回退 regular item。
- `CParty::do_after_dungeon_start_checkndelete_item` 位于 `0x085A0756`；缺物或数量不足在 `0x085A078F` / `0x085A07D2` 返回 `0x11`，成功路径调用 `CInventory::delete_item` 并发送背包更新。

这证明 70 通用策略是“活动票优先、普通票回退、仅消费一份”。`0x11` 也是 70 的返回值，不能直接宣称为 A21 DGN 3557 的拒绝码。

### 调度协议不可直接映射

70 的调度管理器会构造 70 版 `0x01ED`，但 opcode 与布局都不能映射为 A21。A21 的候选必须从本仓已有 `NOTI 0x033F` 静态证据和后续 A21 抓包验证。

## 四元素与封印石状态链

### 精灵死亡写位

`ContentStatic / Confirmed`：四个元素精灵的 `[last action]` 写 `PARTY SYMBOL` 的独立参数，并显示中心提示。

| 元素 | monster id | 精灵所在 map | `[SET BIT PARTY SYMBOL]` 参数 |
|---|---:|---:|---:|
| 暗 | `110348` | `25316` | `1` |
| 火 | `110349` | `25318` | `2` |
| 光 | `110350` | `25334` | `4` |
| 水 | `110351` | `25332` | `8` |

每条动作的关键原语是 `[SET BIT PARTY SYMBOL] <arg>`。脚本给出 `1/2/4/8` 四个不同参数，但本轮没有追到标签解释器实际执行 OR/AND 的 native 指令，所以不能证明参数是掩码值，也不能排除它们是索引或另一种枚举。服务端可用四个命名布尔状态实现等价行为；若内部选用位图，`0x0F` 只能算实现编码，不能冒充 A21 内容或网络布局。

### 封印石读位并销毁

`ContentStatic / Confirmed`：四个普通被动物件的 action 使用与对应精灵相同的参数做检查，满足时执行 `DESTROY`。

| 元素 | passive object id | 封印石所在 map / 坐标 | `[CHECK BIT PARTY SYMBOL]` 参数 |
|---|---:|---|---:|
| 暗 | `54291` | `25324` / `(1,2)` | `1` |
| 火 | `54292` | `25330` / `(2,3)` | `2` |
| 光 | `54293` | `25326` / `(3,2)` | `4` |
| 水 | `54294` | `25320` / `(2,1)` | `8` |

关键原语是 `[CHECK BIT PARTY SYMBOL] <arg>`，命中后执行 `DESTROY`。精灵房与封印石房不同，因此“相同参数跨房写入后读取”已闭合；参数的底层 bit 编码仍是 `Unknown`。

### 中心宝箱

`ContentStatic / Confirmed`：中心 map `25325` 包含 special passive object `15150`（`secretbox2`）。它的普通 action 只显示自定义动画状态转换，没有发现 `CHECK PARTY SYMBOL`。四个封印石是普通被动物件，也不是这个 special object。

因此“集齐四个状态后宝箱自身检查某个合成值”没有静态证据。更符合现有资源的最小解释是：四个对应状态分别消除通往中心的四个封印物件；中心宝箱沿用普通 special-object 奖励链。路径是否必须解四个、只需一条、或有其他导航约束仍要实机确认。

### A21 客户端描述符地址

只读 A21 dump 中解码到以下描述符：

| 字符串 | VA | RVA |
|---|---:|---:|
| `[spicies dungeon]` | `0x03657F90` | `0x03257F90` |
| `[event required item]` | `0x03659BF0` | `0x03259BF0` |
| `[required item]` | `0x03659F78` | `0x03259F78` |
| `[CHECK BIT PARTY SYMBOL]` | `0x0367FFAC` | `0x0327FFAC` |
| `[CHECK PARTY SYMBOL]` | `0x0367FFE8` | `0x0327FFE8` |
| `[SET BIT PARTY SYMBOL]` | `0x03683038` | `0x03283038` |
| `[SET PARTY SYMBOL]` | `0x036831CC` | `0x032831CC` |

绝对引用主要落在资源/action 描述符注册区，例如 `RVA 0x0283B07E`、`0x0283B264`、`0x0283F287`、`0x0283F2C0`。`[required item]` 与 `[event required item]` 的直接引用位于 `VA 0x02AAC079` / `0x02AAC680` 的大型静态注册过程。

这些地址证明 A21 客户端认识这些资源和 action 标签。当前追踪没有把它们连到收包 handler、发包构造器或明确的同步字段，所以 `PARTY SYMBOL` 的权威位置和网络布局仍是 `Unknown`。

## 协议候选与当前实现差距

### 入场：复用 CMD `0x0010`

`CurrentImplementation / Confirmed`：DGN 3557 会走普通 `SELECT_DUNGEON` 流程。已有 CMD `0x0010` 可承载 dungeon id、难度与现有选择字段；普通教学/正式副本成功路径已在其他地下城实机验证。

当前缺口位于内容建模，不在 opcode：

- `PvfDungeonDefinitionSource.Fields` 只声明 `[required item]`。
- 解析循环只把这个字段转换为 `DungeonRequiredItem`。
- `[event required item]` 被忽略。
- 所以 DGN 3557 当前只要求并消费普通票 `10003196`；每日补发的 `10003235` 不参与入场。

不能把两个字段简单拼成同一个 `requiredItems` 列表，因为当前列表语义会自然变成 AND。需要明确的替代组或专用准入策略。

### 入口显示：NOTI `0x033F` 是候选

`ClientStatic / High`：现有 [NOTI 0x033F](../../protocol/game/server-to-client/NOTI-0x033F.md) 研究得到固定 16 字节：

```text
u32 category
u32 state
u32 contentId
u32 opaque
```

类别 0 用于 scheduled dungeon；状态 1 或 2 可见，0 或缺记录会隐藏候选。对 DGN 3557 的待测首选向量是：

```text
category = 0
state = 1    # 也要差分 2
contentId = 3557
opaque = 0
```

`ClientStatic / High`：普通选图链按候选 dungeon id 查询类别 0 状态；DGN 3557 又明确位于调度地下城表，因此通用语义支持 `contentId=3557`。`2331` 是 PVF schedule 配置身份，没有证据把它放进 `contentId`，不应作为与 3557 同等的线上候选。

`Unknown`：尚无 DGN 3557 的 033F 抓包或 UI 验收，所以仍需验证 `{category=0,state=1/2,contentId=3557,opaque=0}` 是否让本图出现，以及 state 0 是否让它消失。

### 元素状态：优先复用既有副本回路

现有协议可作为实现承载面的候选：

1. `CMD 0x0027 DIE_MONSTER` 报告运行 actor 死亡。服务端已有 run actor 身份，可在确认死亡的 actor template 是 `110348..110351` 时设置对应 run bit。
2. `START_MAP` / 当前房间投影负责生成进入房间时的 actor 与 object。服务端可依据 run bit 决定封印石是否仍应存在，或者投影相应的已毁状态。
3. `CMD 0x0027` 的 A21 body 还包含被动物件销毁明细，但物件只有 code 和明细位置，没有已证实的全局实例 id；同 code 多实例仍要以当前 cell 的队列解析。
4. `CMD 0x002A GIVEUP_GAME` 当前会结束运行，可作为丢弃 run-scoped 元素状态的明确边界。

`CurrentImplementation / Confirmed`：

- `DungeonRunDomainService.Objects.ClaimObjectDestruction` 只按 `runId + roomVersion + current cell + objectCode` 预留和消费房间物件队列。
- `DungeonRoomMechanismKind` 当前只有 `Elevator`。
- 没有 PARTY SYMBOL、四元素状态或 DGN 3557 专用规则。

`Unknown`：客户端本地 action 是否已在单人局里自行保存四元素状态并销毁对象，以及服务端是否只需接受由此产生的移动/销毁结果。没有 3557 实机流量前，不能宣称“纯客户端脚本就足够”，也不能宣称必须存在专用同步包。

### 中心宝箱与奖励

当前房间物件奖励队列可承载 ordinary special-object 领取和事务入包。它还不能证明：

- 客户端用哪条动作/协议打开 `15150`；
- 原始元组中 `3500` 的含义；
- 每局可领取次数、背包满处理、重试或重连后的幂等语义；
- 宝箱是否由四元素状态直接控制。

实现时应把 `15150` 的当前 cell 实例、奖励事务和已领取标记绑定到 run，不要仅按全局 object code 记一次。

## 建议实现切片

### 1. 内容与准入

- 在 `DungeonDefinition` 中保留 `RequiredItems` 与 `EventRequiredItems` 两个概念，或引入显式 alternative admission group。
- 仅对存在 `[event required item]` 的地下城启用替代选择，避免改变普通多材料地下城的 AND 语义。
- 以 70 策略作为初始规则时，明确标注为 provisional：先尝试活动票，失败后普通票，成功只扣一种。
- 检查、选票、扣票、创建 run 必须在同一事务/冻结计划中完成，防止并发重入或扣错票。
- A21 缺票拒绝码在抓包前保持可配置或局部封装，不能用 70 的 `0x11` 直接升格。

### 2. 一局四元素状态

- 在 dungeon run aggregate 增加四个命名元素状态和 revision；如果代码内部用 mask，只把它当服务端私有编码。
- 仅在服务端接受特定 actor 的首次死亡时设置对应元素；重复死亡报告应幂等。
- 跨房返回时根据四元素状态重建封印石状态；新 run 全部初始化为未解封，`GIVEUP_GAME` / 正常结算后销毁。
- 先不创建新 opcode。用既有房间投影表达“封印石不存在/已毁”；若 A21 客户端不能正确呈现，再根据抓包补最小同步消息。

### 3. 中心宝箱

- 把 `15150` 作为中心 cell 的 run-owned 特殊物件。
- 奖励表从 PVF 原始元组经过已有静态内容解析器解释，未闭合 `3500` 前不硬编码数量。
- 领取过程继续使用事务预留、持久化入包、提交后消费物件的顺序；为重发和回房增加幂等覆盖。

### 4. 调度显示

- 先做可开关的 DGN 3557 `ScheduledDungeonState` 投影。
- 首测 `033F {0,1,3557,0}`，再差分 state 2 和关闭 state 0；除非新证据推翻通用 dungeon-id 查询链，不发送 `contentId=2331`。
- 调度显示与实际准入分开：UI 可见不等于准入成功，服务端仍必须检查等级、单人、票和运行状态。

## 实机验证矩阵

所有项都需要原生 A21 客户端、全量双向 FrameCapture 和玩家可见记录。至少保存每次 CMD `0x0010` 前后的背包快照与帧序。

| 场景 | 预期需要判定的事实 |
|---|---|
| 两种票都没有 | 入口显示、是否发 0010、拒绝码、控件能否重试 |
| 只有 `10003196` | 能否进图、扣除数量、拒绝/成功帧序 |
| 只有 `10003235` | 是否作为替代票、扣除数量 |
| 两种票都有 | A21 原生优先级、只扣一种还是双扣 |
| 活动票 0 / 1 且跨日 | 原服刷新时机、补到目标还是累加、角色还是账号范围 |
| category 0/content 3557 的 033F 开/关 | 山麓入口是否出现、关闭是否消失、state 1 与 2 差异 |
| 击杀单个元素后离房再返回 | 对应封印石是否保持销毁，是否出现额外 C2S/S2C 帧 |
| 四元素不同顺序 | 四个状态是否可交换累积，提示与道路是否一致 |
| 未解封直接进中心 | 客户端/服务端哪一侧阻止，是否有拒绝帧 |
| 打开 `15150` | 请求 identity、奖励元组解释、背包满和重复请求 |
| `GIVEUP_GAME` 后重入 | 票是否再扣、四元素状态是否重置、宝箱领取状态是否重置 |
| 断线/重连/强退 | run 清理、门票与奖励原子性、状态是否残留 |

抓包后才能把对应结论升级为 `WireCaptured`；完成 UI、跨房、扣票与奖励的玩家可见验收后才能升级为 `ClientVerified`。

## 当前未知清单

1. A21 双票 OR/AND 与优先级。
2. A21 缺票、队伍人数、调度关闭各自的拒绝码和 UI 文案。
3. DGN 3557 是否必须由 `NOTI 0x033F` 开放，以及 state 1/2 的本图 UI 差异；现有通用链高置信支持 `contentId=3557`。
4. PARTY SYMBOL 参数 `1/2/4/8` 的 native 编码语义；状态是客户端本地、服务端权威还是混合；是否存在专用同步消息。
5. 封印石销毁是否会进入 `CMD 0x0027` 的 object destruction 明细，还是完全由客户端 action 处理。
6. 到达中心需要四元素状态全部满足、任一通路，还是其他组合。
7. `15150` 开箱协议和 `[special passive object item]` 原始元组字段语义。
8. 正常结算、重试、放弃、断线时的门票与四元素状态生命周期。

## 可复现材料

- [protocol/a21_dump_inspect.py](protocol/a21_dump_inspect.py)：校验 A21 dump 哈希，解码加密 UTF-16 描述符，查看 xref、常量和反汇编。
- [protocol/reproduce.ps1](protocol/reproduce.ps1)：只读执行身份、描述符、xref 与 70 通用双票函数复查。
- [content/PvfContentProbe/Program.cs](content/PvfContentProbe/Program.cs)：生成 DGN 3557 的冻结 PVF 内容清单。
- [content/inventory.json](content/inventory.json)：当前 PVF 的完整原始证据快照。

从仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Document\SealedTreasure-Research-20261019\protocol\reproduce.ps1
```

脚本只读取已指定的 dump、ELF、PVF 清单和仓库文件，不运行客户端、不启动或重启服务。
