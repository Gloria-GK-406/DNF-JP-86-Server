# 婚房 / 家具 / 情侣树 / 成长胶囊 / 情侣仓库 / 戒指升级：PVF 静态内容调查

> 范围：只读 PVF（ContentStatic）。不涉及客户端 UI 实际触发、协议字段实测或服务端实现。
> 置信度：**Confirmed** = PVF 原文直接写明；**High** = 多处 PVF 证据互相印证的结构推断；**Hypothesis** = 合理但需实机/逆向确认。
> 日期：2026-10-18（会话中断后恢复重做，全部证据重新生成）。

## 0. 证据身份

| 项 | 值 |
|---|---|
| 服务端 PVF | `artifacts/sao-ticket-20261014/Script.server.pvf`，SHA-256 `91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4` |
| 客户端 PVF | `E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，SHA-256 `573388731C394A71D5E75ED7DD215124B3D04383631D93116625ED353E06ACAB` |
| 扫描器 | [scanner/Program.cs](scanner/Program.cs)（命令：`lst/items/dump/text/hash/files/scan/tagwhere/numwhere`），用法见第 10 节 |
| 文件表规模 | 712,010 个文件；全表关键词/ID 扫描摘录见 [housing-scan.tsv](housing-scan.tsv) |

## 1. 结论速览

| # | 结论 | 置信度 | 主要证据 |
|---|---|---|---|
| 1 | 婚房 = town `101` `CoupleRoom.twn`，单 area 0，地图 `CoupleRoom/CoupleRoom.map`，area 类型标签 `[couple room] 474 234`（与所有赛丽亚房间的 `[gate] 474 234` 同形） | Confirmed（数据）/ High（474 234 = 进入落点） | `town/coupleroom.twn`、`town/*.twn` |
| 2 | 婚房内只有两个 NPC：`20012` 闲置的花圃（情侣树）@(799,382)、`20011` 云清（婚房商店/赠礼）@(954,390) | Confirmed | `map/coupleroom/coupleroom.map` |
| 3 | 入口：每个城镇当前生效的 `(r)gate_*.map`（赛丽亚房间）都有 `[town movable area] 46 212 34 52 101 0` → town 101 area 0，并经 `[import script] Common/(R)Gate_Seria.map` 放置门对象 `54170 couple_room_gate.obj` @(64,175)；`(f)` 变体没有此入口 | Confirmed（数据）/ High（`(r)` 为 A21 生效变体） | `map/town/underfoot/(r)gate_underfoot.map` 等 55 处 |
| 4 | 出口：婚房地图 `[town movable area] 1001 194 84 81 -101 0`，目标 town 为负数 `-101` | Confirmed（数据）/ Hypothesis（负数=返回来源城镇） | `map/coupleroom/coupleroom.map` |
| 5 | 婚房装饰物 = 76 个 `[stackable type] [couple room item]` 物品（10002684..10002802），其中 75 个在 `decoration/coupleroomiteminfo.etc` 有摆放定义，28 个摆放槽位（0..27） | Confirmed | `decoration/coupleroomiteminfo.etc`、`stackable/event/chn_coupleroom_decoration/*` |
| 6 | 装饰物"价格"是 `[need material] 10002606 N`，`10002606` 名为「心意点」= 婚姻爱心点（上限 999）的物品化表示；在云清 `chn_mago.shp`（4 个页签：地板/壁纸/窗户/装饰物）兑换 | Confirmed（UI 文本"心意点可用于在NPC云清处兑换婚房装饰物或婚戒升级券"） | `ui/chn_wedding/coupleinfowindow.xui`、`itemshop/chn_mago.shp` |
| 7 | 婚房 Buff 随"华丽程度"分 7 档（阈值 10/40/100/200/500/800/1000），决斗场无效 | Confirmed（表与文本）/ High（华丽度 = 已摆放装饰物心意点价值之和） | `[couple room buff]`、UI 文本 |
| 8 | 情侣树：3 种种子 → 3 种树；30 天到期；每日浇水 2 次；普通洒水壶 +10、强力洒水壶 +20；成长阶段 10/20/30；收获奖励 6 条权重合计 100000 | Confirmed | `decoration/coupletree.etc` |
| 9 | 成长胶囊：`etc/couplegrowthcapsule.etc` = 持有上限 10、领取上限 10、每次 gage 2、满 gage 10、产物 `10002910` 爱情成长胶囊（+30000 EXP，账号绑定，Lv30–84） | Confirmed（数值）/ Hypothesis（gage 增长事件） | `etc/couplegrowthcapsule.etc` |
| 10 | **没有任何"情侣仓库/婚房保管箱"专用配置**：无 tag、无 etc、无物品、无 NPC role；婚房地图也不引用 `map/coupleroom/animation/safe.ani` | Confirmed（PVF 内不存在） | 全表 tag/pool 扫描 |
| 11 | 戒指 Lv0..5 Buff 与升级券已在婚姻主表与券物品声明 | Confirmed | `etc/weddingsystem.etc` |
| 12 | 上述 144 个资源在服务端与客户端 PVF 中规范化解码 SHA-256 全部一致 | Confirmed | [pvf-resource-equivalence.tsv](pvf-resource-equivalence.tsv) |

---

## 2. 婚房 town / map（问题 1）

### 2.1 `town/coupleroom.twn`（town.lst 中 `101 -> CoupleRoom.twn`）

```text
[dungeon what must be cleared] -1
[area]
0 CoupleRoom/CoupleRoom.map
[permission] [need level] 1 [/permission]
[couple room] 474 234
[/area]
[name] 婚房
```

- **Confirmed**：只有 area 0；进入等级门 1；无前置地下城。
- `town/coupleroom1.twn` 内容与之逐字相同，但**不在** `town/town.lst`（未被选入，孤立文件）。Confirmed。
- **`[couple room] 474 234` 的含义（High）**：当前 `town.lst` 中所有城镇的赛丽亚房间 area 都写作 `` `[gate]` 474 234 ``（见 [towns-listed.txt](evidence/towns-listed.txt)），两者形状完全一致（kind tag + 两个整数，且数值相同）。因此 `[couple room]` 是 **area 类型标签**（与 `[gate]`/`[normal]`/`[dungeon gate]` 同一位置），`474 234` 是该私有房间的**进入落点坐标 (x,y)**。服务端 `PvfTownDefinitionSource.TryParseKind` 目前把它归为 `Unrecognized`；`ITownMovementDomainService` 注释已指出 `[gate]` 是"每角色私有、不共享 town presence"的房间，`[couple room]` 应按"每对夫妻私有"处理（Hypothesis，需 A21 确认是否夫妻共享同一实例）。
- **没有家具网格/槽位定义在 twn 或 map 中**；槽位定义在 `decoration/coupleroomiteminfo.etc`（第 3 节）。Confirmed。

### 2.2 `map/coupleroom/coupleroom.map`（全文见 [core-files.txt](evidence/core-files.txt)）

| 字段 | 值 | 说明 |
|---|---|---|
| `[background correction]` | -80 | |
| `[player number]` | 2 8 | 与赛丽亚 gate 地图相同 |
| `[tile]` | `chn_coupleroomtile00..04.til` | `map/coupleroom/tile/viptileex01.til` 存在但未被引用 |
| `[town movable area]` | `1001 194 84 81 -101 0` | 出口矩形 (x=1001,y=194,w=84,h=81) → town `-101` area 0 |
| `[virtual movable area]` | 11 个四元组 | 房间可走区域 |
| `[sound]` | `M_WEDDING_ROOM` | |
| `[NPC]` | `20012 [left] 799 382 0`、`20011 [left] 954 390 0` | 花圃与云清 |
| `[map name]` | `PVP无名` | 模板残留 |

- 无 `[passive object]`、无 `[animation]`：`map/coupleroom/animation/` 下的 `safe.ani`、`jinjya*.ani`、`xtree*.ani`、`vipent*.ani`、`christmas2011_*.ani` 等**全部未被婚房地图引用**（Confirmed：字符串池中没有指向它们的路径），推测是从赛丽亚房间/VIP 房间模板复制来的残留资源（Hypothesis）。
- 出口目标 `-101`（Hypothesis）：赛丽亚 gate 地图同一 tag 中公会大厅入口写作 `-2 -2`，可见负数是"特殊/动态目标"编码；`-101` 最可能意为"离开婚房、回到进入前的城镇/区域"。服务端实现婚房移动时不能把 `-101` 当作真实 town ID。

### 2.3 入口：赛丽亚房间的"婚房门"（High）

全表数值搜索（[town-movable-area-to-101.tsv](evidence/town-movable-area-to-101.tsv)，55 行）：

```text
map/town/underfoot/(r)gate_underfoot.map
[import script] Common/(R)Gate_Seria.map
[town movable area]
330 324 289 24 2 2      ← 出口到 town 2 area 2
833 196 78 61 -2 -2     ← 公会大厅入口（特殊目标 -2）
46 212 34 52 101 0      ← 婚房入口：矩形 (46,212,34,52) → town 101 area 0
[/town movable area]
```

- 当前 `town.lst` 中每个城镇 twn 写的是 `Town/<X>/Gate_<X>.map`，但该文件本身不存在；实际存在的是 `(r)gate_<x>.map` 与 `(f)gate_<x>.map`。服务端 `PvfNpcShopDefinitionSource` 已按"A21 归档以 `(r)` 前缀命名生效版本"处理同类回退。全部 `(r)gate_*` 都含 `46 212 34 52 101 0`，全部 `(f)gate_*` 都没有（例：`(f)gate_underfoot.map` 只有前两条矩形）。
- `map/town/common/(r)gate_seria.map`（被 `(r)` gate 以 import script 引入）在 `[passive object]` 放置 `54170 64 175 0`；`passiveobject.lst` 中 `54170 = MapObject/PathGate/CoupleRoom/couple_room_gate.obj`（`[basic motion] couple_room_gate_open.ani`，另有 `couple_room_gate_close.ani`；`[name]` 抄作「洛兰的右侧门」）。`(f)gate_seria.map` 不放此门。证据：[gate-maps.txt](evidence/gate-maps.txt)、[passiveobject-54170-placements.tsv](evidence/passiveobject-54170-placements.tsv)。
- **结论（High）**：玩家在自己的赛丽亚房间左侧走进婚房门（门对象 @ (64,175)，触发矩形 (46..80, 212..264)）即请求移动到 town 101 area 0。门的开/关动画暗示客户端会按"是否已婚"切换门状态（Hypothesis）。

### 2.4 其它入口 / 相关菜单

| 入口 | 内容 | 置信度 |
|---|---|---|
| `etc/popupmenu.etc` | 无"进入婚房"菜单；婚房相关菜单只有 `1020 婚房装饰 npc 15`、`1021 种植 npc 17`、`1022 浇水 npc 18`、`1023 收割 npc 20`、`1024 清理花圃 npc 21`、`1025 植物状态 npc 19`、`1026 新婚祝词 npc 23`、`1027 领取夫妻赠礼 npc 24`（另有旧条目 `57 浇水 npc 15`） | Confirmed（[popupmenu-wedding-excerpt.txt](evidence/popupmenu-wedding-excerpt.txt)） |
| 物品 | 没有"传送到婚房"物品；`10002915 爱相随守护券` `[teleport to partner] 1`：传送到另一半身边（仅同频道且双方在城镇，赛丽亚房间/地下城等受限区不可用），`[need material] 10002861 5` | Confirmed |
| NPC 菜单与 role 的对应 | 云清 role → 1020/1026/1027；花圃 role → 1021..1025。该映射由客户端代码决定，PVF 只给菜单文本与功能码 | High |

---

## 3. 家具 / 装饰（问题 2）

### 3.1 物品类型与清单

- 76 个物品，路径 `stackable/event/chn_coupleroom_decoration/*.stk`，ID `10002684..10002726`、`10002769..10002802`（`10002700` 空缺）。完整表：[coupleroom-furniture.tsv](coupleroom-furniture.tsv)；原文：[items-coupleroom-decoration.txt](evidence/items-coupleroom-decoration.txt)。
- 共同字段（Confirmed）：`[stackable type] [couple room item] 0`、`[attach type] [trade]`、`[rarity] 2`、`[grade] 1`、`[minimum level] 1`、`[usable job] [all]`、`[icon] Item/chn_coupleroom/chn_*_icon.img N`、`[move wav] PAPER_TOUCH`、`[need material] 10002606 N`。**没有 `[price]`、`[expiration date]`、属性/buff 字段**：单件家具不带属性，属性来自整体华丽度 Buff（3.4）。
- 价值 `N`（心意点）分布：5（`10002796` 普通木质地板）、10、15、20、25、30、50、80、100、150、200；76 件合计 5145。
- 商店：`itemshop/chn_mago.shp`（id 1206，NPC 20011，`[etc shop]`，页签 `地板/壁纸/窗户/装饰物`，`-2` 分页、`-1` 空格）上架 75 件，**唯一不卖的是 `10002796` 普通木质地板**，它也不在 `coupleroomiteminfo.etc` 中——最可能是房间默认地板（Hypothesis）。证据：[shops.txt](evidence/shops.txt)。

按商店页签/摆放区域分组（名称见 tsv）：

| 组 | 物品 ID | 可摆槽位 |
|---|---|---|
| 地板 | 10002797 黑白方格、10002798 金色雕纹（另 10002796 默认） | 0 |
| 壁纸 | 10002799 白色、10002800 蓝天、10002801 典雅金丝 | 1 |
| 窗户 | 10002684/685/686/687/802 | 25（部分可 26/27） |
| 窗帘 | 10002710/711/780/781/782/783 | 3/14/15 |
| 吊饰/吊灯 | 10002697/698/699/778/779 | 2/4/5 |
| 墙饰 | 10002712..10002724、10002784..10002795 等 | 6/7/8/9/10/12/13、26/27 |
| 家具（床/桌/地毯/椅/雕像） | 10002693..10002696、10002701..10002709、10002725/726、10002769 | 10/12/13、16..23、19/20/21 |
| 小摆件（哥布林/牛头人/盆栽/南瓜） | 10002688..10002692、10002770..10002777 | 16..23（不含 21）或 19/20/21 |

### 3.2 槽位（摆放网格）定义 — `decoration/coupleroomiteminfo.etc`

`[position list]` 为 28 个四元组 `(index, x, y, z)`（Confirmed）：

```text
0 (273,-140,-200)  1 (560,-135,-200)   ← 地板 / 壁纸（z=-200 背景层）
2 (380,200) 4 (560,200) 5 (736,200)    ← 天花板吊饰
3 (559,180) 14 (383,180) 15 (737,180)  ← 窗帘
6 (86,100) 7 (1030,80) 8 (236,80) 9 (888,80)   ← 左右墙
10 (105,205) 11 (1187,235) 12 (268,110) 13 (855,105)
16 (330,180) 17 (530,180) 18 (710,180) 19 (280,280) 20 (530,280) 21 (767,280)
22 (180,380) 23 (530,380) 24 (780,380)          ← 地面
25 (544,0) 26 (368,0) 27 (722,0)                ← 窗户
```

`[coupleroom item]` 每条格式（High，75 条全部按此闭合）：

```text
<itemId> <N> { <pos> <dx> <dy> <z?> <flip> } × N  <tail=0>  <actPath>
```

- `N` = 可摆放的候选槽位数；`dx,dy` = 在该槽位上的偏移；第 4 项 75 条全为 0；第 5 项 ∈ {0,1}，在左右墙（6/7、8/9）上交替出现，判断为水平翻转（Hypothesis）；`tail` 75 条全为 0；`actPath` = `decoration/Action/chn_*.act` 渲染动作。
- 每个物品**只能**放在自己列出的槽位上；槽位 `11`、`24` 没有任何物品使用（Confirmed，[placement-usage.txt](evidence/placement-usage.txt)）。
- 注意：stackable.lst 的文件名与 act 名并不一一同名（例 `10002684` 文件 `chn_window_01.stk` 但 act 为 `chn_window_03.act`），**以 ID 为准**。
- 有壁纸、地板概念（槽位 0/1，各只能放一件）。**没有房间等级/扩建字段**（Confirmed：全表无相关 tag）；唯一的"等级"是华丽度 Buff 档位。

### 3.3 `decoration/` 下其它文件 —— 不属于婚房

`decoration/decorationiteminfo.etc`、`decorationitemetc.etc`（top/mid/bott 位置表、5 个碰撞框）、`decorationitemsetbuff.etc`（套装 `99999000 7 99`：物攻/物防/魔攻/魔防 +5、移速 +30、全属抗 +5）、`decorationlimitperiod.etc`（5/10/15/30 天期限）服务于 `2674xxx` 物品。这些物品在 `stackable/arad/decoration/deco2674xxx.stk`，虽同为 `[couple room item]` 类型，但 **`[name]` 为空、图标为 `Item/stackable/myroomUp_jp.img`、需 `2670469` 材料、且不在 `stackable/stackable.lst`**——是日服"マイルーム"系统的孤立残留，当前无法被引用（Confirmed：未入表；用途 Hypothesis）。证据：[items-unlisted-arad-decoration.txt](evidence/items-unlisted-arad-decoration.txt)。

### 3.4 婚房 Buff（华丽度档位）

`[couple room buff]` 格式 `<阈值> <条数> {<属性名> <值>}×条数`（Confirmed）：

| 阈值 | 属性 |
|---:|---|
| 10 | HP上限 +100 |
| 40 | HP/MP上限 +100 |
| 100 | HP/MP上限 +150、城镇移速 +3 |
| 200 | HP/MP上限 +200、城镇移速 +4 |
| 500 | HP/MP上限 +200、城镇移速 +4、全属强 +1 |
| 800 | HP/MP +200、城镇移速 +5、全属强 +5、装备物攻/魔攻 +5、独立攻击 +5 |
| 1000 | HP/MP +200、城镇移速 +10、全属强 +8、装备物攻/魔攻 +10、独立攻击 +20 |

- UI 文本：「婚房可以为拥有者附加提升属性的Buff，并且该Buff效果会随着婚房的华丽程度而改变」「婚房提供的Buff效果在决斗场中无效」（Confirmed）。
- **阈值单位（High）**：不可能是摆放件数（最多 28 槽，阈值到 1000）；不可能是爱心点余额（上限 999 < 1000）。最一致的解释是"已摆放装饰物 `[need material] 10002606` 值之和"：例如 地板100 + 壁纸100 + 床200 + 桌200 + 地毯200 + 水族箱200 = 1000，可达满档。仍需实机或客户端逆向确认。

### 3.5 与 `0x03D8 COUPLE_ROOM` 的候选映射

`0x03D8` 已确认结构：`u32 count; {u16 slot; u32 value_or_item_id} × count; u32 tail0; u32 tail1; u32 tail2; i8 tail3`（见 `protocol/game/server-to-client/NOTI-0x03D8.md`，本调查未修改该目录）。

| 字段 | 候选含义 | 依据 | 置信度 |
|---|---|---|---|
| `count` | 已摆放装饰物数量，≤ 28 | 槽位总数 28 | High |
| `u16 slot` | `[position list]` 的槽位 index 0..27 | 每件物品按 pos index 声明摆放；无其它 0..27 量 | High |
| `u32 value_or_item_id` | 装饰物 itemId（10002684..10002802） | 客户端据 itemId 查 `coupleroomiteminfo` 的 act 与 dx/dy | High |
| 替代解释 | slot = 背包/保管箱格位而非房间槽位 | 云清气泡"物品保管箱开启时，右键单击装饰物，可进行装饰和收回" | Hypothesis（较弱） |
| `tail0..tail2, tail3(i8)` | **候选 A（情侣树状态，首选）**：tail0 = 树 index（1..3）或种子 itemId；tail1 = 成长值（0..30）；tail2 = 种植/到期时间戳（30 天）；tail3 = 当日已浇水次数（0..2）或枯萎状态（1..3，对应 `bad_1..3.ani`） | 花圃 NPC 就在婚房地图里；`coupletree.etc` 正好需要"树种类/成长值/期限/每日次数"四类状态，且只有 tail3 是 1 字节 | Hypothesis |
| | 候选 B：tail0/tail1 = 夫妻两人角色 ID（u32），tail2 = 爱心点或华丽度，tail3 = Buff 档位/戒指等级 | 房间归属两人；爱心点 ≤999、档位 0..7、戒指 0..5 都可放进 i8 | Hypothesis |
| | 候选 C：tail0 = 华丽度总值，tail1/tail2 = 心意点/房主，tail3 = Buff 档位 | 客户端可自行由摆放求和，冗余可能性低 | Hypothesis（弱） |

建议实机差分：摆放一件已知价值的装饰后看 `count/slot/itemId` 与 tail 变化；种树、浇水各做一次看 tail 变化。

---

## 4. 情侣仓库（问题 3）

- **Confirmed：PVF 中不存在情侣仓库/夫妻共享仓库的任何配置。**全表 tag 扫描只有 `[item storage]`/`[storage]` 出现在 `npc/storage.npc`、`npc/storage_ad.npc`、`npcname.lst`（普通仓库），以及 `[guild storage]`/`[guild storage upgrade info]`（公会仓库）；字符串池（107 万 UTF-8 + 22 万 UTF-16 条）中没有"情侣仓库/夫妻仓库/婚房保管箱"类文本；婚房地图不放任何保管箱对象；`map/coupleroom/animation/safe.ani` 未被引用。
- 唯一相关文本是云清气泡「物品保管箱开启时，右键单击装饰物，可进行装饰和收回」：说明装饰/收回操作发生在"物品保管箱"窗口打开时，被收回的装饰物应回到某个保管箱/背包。它是普通仓库、角色背包还是协议层的专用"婚房装饰收纳"，PVF 无法判定（Hypothesis）。
- 因此"容量、可存物品限制"无 PVF 依据；若 A21 协议存在情侣仓库，其容量/限制只能来自客户端常量或服务端设计。

## 5. 情侣树（问题 4）

### 5.1 规则表 `decoration/coupletree.etc`（Confirmed）

```text
[seed]          10002766 1   10002768 2   10002767 3      (种子 itemId → 树 index)
[reward letter] 1078 1192
[tree] index 1 神秘的枫树 / index 2 粉晶石树 / index 3 黄宝石树，三棵树参数相同：
  [expire day]       30
  [day water count]  2
  [grow point]       10002765 10   10002905 20           (洒水壶 itemId → 每次成长值)
  [step]             10 tree_<id>_1.ani  20 tree_<id>_2.ani  30 tree_<id>_3.ani
  [reward]           (itemId, 权重/100000, 数量)
```

| 种子 | 名称 | 树 | 成长动画 |
|---|---|---|---|
| 10002766 | 神秘的枫树种子 | 1 神秘的枫树 | `decoration/growtree/Animation/tree_2673004_{1,2,3}.ani` |
| 10002768 | 粉晶石树的种子 | 2 粉晶石树 | `tree_2673005_{1,2,3}.ani` |
| 10002767 | 黄宝石树的种子 | 3 黄宝石树 | `tree_2673006_{1,2,3}.ani` |

收获奖励（三棵树相同；权重合计正好 100000，High：单次按权重抽 1 条）：

| 物品 | 权重 | 数量 | 概率 |
|---|---:|---:|---:|
| 10002609 爱之摩伽陀邀请函 | 40000 | 2 | 40% |
| 10002901 心意通 | 40000 | 2 | 40% |
| 10002758 同心喇叭 | 10000 | 1 | 10% |
| 10002609 爱之摩伽陀邀请函 | 5000 | 3 | 5% |
| 10002901 心意通 | 4000 | 5 | 4% |
| 10002609 爱之摩伽陀邀请函 | 1000 | 5 | 1% |

### 5.2 推导规则

- 成长阶段 3 个，阈值 10/20/30；成长值 30 达到最终阶段，可收割（High；洒水壶说明"当爱情树的成长值达到一定程度时，即可进行收割"）。
- 每日浇水上限 2 次；普通洒水壶 `10002765` +10，强力洒水壶 `10002905` +20（两者同名「洒水壶」）。全用普通壶最快 2 天（10+10，次日 +10），用强力壶最快也需 2 天（20 + 次日 20 截顶）（High，依赖"每日"重置时点）。
- `day water count 2` 是**每对夫妻共计**还是**每人各自**：PVF 未声明（Unknown）。洒水壶说明"收割后，夫妻二人皆可获得各种各样的礼物"（Confirmed：双方都得奖励）；双方是各抽一次还是共享一次抽取未声明。
- `[expire day] 30`：种植后 30 天到期（Confirmed 数值；到期后的行为——枯萎/自动清除——未声明）。
- `[reward letter] 1078 1192`：与 `weddingsystem.etc` `[letter]` 中第一对相同，且全表其它活动使用 `[letter title index]`/`[letter content index]` 成对编号，判断为"邮件标题文本 ID 1078、正文文本 ID 1192"，收获奖励经邮件发放（High 格式 / Hypothesis 发放方式）。
- 移除：花圃菜单 `1024 清理花圃`（Confirmed 菜单存在；是否要求成熟/到期未声明）。查询：`1025 植物状态`。
- 动画/表现 `decoration/growtree/coupletreeiteminfo.etc`（Confirmed）：`[tree bad grow] badgrow.ani`、`[tree full grow] full_grow.ani`、`[tree bad grow state] 1 bad_1.ani 2 bad_2.ani 3 bad_3.ani`（三级不良生长/枯萎状态，触发条件未声明）、`[tree watering pot] 10002765 watering_2673000.ani`（只列普通壶）、`[tree seeds]` 三种子均用 `seeding_2673004.ani`。花圃 NPC 本体动画 `npc/animation/chn_coupleroom/coupleroomtree.ani`。

### 5.3 物品来源（Confirmed）

| 来源 | 内容 |
|---|---|
| 云清商店 `chn_mago_normal.shp`（id 1207） | 3 种种子、洒水壶 10002765、邀请函 10002609、戒指升级券 10002611..10002615 |
| 物品价格 | 种子/洒水壶 `[price] 10000`；种子 `[waste]`、普通壶 `[material]`、强力壶 `[waste]` 且无价格；均 `[impossible contents] gift`，堆叠 1000 |
| 爱之摩伽陀掉落 `etc/independent_drop.etc` | 怪 64760/64761（SFO00/01）：洒水壶 100%、种子各 5%、守护券 20%、邀请函 100%；Boss 64762：洒水壶 100%、心意通 100%、邀请函 100%×2 + 5%×2、守护券 50%、同心喇叭 3%（[independent-drop-wedding-dungeon.txt](evidence/independent-drop-wedding-dungeon.txt)） |
| UI 文本 | 「爱情树的种植与培育，皆只能在婚房中进行」「A. 在NPC云清处购买；B. [爱之摩伽陀]地下城中有一定几率掉落」 |

`强力洒水壶 10002905` 没有商店来源，也不在独立掉落中；`etc/chn_dblogitem.etc` 中它的日志期限是 `2014-03-11`，推测为已结束的活动物品（Hypothesis）。

### 5.4 孤立的另一套"成长树"

`decoration/growtree/growtree.etc` 与 `growingtreeiteminfo.etc` 定义的是 `2673004..2673006` 三棵树（成长值 50、每日 50、期限 30/15/15 天、`[leaf reward]`/`[special reward]`/`[harvest reward]` 80% normal / 20% special），以及 `2673000/1/2` 三种水壶（`[tree watering pot] 2673000 1 1 / 2673001 2 1 / 2673002 0 1`，对应 `wateringcan_me/you/all.stk`）、收割工具 `2673003 0 100000`。这些物品在 `stackable/arad/growtree/`，**`[name]` 为空、图标 `consumption_jp.img`、不在 stackable.lst**——日服系统残留，婚房不使用（Confirmed 未入表）。但其"给自己/给对方/给全部"水壶设计可作为原版"夫妻各自浇水"语义的旁证（Hypothesis）。婚房树复用了它的 `tree_2673004..6_*.ani` 动画。

## 6. 成长胶囊（问题 5）

`etc/couplegrowthcapsule.etc`（Confirmed）：

```text
[max own count]   10
[max taken count] 10
[gage]            2
[max gage]        10
[capsule id]      10002910
```

`10002910 爱情成长胶囊`（Confirmed）：`[attach type] [account]`、`[minimum level] 30`、`[maximum level] 84`、`[stackable type] [etc]`、`[item group name] potion`、`[increase status type] [expUp] 30000`、`[impossible contents] hardcore mode`、堆叠 1000；说明「蕴含夫妻间真爱的成长胶囊。使用后，可以获得30000点经验值」。

- UI：`ui/chn_wedding/coupleinfowindow.xui` 有 3 个页签 `婚姻信息 / 结婚特权 / 成长胶囊`；成长胶囊页有一个 gauge（ID 1600，`Interface2/wedding/coupleGrowthCapsule.img`）和两个按钮（ID 1200、1201，同图集）以及两个滚动列表（1601 `scrollMax 5`、1602 `scrollMax 8`）。Confirmed（[ui-xui.txt](evidence/ui-xui.txt)）。
- 云清 role `[receive partner gift] 0` 与菜单 `1027 领取夫妻赠礼`（Confirmed）。
- **规则推断（Hypothesis）**：某事件每次让 gauge +2，满 10（5 次）生成 1 个胶囊作为"夫妻赠礼"给对方；对方在云清处"领取夫妻赠礼"；"持有上限 10 / 领取上限 10"限制累计与领取数量。增长事件（组队通关？登录？在线时长？）、上限是每日还是累计、两个按钮的语义（赠送/领取？）PVF 均未声明。
- 注意区分：`event/growthcapsule.evt`（`[capsule id] 2749909`、`[max gage exp] 100000`）与 `ui/event/growthcapsule/growcapsulewindow.xui` 是 Lv80 经验胶囊活动，**与婚姻无关**（Confirmed）。

## 7. 戒指升级（问题 6，简要）

`etc/weddingsystem.etc`（全文 [weddingsystem.txt](evidence/weddingsystem.txt)）：

| Lv | 戒指名（券说明） | Buff `[wedding ring buff]` | 升级到此级所需券 |
|---:|---|---|---|
| 0 | 简约的铂金戒指 | 物防 8、魔防 8 | — |
| 1 | 闪亮的白玉戒指 | 物防10 魔防10 物攻15 魔攻15 | 10002611，50 心意点，500,000 金 |
| 2 | 耀眼的蓝玉戒指 | + 硬直 200 | 10002612，300，1,000,000 |
| 3 | 华丽的紫玉戒指 | + 移速 20 | 10002613，500，3,500,000 |
| 4 | 璀璨的粉玉戒指 | + 物暴/魔暴 2 | 10002614，800，8,000,000 |
| 5 | 神圣的橙玉戒指 | + 攻速 20、施放 20 | 10002615，999，12,000,000 |

- `[upgrade ring ticket] 0 0 10002611 1 10002612 1 10002613 1 10002614 1 10002615 1`：按 Lv0..5 的 (券 itemId, 数量) 对（High）。
- 券：`[action type] [upgrade wedding ring] N`、`[action usable place] [village] [seria room]`、`[need material] 10002606 N`、`[price]` 如上（`[price]` 是售价还是升级手续费未声明；Hypothesis：云清商店售价）。说明「夫妻之中任意一方升级成功，另一方的戒指也会自动升级」（Confirmed）。
- `[wedding ring nametag action]` Lv0..5 → `UI/chn_wedding/action/chn_wedding_ring_01..06.act`（名牌特效）。UI：「婚戒提供的Buff效果在决斗场中无效」。
- 爱心点：`[love point] 1`、`[max love point] 999`、`[love point item] 10002901 1`；UI「已婚角色每日登录游戏时都将获得1点心意点（每日限1次）」；`10002901 心意通` `[love point coupon] 1`（只有已婚角色可用）。

## 8. 文本与 ID（问题 7）

- **n_string 不可用**：`n_string.lst` 列出 `Etc/Etc.chn.str` 等 27 个 `.str`，但两份 PVF 的文件表里都**没有**任何 `.str` 文件（Confirmed）。UI 中 `<99::70989>`、`<99::11111>` 形式的引用无法在 PVF 内解析。客户端错误提示很可能在 exe 内或被剥离的字符串表中。
- `etc/hardcodetexttag*.etc` 不含婚房/树/胶囊相关提示（Confirmed）。
- 可用作文本/功能 ID 的静态值：

| 类别 | ID |
|---|---|
| popupmenu 文本 ID（功能码） | 26 求婚(event 0)、57 浇水(npc 15)、1013 申请订婚(10)、1014 举办婚礼(11)、1015 退婚(12)、1016 离婚(12)、1017 步入礼堂(13)、1018 离开礼堂(14)、1020 婚房装饰(15)、1021 种植(17)、1022 浇水(18)、1023 收割(20)、1024 清理花圃(21)、1025 植物状态(19)、1026 新婚祝词(23)、1027 领取夫妻赠礼(24) |
| 邮件 (标题, 正文) 文本索引 | 树收获 `1078 1192`；婚姻主表 `[letter]` `1078 1192/1193/1194/1195/1196/1197/1218` |
| NPC role | `[couple room shop] 1206`、`[item shop] 1207`、`[wedding message] 0`、`[receive partner gift] 0`（20011）；`[couple room tree] 0`（20012）；`[wedding] 0`（20013）；`[exit church] 0`（20014）；`[wedding message]` 也在 `20010 chn_words_of_blessing_box_01` |
| NPC 气泡（20011） | 「物品保管箱开启时，右键单击装饰物，可进行装饰和收回。」「请通过我购买婚房装饰物~」 |
| UI 控件文本 | `coupleinfowindow.xui` 控件 1340..1365（结婚特权说明 7 段）、页签"婚姻信息/结婚特权/成长胶囊" |

## 9. 服务端 / 客户端 PVF 一致性（问题 8）

用同一扫描器对两份 PVF 的 144 个资源做规范化 dump（token 文本化）并比较 SHA-256，**144/144 全部一致**，另 `ui/chn_wedding/coupleinfowindow.xui` 文本 SHA-256 一致（`2265B4A0…6B658`）。覆盖：婚房 twn ×2、town.lst、npc.lst、婚房地图、3 个婚姻 NPC、`decoration/` 下 9 个 etc、`couplegrowthcapsule.etc`、`weddingsystem.etc`、`popupmenu.etc`、`independent_drop.etc`、itemshop.lst 与 3 个商店、门对象、`map/common/(r)gate_seria.map`、stackable.lst，以及 `chn_coupleroom_decoration` / `chn_wedding_system_2014` 下全部 116 个物品文件。矩阵：[pvf-resource-equivalence.tsv](pvf-resource-equivalence.tsv)。整档 SHA-256 不同，差异不在本主题资源上。

## 10. 关键未知

1. `[couple room]` 实例是"夫妻共享一个房间"还是"各自一个"、访客能否进入；出口 `-101` 的精确语义。
2. 门 `54170` 开/关状态的条件（是否已婚）以及未婚者走进矩形的行为。
3. 华丽度的精确计算口径（推断 = 已摆放装饰物心意点价值之和）。
4. `0x03D8` 13 字节尾部的真实含义（树状态 vs 房主/爱心点）。
5. 浇水次数是夫妻共计还是各自；奖励是否各抽一次；每日重置时点；到期/枯萎（bad grow 1..3）的触发条件；清理花圃的前置条件。
6. 成长胶囊 gauge 的增长事件与两个按钮的语义；上限 10 的时间窗口。
7. 是否真的存在"情侣仓库"——PVF 无任何依据，需 A21 协议/UI 层确认。
8. 装饰物"购买"是消耗心意点后得到物品，还是摆放时消耗——`[need material]` 字段语义（推断为兑换代价）。

## 11. 复现

从仓库根目录执行（Git Bash）：

```bash
dotnet build Tool/PvfProbe/PvfProbe.csproj -c Release        # 扫描器引用其 Dfo.Pvf.dll
dotnet build Document/outputs/marriage-housing-20261018/pvf/scanner/PvfHousingScanner.csproj -c Release -o <scratch>/hsbin
H="dotnet <scratch>/hsbin/PvfHousingScanner.dll"; PV=artifacts/sao-ticket-20261014/Script.server.pvf
$H $PV dump town/coupleroom.twn map/coupleroom/coupleroom.map decoration/coupleroomiteminfo.etc decoration/coupletree.etc etc/couplegrowthcapsule.etc
$H $PV lst stackable/stackable.lst | grep chn_coupleroom_decoration       # 76 件装饰物
$H $PV items stackable/stackable.lst 10002684-10002802                    # 物品原文
$H $PV numwhere 101 -101 | grep 'town movable'                           # 婚房出入口
$H $PV numwhere 54170                                                    # 婚房门对象放置
$H $PV tagwhere "[item storage]" "[storage]" "[couple room]" "[couple room tree]"
$H $PV text ui/chn_wedding/coupleinfowindow.xui
$H $PV scan > housing-scan-full.tsv                                      # 全表扫描（本目录仅保留主题相关摘录）
$H $PV hash <资源...>  与  $H ../DNFClient-A21-protocol/Script.pvf hash <资源...>   # 一致性
```

构建产物输出到 scratch，`scanner/` 下只保留 `Program.cs` 与 `PvfHousingScanner.csproj`。

## 12. 证据文件

- 核心文件原文：[core-files.txt](evidence/core-files.txt)（twn×2、婚房 map、门 obj、花圃 npc、coupletree、decoration 各 etc、couplegrowthcapsule）
- 摆放/装饰表原文：[decoration-iteminfo.txt](evidence/decoration-iteminfo.txt)；解析表 [coupleroom-furniture.tsv](coupleroom-furniture.tsv)；槽位使用 [placement-usage.txt](evidence/placement-usage.txt)
- 物品原文：[items-coupleroom-decoration.txt](evidence/items-coupleroom-decoration.txt)、[items-housing-misc.txt](evidence/items-housing-misc.txt)、孤立物品 [items-unlisted-arad-decoration.txt](evidence/items-unlisted-arad-decoration.txt)、[items-unlisted-arad-growtree.txt](evidence/items-unlisted-arad-growtree.txt)
- NPC / 商店 / 菜单：[npcs.txt](evidence/npcs.txt)、[shops.txt](evidence/shops.txt)、[popupmenu-wedding-excerpt.txt](evidence/popupmenu-wedding-excerpt.txt)
- 出入口：[gate-maps.txt](evidence/gate-maps.txt)、[town-movable-area-to-101.tsv](evidence/town-movable-area-to-101.tsv)、[passiveobject-54170-placements.tsv](evidence/passiveobject-54170-placements.tsv)、[towns-listed.txt](evidence/towns-listed.txt)
- 婚姻主表：[weddingsystem.txt](evidence/weddingsystem.txt)；掉落：[independent-drop-wedding-dungeon.txt](evidence/independent-drop-wedding-dungeon.txt)；UI：[ui-xui.txt](evidence/ui-xui.txt)
- 全表扫描摘录：[housing-scan.tsv](housing-scan.tsv)；一致性矩阵：[pvf-resource-equivalence.tsv](pvf-resource-equivalence.tsv)
