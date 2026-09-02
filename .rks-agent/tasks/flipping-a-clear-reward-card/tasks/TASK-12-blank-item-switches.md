# TASK-12 — 翻空保底的机制与每种牌的开关

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

把 `[gold card blank item]` 与 `[pcroom card blank item]` 的加权抽取实现出来，
由**每种牌各自的开关**控制是否发放，**三个开关默认全关**。

- Referenced spec requirements: RQ-016
- Referenced plan sections: `plan.md` 的 T-012；边界 BD-024
- Owned boundary: BD-024
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`
- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`
- 上述各处对应的判据文件

- Dependencies: TASK-11
- Required dependency outputs: TASK-10 与 TASK-11 的产出
- Acceptance conditions:

1. 三个开关（免费牌 / 黄金牌 / 网吧牌）可独立开合，**默认全关**。
2. 开态：翻空的牌给出归档为该种牌声明的保底物品，按归档权重抽取。
3. 关态：翻空的牌**不给出任何东西**——即默认配置下行为与 revision 3 一致。
4. 在其所在之处写明为何默认全关：黄金牌那件（幽灵赛丽亚的痕迹，物品 10002120）
   归档自述 2013-09-03 已删除；网吧牌那件（钻石硬币，物品 7454）属未实现的黑钻体系；
   免费牌归档根本没有兜底表。
5. **网吧牌本 Spec 不实现**，其开关与兜底表读取到位即可，不接入翻牌路径——
   翻牌协议的牌型字段只接受免费与付费两个取值。
6. 全量判据不低于交付前基线，失败为零，跳过仍为 2。

- Task-specific authorization: 根契约 revision 4 覆盖本任务。合并、推送、建 PR、删分支或清理 worktree 不在其内。
- Required methods:

- `test-driven-development`：开与关两态各先拿到红。
- 归档读法写明段落出处；开关的默认值与理由在其所在之处写明。
- 变异检查同 TASK-10 的要求。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-028 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` --filter Blank | 开与关两态各有用例且全绿 |
| CK-029 | `Non-command:` 默认配置下翻空不给任何东西 | 与 revision 3 行为一致 |
| CK-032 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于基线，失败零，跳过 2 |

- Required evidence: 三条检查的输出；两态各自先红后绿的原始输出。

## Contract Amendment 1 — 由 TASK-11 转来的两处

**来源**：TASK-11 独立评审的非阻塞观察二、三。控制器已核实。

**Permitted files 增补**：`Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs`
与 `Server/tests/Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs`，
**仅下述两处**，不得改其他用例的语义。

**Acceptance conditions 增补**：

7. `CardRewardTests.cs:1173` 的桩仍把 `PaidFlipCost` 的实参叫 `characterLevel`
   ——**全仓唯一还这么叫的地方**，改准为副本基准等级。
8. `What_a_paid_flip_costs_is_the_tables_own_price` 钉了形参个数却没钉形参**名**。
   **形参名是接口层区分「按角色等级取价」与「按副本等级取价」两种读法的唯一线索**，
   而上一条正说明它已经在别处漂了。把名字也钉住。

**可用的基线**：控制器已把 TASK-11 验收时的 63 个文件快照到
`scratchpad/baseline-after-TASK-11`，可据此 diff 出本 Task 改了什么。

## Execution Result — round 1

### 交付了什么

翻空保底的**机制**与**每种牌各自的开关**，三个开关默认全关；关态下行为与 revision 3 一致。

| 新增 | 位置 | 是什么 |
|---|---|---|
| `ClearRewardBlankCard`（`Free` / `Gold` / `PcRoom`） | `ClearRewardRules.cs` | 问「哪一种牌的兜底表」。**故意不复用 `ClearRewardCardKind`**：翻牌协议只认两个牌型（`CardRewardPayloadCodec.ReadSelectCardRequest` 只读牌面字节 0/1，其余丢帧），而归档的兜底是三种牌的事 |
| `ClearRewardBlankItemSwitches`（三个 bool + `Allows(card)`） | `ClearRewardRules.cs` | 三个开关本体，**三个都默认 false**，每个在自己的 XML doc 里写明为何关 |
| `ClearRewardRateOptions.BlankItems`（只读属性，`= new()`） | `ClearRewardRules.cs` | 开关挂在翻牌路径既有的那个 options 上。只读 → 永不为 null，且仍可用对象初始化器嵌套赋值与配置绑定 |
| `ClearRewardRules.BlankItemOf(rows, random)` | `ClearRewardRules.cs` | **加权抽取**：权重求和 → 掷一次 → 走累计和。与 `DropRules.Pick` 同一读法（归档把兜底行的第一列写成权重，和写池子一样）。**无行可抽时在碰生成器之前就返回 null** |
| `ClearRewardRuleService.FallbackRowsFor(table, kind)` | `ClearRewardRuleService.cs` | 牌型 → 牌 → 开关 → 行。关态返回空表 |
| `ClearRewardRuleService.Fallback(rows, random)` | `ClearRewardRuleService.cs` | 一行 → 一个 `ClearRewardCardItem`。品级取阶梯最低（归档的兜底行只有三个数，没有品级），耐久 0（两件都是 .stk 堆叠物，不是 .equ） |

**接入点**：`FaceFor` 里 `Draw(...) ?? Fallback(...)`，以及整面空白那条捷径 `Blank(kind, rows, random)`。
即「**一个空着的牌位，就发它那种牌声明的兜底**」，不问它为什么空。两处都接，是为了不让
`Blank(...)` 那句既有的「这只是省事，不是规则」失效——开态下若只接一处，两条路的答案就会不一致。

**为何默认全关**（写在 `ClearRewardBlankItemSwitches` 各属性的 doc 里，逐条）：

- **黄金牌**：六行都是物品 10002120 →
  `event/chn_event_20130723_GhostSeria/chn_stk_signs_of_seria.stk`「幽灵赛丽亚的痕迹」，
  其 `[explain]` 自述「该道具将于 2013 年 9 月 3 日 6 时统一删除」——归档自己声明已过期的东西。
- **网吧牌**：唯一一行 `50 7454 1`，物品 7454「钻石硬币」，启动黑钻售货机用，**本服务器没有黑钻体系**。
- **免费牌**：**归档根本没有这一节**。开关在，打开也照样什么都不发；不借用黄金牌那张表
  （借了就是本服务器替归档声明了一张它没有的表）。这条不对称是归档原样。

**网吧牌不接入翻牌路径**（契约第 5 条）：表读了、开关有、`BlankItemOf` 能抽它，
但 `ClearRewardCardKind` 没有网吧牌这一项，协议也没有第三个牌面字节。判据里钉住了这一点。

### Amendment 1 两处

- **第 7 条**：`CardRewardTests.cs:1173` 的桩形参 `characterLevel` → `dungeonBasisLevel`。
  改前全仓 grep 确认它是**唯一**一处（接口、实现、另一个桩 `CardRewardDomainServiceTests.cs:123`
  都已经是 `dungeonBasisLevel`）。
- **第 8 条**：形参**名**钉进 `What_a_paid_flip_costs_is_the_tables_own_price`
  （`Assert.Equal("dungeonBasisLevel", priced.Name)`，与既有的形参**个数**断言并列）。
  该用例不在 `PaidCardFlipCostTests.cs` 而在 `ClearRewardRuleServiceTests.cs:617`——
  两者都在许可范围内，`PaidCardFlipCostTests.cs` **未动**。

### 判据

| 检查 | 命令 | 结果 |
|---|---|---|
| CK-028 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo --filter Blank` | **全绿**：Application.Tests 7/7，Infrastructure.Tests 1/1（既有的归档读数用例）。开态 3 条、关态 3 条、纯规则 2 条 |
| CK-029 | `Non-command:` 默认配置下翻空不给任何东西 | 见自检「关态的三段论」 |
| CK-032 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | **1962 通过 / 0 失败 / 2 跳过**（基线 1955 / 0 / 2） |

## Executor Self-check — round 1

### 判据数字与增减对账

基线 **1955 / 0 / 2** → 交付 **1962 / 0 / 2**，差 **+7**，与新增用例数逐条对平：

| # | 用例 | 文件 | 覆盖 |
|---|---|---|---|
| +1 | `Blank_item_draws_are_weighted_by_the_archives_own_weights` | `ClearRewardRulesTests.cs` | AC-2 加权 |
| +2 | `Blank_item_draws_with_no_rows_answer_nothing` | `ClearRewardRulesTests.cs` | AC-3 关态不掷骰 |
| +3 | `Blank_item_switches_are_all_off_by_default` | `ClearRewardRuleServiceTests.cs` | AC-1 三开关独立 + 默认全关（含 DI 注册出来的那一个） |
| +4 | `Blank_items_are_what_a_switched_on_paid_card_hands_out` | `ClearRewardRuleServiceTests.cs` | AC-2 开态 |
| +5 | `Blank_items_are_handed_out_by_nothing_while_the_switches_are_off` | `ClearRewardRuleServiceTests.cs` | AC-3 关态 |
| +6 | `Blank_items_have_no_free_card_table_to_come_from` | `ClearRewardRuleServiceTests.cs` | AC-4 免费牌不对称 |
| +7 | `Blank_items_of_the_pcroom_card_are_read_and_switched_and_reach_no_flip` | `ClearRewardRuleServiceTests.cs` | AC-5 网吧牌读到位、不接入 |

**删除 0 条**。**原地改而不改数量 1 条**：`What_a_paid_flip_costs_is_the_tables_own_price`
（只加了形参名断言与一句注释，既有断言一条未动）。跳过仍为 2（两条既有的缺档案跳过）。

分项：Networking 51、Protocol 479、Pvf 60、Persistence 248、Host 4、
Infrastructure 215（+2 跳过）、Application 905 = 1962。

### 两态各自先红后绿（TDD 原始输出）

三步，每一步都是**真跑出来的红**，不是编译失败：

| 步 | 施加的状态 | `--filter Blank` 结果 |
|---|---|---|
| A | `BlankItemOf` 桩（`return null`），服务未接线 | **失败 5，通过 2，总 7** —— 开态全红 |
| B | 兜底接线完成，**摘掉开关判断** | **失败 2，通过 5，总 7** —— 关态那两条红（`…while_the_switches_are_off`、`…no_free_card_table_to_come_from`） |
| C | 加回 `if (!options.BlankItems.Allows(card)) return [];` | **失败 0，通过 7** |

步 A 里关态用例是「白过」的（还没有东西能违反它），步 B 正是给它取红——
默认关的特性，两态没法同时红，只能这样两轮。

### 变异检查（**全部在最终交付态上跑**）

七个变异，逐个：施加时 mtime 前进、还原后 sha256 与交付态快照逐字节相同（脚本自校验，
不匹配就 `exit 3`）。**没有一个是编译失败**——每次都跑到了测试并给出用例级失败。

| ID | 变异 | 文件 | 真实结果 |
|---|---|---|---|
| M1 | 加权走表换成 `return rows[0]`（仍掷一次骰） | `ClearRewardRules.cs` | **红 3**：`…weighted_by_the_archives_own_weights`、`…are_what_a_switched_on_paid_card_hands_out`、`…with_no_rows_answer_nothing` |
| M2 | 摘掉 `Allows` 开关判断 | `ClearRewardRuleService.cs` | **红 2**：`…while_the_switches_are_off`、`…no_free_card_table_to_come_from` |
| M3 | `GoldCard` 默认改成 `= true` | `ClearRewardRules.cs` | **红 1**：`Blank_item_switches_are_all_off_by_default` |
| M4 | 免费牌也取黄金牌的行 | `ClearRewardRuleService.cs` | **红 1**：`…no_free_card_table_to_come_from` |
| M5 | `Allows` 里 Free/Gold 对调 | `ClearRewardRules.cs` | **红 3**：`…switched_on_paid_card_hands_out`、`…all_off_by_default`、`…no_free_card_table_to_come_from` |
| M6 | 接口形参改回 `characterLevel` | `IClearRewardRuleService.cs` | **红 1**：`What_a_paid_flip_costs_is_the_tables_own_price`（Amendment 第 8 条确实被钉住了） |
| M7 | 无行可抽时**先掷一次骰**再返回 null | `ClearRewardRules.cs` | **红 1**：`…with_no_rows_answer_nothing`。**另跑了一次全量**：1962 里只有这一条红，其余 1961 条全绿 |

M3 只红一条，原因查清了：`ClearRewardFixture.Service` 会把三个开关**显式**写成 false，
把属性默认值挡住了；真正盯默认值的是 `Blank_item_switches_are_all_off_by_default`
（它还断言了 `ApplicationModule` 注册出来的那个 options 三个都关）。这是分工，不是漏。

M7 的全量结果见下「仍未查明 / 我不确定的地方」第 2 条。

### 两把尺子

**cref 尺**（`-p:GenerateDocumentationFile=true -p:NoWarn=1591 -p:TreatWarningsAsErrors=false`，
未改仓里任何文件）：四个工程（Abstractions / Application / Application.Tests / Protocol.Tests）
生成的 XML 里，未解析的 cref 全仓只有 **3 处**，就是 `IQuestStore.cs` 那三个既知的
`!:Carried` / `!:Cleared` / `!:Tracked`，**与本主题无关，未动**。本主题新增的 cref **一个都没解析失败**。

被测集合确实包含我改的文件（这是控制器提醒的那一条）：
`Dfo.Server.Abstractions.xml` 里 `ClearRewardBlankItemSwitches|ClearRewardBlankCard|BlankItemOf` 命中 **33**，
`Dfo.Server.Application.xml` 里 `FallbackRowsFor|ClearRewardBlankItemSwitches` 命中 **6**，
`Dfo.Server.Application.Tests.xml` 里 `Blank_item` 命中 **7**。

**列宽尺**（`awk 'length>100'`，按字节，测的就是我改的这 7 个文件）：

| 文件 | 基线 >100 | 现在 >100 | `\r` 字节 |
|---|---|---|---|
| `ClearRewardContent.cs` | 3 | 3 | 0 |
| `ClearRewardRules.cs` | 0 | **0** | 0 |
| `IClearRewardRuleService.cs` | 1 | 1 | 0 |
| `ClearRewardRuleService.cs` | 0 | **0** | 0 |
| `ClearRewardRuleServiceTests.cs` | 3 | 3 | 0 |
| `ClearRewardRulesTests.cs` | 1 | 1 | 0 |
| `CardRewardTests.cs` | 20 | 20 | 0 |

**新增的超宽行为 0**（中途有 4 行超了，已折行）。行尾按 `\r` 字节数核，七个文件全是 0。

### 与基线的 diff

`diff -r` 对 `scratchpad/baseline-after-TASK-11`（63 个文件），**恰好 7 个文件不同**，
`protocol/` 那 5 个 md **一字未动**：

| 文件 | +行 | −行 | 授权来源 |
|---|---|---|---|
| `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs` | +210 | −1 | Permitted |
| `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` | +96 | −15 | Permitted |
| `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs` | +18 | −4 | Permitted（更新两处已过时的 doc：`ClearRewardBlankItem`、`PcRoomCardBlankItems`） |
| `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` | +12 | −0 | Permitted（只加 doc：`FaceFor` 与 `ClearRewardCardSlot`） |
| `Server/tests/…/World/ClearRewardRuleServiceTests.cs` | +321 | −8 | 对应判据文件 |
| `Server/tests/…/World/ClearRewardRulesTests.cs` | +84 | −0 | 对应判据文件 |
| `Server/tests/…/CardReward/CardRewardTests.cs` | +1 | −1 | Amendment 1 第 7 条（只改了那一个形参名） |

`−15` 那一块是 `FaceFor` 与 `Blank` 的重写；`−8` 是 fixture 的 `Table`/`Service` 加参数；
`−4` 是 `ClearRewardContent.cs` 两段 doc 的替换；`−1`（Rules）是文件首行加 `using` 时的位移。
`ApplicationModule.cs` **未动**，`PaidCardFlipCostTests.cs` **未动**。

### 关态的三段论（CK-029）

「默认配置下翻空不给任何东西」不是一句断言撑起来的，是三段各有判据的链：

1. **三个开关默认关**，包括 `ApplicationModule` 注册出来的那个实例
   —— `Blank_item_switches_are_all_off_by_default`（M3、M5 各能取红）。
2. **关 → 空表**：`FallbackRowsFor` 在碰兜底表之前就返回 `[]`
   —— `Blank_items_are_handed_out_by_nothing_while_the_switches_are_off`（M2 能取红）。
   该用例还把「表里有兜底行」与「表里根本没有兜底行」两个服务的牌面**逐位**比对，
   相同即证明关态根本没读那张表。
3. **空表 → 不掷骰**：`BlankItemOf([], random)` 在碰生成器之前返回 null，且生成器下一次
   `Next` 与未被问过的那个完全一致 —— `Blank_item_draws_with_no_rows_answer_nothing`（M7 能取红）。

三段合起来：关态下 `Draw(...) ?? Fallback(...)` 恒等于 `Draw(...)`，
`Blank(kind, [], random)` 恒等于旧的四个 null，随机序列一位不移。

### 仍未查明 / 我不确定的地方

1. **开关没有接进配置**，只能改代码打开。`ApplicationModule.cs` 不在 Permitted files 里，
   它构造 `ClearRewardRateOptions` 时只读 `ClearRewardRates:RateBasis` 一个键。
   我把这件事写进了 `ClearRewardRateOptions` 的 doc（说明开一个开关等于改那处注册）。
   **需要越界才能补，故没补。** 若评审认为「可独立开合」必须含配置键，这是一个已知缺口。
2. **「关态 == revision 3」是靠上面那条三段论成立的，不是靠回放一张 revision 3 的牌面。**
   M7 的全量运行证实了这一点：全仓 1962 条里**只有我新写的那一条**会因为「关态多掷一次骰」
   变红——既有判据全都不钉牌面的逐位序列。也就是说这条性质目前**只有一道保险**。
   我没有再加一层「把 revision 3 的指纹写成字面量钉住」，因为那要么得让测试工程编译在
   基线源码上（新 API 会编不过），要么就只是把当前实现的输出抄一遍、证不了它等于 revision 3。
3. **兜底也发给「整面空白」那条捷径**，是我的判断，归档没说。理由写在
   `ClearRewardRuleService.Blank` 的 doc 里：`Blank(...)` 原本自称「只是省事，不是规则」，
   若只在抽到空的那条路上发兜底，这句自述就不再成立，两条路会给出不一致的答案。
   反面理由也真实存在——「表都读不出来的场合还发东西」和本仓「absent 不等于 neutral」有张力。
   **这一条最值得复核。**
4. **兜底物品的品级取 `Common`、耐久取 0，是本服务器定的**，归档的兜底行只有三个数。
   理由写在 `Fallback` 的 doc 里（没掷过阶梯 → 取阶梯最低，与 `DropRules.RollRarity` 无阶梯时
   的答案一致；两件都是 .stk，堆叠物没有耐久）。归档没有反证，也没有正证。
5. **网吧牌的开关在生产代码里没有读者**：`Allows(PcRoom)` 这一支只被判据调用。
   这是契约第 5 条要求的状态（读到位、不接入），但它确实是本仓自己批评过的
   「像输入却到不了答案」的形状。我把它做成了「三张牌走同一个 `Allows`」，
   让那一支至少是同一段真代码而不是一个孤立字段。
6. **物品身份没有由我复核到归档原文**：10002120 与 7454 的名字、`[explain]`、.stk 归属，
   我用的是本主题 TASK-01（`:1004`、`:1042`、`:1057`）与 TASK-09（`:148`、`:149`）已记录的读数，
   以及 `finishing-and-leaving-a-run/card-reward-evidence.md`。我**没有重新开 PVF 验一遍**。
7. **四条既有的 CS1573 文档警告仍在**（`ClearRewardProfile.Bands`、`ClearRewardCardSlot.Item`、
   `FaceFor.occasion`、`OffersPaidFlipAsync.cancellationToken`）。都是改前就有的，
   补 `<param>` 会改动交付态、需要重跑七个变异，判断为不划算，故留着。

## Controller Disposition — 三处复核点：一处判反须改，两处准

控制器独立复核：全量 **1962 / 0 / 2**（基线 1955，+7 与新增用例对平），退出码 0。
两件保底品的身份控制器本人早前已直接读过归档（`chn_stk_signs_of_seria.stk` 的
「2013 年 9 月 3 日 6 时统一删除」与 `pcroomtoken_vendingmachine.stk` 的「黑钻售货机」），
执行者沿用既有读数**不构成未核**。

### 复核点一：兜底不该发给「整面空白」那条捷径 —— 判反，须改

执行者把这一条列为最值得复核的，对。**控制器查了触发条件，结论与它相反。**

`ClearRewardRuleService.cs:200-206` 走到 `Blank(...)` 的四个条件是：
`chance.IsImpossible`（某次查表失败）、稀有度阶梯缺失、阶梯退化（`HighestRoll <= 0`）、
该副本基准等级没有品级带。**四条全是「归档没答上来」，没有一条是「玩家掷输了」。**
执行者自己写的注释也这么说：「an occasion outside the tables -- a level in no band,
a difficulty or a party size the file does not list」。

而归档那张兜底表答的是**「掷过骰子之后这个位置空了」**——掷骰以查表成功为前提。
两条路含义不同。

**在数据缺失路径上发兜底，等于「归档答不上来时反而给玩家发四份奖励」**，
把一个可见的缺陷变成一次静默的发放。这与本 Spec 一路的标准正好相反：
缺陷要看得见，不要被奖励盖住。

执行者的反面理由（不让 `Blank` 那句「只是省事，不是规则」失效）是真的张力，
但应当反向解决：**这两条路本来就是不同的东西**，所以 `Blank` 确实是一条规则而非捷径——
它是「表没答上来」那条路。把它照实命名与记述即可。

**今天两种读法结果相同**（开关全关），所以这纯粹是关于将来的行为——
**而这正是最便宜改对、也最难在日后发现的时候。**

### 复核点二：开关未接配置 —— 准，不改

`ApplicationModule.cs` 不在 Permitted files，停手正确。契约只要求「三个开关可独立开合、
默认全关」，代码级开关已满足；`BlankItems` 是 `{ get; } = new()`，将来接配置无需改形状。
doc 里已如实写明「今日不从配置读」。记为后续事项，不在本 Task 办。

### 复核点三：「关态 == revision 3」靠三段论 —— 准，记录

三段各有判据，M7 给了一道保险。可接受；性质本身（默认关时行为与改造前一致）
已由全量判据数字与关态三条用例共同支撑。

## Contract Amendment 2 — 把兜底限定在「掷过骰子」那条路上（本条写在改动之前）

**Permitted files**：无新增，全部落在契约原有清单内。

**Acceptance conditions 增补**：

9. **`Blank(...)` 这条「表没答上来」的路不发兜底物品**，即使开关是开的。
   兜底只发给**掷过骰子之后仍为空**的位置。
10. **要有判据钉住**：把开关打开、并让表答不上来（缺品级带或缺稀有度阶梯），
    牌面为空且**不发任何东西**；该判据须能在现实现（照发兜底）下变红。
11. `Blank(...)` 的命名与注释按其真实含义改写——它是「表没答上来」那条路，不是「省事的捷径」。

**不在本条授权内**：改变掷骰路径上的兜底行为；改动开关的默认值。

## Execution Result — round 2

**你的结论我照做了，并且我同意它。** round 1 我把张力解成了「让 `Blank(...)` 继续当捷径」，
方向错了：那四个进入条件（`chance.IsImpossible`、缺阶梯、阶梯退化、该副本基准等级无品级带）
**没有一个是玩家掷输了**，全是「表没答上来」。归档那张兜底表答的是「掷过之后仍为空」，
掷过以查表成功为前提。在数据缺失路径上发兜底 = 归档答不上来时反而发四份，
把可见缺陷变成静默发放——与本 Spec 一路的标准相反。

### 三项改动

**第 9 条 — 那条路不发兜底。** `FaceFor` 的守卫改为 `return Undrawn(occasion.Kind);`，
`Undrawn` 只有 `kind` 一个参数，**不接兜底行、不接生成器**，四个位置一律 `null`。
兜底的取行与掷骰（`FallbackRowsFor` / `new DropRandom(seed)` / `pools.Load`）**移到守卫之后**，
所以「表没答上来」这条路连兜底表都不去取。掷骰路径上的
`Draw(...) ?? Fallback(fallback, random)` **一字未动**，开关默认值**一字未动**。

**第 11 条 — 命名与注释按真实含义改写。** `Blank(...)` → `Undrawn(...)`，
按「四个调用点的共同点是：**没掷过骰子**」重写注释，并写明为何这里即使开关是开的也不发：
兜底答的是「掷过而仍为空」，为一张没答话的表发东西，等于把读档失败变成发奖。
原来那句「这只是省事，不是规则」删掉了——它现在**是**一条规则（「表没答上来」那条路），
省的那点读取只是附带，注释里也这么写。

顺带把四处口径不一致的注释改准（都只是措辞）：`ClearRewardRuleService` 类注释、
`FallbackRowsFor`、`Fallback`、`ClearRewardBlankItemSwitches` 与 `ClearRewardRateOptions` 的摘要，
一律改成「**被抽过（drawn）而仍为空**的位置」；`IClearRewardRuleService.FaceFor` 的 remarks
加了一段，把「表答不上来 → 无论开关如何都是四个空位」写进接口契约。

**第 10 条 — 判据钉住，且先变红。**
新增 `Blank_items_are_not_handed_out_where_the_tables_could_not_answer`，
开关**打开**、兜底行**在表里**，走四条进入 `Undrawn` 的路：

1. `table with { Rarity = null }`——文件整节丢失；
2. `RarityLadder([0,0,0,0,0,0,1])`——阶梯退化（`HighestRoll = 0`）；
3. `dungeonBasisLevel: 87`——品级表没有该副本等级的行；
4. `difficulty: 9`——难度不在表里，压根没合成出概率。

每条扫 50 个种子，断言四个位置**全空**。末尾一条反证：**同一个服务、同一个空池子**，
换成一张答得上来的表，四个位置**全满**——证明开关确实是开着的，空不是因为开关没开。

**先红**：在 round 1 的实现（照发兜底）下跑，该用例**失败 1 / 通过 7 / 总 8**，
失败信息是 `Assert.All() Failure: 4 out of 4 items ... Assert.True() Failure`（四个位置都不空）。
改完后 8/8 全绿。

### 判据

| 检查 | 结果 |
|---|---|
| CK-028 `--filter Blank` | **全绿**：Application.Tests **8/8**，Infrastructure.Tests 1/1 |
| CK-029 | 关态三段论不变（round 1 自检），再加一条：表答不上来时**开态也不发** |
| CK-032 全量 | **1963 通过 / 0 失败 / 2 跳过** |

## Executor Self-check — round 2

### 判据增减对账

round 1 **1962 / 0 / 2** → round 2 **1963 / 0 / 2**，差 **+1**，逐条对平：

| 变动 | 用例 | 说明 |
|---|---|---|
| +1 | `Blank_items_are_not_handed_out_where_the_tables_could_not_answer` | Amendment 2 第 10 条 |
| 0 | —— | **删除 0 条，改语义 0 条** |

对 TASK-11 基线（1955）合计 **+8**。分项：Networking 51、Protocol 479、Pvf 60、
Persistence 248、Host 4、Infrastructure 215（+2 跳过）、Application **906** = 1963。
`--filter Blank` 由 7 条变 8 条（Application.Tests），Infrastructure.Tests 那 1 条不变。

round 1 的其余 7 条判据**一条都没改**，也没有一条因本轮改动而需要改：
它们要么走掷骰路径（`Blank_items_are_what_a_switched_on_paid_card_hands_out`、
`…have_no_free_card_table_to_come_from`、`…of_the_pcroom_card…` 都用能答话的表 + 空池子），
要么根本不碰服务（两条纯规则用例），要么比对的是两个都关着的服务
（`…while_the_switches_are_off`，用 `OnePiece()` + 最低难度，走的也是掷骰路径）。

### 先红的原始输出

| 步 | 状态 | `--filter Blank`（Application.Tests） |
|---|---|---|
| 红 | round 1 实现（`Blank(...)` 照发兜底）+ 新判据 | **失败 1，通过 7，总 8** |
| 绿 | `Undrawn(...)` 不发兜底 | **失败 0，通过 8，总 8** |

### 变异检查（**八个，全部在 round 2 的最终交付态上重跑**）

重新对交付态取了快照与 sha256（round 1 的旧快照已作废），八个逐个施加：
mtime 前进、还原后 sha256 逐字节相同（不匹配即 `exit 3`）。**没有一个是编译失败。**
施加前先用脚本核过八个锚点在交付态下各命中 **1** 次。

| ID | 变异 | 真实结果 |
|---|---|---|
| M1 | 加权走表换成 `return rows[0]` | **红 3**（`…weighted_by…`、`…switched_on_paid_card_hands_out`、`…with_no_rows_answer_nothing`） |
| M2 | 摘掉 `Allows` 开关判断 | **红 2**（`…while_the_switches_are_off`、`…no_free_card_table_to_come_from`） |
| M3 | `GoldCard` 默认改成 `= true` | **红 1**（`…all_off_by_default`） |
| M4 | 免费牌也取黄金牌的行 | **红 1**（`…no_free_card_table_to_come_from`） |
| M5 | `Allows` 里 Free/Gold 对调 | **红 4**（比 round 1 多一条：新判据末尾那条「开关确实是开的」反证也被打掉） |
| M6 | 接口形参改回 `characterLevel` | **红 1**（`What_a_paid_flip_costs_is_the_tables_own_price`） |
| M7 | 无行可抽时先掷一次骰 | **红 1**（`…with_no_rows_answer_nothing`） |
| **M8**（本轮新增） | **把 round 1 的行为装回去**：守卫处照发兜底 | **红 1**（`Blank_items_are_not_handed_out_where_the_tables_could_not_answer`）——第 10 条要求的「能在现实现下变红」由此二次证实 |

### 两把尺子

**cref 尺**：四个工程重新 Rebuild 生成 XML，未解析的 cref 仍只有
`IQuestStore.cs` 那两行三个（`!:Carried` / `!:Cleared` / `!:Tracked`）；
按 `warning CS1574` 定位，**全仓只有 `IQuestStore.cs:31` 与 `:32`**，本主题为零。
新的 `<see cref="Undrawn"/>` 解析成功（`Dfo.Server.Application.xml` 命中 3 处），
新判据的 doc 也在 `Dfo.Server.Application.Tests.xml` 里（`could_not_answer` 命中 1）。

**列宽尺**（`awk 'length>100'`，按字节，测的就是改动的这 7 个文件）：

| 文件 | 基线 >100 | round 2 >100 | `\r` |
|---|---|---|---|
| `ClearRewardContent.cs` | 3 | 3 | 0 |
| `ClearRewardRules.cs` | 0 | **0** | 0 |
| `IClearRewardRuleService.cs` | 1 | 1 | 0 |
| `ClearRewardRuleService.cs` | 0 | **0** | 0 |
| `ClearRewardRuleServiceTests.cs` | 3 | 3 | 0 |
| `ClearRewardRulesTests.cs` | 1 | 1 | 0 |
| `CardRewardTests.cs` | 20 | 20 | 0 |

本轮新增的超宽行为 0（中途 3 行超了，已折行）。`\r` 全 0。

### 与基线的 diff

仍是**恰好 7 个文件**，`protocol/` 5 个 md **仍一字未动**，未新增任何文件。
与 round 1 相比只有三个文件的数字动了：

| 文件 | round 1 | round 2 |
|---|---|---|
| `ClearRewardRules.cs` | +210 / −1 | **+219 / −1** |
| `IClearRewardRuleService.cs` | +12 / −0 | **+19 / −0** |
| `ClearRewardRuleService.cs` | +96 / −15 | **+102 / −13** |
| `ClearRewardRuleServiceTests.cs` | +321 / −8 | **+382 / −8** |
| 其余三个 | 不变 | 不变 |

`ClearRewardRuleService.cs` 的 `−15 → −13`：`Undrawn` 现在与基线的 `Blank` 同形
（都是 `kind` 一个参数、四个 `null`），只剩注释与名字不同，所以删除行反而少了两行。

### 仍未查明 / 我不确定的地方

1. **round 1 自检里那三条你已判准的，本轮未动**：开关未接配置（`ApplicationModule.cs` 不在清单）、
   「关态 == revision 3」靠三段论而非回放、物品身份沿用既有读数（你已确认那不算未核）。
2. **round 1 第 3 条（`Blank` 该不该发兜底）现已作废**，按 Amendment 2 改正；
   这一轮我没有再留同类的「自选解法」。
3. **`Undrawn` 的四个进入条件里，`chance.IsImpossible` 是复合的**——它可能来自
   四张表中任意一张答不上来（阈值带 / 难度 / 队伍 / 房间数），也可能来自
   房间数表**整张为空**。判据用「难度 9」取其中一条，没有把四张表逐张各取一次。
   四条走的是同一个 `IsImpossible` 分支，我判断逐张再拆没有增量，但这是我的判断。
4. **`Undrawn` 不再消耗生成器**，所以「表答不上来」的场合连种子都不掷。
   这与基线（revision 3）一致，也与「关态 == revision 3」不冲突；
   但它意味着开态下**两条路的随机消耗不同**——这本就无人依赖（没掷过就没有可复现的东西），
   注释里写明了。
5. **`ClearRewardBlankItemSwitches` 的类注释里我引用了 `ClearRewardRuleService.Undrawn`，
   用的是 `<c>` 而不是 `<see cref>`**：Abstractions 不引用 Application，cref 会解析不到
   （就会变成一个 `!:`）。这是有意的，不是漏写。

## Independent Quick Review — round 1

**Verdict: `passed`**（三条非阻塞观察，均为注释措辞与将来行为的记录，不要求本 Task 改动）

独立评审员，不继承执行者结论。以下每一条都是自己跑出来的；控制器已独立复核的三项
（全量数字、`Undrawn` 形状、两件保底品身份）按指示未重做。**未修改任何生产或判据文件**。

### 一、判据（自跑）

| 检查 | 命令 | 我跑出的结果 |
|---|---|---|
| CK-032 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | **1963 通过 / 0 失败 / 2 跳过**，退出码 **0** |
| CK-028 | 同上 `--filter Blank` | **全绿**：Application.Tests **8/8**、Infrastructure.Tests **1/1**，退出码 0 |

分项自加：Protocol 479 + Networking 51 + Pvf 60 + Persistence 248 + Host 4 +
Infrastructure 215 + Application 906 = **1963**。跳过的 2 条是
`PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的缺档案跳过，与本主题无关。

### 二、基线 diff（自跑，逐字节）

对 `scratchpad/baseline-after-TASK-11` 的 63 个文件逐个 `cmp -s`：
**恰好 7 个不同，56 个逐字节相同，0 个缺失**。执行者的报数属实。

- `ApplicationModule.cs` —— **逐字节相同**（关键：开关未接配置这件事没有被偷偷补上）。
- `protocol/` 下 5 个 md —— **逐字节相同**。
- `PaidCardFlipCostTests.cs` —— **逐字节相同**（Amendment 1 第 8 条落在
  `ClearRewardRuleServiceTests.cs`，属许可范围）。

另做了一次不依赖基线集合的越界扫描：全 worktree 内 `mtime > 基线快照时刻`、
排除 `bin/obj/.git` 的文件，只有那 7 个源文件加 `.rks-agent/` 下的三个流程文件。
**没有新增文件，没有基线集合之外的源文件被动。**

### 三、判据账（自算，不看执行者的表）

对 7 个改动文件数 `[Fact]`/`[Theory]` 与 `Assert.` 行，基线 vs 交付：

| 文件 | Fact 基线→现在 | Assert 行 基线→现在 |
|---|---|---|
| `ClearRewardRuleServiceTests.cs` | 17 → **23**（+6） | 72 → 106 |
| `ClearRewardRulesTests.cs` | 12 → **14**（+2） | 42 → 52 |
| `CardRewardTests.cs` | 44 → **44**（0） | 117 → **117** |

再对两个判据文件的**方法名集合**做 diff：**只有增加，没有删除，没有改名**——
新增的正是那 8 个（6 + 2），基线的一条不少。1955 + 8 = **1963**，与我跑出的数字对平。
**没有用例被删掉、被改名或被改弱。** `CardRewardTests.cs` 的 Assert 行数一字未变，
其唯一改动是 `:1173` 的桩形参名，与 Amendment 1 第 7 条一致。

`What_a_paid_flip_costs_is_the_tables_own_price` 是唯一被原地改的既有用例：
`Assert.Single(...)` 改成 `var priced = Assert.Single(...)` 并**加了**
`Assert.Equal("dungeonBasisLevel", priced.Name)`。既有断言一条未删、未松。属**加强**。

Amendment 1 第 7 条另行核实：全仓 `characterLevel` 的剩余命中全在 Channel / Quest /
SkillPoint / Experience 等无关处；clear-reward 与 card-reward 路径下只剩
`ClearRewardRuleServiceTests.cs:568/587` 与 `ClearRewardArchiveTests.cs:691` 三处
**局部变量**，正是「刻意把两种等级并列对照」的用法，不是接口形参。

### 四、我自己跑的变异（五个，全部在最终交付态上）

纪律：施加前先核锚点命中恰为 1；施加时 `os.utime` 把 mtime **前进**；跑完从
交付态副本还原并 `sha256` 逐字节校验，不匹配即 `exit 3`；**五个全部编译通过**
（无一是编译失败充红）。全部跑完后再 `sha256sum -c` 复核 7 个文件，**全部 OK**。

| ID | 变异 | 我跑出的真实结果 |
|---|---|---|
| **QM1** | **把 round 1 的行为装回去**：`ClearRewardRuleService.cs:204` 的守卫改成取兜底行 + 造生成器、四个位置一律发 `Fallback(...)` | **失败 1 / 通过 7 / 总 8**，红的正是 `Blank_items_are_not_handed_out_where_the_tables_could_not_answer` |
| QM2 | `BlankItemOf` 的加权走表换成 `rows[0]`（仍掷一次骰） | **红 3**：`…weighted_by_the_archives_own_weights`、`…are_what_a_switched_on_paid_card_hands_out`、`…with_no_rows_answer_nothing` |
| QM3 | 摘掉 `FallbackRowsFor` 里的 `if (!options.BlankItems.Allows(card)) return [];` | **红 2**：`…while_the_switches_are_off`、`…no_free_card_table_to_come_from` |
| QM4 | `ClearRewardBlankItemSwitches.GoldCard` 默认改成 `= true` | **红 1**：`Blank_item_switches_are_all_off_by_default` |
| QM5b | 无行可抽时**真的**先推进一次生成器再返回 null | **红 1**：`Blank_item_draws_with_no_rows_answer_nothing` |

**QM1 是本轮的关键复核**，它独立证实了 Amendment 2 第 10 条要求的「该判据须能在
现实现（照发兜底）下变红」：数字（1 红 / 7 绿 / 总 8）与执行者报的 round 2 先红输出、
以及它的 M8，三者一致。

QM2–QM5b 与执行者的 M1 / M2 / M3 / M7 命中的用例集合逐条相同。

**我自己的一次失误，如实记录**：QM5 第一版写成 `random.Next(Math.Max(1, ...))`，
结果**存活**。查明是我的等价变异而非判据漏洞——`DropRandom.Next`
（`DropRandom.cs:62-68`）在 `inclusiveUpperBound <= 1` 时**直接返回、不推进状态**，
所以那一版根本没有多掷骰。改成 `random.Next(1000)` 真正推进生成器后即 QM5b，取红。
**没有变异存活。**

### 五、Amendment 2 三条，逐条判定

**第 9 条（表答不上来那条路不发兜底，即使开关是开的）—— 落地。**
`ClearRewardRuleService.cs:198-206`：`FallbackRowsFor` / `pools.Load` /
`new DropRandom(seed)` 三者**全部在守卫之后**（`:208-210`），守卫处只有
`return Undrawn(occasion.Kind);`；`Undrawn`（`:296`）只收 `kind`，四个槽位一律 `null`，
既不碰兜底行也不碰生成器。**结构上不可能在该路径发出兜底。**

**第 10 条（判据钉住，且能在 round 1 实现下变红）—— 落地，QM1 独立证实。**
判据 `Blank_items_are_not_handed_out_where_the_tables_could_not_answer` 走四条路，
每条扫 50 个种子断言四位置全空，末尾一条反证（同一服务、同一空池子、换答得上来的表 →
四位置全满）确保「空不是因为开关没开」。该反证不是装饰：执行者的 M5（Free/Gold 对调）
正是靠它多取一条红。

**第 11 条（`Blank` 按真实含义改名与改注释）—— 落地。**
`Blank` → `Undrawn`，全仓无 `Blank(` 残留；round 1 那句「只是省事，不是规则」已删，
改写为「A rule, and named for … no dice was thrown」，并写明为何开态也不发。
措辞上有一处小瑕疵，见非阻塞观察二。

### 六、掷骰路径上的兜底行为没被动 —— 确认

`FaceFor` 的 `Draw(chance, ladder, grades, pool, random) ?? Fallback(fallback, random)`
（`:212-216`）与 round 1 一字未改。开态确为**按归档权重抽**而非固定发第一行：
QM2 把加权走表换成 `rows[0]` 后红 3。关态确为**什么都不给**：QM3 摘掉开关判断后红 2。

另核：`ClearRewardRules.BlankItemOf`（`ClearRewardRules.cs:466-491`）与
`DropRules.Pick`（`DropRules.cs:195-221`）**逐句同形**——同样 `Math.Max(0, Weight)` 求和、
同样 `total <= 0 || total > int.MaxValue` 早返、同样 `random.Next((int)total)`
一次、同样 `roll <= running` 走累计和、同样 `rows[^1]` 收尾。执行者「与 `DropRules.Pick`
同一读法」的说法属实，不是近似。

### 七、开关默认全关 + 关态与改造前一致 —— 确认

三个 `bool` 属性无初始化器（`ClearRewardRules.cs` 的 `FreeCard` / `GoldCard` /
`PcRoomCard`），`ClearRewardRateOptions.BlankItems` 是 `{ get; } = new()`。
QM4 把 `GoldCard` 改成 `= true` 后 `Blank_item_switches_are_all_off_by_default` 变红，
说明「默认全关」这条性质**有判据真的盯着**——包括 `ApplicationModule` 注册出来的那个实例
（该用例自建 `ServiceCollection` 走一遍 `ApplicationModule.ConfigureServices` 再取回）。

「关态 == 改造前」我按代码走了一遍，是结构上成立的：关态 → `FallbackRowsFor` 在碰
`table.GoldCardBlankItems` **之前**返回 `[]` → `Fallback([], random)` →
`BlankItemOf` 在 `total <= 0` 处返回 null **而不碰生成器**（QM5b 钉住）→
`Draw(...) ?? Fallback(...)` 恒等于 `Draw(...)`，随机序列一位不移。
守卫由基线的两处 `return Blank(...)` 合并为一处四条件短路，条件集合与结果（四个 null）
不变。判据侧由 `Blank_items_are_handed_out_by_nothing_while_the_switches_are_off`
把「表里有兜底行」与「表里没有兜底行」两个服务的牌面**逐位指纹**比对，两种牌型各 200 个种子，
并断言扫描里既有空位也有满位（否则比对说明不了任何事）。控制器 round 1 的裁定
（三段论可接受）我复核后同意。

### 八、越界 —— 无

只动了契约 Permitted files 与两条 Amendment 授权的范围：4 个生产文件（全在原清单）+
2 个对应判据文件 + `CardRewardTests.cs`（Amendment 1 授权）。见第二节。

### 九、执行者自认的两点

**（1）`IsImpossible` 是复合条件，判据只用「难度 9」取其中一条 —— 判准，但执行者的
陈述本身不完整，见非阻塞观察一。**
就其所述的那一层（阈值带 / 难度 / 队伍 / 房间数四张表任一查表失败）而言，四者确实
汇合到 `ClearRewardRuleService.cs:119` **同一条 `return` 语句**、产生**字面相同**的
`ClearRewardChance(0, options.RateBasis)`，逐张再拆没有增量。**判准。**

顺带纠正一处对执行者有利的误述：新判据的四条路里，只有「难度 9」走 `IsImpossible`，
另三条（`Rarity = null`、退化阶梯、缺品级带行）走的是守卫的**另外三个子句**。
也就是说该判据实际覆盖了守卫的**全部四个子句各一次**，比执行者自己描述的更全。

**（2）`ClearRewardBlankItemSwitches` 里用 `<c>` 而非 `<see cref>` 指
`ClearRewardRuleService.Undrawn` —— 判准。**
Abstractions 不引用 Application，`<see cref>` 必然解析不到而落成一个 `!:`。
`ClearRewardRules.cs:156` 用 `<c>` 是正确取舍，不是漏写。

### 十、cref 尺（自跑）

`dotnet build <csproj> --nologo -p:GenerateDocumentationFile=true -p:NoWarn=1591
-p:TreatWarningsAsErrors=false`，四个工程，**未改仓里任何文件**：

- `CS1574` 全仓只有 `Server/src/Dfo.Server.Abstractions/Quests/IQuestStore.cs:31,31,32`
  三条（`Carried` / `Cleared` / `Tracked`），既知、与本主题无关、未动。
- 生成的 XML 里 `cref="!:…"`：`Dfo.Server.Abstractions.xml` 只有那三个；
  `Dfo.Server.Application.xml`、`Dfo.Server.Application.Tests.xml`、
  `Dfo.Server.Protocol.Tests.xml` **各为零**。
- 被测集合确含本次改动：`Dfo.Server.Abstractions.xml` 里
  `ClearRewardBlankItemSwitches|ClearRewardBlankCard|BlankItemOf` 命中 33，
  `Dfo.Server.Application.xml` 里 `Undrawn|FallbackRowsFor` 命中 4。

**本主题相关的 `!:` 为零。**

### 十一、非阻塞观察（三条，均不要求本 Task 改）

**观察一 —— `IsImpossible` 比「表答不上来」更宽，注释把它说死了。**
`ClearRewardRules.cs:31` 是 `Rate <= 0 || Basis <= 0`。除了查表失败那一支
（`ClearRewardRuleService.cs:119`），它还包含两支**四张表全都答上来了、只是合成结果为零**
的情形：`ClearRewardRules.cs:428-429` 的 `scaledParty <= 0`（队伍加成为 0 或非有限），
以及 `:434` 的 `losing >= basis`（骰子补不上的差额）。
因此 `ClearRewardRuleService.cs:268` 那句
「Every route here is a table that did not answer」并非无条件成立——它只在 A21 上成立，
而 `ChanceOf` 自己的 remarks 已经写明「on A21 neither occurs」。
执行者 round 2 自检第 3 条把 `IsImpossible` 列举为「四张表任一答不上来」，同样漏了这两支。

**为什么不阻塞**：(a) 三个开关全关，今日零可观测差异；(b) 这是**基线就有的分类**
（revision 3 同样 `IsImpossible → Blank`），Amendment 2 明写「不在本条授权内：改变掷骰
路径上的兜底行为」，动它才是越界；(c) A21 上不可达。
**但值得记为后续事项**：若将来换一份让合成结果落到零的档案，「掷过而仍为空」的读法会
要求那一支**发**兜底，而现实现不发——这正是本 Spec 一路在防的那种「今天两种读法结果相同、
将来悄悄分岔」。建议把 `:268` 那句改成有条件的表述（如「on this archive every route
here is…」），或在 `IsImpossible` 处补一句说明它兼含「合成为零」。

**观察二 —— `ClearRewardRuleService.cs:267` 的「all four of its callers」不准。**
`Undrawn` 全仓**只有一个调用点**（`:204`）；四个是**进入条件**，不是调用者
（`Blank` 时代也只有两个调用点，从来不是四个）。下一句列举的正是四条 route。
Amendment 2 第 11 条的题目就是注释准确性，这一个词值得顺手改准。

**观察三 —— `rates.Load` 的位置挪了，无判据钉住（纯记录）。**
基线是 `ChanceOf` 内先 load、`IsImpossible` 成立时不再 load；现在
`ClearRewardRuleService.cs:198` 把 `rates.Load` 提到守卫之前，`ChanceOf` 内又 load 一次。
无行为与成本差异：`PvfClearRewardRateSource.Load`（`:166-169`）走
`_tables.GetOrAdd` 缓存，且 `ChanceOf` 本来就要 load。判据侧
`StubRates.Load`（`ClearRewardRuleServiceTests.cs:1280`）不计次，所以这一点没有判据钉住；
`RecordingPools` 只记 pool 的读取。不构成缺陷，记录备查。

### 十二、Verdict

**`passed`**

11 条验收条件（原 6 条 + Amendment 1 两条 + Amendment 2 三条）逐条核过，全部落地：
默认全关且有判据（QM4 取红）、开态按归档权重抽（QM2 取红）、关态什么都不给且随机序列不移
（QM3、QM5b 取红）、网吧牌读到位不接入、表答不上来不发兜底且能在旧实现下变红
（**QM1 独立取红，1 红 / 7 绿 / 总 8**）、命名与注释按真实含义改写、Amendment 1 两处到位。
判据账 1955 → 1963 逐条对平，无删无弱；越界为零；cref 尺本主题为零；
五个变异全部编译通过、全部取红、全部还原并 sha256 校验通过。
三条非阻塞观察均为将来行为与注释措辞，建议记入后续事项而非返工本 Task。

## Controller Disposition — 评审 `passed`，本 Task 置 `done`

控制器独立复核：全量 **1963 / 0 / 2**，退出码 0。
评审在交付态跑了五条变异、无一存活，其中 QM1（把 round 1 行为装回去）红的正是
Amendment 2 第 10 条要求的那一条判据——**该条要求由此得到独立证实，而非自证**。
基线 diff 它逐字节 `cmp` 了 63 个文件：**恰好 7 个不同、56 个相同、0 缺失**，
`ApplicationModule.cs` 与 `protocol/` 五个 md 逐字节相同。判据账它自算对平（+8）。

**两处值得记的评审行为**：

- **它记下了自己的一次错**：QM5 首版存活，查明是它自己的等价变异
  （`DropRandom.Next` 在 bound ≤ 1 时不推进状态），改成 `Next(1000)` 后取红。
  **一个存活的变异被查成"我的变异写错了"而不是"实现有洞"，两者外观相同**，
  它没有就坡下驴。
- **它纠正了一处对执行者有利的误述**：新判据实际覆盖了守卫**全部四个子句各一次**，
  比执行者自述的"只取难度 9 一条"更全。评审通常只往不利方向纠，这次是往有利方向纠。

**TASK-12 置 `done`。** 至此 revision 4 的三个改造任务全部完成。

### 观察一、二转 TASK-09

控制器独立验算了观察一：`scaledParty <= 0` 需组队倍率非正（A21 为 1/1.2/1.4/1.6），
`losing >= basis` 需 shortfall×basis/scaledParty ≥ 10000（A21 上 shortfall 上限 8000）
——**两支在 A21 上都到不了**。所以 `ClearRewardRuleService.cs:268` 那句
"Every route here is a table that did not answer" **是因为数据才成立，不是结构上成立**。
这正是读数审计该收的形状：一句凭今日数据为真的话，写得像凭结构为真。

观察二（`:267` 写 "all four of its callers"，实际只有一个调用点、四个进入条件）一并转。

### 观察三不办

`rates.Load` 挪到守卫之前，因缓存而无行为与成本差异；`StubRates` 不计次故无判据钉。记录备查。
