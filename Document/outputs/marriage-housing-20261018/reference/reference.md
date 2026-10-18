# 婚房/家具/情侣仓库/情侣树/成长胶囊/戒指升级：参考实现调查

日期：2026-10-18
性质：只读调查；未改任何生产源码、`protocol/`、数据库、PVF、客户端
当前仓基线：`DNF-JP-86-Server` `task/next-round-20261013` @ `61beac4036eaad20a92f7c74483bf097764eb543`
前序报告：[婚姻一期参考调查](../../marriage-research-20261016/reference/reference.md)（本报告不重复其一期结论，只补婚房/树/仓库/胶囊/戒指）

## 0. 证据基线

| 标签 | 对象 | 基线 |
|---|---|---|
| `UpstreamReference` | `E:\Project\DNF\ServerS4A21` | `c20dba5fa52c2faf4b5087a3b477676be8434e8f`（工作区仅有未跟踪 `_reference-backup-20260826/`，已排除源码扫描） |
| `PVF` | `artifacts/sao-ticket-20261014/Script.server.pvf` | 经 `Tool/PvfProbe` 读取；本报告 dump 存于 [evidence/](evidence/) |
| `ClientStatic` | 前序 A21 静态取证 | [a21.md](../../marriage-research-20261016/a21/a21.md) §7：`03D8` 为 `u32 count + count×(u16+u32) + u32 + u32 + u32 + i8` |

置信度：`Confirmed` 为源码/工件直接可见；`High` 为多处一致证据推出；`Hypothesis` 为待实帧/客户端验证的推测。

## 1. ServerS4A21（A21 上游）

### 1.1 opcode 名表（仅枚举）

**[UpstreamReference / Confirmed]** `Server/DfoServer/Network/Core/PacketTypesA21.cs`：

| 方向 | opcode | 名称 | 行号 |
|---|---|---|---|
| NOTI | `0x03D1` | `WEDDING_UPGRADE_RING` | 984 |
| NOTI | `0x03D3` | `CHARAC_LOVE_POINT` | 986 |
| NOTI | `0x03D8` | `COUPLE_ROOM` | 991 |
| NOTI | `0x03D9` | `COUPLE_INVEN` | 992 |
| NOTI | `0x03DA` | `COUPLE_ROOM_UPDATE` | 993 |
| NOTI | `0x03DB` | `COUPLE_ROOM_REFRESH` | 994 |
| NOTI | `0x03DC` | `COUPLE_TREE` | 995 |
| NOTI | `0x03DD` | `COUPLE_SIMPLE_INFO` | 996 |
| NOTI | `0x03DE` | `COUPLE_TREE_HARVEST_REWARD` | 997 |
| NOTI | `0x03DF` | `COUPLE_GROWTH_CAPSULE` | 998 |
| NOTI | `0x03E0` | `COUPLE_GROWTH_CAPSULE_GAGE` | 999 |
| CMD | `0x03F4` | `WEDDING_UPGRADE_RING` | 2243 |
| CMD | `0x03F5` | `WEDDING_USE_LOVE_POINT_ITEM` | 2244 |
| CMD | `0x03FB/0x03FC` | `CHECK_MOVE_TO_PARTNER` / `MOVE_TO_PARTNER` | 2250-2251 |
| CMD | `0x03FD` | `COUPLE_INVEN_OPEN_CLOSE` | 2252 |
| CMD | `0x03FE` | `COUPLE_ROOM_MOVE_ITEM` | 2253 |
| CMD | `0x03FF` | `COUPLE_INVEN_DELETE_ITEM` | 2254 |
| CMD | `0x0400` | `COUPLE_TREE_PLANT` | 2255 |
| CMD | `0x0401` | `COUPLE_TREE_WATER` | 2256 |
| CMD | `0x0402` | `COUPLE_TREE_HARVEST` | 2257 |
| CMD | `0x0403` | `COUPLE_TREE_REMOVE` | 2258 |
| CMD | `0x0404` | `MAKE_COUPLE_GROWTHCAPSULE` | 2259 |
| CMD | `0x0405` | `GIVE_COUPLE_GROWTHCAPSULE` | 2260 |
| CMD | `0x0406` | `TAKE_COUPLE_GROWTHCAPSULE` | 2261 |

同表还有一套**非婚姻**的个人房屋（Housing）树系统：NOTI `0x0349-0x0352`（`HOUSING_INVITE_FRIEND`…`HOUSING_TREE_INFO 0x0351`、`HOUSING_WATER_HISTORY 0x0352`，行 848-857）与 CMD `0x0365-0x036A`（`HOUSING_TREE_INFO/SET_NEW_TREE/GIVE_WATER/HARVEST_TREE/WATER_HISTORY/RESET_TREE`，行 2100-2105）、`0x0383-0x0387`（行 2130-2134）。**[High]** 两套树系统并存，不能把 `HOUSING_*` 与 `COUPLE_TREE*` 混用。

### 1.2 实现程度

**[UpstreamReference / Confirmed]** 除 `PacketTypes.cs`/`PacketTypesA21.cs` 外，全源码（排除 `bin/obj/_reference-backup`）对上表任一 opcode（`0x03D9-0x03E0`、`0x03F4/03F5`、`0x03FD-0x0406`、`0x0365-0x036A`）零引用；`couple|housing|love_point|upgrade_ring` 关键词命中仅 6 个文件，全部属于婚礼三包回放：

- `Network/Builders/Init/WeddingReplayBodyBuilders.cs:44-53` `CoupleRoomBodyBuilder`：113 字节常量；
- `Network/Builders/Init/InitPacketBuilderRegistry.cs:113,120-122`：`0x03D8` 第 1 次 occurrence 改用该常量；
- `Game/SelectCharacter/NewCharacterInitSequence.cs:106-109`：选角尾部 `WEDDING_INFO → WEDDING_CHARAC → COUPLE_ROOM(occurrence 1)`；
- `Network/Handlers/InventoryRefreshSender.cs:148-170` `SendWeddingReplayRefresh`，被 `TownHandler.cs:1296`、`Dungeon/DungeonEntryHandler.cs:811`、`Dungeon/DungeonTownReturnCoordinator.cs:145`、`Pets/PetInventoryMoveCoordinator.cs:54` 调用。

`InventoryRefreshSender.cs:150-152` 注释称“左下角图标由 `WEDDING_INFO` 驱动，但面板结婚属性是客户端按 `COUPLE_ROOM` 的婚家家具列表计算的，只发前两个会导致图标在、属性丢”；commit `fdc2907`（2026-08-27）声称“实机验证换宠物/进本/回城后属性图标名誉均保留”，代价是“补发 COUPLE_ROOM 会让客户端结婚房间动画重播一次”。**[UpstreamReference / High]** 这是上游作者的实机观察，未附抓包；它是“家具列表 → 面板属性由客户端计算”的唯一旁证（见 §2.3）。

- **[Confirmed]** 113B 常量最早由 commit `c10f80c`（2026-08-21，“修复收集箱”）加入；`git log -S` 未找到更早来源，也未附原始抓包。仓库内唯一出现处是本服务端自己的发送日志 `Server/DfoServer/bin/Debug/packet_log.txt:192,428`（`SEND ... type=0x03D8 len=128 body=(113B)`），不是外部来源证据。
- **[Confirmed]** 婚房、仓库、树、情侣胶囊、戒指升级：**无** handler、无状态、无表。`Sqlite/item_schema.sql` 及备份 `inventory.db` 的 71 张表中无 couple/wedding/marriage/tree 表。
- **[Confirmed / 易混淆]** 上游存在真实的**账号级**成长胶囊（CMD `GET_GROWTHCAPSULE 0x01F1`，`Network/Handlers/GrowthCapsuleHandler.cs`、`Game/Accounts/GrowthCapsuleClaimService.cs`、`accounts.growth_capsule_exp` 列），它与 `COUPLE_GROWTH_CAPSULE 0x03DF/0x0404-0x0406` 是不同系统，不能当情侣胶囊实现借用，只能借其“经验累计 → 领取道具”的事务形状。

## 2. 113 字节 `COUPLE_ROOM` 常量解码

脚本：[scripts/decode_couple_room_113.py](scripts/decode_couple_room_113.py)；输出：[evidence/couple-room-113-decode.txt](evidence/couple-room-113-decode.txt)。PVF 定义：[evidence/pvf-decoration_coupleroomiteminfo.etc.txt](evidence/pvf-decoration_coupleroomiteminfo.etc.txt)。

**[Confirmed]** `113 = 4 + 16×6 + 13`，与 A21 parser 形状 `17 + 6n` 完全吻合，`count = 16`。

### 2.1 16 个条目（`u16 slot + u32 itemId`）

| # | slot | itemId | PVF 名称（stackable `[couple room item]`） | 心意点 `10002606` 成本 | coupleroomiteminfo 允许的 position |
|---|---|---|---|---|---|
| 0 | 0 | 10002798 | 金色雕纹地板 | 100 | 0 |
| 1 | 1 | 10002801 | 典雅金丝壁纸 | 100 | 1 |
| 2 | 25 | 10002802 | 金色窗户 - 樱花背景 | 100 | 25/26/27 |
| 3 | 20 | 10002725 | 暖冬地毯 | 200 | 19/20/21 |
| 4 | 13 | 10002693 | 寝具 - 清风雅韵 | 200 | 10/12/13 |
| 5 | 7 | 10002715 | 爱恋之心房门吊饰 | 15 | 6/7/8/9 |
| 6 | 4 | 10002698 | 普通吊灯 | 10 | 2/4/5 |
| 7 | 3 | 10002780 | 窗帘 - 淡雅 (粉色) | 100 | 3/14/15 |
| 8 | 19 | 10002692 | 万圣节南瓜装饰 | 50 | 19/20/21 |
| 9 | 9 | 10002717 | 罗莉安的装饰画像 | 50 | 6/7/8/9 |
| 10 | 16 | 10002703 | 烈焰彼诺修的雕像 | 100 | 16-23 |
| 11 | 22 | 10002704 | 冰霜克拉赫的雕像 | 100 | 16-23 |
| 12 | 6 | 10002786 | 骨狱息的巨幅画像 | 30 | 6/7/8/9 |
| 13 | 10 | 10002789 | 休闲的牛头人偶 | 15 | 10/12/13 |
| 14 | 21 | 10002692 | 万圣节南瓜装饰（第二件） | 50 | 19/20/21 |
| 15 | 6 | 10002721 | 光之城主的蓝灵火挂饰 | 100 | 6/7 |

- **[PVF / Confirmed]** 16 个 ID 全部解析到 `stackable/event/chn_coupleroom_decoration/*.stk`，`[stackable type] [couple room item] 0`，且全部出现在 `decoration/coupleroomiteminfo.etc` 的 `[coupleroom item list]`。
- **[High]** `u16` 是 `decoration/coupleroomiteminfo.etc` `[position list]`（28 个 position，`idx x y z`）中的 **position index**：16 个条目的 slot 全部落在该物品 `[coupleroom item]` 行声明的候选 position 集合内（逐条核对见上表），没有一条越界。
- **[Confirmed / 异常]** slot 6 被 #12 与 #15 两件不同物品同时占用。说明要么客户端不校验 position 唯一，要么样本本身经人工拼接/非真实状态。服务端实现应**自行强制 position 唯一**，不可依赖客户端拒绝。
- **[Hypothesis]** `[coupleroom item]` 行格式为 `itemId N × (position dx dy ? flip) 0 actPath`；`dx/dy` 是相对 position 的偏移，`flip` 为 0/1。
- 地板（position 0）与壁纸（position 1）是“必占”的基础层；样本的 16 件构成一间完整装修房。

### 2.2 13 字节 tail（`u32 + u32 + u32 + i8`）

原始：`03000000 EE300569 1E000000 01`

| 字段 | 值 | 解释 | 置信度 |
|---|---|---|---|
| u32 | 3 | 与 `decoration/coupletree.etc` `[tree] [index] 3`（黄宝石树）一致；也可能是种子序号/其他计数 | Hypothesis |
| u32 | `0x690530EE` = 1761947886 | 按 Unix 秒为 **2025-10-31 21:58:06 UTC**，像真实抓包时间戳（种植时间/最近浇水时间/婚房创建时间之一） | High（是时间戳）/ Hypothesis（具体含义） |
| u32 | 30 | 与 `coupletree.etc` `[step] 10/20/30`（30 = 满成长）及 `[expire day] 30` 同值 | Hypothesis |
| i8 | 1 | 状态/当日浇水次数（`[day water count] 2`）/可收获标志之一 | Hypothesis |

**[High]** tail 很可能就是婚房中“闲置的花圃”(`npc/chn_coupleroom_tree.npc`，`[role] [couple room tree]`）的树状态快照：一棵 index 3、成长值 30（满）的树。但树另有独立 NOTI `COUPLE_TREE 0x03DC`，tail 也可能只是房间级字段。需 A21 客户端对 tail 4 个字段的消费点交叉确认（属于并行 A21 静态子任务的范围）。

### 2.3 与婚房 buff 的对照

`decoration/coupleroomiteminfo.etc` `[couple room buff]` 给出 7 档阈值：`10/40/100/200/500/800/1000`，最高档 1000 为 `hp max 200, mp max 200, village moving speed 10, all elemental attack 8, equipment physical/magical attack 10, separate attack 20`。

- **[Confirmed]** 样本 16 件家具的心意点成本合计 **1320**（重复南瓜计两次），件数为 16。
- **[Hypothesis]** 若阈值按“已摆放家具的心意点总和”计，样本命中最高档 1000——与上游“面板结婚属性由家具列表计算”的实机描述一致；若按件数计则只到 10 档。客户端按哪个量计档未证实，需静态或实机验证。
  加法：100+100+100+200+200+15+10+100+50+50+100+100+30+15+50+100 = 1320（16 项）。

### 2.4 同一 PVF 中与房/树/胶囊相关的静态规则（供解码与设计对照）

证据：[evidence/](evidence/) 下 `pvf-*.txt`。这里只列与本报告结论直接相关者；PVF 专项分析不在本子任务范围。

- **[PVF / Confirmed]** 婚房地图 `map/coupleroom/coupleroom.map`：`[player number] 2 8`，背景音 `M_WEDDING_ROOM`，放置 NPC 20012/20011；花圃 NPC `npc/chn_coupleroom_tree.npc`（`[role] [couple room tree]`，名“闲置的花圃”）。
- **[PVF / Confirmed]** 家具成本统一以材料 `10002606 心意点`（`[material]`，叠加上限 999）计，`[need material] 10002606 N`。
- **[PVF / Confirmed]** 树 `decoration/coupletree.etc`：`[seed] 10002766 1 10002768 2 10002767 3`（种子 → tree index）；3 棵树 `[expire day] 30`、`[day water count] 2`、`[grow point] 10002765 10 10002905 20`（普通/强力洒水壶各 +10/+20）、`[step] 10/20/30` 三阶段动画、`[reward letter] 1078 1192`。`[reward]` 为 6 个三元组：`10002609 40000 2 / 10002901 40000 2 / 10002758 10000 1 / 10002609 5000 3 / 10002901 4000 5 / 10002609 1000 5`。中间数加和 40000+40000+10000+5000+4000+1000 = 100000（6 项），**[High]** 应为 `(itemId, 十万分比权重, 数量)` 的单抽奖池（物品：10002609 爱之摩伽陀邀请函、10002901 心意通、10002758 同心喇叭）。
- **[PVF / Confirmed]** 胶囊 `etc/couplegrowthcapsule.etc`：`[max own count] 10`、`[max taken count] 10`、`[gage] 2`、`[max gage] 10`、`[capsule id] 10002910`（“爱情成长胶囊”，使用得 30000 经验）。**[Hypothesis]** 计量条每次 +2、满 10 可做一颗，持有/被领取各上限 10；具体累计事件需客户端或实帧确认。

## 3. 旧单体 `E:\Project\86JP`

基线：`25e6f0af600b32e550a9126fff5758b602f42ce4`（前序报告记其落后 remote 231 commit，本次未拉取）。

- **[UpstreamReference / Confirmed]** 仅 `Server/DfoServer/Network/Core/PacketTypes.cs` 有 JP86 版本号的同名枚举：NOTI `WEDDING_UPGRADE_RING 0x0367`(行 878)、`CHARAC_LOVE_POINT 0x0369`(880)、`COUPLE_ROOM 0x036E`…`COUPLE_GROWTH_CAPSULE_GAGE 0x0376`(885-893)；CMD `WEDDING_UPGRADE_RING 0x0397`(1967)、`COUPLE_INVEN_OPEN_CLOSE 0x03A0`…`TAKE_COUPLE_GROWTHCAPSULE 0x03A9`(1976-1985)；`HOUSING_TREE_INFO` NOTI `0x02E7`(750) / CMD `0x0308`(1824)。
- **[Confirmed]** 全仓其余命中只有商城婚戒商品（`Game/Shop/CeraShopProductCatalog.cs`、`SelfTests/CeraShopSelfTest.cs`），无婚房/仓库/树/胶囊/戒指升级实现；成长胶囊同 S4A21 一样只有账号级 `GrowthCapsule*`。
- **[High / 风险提示]** JP86 与 A21 同名消息 opcode 不同：NOTI 段差 0x6A（`COUPLE_ROOM` 0x036E vs 0x03D8），CMD 段差 0x5D（`COUPLE_INVEN_OPEN_CLOSE` 0x03A0 vs 0x03FD）。不得把 86JP 的 opcode 或旧 `0x036E/0x036F` 语义带到 A21。

## 4. 其它本地资料

| 位置 | 结果 | 置信度 |
|---|---|---|
| `E:\Project\DNF\Research\Document`、`E:\Project\DNF\S4A21ClientPatch` | `couple/coupleroom/婚房/情侣/结婚/wedding/marriage` 零命中 | Confirmed |
| `E:\Project\DNF\Document`（23 GB，限 `*.md/txt/json/py/cs/sql`） | 仅 `outputs/*/live-state.json` 中的副本名与打包清单命中，无婚房实现资料 | Confirmed |
| `E:\Project\DNF\handoff.md` | 零命中 | Confirmed |
| `Research/1995chen-dnf/plugin/{70s2_dp_enhance,dp2}`（解包到 scratchpad） | 只在 frida 符号表 `lua/df/game/symbols*.lua:2914-2917` 复述 `_GLOBAL__I_Arad_Housing*.cpp` 四个静态初始化地址，无业务 hook | Confirmed |
| `Research/tw70-pvf-archive/Script.2013*.pvf` | `Dfo.Pvf` 报 `Invalid PVF signature 0xAAE745EF`，未能读取 | — |

## 5. 70 版 `df_game_r` 与其数据库模板

### 5.1 二进制

脚本：[scripts/scan_df_game_r.py](scripts/scan_df_game_r.py)、[scripts/elf_file_locals.py](scripts/elf_file_locals.py)（pyelftools 0.33）；输出：[evidence/70-df_game_r-scan.txt](evidence/70-df_game_r-scan.txt)、[evidence/70-housing-symbols.txt](evidence/70-housing-symbols.txt)（均已过滤到相关行）。
目标：`Research/1995chen-dnf/other/等级补丁/70/df_game_r`，SHA-256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`，`.symtab` 352,694 项（前序报告 119,333 为函数符号口径）。同目录 50…99 共 12 份 df_game_r 大小相同（36,360,954 B）而哈希各异，属等级补丁变体，本次只扫 70。

- **[70Reference / Confirmed] 无婚姻/情侣代码。** 符号与字符串对 `couple|wedding|marri|spouse|lover` 零命中；字节搜索 `wedding/couple/Couple/Wedding` 计数为 0。70 的 opcode 名表也没有任何 `WEDDING_*`/`COUPLE_*`——情侣系统晚于此版本。
- **[70Reference / Confirmed] 个人房屋（Housing）系统只剩空壳。** 存在 `Arad_Decoration.cpp`、`Arad_Housing.cpp`、`Arad_HousingConvertor.cpp`、`Arad_HousingManager.cpp`、`Arad_HousingTree.cpp` 5 个翻译单元（另有 `GrowthCapsuleEvent.cpp`），但每个 TU 只有 3 个本地函数：头文件带来的 `comp_by_time`(20B)、`__static_initialization_and_destruction_0`(64B)、`_GLOBAL__I_<file>`(28B)。**没有任何 Housing/Tree/Decoration 类方法、Dispatcher 或 DB 类**；全局符号仅 `CUser::sendDecorationObjectCnt(short)`（0x08690d9a，156B）与账号胶囊 `ImportGrowthCapsuleScript`/`stGrowthCapsuleScript`。
- **[70Reference / Confirmed]** opcode 名字符串保留整套 Housing/Decoration：`ENUM_NOTIPACKET_DECORATION_INVENTORY_INFO`、`…_DECORATION_ROOM_INFO`、`…_HOUSING_{INVITE_FRIEND,INVITE_TIMEOUT,ROOM_USERS,LEAVE_USER,LEAVE_OWNER,SEND_MESSAGE,TREE_INFO,WATER_HISTORY}`，`ENUM_CMDPACKET_HOUSING_{TREE_INFO,SET_NEW_TREE,GIVE_WATER,HARVEST_TREE,WATER_HISTORY,RESET_TREE,INVITE_FRIEND,VISIT_ROOM,ENTER_ROOM,LEAVE_ROOM,SEND_MESSAGE}`、`ENUM_CMDPACKET_DECORATION_{USE_STACKABLE,SETUP,MOVE_INVENSLOT}`（文件偏移 `0x00aa3058-0x00aa87fc`）。
- **[70Reference / Confirmed]** 不存在操作 housing 表的 SQL：`charac_housing`、`decoration_inven`、`leaf_point`、`day_water_count`、`tree_id`、`water_history` 字节计数均为 0。
- **[High]** 70 二进制**不提供可用的婚房/情侣树/胶囊状态机**，个人 Housing 实现也被编译剔除；只能借其 opcode 命名佐证 A21 里“个人 Housing 树”与“情侣树”两套系统并存。

### 5.2 数据库模板（唯一有价值的状态模型）

来源：`Research/1995chen-dnf/build/dnf_data/home/template/init/init_sql.tgz`（SHA-256 `3A3D16C7981D91002835F0BA9BC89E9657B9147C5D2BC1CE347871266130609A`，解包到 scratchpad，21 个 `.sql`）。提取脚本：[scripts/extract_70_db_schema.py](scripts/extract_70_db_schema.py)；输出：[evidence/70-db-schema-housing.txt](evidence/70-db-schema-housing.txt)。

- **[70Reference / Confirmed] 21 个库均无 couple/marriage/wedding/spouse/lover 表或列**（含 `taiwan_cain`、`taiwan_cain_2nd`、`taiwan_game_event`、`taiwan_se_event`）。唯一 `wedding_memo`（`taiwan_main_web.sql:8330`，MyISAM：`no, guild_id, m_id, charac_no, charac_name, nick_name, memo varchar(100), create_time`）是官网公会留言板，与游戏内婚姻无关。
- **[70Reference / Confirmed] 个人 Housing 表**（`taiwan_cain.sql`，InnoDB，主键均为 `charac_no`，即一角色一房一树）：

```sql
-- taiwan_cain.sql:983
CREATE TABLE charac_housing_info (
  charac_no        int(10) unsigned NOT NULL default 0,
  installed        smallint(5) unsigned NOT NULL default 0,
  decoration_inven binary(144) NOT NULL default <144 x NUL>,
  version          smallint(5) unsigned NOT NULL default 0,
  PRIMARY KEY (charac_no));
-- taiwan_cain.sql:1001
CREATE TABLE charac_housing_tree_info (
  charac_no       int(10) unsigned NOT NULL default 0,
  tree_id         int(10) unsigned NOT NULL default 0,
  expire_date     datetime NOT NULL,
  current_point   smallint(6) NOT NULL default 0,
  leaf_point      smallint(6) NOT NULL default 0,
  day_water_count smallint(6) NOT NULL default 0,
  PRIMARY KEY (charac_no));
-- taiwan_cain.sql:1021
CREATE TABLE charac_housing_water_history (
  charac_no        int(10) unsigned NOT NULL default 0,
  give_time        timestamp NOT NULL default CURRENT_TIMESTAMP,
  give_charac_name varchar(20) NOT NULL default '',
  PRIMARY KEY (charac_no, give_time));
```

- **[70Reference / Confirmed] GM 审计表**（`taiwan_mng_manager.sql:170,196`，MyISAM）：`charac_housing_mod`（`installed_old/new`、`decoration_inven_old/new blob`）与 `charac_housing_tree_mod`（`tree_id/expire_date/current_point/leaf_point/day_water_count` 各 `_old/_new`），外加 `MNG_user_id, work_time, work_type enum('-','add','mod','del'), memo`。
- 推断的状态模型：
  - **[High]** 树是单行可变状态：`tree_id`（对应 PVF `[tree] [index]`）、`expire_date`（种植时 + `[expire day]`）、`current_point`（成长值，对应 `[grow point]`/`[step]`）、`day_water_count`（当日已浇次数，对应 `[day water count]`）。**每日计数直接存在行上**，而非由历史表推算。
  - **[Hypothesis]** `leaf_point` 可能对应 bad grow 状态（`coupletreeiteminfo.etc` 的 `[tree bad grow state] 1/2/3`）或第二成长量，无证据定论。
  - **[High]** 浇水历史独立成表，按 `(owner, give_time)` 记录**浇水者名字**，说明他人可帮浇并需回显历史（对应 `HOUSING_WATER_HISTORY`）。情侣版的帮浇者天然是配偶。
  - **[Hypothesis]** `decoration_inven binary(144)` 恰为 24×6 B，与 A21 `COUPLE_ROOM` 的 6 B 条目（`u16 position + u32 itemId`）同宽，可能是已摆放家具定长数组的落盘形态；`installed` 可能是已摆放件数或开通标志。个人 Housing 与情侣房同构未证实。
  - **[High]** 官方做法是把房/树作为**带 `version` 的小聚合 + GM 变更审计**，而非逐件家具一行。

## 6. 汇总：各参考的实现程度

| 功能 | ServerS4A21 | 86JP | 70 df_game_r | 70 DB 模板 | PVF |
|---|---|---|---|---|---|
| 婚房家具（`03D8/03DA/03DB/03FE`） | opcode 名 + 113B 常量回放（选角与 4 个刷新点） | opcode 名（JP86 号） | 无 | 无情侣表；个人 `charac_housing_info` | 位置表、家具表、buff 阶梯 |
| 情侣仓库（`03D9/03FD/03FF`） | opcode 名 | opcode 名 | 无 | 无 | 未见独立配置 |
| 情侣树（`03DC/03DE/0400-0403`） | opcode 名；113B tail 疑似树快照 | opcode 名 | Housing 树 TU 空壳 | 个人树表 + 浇水历史（可借鉴） | `coupletree.etc` 全套参数 |
| 成长胶囊（`03DF/03E0/0404-0406`） | opcode 名；账号级胶囊真实实现（不同系统） | 同左 | 仅账号胶囊脚本导入 | 无 | `couplegrowthcapsule.etc` |
| 戒指升级（`03D1/03F4`） | opcode 名 | opcode 名 | 无 | 无 | 见前序 [pvf.md](../../marriage-research-20261016/pvf/pvf.md) 戒指一节 |

**[Confirmed] 没有任何参考提供婚房/仓库/树/胶囊/戒指升级的真实服务端逻辑或情侣持久化表。**

## 7. 对本项目实现的启示

可直接借鉴：

1. **[High] 113B 常量作 `03D8` 非空编码的测试向量**：形状 `17 + 6n` 与 16 个条目的 PVF 合法性都已闭合，可用于 encoder 往返测试和单客户端“装满房”UI 冒烟。生产发送必须按关系生成，不能回放（它不属于任何真实关系，且含 slot 6 冲突）。
2. **[High] 家具摆放的服务端校验可完全由 PVF 推出**：itemId 必须在 `[coupleroom item list]`；position 必须在该物品声明的候选集内；position 唯一由服务端强制；成本按 `[need material] 10002606 N` 扣心意点材料。
3. **[High] 树/胶囊参数全部取 PVF**：种子→index、30 天过期、每日 2 次浇水、壶 +10/+20、10/20/30 阶段、十万分比奖池、胶囊 gage 2 / max 10 / 持有 10 / 领取 10。
4. **[Proposal / High] 持久化形状借 70 Housing 表，但改为以婚姻关系为主键**（情侣共享）：
   - `couple_room(relation_id PK, version)` + `couple_room_item(relation_id, position, item_id, UNIQUE(relation_id, position))`（逐行便于条件更新与审计；也可沿用定长 blob）；
   - `couple_tree(relation_id PK, tree_index, planted_at, expire_at, grow_point, day_water_count, water_period, version)`，`day_water_count` 按 `IDailyResetRuleService` 周期归零；
   - `couple_tree_water_history(relation_id, at, character_id, character_name)`；
   - `couple_capsule(relation_id PK, gage, own_count, taken_count, version)`；
   - 戒指等级挂在关系上（前序 PVF 结论：一方升级，双方同步）；
   - 参照 70 `*_mod` 表保留 old/new 审计，供 GM 修复。
   需等 A21 字段语义与实帧确认后定稿。
5. **[High] 刷新时机提示**：S4A21 commit `fdc2907` 记录，USERINFO subtype0 重建（换宠、进本、回城）会让客户端重置婚姻 UI/属性，需按选角相对顺序补发 `03D0 → 03F3 → 03D8`。本仓实现婚房后应在相同刷新点重发真实 `03D8`，并接受“婚房动画重播”的已知副作用。这是上游的实机描述，不是本仓 ClientVerified。

只能当 UI 回放样本 / 不可依赖：

- 113B 的 13B tail（树/房间字段含义未定），以及 `WEDDING_INFO` 15B、`WEDDING_CHARAC` 31 个零：不得据此推定字段语义。
- S4A21/86JP 的账号级 `GrowthCapsule*`：不是情侣胶囊。
- 70 二进制的 Housing opcode 名：只证明系统存在过，无行为可抄。
- 86JP 的 JP86 opcode 号：对 A21 无效。

参考无法提供、仍需另行取得：`03D9/03DA/03DB/03DC/03DD/03DE/03DF/03E0` 的 A21 消费结构与 `03FD-0406` 请求体（属并行 A21 静态子任务）；家具 buff 按心意点总和还是件数计档；tree tail 四字段含义；情侣仓库容量与可放物品规则；各操作的正常实帧。

