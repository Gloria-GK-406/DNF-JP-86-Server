---
kind: rks-verification
decision: pass
---

# 独立验证（第四轮）：装扮栏也是一个容器

契约 `../TaskContract.md` revision 1（SC-1..SC-8）／Spec `../spec.md`／
清单 `../behavior-change-list.md`／评审 `../reviews/change-review.md`（四轮，终判 `approved`）。

**fresh context，未采信执行方或评审方任何结论。**所有数字、行数、行号、拒绝码方向
均由本轮自跑自数自读。未修改任何产品或判据文件，未提交任何东西。

---

## 验证环境

| | |
|---|---|
| 工作区 | `.worktrees/the-avatar-bag-is-a-container-too`，分支同名 |
| 基线 | `7147d2e96417337de65e3fd481b1a28a3d29ae8b`（`git rev-parse HEAD` 自核） |
| 状态 | 未提交：16 个已跟踪文件被改 + 3 个新判据文件未跟踪 + 任务产物目录 |
| `Dfo.Server.Host` 进程 | **无**。跑判据前 `Get-CimInstance Win32_Process` 逐条读过命令行，两个 `dotnet.exe` 分别是 VS Code 的 `ProjectSystem.Server.BuildHost` 与 IDE 的 `vstest.console`，都不是 host |

---

## 结论

**`decision: pass`。SC-1..SC-8 全部达成。**

前三轮判 `fail` 的唯一实质性缺口（反向表宣布 7 条、表体只有 6 行、R-7 整行不在文档里）
**已经补上并且补对了**。执行方声称的四件本轮改动，**四件都真的做了，四件都做对了**。
本轮的改动**没有再引入新的实质性不实**——这是前三轮各栽过一次的错法，本轮没有复发。

余下一件是**计数不准**，非阻塞，列在 O-1。

---

## 前三轮 `fail` 的经过（保留，勿以为从未 fail 过）

| 轮 | 判定 | 判的是什么 |
|---:|---|---|
| 一 | `fail` | SC-1..SC-7 达成、SC-8 未达成。清单声称「协议文档零改动」为假；`.md` 侧的补扫从未做过 |
| 二 | `fail` | SC-1..SC-7 达成、SC-8 未达成。第一轮的更正**本身是错的**（`AGENTS.md` 把「目标列＝1 走得到 `PutOnAsync`」写成事实）；且「补扫」只跑了声明的五条 `grep` 里的一条 |
| 三 | `fail` | SC-1..SC-7 达成、SC-8 未达成。第二轮开出的 8 件待办 8 件都做了、7 件准确；唯一实质性缺口是 **R-7 整行从文档里消失**，而标题与表头仍说「共 7 条」。错法变了形态：不是写进一句假的，是删掉一行真的 |

三轮都判「产品代码零缺陷，缺口全在记载」。**本轮复核后同意这个定性**，
本轮同样没有在产品代码里发现缺陷。

---

## SC-1..SC-8 逐条判定

| | 判定 | 我这一轮的证据 |
|---|---|---|
| SC-1 | **达成** | `SelectCharacterEntryInventoryDomainService.cs` 的 `BindContainers` diff 自读：`foreach (var container in (InventoryContainerState[])[main, equipment, avatar])`，avatar 由 `SingleOrDefault` 取出、取不到抛 `InvalidDataException`。判据 `Entry_binds_the_character_to_its_own_main_equipment_and_avatar_containers`（`SelectCharacterEntryLifecycleServiceTests.cs:205`）末尾三行断言 `GetContainer(..., Avatar)` 存在、`Type` 正确、不可移除（`:250-251`） |
| SC-2 | **达成** | `MovingInsideTheAvatarListTests` 6 条，**走真 `PersistentItemContainer` + 真 SQLite store + 真 avatar `ContainerShape`**（`side.BindList(InventoryContainerType.Avatar, …)`，`:183`）。覆盖空格→空格（`Dragging_from_an_empty_slot_moves_nothing`）、有物品→空格（`A_costume_moves_into_an_empty_slot_and_the_store_says_so`）、交换两格（`Two_costumes_trade_places`） |
| SC-3 | **达成** | `TheAvatarBagIsServedTests`：`A_costume_moves_from_the_avatar_list_into_the_bag` 与 `A_costume_moves_from_the_bag_back_into_the_avatar_list`，两个方向各一条 |
| SC-4 | **达成** | `Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list` 自读判据体：`Assert.Equal(ItemMoveStatus.NotWearable, result.Status)` **且** `Assert.Equal(CostumeTemplate, result.ItemId)`——**拒绝理由从 `ListNotServed` 变成 `NotWearable`、并且带得出 `{ItemId}`，两件都被钉住**。反向 `Taking_something_off_into_the_avatar_list_reaches_that_list` 断言 `Moved` 并回读装扮栏落点 |
| SC-5 | **达成** | `ItemSlotRuleService.cs:49-62` 自读：`Avatar => slotIndex >= 0 && slotIndex <= 209 && placement.WorldClass == ItemWorldClass.Avatar`，`_ => false` 兜底。判据 `AvatarListHasASlotRuleOfItsOwnTests` 25 条覆盖区间内 4 点 / 区间外 3 点（−1、210、289）/ 非时装 / 未决定的列表一律关门。常量注释标明「Read from upstream source, not measured against a client」，与契约 Assumptions 一致，未偏离上游 |
| SC-6 | **达成** | `A_list_this_server_does_not_serve_is_still_refused` 的 `ListsStillNotServed()` 自读，**5 对**：`PersonalCargo→Main`、`Main→Pet`、`PersonalCargo→AccountCargo`、`Avatar→PersonalCargo`、`QuickSlot→Avatar`。**源端、目的端、两端三个方向都覆盖**，且含个人货柜、宠物、账号金库三种 |
| SC-7 | **达成** | 见下「判据（自跑）」。**2335 / 2 / 0，退出码 0**，不低于基线 2289 / 2 / 0。字节锚 `SelectCharacterAcceptedTranscriptTests.cs` **`git status` 为空 = 逐字节未动**，也不在未跟踪清单里 |
| SC-8 | **达成** | 见下「SC-8：本轮四件的逐条复核」与「我自己的独立扫」 |

---

## 判据（自跑）

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

**一次跑完，退出码 0，无偶发失败，未触发既知的 `SQLitePCL` `ObjectDisposedException`。**

| 项目 | 通过 | 跳过 | 基线 | 差 |
|---|---:|---:|---:|---:|
| Dfo.Pvf | 60 | 0 | 60 | 0 |
| Networking | 52 | 0 | 52 | 0 |
| **Protocol** | **582** | 0 | 567 | **+15** |
| Persistence | 329 | 0 | 329 | 0 |
| Infrastructure | 236 | **2** | 236 | 0 |
| Host | 6 | 0 | 6 | 0 |
| **Application** | **1070** | 0 | 1039 | **+31** |
| **合计** | **2335** | **2** | 2289 | **+46** |

两条跳过是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
——即契约说的 `A_missing_archive_*`，跳过正是档案确实加载了的标志。
**装扮栏那 6 条 `SkippableFact` 因此是真的跑了**（Application 恰好 +31 = 25 + 6）。

**执行方报的 2335 / 2 / 0 属实。** 清单 §六 的 `+46 ＝ Application +31、Protocol +15`
与 §四 S-5 的 `25 / 15 / 6` 三个数彼此对得上，也与我实测的分项差额对得上。

### 改动面（自核）

`git diff --stat -- Server/` → **9 文件 +281 / −50**，与执行方声称的一致；
另有 3 个未跟踪的新判据文件。全仓 `git diff --stat` → 16 文件 +322 / −63。
**`Server/` 下这一轮确实一行代码都没动。**

---

## SC-8：本轮四件的逐条复核（自己数、自己读，不采信）

### 1. R-7 回到表里 —— **做了，且做对了**

**自己数行数**（`awk 'NR>=107 && NR<=113'`）：`behavior-change-list.md` 第三节
表头 `:105`、分隔 `:106`、表体 **`:107`–`:113` 恰好七行**，依次是
R-1 / R-2 / R-3 / R-4 / R-5 / R-6 / **R-7**。
标题 `:95` 写「共 7 条」，**与表体行数一致**。第三轮的缺口已闭合。

表头 `:97-99` 说明它一度丢失，并写下教训「改完一张表要回头数一遍行数」，
以及是由第三轮独立验证数出来的。属实。

**R-7 内容与代码逐条对照**（自己读 `MoveItemSpaceSlice.cs`，不转述清单）：

- `ErrorCodeFor`（`:687-697`）**在本轮 diff 里一行未改**，仍逐条列举、无 fallthrough。
  `ItemMoveStatus.ListNotServed => MoveItemSpaceResponse.NotMovable`（`:690`），
  `ItemMoveStatus.NoBoundContainer => MoveItemSpaceResponse.Silent`（`:696`）。
- 常量自读：`NotMovable = 23`（`:75`）、`Silent = 2`（`:89`）。
  **R-7 写的两个码 `NotMovable(23)` / `Silent(2)` 方向正确，没有写反。**
- 方向自推：取「装扮栏未绑定」这个前提，把五种沾装扮栏的列表对逐条走一遍
  现在的 `HandleAsync`（`:280-320`）与 diff 里的基线版：

  | 请求 | 基线 | 现在 |
  |---|---|---|
  | Avatar→Avatar | 三条 `if` 全不中 → `ListNotServed` | `bag = FindContainer(Avatar)` 为 null → `NoBoundContainer`（`:311`） |
  | Avatar→Main | 同上 `ListNotServed` | 同上 `NoBoundContainer` |
  | Main→Avatar | 同上 `ListNotServed` | `into` 为 null → `NoBoundContainer`（`:317`） |
  | Avatar→Equipment | 同上 `ListNotServed` | `from` 为 null → `NoBoundContainer`（`:294`） |
  | Equipment→Avatar | 同上 `ListNotServed` | `into` 为 null → `NoBoundContainer`（`:302`） |

  **五条都从 `ListNotServed → NotMovable(23)` 变成 `NoBoundContainer → Silent(2)`。
  R-7 的方向、场景、两个码全部正确。**
- 「没有一行代码为此改动」：`ErrorCodeFor` 确实未改（只有 `NoBoundContainer` 的
  枚举文档注释改过），变的是分发路径。措辞与代码相符。
- 「生产不可达（入场绑定失败会先抛 R-1）」：`BindContainers` 取不到 avatar 就抛
  `InvalidDataException`，会话根本建不起来。属实。
- 佐证：`protocol/patterns/data/traces/../probes/cmd-reason-messages-20260818.md:127`
  本来就把「角色没有绑定容器（服务端自身故障）」定为**静默**——
  新落点与本仓既定的应答口径一致，不是新造的行为。

### 2. §八「三轮」→「四轮」 —— **做了，且做对了**

`:279` 标题现为「## 八、评审经过（四轮）」，轮次表 `:283-286` 四行：
一 3 阻塞 / 1 真缺陷、二 3 / 0、三 2 / 0、**四 0 / 0**。

**自核评审产物**：`reviews/change-review.md` frontmatter `verdict: approved`、`round: 4`；
四个轮次大节（`:7`、`:270`、`:678`、`:1189`）。
第二轮开 F-1..F-3（3 条）、第三轮开 G-1 / G-2（2 条）、
**第四轮全篇没有 `Blocking Findings` 节**（`:1208` 起依次是 判据 / 处置判定 / 新第八节逐句核实 /
是否引入新的不实 / 前三轮未被破坏 / 契约判定 / 非阻塞观察 / 明确表态 / 命令）。
**表里四个数字全对。**

### 3. `DomainInstanceDesign.md:100` 的行号 —— **做了，且做对了**

- 文档现值自读：`Docs/Refactor/DomainInstanceDesign.md:100` →
  ``（`ItemSlotRuleService.cs:64`）``。
- 代码真实行号自核：`grep -n "CanOccupyMainSlot" ItemSlotRuleService.cs` →
  **`64: public bool CanOccupyMainSlot(ItemPlacement placement, int slotIndex)`**。
  **`:64` 是对的。**
- 同文件 `:216` 另有一处 `CanOccupyMainSlot` 引用，不带行号、说的是「passes」，
  方法一字未改，**仍然成立，不需要动**。

### 4. `AGENTS.md` 的「可执行语句逐字符未变」 —— **做了，且做对了**

现文（`AGENTS.md:693-695`）：「那条路——`PutOnAsync` → `NotWearable` → 带模板号——
**可执行语句逐字符未变**（注释改过），量具照旧管用。」

**自核 diff**：`MoveItemSpaceSlice.cs` 里 `PutOnAsync` 的方法体，
本轮 diff 只有一处 `-`——**删掉了一个空行**（`@@ -342,7 +401,6 @@` 一段）。
`NotWearable` / `ItemCannotOccupySlot` / `Moved` 三处返回、`WornSlotForAsync` 调用、
`displaced` 的读写分支，**逐字符与基线相同**。
方法上方的 `<remarks>` 注释改写幅度很大（C-4）。
**「可执行语句逐字符未变（注释改过）」与事实相符**，且加上「可执行语句」这个限定之后，
上一版那句「逐字符未变」的过强措辞已被修掉。

顺带核了同一节第 2 条（`TakeOffAsync` 三带两不带）：diff 里
`NotWearable` / `ItemCannotOccupySlot` / `DisplacedCannotOccupySlot` 三处
改为双参带 `piece.ItemId`，`NoBoundContainer` 与 `BothSlotsEmpty` 仍是单参
（且都在读到物品之前返回）。**三带两不带属实**，与清单 R-6 一致。

---

## 我自己的独立扫：有没有第 12、13 处未处置的记载？

### 声明的搜法，我原样重跑了一遍

`behavior-change-list.md:41-45` 声明的五条 `.md` 侧 `grep`，我逐条重跑
（排除 `.rks-agent/`、`.worktrees/`）：

| 命令 | 清单声称 | 我实测 |
|---|---|---|
| `grep -rn "CanOccupyMainSlot" --include=*.md .` | 2 | **2** ✓ |
| `grep -rn "MayOccupyMainSlotAsync\|MainSlotAdmission" --include=*.md .` | 2 | **2** ✓ |
| `grep -rn "InventoryContainerType.Avatar" --include=*.md .` | 0 | **0** ✓ |
| `grep -rn "ListNotServed" --include=*.md .` | 5 | **5** ✓ |
| `grep -rn "BindContainers\|containers.Register" --include=*.md .` | 0 | **0** ✓ |

**2 / 2 / 0 / 5 / 0，一个不差。搜法诚实、可复现。**
五条 `ListNotServed` 命中（`AGENTS.md:686`、`DomainInstanceDesign.md:132`、
`avatar-container-entry-shape.md:96`、`NOTI-0x000D.md:418`、trace `:21`）
**全部在第五节表里有对应行。**

### 我另外自己加的搜法（清单没声明的角度）

1. `grep -rniE "only the main list|only serves|唯一被服务|只服务|只认|只绑" --include=*.md --include=*.cs .`
2. `grep -rln "MoveItemSpace" --include=*.md .`（12 个文件逐个看）
3. `grep -rn "装扮栏|avatar list" --include=*.md --include=*.cs .`（88 命中 / 20 文件）
4. `grep -rniE "NoBoundContainer|绑定.{0,25}容器|两个容器|BindMain|IItemSlotRuleService|SlotAdmission|CanOccupySlot" --include=*.md .`
5. `grep -rn "SnapshotAsync|GetContainers" Server/src --include=*.cs`（核绑定的副作用面）

**结论：没有第 12、13 处未处置的记载。** 逐条交代：

| 我扫到而第五节表里没有的出处 | 判定 |
|---|---|
| `protocol/game/client-to-server/0x0013.md:190`「码 23……因为处理器**当时**只服务 Main→Main。归档见 a21-tutorial-ending」 | **不是未处置**。整段以「**2026-08-26 实机。**」开头、用「当时」、并显式指向归档 trace；说的是装备栏那次（同日已修）。是过去时的实机记录，与清单已定性为「不改」的 trace `:21` 同类同源。**没有一句会让读者误判现状** |
| `protocol/game/client-to-server/0x002F.md:66`「← 只服务 Main 与 Avatar」 | **无关**。那是 `CMD 0x002F DROP_ITEM` 的**上游源码形状**记录，不是本仓 `MoveItemSpace`；该页明写「本协议尚无观察记录」「未实现」。文件不在 diff 里，基线原文如此 |
| `Docs/a21-units/unit-017.md` 的 `requestMoveItemSpace` | **无关**。客户端逆向（`0x017012D0` 的签名与节流门），不涉及服务端服务哪些列表 |
| `Document/reply-to-vmp-agent.md`、`reply-to-vmp-agent-2.md`、`vmp-agent-next-task.md` 里的「装扮栏」 | **未被推翻**。三份都只谈 `NOTI 0x000D/0x000E` 的编码与客户端崩溃，不谈移动路径。其中「服务器把装扮栏库存当作永远为空处理」是对**推送帧**的建议——我自核了它不受本轮影响：`SnapshotAsync` 在 `Server/src` 内**零生产调用方**（只有接口声明、抽象声明、一处 override），`GetContainers` 同样零调用方，**绑定装扮栏不会让任何路径开始推送装扮栏帧**。契约 Requires Confirmation 的第一条不触发 |
| `Server/src/.../GameInventoryDelivery.cs` 三处 avatar 注释 | **未被推翻**，且该文件不在 diff 里（编码器未碰，与字节锚未变互为佐证） |
| `Docs/Refactor/DomainInstanceDesign.md:216` | **仍成立**，`CanOccupyMainSlot` 一字未改 |
| `protocol/patterns/data/probes/cmd-reason-messages-20260818.md:127` | **仍成立**，且正是 R-7 新落点的既定口径 |
| `Server/src/.../ItemContainers.cs:772/784`「only the main list does」 | **仍成立**，说的是 `SnapshotAsync` 的加载器，本轮未改 |

### 第五节「不改」三条的理由，我自己核了

- `Docs/CharacterEntryFacesInventory.md` 页首：「调查日期 2026-09-04，**基准 `main` @ `51cd99c2`**」✓
- `Docs/CharacterDomainCut.md` 页首：「2026-09-05，**基准 `main` @ `51cd99c2`**」✓
- `protocol/patterns/world-entry.md:642` 位于「### 结尾的四堵墙（**同日**）」节下、指向同一份 trace ✓

**三条「不改」的理由属实，不是搪塞。**

### 这一轮的改动本身有没有引入新的不实？

**没有实质性不实。** 我把本轮四处改动的每一句都当作新主张核过：

- R-7 那一行的每个断言 → 上面逐条核过，全对。
- 表头新增的「一度丢失 / 由第三轮验证数出 / 改完一张表要回头数一遍行数」→ 与第三轮
  验证产物、与评审 `:748`「表内 R-1..R-7 七行」的记载一致。
- §八 四行轮次表 → 与评审产物的四个大节、各轮开出的编号一一对上。
- `:64` 与 AGENTS.md 那句 → 直接读代码核过，都对。

**顺带核了另一处容易被这次改动带歪的**：`Document/avatar-container-entry-shape.md:98`
新加的「上面那条『上衣该进 3 号槽』的量法因此也变了」。
它挂在的那条 bullet 记的是两次观测（主背包拖拽 `6->3`、**装扮栏里拖拽 `0->3`**）。
装扮栏那半边基线上确实是 `ListNotServed` + `item 0`，现在走 `PutOnAsync` → `NotWearable` + 模板号，
**量法确实变了**；主背包那半边基线上就带模板号、本轮未变。
一句话覆盖了变与不变两半，**措辞略糙但不误导**（两条路今天都说得出模板号），
计入 O-2，非阻塞。

---

## 非阻塞观察

### O-1　第五节的两个总数与它自己的表对不上（**计数不准，非实质性不实**）

- `:175`「补扫后本轮实际更正的记载共 **5 处**」
- `:190-191`「上表是……**11 处**（**更正 5、不改 6**）。计数口径：**按上表行数**」

**我机械数过**（`awk 'NR>=179 && NR<=188'`）：该表**数据行 10 行**，
其中标「不改」**3 行**、其余 **7 行是更正**。
7 这个数还有一个独立佐证：`git diff --name-only | grep '\.md$'` 恰好 **7 个文件**
（`AGENTS.md`、`CreatureDomainDesign.md`、`ItemWorldModel.md`、`DomainInstanceDesign.md`、
`avatar-container-entry-shape.md`、`NOTI-0x000D.md`、`NOTI-0x000E.md`），与表里 7 行更正一一对应。

按「行数」口径应是 **10（更正 7、不改 3）**；
按「出处」口径（`:187` 一行含三个出处）应是 **12（更正 7、不改 5）**。
**声明的 `11（5、6）` 在两种口径下都不成立。**

**为什么判非阻塞**：这是文档对**它自己表格的结算**，不是对代码、行为或仓库状态的陈述。
表里**每一行的出处、原文、处置我都逐条核过，全部准确**，读者要动手时用的是行、不是总数。
它不会让任何人做出错误判断，属于契约验收标准里明确要求不得升级的「可过期计数」一类。
**并且这不是本轮引入的**——第三轮验证复算的基数是
`2335/2/0、+46、+31/+15、25/15/6、2/2/0/5/0`，**没有覆盖到这两个数**，
本轮也未改动第五节。想收干净的话，一处两字的编辑即可。

### O-2　`avatar-container-entry-shape.md:98` 的一句话覆盖了变与不变两半

见上。改后两条路都说得出模板号，结论没错，只是没分开说主背包那半边本来就带号。
**属「可以更完整」，不是不实。**

### O-3　`§八:316`「完整经过在 `change-review.md` 的**三节**里」

评审产物现有四个轮次大节；被删内容的经过确实分布在一~三轮，第四轮也复述了一部分。
**「三节」可辩护，不构成误导。**

### O-4　`0x0013.md:190` 未被第五节点名

它是过去时的实机归档（见上），**不需要处置**；只是若要把「同一说法的全部出处」列到穷尽，
它会是第 12 处。契约不要求穷尽。

---

## Unverified Conditions

| | 为什么 |
|---|---|
| 实机行为 | 本轮未跑客户端。契约 NA-002 明写实机不作为验收手段（`NotWearable` 与 `ListNotServed` 在玩家侧同样表现为「拖不动、弹回去」） |
| 基线 2289 / 2 / 0 | 我没有在基线上重跑（禁用 git 还原类命令，且契约与前三轮已各实测）。我实测的是**当前** 2335 / 2 / 0，以及分项差额 +15 / +31 与新增判据条数 15 / 25+6 自洽 |
| 装扮栏 `0..209` 上限 | 上游源码读出，本仓从未实机验证——清单 §七已如实标注，代码常量注释也标注了 |
| 变异 M1 / M2 / M3 | **我未自行重跑变异**（评审方已独立复现 M1 的 5 红名单）。我改用读代码的方式独立确认了 M1 要保护的那件事：`CarriedListFor` 只把 `Main` 与 `Avatar` 映到非 null，其余一律 `null` → `ListNotServed`，**放行面恰好是 {Main,Avatar}² 加 {Main,Avatar}↔Equipment，不多不少**，与 F-2..F-5 一致；SC-6 判据从源端、目的端、两端三个方向钉住 |
| 既知 `SQLitePCL` 偶发 | 本轮**没有触发**，无栈可抓 |

---

## A / B / C

### A. SC-8 现在是否达成？

**达成。**

前三轮判 `fail` 的唯一实质性缺口——反向表宣布 7 条而表体只有 6 行、R-7 整行不在文档里——
**已经闭合**：表体现在恰好 7 行，R-7 是真正的表行，其内容（`ListNotServed → NotMovable(23)`
变为 `NoBoundContainer → Silent(2)`）经我自己读 `ErrorCodeFor`、读常量、
把五种沾装扮栏的列表对逐条走一遍 `HandleAsync` 的新旧两版，**方向、场景、两个码全部正确**。

按契约 SC-8 的原文逐项：

- **正向与反向都报** ✓ 正向 5 条（F-1..F-5，表体 5 行与标题「共 2 类 5 条」相符）、
  反向 7 条（R-1..R-7，表体 7 行与标题「共 7 条」相符），并显式写「被移除的生产拒绝：0 条」。
- **从代码出发独立扫** ✓ 反向表明写「逐处扫产线的 `throw` / `return Refused` / `switch` 分支，
  不是核对上面的正向表」；我抽核 R-2、R-4、R-5、R-6、R-7 五条对应的代码位置，都在。
- **搜索集显式列出、含 `.rks-agent/` 本轮全部产物** ✓ `:51-56` 写明范围与未纳入项及其理由。
- **搜法可复现** ✓ 五条 `grep` 我原样重跑，**2 / 2 / 0 / 5 / 0 逐个吻合**。
- **显式列出本轮推翻的说法** ✓ C-1..C-4 加第五节的出处表；
  我自己另做五个角度的独立扫，**没有找到第 12、13 处未处置的记载**。
- **四层错法的自述** ✓ 且三处失败（声明未执行、补扫只跑一条、改完不数行）都留在文档里没有被抹掉。

余下的 O-1 是文档对自己表格的结算数不准，**不是实质性不实**：
表内每一行的出处与处置我都核过、全部准确，读者据以行动的是行不是总数。
按验收标准「不要把可过期计数升级为不达成」，不阻塞。

### B. 判定

**`pass`。SC-1..SC-8 全部达成，成果可以接受提交。**

### C. 若仍 fail —— 不适用

不判 `fail`。剩余四件全部属于「可以更完整」，逐件定性如下：

| | 事项 | 定性 |
|---|---|---|
| O-1 | 第五节「5 处」/「11 处（更正 5、不改 6）」与表的 10 行 / 7 更正 / 3 不改 对不上 | **计数不准，非实质性不实**（表内每行准确；非本轮引入） |
| O-2 | `avatar-container-entry-shape.md:98` 一句覆盖变与不变两半 | **可以更完整** |
| O-3 | §八「change-review.md 的三节」而评审现有四节 | **可以更完整** |
| O-4 | `0x0013.md:190` 未被第五节点名 | **可以更完整**（过去时归档，不需处置） |

四件都不改变任何读者的判断，不阻塞接受提交。

---

## 我实际跑过的命令

```
# 环境与前置
git rev-parse HEAD                       # 7147d2e96417337de65e3fd481b1a28a3d29ae8b
git rev-parse --abbrev-ref HEAD
git status --porcelain=v1
tasklist | grep -i -E "Dfo|dotnet|testhost"
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Select-Object ProcessId,CommandLine | Format-List"

# 判据（全量一次，退出码 0）
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo

# 改动面
git diff --stat
git diff --stat -- Server/
git diff --name-only | grep '\.md$'
git status --porcelain -- Server/
git status --porcelain -- Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs   # 空 = 字节锚未动

# 代码逐段读出（不是转述）
git diff Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
git diff Server/src/Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryDomainService.cs Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs
sed -n '236,330p' Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
sed -n '381,418p' Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
grep -n "ErrorCodeFor" -A 60 Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
grep -n "NoBoundContainer\|ListNotServed\|CarriedListFor\|HandleAsync" Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
grep -n "NotMovable\|Silent\|StackLimitReached" Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
sed -n '38,70p' Server/src/Dfo.Server.Application/Inventory/ItemSlotRuleService.cs
grep -n "CanOccupyMainSlot\|CanOccupySlot\|AvatarSlotStart\|AvatarSlotEnd" Server/src/Dfo.Server.Application/Inventory/ItemSlotRuleService.cs   # → 64

# 绑定的副作用面（契约 Requires Confirmation 第一条）
grep -rn "SnapshotAsync" Server/src --include=*.cs      # 零生产调用方
grep -rn "GetContainers" Server/src --include=*.cs      # 零生产调用方

# 判据面
grep -nE "public (async )?(Task|void|ValueTask)|InlineData" <三个新判据文件>
sed -n '140,180p' Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/TheAvatarBagIsServedTests.cs
sed -n '225,265p' Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/TheAvatarBagIsServedTests.cs
sed -n '1,45p' Server/tests/Dfo.Server.Application.Tests/Inventory/MovingInsideTheAvatarListTests.cs
grep -n "main_equipment_and_avatar\|Avatar" Server/tests/Dfo.Server.Application.Tests/Characters/SelectCharacterEntryLifecycleServiceTests.cs
grep -rn "BindList(" Server/tests --include=*.cs
grep -rn "\.BindMain(" Server/tests --include=*.cs | wc -l   # 17，5 个文件 → T-2 属实

# 五条声明 grep 原样重跑 → 2 / 2 / 0 / 5 / 0
grep -rn "CanOccupyMainSlot" --include=*.md .
grep -rn "MayOccupyMainSlotAsync\|MainSlotAdmission" --include=*.md .
grep -rn "InventoryContainerType.Avatar\|BindContainers\|containers.Register" --include=*.md .
grep -rn "ListNotServed" --include=*.md .

# 我自己另加的扫
grep -rniE "only the main list|only main|only serves|唯一被服务|只服务|只认|只绑" --include=*.md --include=*.cs .
grep -rln "MoveItemSpace" --include=*.md .
grep -rn "装扮栏\|avatar list" --include=*.md --include=*.cs .
grep -rniE "NoBoundContainer|绑定.{0,25}容器|两个容器|BindMain|IItemSlotRuleService|SlotAdmission|CanOccupySlot" --include=*.md .
grep -niE "ListNotServed|只服务|MayOccupyMainSlotAsync|CanOccupyMainSlot" .rks-agent/tasks/.../spec.md .../routing-decision.md .../sdd-execution.yaml

# 文档更正逐处读出
git diff Docs/Refactor/DomainInstanceDesign.md AGENTS.md
git diff Docs/CreatureDomainDesign.md Docs/ItemWorldModel.md Document/avatar-container-entry-shape.md protocol/game/server-to-client/NOTI-0x000D.md protocol/game/server-to-client/NOTI-0x000E.md
head -12 Docs/CharacterEntryFacesInventory.md ; head -12 Docs/CharacterDomainCut.md ; sed -n '628,650p' protocol/patterns/world-entry.md
sed -n '50,80p' protocol/game/client-to-server/0x002F.md ; sed -n '180,200p' protocol/game/client-to-server/0x0013.md
sed -n '70,100p' Document/avatar-container-entry-shape.md

# 数行数（R-7 与第五节两个总数就是这一步数出来的）
awk 'NR>=107 && NR<=113' .../behavior-change-list.md      # 第三节表体 = 7 行
awk 'NR>=179 && NR<=188' .../behavior-change-list.md      # 第五节第三张表 = 10 行，3 行「不改」

# 评审产物结构自核
sed -n '1,15p' .../reviews/change-review.md ; grep -n "^# \|^## " .../reviews/change-review.md
```

**未使用任何 git 还原类命令；未改动任何产品或判据文件；未提交任何东西；
未触碰主检出、`client-debug-lab`、`entries-are-what-the-client-reads`；`ServerS4A21` 本轮未读。**
