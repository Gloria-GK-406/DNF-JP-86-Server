# 宠物领域怎么建

2026-09-05，基准 `main` @ `51cd99c2`。**分析文档，未改动任何代码。**

配套：[会话大聚合剩下三块的清点](CharacterEntryFacesInventory.md)、
[角色状态按领域重切](CharacterDomainCut.md)、
[Domain Instance Design](Refactor/DomainInstanceDesign.md)（本仓既有的领域对象定式）、
[Table Ownership Ruling](Refactor/TableOwnershipRuling.md)（持久层裁定，已落地）。

## 一句话

**宠物是三层东西，今天三层都在，但只有最外一层被用来做判断。**
档案很厚、库表齐全、帧全部对得上，缺的是中间那层——**没有实例、没有动词、没有写入者**。

## 一、宠物今天被切在五处

| # | 位置 | 承载什么 | 谁写 |
|---|---|---|---|
| 1 | `items` 行（`list_type=3`，`slot_index=24`，`core.ItemKind=5`） | **装备着哪只**：`ItemId`（装备模板 id，63000+）＋ `Value`（＝ `creature_key`） | **无人写**。建号只种六行，不含它；`MoveItemSpace` 拒绝装入 |
| 2 | `character_creatures` 行（键 `character_id + sort_order`） | **那只宠物的实例状态**：饱食度、经验、等级、名字、模式（11 列） | **只写 `field04` 一列**，且只有 UPDATE，**全仓零 INSERT** |
| 3 | `character_condition` 五列（`creature_field1..4`、`creature_buffer`） | 未知。只有 `field2/3/4` 上线（subtype-0），另两列连协议位置都没有 | **无人写** |
| 4 | `character_stats.equipped_creature_level` 一列 | 宠物等级；任务宠物门槛的唯一判据 | **无人写，恒 0** |
| 5 | PVF 档案 | 种类定义：等级上下界、三格艺术品槽、进化链、技能、喂养成长 | 只读 |

**注意 2 与 4 的粒度不同**：`character_creatures` 是**每宠物一行**，
`equipped_creature_level` 是**每角色一列**。今天两边都没有写入者，所以
**两种读法都还开着**——这是必须先裁的分叉之一。

**3 不要急着并进宠物。** `TableOwnershipRuling` 的 D-2 说这五列概念上属于宠物但键形不同搬不动。
补一条它没说的：这五列今天**不是等待搬家的数据，是三个空位加两个死列**。
`creature_field1` 与 `creature_buffer` 连协议位置都没有。**在含义查清之前不要动它们。**

## 二、档案里有什么，今天读了多少

**档案很厚。** `creature/creature.lst` 486 条（id 1..20042），`equipment/creature/` 850 条，
外加 `creature/exptable.tbl`——**55 级的宠物经验阈值表**。

**今天只读了 12 个标签**（`PvfCreatureDefinitionSource.cs:318-329`）：
`[name]` `[start level]` `[permission level]` `[max level]` `[artifact slot]`
`[evolution level]` `[evolution quest]` `[evolution creature id]` `[ambient]`
`[equipment type]` `[output index]` `[room list]`。

**零引用的（`Server/src` 与 `Server/tests` 都搜过）**：

- **`creature/exptable.tbl`** —— 宠物升级曲线在服务端**不存在**
- **`[creature species]`** —— 见下，这是最要紧的一条
- 全部技能字段、全部喂养成长字段（`[selective grow creature]` 整块）、
  全部艺术品属性（含 `[creature food consume rate]`——**饱食度消耗速率**）
- `[artifact slot]` **读到了但零消费者**

### 装备到宠物的映射今天用错了方法

档案自己给出了显式映射：`equipment/creature/*.equ` 的 **`[creature species]`**，850 条里 705 条带它。

本仓用的是**文件名 stem 匹配**（`PvfCreatureDefinitionSource.cs:143-151` 的 `StemOf`）。

| | stem 法（今天） | `[creature species]`（档案自己的） |
|---|---:|---:|
| 能解析的装备 | **148** | **705** |
| 能反查到的宠物 | 148 | 454 |
| 宠物蛋（`sub type 1`，239 条） | **0** | 239 |

两法在那 148 条上结论完全一致，**所以 stem 不给错答案，只是漏掉约 79%**——
包括**全部宠物蛋**（文件名带 `egg_` 前缀，stem 必然对不上）。
`creature.lst` 的 486 个 id 里有 **338 个**今天永远无法通过装备反查到。

**这是唯一一条「修缺陷」而非「加功能」的改动。**

顺带一条注释漂移：`PvfCreatureDefinitionSource.cs:103-105` 写「408 entries share 332 stems」，
实测是 **486 条 / 384 个 stem**。

## 三、上游把字段名都确认了

`protocol/game/server-to-client/NOTI-0x0069.md` 给出逐字段的语义与证据行：

| 本仓字段名 | 真实语义 | 置信度 |
|---|---|---|
| `PetKey` | 实例唯一键（`CreatureDetail.Uid`） | Confirmed |
| **`Field04`** | **饱食度 Stomach**——`Stomach > 0` 判定存活，**归零即饿死** | High |
| `ModeFlag` | 长度判别位（==1 时多写两字节） | Confirmed |
| **`ProgressValue`** | **经验 Exp** | High |
| **`FieldAfterValue`** | **等级 Level** | High |
| `CreatureText` | 名字，改名接口限 13 字节 | High |
| `Mode1Field0A/0B` | 上游自己只标「透传，语义未知」 | Confirmed |
| `TailFlag` | **上游字段表里没有这一字节** | **未查清** |

**这改变了「宠物复活」这件事的性质**：`field04` 是饱食度，
`ReviveSelectedCharacterPetCommand` 实际做的是「把饿到 0 的宠物喂到 1」。
而饱食度**会随时间与出勤下降**（消耗速率在艺术品档案的 `[creature food consume rate]` 里），
所以**宠物领域天然带一条周期性的账**。

## 四、设计

### 4.1 三层，各归各位

| 层 | 是什么 | 归谁 | 今天 |
|---|---|---|---|
| **种类** | 这一种契约兽：等级上下界、三格艺术品槽、进化链、技能、喂养规则 | **规则服务**（吃档案） | 有源，只读 12 个标签 |
| **实例** | 这一只：饱食度、经验、等级、名字 | **实例** ＋ **store** | store 有，实例没有 |
| **出战** | 这个角色现在带着哪一只 | **穿过角色实例的一个问题** | 只有一处裸查询 |

**「三个装备槽」属于第一层，不是第二层。** `CreatureDefinition.ArtifactSlots` 已经解析出来了
（判据断言 creature 1 = `red/blue/green`），它是**种类的属性**。
它**不是** `InventoryContainerType.Pet = 7` 那个容器——两者在代码里毫无关联。

### 4.2 怎么和角色关联：按 R-3，穿过角色实例

`DomainInstanceDesign` 的 R-3 已经定了形状：

> 角色实例是**入口**，不是容器。其余一切都是穿过它到达的，各自住在自己的实例里：
> `character.Skills`、`character.Quests`、`character.Bags`。
> 接缝放在**和表被切开的同一批地方**。

所以宠物的关联形状不需要新设计：**角色实例交出一个仓库，仓库交出快照或实例。**

三种对象各自是：

- **仓库**（哪一只）：列出这个角色的全部契约兽、按 `creature_key` 取一只、
  答「现在出战的是哪一只」（要读 24 槽）、创建（孵蛋）、释放。
- **实例**（能做什么）：动词是这个领域的词汇——喂食（提饱食度）、获得经验（可能升级）、
  改名（13 字节上限）、进化、艺术品的装与卸。
  **它持有身份与依赖，不持有状态**（R-1）。
- **规则服务**（档案怎么说）：给定经验求等级、给定等级求下一级阈值、进化可行性、
  喂养分支、艺术品门槛与加成、饱食度消耗速率。**它不解析身份、不读库**（R-6）。

**注册还是按需？按 R-4，按需。** R-4 的判别是「有没有一个必须活过单次调用的选择事实」。
包有——一个角色同时持有好几个同类型的包，必须有东西说现在用哪个。
宠物**没有**：一个角色可以拥有多只，但「哪只出战」这个事实**存在 `items` 24 槽里，
是持久事实而不是运行态注册**。所以仓库按需建实例，和角色实例一样。

**命名上的一条既有约束**：本仓 `store` 只指事务结算的真源且以 `DomainStore` 结尾。
宠物的仓库是领域服务，**不能叫 store**。

### 4.3 持久层：store 已经在了，但只做得了一件事

`ICharacterDomainStore.Creatures` 是
`IDomainStoreSession<OwnedCharacterId, long, StoredCreature>`，已存在。今天的能力是
**只写 `field04` 一列，且只有条件 UPDATE，没有 INSERT**。

要扩的两件：

1. **写全 11 列**（至少 `field04` / `progress_value` / `field_after_value` / `creature_text`）
2. **INSERT** —— 今天全仓零 INSERT，**服务器根本产生不出一只宠物**

**保留一个形状**：现有写操作把前置条件封进值对象里
（`StoredCreature(key, 1, OnlyWhenAtMostZero: true)` → `UPDATE … WHERE field04<=0`，
影响行数不为 1 就抛）。**新动词也应当自带前置条件，而不是让调用方先查后写。**

## 五、必须先裁的五个分叉

**这五个不裁，实现无法开始。**

### D-a 宠物槽是 24 还是 25 —— **可能阻塞**

同一个事实今天有**五份副本、三个答案**：

| 来源 | 说法 |
|---|---|
| `SelectCharacterPostEntryServices.cs:97`、`InventoryEntryServices.cs:336`、`Entry.cs:305`、`SelectCharacterPreparationService.cs:448` | 24 |
| **`PvfEquipmentSlots.cs:38-39`（权威表）** | **24 是 `[earring]` / `[support weapon]`** |
| 上游 A21 的 `EquipmentType`（`ProtocolScaffoldDissonances.md` 第九节） | **25 才是宠物** |

那条悬案自己写着「两个数字**都没有线上证据**……**等到有一个真的戴着宠物的样本再判**」。

**它还带着 GM 文档那三个宠物装备槽一起位移**（`list_type 3` 的 25-27）。
**先裁这个，否则后面全部要返工。**

### D-b 宠物等级归每宠物还是每角色

`character_creatures` 是每宠物一行，`equipped_creature_level` 是每角色一列。
两边都没写入者，两种读法都开着。

后果不是抽象的：因为那一列恒 0，`WorldContentEntryRuleService.cs:139` 的 `<= 0` 分支保证
**任何带宠物等级要求的任务今天永远不会被提供**。

### D-c `creature_key` 谁分配

它是**实例 UID**，不是档案 id——取自 `ItemCore.Value`。GM 文档提到一张
`character_creature_uid_sequence` 自增分配表，**但它不在今天的 schema 里**（实测 0 处命中）。

孵蛋要产生新实例，就必须先答这个。

### D-d `Pet = 7` 容器到底装什么

它是真在加载的容器，但**恒空**，而且**帧里连容量都不写**（Pet 分支比其它容器少一个 u16）。
GM 文档说它三段分区（0-139 宠物 / 140-188 宠物装备 / 189-239 宠物消耗品），
**代码里一个执行者都没有**，也没有抓包对账。

若三格艺术品要落在 `list_type 3` 的 25-27，那 `Pet = 7` 装的就是别的东西。**这两件要一起裁。**

### D-e `TailFlag` 是否真在线上

服务端在名字 dstr 之后**又写一个 u8**，而上游字段表**列到名字就没有了**。
它落在**变长字段之后**，所以它的存在与否**直接决定条目边界**。

## 六、帧钉死的、不能自由选择的形状

- **`sort_order` 是 wire order。** `0x0069` 按传入顺序逐条写，计数是 **u8**——
  最多 255 只，且必须有稳定全序。`sort_order` 不是可重构掉的实现细节。
- **条目变长**：`ModeFlag == 1` 多两字节 × 名字是 `dstr`（i32 长度前缀）＝ 四种长度。
  任何按固定 stride 遍历的代码都是错的。
- **「没有宠物」与「有宠物但没名字」在 subtype-0 里是两种不同的字节**
  （前者是 `0xFFFFFFFF` ＋ 客户端的「没有宠物」文案 ＋ `0xFF`）。
  领域服务必须能区分这两个状态，**不能用空名字统一表示**。
- **subtype-0 的「哪一只」由装备容器决定**：先在 24 槽找 `ItemKind == 5`，
  拿 `Core.Value` 去宠物表 `SingleOrDefault`。三条硬约束：
  两个字段共用同一编号空间、**`PetKey` 在一个角色内必须唯一**（而库的主键是
  `(character_id, sort_order)`，**唯一性今天没有约束保证**）、
  以及装备在而宠物行不在时会**静默输出一个半状态**。
- **`character_condition` 三列在帧里的位置与 triple 不相邻**，中间隔着五个字段。
  即使将来把这五列搬进宠物领域，**它们在帧里的位置不能跟着搬**。
- **入场帧序对着抓包定死**：`0x0069` 在容器快照之前发。

## 七、两条潜伏缺陷——这个块自己会踩响的雷

**两条都要求先有一只宠物，而服务器今天产生不出宠物**（`character_creatures` 零 INSERT）。
所以它们**不是今天在漏的血，是这个块动工时会引爆的雷**。

### 缺陷一：出战信息用的是入场快照，别人看到的是饿宠

`SelectCharacterHandler.cs:293` 广播 `PvpParticipantPublished`，
`:328` 才执行喂食。`CreatePvpParticipant`（`SelectCharacterLifecycleServices.cs:230`）
取的是 `result.CharacterEntry.Features.Pets`——**快照，不是那个已经存在的领域服务**。
`PvpParticipantPublished` 全仓只有一个发布点，入场后永不重发。

**帧序是抓包定死的，所以「把喂食提前」不是选项。** 要动的是取数来源，
或者喂食之后补一次公示。

### 缺陷二：单向丢宠

- **装不进去**：`PutOnAsync` 走 `WornSlotForAsync` → `PvfEquipmentSlots.ByName`，
  那张表**没有 `[creature]` 键** → `NotWearable`
- **拖得出来**：`TakeOffAsync` 走 `MayOccupyMainSlotAsync`，把宠物当**普通装备**处理，
  目标槽落在 9..64 就放行

**拖出来可以，装回去不行。** 而且同一张表写着 `[earring] = 24`——
**玩家戴一只耳环就会把 24 槽的宠物顶进背包**，然后再也装不回去。

顺带一处不一致：`item_core.ItemKind` 对 24 槽宠物记 **5**，
而档案解析器对同一件物品会给出 **1**（`PvfEntryLifecycleContentSource.cs:101-102`）。

## 八、建议的切分

**这一块不该一个成果做完。** 建议三刀：

**第一刀「宠物有一个主人」** —— 口径：**行为逐值不变**。
建仓库＋实例＋规则服务的骨架；把 `equipped_creature_level` 与 `Features.Pets` 的读点改问主人；
**顺带修缺陷一**。不新增协议帧，不动帧序。

**`[creature species]` 那条修复要不要放进第一刀，需要单独裁**：
它**会改变可观察行为**（多 557 条装备可解析、宠物蛋第一次可解析），
和「逐值不变」的口径冲突。要么单独一刀，要么把第一刀的口径改成
「只在这一处改变行为，且逐条列出差异」。

**第二刀「宠物活着」** —— 口径：**必须改变可观察行为**。
饱食度真的消耗与恢复、经验与等级真的记（要读 `exptable.tbl`）、
`character_creatures` 的 INSERT 与 `creature_key` 分配、修缺陷二（补 `[creature]` 槽位）。
**这一刀依赖 D-a、D-b、D-c 三个裁定。**

**第三刀「宠物有装备」** —— 三格艺术品、艺术品属性加成、`Pet = 7` 容器的用途。
**依赖 D-a 与 D-d。**

**技能不在任何一刀里**：`[skill list]` / `[skill node]` 那一大块是否需要进服务端，
取决于伤害结算在哪一侧——**本次调查没有查清，不要凭空排它。**

## 九、这份分析没做的

1. **没有核对 A21 抓包。** 五个分叉里 D-a、D-d、D-e 都需要线上证据，读代码得不出。
2. **没有查清伤害结算在哪一侧**，所以宠物技能要不要进服务端没有答案。
3. **`mode_flag` / `mode1_field0a` / `mode1_field0b` / `tail_flag` 的语义查不出**——
   上游自己就是透传。不要猜。
4. **`creature/exptable.tbl` 的 55 行与 `[max level] 50` 的对应关系未查清**
   （是 1→2..55→56？还是含前置行？）需要实机或上游对照。
5. **没有估工作量。**
