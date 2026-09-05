# TASK-12 — 仓库约定与行为变化清单

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 在仓库约定中记下这套建模与本轮**没做**的部分；汇总行为变化清单，仅 RQ-009 与 RQ-010 两处，每条指到具体代码位置。
- Referenced spec requirements: RQ-012, RQ-015
- Referenced plan sections: T-012
- Owned boundary: BD-013, BD-015
- Permitted files or interfaces: `AGENTS.md`、`.rks-agent/tasks/items-own-their-identity/` 下的成果记录
- Dependencies: TASK-11
- Required dependency outputs: 全部已完成任务的证据
- Acceptance conditions: 仓库约定记载与代码一致，且明说不清扫孤儿、不启用无归属物品的用法、不做契约兽领域本身、不裁契约兽槽位、不动那五个含义未查清的列、旧库作废重建；行为变化清单逐条属实且清单之外没有其它行为变化。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-026 `Non-command: 对照代码阅读仓库约定新小节，逐个数字与陈述复核`
- CK-027 `Non-command: 逐条回到行为变化清单声称的位置核对`
- CK-028 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败

- Required evidence: 仓库约定的 diff；行为变化清单；每个数字的复算依据。

### 记载纪律

清单中的每个数字须**当场复算**，并**按内容而非行号索引**——本仓有过在只追加的文件里列行号、随后行号全部漂移的先例。

## Execution Result — round 1

### 改了哪些文件

**三个，全在允许清单内。允许清单之外一个字未动。**

| 文件 | 改法 |
|---|---|
| `AGENTS.md` | 新增一个 `###` 小节（见下），插在「CMD ⇒ Notify」之后、「判据偶发失败」之前。**纯追加，没有删改任何既有文字，没有放宽任何既有约定。** |
| `.rks-agent/tasks/items-own-their-identity/behavior-change-list.md` | **新建**。行为变化清单 ＋ 十五条账的完整版 ＋ 一条订正 ＋ 一条额外发现 ＋ 逐项目计数。 |
| `.rks-agent/tasks/items-own-their-identity/tasks/TASK-12-conventions-and-the-change-list.md` | 本节与下一节，追加。 |

生产代码 0 改动，判据 0 改动。三个文件均无 UTF-8 BOM
（`head -c 3` 分别是 `23 20 41`、`23 20 e8`、`23 20 54`，都不是 `ef bb bf`）。

### `AGENTS.md` 新增了什么

小节标题 **「物品：有身份的东西，和它在哪，是两张表」**，六个子块：

1. **建模**：`items` 世界表 20 列（身份与 core 2 ＋ 归属与时间 3 ＋ 时装 7 ＋ 契约兽 8）、
   `item_slots` 位置表 8 列与它的一条 UNIQUE 加三条具名 CHECK；
   `core_uid` 为什么是关键（一个列解两笔债）；分类由档案回答、唯一实现只收模板号。
2. **本轮明确没有做的**：六条，逐条写清（清理器／无归属用法／契约兽领域／24 还是 25／
   `character_condition` 五列／旧库作废重建）。
3. **被授权的两处行为变化**：各写起因与连带，并指向完整清单文件。
4. **三处「结论对，但撑着它的不是代码」**：限定一条不省。
5. **已知缺陷与覆盖缺口（只记不修）**：七条。
6. **一条已被证伪的记载，订正**：孤儿是否跨存盘易逝。

### 行为变化清单在哪

`.rks-agent/tasks/items-own-their-identity/behavior-change-list.md`。

**只有两处被授权，连带后果逐条列全：**

- **RQ-009**（契约兽取下之后能装回）：起因是 `PvfEquipmentSlots.ByName` 一个键，
  连带 **A/B/C/D 四条**——A 装得回去（授权变化本身）、B 槽 24 已有契约兽时成为互换、
  C 从槽 24 取下遇到另一只成为互换（**只在源槽为 24 时**）、
  D 内部拒绝理由 `NotWearable` 变 `ItemCannotOccupySlot`
  （`ErrorCodeFor` 把两者映射到同一个 `NotMovable`，**客户端字节不变，但仍然列出**）。
- **RQ-010**（种类判定取档案，1 变 5）：连带 **A/B/C 三条**——
  A 写进存储的种类字节由 1 变 5（授权变化本身）、
  B **契约兽不再能被拖进快捷栏 3..8**（第二处真实可观察变化，无既有判据覆盖故无期望值改动）、
  C `ItemArrivalRuleService.BandsFor` 两条分支产出相同，**无变化**。

**清单之外我没有发现第三处行为变化。** 核法与它的边界原文写在清单的
「清单之外没有其它行为变化 —— 我是怎么核的，以及这个核法的边界」一节。

### 三项检查的实际输出

**CK-026** `Non-command: 对照代码阅读仓库约定新小节，逐个数字与陈述复核` → **通过**

新小节里的每个数字都是我当场从代码数出来的，没有从任何 Task 文件抄：

| 陈述 | 复算方式 | 结果 |
|---|---|---|
| `items` 20 列 | 读 `CharacterDatabaseSchema.cs` 的 `CREATE TABLE items` 单行 DDL，逐列点 | 2+3+7+8 = **20** |
| 时装 7 列（`avatar_uid` ＋ 六列细节） | DDL 里 `avatar_` 前缀列逐个点；六列细节另由 `SqliteItemDomainStore.AvatarColumns` 的 6 个条目（index 0..5）印证 | **7 / 6** |
| 契约兽 8 列 | DDL 里 `creature_` 前缀列逐个点 | **8** |
| `item_slots` 8 列 ＋ 1 UNIQUE ＋ 3 具名 CHECK | 读同文件 `CREATE TABLE item_slots` 单行 DDL | 8 列；`UNIQUE(owner_scope,owner_id,list_type,slot_index)`；`slot_count_is_positive` / `world_slot_holds_exactly_one` / `world_slot_names_its_own_row` |
| `ClassifyAsync` 只收模板号、生产侧 6 个调用点 | `grep -rn ClassifyAsync Server/src`，剔除接口声明、实现本体与实现内自调 | 签名 `ValueTask<ItemWorldClass> ClassifyAsync(int itemId, …)`；调用点 `InventoryEntryAssembly` 二、`ItemContainers` 二、`CharacterRosterService` 一、`DailyRefillService` 一 = **6** |
| `ItemWorldClass` 四值 | 读 `ItemPlacementContent.cs` | Interchangeable=0 / Equipment=1 / Avatar=2 / Creature=3 |
| `LiftAsync` / `NoContainer` 零命中 | `grep -rn` 于 `Server/src Server/tests --include=*.cs` | **无输出** |
| `ByName` 16 个键、无 avatar 键、`avatar` 三处全在注释 | 读 `PvfEquipmentSlots.cs` 全文逐条点；`grep -n avatar` | **16 键**、13 个不同槽号、`avatar` 命中 3 处全在 `///` 里 |
| 快捷栏 3..8、排除 5/6/7/8/9 | 读 `ItemSlotRuleService.cs` | `QuickSlotStart=3`、`QuickSlotEnd=8`、`AllowsQuickSlot` 排除 5..9 |
| 契约兽种类 5、普通穿戴件 1 | 读 `PvfEntryLifecycleContentSource.cs` | `CreatureItemKind = 5`、`WornPieceItemKind = 1`，`WornPieceMetadata` 三元选择 |
| CHECK 在 HEAD 不存在、调和只有 ADD COLUMN | `git show HEAD:…CharacterDatabaseSchema.cs` 后 grep 计数；`grep -rn "ADD COLUMN" Server/src/Dfo.Server.Persistence/` | HEAD **0**、工作区 **1**；`ADD COLUMN` 全仓 **1 处** |
| `ConsumeAsync` 生产调用方 1 处、门是档案数据 | `grep -rn ConsumeAsync Server/src`（剔除二进制）；读 `ItemUseEffectDomainService` 与 `ConsumeAsync` 本体 | 调用方 **1**；门是 `if (rule.IsConsumedOnUse)`，`rule` 来自 `PvfItemUseRuleDefinitionSource`；`ConsumeAsync` 本体只校验 count，**无身份守卫** |
| 三处「清空先于填入」注释 | 读 `SqliteItemDomainStore.cs` | `ApplyDecidedWritesAsync` / `FlushAsync` / `EmptyAsync` 各一处 |
| `spared` 的落点是 `AND item_uid NOT IN (…)` | 读 `EmptyAsync` 的 SQL 拼接 | 是 `item_uid`，**不是** `core_uid`（`core_uid NOT IN` 是 `DeleteOwnExceptAsync` 的谓词）。初稿写错，已改正 |
| 「非时装不得带细节」等三条断言在 tests 零命中 | 三条异常文案分别 `grep -rn` 于 `Server/tests` | 全部 **0 命中** |
| 堆叠物 core 的两个非默认值都上线 | 读 `StackableItemCore.Create` 与 `GameInventoryDelivery` | `Marker16=-1`、`RandomOptionChangedIndex=byte.MaxValue`；`ItemKind == 2` 走不钳那一支 |
| `avatar_uid` 仍在、还有唯一部分索引 | `grep -n avatar_uid` 于 schema 与 src | `CREATE UNIQUE INDEX … ON items(avatar_uid) WHERE avatar_uid IS NOT NULL` |

**一处初稿写错、已改正**：`spared` 的落点谓词（见上表倒数第四行）。**这是本轮唯一一次数字/谓词返工。**

**CK-027** `Non-command: 逐条回到行为变化清单声称的位置核对` → **通过**

逐条回核：

- **RQ-009 起因**：`PvfEquipmentSlots.cs` 的 `["[creature]"] = 24` 存在。
  它的生产消费点：`grep -rn WornSlotForAsync Server/src` 共 7 行，其中接口声明 2、
  `ItemMovementDomainService` 转发 1、`ItemPlacementRuleService` 实现 1，
  **真正的消费点恰好 2 处**，都在 `MoveItemSpaceSlice.cs`（`PutOnAsync` 一、`TakeOffAsync` 一）。
- **后果 A/D**：读 `PutOnAsync` 的分支——`slot is null` 到 `NotWearable`、
  `slot.Value != Destination` 到 `ItemCannotOccupySlot`——与清单一致。
  判据 `CreatureGoesBackOnTests` 里恰好 2 个 `[SkippableFact]`，名字对得上。
- **后果 B**：`PutOnAsync` 的 `displaced is null ? bag.ClearAsync : bag.PutAsync` 分支存在，
  未为契约兽新增代码。
- **后果 C**：`TakeOffAsync` 里读 `WornSlotForAsync(displaced.ItemId)` 存在；
  **无判据覆盖**（`CreatureGoesBackOnTests` 那 2 条都不是它）。
- **后果 D 的字节不变**：`ErrorCodeFor` 里 `NotWearable` 与 `ItemCannotOccupySlot`
  **都**映射到 `MoveItemSpaceResponse.NotMovable`。
- **RQ-010 起因**：`WornPieceMetadata` 的
  `worldClass == ItemWorldClass.Creature ? CreatureItemKind : WornPieceItemKind`。
  它的下游：`grep -rn "placement.ItemKind" Server/src` 共 5 行 3 个文件——
  `ItemArrivalRuleService`（2 行，同一段判定）、`ItemContainers`（写入）、`ItemSlotRuleService`（快捷栏）。
- **后果 B（快捷栏）**：`CanOccupyMainSlot` 的 `slotIndex >= 3 && <= 8 && AllowsQuickSlot(kind)`
  与 `AllowsQuickSlot` 排除 `KindCreature = 5` 都在。
- **后果 C（无变化）**：`BandsFor` 两条分支我逐行读过，产出相同。

**清单之外没有其它行为变化**：两处授权变化的起因各收敛到一个符号，
两个符号的生产消费点分别恰好 2 处与 3 个文件，逐个读过分支后没有第五条/第四条落网。
**我没有发现第三处，因此没有触发「停下报告」。**
这个核法的边界（没跑真客户端、没独立复核其余九个任务的字节不变）**原文写进了清单**。

**CK-028** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
→ **退出码 0**

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 315 | 0 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| **合计** | **2215** | **2** | **0** |

逐项目相加复算：60+52=112；+521=633；+6=639；+236=875；+315=1190；+1025=**2215**。
**逐项目相加得到，没有用 `grep -c`。** 不低于基线 2215/2/0。
两条跳过都在 Infrastructure，是 PVF 档案缺失时的 SkippableFact
（`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`、
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`）。

**这一跑在我开始改动之前发起、而本轮改动全是 markdown**，所以它同时是基线也是终态；
markdown 不参与编译，本轮不存在能改变计数的改动。

### 十五条账各自落在记载的哪一处

| # | 账 | 落点 |
|---|---|---|
| 1 | CHECK 只对新建库无条件成立 | `AGENTS.md` 的「三处结论对，但撑着它的不是代码」第 1 条 ＋ 清单「三条」表第 1 行 |
| 2 | `ConsumeAsync` 只经 item-use 由档案保证、无代码守卫 | `AGENTS.md` 同节第 2 条 ＋ 清单「三条」表第 2 行 |
| 3 | 「清空先于填入」不再承重且无判据钉住 | `AGENTS.md` 同节第 3 条 ＋ 清单「三条」表第 3 行 |
| 4 | `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 名字过时 | `AGENTS.md` 的「已知缺陷与覆盖缺口」第 6 条 ＋ 清单「两条判据名字」表第 4 行 |
| 5 | `…TheOneThatMovedWasRebuilt` 正向断言指向另一个新增索引 | 同上（同一条 bullet 的后半）＋ 清单同表第 5 行 |
| 6 | 时装与宝珠同样「取得下、装不上」 | `AGENTS.md` 的「已知缺陷」第 1 条 ＋ 清单「四条缺陷」表第 6 行 |
| 7 | 时装的种类不一致仍在（存储 8 / 档案 1） | `AGENTS.md` 的「已知缺陷」第 2 条 ＋ 清单同表第 7 行；另在 RQ-010 一节的「明确没有变的」再点一次 |
| 8 | 变化 3（源槽 24 取下遇另一只 → 互换）无判据 | `AGENTS.md` 的「已知缺陷」第 5 条 ＋ 清单「后果 C」正文 ＋ 清单同表第 8 行 |
| 9 | 「非时装不得带细节」与三条取值校验全仓无断言 | `AGENTS.md` 的「已知缺陷」第 4 条 ＋ 清单同表第 9 行 |
| 10 | 孤儿的答案跨存盘易逝 | **已证伪，改为订正**：`AGENTS.md` 的「一条已被证伪的记载，订正」＋ 清单「四条形状与代价」表第 10 行（指向）＋ 清单「订正 T12-1」全文 |
| 11 | 一个列（`core_uid`）解掉两笔债 | `AGENTS.md` 建模一节的 `core_uid` 段 ＋ 清单「四条形状与代价」表第 11 行 |
| 12 | 同一件东西被两个位置同时指名；装载侧无对称拒绝 | `AGENTS.md` 的「已知缺陷」第 3 条 ＋ 清单同表第 12 行 |
| 13 | T-006 主线在到达前已由 T-002 完成（计划冗余） | 清单同表第 13 行（**只在成果记录里，不进仓库约定**——它说的是计划切分，不是仓库事实） |
| 14 | 读任一任务的 diff 要按块归属 | 清单「两条读这份记载的方法」第 14 行 |
| 15 | `Docs/ItemWorldModel.md` 的更正 C-1（堆叠物 core 不可约） | `AGENTS.md` 建模一节的 `core_uid` 段（写明两个非默认值都上线并指向该更正）＋ 清单「两条方法」第 15 行 |

**十五条一条不落。** 另有两条我自己查出来的，见下一节。

## Executor Self-check — round 1

### 我发现的问题

1. **账 10 已被本轮后续改动证伪，我把它改成了一条订正。**
   `DSP-TASK-08-01` 第二条说「`DeleteOwnExceptAsync` 删的恰好就是孤儿查询会报的那类行」。
   我逐条核过：**它在写下时为真，今天不成立。**
   - HEAD 版是 `DELETE FROM items WHERE owner_scope='character' AND owner_id=@cid …`
     ——那时 `items` 自带归属列，**没有槽行指名的世界行也在删除范围内**；
   - 今天是 `DELETE FROM items WHERE item_uid IN (SELECT core_uid FROM item_slots WHERE …)`
     ——**圈定对象来自槽表，而孤儿的定义恰恰是「没有槽行指名」，它根本进不了这个范围。**

   所以孤儿今天**不会**被入场对账顺手清掉。这使 `ReadUnreferencedAsync` 的答案
   **跨存盘稳定**，比原先记的更有用。我按本仓的规矩**没有改 `DSP-TASK-08-01` 的正文**
   （那是历史裁定，改掉会抹掉「何时为真、何时停止为真」这个信息），
   而是在清单里写了「订正 T12-1」作为补记，`AGENTS.md` 收了它面向仓库的那一半。
   **若控制器认为应当直接改裁定正文，那是一次改动就能做的事。**

2. **`avatar_uid` 那条派工没有落地。**
   `DSP-TASK-06-01` 第一条明写「`avatar_uid` 归 T-011 → 写进 T-011 的派工」。
   复算：`TASK-11-retire-the-old-shape.md` 全文 `avatar` 只有 3 处命中，
   **全是把 `avatar_*7` 当交付形状列出，没有一处提到退役它**。
   交付的 `items` 20 列里 `avatar_uid` 仍在，`items` 上还有 `idx_items_avatar_uid` 唯一部分索引。
   即「这一行有没有时装细节」今天仍由一个半身份决定。
   **这不是行为变化**（读写与本轮之前一致），也不是 T-011 的缺陷（它的契约没要求），
   但那条派工确实没落地。已写进清单的「额外发现」与 `AGENTS.md` 的「已知缺陷」。

3. **一处我自己写错、当场改正**：初稿把 `spared` 的落点写成
   `EmptyAsync` 的 `AND core_uid NOT IN (…)`，实为 `AND item_uid NOT IN (…)`；
   `core_uid NOT IN (…)` 是 `DeleteOwnExceptAsync` 的谓词。两处都已改正，
   并在清单里明写「不是同一个谓词」。**这正是「不得从别处抄」要防的那种错。**

4. **`Relocate_…` 那条判据不只是名字过时，`<remarks>` 也过时了。**
   它写着「this store moves an item by replacing its row… the facts are read back under the
   avatar UID the core carries and written on again」，而 `CarriedAvatarsAsync` 今天只对
   **非幸存者**执行。这与本仓最在意的形状（注释说的与代码做的相反）是同一类。
   **我没有改它**（判据与它的注释都在允许清单外），只在记载里写明。

### 我没有验证的东西（明说）

- **没有跑真实客户端、没有抓包。** 「客户端字节不变」的全部依据是判据、字节锚
  （`SelectCharacterAcceptedTranscriptTests`）与 2215 全绿。契约兽槽 24 还是 25 本来就无线上证据。
- **没有独立复核 T-002…T-008、T-011 那批「形状变了但字节不变」的改动**是否真的一处行为未变。
  CK-027 我核的是**两处授权变化的下游封闭性**（起因收敛到一个符号，数消费点，读分支），
  **不是**对整轮 diff 的逐块行为等价性复核。若第三处行为变化藏在那批改动里而字节锚没抓到，
  **我的核法看不见它**。这条边界我原文写进了清单。
- **没有复算档案侧的逐名件数**（`[coat avatar]` 8391 那一批）。
  账 6 我复算的是**结构性事实**——`ByName` 16 个键里没有任何 avatar 键、`avatar` 三处全在注释、
  槽 0..11 是时装带——件数沿用 TASK-09 的普查，**在清单里已标明未复算**。
- **没有复跑第二遍全量**去排除偶发。只跑了一次，退出码 0、零失败，
  没有出现「异常类型 ＋ 栈根 ＋ 复跑即绿」那个形状，所以没有触发身份判别流程。
- **没有验证「改掉 `AGENTS.md` 的某句话会让什么变红」**——记载本来就没有判据守着，
  这正是账 3／账 9 说的那类风险在本记载自身上的复现。
- **没有核对 `Docs/ItemWorldModel.md` 更正 C-1 以外的正文**是否还有别的已被证伪的句子。
  我只核了更正 C-1 本身（两个写死值、`ItemKind == 2` 那一支）属实。

### 不确定的地方

- **账 13（T-006 计划冗余）我只放进成果记录，没有放进 `AGENTS.md`。**
  理由：它说的是本轮计划切分的事，不是仓库的持久事实，写进仓库约定会让后来者困惑。
  **若控制器认为它也该进仓库约定，加一段即可。**
- **「清单之外没有其它行为变化」这句的强度**，取决于「两处授权变化的起因各只有一个符号」
  这个前提。我数过消费点、读过分支，但**这是静态阅读，不是变异验证**——
  我没有为「假如还有第三处变化，哪条判据会红」构造过反例。
  真正独立的证据是字节锚加 2215 全绿，而那两样只覆盖它们自己覆盖的路径。
- **`AGENTS.md` 新小节的位置**我选在「CMD ⇒ Notify」与「判据偶发失败」之间，
  即架构设计一节的末尾附近。放在「其他与主设计的偏差（诚实清单）」之后也说得通
  ——那一节的气质与我这节的后三个子块最接近。**若评审偏好后者，移动一次即可。**

## Controller Disposition — DSP-TASK-12-01

执行方交回两件，**其中一件是控制器的错**。

### 一、账 10 被后续改动证伪——采纳订正，做法也对

`DSP-TASK-08-01` 记的「`DeleteOwnExceptAsync` 删的恰好就是孤儿查询会报的那类行」
**在 T-008 时点为真**（那时 `items` 自带归属列），**今天不成立**。

**控制器复核**：该删除现在是
`DELETE FROM items WHERE item_uid IN (SELECT core_uid FROM item_slots WHERE …)`——
按**有槽行的**那些行圈定；而孤儿的定义正是**没有槽行指名**，**根本进不了这个范围**。

**所以孤儿不会被入场对账顺手清掉，`ReadUnreferencedAsync` 的答案是跨存盘稳定的**——
比原先记的**更有用**。

执行方**没有改写历史裁定正文，而是补了「订正 T12-1」**。这正是本仓要的做法：
历史记录当时为真，订正另记。**采纳。**

### 二、`avatar_uid` 那条派工丢了——**这是控制器的疏漏，不是任何执行方的**

`DSP-TASK-06-01` 明写「`avatar_uid` 归 T-011 → **写进 T-011 的派工**」。
而控制器写 `AMD-TASK-11-01` 时列了四笔债，**没有它**；T-011 的派工也因此没有带它。

**当前状态（控制器实测）**：`items` 上仍有 `avatar_uid`，外加唯一部分索引
`idx_items_avatar_uid`（`CharacterDatabaseSchema.cs:199`／`:247`），
`InventoryItemTables.cs` 引用它 **9 处**——
**「这一行有没有时装细节」今天仍由它是否为 NULL 决定。**

**定性**：不是行为变化，不是 T-011 的缺陷，**任何 RQ 都不要求退役它**
（RQ-003 要的是时装细节**随身份走**，T-006 已用变异证明成立；`avatar_uid` 是查找／索引机制，
不是状态的住处）。

**但它是一个与本成果同名的东西**——一件物品拿到了真身份之后，
还留着第二个「用来认出这一行」的键。**是否在本成果内清掉，属范围决定，交所有者裁。**
→ 已交回所有者；在裁定之前，本 Task 不因此欠交付。

### 三、执行方自己复算揪出的一处，值得记

它初稿把 `spared` 的落点写成 `AND core_uid NOT IN (…)`，**复算发现实为 `AND item_uid NOT IN (…)`**
（`core_uid NOT IN` 是 `DeleteOwnExceptAsync` 的谓词）。两处已改正并在清单里点明区别。

**这正是「每个数字须当场复算，不得从别处抄」那条纪律拦下来的东西。**

## Independent Quick Review — RVW-TASK-12-01

- **Verdict：`changes_required`**（两处可当场改掉的计数与代码不符；结论与方向全部成立）
- 评审的契约修订：Contract revision 1，无修正案（本 Task 无 AMD）。已读裁定 `DSP-TASK-12-01`。
- 拥有的 diff 归属：`AGENTS.md` 的单一 hunk（`git diff --numstat -- AGENTS.md` = **119 增 / 0 删**，
  `@@ -484,6 +484,125 @@` 一块，插在「CMD ⇒ Notify」与「判据偶发失败」之间，
  新小节起于 `### 物品：有身份的东西，和它在哪，是两张表`）；
  新建 `.rks-agent/tasks/items-own-their-identity/behavior-change-list.md`（283 行）；本 Task 文件。
- 本轮**未采信执行方与控制器的任何转述**，全部数字与陈述均回到代码复算。

### 我实际跑过／读过的

- `git rev-parse HEAD` → `51cd99c2`，与记载一致；`git status --porcelain` 79 项，`AGENTS.md` 为 `M`。
- **CK-028 自己重跑一遍**（`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`，退出码 **0**）：
  Networking 52/0、Protocol 521/0、Pvf 60/0、Host 6/0、Persistence 315/0、Infrastructure 236/**2 跳过**、
  Application 1025/0 → **逐项目相加 2215 通过 / 2 跳过 / 0 失败**，与执行方逐格一致；
  两条跳过正是记载点名的 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
  与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。
  **未用 `grep -c`。** 未见任何失败，故不触发偶发身份判别流程。
- BOM：`AGENTS.md` 首三字节 `23 20 41`、`behavior-change-list.md` `23 20 e8`、本 Task 文件 `23 20 54`，
  **均非 `ef bb bf`**。
- 记载纪律：两份交付物中 `.cs:<数字>`／「第 N 行」／`line N` **零命中**，确为按内容索引。

### 逐个数字的独立复算（全部相符）

| 陈述 | 我的复算 | 结论 |
|---|---|---|
| `items` 20 列＝2＋3＋7＋8 | `CharacterDatabaseSchema.cs` 的 `CREATE TABLE IF NOT EXISTS items` 单行 DDL 逐列点：`item_uid`/`item_core`＋`cargo_character_id`/`created_at`/`updated_at`＋`avatar_uid`＋六列 avatar 细节＋八列 `creature_*` | **20**，分组也对 |
| `item_slots` 8 列＋1 UNIQUE＋3 具名 CHECK | 同文件 DDL：8 列；`UNIQUE(owner_scope,owner_id,list_type,slot_index)`；`slot_count_is_positive`／`world_slot_holds_exactly_one`／`world_slot_names_its_own_row` | 相符（另有两条**匿名**内联 CHECK 在 `owner_scope`／`kind` 上，记载说的是「具名」，不冲突） |
| `core_uid` 一列解两笔债 | 孤儿查询 `ReadUnreferencedAsync` 已收成**单条** `NOT EXISTS (SELECT 1 FROM item_slots holder WHERE holder.core_uid = items.item_uid)`；`LiftAsync`／`NoContainer` 在 `Server/src Server/tests` **零命中** | 两笔债均属实 |
| 分类唯一生效处只收模板号 | `IItemPlacementRuleService.ClassifyAsync(int itemId, CancellationToken)`；实现只有 `ItemPlacementRuleService`（tests 里无第二实现，只有 3 处调用）；生产调用点 `CharacterRosterService`／`DailyRefillService`／`InventoryEntryAssembly`×2／`ItemContainers`×2 = **6** | 相符 |
| `ItemWorldClass` 四值 | `ItemPlacementContent.cs`：0/1/2/3 | 相符 |
| CHECK 在 HEAD 不存在、调和只 ADD COLUMN | `git show HEAD:…CharacterDatabaseSchema.cs` 里 `world_identity_fits_the_wire` **0 命中**（HEAD 的 `items` 还带 `owner_scope/owner_id/list_type/slot_index`）；`ADD COLUMN` 在 `Dfo.Server.Persistence/` 恰 **1 处** | 「只对新建库无条件成立」这个限定**必须保留**，记载确实保留了 |
| `ConsumeAsync` 只经 item-use、无守卫 | 生产调用方**恰 1 处**（`ItemUseEffectDomainService`），门是 `if (rule.IsConsumedOnUse)`（`rule` 来自 `PvfItemUseRuleDefinitionSource`）；`ConsumeAsync` 本体只校验 `count > 0` 与 `remaining >= 0`，**读过全文，无任何身份守卫** | 限定属实且记载没省 |
| `spared` 落点 | `EmptyAsync` 是 `AND item_uid NOT IN (…)`；`DeleteOwnExceptAsync` 是 `AND core_uid NOT IN (…)` | 执行方的自纠正确 |
| `ByName` 16 键／无 avatar 键／`avatar` 三处全在注释 | 逐条点：12,13,13,14,15,16,17,18,19,20,21,22,23,24,24,24 = **16 键 / 13 个槽号**；`avatar` 命中 3 处全在 `///` | 相符 |
| 快捷栏 3..8、排除 5..9 | `QuickSlotStart=3`、`QuickSlotEnd=8`、`AllowsQuickSlot` 排除 `5/6/7/8/9` | 相符 |
| 契约兽 5、穿戴件 1 | `PvfEntryLifecycleContentSource`：`CreatureItemKind=5`、`WornPieceItemKind=1`、`WornPieceMetadata` 三元 | 相符 |
| 堆叠物 core 两个非默认值都上线 | `StackableItemCore.Create`：`Marker16 = -1`、`RandomOptionChangedIndex = byte.MaxValue`；`GameInventoryDelivery`：`core.ItemKind == 2 ? core.Marker16 : Math.Max(0, core.Marker16)` | 相符 |
| 三条断言文案 tests 零命中 | `"Non-avatar inventory item carries avatar detail."`、`"Avatar jewel socket must contain exactly 30 bytes."`、`"is malformed or has the wrong owner"` 在 `Server/` 下**各只 1 处且都在 src** | 相符 |
| 装载侧无对称拒绝 | `RequireUniqueSlots` 只用 `HashSet<(ContainerType, SlotIndex)>`，**不校验 `core_uid`**；保存侧拒绝文案 `"A dirty inventory names the same thing in more than one position."` 存在 | 相符 |
| `avatar_uid` 仍在＋唯一部分索引 | `CREATE UNIQUE INDEX IF NOT EXISTS idx_items_avatar_uid ON items(avatar_uid) WHERE avatar_uid IS NOT NULL` | 相符 |

### CK-027：行为变化清单逐条回核（独立）

- **RQ-009 的封闭性我自己走了一遍**：`PvfEquipmentSlots.ByName` 在 `Server/src` 只有**两个读者**
  ——`PvfEquipmentSlotDefinitionSource`（`[equipment type]` → 槽号）与 `PvfInitialEquipmentDefinitionSource`（建角色）。
  前者的输出只经 `IEquipmentSlotDefinitionSource.GetAsync` 流向 `ItemPlacementRuleService.WornSlotForAsync`，
  而 `WornSlotForAsync` 的生产命中共 6 行：接口声明 2、`ItemMovementDomainService` 转发 1、实现 1，
  **真正消费点恰好 2 处**，都在 `MoveItemSpaceSlice.cs`（`PutOnAsync`、`TakeOffAsync` 的 displaced 判定）。
  建角色那一支我另行确认与档案种类**无关**：`CharacterCreationRuleService` 把 `ItemKind` **写死为 `EquipmentKind`**，
  比清单给的「没有职业用 `[creature]`」更强。**后果 A/B/C/D 的分支我逐条读过，与清单一致**；
  `ErrorCodeFor` 里 `NotWearable` 与 `ItemCannotOccupySlot` 确实都映射到 `MoveItemSpaceResponse.NotMovable`
  ——**客户端字节不变属实**，而清单仍然把它列出来，这个取舍是对的。
  后果 C 的「只在源槽为 24 时」也对：`displacedSlot != command.SourceSlotIndex` 才是那个比较。
- **RQ-010 的封闭性**：`placement.ItemKind` 在 `Server/src` 命中 **5 行 3 文件**
  （`ItemArrivalRuleService` **3 行**、`ItemContainers` 1、`ItemSlotRuleService` 1
  ——执行方的 Result 里写成「ItemArrivalRuleService 2 行」，**总数 5 对、分文件计数错一**，见「附带记录」）。
  `BandsFor` 我逐行读过：kind=1 命中 `== EquipmentKind`，kind=5 命中 `!AllowsQuickSlot`，
  **两支都只 `yield return own`，产出确实相同**。快捷栏那条属实。
  另外我自己验了清单里那条附注：`PvfEntryLifecycleContentSourceTests` 把真档案六条补给规则
  **逐条钉死为 kind 4**（字典断言），`WornPieceMetadata` 确实走不到 `DailyRefillRule`。
- **我独立找过第三处行为变化，没找到。** 我没有止步于 `placement.ItemKind`，
  而是把**存进 core 的那个字节**的读者也过了一遍（`SelectCharacterPostEntryServices` 的
  `ItemKind is 2 or 3 or 4 or 7 or 9 or 10 or 11` 计数与 `{ ItemKind: 5 }` 装备兽判定、
  `InventoryEntryServices`、`SelectCharacterPreparationService` 的 24／5 标记投影、
  `StateSynchronizationPayloadCodec.Entry` 的 NOTI-2 creature marker 与入场帧 `SlotIndex==24 && ItemKind==5`、
  `GameInventoryDelivery` 的 `ItemKind == 8`／`== 2` 各支）。结论：
  **没有新的可观察变化落网**——因为 (a) 真实存盘的契约兽 core 本来就带 5，
  (b) 本轮之前一件由服务器生成的契约兽**根本进不了槽 24**（正是 RQ-009 修的那件事），
  (c) 生产侧**没有任何**以 `ItemWorldClass.Creature` 为条件的装载期拒绝
  （`ItemWorldClass.Creature` 在 `Server/src` 只有 2 行，都在档案侧）。
  **第三处行为变化未被证实存在。**

### 订正 T12-1：成立，方向也对

我自己读了两处 SQL：

- 今天的 `InventoryItemTables.DeleteOwnExceptAsync` 是
  `DELETE FROM items WHERE item_uid IN (SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=@cid AND list_type NOT BETWEEN 19 AND 23 [AND core_uid NOT IN (…)])`
  ＋ 一条对 `item_slots` 的同条件删除；
- 孤儿的定义（`ReadUnreferencedAsync`）恰恰是 `NOT EXISTS (… holder.core_uid = items.item_uid)`。

**没有槽行指名的世界行进不了那个子查询，所以不会被入场对账清掉。**
这个订正把结论从「更没用」改成「更有用」，方向有利于交付物，我因此格外核了一遍——**它成立**。
执行方不改 `DSP-TASK-08-01` 正文、另立补记，符合本仓「改已证伪的记载要补记修正案」的规矩。

### `avatar_uid` 的定性：我独立判断，控制器的定性**准确**

- **它是不是仍在承载状态语义？** 不是。六列时装细节就在**物品自己的世界行上**，
  因而天然随 `item_uid` 走；`avatar_uid` 的作用是 (a) 该行是否有时装细节的**判别标志**、
  (b) `CarriedAvatarsAsync` 把**被替换掉那一行**的细节读出来再写回去时的**查找键**
  （`SELECT … FROM items WHERE avatar_uid = @uid`，`@uid` 取自 `core.Value`）、
  (c) `ON CONFLICT(avatar_uid) … DO NOTHING` 的唯一性守卫。三者都是查找／约束机制，**不是状态的住处**。
- **有没有哪条 RQ 要求它退役？** 没有。RQ-003 要的是「可变状态随身份走、位置变动不改变其中任何一项」，
  这已由细节列落在世界行上满足；RQ-005 说的是**无身份的东西**不得获得身份，与它无关。
  spec 里那句「已经有代码在依赖一个每次写入都会改变的半身份」指的是
  **对账扫描所依赖的存储标识**，不是 `avatar_uid`。**没有 RQ 读漏。**
- **它会不会让 `AGENTS.md` 那句「物品有自己的身份」变得不准确？** 不会。
  新小节在「已知缺陷与覆盖缺口」里**明写**了「`avatar_uid` 这个半身份还在……那条派工没有落地」，
  并且那句「『这一行有没有时装细节』今天仍由 `avatar_uid` 是否为 NULL 决定」
  **有代码印证**——`InventoryItemTables` 的读路径注释原文即
  「the same way the avatar's presence is decided by its UID」，且 `Read` 确由 `Avatar(reader, 2)` 判定。
  **记载没有夸大，本 Task 在这一点上不欠缺陷。**

### 需要改的（两处，均为可当场改掉的计数与代码不符）

1. **`AGENTS.md` 新小节「三处『结论对，但撑着它的不是代码』」第 3 条的「注释三处说它」，实为四处。**
   除了它点名的 `ApplyDecidedWritesAsync`／`FlushAsync`／`EmptyAsync` 三个成员的 `<remarks>` 之外，
   `SqliteItemDomainStore.cs` 里 `SqliteItemDomainStoreSession` 的**类型级 `<remarks>`** 也说了同一条
   （原文「The batch below empties every position it names before it fills any, exactly as it always has」）。
   同一句在 `behavior-change-list.md`「三条」表第 3 行重复。
   **改法**：把「三处」改为四处并补上类型级那处，或改写为「以下三个成员各一处（另有类型级 `<remarks>` 同说）」。
   结论（不再承重、无判据钉住）不受影响。
2. **`behavior-change-list.md`「两条判据名字」表第 5 行的「六条 `DoesNotContain` ＋ 一条正向 `Contains`」——
   正向 `Contains` 实为两条。**
   `ItemCarrierShapeTests.EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt`
   里除了指向 `ON item_slots(core_uid)` 的那条，还有一条 `Assert.Contains("idx_characters_account", indexes.Keys)`。
   现文「正向那条指向的是新增的 …」作为**穷尽性**陈述不成立。
   **改法**：改为「两条正向 `Contains`，其中『搬过去重建』那条指向的是新增的 `ON item_slots(core_uid)`」。
   「名字不再描述它证明的东西」这个判断本身仍然成立。

这两条都直接落在本 Task 的验收「仓库约定记载与代码一致」（AC-017／EV-013）与契约的
**记载纪律「每个数字须当场复算」**上，因此按 `changes_required` 处理；除此之外无需改动。

### 附带记录（不要求修，供控制器参考）

- Execution Result 的 CK-027 段把 `placement.ItemKind` 的分文件计数写成
  「`ItemArrivalRuleService`（2 行）」，实为 3 行（`== EquipmentKind`、`!AllowsQuickSlot`、`== ConsumableKind`）；
  **总数 5 行 3 文件是对的**，且这句只在 Task 文件里、不在两份交付物中，故不列为需改项。
- 清单的「清单之外没有其它行为变化」所用核法只追到「`placement.ItemKind` 的消费点」为止，
  **没有明写还追了第二跳（存进 core 的那个字节的读者）**。我这一轮替它追了，结论未变；
  若日后要加强这句的强度，把第二跳的封闭性也写进「我是怎么核的」会更实。
- 「本轮 0 改动生产代码与判据」在**全部十二个任务未提交**的前提下无法用 `git diff` 单独证明；
  可证的是：`AGENTS.md` 为纯追加的单一 hunk、清单为新建文件、且我重跑 CK-028 得到与基线**完全相同**的
  2215/2/0（markdown 不参编译），与该声明不矛盾。
- `AGENTS.md` **确为纯追加**（119/0），既有文字一字未删改；新小节没有放宽任何既有约定
  ——我另检查过 `AGENTS.md` 全文对「迁移／旧库／升级」与 `items`／`item_slots` 的既有表述，
  新小节之外**没有可被它放宽的条款**。

## Execution Result — round 2

针对 `RVW-TASK-12-01` 两条阻断项的整改。**由控制器直接执行**（只改记载，落在本 Task 的允许清单内）。
round 1 与快审记录原样保留，不回改。

### 订正一：「清空先于填入」是**四处**注释说它，不是三处

控制器逐处核过 `SqliteItemDomainStore.cs`：

| 处 | 措辞 |
|---|---|
| 类型级 `<remarks>`（`:60-61`） | 「The batch below empties every position it names before it fills any」 |
| `ApplyDecidedWritesAsync` | 「a batch clears every slot it names before filling any」 |
| `FlushAsync` | 「every named position is emptied before any is filled」 |
| `EmptyAsync` | 「every named position being empty before any is…」 |

**四处措辞各不相同**——控制器第一次按同一句去 grep **只找到一处**，
这正是快审能查出而执行方漏掉的原因。**已把这一点写进订正文字**，
免得后来者重蹈「按一句话 grep 就以为数清了」。

`AGENTS.md` 与 `behavior-change-list.md` 两处均已改。**结论不变**：
这条不变量已不再承重、无判据钉住。

### 订正二：正向 `Contains` 是**两条**，不是一条

`ItemCarrierShapeTests.EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt` 里
六条 `DoesNotContain` 之后有**两条** `Assert.Contains`：
`idx_characters_account`，以及新增的 `ON item_slots(core_uid)`。

原文把「一条正向」当作穷尽性陈述，不成立。已改为两条，并说明**与本轮相关的是后者**。
**判断本身（正当跟随而非削弱、名字不再描述它证明的东西）不受影响。**

### 检查

- 只改两份记载：`AGENTS.md` 与 `behavior-change-list.md`，各一处措辞。
- 全量复跑：**2215 通过 / 2 跳过 / 0 失败**。

**一处当场自纠**：本节初稿写「生产代码与判据 0 改动（`git status --short -- Server/src` 为空）」——
**那句是错的，而且它踩的正是本 Task 自己记下的第 14 条账**：
十二个任务全部未提交，`git status -- Server/src` 显示的是**全部累积改动**（实测非空，
列出的是 TASK-07 的文件），**它根本隔离不出本轮**。

本轮「未动生产代码」的正确依据是**改动动作本身**：round 2 只执行了一次文本替换，
目标是上面那两份记载，没有触及 `Server/` 下任何文件。**以此为准。**

**记下来**：那条账写的是「读 diff 要按块归属」，而我在写完它之后的第一次自证就用错了命令。
**一条记在纸上的纪律，不会自动作用于写它的人。**

## Executor Self-check — round 2

- **两条都是计数错，而收记载的任务栽在计数上，这件事本身值得记。** 契约的记载纪律第一条就是
  「每个数字须当场复算」；round 1 复算了 16 项，漏掉的这两项恰好都是**措辞分散**的那种：
  一条要跨四种不同说法去数，一条藏在六条反向断言之后。
- **控制器自己第一次也数错了**：按类型级那句原文 grep 只命中一处，
  直到逐个成员翻 `<remarks>` 才凑齐四处。**所以订正文字里明写了「四处措辞各不相同」**——
  这不是修饰，是给下一个人省一次同样的错。
- 未验证：本轮只重跑了全量，**没有重新逐条复核 round 1 那 16 项数字**；
  它们已由快审独立复算过一遍，此处不重复。

## Independent Quick Review — RVW-TASK-12-02

- **Verdict：`changes_required`**（两条打回项**均已正确落地**；但同一失败模式的**第三处**仍在两份交付物里，
  且它**不在** round 1 快审复算过的 16 项之内——见「第三处同类计数错」）
- 评审的契约修订：Contract revision 1，无修正案。已读 `DSP-TASK-12-01` 与 `RVW-TASK-12-01`。
  **只审 round 2**，round 1 的结论与前一轮快审的结论一字未回改。
- 本轮拥有的改动（round 2）：`AGENTS.md` 与 `behavior-change-list.md` **各一处措辞**，加本 Task 文件的追加。
- 全部数字均**回到代码自己数**，未采信执行方／控制器／上一轮快审的任何转述。

### 一、两条订正：都落地，数也对

**订正一「四处」——我自己数过，确为四处**，且**四处措辞各不相同**，
按任一句原文 grep 都只会命中一处。`Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`：

| 处 | 行 | 归属 | 原文 |
|---|---|---|---|
| 1 | `:60-61` | `SqliteItemDomainStoreSession` **类型级** `<remarks>`（类型声明在 `:73`） | `The batch below empties every position it names before it fills any` |
| 2 | `:194` | `ApplyDecidedWritesAsync` 的 `<remarks>` | `a batch clears every slot it names before filling any` |
| 3 | `:212-213` | `FlushAsync` 的 `<remarks>` | `every named position is emptied before any is filled` |
| 4 | `:474-475` | `EmptyAsync` 的 `<remarks>` | `every named position being empty before any is filled` |

**没有第五处**：我另读了 `SqliteItemDomainStore` 类自己的 `<remarks>`（`:10-20`，讲的是事务参与，不涉此）
与 `:641` 那处（讲的是「容器读不到会被误当作空」，不是同一条不变量）。全文 667 行已过一遍。

- `AGENTS.md:564` 已改为「**四处**说它（类型级 `<remarks>` 一处，加 `ApplyDecidedWritesAsync`、`FlushAsync`、
  `EmptyAsync` 各一处 `<remarks>`）」——**四处齐了，逐个点名，没有靠数字含糊过去**。
- `behavior-change-list.md:168` 同样改为四处，**并且写出了那句给下一个人的提醒**
  ——「四处措辞各不相同，按同一句去 grep 只会找到一处」。**这一点确实写出来了。**
- **一处不对称（不阻断）**：那句提醒**只在清单里，`AGENTS.md:564` 没有**。
  `AGENTS.md` 是后来者最先读的那份，而它靠「逐个点名四个位置」间接达到了同样效果，故只作记录。

**订正二「两条正向 `Contains`」——我自己数了那个测试方法。**
`Server/tests/Dfo.Server.Persistence.Tests/Schema/ItemCarrierShapeTests.cs`，方法体 `:328-342`：

- 六条 `Assert.DoesNotContain`：`:331`（`idx_account_cargo_items_owner`）、`:332`、`:333`、`:334`、`:335`、
  `:336`（`idx_items_owner_space`）；
- **两条**正向断言：`:338` `Assert.Contains("idx_characters_account", indexes.Keys)`，
  `:339-341` `Assert.Contains(indexes, entry => entry.Value.Contains("ON item_slots(core_uid)", …))`。

`behavior-change-list.md:175` 已改为「六条 `DoesNotContain`（含 `idx_items_owner_space`）＋ **两条**正向 `Contains`
（`idx_characters_account` 与新增的 `ON item_slots(core_uid)`），而**与本轮相关的那条**指向的是……」
——**穷尽性陈述已改掉，并点明了哪一条才是与本轮相关的那条。数对了。**

### 二、结论未被改动或稀释（逐句核对）

- 账 3 的结论在 `AGENTS.md:563` 与清单 `:168` 都仍是
  「**这条不变量已经不再承重，且没有任何判据钉住它**……**这是一条只写在注释里的口头不变量**」，
  并且撑住它的那两个支点（变异下 315 条持久化判据仍全绿；真正扛住换位的是 `spared` 的
  `AND item_uid NOT IN (<幸存者>)`，且**与 `DeleteOwnExceptAsync` 的 `core_uid NOT IN` 不是同一个谓词**）
  **一个字没动**。订正只把「三处」换成「四处」并补齐位置，**结论一分未削**。
- 账 5 的结论在清单 `:175` 仍是「快审判定这是**正当跟随而非削弱**（字面重建不可能——列已不在 `items`；
  被保护的事实由槽表 `UNIQUE` 接住；净断言数上升），**但名字仍然不描述它证明的东西**」。
  **两半都在，且没有被「两条正向」这个新事实拿去反证任何一半。**
- `AGENTS.md:590-592` 那条（判据名字过时）措辞未变：「（正向断言现在指向新增的 `ON item_slots(core_uid)`）」。
  它**不含计数**，说的是「支撑该名字的那条正向断言指向了别的东西」，与代码相符，故不列为需改项；
  若要更严，可写成「与它相关的那条正向断言」。**记录，不要求改。**

### 三、round 2 只动了两份记载——用 mtime 判定，未用整目录 `git status`

`git rev-parse HEAD` → `51cd99c2`，与记载一致（全部未提交）。
**按修改时间排序（已排除 `bin/`、`obj/`）：**

| 时间 | 文件 |
|---|---|
| 19:04:32 – 19:18:04 | `InventoryItemTables.cs`、`InventoryEntryAssembly.cs`、`SelectCharacterPostEntryServices.cs`、`SqliteItemDomainStore.cs`、`CharacterDatabaseSchema.cs`、`InventoryTables.cs`（**TASK-11 的收尾**，其 Task 文件 19:19:22） |
| 20:10:38 / 20:10:50 | `tasks/TASK-13-the-avatar-key-retires.md`、`sdd-execution.yaml`（控制器对 `avatar_uid` 的范围裁定，**早于** round 2） |
| **20:13:10.951 / 20:13:10.952** | **`AGENTS.md` / `behavior-change-list.md`** ← round 2 的两处措辞，同一秒写入 |
| 20:14:42.771 | 本 Task 文件（round 2 的记录） |

**`Server/src` 与 `Server/tests` 下最新的一次源文件写入是 19:18:04，比 round 2 早 55 分钟；
20:13 之后没有任何 `.cs` 被碰过。** 因此「本轮未动生产代码或判据」**成立，且有 mtime 作独立证据**。
（`TASK-13-*.md` 与 `sdd-execution.yaml` 属控制器的任务控制状态，不在本 Task 的交付物内，且写于 round 2 之前。）

### 四、`AGENTS.md` 仍是纯追加

- `git diff --numstat -- AGENTS.md` → **119 增 / 0 删**（与 round 1 相同，说明 round 2 的替换未改行数）；
  `git diff` 里 `^-` 开头的正文行 **0 条**。
- 单一 hunk：`@@ -486,0 +487,119 @@`。新小节占 `AGENTS.md:487-605`
  （起于 `### 物品：有身份的东西，和它在哪，是两张表`，止于「判据偶发失败」小节之前）。
- **既有文字一字未删改，未放宽任何既有约定**：新小节全部是新增行，物理上不可能改到既有条款。
- 记载纪律（按内容而非行号索引）：`487-605` 区间内 `.cs:<数字>`／「第 N 行」／`line N` **零命中**
  （`AGENTS.md` 里的行号引用全部落在 `:170`–`:440` 的既有段落）。清单文件同样零命中。

### 五、控制器 round 2 的那次自纠：诚实，且新依据成立（附一处措辞不准）

- **(a) 那条命令确实隔离不出本轮**——实测 `git status --short -- Server/src` **非空，27 个 `M`**。
  自证「为空 ⇒ 未动生产代码」的前提根本不存在，**自纠属实，不是走过场**。
- **(b) 新依据站得住，但它站得住是我替它验的**：自纠给出的依据是「改动动作本身：round 2 只执行了一次文本替换」
  ——这是**自述**，读记载的人无法独立复核。它**恰好为真**，我用上表的 mtime 独立确认了。
  **建议**：把 mtime（`Server/` 下最新源文件 19:18:04 ＜ 两份记载 20:13:10）写进依据，
  那才是下一个人能自己跑一遍的东西。
- **一处措辞不准（记录，不要求改）**：自纠说该命令「列出的是 TASK-07 的文件」。
  实际列出的 27 个横跨多个任务——`ItemPlacementContent.cs`／`PvfEntryLifecycleContentSource.cs` 是 T-010 的，
  `PvfEquipmentSlots.cs` 是 T-009 的，`InventoryTables.cs`／`CharacterDatabaseSchema.cs` 是 T-011 的。
  **不影响自纠的结论**（该命令隔离不出任何单轮），只是那句归属写窄了。
- 「一条记在纸上的纪律，不会自动作用于写它的人」——这句与账 14 的内容对得上，**记得对**。

### 六、CK-028：我自己跑，逐项目列明细（未用 `grep -c`）

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
→ **退出码 0**

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 315 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| **合计** | **2215** | **2** | **0** |

逐项目相加：52+521=573；+60=633；+315=948；+6=954；+1025=1979；+236=**2215**。
两条跳过的**名字**从日志里读出：`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。
**零失败，不触发偶发身份判别流程**（无异常类型、无栈根，也就无从谈复跑）。
**与声称的 2215/2/0 一致，不低于基线。**

**记录（不阻断）**：round 2 的「检查」一节只写了合计「2215/2/0」，**没有留逐项目明细**，
而 round 1 留了。全量声称在本仓是要能被逐格对照的，下一轮补上一格表更稳。

### 七、BOM

`head -c 3`：`AGENTS.md` = `23 20 41`，`behavior-change-list.md` = `23 20 e8`。
**均非 `ef bb bf`。**

### 八、关于「没有重新逐条复核 round 1 那 16 项」——这个理由**只成立一半**

**成立的那一半**：round 2 未动任何代码、未动那 16 项所在的文字，两条订正与其中任何一项
**没有算术耦合**——我逐项确认过：

- 「四处」那一处所在的表格行里，另外两个数（变异下 **315** 条持久化判据全绿、`spared` 的落点是
  `AND item_uid NOT IN (…)` 而非 `core_uid`）**不依赖注释处数**，且我本轮复算仍成立（Persistence 实测 315）；
- 「两条正向 `Contains`」那一行里的另一个数「**六条** `DoesNotContain`」**同样不依赖它**，我数过仍是六条。

**不成立的那一半，也是本轮打回的理由**：这两条打回项**从来就不在那 16 项里**。
它们的失败模式不是「16 项中某一项算错了」，而是「**一个从没被列进 16 项的计数，因为措辞分散而被数少了**」。
「已由快审独立复算过那 16 项」**恰好覆盖不到**产生这两条缺陷的那一类风险。
自检把它当作免于再查的理由，**推理方向反了**——而我照着这条线去查，**当场又找到第三处**。

#### 第三处同类计数错（**本轮的阻断项**）

> `AGENTS.md:577`：「**三处注释**（`ItemPlacementContent.cs`、`IItemPlacementRuleService.cs`、
> `ItemPlacementRuleService.cs`）点着它。」
> `behavior-change-list.md:182`（「四条已知缺陷」表第 7 行）：「**三处注释点名它**：……」

**实为四处。** 遗漏的第四处是
`Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs:182-185`
（`WornPieceMetadata` 的 `<remarks>` 里那段 `<para>`），原文：

> `<b>The avatar is deliberately left alone.</b> Storage records an avatar as kind 8 and this
> still answers 1 for one, so that pair still disagrees. It is the same defect and it is not
> this outcome's to fix; naming it here keeps the remaining disagreement visible…`

**它明确点名了同一件事**（时装存储 8 ／档案 1、并自称 "the same defect"），
**而且它就是被记载点名过的 `WornPieceMetadata` 自己的注释**——同一行记载的后半句正引用这个方法。

**成因与前两条完全相同**：四处措辞各不相同（`stored as kind 8 while…gives it kind 1` ／
`stored as kind 8 and…gives it kind 1` ／ `stored as kind 8, given kind 1 by the` ／
`Storage records an avatar as kind 8 and this still answers 1 for one`），
按任一句 grep 只命中一处。

**我划的边界（供整改时照抄）**：以「**是否点名 avatar 的 8 ／ 1 这一对**」为准，
则生产侧恰好四处。**不计入**的两处是 `ItemContainers.cs:276` 与 `SqliteItemDomainStore.cs:163`
——它们只泛说「core 的种类字节与档案的答案不一致」，没有点名时装／8／1；
判据侧另有 `ItemClassificationTests.cs:19` 点名了它，**若把判据也算进来数字还会再涨**，
所以订正文字应当写明数的是「**生产注释**」。

**改法**：`AGENTS.md:577` 与 `behavior-change-list.md:182` 的「三处」改为
「生产注释四处：`ItemPlacementContent.cs`、`IItemPlacementRuleService.cs`、`ItemPlacementRuleService.cs`、
`PvfEntryLifecycleContentSource.WornPieceMetadata`（四处措辞各不相同）」。
**结论（「时装的种类不一致仍在：存储 8 / 档案 1」「动它属未授权的行为变化」）完全不受影响，不得改动。**

它直接落在验收「仓库约定记载与代码一致」与契约记载纪律「每个数字须当场复算」上，故按 `changes_required` 处理。

#### 我把这一类计数扫完了，需改项到此为止

为免再来一轮，我把两份交付物里**所有「数注释／数断言／数调用点」**的陈述过了一遍，
除上面那一条外**全部与代码相符**（列行号）：

| 陈述 | 我的复算 | 结论 |
|---|---|---|
| 「清空先于填入」四处 | `SqliteItemDomainStore.cs:60-61`／`:194`／`:212-213`／`:474-475` | **4** 相符 |
| 六条 `DoesNotContain` ＋ 两条 `Contains` | `ItemCarrierShapeTests.cs:331-336` ／ `:338`、`:339-341` | **6 / 2** 相符 |
| `ByName` **16 个键**、13 个槽号 | `PvfEquipmentSlots.cs:37-52` 逐条点 | **16** 相符 |
| 文件里 `avatar` 三处**全在注释** | `PvfEquipmentSlots.cs:7`、`:8`、`:55`，均为 `///` | **3／全在注释** 相符 |
| `CreatureGoesBackOnTests` 用例**恰好 2 个** | `[SkippableFact]` 于 `:64`、`:105`；另 2 个 `public async Task` 于 `:201`、`:227` 是 fixture 辅助 | **2 ＋ 2 辅助** 相符 |
| `ClassifyAsync` 生产调用点 **6** | `CharacterRosterService.cs:205`、`DailyRefillService.cs:105`、`InventoryEntryAssembly.cs:172`／`:280`、`ItemContainers.cs:283`／`:598`；接口声明 `:51`、doc 引用 `:57`／`:15`、实现 `:45`、实现内自调 `:56` 已剔除 | **6** 相符 |
| `ADD COLUMN` 全仓 **1 处** | `CharacterDatabaseSchema.cs:159` | **1** 相符 |
| `LiftAsync` ／ `NoContainer` **零命中** | `Server/src Server/tests --include=*.cs` 无输出 | **0** 相符 |
| 「三处注释点名时装种类不一致」 | 实为**四处**，见上 | **不符，需改** |

### 需要改的（一处）

1. **`AGENTS.md:577` 与 `behavior-change-list.md:182` 的「三处注释」，实为四处**
   （漏 `PvfEntryLifecycleContentSource.cs:182-185`）。改法与边界见上一节。
   **只改计数与位置枚举，结论不得动。**

除此之外，round 2 的两条订正**均已正确落地、结论未被稀释**，
未动生产代码与判据（mtime 为证），`AGENTS.md` 仍为纯追加（119/0），
CK-028 我自己跑出 2215/2/0，BOM 均正常。

### 附带记录（不要求修）

- 「四处措辞各不相同」这句提醒只写进了 `behavior-change-list.md`，`AGENTS.md` 那一处没有；
  `AGENTS.md` 靠逐个点名四个位置达到了同样效果，故不列为需改项。
- round 2 的「检查」一节只留了合计 2215/2/0，未留逐项目明细。
- 控制器自纠里「列出的是 TASK-07 的文件」一句归属写窄了（实为 27 个、横跨多任务），结论不受影响。
- 自纠给出的新依据是「改动动作本身」这一自述；**mtime 才是可被下一个人独立复跑的依据**，建议改记 mtime。
- round 2 的记录里出现了 `:60-61` 这样的行号引用。它在 **Task 文件**里、不在两份交付物里，
  记载纪律管的是「清单」，故不阻断；但同一条纪律的精神对执行记录一样适用。

## Execution Result — round 3

针对 `RVW-TASK-12-02` 的整改。**由控制器直接执行**（只改记载）。前两轮与两次快审记录原样保留。

### 订正三：点名时装 8／1 那一对的是**四处生产注释**，不是三处

控制器逐处核过（按**含义**扫，不按某一句措辞扫）：

| 位置 | 措辞 |
|---|---|
| `ItemPlacementContent.cs:15` | 「an avatar is stored as kind 8 while the archive's equipment index gives it kind 1」 |
| `IItemPlacementRuleService.cs:45` | 「an avatar is stored as kind 8 and the archive's equipment index gives it kind 1」 |
| `ItemPlacementRuleService.cs:41` | 「the archive still disagree about an avatar -- stored as kind 8, given kind 1」 |
| **`PvfEntryLifecycleContentSource.cs:182`** | 「Storage records an avatar as kind 8 and this still answers 1 for one」 |

**漏的正是第四处，而记载同一行的后半句就在引用那个方法（`WornPieceMetadata`）。**

边界已写进订正文字：`SqliteItemDomainStore.cs:381` 只说「时装是 kind 8」而**不点名那对分歧**，不计入；
判据侧 `ItemClassificationTests.cs:19` 也点名，但那不是生产注释。

### 这个失败模式已经连中三次，值得单独命名

| 轮次 | 记的 | 实际 | 数的是什么 |
|---|---|---|---|
| round 1 | 三处 | **四处** | 说「清空先于填入」的注释 |
| round 1 | 一条 | **两条** | 索引判据里的正向 `Contains` |
| round 2 | 三处 | **四处** | 点名时装 8／1 的生产注释 |

**共同点不是「粗心」，是一个可命名的模式：**

> **一个从未被列进核对清单的计数，因为它数的东西措辞分散而被数少了。**

两半各自致命：
- **不在清单里** → 「已复核清单那 N 项」这条辩解**覆盖不到它**；
- **措辞分散** → 按其中任一句去 grep **只命中一处**，看起来像是数清了。

**对策**：数注释、数断言这类东西时，**按含义扫，不按措辞扫**——
把该含义的所有说法都找出来，不要拿某一句当代表。本轮第三处正是照这条线查出来的。

### 两处一并订正的措辞

- round 2 的自纠说「（`git status -- Server/src` 列出的）是 TASK-07 的文件」——**不准**，
  实为 **27 个 `M`，横跨 T-009／T-010／T-011 等多个任务**。已按快审的实测订正。
- round 2 的自证依据「改动动作本身」是自述、不可复核。**改用 mtime 为准**（见下）。

### 检查

- **CK-028**：**2215 通过 / 2 跳过 / 0 失败**（退出码 0）。
- **本轮只动两份记载**，mtime 为证：`behavior-change-list.md` 20:25:20、`AGENTS.md` 20:25:27，
  而 `Server/` 下最后一次 `.cs` 写入远早于此（TASK-11 收尾）。

## Executor Self-check — round 3

- **快审对我那句辩解的反驳是对的，方向反了。** 我说「两条订正与 round 1 那 16 项无算术耦合，
  且已由快审复算过」——耦合确实没有，但**那两条打回项从来就不在那 16 项里**。
  「已复核 16 项」恰恰**覆盖不到**这类风险。它照这条线查，**当场找出第三处**。
- **我自己在本轮又踩了一次同类的坑**：核 `item_slots` 的具名 CHECK 时用了
  `grep -c "CONSTRAINT"`，返回 **1**——因为整个 `CREATE TABLE` 在一行上，
  **`grep -c` 数的是行不是次数**。改用逐条列出后确认是三条，与记载相符。
  **仓里那条「不要用 grep -c」的纪律，我在写它的同一轮里踩了。**
- 未验证：本轮没有重新复核 round 1 那 16 项与 round 2 的两处订正——
  它们已分别由两轮快审独立复算。**但按上面那条失败模式，这个理由对「清单外的计数」不成立**，
  所以我另做了一次全小节的计数普查（26 条含计数的陈述逐条对照），**未再发现第四处**。

## Independent Quick Review — RVW-TASK-12-03

- **Verdict：`changes_required`**（订正三本身**数对了、边界也划对了**；但本轮**新引入**一处对契约记载纪律的违反，
  且我按第 3 题要求抽查时**找到了第四处同类计数错**——round 3 声称的「26 条计数普查」没有拦住它）
- 评审的契约修订：Contract revision 1，无修正案。已读 `DSP-TASK-12-01`、`RVW-TASK-12-01`、`RVW-TASK-12-02`。
  **只审 round 3**，前两轮与两次快审记录一字未回改。
- 本轮拥有的改动（round 3）：`AGENTS.md` 与 `behavior-change-list.md` 各一处（第 7 条计数与位置枚举），
  加本 Task 文件的追加。mtime：`behavior-change-list.md` **20:25:20.934**、`AGENTS.md` **20:25:27.128**、
  本 Task 文件 **20:27:26.392**。
- 全部数字**回到代码自己数**，未采信执行方／控制器／前两轮快审的任何转述。

### 一、订正三：**数对了，没有第五处，边界也划对了**

我按**含义**扫了一遍（不按任一句措辞）：先 `grep -rn "kind 8"`，再用语义邻近词
（`disagree`／`equipment index`／注释行里同时出现 `avatar` 与 `kind|8|archive|classif`）交叉扫，
逐个读了每个候选的完整 `<remarks>`。**点名「时装存储 8 ／档案 1 这一对」的生产注释恰好四处**：

| # | 位置 | 判定依据 |
|---|---|---|
| 1 | `ItemPlacementContent.cs`（`ItemWorldClass` 类型级 `<remarks>` 的 `<para>`） | 「stored as kind 8 while the archive's equipment index gives it kind 1」 |
| 2 | `IItemPlacementRuleService.ClassifyAsync` 的 `<remarks>` | 「stored as kind 8 and the archive's equipment index gives it kind 1」 |
| 3 | `ItemPlacementRuleService.ClassifyAsync` 的 `<remarks>` | 「stored as kind 8, given kind 1 by the equipment index」 |
| 4 | `PvfEntryLifecycleContentSource.WornPieceMetadata` 的 `<remarks>`／`<para>` | 「Storage records an avatar as kind 8 and this still answers 1 for one, so that pair still disagrees」 |

**不计入的，我逐个读过正文确认边界划得对：**

- `SqliteItemDomainStore.CarriedAvatarsAsync` 的 `<remarks>`（「An avatar is an item of kind 8, and the UID…」）
  ——只说时装是 kind 8，**不提档案的 1、不提分歧**。记载点名它并说明不计入，**对**。
- `SqliteItemDomainStore` 的 `AvatarKind = 8` 常量注释（「The kind byte an avatar's core carries.」）——同上，不点名分歧。
- `InventoryEntryAssembly.WithAvatarDetail` 的 `<remarks>`（「An item is an avatar when its kind is 8…」）——同上。
- `ItemContainers.PutAsync` 的 `<remarks>`、`SqliteItemDomainStore.Write` 的 `<remarks>`、
  `InventoryEntryAssembly.IdentityOfAsync` 的 `<remarks>`——**泛说**「core 的种类字节与档案的答案不一致」，
  **不点名时装、不出现 8／1**。
- 判据侧 `ItemClassificationTests`（「an avatar is stored as kind 8 while the archive's equipment…」）
  与 `AvatarCarrierRulesTests`、`ItemCarrierShapeTests` 里的 `kind 8`——**不是生产注释**。

`ItemPlacementContent` 同一注释块里另有一句「the avatar's is the same defect left standing」，
与被计入的那句**同属一处**，不重复计。

**结论：四处成立，我没有数出第五处。** 两份交付物的措辞我逐字比过，**一致且互不矛盾**
（`AGENTS.md` 与 `behavior-change-list.md` 都写「四处生产注释」、都写「数的是点名 8／1 这一对」、
都点名 `SqliteItemDomainStore` 那处不计入；清单另多写了判据侧不计入的理由，是补充不是冲突）。

### 二、**本轮新引入的违反**：两份交付物里出现了行号索引（阻断项一）

契约的「记载纪律」原文：**「按内容而非行号索引——本仓有过在只追加的文件里列行号、随后行号全部漂移的先例。」**
`behavior-change-list.md` 自己在开头第九行也写着：**「索引方式：按内容索引，不写行号」。**

round 3 的订正把位置枚举写成了**文件:行号**，两份交付物各中一次：

- `AGENTS.md`（新小节内）：`ItemPlacementContent.cs:15`、`IItemPlacementRuleService.cs:45`、
  `ItemPlacementRuleService.cs:41`、`PvfEntryLifecycleContentSource.cs:182`、`SqliteItemDomainStore.cs:381`；
- `behavior-change-list.md` 第 7 条：以上五个，外加 `ItemClassificationTests.cs:19`。

我实测：**这六个行号今天全部准确**——所以这不是「数字错」，是**索引方式违反**。
它的后果正是纪律要防的那一个：这六个文件里有五个是本轮 27 个 `M` 中的成员
（`ItemPlacementContent.cs`／`IItemPlacementRuleService.cs`／`ItemPlacementRuleService.cs`／
`PvfEntryLifecycleContentSource.cs`／`SqliteItemDomainStore.cs`），
`AGENTS.md` 又是本仓最长命的追加型文件，行号必然漂移。

**RVW-TASK-12-01 与 RVW-TASK-12-02 都把「`.cs:<数字>`／「第 N 行」零命中」当作合规证据逐轮核过；
round 3 是第一次把它破掉的一轮。** 而 `RVW-TASK-12-02` 给的改法原文用的是
`PvfEntryLifecycleContentSource.WornPieceMetadata`（按成员名索引），round 3 没有照抄。

**改法**：两处都改成按内容索引的成员名，例如
`ItemPlacementContent`（`ItemWorldClass` 类型级 `<remarks>`）、`IItemPlacementRuleService.ClassifyAsync`、
`ItemPlacementRuleService.ClassifyAsync`、`PvfEntryLifecycleContentSource.WornPieceMetadata`；
不计入的写 `SqliteItemDomainStore.CarriedAvatarsAsync`、判据侧 `ItemClassificationTests`。
**计数（四处）与结论一个字都不用动。**

### 三、**第四处同类计数错**（阻断项二）——round 3 的普查没有拦住

`behavior-change-list.md` 的「额外发现：`avatar_uid` 这条派工没有落地」一节写着：

> **复算：`TASK-11-retire-the-old-shape.md` 全文对 `avatar` 只有 3 处命中，
> 都是把 `avatar_*7` 当作交付形状的一部分列出，没有任何一处提到退役 `avatar_uid`。**

我自己数（`TASK-11-retire-the-old-shape.md` mtime 19:19:22，round 1 复算之后未再改动）：

| 数法 | 结果 | 「都是 `avatar_*7`」是否成立 |
|---|---|---|
| 区分大小写 `avatar` | **2 处**（两处都是 `avatar_*7, creature_*8` 那句交付形状） | 成立 |
| 不区分大小写 | **4 次命中、落在 3 行**（多出的一行是判据名 `AvatarDetailFollowsTheIdentityTests.TwoAvatarsSwappingPositionsEachCarryTheirOwnFacts`，同行两次） | **不成立**——那一处是判据名，不是交付形状 |

**两种读法都对不上现文**：若「3 处」指不分大小写的三行，则「都是把 `avatar_*7` 当交付形状列出」这个
**穷尽性限定为假**；若限定为真，则数字应是 **2**。

**它正是 round 3 命名的那个模式的第四次发作**，而且**发作在 round 3 的对策之下**：
「措辞分散」这一次表现为**大小写变体**（`avatar` 与 `Avatar`），
「不在清单里」这一次表现为**它不在 round 3 那份「26 条计数普查」的战果里**。

**改法**：改成「区分大小写的 `avatar` 只有 **2 处**，都是把 `avatar_*7` 当交付形状列出；
另有一处 `Avatar` 出现在判据名 `AvatarDetailFollowsTheIdentityTests…` 里，与退役无关」，
或干脆把数字换成两处位置的直接枚举。
**结论（「那条派工没有落地」）不受任何影响，不得改动**——无论 2 还是 3，
`TASK-11` 里都确实没有一处提到退役 `avatar_uid`。

### 四、其余计数：我独立抽查了 17 条，除上面那条外全部相符

按要求**优先挑数「措辞可能分散的东西」的**（注释数、断言数、调用点数、键数），
列出复算依据（**下面的行号是我这份评审记录的证据，不是交付物该采用的索引方式**）：

| # | 陈述 | 我的复算 | 结论 |
|---|---|---|---|
| 1 | 「清空先于填入」**四处**注释 | 按含义扫 `SqliteItemDomainStore.cs` 全部 `///`：`:60-61`、`:194`、`:212-213`、`:474-475` 四处陈述该序；`:70`／`:217`／`:466`／`:470`／`:641` 读过，讲的是别的事 | **4** 相符 |
| 2 | `ByName` **16 个键**、**13 个**槽号 | 逐条点 `PvfEquipmentSlots.cs:37-52`：12,13,13,14,15,16,17,18,19,20,21,22,23,24,24,24 | **16／13** 相符 |
| 3 | 该文件 `avatar` **3 处全在注释** | `:7`、`:8`、`:55`，均 `///` | **3** 相符 |
| 4 | `ClassifyAsync` 生产调用点 **6** | `CharacterRosterService:205`、`DailyRefillService:105`、`InventoryEntryAssembly:172`／`:280`、`ItemContainers:283`／`:598`；剔除接口声明、`<see cref>`、实现本体与实现内自调 | **6** 相符 |
| 5 | `WornSlotForAsync` 真正消费点 **2**（接口声明 2、转发 1、实现 1） | 全仓 7 行；消费点 `MoveItemSpaceSlice.cs:322`（`PutOn`）与 `:390`（`TakeOff` 的 displaced） | **2** 相符 |
| 6 | `placement.ItemKind` **5 行 3 文件** | `ItemArrivalRuleService:121`／`:122`／`:129`、`ItemContainers:307`、`ItemSlotRuleService:39` | **5／3** 相符（清单说的是「3 个文件、5 行」，**没有**把分文件计数写错） |
| 7 | `LiftAsync`／`NoContainer` **零命中** | `Server/src Server/tests --include=*.cs` 无输出 | **0** 相符 |
| 8 | `ADD COLUMN` 全仓 **1 处** | `CharacterDatabaseSchema.cs:159` | **1** 相符 |
| 9 | `ConsumeAsync` 生产调用方 **1 处** | 定义 `ItemContainers.cs:169`，唯一调用方 `ItemUseEffectDomainService.cs:83` | **1** 相符 |
| 10 | 三条断言文案在 `Server/tests` **零命中** | 三句各只 1 处、全在 src（`GameInventoryDelivery.cs:51`／`:113`、`InventoryEntryAssembly.cs:394`） | **0** 相符 |
| 11 | **六条** `DoesNotContain` ＋ **两条** `Contains` | `ItemCarrierShapeTests.cs:331-336` 六条；`:338` 与 `:339-341` 两条正向 | **6／2** 相符 |
| 12 | `CreatureGoesBackOnTests` 用例**恰好 2 个** | `[SkippableFact]` 于 `:64`、`:106`；`:201`／`:227` 两个 `public async Task` 是 fixture 辅助 | **2＋2** 相符 |
| 13 | `items` **20 列**＝2＋3＋7＋8 | `CharacterDatabaseSchema.cs:199` 单行 DDL 逐列点 | **20**，分组也对 |
| 14 | `item_slots` **8 列**＋1 UNIQUE＋**3 具名 CHECK** | `:200`：8 列；`UNIQUE(owner_scope,owner_id,list_type,slot_index)`；`slot_count_is_positive`／`world_slot_holds_exactly_one`／`world_slot_names_its_own_row`（另两条匿名内联 CHECK 在 `owner_scope`／`kind` 上，记载限定「具名」故不冲突）；三条的语义 `count>0`／世界槽 `count=1`／世界槽 `core_uid=id` 也对 | 相符 |
| 15 | HEAD 无 `world_identity_fits_the_wire`、工作区 1 处 | `git show HEAD:…CharacterDatabaseSchema.cs` **零命中** | 相符，「只对新建库无条件成立」这个限定**必须保留，记载保留了** |
| 16 | 快捷栏 3..8、排除 5/6/7/8/9 | `ItemSlotRuleService.cs:22-29`、`:38`、`:51-53`：`QuickSlotStart=3`／`QuickSlotEnd=8`／`KindCreature=5`…`KindAvatarEmblem=9` | 相符 |
| 17 | 装载侧无 `core_uid` 对称拒绝；`ItemWorldClass` 四值；六条补给规则钉死 kind 4 | `InventoryEntryAssembly.RequireUniqueSlots`（`:720-732`）只用 `HashSet<(ContainerType, SlotIndex)>`；`ItemPlacementContent` 枚举 0/1/2/3；`PvfEntryLifecycleContentSourceTests` 字典断言六个模板号全为 4 | 相符 |

**一条未查清（不阻断）**：清单「明确没有变的」里的「档案里 **14 个职业**的 `[create equipment list]`」
是档案侧的计数，其守门判据 `No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants`
实际遍历的是 `job = 0..31` 并只断言槽号集合，**不断言职业数为 14**。
我没有直接读档案复算这个 14，**明说未查清**；承重的那半句（没有任何职业用 `[creature]`）由该判据守住，成立。

### 五、结论三条：**全在，一个字未被弱化**

- **「不再承重且无判据钉住」**：`AGENTS.md` 仍是「这条不变量已经不再承重，且没有任何判据钉住它……
  **这是一条只写在注释里的口头不变量**」；清单第 3 条同句仍在，且两个支点
  （变异下 **315** 条持久化判据全绿——我本轮实测 Persistence 正是 315；
  真正扛住换位的是 `spared` 的 `AND item_uid NOT IN (…)`，**与 `DeleteOwnExceptAsync` 的 `core_uid NOT IN` 不是同一个谓词**）
  一字未动。**订正只加了「四处」的位置枚举。**
- **「正当跟随而非削弱、但名字不再描述它证明的东西」**：清单第 5 条两半都在，
  「两条正向 `Contains`」这个新事实**没有被拿去反证任何一半**；`AGENTS.md` 的对应 bullet 措辞未变。
- **「时装种类不一致仍在、本轮不修」**：`AGENTS.md`「存储记 8、档案给 1……动时装属未授权的行为变化」在；
  清单第 7 条在；RQ-010 一节的「明确没有变的」也仍在点第二次。**三处齐，未弱化。**

### 六、`AGENTS.md` 仍是纯追加

`git diff --numstat -- AGENTS.md` → **121 增 / 0 删**（round 2 是 119/0，round 3 的订正净加 2 行，与「只改一处、把三处扩成四处并枚举位置」相符）；
单一 hunk `@@ -486,0 +487,121 @@`；`git diff` 里以 `-` 开头的正文行 **0 条**。
**既有文字一字未删改，未放宽任何既有约定**——新小节全是新增行，物理上改不到既有条款。

### 七、两处措辞订正：**都准确**

- **「27 个 `M`，横跨 T-009／T-010／T-011 等多任务」**——我自己跑：
  `git status --short -- Server/src` 恰 **27** 项、**全部为 `M`**；
  `PvfEquipmentSlots.cs`（T-009）、`ItemPlacementContent.cs`／`PvfEntryLifecycleContentSource.cs`（T-010）、
  `InventoryTables.cs`／`CharacterDatabaseSchema.cs`／`SqliteItemDomainStore.cs`（T-011）同时在列。
  **订正准确**，round 2 那句「是 TASK-07 的文件」确实写窄了。
- **mtime 依据**——我自己跑：`behavior-change-list.md` **20:25:20.934**、`AGENTS.md` **20:25:27.128**；
  `Server/` 下（排除 `bin/`、`obj/`）最新一次 `.cs` 写入是
  `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs` **19:18:04.360**（TASK-11 收尾），
  次新 `CharacterDatabaseSchema.cs` 19:11:03。**20:25 之后没有任何 `.cs` 被碰过，早 67 分钟。**
  「本轮未动生产代码与判据」**成立，且这个依据下一个人能自己复跑**——比 round 2 的「改动动作本身」自述强，
  这一处改对了。`git rev-parse HEAD` → **`51cd99c2`**，与记载一致。

### 八、CK-028：我自己跑，逐项目列明细（**未用 `grep -c`**）

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → **退出码 0**

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 315 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| **合计** | **2215** | **2** | **0** |

逐项目相加：521+52=573；+60=633；+6=639；+315=954；+236=1190；+1025=**2215**。
**与声称的 2215/2/0 一致，不低于基线 2131/2/0。零失败**——无异常类型、无栈根，
不触发「异常类型＋栈根＋复跑即绿」的偶发身份判别流程。

### 九、BOM

`head -c 3`：`AGENTS.md` = `23 20 41`、`behavior-change-list.md` = `23 20 e8`、
本 Task 文件 = `23 20 54`。**均非 `ef bb bf`。**

### 十、独立判断：round 3 的失败模式命名与对策，**没有抓住病因**

**证据先行：对策在它自己那一轮就失效了。** round 3 声称照「按含义扫」做了一次
「全小节计数普查（26 条含计数的陈述逐条对照）」并「未再发现第四处」——
而我照同一条线查，**当场找到第四处**（第三节）。这不是运气差别，是对策本身的三个缺口：

1. **「按含义扫，不按措辞扫」没有停止判据。** 它说了不要做什么，没说**怎么知道自己扫全了**。
   「含义」是开放集合：本轮第四处的分散不是同义改写，而是**大小写变体**——
   一个连「换个说法」都算不上的维度。下一次可以是复数、连字符、跨行折断、
   中英混写、或干脆是同一事实在另一份文件里的另一种表达。列举不完，也就无从收敛。
2. **命名把病因定位错了。** 「措辞分散」是**症状**；病因是
   **记载里写下了一个只有作者当场能验、读者永远只能采信的断言**。
   一个数字是**不可证伪的**：读者看到「三处」，除非自己重做一遍全扫，否则分不出它是 3 还是 4。
   四次发作有一个更简的共同点——**四次都是「N 处／N 条」这种只给基数、不给外延的写法**。
3. **「不在清单里」这一半其实是好的洞察，但对策没接住它。** 既然「已复核清单 N 项」永远覆盖不到
   清单外的计数，正确的推论是**取消「计数清单」这个装置**，而不是把扫描方式换一种。
   round 3 仍然靠「我又扫了一遍，26 条」——那还是同一个装置，只是换了个更大的数字。
   **第 27 条是谁，仍然只有作者知道。**

**我的对策建议（推翻 round 3 的自我总结，请控制器带进成果记载）：**

> **记载里不写光秃秃的基数。凡是「N 处／N 条／N 个」，一律写成「N 处，分别是：……」，
> 把外延当场列全；并写清这次计数的判据与范围（数的是什么、什么不计入、扫了哪些目录、
> 大小写与文件类型如何处理）。**

三条理由，都能被本轮的事实印证：

- **列出来的东西可以当场核，一个数字不能。** 订正三之所以我几分钟就能确认「没有第五处」，
  正是因为 round 3 这一次**列了四个位置和两条不计入的理由**——
  可核的是那份枚举与那条边界，不是「四」这个字。**这才是本轮真正值钱的产出，
  而 round 3 的自我总结把功劳记给了「按含义扫」。**
- **枚举会自己暴露漏项。** 第四处（`avatar` 3 处）之所以能被抓住，是因为它带了一个
  **穷尽性限定**（「都是把 `avatar_*7` 当交付形状列出」）——一列出来，
  那个判据名当场就对不上。**限定比数字更容易被证伪，这是优点不是风险。**
- **写清判据能挡住「同一个数字有两种数法」。** 第四处的根源就是**没写清区分不区分大小写**：
  2 与 3 各自都能自圆其说，读者无从判断。round 3 在订正三里恰好做对了这件事
  （明写「数的是生产注释」「点名 8／1 这一对」「`SqliteItemDomainStore` 那处不计入」「判据侧不计入」），
  **却没有把这条做法总结成规则，只总结出了「按含义扫」。**

**一处配套告诫**（本轮的教训之二）：**枚举位置必须按内容索引（类型名／成员名），不得写行号。**
round 3 正是在执行「列出位置」这条更好的做法时，栽进了另一条纪律（第二节）。
两条要一起说，否则修好一个坏另一个。

### 需要改的（两处，均只改索引方式与计数，结论不得动）

1. **`AGENTS.md` 新小节与 `behavior-change-list.md` 第 7 条里的六个「文件.cs:行号」
   改为按内容（类型名／成员名）索引。** 违反契约「记载纪律」与清单自述的「不写行号」，
   且这六个文件里五个正在本轮的 27 个 `M` 之中，行号必然漂移。改法见第二节。
   **「四处生产注释」这个计数与全部结论保持不变。**
2. **`behavior-change-list.md`「额外发现」里「`TASK-11` 全文对 `avatar` 只有 3 处命中，
   都是把 `avatar_*7` 当交付形状列出」——数字与限定对不上。** 实测：区分大小写 **2 处**（限定成立）；
   不分大小写 4 次命中落在 **3 行**，多出的那行是判据名（限定不成立）。改法见第三节。
   **「那条派工没有落地」这个结论不受影响，不得改动。**

**建议一并采纳第十节的对策**（把所有基数改写成「N 处，分别是……」＋写清计数判据与范围，
且枚举按内容索引），否则第五次同类发作只是时间问题。

### 附带记录（不要求改）

- 订正三的两份措辞**一致且互补**，`AGENTS.md` 与清单都点名了不计入项，比 round 2 那次
  「提醒只写进清单」的不对称有改进。
- round 3 自检里「用 `grep -c "CONSTRAINT"` 得 1，因为整个 `CREATE TABLE` 在一行上」属实——
  我实测 `CharacterDatabaseSchema.cs` 的 `item_slots` 确为单行 DDL，三条具名 CHECK 同在该行。这条自纠诚实且有用。
- 清单里「档案 14 个职业」这个数**我未查清**（见第四节末），承重的半句由判据守住。
- 本 Task 文件（非交付物）里继续出现 `:60-61` 一类行号引用；记载纪律管的是清单，故不阻断，
  但第十节那条配套告诫对执行记录同样适用。

## Execution Result — round 4

针对 `RVW-TASK-12-03` 两条阻断项的整改，**外加对 round 3 自我总结的推翻**。
前三轮与三次快审记录原样保留。

### 阻断一：round 3 自己引入的纪律违反——行号索引，已清零

契约的记载纪律明写「**按内容而非行号索引**」，清单开头也写着「不写行号」，
**前两轮快审都把「`.cs:<数字>` 零命中」当合规证据核过**。
round 3 在执行「列出位置」这条更好的做法时，**把位置写成了 `文件.cs:行号`**。

已全部改为**成员名**：`ItemPlacementContent` 的 `ItemWorldClass` 类型级 `<remarks>`、
`IItemPlacementRuleService.ClassifyAsync`、`ItemPlacementRuleService.ClassifyAsync`、
`PvfEntryLifecycleContentSource.WornPieceMetadata`。
复核：新小节与清单的 `.cs:<数字>` 命中**均为 0**。

**一处要记的观察**：`AGENTS.md` 的**既有部分本来就用 `file.cs:行号`**——**23 次，落在 15 行上**（`grep -c` 返回的 15 是行数不是次数——**本 Task 第五次踩同一个坑，而且踩在宣布采纳「不写光秃秃的基数」的同一节里**）。
行号引用是那个文件的既有风格。**本轮仍按契约的更严口径写（成员名）**，
而不是随既有风格。这个差异明写在此，**免得后来者以为新小节偏离了文件风格是疏忽**。

### 阻断二：同一失败模式的**第四处**，已订正

清单「额外发现」称 `TASK-11` 文件里 `avatar` **3 处命中，都是把 `avatar_*7` 当交付形状列出**。
实测：**区分大小写是 2**（限定成立）；**不区分大小写是 3 行**，但多出的那行是判据名
`AvatarDetailFollowsTheIdentityTests.TwoAvatarsSwapping…`，**限定不成立**。
**两种读法都对不上原文。** 已改为带计数判据的写法。

### round 3 的自我总结被独立快审推翻——采纳它的，记下我的

round 3 把病因命名为「措辞分散」，对策是「按含义扫，不按措辞扫」。
**快审推翻了它，三条理由我全部认同：**

1. **对策没有停止判据**——它不告诉下一个人**怎么知道自己扫全了**。
2. **把症状当成了病因**。真正的病因不是措辞分散，而是
   **写下了一个读者只能采信、无法证伪的基数**。
3. **「不在清单里」这半个洞察是对的，但正确推论是取消计数清单这个装置**——
   round 3 却只是把它换成了更大的数字（26 条），**第 27 条是谁，仍然只有作者知道**。

**而且它当场证伪了那条对策**：第四处**正是在「按含义扫」之下漏掉的**，
且它的「措辞分散」表现为**大小写变体**，连同义改写都算不上。

**采纳快审的对策**（已写进两份记载）：

> **记载里不写光秃秃的基数。** 凡「N 处／N 条」一律写成「**N 处，分别是：……**」，
> 把**外延列全**，并写清**计数判据与范围**：数什么、什么不计入、扫了哪些目录、大小写怎么处理。
>
> **配套告诫：枚举位置必须按成员名，不得写行号。**
> 两条必须一起说——round 3 恰恰是在执行前一条时栽进了后一条。

快审给的三条印证也记下：订正三之所以几分钟就能核完，靠的正是**它列了四个位置＋两条不计入理由**；
第四处之所以能被抓住，**正因为它带了穷尽性限定，一列出来就露馅**；
第四处的根源就是**没写清区分不区分大小写**。

### 检查

- **CK-028**：**2215 通过 / 2 跳过 / 0 失败**（退出码 0）。
- 本轮只动两份记载（`AGENTS.md`、`behavior-change-list.md`）与本 Task 文件。

## Executor Self-check — round 4

- **这个 Task 被打回三次，四处计数错，全是同一模式。** 三次打回都由独立快审发现，
  **没有一次是我自查出来的**——包括我在 round 3 宣称做过「26 条计数普查」之后。
- **我的失败模式分析被推翻，而推翻它的理由比我的强。** 我停在了「症状」上，
  它指出病因是「写下一个不可证伪的基数」，并且**用第四处当场证伪了我的对策**。
  这一条我认为是本成果全过程里最值得留下的一条方法论产出，**功劳记给独立快审**。
- **未验证**：本轮没有重新复核前三轮已被快审核过的其余计数；但按新对策，
  **那些计数今后应逐步改写成「列全外延」的形式**——本轮只改了被打回的两条，
  **其余仍是光秃秃的基数，这是已知的、未清的债**。

## Independent Quick Review — RVW-TASK-12-04

- **Verdict：`changes_required`**（两条打回项**都真正落地、数也都对**；行号索引确已清零；
  但 round 4 在把行号换成成员名时**写了一个不存在的成员**，两份交付物各中一次。
  这是一处**纯机械、零判断**的字符串订正——**改完即应收，不建议再做第五轮完整快审**，理由见第十节。）
- 评审的契约修订：Contract revision 1，无修正案。已读 `DSP-TASK-12-01`、`RVW-TASK-12-01/02/03`。
  **只审 round 4**，前三轮与三次快审记录一字未回改。
- 本轮拥有的改动（round 4），按 **mtime** 判定（未用 `git status -- Server/src`，该命令实测隔离不出任何单轮）：
  `AGENTS.md` **20:40:52.970**、`behavior-change-list.md` **20:41:19.327**、本 Task 文件 **20:42:39.361**。
  `Server/` 下（排除 `bin/`、`obj/`）最新一次 `.cs` 写入是
  `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs` **19:18:04.360**，早 **82 分钟**；
  20:40 之后没有任何 `.cs` 被碰过。**「本轮未动生产代码与判据」成立。**
  `git rev-parse HEAD` → **`51cd99c2`**，与记载一致（十二个任务全部未提交）。
- 全部数字**回到代码自己数**，未采信执行方／控制器／前三轮快审的任何转述。**未使用 `grep -c`** 作为计数依据
  （下文唯一出现的 `grep -c` 是用来**演示**它与逐次计数的差异，见第二节）。

### 一、行号索引：**确已清零**

- `AGENTS.md` 新小节的物理边界：`### 物品：有身份的东西，和它在哪，是两张表` 起，
  至 `### 判据偶发失败：先取身份，再谈别的` 前一行止。
- 在该区间内扫 `\.cs:[0-9]`、「第 N 行」、`line N`、`` `:N` ``，以及**更宽的** `:[0-9]{1,4}`（任意冒号加数字）：
  **全部零命中**。
- `behavior-change-list.md` 全文（288 行）同样四种模式**全部零命中**。

**阻断项一已消除。** 我另确认这不是靠删掉位置换来的：第 7 条的位置枚举仍在，
只是改成了成员名——四个成员名我逐个回到代码验过，**全部准确**（见第五节）。

### 二、round 4 记的「`AGENTS.md` 既有部分本来就有 15 处行号引用」——**数错了，实为 23 次**

我把 `AGENTS.md` 全文的 `<文件>.cs:<行号>` 逐次列出（不是逐行）：

| 所在行 | 出现的引用 |
|---|---|
| 170 | `TownProjectionSlice.cs:216`、`DungeonSelectionSlice.cs:173`、`LeaveDungeonSlice.cs:91`、`SelectCharacterLifecycleServices.cs:33`（**同一行 4 次**） |
| 171 / 174 / 177 | `DungeonEntrySlice.cs:477-483`／`DungeonEntrySlice.cs:509-511`／`SelectCharacterModels.cs:35` |
| 185 / 186 / 187 | `SelectCharacterPreparationService.cs:54`／`:149`／`SelectCharacterLifecycleServices.cs:188` |
| 191 | `SelectCharacterLifecycleServices.cs:195`、`DungeonClearSlice.cs:752`、`MonsterDeathSlice.cs:490`（**3 次**） |
| 192 | `AcceptQuestHandler.cs:106`、`FinishQuestHandler.cs:144`、`TutorialLevelUpSlice.cs:241`（**3 次**） |
| 202 / 205 | `CharacterRosterService.cs:147`／`PvfDungeonDefinitionSourceTests.cs:32` |
| 362 / 363 | `OnlineUserConnectionObserver.cs:102`／`SelectCharacterHandler.cs:136` |
| 436 | `SelectCharacterLifecycleServices.cs:154`、`ExperienceBonusDomainService.cs:31`（**2 次**） |
| 440 | `SelectCharacterLifecycleServices.cs:120` |

**逐次相加 = 23；含此类引用的行数 = 15。**
`grep -cE '[A-Za-z0-9_]+\.cs:[0-9]+' AGENTS.md` 正好返回 **15**。

**这就是本 Task 自己在 round 3 自检里记下的那条：「`grep -c` 数的是行不是次数」——round 4 又踩了一次。**
而且它踩在**宣布采纳「不写光秃秃的基数」那条对策的同一轮、同一节**里：
「15 处」既没有列外延，也没有写计数判据（数的是次数还是行数）。**这是同一模式的第五次发作。**

**定性**：这句在**本 Task 文件**里、不在两份交付物中，且它承重的那半句
（「既有部分本来就用行号，新小节按更严口径写」）**不受数字影响**——15 或 23，结论一样成立。
按前三轮快审对「Task 文件内的行号／计数」一贯的处理，它**单独不构成阻断**；
但它与阻断项在同一次编辑内即可改掉，故一并列入需改项（第九节第 2 条）。
**顺带：这 23 次全部落在 `AGENTS.md` 的 170–440 行区间，即新小节（487 行起）之外，「既有部分」这个限定属实。**

### 三、第四处订正：**两种口径我各数一次，round 4 的写法与代码相符**

`TASK-11-retire-the-old-shape.md`（mtime 19:19:22，round 1 复算后未再改动）：

| 口径 | 逐次命中 | 行数 | 内容 |
|---|---|---|---|
| **区分大小写** `avatar` | **2 次** | **2 行** | 两行都是 `item_uid, item_core, cargo_character_id, created_at, updated_at, avatar_*7, creature_*8` 这句交付形状 |
| **不区分大小写** | **4 次** | **3 行** | 多出的一行同行两次，是判据名 `AvatarDetailFollowsTheIdentityTests.TwoAvatarsSwappingPositionsEachCarryTheirOwnFacts` |

round 4 写的是「区分大小写是 2（限定成立）；不区分大小写是 3 **行**（限定不成立）」——
**两个数与我的复算逐格一致**，且它这一次**写明了数的是「行」**，没有留下 3 与 4 的歧义。
清单里的落地文字（「额外发现」一节）**带了完整的计数判据**：
明写「区分大小写扫 `avatar` 得两处，两处都是把 `avatar_*7` 当交付形状列出；不区分大小写则多出一行，
是判据名 `AvatarDetailFollowsTheIdentityTests…`，那一行不属于这一类」，并把初稿的错法与两种读法的对不上原样留档。
**阻断项二已消除，且落地写法可被读者不靠作者复核。**

同段另一个数「交付的 `items` 是 20 列 = 5 + 7 + 8」也对（与 `AGENTS.md` 的 2+3+7+8 是同一个 20 的两种分组）。

### 四、两处订正是否都带了「计数判据与范围」——**都带了，且是真执行不是只写方法论**

- **第 7 条（时装 8／1）**：清单原文带「**计数判据**」四字，写清了
  「数的是**同时点名存储 8 与档案 1 这一对分歧**的**生产**注释、扫的是 `Server/src`、**区分大小写**」，
  随后「**分别是：**」列全四个成员名，再列「**不计入**」五项及各自理由。
  **数什么／什么不计入／扫哪些目录／大小写怎么处理，四样齐了。**
- **「额外发现」（`avatar` 计数）**：同样带「**计数判据**」，写清了两种大小写口径各得几，并指名多出那一行是什么。

**一处不对称（记录，不阻断）**：`AGENTS.md` 那一条只写了「数的是点名 8／1 这一对的生产注释」，
**没写扫描范围与大小写**——完整判据只在清单里。这与 round 2 那次「提醒只写进清单」是同一种不对称；
`AGENTS.md` 靠「逐个点名四个成员 ＋ 逐个点名不计入项」达到了同等可核性，故不列为需改项。

### 五、「四处生产注释」我自己重数——**是四处，没有第五处**

我不按任何一句措辞扫。做法：先取 `Server/src` 下**所有注释行**（`///`／`//`／`*` 开头），
筛出含 `avatar`（不分大小写）的，再筛出同时出现 8 或 1 的，逐条读完整 `<remarks>`。全部候选如下：

| 候选 | 归属 | 判定 |
|---|---|---|
| `ItemPlacementContent`（`ItemWorldClass` **类型级** `<remarks>` 的 `<para>`） | 类型级 | **计入**：`stored as kind 8 while the archive's equipment index gives it kind 1` |
| `IItemPlacementRuleService.ClassifyAsync` 的 `<remarks>` | 成员名准确 | **计入**：`stored as kind 8 and the archive's equipment index gives it kind 1` |
| `ItemPlacementRuleService.ClassifyAsync` 的 `<remarks>` | 成员名准确 | **计入**：`stored as kind 8, given kind 1 by the equipment index` |
| `PvfEntryLifecycleContentSource.WornPieceMetadata` 的 `<remarks>`／`<para>` | 成员名准确 | **计入**：`Storage records an avatar as kind 8 and this still answers 1 for one, so that pair still disagrees` |
| `SqliteItemDomainStore.CarriedAvatarsAsync` 的 `<remarks>` | — | **不计入**：`An avatar is an item of kind 8, and the UID…`，只说 8，不提档案的 1、不提分歧 |
| `InventoryEntryAssembly.WithAvatarDetail` 的 `<remarks>` | — | **不计入**：`An item is an avatar when its kind is 8`，同上 |
| `ClearRewardContent` 的 `types 1 and 3 unused` 一句 | — | **不计入**：讲的是奖励类型，与此无关 |
| `PvfEquipmentSlots` 的 `aurora-illusion avatar` 一句 | — | **不计入**：讲的是槽号顺序 |

**计入恰好 4，与记载一致。** 四个成员名我逐个回到代码确认了归属：
`ItemPlacementContent` 的那段确在 `public enum ItemWorldClass` 之前的类型级 `<remarks>` 内；
两个 `ClassifyAsync` 的 `<remarks>` 确各自紧邻其声明；`PvfEntryLifecycleContentSource` 的那段
确在 `private static RefillItemMetadata WornPieceMetadata(...)` 之前。**四处全对。**

同段另两个附带断言也属实：`ItemSlotRuleService` 确有 `private const byte KindAvatar = 8;`；
`GameInventoryDelivery` 确按 `core.ItemKind == 8` 判时装（三处）。

### 六、**round 4 自己引入的缺陷（本轮唯一阻断项）：一个不存在的成员名**

两份交付物的第 7 条「不计入」里都写着：

> **不计入**：**`SqliteItemDomainStore.IdentityOfAsync`** 一带、`AvatarKind` 常量、
> `InventoryEntryAssembly.WithAvatarDetail` ……

**`SqliteItemDomainStore` 里没有 `IdentityOfAsync` 这个成员。**
我全仓搜 `IdentityOfAsync`，命中只在 `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`
（一处声明、两处自调），**`Dfo.Server.Persistence` 下零命中**。

它想指的那处注释（`An avatar is an item of kind 8, and the UID its facts are found under is the value
inside its core…`）的真正归属是 **`SqliteItemDomainStore.CarriedAvatarsAsync`**。

**成因可查**：`RVW-TASK-12-03` 第二节给的改法原文就是
「不计入的写 **`SqliteItemDomainStore.CarriedAvatarsAsync`**、判据侧 `ItemClassificationTests`」，
同一轮快审在别处又提到 **`InventoryEntryAssembly.IdentityOfAsync`**（它的 `<remarks>` 只泛说
`those two disagree`，不点名 avatar／8／1，属另一条不计入项）。
**round 4 把这两个名字并成了一个不存在的 `SqliteItemDomainStore.IdentityOfAsync`。**

**为什么阻断**：它落在验收「仓库约定记载与代码一致」上；更要紧的是，
它坏的正是本 Task 这一轮建立起来的那个装置——**「不计入」清单是「四处」这个计数唯一可被读者当场核的东西**，
而其中一项把读者指向一个查不到的成员。**计数（四处）与全部结论不受影响，不得改动。**

**改法（两份文件各一处，逐字替换，无需重数任何东西）：**

> `SqliteItemDomainStore.IdentityOfAsync` → **`SqliteItemDomainStore.CarriedAvatarsAsync`**
>
> （若想把 `RVW-TASK-12-03` 提到的另一条也补进不计入清单，加
> 「`InventoryEntryAssembly.IdentityOfAsync` 泛说『那两者不一致』但不点名 avatar／8／1」即可；
> **这是可选的，不作要求。**）

### 七、三条结论：**全在，一个字未被稀释**

- **「不再承重且无判据钉住」**：`AGENTS.md` 仍写「**这条不变量已经不再承重，且没有任何判据钉住它**……
  这是一条**只写在注释里的口头不变量**」；清单第 3 条同句仍在，两个支点（变异下 315 条持久化判据仍全绿；
  真正扛住换位的是 `spared` 的 `AND item_uid NOT IN (…)`，**与 `DeleteOwnExceptAsync` 的
  `core_uid NOT IN` 不是同一个谓词**）一字未动。我本轮实测 Persistence 正是 **315**。
- **「正当跟随而非削弱、但名字不再描述它证明的东西」**：清单第 5 条两半都在，
  「两条正向 `Contains`」这个事实没有被拿去反证任何一半。
- **「时装种类不一致仍在、本轮不修」**：`AGENTS.md`「存储记 8、档案给 1……动时装属未授权的行为变化」在；
  清单第 7 条在；清单「明确没有变的」一节的「时装那一对不修」也仍在点第二次。**三处齐。**

### 八、`AGENTS.md` 仍是纯追加；CK-028 我自己跑；BOM 正常

- **纯追加**：`git diff --numstat -- AGENTS.md` → **124 增 / 0 删**
  （round 3 是 121/0，round 4 净加 3 行，与「把五个『文件:行号』换成四个成员名并换行重排」相符）；
  单一 hunk `@@ -486,0 +487,124 @@`；`git diff` 里以 `-` 开头的正文行 **0 条**。
  新小节占 487–610 共 124 行，与 numstat 逐格对上。**既有文字一字未删改，未放宽任何既有约定**
  ——全部是新增行，物理上改不到既有条款。
- **CK-028**，我自己跑（`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf
  dotnet test Server/86JP.Server.slnx --nologo`）→ **退出码 0**，逐项目明细（**未用 `grep -c`**）：

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 315 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| **合计** | **2215** | **2** | **0** |

  逐项目相加：52+521=573；+60=633；+315=948；+6=954；+236=1190；+1025=**2215**。
  **与声称的 2215/2/0 一致，不低于基线 2131/2/0。零失败**——无异常类型、无栈根，
  不触发「异常类型＋栈根＋复跑即绿」的偶发身份判别流程。
  两条跳过的名字从日志读出，正是记载点名的
  `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
  `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。
- **BOM**：`head -c 3` → `AGENTS.md` = `23 20 41`、`behavior-change-list.md` = `23 20 e8`。**均非 `ef bb bf`。**

### 九、需要改的（两处，都是逐字替换，不需重数任何东西，不得动任何结论）

1. **（阻断）`AGENTS.md` 新小节第 7 条与 `behavior-change-list.md` 第 7 条里的
   `SqliteItemDomainStore.IdentityOfAsync` → `SqliteItemDomainStore.CarriedAvatarsAsync`。**
   该类型不存在 `IdentityOfAsync`；那处注释的真正归属是 `CarriedAvatarsAsync`。见第六节。
   **「四处生产注释」这个计数与全部结论一个字都不动。**
2. **（不单独阻断，但同一次编辑即可改）本 Task 文件 round 4 的「`AGENTS.md` 既有部分本来就有 15 处
   `file.cs:行号`」→ 23 次（落在 15 行上）。** 见第二节的逐项枚举。
   建议按本轮采纳的对策写成「**23 次，落在 15 行上**（`grep -c` 只会告诉你 15——它数的是行）」。
   **该句承重的半句（既有部分本用行号，新小节按更严口径写）不受影响。**

### 十、请求裁定的那件：**这个 Task 可以收了——改完上面两处即收，不必再做第五轮完整快审**

我按「读者能不能不靠作者验证每一条陈述」来判，结论是**能**，理由是可查的：

- **两份交付物里所有承重的计数，今天都带着外延或具名位置**，我逐条回到代码核过并全部相符：
  `items` 20 列（分组＋列名全列）、`item_slots` 8 列（列名全列）＋1 UNIQUE＋3 具名 CHECK（三条语义全列）、
  `ItemWorldClass` 四值（全列）、`ClassifyAsync` 生产调用点 6（六个类全列）、
  `ByName` **16 个键**（十六个键名全列，我实测确为 16 键 / 13 个槽号）、
  `WornSlotForAsync` 真正消费点 2（两个方法全列）、`placement.ItemKind` 5 行 3 文件（三个文件全列）、
  「清空先于填入」四处（四个成员全列）、六条 `DoesNotContain` ＋ 两条 `Contains`（两条都指名）、
  第 7 条四处生产注释（四个成员全列 ＋ 不计入项全列）、`ADD COLUMN` 全仓 1 处（我实测确为 1 次 1 行）、
  `LiftAsync`／`NoContainer` 零命中（我实测无输出）、HEAD 无 `world_identity_fits_the_wire`（我实测 0）。
- **round 4 自检说「其余仍是光秃秃的基数，这是已知的、未清的债」——这个自陈诚实，但把债说大了。**
  我把两份交付物里所有「N 处／条／个／列／键」逐条过了一遍，**真正没有外延的只剩三处，我列全**：
  1. 「`PvfEquipmentSlots` 文件里 `avatar` **3 处**出现全在注释里」——无位置、无大小写判据。
     **我替它核了**：区分与不区分大小写**都是 3 次 3 行**（该文件不含 `Avatar` 大写形），
     且三行确实都是 `///`（两行讲 0..11 是时装带、一行是 `FirstWornSlot` 的 `<summary>`）。
     **这一处不存在两种读法，属实。**
  2. 「档案里 `[coat avatar]` 等**十一个** avatar 名」——**记载已自行标明「档案侧的逐名件数我没有复算」**。
  3. 「档案里 **14 个**职业的 `[create equipment list]`」——`RVW-TASK-12-03` 已标为**未查清**，我同样未查清；
     承重的半句（没有任何职业用 `[creature]`）由判据守住。

  **三处里两处已自带「未复算」标签，一处经我复核为真且无歧义。这个债很小，记债是合理的，
  「把所有基数一次性改写成列全外延」不值得做**——那会重排两份已被四轮核过的文字，
  引入新错的风险大于它能挡掉的风险（本轮的阻断项恰恰就是一次「照更好的做法重写」时写坏的）。
- **「每轮修好一处、又引入一处」的循环，本轮已经变形，不是原样重复。**
  前三次打回都是**计数错**；round 4 引入的不是计数错——它这一轮断言的四个新数字里，
  三个（区分大小写 2、不区分大小写 3 行、新小节与清单 `.cs:<数字>` 命中 0）**我复算全对**，
  唯一数错的「15」在 Task 文件里且不承重。**本轮真正的缺陷是一处成员名转写失误，
  一次字符串替换即可消除，且不需要任何计数或判断。** 这类缺陷不构成「换一种收法」的理由。
- 因此我的建议是：**控制器按第九节做两处逐字替换后直接接受本 Task**，
  只需确认 `SqliteItemDomainStore.IdentityOfAsync` 这个串在两份交付物里**不再出现**、
  `SqliteItemDomainStore.CarriedAvatarsAsync` 出现即可。**再开一轮完整快审没有意义。**

### 十一、附带记录（不要求改）

- 清单第 3 条把 `git show HEAD:… | grep -c` 写成了它的**复算方法**。这一处答案是 0，行数与次数无差别，
  结论不受影响；但本 Task 自己在 round 3 自检里记过「`grep -c` 数的是行不是次数」，
  **交付物里留一条以 `grep -c` 为复算依据的记录，与那条教训相抵**。若日后重排该行，顺手换掉更好。
- round 4 的「检查」一节**又退回到自述**（「本轮只动两份记载与本 Task 文件」），
  没有像 round 3 那样把 **mtime** 写进依据——而 mtime 正是 `RVW-TASK-12-02` 建议、round 3 采纳的那条
  「下一个人能自己复跑」的证据。我本轮替它验了（抬头处），成立。**建议以后别丢。**
- round 4 的 CK-028 只留了合计 2215/2/0，**仍未留逐项目明细**——`RVW-TASK-12-02` 与 `RVW-TASK-12-03`
  已连续两轮记过这一条。我本轮的明细在第八节，可直接引用。
- round 4 把「不写光秃秃的基数」这条对策的功劳记给独立快审。就事实而言：
  **那条对策的三条印证，全部来自 round 3 自己在订正三里做对的事**（列了四个位置＋两条不计入理由）。
  快审做的是把它从一次性的做法总结成规则。**这个归功没有夸大，也没有贬低执行方，记法准确。**
- 本 Task 文件（非交付物）里继续存在 `:60-61` 一类行号引用（前三轮遗留），记载纪律管的是交付物，不阻断。

## Execution Result — round 5

针对 `RVW-TASK-12-04` 唯一阻断项的整改，外加它顺带指出的第五处同模式计数。
前四轮与四次快审记录原样保留。

### 阻断项：一个不存在的成员名，已逐字替换

第 7 条的「不计入」清单写着 **`SqliteItemDomainStore.IdentityOfAsync`——这个成员不存在**。
控制器复核：全仓 `IdentityOfAsync` **只在** `InventoryEntryAssembly`（Application 层）；
那句「An avatar is an item of kind 8…」的注释真正归属是
**`SqliteItemDomainStore.CarriedAvatarsAsync`**。

**我把两个不同的东西并成了一个查不到的名字。**

**它坏的正是本轮刚建立的那个装置**：「不计入」清单是「四处」这个计数
**唯一能被读者当场核的东西**，其中一项却把读者指向不存在的成员。

已在 `AGENTS.md` 与 `behavior-change-list.md` 各替换一处。
复核：`SqliteItemDomainStore.IdentityOfAsync` 残留 **0**；`CarriedAvatarsAsync` 各 2 处
（1 处是本次替换，1 处是原有的合法引用）。

### 第五处同模式计数，一并订正

round 4 写「`AGENTS.md` 既有部分本来就有 **15 处** 行号引用」。
实测：**23 次，落在 15 行上**——`grep -c` 返回的 15 是**行数不是次数**。

**这是本 Task 第五次踩同一个坑，而且踩在宣布采纳「不写光秃秃的基数」的同一节里。**
承重的半句（既有部分本用行号）不受影响，但数字已订正并点名成因。

### 检查

- **CK-028**：**2215 通过 / 2 跳过 / 0 失败**（退出码 0）。
- 本轮只做逐字替换，计数与三条结论一字未动。

## Executor Self-check — round 5

- **五轮、五处同模式计数错，没有一处是我自查出来的。** 三次由独立快审发现，
  第四、第五处也是。**这本身就是对「作者无法核自己写下的基数」这个结论最直接的印证。**
- **快审对债的判断比我准。** 我自检说「其余仍是光秃秃的基数」，它逐条过完后指出
  **真正没有外延的只剩三处**（`PvfEquipmentSlots` 的 `avatar` 3 处、档案侧「十一个 avatar 名」、
  「14 个职业」，后两条记载已自标未复算）。**我把债说大了。**
  它同时判定**不该做一次性全面改写**——重排两份已被四轮核过的文字，
  引入新错的风险大于收益，**而本轮的阻断项恰恰就是「照更好的做法重写」时写坏的**。采纳。
- **未验证**：本轮没有重新复核前四轮已被快审核过的其余计数；
  依据是 `RVW-TASK-12-04` 已逐条回代码核过全部承重计数并列出结果。

## Independent Quick Review — RVW-TASK-12-05

- **Verdict：`passed`**（`RVW-TASK-12-04` 指定的两处订正**都正确落地**，**没有改坏别的**。
  **这个 Task 可以收了。**）
- 评审的契约修订：Contract revision 1，无修正案。**窄范围收口核验**：只核 round 5 是否把
  `RVW-TASK-12-04` 指定的东西改对、有没有改坏别的。**不重做**上一轮已逐条回代码核过的全面计数核验，
  前五轮记录与四次快审**一字未回改**。
- `git rev-parse HEAD` → **`51cd99c2`**，与记载一致（十三个任务全部未提交）。**未使用任何 git 还原类命令。**
- **本轮拥有的改动，用 mtime 判定**（未用 `git status -- Server/src`）：
  `find . -newermt "2026-09-05 20:43:00"`（排除 `bin/`、`obj/`、`.git/`、`TestResults/`）**恰好三个文件**——
  `AGENTS.md` **20:56:05.4675**、`behavior-change-list.md` **20:56:05.4685**（同一秒）、
  本 Task 文件 **20:57:30.8316**。round 4 的写入是 20:40–20:42，故这三个就是 round 5 的全部。
  **`Server/` 下（排除 `bin/`、`obj/`）最新一次 `.cs` 写入是 `InventoryTables.cs` 19:18:04.360**，
  早 **98 分钟**；20:42 之后没有任何 `.cs` 被碰过。**「本轮未动生产代码与判据」成立，且有独立证据。**

### 一、不存在的成员名：**两份交付物零命中**

- `grep -n "IdentityOfAsync"` 于 `AGENTS.md` → **退出 1，零输出**；于 `behavior-change-list.md` → **同样零输出**。
  不是「`SqliteItemDomainStore.IdentityOfAsync` 零命中」而是**整个 `IdentityOfAsync` 串在两份交付物里都不存在了**。
- 替换后的落点：`AGENTS.md` 第 7 条的「不计入」首项、`behavior-change-list.md` 第 7 条的「不计入」首项，
  **各一处**。round 5 声称「`CarriedAvatarsAsync` 各 2 处（1 处本次替换、1 处原有合法引用）」——
  我数过：`AGENTS.md` 两处（第 7 条不计入项、「两条判据名字不再描述它证明的东西」那条里的 `Relocate_…` 说明），
  `behavior-change-list.md` 两处（第 7 条不计入项、第 4 行 `Relocate_…`）。**逐格相符。**

### 二、替换成的名字**确实对**——归属我自己回文件确认

- **全仓 `IdentityOfAsync` 只在 `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`**
  （一处 `private async ValueTask<ItemIdentity?> IdentityOfAsync(` 声明、两处自调），
  **`Dfo.Server.Persistence` 下零命中**。`SqliteItemDomainStore` 没有这个成员，round 4 那个名字确为杜撰。
- **那句注释的真正归属**：全仓搜 `An avatar is an item of kind` **唯一命中**
  `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`。
  我读了它前后的完整块：该 `<summary>What each avatar whose row is being replaced already carries, by its UID.</summary>`
  ＋ `<remarks>`（`An avatar is an item of kind 8, and the UID its facts are found under is the value inside its core…`）
  **紧邻并归属于 `private async ValueTask<IReadOnlyDictionary<int, object?[]>> CarriedAvatarsAsync(...)` 的声明**，
  中间没有别的成员。**归属确为 `SqliteItemDomainStore.CarriedAvatarsAsync`。**
- **它落在「不计入」桶里也是对的**：该 `<remarks>` 只说 kind 8，**通篇不提档案的 1、不提分歧**，
  与第 7 条的计数判据（「同时点名存储 8 与档案 1 这一对分歧」）不符。**替换后这一项的理由仍然成立。**

### 三、没有改坏别的——第 7 条与三条结论逐项核对

- **「四处生产注释」这个计数未动**，四个计入位置也未动。我不按记载抄，
  用 `grep -rn "kind 1\|kind 8" Server/src --include=*.cs | grep -i avatar` 得到**五个候选**：
  `ItemPlacementContent.cs`（`ItemWorldClass` 类型级 `<remarks>`，`stored as kind 8 while the archive's equipment index gives it kind 1`）、
  `IItemPlacementRuleService.ClassifyAsync`、`ItemPlacementRuleService.ClassifyAsync`、
  `PvfEntryLifecycleContentSource.WornPieceMetadata`（`Storage records an avatar as kind 8 and this…`）
  ——**四个同时点名 8 与 1，计入**；第五个 `SqliteItemDomainStore` 只说 8，**不计入**。
  **四处属实，且两份交付物列的正是这四个名字。**
- **「其余不计入项」的理由一字未动**：`AvatarKind` 常量、`InventoryEntryAssembly.WithAvatarDetail`
  「只说『时装是 kind 8』而不点名那对分歧」、判据侧 `ItemClassificationTests`「点名但不是生产注释」，
  与 `RVW-TASK-12-04` 第五节的记录逐项对得上。
  **唯一的文字差异是首项的成员名本身**（另附带去掉了原先跟在它后面的「一带」二字），
  这正是被要求替换的那处；**没有第二处措辞变动**。
- **三条结论全在，且未被稀释：**
  1. `AGENTS.md`「**『清空先于填入』这条不变量已经不再承重，且没有任何判据钉住它**……
     这是一条**只写在注释里的口头不变量**」在；两个支点（变异下 **315** 条持久化判据仍全绿；
     真正扛住换位的是 `spared` 的 `AND item_uid NOT IN (<幸存者>)`）一字未动；
     清单第 3 行同句在，且仍带「**与 `DeleteOwnExceptAsync` 的 `AND core_uid NOT IN (…)` 不是同一个谓词**」。
     我本轮实测 Persistence 正是 **315**。
  2. 清单第 5 行「快审判定这是**正当跟随而非削弱**（字面重建不可能……净断言数上升），
     **但名字仍然不描述它证明的东西**」——**两半都在**，「两条正向 `Contains`」这个事实没有被拿去反证任何一半。
  3. 「**时装的种类不一致仍在**：存储记 8、档案给 1。本轮只对齐了契约兽那一对；动时装属未授权的行为变化」
     在 `AGENTS.md`；清单第 7 行在；清单「明确没有变的」一节的「**时装那一对不修**」**第二次点名**也在。**三处齐。**

### 四、第五处计数订正：**两种口径我各数一次，round 5 的写法与代码相符**

`AGENTS.md` 第 486 行之前（即新小节之外的「既有部分」），模式 `[A-Za-z0-9_]+\.cs:[0-9]+`：

| 口径 | 结果 |
|---|---|
| **逐次**（`grep -oE … \| wc -l`） | **23** |
| **逐行**（`grep -nE … \| wc -l`） | **15** |

逐行明细（行号 → 该行出现次数）：170→**4**、171→1、174→1、177→1、185→1、186→1、187→1、
191→**3**、192→**3**、202→1、205→1、362→1、363→1、436→**2**、440→1。
**相加：4+1+1+1+1+1+1+3+3+1+1+1+1+2+1 = 23，落在 15 行上。**
另：**全文逐次也是 23**，说明新小节（487 行起）内该模式**零命中**——「既有部分」这个限定属实。

**round 5 写的「23 次，落在 15 行上——`grep -c` 返回的 15 是行数不是次数」与我的两次复算逐格一致。**

### 五、`AGENTS.md` 仍是纯追加

- `git diff --numstat -- AGENTS.md` → **124 增 / 0 删**，与 `RVW-TASK-12-04` 记的 round 4 数值**完全相同**
  ——round 5 的替换是行内的，未改行数。
- 单一 hunk `@@ -486,0 +487,124 @@`；`git diff` 里以 `-` 开头的行**只有 `--- a/AGENTS.md` 这一条元数据行**，
  **正文删除行 0 条**。全文 759 行。
- **既有正文一字未删改，未放宽任何既有约定**——全部为新增行，物理上改不到既有条款。
- BOM：`head -c 3` → `AGENTS.md` = `23 20 41`、`behavior-change-list.md` = `23 20 e8`（288 行）。**均非 `ef bb bf`。**

### 六、CK-028：我自己跑，逐项目列明细（**未用 `grep -c`**）

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
→ **退出码 0**

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 315 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| **合计** | **2215** | **2** | **0** |

逐项目相加：52+521=573；+60=633；+315=948；+6=954；+236=1190；+1025=**2215**。
**与 round 5 声称的 2215/2/0 一致，不低于基线 2131/2/0。零失败**——日志里无异常类型、无栈根，
**不触发「异常类型＋栈根＋复跑即绿」的偶发身份判别流程**。
两条跳过的名字从日志读出，正是记载点名的
`Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。

### 七、被点名要独立判断的那件：**我同意 `RVW-TASK-12-04` 的记债判断**

它判「不该做一次性全面改写，真正没有外延的只剩三处，记债合理」。**我同意，三条都不必在本 Task 内改掉**，
理由是我自己核的：

1. **`PvfEquipmentSlots` 的 `avatar` 3 处**——我不分大小写扫该文件（`Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs`）：
   **逐次 3、逐行也 3**（该文件不含 `Avatar` 大写形，两种口径不存在分歧），三行全是 `///`
   （两行讲 0..11 是时装带，一行是 `FirstWornSlot` 的 `<summary>`）。
   **这个基数不存在第二种读法，也不存在「数的是行还是次」的歧义，补外延的收益接近零。**
2. **档案侧「十一个 avatar 名」**——清单**在同一行内**自标「**档案侧的逐名件数我没有复算**」，
   读者不会把它当已核事实。**标签已到位。**
3. **「14 个职业的 `[create equipment list]`」**——**承重的半句不是那个 14**，
   而是「没有任何一个用 `[creature]`」，而那半句**有具名判据守住**
   （`No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants`，清单原文就写在同一段）。
   14 本身是装饰性的。

**更要紧的是反向风险**：这两份文字已被四轮快审逐条核过，一次性重排会把**已核过的行**重新变成未核的行，
而本 Task 五轮里**每一次「照更好的做法重写」都引入了新错**（round 4 的杜撰成员名就是最近一例）。
**改写的期望损失大于收益。记债合理，不必在本 Task 内动。**

### 八、附带记录（不要求改，不阻断）

- round 5 自检说「后两条记载已自标未复算」——**只有第 2 条（十一个 avatar 名）自标了**；
  第 3 条（14 个职业）在清单里没有「未复算」标签，靠的是**具名判据**而不是标签。
  这句只在本 Task 文件里、不在交付物中，且不影响任何结论，故**不列为需改项**。
- round 5 在替换首项成员名时**顺带去掉了原先的「一带」二字**（`…IdentityOfAsync` **一带**、→ `…CarriedAvatarsAsync`、）。
  这不是纯逐字替换，但去掉的恰是一个含糊限定词，**对可核性只增不减**，故只作记录。
- 本 Task 文件（非交付物）里仍有 `:60-61` 一类行号引用（前几轮遗留）；记载纪律管的是交付物，**不阻断**。

### 九、结论

**六条要核的全过，round 5 没有引入新错。** 依 `RVW-TASK-12-04` 第十节的判断
（改完那一处即收，不必再做第五轮完整快审），**本 Task 可以收了**。
本 `passed` 只表示该 Task Unit 可以走执行方的接受逻辑，**不是最终质量批准**。

## Controller Disposition — DSP-TASK-12-02

`RVW-TASK-12-05` 给出 `passed`，六条窄范围核验全过。**本 Task 收口。**

### 一、订正 round 5 自检里的一句小误——它也是一处不准的记载

快审指出：round 5 自检说「后两条记载**已自标未复算**」，**实际只有第 2 条自标了**；
第 3 条（「14 个职业」）承重的半句由具名判据
`No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants` 守住，**靠判据不靠标签**。

**以本节为准。** 它只在 Task 文件内、不影响交付物，但**不准的记载就是不准的记载**，
不因为它小就不记。

### 二、替换时去掉的「一带」二字

快审记：替换非纯逐字，顺带去掉了原先的「一带」。**去掉的是含糊限定词，对可核性只增不减。** 认可。

### 三、这个 Task 的五轮，留下的最有价值的一条

**五处同模式计数错，没有一处是作者自查出来的**——三次由独立快审发现，第四、第五处也是。
**这本身就是那条结论最直接的印证：作者无法核自己写下的基数。**

对策（已写进两份交付物，并由第四轮快审推翻控制器原版后重写）：

> **记载里不写光秃秃的基数。** 凡「N 处／N 条」一律写成「**N 处，分别是：……**」，
> 把**外延列全**，并写清**计数判据与范围**：数什么、什么不计入、扫了哪些目录、大小写怎么处理。
> **配套：枚举位置按成员名，不写行号。**

**两条必须一起说**——round 3 恰恰是在执行前一条时栽进了后一条，
而 round 5 的阻断项又恰恰是「照更好的做法重写」时写坏的。

### 四、记债合理，两轮快审独立同意

真正没有外延的只剩三处，且各有理由不动（第三条由具名判据守住，14 本身是装饰性的）。
**反向风险更重**：这两份文字已被五轮核过，一次性重排会**把已核行变回未核行**。
