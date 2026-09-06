# 行为变化清单（BD-006）

本轮改动：`server-agrees-what-an-avatar-is`，工作区 `.worktrees/server-agrees-what-an-avatar-is`，
基线 `565d33fee899556476f8a582605fb4f439261c0f`，未提交。

**本文件是第二版**（TASK-06 第二轮执行）。第一版写在 TASK-08 落地之前；
QR-06-1 在第一版上找到一条遗漏（背包内拖动一件发放来的时装会把它拆开），
控制器 D-06 判定那不是记载遗漏而是本轮引入的缺陷，新建 TASK-08 修掉它并把 TASK-06 退回重跑。
第二版按修后的实情改写，并把 TASK-08 的生产改动（`ContainerSlotItem.Count`）作为**第五处起因**纳入两个方向的扫描。
第二版里的每一条覆盖读数都由第二轮亲自施加的 23 次变异支撑，未沿用第一轮的读数。

**本文件现在是第三版**（TASK-10）。第三版**不重做任何变异、不动任何可执行代码或判据**，
只做四件事，四件都由 `reviews/change-review.md`（完整变更集独立评审）引出：

1. 新增 **3.7 节「制造者扫」**——第二版扫的是**判定点**，没有扫**写入方**；
   第 37 条（RX-03）的制造者正是一个写入方。这一节独立于 3.1–3.6 可读。
2. 新增 **3.6 的 RX-03**（地面拾取），并把 **RV-08 的两个制造者分开写**（见 3.1 表后）。
   反向扫由此**由 36 条变 37 条**。
3. 新增 **3.8 节「按后果分档」**——把「未测定／无覆盖」按「触发时所有者看到什么」排成三档，
   回答「先看哪个」。这一节**不改 3.5 的任何一个数**。
4. 补记 **第五节的 NB-1b**：「剩余秒数约 2.1×10⁹」是错的，实际约 3.59×10⁸ 秒、约 11.4 年；
   原文保留，算式写出。同一处生产注释（`ItemContainers.ArrivingAvatar.NeverExpires` 的 `<remarks>`）
   同步订正，那是第三版唯一动过的生产文件，**只改注释措辞**。

**第三版没有重测交付态散列**，第〇节下面那份散列表是第二版的读数，本版未重测也未沿用作新凭据。

## 〇、这份清单怎么读

**记法口径（D-03c 收窄后的口径，本清单沿用）**。清单里只写两类句子：

1. **观测**：「在 X 配置下施加变异 Y，观测到 Z」，附施加方、出处与散列。
2. **明写未确立**：「这些实验不足以确定 …」。

另有第三类句子被单独标注：**代码级读出**——由工作区文件的语句文本直接读出，附文件与成员名。
它不是变异实测的结论，标注处会写明证据类型。

**本清单不写全称量词**。需要说「一类」的地方，改写为一次具名的观测，或写成「不足以确定」。
自查判准不是词表：写完之后逐句问「这句在说一个个例，还是在说一类？」，施用结果记在 TASK 文件的自查小节。

**索引口径**：按成员名索引，不按行号。同一成员内的多处判定分别计，并写出它是哪一处。

**「有判据覆盖」三种读数的定义（CK-016）**。本清单只用下面三个词，各自的意思在此写死：

- 「**有覆盖**」＝ 本轮对该处施加过一次能改变它行为的具名变异，那次全量运行报了失败，
  且变红的判据里有走这条路径的（判据名逐条列出）。
  写作「有覆盖」的条目在 3.5 节逐条列出（15 条），逐条在第四节能找到与之对应的那次变异及其散列。
- 「**无覆盖**」＝ 本轮对该处施加过一次能改变它行为的具名变异，而结果是下面两种之一：
  那次全量运行**报 0 失败**；或者那次运行报了失败，但把变红的判据逐条列出之后，
  **其中没有一条走这条路径**。
- 「**未测定**」＝ 本轮没有对该处施加变异，因此本清单不就其覆盖情况作断言。

本清单**不以「全量判据全绿」作为覆盖的凭据**——一次全绿恰恰是「无覆盖」的读数，不是「有覆盖」的。

**交付态散列**（本清单第二轮施加过变异的**十二个**生产文件，变异前后各测一次，末态与首态相同）：

```
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da  PvfEntryLifecycleContentSource.cs
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db  ItemContainers.cs
0438efe963c79cb54ae5f608c7b3c5e8abb94c79aa60073a2ddf835e4e8aa177  ItemSlotRuleService.cs
c6fb4bac69d03fa461670a04a4af67df5de88a1776bf9c7e88180d0024e0f4ba  ItemArrivalRuleService.cs
7e9f4678af319acc514afd0d2ee179bc3f7325e6aaf16ac63fa62b6baf9b5349  InventoryEntryAssembly.cs
87f22d2cd107eec558a542e14cd8e9a93d0300bb42c358a898a7a8469a9c87fe  SqliteItemDomainStore.cs
d96cdb0127c277610a30b672567053317ebade93367a2685d0aa7ecc7b9550d9  MoveItemSpaceSlice.cs
18c22acf2209378aa5e947899d8559f7385dc976aae895dac1e99f67f6ff2dd5  GameInventoryDelivery.cs
be1fd3acb3970d6c5048eb6401eff6170706734045db023726c058022ecde752  StateSynchronizationPayloadCodec.Entry.cs
a39f94f96f3000cb393c1f34c9631d10477a1d48d5c7a501d5afd04ae41cd659  IItemContainer.cs
60877044b4d3070f9c927f4e13d5bf9a3be41d144f07ad0e155cff3f26a3e533  InventoryItemTables.cs
95332a7628bd0671920b5d741923c20e50c3b32830b166e0f33b25f7650ec768  CharacterDatabaseSchema.cs
```

**第三版的一处例外，必须先读**：上表 `ItemContainers.cs` 那一行的散列
`8e7f085d…` 是**第二版交付态**的读数。第三版按第五节 NB-1b 订正了该文件里
`ArrivingAvatar.NeverExpires` 的 `<remarks>`（**只改注释措辞，没有改任何可执行代码**），
所以该文件的散列已经不是这个数。第三版实测的新散列写在 TASK-10 的执行结果里，
上表其余十一行未重测、也未被第三版改动。

其中 `GameInventoryDelivery.cs`、`StateSynchronizationPayloadCodec.Entry.cs`、`InventoryItemTables.cs`、
`CharacterDatabaseSchema.cs` 四个本轮未被改动（`git status --porcelain` 里不出现），
列在这里是因为本清单对它们施加过变异。
`IItemContainer.cs` 是 TASK-08 改动的那个文件（起因 E）。
`git status --porcelain` 在第二轮开工与收尾各读一次，**均为 18 行**（12 个 ` M` ＋ 6 个 `??`），逐行一致；
第一轮时是 16 行，多出的两行是 TASK-08 的 `IItemContainer.cs`（` M`）与
`DraggedAvatarStaysWholeTests.cs`（`??`）。

---

## 一、本轮改动的起因（5 处）

计数口径：本轮在生产代码里**改变行为**的改动点，按成员名列出。
另有五处只改记载的订正不计入起因，分别是 `ItemPlacementContent.ItemWorldClass`、
`IItemPlacementRuleService.ClassifyAsync`、`ItemPlacementRuleService.ClassifyAsync`、
`InventoryEntryAssembly.IdentityOfAsync`、`SqliteItemDomainStoreSession.Write` 五处的 `<remarks>`。
其中四处所在的文件本轮**只被改了注释**，本轮亲自复算过（口径：`git diff -U0` 里以 `+` 或 `-` 开头、
去掉两行文件头之后的行数，与其中以 `///` 开头的行数；两个数相等即该文件本轮无可执行代码改动）：
`ItemPlacementContent.cs` 20 / 20、`IItemPlacementRuleService.cs` 11 / 11、
`ItemPlacementRuleService.cs` 14 / 14、`InventoryEntryAssembly.cs` 16 / 16。
第五处所在的 `SqliteItemDomainStore.cs` 是 129 / 113，差的 16 行本轮逐行读过，
分别是被替换掉的 4 行旧 `facts` 表达式、新写的 6 行 `facts` 表达式、新增成员 `FactsOf` 的 5 行与一行空行
—— 即起因 C 本身，没有第二处可执行代码改动。

| 起因 | 位置（成员名） | 改了什么 |
|---|---|---|
| **A** | `PvfEntryLifecycleContentSource.WornPieceMetadata` | 第二个实参由三元式改为 `switch`：`ItemWorldClass.Avatar => AvatarItemKind`（8），此前落 `WornPieceItemKind`（1）。堆叠上限 1、槽段 9..64、`WorldClass` 三项未动 |
| **B** | `ItemContainers.ArrivingAvatar`（`ItemKind`／`NewIdentity`／`Facts`）＋ `StoreBackedItemContainer.LandAsync` 的 `IntoEmptySlot` 分支 | 落地时若 `itemKind == ArrivingAvatar.ItemKind`，把 core 的 `Value` 换成 `NewIdentity()`（`Random.Shared.Next(1, int.MaxValue)`），并把 `Facts()` 的六项事实（到期日 `int.MaxValue`、清除标识 0、珠宝槽 30 个零字节、两色 0、删除日期 0）随写入交出去 |
| **C** | `IItemDomainStore` 的 `StoredItem.AvatarDetail` ＋ `SqliteItemDomainStoreSession.FillAsync` 的 `facts` 取值 ＋ 新私有成员 `FactsOf` | 写入可自带六项事实；结算算 `facts` 时**先看写入自带的**，不命中才落到 `CarriedAvatarsAsync` 的回捞 |
| **D** | `MoveItemSpaceSlice`：`ItemMoveResult.Refused(ItemMoveStatus, int)` 新重载、`MoveItemSpaceCommandHandler.PutOnAsync` 的两个拒绝返回、`MoveItemSpaceHandler.ResolveAsync` 拒绝分支的 `LogInformation` | 两个拒绝返回改传 `piece.ItemId`；拒绝记录的模板串插入 `item {ItemId} `，实参表在 `session.CharacterId.Value` 之后插入 `result.ItemId`。`{To}` 那一项仍是 `message.DestinationSlotIndex` |
| **E** | `ContainerSlotItem.Count`（`Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs`） | `public int Count => Core.Value;` → `public int Count => this.IsPile ? Core.Value : 1;`。同成员的 XML 文档新增一段 `<remarks>`。由 TASK-08 落地 |

**起因 E 的由来，按发生次序记**（每一句都指向一份持久记载）：

1. 起因 B 把落地 core 的 `Value` 由 `opened.Count` 换成 `ArrivingAvatar.NewIdentity()` 抽出来的身份。
2. `ContainerSlotItem.Count` 在基线上写的是 `=> Core.Value`，**无条件**读同一个字段。
3. **该属性的 `<summary>` 在基线 `565d33fe` 上写的就是**
   「How many the slot holds. **For anything that does not stack, one.**」——
   实现对不堆叠的东西答的却是 `Core.Value`，与这句话相反。
   证据：`git show HEAD:Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` 的第 300 行是这句 `<summary>`、
   第 301 行才是 `Count => Core.Value`；TASK-08 的 diff 里那一行是上下文行而非新增行。
   出处：QR-08-1 第二节与 D-08。**这是本成果第三次撞上「注释漂移到反面」。**
4. 于是起因 B 抽出来的身份被 `DecideMoveAsync` 当成「这一格有几个」读，
   把背包内拖动一件发放来的时装变成了一次拆分——见 3.6 的 RX-01／RX-02。
5. TASK-08 的修法是 `Count => this.IsPile ? Core.Value : 1`，
   分界用既有的 `IsPile`（`=> this.WorldClass is ItemWorldClass.Interchangeable`），不新造概念。
6. **发现者是 QR-06-1（独立快审），不是执行者自查。**

---

## 二、正向扫：从每处起因往下游追

**计数口径**：一条 = 一个被起因触达的**判定点**（按成员名；同一成员内的多处判定分别计）。
同一判定点被多个起因触达时，只在第一次出现的那个起因下计一次，并在该行注明另一个起因。

**合计 60 条**，分别是：起因 A 下 36 条（A-01..A-36）、起因 B 下 5 条（B-01..B-05）、
起因 C 下 3 条（C-01..C-03）、起因 D 下 5 条（D-01..D-05）、起因 E 下 11 条（E-01..E-11）。
（第一版是 48 条；第二版多出的 12 条是起因 E 的 11 条读点，加上 A-36。）

### 起因 A —— 时装模板的种类字节由 1 变 8（36 条）

| 编号 | 判定点（成员名） | 这一轮它对一件时装的答案 | 凭据 |
|---|---|---|---|
| A-01 | `PvfItemPlacementDefinitionSource.GetAsync` 把 `RefillItemMetadata.ItemKind` 转写成 `ItemPlacement.ItemKind` | 变：由 1 变 8 | 观测：`GrantedAvatarIsWholeTests.The_archive_and_storage_answer_the_same_kind_for_one_avatar` 对模板 `101550507` 断言 `Assert.Equal(KindStorageJudgesAnAvatarBy, fromTheArchive!.ItemKind)`；MU-1 下该判据变红。**措辞订正（出处：QR-06-1 非阻断观察第 ③ 条）**：第一版这一栏写的是「字面量 8，不借发放通路的常量」。本轮读了判据文件核对：`KindStorageJudgesAnAvatarBy` 是 `GrantedAvatarIsWholeTests` 自己的 `private const byte KindStorageJudgesAnAvatarBy = 8;`。「不借发放通路的常量」这半句成立（它不引用 `ArrivingAvatar.ItemKind` 之类）；「字面量 8」这半句不精确，断言处写的是这个文件内的 `const` 名字 |
| A-02 | `PvfEntryLifecycleContentSource.ResolveItemMetadata` → `DailyRefillRule.ItemKind` → `DailyRefillService` → `ItemRefill.ItemKind` → `StoreBackedItemContainer.RefillAsync` | 结构上同一个 `WornPieceMetadata` 的返回值，故同样由 1 变 8 | 代码级读出：`WornPieceMetadata` 的返回值同时被这两条通路读取。实测面：`PvfEntryLifecycleContentSourceTests.DailyRefill_RealArchiveProducesAuthoritativeRulesWithStackLimits` 断言的 `ItemKind` 集合仍全绿（出处：TASK-02 执行结果「既有判据的期望值是否被改」一节记的六个模板 `2680738`／`2749110`／`10002520`／`10093393`／`10003235`／`690001556` 都是 kind 4） |
| A-03 | `ItemSlotRuleService.AllowsQuickSlot` | 变：`AllowsQuickSlot(1)` 为真，`AllowsQuickSlot(8)` 为假（排除项里写着 `KindAvatar`） | MU-2（见第四节） |
| A-04 | `ItemSlotRuleService.CanOccupyMainSlot` 的快捷栏那一支 | 变：槽 3..8 对时装答 False | 观测：`AvatarSlotRulesAfterTheKindChangeTests.A_quick_slot_refuses_the_avatar_the_archive_declares` 三个 `InlineData`（3／5／8）；MU-1 与 MU-2 各让这三个变红 |
| A-05 | `ItemArrivalRuleService.BandsFor` 的第一个分支条件 | 变：时装此前经 `placement.ItemKind == EquipmentKind` 进入，现在经 `!ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)` 进入；两条通路都 `yield return own; yield break;` | MU-3（见第四节）。另见第五节 A-06e 那一条 |
| A-06 | `ItemArrivalRuleService.BandsFor` 的 `placement.ItemKind == ConsumableKind` 比较 | 不变：`ConsumableKind` 是 2；1 与 8 都不等于 2。就一件时装而言，执行到不了这一句——上一支已 `yield break` | 代码级读出 |
| A-07 | `StoreBackedItemContainer.DecideMoveAsync` 的目标格判定 `!slotRules.CanOccupyMainSlot(placement, targetSlot)` | 变：一件时装被拖到槽 3..8 时，这一句现在为真 | 见反向扫 RV-02 |
| A-08 | `StoreBackedItemContainer.DecideMoveAsync` 的被挤开一方判定 `!slotRules.CanOccupyMainSlot(displacedPlacement, moving.SlotIndex)` | 变：被挤开的是一件时装、要去的是槽 3..8 时，这一句现在为真 | 见反向扫 RV-03 |
| A-09 | `ItemMovementDomainService.MayOccupyMainSlotAsync` | 变：对时装 + 槽 3..8 答 `SlotNotAllowed` | 见反向扫 RV-04 |
| A-10 | `MoveItemSpaceCommandHandler.TakeOffAsync` 的 `case MainSlotAdmission.SlotNotAllowed` | 变：入口条件由 A-09 决定 | 见反向扫 RV-04 |
| A-11 | `StoreBackedItemContainer.LandAsync` 的 `itemKind == 0` 拒绝 | 不变：1 与 8 都不等于 0 | 代码级读出 |
| A-12 | `StoreBackedItemContainer.LandAsync` 的 `var isAvatar = itemKind == ArrivingAvatar.ItemKind` | 变：这是本轮新增的分叉，档案给 8 才走得到（同时是起因 B 的入口） | MU-1 下 `GrantedAvatarIsWholeTests` 三条变红，即档案改答 1 时这条分叉走不到 |
| A-13 | `StackableItemCore.Create` → `InventoryItemCore.Encode` 写 `core[0] = value.ItemKind` | 变：落到库里那一行的第 0 字节由 1 变 8 | 观测：`GrantedAvatarIsWholeTests.The_archive_and_storage_answer_the_same_kind_for_one_avatar` 把写下去再读回来的那一行的 `Core.ItemKind` 断言为 8 |
| A-14 | `SqliteItemDomainStoreSession.FillAsync` 的回捞分支条件 `item.Core.ItemKind == AvatarKind` | 变：发放来的行现在满足它（`AvatarKind` 是 8） | MU-7（见第四节） |
| A-15 | `SqliteItemDomainStoreSession.CarriedAvatarsAsync` 的 staged 过滤 `staged.Value is { Core.ItemKind: AvatarKind }` | 变：发放来的时装现在进得了这份回捞 | MU-7 |
| A-16 | `SqliteItemDomainStoreSession` 幸存者匹配里的 `core.ItemKind == placing.Item.Core.ItemKind` | 变：库里一条 kind 为 1 的旧时装行，与本轮 kind 为 8 的写入不再在这一句上相等 | 见反向扫 RV-14 |
| A-17 | `InventoryEntryAssembly.SaveAsync` 的脏租约时装扫描 `.Where(value => value.Core.ItemKind == 8)` | 变：发放来的时装现在进得了这份扫描 | 见反向扫 RV-06 |
| A-18 | `InventoryEntryAssembly.SaveAsync` 的 `worldClass == ItemWorldClass.Avatar && item.Core.ItemKind != 8` 拒绝 | 变（方向相反）：发放来的时装此前落在这条拒绝上，现在过 | 见反向扫 RV-17 |
| A-19 | `InventoryEntryAssembly.SaveAsync` 的 `var avatarDetail = item.Core.ItemKind == 8 ? item.AvatarDetail : null` | 变：发放来的时装的六项事实现在跟着这一句写下去，此前被这一句置 null | MU-1 下往返判据变红 |
| A-20 | `InventoryEntryAssembly.WithAvatarDetail` 的 `if (item.Core.ItemKind != 8) return item;` 早返 | 变：发放来的时装现在不走早返，往下走三条检查 | MU-5 下往返判据以 `Avatar detail ... is unavailable.` 变红（TASK-02 变异 B 记的异常原文与此同源） |
| A-21 | `WithAvatarDetail` 的 `item.Core.Value <= 0` 拒绝 | 变：现在到得了（同时是起因 B 触达的判定点） | 见反向扫 RV-07 |
| A-22 | `WithAvatarDetail` 的 `detail is null` 拒绝 | 变：现在到得了 | 见反向扫 RV-08 |
| A-23 | `WithAvatarDetail` 的珠宝槽长度／两色范围拒绝 | 变：现在到得了 | 见反向扫 RV-09 |
| A-24 | `GameInventoryDelivery.WriteItem` 的 `core.ItemKind == 8 && avatar is null` 拒绝 | 变：现在到得了 | 见反向扫 RV-10 |
| A-25 | `GameInventoryDelivery.WriteItem` 的 `core.ItemKind != 8 && avatar is not null` 拒绝 | 变（方向相反）：kind 1 带事实那种行今天造不出来 | 见反向扫 RV-18 |
| A-26 | `GameInventoryDelivery.WriteItem` 的第三个字段三元（`ItemKind == 8` 时写 `Math.Max(0, avatar!.ExpireDate - now)`，否则写 `core.Value`） | 变：发放来的时装现在走左支 | 见反向扫 RP-06；MU-16 让字节锚变红 |
| A-27 | `GameInventoryDelivery.WriteItem` 尾部 `if (core.ItemKind == 8)` 的三十字节珠宝槽与两色块 | 变：发放来的时装现在多写这一块 | 本轮未对这一块单独施加变异。相邻的 MU-16（同成员第三个字段）让字节锚变红，说明该锚对这一成员的时装分支是敏感的；这一块本身记作**未测定** |
| A-28 | `StateSynchronizationPayloadCodec.Entry.WriteNoti2EquippedItem` 的 `core.ItemKind == avatarKind && item.AvatarDetail is null` 拒绝 | 生产上到不了（见反向扫 RV-12） | 代码级读出 |
| A-29 | 同成员的 `core.ItemKind != avatarKind && item.AvatarDetail is not null` 拒绝 | 生产上到不了（见反向扫 RV-19） | 代码级读出 |
| A-30 | 同成员写第三个字段的 `core.ItemKind == avatarKind ? RemainingSeconds(...) : core.Value` | 生产上到不了 | MU-15 让 `SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty` 变红，字节锚未红 |
| A-31 | 同成员的 `core.ItemKind == avatarKind && item.AvatarDetail!.ClearAvatarId != 0` 覆盖 `SealFlag` 的三元 | 生产上到不了 | 代码级读出 |
| A-32 | 同成员 `IsCostumeBarSlot` 里的 `core.ItemKind == avatarKind` 与三十字节检查 | 生产上到不了（见反向扫 RV-13） | 代码级读出 |
| A-33 | `SelectCharacterPostEntryServices` 的负重求和条件 `value.Core.ItemKind is 2 or 3 or 4 or 7 or 9 or 10 or 11` | 不变：这个字面集合里既没有 1 也没有 8 | 代码级读出 |
| A-34 | `SelectCharacterPreparationService` 的 `item.SlotIndex != 24 \|\| item.Core.ItemKind != 5` | 不变：1 与 8 都不等于 5 | 代码级读出 |
| A-35 | `InventoryEntryServices` 的 `item.SlotIndex == CreatureEquipSlot && item.Core.ItemKind == CreatureItemKind`（`CreatureItemKind` 为 5） | 不变：同上 | 代码级读出 |
| A-36 | `GameInventoryDelivery.WriteItem` 的 `writer.Write(core.ItemKind == 2 ? core.Marker16 : Math.Max(0, core.Marker16));` | 不变：1 与 8 都不等于 2，改动前后一件时装都落在 `else` 那一支 | 代码级读出。**第二版新增**，出处 QR-06-1 非阻断观察第 ④ 条：第一版的 35 条里漏了这个读 `ItemKind` 的判定点。与 A-06／A-11／A-33 同类 |

### 起因 B —— 发放时造身份与六项事实（5 条）

| 编号 | 判定点（成员名） | 这一轮 | 凭据 |
|---|---|---|---|
| B-01 | `ArrivingAvatar.NewIdentity()` 写进落地 core 的 `Value` | 变：此前那一位是 `opened.Count`（一件时装开一格，值为 1） | MU-4（见第四节）。另见第五节的 NB-1 与 NB-2。**这一位换了值，谁在读它**——第二版顺着这一问补了起因 E 那一整张表（E-01..E-11）与 3.6 的 RX-01／RX-02；第一版的 B-01..B-05 停在 `FillAsync`，没有往容器动词方向追（出处：QR-06-1 第三节） |
| B-02 | `ArrivingAvatar.Facts()` 交给 `StoredItem.AvatarDetail` | 变：此前发放通路不交任何事实 | MU-5 |
| B-03 | `SqliteItemDomainStoreSession.FillAsync` 里 `@expire` 参数的绑定，以及守卫左侧 `@expire IS NULL` 的取值 | 变：一次发放时装的写入现在把 `@expire` 绑成 `int.MaxValue` 而不是 `DBNull`，左侧不再取真 | MU-6 |
| B-04 | 同一语句守卫子查询的候选集与比对键（`standing.avatar_expire_date IS NOT NULL` 与 `substr(standing.item_core, 6, 4) = substr(@core, 6, 4)`） | 变：发放来的行现在带着非空的 `avatar_expire_date` 落地，因此进得了下一次写入的候选集 | MU-6；另见第六节 |
| B-05 | `FillAsync` 的 `allocated is null ? throw new InvalidOperationException("The world took the item and named no identity.") : ...` | 变：这条出口现在对首次入库的时装到得了 | MU-6；另有 MU-4 下的一次实测（见第四节 MU-4 的第二条红） |

### 起因 C —— 写入自带的事实优先（3 条）

| 编号 | 判定点（成员名） | 这一轮 | 凭据 |
|---|---|---|---|
| C-01 | `SqliteItemDomainStoreSession.FillAsync` 的 `facts` 两个来源的次序（`item.AvatarDetail is { } carriedByTheWrite ? FactsOf(...) : (回捞)`） | 变：新增了第一个来源 | MU-7 |
| C-02 | `SqliteItemDomainStoreSession.FactsOf` 把六项排成 `AvatarColumns` 绑定的次序，两个颜色加宽到 `int` | 新增 | MU-7 |
| C-03 | `StoreBackedItemContainer.PutAsync` 转调 `WriteOneAsync(slotIndex, core, null, cancellationToken)` | 不变：公开签名与语义未动，非时装那一路仍传 null | 观测：第二轮交付态的两次全量运行（开工、收尾）都报 2234 通过 / 2 跳过 / 0 失败，其中 `Dfo.Server.Application.Tests` 两次都报 1036 通过 / 0 失败——移动、拆分、合并、消耗、卡片直放那批既有判据就在这 1036 条里。**这是一条「不变」的凭据，不是「有覆盖」的凭据**（第九节已写明它不足以确定别的输入上的答案）。**QR-06-1 第四节末对这一栏提过一条整改建议**：当时那 1032 条里没有一条把一件 kind 8 的行送进 `MoveToAsync`，所以「移动那批判据全绿」在时装这件事上什么也没说。**第二版的实情已经变了**：TASK-08 新增的 `DraggedAvatarStaysWholeTests` 三条时装判据走的正是 `bag.MoveToAsync` → `DecideMoveAsync`（本轮 MU-20 让这三条变红，见第四节），它们就在这 1036 条里 |

### 起因 D —— 拒绝结果与拒绝记录（5 条）

| 编号 | 判定点（成员名） | 这一轮 | 凭据 |
|---|---|---|---|
| D-01 | `ItemMoveResult.Refused(ItemMoveStatus status, int itemId)` | 新增重载；原单参重载 `Refused(ItemMoveStatus)` 的方法体逐字符未变，仍写 `new(status, 0, 0)` | 代码级读出 ＋ MU-9／MU-10 |
| D-02 | `MoveItemSpaceCommandHandler.PutOnAsync` 的 `slot is null` → `Refused(NotWearable, piece.ItemId)` | 变：携带模板号 | MU-9 |
| D-03 | `PutOnAsync` 的 `slot.Value != command.DestinationSlotIndex` → `Refused(ItemCannotOccupySlot, piece.ItemId)` | 变：携带模板号 | MU-10 |
| D-04 | `MoveItemSpaceHandler.ResolveAsync` 拒绝分支的 `LogInformation` | 变：模板串新增 `item {ItemId} `，实参新增 `result.ItemId`；`{To}` 仍是 `message.DestinationSlotIndex` | MU-8（模板号那一半）；`{To}` 那一半由 TASK-05 的变异 D 支撑（散列 `b22a323f…`，2 红），本轮未重做 |
| D-05 | `ResolveAsync` 拒绝分支的响应构造 `MoveItemSpaceResponse.Failure(message, ErrorCodeFor(result.Status))` | 不变：`result.ItemId` 不出现在这一句里，只出现在上一句 `LogInformation` 的实参表里 | **代码级读出**：拒绝分支的响应由 `message` 与 `ErrorCodeFor(result.Status)` 两样造出，帧的字节布局与内容不因 `ItemId` 而变 |

### 起因 E —— `ContainerSlotItem.Count` 对不堆叠的东西改答 1（11 条）

**计数口径**：在 `Server/src` 下，取一个静态类型为 `ContainerSlotItem` 的表达式的 `.Count`，
一处读点计一条；同一个判定里的两句（E-08）合记为一处并注明。
这 11 处由本轮亲自枚举核对，与 QR-08-1 第一节记的 11 处**逐条相同**。
`Dfo.Server.Protocol` 里读容器的四处（`SelectCardCommand`、`GroundPickupSlice`、
`MoveItemSpaceSlice` 的两条装备通路、`UseStackableSlice`）读的是 `.Core` 而不是这个属性；
`MoveItemSpaceSlice.cs` 里出现的 `Count` 是请求帧自己的 `MoveCount`；
`TaggedIdReportHandler` 的 `item.Count` 读的是 `TutorialRewardItem` 与 `GrantTutorialRewardResult.Placed` 的成员，
不是这个属性——三处都由本轮读类型核对，不计入。

| 编号 | 读点（成员名） | 这一轮它对一件不堆叠的东西的答案 | 凭据 |
|---|---|---|---|
| E-01 | `ItemContainerOperations.SplitAsync` 的源格写值 `moving.Core with { Value = moving.Count - moveCount }` | 变：由「身份 − moveCount」变成「1 − moveCount」。对一件非堆，本轮之后 `DecideMoveAsync` 不再选 `Split`（见 E-07），这一句对非堆走不到 | MU-20：3 红（`DraggedAvatarStaysWholeTests` 三条）；MU-21（`Count => 1`）：22 红，其中 `SplitPileTests` 三条走这一句 |
| E-02 | `MergeAsync` 的目标格写值 `target.Core with { Value = checked(target.Count + moveCount) }` | 变：非堆的这一位由身份变 1。**合堆那一支对非堆走不到**：前置是 `placement.StackLimit > 1`，而 `PvfEntryLifecycleContentSource.WornPieceMetadata` 的返回语句写的是 `new(1, <kind 的 switch>, 9, 64, worldClass)`——**第一个实参是字面量 `1`，与 `worldClass` 取什么值无关**（本轮读该方法核对） | 代码级读出（`WornPieceMetadata` 与 `DecideMoveAsync` 的 `&&` 短路）；堆那一侧由 MU-21 的 `MoveItemSpaceHandlerTests.Two_piles_of_one_item_merge` 等打红 |
| E-03 | `MergeAsync` 的 `moveCount == moving.Count` | 同 E-02：只在合堆分支内 | 代码级读出 |
| E-04 | `MergeAsync` 的 `moving.Count - moveCount` | 同 E-02 | 代码级读出 |
| E-05 | `ItemContainerOperations.ConsumeAsync` 的 `var remaining = item.Count - count;` | 变：非堆由「身份 − count」变成「1 − count」 | 见反向扫 RV-22。**本轮未对这一路施加变异，不足以确定它在生产上会不会被非堆走到** |
| E-06 | `DecideMoveAsync` 的夹取 `requestedCount <= 0 \|\| requestedCount > moving.Count ? moving.Count : requestedCount` | 变：非堆的 `moving.Count` 由身份变 1，于是 `moveCount` 被夹到 1 | MU-20 |
| E-07 | `DecideMoveAsync` 目标格为空时的 `moveCount == moving.Count ? Relocate : Split` | 变：非堆由 `Split` 改回 `Relocate` | MU-20（时装这一侧）；见反向扫 RP-08（普通装备那一侧） |
| E-08 | `DecideMoveAsync` 目标格有东西时的 `placement.StackLimit > 1 && displaced.Count + moveCount <= placement.StackLimit` 与 `moveCount != moving.Count`（同一个判定的两句，合记一处） | 变：非堆的 `moveCount != moving.Count` 由真变假，于是不再返回 `PartialSwapNotAllowed` | MU-20：`A_drag_of_one_onto_another_avatar_trades_the_two_slots` 变红，读数原文 `Expected: Moved { MoveCount = 1, ItemId = 101550507 }` / `Actual: Refused { Reason = PartialSwapNotAllowed }` |
| E-09 | `StoreBackedItemContainer.PilesOf` 的 `new ItemPile(item.SlotIndex, item.Count)` | 变：非堆喂进 `ItemPile.Count` 的数由身份变 1 | 下游 `ItemArrivalRuleService.ForGrant` 的 pile 循环前置是 `stackLimit > 1`（`stackLimit = placement.StackLimit > 0 ? placement.StackLimit : int.MaxValue`），对 worn piece 取 1，走不到；`ForDailyRefill` 不看 `stackLimit`，读得到（见 E-10）。代码级读出 |
| E-10 | `ItemArrivalRuleService.ForDailyRefill` 的 `new ItemArrival.OntoExistingPile(pile.SlotIndex, grant, checked(pile.Count + grant))` | 变：若某条每日补给规则点名一个 worn piece 模板，这一句读到的数由身份变 1 | 见反向扫 RV-22 那一栏。**本轮未实测这一路，不足以确定它在真实归档与真实规则下会不会被走到** |
| E-11 | `DailyRefillService` 的 `held.Sum(item => Math.Max(0, item.Count))` 与 `ItemUseEffectDomainService.ApplyAsync` 的 `var remaining = item.Count;`（两句都在这一条里，各写一次） | 变：同一格若是 worn piece，前一句累加的数由身份变 1；后一句作为 `ItemUseApplication` 的回报值由身份变 1 | **本轮未实测这两句，不足以确定它们在生产上会不会被非堆走到。** `ItemUseEffectDomainService` 那一句 QR-08-1 在枚举里列过、未逐条判读，本清单在此补记 |

---

## 三、反向扫：本轮新增或改变的拒绝与放行路径

**计数口径**：一条 = 一个「路径 × 方向」。**合计 37 条**，分别是：
变严或新增的拒绝 19 条（RV-01..RV-16、RV-20、RV-21、RV-22）、
变松或消失的拒绝 4 条（RV-17、RV-18、RV-19、RV-23）、
新增或改变的放行 9 条（RP-01..RP-09）、量具缺口 2 条（RG-01、RG-02）、
本轮引入并在本轮修掉的缺陷 3 条（3.6 的 RX-01、RX-02、**RX-03**）。

**第 37 条是 RX-03**（地面拾取），**第三版新增**。它由完整变更评审的阻断发现 BF-1 查出，
TASK-09 修掉；本节此前的 36 条里没有它。它的记法见 3.6，它的制造者见新增的第 3.7 节「制造者扫」。
**本节此前的口径「一条 ＝ 一个『路径 × 方向』」扫的是判定点，不是写入方**——
RX-03 正是这个盲区的产物（出处：`reviews/change-review.md` 第八节）。

**第一版是 28 条，第二版多出 8 条，逐条列出它们的来历**：
RV-20（起因 C 让入场存盘侧的撞号守卫对一件发放来的时装到得了）、
RV-21（`items.avatar_jewel_socket` 的 CHECK，出处 QR-06-1 非阻断观察第 ② 条）、
RV-22（`ConsumeAsync` 对非堆，出处 QR-06-1 非阻断观察第 ① 条 ＋ A-06g 第二节第 2 点）、
RV-23 与 RP-08（起因 E 对**普通装备**这类非堆的两个方向，**本轮重扫时新找到的**，见下）、
RP-09（`InventoryItemState.Holds` 的 Template 分支）、
RX-01 与 RX-02（第一版被 QR-06-1 拦下的那两条，定性已变，改记在 3.6）。

**本节不以「全量判据全绿」作为覆盖凭据。** 下面各行的「判据有无」一栏，取值按第〇节写死的三个定义，
由第四节里那次具名变异的读数决定；逐行在「凭据」栏点名是哪一次。

### 3.1 变严或新增的拒绝（19 条）

| 编号 | 位置（成员名） | 拒绝说什么 | 生产可达性 | 判据有无 | 凭据 |
|---|---|---|---|---|---|
| RV-01 | `ItemSlotRuleService.CanOccupyMainSlot` 的快捷栏那一支（经 `AllowsQuickSlot`） | 对一件时装 + 槽 3..8 答 False | 可达：`ItemMovementDomainService.MayOccupyMainSlotAsync` 与 `StoreBackedItemContainer.DecideMoveAsync` 两个调用点都读它 | **有覆盖** | MU-2：5 红，含 `AvatarSlotRulesAfterTheKindChangeTests.A_quick_slot_refuses_the_avatar_the_archive_declares` 的三个 `InlineData` 与既有的 `ItemSlotRuleServiceTests.The_quick_slots_refuse_the_kinds_that_belong_to_another_list(itemKind: 8)` |
| RV-02 | `StoreBackedItemContainer.DecideMoveAsync` 的 `placement is null \|\| !slotRules.CanOccupyMainSlot(placement, targetSlot)` → `ItemMoveRefusal.ItemCannotOccupySlot` | 背包里把一件时装拖到槽 3..8 被拒 | **可达**：`AddAsync` 把发放来的时装落在自有段（实测落第 9 格），随后 `MoveItemSpaceCommandHandler.HandleAsync` 的 Main→Main 一支调 `bag.MoveToAsync`，走到这里。改动前 `AllowsQuickSlot(1)` 为真，这一句为假 | **无覆盖** | MU-2 那次运行报失败 5，5 条判据分别是上面 RV-01 列的那五条；其中没有一条经 `MoveToAsync` 或 `DecideMoveAsync`。**第二轮特意复核了这一条**：TASK-08 新增的 `DraggedAvatarStaysWholeTests` 三条时装判据确实经 `MoveToAsync` → `DecideMoveAsync`，但在 MU-2 那次运行里它们是绿的，所以这个读数不变 |
| RV-03 | `DecideMoveAsync` 的 `displacedPlacement is null \|\| !slotRules.CanOccupyMainSlot(displacedPlacement, moving.SlotIndex)` → `ItemMoveRefusal.DisplacedCannotOccupySlot` | 把快捷栏（3..8）里的一件东西拖到一件时装头上，交换被拒 | **可达**：`moving` 在 3..8、`displaced` 是自有段里的一件时装，是发放之后就能造出的局面 | **无覆盖** | 同 MU-2 那次运行；5 条红里没有交换通路的判据（`DraggedAvatarStaysWholeTests.A_drag_of_one_onto_another_avatar_trades_the_two_slots` 走的是交换，但它在那次运行里是绿的） |
| RV-04 | `ItemMovementDomainService.MayOccupyMainSlotAsync` → `MainSlotAdmission.SlotNotAllowed` → `MoveItemSpaceCommandHandler.TakeOffAsync` 的 `Refused(ItemCannotOccupySlot)` | 把一件时装从装备表取下到槽 3..8 被拒 | **今天到不了**：`TakeOffAsync` 先从装备容器读 `piece`，而时装进不了装备容器——`PutOnAsync` 问 `moves.WornSlotForAsync`，它转调 `placementRules.WornSlotForAsync`，而 `PvfEquipmentSlots.ByName` 是一张 16 条的字面量表，其中以 `avatar]` 结尾的键为 0 条（本轮亲自数过：`["`  开头的行 16 行，匹配 `["[…avatar]"]` 的行 0 行） | **无覆盖** | 同 MU-2 那次运行 |
| RV-05 | `SqliteItemDomainStoreSession.FillAsync` 的撞号守卫 → `InvalidOperationException("The world took the item and named no identity.")`，抛在 `SqliteDatabaseParticipant.PrepareAsync` 里，整笔结算回滚 | 一次入库点名的身份与世界里已站着的时装相同时被拒 | **可达**。**代码级读出**：本轮之前的发放通路不交事实，一次首次入库的时装写入把 `@expire` 绑 `DBNull`，`WHERE @expire IS NULL OR …` 的左侧取真；起因 B 让这类写入把 `@expire` 绑成 `int.MaxValue`，左侧不再取真。至于「哪一道屏障在一般情形下承重」，见第六节末的「明写未确立」 | **有覆盖** | MU-6：3 红，分别是 `TwoAvatarsDoNotShareOneIdentityTests.AWriteNamingAnIdentityTheWorldAlreadyStandsUnderIsRefusedAndRolledBack`、`TwoAvatarsDoNotShareOneIdentityTests.TheGuardIsBlindToFactlessRowsAndEngagesOnceTheWritesCarryFacts`、`SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` |
| RV-06 | `InventoryEntryAssembly.SaveAsync` 的脏租约时装扫描 → `InvalidDataException("A dirty avatar lease requires its exact 30-byte detail row.")` | 存盘前，扫描到的时装若 `Core.Value <= 0`、事实为 null 或珠宝槽不是 30 字节则拒 | **可达**：发放来的时装现在带 kind 8，进得了这份扫描（A-17） | **无覆盖** | MU-13：该次全量运行报 0 失败（2234 通过 / 2 跳过） |
| RV-07 | `InventoryEntryAssembly.WithAvatarDetail` 的 `item.Core.Value <= 0` → `InvalidDataException("Avatar inventory row {itemUid} has no avatar UID.")` | 装载时，一行 kind 8 而身份不大于零被拒 | **可达**：发放来的时装现在走过早返（A-20） | **无覆盖** | MU-11：该次全量运行报 0 失败（2234 通过 / 2 跳过） |
| RV-08 | `WithAvatarDetail` 的 `detail is null` → `InvalidDataException("Avatar detail {item.Core.Value} is unavailable.")` | 装载时，一行 kind 8 而六列取不到被拒 | 可达 | **拒绝本身有覆盖**（本条按此计入 3.5 的 15）；**它的两个制造者分开记，见紧接本表的「RV-08 的两个制造者」** | MU-5：2 红，分别是 `GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts` 与 `GrantedAvatarIsWholeTests.Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`（TASK-02 变异 B 记下的异常原文即 `Avatar detail ... is unavailable.`） |
| RV-09 | `WithAvatarDetail` 的 `detail.JewelSocket.Length != 30 \|\| detail.Color1 is < 0 or > ushort.MaxValue \|\| detail.Color2 is < 0 or > ushort.MaxValue` → `InvalidDataException("... is malformed or has the wrong owner.")` | 装载时，六列取到了但形状不对被拒 | 可达 | **无覆盖** | MU-12：该次全量运行报 0 失败（2234 通过 / 2 跳过） |
| RV-10 | `GameInventoryDelivery.WriteItem` 的 `core.ItemKind == 8 && avatar is null` → `Missing($"AvatarDetail[slot {item.SlotIndex}]")` | 往客户端写背包时，一行 kind 8 而没有事实被拒 | **可达**：发放来的时装在背包里，随入场的背包快照走这条编码 | **有覆盖** | MU-17：1 红，`SelectCharacterPayloadCodecTests.SelectCharacterPayload_HighRiskAvailabilityDistinctionsAreNotZeroFilled` |
| RV-11 | `GameInventoryDelivery.WriteItem` 尾部 `if (avatar!.JewelSocket.Length != 30)` → `InvalidDataException("Avatar jewel socket must contain exactly 30 bytes.")` | 写尾部块前的长度检查 | 可达（同 RV-10） | **无覆盖** | MU-19：该次全量运行报 0 失败（2234 通过 / 2 跳过） |
| RV-12 | `StateSynchronizationPayloadCodec.Entry.WriteNoti2EquippedItem` 的 `core.ItemKind == avatarKind && item.AvatarDetail is null` → `Missing(...)` | 写 NOTI-2 支援装备时缺事实被拒 | **今天到不了**：这条通路写的是装备表的行，而时装进不了装备表（理由同 RV-04） | 未测定（本轮未对该处施加变异） | —— |
| RV-13 | 同成员 `IsCostumeBarSlot` 分支里的 `item.AvatarDetail!.JewelSocket.Length != 30` → `InvalidDataException("Support avatar jewel socket must contain exactly 30 bytes.")` | 同上 | 今天到不了（同 RV-12） | 未测定 | —— |
| RV-14 | `SqliteItemDomainStoreSession` 幸存者匹配的 `core.ItemKind == placing.Item.Core.ItemKind` | 库里一条 kind 为 1 的旧时装行遇上本轮 kind 为 8 的写入时，这一趟匹配不上，搬动退化成删旧建新 | **今天到不了新库**；只对本轮之前已经落过 kind 1 时装行的库有意义 | 未测定（本轮未对该处施加变异；它要的是一个跨版本的旧库夹具，本任务不造判据） | —— |
| RV-15 | `MoveItemSpaceCommandHandler.PutOnAsync` 的 `slot is null` → `Refused(NotWearable, piece.ItemId)` | 拒绝本身未变；变的是它携带的模板号，进而变的是拒绝记录读起来是什么 | **可达**：一件时装被拖向装备表就走到这里（`RefusedEquipIsRecordedWithItsTemplateTests` 用真实档案的 `101500690` 走通了这条路） | **有覆盖** | MU-9：1 红，`RefusedEquipIsRecordedWithItsTemplateTests.An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot` |
| RV-16 | `PutOnAsync` 的 `slot.Value != command.DestinationSlotIndex` → `Refused(ItemCannotOccupySlot, piece.ItemId)` | 同上 | 可达 | **有覆盖** | MU-10：1 红，`RefusedEquipIsRecordedWithItsTemplateTests.A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot` |
| RV-20 | `SqliteCharacterItemTable.UpdateOwnAsync` 与 `.InsertOwnAsync` 的撞号守卫 `NOT EXISTS (SELECT 1 FROM items held WHERE` ＋ 常量 `CarriesTheAvatar`（`"held.avatar_expire_date IS NOT NULL AND substr(held.item_core, 6, 4) = @avatarKey"`），`UpdateOwnAsync` 另加 `AND held.item_uid <> @uid`；守卫不满足时写入触到零行，`InventoryEntryAssembly.SaveAsync` 把它读成 `InvalidDataException("Dirty inventory item {uid} is stale or missing.")` 或 `("Dirty inventory item in slot {n} was not persisted.")` | **入场存盘侧**的撞号拒绝：一次脏租约存盘点名的时装身份若已被别的行占着，整笔被拒 | **本轮新到得了**。代码级读出的链条：`BindAvatar` 在 `detail is null` 时把 `@avatarKey` 绑 `DBNull`，与 null 比较不成立，守卫遇不到任何行；`SaveAsync` 交的 `avatarDetail = item.Core.ItemKind == 8 ? item.AvatarDetail : null`。起因 A 之前一件发放来的时装带 kind 1，在更早的 RV-17 处就被拒、走不到写入；起因 A ＋ B 之后它带 kind 8 且带六项事实，`@avatarKey` 绑成身份的四个字节，这道守卫才对它起作用。与 RV-05（`SqliteItemDomainStoreSession.FillAsync` 的守卫）是**两处不同的语句**，分属两条写入通路 | **有覆盖** | MU-22（第二版新增）：2 红，分别是 `SqliteEntryInventoryMutationRepositoryTests.DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 与 `.DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`，两条的失败读数都是 `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(System.IO.InvalidDataException)`——即它们走的正是这条拒绝 |
| RV-21 | `items` 表上的 `avatar_jewel_socket BLOB CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30)`（`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`） | 库级拒绝：珠宝槽列在场时必须是 30 字节 | **本轮新增了绑定来源**。代码级读出：起因 C 之前 `FillAsync` 的 `@socket` 只有 `CarriedAvatarsAsync` 一个来源，读回来的是已经过这条 CHECK 的行；起因 C 之后 `SqliteItemDomainStoreSession.FactsOf` 把写入自带的 `byte[]` 原样绑上去（`FactsOf` 自己的 `<remarks>` 就写着 What is bound is the array the record carries, whatever length it is. The table is what says a socket that is present is thirty bytes）。**今天到不了**：生产上唯一的生产者是 `ArrivingAvatar.Facts()`，它交的是 30 个零字节。与 RV-18／RV-19 同类。出处：QR-06-1 非阻断观察第 ② 条 | **无覆盖** | MU-23（第二版新增）：把该 CHECK 改成 `CHECK (1 = 1)`，1 红——`ItemCarrierShapeTests.ItemsCarriesEveryConstraintItsSourcesDeclared`。本轮读了这条判据：它拿 `DeclarationAsync("items")` 的 DDL 文本做 `Assert.Contains("CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30)", …)`，**比的是声明文本，没有做一次违反该 CHECK 的写入**。按第〇节的第二种读法（那次运行报了失败，但红的判据里没有一条走这条拒绝路径），这条**拒绝路径**记作无覆盖；**声明本身**由那条判据钉住 |
| RV-22 | `ItemContainerOperations.ConsumeAsync` 的 `var remaining = item.Count - count;` ＋ `if (remaining < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "A pile cannot give up more than it holds.")` | 消耗时索要的数超过这一格所持时被拒 | **对非堆，这个拒绝的触发条件本轮变过两次**。代码级读出：基线上 `Count => Core.Value`，一件发放来的时装的 `Value` 是 `opened.Count`（1），`remaining = 1 - count`；起因 B 之后 `Value` 是抽出来的身份，`remaining = 身份 - count` 不为负，于是走 `PutAsync(… Value = remaining)`——**把身份减一写回去**；起因 E 之后 `Count` 对非堆答 1，`remaining = 1 - count`。相对基线，一件发放来的时装在这一句上的读数与基线相同。**上游是否可达未追**：`ConsumeAsync` 只被 `ItemUseEffectDomainService` 的 `ConsumeFromContainerEffect` 打到，一件时装能不能被「使用」本轮未测。出处：QR-06-1 非阻断观察第 ① 条、QR-08-1「不足以确定的」一段、A-06g 第二节第 2 点 | 未测定（本轮未对 `ConsumeAsync` 施加变异） | —— |

#### RV-08 的两个制造者，分开记（第三版改写）

**前后对照——第二版怎么写的，为什么改。**

- **第二版原文**（保留在此，按仓规不抹去）：
  「判据有无」一栏只有两个字 **有覆盖**，「凭据」一栏只有 MU-5 那一次。
- **改写的理由**（出处：`reviews/change-review.md` 第八节，逐字抄）：

  > 于是 RV-08 被记作「有覆盖」，读起来像已经照看过了，
  > 而**它的第二个生产制造者从头到尾没有被枚举** —— BF-1 就是这么漏掉的。
  > **「有覆盖」这个词在这里压掉了该说的话**：它说的是「这条拒绝有判据会红」，
  > 读者却会读成「这条拒绝不会被意外触发」。

- **改写后**：把「这条拒绝有没有判据会红」与「谁能把系统送进这条拒绝」拆成两问，逐个答。

**第一问：这条拒绝有没有判据会红——有。** MU-5（把 `LandAsync` 的
`isAvatar ? ArrivingAvatar.Facts() : null` 那一支改掉，即在**源头**上不交事实）下
`GrantedAvatarIsWholeTests` 两条以 `Avatar detail ... is unavailable.` 变红。
3.5 小结里 RV-08 计进「有覆盖」的 15 条，指的就是这一问。

**第二问：谁能把一行「kind 8 但六列为空」造出来送进这条拒绝——今天点名两个生产制造者。**

| 制造者（成员名） | 它怎么把六列弄没的 | 谁发现的 | 今天的状态 | 判据有无 |
|---|---|---|---|---|
| **制造者一：背包内拖动** —— `StoreBackedItemContainer.DecideMoveAsync` 目标格为空那一支 → `ItemContainerOperations.SplitAsync` 的目标格写 | 目标格被写下 `StackableItemCore.Create(8, itemId, 1)`，一行 kind 8、不带六列的**新**核心；`FillAsync` 六列落 NULL | **QR-06-1（独立快审）**，不是执行者自查 | 已由 TASK-08 修掉（`Count => this.IsPile ? Core.Value : 1`），即 3.6 的 RX-01 | **有覆盖** —— MU-20 下 `DraggedAvatarStaysWholeTests.An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts` 正是以 `System.IO.InvalidDataException : Avatar detail 1408050310 is unavailable.` 变红，栈根 `InventoryEntryAssembly.WithAvatarDetail` |
| **制造者二：地面拾取** —— `PickUpGroundDropCommandHandler.CarryOverAsync` 的 `PutAsync` | 对同一格再写一次并**换掉 `Core.Value`**，`Survivors` 三趟都不认 → 行被删掉重建，`CarriedAvatarsAsync` 按**新** `Core.Value` 回捞落空 → 六列 NULL | **完整变更评审 BF-1**（`reviews/change-review.md` 第二节），**不是执行方自查**，也不是快审 | 已由 TASK-09 修掉（对时装只写 `Durability`），即 3.6 的 **RX-03** | **有覆盖** —— MU-09-1 下 `PickedUpAvatarStaysWholeTests.An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts` 以 `System.IO.InvalidDataException : Avatar detail 1075541543 is unavailable.` 变红，栈根 `InventoryEntryAssembly.WithAvatarDetail`（出处：TASK-09 第五节 MU-09-1 那张表） |

**这两条读数与第二版的差别，一句话**：第二版写下「RV-08 有覆盖」的那一刻，
**它覆盖的只有制造者一**；制造者二当时既不在 3.1–3.4 的 34 行里、也不在 3.6 的两条里、
也不在正向扫的 B-01..B-05 里。**这不是「量少了」，是「没扫」。**

**还能不能有第三个制造者？** 见新增的 3.7 节「制造者扫」：那一节按一条可复核的枚举口径
把今天所有能写世界表那一行的成员逐个判过，**在那份清单之内没有找到第三个**；
那一节同时写明了这个枚举口径挡不住什么。

### 3.2 变松或消失的拒绝（4 条）

| 编号 | 位置（成员名） | 这一轮 | 生产可达性 | 判据有无 | 凭据 |
|---|---|---|---|---|---|
| RV-17 | `InventoryEntryAssembly.SaveAsync` 的 `worldClass == ItemWorldClass.Avatar && item.Core.ItemKind != 8` → `InvalidDataException("The archive declares item ... an avatar, but the row in slot ... carries kind ...")` | 一件经发放通路入库的时装，改动前带 kind 1，在这里被拒；改动后带 kind 8，从这里过 | 拒绝本身仍可达：一个调用方仍可以递一个种类字节与档案不符的 core | **有覆盖** | MU-14：1 红，既有判据 `ClassificationRefusalTests.AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot`。另一侧：MU-1 把档案答案改回 1 之后 `GrantedAvatarIsWholeTests` 三条变红 |
| RV-18 | `GameInventoryDelivery.WriteItem` 的 `core.ItemKind != 8 && avatar is not null` → `InvalidDataException("Non-avatar inventory item carries avatar detail.")` | 「kind 1 的行带着六项事实」这种形状，本轮之后由装载侧造不出来了：`InventoryEntryAssembly.SaveAsync` 写 `avatarDetail = item.Core.ItemKind == 8 ? item.AvatarDetail : null`，`WithAvatarDetail` 也在 `ItemKind != 8` 时早返 | 今天由这两条通路到不了 | 未测定 | 代码级读出（上一栏所引的两处语句） |
| RV-19 | `WriteNoti2EquippedItem` 的 `core.ItemKind != avatarKind && item.AvatarDetail is not null` → `InvalidDataException("Non-avatar support equipment carries avatar detail.")` | 同上 | 今天到不了 | 未测定 | 代码级读出 |
| RV-23 | `StoreBackedItemContainer.DecideMoveAsync` 的 `if (moveCount != moving.Count) return (null, ItemMoveRefusal.PartialSwapNotAllowed);`，**就一件不是时装的非堆而言**——`ItemWorldClass.Equipment` 的普通装备与 `Creature` 的契约兽 | 在背包里把一件这样的东西拖到另一件东西头上，改动前被拒（`PartialSwapNotAllowed`，经 `StatusFor` 出去），起因 E 之后走 `Swap` | **可达，而且这条拒绝在基线上就到得了**——它与时装无关。代码级读出的链条：`CharacterRosterService.EquipmentCore` 给新建角色的每一件起始装备写 `Value: Random.Shared.Next(1, int.MaxValue)`（该方法在同文件 `InsertOwnAsync(EquipmentCore(piece), null, cancellationToken)` 处被调用，本轮未改动该文件）；`MoveItemSpaceCommandHandler.TakeOffAsync` 把 `piece.Core` 原样 `bag.PutAsync` 进主列；随后一次 Main→Main 请求经 `HandleAsync` 调 `bag.MoveToAsync` 进 `DecideMoveAsync`。基线上 `Count => Core.Value` 使这样一件装备的 `moving.Count` 是那个抽出来的数，客户端 `MoveCount` 是 1（`MoveItemSpaceDecoder` 从 `body[7..]` 读四字节；`MoveItemSpaceSlice` 自己 `<remarks>` 抄下的实机请求 `00 09 00 40 38 00 00 01 00 00 00 03 0F 00` 在那四个字节上是 `01 00 00 00`），两者不等 → 这条拒绝。起因 E 之后 `Count` 答 1，两者相等 → 不再走这条拒绝。**这一条是第二轮重扫时新找到的**：本轮回去读过的五份记载（清单第一版、QR-06-1、TASK-08 执行结果、QR-08-1、D-08）里，没有一份就普通装备讲过这条链 | **无覆盖** | MU-20（把 `Count` 改回 `Core.Value`）那次运行报失败 3，三条红全在 `DraggedAvatarStaysWholeTests`，**没有一条把一件带抽出来的 `Value` 的普通装备送进 `MoveToAsync`**；MU-21（`Count => 1`）那次报失败 22，逐条列在第四节，**其中也没有这样一条**。按第〇节的第二种读法记作无覆盖 |

### 3.3 新增或改变的放行路径（9 条）

| 编号 | 位置（成员名） | 这一轮 | 生产可达性 | 判据有无 | 凭据 |
|---|---|---|---|---|---|
| RP-01 | `ItemArrivalRuleService.BandsFor` 的第一个分支 | 一件时装改走 `!ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)` 这个析取支进入「只给自有段」那一支；两个析取支给出的波段集合相同 | 可达 | **有覆盖** | MU-3：1 红，`AvatarSlotRulesAfterTheKindChangeTests.An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it`。另见第五节 A-06e |
| RP-02 | `StoreBackedItemContainer.LandAsync` 的 `isAvatar ? landing with { Value = ArrivingAvatar.NewIdentity() } : landing` | 时装落地时 core 的 `Value` 由 `opened.Count` 换成抽取值 | 可达 | **有覆盖** | MU-4：2 红——`A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts` 以纯断言红（`Assert.NotEqual() Failure: Values are equal`，判据文件 line 146），`Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts` 以异常红（`The world took the item and named no identity.`，抛在 `GrantAsync` line 250） |
| RP-03 | `LandAsync` 的 `isAvatar ? ArrivingAvatar.Facts() : null` | 时装的写入携带六项事实 | 可达 | **有覆盖** | MU-5：2 红（见 RV-08） |
| RP-04 | `SqliteItemDomainStoreSession.FillAsync` 的 `facts` 取值：写入自带的优先于回捞 | 新增来源 | 可达 | **有覆盖** | MU-7：7 红——Persistence 4 条（`SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates`、`TwoAvatarsDoNotShareOneIdentityTests` 三条）＋ Application 2 条（`GrantedAvatarIsWholeTests` 两条），另有一条既知偶发（见第四节 MU-7 注） |
| RP-05 | `SqliteItemDomainStoreSession.CarriedAvatarsAsync` 的回捞 | 发放来的时装现在进得了这份回捞（因为它带 kind 8） | 可达 | **有覆盖** | MU-7（同上）。另：TASK-01 的变异 M2 把回捞命中后的取值改成恒 null 时，红的是 `SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`（出处：TASK-01 执行结果「CK-002 变异实测」，非本轮实测） |
| RP-06 | `GameInventoryDelivery.WriteItem` 对一行 kind 8 写的第三个字段与尾部块 | 一件发放来的时装到客户端时，第三个字段是 `Math.Max(0, int.MaxValue - now)`（**订正后：约 3.59×10⁸ 秒，约 11.4 年**；原文写的是「约 2.1×10⁹」，**那个数是错的**，修正案见第五节 NB-1b）而不是落地计数，后面多出「30 + 三十字节 + 4 + 两个颜色」一块 | 可达 | **编码分支有覆盖；「发放来的时装走到这个分支」无覆盖** | MU-16：1 红，正是字节锚 `SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`——该锚的夹具里有一件 kind 8 的行。但那件是夹具直接给的，不是发放通路造的：本轮没有找到任何一条判据把一次发放的结果送进 `GameInventoryDelivery` |
| RP-07 | `MoveItemSpaceHandler.ResolveAsync` 拒绝分支的 `LogInformation` | 记录新增 `item {ItemId}`；`{To}` 那一项位置与取值未动 | 可达 | **有覆盖** | MU-8：2 红，`RefusedEquipIsRecordedWithItsTemplateTests` 两条整句比对判据（`Expected …item 101500690 9->2…` / `Actual …item 0 9->2…`） |
| RP-08 | `StoreBackedItemContainer.DecideMoveAsync` 目标格为空时的 `moveCount == moving.Count ? ContainerMoveKind.Relocate : ContainerMoveKind.Split`，**就一件不是时装的非堆而言**（普通装备、契约兽） | 在背包里把一件这样的东西拖到空格，改动前走 `Split`（`SplitAsync` 把源格重写成 `Value = moving.Count - moveCount`，并在目标格写下 `StackableItemCore.Create(itemKind, itemId, moveCount)`），起因 E 之后走 `Relocate` | 可达，链条与 RV-23 同一条（同一次 `DecideMoveAsync` 调用的另一个分支）。这是 RV-23 的另一个方向：目标格空是这一条，目标格有东西是 RV-23 | **无覆盖** | 同 RV-23 那一栏：MU-20 的 3 条红与 MU-21 的 22 条红里都没有一条走这条路径 |
| RP-09 | `InventoryItemState.Holds` 的 Template 分支 `new InventorySlotContent(ItemSlotKind.Template, this.Core.ItemId, Math.Max(this.Core.Value, 1))`（`Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs`） | 一条尚未拿到世界身份的行，这个投影把 `Core.Value` 当「有几个」答出去。起因 B 之后，一件发放来的时装在这一支上答的是抽出来的身份而不是 1 | **今天到不了生产**。代码级读出：本轮在 `Server/src` 下按 `\.Holds\b` 搜，读点为 0 处；读到它的两处都在判据里（`RecordingCharacterDomainStore` 与 `SplitPileTests`）。与 RV-18／RV-19 同类：形状在，今天没有生产读者 | 未测定（本轮未对 `Holds` 施加变异） | —— |

### 3.4 量具缺口（2 条）

| 编号 | 位置（成员名） | 内容 | 生产可达性 | 判据有无 |
|---|---|---|---|---|
| RG-01 | `MoveItemSpaceCommandHandler.HandleAsync` 的 `if (command.SourceListType != MainListType \|\| command.DestinationListType != MainListType) return ItemMoveResult.Refused(ItemMoveStatus.ListNotServed);` | 时装拖拽若把目标列写成 `InventoryContainerType.Avatar`（=1）而不是 `Equipment`（=3），请求在到达 `PutOnAsync` 之前就在这里走**单参** `Refused`，`ItemId` 为 0，于是 `ResolveAsync` 那行记录读作 `item 0 …->N refused as ListNotServed`：**槽号仍在，模板号丢失**。**证据类型：代码路径读出，非实机观测**——真实客户端写哪个列号尚未观测，这正是实机要回答的问题之一。出处：QR-05-A 非阻断观察第 1 条 | 取决于真实客户端写哪个列号，未观测 | 未测定（本轮未对该处施加变异；这一栏写的是代码路径读出，不是变异读数） |
| RG-02 | `MoveItemSpaceCommandHandler.TakeOffAsync` 的三个拒绝返回（`MainSlotAdmission.ItemUnknown` → `Refused(NotWearable)`、`MainSlotAdmission.SlotNotAllowed` → `Refused(ItemCannotOccupySlot)`、`displacedSlot != command.SourceSlotIndex` → `Refused(DisplacedCannotOccupySlot)`） | 这三个返回分别写作 `Refused(ItemMoveStatus.NotWearable)`、`Refused(ItemMoveStatus.ItemCannotOccupySlot)`、`Refused(ItemMoveStatus.DisplacedCannotOccupySlot)`，三处走的是单参重载，走那条路径时记录里的 `{ItemId}` 是 0。同方法更早的 `NoBoundContainer` 与 `BothSlotsEmpty` 两个返回也是单参。**证据类型：代码级读出**。出处：QR-05-A「记法口径」一节 | 见 RV-04：时装今天到不了 `TakeOffAsync`；非时装可达 | 未测定（本轮未对 `TakeOffAsync` 施加变异。本轮的三条新判据 `The_archive_calls_this_template_an_avatar_and_names_no_worn_slot_for_it`、`An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot`、`A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot` 走的是 `PutOnAsync`） |

### 3.5 反向扫的小结（可复算）

- 写作「**有覆盖**」的条目 **15 条**，分别是：RV-01、RV-05、RV-08、RV-10、RV-15、RV-16、RV-17、
  **RV-20**、RP-01、RP-02、RP-03、RP-04、RP-05、RP-06（只就编码分支而言）、RP-07。
  逐条在第四节能找到与之对应的、本轮亲自施加的那次变异，以及那次运行里变红的判据名。
- 写作「**无覆盖**」的条目 **11 条**，分别是：RV-02、RV-03、RV-04、RV-06、RV-07、RV-09、RV-11、
  **RV-21**、**RV-23**、**RP-08**、RP-06 的另一半（「发放通路到达那个编码分支」这一半）。
  其中 RV-06、RV-07、RV-09、RV-11 走的是第一种读法（该次运行报 0 失败），
  RV-02、RV-03、RV-04、RV-21、RV-23、RP-08、RP-06 的另一半走的是第二种读法
  （该次运行报了失败，但红的判据逐条列出后没有一条走这条路径）。
  第五节的 NB-1 是第十二条，它不在 3.1–3.4 的表里，另计。
- 写作「**未测定**」的条目 **9 条**，分别是：RV-12、RV-13、RV-14、RV-18、RV-19、**RV-22**、
  **RP-09**、RG-01、RG-02。
  另有正向扫里的 A-27（`GameInventoryDelivery.WriteItem` 尾部那一块）与起因 E 的 E-05、E-10、E-11
  同样记作未测定，不在这九条内。
- 3.6 的 RX-01、RX-02 与 **RX-03** 各写作「**有覆盖**」：RX-01／RX-02 的凭据同为本轮的 MU-20，
  **RX-03 的凭据是 TASK-09 的 MU-09-1**（2 红，见 3.6 该行）。三条都不在上面三个数里，另计。
- 计数口径：按 3.1–3.4 四张表的行逐行统计；一行若在两个方向上各有一个判断（RP-06），
  在两处各计一次，并在两处都写明它指的是哪一半。三个数相加 15 ＋ 11 ＋ 9 ＝ 35，
  比 34 行多出的一行正是 RP-06（它被计了两次）。
  3.1–3.4 的行数：19 ＋ 4 ＋ 9 ＋ 2 ＝ 34；加上 3.6 的 **3** 条，本节合计 **37** 条。
- **RV-08 的第二问（制造者）不改这三个数**：RV-08 仍按「拒绝本身有覆盖」计进 15 条那一格，
  它的两个制造者分别记在 3.1 表后的「RV-08 的两个制造者」与 3.6 的 RX-01／RX-03 里。
  **第三版对这三个数的唯一改动是 3.6 由 2 条变 3 条，因此本节合计由 36 变 37。**

### 3.6 本轮引入并在本轮修掉的缺陷（3 条）

**三条各由谁发现，先写在这里，不要混起来读**：RX-01／RX-02 由 **QR-06-1（独立快审）** 发现，
**RX-03 由完整变更评审的阻断发现 BF-1** 发现。**三条都不是执行方自查发现的。**
RX-03 是第三版新增的那一条，它的全文在本节末尾。

**下面这两条（RX-01、RX-02）在清单第一版里不存在**：QR-06-1 第三节把它们作为唯一的阻断理由报出来，
控制器 D-06 判定它们不是记载遗漏而是**本轮引入的缺陷**，新建 TASK-08 修掉，
并要求 TASK-06 重跑时按修后的实情记。**它们不是「本轮新增的拒绝／放行」**，
所以不进 3.1／3.3 的表，单列在这里。

**引入者**：起因 B 把落地 core 的 `Value` 由 `opened.Count` 换成 `ArrivingAvatar.NewIdentity()` 抽出来的身份。
**放大者**：`ContainerSlotItem.Count` 在基线上无条件读同一个字段（`=> Core.Value`），
而**该属性的 `<summary>` 在基线上写的就是**「How many the slot holds. For anything that does not stack, one.」——
实现与它相反（`git show HEAD:` 核实，见第一节起因 E 的第 3 点；本成果第三次遇到注释漂移到反面）。
**修法**：`public int Count => this.IsPile ? Core.Value : 1;`（TASK-08，起因 E）。
**发现者**：**QR-06-1（独立快审），不是执行者自查。**

| 编号 | 位置（成员名） | 缺陷是什么 | 修后的实测读数 | 判据有无 |
|---|---|---|---|---|
| RX-01 | `StoreBackedItemContainer.DecideMoveAsync` 目标格为空那一支 → `ItemContainerOperations.SplitAsync` | 在背包里把一件发放来的时装拖到自有段（9..64）的空格：`moveCount`（1）与 `moving.Count`（抽出来的身份）不等 → 走 `Split`；`SplitAsync` 把源格重写成 `Value = 身份 - 1`，并在目标格写下 `StackableItemCore.Create(8, itemId, 1)`——一行 kind 8、`Value` 为 1、不带六列事实的新行；该行在 `FillAsync` 里六列落成 NULL，下一次入场装载在 `InventoryEntryAssembly.WithAvatarDetail` 抛 `Avatar detail 1 is unavailable.`（即 RV-08） | **修后是整件搬走**：源格读回 `null`，目标格的 `Core.Value` 等于拖动前源格读到的那个身份、`ItemKind` 是 8。判据 `DraggedAvatarStaysWholeTests.A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from` 与 `.An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts` 在交付态下绿 | **有覆盖** | 
| RX-02 | `DecideMoveAsync` 的 `if (moveCount != moving.Count) return (null, ItemMoveRefusal.PartialSwapNotAllowed);` | 目标格站着另一个模板的时装时：改动前 `moveCount == moving.Count`（都是 1）→ 走 `Swap`；起因 B 之后不等 → 返回 `PartialSwapNotAllowed`，经 `StatusFor` 变成一次拒绝 | **修后是整件对调**（`Moved { MoveCount = 1 }`），不是被拒。判据 `DraggedAvatarStaysWholeTests.A_drag_of_one_onto_another_avatar_trades_the_two_slots` 在交付态下绿 | **有覆盖** |

**RX-01／RX-02 的覆盖凭据（本轮亲做，不是沿用）**：MU-20——把 `Count` 改回 `Core.Value`
（变异后散列 `d0c8abe02bdaff2977576d005cc42adcadf505141acca09e248f5e528e39844f`），
全量报失败 3，三条红逐条抄下失败读数：

- `A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from`：
  `Assert.Null() Failure: Value is not null`，`DraggedAvatarStaysWholeTests.cs:line 108`——源格残留。
- `A_drag_of_one_onto_another_avatar_trades_the_two_slots`：
  `Assert.Equal() Failure: Values differ` / `Expected: Moved { MoveCount = 1, ItemId = 101550507 }` /
  `Actual:   Refused { Reason = PartialSwapNotAllowed }`，`DraggedAvatarStaysWholeTests.cs:line 205`。
- `An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts`：
  `System.IO.InvalidDataException : Avatar detail 1408050310 is unavailable.`，
  栈根 `InventoryEntryAssembly.WithAvatarDetail`。

即：这三条把 RX-01 的两个后果（源格残留、下一次装载抛）与 RX-02 的拒绝各钉住一次。

**`Count` 被谁读——修法的回归面**（口径与枚举见正向扫的起因 E，11 处）。
逐处判过之后本轮没有读到被这一改弄坏的读点；关键闸门是
`PvfEntryLifecycleContentSource.WornPieceMetadata` 的返回语句里 `StackLimit` 那一位是字面量 `1`
（`new(1, <kind 的 switch>, 9, 64, worldClass)`，与 `worldClass` 取什么值无关）——
**代码级读出**，本轮读该方法与 `DecideMoveAsync`／`ForGrant` 的前置条件核对：
合堆分支的前置是 `placement.StackLimit > 1`，`ForGrant` 的 pile 循环的前置是 `stackLimit > 1`，
在这个解析下两者对由 `WornPieceMetadata` 产出的东西都不成立。出处：QR-08-1 第一节与 D-08。

**三条未实测路径，逐条记为不足以确定**（出处：QR-08-1「不足以确定的」一段、D-08、A-06g 第二节第 2 点）：

1. `ItemContainerOperations.ConsumeAsync` 对非堆——见 RV-22 与 E-05。
   **这些运行不足以确定它在真实归档与真实规则下会不会被非堆走到。**
2. `ItemArrivalRuleService.ForDailyRefill` 对 worn piece——见 E-10。
   它不看 `stackLimit`，读得到 `ItemPile.Count`；要走到它需要某条每日补给规则点名一个 worn piece 模板。
   **本轮未实测，不足以确定。**
3. `DailyRefillService` 的 `held.Sum(item => Math.Max(0, item.Count))` 对 worn piece——见 E-11。
   **本轮未实测，不足以确定。**

**这个属性被判据承重到什么程度**（出处：QR-08-1 第三节，本轮亲自复现）：
MU-21——把 `Count` 改成恒等于 1（变异后散列
`1ca8c5681174ce32093088c0bdf25f64bc5d188346cdab70e6e59d0073c9d6eb`），
全量报失败 **22 条，跨 8 个测试类**，逐类列出（口径：按失败行的类名归并）：
`SplitPileTests` 3、`SqliteEntryInventoryMutationRepositoryTests` 3、
`MoveItemSpaceHandlerTests` 5、`UseStackableHandlerTests` 3、`TutorialRewardPlacementTests` 3、
`BagPlacementVerbTests` 3、`PersistentContainerBoundaryTests` 1、`DraggedAvatarStaysWholeTests` 1
＝ 3+3+5+3+3+3+1+1 = 22。判据名逐条抄在第四节 MU-21 那一行。
这个读数与 QR-08-1 记的 22 条／8 个类**逐条相同**。

#### RX-03 —— 第 37 条：地面拾取把一件已经站着的时装的六列弄没了（第三版新增）

**引入者**：起因 A ＋ 起因 B。起因 A 让档案对时装答 kind 8，起因 B 让落地时六项事实随写入交出去。
**就地面拾取这一条路而言**（这句只说这一条路，不说别的路）：本轮之前它写下的时装行带 kind 1，
`InventoryEntryAssembly.WithAvatarDetail` 在 `item.Core.ItemKind != 8` 上早返，走不到 `detail is null`；
本轮之后它带 kind 8，才走得到那一句。**代码级读出，本任务亲读 `WithAvatarDetail` 的早返与 `LandAsync` 的 `isAvatar` 分叉。**
**放大者**：`PickUpGroundDropCommandHandler.CarryOverAsync` 里那句无条件
`Value = checked((int)found.Instance)`——**这句在基线 `565d33fe` 上就有，本轮没有动过它**。
两者相遇，才有了这一条。

**发现者：完整变更评审的阻断发现 BF-1**（`reviews/change-review.md` 第二节）。
**不是执行方自查，也不是快审。** 评审自陈的凭据是它自己在
`DraggedAvatarStaysWholeTests.cs` 里临时追加的一条探针（用完删除、文件按 `sha256sum` 还原，
散列 `a359da9d50d577c82a5bc19cb9fac023d7aa15302eb7dbb541f66297825f8d01`），读数
`System.IO.InvalidDataException : Avatar detail 123456 is unavailable.`，栈根
`InventoryEntryAssembly.WithAvatarDetail`。

| 编号 | 位置（成员名） | 缺陷是什么 | TASK-09 怎么修的 | 修后的实测读数 | 判据有无 |
|---|---|---|---|---|---|
| RX-03 | `PickUpGroundDropCommandHandler.CarryOverAsync` → `IItemContainer.PutAsync` → `StoreBackedItemContainer.WriteOneAsync(slotIndex, core, **null**, …)` | 一件时装经 `AddAsync` 落地（六列齐备）之后，`CarryOverAsync` 对**同一格**再写一次，把 `Core.Value` 换成地板宣告的 `found.Instance`。`SqliteItemDomainStoreSession.Survivors` 三趟都不认（`SameContent` 因 core 字节变了不成立，`SameThing` 因它比 `core.Value` 而这一位刚被换掉也不成立）→ 该行被 `EmptyAsync` 删掉、由 `FillAsync` 重建；`FillAsync` 算 `facts` 时 `item.AvatarDetail` 是 null，落到 `CarriedAvatarsAsync` 回捞，而检索键取的是**新**的 `Core.Value` → 回捞落空 → **六列全 NULL**。下一次入场装载在 `WithAvatarDetail` 的 `detail is null` 上抛（＝ RV-08） | 在 `CarryOverAsync` 里按 `landed.WorldClass` 分岔：`is ItemWorldClass.Avatar` 时只写 `landed.Core with { Durability = found.Durability }`，不碰 `Value`；其余仍写 `Value` ＋ `Durability`。分岔用 `ContainerSlotItem.WorldClass`（问归档要的那个答案，与 `IsPile` 同一来源），不用调用方递进来的 kind 字节。同一成员的 `<remarks>` 按仓规记下了改前的读法。出处：TASK-09 第一节与第二节 | **交付态下三条新判据全绿**：`PickedUpAvatarStaysWholeTests.An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts`（入场装载仍服务它，六列齐备：`ExpireDate` 是 `int.MaxValue`、`ClearAvatarId` 0、socket 30 字节全零、两色 0、`DeleteDate` 0）、`.Two_avatars_off_one_kill_keep_two_identities_of_their_own`、`.A_piece_of_gear_taken_off_the_floor_still_carries_what_the_floor_announced` | **有覆盖** —— MU-09-1（把 `CarryOverAsync` 的那一岔改回原样，变异后散列 `061e67426e666f08ce2ea98636ebd15cbe62c4b89fc7cfdd82bdf51be50b5094`）：`失败: 2，通过: 1`，两条红分别是 `An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts`（`System.IO.InvalidDataException : Avatar detail 1075541543 is unavailable.`，栈根 `InventoryEntryAssembly.WithAvatarDetail`）与 `Two_avatars_off_one_kill_keep_two_identities_of_their_own`（`Assert.NotEqual() Failure: Values are equal`）。出处：TASK-09 第五节 MU-09-1 |

**一处「不足以确定」，明写**：**一件时装今天能不能真的出现在地板上，至今未确立。**
评审自陈未复测（它记的是 `DropRuleService` 对 `DropItemKind.Avatar` 返回 null、
档案里时装不声明 `[creation rate]` 这两条是契约自己给的）；TASK-09 也未测——
它的判据是用 `DungeonRunDomainService.AllocateMonsterDrops` 直接把时装放上地板做出来的，
**那是「所有者 GM 直接发放／掉落」那个动作的形状，不是对 `DropRuleService` 今天会不会掉时装的测定**。
本条按本轮一贯的口径记（RV-12／RV-13／RV-18／RV-19 也是这么记的）：**今天到不到得了另说，路径照样要记。**

---

### 3.7 制造者扫：谁能写出一行「kind 8 但六列为空」的时装（第三版新增）

**这一节独立于 3.1–3.6 可读**，不需要先读前面几节。它问的是与前面几节**不同的一个问题**。

- **3.1–3.6 问的是「判定点」**：一条拒绝在哪里、它本轮变严还是变松、有没有判据会红。
- **本节问的是「写入方」**：**谁能把系统送进那个拒绝**——具体说，谁能在库里造出一行
  「`item_core` 第 0 字节是 8，而 `avatar_expire_date` 等六列是 NULL」的东西。

两个问题的答案可以不一致：一条拒绝可以「有判据会红」，同时有一个制造者从没被人枚举过。
本轮就出过一次（RV-08，见 3.1 表后的「RV-08 的两个制造者」）。
**本节存在的理由**（出处：`reviews/change-review.md` 第八节「两条具体建议」第 1 条，逐字抄）：

> **反向扫加一趟「制造者扫」**：对每一条「触发即导致装载失败或结算回滚」的拒绝，
> 按成员名枚举**所有能到达它的生产写入方**，而不只是拒绝点本身。

**为什么盯住这个形状**（三步，每步指向工作区里的一句代码）：

1. 六列由 `SqliteItemDomainStoreSession.FillAsync` 算 `facts` 时定，来源两个：写入自带的
   `item.AvatarDetail`（起因 C 新增的第一来源），或按 `Core.Value` 回捞的 `CarriedAvatarsAsync`。
   两个都不命中就是六列 NULL。
2. 一行 kind 8 而六列 NULL，下一次入场装载在 `InventoryEntryAssembly.WithAvatarDetail` 的
   `detail is null` 上抛 `InvalidDataException("Avatar detail {value} is unavailable.")`（＝ RV-08）。
3. 那个异常落进 `SelectCharacterHandler.HandleAsync` 的 `catch (Exception error)`，该分支把
   `session.ConnectionId` 加进 `pendingCloses` 并把失败记进日志——**所有者看到的是「选完角色进不去」**。
   **证据类型：代码级读出，本任务亲读该 `catch` 分支。**

**枚举口径（三句，读者可逐句推翻）**

- **口径一**：「写入方」＝ 一个能把一行物品核心交到世界表 `items` 上的**生产成员**。
  在 `Server/src` 下按写入函数名检索，今天读到**三族**，下表列前两族的 13 行，
  **第三族被明写排除，理由与可复核依据写在下面「明写的排除」一段**：
  - **容器一族**（下表 #1–#11）经 `StoreBackedItemContainer.WriteOneAsync`；
  - **入场存盘一族**（下表 #12–#13）经 `ICharacterItemTable` 的
    `InsertOwnAsync`／`UpdateOwnAsync`（`grep -rn "InsertOwnAsync\|UpdateOwnAsync" Server/src --include=*.cs`
    去掉表实现与接口声明后，生产调用方落在 `InventoryEntryAssembly` 与 `CharacterRosterService` 两个文件里）；
  - **世界表一族**（`IWorldItemTable` 的 `SqliteWorldItemTable.InsertAsync`／`UpdateAsync`）
    **不在下表内**——它今天在 `Server/src` 里**零调用方、零 DI 注册**，见「明写的排除」。

  **这条口径覆盖到什么、排除了什么**（一句可复核的话）：它覆盖 `Server/src` 下能写 `items`
  且**今天在 `Server/src` 里有生产调用方**的成员；`Server/tests/` 下的测试替身不算，
  `Server/src` 下**只被测试构造、生产无人调用**的成员也不算，后者今天只有世界表一族一处，
  已在下面逐条点名并给出核法。
- **口径二**：容器一族的汇流口只有 `WriteOneAsync` 一个。
  `grep -rn "WriteOneAsync" Server/src --include=*.cs` 读出 **3 行**：声明本身
  （`ItemContainers.cs:386`）与两个调用点——`PutAsync`（:377，**恒传 `null` 事实**）与
  `LandAsync` 的 `IntoEmptySlot` 分支（:488，可传 `ArrivingAvatar.Facts()`）。
  于是容器一族的写入方 ＝ `PutAsync` 的生产调用方，加 `LandAsync` 的 `IntoEmptySlot` 那一支。
  **本任务亲自跑了这一次 grep（只读）。**
- **口径三**：`PutAsync` 的生产调用方取 TASK-09 第三节的清单：**15 个调用点、落在 10 个成员上**。
  计数口径（出处：TASK-09 第三节与它第二轮的「订正一」，**本任务未重跑该 grep**）：
  `grep -rn "PutAsync" Server/src --include=*.cs` 读出 **21 行**，扣掉 **6 行**非调用点
  ——接口声明 1 处（`IItemContainer.cs:155`）、`<see cref>`／`<c>` 引用 4 处、实现本身 1 处
  （`ItemContainers.cs:375`）——余 **15 行，每行一个调用点**；15 个调用点按**宿主成员**去重得
  **10 个成员**（`RelocateAsync`、`SplitAsync`、`SwapAsync`、`MergeAsync`、`ConsumeAsync`、
  `LandAsync`、`DeliverAsync`、`CarryOverAsync`、`PutOnAsync`、`TakeOffAsync`）。
  `Server/tests/` 下的测试替身不算生产写入方。

**明写的排除：世界表一族 `SqliteWorldItemTable`，今天生产零调用方（第三版第二轮补记）**

`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` 的
`SqliteWorldItemTable.InsertAsync(InventoryItemCoreState core, AvatarDetailRow? avatar, …)`
与同类的 `UpdateAsync(int itemUid, InventoryItemCoreState core, AvatarDetailRow? avatar, …)`
**也往 `items` 写 `item_core` 与那六列**，且两者都把六列交给同文件的 `BindAvatarColumns`，
该方法在 `avatar is null` 时把六列逐列绑成 `DBNull.Value`——
**这正是本节盯的那个形状（kind 8 的 core ＋ 六列 NULL）**，
所以按口径一的字面读法，只要它有生产调用方，它就该进下表。

**它今天没有生产调用方，这一条可以当场复核**（本轮亲跑三条只读检索，读数照抄）：

```text
grep -rn "IWorldItemTable" Server/src --include=*.cs
  Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs:189:public interface IWorldItemTable
  Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:417:internal sealed class SqliteWorldItemTable(SqliteDatabase database) : IWorldItemTable
  → 2 行，一行接口声明、一行实现的类头，没有第三行

grep -rn "SqliteWorldItemTable" Server/ --include=*.cs
  Server/src/.../InventoryItemTables.cs:417                                （实现的类头）
  Server/tests/Dfo.Server.Persistence.Tests/Inventory/WorldAndSlotTableTests.cs:455 / :463
  Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/CreatureIdentityFitsTheWireTests.cs:55 / :107
  → `Server/src` 下 1 行且是它自己；把它 new 出来的只有两个测试文件

grep -rn "ItemTable\|ItemDomainStore\|ItemSlotTable" Server/src/Dfo.Server.Persistence/PersistenceModule.cs
  :74 ICharacterItemTable  :76 IAccountCargoItemTable  :158/:162 SqliteItemDomainStore／IItemDomainStore
  → 没有 IWorldItemTable，也没有 SqliteWorldItemTable
```

**结论（只就这三条读数而言）**：今天在 `Server/src` 里没有一处按名字取用这两个成员，
也没有一处把这个类注册进容器；因此**今天没有一条生产路径能走到它们**，
它们进不了「谁能造出那个形状」这个问题的答案里。**所以把它排除，并把排除写在这里。**
**这条排除的有效期只到有人第一次给它接线为止**——依据是上面那三条 grep，
谁重跑一次即可核或推翻；一旦读数变了，这两个成员就要作为第 14／15 行进下表。

**这不是本轮新发现的东西，也不是本轮该修的东西——它有渊源**（出处：本仓 `AGENTS.md`
「已知缺陷与覆盖缺口」一节，由**上一个成果 `items-own-their-identity` 写下**，逐字）：

> **今天仍不被覆盖的写入方只剩一个**：`SqliteWorldItemTable`（`InsertAsync`／`UpdateAsync`），
> 它写 `items` 时装列却没有自己的条件，退役前靠 `idx_items_avatar_uid` 抛错。
> 它**生产里零调用方、零 DI 注册**

（末句在原文里还接着一个括号，列出 `PersistenceModule` 实际注册的三项；
此处**截到「零 DI 注册」为止，未改一字**，那个括号的内容与上面第三条 grep 的读数相同。
出处不是本轮写的：`git diff 565d33fe -- AGENTS.md` 里没有一行动到 `SqliteWorldItemTable`。）

同一份记载把它的处置写作「**这个缺口今天到不了生产，是有意留下的**——去补它等于在一个
生产无人调用的类上引入新行为」；该成果的 `spec-result.md`「Successor Guidance」第 4 条
同样记着「**`SqliteWorldItemTable` 有一个有意留下的守卫缺口**（生产零调用方、零 DI 注册）。
谁第一次给它接线，谁要补那道时装唯一性守卫。」
**上面三条 grep 说明它今天仍是那个状态**，本轮既未改动它，也未把它列为新缺陷。

**这条排除对本节结论的影响：没有。** 3.7.3 的三条小结（同型 5 行、今天能被时装走到 5 行、
两者交集 0 行）与「没有查出新的、TASK-09 未覆盖的同型缺陷」这一句，
都是**就下表那 13 行说的**；被排除的这一族今天走不到，加不进任何一个集合。

**下表的行数与它的复算**：容器一族 10 个成员，其中 `LandAsync` 两支各占一行
（`OntoExistingPile` 是 `PutAsync` 调用点 #9，`IntoEmptySlot` 是 `WriteOneAsync` 的另一个调用点），
故 **11 行**；入场存盘一族 2 个成员各一行，故 **2 行**。**11 ＋ 2 ＝ 13 行。**

**「同型」的判据**（下表第四栏都按它判，一句话）：一次**不带事实**的写入会不会毁掉一件站着的时装，
取决于它**改不改那件时装的 `Core.Value`**——
不改，`SqliteItemDomainStoreSession.Survivors` 的第二趟 `SameContent` 或第三趟
`InPlace && SameThing` 认它，行不被重建，六列保住；
改了，三趟全不认，行被删掉重建，而 `CarriedAvatarsAsync` 按**新**的 `Core.Value` 回捞，落空 → 六列 NULL。
出处：TASK-09 第三节（执行方读出）＋ `reviews/change-review.md` BF-1 机理第 3–4 步（评审自读源码）。

#### 3.7.1 容器一族（11 行）

| # | 写入方（成员名） | 它写什么 | 同型吗（改不改站着那行的 `Core.Value`） | 能不能造出「kind 8 六列为空」／挡它的是什么 | 谁核实的、实测还是代码级读出 | 判据有无 |
|---|---|---|---|---|---|---|
| 1 | `StoreBackedItemContainer.LandAsync` 的 `IntoEmptySlot` 分支 | `WriteOneAsync(slot, isAvatar ? landing with { Value = NewIdentity() } : landing, isAvatar ? ArrivingAvatar.Facts() : null, …)` | 不适用：它开的是空格，不改任何站着的行 | **不能——它是六列的生产来源本身**。时装今天**能**走到它（这正是起因 B 的落点） | 本任务亲读 `LandAsync` 的这一支 | **有覆盖** —— MU-5 把 `isAvatar ? ArrivingAvatar.Facts() : null` 改掉之后，`GrantedAvatarIsWholeTests` 两条以 `Avatar detail ... is unavailable.` 变红 |
| 2 | `StoreBackedItemContainer.LandAsync` 的 `OntoExistingPile` 分支（`PutAsync` #9） | `pile.Core with { Value = topped.Total }` | **同型** | 今天时装走不到，两条进路各有闸门：**(a) 经 `AddAsync`**——`ItemArrivalRuleService.ForGrant` 的 pile 循环前置是 `stackLimit > 1`，时装的 `StackLimit` 是 1，直落 `IntoEmptySlot`。**(b) 经 `RefillAsync`**——`ForDailyRefill` 不看上限，挡它的是「归档那六条 refill 规则全是 `Interchangeable`」这条读数 | (a) **QR-09-A 自读源码核实**（TASK-09「订正三」表第 4 行）。(b) **只有执行者第一轮的一次探针实测**：`GetDailyRefillRulesAsync` 在 `preparedAt = 2020-01-01 +08:00` 下答 6 条，itemId `2680738`／`2749110`／`10002520`／`10093393`／`10003235`／`690001556`，六条 `ClassifyAsync` 都答 `Interchangeable`。**这一支未经第二方复核**，见本节末「不足以确定」第 2 条 | 未测定 |
| 3 | `ItemContainerOperations.RelocateAsync`（#1） | `moving.Core` 原样 | 不同型 | 今天**能**被时装走到（整件搬走那一支） | TASK-09 第三节第 1 行；本任务复读该行 | **有覆盖** —— MU-20 下 `DraggedAvatarStaysWholeTests.A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from` 红（`Assert.Null() Failure: Value is not null`） |
| 4 | `ItemContainerOperations.SplitAsync`（#2 源格、#3 目标格） | 源格 `Value = moving.Count - moveCount`；目标格 `StackableItemCore.Create(itemKind, itemId, moveCount)` | **同型**——**它就是 3.6 的 RX-01** | 今天时装走不到：`DecideMoveAsync` 只在 `moveCount != moving.Count` 时选 Split，而 `moveCount` 被夹到 `moving.Count`，非堆的 `Count` 恒为 1（TASK-08 的 `Count => this.IsPile ? Core.Value : 1`）→ clamp 后两者相等，选不上 Split | **QR-09-A 自读源码核实**（TASK-09「订正三」表第 1 行）；缺陷本身由 **QR-06-1** 发现 | **有覆盖** —— MU-20 三红，其中 `An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts` 正是 `Avatar detail 1408050310 is unavailable.` |
| 5 | `ItemContainerOperations.SwapAsync`（#4／#5） | `first.Core`／`second.Core` 原样 | 不同型 | 今天**能**被时装走到（整件对调那一支） | TASK-09 第三节第 4／5 行；本任务复读 | **有覆盖** —— MU-20 下 `A_drag_of_one_onto_another_avatar_trades_the_two_slots` 红 |
| 6 | `ItemContainerOperations.MergeAsync`（#6 目标格、#7 源格） | `Value = target.Count + moveCount`／`Value = moving.Count - moveCount` | **同型** | 今天时装走不到：`DecideMoveAsync` 在 `displaced.ItemId == moving.ItemId` 时，若 `placement.StackLimit > 1` 不成立即以 `MergeExceedsStackLimit` 拒掉；时装的 `StackLimit` 是 1 | **QR-09-A 自读源码核实**（TASK-09「订正三」表第 2 行） | 未测定（本轮未对这道闸门施加变异） |
| 7 | `ItemContainerOperations.ConsumeAsync`（#8） | `Value = remaining` | **同型** | 今天时装走不到，两条独立的理由：**(a)** 非堆的 `Count` 是 1、`ConsumedPerUse` 恒 1，`remaining == 0` 走的是 `ClearAsync` 而不是这个 `PutAsync`（`count <= 0` 则抛）；**(b)** 两个时装模板的 `IItemUseRuleDefinitionSource.GetAsync` 都答 null，连消耗效果都计划不出来 | (a) **QR-09-A 自读源码核实**（「订正三」表第 3 行）。(b) **执行者探针实测**（两个模板，见本节末「不足以确定」第 3 条） | 未测定（＝ 3.2 的 RV-22 与起因 E 的 E-05，两处也都记作未测定） |
| 8 | `SelectCardCommand.DeliverAsync`（#10） | `core with { Durability = piece.Durability }` | 不同型 | **今天能不能被时装走到：不足以确定**（一张卡的面能不能是一件时装，TASK-09 明写未测，本任务也未测）。**但这不影响本节的问题**：它只改耐久，`Survivors` 第三趟 `InPlace && SameThing` 认它，六列由 `LandAsync` 的 UPDATE 保住，所以走不走得到都造不出那个形状 | **完整变更评审 NB-8 自读源码核实**，原话逐字：「`SelectCardCommand.DeliverAsync` 也在 `AddAsync` 之后对同格再 `PutAsync` 一次（补 `Durability`），但它**不改 `Core.Value`**，于是 `Survivors` 第三趟 `InPlace && SameThing` 认它，六列由 `LandAsync` 的 UPDATE 保住。」（源文本在此处换行三次，作表内引用时接排，未改一字） | 未测定（一张卡的面能不能是时装，TASK-09 明写不足以确定；因为它不动 `Value`，这个问题不影响同型判断） |
| 9 | `PickUpGroundDropCommandHandler.CarryOverAsync`（#11） | **改前**：`landed.Core with { Value = checked((int)found.Instance), Durability = found.Durability }`。**TASK-09 修后**：时装只写 `Durability` | **改前同型；改后不同型** | **改前能，而且今天可达——这就是第 37 条（3.6 的 RX-03）**。修后对时装不碰 `Value` | 由**完整变更评审 BF-1** 查出（评审自造探针实测：`Avatar detail 123456 is unavailable.`）；TASK-09 修掉并自做变异 | **有覆盖** —— MU-09-1：2 红（读数逐条抄在 3.6 的 RX-03 行） |
| 10 | `MoveItemSpaceCommandHandler.PutOnAsync`（#12 穿上、#13 被顶下来的） | `piece.Core`／`displaced.Core` 原样 | 不同型 | 今天时装到不了：`WornSlotForAsync` 对实测的两个时装模板都答 null，`PutOnAsync` 在写之前就以 `Refused(NotWearable, …)` 拒掉。**即使可达也不同型**（不改值） | 执行者探针实测（两个模板）＋ 代码级读出（TASK-09 第三节第 12／13 行） | 未测定 |
| 11 | `MoveItemSpaceCommandHandler.TakeOffAsync`（#14 脱下、#15 被顶下来的） | `piece.Core`／`displaced.Core` 原样 | 不同型 | 今天时装到不了：要脱下先得穿上，而第 10 行那两条走不通 | 代码级读出（TASK-09 第三节第 14／15 行） | 未测定 |

#### 3.7.2 入场存盘一族（2 行）

**这一族不经 `IItemContainer`**，所以它不在 TASK-09 那张 `PutAsync` 表里；
本节把它补进来，是因为它同样能往 `items` 写一行带 kind 字节的核心。

| # | 写入方（成员名） | 它写什么 | 同型吗 | 能不能造出「kind 8 六列为空」／挡它的是什么 | 谁核实的、实测还是代码级读出 | 判据有无 |
|---|---|---|---|---|---|---|
| 12 | `InventoryEntryAssembly.SaveAsync` → `ICharacterItemTable.UpdateOwnAsync`／`InsertOwnAsync` | `item.Core` ＋ `avatarDetail = item.Core.ItemKind == 8 ? item.AvatarDetail : null` | **它带事实，所以不靠「改不改 `Value`」判**；它造出该形状的唯一途径是「租约里一行 kind 8 而 `AvatarDetail` 是 null」 | **今天可达**（一件发放来的时装在租约里，存盘就走这条），但被同方法**更早**的脏租约扫描挡住：`avatars.Any(value => value.Core.Value <= 0 \|\| value.AvatarDetail is null \|\| value.AvatarDetail.JewelSocket.Length != 30)` → 抛 `A dirty avatar lease requires its exact 30-byte detail row.`（＝ 3.1 的 RV-06） | **代码级读出，本任务亲读 `SaveAsync` 的这两段** | **无覆盖** —— 挡它的那道扫描本身没有判据钉住：MU-13 那次全量运行报 **0 失败**（见 RV-06）。**这是本节 13 行里唯一一个「闸门本身无覆盖」的写入方** |
| 13 | `CharacterRosterService` 建角色时的 `InsertOwnAsync(EquipmentCore(piece), null, …)` | `EquipmentCore(piece)`，事实位恒传 `null` | 不适用 | **今天造不出 kind 8**：那一行的种类字节取自 `NewCharacterEquipment.ItemKind`，而它在 `Server/src` 里唯一的构造点 `CharacterCreationRuleService` 写的是该类自己的 `private const byte EquipmentKind = 1;`——**字面量，不问归档**，所以起因 A 改档案答案影响不到这条路 | **代码级读出，本任务亲读这两处**（`CharacterRosterService.EquipmentCore` 与 `CharacterCreationRuleService` 的那个 `const`） | 未测定（本轮未对该处施加变异） |

#### 3.7.3 小结（可从上面两张表逐行复算）

**复算口径**：三条小结都按上面两张表**逐行**读，第四栏（同型）逐行记「同型／不同型／不适用」，
第五栏（今天能不能被时装走到）逐行记「能／走不到／不足以确定／造不出」。
「不同型」里虽然也有「同型」二字，**按整词读，不按字串数**。

- **第四栏写「同型」的有 5 行**：#2、#4、#6、#7、#9（**#9 是「改前同型；改后不同型」**）。
  其余 8 行：#3、#5、#8、#10、#11 写「不同型」，#1、#12、#13 写「不适用」。5 ＋ 8 ＝ 13 ✓
- **第五栏写「能／今天可达」的有 5 行**：#1、#3、#5、#9（TASK-09 修后）、#12。
  写「今天走不到」的 6 行：#2、#4、#6、#7、#10、#11。
  写「不足以确定」的 1 行：#8。写「今天造不出 kind 8」的 1 行：#13。5 ＋ 6 ＋ 1 ＋ 1 ＝ 13 ✓
- **既同型、今天又能被时装走到的：在这 13 行之内是 0 行**（改前是 1 行，即 #9）。
  从上面两条当场核：同型那 5 行里，#9 在 TASK-09 修后已不同型，剩下的 #2、#4、#6、#7
  第五栏全写「今天走不到」；能走到那 5 行里，#1、#12 第四栏写「不适用」，#3、#5 写「不同型」，
  #9 修后写「不同型」。**两个集合在交付态上没有共同的行。**
- **判据有无逐行统计**：写「有覆盖」的 **5** 行（#1、#3、#4、#5、#9）、
  写「无覆盖」的 **1** 行（#12）、写「未测定」的 **7** 行（#2、#6、#7、#8、#10、#11、#13）。
  5 ＋ 1 ＋ 7 ＝ **13** ✓，与表的行数相同。
  **#12 是这 13 行里唯一一个第七栏记「无覆盖」的写入方**（挡它的 RV-06 记作无覆盖）。
  **这句话只在第〇节写死的那个词汇下成立，限定域要写清**：按第〇节，「无覆盖」＝
  **施过一次具名变异，而那次运行报 0 失败、或变红的判据里没有一条走这条路径**；
  「未测定」＝ **本轮没有对该处施加变异**。
  所以这一句说的是「量过、而没量出覆盖的只有 #12 一处」，
  **不是**「另外 12 行的闸门都有判据钉住」——
  另外那 7 行记「未测定」，它们的闸门**有没有判据钉住尚未测定**，
  今天不足以确定它们是有覆盖还是无覆盖。

**没有查出新的、TASK-09 未覆盖的同型缺陷。** 按上面的口径逐行判过之后，
除已修掉的 #4（RX-01，TASK-08 修）与 #9（RX-03，TASK-09 修）之外，
**在这份 13 行的清单之内没有找到第三个既同型又今天能被时装走到的写入方。**
这与 QR-09-A 的独立结论方向一致（TASK-09「订正三」原话：
「在第三节枚举的那 15 个调用点这一封闭清单之内，除 #11 之外**没有第二个同型的**」）；
**本节把清单由 15 个 `PutAsync` 调用点扩到 13 个写入方**——扩出来的三行是
#1（`WriteOneAsync` 的另一个调用点）、#12 与 #13（入场存盘那一族），**三行都不同型**。

**这个枚举口径挡不住什么（明写，逐条）**

1. 口径二与口径三是对 `Server/src` 下 `*.cs` 的**文本**枚举。不通过字面 `PutAsync`／`WriteOneAsync`
   抵达那两个方法的调用方（例如反射），不在这份清单内；**本任务对是否存在这类调用方不足以确定。**
2. **#2 的 `RefillAsync` 那一支只有执行者第一轮的一次探针实测挡着，未经第二方复核。**
   QR-09-A 的原话（出处：TASK-09 文件的 `## Independent Quick Review — QR-09-A` 第二部分第 3 条，逐字）：

   > 我没有重跑那个探针（会新增文件，超出本次评审的只读边界），**本评审对那六条读数不足以确定**

   该读数带了 itemId 清单与 `preparedAt`，是可证伪的写法：谁重跑一次同口径的探针即可核或推翻。
3. #2(a)、#7(b)、#10 的「今天到不了」，依据的是执行者对**两个**时装模板
   （`101550507`、`101550511`）的探针实测（`StackLimit`／`WornSlotForAsync`／`IItemUseRuleDefinitionSource`）。
   **其余时装模板的这三个读数不足以确定**（出处：TASK-09 第三节「不足以确定的部分」）。
4. **一张奖励卡的面能不能是一件时装，至今不足以确定**（TASK-09 未测，本任务也未测）。
5. **一件时装今天能不能真的出现在地板上，至今未确立**（见 3.6 的 RX-03 末段）。
6. 本节只回答「谁能造出『kind 8 但六列为空』」这一个形状。**别的坏形状（例如身份撞号、
   六列存在但内容不对）不在本节的问题里**，它们分别记在 RV-05／RV-20（撞号）与 RV-09／RV-21（形状）。
7. **世界表一族被明写排除，而排除的理由只是「今天零调用方」，不是「它写不出那个形状」。**
   它写得出（`BindAvatarColumns` 在 `avatar is null` 时把六列绑成 NULL）。
   谁第一次给 `SqliteWorldItemTable` 接上生产调用方，这份 13 行的清单当天就要变成 15 行，
   而那两行进来时**没有闸门也没有判据**。核法与渊源见上面「明写的排除」。

---

### 3.8 「未测定／无覆盖」按后果分档：先看哪个（第三版新增）

**本节的由来**（出处：`reviews/change-review.md` 第八节「两条具体建议」第 2 条，逐字抄）：

> **「未测定／无覆盖」要按后果分档**：现在 9 条「未测定」＋ 11 条「无覆盖」平铺在一起，
> 读者无法一眼看出哪一条触发了会让角色进不去。
> 建议至少分成「触发即角色进不去 / 触发即一笔结算回滚 / 触发只影响一次请求」三档。
> **所有者马上要做实机验证，他需要的是「先看哪个」，而不是「哪些没量」。**

**分档标准（写死）**。档位只问一件事：**这一条真被触发时，所有者在实机上看到什么。**
判定依据是「这条路径失败之后由谁接住」，逐档写明：

| 档 | 判定依据 | 所有者看到什么 |
|---|---|---|
| **甲** | 失败发生在选角进场那一趟里（`InventoryEntryAssembly.ReadCharacterItemsAsync`／`SaveAsync`，或 `GameInventoryDelivery.WriteItem` 的编码）。抛出的异常落进 `SelectCharacterHandler.HandleAsync` 的 `catch (Exception error)`，该分支把 `session.ConnectionId` 加进 `pendingCloses` 并把失败记进日志 | **选完角色进不去，连接被关掉** |
| **乙** | 失败发生在一笔领域结算里（`SqliteItemDomainStoreSession.FillAsync` 的守卫、`items` 表的 CHECK、入场存盘的撞号守卫、`checked` 溢出）。`DomainTransaction` 的 `catch` 把整笔回滚 | **那一次操作没生效，角色还在，可以接着玩** |
| **丙** | 不抛。只是一次请求得到另一个答案，或一行日志、一段帧字节读起来与此前不同 | **一次拖动被拒／一次拖动的结果不同／日志少一个数／客户端那一格显示的数不同**，库里的数据不变 |

**证据类型**：甲档的接住点由本任务亲读 `SelectCharacterHandler.HandleAsync`
（`GameInventoryDelivery.Notifications` 在 `ExecuteAcceptedWorkflowAsync` 里被调，
而 `ExecuteAcceptedWorkflowAsync` 就在那个 `try` 里）——**代码级读出**。
乙档与丙档的接住点按各条自己的凭据栏，未逐条重读。

**排序规则**：先按档（甲 → 乙 → 丙），**同档之内把「今天可达」的排在「今天到不了」的前面**
——所有者实机能撞上的只有前者。

#### 甲档：触发即角色进不去（8 条）

| 顺位 | 编号 | 位置（成员名） | 今天可达吗 | 读数 | 凭据 |
|---|---|---|---|---|---|
| 1 | RV-07 | `InventoryEntryAssembly.WithAvatarDetail` 的 `item.Core.Value <= 0` | **可达** | 无覆盖 | MU-11 那次全量报 **0 失败** |
| 2 | RV-09 | `WithAvatarDetail` 的珠宝槽长度／两色范围 | **可达** | 无覆盖 | MU-12 那次全量报 **0 失败** |
| 3 | RV-06 | `InventoryEntryAssembly.SaveAsync` 的脏租约时装扫描 | **可达** | 无覆盖 | MU-13 那次全量报 **0 失败**。**它同时是 3.7 节 #12 那个写入方唯一的闸门** |
| 4 | RV-11 | `GameInventoryDelivery.WriteItem` 尾部 `avatar!.JewelSocket.Length != 30` | **可达** | 无覆盖 | MU-19 那次全量报 **0 失败** |
| 5 | RV-18 | `WriteItem` 的 `core.ItemKind != 8 && avatar is not null` | 今天到不了 | 未测定 | 代码级读出 |
| 6 | RV-12 | `StateSynchronizationPayloadCodec.Entry.WriteNoti2EquippedItem` 缺事实 | 今天到不了 | 未测定 | **档位说明**：本任务未逐环追这条编码由哪个 handler 调用，这个档是按它与 RV-10／RV-11 同型（帧编码期抛出）判的，**不足以确定它是否与 RV-10 落进同一个 `catch`** |
| 7 | RV-13 | 同成员 `IsCostumeBarSlot` 分支的 30 字节检查 | 今天到不了 | 未测定 | 同 RV-12 |
| 8 | RV-19 | 同成员的 `core.ItemKind != avatarKind && item.AvatarDetail is not null` | 今天到不了 | 未测定 | 同 RV-12 |

**这一档的读法**：前四条**全部今天可达、全部无覆盖，而且四条的凭据都是同一种——
「那次全量运行报 0 失败」**。也就是说，今天没有任何一条判据钉住它们。
**所有者实机第一个要看的就是这四条指向的同一件事：一件时装的六列在装载与存盘那一趟上是不是齐的。**

#### 乙档：触发即一笔结算回滚（5 条）

| 顺位 | 编号 | 位置（成员名） | 今天可达吗 | 读数 | 凭据 |
|---|---|---|---|---|---|
| 1 | NB-1（第五节那条） | `ItemContainers.ArrivingAvatar.NewIdentity()` 用抽取而非自增，理由是「跨重启保持互异」 | **可达** | 无覆盖 | MU-18 那次全量报 **0 失败**。真撞上时是两件时装拿到同一个身份 → `FillAsync` 的撞号守卫（RV-05）→ 整笔结算回滚 |
| 2 | RV-21 | `items` 表上 `avatar_jewel_socket` 的 CHECK | 今天到不了（唯一的生产者 `ArrivingAvatar.Facts()` 交的是 30 个零字节） | 无覆盖 | MU-23 那次报 1 红，但红的 `ItemCarrierShapeTests.ItemsCarriesEveryConstraintItsSourcesDeclared` **比的是声明文本，没有做一次违反该 CHECK 的写入** |
| 3 | RV-22（＝ 起因 E 的 E-05，同一处，合记一条） | `ItemContainerOperations.ConsumeAsync` 的 `remaining < 0` | 上游可达性未追 | 未测定 | 本轮未对 `ConsumeAsync` 施加变异 |
| 4 | E-10 | `ItemArrivalRuleService.ForDailyRefill` 的 `checked(pile.Count + grant)` | 未确立（要某条每日补给规则点名一个 worn piece 模板） | 未测定 | 本轮未实测这一路 |
| 5 | RV-14 | `SqliteItemDomainStoreSession` 幸存者匹配的 `core.ItemKind == placing.Item.Core.ItemKind` | 今天到不了**新**库；只对本轮之前落过 kind 1 时装行的库有意义 | 未测定 | 它要一个跨版本的旧库夹具，本轮不造判据 |

#### 丙档：只影响一次请求（11 条）

| 顺位 | 编号 | 位置（成员名） | 今天可达吗 | 读数 | 所有者会看到什么 |
|---|---|---|---|---|---|
| 1 | RP-06 的另一半 | 「发放来的时装走到 `GameInventoryDelivery.WriteItem` 的 kind 8 编码分支」这一半 | **可达** | 无覆盖（编码分支那一半是有覆盖的） | 时装那一格的第三个字段写的是剩余秒数，**约 3.59×10⁸ 秒、约 11.4 年**（不是此前记错的「约 2.1×10⁹／约 68 年」，订正见第五节 NB-1b）。**这是所有者实机看时装的第一眼** |
| 2 | A-27 | `WriteItem` 尾部「30 + 三十字节 + 4 + 两个颜色」那一块 | **可达** | 未测定 | 与第 1 条同一帧、同一件时装；珠宝槽与两色画出来对不对 |
| 3 | RV-23 | `StoreBackedItemContainer.DecideMoveAsync` 的 `PartialSwapNotAllowed`，**就普通装备与契约兽而言** | **可达** | 无覆盖 | 把一件普通装备拖到另一件头上：此前对调被拒，现在整件对调 |
| 4 | RP-08 | 同成员目标格为空时的 `Relocate`／`Split` 选择，**就普通装备与契约兽而言** | **可达** | 无覆盖 | 把一件普通装备拖到空格：此前被拆开，现在整件移动 |
| 5 | RV-02 | `DecideMoveAsync` 的 `!CanOccupyMainSlot(placement, targetSlot)` | **可达** | 无覆盖 | 把一件时装拖到快捷栏（槽 3..8）：被拒 |
| 6 | RV-03 | `DecideMoveAsync` 的 `!CanOccupyMainSlot(displacedPlacement, moving.SlotIndex)` | **可达** | 无覆盖 | 把快捷栏里的东西拖到一件时装头上：交换被拒 |
| 7 | RG-01 | `MoveItemSpaceCommandHandler.HandleAsync` 的 `ListNotServed` 单参 `Refused` | 取决于真实客户端写哪个列号，**未观测** | 未测定 | 拒绝记录读作 `item 0 …->N refused as ListNotServed`：**槽号仍在，模板号丢失** |
| 8 | E-11 | `DailyRefillService` 的 `held.Sum(item => Math.Max(0, item.Count))` 与 `ItemUseEffectDomainService.ApplyAsync` 的 `var remaining = item.Count;`（两句） | 未确立 | 未测定 | 每日补给算的数、使用效果回报的数与此前不同 |
| 9 | RV-04 | `ItemMovementDomainService.MayOccupyMainSlotAsync` → `TakeOffAsync` 的 `Refused(ItemCannotOccupySlot)` | 今天到不了（时装进不了装备容器） | 无覆盖 | —— |
| 10 | RG-02 | `TakeOffAsync` 的五个单参 `Refused` | 时装今天到不了；非时装可达 | 未测定 | 走那条路径时记录里的 `{ItemId}` 是 0 |
| 11 | RP-09 | `InventoryItemState.Holds` 的 Template 分支 | **今天到不了**：`Server/src` 下按 `\.Holds\b` 搜到的生产读点为 0 处 | 未测定 | —— |

#### 3.8.1 分档结果的复算

- 甲 **8** ＋ 乙 **5** ＋ 丙 **11** ＝ **24 条**。
- 这 24 条的来历，逐项对得上 3.5 的三个数：
  **无覆盖 11 条**（RV-02、RV-03、RV-04、RV-06、RV-07、RV-09、RV-11、RV-21、RV-23、RP-08、RP-06 的另一半）
  → 甲 4（RV-06、RV-07、RV-09、RV-11）＋ 乙 1（RV-21）＋ 丙 6（RV-02、RV-03、RV-04、RV-23、RP-08、RP-06 的另一半）＝ 11 ✓
  **未测定 9 条**（RV-12、RV-13、RV-14、RV-18、RV-19、RV-22、RP-09、RG-01、RG-02）
  → 甲 4（RV-12、RV-13、RV-18、RV-19）＋ 乙 2（RV-14、RV-22）＋ 丙 3（RG-01、RG-02、RP-09）＝ 9 ✓
  **3.5 点名的另外三条正向扫未测定**（A-27、E-10、E-11；E-05 与 RV-22 是同一处，合记一条不重计）
  → 甲 0 ＋ 乙 1（E-10）＋ 丙 2（A-27、E-11）＝ 3 ✓
  **第五节的 NB-1**（3.5 称它为「第十二条」无覆盖，不在 3.1–3.4 的表里）→ 乙 1 ✓
  11 ＋ 9 ＋ 3 ＋ 1 ＝ **24** ✓
- **本节不改 3.5 的任何一个数**，只是把同一批条目按后果重新排了一次序。

#### 3.8.2 一句话给所有者：先看哪个

**先看甲档前四条指向的同一件事**——一件时装的六列在「装载 → 存盘 → 再装载」那一趟上是不是齐的
（RV-07、RV-09、RV-06、RV-11）。它们**今天都可达，而且今天没有一条判据钉住它们**。
**接着看丙档前两条**（RP-06 的另一半、A-27）：那是时装在客户端上画出来的样子，
剩余秒数应当是**约 11.4 年**（`(2147483647 − 1788687136) ÷ 31556952 = 11.37`，取三位有效数字），
不是**约 68 年**（那是 `int.MaxValue` 本身：`2147483647 ÷ 31556952 = 68.05`）——
看到十一年多是这个占位在正常工作，不是缺陷。
**再看丙档第 3、4 条**（RV-23、RP-08）：普通装备的拖动行为本轮变了，而且是变好，
**这是本轮记在案的、有意的行为变化，不是未记的缺陷**。

---

## 四、CK-016 —— 本轮施加的每一次变异

**这张表是第二轮重做的读数，不是第一版的抄录。**
第一版的 19 次变异（MU-1..MU-19）在第二轮**逐次重跑**，因为 TASK-08 新增了
`DraggedAvatarStaysWholeTests` 四条判据，凡是这四条会红的变异，其红集与失败数都变了；
另新做四次（MU-20..MU-23）。**合计 23 次。**

**方法**：文件级备份（`cp` 到会话 scratchpad）＋ 就地 `perl -0777 -pi` 改 ＋ `sha256sum` 记散列 ＋
`cp` 备份回来并逐文件核散列。
**全程未使用任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash` 一次未出现）。
23 次变异跑的都是同一条命令，**一次 `--filter` 也没用**：
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

**变异前的基线**（第二轮开工实测）：
Networking 52 / Protocol 524 / Pvf 60 / Host 6 / Persistence 320 / Infrastructure 236（另 2 跳过）/ Application 1036
= **2234 通过 / 2 跳过 / 0 失败，EXIT=0**。

**与第一版读数的差别，逐条**（第一版的基线是 2230）：

| ID | 第一版失败数 | 第二版失败数 | 差在哪 |
|---|---|---|---|
| MU-1 | 7 | 10 | 多的 3 条是 `DraggedAvatarStaysWholeTests` 三条时装判据 |
| MU-4 | 2 | 3 | 多的 1 条是 `DraggedAvatarStaysWholeTests.A_drag_of_one_onto_another_avatar_trades_the_two_slots` |
| MU-5 | 2 | 4 | 多的 2 条是 `DraggedAvatarStaysWholeTests` 的 `A_drag_of_one_onto_another_avatar_trades_the_two_slots` 与 `An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts` |
| MU-7 | 7（含 1 条既知偶发） | 8 | 多的 2 条是 `DraggedAvatarStaysWholeTests` 两条；少的 1 条是第一版里那条既知偶发 `CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold`，**第二轮这一次没有出现** |
| MU-2、MU-3、MU-6、MU-8..MU-19 | 见下表 | 与第一版相同 | 红集逐条相同，只有「通过」那一栏因基线由 2230 变 2234 而各加 4 |

**MU-18 的散列与第一版不同**（第一版 `57afb061…`，第二版 `49e29258…`）：
两次施加的是同一个改法（抽取换成进程内自增），但第二轮把
`private static int nextIdentity;` 这行放在了 `NewIdentity()` 之前而不是之后，
且用了全限定的 `System.Threading.Interlocked`。观测结果两次相同（失败 0）。
其余 18 个重做的散列与第一版**逐字符相同**，见下表。

| ID | 位置（成员名） | 改法 | 变异后散列 | 该次全量结果 | 变红的判据 |
|---|---|---|---|---|---|
| MU-1 | `PvfEntryLifecycleContentSource.WornPieceMetadata` | `ItemWorldClass.Avatar => AvatarItemKind,` → `=> WornPieceItemKind,` | `a1e8a7c4968dd74a03f1ba4335514379d73a2f2bce4af1a06012939314fd6781` | 失败 10 / 通过 2224，EXIT=1 | `AvatarSlotRulesAfterTheKindChangeTests` 四个用例（`A_quick_slot_refuses_the_avatar_the_archive_declares` 的 slotIndex 3／5／8，与 `An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it`）＋ `DraggedAvatarStaysWholeTests` 三条（`A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from`、`A_drag_of_one_onto_another_avatar_trades_the_two_slots`、`An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts`）＋ `GrantedAvatarIsWholeTests` 三条。该次运行里 Protocol 报 0 失败；Persistence 报 0 失败 |
| MU-2 | `ItemSlotRuleService.AllowsQuickSlot` | 排除项里去掉 `or KindAvatar` | `180538b4ca9a6debbbe64f2c9bf09a84539f5182215a1b4d775c84285b507864` | 失败 5 / 通过 2229，EXIT=1 | `AvatarSlotRulesAfterTheKindChangeTests` 四个用例 ＋ `ItemSlotRuleServiceTests.The_quick_slots_refuse_the_kinds_that_belong_to_another_list(itemKind: 8)`。**这 5 条里没有一条经 `MoveToAsync` 或 `DecideMoveAsync`**——`DraggedAvatarStaysWholeTests` 在这一次是绿的，所以 RV-02／RV-03／RV-04 的「无覆盖」读数在第二轮仍然成立 |
| MU-3 | `ItemArrivalRuleService.BandsFor` | 条件由 `placement.ItemKind == EquipmentKind \|\| !ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)` 改为 `placement.ItemKind == EquipmentKind` | `3593447673aa86a3a6a9a96c9604e19c4cce7c6fa8546d6fce90535963f00d4e` | 失败 1 / 通过 2233，EXIT=1 | `AvatarSlotRulesAfterTheKindChangeTests.An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it` |
| MU-4 | `StoreBackedItemContainer.LandAsync` 的 `IntoEmptySlot` 分支 | `isAvatar ? landing with { Value = ArrivingAvatar.NewIdentity() } : landing,` → `landing,` | `cd388e911834331b48da322e29085511307647ac7be3e9e9725c412d381ee46b` | 失败 3 / 通过 2231，EXIT=1 | `DraggedAvatarStaysWholeTests.A_drag_of_one_onto_another_avatar_trades_the_two_slots` ＋ `GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts`（`Assert.NotEqual() Failure: Values are equal`——纯断言红）＋ `.Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`（`System.InvalidOperationException : The world took the item and named no identity.`，栈根 `SqliteItemDomainStoreSession.FillAsync`，`SqliteItemDomainStore.cs:656`——异常红） |
| MU-5 | 同一分支 | `isAvatar ? ArrivingAvatar.Facts() : null,` → `null,` | `6222be998e9fe2354c8df8c4e5be7ec67aad7a9e88a4be120ca059324a0c3948` | 失败 4 / 通过 2230，EXIT=1 | `DraggedAvatarStaysWholeTests` 两条（`A_drag_of_one_onto_another_avatar_trades_the_two_slots`、`An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts`）＋ `GrantedAvatarIsWholeTests` 两条。四条的失败读数都是 `System.IO.InvalidDataException : Avatar detail <数> is unavailable.`，栈根同为 `InventoryEntryAssembly.WithAvatarDetail`（`InventoryEntryAssembly.cs:400`） |
| MU-6 | `SqliteItemDomainStoreSession.FillAsync` 的 `INSERT … SELECT` 守卫左侧 | `WHERE @expire IS NULL OR NOT EXISTS (` → `WHERE 1 = 1 OR NOT EXISTS (` | `a826cf2e0764926078899549f804db946661a8847e1442b23238bfeec62d746b` | 失败 3 / 通过 2231，EXIT=1 | `SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`、`TwoAvatarsDoNotShareOneIdentityTests.AWriteNamingAnIdentityTheWorldAlreadyStandsUnderIsRefusedAndRolledBack`、`TwoAvatarsDoNotShareOneIdentityTests.TheGuardIsBlindToFactlessRowsAndEngagesOnceTheWritesCarryFacts` |
| MU-7 | `SqliteItemDomainStoreSession.FillAsync` 的 `facts` 取值 | `item.AvatarDetail is { } carriedByTheWrite` → `item.AvatarDetail is { } carriedByTheWrite && item.Core.ItemKind == 0`（写入自带的事实到不了 INSERT） | `e959bb14bc86f158911bc729919aa3b98745209c7f7605b679a91d4c9a970219` | 失败 8 / 通过 2226，EXIT=1 | Persistence 4 条：`SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates`、`TwoAvatarsDoNotShareOneIdentityTests` 三条（`AWriteNamingAnIdentityTheWorldAlreadyStandsUnderIsRefusedAndRolledBack`、`TheGuardIsBlindToFactlessRowsAndEngagesOnceTheWritesCarryFacts`、`TwoAvatarsPutDownInSeparateSettlementsStandUnderIdentitiesOfTheirOwn`）；Application 4 条：`DraggedAvatarStaysWholeTests` 两条 ＋ `GrantedAvatarIsWholeTests` 两条。**第一版在这一次读到过一条 `CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold`，第二轮这一次没有出现** |
| MU-8 | `MoveItemSpaceHandler.ResolveAsync` 拒绝分支的 `LogInformation` | 第三个实参 `result.ItemId` → 常量 `0` | `97b36b8bd3d897d4782bea4f8e925c0374b36e2efe7c9ae132018acb89b22578` | 失败 2 / 通过 2232，EXIT=1 | `RefusedEquipIsRecordedWithItsTemplateTests.A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot` 与 `.An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot` |
| MU-9 | `MoveItemSpaceCommandHandler.PutOnAsync` 的 `NotWearable` 返回 | `Refused(ItemMoveStatus.NotWearable, piece.ItemId)` → `Refused(ItemMoveStatus.NotWearable)` | `1308ad14e620007adaa6a5edd3022e4f8f5837142ca892b524e4fe943e810c58` | 失败 1 / 通过 2233，EXIT=1 | `RefusedEquipIsRecordedWithItsTemplateTests.An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot` |
| MU-10 | `PutOnAsync` 的 `ItemCannotOccupySlot` 返回 | `Refused(ItemMoveStatus.ItemCannotOccupySlot, piece.ItemId)` → 单参 | `17ecabfeb72050aadc6ae7908188223e00083194cf4f75f691e94046e79a43f3` | 失败 1 / 通过 2233，EXIT=1 | `RefusedEquipIsRecordedWithItsTemplateTests.A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot` |
| MU-11 | `InventoryEntryAssembly.WithAvatarDetail` | `if (item.Core.Value <= 0)` → `if (item.Core.Value <= int.MinValue)` | `d9aa6bfde2f5700563a913b4076a7d43b5aeebf92c2067aba45ef1febc704d71` | **失败 0 / 通过 2234 / 跳过 2，EXIT=0** | 无 |
| MU-12 | `WithAvatarDetail` 的畸形检查 | `detail.JewelSocket.Length != 30` → `detail.JewelSocket.Length == int.MinValue` | `a3057a4b87336373d9253901233c658d24b83db94e6220dc631e5ec8294124a4` | **失败 0 / 通过 2234 / 跳过 2，EXIT=0** | 无 |
| MU-13 | `InventoryEntryAssembly.SaveAsync` 的脏租约时装扫描 | `.Where(value => value.Core.ItemKind == 8)` → `== 88` | `2843a41cc85007200dbe99b3a72d6754308dbb0a354d7b1be89033a096bd8c3a` | **失败 0 / 通过 2234 / 跳过 2，EXIT=0** | 无 |
| MU-14 | `InventoryEntryAssembly.SaveAsync` 的档案／行不符拒绝 | `worldClass == ItemWorldClass.Avatar && item.Core.ItemKind != 8` → `… && item.Core.ItemKind == 88` | `b5806850e0f8b414618861983e0edad66db2cf9cc45b1e7be1b9b5ccd5dda37d` | 失败 1 / 通过 2233，EXIT=1 | `ClassificationRefusalTests.AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot` |
| MU-15 | `StateSynchronizationPayloadCodec.Entry.WriteNoti2EquippedItem` 第三个字段 | `unchecked((uint)(core.ItemKind == avatarKind ? RemainingSeconds(...) : core.Value))` → `unchecked((uint)core.Value)` | `bff6c3b4aedb74af926ec4e5ef9f8c57f508dd88eb3fbd654ba35bca96c5be3d` | 失败 1 / 通过 2233，EXIT=1 | `SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty`。**字节锚 `SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` 在这一次没有变红** |
| MU-16 | `GameInventoryDelivery.WriteItem` 第三个字段 | `core.ItemKind == 8 ? Math.Max(0, avatar!.ExpireDate - checked((int)preparedAt.ToUnixTimeSeconds())) : core.Value` → `core.Value` | `976d98c00749f56f963aec08600b9e5182a447e34afd31e48eac74d96f394054` | 失败 1 / 通过 2233，EXIT=1 | **字节锚 `SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`**，且这一次只有它变红 |
| MU-17 | `GameInventoryDelivery.WriteItem` 的缺事实拒绝 | `if (core.ItemKind == 8 && avatar is null)` → `if (core.ItemKind == 88 && avatar is null)` | `597a2e3608d249c1aae783bc9b0454ce250cae90f7631e7cfb386ebf4680460e` | 失败 1 / 通过 2233，EXIT=1 | `SelectCharacterPayloadCodecTests.SelectCharacterPayload_HighRiskAvailabilityDistinctionsAreNotZeroFilled` |
| MU-18 | `ArrivingAvatar.NewIdentity` | `=> Random.Shared.Next(1, int.MaxValue)` 换成进程内自增 `System.Threading.Interlocked.Increment(ref nextIdentity) + 100`（另在其上加一行 `private static int nextIdentity;`） | `49e292582387c8986d2afe58aca29c90b2608ae086c450eb3083eaae639c18be` | **失败 0 / 通过 2234 / 跳过 2，EXIT=0** | 无 |
| MU-19 | `GameInventoryDelivery.WriteItem` 尾部的珠宝槽长度检查 | `if (avatar!.JewelSocket.Length != 30)` → `if (avatar!.JewelSocket.Length == int.MinValue)` | `13776f94f023949821059a7f90700f3a90fc47e0d5c4ab22adf196e6fdcd9f35` | **失败 0 / 通过 2234 / 跳过 2，EXIT=0** | 无 |
| MU-20 | `ContainerSlotItem.Count`（起因 E，把修法改回原样） | `public int Count => this.IsPile ? Core.Value : 1;` → `public int Count => Core.Value;` | `d0c8abe02bdaff2977576d005cc42adcadf505141acca09e248f5e528e39844f` | 失败 3 / 通过 2231，EXIT=1 | `DraggedAvatarStaysWholeTests` 三条，失败读数逐条抄在 3.6 节：`A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from`（`Assert.Null() Failure: Value is not null`，line 108）、`A_drag_of_one_onto_another_avatar_trades_the_two_slots`（`Expected: Moved { MoveCount = 1, ItemId = 101550507 }` / `Actual: Refused { Reason = PartialSwapNotAllowed }`，line 205）、`An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts`（`System.IO.InvalidDataException : Avatar detail 1408050310 is unavailable.`） |
| MU-21 | `ContainerSlotItem.Count`（反方向：什么都当成一件） | `public int Count => this.IsPile ? Core.Value : 1;` → `public int Count => 1;` | `1ca8c5681174ce32093088c0bdf25f64bc5d188346cdab70e6e59d0073c9d6eb` | 失败 22 / 通过 2212，EXIT=1（Persistence 失败 6 / 通过 314；Application 失败 16 / 通过 1020；其余五个项目零失败） | 跨 8 个测试类，逐条：`SplitPileTests` 的 `EitherHalfCanBeMovedOnItsOwnAfterTheSplit`、`SplittingAPileLeavesTwoUsablePilesAndMarksNeither`、`TheTwoHalvesPourBackIntoOnePile`；`SqliteEntryInventoryMutationRepositoryTests` 的 `DailyRefill_AddsToAPileThatExistsRatherThanOpeningASecond`、`DailyRefill_ClaimsZeroGrantButOwnershipFailureRollsBackAll`、`DailyRefill_RefusesToOpenASlotForARuleWithNoItemKindButStillJoinsAPile`；`MoveItemSpaceHandlerTests` 的 `A_merge_past_the_stack_limit_says_the_pile_is_full`、`A_merge_within_the_stack_limit_still_happens`、`Dragging_part_of_a_pile_onto_an_empty_slot_leaves_the_rest_behind`、`Dragging_part_of_a_pile_onto_another_item_will_not_swap`、`Two_piles_of_one_item_merge`；`UseStackableHandlerTests` 的 `A_large_pile_loses_only_one`、`A_second_use_inside_the_cooldown_is_refused`、`One_item_leaves_the_pile`；`TutorialRewardPlacementTests` 的 `A_grant_joins_a_pile_the_character_already_has`、`A_grant_larger_than_the_pile_can_take_places_only_what_fits`、`The_second_of_two_declarations_sees_what_the_first_wrote`；`BagPlacementVerbTests` 的 `Adding_and_refilling_disagree_about_a_pile_that_is_already_full`、`Adding_answers_that_there_is_no_room_rather_than_spilling_out_of_the_band`、`Adding_joins_a_pile_that_has_room_and_says_how_much_it_took`；`PersistentContainerBoundaryTests.InsideAUseCaseItAccepts`；`DraggedAvatarStaysWholeTests.A_drag_of_one_off_a_pile_still_divides_the_pile` |
| MU-22 | `SqliteCharacterItemTable.CarriesTheAvatar`（入场存盘侧的撞号守卫，第二版新增） | 常量末尾追加 `AND 1 = 0`，使 `NOT EXISTS(...)` 恒真、守卫不再拦任何写入（`@avatarKey` 仍在语句里被引用） | `e5781485e986ecceae0a753dd9befdd262cfb85fc5a0220965fb7999e6b23ab1` | 失败 2 / 通过 2232，EXIT=1 | `SqliteEntryInventoryMutationRepositoryTests.DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 与 `.DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`，两条的读数都是 `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(System.IO.InvalidDataException)` |
| MU-23 | `CharacterDatabaseSchema` 里 `items` 表的 `avatar_jewel_socket` CHECK（第二版新增） | `avatar_jewel_socket BLOB CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30)` → `avatar_jewel_socket BLOB CHECK (1 = 1)` | `d87b201864269cae14d4c1bf8422f8e8b1e4bc832f93fdc4eaa54f01c5de00a7` | 失败 1 / 通过 2233，EXIT=1 | `ItemCarrierShapeTests.ItemsCarriesEveryConstraintItsSourcesDeclared`——它比的是 `DeclarationAsync("items")` 给出的 DDL 文本（`Assert.Contains("CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30)", …)`），没有做一次违反该 CHECK 的写入。见 RV-21 那一栏对这个读数的处理 |

**还原**：23 次变异逐次由备份 `cp` 还原，还原后逐次 `sha256sum` 与该次变异前的散列比对，23 次比对逐次读到相同。
末态十二个文件与第〇节那张表**逐行相同**（本轮收尾时 `sha256sum -c` 与逐文件比对各做一次，都通过）；
`git status --porcelain` 收尾读到 18 行，与开工时逐行一致；
收尾全量重跑 **2234 通过 / 2 跳过 / 0 失败，EXIT=0**。

**第二轮没有跑过任何 `--filter` 运行**：23 次变异与两次交付态运行都是整条 slnx。
第一版记的那两次过滤运行（MU-4 之后取失败形态、MU-7 之后核既知偶发）本轮未重做；
MU-4 与 MU-5 的失败形态本轮直接从全量运行的输出里读出，抄在上表对应行。

---

## 五、点名记下的覆盖缺口

### NB-1（A-06 第 1 条）：「抽取」与「进程内自增」分不开

**观测（第二轮亲做）**：在交付态上把 `ItemContainers.ArrivingAvatar.NewIdentity()` 由
`Random.Shared.Next(1, int.MaxValue)` 换成进程内自增
（`System.Threading.Interlocked.Increment(ref nextIdentity) + 100`），
施加方为本任务，变异后散列 `49e292582387c8986d2afe58aca29c90b2608ae086c450eb3083eaae639c18be`，
观测到全量 **失败 0 / 通过 2234 / 跳过 2，EXIT=0**（MU-20 之前的 MU-18）。
即：这次施加之下，那次运行的 2234 条里报了 0 条失败。
（第一轮的同一处同一改法散列是 `57afb061…`，差别只在那行 `private static int nextIdentity;` 放在哪、
以及是否写全限定名；两轮的观测结果相同。）

**为什么这一条要紧**。本轮选「抽取不排序」的**理由**写在 `NewIdentity()` 自己的 `<remarks>` 里，
照抄 `CharacterRosterService.EquipmentCore`：「a sequence would need a row of its own to stay
distinct across restarts」——即选它是为了**跨重启保持互异**。
而 MU-18 那次运行报 0 失败——按第〇节的定义，本轮真正依赖的那条性质记作**无覆盖**。
`NewIdentity()` 的 `<remarks>` 说自增的坏处在跨进程重启时才显形；MU-18 那次运行是在一个进程里跑的，没有把这个分别照出来。

**A-06 记的是「三条相关判据全绿」**（出处：QR-TASK02-R2 的变异 E 与 D-02b 的移交表）。
本轮把观测面扩到全量：**2234 条一条未红**。两者方向一致，本轮的观测更宽。
TASK-08 新增的四条判据也在这 2234 条里，MU-18 那次它们同样是绿的。

### NB-1b（修正案）：「剩余秒数约 2.1×10⁹」是错的——实际约 3.59×10⁸ 秒，约 11.4 年

**先说清编号**：这里的「NB-1b」是**本清单**给这条修正案起的号。
它订正的对象由 `reviews/change-review.md` 第七节的 **NB-1** 指出——
**那是评审自己的编号，与本节上面那条 NB-1（「抽取」与「进程内自增」分不开）不是同一件事。**

**按仓规：改已被证伪的记载要补记修正案并写明原文，不是默默改数。**

**原文两处，逐字抄下**：

1. 本清单 3.3 的 **RP-06**：「一件发放来的时装到客户端时，第三个字段是
   `Math.Max(0, int.MaxValue - now)`（**约 2.1×10⁹**）而不是落地计数」。
2. 生产注释 `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` 的
   `ArrivingAvatar.NeverExpires` 的 `<remarks>`：
   "Nobody has yet watched a client draw an avatar whose remaining seconds are **two billion**"。

**错在哪，一句话**：那两处把 `int.MaxValue` 本身当成了上线的数，
而 `GameInventoryDelivery.WriteItem` 写上去的是
`Math.Max(0, avatar!.ExpireDate - checked((int)preparedAt.ToUnixTimeSeconds()))`
——**是差，不是被减数**。

**复算（本任务亲自算的一遍，读者可自行重做）**：

```text
int.MaxValue           = 2147483647
now（unix 秒）          = 1788687136      ← 本任务读表时刻 2026-09-06T09:32:11Z，`date +%s`
int.MaxValue − now     = 2147483647 − 1788687136 = 358796511 秒
358796511 ÷ 86400      = 4152.7 天
358796511 ÷ 31556952   = 11.37 年（分母取儒略年 365.2425 天）
```

即 **约 3.59×10⁸ 秒 ≈ 4,152 天 ≈ 11.4 年**。
**本清单从这里起，这个数只有一个说法：「约 11.4 年」**（＝ 上面算式第三行的 `11.37`
取三位有效数字；不写「十一年半」，那是把 `11.37` 说成了 `11.5`）。

**作为对照的那个错数，把它对应到哪个被减数也写清**：错的说法是「约 2.1×10⁹」，
它是 `int.MaxValue` 本身的两位有效数字写法。逐个算：
`2147483647 ÷ 31556952 = 68.05` 年（`int.MaxValue` 本身）；
`2.1×10⁹ ÷ 31556952 = 66.55` 年（写下的那两位有效数字本身）。
**所以「约 68 年」说的是 `int.MaxValue` 本身，不是「2.1×10⁹」这个写法。**
两者相差的倍数：`2147483647 ÷ 358796511 = 5.99`——**差了将近六倍**。

这个数**每过一秒就少一**，所以它不是一个可以钉死的常量——
上面写的是「在这一时刻读到的一次减法」，任何人拿自己的钟重做一次即可核或推翻。

**这一条为什么要紧——它影响所有者实机时判断客户端渲染是不是正常。**
所有者下一步要做的动作是：在实机上发一件时装，看客户端把那一格画成什么。

- 若他记着「约 68 年」，而客户端画出十一年多，**他会去找一个并不存在的缺陷**
  （怀疑服务端算错、或客户端把字段截了）。
- 若他记着「约 11.4 年」，同一次观测就读作「这个占位在正常工作」。
反过来，**客户端若真把这一格画成 68 年或一个不合理的日期，那才是要追的**。
这一格与 3.8 节丙档第 1 条（RP-06 的另一半）指的是同一件事，那里也写了这个数。

**这次订正没有改任何可执行代码。** 生产侧只动了上面第 2 处那段 `<remarks>` 的措辞，
并在其后另起一段 `<para>` 把原句「an avatar whose remaining seconds are two billion」引出来、
写明它错在哪、附上同一条算式。本清单侧在 RP-06 那一栏就地标注并指回本条。

**这个错的形状，与本轮另一个错同类**：`AGENTS.md`「这里曾经差 5」那一段记的是
「表是对的，小计算错了」；这一条是「算式是对的，把被减数当成了差」。
两者共因是**一个读者一次减法就能证伪的数，被写下时没有把算式一并写出来**。
本条按 `AGENTS.md` 自己给出的那条规矩写：**一个由算术得出的数，要么当场把算式写出来
（各项、运算、结果，三样齐），要么就不要写这个数。**

**这条修正案自己第一次写下时，犯了它要修的那件事的小号版本——记在这里，不擦掉。**
它要修的是「同一个数被写成两个说法」（`2.1×10⁹` 与 `3.6×10⁸`）；
而它第一次交付时，**在三份交付物里把订正后的那个数也写成了两个说法**：
本节标题与 3.8.2 写「约 **11.4** 年」，本节所有者段落却写「约**十一年半**」；
`AGENTS.md`「实机须知」第 3 条标题写「约**十一年半**」而正文写「≈ **11.4** 年」；
生产注释的 `<para>` 两处写 "roughly eleven and a half years"／"about eleven and a half"。
复算给的是 `11.37`，「十一年半」＝ `11.5`，**两个说法差了约 0.13 年（约 47 天）**。
由 TASK-10 的独立评审 **QR-10-A 的 F-2** 指出，第二轮统一为「约 11.4 年」，
凡出现该数处一律照此，英文侧写作 "about 11.4 years"。
**这是本成果第五次栽在同一个地方——「表是对的，小结／复述算错了」**：
前四次由执行方自陈并已订正（见第四节与 3.7.3 的复算口径），这是第五次。
**这一次的教训不是「再算一遍」，是「一个数一旦被订正，它的每一处复述都要照着算式抄，
不要用口语约数替换」**——口语约数（「十一年半」「六十八年」）正是让原错传开的那种写法。

### NB-2（A-06 第 2 条）：新增用例那四句 `NotEqual` 尚无断言形式的变异凭据

`GrantedAvatarIsWholeTests.Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`
里的四句 `Assert.NotEqual`（两句「不是自己那次发放的落地计数」、两句「两件时装不是彼此」）
**至今没有一次变异让它们以断言形式变红**。

- 出处（非本轮实测）：QR-TASK02-R2 的 NB-2 记下，变异 C 与变异 D 下该用例都因
  `FillAsync` 的守卫先抛而未走到那四句。
- 第二轮实测：MU-4 下该用例仍是异常红
  （`System.InvalidOperationException : The world took the item and named no identity.`，
  栈根 `SqliteItemDomainStoreSession.FillAsync`，`SqliteItemDomainStore.cs:656`），四句 `NotEqual` **没有走到**；
  MU-5 下该用例以 `System.IO.InvalidDataException : Avatar detail <数> is unavailable.` 变红，也没有走到那四句。
- 「新加的断言会红」这条性质由往返判据里的
  `Assert.NotEqual(landed.Count, first.Core.Value)` 承担——MU-4 下它以纯断言形式变红
  （`Assert.NotEqual() Failure: Values are equal`）。

这一条**不要求改判据**，要求的是记载口径如实。

### A-06e：`BandsFor` 那个析取支此前没有任何既有判据覆盖

**观测（出处：QR-04-1 非阻断观察第 ① 条）**：变异 B——把
`ItemArrivalRuleService.BandsFor` 里的 `|| !ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)` 去掉——
在本轮新判据之前，**1028 个既有用例全绿**。
即这条分支此前没有任何既有判据钉住它，是本轮新增的
`AvatarSlotRulesAfterTheKindChangeTests.An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it`
第一次钉住它。

**本轮的对应观测（MU-3）**：同一处、同一改法，变异后散列 `3593447673…`
（与 TASK-04 与 QR-04-1 所记**逐字符相同**），全量失败 1，红的正是上面那一条新判据。

QR-04-1 另两条非阻断观察（第六节可补记等价性论证使「不变」的凭据更强；
两句「两次 `ForGrant` 相比」的断言各自与钉死具体值的断言配对）**记为已知，不要求改判据**。

### A-06f：时装拖拽的量具缺口

见 3.4 的 RG-01 与 RG-02，两条各自写明了证据类型与出处。

---

## 六、`FillAsync` 守卫两侧的观测记录（A-06d）

**这一节只写观测，不由它推出关于「哪一道屏障承重」的结论。**
口径：同一条 CK-008（整个 `Dfo.Server.Persistence.Tests` 项目，未用 `--filter`）。

| 变异 | 左侧 `@expire IS NULL OR` | 子查询 `standing.avatar_expire_date IS NOT NULL` | 记下的结果 | 变异后散列 | 施加方与出处 |
|---|---|---|---|---|---|
| MR-A | 保留 | 去掉（改为 `1 = 1`） | 失败 0 / 通过 320，EXIT=0 | `614eddb4…` | 评审者，QR-TASK03-R1 第四节 |
| MR-B | 去掉 | 保留 | 失败 0 / 通过 320，EXIT=0 | `204b32e0…` | 评审者，QR-TASK03-R2 第二节 |
| M3 | 去掉 | 去掉（改为 `1 = 1`） | 失败 3 / 通过 317，EXIT=1 | `963d2af6…`（有记录的三次施加同为此值；另一次施加做在备份基线 `ba8bc2e0…` 上、散列为 `edf71b1c…`，还有一次未记散列。逐次记录见 TASK-03 第四轮 §1.3） | 执行者与评审者共 5 次施加 |

M3 那次记下的三条红分别是：
`TwoAvatarsDoNotShareOneIdentityTests.TheGuardIsBlindToFactlessRowsAndEngagesOnceTheWritesCarryFacts`
（红在前半的第二次 `PutDownAsync`）、
`WorldRowSurvivesTheMoveTests.TwoWornPiecesSwappingPositionsEachKeepTheirOwnIdentity` 与
`WorldRowSurvivesTheMoveTests.AMoveNeitherDeletesNorBuildsAWorldRow`（这两条红在各自的铺场那次 `SettleAsync`），
消息同为 `System.InvalidOperationException : The world took the item and named no identity.`。

**明写未确立**：**这些变异不足以确定哪一道屏障在一般情形下承重。**
三条观测都做在 CK-008 现有夹具上；本清单引到的记录里，没有一次施加是在
「库里已有一条带事实的旧行、且它与待写入的非时装行的 `substr(item_core, 6, 4)` 相同」这一配置下做的。
上面三行是三种配置下留有记录的观测，本节不由它们推出爆炸半径。

**另一条记载的覆盖实情（A-06b 第 1 项）**：`SqliteItemDomainStoreSession.FillAsync` 的记载里
「盲区自我延续」的**后半截**——即「子查询上那条 `avatar_expire_date IS NOT NULL` 过滤
是它看不见旧行的原因」——按第〇节的定义记作**无覆盖**。
出处：QR-TASK03-R1 的变异 MR-A，把该过滤单独去掉（保留左侧 `@expire IS NULL OR`）之后
CK-008 记下的是失败 0 / 通过 320。
前半截（落下的行那一列为 NULL）由 `TheGuardIsBlindToFactlessRowsAndEngagesOnceTheWritesCarryFacts` 里的
`Assert.Null(await ExpireDateUnderAsync(path, 9201))` 钉住；该句在 TASK-03 记录的七次变异里一次未红。

### 一条结构事实（A-06d 新增的验收条件第 7 条）

**证据类型：代码级读出，不是变异实测的结论。** 出处：QR-TASK03-R3 的 B-2。
下面三件由本轮直接读取工作区文件核对：

1. `SqliteItemDomainStoreSession.FillAsync` 的守卫子查询写的是
   `SELECT 1 FROM items standing` / `WHERE standing.avatar_expire_date IS NOT NULL` /
   `AND substr(standing.item_core, 6, 4) = substr(@core, 6, 4)`。
   这三行文本里没有出现 `ItemKind`，`FROM items standing` 后面也没有按种类挑行的条件——
   **两侧都不带 `ItemKind` 限制**：候选集是一切 `avatar_expire_date` 非空的旧行，
   比对键是 `substr(item_core, 6, 4)`。
2. `CharacterRosterService.EquipmentCore` 构造 `InventoryItemCoreState` 时，
   `Value` 这一位给的是 `Random.Shared.Next(1, int.MaxValue)`；
   该方法在同文件 `InsertOwnAsync(EquipmentCore(piece), null, cancellationToken)` 处被调用，造的是新建角色的普通装备。
3. `GameInventoryDelivery.CarriedGoldCore` 构造 `InventoryItemCoreState` 时写的是
   `ItemKind: 0`、`ItemId: 0`、`Value: Math.Max(0, carried)`，即用 `Value` 这一位装携带金币额。

即：普通装备的 `Core.Value` **并非恒为 0**；
`WorldRowSurvivesTheMoveTests` 的 `Sword`／`Ring` 的 `value: 0` 是**夹具性质**。

**今天到不了生产**：交付态（`SqliteItemDomainStore.cs` 散列 `87f22d2c…`）里守卫左侧仍写着
`@expire IS NULL OR`；一次非时装写入的 `facts` 为 null，`@expire` 绑 `DBNull`，
`WHERE` 左侧取真，非时装的写入进不了子查询。
本节不由这条读出推出爆炸半径，也不由它推出关于屏障承重的命题。

---

## 七、CK-017 —— 入场帧的整表字节

### 7.1 比对结果：逐字符不变

**锚**：`SelectCharacterAcceptedTranscriptTests.AcceptedKnightFrameSha256`——
一张 62 个 SHA-256 的字面量表，由
`SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`
与当前转录逐帧比对（`CaptureCurrentTranscript(result).Select(Sha256).ToArray()` 对 `Assert.Equal`）。

**这张表本轮未被改动**，第二轮亲自复核了三条实测（TASK-08 落地之后重做一次）：

- `git diff HEAD --stat -- Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`
  输出为空。
- 工作副本 `sha256sum` = `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`；
  `git show HEAD:<同一路径> | sha256sum` = `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`。逐字节相同。
- 第二轮开工与收尾各测一次这两条，两次读数相同；`git status --porcelain` 的 18 行里不出现这个路径。

**该判据在交付态下通过**：第二轮两次交付态全量运行里 Protocol 一栏依次为 524／0、524／0。

**结论：62 帧的每一帧散列与本轮之前的基线相同，即整表字节逐字符不变，没有需要归因的差异。**

### 7.2 这个「不变」不是空判——锚的敏感性

- **MU-16**：把 `GameInventoryDelivery.WriteItem` 里 kind 8 那一支的第三个字段
  由「剩余秒数」改成 `core.Value`，该次全量**只有字节锚一条变红**。
  即：这张表在 MU-16 这一处改动上有反应。成因可由代码级读出说明——
  锚所用的夹具 `SelectCharacterPayloadTestData.Create()` 的 `Avatar` 容器里有一件
  `Core(8, 2002, 9001)` 加六项事实的行，它走的正是 `GameInventoryDelivery.WriteItem` 的 kind 8 那一支。
- **MU-15**：把 `StateSynchronizationPayloadCodec.Entry.WriteNoti2EquippedItem` 的同类分支改掉，
  该次红的是 `SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty`，
  **字节锚没有红**。即：这张表在 MU-15 这一处改动上没有反应。

### 7.3 归因表：五处起因为什么一个字节也没动

| 起因 | 为什么它到不了这 62 帧 | 证据类型 |
|---|---|---|
| A `WornPieceMetadata` | 转录由 `CaptureCurrentTranscript` 造：`EncodeThroughRegisteredEncoders(SelectedCharacterStateMessageFactory.Create(result))`，`result` 是 `SelectCharacterPayloadTestData.CreateComplete()` 给的 `SelectCharacterResult`。这条链上没有档案：`ItemPlacement`／`RefillItemMetadata` 都不在其中，物品行的 `ItemKind` 由夹具的 `Core(8, 2002, 9001)` 与 `CoreForTest(...)` 直接给出 | 代码级读出 |
| B `ArrivingAvatar` ＋ `LandAsync` | 同一条链上没有容器：`StoreBackedItemContainer` 与 `IItemContainer` 不参与 | 代码级读出 |
| C `StoredItem.AvatarDetail` ＋ `FillAsync` | 同一条链上没有存储：`IItemDomainStore` 与 `SqliteItemDomainStoreSession` 不参与 | 代码级读出 |
| D `MoveItemSpaceSlice` | 两条：其一，`MoveItemSpaceResponse` 不在这 62 帧的身份表 `AcceptedStaticIdentities` 里；其二，拒绝分支的响应由 `MoveItemSpaceResponse.Failure(message, ErrorCodeFor(result.Status))` 造出，`result.ItemId` 只出现在同分支的 `LogInformation` 实参里 | 代码级读出 |
| E `ContainerSlotItem.Count` | 同一条链上没有容器（起因 B 那一行的理由同样适用）：`IItemContainer` 与 `ContainerSlotItem` 都不参与 `CaptureCurrentTranscript`；转录里的物品行由 `SelectCharacterPayloadTestData` 直接给出 `InventoryItemState`，不经容器 | 代码级读出 |

**归因表的余项：0 处。** 62 帧逐帧散列相同，没有一处差异需要归因，因此也没有无法归因的差异。

### 7.4 这一节不足以确定的

- 上表说明的是**这 62 帧**为什么不动。它不足以确定一件**发放来的**时装出现在背包里时，
  入场那一批帧会长什么样——锚的夹具里那件 kind 8 的行是夹具直接给的，不是发放通路造的
  （同 RP-06 的后半）。
- MU-15 与 MU-16 各是一种改法。它们不足以确定别的改法会不会让这张表变红。
- 起因 E 那一行同样只是代码级读出。第二轮的 MU-20、MU-21 两次变异里，
  字节锚都没有变红（那两次的红逐条列在第四节，里面没有它）；
  **这不足以确定起因 E 在别的输入上会不会动到这 62 帧。**

---

## 八、CK-018 —— 全量判据与用例数归因

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

**第二轮收尾那次运行的输出**（开工那次逐项相同）：

```
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52 - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   524，已跳过:     0，总计:   524 - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6 - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   320，已跳过:     0，总计:   320 - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   236，已跳过:     2，总计:   238 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:  1036，已跳过:     0，总计:  1036 - Dfo.Server.Application.Tests.dll (net10.0)
EXIT=0
```

合计 **2234 通过 / 2 跳过 / 0 失败，退出码 0**（52+524+60+6+320+236+1036 = 2234）。
两条跳过仍是 `Dfo.Server.Infrastructure.Tests` 里的
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。

### 相对本轮开工基线 2216 的差额：+18，逐条归因

计数口径：`dotnet test` 为每个 dll 报的那一行「总计」；基线取本轮开工时的
521 + 52 + 60 + 6 + 316 + 236 + 1025。

| 项目 | 基线 | 现在 | 差 | 构成它的各项 |
|---|---|---|---|---|
| Dfo.Server.Protocol.Tests | 521 | 524 | **+3** | `RefusedEquipIsRecordedWithItsTemplateTests` 三条（TASK-05）：`The_archive_calls_this_template_an_avatar_and_names_no_worn_slot_for_it`、`An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot`、`A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot` |
| Dfo.Server.Networking.Tests | 52 | 52 | 0 | —— |
| Dfo.Pvf.Tests | 60 | 60 | 0 | —— |
| Dfo.Server.Host.Tests | 6 | 6 | 0 | —— |
| Dfo.Server.Persistence.Tests | 316 | 320 | **+4** | `SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates`（TASK-01，1 条）＋ `TwoAvatarsDoNotShareOneIdentityTests` 三条（TASK-03）：`TwoAvatarsPutDownInSeparateSettlementsStandUnderIdentitiesOfTheirOwn`、`AWriteNamingAnIdentityTheWorldAlreadyStandsUnderIsRefusedAndRolledBack`、`TheGuardIsBlindToFactlessRowsAndEngagesOnceTheWritesCarryFacts` |
| Dfo.Server.Infrastructure.Tests | 236（另 2 跳过） | 236（另 2 跳过） | 0 | —— |
| Dfo.Server.Application.Tests | 1025 | 1036 | **+11** | 三批，分别是：`GrantedAvatarIsWholeTests` 三条（TASK-02）：`The_archive_and_storage_answer_the_same_kind_for_one_avatar`、`A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts`、`Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`；`AvatarSlotRulesAfterTheKindChangeTests` 四个用例（TASK-04）：`A_quick_slot_refuses_the_avatar_the_archive_declares` 的 `InlineData` 三个（slotIndex 3／5／8）与 `An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it` 一个；`DraggedAvatarStaysWholeTests` 四条（TASK-08）：`A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from`、`An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts`、`A_drag_of_one_onto_another_avatar_trades_the_two_slots`、`A_drag_of_one_off_a_pile_still_divides_the_pile`。3 ＋ 4 ＋ 4 = 11 |
| **合计** | **2216** | **2234** | **+18** | 3 ＋ 4 ＋ 11 = 18，与派工给的「Protocol 521→524、Persistence 316→320、Application 1025→1036」逐项目一致 |

**这个基线换过一次**：清单第一版记的差额是 +14（Application +7），
第二版是 +18（Application +11）；多出的 4 条全是 TASK-08 新增的 `DraggedAvatarStaysWholeTests`，
全落在 `Dfo.Server.Application.Tests`，其余六个项目的读数两版相同。
计数口径：`[Fact]`／`[SkippableFact]` 各计 1，`[Theory]`／`[SkippableTheory]` 按其 `[InlineData]` 条数计——
本轮读了这三个新增判据文件复算过。

**既知偶发**：清单第一版的 MU-7 那次运行里出现过一次疑似
（`CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold`）；**第二轮的 MU-7 那次它没有出现**。
第二轮 25 次运行（23 次变异 ＋ 开工 ＋ 收尾）的输出里，
`ObjectDisposedException` 未出现，栈根为 `CharacterDatabaseSchema.KeepWritersAndReadersApart` 的失败未出现。
第二轮交付态的两次全量运行（开工、收尾）都是 2234 通过 / 2 跳过 / 0 失败、EXIT=0，两次相同。

---

## 九、这份清单不足以确定的

- 本清单的覆盖读数来自第二轮施加的 23 次变异。
  **这些变异不足以确定别的改法会不会让某条被记为「无覆盖」的路径变红**——
  「无覆盖」在这里的意思由第〇节写死，指的是本轮施加的那一次具名变异下的读数，不是一句关于判据集的一般命题。
- RV-12、RV-13、RV-14、RV-18、RV-19、RV-22、RP-09、RG-01、RG-02 九处，
  与正向扫的 A-27、E-05、E-10、E-11，**本轮未施加变异**，
  因此它们的覆盖情况**未测定**，本清单不就其覆盖作断言。
- **起因 E 的回归面只被两次变异照过**（MU-20 与 MU-21）。
  这两次不足以确定装备容器、货仓、地面拾取这些别的容器通路上的非堆移动会怎样——本轮没有对它们单独施加变异。
- **RV-23 与 RP-08 的可达性论证是代码级读出**，不是一次实机或实测观测：
  它由 `CharacterRosterService.EquipmentCore` 的那一行、`TakeOffAsync` 的 `bag.PutAsync(…, piece.Core, …)`、
  `HandleAsync` 的 Main→Main 分支、`DecideMoveAsync` 的夹取与比较四处语句读出。
  **本清单没有把一件带抽出来的 `Value` 的普通装备真的拖过一次**，因此
  **不足以确定它在真实归档与真实客户端请求下的实际读数**。
- 正向扫里标为「不变」的判定（A-06、A-11、A-33、A-34、A-35、A-36、C-03、D-05），
  凭据是代码级读出加一次全量绿。**这不足以确定它们在别的输入上的答案。**
- 生产可达性一栏里写「今天到不了」的几处（RV-04、RV-12、RV-13、RV-18、RV-19、A-28..A-32），
  依据是 `PvfEquipmentSlots.ByName` 这张 16 条字面量表里以 `avatar]` 结尾的键为 0 条。
  **这不足以确定往后给这张表补键之后会怎样**；本轮按契约未给它补任何键。
- RG-01 的证据类型是**代码路径读出，非实机观测**：真实客户端把时装拖拽的目标列写成哪个号，本轮未观测。
- 本清单没有查时装能不能被穿上，也没有裁定任何一个时装名字对应哪一格；那是实机要回答的。
- **3.6 的两条（RX-01／RX-02）记的是修后的读数。** 它们在交付态下由
  `DraggedAvatarStaysWholeTests` 三条判据钉住，MU-20 让这三条各红一次。
  **本清单不由此确定「目标格站着同一个模板的时装时会怎样」**——那一支走的是
  `DecideMoveAsync` 的合堆分支，TASK-08 执行结果与 QR-08-1 都写明它只被代码阅读过、没有实测。
  目标格站着可互换的堆、或站着别的 world class 的个体，同样没有实测。
