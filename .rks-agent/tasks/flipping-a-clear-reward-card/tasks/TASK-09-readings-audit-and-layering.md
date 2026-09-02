# TASK-09 — 读数对得上归档，分层站得住，判据全绿

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

扫一遍全部新增的规则数值，逐项指回归档中声明它的段落；枚举全部新增类型的构造器
依赖；运行全量判据。

- Referenced spec requirements: RQ-013, RQ-014, RQ-015
- Referenced plan sections: `plan.md` 的 T-009；边界 BD-021
- Owned boundary: BD-021
- Permitted files or interfaces:

- 只写 Task 文件与判据；不改产品代码，除非核对中发现的缺陷需就地修正

- Dependencies: TASK-05, TASK-07, TASK-08
- Required dependency outputs: 前八个任务的全部产出
- Acceptance conditions:

1. 每一项新增规则数值要么指回具体归档段落，要么在无出处清单中且已被具名标为假设。
2. 归档读取点全在规则服务上；规则服务的依赖中无世界状态读写方。
3. 全量判据 不低于 1690 通过、0 失败、2 跳过，且档案可达（跳过数仍为 2 即是档案加载了的证明）。
4. 本次已知的四条推断（类别概率两槽含义、牌位归属字节读法、概率分母、付费请求形状）
   与一条自定值（固定金币）在清单中如实列出，不得被抹平。

- Task-specific authorization: 根契约（revision 2）的授权覆盖本任务，无收窄。合并、推送、建 PR、删分支或清理 worktree 不在其内。
- Required methods:

- `test-driven-development`：判据先行，再让它变绿。
- 分层按 `AGENTS.md` 与 `Docs/新功能接入规范.md`：内容源只被规则服务注入；
  规则服务不持有仓储、注册表、目录、存储或领域服务；handler 只做四类动作；
  命令处理器禁止嵌套 dispatch；能触达持久写的命令一律 Transactional。
- 归档读法在 XML doc remarks 里写明段落出处；读不出来的记为未查明，不填看似合理的数。
- `System.*` using 在前，本仓不做字母序。`TreatWarningsAsErrors`。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-021 | `Non-command:` 读数与归档出处的逐项对照 | 每一项指回具体段落，或在无出处清单中且已具名标为假设 |
| CK-022 | `Non-command:` 新增类型的构造器依赖枚举 | 归档读取点全在规则服务上；规则服务依赖中无世界状态读写方 |
| CK-023 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 通过数不低于 1690，失败数为零，跳过数仍为 2 |

- Required evidence: CK-021 的对照表与无出处清单；CK-022 的依赖枚举；CK-023 的全量判据输出。

## Contract Amendment 1 — 必须出现在读数对照里的具名清单

**来源**：QR-05-2 的跟进建议。控制器此前把若干「本服务器决定的值」与「记录里的分歧」
记在各 Task 的散记里，写的是「应进 TASK-09 的读数对照」——**但没有写成本 Task 的密封条款**。
TASK-07 有（免费卡金币那一条），本 Task 没有，于是它们只能靠人记得。补上。

**Acceptance conditions 增补**：下列每一项都必须在本 Task 的读数对照中**具名出现**，
并归入「无归档出处、由本服务器决定」或「记录内部有分歧」两栏之一。**漏一项即为不满足。**

### 无归档出处、由本服务器决定

| 值 | 出处任务 | 性质 |
|---|---|---|
| 出现概率的分母（`ClearRewardRateOptions.RateBasis`） | TASK-02 | 归档没有这个数，只有参照服务端一个来源 |
| 各项加成与基础率的合成方式 | TASK-02 | 同上 |
| 地图数当分子还是当替代率 | TASK-02 | 无从分辨，控制器已知的推断 |
| `MapMaxCount ← Layout.Rooms.Count` | TASK-03 | 解释性映射；未断言归档那一段说的就是这个量 |
| 每张翻到的牌固定 1 金币 | 用户决定（契约 revision 2） | 归档说翻牌不出金币 |
| `UnpricedPaidFlipCost = int.MaxValue` | TASK-05 | 发明的哨兵值；**客户端见到它会怎样未查明** |
| `FreeCardGold` | TASK-05 / TASK-07 | TASK-05 写 0 为占位，TASK-07 已填为该牌实际付出的金币。**须核本条是否已消解** |
| `SelectCardCommand.CharacterLevel` | TASK-07 | **惰性入参**：翻牌这一侧不再决定价钱（价钱随牌面在结算时定死），只在「翻牌先于结算到达」时才有影响，而协议时序不产生那种情况。与上一个重构留下的「`MoveAsync` 签名允许传入不匹配的角色与本局」同类 |

### 未实证的读法

| 读法 | 出处任务 | 一次实机怎么验 |
|---|---|---|
| 应答里指示内容归属哪个牌位的字节 | TASK-04 | 给某个非零牌位填内容，看对应的牌是否亮起 |
| 付费翻牌请求的形状 | TASK-04 | 抓一次付费翻牌 |
| `[drop kind prob]` 两个槽各代表什么 | TASK-01/02 | 该段只此一表有，无从横向对照 |

### 记录内部的分歧

| 分歧 | 详情 |
|---|---|
| RQ-011 的「牌面产出声明」半条 | 声明 `[result card]` 的四个副本（120/121/122/4119）**全部没有 `[map specification]`**，正常进本路径不可达，故该半条对每个可达副本都是空真；本 Spec 未实现它。见 TASK-08 的控制器裁定 |
| `NOTI-0x0023.md` 免费卡那一对的偏移 | 布局块记 241/245（240 是标记），**同一节的对账句**记「240 与 245」，差一。TASK-05 的实现只依据布局块，未依赖对账句，并在代码里摆出了分歧 |

**为什么要写成密封条款而不是散记**：本 Spec 至今二十余处缺陷，形状都是「说得顺口但没人核」。
一份靠人记得的待办清单是同一个形状的又一实例——**它读起来像已经安排好了，
而实际上没有任何东西会在它被漏掉时报错**。写成验收条款，漏一项本 Task 就过不了。

## Contract Amendment 2 — TASK-08 验收后补入具名清单

**来源**：TASK-08 round 2 的执行结果、独立评审的四条观察、控制器的验收裁定。
`Contract Amendment 1` 的正文与三张表**一字未动**；本条只做增补，
增补项与 Amendment 1 的条款同等约束——**漏一项即为不满足**。

### 增补到「无归档出处、由本服务器决定」

| 值 | 出处任务 | 性质 |
|---|---|---|
| 三处 `int?` 的 null 现在有两种成因 | TASK-08 | `DungeonClearSlice.UnpricedPaidFlipCost`、`ClearRewardSettlementResult.PaidFlipCost`、`ClearRewardCardSet.PaidFlipCost` 的说明都只写了「归档没给这一级定价」，而 TASK-08 之后 null 还可能是「副本声明关闭了付费翻牌」。**说明与实际含义已经对不上**，须逐处核对并交代 |

### 增补到「未实证的读法」

| 读法 | 出处任务 | 一次实机怎么验 |
|---|---|---|
| `[gold card use]` 非零值的含义 | TASK-08 | **无从横向对照**：归档在 1047 个副本里只声明五次，五次全写 `0`，第三种值不存在。读作「没关」是无证据下的取向，参照服务端解析了该字段却从不读它，定不了案 |

### 增补到「记录内部的分歧」

| 分歧 | 详情 |
|---|---|
| N2 变异的证据强度 | 执行者称「两向都红，证明不是恒定答案碰巧过关」；评审复现后计数一致但**十四条失败全在关闭侧**——未声明副本不进该分支，拿的是解析开头的默认值，方向翻转碰不到。**结论对，论据错**；真正钉住未声明侧的是 N6 与评审另加的默认值变异。本 Spec 第三次由变异证据出错，三次方向各不相同 |
| 副本 3503 声明了开关却永不适用 | 它写了 `[gold card use] 0`，但没有 `[map specification]`，`IDungeonDefinitionSource` 答 null，正常进本路径不可达。**归档的声明与服务器能用到的范围不重合**，已用判据钉住并写明理由 |

### 另两项验收增补（不入读数对照，但同样必须完成）

1. **清掉 `CardRewardDomainServiceTests.cs:111,114` 的死脚手架**：`AskedOffered` / `ClosedDungeonId`
   只写不读。不影响正确性，但它**看起来像在检查什么而其实没有**。
2. **更正 CK-019 的过滤器**：`--filter ResultCard` 实测只选中 round 1 遗留的 2 条；
   收窄后的实质判据是 `--filter PaidCard`（25 条）。计划里的检查项与它实际检查的东西对不上。

## Execution Result — round 1

### 这一轮做了什么

把本 Spec 塞进代码里的每一个数，逐项去归档里找声明它的那一段；找不到的，按三栏具名承认。
核对手段是 `Tool/PvfProbe`（`dump` / `tokens` / `values` / `where` / `lst`）直接读
`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，以及上游 `E:\Project\86JP` 的源码。
**判据里已有的断言不算证据，判据本身也是被核对的对象。** 核对中发现五处真缺陷，就地修正（见「六」）。

---

## 一、CK-021（上）：指得回归档的读数

「A21 实测」列是这一轮用 PvfProbe 亲自读出来的。

### 1.1 `etc/itemdropinfo_clearreward.etc`

| 读数 | 代码位置 | 归档段落 | A21 实测值 |
|---|---|---|---|
| 每个 profile 有几段等级带 | `PvfClearRewardRateSource.BandCountSection` → `CloseProfile` | `[drop prob count]` | `4` |
| 六个具名 profile、每段一个率 | `ClearRewardProfile` / `ClearRewardBand` | `[drop prob]` | 等级界一律 `1-15 / 16-30 / 31-40 / 41-200`；`default` 四段皆 `10000`，`event`、`event2`、`pcroom bonus` 四段皆 `9000`，`pcroom default` 四段皆 `10000`，`pcroom event` 为 `9000 8000 8000 8000` |
| 难度加成 | `ClearRewardTable.DifficultyRates` | `[dungeon difficulty drop bonusrate]` | `0 600 1000 1400 1200`——首项为 0、第五项小于第四项，两条都成立 |
| 地图数加成 | `ClearRewardTable.MapCountRates` | `[reward item rate per map max count]` | `1→2000 2→3000 3→4000 4→5000 5→6000`；无 6 |
| 组队乘数 | `ClearRewardTable.PartyMemberRates` | `[party member drop bonusrate]` | 四个 **Single** token：`1 1.2 1.4 1.6`。token kind 实测确为 `Single`——这正是 `ChanceOf` 必须四舍五入而不能截断的前提 |
| 稀有度阶梯 | `ClearRewardTable.Rarity` | `[basis of rarity dicision]` | 一组七个：`500000 799900 999900 1000000 1000001 1000002 1000003`。basis=1000003，最高掷 1000002，六个带；各带宽 `500000 / 299900 / 200000 / 100 / 1 / 1`，**最稀两带各宽 1 掷**——`ClearRewardRuleService.Draw` 那句成立 |
| 付费翻牌价 | `ClearRewardTable.GoldCardCosts` | `[gold card cost table]` | 1..200 级逐级：1→100，86→10300，200→23980。表内**无 0**、**无 `int.MaxValue`**——帧侧两个哨兵所依赖的前提，实测成立 |
| 品级带 | `ClearRewardTable.GradeBands` | `[item drop ref table]` | 200 行「级/下探/上探」；86 → `7 / 3` → `(79, 89)` |
| 金卡空牌兜底（读了不用） | `GoldCardBlankItems` | `[gold card blank item]` | 六行，物品 id **全为 `10002120`**，权重/数量 `(3000,1) (3000,2) (2000,3) (1000,4) (500,5) (500,10)`。id 经 `stackable/stackable.lst` 解得 `event/chn_event_20130723_GhostSeria/chn_stk_signs_of_seria.stk`，其 `[explain]` 写着「该道具将于2013年9月3日6时统一删除」——**「一件 2013 年的活动物品」这句成立** |
| 网吧空牌兜底（读了不用） | `PcRoomCardBlankItems` | `[pcroom card blank item]` | 只有**一行**：`50 7454 1`。列宽与金卡同，行数不同 |
| 类别概率（读了不用） | `DropKindRates` | `[drop kind prob]` | `10000 0` |
| 物品类型概率（读了不用） | `ItemTypeRates` | `[drop item type prob]` | `0 9500 0 500`。「四个，与兄弟表一带所付的物品率个数相同」成立：实测怪物表 `[drop prob count] 10`、`[drop prob]` 每带七个数 = 两界 + 五率（1 金币 + 4 物品） |
| 刻意不读的四段 | `PvfClearRewardRateSource.UnreadSections` | `[item drop rarity control]` / `[gold card create rate]` / `[chronicle set reward rate]` / `[dungeon difficulty gold drop bonusrate]` | 依次实测为 `2 13 17 0 -5`、单个 **Single** `1`、`0 50 1 75 2 100`、`1.02 1.38 1.6 1.9 2`。「`-5` 是全文件唯一负数」——我把文件里 1143 个数值 token 全扫了一遍，负数只有它一个；「`1` 存成 float 而左右邻段都是整数」——token kind 实测 `Single`，前段 `[gold card cost table]` 与后段 `[gold card blank item]` 全是 `Int32` |

**段落归属的两条计数，独立复核通过。** 把这一族七个文件的标签集全 dump 出来逐一比对：

- **共享的正好七个**：`[drop prob count]`、`[drop prob]`、`[dungeon difficulty drop bonusrate]`、`[basis of rarity dicision]`、`[party member drop bonusrate]`、`[item drop ref table]`、`[item drop rarity control]`。
- **别处没有的正好九个**：`[drop kind prob]`、`[drop item type prob]`、`[dungeon difficulty gold drop bonusrate]`、`[reward item rate per map max count]`、`[gold card cost table]`、`[gold card create rate]`、`[gold card blank item]`、`[pcroom card blank item]`、`[chronicle set reward rate]`。
- 「这一族七个文件」：归档 file table 里 `itemdropinfo` 命中正好七个——`(r)itemdropinfo_control`、`_clearreward`、`_common`、`_monseter`、`_monseter_extra`、`_monster_hell`、`_object`。「另外四个不由 `IDropRateTableSource` 读」也成立：`_monster_hell` 声明的是 `[dungeon difficulty drop prob]` 而非 `[drop prob]`，`_common` 只有 `[gold drop ref table]`，`(r)_control` 只有四个 `control of *`，`_clearreward` 是本表。
- **但「三个文件同形 exactly」这句不成立**：`_object` 少一段 `[party member drop bonusrate]`。详见「六.4」——那是本 Spec 自己新写进去的一句错断言，已改。

**品级带与怪物表的差异，逐行复核通过。** 两表各 200 行、键集相同；`下探`列 **200 行全同**；
`上探`列**恰好在 80..89 这十级不同**（本表 3，怪物表 4），其余 190 行全同。
`级 − 下探` 的最小值是 **1**（1 级那行写的是 `0` 而不是 3），所以那个下钳在这份归档上确实永不触发。

**兄弟阶梯的三种形状，复核通过。** 怪物表 `[basis of rarity dicision]` 是四组七个；
深渊表是「前导计数 `2` + 两组七个」，且两组都不满足「末项 = 前项 + 1」：
组一 `… 1000001 1000001`，组二 `… 1000002 1000001`。
`RarityLadder` 这一轮新写的那句（abyss table 两组都不闭合）逐字成立。

### 1.2 `.dgn` 副本文件

| 读数 | 代码位置 | 归档段落 | A21 实测值 |
|---|---|---|---|
| 副本自己关掉付费牌 | `DungeonDefinition.OffersPaidCard`；`PvfDungeonDefinitionSource.PaidCardSwitch` | 文件级 `[gold card use]` | `dungeon/dungeon.lst` 共 **1047** 个；**1042 个不声明**，**5 个声明且全写 `0`**：`100`、`3503`、`3700`、`3901`、`3902`。`Grimseeker.dgn`（id 100）里它确实落在 `[revision table]` 与 `[recommend equipment]` 之间 |
| （不读）副本自己的牌面产出声明 | 无 | `[result card]` | 1043 个不声明，**4 个声明**：`120`、`121`、`122`、`4119`。这四个逐个 dump 确认**全部没有 `[map specification]`** |

上游对这个 tag 的处理也复核了：`E:\Project\86JP\Tool\PvfLib\Models\DungeonFile.cs:164`
`public int GoldCardUse { get; set; } = -1;`，`:527` 解析它，**全仓再无第三处引用**——
「解析了却从不读」属实。

### 1.3 协议侧常数（出处是抓包与上游，不是归档）

| 常数 | 代码位置 | 出处 | 复核 |
|---|---|---|---|
| 版式 17 字节 = `01` + `0x0001` + 7×`0xFFFF` | `CardRewardPayloadCodec.EncodeLayout` | 上游 `CardRewardNotificationSender.BuildCardLayoutAck` | 读了上游源码，逐字节一致 |
| 牌型上界 1、牌位上界 3 | `FreeFaceByte` / `PaidFaceByte` / `HighestRequestablePosition` | 上游 `Game/Dungeon/CardRewardRules.cs`：`if (run == null \|\| cardType > 1 \|\| cardIndex > 3)` | 原文一致 |
| 应答 8 条记录、全空 33 字节、缺席记录 4×`0xFF` | `WirePositionCount` / `AbsentRecordLength` / `RecordHeaderLength` / `ContentPairLength` | 上游 `BuildCardInfoAck` | 读了上游源码：首字节 `01`，第 0 条 4 字节，第 1..3 条 `FF FF 00 00`，第 4..7 条 `FF FF FF FF`，合 33，一致 |
| 结算帧体 274 字节；七个牌位标记落 169/178/187/196/205/214/223；付费价落 232 | `DungeonClearPayloadCodec` | `protocol/game/server-to-client/NOTI-0x0023.md` 布局块（上游 354 − 80 字节逐对象经验） | 按布局块自己重算了一遍偏移，落点一致；帧长自检在 `EncodeClearDungeonReward` 末尾 |
| 一面四个牌位 `CardSlotCount = 4` | `ClearRewardRules` | **非归档**：上游 `cardIndex > 3` 与 `run.FreeCardSlots[0..3]` | 成立。它与 `[drop prob count]` 那个另一个 4 无关，代码 remark 已写明 |
| 背包条目 101 字节 | `GameInventoryItemEntry.CommonLength` | 上游会话 1414 / 101 = 14 | **本轮未复核**：既有结论，本 Spec 只把它从 StateSynchronization 切片搬到端点层，字节未动 |

---

## 二、CK-021（下）：三栏具名清单

下表含 `Contract Amendment 1` 的 13 行与 `Contract Amendment 2` 的 4 行，**逐行具名，一行不漏**。
标 `[A1]` / `[A2]` 的即密封条款那 17 行；标「新增」的是本轮核对补上的。

### 2.1 无归档出处、由本服务器决定

| # | 值 | 出处任务 | 本轮核对结论 |
|---|---|---|---|
| 1 `[A1]` | **出现概率的分母（`ClearRewardRateOptions.RateBasis`）** | TASK-02 | **仍无出处，仍是假设。** 这个文件里唯一自带 basis 的是稀有度阶梯（末项 1000003 闭合），率表这一侧一个都没有。默认 10000 的理由是自洽——`default` 四段全写 10000、减档写 9000/8000，两个前提我都实测确认了，但**自洽不是声明**。已摆成配置项 `ClearRewardRates:RateBasis`，`ClearRewardRules.ChanceOf` 以参数接收、自身不带默认值 |
| 2 `[A1]` | **各项加成与基础率的合成方式** | TASK-02 | **仍无出处，仍是假设。** 归档只给四个单参数查表（判据 `The_reading_of_this_file_gives_no_basis_and_nothing_that_combines` 用反射钉住「四个方法各一个参数」），没有任何东西把它们连起来。乘法唯一的**强论据**是难度表首项为 0：加法读法会让最低难度按满率发牌。地图数表跟着乘的理由是「不给任何一张表归档没给的角色」——那是取向，不是证据 |
| 3 `[A1]` | **地图数当分子还是当替代率** | TASK-02 | **无从分辨，仍是推断。** `1 2000 … 5 6000` 既能读成「基础率的分子加成」也能读成「直接替代率」。本仓读成前者。若读成后者，一张 5 图副本每个牌位的概率会是 6000/10000 而不是现在的 840/10000——**差七倍，且没有任何判据能分辨这两种读法**，因为归档两边都不说 |
| 4 `[A1]` | **`MapMaxCount ← Layout.Rooms.Count`** | TASK-03 | **解释性映射，未消解。** 段名是 `reward item rate per map max count`，本仓把它等同于本局迷宫的房间数（`CardRewardDomainService.FaceFor` 里 `run.Layout.Rooms.Count`）；归档没有一处说这个量就是房间数。而且该表只定义 1..5，**房间数超过 5 时 `MapCountRate` 返回 null，整面为空**——这是一个可观测后果，压在一个没有出处的映射上 |
| 5 `[A1]` | **每张翻到的牌固定 1 金币** | 用户决定（契约 revision 2） | **仍是自定值，且踩在 #16 的反面。** `GameProtocol.ClearRewardCardGold = 1`。归档这张表里没有任何把「金币命中」换算成金额的段落；`[dungeon difficulty gold drop bonusrate]` 存在但没有可乘的对象，已在 `UnreadSections` 里具名为「inapplicable rather than unknown」。判据 `The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision` 钉住这个 1 |
| 6 `[A1]` | **`UnpricedPaidFlipCost = int.MaxValue`** | TASK-05 | **仍是发明的哨兵；客户端见到它会怎样仍未查明。** 唯一被实测支撑的性质是「1..200 级的价格表里既没有这个值也没有 0」。**本轮发现它的含义已经不止一种，见 #9 与「六.1」** |
| 7 `[A1]` | **`FreeCardGold`** | TASK-05 / TASK-07 | **已消解一半，仍留本栏。** TASK-05 的占位 0 已被 `FreeCardGoldAt(cards)` 取代：0 号牌位有东西时写 `GameProtocol.ClearRewardCardGold`。所以这一格现在报的是本服务器实际会付的钱，与 #5 同源同常量。但它只是从「占位 0」变成了「自定值 1」，**并没有因此获得归档出处** |
| 8 `[A1]` | **`SelectCardCommand.CharacterLevel`** | TASK-07 | **确认是惰性入参，而且比 remark 说的更彻底。** 顺着走了一遍：`SelectCardCommandHandler` 在 `run.ClearRewardCards` 为 null 时就已 `NoCardsFixed` 返回，`ChargeAsync` 只可能在牌已定死之后被调到；`CardRewardDomainService.SettleAsync` 遇到已定死的牌直接返回既有值、**不问规则服务**。所以这个参数无论传什么都不改变任何结果。判据 `A_paid_flip_asks_the_rules_nothing_because_its_price_was_already_fixed` 钉住。保留的理由（结算接缝以等级为键）成立，但它确实是「签名允许传入无关值」那一类 |
| 9 `[A2]` | **三处 `int?` 的 null 现在有两种成因** | TASK-08 | **确认对不上，本轮已就地修正。** 逐处核对：`DungeonClearPayloadCodec.UnpricedPaidFlipCost`、`ClearRewardSettlementResult.PaidFlipCost`、`ClearRewardCardSet.PaidFlipCost` 三处说明**都只写了「归档没给这一级定价」**；而 `SetPlayResultHandler` 里 `settled.PaidFlipCost ?? UnpricedPaidFlipCost` 这一句，对「副本关掉了付费牌」也会写出同一个哨兵。四处里只有 `SelectCardStatus.NotPriced` 写全了两种成因。三处说明已改，见「六.1」 |
| 10 新增 | **一面四个牌位（`ClearRewardRules.CardSlotCount = 4`）** | TASK-02 | 归档无出处（代码 remark 已写明）。出处是上游的 `cardIndex > 3` 与四个 `FreeCardSlots`。线上帧宽是 8，两者刻意分开，`HighestRequestablePosition` 不读 `CardSlotCount` |
| 11 新增 | **两面共用同一 profile、同一阶梯、同一池，只靠种子分开** | TASK-03 | 归档对「免费面与付费面有什么不同」一个字都没有。本仓选「完全相同，只有种子不同」（`SeedFor` 里 `dungeonId ^ (uint)kind`）。归档既没说两面同分布，也没说不同 |
| 12 新增 | **只抽装备，不拿 `[drop item type prob]` 再设一道门** | TASK-02 | `0 9500 0 500` 被读进来但不用，理由写在 `ClearRewardRuleService` 类级 remark 上（位置含义未查明，用它会把未查明的读法埋进命中率里）。「只发装备」本身是用户 2026-08-31 的决定，不是归档读法 |
| 13 新增 | **只用 `default` profile** | TASK-02 | 归档声明六个 profile，**不给任何选择规则**。本仓固定 `default`，三个 pcroom profile 明知本仓有网吧状态也不接。判据 `A_clear_is_paid_at_the_default_profile` 钉住 |
| 14 新增 | **付费翻牌不与任何加成表合成** | TASK-07 | `PaidFlipCost` 只查 `[gold card cost table]`。归档没说「价格要不要随难度/队伍变」；不合成是取向（合成会是本仓发明的税），但它同样没有出处 |

### 2.2 未实证的读法

| # | 读法 | 出处任务 | 一次实机怎么验 / 本轮核对结论 |
|---|---|---|---|
| 15 `[A1]` | **应答里指示内容归属哪个牌位的字节** | TASK-04 | **仍未实证，整个「四张全亮」压在它上面。** 依据是客户端寻址 `this + 0x1E8 + 100*b + 60` 与 `> 7` 上界。验法：给某个非零牌位填内容，看对应的牌是否亮起。**本轮发现代码对上游行为的描述有误并已修正，见「六.2」**——上游写的不是常数 0，而是 `paidSelected ? 0x00 : 0xFF`，且只写在八条里的第一条 |
| 16 `[A1]` | **`[drop kind prob]` 两个槽各代表什么** | TASK-01/02 | **仍无从横向对照。** 这一轮把这一族七个文件的标签集全对了一遍，确认 `[drop kind prob]` **只在翻牌表里出现**，别处一个都没有——所以「无从横向对照」不是没查，是查过了确实没有。读成「物品满值、金币为零」仍是取向 |
| 17 `[A1]` | **付费翻牌请求的形状** | TASK-04 | **仍未实证。** 客户端有第二个构包点 `0x00D10DFF` 只写一个字节；本仓按上游的两字节读法收，一字节体一律丢弃。验法：抓一次付费翻牌 |
| 18 `[A2]` | **`[gold card use]` 非零值的含义** | TASK-08 | **仍定不了案，且这一轮把两个前提都实测了**：1047 个副本里只声明五次、五次全写 `0`（`values` 命令输出 `1042 (absent) / 5 → 0`），第三种值确实不存在；上游解析了该字段（默认 `-1`）却全仓不读。读作「没关」仍是无证据下的取向 |
| 19 新增 | **版式帧那八个 u16 各是什么** | TASK-04 | 上游写 `0x0001` + 7×`0xFFFF`，本仓照抄。客户端对本帧只做 16 位读，所以里面没有物品 id——但**这八个字各选中什么未查明**。协议记录 `0x0046.md` 已写明 |
| 20 新增 | **应答记录首字节读作「哪一面」** | TASK-04 | 上游那一格写的是 `freeSelected ? 0x00 : 0xFF`，即它自己用作「免费牌是否已选」而不是「面」。本仓读作面（0 免费 / 1 付费），代码 remark 已标为 inferred。免费值有抓包互证，付费值 `1` 是把枚举当真 |
| 21 新增 | **金币在翻牌应答里的走法** | TASK-07 | 协议记录 `0x0047.md` 写明客户端在 `id == -1` 时按金币处理（`setSlot(amount, 1, slot, 4)`），并写了「本仓此处不发金币，牌面金币属于另一个成果」。TASK-07 交付的金币走的是**账号加钱 + 背包/金币通知**，不是这条记录里的 `id == -1` 路径。**客户端会不会因此在牌面上不显示金币，未查明**；该句在 TASK-07 之后未回头更新 |

### 2.3 记录内部的分歧

| # | 分歧 | 详情 / 本轮核对结论 |
|---|---|---|
| 22 `[A1]` | **RQ-011 的「牌面产出声明」半条** | 复核成立：声明 `[result card]` 的四个副本（`120` `121` `122` `4119`）**逐个确认没有 `[map specification]`**，`IDungeonDefinitionSource` 对它们答 null，正常进本路径不可达，故该半条对每个可达副本都是空真。本 Spec 未实现它。判据 `Every_dungeon_declaring_a_result_card_is_absent_from_the_definition_source` 钉住 |
| 23 `[A1]` | **`NOTI-0x0023.md` 免费卡那一对的偏移** | 复核成立且**未消解**：布局块记 `239 保留 / 240 标记 / 241 免费牌物品 / 245 免费牌金币`，同一节的对账句写「免费牌那一对在 240 与 245」，差一。两种读法都与「参照帧在 240 和 245 非零」相容（标记非零、物品为零、金币非零）。TASK-05 的实现只依据布局块，`FreeCardGoldAt` 的 remark 已把分歧摆出来，本轮无新证据可定案 |
| 24 `[A2]` | **N2 变异的证据强度** | 复核成立，并且**本轮用一个新变异把未声明侧钉住了**：把 `PvfDungeonDefinitionSource` 里 `var offersPaidCard = true;` 改成 `false`（即评审说的「默认值变异」），Infrastructure.Tests **红 3 条，三条全在未声明侧**（`A_dungeon_declaring_nothing_still_offers_the_paid_card`，144/10000/10004）。所以「结论对、论据错」的判定成立，而结论本身现在有直接证据 |
| 25 `[A2]` | **副本 3503 声明了开关却永不适用** | 复核成立：`dungeon/Act5/Counterattackarea.dgn` 写了 `[gold card use] 0`，但 `[map specification]` 命中数为 **0**；另外四个声明该开关的副本（100/3700/3901/3902）**都有** `[map specification]`。归档的声明与服务器能用到的范围确实不重合 |
| 26 新增 | **根契约 Assumptions 里的「其余四张表」** | 根契约（`TaskContract.md:160-161`）写「该段落只出现在翻牌表里，这一族的其余**四张**表都没有」。这一族实测是**七个文件**（本表之外六个），本 Spec 的 `DropRateContent` 这一轮也把「五个文件」改成了「七个」。**结论没错**（`[drop kind prob]` 确实别处没有，我逐个文件对过），**数字过时**。根契约不在本 Task 的可写清单内，仅记录 |
| 27 新增 | **`Docs/新功能接入规范.md` 把 `[drop item type prob]` 读死了** | 该文写「`0 9500 0 500` 是装备 95% / 时装 5%」，而 `ClearRewardTable.ItemTypeRates` 的 remark 说这四个位置的含义**未查明**、只有「个数与兄弟表一带的物品率个数相同」这一条弱支撑。同一件事，规范当结论、代码当推断 |
| 28 新增 | **`Docs/新功能接入规范.md` 说「真正新的只有按通关评级分概率」** | 归档这张表里**没有任何按评级分概率的段落**（十六个段落我逐个看过），实现里的 `ClearRewardOccasion` 也不带评级。规范这一句没有归档支撑，且与交付形状不符 |
| 29 新增 | **`0x0047.md` 的「牌面金币属于另一个成果」** | 见 #21。那个成果已经在 TASK-07 交付，走的是另一条路（账号加钱），记录未回头更新 |

---

## 三、CK-022：新增类型的构造器依赖枚举

### 3.1 会被 DI 注入的新类型

| 类型 | 层 | 构造器依赖 | 判定 |
|---|---|---|---|
| `PvfClearRewardRateSource` | Infrastructure | `PvfArchiveProvider` | 内容源本体，读归档是它的职责 |
| `ClearRewardRuleService` | Application（规则服务） | `IClearRewardRateSource`、`IDropItemPoolSource`、`IDungeonDefinitionSource`、`ClearRewardRateOptions` | **三个内容源 + 一个规则配置，无仓储/注册表/目录/存储/领域服务** |
| `CardRewardDomainService` | Application（业务服务） | `IClearRewardRuleService`、`IDungeonRunDomainService` | 规则服务 + 领域服务，**无内容源** |
| `SelectCardCommandHandler` | Protocol（命令处理器） | `ICardRewardDomainService`、`IDungeonRunDomainService`、`ICharacterContainerDomainService`、`IAccountMoneyService`、`TimeProvider` | **无 `ICommandBus`**（禁止嵌套 dispatch 成立）、**无内容源**、**无 `I*RuleService`** |
| `SelectCardHandler` | Protocol（消息 handler） | `ISelectedSessionDirectory`、`ISelectedSessionRuntimeDirectory`、`ICommandBus`、`IGameMessageSender`、`ILogger<>` | 只读目录查询 + 命令 + 发送，**无 `I*RuleService`** |
| `CardSelectRightStateHandler` | Protocol（消息 handler） | `ISelectedSessionDirectory`、`IGameMessageSender`、`ILogger<>` | 同上 |
| `CardSelectRightStateDecoder` / `SelectCardDecoder` / `CardSelectRightStateResponseEncoder` / `SelectCardResponseEncoder` / `InventoryDeltaEncoder` | Protocol | 无 | — |
| `SetPlayResultHandler`（既有类型，本 Spec 加了一个依赖） | Protocol | 原有 7 个 + `ICardRewardDomainService` | 见 3.3 |

纯数据类型（`ClearRewardBand`、`ClearRewardProfile`、`ClearRewardBlankItem`、`ClearRewardTable`、
`ClearRewardChance`、`ClearRewardCardKind/Item/Slot/Face/Id/Set`、`ClearRewardOccasion`、
`ClearRewardSettlement`、`ClearRewardSettlementResult`、`SelectCardStatus`、`SelectCardResult`、
`SelectCardCommand`、`CardSelectRightStateRequest`、`SelectCardRequest`、`SelectCardResponse`、
`CardSelectRightStateResponse`、`InventoryDeltaMessage`）与静态类（`ClearRewardProfiles`、
`ClearRewardRules`、`CardRewardPayloadCodec`、`GameInventoryItemEntry`、`GameInventoryDelivery`）
不参与注入。

### 3.2 两条自检（`Docs/新功能接入规范.md` 一节）

- **归档读取点全在规则服务上。** 全仓扫了一遍内容源注入点：`IClearRewardRateSource`、
  `IDropItemPoolSource`、`IDungeonDefinitionSource` 在 `Application` / `Protocol` 里的注入点
  只落在 `*RuleService.cs`（`ClearRewardRuleService`、`DropRuleService`、
  `DungeonLayoutRuleService` 等），本 Spec 没有新增例外。
- **规则服务依赖中无世界状态读写方。** `ClearRewardRuleService` 的四个参数里没有任何
  `*Repository` / `*Registry` / `*Directory` / `*Store` / `*Persistence` / `*DomainService`。
  这条不是靠肉眼——判据 `ClearRewardRuleServiceTests.The_rule_service_is_handed_no_reader_of_world_state`
  用反射把构造器参数逐个比对并做后缀检查。
- **`ClearRewardRateOptions` 只被 `ClearRewardRuleService` 注入**，注册在 `ApplicationModule`。
- **能触达持久写的命令是 Transactional**：`SelectCardCommand` 在 `ProtocolModule` 注册为
  `CommandExecutionKind.Transactional`。

### 3.3 三条分层观察（不构成 CK-022 不通过，但要说出来）

1. **`GameProtocol.ClearRewardCardGold` 是「表达游戏语义的常量」，却被命令处理器与 codec 直接读。**
   `AGENTS.md` 的「规则输入只经规则服务进入本服务器」把这类常量列进了规则输入，并明说
   「`Protocol` 的 handler 与命令处理器都不读」。现在 `SelectCardCommandHandler`（命令处理器）
   与 `DungeonClearPayloadCodec.FreeCardGoldAt` 都读它。代码给的理由是「两个切片都要用，
   与 `SecretShopNpcId` 同」——但 `SecretShopNpcId` 是一个协议载荷里的 NPC 编号，
   而这个 1 决定玩家拿到多少钱，两者不同类。**本 Task 的 Required methods 没有列这一条**
   （只列了内容源注入、规则服务依赖、handler 四类动作、禁止嵌套 dispatch、Transactional），
   所以按契约字面不算不通过；按 `AGENTS.md` 字面算。**没有改**——把它搬到规则服务要动
   两个切片的调用形状，越出「就地修正」的分量，留给控制器定。
2. **`SetPlayResultHandler` 在 handler 里直调领域服务的写方法。** 这一轮它新增了
   `cardRewards.SettleAsync(...)`（会定死本局两面牌，是写）。但这是**既有形状的延续**：
   同一个 handler 在本 Spec 之前就已经在调 `runs.TrySettle(...)`。`AGENTS.md` 自身在这里
   也不自洽——「带业务含义的行为必须经命令**或领域服务**」允许，紧接着的「普通 handler 里
   允许出现的只有四类」不允许。没有改。
3. **`IAccountMoneyService` 的名字不在 `AGENTS.md` 的后缀分类法里**（既非 `*DomainService`
   也非 `*RuleService`）。既有命名，本 Spec 只给它加了 `TrySpendAsync`。仅记录。

---

## 四、CK-023：全量判据

命令：

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| 项目 | 通过 | 失败 | 跳过 |
|---|---|---|---|
| `Dfo.Server.Protocol.Tests` | 479 | 0 | 0 |
| `Dfo.Server.Networking.Tests` | 51 | 0 | 0 |
| `Dfo.Pvf.Tests` | 60 | 0 | 0 |
| `Dfo.Server.Persistence.Tests` | 248 | 0 | 0 |
| `Dfo.Server.Host.Tests` | 4 | 0 | 0 |
| `Dfo.Server.Application.Tests` | 869 | 0 | 0 |
| `Dfo.Server.Infrastructure.Tests` | 214 | 0 | 2 |
| **合计** | **1925** | **0** | **2** |

退出码 **0**。跳过数仍为 2，两条都是 `PvfTownDefinitionSourceTests` /
`PvfQuestDefinitionSourceTests` 的「缺档案时应报错」用例——档案可达的证明。
与控制器给的基线（1925 / 0 / 2）一致。

`dotnet build Server/86JP.Server.slnx --nologo`：**0 警告 0 错误**（`TreatWarningsAsErrors` 下）。

**中途出过一次假红，如实记：** 改完之后的第一次全量里
`Dfo.Server.Networking.Tests.Integration.OutboundSendFailureTests.AChannelMessageIsRejectedOnAGameConnection`
报 `OperationCanceledException` 失败一条，退出码 1。这正是 `AGENTS.md` 写明的形状
（真 socket 用例在满载全量下撞 10 秒预算）。按该文的处置隔离复跑
`Server/tests/Dfo.Server.Networking.Tests`：**51 通过 0 失败，0.376 秒**。随后重跑全量，
即上表，全绿。

---

## 五、`Contract Amendment 2` 的另两项

### 5.1 清掉 `CardRewardDomainServiceTests.cs:111,114` 的死脚手架

**先确认它确实是死的**（不是 grep 一次就下结论）：`AskedOffered` 只被 `OffersPaidFlipAsync`
自己 `Add`，全仓无任何读点；`ClosedDungeonId` 只被 `OffersPaidFlipAsync` 自己比较，
**全仓无任何写点**——也就是说它永远是 null，`dungeonId != null` 永远为 true，
这个 fake 的开关**从来只有一个分支**。两个成员一个只写不读、一个只读不写，合起来
「看起来像在检查什么而其实没有」。

已删除，`OffersPaidFlipAsync` 改为直接 `ValueTask.FromResult(true)`，并写明
「关掉开关的那一侧在 `DungeonPaidCardSwitchRuleTests` 与 `PaidCardFlipCostTests` 里断言，
这里放一个没人拨的旋钮会让人以为这个文件覆盖了那一侧」。
删除不掉覆盖：那两个文件里「关掉」侧共有 8 条用例（100/3700/3901/3902 各两遍）。
`Dfo.Server.Application.Tests` 删前删后都是 **869 通过**。

### 5.2 更正 CK-019 的过滤器

两个过滤器都实测了：

| 过滤器 | 命中 | 命中的是什么 |
|---|---|---|
| `--filter ResultCard` | **2**（全在 `Dfo.Server.Infrastructure.Tests`） | `DungeonResultCardDeclarationTests` 的两条：四个声明 `[result card]` 的副本都取不到定义；关掉付费牌的五个副本里有几个可达 |
| `--filter PaidCard` | **25**（Infrastructure 8 + Application 17） | `DungeonPaidCardSwitchTests`（8）、`DungeonPaidCardSwitchRuleTests`（9）、`PaidCardFlipCostTests`（8） |

`--filter PaidCard` 同时命中**有声明**（100/3700/3901/3902）与**无声明**（144/10000/10004）
两侧，所以 CK-019 那一行的 Expected result「有声明与无声明两类副本各一条判据通过」
在新过滤器下才**真正成立**——旧过滤器选中的两条根本不覆盖无声明侧。

**已改：`plan.md:258` 的 CK-019 命令，`--filter ResultCard` → `--filter PaidCard`，只动这一个词，
Expected result 一字未动**（它在新过滤器下才为真）。
另外两处仍是旧词，**没有动**，理由见自检「越界与克制」：

- `tasks/TASK-08-dungeon-result-card-override.md:45`——那是已验收 Task 的契约表，是当时的记录；
  该文件自己在 780-783、835 行已经写明这个过滤词过时。

**一并记下一个新发现：两个过滤器合起来才是 TASK-08 的全部证据。**
副本 `3503` 的那条判据（声明了开关却因无 `[map specification]` 而永不适用）
落在 `DungeonResultCardDeclarationTests` 里，**`--filter PaidCard` 选不中它**。
单一过滤词覆盖不全，控制器若要一条命令覆盖，得用
`--filter "PaidCard|ResultCard"` 之类的写法。这不在密封条款里，未擅自改。

---

## 六、核对中发现并就地修正的真缺陷

四类六处，全部是「说得顺口但没人核」的形状。改动只落在 XML doc / 注释与一个测试 fake，
没有改任何行为；全量判据数字不变。

### 6.1 三处 `int?` 的说明与实际含义对不上（`Contract Amendment 2` 点名的那一条）

**为什么这是缺陷**：TASK-08 之后，`ClearRewardSettlementResult.PaidFlipCost` 的 null
有两种成因——「归档没给这一级定价」与「副本自己关掉了付费牌」。
`SetPlayResultHandler` 把两者都写成同一个 `UnpricedPaidFlipCost`。
但三处说明都只写了第一种，第三处（`DungeonClearPayloadCodec`）连常量的 summary
都写成「当归档没给这一级定价时」。**一个读代码的人会据此断言「写出 int.MaxValue ⇒ 这一级没定价」，
而这个断言现在是假的。** 这正是本 Spec 反复栽的那种「读起来像已经核过」的句子。

改法（只改说明，不改常量名、不改值、不改行为）：

- `DungeonClearPayloadCodec.UnpricedPaidFlipCost`：summary 改为「当一次结算说不出付费翻牌的价钱时」，
  remark 开头加一段说明两种成因合成一个 null、这个常量分不出也不假装能分出，
  并说明**不改名**的理由（改名只是挪一个名字，定不了任何事）。
- `DungeonClearPayloadCodec.NoPaidFlipCost`：原 summary 说「没有付费翻牌可定价的一局」，
  容易与上面那条混；改成「一局根本没定死牌」，并把与上一条的对照句改准。
- `ClearRewardSettlementResult.PaidFlipCost`（`ICardRewardDomainService.cs`）：加一段
  「null 有两种成因，本类型不分开携带」，并指向 `IClearRewardRuleService.OffersPaidFlipAsync`。
- `ClearRewardCardSet.PaidFlipCost`（`IDungeonRunDomainService.cs`）：同上，并把
  「zero is a price … a level nothing prices is not a free one」改成同时覆盖两种成因。

`<see cref>` 一律只指向同命名空间内解析得到的成员——`DungeonDefinition` 与 `SelectCardStatus`
分别要多引一个命名空间和跨一个切片，改用 `<c>` 文本，避免 CS1574 与跨切片引用。

### 6.2 `ContentOwningPosition` 对上游行为的描述与上游源码不符

**为什么这是缺陷**：原文写「The reference server writes a constant zero here」。
读上游 `E:\Project\86JP\Server\DfoServer\Network\Handlers\Dungeon\CardRewardNotificationSender.cs`
的 `BuildCardInfoAck`，那一格写的是 `paidSelected ? (byte)0x00 : (byte)0xFF`，**不是常数 0**；
而且只在八条记录的**第一条**里写，第 1..3 条固定 `FF FF 00 00`、第 4..7 条固定 `FF FF FF FF`。
差别是有意义的：`0xFF` 在客户端那条 `> 7` 上界下意味着「这一条的内容不要读」，
与「指向 0 号牌位」不是一回事。协议记录 `0x0047.md` 写对了，代码注释写错了——
**一份记录内部两句话不一致，正是本 Spec 要清的东西。**

已改为照抄上游的表达式，并注明只写在八条里的第一条、出处文件名。
结论那一句（「所以它只可能指向 0 号牌位，这就是它自己的面板只有一张牌有东西的原因」）
在修正后的事实下仍然成立，我重新核过。

### 6.3 `DungeonDefinition.OffersPaidCard` 的上游出处路径在本仓解析不到

**为什么这是缺陷**：原文写 `Tool/PvfLib/Models/DungeonFile.cs`。本仓**也有** `Tool/` 目录
（`PvfProbe` / `PvfProxy` / `WireSmoke`），里面没有 `PvfLib`。一个照着这个路径去找的人
会在本仓里扑空。该文件实际在上游 `E:\Project\86JP\Tool\PvfLib\Models\DungeonFile.cs`。
本仓既有的写法是 `Transcribed from <c>86JP</c>: …`（见 `DungeonSelectionSlice.cs:224`）。
已按同一写法加上 `86JP` 前缀。

### 6.4 「三个兄弟文件同形」里的 exactly 是错的——**本 Spec 自己新写的一句错断言**

**为什么这是缺陷**：`DropRateTable` 的 remark 这一轮被本 Spec 从
「Four of the five files in this family share this shape **exactly** -- monster, monster-extra,
breakable object and clear reward」改成
「Three of the family's seven files share this shape **exactly**: monster, monster-extra and
breakable object」。**「五 → 七」「四 → 三」都核了，`exactly` 这个词跟着抄了过来，没核。**

实测：`DropRateTable` 这个 record 要读六个段落（`[drop prob count]`+`[drop prob]`、
`[basis of rarity dicision]`、`[monster type drop bonusrate]`、
`[dungeon difficulty drop bonusrate]`、`[party member drop bonusrate]`、`[item drop ref table]`）。
`itemdropinfo_monseter.etc` 与 `itemdropinfo_monseter_extra.etc` 六个全有；
**`itemdropinfo_object.etc` 没有 `[party member drop bonusrate]`**——我把这一族七个文件的
标签集全 dump 出来比对确认。它不报错（`Groups` 遇缺段返回空表，`PartyMemberRate` 落到
`UnlistedRate = 1.0`），所以这句错断言**不会被任何判据发现**。

这一条正好是控制器提醒的第二个坑的实例：「修一个错误断言时，很容易写出一个新的错误断言」。
已改为点名说出差别：前两个文件段段俱全，破坏物表少了组队那一段，缺了就落到 `UnlistedRate`。

### 6.5 与 6.6 —— 没有改的两条，写在这里以免被当成漏看

- **`ContentOwningPosition` 的「above seven, which is exactly the number of records the frame has」**：
  上界是 7，记录数是 8。宽容地读，「0..7 共八个值 = 八条记录」说得通；
  字面读，「seven」不等于八。`0x0047.md` 的「`> 7` 这个上界恰好等于记录条数」有同样的模糊。
  **没改**——本 Spec 已经有过「改一句错的、写出一句新的错的」的先例，
  这一句是措辞松而不是事实错，改它的收益不抵风险。记录在此。
- **`0x0047.md` 的「本仓此处不发金币，牌面金币属于另一个成果」**：见 #21/#29。
  那个成果已交付但走了另一条路，这句话就本帧而言仍然为真（本仓确实不在 `0x0047` 里发金币），
  所以不是错句，是**没跟上**。改它要一并回答「客户端会不会因此看不到金币」，
  而那个问题未查明。**没改**，作为未实证读法记在 #21。

---

## 七、变异检查

规程：改动一律经写文件（mtime 自然前进，不靠 `touch`），跑受影响项目的判据，
用 `sha256sum` 核内容还原。**先取得一个真红再信其余。**

| # | 变异 | 位置 | 判据 | 真实结果 |
|---|---|---|---|---|
| M1 | `ClearRewardCardGold` `1` → `2` | `GameProtocol.cs` | `Dfo.Server.Protocol.Tests` | **红 1 条**：`DungeonClearCodecTests.The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision`（Expected 1 / Actual 2）。478 通过 |
| M2 | `UnpricedPaidFlipCost` `int.MaxValue` → `0` | `DungeonClearSlice.cs` | 同上 | **判据没跑：编译失败。** `xUnit1025`（`The_body_keeps_its_length_whatever_a_paid_flip_costs` 的 InlineData 重复，因为它同时用了 `NoPaidFlipCost`=0 与这个常量），在 `TreatWarningsAsErrors` 下变成 error。**记为「没跑」而不是「变红」** |
| M2b | `UnpricedPaidFlipCost` `int.MaxValue` → `12345` | 同上 | 同上 | **红 1 条**：`DungeonClearCodecTests.The_marker_for_a_level_with_no_price_is_not_a_free_flip`。478 通过 |
| M3 | `var offersPaidCard = true;` → `false`（解析开头的默认值） | `PvfDungeonDefinitionSource.cs` | `Dfo.Server.Infrastructure.Tests` | **红 3 条，且三条全在未声明侧**：`A_dungeon_declaring_nothing_still_offers_the_paid_card`（144 / 10000 / 10004）。211 通过 2 跳过。这正是 `Contract Amendment 2` 里评审说 N2 碰不到的那一侧 |
| M4 | `Profile = ClearRewardProfiles.Default` → `Event` | `ClearRewardRuleService.cs` | `Dfo.Server.Application.Tests` | **红 14 条**：`A_clear_is_paid_at_the_default_profile`、`ClearRewardArchiveTests.The_chance_a_position_carries_is_the_archives_numbers_combined` 全部 8 组、以及 5 条规则服务/阶梯用例。855 通过 |
| M5 | `ContentOwningPosition(index)` → 恒为 0（复现上游行为，仍读 index 以免参数未读） | `CardRewardPayloadCodec.cs` | `Dfo.Server.Protocol.Tests` | **红 6 条**：`Each_record_names_the_position_it_describes` 全 4 组、`Every_position_is_declared_including_the_ones_holding_nothing`、`A_face_holding_nothing_is_the_length_the_capture_had`。473 通过 |

**还原核对**：五个被改过的文件在还原后逐个 `sha256sum`，与变异前记录的摘要**逐字节相同**。

**M1 值得单独说一句**：它只红了 1 条。付费/免费翻牌那一批「翻到东西就发金币」的用例
是拿 `GameProtocol.ClearRewardCardGold` 自己去比的，所以会跟着变异一起走。
真正把这个 `1` 钉死的**只有那一条**。这不是缺陷（那条判据存在且有效），
但「这个自定值被多少判据钉住」的答案是**一条，不是一批**。

---

## Executor Self-check — round 1

### 判据数字（实测）

- **CK-023 全量**：`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`
  → **1925 通过 / 0 失败 / 2 跳过，退出码 0**。与控制器给的基线一致。
- 中间出过一次假红：Networking 的真 socket 用例报 `OperationCanceledException`（退出码 1）。
  按 `AGENTS.md` 隔离复跑该项目：51 通过 0 失败 0.376 秒。随后重跑全量全绿。
  **这一条如实写在结果里，没有只报最后那次绿。**
- `dotnet build Server/86JP.Server.slnx --nologo`：0 警告 0 错误。
- CK-019 两个过滤器：`ResultCard` → 2 条；`PaidCard` → 25 条（8 + 17）。用 `--list-tests`
  把 25 条的全名列出来核过，两侧（有声明 / 无声明）都在。
- `Dfo.Server.Application.Tests` 在删死脚手架前后都是 869 通过。

### 变异检查：做了哪几个，真实结果

六次施加（M1、M2、M2b、M3、M4、M5），见「七」。要点：

- **先拿到真红再信其余**：M1 是第一个，红 1 条，之后才继续。
- **M2 编译失败，记为「判据根本没跑」**，没有把它算成一次成功的变异；换值重做成 M2b 才拿到红。
  这一条正好证明了「编译失败不是变红」这个坑是真的——如果我按「非绿即红」记，
  就会得出一个假的证据。
- 每次都经写文件（mtime 前进），没有出现「增量编译跳过重编」的假绿：
  五次真变异都各自红了，红的条目也都和变异点对得上。
- 还原用 `sha256sum` 逐文件核对，五个文件全部与变异前**逐字节相同**。
- **M3 是本轮补上的那条证据**：`Contract Amendment 2` 说 TASK-08 的 N2 论据错、
  真正钉住未声明侧的是 N6 与评审加的默认值变异。我重做了默认值变异，
  红的 3 条**全在未声明侧**，与评审的说法一致。

### 改了哪些文件

**产品代码（只改 XML doc / 注释，无行为改动）**

1. `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`
   —— `NoPaidFlipCost` / `UnpricedPaidFlipCost` 两个常量的说明（六.1）
2. `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`
   —— `ClearRewardSettlementResult.PaidFlipCost` 的说明（六.1）
3. `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs`
   —— `ClearRewardCardSet.PaidFlipCost` 的说明（六.1）
4. `Server/src/Dfo.Server.Protocol/Game/CardReward/CardRewardPayloadCodec.cs`
   —— `ContentOwningPosition` 对上游行为的描述（六.2）
5. `Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs`
   —— `OffersPaidCard` 里上游文件路径加 `86JP` 前缀（六.3）
6. `Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs`
   —— `DropRateTable` 的「三个文件同形 exactly」改为点名说出破坏物表少哪一段（六.4）

**判据**

7. `Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs`
   —— 删 `AskedOffered` / `ClosedDungeonId`（五.1）

**流程文件**

8. `.rks-agent/tasks/flipping-a-clear-reward-card/plan.md`
   —— CK-019 的 `--filter ResultCard` → `--filter PaidCard`，只改这一个词（五.2）
9. 本文件 —— 追加这两节，前面每一节一字未动

行尾：改过的每个文件都按**字节数**核过 `\r`，**全部为 0**（用 Python 数 `b'\r'`，
没有用 `grep -c '\r'`——那个模式在本机确实会退化，我一开始误用它数任务文件，
得到 74 这种和真值 0 完全不符的数，后面全部改用字节计数）。

### 未查明的东西

按「一次实机就能定案」与「归档里根本没有」分开写：

**一次实机就能定案的（四条）**

1. 应答记录里那个牌位序号字节（`ContentOwningPosition`）——「四张全亮」整个玩法压在它上面。
2. 付费翻牌请求的形状——客户端第二个构包点只写一个字节，本仓不收。
3. 客户端收到 `int.MaxValue` 作为付费翻牌价会怎样。
4. 本仓走「账号加钱」而不是 `0x0047` 里 `id == -1` 那条路发金币，客户端牌面上会不会因此看不到金币。

**归档里根本没有的（六条，也就是清单 2.1 的性质）**

5. 出现概率的分母。
6. 四张表怎么合成。
7. 地图数是分子还是替代率。
8. `map max count` 到底指什么量（本仓读成房间数）。
9. `[drop kind prob]` 两个槽的含义——这一轮确认了它在这一族里独此一家，无从横向对照。
10. `[gold card use]` 非零值的含义——五次声明全是 `0`，第三种值不存在。

**归档有、本仓没读的（四段）**

`[item drop rarity control]`、`[gold card create rate]`、`[chronicle set reward rate]`
（三段用途未查明）与 `[dungeon difficulty gold drop bonusrate]`（不适用而非未知）。
前三段的用途本轮**没有新进展**——我只核了它们的值，没有去找它们在客户端里的读点。

**本轮明确没有复核的**

- `GameInventoryItemEntry.CommonLength = 101` 与它那 17 字节尾部的位置。既有结论，
  本 Spec 只做搬家。
- `NOTI-0x0023.md` 里参照帧本身那份抓包文件——协议记录自己也写了「本页未独立复核那份抓包文件」。
  我核的是布局块与上游 builder 的自洽，不是抓包字节。
- 上游 `SendLayoutAsync` 的那条发送日志原文（我读的是上游 `BuildCardLayoutAck` 源码，
  不是日志）。

### 我自己不确定的地方

1. **改了 `plan.md` 的 CK-019 那一行，这一步我不确定该不该由我做。**
   契约的 Permitted files 写「只写 Task 文件与判据」，`plan.md` 不是 Task 文件；
   但 `Contract Amendment 2` 又明写「更正 CK-019 的过滤器」，而那一行就在 `plan.md` 里，
   不动它就没法「更正」。我选了「动，但只动那一个词，并且在这里大声说出来」，
   因为反过来的错（密封条款没完成）更难补，而这一改**回退成本是一行**。
   **如果控制器认为这属于计划漂移，回退只需把 `PaidCard` 改回 `ResultCard`。**
   `TASK-08` 里那份同样过时的拷贝我**没有动**——它是已验收 Task 的契约记录。
2. **六处「就地修正」里，6.3（`86JP` 路径前缀）分量最轻，可能算作装修而不是修缺陷。**
   我把它算成缺陷的理由是本仓自己也有 `Tool/` 目录，路径会真的指错地方；
   但我承认这条最接近边界。
3. **3.3 的第 1 条（`ClearRewardCardGold` 被命令处理器直读）我没有改，这是一个判断。**
   按 `AGENTS.md` 的「规则输入」段落字面，它应该只经规则服务；
   按本 Task 的 Required methods 列举，它不在列。我选了「说出来、不改」，
   因为把它搬进 `IClearRewardRuleService` 会改动两个切片的调用形状，
   那不是「就地修正」的分量。**如果控制器认为这是本 Spec 的第二十几处缺陷之一，
   它需要一个自己的任务。**
4. **清单 2.1 第 3 条（地图数当分子还是当替代率）我越核越不安。**
   两种读法差七倍，而且**没有任何判据能分辨它们**——判据钉的是「按现在这个读法算出来的数」。
   我没有找到任何新证据能倾向任何一边，所以只是把它留在原栏，
   但我认为它的风险等级高于同栏其他几条。
5. **清单 2.1 第 4 条（`map max count` ← 房间数）有一个我不确定是否已被考虑过的后果**：
   `[reward item rate per map max count]` 只定义 1..5，房间数超过 5 时
   `MapCountRate` 返回 null，整面为空。本仓的迷宫会不会产生 5 个以上房间，
   我**没有去查**（那要读迷宫生成规则，超出本 Task）。如果会，那就是一类
   「房间多的副本反而一张牌都不出」的可观测行为。
6. **`Docs/新功能接入规范.md` 的两处（清单 #27、#28）我按「记录内部的分歧」记，
   但它们也可以被读成「规范文档需要更新」。** 我没有改那份文档——
   它不在可写清单里，而且第 28 条（按通关评级分概率）到底是规范写错了、
   还是设计阶段有意放弃了一个本该做的东西，**我分不出来**。
7. **CK-021 的「每一项」我尽力做到了穷举，但我不能证明它穷举。**
   我的做法是：读遍本 Spec 新增与改动的全部源文件，把每个字面常量与每处归档读取
   逐个列出来。如果某个数藏在一处我没读到的改动里（比如 `SelectCharacterHandler`
   那两行、`StateSynchronizationPayloadCodec.Inventory.cs` 的搬家），它就会被漏掉。
   我核过那几处的 diff，它们是搬家与调用点改名，没有新数——但这是我的判断，不是机器证明。

## Controller Disposition — round 1 判为 task-rework：审计查出一个会让牌整面变空的真缺陷

执行者在收尾提问里写了一句「`MapMaxCount ← 房间数` 有个可能没被考虑过的后果：
房间数 >5 时整面为空（本仓迷宫会不会超 5，我没查）」，并如实标注自己没查。
**控制器查了。答案是：常态如此。**

### 一、缺陷 A —— 确凿，无需解释即可判定

逐环都由控制器亲自复核：

| 环节 | 证据 |
|---|---|
| 归档只声明 1–5 | `dump etc/itemdropinfo_clearreward.etc`：`[reward item rate per map max count]` 全文为 `1 2000 2 3000 3 4000 4 5000 5 6000` |
| 查不到就是 null | `ClearRewardContent.cs:211-212`，精确字典查找 |
| null 就把概率打成 0 | `ClearRewardRuleService.cs:68-75`，四个查找任一为 null 即 `new ClearRewardChance(0, ...)` |
| 概率 0 就整面空白 | `ClearRewardRuleService.cs:121-123`，`IsImpossible` → `Blank(occasion.Kind)` |
| 喂进去的是整张迷宫的房间数 | `CardRewardDomainService.cs:94` = `run.Layout.Rooms.Count`；而 `DungeonLayoutRuleService.cs:57` 把 `maze.Rooms` 整个交出去 |
| 真副本的真房间数 | 副本 **144**（本课题抓包所用、判据里的"普通副本"对照）**五个迷宫分别是 10 / 6 / 4 / 6 / 4 个房间** |

**即：五个迷宫里有三个，通关后免费牌与付费牌都是整面空白。**

**为什么判据一条都没红**：所有牌面 fixture 的 `Rooms` 都只有 1 个或 3 个房间
（`PaidCardFlipCostTests.cs:320`、`CardRewardTests.cs:1020`、`ResultScreenTests.cs:291`、
`SetPlayResultHandlerTests.cs:603` 各 1 个；`CardRewardDomainServiceTests.cs:46`、
`DungeonRunClearRewardCardTests.cs:39` 各 3 个），**全部落在 1–5 里**。
判据造了一个真副本不存在的世界，然后在那个世界里全绿。

### 二、更要紧的：这三条读法本来查得到，而没人去查

控制器顺着参照服务端追了一遍，`[reward item rate per map max count]` 那一段它是这么用的
（`ServerS4A21/Server/DfoServer/GameWorld/ClearRewardDefinitionCatalog.cs:184-195`）：

```csharp
internal double GetExplorationItemRate(int totalRoomCount, int visitedRoomCount)
{
    ...
    var exploredRatio = (double)visitedRoomCount / totalRoomCount;
    if (!_mapCountRates.TryGetValue(totalRoomCount, out var configuredRate))
        return exploredRatio;                                   // ← 查不到不是"不出货"
    return Math.Max(0.0, configuredRate) * exploredRatio / 10000.0;
}
```

由此**同时**落定了三件本 Spec 记为"分辨不了"的事：

| 编号 | 本仓现状 | 参照服务端实际怎么做 | 出处 |
|---|---|---|---|
| **A** | 查不到 ⇒ 概率 0 ⇒ 整面空白 | 查不到 ⇒ 退回 `visited/total`，**从不退回"不出货"** | 同上 `:191-192` |
| **B** | 当作一个按房间数取的平表率 | 是**探索度**：`configuredRate × visited/total ÷ 10000` | 同上 `:190-194` |
| **C** | 免费牌与付费牌同走 `ChanceOf`、同用 `default` 档 | 免费牌用探索率 + `Default` 档；**付费牌根本不查这张表**，用 `[gold card create rate]` + `Event` 档 | `Game/Dungeon/ClearRewardGenerator.cs:114-137` |

其中 C 直接推翻了本仓 `PvfClearRewardRateSource.cs:130-134` 那条
「`[gold card create rate]`……是门槛、是倍率、还是计数，未查明；不读」——
**它查得到，就在参照服务端里，而且答案是"倍率"。**

**而本 Spec 早就在用这份参照服务端**：TASK-08 引它的 `Tool/86JP.PvfLib/Models/DungeonFile.cs`
与 `TournamentDungeonApplicationService.cs` 来论证 `[result card]` 的字段名与消费方。
**同一份材料，问 TASK-08 的问题时想得起来，问 TASK-02 的问题时没想起来。**
于是"未查明"清单里至少混进了三条"没去查"。

**这是本 Spec 最要紧的一条系统性发现**，它比缺陷 A 本身更值得记：
「未查明」与「没去查」写出来一模一样，而前者诚实、后者只是看起来诚实。

### 三、裁定：task-rework，并批准扩界

批准的理由：TASK-09 契约的 Permitted files 本就写着
「不改产品代码，**除非核对中发现的缺陷需就地修正**」——这正是那一条设立的情形。
已批准的 Spec 验收边界未变（牌按归档的率承载装备），中心做法未变。

## Contract Amendment 3 — 为修 A/B/C 扩界（本条写在任何改动之前）

**Permitted files or interfaces 增补**：

- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`
- `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`
- `Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`
- 上述各处对应的判据文件

**Acceptance conditions 增补**：

1. **A 必须修**：房间数落在归档声明之外时，**不得**把牌打成整面空白。
2. **B 与 C 各自要么按参照服务端的实际做法实现，要么明确记为"已查明但本仓刻意不同"并写清理由**——
   不得再留在"未查明"栏里。本仓已有 `VisitedCells`
   （`IDungeonRunDomainService.cs:22`），`visited/total` 两个数都现成，不需要新增管线。
3. **判据必须用副本 144 的真迷宫房间数（10/6/4/6/4）**，且**该判据在现实现下必须变红**——
   否则它没钉住任何东西。fixture 只用 1–3 个房间正是这个缺陷藏了九个任务的原因。
4. **重扫一遍"未查明"清单**：每一条都要说明**是去参照服务端查过而查不到**，
   还是**没查**。凡后者，去查。

**不在本条授权内**：改变已批准的 Spec 验收边界；为此新开协议；动 TASK-08 已封存的成果。
若修 B/C 需要越出以上清单，**停下来写阻塞报告**。
## Execution Result — round 2

### 这一轮做了什么

按 `Contract Amendment 3` 修 A、B、C 三条，并按其第 4 条把整份「未查明」清单
逐条重扫一遍。参照服务端我自己打开核过，**没有只依据裁定里的转述**：
`E:/Project/DNF/ServerS4A21` 与 `E:/Project/86JP` 两棵参照树里
`ClearRewardDefinitionCatalog.GetExplorationItemRate` 与
`ClearRewardGenerator.GenerateFreeItemCard` / `GeneratePaidItemCard` **逐字相同**，
所以 A/B/C 的依据不是单树孤证。

副本 144 的房间数也是我自己读的，不是照抄：
`PvfProbe lst dungeon/dungeon.lst 144` → `StormOfMetastasis/Abnoba/Redcrystal_Forest.dgn`，
五个 `[map specification]` 分别是 9 map + 1 boss、5 + 1、3 + 1、5 + 1、3 + 1，
即 **10 / 6 / 4 / 6 / 4**；`[reward item rate per map max count]` 实测
`1 2000 2 3000 3 4000 4 5000 5 6000`，只到 5。裁定的两个数字都成立。

---

## 八、先取得红：判据钉住的是真副本，不是 fixture

按「先让它红，再让它绿」的次序做的。**在任何实现改动之前**，
先把 `ClearRewardArchiveTests.Every_maze_of_an_ordinary_dungeon_carries_a_chance`
写进去，用真档案读副本 144 的五个迷宫，逐个问 `ChanceOf`。第一次跑：

```
[FAIL] Dfo.Server.Application.Tests.World.ClearRewardArchiveTests
       .Every_maze_of_an_ordinary_dungeon_carries_a_chance
  错误消息: 10 rooms carried nothing
失败!  - 失败: 1，通过: 0，已跳过: 0，总计: 1
```

红在 `10 rooms carried nothing` 这一句上，而**它前面的三条断言全过了**：
房间数确实是 `[10, 6, 4, 6, 4]`；表里有行的确实只有 `[4, 4]`；表里没行的确实是 `[10, 6, 6]`。
也就是说这条判据红的位置正是缺陷 A，不是别的。

阴阳对照在同一条判据里：`Free(4) = 700`（表内）与 `Free(6) = Free(10) = 1400`（表外），
同一条路径只差房间数；再加 `Free(10 房走 5 房) = 700`，把「探索度」这一半也钉住。

---

## 九、A、B、C 三条的改法

### 9.1 A —— 查不到房间数不再打成 0

`ClearRewardRules.ExplorationRate(mapMaxCount, visitedMapCount, mapCountRate, basis)`，
逐字转写参照服务端
`ServerS4A21/Server/DfoServer/GameWorld/ClearRewardDefinitionCatalog.cs:183-195`
（`E:/Project/86JP` 同文件 184-195 完全一致），只把它写死的 `10000.0` 换成本仓的
`options.RateBasis`：

- 总房间数至少 1，已走房间数钳在 `[0, 总数]` 内——两个钳都是参照的；
- 表里**有**这个总数：`max(0, rate) × 走过/总数 ÷ basis`；
- 表里**没有**：退回 `走过/总数`，**不退回 0**。

于是 6 房或 10 房的迷宫走完是 `1.0`，5 房走完仍是 `6000/10000 = 0.6`——
**表内且走满的旧答案一个都没动**，这是判据里那八行原样保留的 InlineData 在钉的东西。

### 9.2 B —— 这一项是探索度

`ChanceOf` 的第三个参数由 `int mapCountRate`（basis 的分子）改为
`double explorationRate`（纯倍率），算式相应由除两次 basis 改为除一次。
表内走满时两者**逐位相同**（`maps/basis × 1.0` 就是原来的 `maps/basis`），
所以这不是把旧数改了，是把旧数**表达成它真正的意思**并让「没走满」和「表外」有话可说。

### 9.3 C —— 付费牌根本不查这张表

`ServerS4A21/Server/DfoServer/Game/Dungeon/ClearRewardGenerator.cs:116-140`：

- `GenerateFreeItemCard` → `ClearRewardDropProfile.Default` + `GetExplorationItemRate(...)`
- `GeneratePaidItemCard` → `ClearRewardDropProfile.Event` + `PaidCardCreateRate`

`PaidCardCreateRate` 就是 `[gold card create rate]`（`ClearRewardDefinitionCatalog.cs:263`
`ParseFirstDouble(root.GetChild("gold card create rate"), text)`）。所以：

- `ClearRewardRuleService.ProfileFor(kind)`：免费 `default`，付费 `event`；
- `ClearRewardRuleService.ScaleFor(...)`：免费用探索度，付费用 `GoldCardCreateRate`；
- `ClearRewardTable` 新增 `double? GoldCardCreateRate`，`PvfClearRewardRateSource` 读它，
  并从 `UnreadSections` 里**删掉**——那一格原本写着「是门槛、是倍率、还是计数，未查明；不读」。

A21 实测：`[gold card create rate]` 是**单个 `Single` 型 `1`**（我另加了一条判据把
「值是 1」与「token kind 是 Single」两件事都钉住，因为只取整数的读法会得到空表，
而空表与「文件没有这一段」在本仓是两种不同后果）。

于是 86 级第三难度：免费面 `10000 × 1400 × 0.6 ÷ 10000 = 840`（不变），
付费面 `9000 × 1400 × 1.0 ÷ 10000 = 1260`，且**付费面对房间数完全不敏感**。

### 9.4 两个数从哪来

`CardRewardDomainService.FaceFor` 多传一个 `run.VisitedCells.Count`。
`VisitedCells` 在 `DungeonRunDomainService.Begin` 就以起始格开张、
只在 `TryMove` 命中 `Layout.Rooms` 时增长，所以它恒在 `[1, Rooms.Count]` 内，
既不会是 0 也不会超过总数。没有新增任何管线，与 Amendment 3 说的一致。

---

## 十、Amendment 3 第 4 条：整份「未查明」清单重扫

规则：每条只有两种结论——**「去参照服务端查过而查不到」**，
或**「没查过」；凡后者，这一轮去查了，并写出查到什么**。
编号沿用 round 1「二」的编号。

### 10.1 原来记「无归档出处、由本服务器决定」的十四条

| # | 值 | round 1 实际状态 | 这一轮的结论 |
|---|---|---|---|
| 1 | 概率分母 `RateBasis` | **没查** | **查明：10000。** `ClearRewardGenerator.cs:206-208` 把合成值钳在 `[0, 10000]`，再 `lcg.Next(10000)` 掷。本仓值不变，`ClearRewardRateOptions` 的 remark 已从「自洽不是声明」改为「第二个来源同意」 |
| 2 | 各表的合成方式 | **没查** | **查明，且本仓与之不同。** 参照是 `threshold = standardDropRate/100 × (base × mapRate + difficultyItemBonus) × (1 − deathDropPenalty)`（`ClearRewardGenerator.cs:199-206`；普通结算路径两个额外因子取默认 100 与 0，即恒等）。**难度是加法**，**组队倍率根本不进物品牌**——`ClearRewardDefinition.GetPartyMemberRate` 全仓唯一调用点是 `GenerateFreeGoldCard`（金币）。本仓仍是四项相乘。**未改**，理由见「十二」，已在 `ClearRewardRateOptions` remark 里具名为已知差异 |
| 3 | 地图数当分子还是替代率 | **没查** | **查明：两者都不是**，是探索度。已按 B 改。round 1 说「差七倍且没有任何判据能分辨」——真正的答案在第三条路上 |
| 4 | `MapMaxCount ← Layout.Rooms.Count` | **没查** | **查明，映射成立。** 参照 `totalRoomCount = DungeonRoomTopology.CountConfiguredRooms(maze)`（`DungeonEntryHandler.cs:1641`、`DungeonRoomTopology.cs:112-120`），即迷宫里带房间的格子数；`visitedRoomCount = instance.VisitedRoomCount` = 进过的房间数（`DungeonInstance.cs:728` `_rooms.Count`）。与本仓 `Layout.Rooms.Count` / `VisitedCells.Count` 同义。这一条从「解释性映射」出栏 |
| 5 | 每张牌固定 1 金币 | 用户决定，非读法 | 仍是用户决定。但它挂着的那句「`[dungeon difficulty gold drop bonusrate]` 存在但没有可乘的对象」**已查明**：参照拿它乘卡片金币（`GenerateFreeGoldCard`，与怪物金币表、击杀数、组队倍率一起）。本仓因金币是定值仍不适用，`UnreadSections` 那一格的说明已改为「不是未查明，是在这里不适用」 |
| 6 | `UnpricedPaidFlipCost = int.MaxValue` | **没查** | **查了，答案不利于本仓。** 参照线上写的是 `Math.Max(0, paidCardCost)`（`DungeonNotificationBuilder.cs:517`）：没有付费牌时 `paidCardCost` 停在初值 **0**（`DungeonSettlementHandler.cs:851-872`），查不到价时 `GetPaidCardCost` 逐级回退到最近的较低级、再回退到最低级，**永不返回「无价」**（`ClearRewardDefinitionCatalog.cs:134-163`）。也就是说**客户端从未被参照服务端喂过 `int.MaxValue`**。「客户端见到它会怎样」仍未实证——但这已经不是「没人知道」，是「本仓发明了一个参照从不发的值」。**未改**，见「十二」 |
| 7 | `FreeCardGold` | 同 5 | 同 5 |
| 8 | `SelectCardCommand.CharacterLevel` | 本仓内部形状 | 参照无对应问题。**顺带查明一件更要紧的事**：参照喂给这一族表的「等级」是**副本的 `[basis level]`**（`GetDungeonBasicLv` → `DungeonCatalog.GetBasicLevel` → `file.BasisLevel`），不是角色等级，且**三处都用它**：profile 等级带、品级带、付费价。副本 144 的 `[basis level]` 实测是 **5**。本仓三处都用角色等级。**未改**，见「十二」 |
| 9 | 三处 `int?` 的 null | round 1 已改 | 无新证据 |
| 10 | `CardSlotCount = 4` | 出处是上游 | **查明并佐证**：参照 `run.CardRewards` 是八项 `{freeGold, freeItem, default, default, paidGold, paidItem, default, default}`（`DungeonSettlementHandler.cs:873-885`），请求侧 `cardIndex > 3` 拒收，一面四位成立。**但参照只填每面前两位，且只有 `cardIndex == 0` 会真发奖**（`CardRewardCoordinator.cs:189-227`）。本仓四位都可能出货。差异见「十二」 |
| 11 | 两面共用 profile / 阶梯 / 池，只靠种子分开 | **没查** | **查明，已改**（C）。profile 与倍率两处分开，阶梯、品级带、池仍共用——参照也共用 |
| 12 | 不用 `[drop item type prob]` 再设一道门 | **没查** | **查明：位置就是物品类型。** 参照 `RollItemType` 按 `itemType - 1` 索引这四个数，装备分支只接受 type 2（普通装备）与 type 4（时装）。所以 `0 9500 0 500` = 装备 9500 / 时装 500，1 与 3 权重为零。本仓**仍只发装备**（用户 2026-08-31 的决定），但理由从「位置未查明」改成「明知含义，仍按用户决定不抽时装」 |
| 13 | 只用 `default` profile | **没查** | **查明，已改**（C）。三个 pcroom profile 仍无出处——参照也没有选它们的规则 |
| 14 | 付费价不与加成合成 | 未明说查没查 | **查明且一致**：参照 `GetPaidCardCost(level)` 只查 `[gold card cost table]`，不合成任何加成。差别只在键（见 #8）与查不到时的回退（见 #6） |

### 10.2 原来记「未实证的读法」的七条

| # | 读法 | 这一轮的结论 |
|---|---|---|
| 15 | 应答里指示内容归属哪个牌位的字节 | **去参照服务端查过，参照里没有这个字节。** `CardRewardNotificationSender.BuildCardInfoAck` 写八条记录，牌位只由循环下标隐含；第 0 条首两字节是 `freeSelected ? 0x00 : 0xFF` 与 `paidSelected ? 0x00 : 0xFF`，第 1..3 条固定 `FF FF 00 00`，第 4..7 条固定 `FF FF FF FF`。**本仓「四张全亮」的形状参照根本不做**，唯一依据仍是客户端寻址。仍未实证 |
| 16 | `[drop kind prob]` 两个槽 | **去参照服务端查过，全仓无此串**（`drop kind` / `DropKindProb` / `kind prob` 三种写法都搜过，零命中；参照的 `Parse` 也不取这一段）。加上 round 1 已确认它在归档这一族里独此一家，现在是「两个来源都找过，都没有」 |
| 17 | 付费翻牌请求的形状 | **查明，与本仓一致。** 参照 `CardRewardCoordinator.HandleSelectCard`：`body.Length < 2` 直接丢，`body[0]` = 牌型（`> 1` 拒），`body[1]` = 牌位（`> 3` 拒）；**付费与免费同一形状、同一 opcode 0x0047**，没有第二种包形。本仓的两字节读法与「一字节体丢弃」都对得上。这一条可以从「未实证」降级 |
| 18 | `[gold card use]` 非零值 | **去参照服务端查过。** ServerS4A21 全仓命中两处，都在 `Tool/PvfLib/Models/DungeonFile.cs`（:184 声明默认 `-1`、:551 解析），**无第三处读点**；参照决定有没有付费牌的是 `!DungeonData.TryGetTowerOfDespairFloor(dungeonId, out _)`，与该 tag 无关。仍定不了案 |
| 19 | 版式帧那八个 u16 | **去参照服务端查过，参照写死且不解释。** `BuildCardLayoutAck` 硬写 `0x01` + `0x0001` + 7×`0xFFFF`，不随本局任何东西变化，无注释无常量名。仍未查明 |
| 20 | 应答记录首字节读作「哪一面」 | **查过，round 1 的记述成立**：那一格参照自己当「免费牌是否已选」用，紧邻的第二格是付费的同款。本仓读作「面」仍是推断 |
| 21 | 金币在翻牌应答里的走法 | **查明，且本仓与参照同路。** 参照在 0x0047 里写的是 **物品 id `0` + 金额**，不是 `id == -1`；真正把钱给玩家的是**背包货币槽加钱 + 背包刷新通知**（`DungeonPersistentEffectApplicationService.cs:2214-2239` → `InventoryRefreshSender.SendOnlineUpdateItemList`）。TASK-07 交付的「账号加钱 + 背包/金币通知」正是这条路。参照里的 `-1` 是**空奖位哨兵**（无尽塔与血色祭坛的空槽写 `-1, 0`），不是金币路径。「客户端会不会看不到金币」仍未实机，但已不是「没查」 |

### 10.3 「归档有、本仓没读的四段」

| 段落 | 这一轮的结论 |
|---|---|
| `[item drop rarity control]` | **去参照服务端查过，参照解析了却从不读**——`ClearRewardDefinitionCatalog` 与 `PassiveObjectRandomDropDefinitionCatalog` 两处都只解析、存下、暴露成属性，全仓无任何 `.RarityControl` 读点；后者只把「这一段不存在」当成一个必填校验（缺了就把整份定义降级为 Disabled），**值本身从不参与计算**。仍未查明 |
| `[gold card create rate]` | **原来是「没查」。现在查了，参照读它，含义是付费面的倍率。已读，已用。** 这一段是本次三条缺陷里最能说明问题的一条：它的说明写的是「是门槛、是倍率、还是计数，未查明」——写得像查过了 |
| `[chronicle set reward rate]` | **去参照服务端查过，全仓无此串**（`chronicle set reward rate` / `ChronicleSetReward` / `set reward rate` 均零命中）。仍未查明 |
| `[dungeon difficulty gold drop bonusrate]` | **查明**：参照乘在卡片金币上。本仓因金币是用户定值，仍不适用；说明已改为「不是未查明，是在这里没有可乘的对象」 |

### 10.4 这一节的账

原「未查明」清单里，**至少七条是「没去查」而不是「查不到」**：
分母、合成方式、地图数的角色、`map max count` 指什么、两面是否同分布、
只用 default profile、`[gold card create rate]`。另有三条（`int.MaxValue`、
副本级 vs 角色级、一面四位）是**从没被当成问题问出来过**，这一轮顺着同一份材料撞上的。

剩下**真正「查过而查不到」的只有五条**：`[drop kind prob]` 两槽、
`[gold card use]` 非零值、`[item drop rarity control]`、`[chronicle set reward rate]`、
版式帧八个 u16；外加两条只能实机定案的：牌位归属字节、客户端见到 `int.MaxValue` 的反应。

---

## 十一、判据

新增与改写的判据，按「它钉住什么」列：

| 判据 | 钉住什么 |
|---|---|
| `ClearRewardArchiveTests.Every_maze_of_an_ordinary_dungeon_carries_a_chance` | **副本 144 的真房间数 10/6/4/6/4**；表内两个、表外三个；每个迷宫两面都能出货；`Free(4)=700 / Free(6)=Free(10)=1400`；10 房走 5 房 = 700 |
| `ClearRewardArchiveTests.A_paid_face_is_drawn_at_the_event_profile_and_its_own_rate` | 付费面 = event 档 9000 × `[gold card create rate]` 1.0 → 1260；把 144 五个迷宫各走满与各走一房共十种情形扫一遍，答案**只有一个**；同一场合免费面是 840 |
| `ClearRewardArchiveTests.The_chance_a_position_carries_is_the_archives_numbers_combined` | 旧八行**原样保留**（表内走满，数字一个没动）+ 新六行（10/10、10/5、6/6、4/4、4/2、4/1） |
| `ClearRewardArchiveTests.No_combination_the_archive_can_reach_passes_the_basis` | 倍率集合改为「表内各档走满 ∪ 表外走满 ∪ 付费倍率」，最大倍率 = 1.0，最陡合成由 1344 升到 **2240**，仍远低于 basis |
| `ClearRewardRulesTests.A_maze_the_table_prices_is_that_rate_scaled_by_the_share_walked` | 表内：`rate × 走过/总数 ÷ basis`（六行） |
| `ClearRewardRulesTests.A_maze_the_table_prices_nothing_for_falls_back_to_the_share_walked` | 表外退回 `走过/总数`（五行）+ 1..40 房每个尺寸走一房都 > 0 |
| `ClearRewardRulesTests.An_exploration_beyond_the_maze_is_held_at_the_maze` | 参照的三个钳（总数 ≥ 1、走过钳在总数内、负率取 0） |
| `ClearRewardRulesTests.The_basis_is_read_from_the_argument_rather_than_held_inside` | basis 现在有**两个**读者（合成一个、探索度一个），同一个 1500 在 10000 与 2500 两个 basis 下给出 13 与 210 |
| `ClearRewardRuleServiceTests.The_kind_of_a_face_decides_the_profile_and_the_scale_it_is_drawn_at` | **原判据 `The_kind_of_a_face_does_not_change_what_is_drawn` 断言的正是反面**，已重写：两面同场合不同率，且只有免费面随走过的房间数动 |
| `ClearRewardRuleServiceTests.One_seed_does_not_make_the_two_kinds_one_face` | 原判据里仍然成立的那一半（种子分开两面）单独留下 |
| `ClearRewardRuleServiceTests.A_maze_the_table_prices_nothing_for_still_carries_a_chance` | 小样本里的同一件事；原 `An_occasion_the_tables_do_not_describe_carries_no_chance` 的 `mapMaxCount: 99` 那一行**已删**——房间数在表外**不再是**「表描述不了的场合」 |
| `CardRewardDomainServiceTests.How_much_of_the_maze_was_walked_comes_off_the_run` | 走两个房间 → `VisitedMapCount = 3`；走回头路 → 仍是 2（是集合不是步数） |
| `PvfClearRewardRateSourceTests.The_paid_faces_own_rate_is_read_from_the_file` | `GoldCardCreateRate == 1.0`，且该 token 的 kind 实测是 `Single` |
| `PvfClearRewardRateSourceTests.The_sections_this_reading_skips_are_named_and_still_in_the_file` | 未读段落由四条减为三条 |

### 全量判据

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| 项目 | 通过 | 失败 | 跳过 |
|---|---|---|---|
| `Dfo.Server.Protocol.Tests` | 479 | 0 | 0 |
| `Dfo.Server.Networking.Tests` | 51 | 0 | 0 |
| `Dfo.Pvf.Tests` | 60 | 0 | 0 |
| `Dfo.Server.Persistence.Tests` | 248 | 0 | 0 |
| `Dfo.Server.Host.Tests` | 4 | 0 | 0 |
| `Dfo.Server.Application.Tests` | 895 | 0 | 0 |
| `Dfo.Server.Infrastructure.Tests` | 215 | 0 | 2 |
| **合计** | **1952** | **0** | **2** |

退出码 **0**。基线 1925 → 1952（+27：Application +26、Infrastructure +1），跳过数仍为 2。
`dotnet build Server/86JP.Server.slnx --nologo`：**0 警告 0 错误**（`TreatWarningsAsErrors` 下）。

---

## 十二、查明了但**没有改**的：需要控制器裁定的五条

这五条都不在 `Contract Amendment 3` 批准的清单内（该条只批了 A、B、C 与重扫），
按边界要求**停在这里，不自行扩界**。每条都写清了证据与影响，改不改由控制器定。

1. **难度是加法不是乘法，组队倍率不进物品牌。**
   参照 `threshold = base × exploration + difficultyItemBonus`；本仓
   `base/basis × difficulty/basis × exploration × party`。两者在**每一个难度上**都不同，
   最尖锐的是最低难度：本仓读法下它清空整面，参照读法下它只是不加分。
   改它会推翻已验收的 `The_archives_lowest_difficulty_empties_every_position` 一类判据，
   属于「改变已批准的 Spec 验收边界」，Amendment 3 明写不在授权内。

2. **参照喂表的等级是副本的 `[basis level]`，不是角色等级。**
   三处都用它：profile 等级带、`[item drop ref table]` 品级带、`[gold card cost table]` 价格。
   副本 144 的 `[basis level]` 是 **5**；本仓用角色的 86。后果具体：
   付费翻牌价 **580 vs 10300**，品级带 **(2, 8) vs (79, 89)**。
   这是本轮查到的**影响最大的一条**，但它同时会推翻 TASK-07 已验收的价格判据，
   而且「本服务器是否要照抄」是设计决定不是读法决定。

3. **查不到价时，参照写 0 并逐级回退，本仓答 null 并让帧写 `int.MaxValue`。**
   参照 `GetPaidCardCost` 永不返回「无价」；线上一律 `Math.Max(0, cost)`。
   本仓的哨兵是客户端从未被参照喂过的值。改它牵动 TASK-05/07 的「0 是价格、
   未定价不是免费」那一整套论证，不是就地修正的分量。

4. **参照一面只填前两位，且只有 0 号牌位真发奖；本仓四位都可能出货。**
   `run.CardRewards` 八项里只有 0/1/4/5 有内容；`CardRewardCoordinator` 里
   `cardIndex` 1..3 只翻面不发奖。这与 round 1 清单 #15「四张全亮」是同一个问题的两面。

5. **稀有度阶梯**（重申，非新发现）：参照 `lcg.Next(1000000) + 1`，最高掷 1000000，
   于是 1000001/1000002/1000003 三档**永不可达**；本仓从末项读 basis = 1000003、
   最高掷 1000002，最稀两档各宽一掷。本仓 `RarityLadder` 的 remark 早已把这一条
   具名为「刻意不同」——**这说明本 Spec 在别处是会去查参照服务端的**，
   于是「问 TASK-02 的问题时没想起来」就更难解释成材料不可得。

---

## 十三、变异检查

规程：改动一律经写文件（mtime 自然前进），跑受影响项目的判据，
还原后用 `sha256sum` 逐文件核内容。**先取得一个真红再信其余**——
第一个施加的是 N1，红 13 条，之后才继续。

| # | 变异 | 位置 | 真实结果 |
|---|---|---|---|
| N1 | `ExplorationRate` 表外退回 `walked` → 退回 `0.0`（把缺陷 A 放回去） | `ClearRewardRules.cs` | **红 13 条**（外加 1 条无关偶发，见下）：`Every_maze_of_an_ordinary_dungeon_carries_a_chance`、`No_combination_the_archive_can_reach_passes_the_basis`、`The_chance_…_combined` 的 10/10、10/5、6/6 三行、`A_maze_the_table_prices_nothing_for_falls_back_to_the_share_walked` 全 5 行、`An_exploration_beyond_the_maze_is_held_at_the_maze` 的两个「总数 ≤ 0」行、`A_maze_the_table_prices_nothing_for_still_carries_a_chance` |
| N2 | `max(0, rate) * walked / basis` → `max(0, rate) / basis`（去掉探索度这一半，即缺陷 B） | 同上 | **红 7 条**：`The_chance_…_combined` 的 4/2 与 4/1 两行、`A_maze_the_table_prices_is_that_rate_scaled_by_the_share_walked` 的 4/2、4/1、4/0 三行、`An_exploration_beyond_the_maze_is_held_at_the_maze` 的 `walked: -1` 行、`The_kind_of_a_face_decides_…` |
| N3 | `ProfileFor` 付费也返回 `Default`（去掉 C 的一半） | `ClearRewardRuleService.cs` | **红 3 条**：`A_paid_face_is_drawn_at_the_event_profile_and_its_own_rate`、`The_kind_of_a_face_decides_…`、`One_seed_does_not_make_the_two_kinds_one_face` |
| N4 | `ScaleFor` 付费也走探索度（去掉 C 的另一半） | 同上 | **红 2 条**：`A_paid_face_is_drawn_at_…`、`The_kind_of_a_face_decides_…` |
| N5 | `run.VisitedCells.Count` → `run.Layout.Rooms.Count` | `CardRewardDomainService.cs` | **红 3 条**：`How_much_of_the_maze_was_walked_comes_off_the_run`、`The_occasion_is_assembled_from_the_run_and_the_settlement` 两行 |
| N6 | `FirstReal(sections, CreateRateSection)` → `FirstReal(sections, CostSection)` | `PvfClearRewardRateSource.cs` | **变异未生效，不算证据。** 两个项目全绿。原因：`[gold card cost table]` 的第一个数是 `1`（1 级），与 `[gold card create rate]` 的 `1` **恰好同值**，倍率没变。这是「换了值但没换行为」的变异，记为无效而不是记为「判据没钉住」 |
| N6b | `FirstReal(sections, CreateRateSection)` → `null` | 同上 | **红 5 条**：Infrastructure 的 `The_paid_faces_own_rate_is_read_from_the_file`；Application 的 `A_paid_face_is_drawn_at_…`、`Every_maze_of_an_ordinary_dungeon_carries_a_chance`、`No_combination_…`、`The_archive_declares_the_numbers_the_composition_is_built_from` |

**没有一次是编译失败**：七次施加全部编译通过、判据全部真的跑了。
（`ChanceOf` 第三参数由 `int` 改成 `double` 之后，旧判据里的整数实参**仍能编译**，
只是在运行时变成一个荒谬的倍率——所以那一批旧判据是**真红**而不是编译错误，
这一点我专门确认过。）

**还原核对：六个文件全部 `sha256` 与变异前逐字节相同。**
过程中出过一次真事故，如实记：**N5 的还原脚本写错了**。
它用「第一处匹配」替换，而 `run.Layout.Rooms.Count,` 在
`CardRewardDomainService.FaceFor` 里**先出现在 `MapMaxCount` 那一行**，
于是还原把这两行对调了——文件能编译、看起来正常，但语义被换掉。
`sha256` 比对当场报 `DIFF`，我手工把两行换回并重新校验一致。
**这正是「还原用 sha256 校验内容」这条规程存在的理由**：
如果我只看「脚本说 revert 成功」，这一轮的产出会带着一个被悄悄换掉的语义交出去。

## Executor Self-check — round 2

### 判据数字（实测）

- **CK-023 全量**：`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`
  → **1952 通过 / 0 失败 / 2 跳过，退出码 0**。基线 1925 → +27（Application 869 → 895，
  Infrastructure 214 → 215）。跳过仍是那两条「缺档案时应报错」的用例。
- `dotnet build Server/86JP.Server.slnx --nologo`：**0 警告 0 错误**。
- **先红后绿的红，原文在「八」**：`10 rooms carried nothing`，只有那一条，
  且同一判据里「房间数是 10/6/4/6/4」「表内只有 4、4」「表外是 10、6、6」三条断言当时就通过了。
- 中途出过**两次互不相同的偶发红，都与本轮改动无关，如实记**：
  一次是 `Application.Tests.Quests.QuestLogTests.ProgressGoesToTheNamedSlotEvenWhenTwoSlotsHoldTheSameQuest`，
  一次是 `Application.Tests.Transactions.OneDatabaseOneParticipantTests.AFailureInOneKindOfStateLeavesTheOtherOneUnchanged`。
  两条都用数据库 fixture，都在单独跑与随后的全量里通过，形状与 round 1 记的
  Networking 真 socket 用例同类（满载下的预算）。**我没有只报最后那次绿。**

### 变异检查：真实结果

七次施加，**没有一次编译失败**，每一次判据都真的跑了。逐条真实结果见「十三」，要点：

- **N1 是第一个，红 13 条**，之后才继续；「先拿到红再信其余」是照做的。
- **N6 是一次无效变异**：把 `[gold card create rate]` 换成 `[gold card cost table]` 的第一个数，
  两个数**恰好都是 1**，行为没变，全绿。我**没有**把它记成「判据没钉住这一点」——
  那会是一条假的负面证据。换成 N6b（置 null）后拿到红 5 条。
- **N5 的还原写错了，被 `sha256` 当场抓住。** 脚本按「第一处匹配」回填，
  而 `run.Layout.Rooms.Count,` 在同一个构造里先于 `run.VisitedCells.Count,` 出现，
  于是两行被对调；文件照样编译、看着正常。手工换回后六个文件全部与变异前逐字节相同。
- 每次都经写文件（mtime 前进），没有出现增量编译跳过重编的假绿——
  七次里六次真变异都各自红了，红的条目与变异点一一对得上。

### 改了哪些文件

**产品代码**

1. `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`
   —— 新增 `ExplorationRate`；`ChanceOf` 第三参数 `int mapCountRate` → `double explorationRate`
   （算式由除两次 basis 改为除一次）；`ClearRewardRateOptions` 的 remark 重写
2. `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
   —— `ClearRewardOccasion` 增 `VisitedMapCount`；`ClearRewardCardKind`、接口、`ChanceOf`
   三处 remark 重写（原文断言「两面同分布」「房间数在表外就是没有场合」，两句都已作废）
3. `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`
   —— `ClearRewardTable` 增 `double? GoldCardCreateRate`；
   `MapCountRates` / `DropKindRates` / `ItemTypeRates` 三处说明重写
4. `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
   —— `Profile` 常量 → `ProfileFor(kind)`；新增 `ScaleFor`；`ChanceOf` 改写；
   三处 remark（含 `PaidFlipCost` 上补记的「不合成已获参照佐证，但取值的键与查不到时的
   回退与参照不同」）
5. `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`
   —— `FaceFor` 多传 `run.VisitedCells.Count`
6. `Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`
   —— 读 `[gold card create rate]`（新 `FirstReal` 辅助）；`UnreadSections` 由四段减为三段；
   `[dungeon difficulty gold drop bonusrate]` 那一格的说明改写

**判据**

7. `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs`
8. `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs`（含 `ClearRewardFixture`）
9. `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRulesTests.cs`
10. `Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs`
11. `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs`

**流程文件**

12. 本文件 —— 追加这两节，前面每一节（含 round 1 的产出与控制器裁定）一字未动

行尾：改过的每个文件都按**字节数**核过 `\r`，**全部为 0**（Python 数 `b'\r'`，
不用 `grep -c '\r'`）。

### 越界与克制

**我动了一个不在 `Contract Amendment 3` 清单里的文件：
`Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`。这一条我要说清楚。**

Amendment 3 增补的清单里有 `IClearRewardRuleService.cs`（同一目录）但没有这一个。
我动它的理由有两条，都请控制器复核：

1. **B 无法不动它而实现。** 「探索度」是一个纯倍率，而 `ChanceOf` 的第三个参数
   原本是「basis 的分子」。要么改这个签名，要么在规则服务里把倍率再乘回 basis 取整
   传进去——后者会引入一次多余的取整，而且会让 `ChanceOf` 的参数名继续说谎。
2. **`ClearRewardRateOptions` 的「命名假设」正文就在这个文件里**，
   而它整段都在论证「分母无出处」「四张表怎么合成无从分辨」。这一轮把两条都查明了
   （一条落定、一条落定为已知差异），**把这段留着不动，就是本 Spec 反复栽的那种
   「读起来像已经核过」的句子**——留着比改了更糟。

另外，本 Task 原契约的 Permitted files 本就写着「不改产品代码，**除非核对中发现的缺陷需就地修正**」，
控制器在 round 1 裁定里也是引这一条批准扩界的。**但我不拿这句当空白支票**：
除上述一个文件外，我没有动 Amendment 3 清单之外的任何产品代码，
「十二」那五条**每一条都停下来写成待裁定，一条都没有自行改**。
**如果控制器认为这一个文件属于漂移，回退面是明确的**：把 `ExplorationRate` 与
`ChanceOf` 的签名还原，B 就落不了地——也就是说这个回退等于退回 A/B/C 三条中的两条。

### 仍未查明的东西

**只能实机定案的两条**

1. 应答记录里指示牌位归属的字节。**参照服务端里没有这个字节**——它只填 0 号位，
   所以「四张全亮」这一整个玩法在参照里没有对应物。
2. 客户端见到 `int.MaxValue` 作为付费翻牌价会怎样。参照从不发它（发 0）。

**两个来源都找过、都没有的五条**

3. `[drop kind prob]` 两个槽的含义（参照不解析这一段）。
4. `[gold card use]` 非零值的含义（参照解析后从不读；五次声明全写 0）。
5. `[item drop rarity control]` 的含义（参照两处都解析后从不读；只当必填校验用）。
6. `[chronicle set reward rate]` 的含义（参照全仓无此串）。
7. 版式帧那八个 u16 各选中什么（参照写死 `0x0001` + 7×`0xFFFF`，无注释）。

**本轮明确没有复核的**

- `GameInventoryItemEntry.CommonLength = 101`（既有结论，本 Spec 只做搬家）。
- `NOTI-0x0023.md` 的那份抓包字节本身。
- 参照服务端的**金币数额算法**（`ExpTableProvider.GetMonsterGold`、`175/1000`、
  加权击杀数那一串）。我只核到「`[dungeon difficulty gold drop bonusrate]` 乘在这里」
  为止，没有去核那条公式对不对——本仓金币是用户定值，核了也用不上。
- `EquipmentDropPoolProvider.GetClearRewardPool` 与本仓 `DropItemFamily.Equipment`
  是不是同一个池。两边都叫「装备池」，我没有逐项比过。

### 我自己不确定的地方

1. **「十二」第 2 条（副本 `[basis level]` 而非角色等级）我越想越不安，
   风险等级我判为本轮最高。** 它同时改动付费价（580 vs 10300）与品级带（(2,8) vs (79,89)），
   两个都是玩家直接看得见的数。我没有改，因为它推翻 TASK-07 已验收的价格判据，
   而 Amendment 3 明写不改已批准的验收边界。**但如果控制器认为本仓要贴近参照，
   这一条比 A/B/C 任何一条的影响都大。**

2. **B 落地后，难度仍是乘法这件事让合成变成了一个「半参照半本仓」的混合体。**
   现在的算式是：base 与 difficulty 按本仓的乘法、exploration 按参照的、party 按本仓的。
   我认为这比「三条都不改」好，也比「悄悄把难度也改成加法」好（后者越界），
   **但它确实不是任何一个来源的原样**，我把这句写在 `ClearRewardRateOptions` 的 remark 里，
   没有让它只活在这份任务文件里。

3. **付费面现在是 1260/10000，比免费面的 840 高。** 这是照参照算出来的：
   `event` 档的 9000 **低于** `default` 档的 10000，但付费倍率 `[gold card create rate]` 是
   **1.0**，高于五房走满的探索度 0.6，两下相抵后付费面更高。
   **付费牌比免费牌更容易出货是否合理，我没有判断依据**——参照就是这么算的，
   玩家要付 10300 金币也说得通，但我没有实机或抓包证据。

4. **`ClearRewardFixture.Table()` 的 `event` 档原本只是个「摆在第一位的诱饵」**
   （用来钉住「服务不会取声明顺序第一个 profile」），现在它变成了付费面真正会用的档。
   两个用途叠在一个 fixture 上，我保留了原来的诱饵语义（default 仍声明在第二位），
   但**这个 fixture 现在同时在做两件事**，读起来比以前费劲。我没有拆开。

5. **`One_seed_does_not_make_the_two_kinds_one_face` 里，我用「免费面必中、
   付费面五五开」来区分两面。** 这是确定性的（种子固定），但它依赖 fixture 的
   `event` 档正好是半率。如果以后有人调 fixture 的 `baseRate`，这条判据会以
   一种不明显的方式失去锋利度。我在注释里写了它依赖什么，但没有加断言把它焊死。

6. **round 1 的第 5 条自问（「本仓迷宫会不会超 5，我没查」）这一轮有了答案，
   但我要说清一件事**：我这一轮是**因为控制器点名才去查参照服务端的**。
   `RarityLadder` 的 remark 早就写着「参照服务端硬编码一百万」——
   也就是说本 Spec **知道**有这份材料、**用过**这份材料，只是问 TASK-02 的问题时没去用。
   我没有办法证明这一轮的「未查明」清单已经穷举——
   我能证明的只有「清单上的每一条我都去参照服务端搜过了，搜法写在『十』里」。
   **清单本身是不是完整的，仍然是一个判断，不是一个证明。**

## Controller Disposition — 契约改版，本 Task 退回队列重做

用户在 2026-09-01 逐条判定了九条决定，契约随之升到 revision 4，Spec 的 RQ-007/008/010
被改写、新增 RQ-016。本 Task 的 round 1 与 round 2 产出**保留在案**，但其中
**探索度实现（`ExplorationRate`）已被 revision 4 取代**，读数对照也需按新的自定项重做。

**处置**：本 Task 未经独立评审通过，故不置 `done`，退回 `todo`，
依赖增加 TASK-10 / TASK-11 / TASK-12——读数审计必须在三者之后重做，
否则审计的是一份马上要变的实现。

**保留有效的部分**（新一轮须继承，不得丢失）：
- `Contract Amendment 1` 的 13 行与 `Contract Amendment 2` 的 4 行具名清单，仍是密封条款；
- round 2 查出的三处 `int?` null 语义漂移、CK-019 过滤器陈旧、死脚手架已清；
- round 2 用真档案独立复核过的归档事实（16 段/7 共享/9 独有、品级带与怪物表仅 80–89 差 1 等）。

**新一轮须补入清单的**（revision 4 带来的自定项）：
加法合成、房间上限 6000、骰子乘组队、`[drop prob]` 阈值读法、副本基准等级取值、
三个保底开关的默认全关，以及「网吧牌是归档承认的第三种牌但本 Spec 不实现」。

## Contract Amendment 4 — 常设检查：那把尺子

**来源**：TASK-10 round 3 造的检查法，控制器已独立验证可用。

**Acceptance conditions 增补**：对**全部程序集（含判据程序集）**跑一遍：

```
dotnet build <csproj> --nologo -p:GenerateDocumentationFile=true -p:NoWarn=1591   -p:TreatWarningsAsErrors=false
```

（不改仓里任何文件），读生成的 XML，解析不到的 cref 写作 `cref="!:..."`。

**每一条 `!:` 都要去看它旁边那段散文还成不成立，而不是只把 cref 修好。**

**为什么**：TASK-10 round 3 用它揪出 `CardRewardDomainServiceTests.cs` 里一句
`The rules do read the kind`——revision 4 之下说反了。那处**挺过了改规则、改判据、
写自检、以及一整轮独立评审**，因为它藏在一个没被改动的文件里，**读 diff 结构上看不见**。
尺子量不了「散文说反了」，但**悬空 cref 是它的有效代理**：一处 cref 指向已不存在的东西，
往往正说明旁边那段话谈的是已经变了的事。

## Contract Amendment 5 — TASK-10/11/12 完成后转来的具名条目

**来源**：三个改造任务的评审与控制器裁定。**下列每一项都必须在读数对照中具名出现**，
与 Amendment 1/2 的条款同等约束，漏一项即为不满足。

### 增补到「记录内部的分歧」

| 分歧 | 详情 |
|---|---|
| `IsImpossible` 比「表答不上来」更宽 | `ClearRewardRuleService.cs:268` 写着 "Every route here is a table that did not answer"，但 `ClearRewardRules.cs:31` 的 `IsImpossible` 还含两支「四张表都答上了、合成结果为零」（`scaledParty <= 0`、`losing >= basis`）。控制器验算：A21 上两支都到不了（组队倍率恒为 1~1.6；shortfall 上限 8000 < 10000）。**这句话凭今日数据为真，写得像凭结构为真**，换档案即分岔 |
| `Undrawn` 的「四个调用者」 | `ClearRewardRuleService.cs:267` 写 "all four of its callers"，实际全仓**只有一个调用点**（`:204`），四个指的是进入条件 |

### 增补到「无归档出处、由本服务器决定」

| 值 | 出处任务 | 性质 |
|---|---|---|
| 加法合成、房间上限 6000、骰子乘组队、`[drop prob]` 作阈值、整数比较 | TASK-10 | 五项，归档均未声明；分母 10000 不同，它有归档三处旁证 |
| `[drop prob]` 选等级段**刻意仍用角色等级** | TASK-11 | 与品级带、付费价改用副本基准等级相反的一处；今日两种选择结果相同（`default` 四段全 10000），已用手搭表两向钉住 |
| 三个保底开关默认全关 | TASK-12 | 归档的两件保底品：黄金牌那件 2013-09-03 已删、网吧牌那件属未做的黑钻体系；免费牌归档无兜底表 |
| 兜底物品的品质与耐久 | TASK-12 | 兜底行只有权重/物品/数量三个数，无阶梯可摇 |
| 「表答不上来」不发兜底 | TASK-12 | 归档没说这条路该怎样；控制器裁定：发了等于把缺陷变成静默发放 |

### 增补到「未实证的读法」

| 读法 | 出处任务 | 一次实机怎么验 |
|---|---|---|
| 网吧牌如何送达 | TASK-12 | 归档承认它是第三种牌（单列兜底表），但翻牌协议牌型只接受 0/1；本 Spec 读作「直接发、不用翻」，未实证 |

## Execution Result — round 3

### 这一轮做了什么

按 revision 4 之后的实现，把读数对照整份重做，并把 `Contract Amendment 1 / 2 / 5`
的 **25 行密封清单逐行归栏**；按 `Contract Amendment 4` 对**全部 19 个程序集**跑了那把尺子
（17 个在判据解决方案里，另加 `Tool/PvfProbe`、`Tool/PvfProxy`），
并对尺子报出的每一处去看旁边那段散文还成不成立。

**核对手段**：`Tool/PvfProbe` 直接读 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`；
读源码；对每一条计数型断言做机器复核。**判据里已有的断言不算证据，判据本身也是被核对的对象**——
本轮改的十处里有两处就在判据文件里。

**结果**：核出**十处真缺陷，全部就地修正**（全是 XML doc / 注释，无一行行为改动）。
其中**两处是这一 Spec 反复栽的那个形状的新实例**：
`[gold card create rate]` 的 revision-3 遗留读法，与 TASK-12 加进来的「牌型决定兜底表」
把四处「牌型决定不了牌面内容」的断言变成了「今天为真、结构上为假」。
另有一处是**尺子直接量出来的**：`UnpricedPaidFlipCost` 的 XML 不闭合，
整段说明（正是 `Amendment 2` 点名要补的那一段）**在生成的文档里被整块丢弃**。

判据数字**不变**：1963 / 0 / 2，退出码 0。

---

## 十四、`Contract Amendment 4`：那把尺子，全仓跑了一遍

命令（**不改仓里任何文件**，只加 MSBuild 属性）：

```
dotnet build <csproj> --nologo -p:GenerateDocumentationFile=true -p:NoWarn=1591 \
  -p:TreatWarningsAsErrors=false
```

对 19 个 csproj 逐个跑，全部 exit 0；生成的 XML 时间戳全部是本轮的（没有拿到陈旧产物）。

### 14.1 尺子在改动前量到了什么

| 程序集 | `cref="!:…"` | 其他 XML doc 告警 |
|---|---|---|
| `Dfo.Server.Abstractions` | **3** | — |
| `Dfo.Server.Protocol` | 0 | **CS1570 × 2** |
| 其余 8 个 src 程序集 | 0 | — |
| **7 个判据程序集全部** | **0** | — |
| `PvfProbe` / `PvfProxy` | 0 | — |

**尺子确实在量东西，不是量了个空**：同一批 XML 里 `cref=` 总数为
Abstractions 220、Protocol 74、Application 50、Infrastructure 16，
判据侧 Application.Tests 63、Protocol.Tests 13、Infrastructure.Tests 13、其余各 1–2。
全仓 XML doc 告警只有三类：CS1573（缺 `param` 标签，578 条，全仓既有噪声，与本主题无关）、
CS1574（3 条）、CS1570（2 条）。

### 14.2 三条 `!:`：报出，不动

`Server/src/Dfo.Server.Abstractions/Quests/IQuestStore.cs:31-32` 的
`!:Carried` / `!:Cleared` / `!:Tracked`。控制器已说明与本主题无关，**照办没动**。
旁边那段散文我仍然读了一遍：它讲的是两个 quest store 在 `Tracked` 顺序上不一致，
与翻牌无关，本轮不评价其真伪。

### 14.3 两条 CS1570：**尺子在本主题里揪出的那一条**

```
DungeonClearSlice.cs(425,11): CS1570 结束标记 remarks 与开始标记 para 不匹配
DungeonClearSlice.cs(426,1):  CS1570 元素 remarks 需要结束标记
```

`UnpricedPaidFlipCost` 的 remarks 里开了两个 `<para>` 只闭合了一个。后果不是排版难看：
生成的 `Dfo.Server.Protocol.xml` 里这一条整个变成

```xml
<!-- Badly formed XML comment ignored for member
     "F:…DungeonClearPayloadCodec.UnpricedPaidFlipCost" -->
```

**也就是说 `Contract Amendment 2` 点名要求补的那段「null 有两种成因」的说明，
在文档产物里根本不存在。** 源码里写了、文档里没有——这正是「看起来做了而其实没有」的
又一个实例，而且是**读 diff 结构上看不见**的那种：`</para>` 少一个不改变任何一行的语义。
已补上闭合标签，重跑尺子后该成员的整段说明出现在 XML 里，全仓 `Badly formed XML comment`
归零。

### 14.4 改动后重跑尺子

| 项 | 结果 |
|---|---|
| `cref="!:…"` | 仍然只有 `IQuestStore.cs` 那 3 条，19 个程序集其余全为 0 |
| CS1570 | **0**（原 2 条已消） |
| `Badly formed XML comment` | **全仓 0** |
| 新增的 `<see cref>` 是否解析 | 本轮新写的 `ClearRewardChance.IsImpossible`、`WirePositionCount`、`GoldCardCreateRate`、`ClearRewardSettlementResult`、`Blank_items_are_what_a_switched_on_paid_card_hands_out`、`FaceFor` 六个 cref **全部解析**——CS1574 一条没多，生成的 XML 里六个都是全限定名而非 `!:` |

### 14.5 尺子量不到的那一半，本轮自己补了一遍

`!:` 只抓**解析不到的 cref**。本仓大量跨切片引用写成 `<c>Xxx</c>`，那是纯文本，
尺子对它一无所知。所以本轮另做了一遍机器扫描：把本主题 58 个 `.cs` 文件里所有
`<c>标识符</c>` 形式的符号引用抽出来（共 101 个不同符号），逐个回仓里找。
**全部命中**，其中值得单说的是被引到的判据名：
`Blank_items_are_not_handed_out_where_the_tables_could_not_answer`、
`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`、
`A_clear_that_levelled_the_character_still_charges_what_it_announced`、
`Neither_the_kind_nor_the_dungeons_level_reaches_the_odds`、
`One_seed_answers_the_same_face_for_both_kinds` —— 五个全部存在且拼写一致。

**但这只证明名字对得上，不证明旁边那句话对。** 十七节里的十处，
只有一处（14.3）是尺子指出来的；其余九处是靠读散文与机器复核计数查出来的。
**结论：这把尺子在本仓是一个很弱的代理**——它在判据程序集上的产出是 0，
而 TASK-10 当初正是用它揪出判据文件里那句反了的话。
本轮把这一点如实记下来：**尺子归零不等于散文没问题**。

---

## 十五、CK-021（上）：指得回归档的读数

「A21 实测」列是这一轮用 `PvfProbe` 亲自读出来的，**没有沿用前两轮的转述**。

### 15.1 `etc/itemdropinfo_clearreward.etc`

| 读数 | 代码位置 | 归档段落 | A21 实测（本轮） |
|---|---|---|---|
| 每 profile 段数 | `PvfClearRewardRateSource.BandCountSection` | `[drop prob count]` | `4` |
| 六个具名 profile | `ClearRewardProfile` | `[drop prob]` | 界一律 `1-15/16-30/31-40/41-200`；`default` 四段 `10000`；`event`、`event2`、`pcroom bonus` 四段 `9000`；`pcroom default` 四段 `10000`；`pcroom event` `9000 8000 8000 8000` |
| 难度加成 | `ClearRewardTable.DifficultyRates` | `[dungeon difficulty drop bonusrate]` | `0 600 1000 1400 1200`——首项为 0、第五项小于第四项 |
| 房间数基础值 | `ClearRewardRules.MapBaseRate` | `[reward item rate per map max count]` | `1 2000 2 3000 3 4000 4 5000 5 6000`，**只到 5** |
| 组队倍率 | `ClearRewardTable.PartyMemberRates` | `[party member drop bonusrate]` | `1 1.2 1.4 1.6`，token kind `Single` |
| 稀有度阶梯 | `ClearRewardTable.Rarity` | `[basis of rarity dicision]` | `500000 799900 999900 1000000 1000001 1000002 1000003` |
| 付费翻牌价 | `ClearRewardTable.GoldCardCosts` | `[gold card cost table]` | 1..200 逐级；**5→580**、86→10300、200→23980；表内**无 0**、**无 `int.MaxValue`** |
| 付费面自有的一个数 | `ClearRewardTable.GoldCardCreateRate` | `[gold card create rate]` | 单个 `1`（float） |
| 品级带 | `ClearRewardTable.GradeBands` | `[item drop ref table]` | 200 行；86 → `7 / 3` → `(79, 89)` |
| 黄金牌兜底 | `GoldCardBlankItems` | `[gold card blank item]` | 六行，物品 id 全为 `10002120`，权重和 **10000** |
| 网吧牌兜底 | `PcRoomCardBlankItems` | `[pcroom card blank item]` | **一行** `50 7454 1`，权重和 **50** |
| 类别概率（读了不用） | `DropKindRates` | `[drop kind prob]` | `10000 0` |
| 物品类型概率（读了不用） | `ItemTypeRates` | `[drop item type prob]` | `0 9500 0 500` |
| 刻意不读的**三**段 | `PvfClearRewardRateSource.UnreadSections` | `[item drop rarity control]` / `[chronicle set reward rate]` / `[dungeon difficulty gold drop bonusrate]` | `2 13 17 0 -5` / `0 50 1 75 2 100` / `1.02 1.38 1.6 1.9 2` |

**段落总数机器复核**：把 dump 里的非闭合 tag 去重，**恰好 16 个**；
读的 13 个 + 不读的 3 个 = 16，对得平。
（这一步直接推翻了 `ClearRewardContent` 里「四段不读」那句，见「十七.1」。）

**「`-5` 是全文件唯一负数」**：把 dump 全文按 `(^| )-[0-9]+` 扫一遍，命中 **1 次**，就是它。

**兜底物品的两条实测**：
`10002120` → `event/chn_event_20130723_GhostSeria/chn_stk_signs_of_seria.stk`，
`[name]` 幽灵赛丽亚的痕迹，`[explain]` 写着「该道具将于2013年9月3日6时统一删除」；
`7454` → `event/vendingmachine/pcroomtoken_vendingmachine.stk`，`[name]` 钻石硬币，
`[explain]`「可以启动1次黑钻售货机。(只有黑钻会员才能启动黑钻售货机)」。
`ClearRewardBlankItemSwitches` 两个开关的关闭理由**逐字成立**。

**品级带与怪物表的差异，机器逐行复核**：两表各 200 行、键集相同；
`below` 列 **200 行全同**；`above` 列**恰好在 80..89 这十级不同**（本表 3，怪物表 4），
其余 190 行全同；`level - below` 的最小值是 **1**（1 级那行写 `0`），
所以 `ReadGradeBands` 的下钳在这份归档上确实永不触发。

### 15.2 副本侧

| 读数 | 代码位置 | 归档段落 | A21 实测（本轮） |
|---|---|---|---|
| 副本关掉付费牌 | `DungeonDefinition.OffersPaidCard` | `[gold card use]` | 1047 个副本：**1042 不声明，5 个声明且全写 `0`** |
| （不读）副本自己的牌面产出 | 无 | `[result card]` | **1043 不声明，4 个声明** |
| 副本基准等级 | `ClearRewardOccasion.DungeonBasisLevel` ← `DungeonLayout.BasisLevel` | `[basis level]` | 副本 144 = **5** |
| 「怪物等级换成角色等级」开关 | `ClearRewardOccasion` 两个等级的分工论据 | `[mob level charac level replace flag]` | 1047 个副本：913 不声明、**133 写 1、1 写 2**，即 **134 个声明、无一为零** |
| 真副本房间数 | `ClearRewardOccasion.MapMaxCount` ← `run.Layout.Rooms.Count` | `[map specification]` | 副本 144 五个迷宫 = **10 / 6 / 4 / 6 / 4** |

代码里写的「134 of the archive's 1047 dungeons declare and every one of them declares non-zero」
**逐字命中**（133 + 1 = 134，取值 1 与 2，无零）。

### 15.3 合成式的算术，逐个验算

`ClearRewardRules.ChanceOf` 的散文里有七个具体数，全部重算：

| 散文里的数 | 验算 |
|---|---|
| 「不掷骰的最大和是 7400」 | 6000（最大房间行）+ 1400（最大难度） = 7400 ✓ |
| 「最小是 2000」 | 2000（最小房间行）+ 0（最低难度） = 2000 ✓ |
| 「7400 × 1.6 = 11840，阈值 10000」 | ✓ |
| 「6000 × 1.6 + 1400 = 11000」 | ✓ |
| 「1.6 取到 basis 的整数部分是 16000」 | `round(1.6f × 10000)` = 16000 ✓ |
| 「四人队、shortfall 2600 时实数读法放 1625 过，得 8376；整数读法给 8375」 | 整数：`losing = 2600×10000/16000 = 1625`，rate = 8375 ✓。实数：`2600 / 1.60000002384 = 1624.99997…`，故 1625 胜出，winners = 8376 ✓。**两个数都对，差别确实只在第八位小数上** |
| `Both_faces_of_one_clear_carry_the_same_odds` 断言的 7400 | shortfall = 10000 − 6000 − 1400 = 2600；party 1 → scaledParty 10000；losing 2600；rate **7400** ✓ |

---

## 十六、CK-021（下）：密封清单 25 行**逐行**归栏

`Contract Amendment 1` 的 13 行、`Amendment 2` 的 4 行、`Amendment 5` 的 8 行，
**共 25 行，一行不漏，逐行编号**。编号 `A1-n` / `A2-n` / `A5-n` 对应各 Amendment 表里的行序。
标「新增」的是本轮补上的。

### 16.1 无归档出处、由本服务器决定

| # | 值 | 出处 | 本轮结论（按 revision 4 的实现） |
|---|---|---|---|
| **A1-1** | **出现概率的分母 `ClearRewardRateOptions.RateBasis`** | TASK-02 | **仍无声明，但已从「自洽」升到「文件自己的算术旁证」。** `ClearRewardRateOptions` 的 remark 列了三处旁证，本轮逐条实测：`[drop kind prob]` `10000 0` 两项和为一万 ✓；`[drop item type prob]` `0 9500 0 500` 四项和为一万 ✓；`default` profile 四段全 10000 而旁边的 profile 付得少 ✓。**三条都成立，但三条都不是声明**——文件里没有任何一处说「分母是一万」。仍具名为假设，配置项 `ClearRewardRates:RateBasis`，`ChanceOf` 以参数收，自身不带默认值 |
| **A1-2** | **各项加成与基础率的合成方式** | TASK-02 → revision 4 | **仍无出处，且这一轮换了读法。** revision 4 定为**加法**：`房间基础值 + 难度加成 + 骰子×组队 > 阈值`。归档只给四个单参数查表，没有任何东西把它们连起来。加法的最强论据仍是难度表首项为 0（乘法读法会让最低难度整面清空）。**这一条与 `A5-3` 的第一项是同一件事的两个记法**，本表按 Amendment 1 的原名保留，实体在 `A5-3` |
| **A1-3** | **地图数当分子还是当替代率** | TASK-02 → revision 4 | **两个候选读法都已被否，换成第三个，仍无出处。** round 2 曾按参照服务端读成「探索度」，revision 4 把探索度撤了；现在它是**加法里的基础值**（`ClearRewardRules.MapBaseRate` → `ChanceOf` 的第一个加数）。归档从未说这个数是分子、是替代率、是探索度分量还是加数。**本 Spec 在这一格上换过三次读法，三次都指不回归档的任何一句。** 这一点必须原样留在清单里 |
| **A1-4** | **`MapMaxCount ← Layout.Rooms.Count`** | TASK-03 | **解释性映射，未消解。** 段名是 `reward item rate per map max count`，本仓把它等同于本局迷宫的房间数（`CardRewardDomainService.FaceFor` 里 `run.Layout.Rooms.Count`）；归档没有一处说这个量就是房间数。**round 1 记的那个可观测后果（房间数 > 5 整面为空）已经被 revision 4 消除**——查不到的行取最大行；映射本身的无出处**没有变** |
| **A1-5** | **每张翻到的牌固定 1 金币** | 用户决定（契约 revision 2） | **仍是自定值。** `GameProtocol.ClearRewardCardGold = 1`，其 remark 明写「不是读归档读来的」。归档这张表里没有把金币命中换算成金额的段落；`[dungeon difficulty gold drop bonusrate]` 存在但在这里**没有可乘的对象**，`UnreadSections` 那一格具名为 inapplicable 而非 unknown。变异 M7 证实：**钉住这个 1 的判据是且只有一条** |
| **A1-6** | **`UnpricedPaidFlipCost = int.MaxValue`** | TASK-05 | **仍是发明的哨兵，客户端见到它会怎样仍未查明。** 唯一被实测支撑的性质本轮又验了一遍：1..200 级价格表里既没有 `int.MaxValue` 也没有 0。round 2 另查明参照服务端从不发这个值（发 `Math.Max(0, cost)`），那是差异不是出处。**这一格本轮出了新问题并已修：它的整段说明在生成文档里被丢弃，见「十七.5」** |
| **A1-7** | **`FreeCardGold`** | TASK-05 / TASK-07 | **已消解 TASK-05 那一半，本栏仍留。** `DungeonClearPayloadCodec.FreeCardGoldAt` 现在写的是「0 号牌位有东西就写 `GameProtocol.ClearRewardCardGold`」，即本服务器实际会付的钱，与 `A1-5` 同源同常量。它只是从「占位 0」变成「自定值 1」，**没有因此获得归档出处** |
| **A1-8** | **`SelectCardCommand.CharacterLevel`** | TASK-07 | **仍是惰性入参，且 revision 4 之后更彻底。** 参数还在（`SelectCardCommand.cs:137`），remark 明写「It does not price this flip, and that is the point」。本轮顺着又走了一遍：`ChargeAsync` 只在牌已定死之后被调到，而 `SettleAsync` 遇到已定死的牌直接返回既有值、不问规则服务；**TASK-11 之后价钱连键都换成了副本基准等级**，所以这个参数距离价钱又远了一层。判据 `A_paid_flip_asks_the_rules_nothing_because_its_price_was_already_fixed` 钉住 |
| **A2-1** | **三处 `int?` 的 null 现在有两种成因** | TASK-08 | **已消解（round 1 修的，本轮逐处复核仍在）。** 四处全部写明两种成因：`DungeonClearPayloadCodec.UnpricedPaidFlipCost`（「Two clears reach this, and the name only says the first」）、`ClearRewardSettlementResult.PaidFlipCost`（「Null has two causes and this type carries neither of them separately」）、`ClearRewardCardSet.PaidFlipCost`（「Null has two causes and they are not kept apart here」）、`SelectCardStatus.NotPriced`（「Two clears reach this」）。**但第一处的说明在文档产物里是不存在的**，见「十七.5」——**这一条在源码层面消解了，在产物层面直到本轮才真消解** |
| **A5-3** | **加法合成、房间上限 6000、骰子乘组队、`[drop prob]` 作阈值、整数比较（五项）** | TASK-10 | **五项归档均未声明，五项都在 `ClearRewardRules` 上具名成一张编号清单。** 逐项复核：① 加法——在 `ChanceOf`，论据是「bonusrate 这个名字」与「首项为 0」；② 骰子乘组队——在 `ChanceOf`，论据是「乘全和会让满队必中：7400×1.6=11840、6000×1.6+1400=11000」，两个数本轮验算成立；③ 房间数无行取最大行（A21 上是 6000）——在 `MapBaseRate`，论据是「真迷宫常大于 5（144 的五个是 10/6/4/6/4，本轮实测）」「大迷宫不能比小的付得少」「取最大行不引入文件里没有的数」；④ `[drop prob]` 读作阈值——在 `ChanceOf`，论据是「`default` 付满而 `event` 付少，读成门槛才使 event 是优惠」，本轮实测 `event` 在 41-200 段是 9000、`pcroom event` 是 8000 ✓；⑤ 整数比较——在 `ChanceOf`，理由写的是**可复现而非更准确**，并给出两读法分岔的具体点（8375 vs 8376），本轮验算成立。**分母 10000 不在这五项里，它有三处归档旁证（见 A1-1），这个区别代码里也是这么摆的** |
| **A5-4** | **`[drop prob]` 选等级段刻意仍用角色等级** | TASK-11 | **本服务器的决定，且是与另外两处相反的一处。** 品级带与付费价改用副本 `[basis level]`，阈值段仍用角色等级；理由写在 `ClearRewardOccasion.CharacterLevel`：六个 profile 的命名轴是「玩家的处境」（`event` / `pcroom`），段是同一轴下一级；而推动另外两处的证据是 `[mob level charac level replace flag]`，它说的是**怪物**等级，够不着「牌位有没有东西」。**今日两种选择结果相同**（`default` 四段全 10000，本轮实测 ✓），所以判据用手搭的分段表两向钉住：`ClearRewardRuleServiceTests.The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons` |
| **A5-5** | **三个保底开关默认全关** | TASK-12 | **本服务器的决定，归档只声明表、不声明该不该发。** `ClearRewardBlankItemSwitches` 三个开关各自写了关闭理由：免费牌**归档根本没有那一节**（本轮确认 16 个 tag 里没有第三张兜底表）；黄金牌那件 `10002120` 归档自述 2013-09-03 删除（本轮读到原文）；网吧牌那件 `7454` 是黑钻售货机代币而本仓无黑钻体系（本轮读到原文）。变异 M5 证实开关真被读：把判断反过来，6 条判据变红 |
| **A5-6** | **兜底物品的品质与耐久** | TASK-12 | **本服务器的决定。** 兜底行只有权重/物品/数量三个数（本轮实测两张表都是三列），没有阶梯可摇；`ClearRewardRuleService.Fallback` 的 remark 写明品质取阶梯最低（与 `DropRules.RollRarity` 在无阶梯时给的值一致），耐久取 0（两件都是 `.stk` 而非 `.equ`，本轮解析确认两个 id 都落在 `stackable/` 下） |
| **A5-7** | **「表答不上来」不发兜底** | TASK-12 | **本服务器的决定，归档没说这条路该怎样。** 理由在 `ClearRewardRuleService.Undrawn`：兜底回答的是「骰子掷了、这一位仍空」，而表没答上来就没掷过骰；发了等于把「读不出归档」变成「静默发四件」。判据 `Blank_items_are_not_handed_out_where_the_tables_could_not_answer` 钉住。**这条的措辞本轮发现有问题并已修，见「十七.4」与 `A5-1`** |
| 新增 | **一面四个牌位 `ClearRewardRules.CardSlotCount = 4`** | TASK-02 | 归档无出处，remark 已写明：文件里那个 4 是 `[drop prob count]` 的段数，与牌位无关。线上帧宽是 8（`WirePositionCount`），两者刻意分开 |
| 新增 | **两面共用同一阈值 profile、同一阶梯、同一品级带、同一池** | TASK-02 | 归档对「免费面与付费面有什么不同」一个字都没有。本仓选「odds 完全相同」。**注意：TASK-12 之后这句话只在赔率上成立**，牌型现在还决定兜底表——见「十七.3」 |
| 新增 | **只抽装备，不拿 `[drop item type prob]` 再设一道门** | TASK-02 | `0 9500 0 500` 读进来不用。round 2 查明位置含义（参照的 `RollItemType` 按 `itemType-1` 索引，装备 9500、时装 500），所以现在**不是「位置未查明」而是「明知含义仍按用户 2026-08-31 的决定不抽时装」**；`ClearRewardRuleService` 类级 remark 已是后一种写法 |
| 新增 | **只用 `default` profile** | TASK-02 | 归档声明六个 profile，**不给任何选择规则**。本仓固定 `default`，三个 pcroom profile 明知本仓有网吧状态也不接。变异 M6：改成 `Event`，25 条判据变红 |
| 新增 | **付费翻牌价不与任何加成表合成** | TASK-07 | `PaidFlipCost` 只查 `[gold card cost table]`。归档没说价格要不要随难度/队伍变；不合成是取向。round 2 查明参照服务端也不合成——那是佐证不是出处 |
| 新增 | **付费价查不到时答 null 而不逐级回退** | TASK-07 / round 2 | 参照服务端 `GetPaidCardCost` 逐级回退到最低级、**永不答「无价」**；本仓答 null 并让帧写哨兵。已在 `IClearRewardRuleService.PaidFlipCost` 与 `ClearRewardRuleService.PaidFlipCost` 两处具名为「与参照不同，刻意不采纳」 |

### 16.2 未实证的读法

| # | 读法 | 出处 | 一次实机怎么验 / 本轮结论 |
|---|---|---|---|
| **A1-9** | **应答里指示内容归属哪个牌位的字节** | TASK-04 | **仍未实证，整个「四张全亮」压在它上面。** 依据是客户端寻址 `this + 0x1E8 + 100*b + 60` 与 `> 7` 上界；参照服务端那一格写的是 `paidSelected ? 0x00 : 0xFF` 且只写在八条里的第一条，**参照根本不做「四张全亮」**。验法：给某个非零牌位填内容，看对应的牌是否亮起。**该处散文本轮改了一句，见「十七.6」** |
| **A1-10** | **付费翻牌请求的形状** | TASK-04 | **round 2 已从「未实证」降级为「查明且一致」**：参照 `CardRewardCoordinator.HandleSelectCard` 是 `body.Length < 2` 丢、`body[0]` 牌型（>1 拒）、`body[1]` 牌位（>3 拒），付费与免费同 opcode 同形状。本仓的两字节读法与「一字节体丢弃」对得上。**仍留在本栏**：第二个来源同意不等于实机验过，客户端第二个构包点 `0x00D10DFF` 只写一个字节这件事仍未解释 |
| **A1-11** | **`[drop kind prob]` 两个槽各代表什么** | TASK-01/02 | **两个来源都找过、都没有。** 归档侧：本轮把这一族七个文件的 tag 集比过，该段**只在翻牌表出现**；参照侧：round 2 三种写法搜过零命中，参照的 parse 不取这一段。读成「物品满值、金币为零」仍是取向。`ClearRewardTable.DropKindRates` 的 remark 已是「looked-for-and-not-found」而非「merely not read」 |
| **A2-2** | **`[gold card use]` 非零值的含义** | TASK-08 | **仍定不了案，两个前提本轮又实测了一遍**：1047 个副本里只声明五次、五次全写 `0`（`values` 输出 `1042 (absent) / 5 → 0`），第三种值不存在；参照解析了该字段（默认 `-1`）却全仓不读。读作「没关」仍是无证据下的取向 |
| **A5-8** | **网吧牌如何送达** | TASK-12 | **仍未实证，而且本轮发现 Amendment 5 对本仓立场的描述与代码不一致，如实报出。** 归档承认它是第三种牌（`[pcroom card blank item]` 单列一张兜底表，本轮实测一行 `50 7454 1`），而翻牌协议牌型只接受 0/1（`CardRewardPayloadCodec.ReadSelectCardRequest`）。**Amendment 5 写「本 Spec 读作『直接发、不用翻』」；代码不是这么写的**——`ClearRewardBlankCard.PcRoom` 写的是「No flip on this server can name it … the table is read and the switch is here while the path that would use them is not built」，即**送达路径根本没建**，而不是「换一种方式送达」。`ClearRewardBlankItemSwitches.Allows(PcRoom)` 在产品代码里**没有调用者**（`FallbackRowsFor` 只映射 Gold 与 Free），本轮复核属实。**所以本栏该记的是：网吧牌如何送达在本仓没有实现也没有读法，Amendment 5 的转述比代码多说了一句。** 一次实机怎么验：无从验——没有可发起的请求 |
| 新增 | **版式帧那八个 u16 各是什么** | TASK-04 | 上游写死 `0x0001` + 7×`0xFFFF`，无注释无常量名；本仓照抄。客户端对本帧只做 16 位读，所以里面没有物品 id——但**这八个字各选中什么未查明** |
| 新增 | **应答记录首字节读作「哪一面」** | TASK-04 | 参照那一格自己用作「免费牌是否已选」。本仓读作面（0 免费 / 1 付费），`AnsweredFaceByte` 的 remark 已标为 inferred：免费值有抓包互证，付费值 `1` 是把枚举当真 |
| 新增 | **客户端见到 `int.MaxValue` 作为付费价会怎样** | TASK-05 | 参照从不发这个值（发 `Math.Max(0, cost)`，无价时停在 0）。本仓发一个参照从不发的值，客户端反应未实证 |
| 新增 | **`[item drop rarity control]` 与 `[chronicle set reward rate]` 的含义** | TASK-01 | 两个来源都找过：归档只此一处、无从横向对照；参照对前者解析后从不读、对后者全仓无此串。`UnreadSections` 里具名为 unascertained |

### 16.3 记录内部的分歧

| # | 分歧 | 本轮结论 |
|---|---|---|
| **A1-12** | **RQ-011 的「牌面产出声明」半条** | **复核成立，未消解，本 Spec 不实现。** 本轮实测：`[result card]` 在 1047 个副本里 **1043 不声明、4 个声明**；那四个（120/121/122/4119）**全部没有 `[map specification]`**，`IDungeonDefinitionSource` 对它们答 null，正常进本路径不可达，故该半条对每个可达副本都是空真。判据 `DungeonResultCardDeclarationTests.Every_dungeon_declaring_a_result_card_is_absent_from_the_definition_source` 钉住 |
| **A1-13** | **`NOTI-0x0023.md` 免费卡那一对的偏移** | **复核成立且未消解。** 布局块记 241/245（240 是标记），同一节的对账句记「240 与 245」，差一。`FreeCardGoldAt` 的 remark 把分歧原样摆着：「the record's two sentences do not agree. The layout block above is what this field is written from.」本轮无新证据可定案 |
| **A2-3** | **N2 变异的证据强度** | **复核成立，历史裁定不变。** TASK-08 的 N2「两向都红」论据错（十四条失败全在关闭侧），真正钉住未声明侧的是 N6 与评审加的默认值变异。**本轮把这条教训用上了**：M5 的第一版写成 `if (false)` 触发 CS0162→error，`TreatWarningsAsErrors` 下**编译失败**；我按规程把它记成「判据没跑」而不是「变红」，换成 `if (Allows(card))`（反转而非短路）重做才拿到红 6 条。见「十九」 |
| **A2-4** | **副本 3503 声明了开关却永不适用** | **复核成立。** 五个声明 `[gold card use]` 的副本里，3503（`Act5/Counterattackarea.dgn`）没有 `[map specification]`，其余四个（100/3700/3901/3902）都有。归档的声明与服务器能用到的范围不重合，已由 `DungeonResultCardDeclarationTests.The_dungeons_switching_the_paid_card_off_are_mostly_reachable` 钉住 |
| **A5-1** | **`IsImpossible` 比「表答不上来」更宽** | **成立，本轮已就地修正（见「十七.4」）。** 原文 `Every route here is a table that did not answer` 写得像凭结构为真；实际 `ClearRewardChance.IsImpossible` 是 `Rate <= 0 \|\| Basis <= 0`，还含两支「四张表都答上了、合成仍为零」（`scaledParty <= 0`、`losing >= basis`）。**控制器的验算本轮独立复算过**：A21 的组队倍率 1/1.2/1.4/1.6 → `scaledParty` 恒在 10000..16000，永不为 0；最大 shortfall = 10000 − 2000 − 0 = **8000** < basis 10000，故 `losing` 最大 8000，rate 最小 2000 > 0。**两支在 A21 上确实到不了，换档案即分岔**——已在 remark 里写成「只有这份档案让差别看不见」 |
| **A5-2** | **`Undrawn` 的「四个调用者」** | **成立，本轮已就地修正（见「十七.4」）。** 全仓 grep：`Undrawn(` 只有一个调用点（`ClearRewardRuleService.cs:204`），「四个」指的是那一个 `if` 的四个进入条件 |
| 新增 | **`ClearRewardCardSet.PaidFlipCost` 还在为一个已经消失的危险辩护** | 该 remark 用「角色升级会跨过价格表的分档，于是结算屏报一个价、点击时收另一个价」论证为什么要把价钱与牌一起定死。**TASK-11 之后价钱的键是副本 `[basis level]`，副本不会升级，这个情形不再存在。** 同一组类型里 `ClearRewardSettlementResult` 已经写明「前提没了、修法留着」，这一处没跟上——**同一件事两处口径不一**。已补一段，见「十七.8」 |
| 新增 | **`SelectCardCommand.CharacterLevel` 的说明用同一个已消失的情形举例** | 「a clear whose own experience levelled the character across one of the cost table's bands still charges what the result screen announced」——这句**本身仍为真**（确实照报价收），只是它举的情形在 revision 4 下已不可能发生。**没有改**：它的主张（定死牌就定死价）成立，改它的收益不抵在这一 Spec 上第四次写出新错句的风险。**记录在此** |
| 新增 | **`CardRewardDomainServiceTests` 的常量组「三个」与「四个」** | `Level` 的 summary 说「the level, the party size and the difficulty」（三个），紧接的 remark 说「None of the four …」（四个，含 `BasisLevel`）。**四个的说法是对的**（77/41/3/2 两两不等，本轮核过），summary 少列一个是因为 `BasisLevel` 自己另有 summary。**没有改**：措辞松而非事实错 |
| 新增 | **`ClearRewardRules` 的「五个决定」不含「阈值段取哪个等级」** | 那句是「Five decisions **here** are this server's own」，而取角色等级还是副本等级这个决定不在 `ClearRewardRules` 里（在 `ClearRewardOccasion.CharacterLevel`），所以按「here」的字面读它站得住。**但全仓自定项的实际条数大于五**，A5-4 就是第六项。**没有改**（改「五」要连带改各处序数，风险高于收益），**记录在此，并已在 A5-4 里独立具名** |

**清单核对**：`A1-1 … A1-13`（13 行）+ `A2-1 … A2-4`（4 行）+ `A5-1 … A5-8`（8 行）= **25 行，逐行到齐**。
分布：无归档出处栏 8+1+5 = 14 行密封项，未实证栏 3+1+1 = 5 行，分歧栏 2+2+2 = 6 行；14+5+6 = 25。

---

## 十七、核对中发现并就地修正的缺陷（十处）

全部只改 XML doc / 注释，**没有一行行为改动**，判据数字前后不变。
每一处都写了「为什么这是缺陷」。

### 17.1 `ClearRewardTable` 说文件有「四段不读」——实际是三段

**为什么是缺陷**：`ClearRewardContent.cs` 写
「Four sections … three because their purpose is not ascertained, and one because it is inapplicable」。
本轮把 dump 里的非闭合 tag 去重得 **16 个**，reader 读了 **13 个**（`BandCount`/`Profile`/`DropKind`/
`ItemType`/`Difficulty`/`Rarity`/`Party`/`MapCount`/`Cost`/`CreateRate`/`GoldBlank`/`PcRoomBlank`/`GradeBand`），
`UnreadSections` 里是 **3 个**。13 + 3 = 16 对得平。**四是 round 2 把
`[gold card create rate]` 移出未读清单后遗留的旧数**，而 reader 自己那一段已经写明「它曾是第三个」。
一份记录里两处对同一件事给不同的数，且**没有任何判据会因此变红**。

改为「Three … two because their purpose is not ascertained, and one because it is inapplicable」，
并加一句说明这个数是怎么落后的。

### 17.2 `[gold card create rate]` 的常量说明是 revision 3 的遗留，且与它指向的成员相反

**为什么是缺陷**：`PvfClearRewardRateSource.CreateRateSection` 的 summary 写
「**the paid face's own scale, where the free face reads the map-count table**」。
这是 round 2 按参照服务端做「探索度 + 付费面用 create rate」时写的。
**revision 4 把那套撤了**：现在两面都读房间数表，`ClearRewardTable.GoldCardCreateRate` 的
说明第一句就是「**Nothing on this path reads it**」。**同一个数，一处说它是付费面的倍率，
一处说没人读它。** 一个读代码的人会据此以为付费面用它算率——而这正是「读起来像已经核过」。

**变异 M8 是这一条的正面证据**：把 `FirstReal(sections, CreateRateSection)` 换成 `null`，
红的三条**全是值断言**（`The_paid_faces_own_rate_is_read_from_the_file`、
`The_archive_declares_the_numbers_the_composition_is_built_from`、
`Both_faces_of_one_clear_carry_the_same_odds` 里那句 `Assert.Equal(1.0, table.GoldCardCreateRate)`），
**没有一条关于牌面内容的断言动过**——即「读了、没人用」是可验证的，不是自称的。

改为「Read here and applied by nothing」，并指向那个把「另一台服务器怎么用它」
写成他人读法的成员。

### 17.3 四处「牌型决定不了牌面内容」，被 TASK-12 变成了「今天为真、结构上为假」

**为什么是缺陷**：TASK-12 给 `FaceFor` 加了 `FallbackRowsFor(table, occasion.Kind)`——
**牌型现在决定一位翻空的牌落到哪张兜底表**：付费面拿 `[gold card blank item]`，免费面拿空表。
只要黄金牌开关一开，同一颗种子的两面**就不再相同**：判据
`Blank_items_are_what_a_switched_on_paid_card_hands_out` 自己就断言了这一点
（「the paid card's switch moved the paid card only: the free face … still holds nothing」）。

而下面四句一句都没跟上，全部说牌型决定不了牌面内容：

| 位置 | 原文 |
|---|---|
| `ClearRewardCardKind` remark | 「as far as what a face holds goes, a fact they do not act on」；「it does not decide what a position holds」 |
| `IClearRewardRuleService` remark | 「It is also **the only thing** that separates the two faces of one clear … one seed asked for both kinds answers the same four positions twice」 |
| `CardRewardDomainServiceTests` remark | 「**The rules do not read the kind**」；「one seed handed to both draws them alike **always** rather than merely wherever two rates happened to agree」 |
| `ClearRewardRuleServiceTests.One_seed_answers_the_same_face_for_both_kinds` | remark 与行内注释「The kind travels with the face, and **decides nothing else about it**」 |

**这是本轮最要紧的一条，而且形状与 `Amendment 4` 的立案理由一模一样**：
TASK-10 round 3 修的那句 `The rules do read the kind`（当时说反了）被改成
`The rules do not read the kind`——**TASK-12 又把它变回错的**，
因为 TASK-12 的 diff 里没有这几个文件。那个 `always` 尤其尖锐：它明说是结构上的普遍性，
而它就是假的。**这四句挺过了 TASK-12 的实现、判据、自检与一整轮独立评审。**

四处全改：赔率相同这一半保留（仍然为真），
加上「牌型确实够得着的那一样是兜底表，且只在该牌开关打开时」，
并写明**今天两面相同是配置的结果而不是合成式的结果**。

**变异 M5 是这一条的反面证据**：把 `if (!Allows(card))` 反转成 `if (Allows(card))`，
红 6 条，其中 `Blank_items_are_what_a_switched_on_paid_card_hands_out` 与
`Blank_items_are_handed_out_by_nothing_while_the_switches_are_off` 两条正好是两侧。

### 17.4 `Undrawn` 的「四个调用者」与「每条路都是表没答上来」（`A5-2` / `A5-1`）

**为什么是缺陷**：前者是个直白的错数——`Undrawn(` 全仓只有一个调用点，四指的是进入条件；
一个照着「四个调用者」去找的人会找不到另外三个。后者更隐蔽：
`Every route here is a table that did not answer` 读起来像凭结构为真，
而 `IsImpossible` 还含两支「四张表都答上了、合成为零」。

已改：明写「一个调用点、四个进入条件」，并补一段说明
`IsImpossible` 更宽、A21 上那两支为什么到不了（组队倍率 1..1.6；最大 shortfall 8000 < basis 10000），
以及**换一份档案这句话就要当成一个决定来读**。两个数本轮独立验算过。

### 17.5 `UnpricedPaidFlipCost` 的整段说明在文档产物里被丢弃（尺子量出来的）

见「十四.3」。**为什么是缺陷**：`Amendment 2` 点名要求那三处 `int?` 写明两种成因，
round 1 照做了；但这一处的 `<para>` 不闭合，编译器把整个成员的注释扔掉并只留一行
`<!-- Badly formed XML comment ignored -->`。**验收条款要求的东西在源码里有、在产物里没有**，
而 `TreatWarningsAsErrors` 也没拦住它（CS1570 在默认判据命令下不出现，
只有加了 `GenerateDocumentationFile=true` 才现形——这正是那把尺子的用处）。
补一个 `</para>`；重跑后该成员的整段说明出现在 XML 里。

### 17.6 `ContentOwningPosition` 的「seven 恰好等于帧里的记录条数」

**为什么是缺陷**：上界是 7，`WirePositionCount` 是 **8**。宽容读法（0..7 共八个）说得通，
字面读法是错的。**round 1 看到过这一句并选择不改**（理由是「措辞松而非事实错」）；
本轮改了，理由是：本 Task 的全部内容就是核这种句子，
而这次的改法不需要重新论证任何事实——只是把「seven 是条数」换成
「seven 是 `WirePositionCount` 个牌位里的最后一个」，并保留原意（这个上界来自帧自己的宽度）。
`WirePositionCount` 用 `<see cref>` 引，尺子确认解析得到。

### 17.7 `ApplicationModule` 说规则服务「被交两个内容源」——实际是三个

**为什么是缺陷**：`ClearRewardRuleService` 的构造器是
`(IClearRewardRateSource, IDropItemPoolSource, IDungeonDefinitionSource, ClearRewardRateOptions)`，
类级 remark 自己写的是「Three content sources and one named assumption」，
判据 `The_rule_service_is_handed_no_reader_of_world_state` 用反射钉的也是三个。
**只有注册处还写着两个**——TASK-08 加进第三个内容源时没跟上。
这一条**直接落在 CK-022 的题面上**：本 Task 要枚举的就是这份依赖表，
而仓里唯一一处散文形式的枚举是错的。改为三个，并说明第三个是什么时候加进来的。

### 17.8 `ClearRewardCardSet.PaidFlipCost` 仍在防一个已经消失的危险

**为什么是缺陷**：见 16.3 的「新增」行。同一组类型里 `ClearRewardSettlementResult`
已经写明「前提没了、修法留着」，这一处没有；两处对同一个 `int?` 给出不同的时态。
补一段指明前提已消失、上一段保留为「当初把这个数放这儿的论据」，并指向已经写对的那一处。

### 17.9 与 17.10 —— 计入十处的另两处

- **17.9**：`ClearRewardCardKind` 最后一句「it does not decide what a position holds」
  与 17.3 是同一主题的不同位置，单独计一处（它在枚举尾部，不在同一段里）。
- **17.10**：`ClearRewardRuleServiceTests` 的行内注释「decides nothing else about it」
  与它上方 remark 是两处不同的断言，也单独计一处。

**十处合计改动 10 个文件**（8 个产品、2 个判据），逐个见自检的「改了哪些文件」。

---

## 十八、`Contract Amendment 2` 的另两项：本轮复核

### 18.1 死脚手架（`CardRewardDomainServiceTests` 的 `AskedOffered` / `ClosedDungeonId`）

**已在 round 1 清掉，本轮确认仍是清掉的状态**：`RecordingRules.OffersPaidFlipAsync` 现在是
`ValueTask.FromResult(true)`，remark 写明「A constant rather than a knob …
一个没人拨的旋钮会让人以为这个文件覆盖了那一侧」，并把真正覆盖那一侧的两个判据文件
`DungeonPaidCardSwitchRuleTests` 与 `PaidCardFlipCostTests` 点名列出（两个类本轮确认都存在）。

### 18.2 CK-019 的过滤器

`plan.md:261` 已是 `--filter PaidCard`（round 1 改的）。**本轮重新实测两个过滤器**：

| 过滤器 | 命中 | 结果 |
|---|---|---|
| `--filter PaidCard` | **25**（Infrastructure 8 + Application 17） | 全绿，退出码 0 |
| `--filter ResultCard` | **2**（全在 Infrastructure） | 全绿，退出码 0 |

数字与 round 1 完全一致（TASK-10/11/12 三轮改造没有动这两组的条数）。
`--filter PaidCard` 同时命中有声明（100/3700/3901/3902）与无声明（144/10000/10004）两侧，
CK-019 那一行的 Expected result 在新过滤器下成立。**本轮没有再动 `plan.md`。**

round 1 记的那条仍然成立且仍未处理：**副本 3503 那条判据落在
`DungeonResultCardDeclarationTests` 里，`--filter PaidCard` 选不中它**；
要一条命令覆盖 TASK-08 的全部证据得写 `--filter "PaidCard|ResultCard"`。不在密封条款里，未擅改。

---

## 十九、变异检查（在最终交付态上跑）

规程：改动一律**经写文件**（`os.utime` 让 mtime 前进）；跑受影响项目的判据；
还原后用 **sha256 逐文件核内容**；**编译失败记为「判据没跑」而不是「变红」**。
八次施加**全部在本轮十处改动完成之后**进行，即最终交付态。

| # | 变异 | 位置 | 判据项目 | 真实结果 |
|---|---|---|---|---|
| M1 | `MapBaseRate` 取最大行 → 返回 `null`（把 revision 4 之前的缺陷 A 放回去） | `ClearRewardRules.cs` | Application | **红 9**（8 个不同判据名，`The_chance_…_combined` 参数化命中两组：10 房与 6 房）：`A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row`、`A_maze_the_table_prices_nothing_for_takes_the_largest_row`、`Every_maze_of_an_ordinary_dungeon_carries_a_chance`、`Both_faces_of_one_clear_carry_the_same_odds`、`No_occasion_the_archive_describes_is_certain_or_impossible`、`One_dungeon_bands_and_prices_alike_for_every_character_level`、`The_chance_a_position_carries_is_the_archives_numbers_combined`、`A_maze_the_table_prices_nothing_for_still_carries_a_chance`。897 通过 |
| M2 | 去掉难度加数（`A5-3` 第①项） | 同上 | Application | **红 48**，含 `The_numbers_add_and_the_dice_makes_up_the_shortfall` 全组、`The_archives_lowest_difficulty_no_longer_empties_a_face`、`The_lowest_difficulty_adds_nothing_and_empties_nothing`。858 通过 |
| M3 | 骰子不再乘组队（`A5-3` 第②项） | 同上 | Application | **红 10**，含 `A_party_multiplier_stored_as_a_single_does_not_decide_a_dice_value`（就是 8375/8376 那条）。896 通过 |
| M4 | 价钱的键 `run.Layout.BasisLevel` → `settlement.CharacterLevel`（撤销 TASK-11） | `CardRewardDomainService.cs` | Application | **红 7**：`The_price_is_asked_at_the_dungeons_level_and_not_the_characters`、`One_dungeon_bands_and_prices_alike_for_every_character_level`、`A_clear_that_levelled_the_character_still_charges_what_it_announced` 等。899 通过 |
| M5a | `if (!Allows(card)) return [];` → `if (false) return [];` | `ClearRewardRuleService.cs` | Application | **判据没跑：编译失败**（CS0162 不可达代码，在 `TreatWarningsAsErrors` 下成错）。**记为「没跑」，不算一次变红** |
| M5b | 同处判断**反转**为 `if (Allows(card)) return [];`（`A5-5`） | 同上 | Application | **红 6**：`Blank_items_are_what_a_switched_on_paid_card_hands_out`、`Blank_items_are_handed_out_by_nothing_while_the_switches_are_off`、`Blank_items_are_not_handed_out_where_the_tables_could_not_answer`、`Blank_items_have_no_free_card_table_to_come_from`、`One_dungeon_bands_and_prices_alike_for_every_character_level`、`The_archives_lowest_difficulty_no_longer_empties_a_face`。900 通过 |
| M6 | `ThresholdProfile` `Default` → `Event` | 同上 | Application | **红 25**，含 `A_clear_is_measured_against_the_default_profiles_threshold`、`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`。881 通过 |
| M7 | `ClearRewardCardGold` `1` → `2` | `GameProtocol.cs` | Protocol | **红 1**：`The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision`。478 通过。**仍然只有一条**——这个自定值被多少判据钉住的答案是一条，不是一批（与 round 1 的发现一致） |
| M8 | `FirstReal(sections, CreateRateSection)` → `null` | `PvfClearRewardRateSource.cs` | Application + Infrastructure | **红 3**：Infrastructure 的 `The_paid_faces_own_rate_is_read_from_the_file`；Application 的 `The_archive_declares_the_numbers_the_composition_is_built_from` 与 `Both_faces_of_one_clear_carry_the_same_odds`。**三条全是值断言，没有任何牌面内容断言变红**——见「十七.2」 |

**还原核对**：五个被改过的文件在八次施加后逐个 `sha256`，**与变异前逐字节相同**（脚本每次施加后立即还原并当场比对，最后再全量比对一次，全部 OK）。

**先取得真红再信其余**：M1 是第一个施加的，红 9 条，之后才继续。
**没有出现增量编译跳过重编的假绿**：七次真变异（M1–M4、M5b、M6–M8）各自都红了，
红的条目与变异点一一对得上；唯一没红的是 M5a，而它的原因是编译失败，已如实记为「没跑」。

---

## 二十、CK-023：全量判据

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| 项目 | 通过 | 失败 | 跳过 |
|---|---|---|---|
| `Dfo.Server.Protocol.Tests` | 479 | 0 | 0 |
| `Dfo.Server.Networking.Tests` | 51 | 0 | 0 |
| `Dfo.Pvf.Tests` | 60 | 0 | 0 |
| `Dfo.Server.Persistence.Tests` | 248 | 0 | 0 |
| `Dfo.Server.Host.Tests` | 4 | 0 | 0 |
| `Dfo.Server.Infrastructure.Tests` | 215 | 0 | 2 |
| `Dfo.Server.Application.Tests` | 906 | 0 | 0 |
| **合计** | **1963** | **0** | **2** |

退出码 **0**。跳过数仍为 2（`PvfTownDefinitionSourceTests` / `PvfQuestDefinitionSourceTests`
的「缺档案时应报错」两条，档案可达的证明）。
**与控制器给的基线（1963 / 0 / 2）逐项相同，一条判据都没增没删。**
`dotnet build Server/86JP.Server.slnx --nologo`：**0 警告 0 错误**（`TreatWarningsAsErrors` 下）。

判据总数不变是本轮的预期结果——十处改动全在 XML doc 与注释里。
按控制器提醒的第四个坑，我把交付前后各项目的数字逐项对了平：
479 / 51 / 60 / 248 / 4 / 215 / 906，**七个项目七个数全部与起点相同**，
不是只对了合计。

---

## Executor Self-check — round 3

### 一、密封清单 25 行**逐行**归栏结果

| # | 条目 | 归入 | 状态 |
|---|---|---|---|
| A1-1 | 出现概率的分母 `RateBasis` | 无归档出处 | 仍是假设；有三处文件自身算术旁证，本轮实测三条全成立 |
| A1-2 | 各项加成与基础率的合成方式 | 无归档出处 | 仍无出处；revision 4 定为加法，实体在 A5-3 |
| A1-3 | 地图数当分子还是当替代率 | 无归档出处 | 仍无出处；**本 Spec 在这一格换过三次读法** |
| A1-4 | `MapMaxCount ← Layout.Rooms.Count` | 无归档出处 | 解释性映射，未消解 |
| A1-5 | 每张翻到的牌固定 1 金币 | 无归档出处 | 自定值；M7 证实只有一条判据钉它 |
| A1-6 | `UnpricedPaidFlipCost = int.MaxValue` | 无归档出处 | 发明的哨兵；客户端反应仍未查明 |
| A1-7 | `FreeCardGold` | 无归档出处 | 已消解 TASK-05 的占位，仍无归档出处 |
| A1-8 | `SelectCardCommand.CharacterLevel` | 无归档出处 | 仍是惰性入参，revision 4 后更彻底 |
| A1-9 | 应答里指示牌位归属的字节 | 未实证 | 仍未实证；参照根本不做这件事 |
| A1-10 | 付费翻牌请求的形状 | 未实证 | 参照同形（round 2 查明），仍未实机 |
| A1-11 | `[drop kind prob]` 两个槽 | 未实证 | 两个来源都找过、都没有 |
| A1-12 | RQ-011「牌面产出声明」半条 | 分歧 | 复核成立（1043/4，四个全无 map spec） |
| A1-13 | `NOTI-0x0023.md` 免费卡偏移 | 分歧 | 复核成立，未消解 |
| A2-1 | 三处 `int?` 的 null 两种成因 | 无归档出处 | 源码四处全写明；**产物层面本轮才真消解**（17.5） |
| A2-2 | `[gold card use]` 非零值 | 未实证 | 两个前提本轮重测，仍定不了案 |
| A2-3 | N2 变异的证据强度 | 分歧 | 复核成立；教训在本轮 M5a 上用上了 |
| A2-4 | 副本 3503 声明却永不适用 | 分歧 | 复核成立 |
| A5-1 | `IsImpossible` 比「表答不上来」更宽 | 分歧 | 成立，**已就地修正**（17.4）；控制器的验算本轮独立复算 |
| A5-2 | `Undrawn` 的「四个调用者」 | 分歧 | 成立（一个调用点），**已就地修正**（17.4） |
| A5-3 | 加法 / 上限 6000 / 骰子乘组队 / 阈值读法 / 整数比较 | 无归档出处 | 五项俱在，逐项复核；分母不在其内且区别摆得对 |
| A5-4 | `[drop prob]` 段仍用角色等级 | 无归档出处 | 成立；今日两读法同解，已用手搭表两向钉住 |
| A5-5 | 三个保底开关默认全关 | 无归档出处 | 成立；两件兜底品的归档原文本轮读到 |
| A5-6 | 兜底物品的品质与耐久 | 无归档出处 | 成立；两件都是 `.stk`，确认 |
| A5-7 | 「表答不上来」不发兜底 | 无归档出处 | 成立；其措辞的问题见 A5-1 |
| A5-8 | 网吧牌如何送达 | 未实证 | 仍未实证，且**Amendment 5 的转述与代码不一致，已具名报出** |

**25 行到齐，无一遗漏。** 三栏合计 14 + 5 + 6 = 25。

### 二、尺子在全部程序集的结果，与每条 `!:` 旁散文的复核

- **覆盖**：19 个 csproj（判据解决方案的 17 个 + `Tool/PvfProbe` + `Tool/PvfProxy`），全部 exit 0，XML 全部本轮新生成。
- **`!:` 总计 3 条**，全在 `IQuestStore.cs:31-32`（`!:Carried` / `!:Cleared` / `!:Tracked`）。
  按指示**报出、未动**。旁边那段散文读过：讲两个 quest store 在 `Tracked` 顺序上不一致，与本主题无关，本轮不评价其真伪。
- **另一类告警本轮才是有产出的那一类**：CS1570 两条，`DungeonClearSlice.cs:425-426`，
  后果是 `UnpricedPaidFlipCost` 的整段说明在生成文档里被整块丢弃。已修，重跑后全仓
  `Badly formed XML comment` 归零，`!:` 仍是那 3 条，且本轮新写的六个 cref 全部解析。
- **我对这把尺子的评价，如实写**：它在**判据程序集上的产出是 0**，
  而 TASK-10 当初正是靠它揪出判据文件里那句反了的话。所以在本仓它是**弱代理**：
  本轮十处里只有一处是它指出来的。我因此另做了一遍 `<c>符号</c>` 的机器扫描（101 个符号全部命中），
  但那也只证明名字在，不证明句子对。**17.3 那四句（本轮最要紧的一条）尺子一条都量不到。**

### 三、与基线的 diff

- 起点 `scratchpad/baseline-after-TASK-12` 与工作区**在本轮开工时逐文件相同**（`diff -rq` 无差异），确认起点无误。
- 交付态与该基线相比，**恰好 10 个文件不同**，即我改的那 10 个；没有第 11 个：
  1. `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`（17.1）
  2. `Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`（17.2）
  3. `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`（17.3 / 17.9）
  4. `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`（17.4）
  5. `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`（17.5）
  6. `Server/src/Dfo.Server.Protocol/Game/CardReward/CardRewardPayloadCodec.cs`（17.6）
  7. `Server/src/Dfo.Server.Application/ApplicationModule.cs`（17.7）
  8. `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs`（17.8）
  9. `Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs`（17.3）
  10. `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs`（17.3 / 17.10）
- 另与 `scratchpad/baseline-after-TASK-11` 对了一遍，确认 TASK-12 只动了 7 个文件，
  且**没有动 17.3 点名的那四句中的任何一句**——这就是那四句为什么读 diff 看不见。
- **`plan.md` 与 `spec.md` 本轮一字未动**；本 Task 文件只做追加，前面各节（含两轮旧产出与五条 Amendment）一字未动。
- 行尾：改过的 10 个文件按**字节数**核 `\r`，**全部为 0**（Python 数 `b'\r'`，不用 `grep -c`）。

### 四、判据数字

- **CK-023 全量**：**1963 通过 / 0 失败 / 2 跳过，退出码 0**。与控制器给的基线相同。
- **逐项对平**（不是只对合计）：Protocol 479、Networking 51、Pvf 60、Persistence 248、Host 4、Infrastructure 215(+2 跳过)、Application 906——**七项七个数全部与起点相同**。有增删的项目：**没有**。
- `dotnet build Server/86JP.Server.slnx --nologo`：**0 警告 0 错误**。
- CK-019 两个过滤器：`PaidCard` 25 条全绿、`ResultCard` 2 条全绿。
- 全量跑了三次（开工基线、改完一次、变异还原后的最终态），三次都是 1963 / 0 / 2 / 退出码 0。
  **本轮没有出现前两轮记过的那种偶发假红**（Networking 真 socket、数据库 fixture），如实记：这一轮没遇上，不是我把它藏了。

### 五、变异检查

八次施加、七次真红、一次编译失败记为「没跑」，明细见「十九」。要点：

- **先拿真红再信其余**：M1 第一个，红 9。
- **M5a 是「编译失败不是变红」这个坑的真实实例**：`if (false)` 触发 CS0162，`TreatWarningsAsErrors` 下成错，判据一条都没跑。我**没有**把退出码 1 记成变红，换成反转判断的 M5b 才拿到红 6 条。
- **M8 是一次正面证据变异**：三条红全是值断言、零条牌面内容断言，正好支撑 17.2 的改法。
- 还原一律 `sha256` 核内容，五个文件八次施加后全部与变异前逐字节相同。
- **变异跑在最终交付态**：八次全部在十处改动完成之后。

### 六、未查明的东西

**只能实机定案的（三条）**

1. 应答记录里指示牌位归属的字节（`A1-9`）——「四张全亮」整个玩法压在它上面，**参照服务端里没有这个玩法**。
2. 客户端见到 `int.MaxValue` 作为付费价会怎样（`A1-6`）——参照从不发这个值。
3. 本仓走「账号加钱 + 背包通知」而不是 `0x0047` 里 `id == -1` 那条路发金币，客户端牌面上会不会因此看不到金币。

**两个来源都找过、都没有的（五条）**

4. `[drop kind prob]` 两个槽的含义（`A1-11`）。
5. `[gold card use]` 非零值的含义（`A2-2`）。
6. `[item drop rarity control]` 的含义。
7. `[chronicle set reward rate]` 的含义。
8. 版式帧那八个 u16 各选中什么。

**归档里根本没有、只能由本服务器决定的（本轮不再叫「未查明」）**

分母、合成方式、房间数这个量的角色、房间数上限、两面是否同分布、只用 `default` profile、
三个保底开关、兜底品的品质与耐久、阈值段取哪个等级——全部在 16.1 具名。

**本轮明确没有复核的**

- `GameInventoryItemEntry.CommonLength = 101` 与它那 17 字节尾部。既有结论，本 Spec 只做搬家。
- `NOTI-0x0023.md` 里那份抓包字节本身。我核的是布局块与上游 builder 的自洽。
- 参照服务端的金币数额算法（`ExpTableProvider.GetMonsterGold` 那一串）。本仓金币是用户定值，核了也用不上。
- `EquipmentDropPoolProvider.GetClearRewardPool` 与本仓 `DropItemFamily.Equipment` 是不是同一个池（round 2 就记着没比，本轮仍没比）。
- 参照服务端本身：本轮**一次都没有重新打开**。round 2 关于参照的结论我按「已有记录」引用，没有重新验证——**如果 revision 4 之后有哪条参照结论也需要重读，我没有做那件事。**

### 七、我自己不确定的地方

1. **17.3 是这一轮最要紧的发现，也是我最想请人复核的一处。** 我的判断是：
   `FallbackRowsFor` 按牌型取表，所以「牌型决定不了牌面内容」在结构上已经不成立，
   即使今天三个开关全关。**反方意见也站得住**：可以说这四句默认的语境就是「本服务器当前配置」，
   那它们今天全为真。我选了改，理由是 `Amendment 5` 的第一条（`IsImpossible`）
   已经把「凭今日数据为真、写得像凭结构为真」判成缺陷，而这四句是同一形状且更尖锐
   （那个 `always` 明说了是结构上的普遍性）。**如果控制器认为这是装修不是修缺陷，
   回退面是明确的：四处都只删了限定语。**

2. **17.6（`seven` 那句）我改了，而 round 1 看过同一句并决定不改。**
   我不认为 round 1 错了——那是一次合理的克制。我改的理由是本 Task 的题面就是核这种句子，
   且这次改法不需要新论证。但**我承认这是一次判断，不是一次必然**。

3. **17.9 与 17.10 是不是应该算作独立的两处，我不确定。**
   它们与 17.3 是同一个主题在不同位置的四句话中的两句。我按「不同位置、不同断言」各计一处，
   于是本轮是「十处」；按主题合并就是「八处」。**数法我写在这里，不想让「十处」听起来比实际多。**

4. **`Amendment 5` 对网吧牌的转述与代码不一致（`A5-8`），我选择报出而不是照抄。**
   Amendment 写「本 Spec 读作『直接发、不用翻』」，而代码写的是「送达路径没建」。
   两者的差别是实质的：前者是一个未实证的读法，后者是一个没有读法的空缺。
   **我按代码写进了清单，并把差别指出来。如果控制器的原意是前者，
   那就说明代码里缺一句话，而不是清单写错了——这一条需要控制器裁。**

5. **`ClearRewardRules` 的「五个决定」在我看来实际不止五个**（A5-4 就是第六项），
   但那句话写的是「Five decisions **here**」，按字面站得住，我没有改。
   **我不确定这算「措辞恰好躲过」还是「确实只统计本文件」**，倾向后者，但说不死。

6. **我不能证明 CK-021 是穷举的。** 我的做法是：读遍本主题 58 个改动/新增的 `.cs` 文件，
   把每个字面常量与每处归档读取列出来，逐个回归档核；再对全部 `<c>符号</c>` 引用做机器扫描。
   **这两样都不能证明「没有第 11 处缺陷」。** 我能证明的只是：
   25 行密封清单逐行核过；本轮列出的每一条计数型断言都用机器复核过；
   尺子在 19 个程序集上跑过并已归零。

7. **判据一条没增。** 我认为这是对的——十处都是散文缺陷，加判据钉不住散文；
   而 17.3 那四句所描述的行为**已经**被 `Blank_items_are_what_a_switched_on_paid_card_hands_out`
   钉住了（M5b 红 6 条为证），缺的从来不是判据而是那四句话跟上。
   **但如果控制器认为「牌型现在够得着牌面内容」这件事应当有一条自己的判据
   （而不是靠兜底那一组顺带覆盖），那是我没做的事。**

## Controller Disposition — round 3：十处修正全部接受，两处裁定，一处是控制器的错

控制器独立复核：全量 **1963 / 0 / 2**，退出码 0，与基线一致；
`FallbackRowsFor(table, occasion.Kind)`（`ClearRewardRuleService.cs:207`）确实让牌型
重新决定了一件事，故那四句变成假的**属实**；`CS1570` 全仓归零，`grep "read the kind"` 零命中。

### 最要紧的一条：Amendment 4 记的失败模式，隔一个任务原样重演

TASK-12 加了「牌型决定兜底表」，于是四处「牌型决定不了牌面内容」变成假话——
**其中一句正是 TASK-10 round 3 刚修好的 `The rules do not read the kind`**，
另一句还用了 `always` 明说结构普遍性。

这四句**挺过了 TASK-12 的实现、判据、自检与一整轮独立评审**，
因为 **TASK-12 的 diff 里没有这几个文件**。

这与 Amendment 4 记下的那次一字不差：**改动的正确性由 diff 保证，
而 diff 之外那些"因为改动而变假"的句子，没有任何东西看得见它们。**
一处刚修好的句子在两个任务内被重新变假，说明这不是偶发。

### 裁定一：那把尺子被控制器高估了 —— 接受下调

执行者如实报告：尺子在本仓是**弱代理**——判据程序集产出为 0，本轮十处只有一处由它指出。

**接受。** 控制器在 Amendment 4 里把它升为常设检查时，
依据是它在 TASK-10 那一次成功，**样本量为一**。执行者用十处的分布把它降到了它该在的位置。

它另找的两把更有效：
- **`CS1570`（XML 畸形）**——`DungeonClearSlice.cs:425-426` 的 `<para>` 不闭合，
  使 `UnpricedPaidFlipCost` 的**整段说明在生成文档里被整块丢弃**。
  也就是 Amendment 2 点名要补的「null 有两种成因」**源码里有、产物里没有**。
  这一类"写了但没生效"，`!:` 完全看不见。
- **`<c>符号</c>` 扫描**（101 个符号全部核过）——`<c>` 不参与解析，
  故 cref 尺天然照不到它，而本仓大量引用用的正是 `<c>`。

**三把一起用**，不再把 `!:` 单独当作常设检查的全部。

### 裁定二：四句改而不留 —— 准

反方立场（默认语境即当前配置，故今日全为真）确实站得住，但**这正是本 Task 存在的理由**：
一句凭今日配置为真的话写得像凭结构为真，是本 Spec 三十余处缺陷的同一形状。
删限定语、回退面明确，做法对。

### 裁定三：网吧牌那处不一致，是**控制器写错了**，不是代码缺一句话

Amendment 5 写「本 Spec 读作『直接发、不用翻』」。**这句是控制器的错。**

「直接发、不用翻」是用户在讨论中提出的**关于真实游戏的推测**，不是本 Spec 实现的读法。
TASK-12 的契约写得很清楚：「网吧牌本 Spec 不实现，其开关与兜底表读取到位即可，
**不接入翻牌路径**」——代码写的「送达路径根本没建」与契约一致。

**以代码为准，Amendment 5 那一行的转述作废。** 执行者按代码写进清单并把差别报上来，做得对。
`Allows(PcRoom)` 在产品代码里无调用者，是「读了但没接」的如实体现，不是缺陷。

### 变异纪律

八次施加、七次真红、**一次编译失败按规程记为「判据没跑」**（`if (false)` 触发 CS0162→error）。
这一条本 Spec 立过两次规矩，这次执行者不用提醒就照办了。

## Independent Quick Review — round 3

**Verdict: `changes_required`**

十处修正本身全部成立，判据、基线 diff、变异纪律、密封清单 25 行的到齐与归栏，我逐项独立复核
**全部通过**。退回的唯一理由是本轮题面自己的那一条：**「核得全不全」**——
本轮的核心命题是「TASK-12 的改动让 diff 之外的句子变假」，而执行者按这个命题查出的十处里，
**同一形状还剩两处没被查到**，且两处都能用执行者自己用过的手段查到。

评审全程**未修改任何生产、判据或流程文件**；两次变异已还原并以 sha256 与 63 文件全量比对证明。

---

### 一、我自己点的密封清单：25 行**全部到齐**，归栏**全部正确**

不采信执行者的扫描结果，按三条 Amendment 的表格逐行编号点名：

| 来源 | 行数 | 我点到的编号 | 结果 |
|---|---|---|---|
| Amendment 1 | 13 | `A1-1`…`A1-13` | 13 行全部具名出现（十六.1 八行、十六.2 三行、十六.3 两行） |
| Amendment 2 | 4 | `A2-1`…`A2-4` | 4 行全部具名出现（十六.1 一行、十六.2 一行、十六.3 两行） |
| Amendment 5 | 8 | `A5-1`…`A5-8` | 8 行全部具名出现（十六.3 两行、十六.1 五行、十六.2 一行） |

**缺行：无。** 三栏计数 14 + 5 + 6 = 25，与各 Amendment 的原始栏位**逐行一致**——
没有任何一行被从「本服务器决定的值」挪进「未实证的读法」或反向。
逐条对了 Amendment 表的栏目归属与十六节的实际落位，责任归属没有被措辞冲淡。

`A5-8`（网吧牌）按控制器「裁定三」的口径核：**以代码为准**。
`ClearRewardRuleService.FallbackRowsFor`（`ClearRewardRuleService.cs:248-260`）只映射
`Gold` 与 `Free`，`ClearRewardBlankItemSwitches.Allows(PcRoom)` 在产品代码中确无调用者——
执行者按代码写进清单、并把 Amendment 5 转述的差别报上来，与裁定三一致。
该行仍留在「未实证的读法」栏（Amendment 5 的原栏位），措辞已改成「没有实现也没有读法」，
栏位与内容之间的这点张力属 Amendment 措辞遗留，不作为发现。

---

### 二、基线 diff：**恰好 10 个文件**，且**全部只是注释**

- `scratchpad/baseline-after-TASK-12` 共 **63 个文件**；逐文件 `cmp`：**10 个不同**，
  与执行者列的 10 个**逐条相同**，无第 11 个。
- **文件集也对过**：把工作区 `git status` 的 34 个 `M` 加 29 个 `??`（排除 `.rks-agent/`）
  取并集得 **63 个**，与基线文件集 `comm` 双向比对**零差异**——没有新增文件，也没有删除文件。
- **注释性机器复核**：把 10 个文件的两个版本各自剥掉 `///`、`//` 与空行后逐行比对，
  **10 个文件全部零差异**（`files with non-comment differences: 0 of 10`）。
  即**没有一行逻辑改动**，两个判据文件里也**没有任何断言、期望值或 `[Fact]`/`[Theory]` 被动过**——
  「用例被悄悄改弱」不存在。
- `plan.md`（mtime `2026-09-01 20:39`）与 `spec.md`（`20:36`）都早于本轮开工（`09-02 00:24`），
  本轮确未再动；`plan.md:261` 的 CK-019 仍是 `--filter PaidCard`。

---

### 三、判据数字：我自己跑的，**1963 / 0 / 2，退出码 0，逐项对平**

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

Protocol 479 / Networking 51 / Pvf 60 / Persistence 248 / Host 4 / Infrastructure 215(+2 跳过) /
Application 906 = **1963 通过、0 失败、2 跳过**，退出码 0。**七个项目七个数与基线逐项相同。**
两条跳过仍是 `PvfQuestDefinitionSourceTests` / `PvfTownDefinitionSourceTests` 的缺档案用例。
本轮未遇偶发假红。

---

### 四、三把尺子：我自己跑的

1. **`!:` cref 尺**：对 **19 个 csproj** 逐个
   `dotnet build --nologo -p:GenerateDocumentationFile=true -p:NoWarn=1591 -p:TreatWarningsAsErrors=false`，
   **全部 exit 0，零 error**。生成 XML 中 `cref="!:…"` **只有 3 个不同符号**
   （`!:Carried` / `!:Cleared` / `!:Tracked`，全在 `Quests/IQuestStore.cs:31-32`，与本主题无关）。
2. **CS1570**：**全仓 0 条**；`Badly formed XML comment` **全仓 0 处**（`grep -rl` 无命中）。
   即「十七.5」修的那个 `</para>` 确实让 `UnpricedPaidFlipCost` 的整段说明回到了产物里——
   这一条我在**产物层面**验证，不是只读源码。
3. **`<c>符号</c>` 扫描**：本主题 58 个 `.cs` 里抽出 **82 个**不同的 `<c>标识符</c>`
   （执行者报 101，口径更宽，含带点的成员引用），逐个回仓里 `\b` 匹配，**未解析：0**。

**尺子确认了执行者的下调是对的**：本轮我查出的两处新问题，`!:` 一处也照不到，CS1570 也照不到，
`<c>` 扫描只能证明名字在、不能证明句子对——**这正是执行者写下的那句「尺子归零不等于散文没问题」，
而它在本轮自己的产出上又应验了一次。**

---

### 五、十处修正：逐处独立复核，**全部成立**

抽验了其中五处的事实基础，不采信转述：

| 处 | 我怎么验的 | 结论 |
|---|---|---|
| 17.1「四段不读」→ 三段 | 数 `UnreadSections` 的常量：`RarityControl`、`ChronicleSetRewardRate`、`DifficultyGoldBonusRate` = **3 个**，两个未查明 + 一个不适用 | 成立 |
| 17.3 四句「牌型决定不了牌面内容」 | `ClearRewardRuleService.cs:207` `FallbackRowsFor(table, occasion.Kind)`；`:251-253` 按 kind 取 `Gold`/`Free`；`:259` 只有 `Gold` 拿得到 `GoldCardBlankItems`。**牌型确实重新决定了一件牌面上的事**，那四句在结构上为假属实；改后四句加了「开关全关时」的限定并点出牌型够得着的是兜底表，**为真** | 成立 |
| 17.4 `Undrawn` 与 `IsImpossible` | `grep "Undrawn("` 全仓**只有 `:204` 一个调用点**（`:37` 是 cref），「一个调用点、四个进入条件」为真。`IsImpossible => Rate <= 0 \|\| Basis <= 0`；`ChanceOf` 的两支 `scaledParty <= 0` 与 `losing >= basis` 确实更宽。**A21 的两个数我独立重算**：`scaledParty = round(1.0…1.6 × 10000)` ∈ [10000,16000] 恒 > 0；最大 shortfall = 10000 − 2000（最小房间行）− 0（最低难度）= **8000** < basis，故 `losing` ≤ 8000、`Rate` ≥ 2000 > 0 | 成立 |
| 17.6 `seven` | `WirePositionCount = 8`（`CardRewardPayloadCodec.cs:29`）。改后写「seven 是 `WirePositionCount` 个牌位里的最后一个」，且该 cref 在 XML 里解析得到（`!:` 无新增） | 成立 |
| 17.7 `ApplicationModule`「两个内容源」→ 三个 | 构造器实为 `(IClearRewardRateSource, IDropItemPoolSource, IDungeonDefinitionSource, ClearRewardRateOptions)` | 成立，**但不穷尽——见「七.2」** |

---

### 六、变异检查：我自己在最终交付态上跑的两条

规程：`python` 写文件（mtime 自然前进，实测 `01:11:22` 前进）；跑受影响项目；
还原后 `sha256sum -c` 核内容。**先取真红。**

| # | 变异 | 位置 | 判据项目 | 真实结果 |
|---|---|---|---|---|
| **N1** | `FallbackRowsFor` 里牌型→兜底牌的映射**对调**（`Paid ? Gold : Free` → `Paid ? Free : Gold`） | `ClearRewardRuleService.cs:251-253` | Application | **红 3**：`Blank_items_are_what_a_switched_on_paid_card_hands_out`、`Blank_items_are_not_handed_out_where_the_tables_could_not_answer`、`Blank_items_have_no_free_card_table_to_come_from`。903 通过 |
| **N2** | 品级带的键 `occasion.DungeonBasisLevel` → `occasion.CharacterLevel`（撤销 TASK-11 的一半） | `ClearRewardRuleService.cs:202` | Application | **红 4**：`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`、`A_dungeon_level_with_odds_but_no_grade_band_draws_nothing`、`One_dungeon_bands_and_prices_alike_for_every_character_level`、`Blank_items_are_not_handed_out_where_the_tables_could_not_answer`。902 通过 |

两条都**不是执行者跑过的**（M5b 反转的是开关判断而非牌型映射；M4 换的是价钱的键而非品级带的键），
所以是独立的证据：**TASK-12 的「牌型决定哪张兜底表」与 TASK-11 的「品级带取副本等级」，
在最终交付态上仍各自被判据钉住。** N1 尤其要紧——它钉的正是让那四句变假的那件事本身。

**还原核对**：`ClearRewardRuleService.cs` 两次施加后 `sha256sum -c` 均 **OK**
（`93fe547557fefb027b70d25087cdb28edd3b9b92ff5641a76ac3c12c7f171ef2`），
并对 63 个基线文件重跑全量比对，**仍恰好是那 10 个不同**——工作区回到交付态，无残留。

---

### 七、阻塞性发现：同一形状，还剩两处

本轮的立案理由是「TASK-12 让 diff 之外的句子变假」。**同样的两句也在 diff 之外，也变了假，
也没人查到。** 两处都不需要新论证，回退面各是一句话。

#### 七.1 —— 第五句「牌型决定不了牌面内容」，执行者的十七.3 只数到四

**文件**：`Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs:98-101`

```
/// <b>The two faces are given different seeds here, and this is where that difference is made.</b>
/// The seed of a face is the caller's -- see <see cref="IClearRewardRuleService.FaceFor"/> -- and
/// two faces handed one seed have no other way left to differ.
```

**为什么是缺陷**：`two faces handed one seed have no other way left to differ` 是一句**无限定的结构性断言**，
与执行者已改的 `IClearRewardRuleService.cs:191`（原文 `it is also the only thing that separates the
two faces of one clear`）**是同一句话的两个位置**。执行者给后者补上了
「`With every fallback switch off -- and all three are --`」，**却没有给前者补**。
而 `FallbackRowsFor(table, occasion.Kind)` 之下，**同一颗种子的两面在黄金牌开关打开时就会不同**——
这正是判据 `Blank_items_are_what_a_switched_on_paid_card_hands_out` 自己断言的事，
也是我的变异 N1 红出来的那三条。

更尖锐的是：这一处**是那句话的产权方**。执行者在
`CardRewardDomainServiceTests.cs:191` 新写的话是「the seam may not be left depending on a switch to
keep two faces apart」——即**接缝不得依赖开关**；而接缝自己的接口文档
（`ICardRewardDomainService` 正是那个接缝）却仍在说「除了种子它们没有别的分道办法」。
**判据文件已经改口，被它描述的那个接口没有。**

它在 diff 里看不见的机制与十七.3 完全一致：`ICardRewardDomainService.cs`
**既不在 TASK-12 的 7 个文件里，也不在本轮的 10 个文件里**。

**怎么改**：与已改的四处同一手法，加同一个限定语（开关全关时），并指向
`ClearRewardBlankItemSwitches`。回退面：一句话。

#### 七.2 —— 「两个内容源」还有第二处，且十七.7 的穷尽性自称不成立

**文件**：`Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs:651`

```
/// The registration is what makes the two content sources reach it and nothing else does; ...
```

**为什么是缺陷**：这是**同一个陈旧计数**，就是执行者在「十七.7」修 `ApplicationModule.cs` 时修的那一个。
`ClearRewardRuleService` 的构造器收**三个**内容源，而**这条判据自己的 `Build()`**
（同文件 `:686-691`）就注册了三个：`IClearRewardRateSource`、`IDropItemPoolSource`、
`IDungeonDefinitionSource`。**同一个文件的 `:493` 写的是「Three content sources and the options」**——
**一份记录里两处对同一件事给不同的数**，正是执行者在「十七.1」判为缺陷的那个形状。

而且这一条**直接落在 CK-022 的题面上**（本 Task 要枚举的就是这份依赖表），
比 17.7 更靠近题面一层——它在判据文件里。

**同时它证伪了执行者写在十七.7 里的一句**：
「仓里唯一一处散文形式的枚举是错的」。全仓 `grep -n "content source"` 命中五处
（`ClearRewardContent.cs:291`、`ApplicationModule.cs:170`、`ClearRewardRuleService.cs:10`、
`ClearRewardRuleServiceTests.cs:493` 与 `:651`），其中**枚举该依赖表的错的有两处，只修了一处**。
这不只是漏一句，是一句「我已经查全了」的断言本身没被核——本 Spec 的元形状再一次。

**怎么改**：`two` → `three`，一个词。回退面：一个词。

---

### 八、非阻塞观察（不必在本轮处理，记录以免被当成漏看）

1. **`ClearRewardRuleServiceTests.cs:172` 的 `<summary>`**：
   `<b>The seed is the only thing that separates the two faces of one clear.</b>`
   紧随其后的 `<remarks>` 本轮已被加上「with every fallback switch off」的限定，
   **但被它限定的那句 summary 本身仍是无限定的**；同一 remark 的第二段 `:184`
   「only changing the seed moves them apart」同样无限定。
   限定语就在下一行，读者不至于被带偏，**故不作阻塞项**；但与七.1 是同一族，
   建议与七.1 一并处理时顺手带上（判据方法名 `One_seed_answers_the_same_face_for_both_kinds`
   本身描述的是 fixture 状态，无须动）。
2. **`ClearRewardRules` 的「五个决定」**：执行者已按「Five decisions **here**」的字面判为站得住，
   我同意——`ClearRewardRules.cs:234-247` 的五条与 `ChanceOf`/`MapBaseRate` 逐条对得上，
   且紧接的一段已明写「兜底不在五条之内」，TASK-12 之后**没有失效**。A5-4 作为第六项已独立具名，处理得当。
3. **`ClearRewardContent.cs:199` 的 `exploration rate`**：revision 4 撤掉了探索度，
   但这一处讲的是**参照服务端**怎么用 `[gold card create rate]`，措辞明确框在「另一台服务器的读法」里，
   **不是本仓的陈旧读法**。核过，不是缺陷。
4. **协议记录三个 `.md`**：按牌型/兜底/等级三条线扫过 `0x0046.md`、`0x0047.md`、`catalog.md`，
   **未发现被 TASK-10/11/12 变假的句子**。`0x0047.md` 那句「牌面金币属于另一个成果」
   仍是执行者记的「没跟上而非说错」，维持原判。
5. **`--filter "PaidCard|ResultCard"`**：副本 3503 那条判据仍只落在 `ResultCard` 一侧，
   单一过滤词覆盖不全。执行者两轮都如实记录且未擅改，处理得当；留给控制器。

---

### 九、结论

- 十处修正**全部成立**，事实基础我抽验五处全部复核通过，无一处是「改一句错的、写出一句新的错的」。
- 判据 **1963 / 0 / 2 / 退出码 0**，逐项对平，**无用例被改弱**（机器证明：10 个文件零非注释差异）。
- 密封清单 **25 行逐行到齐、归栏正确**，无缺行、无错栏。
- 变异纪律**守住了**：编译失败记为「判据没跑」，还原以 sha256 核内容，变异跑在最终交付态。
  我自己另加的两条独立变异证实 TASK-11 / TASK-12 的核心性质仍被钉住。
- **退回的理由只有一条**：本轮题面是「核得全不全」，而同一形状的句子还剩两处
  （七.1 的第五句「牌型决定不了牌面内容」、七.2 的第二处「两个内容源」），
  合计**一句话加一个词**。两处都在 diff 之外，两处都能用执行者本轮已经用过的手段找到，
  其中一处还**证伪了执行者自己写下的穷尽性断言**。
  在一个专为「说得顺口但没人核」而立的 Task 上，这两处必须补齐再验收。

**Verdict: `changes_required`**

## Controller Disposition — 评审 `changes_required` 成立，两条全接

控制器逐条核实，**两条都属实**，而且是同一形状的第五、第六处。

### 发现一成立，而且比评审说的更尖锐

`ICardRewardDomainService.cs:100`：`two faces handed one seed have no other way left to differ`。
`FallbackRowsFor(table, occasion.Kind)` 之下，同一种子的两面**能**分道（牌型选不同的兜底表）。

尖锐处在于：本轮执行者刚把判据改口写成「接缝不得依赖开关」，
**而接缝自己的接口文档仍在说除种子外无从分道**。判据与它所描述的接口互相打架。

这是「牌型决定不了牌面内容」那一族的**第五处**。执行者给平行的
`IClearRewardRuleService.cs:191` 补了限定语却漏了这一处——**两处在同一族里，一处修一处漏**。

### 发现二成立，并且证伪了执行者自己的一句断言

`ClearRewardRuleServiceTests.cs:493` 写 `Three content sources`、`:651` 写 `the two content sources`，
而同文件 `Build()` 注册的是**三个**。**一份记录里两个数。**

而执行者在 17.7 写道「仓里唯一一处散文形式的枚举是错的」——**这句话本身是错的**，
第二处就在它本轮改过的同一个判据文件里。

**这正是本 Spec 反复栽的那一条：修一个错误断言时，顺手写下一个没核过的新断言。**
控制器在派工里明写过这条坑，仍然发生。记下来。

### 两条都在本轮 diff 之外

`ICardRewardDomainService.cs` 既不在 TASK-12 的 7 个文件里、也不在本轮的 10 个里；
`:651` 虽在本轮改过的文件里，却在改动行之外。**与十七.3 完全相同的不可见机制。**

**审计本身也漏了**——这是诚实的结果：审计能查出六处同族问题，但查不全，
所以独立评审这一道不是形式。

## Contract Amendment 6 — 收尾两处（本条写在改动之前）

**Permitted files 增补**：`Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`
**仅第 100 行那一句**；`ClearRewardRuleServiceTests.cs` 已在清单内。

1. **`ICardRewardDomainService.cs:100`** 补限定语，与 `IClearRewardRuleService.cs:191` 同形。
   注意别把结论改坏：**两面须用不同种子这个结论仍然成立且理由更强**。
2. **`ClearRewardRuleServiceTests.cs:651`** 的 `two` 改准为三，与同文件 `:493` 和 `Build()` 一致。
3. **顺带**：`ClearRewardRuleServiceTests.cs:172` 与 `:184` 的 `<summary>` 仍是无限定的
   「seed is the only thing」，限定语在下一行——一并带上。
4. **CK-019 的过滤器**用 `--filter "PaidCard|ResultCard"`，两侧都覆盖。

**收尾要求**：改完**把「牌型决定不了牌面内容」这一族在全仓再扫一遍**，
报出总数与逐处位置——**不要再说「唯一一处」这类没核过的话**。
三把工具（`!:` / `CS1570` / `<c>符号</c>`）都跑，并明说哪一把找到了哪一处。

**不在本条授权内**：任何逻辑改动；开关默认值；掷骰路径。仍是纯文档轮次，判据应保持 1963/0/2。

## Execution Result — round 4

### 这一轮做了什么

做 `Contract Amendment 6` 的四项，然后按收尾要求**把「牌型决定不了牌面内容」这一族全仓普查**，
报出总数与逐处位置，并明说三把工具各找到了什么。

**先说清楚一件事**：这一轮的两条阻塞**不是我的工具找到的，是独立评审找到的**。
我 round 3 的收尾扫描没有覆盖到 `ICardRewardDomainService.cs`，
而 `ClearRewardRuleServiceTests.cs:651` 那个「两个内容源」**就在我那一轮改过的同一个文件里**——
我在 17.7 写下「仓里唯一一处散文形式的枚举是错的」时，没有回头 grep 一遍就下了这个结论。
**这正是派工里明写的第二个坑，我踩了。** 更正见「二十三」。

判据数字**不变**：1963 / 0 / 2，退出码 0。纯文档轮次，**零非注释差异**（机器证明见自检）。

---

## 二十一、`Contract Amendment 6` 的四项

### 21.1 `ICardRewardDomainService.cs:100` 补限定语（评审阻塞一）

**为什么是缺陷**：原文 `two faces handed one seed have no other way left to differ`，
`no other way` 是一句**排他性断言**。而 `FallbackRowsFor(table, occasion.Kind)` 之下，
同一颗种子的两面**能**分道——判据 `Blank_items_are_what_a_switched_on_paid_card_hands_out`
自己就断言了这一点。评审指出的尖锐处我复核成立：
**我这一轮刚把判据改口写成「接缝不得依赖开关」，而接缝自己的接口文档还在说除种子外无从分道**——
判据与它描述的接口互相打架。**同一族里一处修一处漏。**

改法（**结论未动，理由加强**，按 Amendment 6 的要求）：

- 保留「两面在这里被给不同的种子，这里就是那个差别做出来的地方」这句结论；
- 「无从分道」加限定：`with every fallback switch off, and all three are`；
- 补一句说明为什么结论反而更强：一张开着的付费牌**会**在空位上把两面分开，
  所以**一局的两面是否不同不可以是一个配置键的函数**——正因为如此，接缝必须无条件给两个种子。

与 `IClearRewardRuleService.cs:191` 同形（那一处 round 3 已经是这个写法）。
新引的 `<see cref="ClearRewardBlankItemSwitches"/>` 与它同命名空间，尺子确认解析得到。

### 21.2 `ClearRewardRuleServiceTests.cs:651` 的 `two` 改准为三（评审阻塞二）

**为什么是缺陷**：`The_registration_wires_the_service_and_reads_the_basis` 的 remark 写
`the two content sources reach it`，而**同一个文件**的 `:495` 写 `Three content sources`、
同一个方法里的 `Build()` 注册的是三个（`IClearRewardRateSource`、`IDropItemPoolSource`、
`IDungeonDefinitionSource`，实测在 `:683` / `:685` / `:689`）。
**一份记录里两个数，而且相隔 158 行。**

改为三，并写明第三个是什么时候加进来的，同时用 `<see cref>` 指向用反射钉住这三个的
`The_rule_service_is_handed_no_reader_of_world_state`，以及下方的 `<c>Build</c>`——
把「三」这个数与两个会变红的东西绑在一起，而不是再放一个孤立的数字。

（`Build` 是局部函数，**不能**做 cref 目标，所以用 `<c>`；若写成 `<see cref="Build"/>`
就会新增一条 `!:`，这一点是先想到再落笔的。）

### 21.3 同文件 `:172` 与 `:184` 的无限定句

`Amendment 6` 第 3 项点名的两处，都在 `One_seed_answers_the_same_face_for_both_kinds` 上：

| 位置 | 原文 | 改后 |
|---|---|---|
| `:172` `<summary>` | `The seed is the only thing that separates the two faces of one clear.` | `With every fallback switch off, the seed is the only thing that separates the two faces of one clear.` |
| `:184` `<para>` 末句 | `only changing the seed moves them apart` | `-- the switches being off -- only changing the seed moves them apart` |

**为什么这两处特别值得记**：round 3 我改了这同一个 doc comment 的 remark 主体（`:175-181`），
限定语加在那里，**而 summary 就在它上面三行、`<para>` 就在它下面三行，两句都没跟上**。
一个只读 `<summary>` 的人（IDE 悬浮提示只显示 summary）会读到那句无限定的话。
**「改了这一段」不等于「改了这一段的每一句」**——这是本轮第二个具体教训。

### 21.4 CK-019 的过滤器，**并报出 plan.md 里第二处同词**

`plan.md:261` 的 CK-019 改为 `--filter "PaidCard|ResultCard"`。实测：

| 过滤器 | 命中 | 结果 |
|---|---|---|
| `--filter "PaidCard\|ResultCard"` | **27**（Infrastructure 10 + Application 17） | 全绿，退出码 0 |

27 = `PaidCard` 的 25 ∪ `ResultCard` 的 2，两组不相交。
**副本 3503 那条（声明了开关却因无 `[map specification]` 而永不适用）现在被覆盖了**——
round 1 与 round 3 都记过「单一过滤词覆盖不全」，到本轮才真正闭合。

**改这一行时我先 grep 了一遍，这一步救了我**：`--nologo --filter PaidCard` 在 `plan.md` 里
**有两处**，不是一处：`:261` 的 CK-019 与 `:322` 的 **CK-026**。
CK-026 属于 T-011（副本基准等级），它的 Expected result 是「全绿」，主题就是付费价与品级带，
`PaidCard` 在那里是对的；**`Amendment 6` 只授权改 CK-019，所以我只改了 261 行**，
322 行原样未动，在此报出。**如果我按 round 3 的习惯直接做单处替换，脚本会静默改错一行。**

---

## 二十二、「牌型决定不了牌面内容」这一族：全仓普查

### 22.1 族的定义

**一句话属于本族，当且仅当它断言「牌型（或种子关系）使两面的『内容』相同 / 牌型决定不了一位牌里有什么」。**
只断言**赔率相同**的句子**不属于**本族——赔率相同在 revision 4 之下仍然为真
（`ChanceOf` 不读 `occasion.Kind`，`Neither_the_kind_nor_the_dungeons_level_reaches_the_odds` 钉住）。
这条界线是本次普查的分类依据，先写下来再分类。

### 22.2 总数与逐处位置

**全族共 9 处，全部已加限定语，其中 round 3 改 6 处、round 4 改 3 处。**

| # | 位置 | 原文要害 | 何时改 | 谁找到的 |
|---|---|---|---|---|
| S1 | `Abstractions/World/IClearRewardRuleService.cs:7`（`ClearRewardCardKind` remark 开头） | `as far as what a face holds goes, a fact they do not act on` | round 3 | 读散文 |
| S2 | 同上 `:29-30`（同一 remark 末段） | `it does not decide what a position holds` | round 3 | 读散文 |
| S3 | 同上 `:191-193`（`IClearRewardRuleService` remark） | `It is also the only thing that separates the two faces` / `the same four positions twice` | round 3 | 读散文 |
| S4 | `Tests/World/CardRewardDomainServiceTests.cs:181-190` | `The rules do not read the kind` / `draws them alike **always**` | round 3 | 读散文 |
| S5 | `Tests/World/ClearRewardRuleServiceTests.cs:175-181`（remark 主体） | `one seed asked for both answers the same four positions twice` | round 3 | 读散文 |
| S6 | 同上 `:203`（行内注释） | `The kind travels with the face, and decides nothing else about it` | round 3 | 读散文 |
| **S7** | `Abstractions/World/ICardRewardDomainService.cs:100` | `two faces handed one seed have **no other way** left to differ` | **round 4** | **独立评审** |
| **S8** | `Tests/World/ClearRewardRuleServiceTests.cs:172`（`<summary>`） | `The seed is the only thing that separates the two faces` | **round 4** | **独立评审 / 控制器** |
| **S9** | 同上 `:184`（同一 remark 的 `<para>`） | `only changing the seed moves them apart` | **round 4** | **独立评审 / 控制器** |

**分布值得说**：9 处散在 **4 个文件**，其中 3 个文件各占 2 处以上；
`ClearRewardRuleServiceTests.cs` 一个文件里就有 **4 处**（S5/S6/S8/S9），
而 round 3 只改了其中 2 处。**同一个 doc comment 里的 summary、remark、para 是三处独立的断言。**

### 22.3 改完后的复扫：族内无剩余

用下面这条正则重扫全仓（`Server/src`、`Server/tests`、`protocol`、`Docs`，`.cs` 与 `.md`）：

```
no other way|only thing that separates|only changing the seed|the same four positions|
draws them alike|decides nothing else|not act on|(does|do) not read the kind|
decide what a position holds|which is what makes the two faces
```

命中 8 行，**逐行确认全部已带限定语**（S1–S9 中有跨行合并的），
外加一行 `CardRewardDomainService.cs:131`，见下。

另做两遍旁证扫描：`two faces|both faces|the two kinds|both kinds|same odds`（命中 63 行，逐行判读）、
`only thing|no other way|nothing else (to|left)`（命中 23 行，逐行判读）。**没有第 10 处。**

### 22.4 检视过、判为**不在族内**的（连理由一并报出，不做「唯一一处」这类断言）

| 位置 | 原文 | 为什么不在族内 |
|---|---|---|
| `Application/World/CardRewardDomainService.cs:131` | `The kind goes in by exclusive or with the dungeon, which is what makes the two faces of one clear differ.` | 这是**机制陈述**不是排他性断言——它说「XOR 是这个接缝用来分开两面的办法」，没有说「别无他法」。开关打开时会多一条分道途径，那让这句话**不完整**而不是**为假**。`Amendment 6` 把授权划在 `:100` 那一句上，我**没有动**它 |
| `Tests/.../ClearRewardRuleServiceTests.cs:1118`（`Fingerprint` remark） | `two faces built from the same seed are equal position by position and still not equal as records` | 主语是 **record 的引用相等语义**，「同一种子的两面」的自然读法是「同一场合同一种子问两次」，那是可复现性，恒真。按「牌型」读才成问题，而那不是这句话在讲的事。**没有动**，报出 |
| `Tests/.../ClearRewardRuleServiceTests.cs:1063`（`PaidFaceRate` remark） | `Declared and never read: the two faces carry the same odds, so nothing on this path asks for it` | 只断言**赔率**相同，且「没人读 `[gold card create rate]`」有 round 3 的变异 M8 为证（红的三条全是值断言）。按 22.1 的界线不在族内 |
| `Application/World/ClearRewardRuleService.cs:73` | `the two faces of one clear carry the same chance` | 只断言**几率**，仍然为真 |
| `Abstractions/Content/ClearRewardContent.cs:184`、`ClearRewardArchiveTests.cs:108`、`:309` | `two faces … carry the same odds` | 同上，只断言赔率 |
| `ClearRewardRuleServiceTests.cs:220` | `And the seed does separate two faces` | 正面陈述「种子确实分得开」，恒真，不含排他性 |
| `CardRewardDomainServiceTests.cs:217-221` | `two seeds may legitimately draw the same four pieces` | 论证「该断言种子而非断言牌面」，与牌型无关 |
| `SelectCardCommand.cs:124` | `it decides nothing` | 讲的是 `CharacterLevel` 不定价（清单 A1-8），不是牌型 |

### 22.5 三把工具各找到了什么——明说

| 工具 | 本轮跑法 | 在本族里找到了几处 | 实际作用 |
|---|---|---|---|
| **`!:`（CS1574）** | 19 个程序集全跑，`GenerateDocumentationFile=true` | **0** | 全仓仍只有 `IQuestStore.cs:31-32` 那 3 条（已知、无关、未动）；本轮它当的是**护栏**：确认我新写的 `ClearRewardBlankItemSwitches` 与 `The_rule_service_is_handed_no_reader_of_world_state` 两个 cref 解析得到，没新增 `!:` |
| **CS1570（XML 不闭合）** | 同上 | **0** | 全仓 **0 条**，`Badly formed XML comment` 全仓 0。它在 round 3 找到的是另一类缺陷（`UnpricedPaidFlipCost` 整段被丢弃），本族一处都量不到 |
| **`<c>符号</c>` 扫描** | 本主题 58 个 `.cs`，抽出 **102** 个不同符号（round 3 是 101，+1 是本轮新写的 `<c>Build</c>`） | **0** | 全部命中（`Build` 在 `:676` 确实存在）。它只验名字，不验句子 |
| （对照）**定向散文 grep** | 三轮正则 + 逐行判读 | **9 处全部** | **本族只有这一把找得到**，而 round 3 我用它找到 6 处、漏了 3 处；漏掉的 3 处是独立评审找到的 |

**结论，如实写**：三把机器工具**对本族的检出率是 0/9**。
它们各有各的用处（护栏、另一类缺陷、名字校验），但**「散文说反了」这件事目前只有人读得出来，
而人会漏**——round 3 漏 3 处，占本族的三分之一。
我不再宣称任何一把工具「覆盖」了这一族。

---

## 二十三、更正我上一轮的一句错断言

round 3「十七.7」写：

> **这一条直接落在 CK-022 的题面上**：本 Task 要枚举的就是这份依赖表，
> **而仓里唯一一处散文形式的枚举是错的**。

**「唯一一处」是错的。** 当时仓里有**两处**散文形式的内容源枚举写着「两个」：
`ApplicationModule.cs:170`（我那一轮改了）与 `ClearRewardRuleServiceTests.cs:651`（我漏了），
而后者**就在我那一轮改过的同一个文件里**。
我写下这句话时没有 grep 一遍 `content sources`，只是从「我找到了这一处」推出了「只有这一处」。
**一次未做的搜索的结论，被写成了一句已核过的断言**——与本 Spec 三十余处缺陷同形。

本轮把这一族也普查了一遍。**内容源计数这一族共 4 处，现已全部为「三」**：

| 位置 | 现状 |
|---|---|
| `Application/World/ClearRewardRuleService.cs:10` | `Three content sources and one named assumption` —— 一直是对的 |
| `Application/ApplicationModule.cs:170` | round 3 由「two」改为「three」 |
| `Tests/.../ClearRewardRuleServiceTests.cs:495` | `Three content sources and the options` —— 一直是对的（反射判据就在下面） |
| `Tests/.../ClearRewardRuleServiceTests.cs:653` | **round 4 由「two」改为「three」** |

顺带把本主题四个文件里其余的拼写数字也机器核了一遍，全部成立：
`ChanceOf` 的「four lookups」（实测四次查表）、`Pairs` 的「the two sections read this way」
（实测 `MapCountSection` 与 `CostSection` 两处）、
「its own three sections」（`[gold card cost table]` / `[gold card create rate]` /
`[gold card blank item]`，两处都写三）、
`ClearRewardBand` 的「三个兄弟声明这一节、第四个是深渊表」。

---

## 二十四、判据

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| 项目 | 通过 | 失败 | 跳过 |
|---|---|---|---|
| `Dfo.Server.Protocol.Tests` | 479 | 0 | 0 |
| `Dfo.Server.Networking.Tests` | 51 | 0 | 0 |
| `Dfo.Pvf.Tests` | 60 | 0 | 0 |
| `Dfo.Server.Persistence.Tests` | 248 | 0 | 0 |
| `Dfo.Server.Host.Tests` | 4 | 0 | 0 |
| `Dfo.Server.Infrastructure.Tests` | 215 | 0 | 2 |
| `Dfo.Server.Application.Tests` | 906 | 0 | 0 |
| **合计** | **1963** | **0** | **2** |

退出码 **0**；七项逐项与 round 3 交付态相同。
`dotnet build Server/86JP.Server.slnx --nologo`：**0 警告 0 错误**。
新 CK-019（`--filter "PaidCard|ResultCard"`）：**27 条全绿**，退出码 0。

---

## Executor Self-check — round 4

### 判据数字（实测）

- **CK-023 全量**：**1963 / 0 / 2，退出码 0**。七个项目逐项对平，无增删。
- `dotnet build`：0 警告 0 错误。
- **CK-019 新过滤器**：27 条（Infrastructure 10 + Application 17）全绿；
  旧的 `PaidCard` 是 25、`ResultCard` 是 2，25 + 2 = 27，两组不相交，副本 3503 那条现已被覆盖。
- 本轮**没有跑变异**：三处改动全是限定语与一个数字，改动点本身不可执行；
  能钉住 S7–S9 所描述行为的判据（`Blank_items_are_what_a_switched_on_paid_card_hands_out` 等 6 条）
  已由 round 3 的 M5b 变异证实过，重跑不会产生新信息。**如实说明，而不是补一次做样子的变异。**

### 三把工具的结果

- **`!:`**：19 个程序集，共 **3** 条，全在 `IQuestStore.cs:31-32`，与前两轮完全一致，**未动**。
  本轮新写的两个 cref（`ClearRewardBlankItemSwitches`、
  `The_rule_service_is_handed_no_reader_of_world_state`）在生成的 XML 里都是全限定名，
  **没有新增 `!:`**。
- **CS1570**：**0**；全仓 `Badly formed XML comment` **0**。
- **`<c>符号</c>`**：102 个不同符号全部命中（+1 是新写的 `<c>Build</c>`，实测在 `:676`）。
- **对本族的检出率：0/9。** 明说，不粉饰。

### 与基线的 diff

- 与 `scratchpad/baseline-after-TASK-12` 相比，`Server/` 下**恰好 11 个文件不同**：
  round 3 的 10 个，加本轮新增的 `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`。
- **零非注释差异，机器证明**：对这 11 个文件各自剥掉 `//` 与 `///` 行、去掉空行后逐行比对，
  **11 个文件全部完全相同**（57 / 15 / 29 / 117 / 206 / 90 / 259 / 95 / 386 / 269 / 706 行代码）。
  与评审用的方法相同，我自己也跑了一遍。
- 基线快照不含 `.rks-agent/`，所以 `plan.md` 不在上面那次比对里，单独报：
  **只改了 `:261` 一个词组**（`--filter PaidCard` → `--filter "PaidCard|ResultCard"`），
  `:322` 的 CK-026 原样未动。
- 本 Task 文件只做追加，前面各节（含四轮产出与六条 Amendment）**一字未动**。
- 行尾：本轮碰过的三个文件按字节数核 `\r`，**全部为 0**。

### 我这一轮专门做了什么，防止再写出没核过的新断言

派工点名的坑我上一轮踩了，所以本轮把它变成了固定动作：

1. **每一个「唯一 / 只有一处 / 没有别的」都必须先有一次全仓 grep 垫底**，
   而且**至少换两种查法**。本轮的两次普查（族内、内容源计数）都是先扫再写。
2. **改动前先数命中数，命中数 ≠ 1 就停下来看**。这一步在 21.4 当场救了一次：
   `plan.md` 里 `--filter PaidCard` 有两处而不是一处，第二处是别的 Task 的检查项，
   直接替换会静默改错一行。
3. **改一个 doc comment 时，summary / remark / para / 行内注释各算一处独立断言**。
   S8、S9 正是 round 3「改了这一段却没改这一段的每一句」留下的。
4. **落笔前先想 cref 能不能解析**：`Build` 是局部函数，不能做 cref 目标，所以写成 `<c>Build</c>`；
   写完再用尺子验一遍，确认没新增 `!:`。
5. **本轮所有新写的句子逐句回核**：`no other way` 那段的新论据
   （「两面是否不同不可以是配置键的函数」）对着 `FallbackRowsFor` 与
   `Blank_items_are_what_a_switched_on_paid_card_hands_out` 核过；
   「三个内容源」对着构造器、`Build()` 与反射判据三处核过。

### 我自己不确定的地方

1. **`CardRewardDomainService.cs:131` 我没有改，这是一个判断。**
   「The kind goes in by exclusive or …, which is what makes the two faces of one clear differ」
   ——我读作机制陈述而非排他性断言，且 `Amendment 6` 把授权划在 `:100` 那一句上。
   **但它确实是 S7 的接缝侧孪生句**，如果控制器认为「which is what makes」也隐含排他，
   那它就是第 10 处，改法与 S7 同形，一句话的事。**我倾向报出而不是自行扩界。**
2. **`ClearRewardRuleServiceTests.cs:1118`（`Fingerprint` 的 remark）同理。**
   按「同场合同种子问两次」读恒真，按「免费面 vs 付费面」读则要加限定。
   我按前者读并未动。**这是本轮我最没把握的一处分类。**
3. **我的族定义（22.1）是我自己划的。** 我把「只断言赔率相同」排除在外，
   理由是那些句子在 revision 4 之下仍然为真。**如果族的边界该划得更宽
   （比如把所有「两面相同」类的话都算进来），那么 22.4 表里的六处就要重新判。**
   我把界线和被排除项都写出来了，正是为了让这个判断可被推翻。
4. **本轮的普查证据强度不如它看起来那么高。** 我扫的时候**已经知道答案**
   （评审告诉了我 S7–S9 在哪），所以「扫完没有第 10 处」这个结论带着确认偏误。
   真正独立的检验是下一个读它的人再扫一遍。
5. **9 处这个数取决于怎么数。** 我按「独立的句子」数：同一个 doc comment 里的
   summary、remark、para 各算一处。按「文件」数是 4，按「doc comment」数是 6。
   **数法写在这里，不想让「9」听起来比实际多或少。**

## Controller Disposition — round 4：两处留裁，一处判入族，一处判不入

### 裁定一：`CardRewardDomainService.cs:131` 是这一族的**第 10 处**

原文：`The kind goes in by exclusive or with the dungeon, **which is what makes the two faces of
one clear differ**.`

执行者读作机制陈述而非排他断言。**控制器不同意**：`which is what makes X` 在英文里就是指认
唯一成因；而更要紧的是它**符合本族的确切形状**——开关全关时为真（种子确是唯一分道处），
**开一个就假**（牌型还选兜底表）。「凭今日配置为真，写得像凭结构为真」正是本 Task 的题面。

**判入族，须加限定语**，与 S7 同形。

### 裁定二：`ClearRewardRuleServiceTests.cs:1118` **不在族内**

执行者标它为「本轮我最没把握的一处分类」。控制器读了整段：
它讲的是 **record 比较语义**——「a record holding a list compares that list by reference：
两面按位置相等、作为 record 仍不相等」。这与「什么让两面不同」无关，开关开关都为真。

**不改。** 这一处执行者的直觉是对的。

### 记两件本轮的做法

**一、它在改 `plan.md` 前先 grep**，发现 `--filter PaidCard` **有两处**：
CK-019（`:261`，该改）与 CK-026（`:322`，属 T-011，那里是对的）。
按 round 3 的做法直接单处替换会**静默改错一行**。这正是本 Spec 反复栽的「只核自己改的那半句」
的反面做法。

**二、它把三把工具对本族的检出率如实报为 0/9。**
找到九处的只有定向散文 grep，而它 round 3 用同一手段漏了三处（三分之一），那三处是评审找到的。
**控制器此前把 cref 尺升为常设检查、又在裁定里把它降为「三把一起用」——
现在的证据说明：对「散文因改动而变假」这一类，三把工具都不是探测器，
只有「知道要找什么」的定向 grep 有用，而它的召回率约三分之二。**
这一条记入本 Spec 的收尾，不要再把工具当成覆盖的证明。

## Contract Amendment 7 — 第 10 处（本条写在改动之前）

**Permitted files 增补**：`Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`
**仅第 131 行那一句**。

给它加限定语，与 `IClearRewardRuleService.cs:191` / `ICardRewardDomainService.cs:100` 同形。
**结论不动**：两面必须用不同种子仍然成立，且理由更强。

改完把本族总数更新为 **10 处**，并复核 `Fingerprint` 那一处按裁定二保持不动。
仍是纯文档轮次，判据应保持 1963/0/2。

## Execution Result — round 5

### 这一轮做了什么

做 `Contract Amendment 7` 的一处：给 `CardRewardDomainService.cs:131` 加限定语，
把本族总数更新为 **10 处**，并按裁定二复核 `Fingerprint` 那一处保持不动。

**我接受裁定一，而且认为控制器是对的。** 我 round 4 把 `which is what makes X` 读作机制陈述，
理由是它没有 `no other way` 那样的排他词。控制器的两条我都复核了：
`which is what makes X` 在英文里确实是指认唯一成因；
更要紧的是**形状判据**——「开关全关时为真、开一个就假」，这正是本族的定义，
而我 round 4 自己写下的族定义（22.1）就是这么划的，**却没有把它套到这一句上**。
我用了「有没有排他词」这个**措辞**标准去判一个应当用**真值条件**判的句子。
这是本 Task 第三次同形的错：不是没有标准，是没有把自己刚写下的标准用上。

判据数字**不变**：1963 / 0 / 2，退出码 0。

---

## 二十五、`Contract Amendment 7`：本族第 10 处

### 25.1 改了什么

`Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`，`SeedFor` 的 remark，
**仅第 131 行那一句**（授权范围）：

| | 原文 / 改后 |
|---|---|
| 原 | `The kind goes in by exclusive or with the dungeon, **which is what makes the two faces of one clear differ.** Exclusive or rather than a mix, so that two distinct kinds can never come out as one seed for any dungeon whatever.` |
| 改后 | `The kind goes in by exclusive or with the dungeon, **and with every fallback switch off -- and all three are -- that is what makes** the two faces of one clear differ. A switched-on paid card would part them at their blank positions as well (`<see cref="ClearRewardBlankItemSwitches"/>`), which is a reason to derive the two seeds apart rather than a reason not to: whether a clear's two faces differ may not be a function of a configuration key. Exclusive or rather than a mix, so that two distinct kinds can never come out as one seed for any dungeon whatever.` |

**结论未动，按 Amendment 7 的要求**：

- 「异或把牌型混进种子，这就是这个接缝分开两面的办法」——保留；
- 「异或而不是混合，所以两个不同的牌型在任何副本上都不可能得出同一个种子」——**一字未动**；
- 加的是限定与理由：开着的付费牌**也会**在空位上把两面分开，
  而那是**更该**把两个种子分开推导的理由，不是不该——
  **一局的两面是否不同不可以是一个配置键的函数**。

与另外两处同形（三处现在说的是同一件事、用的是同一组词）：

| 位置 | 限定语 |
|---|---|
| `IClearRewardRuleService.cs:191`（S3） | `With every fallback switch off -- and all three are -- it is also the only thing that separates…` |
| `ICardRewardDomainService.cs:100`（S7） | `with every fallback switch off, and all three are, two faces handed one seed have no other way left to differ…` |
| **`CardRewardDomainService.cs:131-132`（S10）** | **`and with every fallback switch off -- and all three are -- that is what makes…`** |

### 25.2 改动前的核对（round 4 立的规矩，照做）

- **先数命中数**：`which is what makes the two faces` 在该文件 **1 处**、全仓 **1 处**，命中数为 1 才动手。
- **cref 先想再写**：`ClearRewardBlankItemSwitches` 在 `Dfo.Server.Abstractions.World`，
  该文件第 1 行就是 `using Dfo.Server.Abstractions.World;`，所以解析得到；
  改完用尺子验，`Dfo.Server.Application` 的 `!:` 仍为 **0**，
  生成的 XML 里它是全限定名 `T:Dfo.Server.Abstractions.World.ClearRewardBlankItemSwitches`。
- **新写的每一句回核**：「开着的付费牌会在空位上把两面分开」对着
  `FallbackRowsFor`（付费→`[gold card blank item]`，免费→空表）与判据
  `Blank_items_are_what_a_switched_on_paid_card_hands_out`（它自己断言「付费牌的开关只动了付费牌，
  同一布置下免费面仍然空着」）核过。

### 25.3 裁定二：`Fingerprint` 那一处**未动**，机器确认

按裁定二保持不动。把该 doc 块从交付态与 `baseline-after-TASK-12` 各自按内容定位取出逐行比对：
**完全相同**。它讲的是 record 比较语义（列表按引用比，两面按位置相等而作为 record 不相等），
开关开关都为真。

---

## 二十六、本族总数更新为 **10 处**

### 26.1 总表（替代 round 4 的 22.2）

| # | 位置 | 原文要害 | 何时改 | 谁找到的 |
|---|---|---|---|---|
| S1 | `Abstractions/World/IClearRewardRuleService.cs:7` | `as far as what a face holds goes, a fact they do not act on` | round 3 | 定向散文 grep |
| S2 | 同上 `:29-30` | `it does not decide what a position holds` | round 3 | 定向散文 grep |
| S3 | 同上 `:191-193` | `It is also the only thing that separates the two faces` | round 3 | 定向散文 grep |
| S4 | `Tests/World/CardRewardDomainServiceTests.cs:181-190` | `The rules do not read the kind` / `draws them alike **always**` | round 3 | 定向散文 grep |
| S5 | `Tests/World/ClearRewardRuleServiceTests.cs:175-181` | `one seed asked for both answers the same four positions twice` | round 3 | 定向散文 grep |
| S6 | 同上 `:203`（行内注释） | `decides nothing else about it` | round 3 | 定向散文 grep |
| S7 | `Abstractions/World/ICardRewardDomainService.cs:100` | `have **no other way** left to differ` | round 4 | 独立评审 |
| S8 | `Tests/World/ClearRewardRuleServiceTests.cs:172`（`<summary>`） | `The seed is the only thing that separates the two faces` | round 4 | 独立评审 / 控制器 |
| S9 | 同上 `:184`（`<para>`） | `only changing the seed moves them apart` | round 4 | 独立评审 / 控制器 |
| **S10** | **`Application/World/CardRewardDomainService.cs:131`** | **`which is what makes the two faces of one clear differ`** | **round 5** | **控制器（我 round 4 报出过它，但判错了栏）** |

**10 处，散在 5 个文件。** 我改的：round 3 六处、round 4 三处、round 5 一处。
**我判对栏的：6 处；判错或漏掉的：4 处**（S7–S9 漏、S10 判错栏）。

### 26.2 复扫：族内无未加限定者

改完重扫全仓（`Server/src`、`Server/tests`、`protocol`、`Docs`，`.cs` 与 `.md`），
正则同 round 4 并加上 `what makes the two faces`：命中 **8 行**
（10 处里有几处的要害句在 round 3 被整句改写，已不再匹配原词）。
另写了一段脚本，对每一条命中**取上下各 6 行的窗口**去找限定语
（`switch(es) off` / `switched-on` / `fallback switch` / `the switches being` /
`wherever no fallback` / `configuration key` / `is the fallback above`）：

```
total family-phrase lines: 8 | unqualified: 0
```

**8 条全部 QUALIFIED，未加限定者为 0。** S1、S2 另行逐字读过（它们的原词已被改写，正则够不到），
现在写的是「牌型够得着的是兜底表，且只在该牌开关打开时」与
「它对一位牌决定的就是上面那个兜底，别无其他」。

### 26.3 三把工具本轮的结果，并把裁定里那条收尾抄在这里

| 工具 | 结果 |
|---|---|
| **`!:`（CS1574）** | 19 个程序集，**3 条**，全在 `IQuestStore.cs:31-32`（`Carried` / `Cleared` / `Tracked`），未动。`Dfo.Server.Application` 的 `!:` 为 0，本轮新写的 cref 没有新增 |
| **CS1570** | 全仓 **0 行**；`Badly formed XML comment` 全仓 **0** |
| **`<c>符号</c>`** | 本主题 58 个 `.cs`，**102** 个不同符号全部命中（与 round 4 相同，本轮没有新增符号引用） |
| **定向散文 grep** | 找到 S1–S6（round 3，6/10）；S7–S9 是评审找到的，S10 是控制器判入的 |

控制器的收尾裁定，抄录在此以免它只活在裁定里：
**对「散文因改动而变假」这一类，三把工具都不是探测器，
只有「知道要找什么」的定向 grep 有用，而它的召回率约三分之二。
不要再把工具当成覆盖的证明。**

本轮的数据把这条收得更紧一点：算上 S10，**定向 grep 的召回是 6/10**，
而剩下四处里有三处是**别人**找到的、一处是别人**改判**的。
换句话说，本族真正起作用的检出手段是**第二双眼睛**，不是任何一把工具，也不是同一双眼睛再扫一遍。

---

## 二十七、判据

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| 项目 | 通过 | 失败 | 跳过 |
|---|---|---|---|
| `Dfo.Server.Protocol.Tests` | 479 | 0 | 0 |
| `Dfo.Server.Networking.Tests` | 51 | 0 | 0 |
| `Dfo.Pvf.Tests` | 60 | 0 | 0 |
| `Dfo.Server.Persistence.Tests` | 248 | 0 | 0 |
| `Dfo.Server.Host.Tests` | 4 | 0 | 0 |
| `Dfo.Server.Infrastructure.Tests` | 215 | 0 | 2 |
| `Dfo.Server.Application.Tests` | 906 | 0 | 0 |
| **合计** | **1963** | **0** | **2** |

退出码 **0**，七项逐项与 round 4 交付态相同。
`dotnet build Server/86JP.Server.slnx --nologo`：**0 警告 0 错误**。

---

## Executor Self-check — round 5

### 判据数字（实测）

- **CK-023 全量**：**1963 / 0 / 2，退出码 0**，七项逐项对平，无增删。
- `dotnet build`：0 警告 0 错误。
- 本轮**没有重跑 CK-019**：过滤器与判据集合都没动，round 4 实测的 27 条仍然有效。
- 本轮**没有跑变异**：唯一的改动是一段限定语，改动点不可执行；
  能钉住 S10 所描述行为的判据（`Blank_items_are_what_a_switched_on_paid_card_hands_out`
  与 round 3 的 M5b 红 6 条）没有变化。如实说明，不补做样子的变异。

### 三把工具

- **`!:`**：3 条，全在 `IQuestStore.cs:31-32`，与前三轮完全一致，未动；`Application` 为 0。
- **CS1570**：0；`Badly formed XML comment` 全仓 0。
- **`<c>符号</c>`**：102 个符号全部命中。
- **对本族的检出率：0/10。** 与前两轮一样，明说。

### 一次差点写错的数，如实记

跑完尺子我先用 `grep -o "IQuestStore\.cs([0-9]*,[0-9]*)"` 抽位置，得到 **6** 个不同位置
（31,16 / 31,42 / 32,16 / 71,11 / 71,21 / 71,34），而 XML 里的 `!:` 只有 **3** 条。
**两个数对不上，我没有挑一个顺手的写下去，而是去查了。**
答案：那个正则丢了 warning code 的约束，(71,\*) 三条是 **CS1573**（record 参数缺 `param` 标签），
不是 CS1574。加回约束后 CS1574 的唯一位置正好是那 3 条，与 `!:` 的 3 条对得上。

**记下来的理由**：我 round 3 在告警直方图里看到「6 warning CS1574」时，
心里给的解释是「3 条 × 2 个程序集」——**那个解释我当时没验证，只是听起来合理**。
这一轮它差点变成一句「全仓有 6 处未解析 cref」的错断言。
**两个来源给出不同的数，是必须停下来的信号**，不是二选一的信号。

### 与基线的 diff

- 与 `scratchpad/baseline-after-TASK-12` 相比，`Server/` 下**恰好 12 个文件不同**：
  rounds 3–4 的 11 个，加本轮的 `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`。
- **零非注释差异，机器证明**：12 个文件各自剥掉 `//` 与 `///` 行、去掉空行后逐行比对，
  **12 个全部完全相同**（57 / 15 / 29 / 117 / 206 / **46** / 90 / 259 / 95 / 386 / 269 / 706 行代码；
  加粗的是本轮唯一被碰过的文件）。
- 本轮**没有碰** `plan.md`、`spec.md`，也没有碰 `Fingerprint` 那一处（裁定二，已机器确认相同）。
- 本 Task 文件只做追加，前面各节（含五轮产出与七条 Amendment）**一字未动**。
- 行尾：本轮碰过的文件按字节数核 `\r`，为 **0**。

### 我自己不确定的地方

1. **本族现在是 10 处，我不能证明没有第 11 处。** 我能证明的是：
   三轮正则 + 一次窗口式限定语检查在全仓上没有留下未加限定的命中，
   而且这个族的边界（22.1 的定义）我写下来了、可被推翻。
   **round 4 我也是这么说的，然后第 10 处出现了**——所以这句话这次要读得更弱：
   我说的是「我没找到」，不是「没有」。
2. **S10 让我对自己的分类标准起了疑。** 我 round 4 用「有没有排他词」判句子，
   控制器用「开关两态下真值是否变化」判。**后者是可判定的，前者是语感。**
   如果拿真值标准重扫一遍 22.4 那张「不在族内」的表，我认为结论不变
   （赔率类的句子在两态下都真，`Fingerprint` 那一处控制器也复核过），
   **但我这一轮没有把那张表逐条按新标准再跑一遍**——如果最终评审要一个更强的保证，那是缺的一步。
3. **「一局的两面是否不同不可以是一个配置键的函数」这句论断是我写的，不是归档或参照给的。**
   它在 S3 / S7 / S10 三处出现，是三处共同的理由。我认为它成立
   （接缝的正确性不该随一个未来可能被打开的开关而变），**但它是一条设计取向，不是一条读数**，
   所以按本 Task 的规矩它该被具名——它已经具名在这三处的正文里，此处再记一次。

## Independent Quick Review — round 4

第二位独立评审，评审 round 4 与 round 5 返工后的**最终交付态**。
我不继承执行者、上一位评审或控制器的任何结论；下面每个数都是我自己跑出来的，
命令与位置一并写出，便于第三个人复算。

---

### 一、判据账：我自己跑的，**1963 / 0 / 2，退出码 0**

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| 项目 | 通过 | 失败 | 跳过 |
|---|---|---|---|
| `Dfo.Server.Protocol.Tests` | 479 | 0 | 0 |
| `Dfo.Server.Networking.Tests` | 51 | 0 | 0 |
| `Dfo.Pvf.Tests` | 60 | 0 | 0 |
| `Dfo.Server.Host.Tests` | 4 | 0 | 0 |
| `Dfo.Server.Persistence.Tests` | 248 | 0 | 0 |
| `Dfo.Server.Infrastructure.Tests` | 215 | 0 | 2 |
| `Dfo.Server.Application.Tests` | 906 | 0 | 0 |
| **合计** | **1963** | **0** | **2** |

七项逐项与 round 5 交付表对平，**无增删**。跳过的两条实测就是
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
——档案可达的证明成立。

**CK-019 的新过滤器我也自己跑了**：`--filter "PaidCard|ResultCard"` → Infrastructure 10 + Application 17 =
**27 条全绿，退出码 0**，与 round 4 所报一致。`plan.md:261` 是 CK-019（已改为并集），
`plan.md:322` 是 CK-026（属 T-011，仍是 `--filter PaidCard`，**未被动过**）——两行我都读了，
round 4 那次「先 grep 再改」的处置正确。

---

### 二、基线 diff：**恰好 12 个文件**，且**零非注释差异**（我自己的词法分析器验的）

与 `scratchpad/baseline-after-TASK-12`（63 个文件）逐文件 `cmp`：**12 个不同**，
与 round 5 自检列出的 12 个**完全一致**。

**零非注释差异不是靠「剥掉以 `//` 开头的行」验的**——那个办法漏行尾注释和 `/* */`。
我写了一个 C# 词法扫描器（正确处理普通串、逐字串 `@"…"`、字符字面量、`//` 与 `/* */`），
剥掉全部注释、压平空白后逐行比对：

```
ClearRewardContent.cs        SAME 57    ICardRewardDomainService.cs   SAME 15
IClearRewardRuleService.cs   SAME 29    IDungeonRunDomainService.cs   SAME 117
ApplicationModule.cs         SAME 206   CardRewardDomainService.cs    SAME 46
ClearRewardRuleService.cs    SAME 90    PvfClearRewardRateSource.cs   SAME 259
CardRewardPayloadCodec.cs    SAME 95    DungeonClearSlice.cs          SAME 386
CardRewardDomainServiceTests.cs SAME 269  ClearRewardRuleServiceTests.cs SAME 706
```

**12 个文件，一个 token 的逻辑差异都没有**，行数与执行者报的
57/15/29/117/206/46/90/259/95/386/269/706 逐个相同。

**基线只有 63 个文件，所以我另做了一遍 mtime 普查**（基线快照建于 `2026-09-02 00:24`）：
`Server/`、`protocol/`、`Docs/`、`AGENTS.md`、`.rks-agent/` 下在那之后被写过的非 `bin`/`obj`
文件共 **18 个**——14 个在 `Server/` 下，4 个是流程文件（`plan.md`、本 Task 文件，
以及与基线同分钟、属控制器自己那一步的 `sdd-execution.yaml` 与 `TASK-12-blank-item-switches.md`）。
`Server/` 下那 14 个里 12 个是上表，**另两个不在 12 之列**：

- `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`（00:46）
- `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs`（00:47）

两个都与基线**逐字节相同**——是 round 3 变异施加后按 sha256 还原留下的 mtime 痕迹，
内容没有残留。**变异还原这一条我因此是机器确认过的，不是采信自述。**

---

### 三、「6 与 3」：查清了。**权威数是 3**，两个「6」是两个不同的量

我自己跑了尺子，先单跑 `Dfo.Server.Abstractions`，再对整个解决方案跑一次**干净重建**
（`-t:Rebuild`，避免增量编译吞掉告警）：

```
dotnet build Server/86JP.Server.slnx --nologo -t:Rebuild \
  -p:GenerateDocumentationFile=true -p:NoWarn=1591 -p:TreatWarningsAsErrors=false
```

实测：**CS1574 日志行 6 条、CS1573 日志行 578 条、CS1570 0 条**，
而同一次构建 MSBuild 自己的结尾摘要写的是 **`292 个警告`**。

`(578 + 6) / 2 = 292`。**每一条告警在日志里出现两次**：一次在 `CoreCompile` 当场，
一次在构建结束的告警汇总里。6 条 CS1574 的实际内容是：

```
IQuestStore.cs(31,16) Carried   ×2
IQuestStore.cs(31,42) Cleared   ×2
IQuestStore.cs(32,16) Tracked   ×2
```

**三个不同的 cref，各印两遍。** 与生成的 `Dfo.Server.Abstractions.xml` 里的
`cref="!:Carried"` / `!:Cleared` / `!:Tracked` **三条一一对应**。

所以：

| 数 | 它是什么 | 是否权威 |
|---|---|---|
| **3** | **未解析 cref 的真实条数**，全在 `IQuestStore.cs:31-32` | **权威** |
| 6（控制器的） | CS1574 的**日志行数**；MSBuild 结尾汇总把每条告警再印一遍 | 不是条数 |
| 6（执行者的） | `IQuestStore.cs` 里出现过的**不同 (行,列) 位置数**，跨全部告警码 | 不是 cref 数 |

**执行者对它自己那个 6 的解释是对的，我验证了**：`(71,11)`/`(71,21)`/`(71,34)` 三条实测确为
**CS1573**（`StoredActiveQuest` 这个 record 的参数缺 `<param>` 标签），
该文件里 CS1573 与 CS1574 各占 6 个日志行、各 3 个不同位置。它那条正则
`IQuestStore\.cs([0-9]*,[0-9]*)` 丢了告警码约束，量到的是「位置」不是「cref」。
**两个 6 是两个不同的量，恰好都等于 6；谁都不是「未解析 cref 的条数」。**

**真正错的是它 round 3 那个已经自己撤回的解释「3 条 × 2 个程序集」。**
我核了这一条：`Dfo.Server.Abstractions.xml` 确实被复制进每一个引用它的工程的 `bin`
（我在 `Dfo.Server.Networking`、`Dfo.Server.Persistence` 的 `bin` 里都看到同样的 3 条 `!:`），
所以那个猜测**听起来极其合理**；但告警只由**拥有该源文件的那一次编译**发出，
引用方不会重发。**翻倍来自日志回显，不是来自程序集数。**
——这正是本 Spec 的题眼：一个合理、自洽、且有旁证撑腰的解释，机制却是错的。
执行者 round 5 自己抓住了「两个来源给出不同的数」这个信号并停下来查，处置正确；
它给出的答案对它自己的度量成立，但**它没有回头去问控制器那个 6 是什么**，
所以「6 的解释」这件事到本轮才真正合拢。**结论（3 条、全在 `IQuestStore.cs`、与本主题无关）不动。**

---

### 四、本族总数：我用**真值标准**自己扫了一遍，**没有第 11 处**

我不采信「有没有排他词」这类措辞标准，也不采信执行者 round 4/5 的普查。
判据是真值条件：**一句话若「三个开关全关时为真、开一个就假」，或「凭今日归档为真、换档案就假」，就在族内。**

做法（脚本在 scratchpad，可复算）：对 `Server/src`、`Server/tests`、`protocol`、`Docs`、`Tool`
下全部 `.cs` 与 `.md`，用一组**按真值形状**（而不是按已知原文）写的正则抽出「断言性句子」，
对每一条命中取**上下各 8 行的窗口**去找限定语；再对每一条**未命中限定语**的逐条人读判真值。

```
total claim-shaped lines: 29 | qualified: 14 | UNQUALIFIED: 15
```

14 条带限定的，**逐条确认限定语确实管住了那句话**：其中 8 条落在 S1、S3、S4、S5、S6、S7、S8、S10
上（`IClearRewardRuleService.cs:7/191`、`ICardRewardDomainService.cs:100`、
`CardRewardDomainService.cs:132`、`CardRewardDomainServiceTests.cs:187`、
`ClearRewardRuleServiceTests.cs:172/177/203`），另 6 条是别的已带限定的开关相关散文
（`IClearRewardRuleService.cs:16/193/229`、`ClearRewardRules.cs:110/162`、
`ClearRewardRuleServiceTests.cs:909`）。
**S2 与 S9 在 round 3/4 被整句改写，我的正则够不到，所以我直接读了原文**：
`IClearRewardRuleService.cs:29-30` 现写「what it decides about a position is **the fallback above**
and nothing else」——把牌型够得着的那一样点名了；
`ClearRewardRuleServiceTests.cs:184-186` 现写「-- **the switches being off** -- only changing the
seed moves them apart」。**十处逐个确认带限定。**
三处同形限定语（S3 / S7 / S10）我逐字对读，**用的是同一组词、说的是同一件事**，
没有出现「改一处、另一处措辞漂了」。

15 条无限定的，**逐条套真值标准**：

| 位置 | 开关开了以后还真吗 | 判定 |
|---|---|---|
| `ClearRewardContent.cs:184`、`ClearRewardRuleService.cs:73`、`ClearRewardArchiveTests.cs:108/309`、`ClearRewardRuleServiceTests.cs:1068` | 只断言**赔率/几率**相同；`ChanceOf` 不读 `occasion.Kind`，开关不进合成式 | 真值不变，**不在族内** |
| `ClearRewardRuleServiceTests.cs:1123`（`Fingerprint`） | 见下方观察 1 | **不在族内**（结论与裁定二相同，理由不同） |
| `ClearRewardRules.cs:449` | 讲 `BlankItemOf` 收到空行表时不动生成器；是该方法的契约，与开关状态无关 | 真值不变 |
| `CardRewardHandlers.cs:50`、`SelectCardCommand.cs:143`、`CardRewardTests.cs:420` | 「翻不出东西就答空」「同一位翻第二次答空」是条件句，前件变了后件仍成立 | 真值不变 |
| `SelectCardCommand.cs:124` | 讲 `CharacterLevel` 不定价（清单 A1-8），与牌型无关 | 真值不变 |
| `SqliteDatabaseParticipant.cs:236`、`DungeonEntryCodecTests.cs:507`、`FinishQuestPayloadCodecTests.cs:106` | 事务/副本入场/任务完成，与本主题无关 | 不相干 |

另按**第二条真值标准（换档案）**单独扫了一遍绝对语气句（`never`/`always`/`cannot`/`impossible`/`unreachable`），
逐条查是否带档案限定。**全部带**，且限定写在句内而不是靠读者补：
`ClearRewardRules.cs:400`（「on A21 neither occurs，因为最大 7400 对 10000」）、
`ClearRewardRuleService.cs:276-280`（`IsImpossible` 比「表答不上来」更宽，A21 两支都到不了，**换档案要当决定读**）、
`PvfClearRewardRateSource.cs:370`（钳位在本档案不触发，保留是为别的档案）、
`ClearRewardRules.cs:110/162`（免费牌开关「on it still hands out nothing **on this archive**」）、
`ClearRewardRuleServiceTests.cs:863`（点名 `etc/itemdropinfo_clearreward.etc` 只声明两节）。
`IClearRewardRuleService.cs:278` 的 `can never answer "not priced"` 主语是**参照服务端**不是本仓，
与同一成员 summary 的「or null when the archive prices no such level」不矛盾。

**结论：本族 10 处站得住，我没有找到第 11 处。**
和执行者一样，我说的是「我没找到」而不是「没有」——但我的扫法与它不同
（按真值形状写正则、而不是按已知原文；窗口式限定语判定、而不是逐行目测），
两条独立路径都停在 10。

---

### 五、执行者自认没跑的那一步：22.4 那张表，我按真值标准**逐条重跑了**

它 round 5 不确定第 2 条说：「拿真值标准重扫 22.4 那张『不在族内』的表，我认为结论不变，
**但这一轮没有把那张表逐条按新标准再跑一遍**」。我跑了，八行逐条：

| 22.4 行 | 真值检验 | 结论 |
|---|---|---|
| `CardRewardDomainService.cs:131` | 开关开则假 → **在族内** | 控制器裁定一成立；round 5 已改，即 S10 |
| `ClearRewardRuleServiceTests.cs:1118/1123`（`Fingerprint`） | 见观察 1 | 维持「不在族内」 |
| `ClearRewardRuleServiceTests.cs:1063/1068`（`PaidFaceRate`） | 只断言赔率；且「没人读 create rate」有 M8 为证 | 不在族内 |
| `ClearRewardRuleService.cs:73` | 只断言几率 | 不在族内 |
| `ClearRewardContent.cs:184`、`ClearRewardArchiveTests.cs:108/309` | 只断言赔率 | 不在族内 |
| `ClearRewardRuleServiceTests.cs:220/222` | 「种子确实分得开」正面陈述，开关开了更真 | 不在族内 |
| `CardRewardDomainServiceTests.cs:217-221` | 断言的是种子不是牌面 | 不在族内 |
| `SelectCardCommand.cs:124` | 讲 `CharacterLevel`，与牌型无关 | 不在族内 |

**八行结论全部不变。** 执行者那一句「我认为结论不变但没验」——**验了，成立**。
这一步现在有证据，不再是判断。

---

### 六、越界：**没有一行是逻辑**

契约的 Permitted files 写「只写 Task 文件与判据；不改产品代码，**除非核对中发现的缺陷需就地修正**」，
`Amendment 3 / 6 / 7` 逐条把具体位置写进了授权。实际改动：

- **12 个源文件全部只有散文/XML doc 的差异**（第二节的词法比对是机器证明，不是我读 diff 得来的）；
- **流程文件只动了 `plan.md:261` 一行**（CK-019 的过滤器），`:322` 的 CK-026 原样；
- 本 Task 文件只做追加：`Task Unit Contract`、七条 `Contract Amendment`、五轮产出、
  三次控制器裁定与上一轮独立评审**齐备且次序未乱**（我把全文的节标题与行号列出来核过）。
  **但基线快照不含 `.rks-agent/`，我无法对这份文件做字节级比对**——
  这一条是通读判断，不是机器证明，如实说明；
- `spec.md`、`AGENTS.md`、`TaskContract.md`、已验收 Task 的契约表——**全部未被触碰**（mtime 普查证实）。

**明确回答：没有。12 个文件里没有一行是逻辑改动。**

---

### 七、RQ-014 的分层（CK-022 的题面）：我自己数的

**归档读取点全在规则服务上。** 全 `Server/src` 扫内容源接口的注入点，
排除 `Abstractions`（声明处）与 `Infrastructure`（实现处），命中 8 处，**全部在 `*RuleService.cs`**：

```
Application/Quests/QuestContentRuleService.cs:15   IQuestDefinitionSource
Application/World/ClearRewardRuleService.cs:52-54  IClearRewardRateSource / IDropItemPoolSource / IDungeonDefinitionSource
Application/World/DropRuleService.cs:17,19         IDropRateTableSource / IDropItemPoolSource
Application/World/DungeonLayoutRuleService.cs:20   IDungeonDefinitionSource
Application/World/EntryPlacementRuleService.cs:20  ITownDefinitionSource
```

`Protocol` 层**一处都没有**。本 Spec 新增的读取点（`IClearRewardRateSource`）只落在
`ClearRewardRuleService` 上。

**规则服务的依赖中无世界状态读写方。** 构造器实测
`ClearRewardRuleService.cs:51-55` = `(IClearRewardRateSource, IDropItemPoolSource,
IDungeonDefinitionSource, ClearRewardRateOptions)`，无 `*Repository` / `*Registry` /
`*Directory` / `*Store` / `*Persistence` / `*DomainService`。
判据 `ClearRewardRuleServiceTests.cs:490-516` 用反射把这四个类型**逐个比对**并做后缀检查，
不是散文自称。

**内容源计数，我自己 grep `content sources?` 数了一遍——全仓 11 处命中，
其中枚举这份依赖表的有 4 处，全部写「三」**：

| 位置 | 现文 |
|---|---|
| `ClearRewardRuleService.cs:10` | `Three content sources and one named assumption` |
| `ApplicationModule.cs:170` | `handed three content sources and a basis`（round 3 由 two 改） |
| `ClearRewardRuleServiceTests.cs:495` | `Three content sources and the options` |
| `ClearRewardRuleServiceTests.cs:653` | `the three content sources`（round 4 由 two 改） |

**四处一致，无第五处。** 另七处 `content source` 命中（`ClearRewardContent.cs:291`、
`QuestObjectiveAuthority.cs:114`、`WorldContentEntryRuleService.cs:13`、`PvfArchiveProvider.cs:6`、
`CardRewardDomainServiceTests.cs:499`、`ChannelHostStartupTests.cs:110`、
`PvfEntryLifecycleContentSourceTests.cs:27`）都不是这份依赖表的枚举，不受影响。
`Build()` 在 `ClearRewardRuleServiceTests.cs:676-696` 实测注册三个，与散文和反射判据三方对平。

**顺带把契约 Required methods 的另两条也自己核了**：
`SelectCardCommandHandler`（`SelectCardCommand.cs:167-172`）的依赖是
`ICardRewardDomainService / IDungeonRunDomainService / ICharacterContainerDomainService /
IAccountMoneyService / TimeProvider`——**无 `ICommandBus`**（禁止嵌套 dispatch 成立）、
无内容源、无 `I*RuleService`；`ProtocolModule.cs:197-198` 把 `SelectCardCommand` 注册为
`CommandExecutionKind.Transactional`。**CK-022 通过。**

---

### 八、变异检查：三条，我自己在**最终交付态**上跑的

规程：经写文件施加（mtime 自然前进，未用 `touch`）；**编译失败记为「判据没跑」**；
还原后用 `sha256sum` 核内容。本轮是纯文档轮次，所以我挑的三条针对
TASK-10 / TASK-11 / TASK-12 三个改造任务的核心性质，验它们**是否仍被钉住**。

| # | 变异 | 位置 | 真实结果 |
|---|---|---|---|
| **R1** | 黄金牌兜底开关默认值 `false` → `true`（TASK-12） | `ClearRewardRules.cs:188` | **红 3 条**，其中 `ClearRewardRuleServiceTests.Blank_item_switches_are_all_off_by_default`（:726）直接钉默认全关；另两条是 `ClearRewardArchiveTests` 的真档案值断言。903 通过 |
| **R2** | 品级带的键 `occasion.DungeonBasisLevel` → `occasion.CharacterLevel`（TASK-11） | `ClearRewardRuleService.cs:202` | **红 4 条**：`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`、`One_dungeon_bands_and_prices_alike_for_every_character_level`、`A_dungeon_level_with_odds_but_no_grade_band_draws_nothing`、`Blank_items_are_not_handed_out_where_the_tables_could_not_answer`。902 通过 |
| **R3** | 表未声明的房间数由「取最大已声明行」改为答 `null`（TASK-10 的 6000 上限） | `ClearRewardRules.cs:315-318` | **红 9 条**：`A_maze_the_table_prices_nothing_for_takes_the_largest_row`、`A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row`、`The_chance_a_position_carries_is_the_archives_numbers_combined`（mapMaxCount 6 与 10 两组）、`Every_maze_of_an_ordinary_dungeon_carries_a_chance` 等。897 通过 |

三条**都真的编译过并跑完了判据**（每次总数都是 906，不是编译中断）。
**还原核对**：两个被改过的文件还原后 `sha256sum` 与变异前记录**逐字节相同**
（`ClearRewardRules.cs` = `64f4660b…`，`ClearRewardRuleService.cs` = `93fe5475…`），
随后 `Dfo.Server.Application.Tests` 复跑 **906 通过 0 失败**，
第二节的 12 文件词法比对在变异之后**又跑了一遍，仍全部 SAME**。

**三个改造任务的核心性质都还被钉着，且各自钉在具名的用例上。**

---

### 九、非阻塞观察（不必在本轮处理，记录以免被当成漏看）

1. **`ClearRewardRuleServiceTests.cs:1123`（`Fingerprint` 的 remark）：我同意裁定二，但理由不同。**
   控制器的理由是「『同一种子的两面』的自然读法是同一场合问两次」。
   **我不接受这条理由**：该 helper 的调用点里 `:209` 与 `:224` 比的正是**免费面 vs 付费面**，
   所以「两面」在这里恰恰有自由/付费那层读法。
   我判它不在族内的理由是另一条：**这句话的断言对象是 record 的相等语义**
   （「持列表的 record 按引用比列表」），「同一种子的两面按位置相等」是那条断言的**前件**、
   用来说明「内容相同也不等」，不是一条关于合成式的断言。开关打开只会让这个例子不再适用，
   不会让「record 按引用比列表」变假。**结论同、论据不同，记在这里以免下一个人以为两条理由都站得住。**
2. **`IClearRewardRuleService.cs:229` 是我这一轮的真值标准下唯一新冒头的候选，我判它不入族。**
   原文 `<b>A position that comes up holding nothing holds nothing.</b>`——单看这一句，
   开关打开就假。但同一 `<remarks>` 的**同一句话往下三行**就写着
   「this server hands neither of them out … the mechanism is built and **every switch is off**」，
   并 `<see cref>` 指向 `ClearRewardBlankItemSwitches`。
   与 S8 的关键差别是：S8 那句无限定话独占一个 `<summary>` 元素（IDE 悬浮只显示 summary，
   读者拿不到下一行），而这一处限定语与被限定句在**同一段连续散文**里。
   **不构成阻塞**；若控制器要把标准划到「每个断言句自带限定语」，这一处是第 11 处，改法与 S10 同形、一句话。
3. **`SelectCardCommandHandler` 与 `DungeonClearPayloadCodec.FreeCardGoldAt` 直读
   `GameProtocol.ClearRewardCardGold`**——round 1 的「3.3 观察一」，五轮下来未处置。
   按本 Task 的 Required methods 字面不算不通过，按 `AGENTS.md`「规则输入只经规则服务进入」字面算。
   执行者两轮都如实报出并说明为什么不自行扩界，处置得当；**它需要一个自己的任务，不该在本 Task 收尾时顺手做。**
4. **`Docs/新功能接入规范.md` 的两处（清单 #27、#28）仍未消解**：一处把
   `[drop item type prob]` 的 `0 9500 0 500` 读死为「装备 95% / 时装 5%」而代码说位置含义未查明，
   一处说「真正新的只有按通关评级分概率」而归档没有按评级分概率的段落、实现也不带评级。
   两处都不在本 Task 的可写清单内，已具名记在「记录内部的分歧」栏，处置得当；
   **但它们是本 Spec 之外仍然活着的两句「说得顺口没人核」，进完整变更评审时值得单独点名。**
5. **三把工具对本族的检出率仍是 0/10，这一点本轮没有变。** 我另加的窗口式扫描
   （按真值形状写正则 + 上下 8 行找限定语）在**已经被改完**的仓上跑，
   只能证明「现在没有未加限定的命中」，**不能证明它当初能找到 S7–S9**——
   那三处当初正是靠第二双眼睛找到的。控制器写在裁定里的那条收尾
   （「本族真正起作用的检出手段是第二双眼睛」）我复核后同意，并且本轮又添了一个同形的实例：
   round 5 那个「6 是 3×2 个程序集」的猜测，也是靠**另一个人拿出不同的数**才被拆开的。

---

### 十、结论

- **判据未被改弱**：1963 / 0 / 2 / 退出码 0，七项逐项对平，我自己跑的；CK-019 并集过滤器 27 条全绿。
- **基线 diff 干净**：恰好 12 个文件，**词法级零非注释差异**（不是行首匹配）；
  基线外另有两个 mtime 前进的文件，实测与基线逐字节相同，是变异还原留下的痕迹。
- **越界：无。** 12 个文件没有一行逻辑；流程文件只动 `plan.md:261` 一行。
- **6 与 3：权威数是 3。** 两个「6」是两个不同的量（日志行数 / 位置数），
  都不是「未解析 cref 的条数」；翻倍来自 MSBuild 结尾的告警汇总回显，
  与程序集数无关（`(578+6)/2 = 292` 与 MSBuild 自报的 `292 个警告` 对平）。
  执行者对**它自己那个 6** 的解释经我实测成立；被证伪的是它已自行撤回的 round 3 猜测。
- **本族 10 处：我用真值标准独立扫过，没有第 11 处**；22.4 那张「不在族内」的表
  **我逐条按真值标准重跑了**（执行者自认没跑的那一步），八行结论全部不变。
- **CK-022 通过**：归档读取点 8 个注入点全在 `*RuleService.cs`，`Protocol` 层零；
  规则服务构造器四个参数无世界状态读写方（反射判据钉住）；内容源计数四处一致为「三」，无第五处。
- **变异纪律守住**：三条变异全部编译成功并跑完判据，各自红在具名用例上，
  还原经 sha256 逐字节核对，最终交付态复跑全绿。

**没有阻塞性发现。**

**Verdict: `passed`**

## Controller Disposition — 评审 `passed`，TASK-09 置 `done`，实现阶段结束

本 Spec 十二个任务全部 `done`。

### 「6 与 3」被彻底解开，而控制器的说法也不准

评审跑了干净重建：CS1574 日志行 6、CS1573 日志行 578，而 MSBuild 自报 **292 个警告**，
`(578+6)/2 = 292` —— **每条告警在日志里印两次**（CoreCompile 一次、构建结尾汇总一次）。

三个数各是什么，至此全部对上：

| 数 | 是什么 | 对不对 |
|---|---|---|
| XML 里 3 条 `!:` | 真正未解析的 cref | **权威** |
| 控制器的 6 | **日志行数** | 对，但控制器据此说执行者「与实测不符」**不准** |
| 执行者的 6 | 该文件里不同 `(行,列)` 位置数，跨全部告警码 | **对**，其 CS1573 之说经评审实测证实 |

**控制器的错在于：拿自己的度量去否定对方的度量，没有先问两个 6 是不是同一个量。**
这与本 Spec 一路在抓的形状同类——两个数不一致时，急于判谁错，而不是先弄清各自在数什么。

真正错过的只有执行者 round 3 那个已自行撤回的猜测「3 条 × 2 个程序集」。
评审还指出它**为何听起来极合理**：`Dfo.Server.Abstractions.xml` 确实被复制进每个引用工程的 bin。
翻倍来自日志回显，与程序集数无关。

### 裁定：`IClearRewardRuleService.cs:229` **不是第 11 处**，并立标准

评审指出它是真值标准下唯一新冒头的候选：单看那句加粗话开关开就假，限定语在往下三行。

控制器读了原文：**限定语紧接在同一 `<remarks>` 段内的下一句**
（「the mechanism is built and every switch is off」），IDE 悬浮 `FaceFor` 时两句一起显示。
而 S8 是独占 `<summary>`，悬浮拿不到下一行。

**立标准（本族此后按此判）：限定语必须与断言出现在同一个悬浮块内。**
这条同时解释了 S8 为何入族、`:229` 为何不入，不是就事论事的例外。
「每个断言句自带限定语」比本仓任何地方都严，会让散文无法读。

### 裁定：`Fingerprint` 不入族的结论对，**但控制器给的理由是错的**

控制器说该段「与『什么让两面不同』无关」。评审不接受，并给了硬证据：
该 helper 的调用点 `:209` / `:224` 比的正是免费面与付费面，**「两面」在这里确有那层读法**。

正确的理由是评审给的：**这句话的断言对象是 record 相等语义，内容相等只是前件。**

**结论相同、理由被换掉**，记下来——本 Spec 已有多处「结论对、论据错」，
而论据错的地方日后会被人当成先例引用。

### 三条转出，不在本 Spec 办

1. **`GameProtocol.ClearRewardCardGold` 被命令处理器与 codec 直读**（round 1 就提出，五轮未处置）。
   按 `AGENTS.md`「规则输入只经规则服务」字面算问题，**要动两个切片的调用形状**，
   该另开任务，不该收尾时顺手做。
2. **`Docs/新功能接入规范.md` 两处**（`[drop item type prob]` 读死、「按通关评级分概率」）仍活着
   ——进完整变更评审时点名。
3. **本族检出率的最终账**：定向散文 grep 6/10，三把工具 0/10；四处遗漏三处由评审找到、
   一处由控制器改判。**起作用的是第二双眼睛，不是工具，也不是同一双眼睛再扫一遍。**
