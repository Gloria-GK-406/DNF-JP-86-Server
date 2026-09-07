# 变化清单：装扮栏也是一个容器

契约 `./TaskContract.md` revision 1（SC-1..SC-8）／Spec `./spec.md`（RQ-001..RQ-010）。
基线 `7147d2e9`，未提交。

本清单**正向与反向都报**，且**从代码出发独立扫**，不是核对 Spec 的条目。

---

## 一、扫法

上一件成果诊断出四层错法（记在 `one-length-for-the-jewel-socket` 的 `C-1 的补充` 与 `补充之二`，
同一族失误在本仓已 8 次、无一次由自查发现）。本轮按四层各做一遍。

### 1. 搜数字与标识符（反向搜，不是顺着改动往下追）

```
grep -rn "CanOccupyMainSlot" Server/src Server/tests --include=*.cs
grep -rn "MayOccupyMainSlotAsync\|MainSlotAdmission" Server/src Server/tests --include=*.cs
grep -rn "InventoryContainerType.Avatar" Server/src Server/tests --include=*.cs
grep -rn "ListNotServed" Server/src Server/tests --include=*.cs
grep -rn "BindContainers\|containers.Register" Server/src --include=*.cs
```

### 2. 搜说法（被本轮推翻的话，里面没有可搜的标识符）

见第五节。三条里有两条是**代码注释里的散文**，不含任何被改动的标识符。

### 3. 搜索集

**初版在这里列了一份范围，而上面第 1 节实际敲的五条 `grep` 全都带
`Server/src Server/tests --include=*.cs` 限定——`Docs/`、`Document/`、`protocol/` 一次都没扫过。**
**搜索集是声明的，不是执行的。** 由独立验证方发现，代价是漏掉 5 处已被本轮推翻的记载（见第五节）。

这是第三层错法（搜索集本身没被检查）的一个新形态：前几次是**范围漏了东西**，
这次是**范围写对了但没照它执行**。**下一个人：把声明的范围和真正敲下去的命令逐条对一遍。**

**补扫后实际执行的命令**（`.md` 侧，**§一.1 的五条逐条重跑**，排除 `.rks-agent/` 与 `.worktrees/`）：

```
grep -rn "CanOccupyMainSlot"                       --include=*.md .   # 2 命中，仍成立（方法未改）
grep -rn "MayOccupyMainSlotAsync\|MainSlotAdmission" --include=*.md . # 2 命中，均为本轮改名造成的死标识符 → 已改
grep -rn "InventoryContainerType.Avatar"           --include=*.md .   # 0 命中
grep -rn "ListNotServed"                           --include=*.md .   # 5 命中
grep -rn "BindContainers\|containers.Register"      --include=*.md .   # 0 命中
```

**第一次补扫只跑了这五条里的一条**（`ListNotServed`），由第二轮独立验证指出——
**「承认没执行」和「执行」是两回事**，这一条同样要记。

**范围**：`Server/src/`、`Server/tests/`（`.cs`）；`protocol/`、`Docs/`、`Document/`、`AGENTS.md`（`.md`）；
`.rks-agent/tasks/the-avatar-bag-is-a-container-too/`（本轮全部产物）；
只读参照 `E:/Project/DNF/ServerS4A21/`。

**未纳入**：`.worktrees/client-debug-lab`、`.worktrees/entries-are-what-the-client-reads`、
主检出的 `ClientHack/` —— 契约禁止触碰。

### 4. 连证据一起看（第四层）

改一处散文时同时看它引用的表、代码块、行号。本轮触发了一次：
`MoveItemSpaceCommandHandler` 的类注释把 avatar 列在「本服务器不答应的容器」里，
而那句话下面就是它据以成立的三条服务规则——**两者必须同时改**，见 S-4。

**这一层本轮做到了，第二层没有做到**：一段不含任何标识符的因果论证被漏掉，
由评审找出（C-4）。第一节这套搜法**不足以覆盖第二层**，如实记在这里。

---

## 二、正向：可观察的行为变化，共 2 类 5 条

| # | 变化 | 之前 | 现在 |
|---|---|---|---|
| F-1 | 入场时装扮栏被绑定为容器 | 只绑主背包与装备栏 | 三个都绑 |
| F-2 | 装扮栏 → 装扮栏 的移动 | `ListNotServed`（`ItemId=0`） | 交给该容器的 `MoveToAsync` |
| F-3 | 装扮栏 ↔ 主背包 双向 | `ListNotServed` | 由新的 `CarryAcrossAsync` 搬运 |
| F-4 | 装扮栏 → 装备栏 | `ListNotServed` | 走到 `PutOnAsync`；今天**仍被拒**，但理由是 `NotWearable` 且**带 itemId** |
| F-5 | 装备栏 → 装扮栏 | `ListNotServed` | 走到 `TakeOffAsync`，落进装扮栏 |

**F-4 是本件今天对玩家唯一可见的净效果，而它仍然是一次拒绝。** 时装还是穿不上——
槽位映射是 issue #11。变的是拒绝**由谁做出**：从「这两个列表号我不服务」（什么都没读）
变成「我读到了这一件，但档案没说它进哪个槽」（读到了，说得出是哪一件）。
**日志因此从 `item 0` 变成 `item 413500000`**，那正是 issue #10 的第二条接受条件，
也是量 0..11 槽位映射的量具。

### 没有第六条

主背包内的移动、主背包↔装备栏、以及任何未服务列表对的**线上行为逐值不变**——
`PutOnAsync` 的可执行语句与基线逐字符相同（第三轮评审剥掉注释逐行核对确认）。
`CanOccupyMainSlot` 一字未改，它的既有判据一条未动。
**一处不在「线上行为」之内但要说明**：`Equipment → Main` 的三处拒绝，
服务器日志现在带得出物品号（见 R-6）。线上应答码不变，玩家侧无差别。

---

## 三、反向：新增或改变的拒绝与放行路径，共 7 条

> **R-1..R-6 是初版的六条；R-7 由独立评审指出本清单漏报后补上。**
> （R-7 一度只写在后来被删掉的那一节里，随之丢失，而表头仍说七条——
> 由第三轮独立验证数出来。**改完一张表要回头数一遍行数。**）
> 曾经有过一条 R-8（`PutOnAsync` 里对被顶下来那件的准入判定），**它已被删掉**——
> 那条判定问错了问题，而且越出了本轮授权面。经过见第八节。

**从代码出发逐处扫产线的 `throw` / `return Refused` / `switch` 分支，不是核对上面的正向表。**

| # | 位置 | 是什么 |
|---|---|---|
| R-1 | `SelectCharacterEntryInventoryDomainService.BindContainers` | **新增一条生产拒绝**：入场库存里没有装扮栏容器时抛 `InvalidDataException`。与既有的 main / equipment 两条同形。**生产不可达**：`InventoryEntryProjection.ProjectCharacter` 的 `CharacterContainerOrder` 无条件产出全部五种容器。它抓到的是**夹具**——本轮有 4 个既有用例因此变红，见 S-5。 |
| R-2 | `ItemSlotRuleService.CanOccupySlot` | **新增一条默认拒绝**：`_ => false`。凡没有写下规则的列表一律不放行。这是新增的放行面之外的**兜底拒绝**，也是 M1 变异唯一能被抓住的原因。 |
| R-3 | `MoveItemSpaceCommandHandler` | **新增两条 `NoBoundContainer` 拒绝路径**：穿戴/脱下时源或目的容器未绑定。之前只有主背包一处这样判。生产可达性：仅当入场绑定失败，而那时 R-1 已经抛了。 |
| R-4 | `CarryAcrossAsync` | **新增四条拒绝**：`BothSlotsEmpty`（源格空）、`NotWearable`（档案不认识该物品）、`ItemCannotOccupySlot`（目的列表不收）、`DisplacedCannotOccupySlot`（交换时被顶出来的那件回不去源格）。四条都可达。 |
| R-5 | `StoreBackedItemContainer.DecideMoveAsync` | **一处已有拒绝的判据变了**：由主背包的 band 改为**本容器的**规则。对主背包**逐值不变**（`CanOccupySlot(Main,…)` 直接调 `CanOccupyMainSlot`，并有判据钉住两者对 4 种物品 × 8 个槽位答案相同）。对**装备栏容器**：`CanOccupySlot(Equipment,…)` 恒 false，所以在装备栏容器上调 `MoveToAsync` 现在必然拒绝——**生产不可达**，处理器不服务装备栏→装备栏，且 `PutOn`/`TakeOff` 用的是 `PutAsync`/`ClearAsync` 而非 `MoveToAsync`。 |
| R-6 | `TakeOffAsync` | **一处已有拒绝的判据变了**：落点准入由「主背包的」改为「实际落进的那个容器的」。脱到主背包时**拒绝条件逐值不变**；脱到装扮栏时按装扮栏规则判。另有**三处**拒绝（`NotWearable`、`ItemCannotOccupySlot`、`DisplacedCannotOccupySlot`）改为在结果里带上被脱下那件的物品号——**拒绝条件与线上应答码都未变，变的只有服务器日志**。 |
| R-7 | `MoveItemSpaceHandler.ErrorCodeFor` 的**下游效果**（非本轮改动的代码） | 涉及装扮栏的请求，在装扮栏**未绑定**时，过去落在 `ListNotServed → NotMovable(23)`，现在落在 `NoBoundContainer → Silent(2)`——客户端由「不可移动」变为「静默弹回」。**没有一行代码为此改动**，是新的分发路径把请求送到了另一条既有拒绝上。生产不可达（入场绑定失败会先抛 R-1）。**本清单初版漏报，补记时又把两个码写反、场景也取错，此处为第三版。** |

**被移除的生产拒绝：0 条。**
**新增放行面：恰好 F-2..F-5 四类，不多不少**——由 M1 变异反向证明（见第六节），
并经独立评审自行复现同一变异确认。

---

## 四、非行为改动（结构与记载）

| # | 内容 |
|---|---|
| S-1 | `IItemSlotRuleService` 新增 `CanOccupySlot(InventoryContainerType, ItemPlacement, int)`。`CanOccupyMainSlot` **保留且一字未改**，新方法对 `Main` 直接委托给它——一处实现，不是两份。 |
| S-2 | `IItemMovementDomainService.MayOccupyMainSlotAsync` → `MayOccupySlotAsync(containerType, …)`；枚举 `MainSlotAdmission` → `SlotAdmission`。**纯改名 + 加一个参数**，零判据受影响（全仓命中只有 3 个生产文件）。改名的理由是留着 `Main` 这个名字会让它在服务第二个列表之后变成谎话。 |
| S-3 | `ItemSlotRuleService` 新增两个私有常量 `AvatarSlotStart = 0` / `AvatarSlotEnd = 209`，注释标明**上游源码读出、非实机**。 |
| S-4 | 三处注释按已查明改写：`MoveItemSpaceCommandHandler` 的类注释（它把 avatar 列在「不答应的容器」里）、`DecideMoveAsync` 的注释（它说主背包是唯一被服务的列表）、`BindContainers` 的注释（补上装扮栏那一段，并点名它与 2026-08-26 装备栏那次是同一形态）。 |
| S-5 | 判据：新增 3 个文件共 **46 条**（`AvatarListHasASlotRuleOfItsOwnTests` 25、`TheAvatarBagIsServedTests` 15、`MovingInsideTheAvatarListTests` 6）；修改既有 2 个文件（见下 T-1、T-2）。 |

### T-1：被修改的既有判据文件之一

`SelectCharacterEntryLifecycleServiceTests.cs`

| 改动 | 归因 |
|---|---|
| 4 处夹具各补一行 `InventoryContainerState(Avatar, 0, [])` | R-1。**这是夹具在追上现实**：生产的入场投影无条件产出全部五种容器，夹具此前只造两种，是它不像一个真的入场库存 |
| 用例改名 `..._main_and_equipment_containers` → `..._main_equipment_and_avatar_containers` | F-1 |
| 该用例末尾**新增** 3 行断言（装扮栏已绑、类型正确、不可移除） | F-1 |

**没有任何断言被放宽或删除**，也没有任何期望值是为迁就实现而改的。
M2 变异（去掉绑定）证明这条判据真的在钉住它。独立评审逐条核过这份 diff，
并确认「给夹具补 avatar」**不是**在掩盖生产缺陷。

### T-2：判据支持代码（评审要求真容器判据后新增）

`Server/tests/Dfo.Server.Persistence.Tests/Support/InventorySide.cs`：
`BindMain` 改为委托给新的 `BindList(containerType, …)`，**行为逐值不变**，
既有 **17 处调用点（5 个文件）** 一处未改
（口径：`grep -rn "\.BindMain(" Server/tests --include=*.cs`；初版写「三个调用方」是错的，
由第二轮评审 F-6 指出）。加它是因为真容器只能绑主背包，
而本轮要验的正是「真容器 + 装扮栏形状」这个此前全仓不存在的组合。

---

## 五、本轮推翻的说法

| # | 说法 | 出处 | 处置 |
|---|---|---|---|
| C-1 | 「The other containers this request can name -- **avatar**, cargo, pet … answering for them without those rules would let an item through a door that should have been shut」 | `MoveItemSpaceSlice.cs` 类注释 | 改写：装扮栏的规则已写下（`CanOccupySlot`），它不再在那份名单上；并点明「没写下规则」正是那次拒绝说不出物品号的原因 |
| C-2 | 「The band consulted is the main list's, **which is the only list this server serves**」 | `ItemContainers.cs` `DecideMoveAsync` 注释 | 改写。**原注释同时预言了正确做法**（「a second served list … belongs in the rule service beside this one rather than as a branch here」），本轮照它做了，改写时把这一点说出来 |
| C-3 | 「Wearing a piece is a move between the two lists, so **both ends** have to be bound」（隐含只有两个列表要绑） | `SelectCharacterEntryInventoryDomainService.cs` 注释 与 `SelectCharacterEntryLifecycleServiceTests` 的用例名 | 两处一并改：注释补第三段，用例改名并加断言 |

**初版写「没有找到第四条」，那是错的。** 第四条由独立评审找出：

| # | 说法 | 出处 | 处置 |
|---|---|---|---|
| C-4 | 「Whatever was already worn goes back into the slot the new piece came out of. **That slot held a piece of equipment a moment ago**, so it is a slot equipment may occupy and there is no second judgement to make」 | `MoveItemSpaceSlice.cs` `PutOnAsync` 的注释 | 那是一句关于**背包**的论证，而源列表不再恒为背包，所以它不再成立。**结论（不做第二次判定）仍然成立，理由换了**：`ByName` 最低只到 12，装扮槽上站不了任何东西，displacement 无从发生。注释已按此改写，并点名 issue #11 落地时要回来重判。见第八节 |

**它为什么没被扫到**：它是一段**散文里的因果论证**——不含任何被改动的标识符，
不含任何数字，主题词也不是「容器」「列表」「绑定」中的任何一个，而是「被顶下来的那件去哪」。
按第一节的三个主题词扫不到它。**这是四层错法里第二层（搜说法而不是搜标识符）在本轮的复发。**

**「协议文档零改动」这句初版断言是假的**（独立验证方发现）：本件确实不碰任何帧的字节布局，
但 `protocol/` 下有一处**散文**记着旧行为。补扫后逐处处置如下：

| 出处 | 说了什么 | 处置 |
|---|---|---|
| `AGENTS.md:686-697` | 量具说明书的两条前提：拖到装扮栏得 `item 0 … ListNotServed`；`TakeOffAsync` 五个拒绝都不带模板号 | **就地更正，且改过两次**。第一次的更正**本身是错的**：写成「目标列＝1 现在走得到 `PutOnAsync`、被拒为 `NotWearable`、量具因此变好了」——`PutOnAsync` 的入口条件是**目标列 == 3**，目标列 == 1 走 `CarryAcrossAsync`，时装会被真的搬进装扮栏、**一行拒绝都没有**。由第二轮独立验证指出。现文写明「那条路是哑的，别走它」，并写明实机观测到的目标列是 `Equipment`(=3)、那条路逐字符未变、量具照旧管用。`TakeOffAsync` 那条（三带两不带）经验证复核准确 |
| `protocol/game/server-to-client/NOTI-0x000D.md:418` | 「装扮栏里拖动会被拒为 `ListNotServed`」 | 原文留记 + 加注更正 |
| `Document/avatar-container-entry-shape.md:96` | 「装扮栏的移动路径没接……纯服务器侧的缺口」 | 原文留记 + 加注「已补」 |
| `Docs/Refactor/DomainInstanceDesign.md:131` | 「both are **currently** refused with `ListNotServed` **because only the main list is served**」 | 改为「两个列表被服务」，并说明它的论证不受影响 |
| `protocol/patterns/data/traces/a21-tutorial-ending-20260826.md:21` | 「`MoveItemSpaceCommandHandler` 写死了只服务 Main→Main」 | **不改**。它是 2026-08-26 的**实机 trace 归档**，记的是当时的观测；且它说的是装备栏那次（同日即已修复），与本轮无关。归档记录按仓内惯例不追改 |
| `Docs/CreatureDomainDesign.md:233` | `MayOccupyMainSlotAsync` —— **本轮改名造成的死标识符**（`Server/` 内零命中） | **就地更正**为 `MayOccupySlotAsync` 并注明旧名 |
| `Docs/ItemWorldModel.md:93` | `MainSlotAdmission` —— 同上 | **就地更正**为 `SlotAdmission` 并注明旧名 |
| `protocol/game/server-to-client/NOTI-0x000E.md:200-207` | 把「非时装进不来装扮栏」列为将来的事 | **加更正**：第 2 道门的**移动侧**已立；但门只管移动不管既有的行，且第 1 道门仍未做，**其崩溃论证与 RQ-006 照旧成立** |
| `Docs/CharacterEntryFacesInventory.md:167`、`:248`；`Docs/CharacterDomainCut.md:74` | 「`MoveItemSpace` 今天只认 `Main`/`Equipment`」 | **不改**。三份页首均注明基准 `51cd99c2`，是那个时点的快照，不是现状陈述 |
| `protocol/patterns/world-entry.md:642` | 同一说法 | **不改**，编年叙述，同 trace 惯例 |

**上表 10 行：更正 7、不改 3**（口径：按行计，其中「不改」的两行各含多个出处）。
更正的 7 行与本轮实际改动的 7 个 `.md` 文件一一对应，可用
`git diff --name-only | grep '\.md$'` 复核。

**这个计数改过三次**：第一次补扫报「5 处」（漏了后一半）；补齐后写「11 处（更正 5、不改 6）」，
**两种口径都不成立**（第四轮独立验证机械数出来）；此处为第三版。
**教训与 R-7 那次同源——写下一张表的总数之前，先数一遍它自己。**

---

## 六、证据

### 判据

| | 值 |
|---|---|
| 基线（`7147d2e9`，主检出与本 worktree 各实测一次，一致） | **2289 通过 / 2 跳过 / 0 失败**，退出码 0 |
| 现在 | **2335 通过 / 2 跳过 / 0 失败**，退出码 0 |
| 差额 | **+46** ＝ 新增判据 46 条（Application +31、Protocol +15） |

字节锚 `SelectCharacterAcceptedTranscriptTests` 的 62 帧**未变**（本件不碰编码器，该文件不在 diff 里）。

### 变异验证（文件级备份 + `sha256sum`，每次用完立刻还原并核散列；未使用任何 git 还原类命令）

| 变异 | 做了什么 | 红了几条 | 红的是谁 |
|---|---|---:|---|
| **M1** | `CarriedListFor` 改成 `(InventoryContainerType)listType`——即**不再看列表号，任何列表都当作可携带列表** | **5** | **全部是 `A_list_this_server_does_not_serve_is_still_refused` 的 5 个 InlineData，其余 574 条 Protocol 判据一条没红** |
| M2 | `BindContainers` 去掉 avatar | 1 | `Entry_binds_the_character_to_its_own_main_equipment_and_avatar_containers` |
| M3 | 装扮栏规则去掉 `WorldClass == Avatar`（只剩区间判定） | 4 | `The_avatar_list_takes_nothing_but_costumes` 的 3 个 InlineData + `Something_that_is_not_a_costume_does_not_get_into_the_avatar_list` |

**M1 是本轮最重要的一条证据。** Spec 预判「最省事的错误写法是不再看列表号」。
M1 把它变成了实测：**Protocol 套件里除了专为此写的那 5 条，一条都没红**
（独立评审自行复现，同样的 5 红 / 574 绿、同一份名单）。

**但「会原样通过全部判据」这句话是假的，本清单初版这么写过。** 独立验证方跑全量后指出：
基线上还有一条会红——`MoveItemSpaceHandlerTests.A_list_other_than_the_main_one_is_refused`
（Application 套件，目的列写个人货柜）。所以准确的说法是：
**基线只有一条判据挡这个错法，且只覆盖「个人货柜作目的地」一种**；
新增那 5 条把源、目的、两端三个方向都覆盖了。差别在覆盖面，不在有无。

每次还原后 `sha256sum -c` 全 `OK`，复跑全绿。
（上表三次做于第一轮，当时的全量计数是 2326，现为 2335。评审各轮另做过若干次变异，记在评审产物里。）

### 「一处实现」的证据

```
$ grep -rn "CanOccupyMainSlot" Server/src --include=*.cs
IItemSlotRuleService.cs:21          ← 接口声明（未改）
ItemSlotRuleService.cs:48           ← 唯一实现（未改）
ItemSlotRuleService.cs:41           ← CanOccupySlot 对 Main 的委托
（ItemContainers.cs 与 ItemMovementDomainService.cs 的两处调用已改为按容器问）

$ grep -rn "CanOccupySlot" Server/src --include=*.cs
IItemSlotRuleService.cs             ← 声明
ItemSlotRuleService.cs:38           ← 唯一实现
ItemContainers.cs ×2                ← 目标格与被顶出的那件
ItemMovementDomainService.cs        ← 供处理器问
```

并有判据 `The_main_list_answers_the_same_either_way` 钉住两种问法对
4 种物品 × 8 个槽位答案完全相同。**它今天不可能红**——新方法对 `Main` 是直接委托，
所以它证明的是「只有一份实现」，**不是**主背包规则的内容（评审 N-3，如实照记）。

### RQ-007 要求的现场查证（Spec 点名必须做的一件）

用真实档案查三个时装模板，全部答：

```
PROBE itemId=413500000 => kind=8 stack=1 band=9..64 worldClass=Avatar
PROBE itemId=413550000 => kind=8 stack=1 band=9..64 worldClass=Avatar
PROBE itemId=413600000 => kind=8 stack=1 band=9..64 worldClass=Avatar
（对照：itemId=1000 => kind=2 stack=1000 band=65..120 worldClass=Interchangeable）
```

**时装在主背包里有 band（`9..64`，装备带）**，所以 RQ-004 的「装扮栏→主背包」可达，
不触发 Spec 里那条「停下报告」。探针是一次性的，取完即删（`git status` 已确认无残留）。

---

## 七、本清单的已知局限

- **本件没有实机验证，也不打算有。** `NotWearable` 与 `ListNotServed` 在客户端都表现为
  「拖不动、弹回去」，**玩家侧看不出区别**；能分辨的只有服务器日志。
  跑一次实机可以看到日志从 `item 0` 变成带模板号，那是佐证不是判据。
- **装扮栏容量取的是上游读数 `0..209`，本仓从未实机验证过这个上限。**
  issue #12（`listParam16` 的语义）仍未清，本件不碰。
- **「客户端会不会提供主背包↔装扮栏的拖拽」未观测。** 本件按「服务它」实现，
  依据是 issue #10 的接受条件与上游的槽位区间，不是观测。
- **`R-5` 里「装备栏容器上调 `MoveToAsync` 现在必然拒绝」这一条是代码读出的不可达，未写判据。**
  写一条断言它的判据等于把一条不可达路径钉成契约，反而会挡住将来真要服务装备栏内移动的人。


---

## 八、评审经过（四轮）

| 轮 | 阻塞 | 其中真的生产缺陷 |
|---:|---|---|
| 一 | 3（无真容器判据 / `PutOnAsync` 漏一次准入判定 / 文档插错位置） | **1**（那次准入判定） |
| 二 | 3 | 0 |
| 三 | 2 | 0 |
| 四 | **0** | 0 |

**唯一的生产缺陷，最终的正确处置是把它回退掉。** 第一轮判我在 `PutOnAsync` 漏了一次准入判定，
我补上；第二轮查出那次判定**越出授权面**——它对源列表是主背包时同样生效，
会拒绝一次今天能成功的交换（耳环在快捷栏、24 槽戴着契约兽）。
所有者据此重新定性：

> 要检查的不是能不能互换，而是**能不能装备**。不能装备直接拒绝。能装备就意味着交换是合法的。

判定因此删除，主背包→装备栏**逐字符回到基线**（第三轮评审剥掉注释逐行核对确认）。

**第三轮又查出我为这次回退写的理由是假的**：我写「穿戴槽决定站在其上那件的类别」，
而 `ByName` 把 `[earring]`、`[support weapon]`、`[creature]` 同映槽 24——**同一段注释的下一段就是这个反例**。
今天成立的是一句窄得多的话：**`ByName` 最低只到 12，所以装扮槽 0..11 上永远站不了任何东西**，
displacement 根本无从发生。这是**空真**，而 issue #11 给 0..11 补键时会按构造摧毁它——
`PutOnAsync` 的注释已点名要求那个人回来重新判断。
一条名为钉住此事、实则用槽 14 且夹具自相矛盾的判据已删除，换成钉「装扮槽装不上东西」的两条。

### 剩下的两条债，都不在本轮范围

- **快捷栏 / 契约兽**：`ByName` 让耳环与契约兽同映槽 24，戴耳环会把契约兽顶进背包，
  而契约兽进不了快捷栏。本仓 `Docs/CreatureDomainDesign.md` 记着这条，**另开 issue**。
- **#11 落地时要回来重判 `PutOnAsync`**：见上，注释里已写明。

### 这一节为什么这么短

初版的第八、九节是逐轮的整改流水账，三轮下来**每修一处就写进约一处新的不实**
（第二轮 2 处假声称 → 第三轮 3 处），且第三轮的两条阻塞里有一条的唯一来源就是它自己。
第三轮评审据此建议「用删除修，不要用新增修」，本节即照此重写：
**只留下会影响后来者决定的东西，逐轮的自述已删。**
被删内容的完整经过在 `./reviews/change-review.md` 的三节里，那是评审方写的，不是我的自述。
