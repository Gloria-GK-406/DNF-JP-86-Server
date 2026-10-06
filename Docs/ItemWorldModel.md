# 物品建模：世界表与槽引用

2026-09-05，基准 `main` @ `51cd99c2`。**设计文档，未改动任何代码。**

> **本文已经落地了，就在加入它的那个提交里**（`499c99aa`「Give an item an identity of
> its own, and let a slot say only where it is」）。所以：
>
> - **第一节「今天的形状与它的三个后果」讲的是 `51cd99c2`，不是今天。** 那三个后果——
>   `item_uid` 每写一次就换一个、物品无法「存在但不在任何槽里」、`Core.Value` 一字三义——
>   前两条已经不成立。**今天的形状看 [AGENTS.md](../AGENTS.md)「物品：统一属性、容器内引用与取出记录」，
>   以及 [物品生命周期规格](../Document/tasks/item-container-lifecycle/spec.md)。**
> - 第三条只解掉一半：`Core.Value` 对时装不再是身份（`server-agrees-what-an-avatar-is`，
>   `fa10dfa5`），对堆叠物仍是数量、对契约兽仍是 `creature_key`。
> - **本文其余部分作为「为什么是这个形状」的推导保留**，那部分没有过期。
>
> 配套文档里 `CreatureDomainDesign.md` 那句「本文落地后要按本文改写」**尚未兑现**。

配套：[Domain Instance Design](Refactor/DomainInstanceDesign.md)（领域对象定式）、
[Table Ownership Ruling](Refactor/TableOwnershipRuling.md)（持久层裁定，已落地）、
[宠物领域怎么建](CreatureDomainDesign.md)（本文落地后要按本文改写）。

## 一句话

**一件物品是一件物品；它在哪个槽是另一回事。**
今天这两件事挤在同一行里，所以物品没有稳定身份、没法跨人交易、也没法「存在但不在任何槽里」。

## 一、今天的形状与它的三个后果

`items` 一行同时承载「这是什么东西」（`item_core` 82 字节）与「它在哪」
（`owner_scope`／`owner_id`／`list_type`／`slot_index`），唯一键是后四列。

结算的做法是**先 `DELETE` 掉那个槽，再 `INSERT`**
（`SqliteItemDomainStore.cs:270`／`:292`）。代码里那句注释交代了原因：谓词与唯一键完全同名，
这样 insert 不会撞上残留——更窄的谓词会放过那种行，让 insert 失败。

三个后果：

1. **`item_uid` 每写一次就换一个。** 它是 `PRIMARY KEY AUTOINCREMENT`，但行被销毁重建，
   所以它不是身份。领域层根本看不见它——`ContainerSlotItem` 是 `(SlotIndex, Core)`。
2. **物品无法「存在但不在任何槽里」。** 地上的掉落、交易托管、信里的附件、拍卖挂单，
   四件事今天都没有地方表达。裁定的 **D-1**（「一封信里的物品是什么」）正卡在这里。
3. **`Core.Value` 一字三义**：堆叠数量 ／ `creature_key` ／ 头像 UID，靠 `ItemKind` 分辨，
   没有任何东西强制。宠物那边已经在咬人。

## 二、模型

**世界表按类型分**，每张只装真有身份的东西：装备、时装、契约兽。一行 = 世界上的一件。

**槽行记 `(type, id, count)`**：这个槽放的是什么类型的什么东西、几个。

`type` 决定去哪张世界表查。**查询本身就是判别式**——这与 `Core.Value` 靠 `ItemKind` 分辨
而无人强制不是一回事。

### 三类东西，差别在两处

| | 身份 | 可变状态住哪 | 一个槽放几个 | 动词属于谁 |
|---|---|---|---:|---|
| **装备 / 时装 / 契约兽** | 世界行 id | **世界行** | 1 | 世界表交出的实例 |
| **消耗品 / 材料** | **无**——同模板同状态可互换 | **槽行的 `count`** | N | 容器（槽） |

差别是「一个槽放几个」与「id 有没有语义」，**不是「状态住哪」**。

对消耗品，`id` 是 PVF 的模板 id；对有身份的，`id` 是世界行 id。
两者在同一列，但 `type` 决定读哪张表，所以不会被误读。

## 三、这个模型是现有代码已经隐含的

**四条证据，都可当场核。**

**1. 82 字节 blob 只是存储格式，不是线上格式。**
`InventoryItemCore.Encode/Decode` 全仓 7 处命中，**全部在 Persistence 层**。
线上那 101 字节条目是**逐字段写的**（`GameInventoryDelivery.cs:53-108`）。
**所以按类型拆存储，客户端一个字节都不会变**——只要投影还能拼出一个 `InventoryItemCoreState`。

**2. 线上已经按类型分支，而且强制得比存储还严。**
同一个写入器（`GameInventoryDelivery.cs:47-58`）：

```csharp
if (core.ItemKind == 8 && avatar is null) throw Missing($"AvatarDetail[{item.StorageEntryId}]");
if (core.ItemKind != 8 && avatar is not null) throw new InvalidDataException("Non-avatar inventory item carries avatar detail.");
writer.Write(core.ItemKind == 8 ? Math.Max(0, avatar!.ExpireDate - now) : core.Value);
```

`InventoryItemState` 上**已经挂着一个 `AvatarDetail` 侧记录**。
**要的形状线上已经是这样了，反而是存储不是。**

**3. 拆堆已经在造一个全新的堆。**
`ItemContainers.cs:113-117`：源堆是 `moving.Core with { Value = moving.Count - moveCount }`，
拆出去的那一堆是 **`StackableItemCore.Create(kind, itemId, moveCount)`**——
一个新 core，不继承源堆任何东西。
**堆叠在今天的代码里本来就没有身份**；给它一个稳定 id 会是凭空捏造。

**4.「这个 id 在档案里存在吗」这道检查已经有了，而且由规则服务回答。**
`SlotAdmission.ItemUnknown`（2026-09-07 前叫 `MainSlotAdmission`），注释原文：
「The archive declares no item that can be held under that identifier.」

## 四、不要做的：凭证来回

一个自然但错误的想法是：**从槽里取出凭证 → 去世界表换到实例 → 操作 → 换回一张新凭证 → 写回槽**。

**这个形状违反 R-1，而且这仓已经踩过这个坑。** R-1 原文：

> 实例持有身份与依赖，**不持有状态**。每一次读都去 store。实例不缓存任何它能再问一次的东西。
>
> *为什么。* 一条命令跑在一个事务里。事务回滚时，
> **缓存了自己状态的实例就领先于数据库，而没有人会报告这件事**。

R-1 还说：这条规则正是移动用例后来不再需要「竞态失败」返回码的原因。

**凭证换回去 = 手上攥着「我读的时候是 37 个」。** 中间任何人动了那个槽，写回去就覆盖。

### 正确的形状：槽行本身就是地址

- **强化 +1 不碰槽行**——强化住在世界行里，槽行只说「这个槽引用第 id 号」
- **用掉一个药水不碰世界表**——改的是槽行的 `count`，那是容器今天就在干的事

消耗品带来效果的那种动作，是**两段各有主语的动作，同一事务，中间不传值**：

```
第一段  bag.Consume(slot, 1)                 ← 槽行 count 减一，容器的动词
第二段  worldCreature.At(petId).Hunger(-20)  ← 世界行的动词
```

这也顺带答了 `DomainInstanceDesign` 挂着的最后一个未决项
（`ItemUseEffectDomainService.ApplyAsync` 同时吃 `CharacterId` 与 `IItemContainer`，
按 R-2 像是仓库操作，但两个操作数又都在同一个角色管辖内）：
**它不是一个跨实例操作，是两个动作。** 拆成两段之后 R-2 不再适用。

## 五、稳定身份不是被解决，是被溶解

今天身份会飘，是因为**一行同时承载「是什么」与「在哪」，而改位置的做法是删了重插**。

分开之后，**移动一件装备根本不动世界行**——只改哪个槽引用它。
世界行从生到灭只被 INSERT 一次、DELETE 一次。

**飘不起来了，不是因为盯着它，是因为没有让它飘的操作了。**

这同时消掉了那个「两阶段避唯一键冲突」的难题：槽行仍有
`UNIQUE(owner, list, slot)`，交换两个槽仍要小心，但**载荷不再跟着搬**，
腾挪的只是引用。

## 六、四件必须先定的

### W-1 世界 id 的宽度 —— **只有契约兽受线上约束**

- **装备**：线上写的是 `core.ItemId`（模板）与 `core.Value`（数量），
  **世界 id 不上线**，可以是 i64。
- **契约兽**：`0x0069` 把 `PetKey` 用 **`checked((int))`** 写出去——
  **必须塞进 i32，超了当场抛**。

**建议**：世界 id 统一用 i64，但**契约兽表另有一个 i32 的对外键**（就是今天的 `creature_key`），
由持久层分配。理由是不要让一个协议的宽度限制去约束整个物品系统；
代价是契约兽有两个 id，必须在类型上分清哪个上线。

### W-2 孤儿谁清

今天「销毁一件物品」＝ 删掉那个槽的行。拆开之后是**两次删**：槽的引用，与世界行。
两者不一致就漏，而且是慢性的、没人会发现的那种。

**建议**：把「谁引用它」做成世界行自己的一个字段（当前归属），
**没有归属的世界行就是待回收的**。这样孤儿是可查询的，不是靠两次删都别忘。
需要一条明确规则写进仓库约定。

### W-3 时装那六列刚落地，要再搬一次

`character_avatar_detail` 的 6 列**刚按 M-2 并进 `items`**。在这个建模下它们要搬去世界时装表。
**刚落地的活要重做一遍。** 不致命，但要知道自己在付这个钱。

**建议**：接受。M-2 的理由是「细节的键是物品已经持有的一个值」，
而这个建模给了物品一个真正的键，M-2 的前提被更好的东西取代了。

### W-4 命名

本仓 `store` 只指**事务结算的真源**且以 `DomainStore` 结尾；
上一轮专门把 `OnlineStateStore` 改名成 `Registry` 来守这个词。
世界表交出实例，它**是**事务结算的真源，所以叫 `DomainStore` 可能反而是对的——
但要与「容器」区分清楚：容器管槽，世界表管东西。

**建议**：具体名字留给 Spec 阶段裁，但**不得让 `store` 回到两种读法**。

## 七、迁移

现有 `items` 行要一分为二：

- `ItemKind` 属于有身份的类型 → 造一行世界行（含 core 的实例部分），槽行引用它
- 其余 → 槽行直接记 `(模板 id, count)`，不造世界行

**两处要当心**：

1. **`ItemKind` 今天不可靠。** 24 槽契约兽记 `5`，而档案解析器对同一件物品给 `1`
   （`PvfEntryLifecycleContentSource.cs:101-102`）。迁移的分类依据要先定死，
   不能直接信库里的 `ItemKind`。
   **⚠ 见文末「更正 C-2」：这一条对契约兽已不成立，且它引的行号已经漂了。**
2. **`Core.Value` 的三义要在迁移时摊牌**：堆叠取 count，契约兽与时装取实例 UID。
   分错了不会报错，会静默错。

## 更正 C-1：堆叠物的 core 不可约到「模板号 + 数量」

**2026-09-05，由 TASK-06 的取证推翻，控制器复核属实。**

本文第二节的表把消耗品／材料的可变状态写成「模板 id + 数量，也在 core 里」。
**作为「只需这两样」它是错的。**

`ItemContainers.cs:31-38` 的 `StackableItemCore.Create` 写死了两个**非默认值**：

```csharp
Marker16 = -1,
RandomOptionChangedIndex = byte.MaxValue,
```

而线上是**逐字段**写的，`GameInventoryDelivery.cs:65`：

```csharp
writer.Write(core.ItemKind == 2 ? core.Marker16 : Math.Max(0, core.Marker16));
```

**消耗品（`ItemKind == 2`）的 `-1` 原样上线，不被钳成 0。**
另据 TASK-06 取证，`SelectCardCommand` 还有一处 `Durability` 耦合，
靠取值侧**碰巧为零**而不是靠守卫。

### 这条更正改变了什么

**「无身份的东西不在世界表留行」今天做不到，而且不是工作量问题。**
要停存堆叠物的 core，就得在读出时重建出**恰好这些常量**；只要有一处不是由构造保证的，
丢掉 core 就会改变线上字节，直接违反逐值不变。

本文第四节「用掉一个药水不碰世界表——改的是槽行的 `count`」这句**目标仍然成立**，
但它成立的前提是先把这些常量收进一处由构造保证的地方，**那件事本文原来没有提。**

### 这条更正没有改变什么

三类东西的区分（有身份／无身份、身份来源、动词归谁）不受影响；
槽行 `(type, id, count)` 的形状不受影响；世界表一张的裁定不受影响。
**变的只是「堆叠物在世界表里留不留行」的可行性与前置。**

## 八、这份设计没答的

1. **世界表分几张。** 装备／时装／契约兽是三张，还是一张带 `type` 判别？
   本文按三张写，理由是每张的列各自稠密；但没有量过它们的列重合度。
2. **地上掉落、交易托管、拍卖挂单要不要现在就表达。** 模型允许，但本轮做不做是范围问题。
3. **`Pet = 7` 容器装什么**，与契约兽的槽位是 24 还是 25——见
   [宠物领域怎么建](CreatureDomainDesign.md) 的 D-a 与 D-d，**都要线上证据，读代码得不出**。
4. **工作量没有估。**

## 更正 C-2：上面「两处要当心」的第 1 条，对契约兽已不成立；它引的行号也漂了

写这份文档时，`ItemKind` 的两处不一致（契约兽 5／1、时装 8／1）都还在。**契约兽那一对已经对齐**
——成果「物品有自己的身份，槽只说它在哪」的被授权行为变化二（RQ-010）把
`PvfEntryLifecycleContentSource.WornPieceMetadata` 对 `ItemWorldClass.Creature` 的答案改成了 **5**。
**今天档案对契约兽给 5，与存储一致。**

**时装那一对仍在**：存储记 8、档案给 1，动它属未授权的行为变化，那个成果明确没做。
所以第 1 条的**结论**（迁移分类不能直接信库里的 `ItemKind`）**仍然成立**，
但它举的例子（契约兽）已经不是例子了，**正确的例子是时装**。

分类今天有唯一实现：`ItemPlacementRuleService.ClassifyAsync`，**签名只收模板号**
（`int itemId`），物理上拿不到库里那个字节。这正是第 1 条希望的「分类依据先定死」。

**另：`PvfEntryLifecycleContentSource.cs:101-102` 这个引用已经失效**，那两行今天不是那对常量。
本仓的记法是**按内容索引、不写行号**（`behavior-change-list.md` 开头写着这条纪律，
理由是本仓有过在只追加的文件里列行号、随后行号全部漂移的先例）。
**这条注就是那条纪律的一个反例，留在这里当反面教材**：要找那两个常量，
按名字找 `WornPieceItemKind` 与 `CreatureItemKind`。
