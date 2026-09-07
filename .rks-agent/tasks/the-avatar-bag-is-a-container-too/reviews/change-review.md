---
kind: rks-review
verdict: approved
round: 4
---

# 变更评审：装扮栏也是一个容器（scope = change）

独立复核，未采信执行方任何自评。所有数字与名单均自跑自数。
工作区 `.worktrees/the-avatar-bag-is-a-container-too`，基线 `7147d2e9`，未提交。
评审期间对生产文件做过两次变异并已还原，`sha256sum -c` 两行全 `OK`，
`git status` 回到 8 改 + 2 新增 + 1 个未跟踪任务目录（未使用任何 git 还原类命令）。

---

## Blocking Findings

### B-1　SC-2「装扮栏内换位……真的落库」未被判据支撑，且判据自己的注释把这件事说反了

**错在哪。** SC-2 要求「装扮栏内换位被服务**并真的落库**；判据覆盖**空格→空格**、
**有物品→空格**、以及**交换两格**」。实际交付的栏内换位判据只有一条：
`TheAvatarBagIsServedTests.A_move_inside_the_avatar_list_is_handed_to_that_list`
（`Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/TheAvatarBagIsServedTests.cs:113-124`）。
它跑在自造的 `RecordingContainer` 上，而那个假容器的 `MoveToAsync`
（同文件 `:313-321`）**只记下槽位对并返回一个罐头 `Moved`，字典一格没动**。
所以这条判据断言的全部内容是「请求被转交给了装扮栏容器」，
既没有证明换位落库，也没有覆盖三种场景中的另外两种：

| SC-2 要求的场景 | 有判据吗 |
|---|---|
| 有物品 → 空格 | 只有 dispatch 层（假容器），未证落库 |
| 空格 → 空格（应答 `BothSlotsEmpty`） | **无** |
| 交换两格 | **无** |

**全仓无任何判据用真实容器跑过装扮栏。** 计数口径：`grep -rn "new ContainerShape(" Server/tests --include=*.cs`
共 11 处命中，其中 10 处写死 `InventoryContainerType.Main`，第 11 处
`Server/tests/Dfo.Server.Application.Tests/Game/CreatureGoesBackOnTests.cs:182` 用变量 `type`，
而 `:174-177` 把它限定为 `{ Main, Equipment }`。
`CreatureGoesBackOnTests` 恰恰是本仓已有的、拿真 SQLite + 真 `PersistentItemContainer` +
真 `MoveItemSpaceCommandHandler` + `DomainTransactionScope` 跑移动的夹具——
把 `Avatar` 加进那个数组就是 SC-2 要的证据形态，而它没有被加。

**判据的注释把这个缺口说反了。** `TheAvatarBagIsServedTests.cs:262-263`：

> The move inside one list belongs to the container and **is asserted where a real one is**

不存在这样一处。这是「记载写得比证据强」，正是契约末条禁止的。

**变化清单第七节没有披露这一条。** 该节列了 4 条局限（无实机、容量取上游读数、
客户端拖拽未观测、R-5 不可达未写判据），**没有一条**提到栏内换位只被假容器证明。

**影响**：SC-2 未达成；AC-002 的两个 Gherkin 场景（落空格、与另一件交换）未被覆盖。

**要做的**：把 `Avatar` 加进 `CreatureGoesBackOnTests` 那类真容器夹具（或另起一个同形夹具），
补上三种场景的落库断言；同时改掉 `:262-263` 那句不成立的话，
并在变化清单第七节如实记下假容器证不了什么。

---

### B-2　`PutOnAsync` 把被顶下来的装备写回源列表时不问源列表的规则——本轮新开的一个绕过口，且它的辩护注释已经变成假话

**错在哪。** `MoveItemSpaceSlice.cs:290-296` 现在允许 `Avatar → Equipment` 走进 `PutOnAsync`，
`from` 参数因此可能是**装扮栏容器**。而 `PutOnAsync` 的换下分支
（`MoveItemSpaceSlice.cs:389`）是：

```csharp
await bag.PutAsync(command.SourceSlotIndex, displaced.Core, cancellationToken)
```

`displaced` 是原先穿在身上的那件装备，`bag` 现在可能是装扮栏。
**这一写没有经过任何准入判定**，于是一件 `WorldClass == Equipment` 的装备
会被写进只收 `WorldClass == Avatar` 的装扮栏——绕过本轮刚立起来的那条规则。

它的辩护写在 `MoveItemSpaceSlice.cs:354-356`：

> Whatever was already worn goes back into the slot the new piece came out of.
> **That slot held a piece of equipment a moment ago, so it is a slot equipment may occupy**
> and there is no second judgement to make.

这句话在源列表只可能是主背包时成立。本轮把装扮栏加成可能的源之后它不成立了：
那一格刚才装的是装扮栏里的东西，不是「一件装备待过的格子」。
**这是本轮改动使之失效的第四条说法**，而变化清单第五节列了 C-1..C-3 并写明
「**没有找到第四条**」。它就在被改动的同一个文件里，距离新增的 `CarriedListFor` 二十几行。

**同族的另外两条路都做了这次判定，唯独这一条没有**——这说明它是漏掉而不是决定：

- `CarryAcrossAsync`（`:454-462`）：`back = MayOccupySlotAsync(from.Type, displaced.ItemId, sourceSlot)`，
  不合就 `DisplacedCannotOccupySlot`。
- `TakeOffAsync`（`:507-516`）：`WornSlotForAsync(displaced.ItemId) != SourceSlotIndex` 就拒。
- `PutOnAsync`：无。

**可达性**（自己核的，不引用执行方）：今天要触发需要装扮栏里躺着一件
`WornSlotForAsync` 答得出槽位的东西，也就是一件**非时装**——时装今天全部答 `null`（issue #11 未做）。
装扮栏里出现非时装，经由玩家操作不可能（`Main→Avatar` 走 `CarryAcrossAsync` 会被规则拒、
`Equipment→Avatar` 走 `TakeOffAsync` 会被 `bag.Type` 那一问拒、栏内移动走 `DecideMoveAsync` 也被拒），
但**经由直接写库完全可能**——本仓的日常做法就是这样（契约自己记着「本轮实机把 11 件时装用 SQL 塞进主背包」）。
更要紧的是：issue #11 一落地，`Avatar → Equipment` 就变成天天走的路，
这条无判定的写回也就从边角变成主干。

**影响**：违反 RQ-006「放行面不得超出 RQ-003..RQ-005」与 RQ-007 的准入约束；
是变化清单第三节（反向 6 条）漏掉的第 7 条，也是第五节漏掉的 C-4。

**要做的**：给 `PutOnAsync` 的换下路径补上与 `CarryAcrossAsync` 同形的第二次判定
（或明确写下为什么不需要，但那需要一个比现有注释更真的理由），
并把这一条与失效的注释一起记进变化清单。

---

### B-3　`TakeOffAsync` 的 XML 文档被 `CarryAcrossAsync` 顶掉了：一个成员挂了两份 `<summary>`/`<remarks>`，另一个成员一份没有

**错在哪。** 新方法被插在了 `TakeOffAsync` 的文档注释与 `TakeOffAsync` 本身之间：

```
MoveItemSpaceSlice.cs:396      /// <summary>Taking a piece off: ...</summary>   ← 原属 TakeOffAsync
MoveItemSpaceSlice.cs:397-408  /// <remarks> ... </remarks>                     ← 原属 TakeOffAsync
MoveItemSpaceSlice.cs:409      /// <summary>Carrying a thing from one of the character's lists ...</summary>
MoveItemSpaceSlice.cs:410-427  /// <remarks> ... </remarks>
MoveItemSpaceSlice.cs:428      private async ValueTask<ItemMoveResult> CarryAcrossAsync(  ← 两份文档全挂在它头上
MoveItemSpaceSlice.cs:481      private async ValueTask<ItemMoveResult> TakeOffAsync(      ← 零文档
```

于是 `CarryAcrossAsync` 的第一句文档说自己是「Taking a piece off: out of its slot and into the
bag slot the player aimed at」——**假的**；而 `TakeOffAsync` 关于「没有抓包依据」「换位要两端都成立」
的那段来历记载**从它身上消失了**。编译器不为重复的 `<summary>` 报警（CS1571 只管 `param`），
所以 `TreatWarningsAsErrors` 抓不到，全绿也抓不到。

**影响**：本仓把注释当成来历档案；这是一处由本轮改动制造的、指向错误成员的记载。
变化清单 S-4 声称「三处注释按已查明改写」，没有这一条。

**要做的**：把 `:396-408` 那段移回 `TakeOffAsync` 上方。

---

## 非阻塞观察

**N-1（漏报的一条已变拒绝）** `HandleAsync` 把绑定检查从「进函数第一件事」挪到了
`ListNotServed` 判定之后（`MoveItemSpaceSlice.cs:282-305`）。基线上主背包未绑定时，
**任何**请求（含未服务列表对）都答 `NoBoundContainer`；现在未服务列表对先答 `ListNotServed`。
两者的客户端码不同：`ErrorCodeFor` 里 `NoBoundContainer → Silent(2)`、`ListNotServed → NotMovable(23)`。
生产不可达（入场必绑，缺容器时 R-1 已抛），但它是一条**被改变的拒绝**，
变化清单第三节只记了 R-3 的两条新增 `NoBoundContainer`，没记这一条。

**N-2（新可达路径上的 `{ItemId}` 仍是 0）** 契约 SC-4／issue #10 第二条要求
新服务的列表对「先读到物品再拒」。`Equipment → Avatar` 这条新服务的路上，
`TakeOffAsync` 的两处拒绝仍不带 itemId：
`MoveItemSpaceSlice.cs:498` `Refused(ItemMoveStatus.NotWearable)`、
`:501` `Refused(ItemMoveStatus.ItemCannotOccupySlot)`——`piece` 早已读到手，只是没传。
这两行本轮未被改动（是既有形态），但**新可达**。
契约 Scope 允许「仍不服务的列表对 itemId 仍为 0 但要如实说明」；
这是**已服务**的列表对，不在那条豁免里，而交付物没有说明。
（对照：`PutOnAsync` 的两处都带 itemId，`CarryAcrossAsync` 的四处也都带。）

**N-3（判据名与它证明的东西）** `AvatarListHasASlotRuleOfItsOwnTests.The_main_list_answers_the_same_either_way`
不是字面恒真（它比较两个不同方法的返回），但在当前实现下**不可能红**：
`ItemSlotRuleService.cs:39` 对 `Main` 是 `=> this.CanOccupyMainSlot(placement, slotIndex)` 直接委托。
它证明的是「今天只有一份实现」，不是「主背包规则答对了」——
若有人把 `CanOccupyMainSlot` 改成恒 `false`，这条仍绿。作为防演化哨兵成立，
清单里「钉住两种问法对 4 种物品 × 8 个槽位答案完全相同」的说法也准确，
但把它算进「一处实现的证据」时应说清它只覆盖委托关系。

**N-4（陈旧文档）** `MoveItemSpaceSlice.cs` 的 `ItemMoveStatus.NoBoundContainer` 仍写
「The character is not bound to a **main** container」，现在它也用于装扮栏与装备栏未绑。

**N-5（AC-009 的证据很薄）** 跨容器移动的事务性只靠既有的
`CommandRegistrationTests`（钉 `MoveItemSpaceCommand` 注册为 `Transactional`，
`ProtocolModule.cs:195-196`）与 `PersistentContainerBoundaryTests`（无环境事务即抛）。
AC-009 允许「用既有的事务判据形态」，所以勉强成立，
但交付物没有指名它靠的是哪一条既有判据，读者无从复核。

**N-6（流程产物账目）** `sdd-execution.yaml` 声明 `artifacts.result: "./spec-result.md"`，
该文件不存在且没有任何 stage 的 `writes` 产出它；反过来，本轮的核心产物
`behavior-change-list.md` 根本没被 `artifacts` 声明。

---

## 已独立核实为正确的部分（复核证据）

**判据总数（自跑）**：`PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo`
→ 退出码 0，逐项目 Protocol 579 / Networking 52 / Pvf 60 / Persistence 329 / Host 6 /
Infrastructure 236(+2 跳过) / Application 1064 = **2326 通过 / 2 跳过 / 0 失败**。
与基线 2289 的差 +37 = Protocol +12、Application +25，与两个新文件的用例数吻合
（`TheAvatarBagIsServedTests` 7 Fact + 1 Theory×5 = 12；
`AvatarListHasASlotRuleOfItsOwnTests` 4+3+3+7+8 = 25）。**SC-7 达成。**

**放行面恰好等于授权（SC-6 / RQ-006 / AC-006）**——从 `HandleAsync` 穷举有序列表对：
`CarriedListFor` 只对 `Main(0)` 与 `Avatar(1)` 非空，于是被服务的 8 对是
`Main→Main`、`Avatar→Avatar`（`MoveToAsync`）；`Main→Avatar`、`Avatar→Main`（`CarryAcrossAsync`）；
`Main→Equipment`、`Avatar→Equipment`（`PutOnAsync`）；`Equipment→Main`、`Equipment→Avatar`（`TakeOffAsync`）。
基线服务 3 对，新增恰好 5 对，与契约 In 段的三行逐条对应。
`Equipment→Equipment` 未被顺带放行（两端 `CarriedListFor` 皆空 → `ListNotServed`）。
**没有别的组合被放行；主背包↔宠物栏等一律 `ListNotServed`。**

**M1 变异（自己复现，最重要的一条）**：备份 + `sha256sum`，把 `CarriedListFor`
改成 `(InventoryContainerType)listType`，跑 `Dfo.Server.Protocol.Tests`：
→ `失败: 5，通过: 574，总计: 579`，红的全部是
`TheAvatarBagIsServedTests.A_list_this_server_does_not_serve_is_still_refused`
的 5 个 `MemberData` 用例，**其余 574 条一条没红**。
还原后 `sha256sum -c` → `OK`。**与执行方所报的 5 / 574 与名单完全一致。**

**M3 变异（第二次抽核）**：去掉装扮栏规则里的 `placement.WorldClass == ItemWorldClass.Avatar`，
只剩区间判定 → Application `失败: 3，通过: 1061`（全是
`AvatarListHasASlotRuleOfItsOwnTests.The_avatar_list_takes_nothing_but_costumes` 的 3 个用例）、
Protocol `失败: 1，通过: 578`（`Something_that_is_not_a_costume_does_not_get_into_the_avatar_list`）。
合计 4 红，**与所报一致**。还原后两个文件的 `sha256sum -c` 均 `OK`。

**改名基数（S-2）**：`git grep -n "MayOccupyMainSlotAsync\|MainSlotAdmission" 7147d2e9 -- "*.cs"`
在基线上 **9 处命中，分布于 3 个文件，全部在 `Server/src`**：
`IItemMovementDomainService.cs`（2）、`ItemMovementDomainService.cs`（4）、`MoveItemSpaceSlice.cs`（3）。
`Server/tests` 零命中。改后全仓两个旧名零残留。**零判据受影响属实。**

**R-1「生产不可达」属实**：`InventoryEntryProjection.ProjectCharacter`
（`Server/src/Dfo.Server.Application/Inventory/InventoryEntryServices.cs:38-66`）
对 `CharacterContainerOrder` 这个含 `Main, Avatar, PersonalCargo, Equipment, Pet` 五项的静态数组
做 `.Select(...)` 无条件产出，缺项走 `GetDefaultParameter`。生产不可能少一个容器。
**因而 E 问的答案是：给夹具补 avatar 不是在掩盖生产缺陷**——
生产恒产五种，此前只造两种的手搭夹具确实不像一个真的入场库存。

**R-5「装备栏容器上 `MoveToAsync` 必然拒绝，但生产不可达」属实**：
`grep -rn "MoveToAsync" Server/src --include=*.cs` 共 3 处——接口声明
（`IItemContainer.cs:197`）、唯一实现（`ItemContainers.cs:521`）、唯一调用方
（`MoveItemSpaceSlice.cs:320`）。那个调用点在 `source == destination` 分支内，
只可能是 `Main` 或 `Avatar`。装备栏到不了。
（而且 `AvatarListHasASlotRuleOfItsOwnTests.ListsWithNoRuleOfTheirOwn` 把 `Equipment` 列了进去，
这条不可达的新语义反而被显式钉住了。）

**AS-003 属实**：`grep -rn "SnapshotAsync" Server/src` 只有接口声明、抽象声明与
`ItemContainers.cs:779` 的实现，**零生产调用方**；绑定装扮栏踩不到 `:783` 那个 `NotSupportedException`。

**分层合法**：`Server/src/Dfo.Server.Abstractions/Dfo.Server.Abstractions.csproj` 在**基线上就已经**
`ProjectReference` 到 `Dfo.Server.Data`（该 csproj 不在 diff 里），
`Dfo.Server.Data.csproj` 无任何 ProjectReference。
`using Dfo.Server.Data.Inventory;` 顺 `Data → Abstractions` 方向，合法。

**上游读数属实**（只读核对 `E:/Project/DNF/ServerS4A21`）：
`Game/Inventory/Services/ItemSlotBoundService.cs:248-251`
`GetAvatarOpenRange(int avatarListParam16) { return new ItemSlotRange(0, 209); }`——参数确实被丢弃；
`:137-139` `case InventoryListType.Avatar: itemKind = ItemCore.KindAvatar; return GetAvatarOpenRange(0).Contains(slotIndex);`。
AS-001 / RQ-007 / SC-5 的上游依据准确，且交付物标明了「上游源码读出、非实机」。

**`ErrorCodeFor` 仍逐条列举**：`MoveItemSpaceSlice.cs` 尾部 11 个 `ItemMoveStatus` 分支逐条列出，
未改成 fallthrough；该方法不在 diff 内。

**协议帧未动 / 字节锚未变**：`git diff --name-only` 不含任何编码器或
`SelectCharacterAcceptedTranscriptTests.cs`；`Dfo.Server.Protocol.Tests` 579 条全绿。**SC-7 后半达成。**

**架构判据未被放宽**：`CommandHandlerArchitectureTests.cs` 与
`NotificationHandlerArchitectureTests.cs` 均不在 diff 内，随 Application 套件全绿。

**T-1 逐条核对属实**：`SelectCharacterEntryLifecycleServiceTests.cs` 的 diff 共 5 个 hunk——
`:62`、`:122`、`:215`、`:270` 各补一行 `InventoryContainerState(Avatar, 0, [])`（4 处夹具），
`:205` 用例改名，`:245-252` 末尾新增注释与 3 行断言。
**没有任何既有断言被删除、放宽或改期望值**（`:230-240` 的
`Assert.Equal(loaded.Parameter, bound.Shape.Parameter)` / `Assert.Equal(156, ...)` 等原样保留）。

### 逐条契约判定

| | 判定 | 依据 |
|---|---|---|
| SC-1 | 达成 | 绑定见 `SelectCharacterEntryInventoryDomainService.cs:73-83, 88`；`Entry_binds_the_character_to_its_own_main_equipment_and_avatar_containers:250-252` 钉住 |
| SC-2 | **未达成** | B-1 |
| SC-3 | 达成 | `A_costume_moves_from_the_avatar_list_into_the_bag` / `..._from_the_bag_back_into_the_avatar_list`，跑真实档案，两端读回均断言 |
| SC-4 | 基本达成，一半有缺 | `Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list` 钉住 `NotWearable` + itemId；反向 `Taking_something_off_into_the_avatar_list_reaches_that_list` 达到 `TakeOffAsync`。但见 N-2 |
| SC-5 | 达成 | `AvatarListHasASlotRuleOfItsOwnTests` 25 条；M3 证明规则可被抓；上游读数已逐行核对 |
| SC-6 | 达成 | 5 条反向判据覆盖源/目的/两端三种形态；M1 亲手复现 5 红 574 绿 |
| SC-7 | 达成 | 2326/2/0，退出码 0；字节锚文件未动且全绿 |
| SC-8 | **未达成** | 反向漏 B-2、N-1；被推翻的说法漏 C-4（即 B-2 的注释）；S-4 漏 B-3；第七节未披露 B-1 的假容器局限 |

---

# 第二轮：整改后复审（scope = change）

独立复核，**未采信执行方任何自评**，也未采信第一轮评审的任何结论——凡引用第一轮的地方
都注明「第一轮已核、本轮未重跑」。所有数字自跑自数。

工作区 `.worktrees/the-avatar-bag-is-a-container-too`，基线 `7147d2e9`，未提交。
复审期间对生产文件做过两次变异（M4 复现、M5 自拟），每次用完立刻用文件级备份还原并
`sha256sum -c`，三行全 `OK`；**未使用任何 git 还原类命令**。收工时 `git status` 为
9 改 + 3 个未跟踪判据文件 + 1 个未跟踪任务目录，与开工时逐行一致。

**判定：`changes_required`。**
B-1 / B-2 / B-3 三条阻塞**在代码上确已解决**，判据也真的钉得住（M4、M5 均由我自己复现）。
**新的阻塞全部在记载一侧**：整改本身引入了一处**未申报的、可达的既有路径行为变化**，
外加三处「已改」其实没改／说反了的陈述。这正是本仓「修一句错话时写下另一句错话」的形态。

---

## 一、判据（自跑）

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

跑前 `tasklist | grep -i Dfo.Server.Host` 无命中。**跑了四次**（开工一次、两次变异后各一次、
收工一次），无偶发失败，无需按既知偶发形态判别。

| 项目 | 通过 | 跳过 |
|---|---:|---:|
| Pvf | 60 | 0 |
| Networking | 52 | 0 |
| Protocol | 581 | 0 |
| Persistence | 329 | 0 |
| Infrastructure | 236 | 2 |
| Host | 6 | 0 |
| Application | 1070 | 0 |
| **合计** | **2334** | **2** |

失败 0，退出码 0。与执行方所报的 **2334 / 2 / 0** 一致。
基线 2289 → 差 **+45**；逐项目差为 Protocol +14、Application +31，
与 `TheAvatarBagIsServedTests` 14 条（9 Fact + 1 Theory×5）、
`AvatarListHasASlotRuleOfItsOwnTests` 25 条、`MovingInsideTheAvatarListTests` 6 条
（4 SkippableFact + 1 SkippableTheory×2）逐项吻合。**变化清单第六节的计数属实。**

---

## 二、三条阻塞的处置判定

### B-2　**已解决**（代码正确、位置正确、判据钉得住）

**判定加在正确的位置。** `MoveItemSpaceSlice.cs:383-389`：读到 `displaced` 之后、
`worn.PutAsync` / `bag.PutAsync` **两端任何一次写入之前**返回 `DisplacedCannotOccupySlot`。
`An_ordinary_piece_coming_off_does_not_land_in_the_avatar_list` 除断言拒绝码与 itemId 外，
还回读装扮栏槽 0 与装备栏槽 14 两端**均未改变**——所以「整个移动被拒而不是写了一半」
是被断言的，不是推断的。

**M4 自己复现。** 用 `python` 二进制模式删去 `:383-389` 那七行（保持无 BOM / LF 不变），
跑**全量**：

```
失败 Dfo.Server.Protocol.Tests.Game.MoveItemSpace.TheAvatarBagIsServedTests.An_ordinary_piece_coming_off_does_not_land_in_the_avatar_list
失败!  - 失败: 1，通过: 580，总计: 581 - Dfo.Server.Protocol.Tests.dll
其余六个套件全绿（Application 1070 全通过）
```

**全套件恰好红 1 条，且就是那一条**，与执行方所报一致。还原后 `sha256sum -c` `OK`。

**「这条路今天不可达」——独立判定为属实。** 论证链我自己走了一遍：

1. 要走到那次判定，到达件必须 `WornSlotForAsync` 答得出槽位。时装今天全部答 `null`
   （由绿着的 `Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list`
   反证），所以到达件必须是**待在装扮栏里的非时装**。
2. 非时装进不了装扮栏：`Main→Avatar` 与栏内移动都过 `CanOccupySlot(Avatar,…)`
   （`ItemSlotRuleService.cs:47-50` 要求 `WorldClass == Avatar`），`Equipment→Avatar` 过
   `TakeOffAsync` 的同一问。
3. **生产里没有第四个写入者**：`grep -rn "FindContainer\|GetContainer" Server/src --include=*.cs`
   的全部命中中，除 `MoveItemSpaceSlice` 外每一处都写死 `Main` 或 `Equipment`
   （`SelectCardCommand:323`、`GroundPickupSlice:307`、`AcceptQuestCommand:120`、
   `FinishQuestCommand:115`、`GrantTutorialRewardCommand:49`、`UseStackableSlice:239`、
   `DailyRefillService:68`）；`GetContainers`（遍历型）在 `Server/src` 内零调用方。
   **没有任何生产路径遍历已绑定容器**，所以绑定装扮栏不会让别的路径开始行为不同。

结论：只有直接写库能造出该状态，判据用 `10018` → 槽 14 构造是诚实的。
「#11 落地即可达」也成立。**这一条的记载与实现都对。**

### B-1　**已解决**（真容器 + 真存储 + 真档案，且那条判别性判据真的有判别力）

`MovingInsideTheAvatarListTests` 经 `InventorySide.BindList(Avatar, …)` →
`new ContainerShape(containerType, parameter)` → `PersistentItemContainer` + `SqliteItemDomainStore`
+ 真档案 `IItemPlacementDefinitionSource`。**「真容器 + 装扮栏形状」这个组合确实建立起来了。**
SC-2 要求的三种场景各有一条并**回读存储**：

| SC-2 场景 | 判据 | 回读 |
|---|---|---|
| 有物品 → 空格 | `A_costume_moves_into_an_empty_slot_and_the_store_says_so` | 两端都读回（源 `Null`、目的等于模板号） |
| 交换两格 | `Two_costumes_trade_places` | 两端都读回，两件互换 |
| 空格 → 空格 | `Dragging_from_an_empty_slot_moves_nothing` | 答 `BothSlotsEmpty` |

**「槽 70 那条是否真有判别力」——我自己变异验证，不只看推理。**
自拟变异 **M5**：把 `ItemContainers.DecideMoveAsync` 的两处
`slotRules.CanOccupySlot(shape.ContainerType, …)` 改回 `slotRules.CanOccupyMainSlot(…)`
（即「容器退回读主背包 band」），跑全量：

```
失败 MovingInsideTheAvatarListTests.The_avatar_list_turns_away_what_is_not_a_costume(targetSlot: 70)
失败 MovingInsideTheAvatarListTests.The_avatar_list_turns_away_what_is_not_a_costume(targetSlot: 7)
失败 MovingInsideTheAvatarListTests.A_costume_moves_into_an_empty_slot_and_the_store_says_so
失败 MovingInsideTheAvatarListTests.Two_costumes_trade_places
失败!  - 失败: 4，通过: 1066，总计: 1070 - Dfo.Server.Application.Tests.dll
Protocol 581 全绿
```

**槽 70 那条确实红**——变化清单第八节「若容器退回读主背包的 band，槽 70 那条会漏过」**属实**。
而且这一整个文件（4/6 条）都能抓住这次退化，**Protocol 套件一条都抓不住**，
正说明第一轮 B-1 指出的缺口是真的，现在被补上了。

**但判据自己的注释有一句是假的**，见第三节 F-4。

**`BindMain` → `BindList` 的委托行为逐值不变——自己核过。** 旧 `BindMain` 的形参
（`characterId`、`ushort parameter = 0`、`IItemPlacementDefinitionSource? placements = null`）
与新 `BindList` 逐项相同且默认值相同，新 `BindMain` 原样透传并传 `InventoryContainerType.Main`；
`BindList` 体内只把两处写死的 `Main` 换成参数。既有 17 处 `BindMain` 调用点
（`DraggedAvatarStaysWholeTests`、`GrantedAvatarIsWholeTests`、`PickedUpAvatarStaysWholeTests`、
`SplitPileTests` ×3、`SqliteEntryInventoryMutationRepositoryTests` ×11）**一处未改**，
`git diff --name-only` 里不含这 5 个文件。**行为不变属实。**

### B-3　**已解决**

`MoveItemSpaceSlice.cs:407-424` 是 `CarryAcrossAsync` 自己的 `<summary>`/`<remarks>`；
`:478-490` 是 `TakeOffAsync` 自己的。**各有且只有一份，第一句都与所挂成员相符。**
第一轮那种「一个成员挂两份、另一个零份」的形态已消失。

---

## 三、新发现（本轮整改**自己**引入的问题）

### F-1（阻塞）　`PutOnAsync` 的新判定同时改变了**主背包→装备栏**这条既有路径，未申报，且被两处明文否认

这是 B-2 的正确修法带来的必然副作用，**代码没错，错在没有报**。

`PutOnAsync` 的新判定问的是 `moves.MayOccupySlotAsync(bag.Type, displaced.ItemId, SourceSlotIndex)`。
`bag` 是**源列表容器**，而源列表既可能是 `Avatar` **也可能是 `Main`**——
`Main → Equipment` 是基线上就服务的路径。于是这条路上凭空多了一次此前不存在的判定。

**它不是恒 `Allowed`，我从代码逐段核的：**

- `CanOccupySlot(Main, …)` 直接委托 `CanOccupyMainSlot`（`ItemSlotRuleService.cs:41`）；
- `CanOccupyMainSlot` 只在「槽在该件自己的 band 内」或「槽在快捷区 3..8 且
  `AllowsQuickSlot(kind)`」时为真（`ItemSlotRuleService.cs:64-72`）；
- `AllowsQuickSlot` **排除 kind 5（创造物）**（`:84-86`；`QuickSlotStart/End = 3/8`，`:23-24`）；
- `PvfEquipmentSlots.ByName` 把 **`[creature]`、`[earring]`、`[support weapon]` 三者都映到穿戴槽 24**
  （`PvfEquipmentSlots.cs:50-52`）；
- 创造物**真的**穿在装备容器的槽 24 上、真的走 `Main↔Equipment` 移动——
  `CreatureGoesBackOnTests.A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature`
  拿真档案 + 真库跑通了完整往返，绿着；
- 改动**之前**这条路是 `bag.PutAsync(sourceSlot, displaced.Core)` 直接写，
  而 `StoreBackedItemContainer.PutAsync`（`ItemContainers.cs:388-416`）**不查任何槽位规则**，
  只 `ClassifyAsync` 后落行。**所以以前这次交换是成功的。**

于是这个场景在改动前后不同：

> 角色槽 24 穿着创造物；一件同映到槽 24 的耳环待在主背包**快捷槽 3..8**；玩家把耳环拖到槽 24。
> **改动前**：交换成功，创造物被写进那个快捷槽。
> **改动后**：`DisplacedCannotOccupySlot`（客户端码 `NotMovable` 23）。

**这个场景不是我编的，本仓自己的文档写着它**：`Docs/CreatureDomainDesign.md:236`
——「同一张表写着 `[earring] = 24`——**玩家戴一只耳环就会把 24 槽的宠物顶进背包**」。

**证据分级**（按契约「记载不得写成比证据强」）：
上面除最后一环外全部是**本仓代码/判据读出**并已复核。**未由我实测的一环**是
「耳环的 `ItemKind` 允许它待在快捷槽 3..8」与「创造物的主背包 band 起点是 9」——
前者依据 `AllowsQuickSlot` 的排除集不含普通装备种类，后者依据
`CreatureGoesBackOnTests:47` 自己的注释「the equipment band of the bag, which runs 9 to 64」
以及 `TakeOffAsync` 放行创造物落到槽 9。**我没有查档案取这两个值。**

**但无论这最后一环是否成立，记载都已经错了**，因为交付物在两处**明文否认**这条路径有任何变化：

- 变化清单**第二节「没有第六条」**：「主背包内的移动、**主背包↔装备栏**、以及任何未服务
  列表对的行为**逐值不变**。」
- 变化清单**第八节 R-8**：「`PutOnAsync` **新增一条拒绝**……**今天不可达**，#11 落地即可达。」
  R-8 把这条新拒绝整个安放在装扮栏的故事里，从未提及 `bag.Type == Main`。

**全量判据全绿并不构成反证**——恰恰相反：AC-006 自己写着「本仓吃过这个亏：一次变化清单漏了
13 条新增生产拒绝，根因是**全量判据全绿恰恰意味着没有用例走到那些新路径上**」。
今天没有任何用例跑「从快捷槽穿戴并顶下一只创造物」，所以它绿。**这是同一族失误的当轮复发。**

契约 Constraints 第一条：「被授权改变的可观察行为，只有『涉及装扮栏的移动请求不再被拒为
`ListNotServed`』这一件……**其余任何行为变化都要停下报告**」。这一条落在授权面之外，
**该报而未报**，且被否认。

**要做的**：不必改代码（这次判定本身是对的、也是第一轮要求的）。要做的是
(a) 把 `Main → Equipment` 这一侧写进反向表（R-8 拆成两条，或补一条 R-9），
(b) 撤掉第二节「主背包↔装备栏……逐值不变」那半句，
(c) 把「今天不可达」限定到 `Avatar → Equipment` 那一侧，
(d) 按 Requires Confirmation 的口径把这条越界的行为变化报给所有者。

### F-2（阻塞）　R-7 把两个拒绝码**说反了**，而且换掉了它本该记录的那个场景

反向表新增的 R-7 原文：

> 未绑定容器时，涉及装扮栏的请求过去落在 `ListNotServed → Silent(2)`，
> 现在落在 `NoBoundContainer → NotMovable(23)`。

`ErrorCodeFor`（`MoveItemSpaceSlice.cs:675-690`）逐条列着：

```
ItemMoveStatus.ListNotServed    => MoveItemSpaceResponse.NotMovable
ItemMoveStatus.NoBoundContainer => MoveItemSpaceResponse.Silent
```

**两条映射都写反了。** 正确是 `ListNotServed → NotMovable(23)`、`NoBoundContainer → Silent(2)`。

而且 R-7 记的场景也不是 N-1 报的那个。我自己把 `HandleAsync`（`:282-312`）两种情形都走了一遍：

| 情形 | 基线 | 现在 | 客户端码 |
|---|---|---|---|
| **未服务列表对**（如 个人货柜→主背包）且主背包未绑 —— **这是 N-1 报的** | `NoBoundContainer` | `ListNotServed` | **`Silent(2)` → `NotMovable(23)`** |
| 涉及装扮栏的请求，主背包已绑而装扮栏未绑 —— **R-7 写的** | `ListNotServed` | `NoBoundContainer` | **`NotMovable(23)` → `Silent(2)`** |

R-7 取了第二种情形的**状态名**，配上第一种情形的**方向**，两边都对不上。
读者据此会得出**恰好相反**的客户端码结论，而这一行的存在理由正是「把漏报的那条拒绝码变化补上」。
（两种情形生产都不可达——入场绑定失败时 R-1 已抛——但不可达不是可以说反的理由。）

### F-3（阻塞）　N-4 与 N-6 被标为「已改」，实际**一个字都没改**

第八节的非阻塞处置表写着：

| 声称 | 实际 |
|---|---|
| N-4 `NoBoundContainer` 文档陈旧 → **已改** | `MoveItemSpaceSlice.cs:176` 仍是 `/// <summary>The character is not bound to a main container.</summary>`。`git show 7147d2e9:…` 取基线同一行，**逐字符相同**；`git diff` 对该文件搜 `bound to a` **零命中**。**未改。** |
| N-6 后者（`behavior-change-list.md` 未被 `artifacts` 声明）→ **已补** | `sdd-execution.yaml` 的 `artifacts` 仍只有 `spec` / `result` / `change_review` / `completion_evidence`；全文件 `grep behavior` 只命中 `done_when` 里的英文单词。**未补。** |

两条本身都是非阻塞的小事；**把没做的事写成做了**不是小事，而且下游的 verification 阶段会照着
这张表勾。这是第三处「修一句错话时写下另一句错话」。

### F-4（非阻塞，但是新引入的假话）　两处新增判据注释与它们自己的夹具不符

**(a) `A_costume_coming_off_does_land_in_the_avatar_list` 的注释说「Both pieces are costumes」——假的。**
它调 `Build(ordinaryInAvatarListAt: AvatarSlot, wornAt: CoatSlot)`，而 `Build` 里
`worn.Seed(slot, wornIsOrdinary ? OrdinaryTemplate : CostumeTemplate)` 的 `wornIsOrdinary` 默认 `false`。
所以**装扮栏里那件是普通外套 `10018`，穿着的那件才是时装 `413500000`**——两件不同类。
用例**本身没问题**：名字（「一件脱下来的时装确实落进装扮栏」）与它实际断言的事完全相符，
也确实是那次新判定的正面对照（`MayOccupySlotAsync(Avatar, 413500000, 0)` 答 `Allowed`），
M4 下它保持绿也说明它不是把 B-2 的判定重复钉一遍。**只有那一句注释是假的。**
（它不是恒真：若装扮栏规则改成一刀切拒绝，它会红。）

**(b) `The_avatar_list_turns_away_what_is_not_a_costume` 的注释说「slot 70 would be allowed and
slot 7 refused」——后半句假的。** 快捷槽是 3..8（`ItemSlotRuleService.cs:23-24`），药水 kind 2
不在 `AllowsQuickSlot` 的排除集里，所以**主背包规则下槽 7 同样是放行的**。
我的 M5 实测：`targetSlot: 7` 与 `targetSlot: 70` **两条都红**。
这一句把判据说得比实际**弱**（两条都有判别力，注释只认一条），
方向上不至于让人高估证据，但它是一句可证伪的假话。
接下来那句「fails on the first of them」在 `InlineData(7), InlineData(70)` 的书写顺序下也读不通。

### F-5（非阻塞）　N-5 的答复覆盖不到 AC-009 问的那件事

第八节称 N-5「已改：`MovingInsideTheAvatarListTests` 全部经 `InUseCaseAsync`，无边界时容器直接抛
（`StoreBackedItemContainer.WriteAsync`），这条现在有真实依托」。
前半属实（`InventorySide.InUseCaseAsync:203-218` 开 lease 再 commit；
`ItemContainers.cs:788-792` 无 ambient session 即抛）。
但 **AC-009 问的是「一次*跨容器*移动的两端在同一事务边界内结算」**，
而 `MovingInsideTheAvatarListTests` 全部是**栏内**移动，一个容器。
它证明的是「真容器的写必须有边界」，不是跨容器两端同生同灭。
AC-009 本来就允许「用既有的事务判据形态」，靠 `CommandRegistrationTests` 钉住
`MoveItemSpaceCommand` 注册为 `Transactional` 也说得过去——**但那条既有判据仍然没有被指名**，
N-5 的诉求（「读者无从复核」）没有被满足，只是换了一个覆盖不到的依托。

### F-6（非阻塞）　T-2 的「既有三个调用方」是错的基数

实际 `BindMain` 调用点 **17 处，分布 5 个文件**（清单见第二节 B-1）。
承载意思的那半句（「一处未改」）**属实**，错的只是数目。
契约禁的是「读者无法证伪的基数」，这条可证伪且为假。

### F-7（非阻塞）　`TakeOffAsync` 的文档仍只说「bag」

`:478` 起的那段现在归位正确，但正文仍写「into the **bag** slot the player aimed at」
「The **bag** slot is judged by…」，而本轮之后落点也可能是装扮栏。
与 N-4 同一族的陈旧措辞，本轮未触及（不是整改引入的）。

---

## 四、第一轮已通过部分：**没有被破坏**（自己重核）

**放行面仍恰好是那四类——自己重新穷举一次。** 按 `HandleAsync:282-312` 的分支顺序，
`CarriedListFor` 只对 `Main(0)`、`Avatar(1)` 非空，被服务的有序列表对是且只有 8 对：

| 分支 | 有序对 |
|---|---|
| `PutOnAsync` | `Main→Equipment`、`Avatar→Equipment` |
| `TakeOffAsync` | `Equipment→Main`、`Equipment→Avatar` |
| `CarryAcrossAsync` | `Main→Avatar`、`Avatar→Main` |
| `MoveToAsync` | `Main→Main`、`Avatar→Avatar` |

`Equipment→Equipment` 落在 `source is null` → `ListNotServed`（两端 `CarriedListFor` 皆空）。
基线服务 3 对，净增 5 对，**与契约 In 段三行逐条对应，一对不多**。
`A_list_this_server_does_not_serve_is_still_refused` 的 5 个 `MemberData` 覆盖
源/目的/两端三种形态，在 M4、M5 两次变异下均保持绿（说明它们没有被这轮改动误伤）。
M1 本轮未重跑（第一轮已亲手复现 5 红 / 574 绿，`CarriedListFor` 与那 5 条判据本轮均未改动）。

**`CanOccupyMainSlot` 一字未改——重核。** `git diff 7147d2e9 -- ItemSlotRuleService.cs | grep -c "^-"`
= 1，那一行是 `--- a/…` 文件头，**零行被删除**；方法体逐行与基线相同。
其既有判据的期望值一处未动。

**字节锚仍不在 diff 里。** `git diff --name-only` 的 9 个文件中无任何编码器、
无 `SelectCharacterAcceptedTranscriptTests.cs`；Protocol 套件 581 条全绿。

**架构判据仍全绿且未被放宽。** `CommandHandlerArchitectureTests.cs` /
`NotificationHandlerArchitectureTests.cs` 不在 diff 内，随 Application 1070 条全绿。

**`ErrorCodeFor` 仍逐条列举**（`:675-690`，11 个分支 + 两条 `throw`），未改成 fallthrough；
该方法不在 diff 内。

**分层仍合法。** diff 里没有任何 `.csproj`；`Data → Abstractions` 的引用在基线上就存在。

**既有判据无一处期望值被改写。** 唯一被改的既有判据文件
`SelectCharacterEntryLifecycleServiceTests.cs` 的 diff 共 5 个 hunk：4 处夹具各补一行
`InventoryContainerState(Avatar, 0, [])`、1 处用例改名、末尾新增注释与 3 行断言。
`Assert.Equal(loaded.Parameter, bound.Shape.Parameter)`、`Assert.Equal(156, …)` 等原样保留，
**没有任何断言被删除或放宽**。N-2 的改动（`TakeOffAsync` 两处拒绝带上 itemId）
**没有波及任何既有判据的期望值**——测试侧的 diff 只有上述两个文件，均与 N-2 无关。

**N-3 已按诉求处理**：`The_main_list_answers_the_same_either_way` 的注释现在只声称
「两者是一份实现，具名那个调用主背包那个」，不再声称它证明了主背包规则的内容。**属实。**

**N-2 已改属实**：`TakeOffAsync` 的 `NotWearable`、`ItemCannotOccupySlot`、
`DisplacedCannotOccupySlot` 三处 `Refused(…)` 现在都带 `piece.ItemId`。

---

## 五、逐条契约判定（本轮我自己的证据）

| | 判定 | 我的证据 |
|---|---|---|
| SC-1 | **达成** | `SelectCharacterEntryInventoryDomainService.cs:73-80` 取 avatar 并入 `[main, equipment, avatar]`（`:84`）；`Entry_binds_the_character_to_its_own_main_equipment_and_avatar_containers:245-252` 断言已绑、类型正确、不可移除。M2 本轮未重跑（第一轮已核，绑定代码与该判据本轮均未改动） |
| SC-2 | **达成**（第一轮未达成，本轮补上） | `MovingInsideTheAvatarListTests` 6 条跑真 `PersistentItemContainer` + 真 SQLite + 真档案，三种场景各一条且**回读存储**；自拟 M5 证明其中 4 条能抓住「容器退回读主背包 band」，Protocol 套件一条也抓不住 |
| SC-3 | **达成** | `A_costume_moves_from_the_avatar_list_into_the_bag` / `..._from_the_bag_back_into_the_avatar_list` 两向各一条，两端均回读断言 |
| SC-4 | **达成**（第一轮「一半有缺」，N-2 修好了那一半） | 正向 `Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list` 钉 `NotWearable` + `413500000`；反向 `Taking_something_off_into_the_avatar_list_reaches_that_list` 钉 `Moved` + itemId；`TakeOffAsync` 的拒绝现在也带 itemId |
| SC-5 | **达成** | `AvatarListHasASlotRuleOfItsOwnTests` 25 条 + `MovingInsideTheAvatarListTests` 的区间/种类各一条；M3 本轮未重跑（第一轮已核，规则本轮未改动） |
| SC-6 | **达成** | 第四节的穷举：8 对被服务，净增 5 对，与授权逐条对应；5 条反向判据在两次变异下均未误伤 |
| SC-7 | **达成** | 自跑 2334 / 2 / 0，退出码 0；字节锚文件不在 diff、Protocol 581 全绿 |
| SC-8 | **未达成** | F-1（未申报且被明文否认的既有路径行为变化）、F-2（拒绝码说反）、F-3（两条「已改」其实没改）、F-4（两句新引入的假注释）、F-6（错基数） |

**Spec NA-001 / 契约 SC-4 的「做完之后仍被拒为 `NotWearable`」**：本轮判据明确钉住了，
且日志带得出 itemId。**这是要求的状态，不是未完成**——本评审不把它算作任何缺陷。

---

## 六、明确表态

**A. 还有没有「读者会据此做出错误判断」的实质性不实？**

**有，三条。**

1. **F-1** —— 读者读第二节会认定「主背包↔装备栏逐值不变」、读 R-8 会认定新拒绝「今天不可达」。
   两者都不成立：那次判定在 `Main → Equipment` 上同样生效，且有一条本仓文档自己写着的
   真实交互（耳环顶掉 24 槽的创造物）落在它的射程内。这还是一条**越出授权面**、
   按契约应当停下报告的行为变化。
2. **F-2** —— 读者读 R-7 会把 `ListNotServed` 与 `NoBoundContainer` 的客户端码记反，
   且会以为 N-1 报的那个场景已被记录（并没有）。
3. **F-3** —— 读者（以及下游 verification 阶段）会认为 N-4、N-6 已经处理，实际一字未动。

F-4 / F-5 / F-6 / F-7 属于「读了会略微误解、但不会据此做出错误决策」的档次，列为非阻塞。

**B. 是否只剩措辞、编号、可过期计数？**

**不是。** F-1 是一条未申报的、可达的既有路径行为变化，F-2 是把行为说反，
F-3 是把没做的事写成做了——三者都超出措辞层面，所以**没有**判 `approved`。
但请注意本轮判定的**性质**：**三条阻塞都不要求改一行生产代码**。
B-1 / B-2 / B-3 的实现是对的，判据是硬的，两次变异我都亲手复现过。
要改的是变化清单第二节、第八节 R-7 / R-8 与非阻塞处置表，加上 N-4 / N-6 那两处真正的一行改动，
以及按 Requires Confirmation 把 F-1 报给所有者。

**C. SC-1..SC-8 是否全部达成？**

**SC-1..SC-7 全部达成**（逐条证据见第五节；SC-2 与 SC-4 是本轮由「未达成 / 一半有缺」转为达成的）。
**SC-8 未达成**，理由见第五节末行。

---

## 七、本轮实际跑过的命令

```
tasklist | grep -i Dfo.Server.Host                                  （无命中）
git status --short / git rev-parse HEAD / git diff / git diff --stat / git diff --name-only
git show 7147d2e9:Server/src/.../MoveItemSpaceSlice.cs | grep -n "not bound to a"
git diff 7147d2e9 -- Server/src/.../ItemSlotRuleService.cs | grep -c "^-"
grep -rn "FindContainer\|GetContainer\|\.Register(" Server/src --include=*.cs
grep -rn "BindMain\|BindList" Server/tests --include=*.cs
grep -rn "earring\|support weapon" Server Docs Document -i
sha256sum / sha256sum -c                                            （备份 1 次、还原核对 3 次）

PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
    ① 开工基线            → 2334 / 2 / 0，退出码 0
    ② M4（删去 PutOnAsync 新判定 7 行）→ 失败 1，唯一红
                            An_ordinary_piece_coming_off_does_not_land_in_the_avatar_list
    ③ M5（DecideMoveAsync 两处改回 CanOccupyMainSlot）→ 失败 4，全部在 MovingInsideTheAvatarListTests
                            （含 targetSlot: 70 与 targetSlot: 7）
    ④ 收工复跑            → 2334 / 2 / 0，退出码 0
PVF_ARCHIVE_PATH=... dotnet test Server/tests/Dfo.Server.Protocol.Tests/... --nologo   （M4 先做的单套件确认）
```

变异一律用 `python` 二进制读写替换（保持无 BOM / LF 不变），用完立刻 `cp` 回备份并
`sha256sum -c`，三行全 `OK`；**全程未使用 `git checkout --` / `git restore` / `git stash`**。
未触碰主检出、`.worktrees/client-debug-lab`、`.worktrees/entries-are-what-the-client-reads`；
`E:/Project/DNF/ServerS4A21` 本轮未读（第一轮已逐行核过上游读数，本轮上游相关代码未改动）。
除本节所在的评审文件外，**未修改任何文件**。

---

# 第三轮：二次整改后复审（scope = change）

独立复核（fresh context）。**未采信执行方任何自评，也未采信第一、二轮评审的任何结论**——
凡本轮未自行复核的地方都注明「前轮已核、本轮未重跑」。所有数字自跑自数。

工作区 `.worktrees/the-avatar-bag-is-a-container-too`，基线 `7147d2e9`，未提交。
复审期间对生产文件做过两次变异（**M6、M7，均为本轮自拟**），每次用完立刻用文件级备份还原并
`sha256sum -c`，两行全 `OK`；**未使用任何 git 还原类命令**。
收工时 `git status` 为 9 改 + 3 个未跟踪判据文件 + 1 个未跟踪任务目录，与开工时逐行一致。

**判定：`changes_required`。**

前两轮的三条阻塞 F-1 / F-2 / F-3 的**实质**已经解决，**生产代码本轮无可挑剔**：
`PutOnAsync` 逐语句回到基线、放行面仍恰好是授权的四类、全量 2334 / 2 / 0。
**新的两条阻塞仍然全在记载一侧，且都不要求改一行生产代码。**
其中一条是第三轮自己引入的一条假不变式，
另一条是**「把没做的事写成做了」在第三轮的第三次复发——而第三轮正是为修这个而开的**。

---

## 一、判据（自跑）

跑前 `tasklist | grep -i Dfo.Server.Host` **无命中**（退出码 1）。

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| 项目 | 通过 | 跳过 |
|---|---:|---:|
| Pvf | 60 | 0 |
| Networking | 52 | 0 |
| Protocol | 581 | 0 |
| Persistence | 329 | 0 |
| Infrastructure | 236 | 2 |
| Host | 6 | 0 |
| Application | 1070 | 0 |
| **合计** | **2334** | **2** |

失败 0，退出码 0。两条跳过是 `A_missing_archive_*`（档案确实加载了）。
与执行方所报的 2334 / 2 / 0 一致。基线 2289 → **+45**，逐项目 Protocol +14、Application +31，
与三个新文件（`AvatarListHasASlotRuleOfItsOwnTests` 25、`TheAvatarBagIsServedTests` 14、
`MovingInsideTheAvatarListTests` 6 = 45）逐项吻合。**SC-7 达成。**

无偶发失败，无需按既知偶发形态（`ObjectDisposedException: 'SQLitePCL.sqlite3'`）判别。

---

## 二、F-1 / F-2 / F-3 与各非阻塞的处置判定

### F-1 **已解决**（代码），但它带来的新记载有问题，见第三节 **G-1**

**那次判定真的删了，而且 `PutOnAsync` 逐语句回到基线——我自己比对的，不是看执行方的话。**
把基线与当前的 `PutOnAsync` 各自剥掉空行与 `///` 注释后逐行对照：

```
git show 7147d2e9:.../MoveItemSpaceSlice.cs | sed -n '/PutOnAsync/,/Taking a piece off/p' | grep -v 空行 | grep -v 注释
sed -n '/PutOnAsync/,/Carrying a thing/p'   .../MoveItemSpaceSlice.cs | grep -v 空行 | grep -v 注释
```

两侧**逐行逐字符相同**（29 行可执行语句，含 `worn` / `piece` / `slot` / `displaced` 四个局部与
两条 `Refused(..., piece.ItemId)`）。diff 里该方法体内唯一的改动是**删掉了一个空行**
（`ReadSlotAsync` 与 `worn.PutAsync` 之间），无语义。

**「主背包→装备栏」因此确实回到基线行为。** 我没有重跑第二轮的耳环/契约兽场景去「看它不再被拒」——
**不必跑**：可执行语句逐字符相同，行为相同是编译器保证的，跑一遍只能证明同一件事而且更弱。
（`HandleAsync` 那一侧也核了：`source=Main, dest=Equipment` 走
`FindContainer(CharacterId, Main)`，null 则 `NoBoundContainer`，否则 `PutOnAsync(command, from, ...)`，
与基线开头那两句 `FindContainer(Main)` / `Refused(NoBoundContainer)` 逐值等价。）

**反向表的回头改也核了**：第三节标题「共 7 条」、R-8 已删、表内 R-1..R-7 七行，
表头注明「曾经有过一条 R-8……它在第二轮被删掉了」。计数与内容自洽。

### F-2 **已解决**

R-7 第三版：「涉及装扮栏的请求，在装扮栏**未绑定**时，过去落在 `ListNotServed → NotMovable(23)`，
现在落在 `NoBoundContainer → Silent(2)`」。
我自己读 `ErrorCodeFor`（`MoveItemSpaceSlice.cs:684-690`）：

```
ItemMoveStatus.ListNotServed    => MoveItemSpaceResponse.NotMovable
ItemMoveStatus.NoBoundContainer => MoveItemSpaceResponse.Silent
```

**两条映射与 R-7 第三版一致，方向也与该场景一致。** 拒绝码不再说反。
（`ErrorCodeFor` 仍逐条列举 11 个分支 + 两条 `throw`，未改成 fallthrough；该方法不在 diff 内。）

**残留一处**，见第三节 G-4：第八节 N-1 那一行仍写着「已补进反向表（见下 R-7）」，
而 R-7 记的是**另一个**场景，N-1 自己那条（未服务列表对 + 主背包未绑：
`NoBoundContainer → ListNotServed`，即 `Silent(2) → NotMovable(23)`）**仍然不在反向表里**。

### F-3 **一半已解决，一半在同一张表里第三次复发**

**真的改了的两处（自己核过）：**

- **N-4**：`MoveItemSpaceSlice.cs:176` 现在是
  `<summary>The character is not bound to one of the containers the request named.</summary>`
  外加一段 `<remarks>`。**这一段在 `git diff 7147d2e9` 里，是真的新写的。**
- **N-6 后者**：`sdd-execution.yaml` 的 `artifacts` 现在含
  `behavior_change_list: "./behavior-change-list.md"`。**真的补了。**
  （`result: "./spec-result.md"` 仍指向不存在的文件、`behavior_change_list` 仍不在任何 stage 的
  `writes` 里——执行方明写「前者不改，是 Profile 正常形态」，本评审接受这个说法，非阻塞。）

**但同一张表里新出现了三处「已处理」而实际未处理**，见第三节 **G-2**。

### F-6 **已解决**（自己数的）

`grep -rn "\.BindMain(" Server/tests --include=*.cs | wc -l` = **17**，
`grep -rln` 得 **5 个文件**（`DraggedAvatarStaysWholeTests`、`GrantedAvatarIsWholeTests`、
`PickedUpAvatarStaysWholeTests`、`SplitPileTests`、`SqliteEntryInventoryMutationRepositoryTests`）。
T-2 现在写「17 处调用点（5 个文件）」并给出 grep 口径，与我数的一致。
`git diff --name-only` 不含这 5 个文件，「一处未改」属实。

### F-4 / F-5 / F-7 **处置不实**，并入 G-2

- **F-4(a)**（`A_costume_coming_off_does_land_in_the_avatar_list` 的「Both pieces are costumes」）
  ——该判据已随 F-1 整改被删除，问题**随之消失**，属实。
- **F-4(b)**（`MovingInsideTheAvatarListTests` 的「slot 7 refused」）——**一字未改**，见 G-2。
- **F-5**（AC-009 所依赖的既有事务判据仍未被指名）——**未处理**，见 G-2。
- **F-7**（`TakeOffAsync` 文档仍只说 bag）——**一字未改**，见 G-2。

### 第一、二轮其余非阻塞

N-1 见 G-4；N-2 / N-3 本轮未再改动，第二轮已核；R-5 的「不可达未写判据」仍如第七节所述，属实。

---

## 三、本轮新发现

### G-1（阻塞）　`PutOnAsync` 的新「不变式」被**同一段注释的下一段自己推翻**，而它是这条无判定写入路径的全部辩护

`MoveItemSpaceSlice.cs:364-371`（加粗是原文的 `<b>`）：

> The reason that does hold: **the worn slot decides the class of what is in it.** A piece may only
> be put on the slot its own archive entry names, so what is already sitting there was admitted by
> that same rule and names the same slot. Both ends of the swap are therefore the same sort of
> thing, **and a list that let one of them out will take the other back.**

`MoveItemSpaceSlice.cs:372-377`，**紧接的下一段**：

> ...the source may be the bag, where it would have refused a swap that works today -- **a creature
> displaced into a quick slot, which the bag's own band rule turns away.** That is a real defect of
> its own...

**后一句就是前一句的反例，两句相隔六行，注释没有说它们是同一件事的两面。**
一个来读「这里要不要补一次判定」的人，从同一段注释里拿到互相矛盾的两条指示。

**反例是本仓基线上就成立的事实，我自己核的：**

- `PvfEquipmentSlots.ByName`（`Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs:36-53`）
  把 `[earring]`、`[support weapon]`、`[creature]` **三个名字都映到槽 24**——
  `git show 7147d2e9:.../PvfEquipmentSlots.cs` 确认 `[creature] = 24` **基线上就在表里**；
- `ItemWorldClass`（`Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs:35-44`）
  是 `Interchangeable=0 / Equipment=1 / Avatar=2 / Creature=3`；
  契约兽的 `WorldClass=Creature`（变化清单第九节自己测得 `63000 => kind=5, WorldClass=Creature`），
  耳环是普通装备。
- **所以「the worn slot decides the class of what is in it」是假的**：槽 24 上站的东西可能是
  `Equipment` 也可能是 `Creature`。**「a list that let one of them out will take the other back」也是假的**
  ——`CanOccupyMainSlot(契约兽, 快捷槽 3..8) = False`（`AllowsQuickSlot` 排除 kind 5，
  `ItemSlotRuleService.cs:84-86`）。

**装扮槽那一半的论证，其证据证明的是另一件事。** 注释与判据都写：

> Costume slots are `0..11` and no ordinary piece names one -- `PvfEquipmentSlots` starts at twelve
> -- so what comes off a costume slot is a costume.

我自己顺着查了一遍取槽的唯一通路：
`PutOnAsync` → `moves.WornSlotForAsync`（`MoveItemSpaceSlice.cs:392`）
→ `ItemMovementDomainService.WornSlotForAsync:11-12`
→ `ItemPlacementRuleService.WornSlotForAsync:28-29`
→ `IEquipmentSlotDefinitionSource.GetAsync`
→ `PvfEquipmentSlotDefinitionSource.cs:76`，**返回值只可能来自 `ByName` 或 `null`**。
`ByName` 的最小值是 **12**。而 `PutOnAsync:396` 要求 `slot.Value == DestinationSlotIndex`。

**因此今天不是「装扮槽只可能站着时装」，而是「装扮槽上永远站不了任何东西」。**
那句话是**空真**：从来没有东西从装扮槽上被顶下来过。
无判定的写回今天安全，靠的是这份空真，不是靠注释所说的机制。
**而 issue #11 要做的事，恰恰就是给 `0..11` 补上键**——它一落地，
「no key falls in 0..11」这个前提就**按构造被摧毁**，同时这条路第一次变成天天走的路。
注释把一条会被 #11 亲手推翻的前提写成了一条经得起时间的不变式。

**变化清单第九节（`behavior-change-list.md:355-358`）把同一句写成了「本轮实证」**：

> 本轮实证：`PvfEquipmentSlots.ByName` **从 12 开始，没有任何键落在 0..11**，
> 所以**装扮槽只可能站着时装**，而时装正是装扮栏收的东西。

实证的部分（表从 12 开始）我复核属实；**「所以」之后的推论不是那份实证支持的结论**。
这正是契约末条「记载不得写成比证据强」。

**判据也没有钉住它所声称的那件事**（同一根因，故并入本条）。
新增的 `What_comes_off_a_costume_slot_goes_back_into_the_avatar_list`
（`TheAvatarBagIsServedTests.cs:188-203`）用的是
`Build(ordinaryInAvatarListAt: AvatarSlot /*=0*/, wornAt: CoatSlot /*=14*/)`：

- 它移动的目的槽是**装备栏槽 14**（`[coat]`），**不是** `0..11` 里的任何一个装扮槽；
- `wornIsOrdinary` 默认 `false`，所以槽 14 上被它塞进去的是**时装 `413500000`**——
  而按它自己 remarks 里的那条不变式，**时装名不出任何穿戴槽，永远站不到槽 14 上**。
  **这个夹具构造的正是它所辩护的不变式宣称不可能的状态。**
- 它的 remarks（`:176-179`）写「A costume slot is the case worth pinning......
  so nothing but a costume can be standing there to come off」——**这条判据一次也没碰过装扮槽**，
  而且**碰不到**：任何瞄准 `0..11` 的 `PutOnAsync` 都会先在 `slot.Value != DestinationSlotIndex` 上被拒。
- 讽刺的是它 remarks 末尾写「An earlier draft asserted the opposite for an ordinary piece stranded
  in the avatar list. That state is one the store permits and no move produces」——
  **删掉那条判据的理由，逐字适用于它自己的夹具。**

**这条判据本身有判别力，只是判的不是它名字上那件事**——本轮变异 **M7** 证明：
把 `CarriedListFor` 的 `AvatarListType => InventoryContainerType.Avatar` 改成 `=> null`，
Protocol **红 8 条**、其余六个套件全绿，`What_comes_off_a_costume_slot_...` 在红的 8 条之列。
它钉住的是「`Avatar → Equipment` 被分发到 `PutOnAsync` 且被顶下来那件写回装扮栏」——
一件真事，也确实是本轮的成果，**只是与它的名字和 remarks 说的不是同一件事**。

**另一条新判据 `A_piece_may_not_be_worn_in_a_slot_its_archive_entry_does_not_name` 是真的、有判别力的，
但它钉的规则在基线上已经被钉过两次。** 本轮变异 **M6**：把 `PutOnAsync:396` 的
`if (slot.Value != command.DestinationSlotIndex)` 短路成永不成立，跑全量 → **恰好红 3 条**：

```
Protocol   TheAvatarBagIsServedTests.A_piece_may_not_be_worn_in_a_slot_its_archive_entry_does_not_name   ← 本轮新增
Protocol   RefusedEquipIsRecordedWithItsTemplateTests.A_piece_aimed_at_the_wrong_slot_is_recorded_...    ← 基线既有
Application CreatureGoesBackOnTests.A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused            ← 基线既有
（Protocol 失败 2 / 通过 579；Application 失败 1 / 通过 1069；其余五个套件全绿）
```

所以「新增两条判据钉住不变式」这个说法里：一条钉的是别的事（名字与内容不符），
另一条钉的是**基线上已经钉过两遍的半条规则**。**不变式里真正新的、真正承重的那一半
（「所以装扮槽上下来的一定是时装」）没有任何判据，也无法有——因为它今天是空真。**

**要做的**（不改一行生产代码）：

1. `MoveItemSpaceSlice.cs:364-371` 把「the worn slot decides the class of what is in it」
   与「a list that let one of them out will take the other back」这两句**删掉或改成真话**。
   真话是窄的、也够用：*今天没有任何物品名得出 `0..11` 里的槽（`ByName` 从 12 开始），
   所以源列表是装扮栏时这条写回根本到不了；源列表是主背包时这段代码逐语句就是基线。
   一般形式的「两端同类」在槽 24 上已经不成立（`[earring]` / `[creature]` 同槽异类），
   这也正是下一段所说的那个缺陷。* 并写明 **#11 会推翻「`0..11` 无键」这个前提**，
   届时这里必须重新判断。
2. `TheAvatarBagIsServedTests.cs:174-186` 的 remarks 改成它实际做的事
   （`Avatar → Equipment` 的分发与写回，槽 14，M7 是它的证据），
   或者把名字改成与夹具相符；不要再声称它钉住了 `0..11`。
3. `behavior-change-list.md:355-358` 把「所以装扮槽只可能站着时装」降到证据的强度。

### G-2（阻塞）　第九节的非阻塞处置表里有**三处**「已处理」是假的——而第三轮正是为修 F-3（同一形态）而开的

| 声称（`behavior-change-list.md`） | 我核到的实际 |
|---|---|
| `:391` F-4 —— 「`MovingInsideTheAvatarListTests` 里『槽 70 才有判别力』的说法**已由评审 M5 证伪并改正**」 | `MovingInsideTheAvatarListTests.cs:110-114` **一字未改**，仍写着「slot 70 would be allowed and **slot 7 refused**」与「a container gone back to reading the main band **fails on the first of them**」。**两句都是假的**：药水 kind 2 不在 `AllowsQuickSlot` 的排除集里（`ItemSlotRuleService.cs:84-86`），快捷区是 3..8（`:23-24`），所以主背包规则下**槽 7 同样放行**，两个 `InlineData` 都有判别力——第二轮 M5 已实测两条都红。**「已改正」是假的。** |
| `:392`「F-5 / F-7 措辞，随本节改正」——**F-5** | AC-009 所依赖的既有事务判据**仍未被指名**：`grep -rn "CommandRegistrationTests\|Transactional" behavior-change-list.md spec.md` → **零命中**。F-5 的整条诉求（「读者无从复核」）原样留着。**未处理。** |
| `:392`「F-5 / F-7 措辞，随本节改正」——**F-7** | `MoveItemSpaceSlice.cs:488-499` `TakeOffAsync` 的文档**一字未改**，仍是「into the **bag** slot the player aimed at」「The **bag** slot is judged by...」，而本轮之后落点也可能是装扮栏。**未处理。** |

第二轮判 F-3 阻塞的理由原文是「两条本身都是非阻塞的小事；**把没做的事写成做了不是小事**，
而且下游的 verification 阶段会照着这张表勾」。**同一句逐字适用于这三条。**
第九节自己在 `:385` 写「**这是第八节那张处置表自己出的错**，与上一件成果里
『第九节把没做的事写成已改』是同一形态」——**然后在同一节的下一张表里把它又犯了三次。**

按第二轮判 F-3 的同一把尺子，本条阻塞。**修法是删掉这三处声称，不是再写三段解释。**

### G-3（非阻塞）　第九节的「第三轮判据」表是空的，而且它给出的差值是错的

`behavior-change-list.md:397-400`：

| | 值 |
|---|---|
| 现在 | **见下方复跑** |
| 与第二轮的差 | **判据净减 1 条（删 1 加 2 减 1，见 F-1）**；生产代码净减一次判定 |

- **「见下方复跑」下方什么都没有**——文件在第 400 行结束（`wc -l` = 400）。
  **交付物里没有记下本轮的判据结果。**（我自己跑到了 2334 / 2 / 0，所以 SC-7 这个**条件**是达成的，
  缺的只是记录。）
- **「净减 1 条」是错的，实际净变化为 0。** 第二轮 2334，本轮 2334。
  被删的是**两条**——`An_ordinary_piece_coming_off_does_not_land_in_the_avatar_list` 与
  `A_costume_coming_off_does_land_in_the_avatar_list`（后者的消失只在 F-4 行里被顺带提了一句
  「那条判据已删」，F-1 那段只说「删掉」一条）——新增两条，净 0。
  `TheAvatarBagIsServedTests` 现为 **9 `[Fact]` + 1 `[Theory]` × 5 `MemberData` = 14 条**（我自己数的），
  与第二轮的 14 相同，正是净 0 的直接证据。
- 「删 1 加 2 减 1」这个括号本身读不通。

第五节 S-5「新增 3 个文件共 45 条」与第六节「+45」仍然**正确**（因为净变化是 0），未受牵连。

### G-4（非阻塞）　N-1 报的那条拒绝码变化仍不在反向表里，而处置表说它在

`behavior-change-list.md:294` 写「N-1 ...... **属实，已补进反向表**（见下 R-7）」。
但 R-7 第三版记的是**另一个**场景（装扮栏未绑：`NotMovable(23) → Silent(2)`）。
N-1 自己那条——**未服务列表对 + 主背包未绑**，基线 `NoBoundContainer`（`Silent 2`）、
现在 `ListNotServed`（`NotMovable 23`）——**反向表 R-1..R-7 七行里一行都没有**。
我自己从 `HandleAsync:286-307` 走了一遍确认这条变化是真的：
`CarriedListFor` 两端皆 null 时**在查任何绑定之前**就返回 `ListNotServed`，
而基线第一句就是 `FindContainer(Main)`。生产不可达（入场绑定失败时 R-1 已抛）。
**事实很小，但「已补进反向表」这句话是假的**——与 G-2 同族，只是它是第二轮遗留而非第三轮新造。

### G-5（非阻塞）　第五节 C-4 与第八节 B-2 因这次回退而变成假话，未改

- `:143` **C-4** 的处置列仍写「已改写注释并**把那次判定加回来**，见第八节 B-2」。
  第五节是一张**当前状态**表（C-1..C-3 都是当前状态），而那次判定已经不存在了。
  同一份文档的第三节表头已写明 R-8「在第二轮被删掉了」——**两处互相矛盾**。
- 第八节 B-2 整段（`:239-268`）仍写「**已改**：加上判定，refuse `DisplacedCannotOccupySlot`」、
  「**判据**：`An_ordinary_piece_coming_off_does_not_land_in_the_avatar_list`，
  加一条正面对照 `A_costume_coming_off_does_land_in_the_avatar_list`」、
  「变异 **M4**（去掉那次判定）→ 恰好红这一条」——**判定、两条判据、M4 现在都不存在。**
  第八节是历史叙事，按顺序读到第九节会被纠正，但**那一段本身没有任何前向标记**。

### G-6（非阻塞）　为「另开 issue」引用的那条本仓记载，其痛点已经不存在

`behavior-change-list.md:371-372` 引 `Docs/CreatureDomainDesign.md:236` 说
「玩家戴一只耳环就会把 24 槽的宠物顶进背包，**然后再也装不回去**」，并断言
「它比我碰到的这一面更大——**真正的痛点是「再也装不回去」**」。

我读了 `Docs/CreatureDomainDesign.md:230-238`：那一段的前提是
「那张表**没有 `[creature]` 键** → `NotWearable`」。
而 `git show 7147d2e9:.../PvfEquipmentSlots.cs` 显示 **`[creature] = 24` 在基线上就在表里**
（并带着一整段解释它为什么被加进去的 `<remarks>`）。
**所以「再也装不回去」这个痛点已经被前一件成果修掉了**，那段文档是陈旧的。
真正**还活着**的那一面，恰恰是执行方自己碰到的那一面——契约兽被顶进快捷槽 3..8，
而 band 规则不让它待在那里——**而被引用的那段文档并没有记载这一面**（它说的是「顶进背包」）。
把缺陷推给「本仓早记着」时，引的是一段记着另一件（且已修好的）事的文字。
**缺陷本身是真的、该另开 issue 也是对的**；错的是它的出处与定性。

### G-7（非阻塞）　第二节「没有第六条」的「逐值不变」与第三节 R-6 互相不符，R-6 的基数也少了一处

第二节写「主背包内的移动、**主背包↔装备栏**、以及任何未服务列表对的行为**逐值不变**」。
`Equipment → Main` 是基线上就服务的路径，而它上面**三处**拒绝现在带上了 `piece.ItemId`
（`MoveItemSpaceSlice.cs:518` `NotWearable`、`:521` `ItemCannotOccupySlot`、
`:535-536` `DisplacedCannotOccupySlot`）。

我核了这件事的可观察面：`ItemId` **只进日志行**（`:648-655`），
应答帧是 `MoveItemSpaceResponse.Failure(message, ErrorCodeFor(result.Status))`，**不含 ItemId**。
所以**线上逐值不变属实，日志逐值不变不属实**——而本件的整个立意就是日志里的 `{ItemId}`。
第三节 R-6 已经披露了这件事（「另有**两处**拒绝改为带上物品号」），
所以第二节与第三节口径不一；且 R-6 说「两处」，**实为三处**（第二轮评审也数到三处）。

同类的小账：S-4 写「三处注释按已查明改写」，本轮之后实际被改写的注释至少是五处
（类注释、`DecideMoveAsync`、`BindContainers`、`PutOnAsync`、`NoBoundContainer`）。

---

## 四、前两轮已通过的部分：**没有被破坏**（本轮自己重核）

**放行面仍恰好是那四类——自己重新穷举一次。** 读 `HandleAsync:280-340`：
`CarriedListFor`（`:269-274`）只对 `MainListType(0)` 与 `AvatarListType(1)` 非空，
分支顺序是 `PutOn` → `TakeOff` → `ListNotServed` → `NoBoundContainer` → `CarryAcross` → `MoveToAsync`。
被服务的有序列表对是且只有 8 对：

| 分支 | 有序对 |
|---|---|
| `PutOnAsync` | `Main→Equipment`、`Avatar→Equipment` |
| `TakeOffAsync` | `Equipment→Main`、`Equipment→Avatar` |
| `CarryAcrossAsync` | `Main→Avatar`、`Avatar→Main` |
| `MoveToAsync` | `Main→Main`、`Avatar→Avatar` |

`Equipment→Equipment` 两端 `CarriedListFor` 皆 null，落在 `:306-307` `ListNotServed`。
基线服务 3 对，净增 5 对，**与契约 In 段三行逐条对应，一对不多**。
**M7（本轮自拟）从反面确认了这个面**：把 `AvatarListType => null`，
Protocol 恰好红 8 条且全部在 `TheAvatarBagIsServedTests`，其余六个套件 1753 条全绿——
说明装扮栏那五对放行**只被这一个文件钉着**，也说明它确实被钉着。

**`CanOccupyMainSlot` 仍一字未改。**
`git diff 7147d2e9 -- .../ItemSlotRuleService.cs | grep "^-"`
只有一行 `--- a/...` 文件头，**零行被删除**。该文件的改动全是新增（`CanOccupySlot`、两个常量、注释）。
`MayOccupySlotAsync(Main, ...)` 与基线 `MayOccupyMainSlotAsync` 逐值等价：
`ItemMovementDomainService.cs:19-29` 除把 `CanOccupyMainSlot(placement, slot)` 换成
`CanOccupySlot(containerType, placement, slot)` 外结构相同，而
`CanOccupySlot(Main, ...)` 是 `=> this.CanOccupyMainSlot(placement, slotIndex)` 直接委托。

**`MovingInsideTheAvatarListTests` 仍在跑真容器。** `:183`
`side.BindList(InventoryContainerType.Avatar, Character, placements: placements)`
→ `InventorySide.BindList` 造 `new PersistentItemContainer(..., new ContainerShape(containerType, parameter),
new SqliteItemDomainStore(this.databasePath), ...)`（`InventorySide.cs` 的 diff 逐行核过）。
4 `[SkippableFact]` + 1 `[SkippableTheory]`×2 = **6 条**，与 S-5 相符。
M5 本轮未重跑（第二轮已亲手复现；`DecideMoveAsync` 与该文件本轮均未改动）。

**字节锚仍不在 diff。** `git diff --name-only 7147d2e9` 过滤 transcript / Architecture / csproj **零命中**；
9 个改动文件里无任何编码器。Protocol 套件 581 条全绿。

**架构判据全绿且未被放宽。** `CommandHandlerArchitectureTests.cs` /
`NotificationHandlerArchitectureTests.cs` 不在 diff 内，随 Application 1070 条全绿。

**分层仍合法。** diff 里没有任何 `.csproj`；`Data → Abstractions` 的引用在基线上就存在
（第一轮已核，本轮 `.csproj` 未动）。新增的 `using Dfo.Server.Data.Inventory;`
出现在 Abstractions / Application 两层，顺方向。

**既有判据无一处期望值被改写。** 测试侧的 diff 只有
`SelectCharacterEntryLifecycleServiceTests.cs`（4 处夹具补一行 + 1 处改名 + 末尾加 3 行断言）
与 `InventorySide.cs`（`BindMain` 委托给 `BindList`，形参与默认值逐项相同）。
两者本轮均未再改动（第二轮已逐 hunk 核过）。

---

## 五、逐条契约判定（本轮我自己的证据）

| | 判定 | 我的证据 |
|---|---|---|
| SC-1 | **达成** | `SelectCharacterEntryInventoryDomainService.cs:73-80` 取 avatar 并入 `[main, equipment, avatar]`（`:84`），缺则抛 `InvalidDataException`；`Entry_binds_the_character_to_its_own_main_equipment_and_avatar_containers` 钉住。M2 本轮未重跑（绑定代码与该判据本轮均未改动） |
| SC-2 | **达成** | `MovingInsideTheAvatarListTests` 6 条经 `BindList(Avatar,...)` 跑真 `PersistentItemContainer` + `SqliteItemDomainStore` + 真档案，三种场景各一条且回读存储 |
| SC-3 | **达成** | `A_costume_moves_from_the_avatar_list_into_the_bag` / `..._from_the_bag_back_into_the_avatar_list`，两端均回读断言；M7 下两条都红，证明它们真走装扮栏分发 |
| SC-4 | **达成** | `Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list` 钉 `NotWearable` + `413500000`；`Taking_something_off_into_the_avatar_list_reaches_that_list` 钉 `Moved` + itemId。**「做完仍被拒为 `NotWearable`」是 SC-4 与 Spec NA-001 要求的状态，本评审不把它算作缺陷** |
| SC-5 | **达成** | `AvatarListHasASlotRuleOfItsOwnTests` 25 条 + `MovingInsideTheAvatarListTests` 的区间/种类各一条；上游读数（`0..209` + `KindAvatar`）第一轮已逐行核对，本轮相关代码未改动 |
| SC-6 | **达成** | 第四节的穷举：8 对被服务，净增 5 对；`A_list_this_server_does_not_serve_is_still_refused` 的 5 个 `MemberData` 在 M6、M7 两次变异下均未误伤 |
| SC-7 | **达成** | 自跑 2334 / 2 / 0，退出码 0；字节锚文件不在 diff、Protocol 581 全绿 |
| SC-8 | **未达成** | G-1（假不变式 + 名不符实的判据 + 「本轮实证」写得比证据强）、G-2（三处「已处理」是假的）、G-3、G-4、G-5、G-6、G-7 |

---

## 六、明确表态

### A. 还有没有「读者会据此做出错误判断」的实质性不实？

**有，两条，都在记载，都不要求改一行生产代码。**

1. **G-1** —— 读 `PutOnAsync` 的注释会认定「穿戴槽决定了站在它上面那件东西的类别，
   所以放得出一端的列表必收得回另一端」。**这是假的**：槽 24 上 `[earring]`（Equipment）与
   `[creature]`（Creature）同槽异类，而**同一段注释的下一段就是它的反例**。
   同时会认定装扮槽 `0..11` 的安全性已被论证并被判据钉住：
   实际上今天**没有任何东西能站上 `0..11`**（`ByName` 从 12 开始，`WornSlotForAsync` 只经它），
   那句话是空真；**而 issue #11 要做的正是给 `0..11` 补键，届时这个前提被按构造摧毁**，
   恰在这条路第一次变成主干的时刻。名为钉住它的那条判据用的是槽 14，一次也没碰过装扮槽。
2. **G-2** —— 读者（以及下游 verification 阶段照着勾的那张表）会认为
   F-4(b)、F-5、F-7 三条已经处理，**实际全部一字未动**。
   这与第二轮判 F-3 阻塞的形态**逐字相同**，且发生在专为修 F-3 而开的这一轮里、
   同一节里、就在自己写下「这是第八节那张处置表自己出的错」的下一张表中。

G-3..G-7 属于「读了会略微误解或找不到对应物、但不会据此做出错误决策」的档次，列为非阻塞。

### B. 是否只剩措辞、编号、可过期计数？

**不是**，所以没有判 `approved`。G-1 是一条被同段自证为假的承重不变式，
G-2 是把没做的事写成做了——两者都不是措辞偏好。

但请注意本轮判定的**性质**，它与第二轮相同并且更极端：
**生产代码本轮没有任何可挑剔之处。** `PutOnAsync` 逐语句回到基线（我逐行比对过），
放行面恰好是授权的四类（我穷举并用 M7 反证过），全量 2334 / 2 / 0（我自跑过）。
**F-1 / F-2 / F-3 的实质全部解决。** 要改的是三段注释、三行处置表、一张判据表。

**我特意没有把这些升级为阻塞**：F-5 / F-7 本身的技术内容、R-6 的「两处 vs 三处」、
S-4 的「三处 vs 五处」、G-3 的「净减 1」、G-6 的出处错、第二节与第三节的口径不一——
这些都是可过期计数与措辞，一律列为非阻塞。G-2 之所以阻塞，
不是因为 F-4(b) / F-5 / F-7 重要，而是因为**「声称已做而未做」这一条本身**已经被判过一次阻塞。

### C. 契约 SC-1..SC-8 是否全部达成？

**SC-1 ~ SC-7 全部达成**，逐条证据见第五节，全部由我本轮自跑或自读得出。
**SC-8 未达成**——变化清单仍有 G-1 / G-2 两条实质性不实，外加 G-3..G-7 五条记账不符。

### D. 第八、九节（自述整改过程）是否已经超出应有价值？

**是。已经超出，而且它本身已经成为本轮两条阻塞里的一条的唯一来源。这是我的结论，不是修辞。**

证据是这份文档自己的账：

- 第八节整段 B-2（30 行）描述的判定、两条判据、M4 变异，**现在一样都不存在**（G-5）；
- 第五节 C-4 与第三节 R-8 表头**互相矛盾**（G-5）；
- 第八节的非阻塞处置表出过 2 次假声称（第二轮 F-3），第九节的非阻塞处置表**又出了 3 次**（G-2）；
- 第九节的判据表**留着一个没填的占位符**，并给出一个错的差值（G-3）；
- 第八节 N-1 行指向的 R-7 记的是别的场景（G-4）。

**规律很清楚：这两节里每修一处，就有约一处新的不实被写进去。** 三轮下来，
**评审提出的每一条阻塞中，只有第一轮的 B-2 是真正的生产缺陷**（而它最终的正确处置是**回退**）；
其余全部是这份自述本身制造的。它现在的边际收益为负。

**建议（供所有者决定，我不改文件）：**

1. **第八、九节整体删除，或压成不超过 10 行的「整改要点」**：
   保留「第一轮发现 `PutOnAsync` 漏一次判定 → 第二轮加上 → 所有者重新定性 → 第三轮删掉，
   `Main→Equipment` 逐语句回到基线；快捷栏 / 契约兽的缺陷另开 issue」这一条主线，
   以及最终判据 2334 / 2 / 0。**过程细节属于评审文件（就是本文件），不属于变化清单。**
   变化清单的价值在第一到第七节——**那七节我本轮逐条核过，除 G-6、G-7 两处小账外全部属实**。
2. **G-1 必须修**，因为它**不在**第八、九节里——它在 `MoveItemSpaceSlice.cs` 的源码注释和
   一条判据的名字里，会随代码一起活下去，而 issue #11 正是会撞上它的那件事。
   **这一条不要「再写一段解释」，要把假话删掉、换成那句窄的真话。**
3. **G-2 / G-3 / G-4 / G-5 用删除来修，不要用新增来修。** 若采纳建议 1，这四条一并消失。
4. 若所有者认为第八、九节的留存价值大于风险，**至少给第八节 B-2 那一整段加一行前向标记**
   （「本节所述的判定与两条判据已在第三轮删除，见第九节 F-1」），成本一行。

**一句话**：本件的**代码**已经可以接受了；卡住它的是一份**关于它自己的记载**，
而那份记载的后半部分正在以每轮一条的速度制造新的错误。**修法是让它变短，不是让它更准确。**

---

## 七、本轮实际跑过的命令

```
tasklist | grep -i Dfo.Server.Host                                （无命中，退出码 1）
git status --porcelain / git log --oneline -3 / git diff 7147d2e9 --stat
git diff 7147d2e9 -- .../MoveItemSpaceSlice.cs
git diff 7147d2e9 -- .../{ItemMovementDomainService,ItemContainers,SelectCharacterEntryInventoryDomainService}.cs
git diff 7147d2e9 -- .../{IItemMovementDomainService,IItemSlotRuleService}.cs
git diff 7147d2e9 -- Server/tests/Dfo.Server.Persistence.Tests/Support/InventorySide.cs
git diff 7147d2e9 -- .../ItemSlotRuleService.cs | grep "^-"                     （只有文件头一行）
git diff --name-only 7147d2e9 | grep -i "transcript|Architecture|csproj"        （零命中）
git show 7147d2e9:.../MoveItemSpaceSlice.cs | sed -n '/PutOnAsync/,/Taking a piece off/p'（剥空行与注释后逐行对照）
git show 7147d2e9:.../PvfEquipmentSlots.cs | grep -n "creature|earring"          （[creature]=24 基线即有）
grep -rn "\.BindMain(" Server/tests --include=*.cs | wc -l                       （17；5 个文件）
grep -rn "WornSlotForAsync" Server/src --include=*.cs                            （唯一通路经 ByName）
grep -rn "CommandRegistrationTests|Transactional" behavior-change-list.md spec.md （零命中 → F-5 未处理）
grep -n "\[Fact\]|\[Theory\]|\[InlineData|\[MemberData" 三个新判据文件             （14 / 25 / 6）
sha256sum / sha256sum -c                                （备份 1 次、还原核对 2 次，全 OK）

PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
    ① 开工基线  → 2334 通过 / 2 跳过 / 0 失败，退出码 0
    ② M6（PutOnAsync:396 的 slot.Value != Destination 短路成永不成立）
                → 恰好红 3 条：A_piece_may_not_be_worn_in_a_slot_its_archive_entry_does_not_name（新增）、
                  RefusedEquipIsRecordedWithItsTemplateTests.A_piece_aimed_at_the_wrong_slot_...（基线既有）、
                  CreatureGoesBackOnTests.A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused（基线既有）
    ③ M7（CarriedListFor 的 AvatarListType => InventoryContainerType.Avatar 改成 => null）
                → Protocol 红 8 条，全部在 TheAvatarBagIsServedTests（含 What_comes_off_a_costume_slot_...），
                  其余六个套件全绿
```

两次变异一律用 `python` 二进制读写替换（保持无 BOM / LF 不变），用完立刻 `cp` 回备份并
`sha256sum -c`，两次均 `OK`；**全程未使用 `git checkout --` / `git restore` / `git stash`**。
未触碰主检出、`.worktrees/client-debug-lab`、`.worktrees/entries-are-what-the-client-reads`；
`E:/Project/DNF/ServerS4A21` **本轮未读**（第一轮已逐行核过上游读数，本轮上游相关代码未改动）。
除本节所在的评审文件外，**未修改任何文件**——`git status` 收工时与开工时逐行一致。

# 第四轮：三次整改后复审（scope = change）

独立复核（fresh context）。**未采信执行方任何自评，也未采信第一、二、三轮评审的任何结论**——
所有事实自跑自读自数；凡引用前轮结论处均注明是我自己重核过的还是原样引用。

工作区 `.worktrees/the-avatar-bag-is-a-container-too`，基线 `7147d2e9`，未提交。
复审期间对生产文件做过**一次**变异（**M8，本轮自拟**），用完立刻用文件级备份还原并
`sha256sum -c`，输出 `OK`；**未使用任何 git 还原类命令**。
收工时 `git status` 为 9 改 + 3 个未跟踪判据文件 + 1 个未跟踪任务目录，与开工时逐行一致。

**判定：`approved`。**

**新增阻塞 0 条。** G-1..G-7 全部处置到位（G-7 后半留一处计数，非阻塞）。
生产代码本轮未动一行可执行语句，`PutOnAsync` 与基线**散列相同**。
剩下的全部是计数过期、两处因删节而悬空的指针、以及论证覆盖面窄一步——
**没有一条会让读者据此做出错误判断**，故不阻塞。

---

## 一、判据（自跑）

跑前 `tasklist | grep -i Dfo` **无命中**（退出码 1）。另有两个 `dotnet.exe` 是构建/测试宿主，非 `Dfo.Server.Host`。

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| 项目 | 通过 | 跳过 |
|---|---:|---:|
| Pvf | 60 | 0 |
| Networking | 52 | 0 |
| Protocol | 582 | 0 |
| Persistence | 329 | 0 |
| Infrastructure | 236 | 2 |
| Host | 6 | 0 |
| Application | 1070 | 0 |
| **合计** | **2335** | **2** |

失败 0，退出码 0。基线 2289 → **+46**。与执行方所报的 2335 / 2 / 0 一致。
第三轮我方实测为 2334；本轮 Protocol +1，正是本轮删 1 条 `[Fact]`、加 1 条 `[Theory]`（2 个 `InlineData`）的净效果。
`TheAvatarBagIsServedTests` 现为 **8 `[Fact]` + `Nothing_may_be_worn_in_a_costume_slot` 2 例
+ `A_list_this_server_does_not_serve_is_still_refused` 5 例 = 15 条**（我自己数的）。
无偶发失败，未触发既知的 `ObjectDisposedException: 'SQLitePCL.sqlite3'` 形态。**SC-7 达成。**

---

## 二、G-1 / G-2 与 G-3..G-7 的处置判定

### G-1 **已解决**，且比第三轮开出的药方更强

第三轮的三条要求逐条核：

**(1) 假不变式已删，且反例被明写为「不得推广」。**
`MoveItemSpaceSlice.cs` 的 `PutOnAsync` remarks 现在不再出现
「the worn slot decides the class of what is in it」与
「a list that let one of them out will take the other back」。取而代之的是一句**窄得多**的断言，
并在同一段里**主动**给出槽 24 的反例：

> Do not generalise it to "the worn slot decides what is in it". That is false today:
> `ByName` maps `[earring]`, `[support weapon]` and `[creature]` all to slot 24 ...
> That swap succeeds today because nothing here judges it ...

第三轮的病根是「两句互相推翻、相隔六行、注释不承认它们是同一件事的两面」。
现在这两半被**显式**接成一句禁令加一个反例，矛盾消失。

**(2) 那句窄断言属实——我自己顺着唯一通路核的，并且核到了第三轮没核的第二个写入方。**

- 取槽通路：`PutOnAsync:395` → `moves.WornSlotForAsync` →
  `ItemMovementDomainService.cs:11-12` → `ItemPlacementRuleService.cs:28-29` →
  `IEquipmentSlotDefinitionSource.GetAsync`。
  `grep -rn "IEquipmentSlotDefinitionSource" Server/src` 显示**生产侧只有一个实现**
  `PvfEquipmentSlotDefinitionSource`（`InfrastructureModule.cs:76` 注册），
  其 `Read` 的唯一有值 `return` 是 `PvfEquipmentSlots.ByName.TryGetValue(...) ? slot : null`
  （`PvfEquipmentSlotDefinitionSource.cs:76-78`），其余出口全是 `null`。
  `ByName` 十六个键，最小值 **12**（`PvfEquipmentSlots.cs:38-53`）。
  而 `PutOnAsync:399` 要求 `slot.Value == command.DestinationSlotIndex`。
  **所以 `PutOnAsync` 永远写不到装备栏的 0..11。**
- **第三轮只核了 `PutOnAsync` 这一条通路，没核装备栏容器的另一个写入方。我核了**：
  `grep -rn "InventoryContainerType.Equipment" Server/src` 里唯一另一处写入是
  `CharacterRosterService.cs:230` 的建号起始装备，而它的 `piece.SlotIndex` 同样来自
  `PvfInitialEquipmentDefinitionSource.cs:122` 的 `PvfEquipmentSlots.ByName.TryGetValue`。
  **同一张表，同一个下限。** `TakeOffAsync` / `CarryAcrossAsync` 不写装备栏的任意槽。
  因此注释里「Slots `0..11` are therefore empty always」在本服务器上**成立**，不只是对 `PutOnAsync` 成立。

**(3) 「#11 会摧毁它、要求那个人回来重判」的提醒到位，且拒绝替后来者预先下结论。** 原文：

> That stops being true the day those slots get names -- which is exactly what giving
> costumes a slot map amounts to. Whoever does that has to come back here and decide whether what
> comes off a costume slot can go back where the arriving costume came from. It probably can,
> since only costumes should ever name `0..11`, but "probably" is the whole of the argument and it
> is not one this comment should be read as having made.

最后半句正是第三轮要的东西：**把「大概可以」标成「不是本注释做出的论证」**，
而不是把它写成一条经得起时间的不变式。空真的性质由「empty always」直接表出。

**(4) 用槽 14、夹具自相矛盾的那条判据确实已删；新判据既不恒真，也与名字相符——本轮变异 M8 实测。**

`What_comes_off_a_costume_slot_goes_back_into_the_avatar_list` 已不在文件中。
新的 `Nothing_may_be_worn_in_a_costume_slot`（`TheAvatarBagIsServedTests.cs:186-197`）
是 `[Theory]` 两例（普通外套 `10018` / 时装 `413500000`），
把物品从主背包槽 12 拖向**装备栏槽 0**（一个真正的装扮槽，不再是槽 14），
断言 `result.ItemId` 是那一件，且**装备栏槽 0 仍为空**。

第三轮判「这条规则在基线上已被钉过两遍」，**对槽 ≥12 成立，对装扮槽不成立**。
我用 **M8** 把这件事分开量了：把 `PutOnAsync:399` 改成
`if (command.DestinationSlotIndex >= 12 && slot.Value != command.DestinationSlotIndex)`
——即**只对 0..11 开门，槽 ≥12 的判定原样保留**——跑全量：

```
失败!  - 失败: 2，通过: 580，总计: 582  Dfo.Server.Protocol.Tests.dll
  失败 TheAvatarBagIsServedTests.Nothing_may_be_worn_in_a_costume_slot(template: 10018)
  失败 TheAvatarBagIsServedTests.A_piece_may_not_be_worn_in_a_slot_its_archive_entry_does_not_name
其余六个套件全绿（Application 1070 全通过）
```

**第三轮点名的两条基线既有判据（`RefusedEquipIsRecordedWithItsTemplateTests`、
`CreatureGoesBackOnTests`）在 M8 下是绿的**——它们只走槽 ≥12。
所以对**装扮槽**这一段，本轮新增的这两条是全仓仅有的守卫，不是重复劳动。
`Nothing_may_be_worn_in_a_costume_slot` 因此**有判别力、不恒真、名副其实**。
（时装那一例在 M8 下仍绿，它守的是 `slot is null → NotWearable` 这一支，
由 `Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list` 同时钉着。）

还原后 `sha256sum -c` 输出 `OK`，`git status` 逐行回到开工状态。

**残留一处非阻塞**，见 H-4：注释给的理由比它辩护的那件事**窄一步**。

### G-2 **已解决**（用删除修的，不是用新增修的）

第八、九两节整体删除，替换为一节 `behavior-change-list.md:235-271`。
第三轮那三处假「已处理」随之消失：

| 第三轮 G-2 的三条 | 现状（我自己核的） |
|---|---|
| F-4「已由评审 M5 证伪并改正」 | **该声称已不存在**。`MovingInsideTheAvatarListTests.cs:105-113` 的「slot 7 refused」措辞仍在（F-4(b) 本身是非阻塞、第三轮也未要求改代码），但**清单里再没有任何地方说它改过**。 |
| F-5「随本节改正」 | **该声称已不存在**。`grep -rn "CommandRegistrationTests\|Transactional"` 于清单与 Spec 仍零命中，即 F-5 的诉求仍未处理——但**没有再被写成已处理**。 |
| F-7「随本节改正」 | **该声称已不存在**。`TakeOffAsync` 的文档仍写「the **bag** slot」（我核了 `:490-500`），同样**没有再被写成已改**。 |

第二轮定 F-3 阻塞的尺子是「把没做的事写成做了」。这三处**做没做**照旧，
**写成做了这件事没有了**——按同一把尺子，本条解除。新第八节我逐句核过，见第三节。

### G-3 **已解决**（第九节整体删除），但计数留下一处过期 → H-1

空表与「净减 1 条」的错误差值随第九节消失。
但第六节的判据表仍写「现在 **2334**」「差额 **+45**」，S-5 仍写
「`TheAvatarBagIsServedTests` **14**」「共 **45** 条」，`:182` 仍写「现为 2334」。
实测 **2335 / +46 / 15 条**。本轮删 1 加 2 造成的净 +1 未回填。**非阻塞，见 H-1。**

### G-4 **已解决**（删除）

「N-1 ...... 已补进反向表（见下 R-7）」这句随第八节删除，清单里已无 `N-1` 字样。
假声称消失。N-1 那条变化本身仍不在 R-1..R-7 里，但第三轮已自判其**生产不可达**
（`HandleAsync:286-307` 两端皆非可携带列表时先返回 `ListNotServed`，而入场绑定失败时 R-1 已抛），
且清单再没有声称它在表里。

### G-5 **已解决**

- `:145` C-4 的处置列已改写为「**结论（不做第二次判定）仍然成立，理由换了**：`ByName` 最低只到 12，
  装扮槽上站不了任何东西，displacement 无从发生。注释已按此改写，并点名 issue #11 落地时要回来重判」——
  「把那次判定加回来」的措辞已删，与第三节表头「R-8 已被删掉」不再矛盾。
- 第八节 B-2 那整段（曾写「已改：加上判定」「两条判据」「M4 → 恰好红这一条」）**随第八节删除**。
  悬空的轮次标签我 grep 过，`第九节` 零命中。
  仅剩两处弱悬空（`见第八节 B-1`、`M4，见第八节`），见 H-2/H-3。

### G-6 **主要部分已解决**，留一处措辞 → H-5

第三轮的病是把「再也装不回去」定性为**真正的痛点**，而那已被前一件成果修掉。
现文 `:261-262` 已改为：「`ByName` 让耳环与契约兽同映槽 24，戴耳环会把契约兽顶进背包，
**而契约兽进不了快捷栏**。本仓 `Docs/CreatureDomainDesign.md` 记着这条，**另开 issue**。」
**错误定性已删，活着的那一面（快捷栏）已被点出，处置（另开 issue）正确。**
残留：我 `grep -n -i "quick|快捷|band" Docs/CreatureDomainDesign.md` **零命中**——
那份文档记的是「顶进背包」这一半，**不记快捷栏那一半**，且其「缺陷二」的前提
（「那张表没有 `[creature]` 键」）在基线上已不成立。「记着这条」略过头。**非阻塞，见 H-5。**

### G-7 **前半已解决，后半未回填** → H-7

- **R-6 已由「两处」改为「三处」并列名**：`NotWearable` / `ItemCannotOccupySlot` /
  `DisplacedCannotOccupySlot`。我把基线与当前的 `TakeOffAsync` 剥空行后 `diff`，
  **带上 `piece.ItemId` 的正好是这三处 `return ItemMoveResult.Refused(...)`**，一处不多一处不少。**属实。**
- **第二节已补上那句**（`:70-71`）：「一处不在「线上行为」之内但要说明：`Equipment → Main` 的三处拒绝，
  服务器日志现在带得出物品号（见 R-6）。线上应答码不变，玩家侧无差别。」
  我核了可观察面：`ItemId` 只进日志，应答帧是 `Failure(message, ErrorCodeFor(status))`，不含 ItemId。**属实，且与 R-6 口径一致。**
- **未回填**：第三轮 G-7 末尾那笔「小账」——S-4 仍写「**三处**注释按已查明改写」，
  而 `PutOnAsync` 那段注释的改写记在 C-4、不在 S-4 的三处之内。**非阻塞，见 H-7。**

---

## 三、新第八节逐句核实（G-2 / D 问的交付物）

`behavior-change-list.md:235-271`，37 行。逐句核，**没有把没做的事写成做了**：

| 句子 | 我核到的 |
|---|---|
| 轮次表：一 3 阻塞 / 二 3 / 三 2 | 与本评审文件三节的标题数逐条吻合（B-1..B-3、F-1..F-3、G-1..G-2）。**属实。** |
| 「唯一的生产缺陷，最终的正确处置是把它回退掉」 | 与第二、三轮记载一致；下文立刻说明为何回退。**属实**（「其中真的生产缺陷 1」这个计法可争，但同段自己讲清了来龙去脉，读者不会被误导）。 |
| 所有者定性引文（`:248`） | **无法从仓内证伪**——该原话不在本评审文件、契约、Spec 或任何可读产物中。但「所有者重新定性」这一事件本身第三轮已记（本文件 `:1137`）。**见 H-8。** |
| 「主背包→装备栏逐字符回到基线」 | **我自己重核，不是引用第三轮**：`git show 7147d2e9:...` 与当前文件各取 `PutOnAsync` 方法体，剥空行与 `///` 后各 **31 行**，`diff` 无输出，`sha256sum` 两侧同为 `59fd46ed…f0a`。**属实。** |
| 「`ByName` 最低只到 12，所以装扮槽 0..11 上永远站不了任何东西……这是空真」 | 见 G-1(2)，并额外核了建号那条写入方。**属实。** |
| 「issue #11 给 0..11 补键时会按构造摧毁它——`PutOnAsync` 的注释已点名要求那个人回来重新判断」 | 注释原文确有「Whoever does that has to come back here and decide」。**属实**（注释点的是情形而非 issue 号，不影响）。 |
| 「一条名为钉住此事、实则用槽 14 且夹具自相矛盾的判据已删除，换成钉「装扮槽装不上东西」的两条」 | 旧判据已不在文件；新的是 1 个 `[Theory]` × 2 例，xUnit 计 2 条。**属实。** |
| 剩下两条债：快捷栏/契约兽另开 issue；#11 落地要回来重判 `PutOnAsync` | 两条都真、都在范围外、处置都正确。文档引用略过头，见 H-5。 |
| 「第二轮 2 处假声称 → 第三轮 3 处」 | 第二轮 F-3 是 N-4 与 N-6 两处；第三轮 G-2 是三处。**属实。** |
| 「被删内容的完整经过在 `./reviews/change-review.md` 的三节里」 | 本文件确有三节。**属实。** |

**对第三轮 D 问的复核结论**：第三轮建议「整体删除或压成 ≤10 行主线」，
执行方交的是 37 行。**超出建议长度，但每一行都在承重**（轮次表、回退定性、G-1 结论、两条债、为何这么短），
且**没有一行是逐轮自述流水账**。第三轮真正要防的是「每修一处就写进约一处新的不实」——
本节 37 行里我核出 **0 处不实**。长度是偏好，不是缺陷，不予升级。

---

## 四、本轮整改是否引入新的实质性不实（含悬空引用普查）

`grep -n "第八节|第九节|M4|B-1|B-2|N-1|N-4|F-6|R-8"` 于清单，逐条判：

| 位置 | 引用 | 判 |
|---|---|---|
| `:79` | R-8「经过见第八节」 | **不悬空**。新第八节确有该经过（未用 R-8 这个标号，但叙事完整）。 |
| `:122` | T-2「见第八节 B-1」 | **弱悬空**：新第八节无 `B-1` 标号，内容落在轮次表「无真容器判据」一格。**H-3。** |
| `:128` | 「由第二轮评审 F-6 指出」 | **不悬空**：F-6 在本评审文件里，且 `:271` 已把读者指向本文件。 |
| `:145` | C-4「见第八节」 | **不悬空**，新第八节第三段正是它。 |
| `:182` | 「评审之后另做了 M4，见第八节」 | **悬空**：新第八节全篇无 M4，且 M4 的变异目标（那次准入判定）已不存在。**H-2。** |
| 跨文件 | `spec.md` / `TaskContract.md` / `sdd-execution.yaml` | 均**不引用**清单的节号，无牵连。 |

前七节因删节而变得无法理解或自相矛盾的地方：**未发现**。
第二、三、五节读下来自洽；C-4 的「见第八节」落点正确；第三节表头与 C-4 不再打架（G-5）。

**新引入的不实：0 条。** 新引入的瑕疵是 H-1（计数未回填）与 H-2/H-3（两处指针悬空），
三条都不会让读者对**代码是什么样**做出错误判断。

---

## 五、前三轮已通过的部分：**没有被破坏**（本轮自己重核）

| 项 | 我这一轮的证据 |
|---|---|
| `PutOnAsync` 可执行语句仍与基线逐字符相同 | 剥注释后各 31 行，`diff` 空，`sha256sum` 相同（`59fd46ed…`）。 |
| 放行面仍恰好那四类 | 读 `HandleAsync:280-320` + `CarriedListFor:273-278`：可携带列表只有 `{Main, Avatar}`，装备栏只与可携带列表配对，其余一律 `ListNotServed`。新增组合恰为 Avatar↔Main、Avatar→Equipment、Equipment→Avatar、Avatar→Avatar，即 F-2..F-5。货柜/宠物/快捷栏无一条新门。 |
| `CanOccupyMainSlot` 一字未改 | `git diff 7147d2e9 -- ItemSlotRuleService.cs`：唯一改动是文件顶部 `using` 一行 + 在 `CanOccupyMainSlot` **之上**新增 `AvatarSlotStart/End` 与 `CanOccupySlot`。方法体、`AllowsQuickSlot` 均在 diff 之外。 |
| `MovingInsideTheAvatarListTests` 仍跑真容器 | 文件用 `Microsoft.Data.Sqlite` 真库、`PersistentItemContainer` 真形状（`:22` 注释与 `:171-188` 的连接/`BindList`）。三条 SC-2 场景齐备（见第六节）。 |
| 字节锚仍不在 diff | `git diff --name-only 7147d2e9 | grep -i Transcript` 退出码 1，零命中。 |
| 架构判据全绿 / 分层合法 | 全量 2335 / 2 / 0，架构与分层判据在 Application 与 Infrastructure 套件内，无一红。`ItemSlotRuleService` 新引的 `Dfo.Server.Data.Inventory` 是既有的下层数据契约命名空间，未反向依赖。 |

---

## 六、逐条契约判定（本轮我自己的证据）

| | 判定 | 我的证据 |
|---|---|---|
| **SC-1** | **达成** | `SelectCharacterEntryLifecycleServiceTests` 的 `Entry_binds_the_character_to_its_own_main_equipment_and_avatar_containers` 断言装扮栏已绑、类型正确、不可移除；`BindContainers` 缺 avatar 时抛 `InvalidDataException`（R-1）。M2 已由前轮实证，本轮未重跑。 |
| **SC-2** | **达成** | `MovingInsideTheAvatarListTests` 跑**真 SQLite + 真 `PersistentItemContainer` + 真装扮栏形状**，三场景齐：`A_costume_moves_into_an_empty_slot_and_the_store_says_so`（有物品→空格，断言落库）、`Dragging_from_an_empty_slot_moves_nothing`（空格→空格，`BothSlotsEmpty`）、`Two_costumes_trade_places`（交换两格）。第一轮 B-1 的缺口已实补。 |
| **SC-3** | **达成** | `A_costume_moves_from_the_avatar_list_into_the_bag` 与 `A_costume_moves_from_the_bag_back_into_the_avatar_list`，两个方向各断言两端槽位。 |
| **SC-4** | **达成** | `Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list` 断言 `NotWearable` **且** `result.ItemId == 413500000`——正是契约要的「理由从 `ListNotServed` 变成 `NotWearable` 且日志带得出 `{ItemId}`」；反向 `Taking_something_off_into_the_avatar_list_reaches_that_list` 断言落进装扮栏。**时装仍穿不上，这是契约明写的应有状态。** |
| **SC-5** | **达成** | `AvatarListHasASlotRuleOfItsOwnTests` 五组：区间内放行、`209` 为界、只收 `WorldClass == Avatar`、未决定的列表一律关门、主背包两种问法答案相同。上游依据写在 `ItemSlotRuleService` 的 `AvatarSlotStart` remarks 并**明标「读自上游源码、非实机」**。 |
| **SC-6** | **达成** | `A_list_this_server_does_not_serve_is_still_refused` 五对，含 `PersonalCargo→Main`、`Main→Pet`、`PersonalCargo→AccountCargo`，超过契约要求的两种。M1（前轮）证明它是唯一能抓住「顺手放行所有列表」的东西。 |
| **SC-7** | **达成** | 自跑 **2335 / 2 / 0**，退出码 0，≥ 基线 2289；字节锚文件不在 diff（`grep -i Transcript` 零命中）。 |
| **SC-8** | **达成** | 清单正向 5 条、反向 7 条，从代码出发（逐处扫 `throw` / `return Refused` / `switch`），第一节显式列出搜索集**含 `.rks-agent/tasks/` 本轮全部产物**，并覆盖第四层（连证据一起看），且**如实写下第二层本轮仍失手**（C-4 由评审找出）。 |

---

## 七、非阻塞观察（第四轮）

- **H-1（计数过期）** `:106` S-5「`TheAvatarBagIsServedTests` **14**」「共 **45** 条」、
  `:162`「现在 **2334**」、`:163`「差额 **+45**」、`:182`「现为 **2334**」——
  实测 **15 / 46 / 2335 / +46**。本轮删 1 `[Fact]` 加 1 `[Theory]`（2 例）造成，未回填。
  读者复跑即可自证，不会对代码形成错判。
- **H-2（悬空指针）** `:182`「评审之后另做了 M4，见第八节」——新第八节全篇无 M4，
  且 M4 的变异目标（`PutOnAsync` 里那次准入判定）已随回退不存在。
  建议直接删掉这半句，而不是补写 M4。
- **H-3（弱悬空）** `:122`「见第八节 **B-1**」——新第八节不用 B 编号；内容在轮次表第一行。
  改成「见第八节轮次表第一行」或「见 `./reviews/change-review.md` B-1」即可。
- **H-4（论证窄一步）** `PutOnAsync` 注释给的理由是「装扮槽恒空 → 没有东西从装扮槽被顶下来」，
  而它要辩护的那件事是「**被顶下来那件无判定地写回源列表，而源列表可能是装扮栏**」。
  两者只有在「装扮栏里不可能有非时装」时才等价——而这一步注释**没写**。
  我自己补核了这一步并确认结论成立：`CarryAcrossAsync:449` 与 `TakeOffAsync:519` 都用
  `MayOccupySlotAsync(目的容器, …)` 问过装扮栏规则（`CanOccupySlot` 要求 `WorldClass == Avatar`），
  所以**没有任何移动能把非时装放进装扮栏**；而时装 `WornSlotForAsync` 返回 `null`，
  在 `NotWearable` 处就被拒，根本走不到写回。判据文件 `:178-181` 正好写着这句
  （「a state the store permits and no move produces」），只是没搬进注释。
  **结论正确，理由少一环。** 补一句即可，不影响任何判断。
- **H-5（引用略过头）** `:262` 与 `PutOnAsync` 注释都把快捷栏/契约兽那条债指向
  `Docs/CreatureDomainDesign.md`。该文档「缺陷二」记的是**耳环把宠物顶进背包**这一半，
  `grep -i "quick|快捷|band"` 于该文件**零命中**——**不记快捷栏那一半**；
  且其前提「那张表没有 `[creature]` 键」在基线上已不成立（`[creature] = 24` 已在表里）。
  操作性指示（**另开 issue**）是对的，建议新 issue 里顺手标注该文档已陈旧。
- **H-6（判据措辞）** `Nothing_may_be_worn_in_a_costume_slot` 的 remarks 写
  「the request is refused for naming a slot the coat does not belong in, and a costume is refused
  **the same way**」——实际普通外套落 `ItemCannotOccupySlot`、时装落 `NotWearable`，是两条不同的支；
  且该 `[Theory]` **不断言 `Status`**（判别力来自 `Assert.Null(装备栏槽 0)`，M8 已证有效）。
  改成「refused as well」或补一条 `Status` 断言都行。
- **H-7（G-7 后半）** S-4 仍写「**三处**注释按已查明改写」，`PutOnAsync` 那段的改写记在 C-4 而不在 S-4。
  两处加起来读者读得全，单看 S-4 会少数一处。
- **H-8（无法证伪的出处）** `:248` 所有者定性引文在任何可读产物中都无二次出处。
  事件本身第三轮已记，引文的**逐字**性质读者无从复核。不建议删（它承载了回退的理由），
  建议注明出处形式（如「所有者口头定性，转述」）。

---

## 八、明确表态

### A. 还有没有「读者会据此做出错误判断」的实质性不实？

**没有。**

我按「读者据此会不会对代码形成错判」这一条尺子过了全部记载：
生产注释、三个新判据文件的 remarks、变化清单八节、契约与 Spec 的对照。
第三轮那两条（假不变式、三处假「已处理」）都已从根上消失——
一条是把话改窄并当场自证反例，一条是**删掉了产生它的整整两节**。
H-1..H-8 里没有一条会让读者以为代码做了它没做的事，或没做它做了的事：

- H-1 是计数，复跑即证；
- H-2/H-3 是指针落空，读者找不到东西、不会被引向假东西；
- H-4 结论正确、缺一步理由，且缺的那一步就写在同一变更集的判据文件里；
- H-5 的操作性指示（另开 issue）正确，错的只是引文覆盖面；
- H-6/H-7 是措辞与计数；
- H-8 是出处形式。

### B. 是否只剩措辞、编号、可过期计数？

**是。** 故判 **`approved`**，H-1..H-8 全部列为非阻塞观察，交由所有者自行取舍。
其中**建议但不要求**的只有 H-2（那半句删掉即可）与 H-1（回填四个数）。
本件已四轮，生产代码三轮无实质问题且本轮散列级不变，记载已过「不会误导」这条线。

### C. 契约 SC-1..SC-8 是否全部达成？

**全部达成。** 逐条证据见第六节。特别地：

- **SC-2** 是第一轮的阻塞项，本轮我用真容器判据的三条用例名逐一对上契约三场景，确认已实补；
- **SC-4** 的「仍被拒，但理由是 `NotWearable` 且带得出 itemId」由
  `Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list` 双断言钉住，
  **这正是契约与 Spec NA-001 要的状态，不是未完成**；
- **SC-7** 由我自跑的 2335 / 2 / 0 与字节锚不在 diff 共同支撑。

---

## 九、本轮实际跑过的命令

```
git status --porcelain=v1 ; git log --oneline -3 ; git diff --stat 7147d2e9
tasklist | grep -i Dfo            -> 无命中（退出码 1）
tasklist | grep -i dotnet         -> 2 个构建/测试宿主，非 Dfo.Server.Host

PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet test Server/86JP.Server.slnx --nologo          -> 2335 / 2 / 0，退出码 0

git show 7147d2e9:Server/src/.../MoveItemSpaceSlice.cs > /tmp/base_slice.cs
sed -n '/PutOnAsync/,/^    }$/p' <两侧> | grep -v 空行 | grep -v '///' | diff ; sha256sum
sed -n '/TakeOffAsync/,/^    }$/p' <两侧> | grep -v 空行 | diff
git diff 7147d2e9 -- Server/src/Dfo.Server.Application/Inventory/ItemSlotRuleService.cs
git diff --name-only 7147d2e9 | grep -i Transcript      -> 零命中

grep -rn "IEquipmentSlotDefinitionSource" Server/src --include=*.cs
grep -rn "InventoryContainerType.Equipment" Server/src --include=*.cs
grep -rln "PvfEquipmentSlots" Server/src --include=*.cs
grep -n -i "quick|快捷|band" Docs/CreatureDomainDesign.md   -> 零命中
grep -n "第八节|第九节|M4|B-1|B-2|N-1|N-4|F-6|R-8" behavior-change-list.md

# 变异 M8（文件级备份 + sha256sum；未使用任何 git 还原类命令）
cp MoveItemSpaceSlice.cs /tmp/M8.bak ; sha256sum MoveItemSpaceSlice.cs > /tmp/M8.sha
sed -i 's/if (slot.Value != command.DestinationSlotIndex)/if (command.DestinationSlotIndex >= 12 && slot.Value != ...)/'
PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo   -> Protocol 失败 2 / 通过 580，其余全绿
cp /tmp/M8.bak MoveItemSpaceSlice.cs ; sha256sum -c /tmp/M8.sha     -> OK
git status --porcelain=v1                                           -> 与开工逐行一致
```
