# 执行期取证：档案改了三处结论

取证日期 2026-08-30，档案 `DNFClient-A21-protocol/Script.pvf`。
工具：临时 PVF dumper（scratchpad），全量扫描 `equipment.lst` 118479 条与 `stackable.lst` 42272 条。

## 一、装备池的出处是 `.equ` 自己，不是 `ItemDictionary.etc`

`Docs/DropRates.md` 第五节按上游的读法写着「装备取自 `ItemDictionary.etc`，
`[5] genRate` 决定入池，`grade * 10 + rarity` 分桶」。**这一整条是错的。**

把 13474 条字典行与它们各自的 `.equ` 全部对上之后：

| 字典列 | 上游读作 | 实际是什么 | 证据 |
|---|---|---|---|
| `[1]` | rarity | 图鉴的陈列分类 | 8 个取值一一映到 `.equ` 稀有度，但不是同一个：`4→5`、`3→2`、`6→3`、`7→4` |
| `[2]` | grade | `[minimum level]` | 13474 行里只有 5930 行与 `.equ` `[grade]` 相等 |
| `[3]` | ？ | `[grade]` | 上游没读这一列 |
| `[5]` | genRate（权重） | 只取 0/1 的旗标 | 全表仅两个取值 |

`ItemDictionary` 与 `(f)/(r)itemdictionary`、`baseitemdictionarytable`、`dungeondropinfo`、
`independentdropinfo` 同在 `etc/itemdictionary/`，与 `ui/itemdictionary/*.xui` 成对——
**它是图鉴 UI 的索引，不是掉落池。** 13474 条，而装备总数 118479。

真正的闸和堆叠物是同一条规则，即文档第五节自己写下的原则「闸在物品自己身上」：

```text
equipment/equipment.lst → 每个 .equ 的 [creation rate] / [grade] / [rarity]
stackable/stackable.lst → 每个文件的 [creation rate] / [grade] / [rarity]
```

* 装备：118479 件里 **6062 件** 声明 `[creation rate] > 0`，权重取值
  30/40/41/125/187/250/312/375/500/625/750/1125 等 17 种（**不是 0/1**）；
  另有 9505 件显式声明 0（明确排除），102912 件未声明。
  入池的稀有度只有 0..4，件数 911/919/930/2465/837。
* 堆叠物：42272 件里 **774 件** 声明 `[creation rate] > 0`，760 件是 `[recipe]`、14 件是 `[throw]`，
  权重多为 1000。文档第五节点名的三件全部核对通过：
  `10087807` grade 57 / rarity 2 / cr 1000 `[recipe]`；`10088621` 与 `10088620` 未声明。

上游自己在堆叠物那一侧就是用 `[creation rate]` 正则读的，只有装备那一侧换成了字典——
**它与自己不一致，这就是那处编造的破绽。**

## 二、`[item drop ref table]` 是等级→品级带，读法可自证

每张费率表里 200 行 × 3 数：`等级 向下 向上`，品级带 = `[等级−向下, 等级+向上]`。

自证在于：**等级 ≥ 86 的每一行，`等级 − 向下` 恒等于 79。**
86→`7 4`、87→`8 4`、88→`9 4`、89→`10 4`、90→`11 3`、…、200→`121 3`。
79 正是 A21 顶级装备的品级下沿；这不是巧合能凑出来的形状。

`Docs/DropRates.md` 完全没有提这张表，品级带此前无出处。现在有了。
五张表各自声明一份（200 行 600 个数），且并不相同：86 级时怪物表给 `7 4`、可破坏物表给 `7 3`。

## 三、五个率的分母仍然不在这一族档案里

`itemdropinfo_*` 全族没有任何基数段。`Etc/ServerParameter.etc` 里有
`[basis of item dicision] 2500`，但它紧挨着 ServerParameter **自己的**
`[drop prob]`（3 段 × 2 个率），归属存疑：按 2500 读，普通怪掉金币 30%；按上游的 10000 读，7.5%。

按用户既有约束「pvf 外的服务端配置最好能配置化」，本期把它做成**服务端策略配置**，
默认 10000（复现文档第三节那张与游玩记忆吻合的形态），并在近旁写明两个候选与未定的理由。
规则本身吃传入的基数，不认识任何字面量。

## 四、顺带查明、本期不做的

| 段 | 是什么 | 处置 |
|---|---|---|
| `[drop bonus of dungeon level increase]` | 等级差掉落惩罚：+8→0.4 … 0→1 … −3→1.225 | 记下，本期不加（RQ 未要求；缺它是偏高的有界偏差） |
| `[gold quantity] 1 2 4 6` / `[gold volume] 100 110` | 金币按档次的倍数与 ±浮动 | 金币数额用得上 |
| `[gold drop ref table]` | 每级金币基数（86 级 = 1357） | 同上 |
| `[user derived drop job mapping]` | 职业→可掉落装备类别 | 记下，抽取不吃角色，本期不做 |
| `[drop upgrade rarity]` | 掉落装备的强化等级分布（仅 `[rare]` 有表） | 记下 |
| `[condition rate]` / `[first boss/named mob hunting]` / `[item drop rarity control]` | 未查明 | 未查明 |
| `[basis of rarity dicision]` 的前导计数 | 只有深渊表有，其余四张没有 | 读四张同构表时不吃计数 |
| `itemdropinfo_clearreward.etc` 的 `[drop prob]` | 形状完全不同（按 profile 命名分组、每段只有 1 个率） | 二期不是「换一张表」那么简单，须另写一个读法 |

## 五、怪物档次已经建模了——契约的那条假设不成立

契约 Assumptions 写着「怪物档次尚未在本仓建模」。**不对。**
`PvfDungeonRoomActorSource.RankOf` 把地图上的引号词读成
`[normal]→0 / [champion]→1 / [super champion]→2 / [boss]→3`，存进 `DungeonRoomActor.ActorType`，
`MonsterDeathHandler` 已经把它转成 `MonsterRank` 交给经验命令。
`[monster type drop bonusrate]` 每组正好 4 档，与 0..3 一一对应。

所以档次倍率取得到真值，不需要「按普通处理」的缺省，也就没有那个偏低的有界偏差。

## 六、掉落记录的 48 字节布局已经被真实帧证实

契约 Assumptions 说这个布局「取自上游构包器，尚未用带掉落的真实帧验过」。**现在验过了。**

上游抓包里有 6 帧带掉落的 `NOTI 0x0026`（两个不同的抓包、两台不同的角色）：

```text
packet_log.upstream-killexp-20260827.txt  1/1/2/1/1 件
packet_log.reference-20260823.txt         2 件
```

按 `u16 actor + u8 count + count×48B + 4B` 解，**六帧全部整除，无一字节剩余**
（103 = 3 + 48×2 + 4；55 = 3 + 48 + 4），且每条记录里该为零的 24 字节全部为零。
44 字节或 52 字节的读法解不开那两帧 103 字节的。

一条记录：

| 偏移 | 宽 | 字段 | 观测值 |
|---|---|---|---|
| +0 | u16 | 场景槽号 | 1,2,3,4,5,6 —— **整局连续递增，不按房间重置** |
| +2 | u32 | 物品号 | `0` = 金币；`3142`、`10088620`、`109040005` = 物品 |
| +6 | u8 | 强化等级 | 全为 0 |
| +7 | u32 | 数量 | 金币是**金额**（38 / 44 / 131），物品是件数 |
| +11 | 5B | 零 | |
| +16 | u32 | 判别值 | 上游写的是 **Unix 时间戳**（0x6A904F8E…），同一次击杀的两件相同 |
| +20 | 24B | 零 | |
| +44 | u16 | 归属 | = 该角色的 wire user id |
| +46 | u16 | 零 | |

尾部 4 字节 `00 00 FF 00`，与本仓无掉落时写的完全一致。

**由此定死的三件事**：

1. **金币就是物品号 0，金额写在数量字段。** 客户端 `DropInfo.IsGold => TemplateId == 0`
   这条读法与真实帧一致，不再只有上游一个来源。
2. **一次击杀可以掉两件。** 抓包里有一帧同时给了 44 金币和一件物品，这正是 AC-008 的形状。
3. **判别值上游用时间戳。** 本仓房间声明掉落那一侧用的是从种子推出来的值（可复现），
   怪物掉落沿用同一套；时间戳不可复现，是本仓明确不采用的那种做法。

顺带：抓包里 10088620（精灵气息）从怪物身上掉了三次。它没有 `[creation rate]`，
不可能走通用阶梯——那是上游 `AreaMaterialDropProvider` 那 40 行硬编码的产物，
**是它编造行为的实物证据，不是关于游戏的证据。**
