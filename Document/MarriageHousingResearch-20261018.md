# 婚房、情侣仓库、爱情树与成长胶囊：协议与实现调查

日期：2026-10-18。分支 `task/next-round-20261013`，调查基线 `61beac40`。
性质：只读调查与实现预设计。没有修改生产代码、PVF、客户端、数据库或运行中的服务，也没有启动客户端。
新增证据全部是 `ClientStatic`、`ContentStatic`、`UpstreamReference` 和公开资料，**没有新增 `WireCaptured` 或 `ClientVerified`**。

## 结论

1. **协议骨架已经足够写保守的 codec。** 婚房相关的 13 条 C2S、10 条 NOTI 的正文长度和字段宽度都已逐条读出（第 3 节）。按原有规则，C2S 正文等于整个 raw 结构的长度，业务字段从正文 +14 开始，**不能再减 14**。`0402/0403/0404/0405/0406` 的正文确认为 0 字节。
2. **NOTI `03D8` 的结构三方吻合。** 它是 `n × (u16 家具槽 + u32 家具物品 ID)` 加 13 字节尾部，尾部就是爱情树快照（与 NOTI `03DC` 读法相同）。客户端读法、PVF 的 28 个摆放槽位和 76 件家具、上游 113 字节回放常量的解码，三者互相印证。家具坐标不在协议里，由槽位编号对应婚房布局里的固定点。
3. **规则数值基本都在 PVF 里。** 家具价格以心意点计；婚房 Buff 按华丽度分 7 档；爱情树有 3 种，30 天到期，每天浇水 2 次，成长值 10/20/30 分三阶段，收获权重表合计 100000；成长胶囊和戒指升级的参数也都齐全。只有**情侣仓库在 PVF 里没有任何配置**，它的 40 格、互斥打开等规则来自客户端代码。
4. **参考实现里没有可用的业务逻辑。** ServerS4A21 只有 opcode 名和固定常量回放；70 版二进制里婚姻代码已被剔除，但它的数据库模板里有个人房屋和树的表结构，可以借鉴。
5. **实现上有三处必须先定的东西：**
   - town 101 只有一个区域，教堂那种"一对一个区域"的隔离做法行不通，需要给区域键加实例维度；
   - 情侣仓库的持久化落点；
   - 心意点的消耗与现有"心意点只增不减"的启动对账规则冲突。
6. **还有一个现存越权口要先修。** 今天任何角色发通用换城请求 `0x0024` 都能进 town 101：教堂以外的区域，服务端一律放行。
7. **两条关键链路静态上追不到，必须实机抓包。** 一是怎么进出婚房：客户端只用 `town==101` 判断，没有专用请求。二是个人背包和情侣仓库之间怎么存取：候选是通用 `0x0013` 加容器类型 `0x1F`。

建议的实施顺序是：先补准入门和实例层，再做只读投影，然后依次是家具与仓库、爱情树、成长胶囊，最后做戒指升级。见第 6 节。

## 1. 子报告与证据身份

| 子报告 | 范围 | 证据层 |
|---|---|---|
| [A21 客户端静态逆向](outputs/marriage-housing-20261018/a21/a21.md) | 21 个构包点、40 个 handler 注册点、错误码→dstr、NPC 菜单、`town==101` 判定点 | ClientStatic |
| [PVF 内容](outputs/marriage-housing-20261018/pvf/pvf.md) | CoupleRoom town/map、入口门、76 件家具与 28 槽、Buff、树、胶囊、戒指；双端 PVF 144 项资源一致性 | ContentStatic |
| [参考实现](outputs/marriage-housing-20261018/reference/reference.md) | ServerS4A21、86JP、70 版 df_game_r 与 DB 模板；113B 常量解码 | UpstreamReference / 70Reference |
| [当前服务端接缝](outputs/marriage-housing-20261018/server/server.md) | 换城门、教堂隔离、物品容器、并发、时间、投影、婚姻聚合判据、风险 | CurrentImplementation / Proposal |
| [公开玩法资料](outputs/marriage-housing-20261018/web/web.md) | 国服 2014 起的官方公告与攻略 | 公开资料（多为国服） |

| 工件 | 身份 |
|---|---|
| A21 EXE | `DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe`，SHA-256 `CA7ECA4E…388D14` |
| A21 内存镜像 | `dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`，SHA-256 `6BD2746D…953507` |
| 服务端 PVF | `artifacts/sao-ticket-20261014/Script.server.pvf`，SHA-256 `91902D91…DEBF4`；与客户端 PVF 的相关资源解码后一致 |
| ServerS4A21 | `c20dba5f` |

公开资料有一个前提要注意：网上能找到的婚姻系统资料全部来自国服（2014-02 上线）。韩服、日服、台服都没搜到上线记录。A21 PVF 里这套内容的路径带 `chn_` 前缀，可能是随客户端带着、但日服没有开放的内容（推断）。所以公开资料只能当规则旁证，以 PVF 和客户端为准。

今天早先留下的未提交探针 `outputs/marriage-housing-a21-20261018/` 有两处入口地址写错，也没有解析跳表，已被 a21 子目录的产物取代，没有纳入本次提交。

## 2. 玩法规则（PVF 为准，公开资料旁证）

### 2.1 婚房

- **[ContentStatic / Confirmed] 结构**
  - town `101 CoupleRoom.twn` 只有 area 0，地图是 `CoupleRoom/CoupleRoom.map`。
  - 房内只有两个 NPC：`20012` 花圃（爱情树）和 `20011` 云清（商店 1206/1207、新婚祝词、领取夫妻赠礼）。
- **[ContentStatic / Confirmed，语义 High] 入口**
  - 各城镇生效的赛丽亚房间地图 `(r)gate_*.map` 都有一块可走区域 `46 212 34 52 → 101 0`，并放置门对象 `54170 couple_room_gate.obj`。
  - 公开资料的描述是"赛丽亚房间左侧带锁的门，只有夫妻能进"。
- **出口**：可走区域的目标是 `-101`。推测是"回到来源城镇"（Hypothesis），服务端不能把它当真实 town ID。
- **[ClientStatic] 客户端判断**：客户端只用 `town == 101` 判断"当前在婚房"，共 11 处判定，没有一处在构包函数里。"移动到伴侣身边"（`03FB/03FC`）在任一方位于 town 101 时会被客户端拒绝。
- **访问权**：`03D8/03D9/03DB` 都不带"房主"字段，访客规则也没有任何证据。本服的初始选择（Proposal）是：只允许已婚双方进入自己这对的房间；配偶离线也能进；暂不开放访客。

### 2.2 家具与华丽度

- **[ContentStatic / Confirmed] 家具与价格**
  - 共 76 件 `[stackable type] [couple room item]`（`10002684..10002802`），都是可堆叠物品，单件不带属性。
  - 价格是 `[need material] 10002606 N`。`10002606` 名为「心意点」，是婚姻爱心点（上限 999）的物品化表示，在云清商店兑换。
- **[ContentStatic / Confirmed] 摆放槽位**：`decoration/coupleroomiteminfo.etc` 定义了 28 个槽位（0..27），每件家具声明自己可以摆在哪些槽。地板占槽 0，壁纸占槽 1。
- **[ClientStatic / Confirmed] 客户端提示**：
  - 摆放：位置不匹配（`0x11C5F`）、槽位已占（`0x11C60`）、壁纸和地板不可回收（`0x11C6B`）、窗帘与窗户只能二选一（`0x11C6C`）。
  - 收回：收回的装饰移至物品保管箱（`0x11C6D`）。
  - 这些都是服务端必须复做的校验。
- **[ContentStatic / Confirmed 表，High 口径] Buff**
  - `[couple room buff]` 分 7 档，阈值 10/40/100/200/500/800/1000，决斗场无效。
  - 阈值单位推断为已摆放家具的心意点价值之和：件数最多 28，爱心点上限 999，都到不了 1000。
  - 客户端 `00881DF0` 会遍历已摆放的家具，累加其数值写入角色 +0x388；`03DD` 也会为他人写同一字段。所以 Buff 很可能由客户端自己算，服务端不必下发档位（High）。

### 2.3 情侣仓库

- **[ClientStatic / High] 结构与互斥**
  - 仓库 40 格，属于全局物品管理器上的容器类型 `0x1F`，窗口 0x1BD。
  - `03D9` 的条目只有 `(slot, itemId, count)`，不带 UID 或耐久，说明仓库只装可堆叠物品。
  - `CMD 03FD/03FE` 回 -4/-20 时客户端提示"爱人正在使用物品保管箱"，可见同一时刻只允许一方打开。
- **[ContentStatic / Confirmed] PVF 没有任何仓库配置。** 公开资料说"初始 40 格可扩到 120 格、只自动存放婚房道具"（国服）；A21 客户端是固定 40 格。

### 2.4 爱情树

- **[ContentStatic / Confirmed] 种子与成长**（`decoration/coupletree.etc`）
  - 3 种种子对应 3 棵树：`10002766→1`、`10002768→2`、`10002767→3`。
  - 30 天到期，每日浇水 2 次。
  - 洒水壶 `10002765` 每次 +10，强力洒水壶 `10002905` 每次 +20。成长阶段阈值 10/20/30，到 30 可收割。
- **[ContentStatic / Confirmed] 收获**
  - 奖励按权重抽取（合计 100000）：邀请函×2 40%、心意通×2 40%、同心喇叭 10%、邀请函×3 5%、心意通×5 4%、邀请函×5 1%。
  - PVF 文本写明"夫妻二人皆可获得"。邮件标题/正文文本 ID 是 1078/1192，所以奖励大概率走邮件（High）。
- **来源**：种子和洒水壶在云清商店 1207 购买，或从爱之摩伽陀掉落（洒水壶 100%，种子各 5%）。
- **[ClientStatic / High] 状态由服务端推送**
  - 快照是 `u32 树种类(0=无) + u32 枯萎时刻(Unix 秒) + u32 成长值 + i8 未知`。
  - 客户端只拿枯萎时刻和本地秒时钟比较，显示"已枯萎"和"剩余天数"。成长值、每日次数、收获资格全由服务端决定。
  - 菜单 1021..1025 分别是种植、浇水、收割、清理、信息。
- **未定规则**：
  - 每日 2 次是夫妻合计还是各自 2 次；
  - 收获是双方各抽一次，还是共享一次；
  - 到期后自动清除还是留着枯树。
  - 70 版数据库模板的 `day_water_count` 存在树那一行上，偏向"合计"（Hypothesis）。

### 2.5 成长胶囊

- **[ContentStatic / Confirmed] 参数**（`etc/couplegrowthcapsule.etc`）：持有上限 10、领取上限 10、每次 gage +2、满 10 生成 1 个 `10002910 爱情成长胶囊`（+30000 经验，账号绑定，Lv30–84）。
- **[ClientStatic / High] 客户端状态**：胶囊 manager 存三个值：能量、对方送来的待领取数、我方持有数。make/give 是窗口里的两个按钮，take 在云清的"领取夫妻赠礼"菜单。
- **增长事件未知**：gage 靠什么增长（登录、组队、在线时长）不知道，公开资料也查不到。
- **和账号成长胶囊不是同一个系统。** 现有 `IExperienceCapsuleDomainService` 管的是账号成长胶囊（上游 `0x01F1`）。爱情成长胶囊是另一套，产出的是一件普通消耗品，不要共用账本。

### 2.6 戒指升级（次要）

- **[ContentStatic / Confirmed] 等级与券**：戒指 Lv0..5。升级券 `10002611..15`，所需心意点 50/300/500/800/999；`[price]` 是 50 万到 1200 万金币，这是售价还是手续费 PVF 没有声明。
- **同步规则**：PVF 写明"一方升级成功，另一方自动升级"，对应 `CMD 03F4` 成功和 NOTI `03D1` 同步，两者走同一个 setter。
- 公开资料都没提到升级失败，推断必定成功。

## 3. 协议总表（A21，摘要）

完整的 handler 地址、错误码与 dstr 映射、manager 字段见 [A21 子报告第 2 节](outputs/marriage-housing-20261018/a21/a21.md#2-协议总表a21)。确证结论已写入下列记录的人工区。

### 3.1 C2S（CMD 族，正文偏移从 +14 起为业务区）

| type | 名称 | 正文 | 业务字段 | 回包消费 |
|---|---|---:|---|---|
| [`03FD`](../protocol/game/client-to-server/0x03FD.md) | COUPLE_INVEN_OPEN_CLOSE | 15 | `u8 flag`（0 开，1 关） | `i32 status + u8`；-4 爱人正在使用 |
| [`03FE`](../protocol/game/client-to-server/0x03FE.md) | COUPLE_ROOM_MOVE_ITEM | 28 | 两条 7B `(u8 kind, u16 slot, u32 itemId)`；kind 1=仓库格，0=房间槽 | `i32`；结果走 NOTI 03DA |
| [`03FF`](../protocol/game/client-to-server/0x03FF.md) | COUPLE_INVEN_DELETE_ITEM | 24 | `u16 仓库槽 + u32 itemId + u32 数量` | 不读正文；结果走 NOTI 03DB |
| [`0400`](../protocol/game/client-to-server/0x0400.md) | COUPLE_TREE_PLANT | 20 | `u16 种子背包槽 + u32 种子 ID` | `i32`；-4 已有植物 |
| [`0401`](../protocol/game/client-to-server/0x0401.md) | COUPLE_TREE_WATER | 20 | `u16 洒水壶槽 + u32 洒水壶 ID` | `i32`；-11 已枯萎、-9 水分已足、-6 今日次数用完、-4 未种植 |
| [`0402`](../protocol/game/client-to-server/0x0402.md) | COUPLE_TREE_HARVEST | 0 | — | `i32`；-6 成长值不足、-7 已枯萎 |
| [`0403`](../protocol/game/client-to-server/0x0403.md) | COUPLE_TREE_REMOVE | 0 | — | `i32`；0 → 提示花圃已清空 |
| [`0404`](../protocol/game/client-to-server/0x0404.md) | MAKE_COUPLE_GROWTHCAPSULE | 0 | — | `i32 + u32 能量 + u8 持有` |
| [`0405`](../protocol/game/client-to-server/0x0405.md) | GIVE_COUPLE_GROWTHCAPSULE | 0 | — | `i32 + u8 持有` |
| [`0406`](../protocol/game/client-to-server/0x0406.md) | TAKE_COUPLE_GROWTHCAPSULE | 0 | — | `i32 + u8 待领` |
| [`03F4`](../protocol/game/client-to-server/0x03F4.md) | WEDDING_UPGRADE_RING | 19 | `u32 升级券背包槽 + u8 目标等级` | `i32 + u8` |
| [`03FB`](../protocol/game/client-to-server/0x03FB.md) / [`03FC`](../protocol/game/client-to-server/0x03FC.md) | CHECK_MOVE_TO_PARTNER / MOVE_TO_PARTNER | 22 / 34 | 配偶身份、道具槽、位置 | 不是进婚房的方式 |

### 3.2 NOTI

| type | 名称 | 正文 | 布局 |
|---|---|---:|---|
| [`03D8`](../protocol/game/server-to-client/NOTI-0x03D8.md) | COUPLE_ROOM | 17+6n | `u32 n` + n×`(u16 槽, u32 家具 ID)` + 13B 树快照 |
| [`03D9`](../protocol/game/server-to-client/NOTI-0x03D9.md) | COUPLE_INVEN | 8+10n | 先清 40 格；`u32 n` + n×`(i16 slot, u32 id, u32 count)` + `u32`（读后丢弃） |
| [`03DA`](../protocol/game/server-to-client/NOTI-0x03DA.md) | COUPLE_ROOM_UPDATE | 23 | `u8 op` + 两条 11B `(u8 kind, u32 itemId, u16 slot, u32 value)` |
| [`03DB`](../protocol/game/server-to-client/NOTI-0x03DB.md) | COUPLE_ROOM_REFRESH | 4+11n | `u32 n` + n×11B 记录；家具 value 0=撤下、1=摆放；仓库 value=数量 |
| [`03DC`](../protocol/game/server-to-client/NOTI-0x03DC.md) | COUPLE_TREE | 13 | 树快照，同 03D8 尾部 |
| [`03DD`](../protocol/game/server-to-client/NOTI-0x03DD.md) | COUPLE_SIMPLE_INFO | 14 | 他人角色的配偶、戒指等级候选、婚房装饰值 |
| [`03DE`](../protocol/game/server-to-client/NOTI-0x03DE.md) | 收获奖励 | 2+8n | `u16 n` + n×`(u32 itemId, u32 count)`，仅展示 |
| [`03DF`](../protocol/game/server-to-client/NOTI-0x03DF.md) / [`03E0`](../protocol/game/server-to-client/NOTI-0x03E0.md) | 成长胶囊 / 能量 | 7 / 4 | 能量、持有、待领、flag / 能量 |
| [`03D1`](../protocol/game/server-to-client/NOTI-0x03D1.md) | WEDDING_UPGRADE_RING | 1 | `u8 戒指等级`，同步另一方 |

注意 `03FE` 的 7B 记录和 NOTI 的 11B 记录字段顺序不同。这些记录的机器区标题有几处沿用了其他版本的名称（如 `0x03FD` 写作 `PLAY_DNF_AT_LABORDAY`），以人工区的 A21 裁定为准。

### 3.3 交叉印证

上游 113 字节回放常量解码为 `n=16`：

- 16 个 ID 全部是 PVF 婚房家具，每个槽位编号都在该家具允许的位置内。
- 但其中有一个槽位放了两件家具，所以服务端必须自己保证槽位唯一，不能拿这段常量当合法样本。
- 尾部是 `3 / 1761947886 / 30 / 1`，即黄宝石树、某个 Unix 时刻、成长值 30（满阶段）、i8=1。这与客户端的树快照读法和 PVF 的成长阈值吻合。

这段常量只能当编码测试样本和 UI 预览，不能在生产中回放。

## 4. 本轮纠正的认识

| 旧说法 | 纠正 | 依据 |
|---|---|---|
| `0x0403` 机器区说不能断言正文为空 | 正文为 0，Confirmed | 构包函数 `01047AA0` 在 MakeHeader 后直接跳 Finalize |
| 03D8 尾部的业务含义 Unknown | 尾部是爱情树快照，同 03DC | 两个 handler 写入同一组 manager 字段 |
| `MarriagePhase1Research-20261016.md:118` 说当前仓仍按旧彩票编码 03D8 | 已过时；选角固定发 204 个零的空态兼容体 | `SelectedCharacterStateMessageFactory.cs:111` |
| 0x0404..0406 的"成长胶囊"可能是账号成长胶囊 | 是另一个独立系统 | `etc/couplegrowthcapsule.etc` |
| 进婚房可能走"移动到伴侣身边" | 不是；客户端在 town 101 会拒绝该操作 | `03FB` 回包 handler |

## 5. 实现方案（Proposal）

### 5.1 单元划分

按 [婚姻聚合设计](MarriageAggregateDesign-20261017.md) 的判据："这块状态是否参与其他聚合的不变量"。婚房、仓库、树、胶囊都**不改变婚姻阶段**，只要求关系处于 Married。因此新增一个与婚前任务单元平级的 `CoupleHousingDomainService`：

- 只读 `IMarriageRelations`，不接 `IMarriageRelationWriter`，不接 `MarriageDomainService`；补一条与婚前任务单元同款的可达性判据。
- 下设 `CoupleRoomLayout`（摆放）、`CoupleStorage`（仓库）、`CoupleTree`（树）、`GrowthCapsules`（胶囊）四个组件，各自独占自己的表。新 store 放在 `Abstractions.Marriage` 命名空间，自动受"单一所有者"判据约束。
- 对外两个门面：`IMarriageHousingQueryDomainService`（只读投影与准入）和 `IMarriageHousingDomainService`（写用例）。接口草案、DI 位置和表草案见[服务端子报告第 5 节](outputs/marriage-housing-20261018/server/server.md#5-婚姻聚合扩展建议)。
- 以后做离婚时，由婚姻实体在离婚迁移的同一事务里调用本单元的清算入口；本单元不反向依赖婚姻实体。
- 新增整数夫妻号表 `marriage_couple_numbers`，在婚礼完成时分配或首次使用时惰性分配。它同时充当婚房实例号和各表外键。

### 5.2 进入婚房与实例

1. **先补准入门（与功能开发解耦，可以单独先做）**：在 `TownProjectionSlice.cs:317` 之前调 `CanEnterRoomAsync`，只放行 Married 关系的参与者；拒绝时复用 `RefuseTownChangeAsync`。
2. **实例层**：town 101 只有 area 0，所以给 `TownAreaKey` 加 `Instance` 维度。默认值 0，现有行为不变；进入婚房时取实例号 = 夫妻号。影响范围是 `TownAreaDomainService`、`TownAreaProjectionDelivery.KeyOrder`、队伍目录的 scope，以及所有 `new TownAreaKey(...)` 构造点。这是本期改动面最大的一项，建议单独一个提交并补同屏可见性判据。
3. **不持久化婚房坐标**：仿照临时频道分支，进入 101 时不写坐标，保留进房前的城镇坐标，避免重登直接落进一个已经丢了实例的房间。
4. **出口 `-101`**：先抓包确认客户端离开时发的 town 值，再决定是由服务端改写成来源城镇，还是由客户端自己解析后才发出。

### 5.3 家具与情侣仓库

已知的事实对模型有强约束：仓库只装可堆叠物品（03D9 没有 UID）；家具是可堆叠物品；`03FE` 只在"仓库格 ↔ 房间槽"之间移动；收回的家具回到仓库。

**建议用专用表，而不是给通用容器加第三种主人。** 这与服务端子报告倾向的做法不同，理由如下：

| | 专用表 `marriage_couple_storage(couple_no, slot 0..39, item_id, count)` + `marriage_room_furniture(couple_no, slot 0..27, item_id)` | 通用容器加 `couple` 主人 |
|---|---|---|
| 与协议形状 | 一一对应：03D9/03DB 就是两张表的投影 | 要把通用容器再投影成只有 id+count 的形状 |
| 迁移风险 | 只新增表 | 要重建全库核心表 `item_slots` 才能放宽 CHECK |
| 物品单一真源 | 堆叠物没有 UID，"取出 n 个 → 加到另一张表"不会出现双份身份 | 天然满足 |
| 代价 | 个人背包 ↔ 仓库之间需要一个桥：通用生命周期 `Take` + `DeleteItem` 与仓库表加数同事务，反向用 `Create` | 现成 |

前提是抓包确认两件事：仓库只能放可堆叠物品，背包与仓库之间存取的协议形状也符合这个假设。如果实机发现仓库能放带 UID 的装备，就回到通用容器方案。

- **摆放家具**：从仓库格减 1、房间槽写一行，同事务；回包后发 `03DA` op=0（A 为源，B 为目标）。收回是反向操作。壁纸和地板不可回收；替换壁纸或地板时旧件直接消耗，这一点需要实机确认。
- **服务端必须复做的校验**：槽位与家具的 `[position list]` 是否匹配；槽位是否已占用；窗帘与窗户互斥；仓库是否有空格（-13 空间不足）。
- **互斥打开**：`03FD flag=0` 时登记"谁在用"（内存运行态，按夫妻号），`flag=1` 或掉线时释放；另一方打开返回 -4。
- **并发**：所有写用例都是 Transactional + 双账号排序租约的 `IMultiAccountCommand`，账号列表由 `CoupleAccountsAsync` 预取，在用例内复核；各表再加 revision CAS 作第二道防线。配偶离线不影响操作，因为租约是内存门。

### 5.4 爱情树

- **只存事实，不存阶段**：表存 `planting_id, tree_kind, planted_at, planted_by, growth, revision`，外加浇水记录（每个日周期一行）和收获记录。到期时刻等于 `planted_at + 30 天`，按 `TimeProvider` 惰性判断，写路径先结算再校验。
- 纯规则放在静态类 `MarriageTreeRules`，参数由 `IMarriageHousingRuleService` 从 PVF 内容源读取；每日周期用 `IDailyResetRuleService`。默认不注册定时任务：玩家进房或打开树界面时，读路径发 `03DC` 即可。
- **消耗与奖励**：种子和洒水壶只扣操作者本人的，走 effect 结算，请求实现 `IEffectSettlementRequest`，重发的帧会走重放而不是再结算一次。收获的奖励按 PVF 文本 1078/1192 走邮件（`ItemDeliveryMode.MailOnly`），**双方各一封**，正好绕开 effect 结算只能有一个收件人的限制；`03DE` 只用于给操作者展示。收获幂等由 `marriage_tree_harvests` 的主键保证。
- **规则待定**：每日次数是合计还是各自、收获是各抽还是共抽。见第 8 节。

### 5.5 成长胶囊

数值和协议都已闭合，但 gage 的增长事件不明。建议在婚房和树稳定之后再做。增长事件需要实机或进一步逆向确认；在那之前不要编造触发条件，不实现这条的话界面会一直显示能量 0。产出的 `10002910` 走普通物品发放，与账号成长胶囊无关。

### 5.6 心意点的消耗（必须先定）

家具、戒指升级券都以心意点计价，这会打破现有的"只增不减"前提。现在的启动对账会把关系行的心意点抬回旧婚礼列的较大值（`MarriageRelationMigration.cs:42-43`），扣掉的点数会在重启后复原。开放任何消耗之前，必须二选一：要么扣点时同步双写旧列，要么先停写旧列并结束回退兼容。

还有一处待抓包：商店显示和扣除的"心意点"，是作为物品 `10002606` 校验，还是读关系上的计数。这决定 NPC 商店（`0x0015`）的购买路径要不要识别这种特殊材料。

### 5.7 投影时机

| 时机 | 发什么 |
|---|---|
| 选角，已婚 | 真实 `03D8`（家具 + 树快照）。落点 `SelectedCharacterStateMessageFactory.cs:111`，数据由选角准备阶段经只读门面填入 `SelectCharacterResult` |
| 选角，未婚 | 保持 204 个零，不缩短（有回归测试守着） |
| 进入 101 之后 | 本对夫妻的 `03D8`、`03DC`，在 `TransferAsync` 之后发 |
| `03FD` 打开 | `03D9` 整仓快照 |
| 共享物变化 | 操作者收到 CMD 回包和 `03DA/03DB`；配偶在线时，提交后按精确会话发同类 NOTI |
| 同帧重放 | 新增 `ReplayProjectionNeeds` 位时，必须同步改 `ReplayProjectionRequest.Validate` 的白名单（`IReplayStateProjection.cs:29`），否则第一次重放就会抛异常 |

上游 commit `fdc2907` 自称经过实机验证：换宠、进本、回城后会重建 USERINFO，需要按选角顺序补发 `03D0 → 03F3 → 03D8`，婚姻面板的属性才会恢复。实现后要专门回归这三条路径。

## 6. 分阶段实施建议

每一步都可以独立验收。前一步在实机上没过，后一步不开放。

| 阶段 | 内容 | 验收 |
|---|---|---|
| H0 | town 101 准入门；`TownAreaKey` 实例维度；婚房坐标不落库；夫妻号表 | 非夫妻用 0x0024 被拒；两对夫妻同时在婚房互不可见；重登回到原城镇 |
| H1 | 只读投影：选角和进房发真实 `03D8`（无家具时 n=0）、`03DC`；`03D9` 空仓 | 婚房界面不崩，换宠、进本、回城后不丢 |
| H2 | 情侣仓库开关与互斥、背包↔仓库存取、`03FE/03FF` 摆放、收回、删除，含所有校验 | 双端看到一致的家具和仓库；并发与重放测试 |
| H3 | 心意点消耗规则（5.6）+ 云清商店兑换家具 | 重启后点数不复原 |
| H4 | 爱情树全流程 | 跨日浇水、到期、双方收奖邮件 |
| H5 | 戒指升级 `03F4/03D1` | 双方等级同步 |
| H6 | 成长胶囊（增长事件确认后） | — |

## 7. 需要你做的实机取证

需要一对已婚测试角色，双端开 FrameCapture，原样保留抓包日志。由于服务端还没实现这些功能，原服行为无从抓起，取证分两种：

- **能直接抓的**：客户端发出的 C2S。服务端未接线时这些帧会被丢弃，但客户端照样会发出来，可以先确认 opcode、正文和字段取值。
- **需要先做 H0/H1 的**：进出婚房的帧序、`-101` 出口发出的值、服务端快照对 UI 的影响。

建议顺序：

1. **进出婚房**：从赛丽亚房间的婚房门进入 town 101 再出来，抓前后全部 C2S，确认是不是 `0x0024`、出口发出的 town 值。可以在 H0 之前就抓一次，看客户端发了什么。
2. **仓库开关**：婚房 NPC 菜单"物品保管箱"，抓 `03FD` flag 0/1。
3. **背包↔仓库**：从个人背包拖一件物品进保管箱，确认 opcode、容器类型和能否放非家具物品。这决定 5.3 的模型。
4. **摆放与收回**：把家具摆到两个不同槽位，记录屏幕位置，抓 `03FE`。
5. **树**：种植、浇水、收割、清理，抓 `0400..0403`。
6. **胶囊**：make、give、take，抓 `0404..0406`。
7. **商店**：在云清处兑换一件家具，抓购买请求，确认心意点的扣法（5.6）。

## 8. 待决问题（需要你定）

| 问题 | 已知证据 | 建议默认值 |
|---|---|---|
| 访客能否进入别人的婚房 | 无证据；公开资料说只有夫妻能进 | 一期不开放访客 |
| 配偶离线能否进入或操作 | 租约不要求在线 | 允许 |
| 每日浇水 2 次是合计还是各自 | 70 版 DB 把次数存在树那一行上 | 夫妻合计 2 次 |
| 收获奖励各抽一次还是共享一次 | PVF 写"夫妻二人皆可获得" | 各抽一次，各发一封邮件 |
| 树到期后 | PVF 未声明 | 保留枯树，清理后才能再种 |
| 仓库容量 | A21 客户端固定 40 格；国服可扩到 120 | 40 格，不做扩容 |
| 离婚后共享资产归属 | 离婚尚未实现 | 离婚功能开发前必须先定 |

## 9. 风险

完整清单见[服务端子报告第 7 节](outputs/marriage-housing-20261018/server/server.md#7-风险清单)。最需要关注的五项：

1. **现存越权**：任何人都能用 `0x0024` 进 town 101，H0 应该先修。
2. **字段语义未闭合的协议不要发。** 树快照的 i8、`03DA` 的 op=1/2、`03DD` 的两个 u8 还没确定，长度或计数发错会让客户端解析错位甚至断线。没经实机确认的 NOTI 先不发，或只发已经验证过的形状。
3. **心意点被启动对账复原**（5.6）。
4. **实例维度改动面大**，会波及区域广播和队伍目录，需要独立提交和判据。
5. **同帧重放白名单**：漏改会在第一次重放时断线。
