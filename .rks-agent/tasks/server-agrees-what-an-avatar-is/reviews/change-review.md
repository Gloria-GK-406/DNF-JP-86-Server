---
kind: rks-review
verdict: changes_required
---

# 完整变更集独立评审 — `server-agrees-what-an-avatar-is`

评审范围：**change**（完整变更集，非单个 Task Unit）。
基线 `565d33fe`，未提交。评审者 fresh context，未采信任何 Task 自评或快审结论。

**本评审自己跑过的东西**（不是转述）：两次交付态全量判据、五次变异实测（含一次自造探针）、
十三个文件的 `sha256sum`、字节锚与 HEAD 的比对、`.Count` 与 `ItemKind` 读点的自行枚举。
每一次变异都用文件级备份 ＋ `sha256sum` 还原并核过散列，**未使用任何 git 还原类命令**。

---

## 一、判定

**`changes_required`。** 阻断理由一条：**反向扫漏了第 37 条**，
而漏掉的那一条**正是 RX-01 那个缺陷的第二个生产制造者，今天还活着**。
其余七条成功条件在我自己的证据下成立，判据质量与既有断言未被迁就实现改写，
分层、切片、架构判据、协议帧、`store` 一词五项守住。

---

## 二、阻断发现

### BF-1 — 地面拾取这道门上，一件时装仍然被造成「没有六项事实的行」

**位置（成员名）**：
`Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs`
的 `PickUpGroundDropCommandHandler.CarryOverAsync`，
以及同类 `HandleAsync` 里 `case ItemPlacementOutcome.PutInEmptySlot` 那一支对它的调用。

**机理（逐环点名，全部由工作区文件的语句读出，末环由我实测坐实）**：

1. `PickUpGroundDropCommandHandler.HandleAsync` 先 `container.AddAsync(piece.ItemId, piece.Count, …)`。
   起因 B 之后，这条路对一件时装写下的是：core 的 `Value` ＝ `ArrivingAvatar.NewIdentity()` 抽出来的身份，
   外加 `ArrivingAvatar.Facts()` 的六项事实；`StoreBackedItemContainer.LandAsync` 随即
   `ApplyDecidedWritesAsync`，该行落进库里，六列齐备。
2. 紧接着 `CarryOverAsync` 对**同一格**再写一次，写的是
   `landed.Core with { Value = checked((int)found.Instance), Durability = found.Durability }`，
   走的是 `IItemContainer.PutAsync`。
   `StoreBackedItemContainer.PutAsync` 把它转成 `WriteOneAsync(slotIndex, core, null, cancellationToken)`
   —— **`avatarDetail` 是 `null`**（这正是清单 C-03 写作「不变」的那一句）。
3. 这次写入把 **`Core.Value` 换掉了**。于是在
   `SqliteItemDomainStoreSession.Survivors` 的三趟里：
   `SameContent` 不成立（core 字节变了），
   `SameThing` 也不成立（它比 `core.Value == placing.Item.Core.Value`，而这一位刚被换掉）。
   **该行不是幸存者**，于是被 `EmptyAsync` 删掉，由 `FillAsync` 重建。
4. `FillAsync` 算 `facts`：`item.AvatarDetail` 是 null（第 2 步），落到回捞；
   而 `CarriedAvatarsAsync` 的检索键取的是 **staged 那一条的 `Core.Value`**，即新的 `found.Instance`，
   库里站着的那一行的 UID 是旧的抽取身份，`substr(item_core, 6, 4) = @uid` 对不上。
   **回捞落空** → 六列全 NULL，`@expire` 绑 NULL，`FillAsync` 的撞号守卫左侧再次取真、短路。
5. 下一次入场装载：`InventoryEntryAssembly.WithAvatarDetail` 的 `detail is null` 命中，
   抛 `InvalidDataException: Avatar detail {value} is unavailable.` —— **整个角色的物品装载失败**。

**我的实测**（本评审亲做，非引用）：
在 `Server/tests/Dfo.Server.Application.Tests/Inventory/DraggedAvatarStaysWholeTests.cs` 里
临时追加一条探针，复现第 1–2 步的形状（同一个 `AddAsync` 发放，然后
`bag.PutAsync(slot, held!.Core with { Value = 123456, Durability = 77 })`，再请入场装载读它）：

```
System.IO.InvalidDataException : Avatar detail 123456 is unavailable.
   at Dfo.Server.Application.Inventory.InventoryEntryAssembly.WithAvatarDetail(...)
      InventoryEntryAssembly.cs:line 400
   at Dfo.Server.Application.Inventory.InventoryEntryAssembly.ReadCharacterItemsAsync(...) line 334
```

失败 1 / 通过 4。探针已删除，该文件还原后 `sha256sum` ＝
`a359da9d50d577c82a5bc19cb9fac023d7aa15302eb7dbb541f66297825f8d01`，与变异前逐字节相同。

**为什么这是阻断，而不是一条观察**：

- **它违反 RQ-002／RQ-003 的字面。** `spec.md` 的 Definitions 把「发放通路」定义为
  「服务器把一件事先不在角色身上的物品交给角色的那条路，卡片奖励、**地面拾取**、
  任务给予与奖励、教程奖励都走它」。RQ-002／RQ-003 说的是「经**发放通路**入库之后是完整的、
  可被重新装载」。**地面拾取这道门上不成立。**
  因此 SC-2「这一失败模式不再存在」在交付态只对 `AddAsync` 单独一条路成立。
- **它违反 SC-5／RQ-008。** 清单第三节的计数口径是「一条 ＝ 一个『路径 × 方向』」，
  而这条新增的制造者不在 3.1–3.4 的 34 行里、不在 3.6 的两条里、不在正向扫的 B-01..B-05 里，
  `AGENTS.md` 也没有点到它。清单把 RV-08（`WithAvatarDetail` 的 `detail is null` 拒绝）
  记作「**有覆盖**」，读起来像这条拒绝已经被照看过了；
  **但它只覆盖了拖动这一个制造者，另一个制造者从未被枚举。**
- **它与上一个成果被拦下的失败模式同型。** 上一轮漏的是「新增拒绝没被走到」，
  这一轮漏的是「已列出的拒绝多出一个制造者」。两者共因：**扫的是判定点，不是写入方**。

**可达性，我如实分档**：
`ItemPlacementRuleService.IsEquipmentAsync` 的实现是
`ClassifyAsync(...) is not ItemWorldClass.Interchangeable`，
**对 `ItemWorldClass.Avatar` 返回真**，所以 `CarryOverAsync` 的 `if (!found.IsEquipment) return;`
这道闸门**拦不住时装**。至于一件时装今天能不能出现在地板上，
本评审**未确立**：`DropRuleService` 对 `DropItemKind.Avatar` 返回 null，
且档案里时装不声明 `[creation rate]` —— 这两条是契约自己给的，我没有复测。
但（a）本轮自己的口径是「今天到不了」的路径**照样要记**（RV-12／RV-13／RV-18／RV-19 就是这么记的），
（b）所有者下一步就是 GM 直接发放／掉落，那正是绕开这两道闸门的动作。

**要补什么**（三样都要，缺一不算修完）：

1. **代码**：让「把一件已经站着的时装的 `Core.Value` 换掉」这件事要么不发生，
   要么带着它的六项事实一起写。可选落点是 `CarryOverAsync` 本身（时装不改 `Value`）、
   `WriteOneAsync`（换 `Value` 的写入必须交代事实）、或
   `Survivors`／`CarriedAvatarsAsync`（按旧 UID 而不是新 UID 回捞）。
   **选哪个由实施方定，但要给出理由。**
2. **判据**：一条走「`AddAsync` → 改 `Value` 的二次 `PutAsync` → 入场装载」的判据，
   加一次变异实测证明它会红。
3. **记载**：`behavior-change-list.md` 反向扫补这一条（它使 3.1–3.4 的行数由 34 变 35），
   并把「RV-08 有覆盖」改写成「RV-08 的两个制造者分别是什么、各自有无覆盖」；
   `AGENTS.md` 同步。

---

## 三、成功条件逐条判定

| ID | 判定 | 我自己的证据 |
|---|---|---|
| SC-1 | **达成** | `PvfEntryLifecycleContentSource.WornPieceMetadata` 的 `switch` 对 `ItemWorldClass.Avatar` 给 `AvatarItemKind`（8）——我读了改后的返回语句。`GrantedAvatarIsWholeTests.The_archive_and_storage_answer_the_same_kind_for_one_avatar` 对真实档案的模板 `101550507` 同时断言档案侧 `ItemKind`、`ClassifyAsync`、以及经生产发放路径落库后读回的 `stored.Core.ItemKind` 三者都是 8，且那个 8 是该判据自己的 `const`，不借发放通路的常量 |
| SC-2 | **未达成（见 BF-1）** | 「发放 → 装载 → 存盘 → 再装载」那一趟由 `A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts` 走通，且**真的承重**：我把 `ArrivingAvatar.NewIdentity()` 改成 `=> 1`，该判据与 `Two_granted_avatars_…` 各红一次（`Assert.NotEqual() Failure: Values are equal`），还原后散列 `8e7f085d…` 一致。**但这只覆盖 `AddAsync` 这一道门**；地面拾取那道门上失败模式仍在 |
| SC-3 | **达成** | `AGENTS.md` 新节点名三类来源（档案内容／`protocol/` 实测／上游转写）各自的结论，17 个未补的键逐行列出并带模板数。我复算了那张十七行表：时装 11 项相加 66,328、非时装 6 项相加 268、合计 66,596、行数 17 —— 与文中给的复算逐项相同。三个对不齐的数（12 格带宽／11 种类型名／GM 文档 0-10）在文中被裁定 |
| SC-4 | **达成** | 我自己做了两次变异。其一把 `MoveItemSpaceHandler.ResolveAsync` 日志实参 `result.ItemId` 换成常量 `0` → `RefusedEquipIsRecordedWithItsTemplateTests` 两条整句判据变红。其二把同一实参表里的 `message.DestinationSlotIndex` 换成 `99` → 同样两条变红。**模板号与槽号两半各自被钉住**，不是只钉了一半。两次都还原并核散列 `d96cdb01…` |
| SC-5 | **未达成（见 BF-1）** | 清单确有「新增或改变的拒绝与放行路径」一节，每条给位置（按成员名）、生产可达性、判据有无，三个读数的定义在第〇节写死，且明写「不以全量全绿作为覆盖凭据」——**方法是对的**。但完整性不成立：漏了 BF-1 那条制造者；另有一处自己的口径没贯彻到底（见 NB-3） |
| SC-6 | **达成** | `git diff --stat 565d33fe -- …/SelectCharacterAcceptedTranscriptTests.cs` 输出为空；工作副本 `sha256sum` ＝ `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`。**与 HEAD 逐字节相同，没有需要归因的差异** |
| SC-7 | **达成** | 我自己跑的全量（开工一次、收尾一次，读数相同）：`524 + 52 + 60 + 6 + 320 + 236 + 1036 = 2234` 通过 / 2 跳过 / 0 失败，`EXIT=0`。差额 +18 ＝ Protocol +3、Persistence +4、Application +11。我按「`[Fact]`／`[SkippableFact]` 计 1、`[SkippableTheory]` 按 `[InlineData]` 条数计」逐文件复算：Protocol 3（`RefusedEquip…`）、Persistence 1 ＋ 3、Application 4（`AvatarSlotRules…` ＝ 1 Theory×3 ＋ 1 Fact）＋ 3（`GrantedAvatar…`）＋ 4（`DraggedAvatar…`）＝ 11。**逐项对上** |
| SC-8 | **达成，但记载随 BF-1 一起要补** | `AGENTS.md` 记了做成的三件、没做的一件（时装仍穿不上及其理由）、四条修正案、两条实机须知，以及本轮之后仍剩的缺陷。缺的是 BF-1 那一条 |

---

## 四、五处指定复核，逐处结论

### 复核 1 —— `ContainerSlotItem.Count` 那处超出时装的行为变化

**结论：不算越权。记法与定性对，证据类型标得准（有一处措辞可改进，见 NB-4）。**

- **授权面**：契约 Constraints 写的是「本轮**允许**改变可观察行为（这是目的），
  但每一处都要能逐条归因」；Spec Constraints 写的是「未被列出的可观察变化即为缺陷」。
  **两处约束的是「记全」，不是「只许改 Spec 列举的那几处」。**
  Spec 的 Non-Goals 里也没有一条排斥它。
- **被迫性成立**：起因 B 把身份写进 `Core.Value` 之后，只有三条路——
  给时装开特例（普通装备继续坏着）、不把身份写进 `Value`（那要放宽三处明文写下的要求，
  按仓规必须停下来问，plan 已明写否掉）、或者按属性自己的 `<summary>` 把读法修对。
  选了第三条，**而那句 `<summary>`（"For anything that does not stack, one."）在基线上就写着**，
  我用 `git show 565d33fe:Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` 核过。
- **证据类型准**：`AGENTS.md` 与清单 RV-23／RP-08 都标「代码级读出，未实测也未实机」。
  **我复现了 MU-20**（`Count` 改回 `Core.Value`，变异后散列
  `d0c8abe02bdaff2977576d005cc42adcadf505141acca09e248f5e528e39844f`，与清单记的逐字符相同）：
  全量报失败 **3**，三条全在 `DraggedAvatarStaysWholeTests`，
  **没有一条把一件带抽取 `Value` 的普通装备送进 `MoveToAsync`**。
  即「无覆盖」这个读数属实，「未实测」这个标签属实。还原后散列 `a39f94f9…` 一致。

### 复核 2 —— RX-01／RX-02 这条「本轮引入又本轮修掉」的链

**结论：链记全了。** 引入者（起因 B 换 `Value`）、放大者（基线 `Count => Core.Value`
与它自己的 `<summary>` 相反）、症状（`Split` 拆坏 → 下次装载抛
`Avatar detail … is unavailable.`）、发现者（**QR-06-1 独立快审，不是执行者自查**，清单两处写明）、
处置（D-06 判为本轮引入的缺陷而非记载遗漏 → 新建 TASK-08 → TASK-06 退回重跑）、
修法（`Count => this.IsPile ? Core.Value : 1`）、覆盖（MU-20 三条红，逐条抄了失败读数）
—— 七环齐备，且第一版与第二版的差异（反向 28→36 条、正向 48→60 条）逐条给了来历。
**MU-20 我亲自复现，读数与记载逐条相同。**

### 复核 3 —— 那处基数错值的三处订正

**结论：三处彼此一致，都没有比证据说得更强。**
`TaskContract.md` 的 **C-3**、`findings-archive-measurement.md` 的 **F-1**、
`AGENTS.md` 的「这里曾经差 5」一段，三处给的数完全相同：
时装 **66,328**、非时装 **268**、合计 **66,596**。三处都保留原文另起一段订正，符合仓规。
定性「表是对的，小计算错了」的凭据是：探针输出那 11 个数相加 ＝ 66328、
两次独立重测与表逐个相同、非时装六项 268 两版一致（差额 5 全落在时装小计）、
上一个成果三处独立记载都写 66328。**这四条支撑「加法错误」这个结论是够的，没有过强。**
`AGENTS.md` 那句「档案里并没有另外 5 个时装模板可数」也带了口径限定
（「按上面那一遍 118,479 个模板的口径」），没有说成全称。
我把十七行表的加法当场重算过：8391+8325+8194+8070+7929+7565+7028+7000+2954+449+423 = 66,328，
65+50+47+47+31+28 = 268，合计 66,596。对得上。

### 复核 4 —— 契约 Scope Out 的四条

**结论：四条都没做，记载也没写成比这更完整。**

- `Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs`
  **不在变更集里**（`git status --porcelain` 与 `git diff --stat 565d33fe` 都不出现）。一个键也没补。
- 契约兽 24／25 未裁定。`AGENTS.md` 提到三个键写作 24 时，明写
  「所以『`[earring]` 一次都没被用到』说的只是这一个键，不是 24 号槽在档案里没有东西」
  —— **是在收窄，不是在裁定**。
- 契约兽领域未动；孤儿清理器未做（变更集内无相关文件）。
- 记载对「没做到」是主动写出来的（`AGENTS.md`「没做的那一件：时装仍然穿不上」整节）。

### 复核 5 —— 量具的两处已知缺口

**结论：两条都属实，标签准确。**

- **RG-01**：我读了 `MoveItemSpaceCommandHandler.HandleAsync`。它先判 Main→Equipment（`PutOnAsync`）、
  再判 Equipment→Main（`TakeOffAsync`），**都不成立才落到**
  `if (command.SourceListType != MainListType || command.DestinationListType != MainListType)`
  → `ItemMoveResult.Refused(ItemMoveStatus.ListNotServed)`，**单参重载，`ItemId` 位是 0**。
  目标列写成 `Avatar`（＝1）确实落在这里，槽号仍在、模板号丢失。
  记载标「代码路径读出，非实机观测」—— 准。
- **RG-02**：`TakeOffAsync` 里五个返回（`BothSlotsEmpty`、`ItemUnknown`→`NotWearable`、
  `SlotNotAllowed`→`ItemCannotOccupySlot`、`displacedSlot != command.SourceSlotIndex`→
  `DisplacedCannotOccupySlot`，加更早的 `NoBoundContainer`）**全部是单参重载**，我逐个读过。
  记载标「代码级读出」—— 准。

---

## 五、判据质量与既有断言

**没有找到恒真断言。** 唯一一处形似的是
`GrantedAvatarIsWholeTests` 里的 `Assert.True(first.Core.Value > 0)`
（正是派工提醒的那种陷阱：一格开给一件东西时那一位本就是 1，恒真）。
**它被同一处的 `Assert.NotEqual(landed.Count, first.Core.Value)` 兜住了，而且那句真的承重**——
我把 `ArrivingAvatar.NewIdentity()` 改成 `=> 1`（即「什么也没抽」），
`Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts` 与
`A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts` **各红一次**，
读数 `Assert.NotEqual() Failure: Values are equal`。判据文件自己的注释把这个理由写在断言旁边。

**没有找到判据名与它证明的东西不符的。** 抽查三处：
`TheGuardIsBlindToFactlessRowsAndEngagesOnceTheWritesCarryFacts` 确实两半都在同一条用例里跑
（不带事实 → 两行并存无报错；带事实 → 第二次被拒），**单独任何一半都不成立**，名副其实；
`A_drag_of_one_off_a_pile_still_divides_the_pile` 确实读回源格与目标格各 1，是对照而非陪衬；
`The_archive_calls_this_template_an_avatar_and_names_no_worn_slot_for_it` 确实两个前提各断言一次。

**既有断言没有为迁就实现被改写。** 变更集里只有两个既有判据文件被动过：
`ItemClassificationTests.cs` 的改动**全部在 `<remarks>` 里**（补记修正案，无断言变化）；
`SqliteItemDomainStoreTests.cs` 是**净新增一条 `[Fact]`**，既有用例一字未动。
两处我都逐行读过 diff。

---

## 六、整体成立性

- **分层**：新增面只有 `StoredItem.AvatarDetail`（Abstractions）、`ArrivingAvatar`（Application，`internal`）、
  `FactsOf`（Persistence，`private`）、`ItemMoveResult.Refused` 双参重载（Protocol）。
  没有反向引用；`*.csproj`／`*.slnx` 一个都没改。
- **切片架构**：`git diff --stat 565d33fe -- "*ArchitectureTests*"` 输出为空——
  `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 的
  **判定谓词与具名豁免清单一个字都没动**，且两者在我这两次全量里通过。
- **协议帧**：变更集里没有新增消息类型，`git diff 565d33fe` 的新增行里
  没有一处 `writer.Write`／`MessageId`／`IGameCmdMessage`／`IGameNotiMessage`。
  `ItemMoveResult.ItemId` 只进日志实参，拒绝分支的响应仍由
  `MoveItemSpaceResponse.Failure(message, ErrorCodeFor(result.Status))` 造出——**帧布局未动**。
- **`store` 一词**：新增行里出现的 `store` 全部是 `SqliteItemDomainStore` 的局部变量，
  仍只有「事务结算的真源、名字以 `DomainStore` 结尾」这一种读法。
- **`PutAsync` 的语义**：由 `async ValueTask` 改为转调 `WriteOneAsync` 的表达式体，
  `ArgumentNullException.ThrowIfNull(core)` 随之移进 `WriteOneAsync`（仍是 `async`），
  所以对 null 仍是「返回一个 faulted 的 ValueTask」而不是同步抛。**语义未变**，我读过两版。

---

## 七、非阻断观察

- **NB-1（数目可被读者当场证伪）**：清单 RP-06 写「一件发放来的时装到客户端时，第三个字段是
  `Math.Max(0, int.MaxValue - now)`（**约 2.1×10⁹**）」；
  `ItemContainers.ArrivingAvatar.NeverExpires` 的 `<remarks>` 同样写
  "an avatar whose remaining seconds are **two billion**"。
  实际算式是 `GameInventoryDelivery.WriteItem` 的
  `Math.Max(0, avatar!.ExpireDate - checked((int)preparedAt.ToUnixTimeSeconds()))`，
  即 `2,147,483,647 − ≈1.79×10⁹ ≈ 3.6×10⁸` 秒（约 11.4 年），**不是 2.1×10⁹（约 68 年）**。
  这正是「不写读者无法证伪的基数」那条规矩要防的形状 —— 一次减法就能证伪。
  所有者实机看到的是「约十一年」而不是「约六十八年」，**会影响他判断客户端渲染是否正常**。
- **NB-2（记载与交付态对不上）**：清单第〇节与 7.1 节都写
  「`git status --porcelain` … 均为 **18 行**（12 个 ` M` ＋ 6 个 `??`）」。
  **交付态是 19 行**（13 个 ` M` ＋ 6 个 `??`）—— 多出的是 `AGENTS.md`，由其后的 TASK-07 改动。
  18 是 TASK-06 那一刻的读数，却写在自称描述交付态的段落里。
  （7.1 节的实质结论「字节锚不在改动清单里」我独立核过，成立。）
- **NB-3（自己的口径没贯彻到底）**：正向扫起因 A 自称 36 条，
  口径是「一个被起因触达的判定点，按成员名」。
  `Server/src/Dfo.Server.Application/Characters/SelectCharacterPostEntryServices.cs`
  里除 A-33 那处负重求和之外，还有一处读 `ItemKind` 的判定
  （`equipped?.Core is not { ItemKind: CreatureItemKind }`），**未列**。
  它与已列的 A-34／A-35 同类（都是拿 `ItemKind` 与 5 比、结论都是「不变」），
  所以不影响结论，但它是继 A-36 之后**同一类枚举遗漏的第二例**。
- **NB-4（措辞会误导所有者）**：`AGENTS.md` 写
  「所有者实机测普通装备时若看到与线上老服不同的拖动行为，**那不是本轮新引入的缺陷**」。
  那个行为差异**确实是本轮新引入的**（只是不是缺陷）。
  建议改成「那是本轮**记在案的、有意的**行为变化，不是未记的缺陷」。
- **NB-5（plan 要求的一条残余未点名）**：`plan.md` 的 Assumptions 写
  「唯一的例外是邮箱附件摘要，它读的是另一张表的核心，不经世界表 —— 本轮不动它，
  **但要在记载里点名为残余**」。
  我在 `behavior-change-list.md` 与 `AGENTS.md` 里按「邮箱／Mailbox／附件」检索，**0 命中**。
- **NB-6（漂移面）**：时装那个 `8` 现在有四个独立落点 ——
  `PvfEntryLifecycleContentSource.AvatarItemKind`、`ItemContainers.ArrivingAvatar.ItemKind`、
  `SqliteItemDomainStoreSession.AvatarKind`，加 `InventoryEntryAssembly`／`GameInventoryDelivery`
  里的裸字面量 `8`。每一处都写了为什么它在那里，本轮也没有让它们不一致；
  仅记为往后的漂移面。
- **NB-7（一处我核过、成立的隐患）**：`LandAsync` 用 `itemKind == ArrivingAvatar.ItemKind`
  而不是 `worldClass` 决定要不要造时装形状。我核了
  `PvfEntryLifecycleContentSource.ResolveStackableItemKind` 的全部返回值（7／9／10／4／11／3／2），
  **没有一条返回 8**，所以今天只有 `WornPieceMetadata` 对 `ItemWorldClass.Avatar` 那一支能产出 8。
  当前成立，不构成发现；若将来有第二个 8 的来源，这一支会给一个堆造出身份并毁掉它的计数。
- **NB-8（我核过、安全的相邻门）**：`SelectCardCommand.DeliverAsync` 也在 `AddAsync` 之后
  对同格再 `PutAsync` 一次（补 `Durability`），但它**不改 `Core.Value`**，
  于是 `Survivors` 第三趟 `InPlace && SameThing` 认它，六列由 `LandAsync` 的 UPDATE 保住。
  **这道门是安全的** —— 记在这里，是因为它证明 BF-1 建议的那趟「制造者扫」能出结论，不是空转。

---

## 八、方法论：收窄后的记法口径，判一下

**判定：口径本身是对的，但它有一个盲区，而这一轮的漏项正是那个盲区的产物。**

**它做对的地方**（有实据，不是恭维）：
第〇节把「有覆盖／无覆盖／未测定」三个词的意思写死，并明写
「不以全量判据全绿作为覆盖的凭据 —— 一次全绿恰恰是『无覆盖』的读数」；
第九节把「不足以确定」的九处逐条点名；
`AGENTS.md` 主动写「本轮**没有**逐文件穷尽 `protocol/` 的 1,819 个文件」。
**这些都是该说的、且过去被说得太满的地方。** 我复现的三次变异（MU-2 同型、MU-20、日志两半）
读数与记载逐条相同，说明这些标注不是保守修辞，是真读数。

**盲区在哪**：这套口径管的是**一句话怎么写**（不许全称、要给口径、要标证据类型），
它**不管扫描的对象选得对不对**。反向扫的口径是「一条 ＝ 一个『路径 × 方向』」，
落到执行上扫的是**判定点（拒绝／放行发生在哪）**，
**不是写入方（谁能把系统送进那个拒绝）**。
于是 RV-08 被记作「有覆盖」，读起来像已经照看过了，
而**它的第二个生产制造者从头到尾没有被枚举** —— BF-1 就是这么漏掉的。
**「有覆盖」这个词在这里压掉了该说的话**：它说的是「这条拒绝有判据会红」，
读者却会读成「这条拒绝不会被意外触发」。

**两条具体建议**：

1. **反向扫加一趟「制造者扫」**：对每一条「触发即导致装载失败或结算回滚」的拒绝，
   按成员名枚举**所有能到达它的生产写入方**，而不只是拒绝点本身。
   本轮若做了这一趟，`GroundPickupSlice.CarryOverAsync` 与
   `SelectCardCommand.DeliverAsync` 会在同一张表上被逐个判过（见 NB-8）。
2. **「未测定／无覆盖」要按后果分档**：现在 9 条「未测定」＋ 11 条「无覆盖」平铺在一起，
   读者无法一眼看出哪一条触发了会让角色进不去。
   建议至少分成「触发即角色进不去 / 触发即一笔结算回滚 / 触发只影响一次请求」三档。
   **所有者马上要做实机验证，他需要的是「先看哪个」，而不是「哪些没量」。**

**有没有把该说的话压掉？** 除上面那一处外，只有一处轻微的：
`AGENTS.md` 讲普通装备拖动行为变化时，结论句被三层证据类型限定包着，
**没有给出一句所有者能直接照做的检查**（「拖一件普通装备到空格：此前拆分、或对调被拒；
现在整件移动、整件对调」）。信息在文里，但不在他会先读到的位置。这是排版问题，不是记法问题。

**有没有过强的话？** 我没有找到。抽查的每一处基数都带口径且可复算；
每一处标「代码级读出」的确实只是代码级读出，没有被写成实测。

---

## 九、本评审自己跑过的东西（可复现）

| # | 动作 | 读数 |
|---|---|---|
| 1 | 交付态全量（开工、收尾各一次）：`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 两次同为 2234 通过 / 2 跳过 / 0 失败，`EXIT=0`（524+52+60+6+320+236+1036） |
| 2 | 12 个生产文件 `sha256sum` 对清单第〇节的交付态散列 | **12 个逐个相同** |
| 3 | 字节锚 `git diff --stat 565d33fe --` ＋ `sha256sum` | 输出为空；`1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69` |
| 4 | 变异：`ItemSlotRuleService.AllowsQuickSlot` 去掉 `KindAvatar` | 5 红（`AvatarSlotRules…` 4 ＋ 既有 `ItemSlotRuleServiceTests…(itemKind: 8)` 1）；还原核散列 `0438efe9…` |
| 5 | 变异：`ArrivingAvatar.NewIdentity() => 1` | 2 红（`GrantedAvatarIsWholeTests` 两条），读数 `Assert.NotEqual() Failure: Values are equal`；还原核散列 `8e7f085d…` |
| 6 | 变异：日志实参 `result.ItemId` → `0` | 2 红（`RefusedEquip…` 两条整句判据） |
| 7 | 变异：日志实参 `message.DestinationSlotIndex` → `99` | 2 红（同上两条）；还原核散列 `d96cdb01…` |
| 8 | 变异 MU-20 复现：`Count => Core.Value` | 全量失败 **3**，三条全在 `DraggedAvatarStaysWholeTests`；变异态散列 `d0c8abe0…` 与清单记的相同；还原核散列 `a39f94f9…` |
| 9 | **探针**（BF-1）：`AddAsync` 之后对同格 `PutAsync` 换 `Core.Value`，再请入场装载 | `InvalidDataException : Avatar detail 123456 is unavailable.`，栈根 `InventoryEntryAssembly.WithAvatarDetail`；文件还原核散列 `a359da9d…` |
| 10 | 自行枚举 `ContainerSlotItem.Count` 的读点 | **与清单起因 E 的 11 处逐条相同**，未找到第 12 处 |
| 11 | 自行枚举 `Server/src` 里读 `ItemKind` 的判定点 | 找到一处未列（见 NB-3），其余与起因 A 的表相合 |
| 12 | `git diff --stat 565d33fe -- "*ArchitectureTests*" "*PvfEquipmentSlots*" "*.csproj" "*.slnx"` | 输出为空 |

**变异纪律**：全部用文件级备份 ＋ `sha256sum` 还原并核散列；
**未使用任何 git 还原类命令**；每次变异用完立刻还原；
收尾时 `git status --porcelain` 仍为 19 行、全量仍为 2234 / 2 / 0 / `EXIT=0`，
与我开工时读到的逐行、逐项相同。
**未触碰主检出与 `.worktrees/client-debug-lab`。**
