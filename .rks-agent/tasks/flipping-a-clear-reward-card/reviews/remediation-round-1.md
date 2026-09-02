---
kind: rks-remediation
round: 1
scope: change-review.md 的四条阻塞项（B1…B4）
verdict: 四条全部处置完毕，零逻辑改动
---

# Remediation Round 1：四条阻塞项

判据来源：`reviews/change-review.md`（B1…B4）、`TaskContract.md` revision 4 的九条决定、
`spec.md`。**只改文字，未碰任何 `.cs` 文件。**

改动的四个文件：

| 文件 | 对应 | 净改动 |
|---|---|---|
| `protocol/game/client-to-server/0x0047.md` | B1 | 2 行 → 10 行 |
| `protocol/game/server-to-client/NOTI-0x0023.md` | B2 | 3 行 → 54 行（含新增「服务端实现」表与 240/241 分歧回填） |
| `Docs/新功能接入规范.md` | B3 | 六处 |
| `Docs/DropRates.md` | B4 | 四处 |

`change-review.md` **未被修改**（评审的产物）。

---

## 判据数字

```bash
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet test Server/86JP.Server.slnx --nologo
```

| 程序集 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 51 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 479 | 0 | 0 |
| Dfo.Server.Host.Tests | 4 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 248 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 215 | 0 | 2 |
| Dfo.Server.Application.Tests | 906 | 0 | 0 |
| **合计** | **1963** | **0** | **2** |

退出码 0。**与改动前一致**，这是预期的：本轮一个 `.cs` 都没动。
两条跳过仍是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，
**因为档案可达而跳过**。

`git status --short` 与改前逐行比对：受控修改文件由 35 增至 37
（新增 `Docs/DropRates.md`、`Docs/新功能接入规范.md`），
`protocol/` 由 5 份增至 6 份（新增 `NOTI-0x0023.md`），
`Server/` 下 32 个文件的修改状态一个未变。

## 行尾核查

按**字节数**核 `\r`（`tr -dc '\r' < FILE | wc -c`），不用 `grep -c '\r'`：

| 文件 | `\r` 字节数 |
|---|---:|
| `protocol/game/client-to-server/0x0047.md` | 0 |
| `protocol/game/server-to-client/NOTI-0x0023.md` | 0 |
| `Docs/新功能接入规范.md` | 0 |
| `Docs/DropRates.md` | 0 |

全部纯 LF。

---

## B1：`0x0047.md:109` —— 先核实，再分开处置两半句

### 核实结论：前半句「本仓此处不发金币」**是真的**，保留

`CardRewardPayloadCodec.EncodeSelectCardResponse` / `WriteRecord`
（`Server/src/Dfo.Server.Protocol/Game/CardReward/CardRewardPayloadCodec.cs:196-237`）
每条记录只写 `slot.Item` 的 `(u32 ItemId, u32 Quantity)` 对；
`ClearRewardCardItem` 上根本没有金币字段，**没有任何路径能写出 `id == -1` 的记录**。
长度计算 `if (message.Slots[record].Item is not null) length += ContentPairLength;` 同样只认物品。
所以这一帧确实不发金币。

### 那金币走哪里？——三条都核过

1. **入账，不入帧。** `SelectCardCommandHandler`（`SelectCardCommand.cs:230-232`）：
   `var paid = claimed.Item is null ? 0 : GameProtocol.ClearRewardCardGold;`
   然后 `money.AddAsync(command.AccountId, paid, …)`。
   `AccountMoneyService.AddAsync` → `IAccountCargoStateTable.AddValue32Async`，
   一条 SQL，**不发任何帧**（`Application/Inventory/AccountMoneyService.cs:17-19`）。
2. **不是经 `NOTI 0x000E` 送达的。** `SelectCardHandler`（`CardRewardHandlers.cs:128-139`）
   一次投递里最多两帧：`SelectCardResponse`，以及
   `GameInventoryDelivery.Notification(delivered)`——后者的 `delivered` 是
   `CommittedInventoryUpdateState(container.Type, [InventoryItemState(...)], …)`，
   即**那件装备落在主背包哪一格**。金币在账号 cargo 上，不在容器里，这一帧带不到它。
   （对照：地面拾取的金币走的是另一条路 `GroundGoldPickupNotification`，
   本路径没有它的等价物。）
3. **唯一公布过这份金币的帧，是更早的结算帧。**
   `DungeonClearSlice.cs:379-380` 的 `FreeCardGoldAt`：
   免费牌位 0 有东西时，`NOTI 0x0023` 的免费牌金币位写
   `GameProtocol.ClearRewardCardGold`。**只覆盖免费牌位 0**——
   牌位 1..3 的那七个 seat 写的是 `(物品, 数量)`，`AmountAt` 是物品数量不是金币；
   付费牌面更是一帧都没有。**所以其余三个牌位的那一份金币，没有任何帧公布。**

### 后半句「牌面金币属于另一个成果」确实是假的

`RQ-006`（「玩家每翻到一张有东西的牌，账号金币增加固定的一份」）与
`AC-007` 就在本次交付里，判据是 `A_position_that_held_something_pays_the_cards_gold`。
`GameProtocol.ClearRewardCardGold` 定义在 `GameProtocol.cs:45`，生产读者两个：
`SelectCardCommand.cs:230` 与 `DungeonClearSlice.cs:380`。

### 改法

保留前半句并补上它为什么为真（编码器不写 `id == -1`），
把后半句换成上面三条核实出来的事实——金币入账号、本次交换的两帧都不报它、
免费牌位 0 在结算帧预告过、其余三位无帧公布。
同时按契约约束点明这个 1 是**本服务器自定的占位值而非档案读数**。

---

## B2：`NOTI-0x0023.md` —— 补齐本次改写的那一帧，并回填 240/241 分歧

原文 `123-125` 行：「本仓这一局什么都不发…通关奖励需要经验公式、评级奖励表和翻牌生成器,
这三样都还没有」。逐条核过，**三样现在都有**：

| 原文说没有 | 现在 | 出处 |
|---|---|---|
| 经验公式 | 有 | `DungeonClearExperienceRules.BaseWorth` / `.RankBonus`（`Abstractions/World/`） |
| 评级奖励表 | 有 | `RankSystemInfo.etc` 八档门槛经 `DungeonRankGrades.BandOf` + `ExperienceParameters.ClearRankRates` |
| 翻牌生成器 | 有 | `ClearRewardRuleService` / `CardRewardDomainService`（`Application/World/`） |

而帧本身写什么，逐格核了 `EncodeClearDungeonReward`（`DungeonClearSlice.cs:248-316`）：

- `+0` 写 `award.Base`，`+4` 写 `award.Gained - award.Base`——**是实际入账的那一份，
  不是估价**（`DungeonClearSlice.cs:731-734` 的注释自己写明这一点）；
  会话无技能点镜像或授予未发生时两格写 0（`:630`、`:732`）。
- `160` 免费翻牌位数：`FreeFlipPositions`，有东西 2 否则 1。
- `161` 免费牌物品：`ItemIdAt(cards, 0)`。
- `165` 免费牌金币：`FreeCardGoldAt`。
- `169…223` 七个 seat：标记恒 `(byte)1`，内容是 `ItemIdAt/AmountAt(cards, seat+1)`；
  本仓只有四个牌位，**后四个 seat 恒零**（写进了记录的表里）。
- `232` 付费花费：`NoPaidFlipCost = 0`（这一局没定下牌）或
  `UnpricedPaidFlipCost = int.MaxValue`（定下了牌但档案不给这一级报价，
  或该副本 `[gold card use] 0`）——两者的区分与理由都核对了 `:389-427` 的 remarks。

新增的内容：

1. **240/241 分歧的回填**（评审点名的那条）。本页 `118-121` 行的散文说
   「免费牌那一对在 240 与 245」，而本页自己的布局块把标记位放 240、物品放 241、金币放 245。
   照 `DungeonClearSlice.cs:353-360` 的代码注释原样记进记录，
   并写明**本仓不依赖它**：金币与物品同条件（同一次 `PositionOf` 查找），
   非零金币必然伴随非零物品与标记 2，两种读法下写出的字节相同。
2. **一节「本仓写什么」**，即上表。
3. **一张「服务端实现」表**，形式与本次交付更新的 `0x0046.md` / `0x0047.md` 一致。
   这是必要的：指回本记录当依据的地方共 9 处——生产 3 处
   （`DungeonClearSlice.cs:331`、`349`、`456`）、判据 6 处（`DungeonClearCodecTests.cs`）。
   （评审写的是「生产代码里有八处」并举 `:332/:347/:440`；
   实测生产是 3 处、连判据共 9 处，行号相差一两行。这不影响 B2 的结论，
   记在此处仅为对齐事实，**未改 `change-review.md`**。）

还写进去了两条本次交付已批准、但记录里没有的设计后果：
结算帧在玩家翻牌**之前**就公布了免费牌位 0 的内容（`spec.md` RQ-012 / AC-016 明要，
评审 N8 也点了），以及 `int.MaxValue` **客户端见到会怎么显示未查明**。

---

## B3：`Docs/新功能接入规范.md` 六处

| 行（原） | 原文 | 改成 | 依据 |
|---|---|---|---|
| 200 | 内容源读「…探索度加成」 | `[reward item rate per map max count]`，并明写**不是探索度**：按迷宫房间总数取，与走了几间无关，本服务器不记录探索度；A21 只声明 1–5 房五行 | `spec.md` Non-Goals 末条；`ClearRewardRules.MapBaseRate` remarks（`ClearRewardRules.cs:272-310`）「How far the run got makes no difference here」；BD-022 撤掉 revision 3 的探索度实现 |
| 203 | 「`[result card]` 覆盖声明落在 `IDungeonDefinitionSource` 那一侧」 | **不是翻牌的按副本覆盖，本仓不实现**；key 参照服务端填的是大会已完成轮数（0..4，优胜 5），与本仓八档评级数目对不上；大会走 `0x01CA`/`0x01CB`；四个声明副本无 `[map specification]` 不可达。真正落在那一侧的是 `[gold card use]` → `DungeonDefinition.OffersPaidCard` | TASK-08 §一/§二探针输出与裁定；`DungeonResultCardDeclarationTests`；`PvfDungeonDefinitionSource.cs:206`、`:330`；`ClearRewardRuleService.cs:176` |
| 208 | 「真正新的只有『按通关评级分概率』」 | 真正新的是**几率合成** `ClearRewardRules.ChanceOf`（房间数基础值 + 难度加成 + 骰子×组队增益，与阈值比）；**全局表不按评级分概率**，`[drop prob]` 六个 profile 是按场合命名的，只取 `default`，启用条件未查明；并列出本服务器自定的五条 | `ClearRewardRules` 类注释的五条清单（`:235-247`）；`ClearRewardRuleService` 的 profile 选取 remarks；`spec.md` Assumptions 第 5 条与末条 |
| 212 | facts「（副本 id、角色等级、难度、评级、探索度、队伍人数、显式种子）」 | `ClearRewardOccasion(牌型、角色等级、副本基准等级、难度、迷宫房间数、队伍人数)` + 显式种子；明写**没有副本 id / 评级 / 探索度**，多的是副本基准等级，并写出它堵掉的那个口子；角色等级只用于取 `[drop prob]` 的等级段。构造器依赖同时补全为四项（漏了 `ClearRewardRateOptions`） | `IClearRewardRuleService.cs:180-186` 的 record 定义；`ClearRewardRuleService.cs:51-55` 的构造器；契约 revision 4 决定 6；`ClearRewardRuleServiceTests.The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons` |
| 239 | 「`NetworkMessages.cs` 加**三个**目录成员」 | **两个**（`GameCardSelectRightState`、`GameSelectCard`），两个枚举各加这两个，共四行 | `git diff Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs` 逐行 |
| 256 | 「『奖励跟你选的那张走』还是『永远只有第 0 张有货』：没有证据可断」 | 划掉并记**已定（2026-09-01）：四张各有各的，翻一张全亮出**；并保留原文里为真的那半——协议两种都支持、上游只填牌位 0 是它的简化——再写明这条走的正是「自定并写明是自定」，读法未实机确认且集中在 `ContentOwningPosition` 一处 | 契约 revision 4 决定 9；`CardRewardPayloadCodec.cs:239-264`；M8 变异 |

「`ProtocolModule` 加四类注册行」这半句**没改**：核过 diff，解包器 / 编码器 / 命令 / 消息 handler
四类各有新增行，仍然为真。

---

## B4：`Docs/DropRates.md` 四处

| 行（原） | 原文 | 改成 | 依据 |
|---|---|---|---|
| 263 | 「费率表 `etc/itemdropinfo_clearreward.etc`，**同构**，另加：」 | **不同构**：16 个段标签，7 个与同族共有、9 个独有；共有的 7 个里 `[drop prob]` 形状还不同；由 `IClearRewardRateSource` 整份读，交给 `IDropRateTableSource` 会抛、而且抛的是别的段落的错 | `DropRateTables.ClearReward` XML doc（`DropRateContent.cs:208-215`，本次交付自己改正的）；`ClearRewardTable` remarks（`ClearRewardContent.cs:100-110`）；`PvfClearRewardRateSourceTests.Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else` 逐段列名；`PvfClearRewardRateSource` 类注释关于兄弟读取器如何误报的那段 |
| 271 | `[reward item rate per map max count]` ← **探索度加成** | 迷宫房间总数 → 基础值（A21 只声明 1–5 房五行），并补一段说明为何不是探索度、超表取最大一行是本服务器自定、以及旧读法造成的「三个迷宫整面空白」 | 同 B3 第 200 行；`ClearRewardRules.MapBaseRate` remarks |
| 277 | 「4 个声明 `[result card]`（…+ **按评级分概率**）」 | 「按一个 0..5 的 key 分概率」，另起一段说明这个 key 不是评级、那一段不是普通翻牌的按副本覆盖、本仓不实现，按副本覆盖只落地 `[gold card use]` | TASK-08 §二的三条证据（参照服务端 `CompletedRounds`、`[flavor text]`、八档 vs 六 key）与 §四的可达性 |
| 405 | 「**二期**：翻牌。同一台机器**换一张费率表**」 | **换不成一张费率表**——16 段里 9 段独有、`[drop prob]` 形状不同，要自己的读取器 `IClearRewardRateSource`（已落地）；复用的是掷稀有度 / 品级带 / 桶内加权抽件那几步 | 同 263 行；`ClearRewardRules` 类注释「the three steps after them … are the existing drop parts and are called rather than restated」 |

保留未动、且**核过仍为真**的相邻语句：
第 274-275 行的稀有度门槛与「翻牌出紫是打怪的八倍」；
第 278 行「副本 144 什么都不声明，全走全局表」；
第 491 行「`[drop prob]` … **二期不是「换一张表」那么简单**」（这一条本来就是对的）。

---

## 四份文档通读的结论：**还有别处因本次交付而变假，共 5 处，我一处都没改**

按指示报出而不动（超出本轮授权）。按「同一文件里往往不止一处」排的优先级：

### 高：`Docs/新功能接入规范.md:188-194` —— 一段引文，被引的源码已被本次交付改写

```markdown
更进一步，`DropRateTables.ClearReward` 常量已经声明，它的 XML doc 还写明了唯一的差别：

> 同样的段落，但它的 `[drop prob]` 形状不同：声明的是「一率一具名 profile」而不是
> 「一带五率」，所以翻牌需要自己读那一段，而不是把这个文件换成那个文件。
```

`DropRateContent.cs:208-215` 现在写的是「**Not the same sections**: seven of its sixteen are
the family's and the other nine are declared by no other file of it」。
「同样的段落」「**唯一**的差别」正是本次交付改正掉的说法。
**这是 B4 第 263 行的同族缺陷，只是长在另一份文档里**，而且以引文形式出现——
一个照着它读回源码的人会发现引文对不上。与 B3 一样落在契约 Constraints 点名的那份文档里。

### 高：`Docs/DropRates.md:65` —— 「前五张表**同构**，可以用同一个读取器」

第 47-63 行那张清单的前五张是 monseter / monseter_extra / **monster_hell** / **clearreward** /
object。而 `DropRateContent.cs:115-124` 本次改正为「Three of the family's seven files are read
through this one: monster, monster-extra and breakable object」，
深渊表用 `[dungeon difficulty drop prob]` 取代 `[drop prob]`、翻牌表另有 9 段独有；
`PvfClearRewardRateSourceTests` 里两条判据分别钉住兄弟读取器在**深渊表**与**翻牌表**上都抛。
**改了 263 行而不改 65 行，正是本 Spec 反复栽的那个形状**：同一份文件里的第二处。

### 中：`Docs/DropRates.md:62` —— 半真半假的一行

> 副本 `.dgn` 的 `[result card]` / `[gold card use]` | 翻牌的按副本覆盖（1047 个副本里只有 4 + 5 个声明）

`[gold card use]` 那一半**是真的**且已实现（`DungeonDefinition.OffersPaidCard`）；
`[result card]` 那一半按 TASK-08 是大会玩法的数据，不是翻牌的按副本覆盖。
**按 B1 的教训，这一行不能整删**——真的那半要留住。

### 中：`Docs/新功能接入规范.md:300` —— 基线数字陈旧

> 基线：**1690 通过 / 2 跳过 / 0 失败**。

本次交付后是 **1963 / 2 / 0**。同节对「两条跳过是因为档案在才跳过」的解释仍然为真，
第 296-297 行的命令也与本次判据命令一致，只有那个数字变了。

### 低：`Docs/DropRates.md:390` —— 分层草图里的服务名与落地名不一致

草图写 `ICardRewardService 二期`，落地的是 `ICardRewardDomainService`（业务服务）
加 `IClearRewardRuleService`（规则服务）两个。是一份未来规划的草图，
不构成对服务器行为的错误陈述，只是名字对不上。

### 不属于「因 revision 4 变假」，但通读时看到，一并记下

- `protocol/game/server-to-client/NOTI-0x0023.md:85-86` 仍留着模板句
  「本协议尚无观察记录」，而紧接其后的 `## A21` 一节就引了 2026-08-26 的归档。
  **这是本次交付之前就有的**（两段各自独立生成/追加），不在本轮四条之内。
- 同文件第 89 行「是 `SET_PLAY_RESULT` 的第三帧回答」：升级那一支会多插一帧
  `AvailableQuestListMessage`，此时它是第四帧。核过 diff，帧序列**本次未改动**，
  所以这也是既有的近似说法，不是本次交付造成的。

## 我不确定的地方

1. **B2 里我新增了「服务端实现」表**，这超出「把变假的句子改真」的最小改法。
   我判断它是必要的：本记录被 9 处代码引为依据，而 `0x0045`/`0x0046`/`0x0047`/`NOTI-0x000E`
   四份本次都补了同形的表，唯独这一份没有；但如果评审认为记录只该修正而不该扩写，
   这一节可以整块删掉，不影响 B2 其余部分的正确性。
2. **B3 第 256 行我用了删除线保留原文再标「已定」**，而不是整段替换。
   理由是原文里「协议两种都支持、上游只填牌位 0 是它的简化」这半句仍为真，
   且「要么实机试，要么自己定一条并写明是自定」正是最终采用的路径——
   删掉会丢掉这条决定的由来。但这让「### 还没定的」这个小节标题下只剩一条已定的事，
   标题与内容有点不合；改标题超出授权，没动。
3. **`[result card]` 那个 key 的表述**我按 TASK-08 的证据等级写成
   「档案没说是什么，**参照服务端**把它填成大会已完成轮数」，
   而不是「它就是大会轮数」——TASK-08 §二把这个 key 本身记为「未查明，且不使用」。
   B4 第 277 行同样处理。如果评审认为这条应当写得更硬（旁证有三条：参照服务端、
   `[flavor text]`、六 key vs 八档），可以加强，但我按契约「读不出来的档案字段记为未查明」
   取了保守的写法。
4. **B1 里「其余三个牌位的那一份金币没有任何帧公布」**是我从
   `FreeCardGoldAt` 只覆盖牌位 0、seven seats 写的是数量、以及本路径没有
   `GroundGoldPickupNotification` 等价物三条推出来的。三条各自都核到了代码，
   但「全仓没有第四条路会公布它」这一点我是靠对 `SelectCardHandler` 发出的帧的枚举
   （只有两种）来断的，不是靠一条判据钉住的。
5. Markdown linter（MD060 table-column-style）对我新增的两张表报了告警。
   核过：本仓所有既有表都用同样的 `|---|---|` 紧凑写法，仓库里没有 markdownlint 配置文件，
   同一告警在未改动的行上同样成立。按仓库既有风格保留，未改。
