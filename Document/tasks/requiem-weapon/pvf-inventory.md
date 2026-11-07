# 镇魂 / 释魂武器 PVF 盘点

只读盘点：没有修改任何仓库文件、数据库、服务，也没有修改 PVF。
置信度含义：Confirmed 表示 PVF 原文就是这样写的；High 表示由多处原文相互印证得出的推断；
Hypothesis 表示推测，未经客户端或服务端验证；Unknown 表示 PVF 中没有找到答案。

## 0. 数据源

| 项 | 值 |
|---|---|
| 主干运行服务 | 2026-11-07 调查时没有 Dfo.Server.Host/dotnet 进程在跑。主干 `appsettings.json` 不含 PVF 路径，路径由环境变量 `PVF_ARCHIVE_PATH`/`Pvf:ArchivePath` 决定，而用户级、机器级环境变量都为空。因此按任务约定改用 A21-debug 客户端的 PVF |
| 使用的 PVF | `E:\Project\DNF\DNFClient-A21-debug\Script.pvf`，73,067,145 B，2026-10-15 14:08:37 |
| SHA-256 | `DC250D42A7DE1123F7CF7813260EA9359695E71A05DF6BC0B5341E8E250F27CF` |
| 其他副本（未使用，仅作对照） | `DNFClient\Script.pvf` 57338873…ACAB；`DNFClient-A21-protocol\Script.pvf` 01F1BE68…5189；`DNFClient-OneKey\Server\Data\Pvf\Script.pvf`（OneKey 启动脚本会把它设为 `PVF_ARCHIVE_PATH`）49D4CAFA…6E7A。四份哈希各不相同，结论未在其他副本上复核 |
| 读取方式 | 用主干 `Dfo.Pvf`（`Server/src/Dfo.Pvf/bin/Debug/net10.0/Dfo.Pvf.dll`）的 `PvfArchive.OpenScript`，把 712,010 个文件全部按 token 转成文本（渲染方式与 `Tool/PvfProbe dump` 相同：标签单独成行），写在会话 scratchpad 里，再做全文检索。ID 与路径的对应以 `equipment/equipment.lst`、`stackable/stackable.lst`、`n_quest/quest.lst`、`npc/npc.lst`、`itemshop/itemshop.lst`、`dungeon/dungeon.lst` 为准 |
| 文本语言 | 该 PVF 的名称与说明都是**简体中文**，关键字是“镇魂/释魂”，没有日文“鎮魂/レクイエム/釈魂”。Confirmed |

## 1. 镇魂武器模板（31 个）

识别依据：全档案带 `[emancipate type] 19` 的装备共 31 个，名称都是“镇魂之封灵X”，正好每种武器类型一个（Confirmed）。
31 个模板逐个提取后，下列公共字段完全相同（Confirmed）：

- `[grade] 90`、`[rarity] 6`、`[minimum level] 85`、`[attach type] [trade]`、`[need material] 10000204 100`（镇魂之石 ×100）
- `[emancipate]` 块：`[input] 10000204 15 10000327 75 10000224 1 3167 250 3037 10000`、`[output] <释魂ID> 1`、`[emancipate type] 19`

原文样例 `equipment/character/swordman/weapon/ssword/101000027.equ`：

```
[emancipate]
[input]
10000204	15	10000327	75	10000224	1	3167	250	3037	10000
[/input]
[output]
101000632	1
[/output]
[emancipate type]
19
[/emancipate]
```

`[rarity] 6` 在游戏里叫什么：镇魂之石的说明写“兑换传说装备”，据此推测 6 = 传说（Hypothesis）。服务端 rarity 枚举是否包含 6，本次未核对。

### 1.1 镇魂 → 释魂映射总表

两条路径产出的是**不同的模板 ID**：开光（emancipate）产出 `[attach type] [trade]` 版本，设计图产出 `[sealing]` 版本（Confirmed）。
两个版本的 grade、rarity、等级、图标一致（High）。只有短剑 101000028 与 101000632 逐字段比对过，结果仅 attach type 不同。

| 镇魂 ID | 镇魂名称 | 职业目录 | 武器类型 | 可用职业 [usable job] | [equipment type] 子值 | 开光输出 ([emancipate][output], attach) | 设计图 ID | 设计图输出 (attach) |
|---|---|---|---|---|---|---|---|---|
| 101000027 | 镇魂之封灵短剑 | swordman | ssword | [swordman] [demonic swordman] [at swordman] [knight] | [weapon] 23 | 101000632 ([trade]) | 2651400 | 101000028 ([sealing]) |
| 101010044 | 镇魂之封灵巨剑 | swordman | hsword | [swordman] [demonic swordman] [at swordman] [knight] | [weapon] 24 | 101030660 ([trade]) | 2651401 | 101010045 ([sealing]) |
| 101020046 | 镇魂之封灵太刀 | swordman | katana | [swordman] [demonic swordman] [at swordman] | [weapon] 23 | 101010789 ([trade]) | 2651402 | 101020047 ([sealing]) |
| 101030041 | 镇魂之封灵骨槌 | swordman | club | [swordman] [demonic swordman] [at swordman] | [weapon] 23 | 101020601 ([trade]) | 2651403 | 101030042 ([sealing]) |
| 101040045 | 镇魂之封灵光剑 | swordman | beamsword | [swordman] [demonic swordman] [at swordman] | [weapon] 24 | 101040456 ([trade]) | 2651404 | 101040046 ([sealing]) |
| 102000056 | 镇魂之封灵手套 | fighter | knuckle | [fighter] [at fighter] | [weapon] 23 | 102000487 ([trade]) | 2651405 | 102000057 ([sealing]) |
| 102010113 | 镇魂之封灵臂铠 | fighter | gauntlet | [fighter] [at fighter] | [weapon] 24 | 102010551 ([trade]) | 2651408 | 102010114 ([sealing]) |
| 102020049 | 镇魂之封灵鬼爪 | fighter | claw | [fighter] [at fighter] | [weapon] 23 | 102020457 ([trade]) | 2651406 | 102020050 ([sealing]) |
| 102030045 | 镇魂之封灵东方棍 | fighter | tonfa | [fighter] [at fighter] | [weapon] 23 | 102040522 ([trade]) | 2651407 | 102030046 ([sealing]) |
| 102040045 | 镇魂之封灵拳套 | fighter | boxglove | [fighter] [at fighter] | [weapon] 24 | 102030459 ([trade]) | 2651409 | 102040046 ([sealing]) |
| 104000046 | 镇魂之封灵左轮 | gunner | revolver | [gunner] [at gunner] | [weapon] 23 | 104000523 ([trade]) | 2651410 | 104000047 ([sealing]) |
| 104010040 | 镇魂之封灵手枪 | gunner | automatic | [gunner] [at gunner] | [weapon] 23 | 104010509 ([trade]) | 2651411 | 104010041 ([sealing]) |
| 104020042 | 镇魂之封灵手弩 | gunner | bowgun | [gunner] [at gunner] | [weapon] 23 | 104040527 ([trade]) | 2651412 | 104020043 ([sealing]) |
| 104030049 | 镇魂之封灵步枪 | gunner | musket | [gunner] [at gunner] | [weapon] 24 | 104020507 ([trade]) | 2651413 | 104030050 ([sealing]) |
| 104040044 | 镇魂之封灵手炮 | gunner | hcannon | [gunner] [at gunner] | [weapon] 24 | 104030511 ([trade]) | 2651414 | 104040045 ([sealing]) |
| 106000044 | 镇魂之封灵战矛 | mage | spear | [mage] [at mage] [creator mage] | [weapon] 23 | 106000414 ([trade]) | 2651415 | 106000045 ([sealing]) |
| 106010043 | 镇魂之封灵魔杖 | mage | rod | [mage] [at mage] [creator mage] | [weapon] 23 | 106020507 ([trade]) | 2651416 | 106010044 ([sealing]) |
| 106020041 | 镇魂之封灵战棍 | mage | pole | [mage] [at mage] [creator mage] | [weapon] 23 | 106010443 ([trade]) | 2651417 | 106020042 ([sealing]) |
| 106030043 | 镇魂之封灵法杖 | mage | staff | [mage] [at mage] [creator mage] | [weapon] 23 | 106030539 ([trade]) | 2651418 | 106030044 ([sealing]) |
| 106040047 | 镇魂之封灵扫把 | mage | broom | [mage] [at mage] [creator mage] | [weapon] 24 | 106040416 ([trade]) | 2651419 | 106040048 ([sealing]) |
| 108000044 | 镇魂之封灵十字架 | priest | cross | [priest] | [weapon] 23 | 108000440 ([trade]) | 2651420 | 108000045 ([sealing]) |
| 108010042 | 镇魂之封灵念珠 | priest | rosary | [priest] | [weapon] 23 | 108010410 ([trade]) | 2651421 | 108010043 ([sealing]) |
| 108020041 | 镇魂之封灵图腾 | priest | totem | [priest] | [weapon] 23 | 108020392 ([trade]) | 2651422 | 108020042 ([sealing]) |
| 108030040 | 镇魂之封灵战斧 | priest | axe | [priest] | [weapon] 24 | 108040398 ([trade]) | 2651423 | 108030041 ([sealing]) |
| 108040041 | 镇魂之封灵战镰 | priest | scythe | [priest] | [weapon] 24 | 108030444 ([trade]) | 2651424 | 108040042 ([sealing]) |
| 109000042 | 镇魂之封灵匕首 | thief | dagger | [thief] | [weapon] 23 | 109000447 ([trade]) | 2651425 | 109000043 ([sealing]) |
| 109010041 | 镇魂之封灵双剑 | thief | twinsword | [thief] | [weapon] 23 | 109010414 ([trade]) | 2651426 | 109010042 ([sealing]) |
| 109020039 | 镇魂之封灵权杖 | thief | wand | [thief] | [weapon] 23 | 109030306 ([trade]) | 2651427 | 109020040 ([sealing]) |
| 109040072 | 镇魂之封灵苦无 | thief | chakraweapon | [thief] | [weapon] 17 | 109040283 ([trade]) | 10095704 | 109040073 ([sealing]) |
| 114000100 | 镇魂之封灵长枪 | demonicLancer | pike | [demonic lancer] | [weapon] 17 | 114000101 ([trade]) | 10154148 | 114000102 ([sealing]) |
| 114010086 | 镇魂之封灵战戟 | demonicLancer | halberd | [demonic lancer] | [weapon] 18 | 114010087 ([trade]) | 10154149 | 114010088 ([sealing]) |

注：

- 路径：镇魂模板在 `equipment/character/<职业目录>/weapon/<武器类型>/<ID>.equ`；设计图 2651400–2651427 在 `stackable/recipe/<ID>.stk`，10095704、10154148、10154149 在 `stackable/<ID>.stk`。Confirmed
- 开光产出的 ID 号段不连续，有时还落在别的武器类型号段里（例如东方棍 102030045 → 102040522，拳套 102040045 → 102030459）。映射只能从 `[emancipate][output]` 读，不能按 ID 规律推算。Confirmed
- 释魂苦无 109040073/109040283 和释魂长枪、战戟 114000101/102、114010087/088 没有 `[need material]`，其余释魂模板都带 `[need material] 10000204 100`。Confirmed
- 释魂模板自身没有 `[emancipate]` 块（它们都不在 `[emancipate]` 装备清单中），即释魂是终点。Confirmed
- 名字含“镇魂”但不属于本系列、已排除的条目：35925 `镇魂钩镰`；2224104 `密制镇魂安曲剑` 及其设计图 2654034 `(魔法封印) 镇魂安曲剑`；“镇魂曲 : 幽游板甲”套装（100090009 等 5 件，套装 11719）；圣职者时装“苍灵镇魂”系列；`chn_rental_*` 租赁武器（只在说明中提到“时空之门 - 镇魂曲”区域效果）。Confirmed

## 2. 镇魂 → 释魂由什么字段表达

PVF 中有**两条**并行的升级路径，都由数据声明（Confirmed）。

### 2.1 开光 / 转换：`[emancipate]` + `[emancipate ticket]`

- 武器侧：镇魂模板的 `[emancipate]` 块，包含 input 材料、output 目标和 `[emancipate type] 19`。
- 消耗券侧：`stackable/10150749.stk` `释魂之真灵转换书`，字段为 `[stackable type] [etc] 1`、`[emancipate ticket] 19`、`[need material] 10150748 10`、`[stack limit] 1000`、`[attach type] [trade]`、`[price] 1000000`。说明原文：“使用后， 可以将镇魂武器转换为无法交易的释魂之真灵武器。 材料道具与合成释魂之真灵武器的材料相同， 转换后保留装备的强化、 增幅、 锻造、 附魔， 以及异次元属性。”
- 匹配规则：券的 `[emancipate ticket]` 值等于装备的 `[emancipate type]` 值（这里都是 19）时券可用；转换消耗装备 `[emancipate][input]` 列出的材料，产出 `[emancipate][output]`。依据是两个字段同名同值，且全档案 ticket 值与 type 值成套出现（见 §6）。High
- 全档案只有 10150749 一张券的 ticket 值是 19。Confirmed
- 已知矛盾：说明写“转换为无法交易的”武器，但开光产出模板（如 101000632）的 `[attach type]` 是 `[trade]`；反而设计图产出（101000028）是 `[sealing]`。`[trade]`/`[sealing]` 在本客户端的实际交易语义，以及转换后服务端是否改写交易限制，都是 Unknown，需要客户端逆向或实机确认。
- 10150749 没有其他券（如 10149519）带的 `[emancipate grade max]`/`[emancipate amplify max]`/`[emancipate genuineGrade max]`。说明写保留强化、增幅、锻造、附魔、异次元属性。Confirmed（保留的强化等是否有上限：PVF 未声明，Unknown）
- `etc/equipmentlevelemancipate.etc`（按 rarity 消耗 3311，并按部位、等级、强化、增幅、锻造加权）更可能只服务于 ticket 5（次元能量补充卷轴，该券带 `[equipment level emancipate]`），没有任何地方把它与 type 19 关联。Hypothesis。服务端已有的 `ItemSnapshot.EmancipateEquipmentLevel`（核心字节 0x4B）也更可能属于这套“次元/等级开光”，与镇魂无关。Hypothesis
- 开光在客户端从哪里发起（右键使用券、拖拽，还是 NPC 界面）：PVF 里没有任何 NPC role 引用 emancipate，Unknown，需要客户端逆向。

### 2.2 设计图合成：`[stackable type] [recipe] 1`

31 张设计图除了输入、输出、失败返还的武器 ID 外内容完全相同（Confirmed）。样例 `stackable/recipe/2651400.stk` 的原文要点：

```
[name] `释魂之真灵短剑  设计图`
[grade] 84  [rarity] 6  [usable job] [all]  [attach type] [free]  [minimum level] 84
[price] 1000000  [weight] 10
[stackable type] `[recipe]` 1
[string data] `[weaving]`
[input item]  101000027 1  10000204 15  10000327 75  10000224 1  3167 250  3037 10000
[output item] 101000028 1
[need skill] (空)
[success rate] 1000
[maintain ability] `normal`
[broadcast type] 1
[fail output] 101000027 1  10150748 1
```

说明原文：“有一定几率将镇魂武器升级为释魂武器。 成功时， 会保留原武器的强化、 增幅、 锻造以及附魔效果， 且该武器为封装状态； 失败时， 则除武器之外的其它材料全部消失。(武器合成成功后， 武器特效将会消失。)”

- 成功率：`[success rate] 1000`，单位 Unknown（千分比即 100%，万分比即 10%）。说明写“有一定几率”，与 100% 不太相符，但无法据此判定单位。
- 失败处理：`[fail output]` 返还原镇魂武器 ×1，外加 `10150748 浓缩的灵魂` ×1；其余材料全部消失（说明原文）。Confirmed
- 保留属性：`[maintain ability] normal`；说明写保留强化、增幅、锻造、附魔。是否还保留异次元属性等其他属性，Unknown。
- 全档案 9,268 张设计图中，`[success rate]`、`[fail output]`、`[broadcast type]` 只出现在这 31 张上，是镇魂专用的概率合成格式（Confirmed）。`[maintain ability]` 共出现在 52 张设计图上：48 张值为 `normal`，4 张以 `infinite` 开头。
- 物品辞典：`etc/itemdictionary/(r)recipelistmakeequip.etc` 与 `(f)` 版本中有行 `101000028 2651400 0 0 0 1 132 0`，依次是产物、设计图、4 个标志、NPC 132（艾丽丝）。其他设计图的同一位置是 `0 0 1 0 0`，不带 NPC。列语义 Hypothesis。

## 3. 礼盒、材料、任务、NPC、商店

### 3.1 礼盒 / 选择箱（Confirmed）

| ID | 名称 | 类型 | 关键字段 |
|---|---|---|---|
| 10099772 | 镇魂武器礼盒 | `[booster selection]` 0，`[trade]` | `[need material] 10099671 140`（低级镇魂之石 ×140，即在艾丽丝处的兑换价，High）；`[booster category num] 1 8`、`[booster selection num] 2`。每个职业 × 转职类别：`[default select] 1 10000204`，`[equipment] 10000204 3` 加上本职业全部镇魂武器各 1。说明：“各职业可以选择一种相应的镇魂武器。 含有3个镇魂之石。”类别 0–7 依次为鬼剑士/黑暗武士、格斗家、神枪手、魔法师/缔造者、圣职者、暗夜使者、守护者（只有短剑和巨剑）、魔枪士 |
| 10003675 | 终极镇魂武器礼盒 | `[booster selection]`，`[account]` | `[booster equipment upgrade] 12`（+12）、`[expiration date] 2014-07-29 06:00:00`，属于国服活动 `chn_event_20140617_phoenix_weapon` |
| 10005172 | 镇魂武器礼盒 | `[booster selection]`，`[account]` | +12、`[expiration date] 2015-06-18 06:00:00`，国服新手活动 |
| 10101865 | 镇魂曲材料袋 | `[booster]` | `[booster info][etc]` 随机开出远古/镇魂曲材料，其中 10000327 灵魂之痕、10000207 圣洁之魂的权重各为 70；flavor 为“<每日挑战任务奖励>” |
| 10003826 | 镇魂曲地下城入场券礼盒 | 国服活动 | 只装入场券，未展开 |

两个 +12 礼盒都已过期，属于国服活动残留，是否需要支持由业务决定。

### 3.2 材料与货币（Confirmed）

| ID | 名称 | 类型 | 用途（出自说明原文或字段） |
|---|---|---|---|
| 10000204 | 镇魂之石 | `[quest]` 0，`[trade]`，rarity 3 | 开光、设计图各需 15；在艾丽丝处兑换镇魂武器（武器的 `[need material] 10000204 100`）；来自镇魂曲每日任务。requiem 秘密商店（`etc/secretshop.etc` 中 dungeon 200–206 各行）也有 `10000204 0 100000 0 1 2000`，列语义 Unknown |
| 10099671 | 低级镇魂之石 | `[quest]` 0 | 远古每日任务奖励；兑换礼盒 10099772 需 140 个 |
| 10000327 | 灵魂之痕 | `[material]` 0，`[free]` | 开光、设计图各需 75；可兑换时空之石（10000205 的 `[need material] 10000327 25`） |
| 10000224 | 小铁柱的火炉使用券 | `[material]` 0，`[trade]` | 开光、设计图各需 1；**龙跃升星也要用**（见 §6） |
| 3167 | 上级元素结晶 | material | 开光、设计图各需 250 |
| 3037 | 无色小晶块 | material | 开光、设计图各需 10000 |
| 10150748 | 浓缩的灵魂 | `[material]` 0 | 设计图失败时返还 1 个；在艾丽丝处兑换转换书（10150749 的 `[need material] 10150748 10`） |
| 10000207 | 圣洁之魂 | — | 每日任务 4533 收集物，需 15 个 |
| 10099672 | 微弱的灵魂 | — | 远古任务 1994 收集物，需 15 个 |
| 10000205 | 时空之石 | `[quest]` 0 | 艾丽丝商店“其他”页签商品，`[need material] 10000327 25` |
| 10099372 / 490002173 / 490002489 | 镇魂曲入场券 | — | 入场材料，未展开 |
| 10099377 / 10089053 | 镇魂钟 / 镇魂铃 | — | 前者是地下城 159 内使用的道具，后者是任务道具，都与武器无关 |

`[need material]` 写在装备或消耗品上时，表示在 NPC 商店兑换该物品要付的材料（High）。依据：镇魂之石、浓缩的灵魂、灵魂之痕的说明都写着“在艾丽丝处兑换”，与艾丽丝商店的商品一一对应。商店是否还额外收金币（镇魂武器 `[price]` 144000、转换书 1000000）：Unknown。

### 3.3 NPC 与商店（Confirmed）

- NPC 132 是 `npc/iris2.npc` `艾丽丝`（field name `吟游诗人艾丽丝`，field role `高等装备商人`），`[role] [item shop] 63 [play quest movie] 0`。镇魂曲城镇地图 `map/timegaterequiem/timegaterequiemwr.map` 中有 `[NPC] 132 [right] 410 187 0`。
- 城镇：town 13 `TimeGateRequiem.twn` `时空之门 - 镇魂曲`，`[permission]` 为 `[need quest] 12855`（`ancient/opengate_ancient.qst`）且 `[need level] 85`。进出城镇的 NPC 是 129 `requiem_move.npc`、131 `requiem_back.npc`。
- 商店 63 是 `itemshop/iris2.shp`：`[type] [weapon shop]`，`[use category] basic job`，按职业分类。category id 即职业编号，对照内容推断为：0 鬼剑、1 格斗（男）、2 神枪（男）、3 魔法（女）、4 圣职、5 神枪（女）、6 暗夜、7 格斗（女）、8 魔法（男）、9 黑暗武士、10 缔造者、11 鬼剑（女）、12 守护者、13 魔枪（High）。“商店”页签每个类别都卖：礼盒 10099772、本职业的镇魂武器、对应设计图、转换书 10150749；“其他”页签卖 10000205 时空之石。

### 3.4 任务（Confirmed；全部为 `[npc index] 132`、`[level] 85 99`、`[pre required quest] 12855`）

| 任务 ID | 文件 / 名称 | grade | 内容 | 奖励 |
|---|---|---|---|---|
| 4540 | requiem/opengate_1.qst `镇魂曲` | `[achievement]` | `[seeking]` 收集 3262 金色小晶块 ×100，完成后开启该模式 | 无物品 |
| 4533 | requiem/requiem_day01.qst `真远古 - 灵魂的救赎` | `[daily]` | `[seeking]` 10000207 ×15 + 10000327 ×15；`[monster reward item]` 让 dungeon 200–206、5047–5053 掉落 10000207 | 10000204 ×1 |
| 4534, 4615–4620 | requiem_day02*.qst `真远古 - 镇魂曲之净化` | `[daily random]` | `[clear a dungeon]`：通关 魂·王的遗迹（200/5047）至 魂·卡勒特指挥部（206/5053）之一 | 10000204 ×1 |
| 1994 | requiem/ancient_day01.qst `远古 - 低级镇魂之石` | `[daily]` | 收集 10099672 ×15 + 10000327 ×15 | 10099671 ×1 |
| 1995–2001 | ancient_day02–08.qst `远古 - 黑暗净化` | `[daily random]` | 通关远古地下城（33/1500/1501/1502/1504/1506/1507 及 5040–5046）之一 | 10099671 ×1 |
| 8538–8554, 8567 | requiem/event/*.qst | 活动版 | 结构与上面相同 | 未展开 |

`requiem_re01–03.qst`（`灵魂的慰籍 (重复)`、`前往时空之门 - 镇魂曲`、`前往时空之门 - 镇魂曲 (重复)`）存在于档案中，但没有登记在 `quest.lst` 里，是不可达的死数据。Confirmed
每日挑战 `n_quest/dailychallenge/challenge_225、270–291.qst` 的文本提到镇魂曲，本次未展开；材料袋 10101865 的 flavor 写的是每日挑战奖励。

## 4. 职业与武器类型对应（Confirmed，取自 `[usable job]` 与目录）

| 职业目录 | 武器类型（镇魂 ID） | `[usable job]` |
|---|---|---|
| swordman | ssword 101000027、hsword 101010044 | swordman、demonic swordman、at swordman、**knight** |
| swordman | katana 101020046、club 101030041、beamsword 101040045 | swordman、demonic swordman、at swordman |
| fighter | knuckle 102000056、gauntlet 102010113、claw 102020049、tonfa 102030045、boxglove 102040045 | fighter、at fighter |
| gunner | revolver 104000046、automatic 104010040、bowgun 104020042、musket 104030049、hcannon 104040044 | gunner、at gunner |
| mage | spear 106000044、rod 106010043、pole 106020041、staff 106030043、broom 106040047 | mage、at mage、creator mage |
| priest | cross 108000044、rosary 108010042、totem 108020041、axe 108030040、scythe 108040041 | priest |
| thief | dagger 109000042、twinsword 109010041、wand 109020039、chakraweapon 109040072 | thief |
| demonicLancer | pike 114000100、halberd 114010086 | demonic lancer |

`[equipment type] [weapon] N` 的第二个值（17/18/23/24）因武器而异，含义 Unknown。

## 5. `PvfRecipeContentSource` 覆盖度

源码：`Server/src/Dfo.Server.Infrastructure/Content/PvfRecipeContentSource.cs`（group8 分支）。

结论：**31 张释魂设计图全部会被拒绝（`Read` 返回 null）**，现有读取器表达不了。Confirmed（静态读码所得，未运行验证）

| 原因 / 字段 | 现状 |
|---|---|
| `[stackable type] [recipe] 1` | 读取器只接受第二个值为 `0`，这 31 张都是 1，第一道检查就返回 null。全档案 9,268 张设计图中值为 1 的有 8,649 张、值为 0 的只有 491 张，可见现有读取器只覆盖了少数设计图 |
| `[success rate] 1000` | 带这个字段直接返回 null（代码注释写明：概率配方不得默认按成功处理） |
| `[fail output]` | 读取器只认 `[fail item]`/`[failure item]`，不认 `[fail output]`。即使放开前两项检查，失败产出也会被**静默忽略** |
| `[maintain ability] normal` | 不读取。`RecipePlan(Required, Produced)` 没有“继承输入装备的强化、增幅、锻造、附魔”这一语义 |
| `[broadcast type] 1` | 不读取（推测是全服广播，Hypothesis） |
| `[string data] [weaving]` | 不读取，也不会因此拒绝（与职业制作技能有关；`[need skill]` 为空） |
| 装备作为输入/输出 | `RecipeAmount(ItemId, Count)` 在语法上能表示 `101000027 1`，但它是按数量扣减的模型，既不能指定玩家选的是哪一件装备实例，也不能把实例属性带到产物上。下游制作服务能否支持，本次只读未深入；推测现有普通设计图流程是按可堆叠材料设计的（Hypothesis） |
| 开光 `[emancipate]` / `[emancipate ticket]` | 完全不在设计图读取器的职责内，需要单独的内容源 |

## 6. 与 #281 龙跃武器共用的机制

- **龙跃与镇魂共用 `[emancipate]` 块和 `[emancipate ticket]` 券机制。** Confirmed
  - 龙跃武器（例如 `101000042 龙跃之短剑[1星] - 剑神`）的 `[emancipate]` 块包含 `[equipment grow] 1 1`、`[input] 10000224 1`、`[output] 101000043 1`（升到下一星）。7 星模板带 `[emancipate explain]`，没有 `[input]/[output]`。龙跃的块里**没有** `[emancipate type]`。全档案带 `[equipment grow]` 的龙跃模板共 1204 个。
  - 龙跃用的券：
    - `10012055 初级龙跃之息卷轴`：`[emancipate ticket] 2`、`[emancipate grow apply] [group] 1 4 3 4 5 6`。说明：第 4 阶段起必需，使用后原装备的附加效果消失。
    - `10012056 高级龙跃之息卷轴`：`[emancipate ticket] 3`，grow apply 与初级相同，另有 `[emancipate material revision] -1 100`。说明：使用后保留强化等效果。
  - 成长经验表：`etc/equipment_grow.etc`（group 1，max level 7，`[exp base]` 1–7，含 fatigue 惩罚）。
  - 共用材料：`10000224 小铁柱的火炉使用券`，龙跃每升一星需要 1 个，镇魂开光和设计图合成也各需要 1 个。
- 全档案 ticket 值与券的对照（字段原文 Confirmed，ticket 与 type 的配对关系 High）：
  - 0/1 勇者装备改造券（低级/高级）
  - 2/3 龙跃之息卷轴（龙跃装备没有 type，推测靠 `equipment grow` 匹配，Hypothesis）
  - 4 远古神器装备进化书
  - 5 次元能量补充卷轴（等级开光）
  - 6 远古传说装备进化书
  - 7 决斗场装备转换书
  - 8–17 魔法封印装备圣物转换书（按等级段区分）
  - 18 传承装备圣物转换书
  - 19 释魂之真灵转换书
- 装备上 `[emancipate type]` 各值的模板数：4:191、6:296、7:305、8:38、9:93、10:98、11:108、12:118、13–17 各 59、18:938、19:31。
- 实现建议（仅供参考，不属于本任务）：镇魂和龙跃用的是同一个“装备开光/转换”通用机制，适合共用一个 emancipate 内容源和一套执行规则，再按券上的附加字段（grow apply、material revision、grade/amplify/genuine max）区分行为。

## 7. 未知项清单

1. `[success rate] 1000` 的单位，以及服务端如何取随机数。
2. `[trade]` 与 `[sealing]` 的实际交易语义；转换书说明（“无法交易”）与开光产出模板（`[trade]`）的矛盾。
3. 开光在客户端从哪里发起、走什么协议（例如使用券后选择目标装备），需要客户端逆向。
4. 商店兑换除 `[need material]` 外是否另收金币。
5. `recipelistmakeequip.etc`、`secretshop.etc` 的列语义。
6. 以上结论只在 A21-debug 的 PVF 上核对过；主干实际运行用哪份 PVF，要等服务启动后从 `PVF_ARCHIVE_PATH` 确认。
