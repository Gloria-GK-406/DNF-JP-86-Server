# 客户端扩展面：不写原生代码能做到哪一步

一次性摸底的留档。**这不是当前路线**——现阶段目标是重建服务端，扩展能力是之后的事。
写下来是因为这些结论的取证成本远高于阅读成本，等真要动手时不必重来一遍。

其中一条与服务端重建**有直接交集**，见[第七节](#七双端约束与服务端重建的交集)：
装备的 `[if]/[then]` 规则引擎决定了大量装备的实际战斗行为，服务端迟早要消费它，
不是扩展需求而是正确性需求。

同目录另两篇：[ScriptRuntimes.md](ScriptRuntimes.md)（脚本运行时与地址）、
[BehaviorDataModel.md](BehaviorDataModel.md)（`.act` / `.obj` / `.atk` / `.apd` 的表达力）。

## 取证基线

归档 `DNFClient-play/Script.pvf`（2015-09-24，588,559 文件），
读法见 [ScriptRuntimes.md](ScriptRuntimes.md#取证基线)。本文统计由这些命令复现：

```text
PvfProbe tags   <ext>                        某文件类型的标签词汇表
PvfProbe values equipment/equipment.lst <tag>  某标签在该 .lst 全部文件中的取值分布
PvfProbe files  [substring]                  文件表
```

`[if]` / `[then]` 块内词汇表与 `.xui` 的 XML 词汇表由一次性扫描得出，
判定规则记在各节内，可按同样口径复算。

## 一、三层扩展面

| 层 | 需要什么 | 覆盖范围 |
|---|---|---|
| **A 纯数据** | 只改 PVF | 装备特殊效果、新副本、新任务、新掉落、新怪物行为、新实体、UI 展示层、征用空置装备槽 |
| **B 数据 + 少量原生绑定** | 插件绑 2–3 个函数 | 从 Lua 主动生成实体 / 挂 appendage / 加载新 `.xui` |
| **C 必须写原生代码** | C++ | 角色新动作状态、新装备槽、新可交互控件、任何引擎未建模的语义 |

A 层比预期宽得多，下面逐条展开。

## 二、装备规则引擎：最宽的一条

`.equ` 里嵌着一套**事件-条件-效果**规则引擎。全库 11,420 个文件含 `[if]`，
其中 **8,667 个是 `.equ`**。

### 2.1 语法

```text
[if]
  [module] [dungeon type] [war room] [assault] [/module]   环境过滤
  [attack success] 1                                        事件
  [cooltime] 30000                                          内置冷却
[/if]
[then]
  [target] myself -1
  [probability] 2
  [specified remain skill cooltime change] ...              效果
[/then]
```

最简形式（落炎之舞项坠，攻击时 20% 附加 50000 火伤）：

```text
[if] [attack success] 1 [/if]
[then] [target] enemy -1 [probability] 20 [add absolute damage] fire + 50000 [/then]
```

### 2.2 条件词汇（`[if]` 块内，39 种）

```text
事件    attack success 10933   event attack success 1254   use skill 406   event use skill 632
        hit 1012   casting 1590   use command 271   attack condition 273   damage check 45
门控    cooltime 8939   cooltime group 147   time 1969   probability(见效果侧)
        module 2618   dungeon check 687   disable pvp 55
自身    my state 385   set my state 217   my appendage 589   not my appendage 515
        my appendage count 69   gauge appendage state 232   my empty skill slot count 36
装备    any body equipped 468   not equipped index 434   check equipment rarity 80
        equipment upgrade 275   indexes 902   skill 262
目标    target 902   target type 235   target grade 131   target active status 710
        target death 50   distance 85   element 95   attack type 38   character 85
队伍    party count 160
其他    stat change 4877
```

`[module]` 是环境过滤器，取值如 `[dungeon type]` / `[war room]` / `[assault]`，
用来让效果在特定玩法下失效——决斗场平衡就是这么做的。

### 2.3 效果词汇（`[then]` 块内，60 种）

```text
选择    target 24158   probability 13665   then probability 6346   target case 14
        target item index 2   level condition 7   cooltime condition 8
数值    stat 8389   stat by condition 4151   stat change by stat 108   swap stat 2
        add absolute damage 5467   add absolute damage with item stack 2
        increase damage 440   increase critical damage 695   decrease critical damage 6
        add absolute defense 7   add absolute defense percent 81   reduction 19
        change value 14   restore 453   weakness 112   thorn 20
状态    appendage 7867   remove appendage 1152   appendage unique 4   duration 6626
        equipment duration 1504   aura 5762   aura active 13   active status 3076
        apply status in range 101   buff 259   elemental weapon 249
技能    skill levelup 326   skill data up 98   skill duration 33   skill super armor 37
        remain skill cooltime change 39   specified remain skill cooltime change 14
        end skill 3   add skill summon 8   perform skill 20
实体    passive object 874   summon monster 199   summon apc 102   event summon monster 5
表现    replace avatar ani 156   speech on 81   knockback 4   disenchant 37
        consume item 573   unit of measure 2   maximum coverage levels 2
PvP 修正  reduce probability at pvp module 1556   reduce duration to human armor … 852
        reduce probability to human armor … 403   reduce duration at pvp module 85
        reduce probability at pvp type module 9
```

### 2.4 四条通往行为系统的桥

`[passive object]`、`[summon monster]`、`[summon apc]`、`[perform skill]` 让规则引擎
接上 [BehaviorDataModel.md](BehaviorDataModel.md) 描述的那套数据驱动行为系统：

```text
装备 → [if]/[then] 规则 → [passive object] → .act 行为脚本 → 生成子对象 / 打伤害 / 播动画 / 递归
```

**一件装备可以在特定条件下召出一个按脚本演戏的实体，全程零代码。**
这是 A 层最深的一条链，也是"新机制"最现实的载体。

## 三、UI：按三种情况分开判断

867 个 `.xui`，UTF-16 声明式 XML，**60 种控件**：

```text
CNUIControlImage 5415   CNUIControlText 3679   CNButton 1900
CNUIControlAnimationForRDAR 1094   IControl 1031   ItemControl 631
POPUPWINDOW 576   CNCheckBox 563   CNBoardImage 312   CNToolTips 225
CNLabelHyperLink 206   CNTab 175   UIContainer 159   CNComboBoxEx 80
CNUIControlNumberSet 65   CNGauge 45   CNRadioButton 45   CNUIFace 42
CNTree 39   CNChangeableButton 39   CNImagePattern 36   ControlMatrix 27
CNFilteringBoard 26   CNFreeSizeButton 24   CNPageButton 20   CNVScroll 17
CNUIControlGrid 15   CNWebImage 11   CNList 8   CNSlide 8   CNSmoothGauge 1 …
```

| 想做的事 | 判定 |
|---|---|
| 改现有窗口外观，加图片 / 文字 / 装饰 | **A 层**，改 `.xui` 即可 |
| 加**可交互**控件 | 画得出来，但事件按 control ID 由窗口的原生 handler 分派，新 ID 无人接管 → **C 层** |
| 全新窗口 | 需要 `.xui` → 窗口的加载器地址 → **B 层**，前置是 in-game dump |
| 主菜单增删条目 | **A 层**——`etc/channeluiinfo.etc` 按窗口名逐通道列出；但窗口名本身是原生枚举 |

`etc/channeluiinfo.etc` 的形状（按频道给出主菜单构成）：

```text
[channel] CHANNEL_ATTACK_ZONE [/channel]
[main menu]
MYINFO_WINDOW -1 -1  INVENTORY_WINDOW -1 -1  QUEST_WINDOW -1 -1  SKILL_WINDOW -1 -1
… AUCTION_WINDOW -1 -1  GUIDEBOOK_WINDOW -1 -1  GO_TO_TOWN 1 -1  EXIT_GAME -1 -1
[/mainmenu]
[town index] 15 6 [/town index]
```

**唯一找到的 `.xui` 数据绑定点是世界地图**：字符串池里有 80 条形如
`WorldMap/UI/Noirpera.xui` 的带路径引用，对应 `worldmap/ui/` 下的 41 个文件。
其余 826 个 `.xui` 在池中只以裸文件名出现，即由原生按窗口身份加载。
要试"能不能让客户端加载一个新窗口"，世界地图这条是最省力的切入。

> ⚠️ 「新 control ID 无人接管」是**推断**，依据是转储里 RTTI 露出的事件签名
> `void T::OnEvent(control_event::TYPE, int, int)`。未经实机验证。

## 四、装备槽：新槽位要代码，空槽位是现成的

`[equipment type]` 在 `equipment/equipment.lst` 所列 101,868 个装备上的取值分布——
这就是全部槽位：

```text
weapon 14973    coat 3753    shoulder 3562   ring 2753        support 2089     creature 608
pants 3707      waist 3543   amulet 2661     title name 1703  aurora avatar 322
shoes 3622                   wrist 2651      magic stone 1241 weapon avatar 303
avatar 11 种：coat 6973  hat 6887  pants 6807  shoes 6676  hair 6539
              breast 6353  waist 5932  face 5861  skin 2112
—— 以下几乎空置，合计 236 件 ——
artifact red 63   charm 49   artifact blue 45   artifact green 29   name tag 27   support weapon 23
```

30 项是**原生枚举**：加一个真正的新槽位要动枚举、背包布局与协议，属 C 层。

但最后那排——artifact 三色、charm、name tag、support weapon——**引擎已经建好、
内容却几乎空着**。把其中一个征用成新系统的载体是 **A 层**的事，
性价比远高于新增槽位。

## 五、机制层：310 张 `etc` 系统表

`etc/` 下 310 张顶层 `.etc`（含子目录共 1,021 个），每张是一个子系统的配置。抽样：

```text
actionpointsystem  hardcoremode  bloodwar  honorlevel  chronicle  bindsphere
amplifyitem/amplifyupgrade  equipmentpartset  equipmentlevelemancipate
equipmenteffectset  equipmentexcludeoption  randomoption  secretshop  questshop
hellparty  quickparty  skilloverlapping  skillfrequency  finishsystem
characlinksystem  ranksysteminfo  recycleecoitem  reseal  honorlevel
itemdropinfo_{common,monseter,clearreward}  independent_drop  armordropbygrowtype
pvp{parameter,colosseum,skillpoint,mapbasicdata,practiceailist}  fairpvp{equipment,stat}
channeluiinfo  hotkeysystem  chatsystem  gmlist  invalidname  slanglist
```

副本与任务同样是数据：

- **`.dgn` 1,233 个**：`maze info` / `size` / `start map` / `boss map` / `named monster`
  / `champion` / `difficulty` / `dungeon type` / `quest connection` / `required item`
  / `special passive object item` / `escape hell` / `revision table` / `worldmap pattern info`
  / `experience increasing point` / `no fatigue` / `prohibit practice`
- **`.qst` 6,986 个**：`condition message` / `type` / `sub type` / `reward type`
  / `reward int data` / `pre required quest` / `monster reward item` / `enemy reward item`
  / `dungeon info` / `dialog` / `npc index` / `complete npc index` / `target character`
  / `job` / `grow type` / `gold multiple` / `exposed by npc`

**新副本、新任务、新掉落规则、新套装/强化/附魔曲线全部是 A 层。**
只要"新机制"能映射到某张现有 `etc` 表已建模的语义，就不需要代码。

## 六、这套东西的性质：配置驱动，不是插件驱动

引擎留下的是三台**数据驱动的解释器**——`.act` 行为 VM（52,066 文件）、
`.equ` 规则引擎（8,667 文件）、310 张 `etc` 系统表。它们是给内部策划复用行为用的，
不是给外部扩展用的。这个区别决定了扩展工作的形状：

- **在已建模的语义内**：能力极强、零代码、组合性好（装备 → 规则 → 实体 → 行为脚本 → 递归）
- **越出已建模的语义**：几乎没有余地。**没有通用 hook、没有回调注册、
  没有对数据开放的脚本 VM** —— Lua 那条唯一的口子仍停在半成品
  （3 个 `clientonly/` 怪物，见 [ScriptRuntimes.md](ScriptRuntimes.md)），
  Squirrel 那条已被拆除

**边界不是"能力强弱"，而是"当年有没有想到这件事"。**
想到了的参数化得极其彻底；没想到的，一行都加不进去。

## 七、双端约束与服务端重建的交集

规则引擎、召唤、伤害、掉落都是**服务端权威**。双端读同一份 PVF，
所以数据只写一份，客户端演、服务端判。

当前 [`Dfo.Server.Infrastructure/Content/`](../../Server/src/Dfo.Server.Infrastructure/Content/)
下的 PVF content source 全是定义类的（creature、dungeon、quest、town、
initial skill、skill point、item placement、collection box 等），
**没有一个消费 `.equ` 的 `[if]/[then]`、`.obj`、`.act`、`.atk`**。

对服务端重建而言，这里有一条**不是扩展需求、而是正确性需求**的项：

> 8,667 件装备带 `[if]/[then]` 规则。只要这些装备能被穿上，它们的实际战斗行为
> 就由这套规则决定。服务端不消费它，等于这批装备的特殊效果全部失效——
> 这是重建的完成度问题，不是扩展。

其余（新 UI、新槽位、新机制）都可以安全地推迟到服务端主体成型之后。

## 八、未验证事项

1. **`.xui` 的加载入口地址** —— 决定"全新窗口"是 B 层还是 C 层。
2. **control ID 的事件分派方式** —— 决定"新可交互控件"能否降到 B 层。
3. **`[CREATE PASSIVEOBJECT]` 的原生入口地址** —— B 层所有实体生成能力的前置。
4. 上述三条共同的硬前置是**一份进游戏之后的内存转储**；现有两份都是早期阶段，
   只解出 373 个 RTTI。
5. **规则引擎的求值语义** —— 本文只枚举了词汇，没有验证多条件的组合方式
   （`[if]` 内多个条件是与还是序列）、`[then probability]` 与 `[probability]` 的区别、
   `[multiple then]` 的分支语义。真要实现服务端消费时，这些必须逐条对照实机确认。
