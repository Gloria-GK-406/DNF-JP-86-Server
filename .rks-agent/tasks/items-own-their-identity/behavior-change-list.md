# 行为变化清单 —— 「物品有自己的身份，槽只说它在哪」

本成果只授权**两处**行为变化（RQ-009、RQ-010）。下面逐条列出这两处本身、
以及每一处的**全部连带后果**——包括不穿到线上的那些。

**本文件分两部分，两部分合起来才是完整的变化面**：

1. **被授权的两处**及其连带后果（下面两节）。这个数不变，**它不会变成三处**。
2. **新增的生产拒绝路径（全部 13 条）**（同名一节）。它们**不是**被授权的行为变化，
   但它们确实是本轮新增的、代码可观察的东西，按同一把尺子必须列全。
   **初版这里写的是「清单之外没有其它行为变化。」——那句是假的，由完整变更评审 F-1 查出**，
   原因写在「我是怎么核的，以及这个核法漏掉了什么」一节的订正里。

- 基线：HEAD `51cd99c2`，十二个任务的改动**全部未提交**。
- 全量判据：**2216 通过 / 2 跳过 / 0 失败**（逐项目明细见文末）。
- 索引方式：**按内容索引，不写行号**——本仓有过在只追加的文件里列行号、随后行号全部漂移的先例。

---

## 授权变化一（RQ-009 / AC-011）：契约兽取下之后能装回去

### 起因：一个键

`Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs` 的 `ByName` 字典
新增 `["[creature]"] = 24`。**生产改动只有这一行。**
该字典今天共 **16 个键**（复算：weapon / title / title name / coat / shoulder / pants /
shoes / waist / amulet / wrist / ring / support / magic stone / earring / support weapon /
creature），映射到 13 个不同的槽号；`FirstWornSlot = 12`、`LastWornSlot = 24` 未动。

这一行只被**两处生产代码**读到——`WornSlotForAsync` 在 `Dfo.Server.Protocol` 里的两个调用点，
都在 `Game/MoveItemSpace/MoveItemSpaceSlice.cs`：`PutOnAsync` 一处、`TakeOffAsync` 判「被顶开的那件」一处。
（复算：`grep -rn WornSlotForAsync Server/src` 的命中里，接口声明 2 处、
`ItemMovementDomainService` 转发 1 处、`ItemPlacementRuleService` 实现 1 处，
**真正的消费点恰好 2 处**。）因此可观察的后果**至多**只能是下面四条。

### 后果 A：契约兽装得回去了 —— 授权变化本身

`MoveItemSpaceSlice.PutOnAsync`：背包里的契约兽瞄准装备槽 24，
从 `ItemMoveStatus.NotWearable` 变为 `Moved`，物品进入槽 24。
理由：`WornSlotForAsync` 现在对契约兽答 24，`slot is null` 那条分支不再命中。

判据：`Server/tests/Dfo.Server.Application.Tests/Game/CreatureGoesBackOnTests.cs`
的 `A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature`。

### 后果 B：槽 24 已有契约兽时，拖第二只进去成为互换

同一段 `PutOnAsync`：被顶开的那只回到契约兽出发的那个背包格。
这是 `PutOnAsync` 既有的「被顶开的那件放回源格」逻辑**第一次对契约兽可达**，
没有为它新加任何代码。

### 后果 C：从槽 24 取下时，目标背包格里放着另一只契约兽，现在是互换而不是拒绝

`MoveItemSpaceSlice.TakeOffAsync`：`WornSlotForAsync(displaced)` 从 `null` 变成 24，
于是 `displacedSlot != command.SourceSlotIndex` 这个比较在**源槽恰好是 24 时**不再成立。
**只在源槽为 24 时改变**；从 12..23 任一槽取下、目标格里放着契约兽，
仍然是 `DisplacedCannotOccupySlot`（24 ≠ 源槽），与本轮之前相同。

**这一条没有判据守着**（见文末「已知覆盖缺口」）。

### 后果 D：内部拒绝理由从 `NotWearable` 变成 `ItemCannotOccupySlot`

契约兽瞄准**非**契约兽槽位时，`PutOnAsync` 命中的分支从「`slot is null` → `NotWearable`」
变成「`slot.Value != DestinationSlotIndex` → `ItemCannotOccupySlot`」：
档案现在对契约兽有答案了，于是命中的是「槽不对」而不是「根本不可佩戴」。

**客户端字节不变**：`MoveItemSpaceSlice.ErrorCodeFor` 把
`ItemMoveStatus.NotWearable` 与 `ItemMoveStatus.ItemCannotOccupySlot`
**都**映射到 `MoveItemSpaceResponse.NotMovable`——同一个错误码、同一句提示。

**但它仍然列在这里**：服务器内部的拒绝语义确实变了。
**清单要记的是变化，不是只记穿线的变化。**

判据：同文件的 `A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused`。

### 明确没有变的（逐条查过）

- **角色创建**。这张表的第二个读者是 `PvfInitialEquipmentDefinitionSource`；
  档案里 14 个职业的 `[create equipment list]` 只用 `[weapon]`/`[coat]`/`[pants]`，
  没有任何一个用 `[creature]`。由
  `No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants` 守住。
- **其它槽位归属**。`[earring]`、`[support weapon]` 仍是 24，12..23 逐个未动。
- **24 还是 25 的裁定**。没有碰；悬案原样留在 `Docs/ProtocolScaffoldDissonances.md` 第九节。

---

## 授权变化二（RQ-010 / AC-012）：契约兽的种类判定取档案的解释

### 起因：一个常量选择

`Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs` 的
`WornPieceMetadata`：对 `ItemWorldClass.Creature` 给 `CreatureItemKind`（**5**）
而不是 `WornPieceItemKind`（**1**）。两个常量都在同文件里。

改的是**档案侧的读法**，存储侧一个字节都没动——存储与入场读法本来就用 5
（`EquippedCreatureQuery`、`SelectCharacterPostEntryServices`），是档案给的 1 与它们不一致。

这个数流到**三个**读 `placement.ItemKind` 的地方（复算：
`grep -rn "placement.ItemKind" Server/src` 命中 3 个文件、5 行）。逐个查过：

### 后果 A：写进存储的种类字节由 1 变 5 —— 授权变化本身

`ItemContainers` 的 `AddAsync` 把 `placement.ItemKind` 原样写进 core。
这正是 RQ-010 要的「存储与档案对上」。

判据：`Server/tests/Dfo.Server.Application.Tests/Inventory/CreatureKindAgreesWithTheArchiveTests.cs`。
**注意其中的恒真陷阱**：`AddAsync` 写进去的就是 `placement.ItemKind`，
所以「读回的字节 == 档案给的字节」这一条**单独看永远绿**。承重的是第三条断言——
两者对上的那个数**正是 `EquippedCreatureQuery` 读契约兽用的 5**。

### 后果 B：契约兽不再能被拖进快捷栏 3..8 —— 第二处真实的可观察行为变化

`ItemSlotRuleService.CanOccupyMainSlot` 走
`slotIndex >= QuickSlotStart && slotIndex <= QuickSlotEnd && AllowsQuickSlot(placement.ItemKind)`
（`QuickSlotStart = 3`、`QuickSlotEnd = 8`），而 `AllowsQuickSlot` 把
`KindCreature`(5) / `KindCreatureEquipment`(6) / `KindCreatureConsumable`(7) /
`KindAvatar`(8) / `KindAvatarEmblem`(9) **排除在外**。

档案原先给 1 → 契约兽能进快捷栏；现在给 5 → 被排除。

**这不是「推论」，是一处真实的可观察行为变化**，只是**没有任何既有判据覆盖它**，
所以它没有产生任何期望值改动。新行为与 `ItemSlotRuleService` 自己的注释一致
（「Creatures and what they wear or eat may not occupy one at all」），
旧行为才是档案与存储不一致的产物。

**不得因为「没有判据改动」就把它漏掉。**

### 后果 C：无 —— `ItemArrivalRuleService.BandsFor` 两条分支产出相同

种类 1 走 `placement.ItemKind == EquipmentKind` 分支，种类 5 走
`!ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)` 分支，
**两条分支产出完全相同**（只 yield 自己的槽段）。无变化。

另有一条下游 `GetDailyRefillRulesAsync → DailyRefillService`，**事实为空**：
既有判据把真档案全部六条补给规则钉死为 kind 4，`WornPieceMetadata` 走不到那里。

### 明确没有变的

- **时装那一对不修**：存储 8 / 档案 1 的不一致仍在。
  RQ-010 只授权了契约兽这一处，动时装是未授权的行为变化。

---

## 我是怎么核的，以及这个核法漏掉了什么

**做过的：**

1. 两处授权变化各自的**唯一起因**都收敛到一个符号，逐个数了它的生产消费点
   （`WornSlotForAsync` 恰好 2 处、`placement.ItemKind` 恰好 3 个文件），
   再逐个读了每个消费点的分支，判断它是否会因这一个值的改变而走到别的分支。
2. 全量判据 **2216 / 2 / 0**，逐项目复算（见下）。线上字节由
   `SelectCharacterAcceptedTranscriptTests` 的字节锚守着。

**没做的（明说，这是这个核法的边界）：**

- **没有跑真实客户端、没有抓包。** 「客户端字节不变」的全部证据是判据与字节锚，
  不是线上样本。契约兽槽（24 还是 25）本来就没有线上证据。
- **没有独立复核 T-002…T-008、T-011 那批「形状变了但字节不变」的改动**是否真的一处行为未变。
  我采信的是那些任务各自的检查、字节锚判据全绿、以及 2216 全绿这三样。
- **没有为后果 C 与后果 B 构造判据**（本成果不改判据）。

---

### 订正（完整变更评审 F-1，2026-09-05）：这一节原本的标题是一句假话

这一节原标题是「**清单之外没有其它行为变化** —— 我是怎么核的」。**那句断言不成立**，
已改标题，并在下一节把漏掉的逐条补上。

**漏掉的原因就在上面「做过的」两条里**：它们都是从两处被授权变化的**起因**往下游追，
加上一个全量判据。这两件事都不能发现**新增的拒绝路径**：新拒绝不在那两个符号的下游，
而全量判据全绿恰恰意味着**没有任何已有用例走到那些新拒绝上**。
一个只往前追因果、不回头扫自己新增了哪些 `throw` 的核法，**结构上就看不见这一类变化**。
后来者再写这种清单时，**两个方向都要扫**。

## 新增的生产拒绝路径（全部 13 条）

**计数口径**：`git diff HEAD -U0 -- Server/src/` 的 `+` 行里以 **`throw new`** 开头的语句
（含 `? throw new` 与 `_ => throw new` 这两种表达式形式），
加上未跟踪的生产新文件里的同类语句（**本轮为零**）；
**不数**注释里的 "throw" 字样，**不数** HEAD 上已存在同一消息的改写（恰有一条：
`InventoryEntryAssembly.SaveAsync` 的「Dirty inventory item … is stale or missing」，
`git grep` HEAD 有命中）；**区分大小写**；只扫 `Server/src`。
共 14 行新增 `throw new` 减去那 1 条改写 = **13 条**。**按成员名索引，不按行号。**

**判据有无是变异实测出来的，不是搜消息串出来的。** 两次互相独立的变异合起来盖住了全部存疑格：

- **执行方跑的一次**：把 **#1、#2、#3、#4、#6** 这五道守卫（注意**不是**下表的前五行——
  #5 不在其中）的异常类型改成 `InvalidTimeZoneException`，跑全量 → **恰好 2 条红**：
  `ClassificationRefusalTests` 的 `CreationRefusesAStartingPieceTheArchiveReadsAsAPile`（钉 #1）
  与 `AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot`（钉 #4）。
  还原后两个文件 sha256 逐字节一致（`fa77fb82…` / `4a04f19f…`）。
- **完整变更评审复评时自己重做的一次**：变异 **#2、#3、#5、#6** → **恰好 1 条红**，
  正是 #5 的 `EntrySaveReconciliationTests.ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing`。

两次合起来：#1、#4、#5 有判据；**#2、#3、#6 确实无判据**（两次变异都没能让它们红）。

—— **按消息串 grep 会把 #1 与 #4 误记成「无判据」**，因为那两条判据断言的是 item id 而不是消息文本。
**执行方初稿正是这么栽的**，是评审的表把它纠回来的。
**（初稿此处还写成「把下表前五条……→ 恰好 2 条红」，那句自证本身有错：实际变异的是
第 1／2／3／4／6 条而非表的前五行；若真按前五行变异，第 5 条也会红，答案是 3 条。
表里的结论没错，错的是描述实验的那句话。——由复评查出，已改成上面的写法。
一句写在「我用变异不用 grep」旁边的自证，自己却没被核对过。）**

| # | 位置（成员名） | 拒绝 | 生产可达？ | 判据 |
|---|---|---|---|---|
| 1 | `CharacterRosterService.CreateAsync` | 「Starting piece {id} is not an individual piece the archive declares wearable…」 | 可达（取决于创建表内容） | **有** `CreationRefusesAStartingPieceTheArchiveReadsAsAPile` |
| 2 | `CharacterRosterService.CreateAsync` | 「Starting piece {id} could not be put into the world.」 | 只在世界表插入落空时 | **无** |
| 3 | `CharacterRosterService.CreateAsync` | 「Two starting pieces claim equipment slot {n}.」 | 同上 | **无** |
| 4 | `InventoryEntryAssembly.SaveAsync` | 「The archive declares item {id} an avatar, but the row in slot {n} carries kind {k}.」 | **代码上可达，见下节** | **有** `AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot` |
| 5 | `InventoryEntryAssembly.SaveAsync` | 「A dirty inventory names the same thing in more than one position.」 | 可达 | **有** `EntrySaveReconciliationTests.ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing`（已列在账 12） |
| 6 | `InventoryEntryAssembly.SaveAsync` | 「Two of this character's things claim container {l} slot {s}.」 | 可达 | **无** |
| 7 | `SqliteWorldItemTable.InsertAsync` | 「This database has no world identity left to give…」 | **否**：该类生产零调用方、零 DI 注册 | 无（无需） |
| 8 | `SqliteWorldItemTable.InsertAsync` | 「The world took the item and named no identity.」 | **否**（同上） | 无（无需） |
| 9 | `InventoryItemTables.Name(ItemSlotKind)` | `ArgumentOutOfRangeException` | 程序员错误路径 | 无 |
| 10 | `InventoryItemTables.Kind(string)` | 「A slot says it holds '{stored}', which is nothing.」 | 库里读到未知 kind 字符串时 | 无 |
| 11 | `SqliteItemDomainStoreSession.Write` | `ArgumentException`「A write says what the archive makes of the thing being written…」 | 可达 | **有** `WorldRowSurvivesTheMoveTests.AWriteThatDoesNotSayWhatTheArchiveMakesOfItIsRefused` |
| 12 | `SqliteItemDomainStore.FillAsync` | 「The world took the item and named no identity.」（时装 UID 唯一性守卫） | 可达 | **有** `SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` |
| 13 | `SqliteItemDomainStore.FillAsync` | 「This database has no world identity left to give…」（RQ-008 上界） | 只在身份耗尽时 | **无** |

### 异常类型变化：两处，同一类，同一种记法

旧形状下由**库级约束**招住、新形状下改由**应用层语句**先招住的，恰有两处：

- 上表 **#3**：两件起始装备抢同一装备槽。HEAD 上 `items` 带
  `UNIQUE(owner_scope, owner_id, list_type, slot_index)`（我查了 `git show HEAD:` 的建表语句），
  抛 `SqliteException`；今天先抛 `InvalidDataException`。`item_slots` 仍带同名唯一键，库依旧兜底。
- 上表 **#12**：时装 UID 全库唯一。旧索引 `idx_items_avatar_uid` 抛 `SqliteException`；
  今天 `FillAsync` 自带的条件落空后抛 `InvalidOperationException`。等价性逐段论证见
  上面「订正（TASK-13）」第 2 条。

**两处都：同样回滚、同样不上线、同样不改客户端字节。**
它们**不升格为「被授权的行为变化」**——被授权的仍然只有 RQ-009 与 RQ-010 两处，
把它们编号进去会让那个数失真。**但它们必须在清单上，而且只能有这一种记法。**

### 第 4 条的处置：今天不可达，档案侧已穷尽；但那道守卫与本轮自己的写入方是矛盾的

上表 #4 是十三条里**唯一方向为「原本成功 → 现在抛错」**的，所以单独裁定。

**矛盾在哪**：`ItemContainers.AddAsync` 把 `placement.ItemKind` 原样写进 core，而
`PvfEntryLifecycleContentSource.WornPieceMetadata` 对**非 Creature 一律给 1**——
时装那对 8／1 不一致本轮**明确不修**（RQ-010 只授权了契约兽）。于是一件时装模板会以
`ItemKind = 1` 落库，下一次入场存盘的 #4 守卫（`worldClass == Avatar && ItemKind != 8`）**必然**拒绝它。
**这一轮自己的写入方产出的行，会被这一轮自己的守卫拒掉。** HEAD 上同一行是良性的：
`WithAvatarDetail` 对 `ItemKind != 8` 原样放行，存盘成功。

**今天走不到，这是查出来的，不是推出来的。**

`LandAsync`（写 core 的那一处）有**两个**入口，两个都要交代：

- **`ItemContainers.AddAsync`** —— 下表五条发放通路走的就是它，逐条对真实档案 `Script.pvf` 穷尽。
- **`ItemContainers.RefillAsync`** —— 每日补货。它的 kind 来自 `refill.ItemKind` 而非
  `WornPieceMetadata`，且既有判据把真档案的全部六条补给规则钉死为 **kind 4**
  （见上面「授权变化二·后果 C」），**送不出时装**。
  *（初稿这里只写了 `AddAsync` 一个入口，漏了 `RefillAsync`；结论不受影响，但枚举不该缺一项。
  ——由完整变更评审复评查出。）*

五条 `AddAsync` 通路逐条：

| 通路 | itemId 来源（成员名） | 时装？ | 穷尽方式 |
|---|---|---|---|
| 卡片奖励 | `ClearRewardRuleService.Draw` → `pools.Load(DropItemFamily.Equipment)`；`Fallback` 走 `GoldCardBlankItems` | **0 个** | 候选集就是装备掉落池；Fallback 被 `ClearRewardBlankItemSwitches` 关死（三个开关都是默认 false 的自动属性，生产无处置 true） |
| 地面拾取 | ① `PvfDungeonRoomDropSource.Read`（`[special passive object item]` 每 group 首条）② `DropRuleService.Draw` | **0 个** | ① 扫遍 1047 个 dungeon 文件、405 个非空 group，得 66 个不同 id（全地图并集，是真实可达集的**超集**），无一时装；② `DropRuleService` 对 `DropItemKind.Avatar` 槽**显式 `return null`** |
| 接任务给的道具 | `PvfQuestDefinitionSource.ProjectEventItems`（`[depend give item]`） | **0 个** | 3441 个 quest 文件、25 行、19 个不同 id：18 个 stackable ＋ 1 个 `[ring]` |
| 交任务奖励 | `FinishQuestCommandHandler.ItemRewardsOf`（`[reward type]` 为 `[item]` 的 `[reward int data]`） | **0 个** | 3441 个 quest 文件、9706 行、1558 个不同 id（**超集**：480 处 `[reward type]` 写 `[circle dungeon]`，该 token 不在 `RewardTokens` 里，那些 quest 整个被目录丢弃）。另与 66328 个时装 id 做过一次独立求交，结果 0 |
| 教程奖励 | `PvfTutorialRewardDefinitionSource.Read`（`etc/serverparameter.etc` 的 `[escalade tutorial reward]`） | **0 个** | 整段只有 1 个三元组 `flag=38, item=8474, count=1`；8474 是 stackable，**不在 `equipment.lst` 里** |

**决定性的一条**：装备掉落池的唯一闸门是 `[creation rate] > 0`
（`PvfDropItemPoolSource.ReadCreatable`：没有该段的文件直接跳过）。
`equipment/equipment.lst` 的 **118479 条**里，按仓库自己的 `ClassifyEquipmentType` 分类得
**Avatar 66328、Creature 707、Equipment 51444**；
而 **66328 个时装模板里声明了 `[creation rate]` 的是 0 个**（整段根本不存在），契约兽同样 0 个。
非时装装备 52151 个里 15567 个声明、6062 个 > 0。**所以装备掉落池 = 6062 项，时装 0 个。**
这一条同时封死了卡片奖励的 `Draw` 与地面拾取的怪物掉落。

**这组数字有一次独立的算术交叉验证**：`PvfDropItemPoolSource` 里一句**本轮之前就写着的**注释说
「9505 pieces of equipment 主动把 creation rate 设成了零」，而 15567 − 6062 = **9505**，严丝合缝；
同一段注释里的「118479 archive files」也与枚举数一致。

**裁定：不作为行为变化记入被授权清单，因为今天没有任何发放通路能触发它。**
**但它是一处潜伏矛盾，必须留着账**——档案内容或发放表将来一变（新增一件带 `[creation rate]` 的时装、
或某个任务奖励直接点名一个时装 id），它就会从「不可达」变成「玩家存盘失败」。
真正的根因是时装 8／1 那对不一致没修；修了它，守卫与写入方自然一致。
`AGENTS.md` 的「已知缺陷与覆盖缺口」已记这一条。

### 顺带查实：授权变化二的后果 A 与后果 B 是**真实可达**的，有具体档案实例

同一次调查发现：**交任务奖励里有一个契约兽模板** —— id **63465** =
`equipment/creature/WolfLee.equ`，`[equipment type]` 原文 `[creature]`，
而 `FinishQuestCommandHandler` 走的正是同一条 `IItemContainer.AddAsync`。

于是上面「授权变化二」的两条后果**不再是纸面推论**：
- **后果 A**（写进存储的种类字节由 1 变 5）：领这个任务奖励就会发生。
- **后果 B**（契约兽不再能被拖进快捷栏 3..8）：同一件东西，落库即受影响。
  该后果原记「没有任何既有判据覆盖它」——**那句仍然成立**，
  但它的可达性今天有了一个点名的实例，**不再是「理论上可达」**。

## 这套建模里必须一起带下去的账

下面这些**不是行为变化**，是本成果留下的、后来者容易读错的东西。
仓库约定（`AGENTS.md` 的「物品：有身份的东西，和它在哪，是两张表」一节）已收录其中面向仓库的部分；
这里是完整版，带出处。

### 三条「结论对，但撑着它的不是代码」

| # | 账 | 出处 |
|---|---|---|
| 1 | `world_identity_fits_the_wire` 这条 CHECK **只对新建库无条件成立**。发布版（HEAD）的 `items` 表没有它（复算：`git show HEAD:…/CharacterDatabaseSchema.cs \| grep -c` 得 **0**；工作区版本 **1** 处），而 schema 调和只有 `ALTER TABLE … ADD COLUMN`（全仓 `ADD COLUMN` 恰 **1** 处），**补不了 CHECK**。既存库上溢出会落到编码时的 `checked((int))`，抛 `OverflowException`。与「旧库作废重建」不冲突，**但限定不得省略**。 | `DSP-TASK-07-02` 一 |
| 2 | 「今天不存在能销毁有身份物品的生产通路」成立，**但 `ConsumeAsync` 只经 item-use 这一条由档案数据保证、代码里没有守卫**。复算：`ItemContainers.ConsumeAsync` 的生产调用方**恰好 1 处**（`ItemUseEffectDomainService`），门是 `if (rule.IsConsumedOnUse)`，`rule` 来自 `PvfItemUseRuleDefinitionSource`；`ConsumeAsync` 本体只校验 count 的正负与是否超出堆叠，**不看这件东西有没有身份**。 | `DSP-TASK-08-01` 三 |
| 3 | **「清空先于填入」这条不变量已不再承重，且无任何判据钉住。** `SqliteItemDomainStore` 的注释**四处**说它（类型级 `<remarks>` 一处，加 `ApplyDecidedWritesAsync`、`FlushAsync`、`EmptyAsync` 各一处；四处措辞各不相同，按同一句去 grep 只会找到一处），实现也确实是两个分开的循环；但快审变异逐槽交替 empty/fill，**当时的 315 条持久化判据全绿；TASK-13 重跑同一次变异，今天的 316 条依旧全绿，结论未变**。真正扛住换位的是 `spared`——幸存者按名字免删，落点是 `EmptyAsync` 里 `DELETE FROM items` 上追加的 `AND item_uid NOT IN (<幸存者>)`（**注意与 `DeleteOwnExceptAsync` 的 `AND core_uid NOT IN (…)` 不是同一个谓词**）。**这是一条只写在注释里的口头不变量。** | `DSP-TASK-11-02` 三 |

### 两条「判据的名字不再描述它证明的东西」

| # | 判据 | 现状 | 出处 |
|---|---|---|---|
| 4 | `SqliteItemDomainStoreTests.Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` | 现在靠「行没被删」通过。它的 `<remarks>` 还写着「this store moves an item by replacing its row… the facts are read back under the avatar UID the core carries and written on again」，而那套搬运 `CarriedAvatarsAsync` 今天只对**非幸存者**执行——**名字与注释一起过时了**。 | `DSP-TASK-04-02` 三 |
| 5 | `ItemCarrierShapeTests.EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt` | 六条 `DoesNotContain`（含 `idx_items_owner_space`）＋ **两条**正向 `Contains`（`idx_characters_account` 与新增的 `ON item_slots(core_uid)`），而**与本轮相关的那条指向的是新增的 `ON item_slots(core_uid)`**，不是「搬过去重建的那个」。快审判定这是正当跟随而非削弱（字面重建不可能——列已不在 `items`；被保护的事实由槽表 `UNIQUE` 接住；净断言数上升），**但名字仍然不描述它证明的东西**。 | `DSP-TASK-11-02` 三·7 |

### 四条已知的同类缺陷／覆盖缺口（本成果不修）

| # | 缺陷 | 复算 | 出处 |
|---|---|---|---|
| 6 | **时装与契约兽宝珠同样「取得下、装不上」。** | `PvfEquipmentSlots.ByName` **16 个键**里没有任何 avatar 键；文件里 `avatar` **3 处出现全在注释里**。槽 0..11 是时装带。档案里 `[coat avatar]` 等十一个 avatar 名、`[artifact red/blue/green]`、`[charm]`、`[flag]`、`[name tag]` 都不在表里。（**档案侧的逐名件数我没有复算**，见「我没有验证的东西」。） | `DSP-TASK-09-01` 二 |
| 7 | **时装的种类不一致仍在**：存储 8 / 档案 1。 | **四处生产注释**点名它——**计数判据**：数的是「同时点名存储 8 与档案 1 这一对**分歧**」的**生产**注释，扫的是 `Server/src`，区分大小写。分别是：`ItemPlacementContent` 的 `ItemWorldClass` 类型级 `<remarks>`、`IItemPlacementRuleService.ClassifyAsync`、`ItemPlacementRuleService.ClassifyAsync`、**`PvfEntryLifecycleContentSource.WornPieceMetadata`**（本行后半句正引用这个方法，却漏了它的注释）。**不计入**：`SqliteItemDomainStore.CarriedAvatarsAsync`、`AvatarKind` 常量、`InventoryEntryAssembly.WithAvatarDetail` 只说「时装是 8」不点名分歧；判据侧 `ItemClassificationTests` 点名但非生产注释；`PvfEntryLifecycleContentSource.WornPieceMetadata` 对非 Creature 一律给 1，而 `ItemSlotRuleService.KindAvatar = 8`、`GameInventoryDelivery` 按 `ItemKind == 8` 判时装。 | `DSP-TASK-10-01` 三 |
| 8 | **后果 C（源槽 24 取下时遇到另一只契约兽 → 互换）无判据。** | `CreatureGoesBackOnTests.cs` 里的用例属性**恰好 2 个**（复算：`grep -n "Fact\|Theory"` 得两条 `[SkippableFact]`；文件里另有 2 个 `public async Task` 是 fixture 辅助方法）。这 2 条覆盖的是后果 A 与后果 D，**没有一条覆盖后果 C**。 | `DSP-TASK-09-02` 二 |
| 9 | **「非时装不得带细节」与三条取值校验全仓无断言，删掉仍全绿。** | 复算：`"Non-avatar inventory item carries avatar detail."` 在 `Server/` 下**只 1 处命中，且在 src**；`"Avatar jewel socket must contain exactly 30 bytes."` 同样只在 src；`InventoryEntryAssembly` 的「珠宝槽 30 字节 ＋ `Color1`/`Color2` 落在 0..65535」那条的异常文案 `"is malformed or has the wrong owner"` 在 `Server/tests` **零命中**。（`avatar_jewel_socket` 另有 schema CHECK 兜底，另两条没有。） | `DSP-TASK-06-02` 三 |

### 四条形状与代价

| # | 账 | 出处 |
|---|---|---|
| 10 | **孤儿的答案跨存盘易逝** —— **⚠ 这条已被本轮后续改动证伪，见下方「订正 T12-1」。** | `DSP-TASK-08-01` 二 |
| 11 | **一个列（`core_uid`）解掉了两笔债。** 槽行不论哪种 `kind` 都指名它那 82 字节住在哪一行，于是 (a) 孤儿查询的第二条 `NOT EXISTS` 整条消失——不再需要靠位置列把堆叠物挑出去（堆叠物的 core 不可约，见订正 C-1）；(b) 抬起机制随之干净摘掉——复算：全仓 `LiftAsync` / `NoContainer` **零命中**，腾位置改由 `EmptyAsync` 的 `AND item_uid NOT IN (<幸存者>)` 接掉。 | `DSP-TASK-11-01` 五 |
| 12 | **新形状引入一个旧形状表达不出的失败模式：同一件东西被两个位置同时指名。** 旧形状下一行只能有一个位置，这种错误的表现是悄悄丢掉第一个位置；新形状下玩家会看见两把同一把剑，还能把两边都搬走。**保存侧已加拒绝**（`InventoryEntryAssembly.SaveAsync` 在落位前比 `settled` 的身份集合大小，不等就抛「A dirty inventory names the same thing in more than one position.」），判据 `EntrySaveReconciliationTests.ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing` ＋ 变异验证。**装载侧没有对称的拒绝**——复算：入场读只有 `RequireUniqueSlots` 校验 (`ContainerType`, `SlotIndex`) 不重复，**没有任何地方校验 `core_uid` 不重复**。 | `DSP-TASK-11-01` 六 ／ `DSP-TASK-11-02` 四·4 |
| 13 | **T-006 的主线在到达前已由 T-002 的实现方式完成。** 计划把 T-006 写成「把时装六列搬到世界表」，但 T-002 裁定的「`items` 就地变成世界表」已经顺带完成了这次搬运——六列开工前就已经是 `items` 的列，`InventoryItemCore` 无事可做（六列从来不在那 82 字节里）。**这是计划层面的冗余，不是执行方偷懒；不要让后来者以为 T-006 做了一次搬运。** T-006 真正的产出是一条判据：变异「让细节属于位置」下恰好 1 条红，而 T-004 的既有判据在同一变异下仍绿——**本轮之前仓里没有任何东西能把「随身份」与「随槽」分开**。 | `DSP-TASK-06-01` 二／五、`DSP-TASK-06-02` 四 |

### 两条读这份记载的方法

| # | 方法 |
|---|---|
| 14 | **十二个任务全部未提交（HEAD 仍是 `51cd99c2`）**，所以 `git diff` 会把所有任务的改动一并算进来。**读任一任务的 diff 不能按整文件计数，要按块归属。** 已经踩到过一次：`PvfEntryLifecycleContentSource.cs` 的 `+123/-8` 里，T-010 真正拥有的只有三块（两个常量、一处调用点、`WornPieceMetadata`），其余是 T-003 的 `ReadEquipmentPaths` / `ResolveWorldClass` / `ClassifyEquipmentType` / `RefillItemMetadata.WorldClass`。（`DSP-TASK-10-02` 一） |
| 15 | **`Docs/ItemWorldModel.md` 带一条「更正 C-1」：堆叠物的 core 不可约到「模板号 + 数量」。** `StackableItemCore.Create` 写死 `Marker16 = -1` 与 `RandomOptionChangedIndex = byte.MaxValue`(255)，而 `GameInventoryDelivery` 对 `ItemKind == 2` 走 `core.Marker16` 那一支（不 `Math.Max(0, …)` 钳）、255 也原样上线——**两者都上线**。所以「无身份的东西不在世界表留行」今天做不到，且不是工作量问题：丢掉 core 就会改变线上字节。读该设计文档时必须连这条更正一起读。（`DSP-TASK-06-01` 三、`DSP-TASK-06-02` 二） |

---

## 订正 T12-1：账 10 已被本轮后续改动证伪

`DSP-TASK-08-01` 第二条记着：

> `DeleteOwnExceptAsync` 删除的，恰好就是 `ReadUnreferencedAsync` 会报出的那一类行
> ——即一个角色的孤儿在下一次入场存盘时已被对账顺手清掉，这条读的答案是跨存盘易逝的。

**这句在写下时为真，今天不成立。** 我逐条核过：

- **HEAD（本成果之前）** 的 `DeleteOwnExceptAsync` 是
  `DELETE FROM items WHERE owner_scope='character' AND owner_id=@cid AND list_type NOT BETWEEN 19 AND 23 [AND item_uid NOT IN (…)]`
  ——那时 `items` 自己带归属列，所以一条**没有任何槽行指名**的世界行仍然在这个删除的范围内。
  **DSP-TASK-08-01 写下时（T-008 完成时）位置列尚未退役，这句因此为真。**
- **今天**（T-011 退役位置四列之后）它是
  `DELETE FROM items WHERE item_uid IN (SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=@cid AND list_type NOT BETWEEN 19 AND 23 [AND core_uid NOT IN (…)])`
  ＋ 一条对 `item_slots` 的同条件删除。**圈定对象来自槽表**，
  而孤儿的定义恰恰是「没有槽行指名」——**它根本不在这个删除的范围内。**

**订正后的说法：** 孤儿今天**不会**被入场对账顺手清掉，它会一直留着直到有人写清理器
（而本轮明确不做清理器）。这使 `ReadUnreferencedAsync` 的答案**跨存盘稳定**，比原先记的更有用。

**没有被这条订正影响的：**

- 「今天的生产上不会产生孤儿」仍然成立（没有任何生产通路销毁一件有身份的物品，
  限定见账 2）。
- 「`ReadUnreferencedAsync` 的答案只与槽表一样真实」仍然成立：
  两个曾经不写 `item_slots` 的写入方（建角色、入场存盘）已在 T-011 补齐，
  `InventoryTables.cs` 的 `<remarks>` 记着这一点。
- 「`IItemSlotTable` 在生产里仍然无人调用」仍然成立，
  所以这条读**今天实际只在判据里被用到**。

**为什么写在这里而不是改 `DSP-TASK-08-01` 的正文**：那是一份历史裁定，
它在自己的时点上是对的；把它改掉会抹掉「这句何时为真、何时停止为真」这个信息。
按本仓的规矩（改已证伪的记载可越任务边界，但要补记修正案），**本节即是那份补记**，
`AGENTS.md` 的「一条已被证伪的记载，订正」是它面向仓库的那一半。

---

## 额外发现：`avatar_uid` 这条派工没有落地

`DSP-TASK-06-01` 第一条裁定：「本轮验收已达成，`avatar_uid` 归 T-011」，
并写「→ **写进 T-011 的派工**」。

**复算：`TASK-11-retire-the-old-shape.md` 全文提到 `avatar` 的地方，
没有任何一处提到退役 `avatar_uid`。** **计数判据**：区分大小写扫 `avatar` 得**两处**，
两处都是把 `avatar_*7` 当作交付形状的一部分列出；**不区分大小写**则多出一行，
是判据名 `AvatarDetailFollowsTheIdentityTests.TwoAvatarsSwappingPositionsEachCarryTheirOwnFacts`，
**那一行不属于「把 `avatar_*7` 当交付形状列出」这一类**。
（本条初稿写「3 处命中，都是……」——**两种读法都对不上**：区分大小写是 2，
不区分大小写是 3 但限定不成立。这是同一失败模式的第四处，见文末。）
交付的 `items` 是 20 列 = 5（身份/core/归属/时间）+ 7（avatar）+ 8（creature），
`avatar_uid` 仍在，并且 `items` 上还有 `idx_items_avatar_uid`
（`CREATE UNIQUE INDEX … ON items(avatar_uid) WHERE avatar_uid IS NOT NULL`）。

即：**物品拿到了真身份 `item_uid`，但「这一行有没有时装细节」今天仍由 `avatar_uid`
是否为 NULL 决定**——一个半身份留在原地。

**这不是行为变化**（`avatar_uid` 的读写与本轮之前一致），也不是本成果的缺陷
（T-011 的契约没要求它），但**那条派工确实没有落地**，据实记在这里。
`AGENTS.md` 已把它列进「已知缺陷与覆盖缺口」。

### 订正（TASK-13，2026-09-05）：上面这一节从「没有落地」变成「已经落地」

上一段的结论已被推翻，原文保留在上面不改，修正案在这里。所有者当天就
「`avatar_uid` 这个残留怎么处置」裁定**「本轮清掉」**，控制器为此补派 TASK-13，已做完：

- `items` 现在 **19 列**（2 身份/core ＋ 3 归属/时间 ＋ **6** 时装 ＋ 8 契约兽），
  `avatar_uid` 那一列不在了；`idx_items_avatar_uid` 也不在了，且**没有任何后继索引**顶替它
  （`CharacterDatabaseSchema.IndexDeclarations` 今天 **3 条，分别是**：
  `idx_item_slots_core`、`idx_item_slots_account_slot`、`idx_characters_account`）。
- 「这一行有没有时装细节」的判定处，**读侧只有一处**：
  `InventoryItemTables.cs` 的 `SqliteCharacterItemTable.Avatar(SqliteDataReader, int)`，
  判据是六列的第一列 `avatar_expire_date` 是不是 NULL。

**这一节原来说「这不是行为变化」，那句今天要加一个限定。** 退役本身对生产逐值不变，
但索引顺带保住的东西没有全部原样搬走。逐条：

1. **保住了的**：「同一个时装 UID 全库至多一行带着它的细节」这条规则，在入场存盘的两条写入上
   **一字不变**——它本来就是那两条语句自己的条件，不是靠索引。
   落点是 `SqliteCharacterItemTable.UpdateOwnAsync` 的 `NOT EXISTS(…)`，与
   `SqliteCharacterItemTable.InsertOwnAsync` 的 `INSERT … SELECT … WHERE NOT EXISTS(…)`
   （后者取代原先的 `ON CONFLICT(avatar_uid) WHERE avatar_uid IS NOT NULL DO NOTHING`，
   拒绝的表现完全一样：插入落空、`InsertOwnAsync` 返回 `null`、
   `InventoryEntryAssembly` 抛 `InvalidDataException` 并回滚）。
   两条各有判据钉住（**变异实测**：把共用条件 `CarriesTheAvatar` 改成恒假，
   `DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 与
   `DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds` **两条一起红**）。
2. **补回来的第三条落点**：`SqliteItemDomainStore.FillAsync`。它原先没有自己的条件，
   靠索引抛错；索引退役后本 Task 给它补了等价的一段
   `INSERT … SELECT … WHERE @expire IS NULL OR NOT EXISTS(…)`。
   **等价性是逐段核过的，既不更严也不更宽**：受约束的插入集合相同
   （`@expire IS NULL` ⟺ `facts is null`，因为 `CarriedAvatarsAsync` 的 `SELECT` 带着
   `WHERE avatar_expire_date IS NOT NULL`，读回来的第一列必然非空）；算数的既存行相同
   （旧索引的部分条件是 `avatar_uid IS NOT NULL`，新守卫问 `standing.avatar_expire_date IS NOT NULL`，
   而 `AvatarDetailRow.ExpireDate` 是不可空的 `long`、两列同生同灭）；比的键相同
   （`substr(item_core, 6, 4)` 就是旧的 `avatar_uid`，后者历来取自 `core.Value`）。
   **被拒时的表现与旧索引只差异常类型**：`INSERT … SELECT` 落空 → `RETURNING` 无行 →
   `ExecuteScalarAsync` 返回 `null` → 紧随其后那句 `allocated is null` 抛
   `InvalidOperationException`（消息含 `named no identity`），**同样回滚**。
   旧索引抛的是 `SqliteException`。**这是一处内部异常类型的变化，不上线，不改客户端字节。**
   由 `SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` 钉住
   （**变异实测**：把守卫撤回成 `VALUES(…)`，`Dfo.Server.Persistence.Tests`
   **失败 1／通过 315／总计 316**，红的恰好是这一条；315 条既有判据一条未动）。
3. **仍然没有原样搬走的**：**只剩 1 处**——`SqliteWorldItemTable`（`InsertAsync` 与 `UpdateAsync`）
   写 `items` 时装列却没有自己的条件。它**生产里零调用方、零 DI 注册**
   （`PersistenceModule` 注册的是 `ICharacterItemTable`、`IAccountCargoItemTable`、
   `SqliteItemDomainStore`／`IItemDomainStore`，**没有** `IWorldItemTable`），
   全仓只有 `WorldAndSlotTableTests` 与 `CreatureIdentityFitsTheWireTests` 把它 new 出来。
   **这个缺口今天到不了生产**，故**有意留着不补**——补它等于在一个生产无人调用的类上引入新行为，
   收益为零而风险非零。这一条**如实记为本 Task 的残留**，不作为被授权的行为变化
   （被授权的仍然只有 RQ-009 与 RQ-010 两处）。

**`CarriedAvatarsAsync` 的去留**：**留**。本 Task 只把它的检索键从 `avatar_uid = @uid`
换成「`avatar_expire_date IS NOT NULL` ＋ `substr(item_core, 6, 4) = @uid`」——
读到的行与换之前**逐行相同**（`avatar_uid` 从来就是 `core.Value` 的一份副本），
所以本轮**没有**让它更走不到，也**没有**让它走得到。它今天仍然只在
「同一批次里既换了位置、core 字节又变了」这一种情形上有话说
（`Survivors` 的三趟：同位同字节、异位同字节、同位同 id/kind/value——这一种三趟都不认），
`AGENTS.md` 早就把它记作「近乎走不到」。**「全仓零覆盖」这句今天不再成立**：
让它恒返回空表，`TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` 会红，
因为空表使 `@expire` 为 NULL、上面第 2 条那道守卫短路放行、该判据等的抛错不再发生。
**但钉住的只是「守卫依赖这次检索」，不是「那六个值被正确搬到新行上」**——后者今天仍然零覆盖。
按「不得留一段没有理由的机制」，它的理由还在（那一种情形），所以不删；
`Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 这条判据也不动——
它今天证明的是「行没被删所以细节还在」，仍然是一件值得钉住的事，只是名字比内容大，
`AGENTS.md` 的「两条判据的名字不再描述它证明的东西」已经记着这一点。

---

## 全量判据 —— 逐项目复算

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
（在 `.worktrees/items-own-their-identity` 下运行，退出码 **0**）

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 316 | 0 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| **合计** | **2216** | **2** | **0** |

逐项目相加复算：60 + 52 = 112；+521 = 633；+6 = 639；+236 = 875；+316 = 1191；+1025 = **2216**。
（`Dfo.Server.Persistence.Tests` 由 315 增至 316、合计由 2215 增至 2216，
**净增的一条就是** `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`。）
两条跳过都在 `Dfo.Server.Infrastructure.Tests`，均为 PVF 档案缺失时的 SkippableFact
（`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`、
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`）。

**计数是按项目逐个相加得到的，没有用 `grep -c`。**
