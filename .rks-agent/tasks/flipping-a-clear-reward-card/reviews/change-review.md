---
kind: rks-review
verdict: approved
---

# Change Review: 通关结算的翻牌奖励

Scope `change`。判据来源：`TaskContract.md`（revision 4）、`spec.md`、`plan.md`、
`AGENTS.md`、`Docs/新功能接入规范.md`。变更集：`git diff` 的 35 个受控文件（+1820 / −221）
与 28 个新增未跟踪文件（排除 `.rks-agent/`）。十二份 Task 的快评作为支持性证据读取，
未作为对完整变更的批准。

**结论：行为、分层、正确性与判据质量全部站得住；SC-1…SC-13 逐条有当前证据。
阻塞项全部是同一族缺陷——「改动本身没错，但改动之外（或改动新写）的某句陈述因此变假」。
四处，全部可 diff 定位，全部只改文字，不动生产代码与判据。**

## 我自己跑过的

| 项 | 命令 | 结果 |
|---|---|---|
| 全量 | `PVF_ARCHIVE_PATH=…/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | **1963 通过 / 0 失败 / 2 跳过**，退出码 0 |
| 跳过的两条 | 读 `PvfQuestDefinitionSourceTests.cs:364-376` 与 `PvfTownDefinitionSourceTests` | 均为 `Skip.If(ContentArchive.Available, …)` 的「缺档案时应报错」用例，**因为档案可达而跳过**，与 SC-10 的声明一致 |

分项：Protocol 479 / Networking 51 / Dfo.Pvf 60 / Persistence 248 / Host 4 /
Application 906 / Infrastructure 215（+2 跳过）。

### 变异检查（九个，全部在最终交付态，mtime 前进，还原后 sha256 逐文件校验通过，无编译失败）

| # | 变异 | 结果 | 钉住的 |
|---|---|---|---|
| M1 | `ClearRewardRules.MapBaseRate` 的 `largestCount = int.MinValue` → `int.MaxValue`（等价于「查不到即概率为零」的旧实现） | **红 9 条**，含 `A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row`（期望 7400、实得 0）与 `Every_maze_of_an_ordinary_dungeon_carries_a_chance`（"Free at 10 rooms carried nothing"） | SC-12 |
| M2 | `GradeBandFor(occasion.DungeonBasisLevel)` → `CharacterLevel` | **红 4 条**，含 `One_dungeon_bands_and_prices_alike_for_every_character_level` | SC-11 |
| M3 | `BandFor(occasion.CharacterLevel)` → `DungeonBasisLevel`（反方向） | **红 2 条**：`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`、`Neither_the_kind_nor_the_dungeons_level_reaches_the_odds` | 「`[drop prob]` 刻意仍用角色等级」确实**两向钉住** |
| M4 | `ClearRewardBlankItemSwitches.GoldCard` 默认改 `true` | **红 3 条**，含 `Blank_item_switches_are_all_off_by_default` | SC-13 默认全关 |
| M5 | `PaidFlipCost(run.Layout.BasisLevel)` → `settlement.CharacterLevel` | **红 7 条**，含 `The_price_is_asked_at_the_dungeons_level_and_not_the_characters` | SC-11 付费价 |
| M6 | `TryClaimClearRewardCard` 取消已认领拒绝 | **红 4 条**，含 `A_position_is_claimed_once` | SC-4 |
| M7 | `SqliteAccountCargoStateTable.TryTakeValue32Async` 的 `amount == 0 ? 0 : null` → `0`（余额不足报成扣款成功） | **红 2 条**：`An_account_short_of_the_price_keeps_every_coin_where_adding_would_empty_it`、`An_account_with_no_purse_can_pay_nothing_and_nothing_only` | SC-8 拒绝路径 |
| M8 | `ContentOwningPosition(index)` → 恒 0（退回上游「只有第 0 张有货」） | **红 6 条**，含 `Each_record_names_the_position_it_describes` 四例 | SC-2 四张全亮出 |
| M9 | `FixClearRewardCards` 的幂等闸 + `SettleAsync` 短路一并去掉 | **红 4 条**，含 `A_second_settlement_keeps_the_cards_already_fixed` | SC-3 重复结算 |

还原校验：七个被动过的生产文件 `sha256sum -c` 全部 `OK`，`git diff --stat -- Server/src`
回到 20 文件 / 768 插入 / 199 删除。

## SC 逐条

| ID | 结论 | 当前证据 |
|---|---|---|
| SC-1 | 有 | `CardRewardTests.The_layout_is_the_body_the_capture_carried`（17B 逐字节）、`A_face_holding_nothing_is_the_length_the_capture_had`（33B）、`0x0045` 的 `[01]` 既有判据；端到端 `A_flipped_piece_is_in_the_bag_afterwards` + `The_answer_comes_first_and_the_bag_change_after_it_in_one_exchange` |
| SC-2 | 有 | `Every_position_is_declared_including_the_ones_holding_nothing`、`Each_record_names_the_position_it_describes(0..3)`；M8 红 |
| SC-3 | 有（重连一支为推论） | `A_second_settlement_keeps_the_cards_already_fixed`、`Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing`、`Both_seeds_are_derived_from_the_run_and_from_nothing_else`；M9 红。见非阻塞 N4 |
| SC-4 | 有 | `A_position_is_claimed_once`、`A_second_flip_of_the_same_position_is_answered_with_nothing`；M6 红 |
| SC-5 | 有 | `The_bag_change_carries_the_slot_the_piece_and_how_many`、`A_position_that_held_something_pays_the_cards_gold` |
| SC-6 | 有 | `ClearRewardRarityLadderTests.Every_Rarity_band_the_archive_declares_is_reachable_including_the_two_rarest`、`The_service_produces_every_Rarity_band_its_ladder_declares` |
| SC-7 | 有 | 逐段 XML doc 出处；`ClearRewardRules` 类注释集中列出五项本服务器自定项并逐处具名；`ClearRewardArchiveTests.The_reading_of_this_file_gives_no_basis_and_nothing_that_combines`；TASK-09 §一/§二的三栏具名清单 |
| SC-8 | 有 | `A_paid_flip_costs_the_archives_price_for_the_dungeons_level`、`An_account_that_cannot_afford_a_paid_flip_keeps_its_money_and_its_card`、`AccountMoneyServiceTests` 三条；M7 红 |
| SC-9 | 有 | `The_rule_service_is_handed_no_reader_of_world_state`（反射枚举四个参数 + 六个后缀禁用）、`The_settlement_service_reaches_the_archive_only_through_the_rules`。我独立复扫：`IClearRewardRateSource` 在 `Server/src` 的注入点只有 `ClearRewardRuleService.cs:52`；`IClearRewardRuleService` 的注入点只有 `CardRewardDomainService.cs:16` |
| SC-10 | 有 | 我自己跑的 1963 / 0 / 2 |
| SC-11 | 有 | `One_dungeon_bands_and_prices_alike_for_every_character_level`、`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`；M2 / M3 / M5 三向红 |
| SC-12 | 有 | `A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row`（副本 144，10 房）；**M1 亲自证明它在「查不到即概率为零」的实现下变红**，这正是 SC-12 要求的那半 |
| SC-13 | 有 | 三种牌各自开关：`Blank_item_switches_are_all_off_by_default`、`Blank_items_are_what_a_switched_on_paid_card_hands_out`、`Blank_items_are_handed_out_by_nothing_while_the_switches_are_off`、`Blank_items_have_no_free_card_table_to_come_from`、`Blank_items_of_the_pcroom_card_are_read_and_switched_and_reach_no_flip`；M4 红 |

## Blocking Findings

四条同族：一句陈述因本次变更而变假。B1 是**本次变更自己新写的**，落在 BD-019 之内；
B2 是 BD-015 改写的那一帧的权威记录；B3 / B4 是契约 Constraints 点名为分层依据的两份文档。
四条都只需改文字。

### B1（阻塞）本次变更新写的协议记录里有一句当场即假的话

`protocol/game/client-to-server/0x0047.md:109`（本次变更新增行）：

> `id == -1` 且标志成立时客户端按金币处理（`setSlot(amount, 1, slot, 4)`）。**本仓此处不发金币**，
> 牌面金币属于另一个成果。

后半句在**同一次交付里**就不成立。RQ-006 与 AC-007 正是本 Spec 的要求，
`GameProtocol.ClearRewardCardGold`（`Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs:45`）
在 `SelectCardCommand.cs:230` 每翻到一张有货的牌就入账一次，
并在 `DungeonClearSlice.cs:380` 写进结算帧的免费牌金币位。
「本仓此处不发金币」就这一帧而言是对的（codec 从不写 `id == -1` 的记录），
但「牌面金币属于另一个成果」把一个已交付的需求说成了别人的事。
落在 BD-019（协议记录的实现映射与代码一致）之内。

修法：把后半句改成「金币不走本帧，而是入账并在 `NOTI 0x0023` 的免费牌金币位公布；
见 `GameProtocol.ClearRewardCardGold`」之类的表述。

### B2（阻塞）BD-015 改写的那一帧，其权威记录仍说本仓什么都不写

`protocol/game/server-to-client/NOTI-0x0023.md:123`：

> **本仓这一局什么都不发,帧里也就这么写。** 通关奖励需要经验公式、评级奖励表和翻牌生成器,
> 这三样都还没有;凭空写数字会在结算画面上留下服务端自己交代不了的钱和经验。

基线时这句已因经验发放而部分失真；**本次变更把它彻底变假**：
`DungeonClearPayloadCodec.EncodeClearDungeonReward`（`DungeonClearSlice.cs:246-300`）
现在写免费牌标记位（160）、免费牌物品（161）、免费牌金币（165）、
七个牌位（169…223）与付费翻牌花费（232），翻牌生成器即
`ClearRewardRuleService` / `CardRewardDomainService`。
本次变更更新了 `0x0045.md`、`0x0046.md`、`0x0047.md`、`NOTI-0x000E.md` 四份记录
与 `catalog.md`，唯独漏了自己改写的这一帧的记录。
生产代码里有八处 `<see cref>` / 注释指向这份记录当依据
（`DungeonClearSlice.cs:332`、`347`、`440` 等），一个照着依据读回来的人会读到相反的话。

同一份记录的人工区还有一处**本次变更已经识别但未回填**的内部分歧：
散文说「免费牌那一对在 240 与 245」，而它自己的布局块把标记位放在 240、物品放在 241。
`DungeonClearSlice.cs:347-356` 把这条分歧写进了代码注释并声明不依赖它——
**记录本身没有留下这条分歧**，下一个读者会重新踩一遍。

### B3（阻塞）`Docs/新功能接入规范.md` §三的整节现在教人建一个已被推翻的设计

契约 Constraints 明写「分层按 `AGENTS.md` 与 `Docs/新功能接入规范.md`」，
`AGENTS.md` 也把它列为操作清单。该文 §三「走一遍：翻牌子」正是本功能的示范，
现有六处与交付实现相反：

| 行 | 陈述 | 现状 |
|---|---|---|
| 200 | 内容源读「`[drop prob]`、`[drop item type prob]`、**探索度加成**」 | `[reward item rate per map max count]` **不是探索度**（`spec.md` Non-Goals 明写），revision 4 的 BD-022 已「撤掉 revision 3 遗留的探索度实现」 |
| 203 | 「副本自己的 `[result card]` 覆盖声明落在 `IDungeonDefinitionSource` 那一侧」 | TASK-08 已裁定不实现：那是大会玩法、走 `0x01CA`/`0x01CB`、四个声明副本本服务器全部不可达（`DungeonResultCardDeclarationTests` 钉住） |
| 208 | 「真正新的只有**「按通关评级分概率」**这一段」 | 真正新的是几率合成（`ClearRewardRules.ChanceOf`）；全局表**不按评级分档**（`spec.md` Assumptions），`ClearRewardOccasion` 里根本没有评级 |
| 212 | 规则服务接收 facts「（**副本 id**、角色等级、难度、**评级**、**探索度**、队伍人数、显式种子）」 | 实际是 `ClearRewardOccasion(Kind, CharacterLevel, **DungeonBasisLevel**, Difficulty, MapMaxCount, PartyMemberCount)`——无副本 id、无评级、无探索度，而**多了副本基准等级**，正是堵掉「85 级角色刷 1 级副本得 85 级装备」那个口子的读法 |
| 239 | 「`NetworkMessages.cs` 加**三个**目录成员」 | 实际两个（`GameCardSelectRightState`、`GameSelectCard`），两个枚举各加两个 |
| 256 | 「「奖励跟你选的那张走」还是「永远只有第 0 张有货」：**没有证据可断**」 | 契约 revision 4 决定 9 已定为「四张各有各的，翻一张全亮出」，已实现并由 M8 钉住 |

一个按 `AGENTS.md` 指引照这一节做下一个功能的人，会重建探索度读法与按评级分概率，
即 revision 4 刚修掉的那个缺陷（三个迷宫通关后整面空白）。

### B4（阻塞）`Docs/DropRates.md` 与本次变更自己改正的代码注释互相矛盾

本次变更在 `Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs:113-121` 与 `:206-213`
把「四个文件同形、换一张费率表即可」改正为「七个文件里三个走这条读法；
翻牌表十六段里九段是它独有的，`[drop prob]` 形状不同，所以由 `IClearRewardRateSource` 读」。
`Docs/DropRates.md` 仍保留被改正掉的说法：

| 行 | 陈述 | 现状 |
|---|---|---|
| 263 | 「费率表 `etc/itemdropinfo_clearreward.etc`，**同构**，另加：」 | 同一次变更已在代码里改正为「Not the same sections」 |
| 271 | ``[reward item rate per map max count]`` ← **探索度加成** | 同 B3 第一行 |
| 277 | 「4 个声明 `[result card]`（指名物品 + 按权重给数量 + **按评级分概率**）」 | TASK-08 探针证明第一位 key 是**大会已完成轮数**（0..4，优胜 5），不是评级；数目也与本仓八档评级对不上 |
| 405 | 「**二期**：翻牌。同一台机器**换一张费率表**」 | 换不了：翻牌需要自己的读取器，这正是 `DropRateTables.ClearReward` 的注释本次被改写要说的事 |

## Non-blocking Observations

### N1 `GameProtocol.ClearRewardCardGold` 的处置——我独立判为「不阻塞本次交付」，但控制器的理由不是最好的那条

事实核准：该常量定义在 `GameProtocol.cs:45` **唯一一处**，生产读者恰好两个——
`SelectCardCommand.cs:230`（命令处理器）与 `DungeonClearSlice.cs:380`（切片 codec）。

- **按 `AGENTS.md:111-113` 字面，这是违规**：「表达归档或游戏语义的常量」属规则输入，
  「`Protocol` 的 handler 与命令处理器都不读」。代码给的先例辩护（`SecretShopNpcId`）站不住：
  那是一个协议载荷里的 NPC 编号，这个 1 决定玩家账号加多少钱，不同类。
  TASK-09 §3.3.1 已自己写明这一点，比控制器后来的「要动两个切片的调用形状」说得准。
- **但按本 Spec 的判据它不违规**：SC-9 与 RQ-014 说的都是「新增的**档案读取点**」，
  而这个 1 恰恰**不是**档案读数（档案说翻牌不出金币）；
  契约 Constraints 把分层规则渲染为「**内容源**只被规则服务注入」，同样不覆盖常量。
  RQ-013「无出处值单独具名并写明是假设及其理由」则**已满足**。
- **所以：不该阻塞本次交付。** 但后续任务的正确形状不是「改两个切片的调用形状」，
  而是**把金币搬到牌位上**——`spec.md` 的 Definitions 自己就写着「牌面内容 = 一件装备**与一份金币**」，
  而 `ClearRewardCardItem` 今天只有装备。按 Spec 的数据模型，这一份金币本就属规则层。
- **另一半今天缺失**：`AGENTS.md` 的例外清单只列了 `ExperienceDiagnostics` 与
  `SelectCharacterDeliveryOptions` 两类，没有这一条。也就是说仓库的规矩今天说它不许，
  而没有任何地方记着这是**明知而为的例外**。建议二选一：现在就开后续任务，
  或把它连同理由补进那份例外清单。

### N2 RQ-011 只有后半句成立；spec.md 未被修订以记录这一裁定

`[result card]` 的副本级牌面覆盖**没有实现**。TASK-08 的裁定与证据我复核认可：
四个声明副本（120/121/122/4119）都开 `[maze info]` 而不声明 `[map specification]`，
`PvfDungeonDefinitionSource` 一律答 null，本 Spec 建的路进不去；
`DungeonResultCardDeclarationTests.Every_dungeon_declaring_a_result_card_is_absent_from_the_definition_source`
以副本 144 作阴性对照钉住了这个事实。实现改做 `[gold card use]` 付费牌开关，四个可达副本，
`DungeonPaidCardSwitchTests` / `DungeonPaidCardSwitchRuleTests` 阴阳两向。

- RQ-011 前半句对每个**可达**副本空真，后半句成立且有判据——这个读法我接受。
- 但 **AC-014 与 EV-009 未被满足**：没有「牌面内容来自该副本的声明」的判据，
  交付的是「声明者不可达」的判据。这是替代证据，不是原证据。
- **spec.md 从未被修订**记录这条裁定。只读 `spec.md` 的人会得出「一条需求没做」的结论，
  必须翻到 `tasks/TASK-08-*.md` 才知道原委。建议在 spec.md 的 RQ-011 / AC-014 / EV-009 处
  就地记上裁定与其判据文件名。不阻塞，因为裁定本身有据、可追、且实现相反做法会违反契约
  「读不出来的档案字段不得填一个看起来合理的数」。

### N3 `spec.md` frontmatter 的契约版本号陈旧

`spec.md` 头部 `contract.revision: 2`，而 `TaskContract.md` 是 revision 4，
且 spec 正文已含 revision 4 的内容（RQ-008 的房间数上限、RQ-016、AC-011、AC-020…022）。
同族：`tasks/TASK-08-*.md` 的 Task-specific authorization 亦写「根契约（revision 2）」。
流程产物，不阻塞代码，但它是本 Spec 反复出现的那个形状。

### N4 SC-3 的「重连后再问」一支是推论，不是判据

EV-002 要「同一局内重复读取**与连接中断后再读**，牌面逐位相同的判据」。
重复读取有 M9 钉住；重连这一支靠结构成立：牌面挂在 `DungeonRunState` 上、按 `CharacterId` 索引，
`IDungeonRunDomainService.End` 全仓只有三个调用点
（`DungeonClearSlice.cs:159` 放弃、`LeaveDungeonSlice.cs:119` 出门、`DungeonSelectionSlice.cs:202` 开新局），
**没有一个是断线路径**，且种子只取 `run.Layout.Seed` / `DungeonId`
（`Both_seeds_are_derived_from_the_run_and_from_nothing_else` 钉住）。
结论对，但「断线不结束本局」这一条本身没有本 Spec 的判据钉住，
若日后有人给断线加上 `runs.End`，SC-3 会静默失效而全量仍绿。

### N5 `ResultScreenTests` 两条既有用例丢掉了帧数断言

`Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/ResultScreenTests.cs:192-196` 与 `:212-216`：
`Assert.IsType<…>(Assert.Single(sender.Sent))` 改成了 `sender.Sent[0]` / `sender.Sent[1]`。
`Assert.Single` 原来兼作「恰好一帧」的断言，现在帧数完全不受约束，
第三帧、第四帧都能悄悄加进来。用例自己的 remarks 声明「断的是顺序不是数量」，
但补一行 `Assert.Equal(2, sender.Sent.Count)` 不花任何代价。
`CardRewardTests.The_scroll_acknowledgement_is_followed_by_the_layout` 用 `Assert.Collection`
做对了同一件事——两处不一致。

### N6 那个 1 金币没有被任何字面断言钉住

判据一律经符号引用（`CardRewardTests.cs:736,757,758,796`、`DungeonClearCodecTests.cs:233,388`）。
把常量改成 0，全量仍绿，而 `A_position_that_held_something_pays_the_cards_gold`
的两个分支会同时退化成 `0 == 0`——**一条看起来在检查其实不再检查的判据**。
这是「可改」这个目标的必然代价，我不主张改成字面 1；
但可以补一条「有货的牌付得比空牌多」的关系断言，让它在任何取值下仍有内容。

### N7 `ClearRewardTable.MapCountRate(int)` 已成只有判据在用的死 API

`ClearRewardContent.cs:272`。生产路径走 `MapCountRates` 字典经 `ClearRewardRules.MapBaseRate`
（`ClearRewardRuleService.cs:116`）；`MapCountRate` 只剩 `ClearRewardArchiveTests.cs:102-103` 引用。
与 `ClearRewardOccasion` 上刚被删掉的 `VisitedRooms` 同形——
「看起来是输入、其实到不了任何答案」。不影响正确性，记录备查。

### N8 结算帧在玩家翻牌之前就公布了免费牌的内容

`DungeonClearSlice.cs:380-386`：`NOTI 0x0023` 的免费牌物品与金币在结算时即写出，
而 `CMD 0x0046` 版式帧「不携带任何物品，牌是背面朝上的」。两者并存是 `spec.md` 批准的
（RQ-012 / AC-016 明要，且版式与结算是两帧），实现无误。仅指出这是一个已批准的设计后果：
客户端在翻牌前就已握有免费牌位的内容。若日后有人认为这是剧透，改动落在 RQ-012 而非代码。

### N9 交付质量中值得点名的几处

- **金币原子性**：扣款与判定在同一条 SQL（`InventoryStateTables.cs:118-133`
  的 `WHERE … AND value32 >= @amount … RETURNING`），拒绝时一个字节都不写；
  `TrySpendAsync` 另加负数守卫。M7 证明这条路真的被钉着。
- **先收费后认领，认领失败退款**（`SelectCardCommand.cs:212-225`），
  且命令注册为 `Transactional`（`ProtocolModule.cs:196`），扣与退在同一个领域事务内。
- **背包增量帧的上提是纯搬家**：`GameNotiStateSynchronizationEncoder<T>` 与
  `InventoryDeltaEncoder` 的 `EncodePayloads` 形状逐字相同，wire type 仍 `0x000E`；
  `InventoryDeltaLiftTests` 与新增的
  `SelectCharacterLifecycleTests.CommittedRefill_SendsTheBagChangeBetweenTheRefillAndTheTail`
  两侧都钉住了，且既有用例的期望值一个未改。
- **`ChanceOf` 的整数比较是对的**：`losing = shortfall * basis / scaledParty` 是
  `dice * scaledParty <= shortfall * basis` 的 floor，`Rate = basis − losing`；
  `shortfall < 0` 全过、`scaledParty <= 0` 全不过，两个边界都有用例。
  `BlankItemOf` 的加权抽取与 `DropRandom.Next` 的 1..n 语义吻合，权重为零的行抽不中。
- **无嵌套 dispatch**：`SelectCardCommandHandler` 不持有 `ICommandBus`；
  `ICommandBus` 只在消息 handler `SelectCardHandler` 上，派发一次。

---

## Re-review — round 2

**裁决：`approved`。** 第 1 轮的四条阻塞全部消解，且**改后的说法逐句为真**——
不是把假话换成另一句假话。五轮返工对 `Server/` 的净影响为零，我用比 diffstat 更强的证据核过。
返工新写的八处衍生修正 + 稀有度三处 + 归档指针一处，我逐处独立核实，未发现新的假话。

本节由第二位评审员独立完成。**第 1 轮全文未改一字**；返工方的自证一律当作待验证的主张。

### 我自己跑过的

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| 全量 | `PVF_ARCHIVE_PATH=…/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | **1963 / 0 / 2**，退出码 0（Pvf 60 / Networking 51 / Protocol 479 / Host 4 / Persistence 248 / Infrastructure 215+2 / Application 906） |
| 代码净影响 | `git diff --stat -- Server/src` ／ `-- Server/tests` | **20 / 768 / 199** 与 **10 / 875 / 14**，与第 1 轮变异还原后所记逐字相同 |
| 帧布局 | 手工重算 `EncodeClearDungeonReward` 全部 274 字节 | 见 B2 |
| 行尾 | 四份文档 `tr -dc '\r' | wc -c` | 全为 0 |

### 变异（最终交付态，mtime 前进，还原后 sha256 逐文件 `OK`）

| # | 变异 | 结果 | 钉住的 |
|---|---|---|---|
| **M-A** | `GameProtocol.ClearRewardCardGold` `1` → `0` | **红 4**：`The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision`、`Gold_behind_the_free_position_never_arrives_without_an_item`（2 例）、`A_paid_flip_costs_the_archives_price_for_the_dungeons_level` | B1 的金币；**并推翻第 1 轮 N6** |
| **M-B** | `FreeCardGoldAt` 的 `PositionOf(cards, FreeCardPosition)` → `PositionOf(cards, 1)` | **红 4**：`The_free_positions_gold_is_what_a_flip_of_that_position_pays`、`The_card_tail_carries_the_free_face_position_for_position`、`Gold_behind_the_free_position…`（2 例） | B1 / B2「只覆盖免费牌位 0」 |
| **M-C** | `UnpricedPaidFlipCost` `int.MaxValue` → `0` | **作废**：触发 `error xUnit1025`（与既有 `InlineData 0` 重复），**编译失败不算变红**，已丢弃 |
| **M-C2** | 同上 → `int.MaxValue - 1` | **红 1**：`The_marker_for_a_level_with_no_price_is_not_a_free_flip` | B2 的 232 位 |

还原后全量复跑仍 **1963 / 0 / 2**，三个被动过的文件 `sha256sum -c` 全部 `OK`。

### 四条阻塞逐条

#### B1 —— **消解，两半句我各自去代码里验过**

- **保留的那半「本仓此处不发金币」为真。** `CardRewardPayloadCodec.WriteRecord` 只在
  `slot.Item is not null` 时写 `(uint)ItemId, (uint)Quantity`；长度式也只对有物品的记录加
  `ContentPairLength`。没有任何路径写出 `id == -1` 的记录。
- **换掉的那半现在为真。** `SelectCardCommand.cs:230` 每翻到有货的牌位入账
  `GameProtocol.ClearRewardCardGold`；该常量 `GameProtocol.cs:45` 定义为 `1`，其 XML doc
  明写是本服务器自定的占位值而非档案读数——与新文字一致。
- **「这一次交换的两帧都不报它」为真**：`SelectCardHandler` 一次投递最多两帧
  （`SelectCardResponse` + `GameInventoryDelivery.Notification`），后者载的是装备落在哪一格。
- **「其余三个牌位那份金币没有任何帧公布」为真**：`FreeCardGoldAt` 只查 `FreeCardPosition = 0`；
  七个 seat 写的是 `AmountAt` = `Quantity`（`DungeonClearSlice.cs:462`），不是金币。**M-B 钉住。**
- 顺带核过新增段其余可验证的数：33 字节 = `8 × 4 + 1`（`AbsentRecordLength = 4`，
  `RecordHeaderLength 3 + 尾 1`，`GameCmdEnvelope.Accept` 的成功字节），
  「每多一件东西多 8 字节」= `ContentPairLength = 8`；`divergent` 确在本页机器区第 44 行。

#### B2 —— **消解，我把 274 字节逐格重算了一遍**

从 `EncodeClearDungeonReward` 手工累加：`u32,i32,u32,i32,u8,23×i32` → 0..108；
`u8,u8,u32,u32` → 109..118；`5×i32` → 119..138；`4×i32` → 139..154；`u32` → 155..158；
保留 `u8` → 159；**160 标记 / 161 物品 / 165 金币**；七个 seat 的标记落在
**169,178,187,196,205,214,223**；**232** 付费价；其后 16 零 + i32 + u8 + u8 + u32 + u32 + 8 零
= **274**，与 `ClearRewardBodyLength` 的收尾自检一致。记录里那张表**逐格对得上**。

- **240/241 分歧的回填属实。** 参照侧 +80：159→239 保留、**240 标记 / 241 物品 / 245 金币**、
  249…303、312。散文说「免费牌那一对在 240 与 245」，布局块把物品放 241——两句确实打架，
  记录如实记下且声明本仓不依赖它（金币与物品同一次 `PositionOf` 查找，两种读法字节相同）。
- **经验两格的说法为真**：`award.Base` 与 `award.Gained - award.Base`；`award` 为 null 或
  会话无技能点镜像（`DungeonClearSlice.cs:630`）时两格写 0。
  `DungeonClearExperienceRules.BaseWorth` 确按 `BasisLevel` 查任务奖励表再乘
  `ClearOverallRate × DifficultyRate × DungeonWeight`；`RankBonus` 确按上报评分经
  `DungeonRankGrades.BandOf` 定档乘 `ClearRankRates[band]`。
- 「服务端实现」表六项制品**逐个存在**。**M-C2 钉住 232 位。**

#### B3 —— **消解，六处逐条核过**

| 处 | 我的核实 |
|---|---|
| 探索度 | `ClearRewardRules.cs:293` 明写不按已走房间缩放；`VisitedRooms` 全仓 0 命中 |
| `[result card]` | `DungeonResultCardDeclarationTests.cs:49` `Assert.Equal([120,121,122,4119], declared)`；`0x01CA` 文件名即 `TOURNAMENT_REWARD_SELECT_STATE` |
| 「真正新的是几率合成」 | `ClearRewardRules` 类注释逐条列出五项自定：加法、组队乘骰、超表取最大行、`[drop prob]` 作阈值、整数比较——与规范新文字一一对应 |
| facts 清单 | `ClearRewardOccasion(Kind, CharacterLevel, DungeonBasisLevel, Difficulty, MapMaxCount, PartyMemberCount)`——无副本 id、无评级、无探索度，多副本基准等级；构造器恰为四项 |
| 目录成员「两个」 | `git diff NetworkMessages.cs` 恰为两个成员 × 两个枚举 = 四行 |
| 牌面形状已定 | `ContentOwningPosition` 仍是唯一读法点 |

六个 profile 亦核过：`default / event / event2 / pcroom default / pcroom bonus / pcroom event`，
`ThresholdProfile` 取 `Default`——「六个、按场合命名、只取 default」为真。

#### B4 —— **消解，数字我自己算过**

`ClearRewardRarityLadderTests` 断言阶梯为 `500000 799900 999900 1000000 1000001 1000002 1000003`、
`Basis = 1000003`、`HighestRoll = 1000002`、`BandCount = 6`、`BandOf(1000002) == Chronicle`。
按 1000002 复算百分数：50.00 / 29.99 / 20.00 / 0.01 / 0.0001 / 0.0001——与文档逐个相符，
**错的确实只有第六档的名字**，已改为「远古」。`20.00 / 2.51 ≈ 8` 支持「翻牌出紫是打怪的八倍」。
可破坏物表少 `[party member drop bonusrate]`（`DropRateContent.cs:118`）、
深渊表的 `[dungeon difficulty drop prob]`、80–89 多开一个品级
（`Assert.Equal(Enumerable.Range(80, 10), aboveDiffers)`）三条均属实；
`DropRateTables.ClearReward` 的 XML doc 与文档里的中译一致。

### 我自己重跑的扫描（12 条里的 4 条）

语料规模复现**完全一致**：`Docs/**.md` 102 + `protocol/**.md` 1741 +
`Server/**.cs`（去 `obj`/`bin`）651 = **2494**。

| 条 | 我的结果 | 判定 |
|---|---|---|
| **S1** 探索度 | **8 命中 / 8 真**，`VisitedRooms` **0 命中** | 完全复现。两处 `.cs` 命中逐一读了上下文：`ClearRewardContent.cs:199` 明确标为「另一个服务器的读法，不是本文件的规则」；`ClearRewardRules.cs:293` 明确说本仓不这么做 |
| **S3** 按评级分概率 | **6 命中**；其中 **S3b（四个牌面类型里搜 `rank`）= 0 命中** | 0 命中是独立强证据：`ClearRewardOccasion` 里确实没有评级。5 条现在时为真，1 条为带日期的 trace |
| **S8** 房间数上限 | 在范围内的命中全真 | 最大一行 = 5 房 = **6000**（`ClearRewardArchiveTests.cs:103`），与 revision 4 决定 3「超过 5 房一律取 6000」吻合 |
| **S10** 同构 / 复用 | 9 处在范围内的命中**全真** | 栽过两次的那条陈述已收敛；`DropRates.md:264`（可破坏物表同构）确为真 |

另复核 `可能走副本自己的` = **0 命中**（第 5 轮那处已无残留拷贝）。

### 代码未被动 —— 我用了比 diffstat 更强的证据

`git diff --stat` **只覆盖已跟踪且已修改的文件**，约 28 份新增未跟踪 `.cs` 不在其内，
所以「三个数逐字相同」本身**并不能**证明代码没动。mtime 补上了这一半：

```text
Server/ 下（去 obj/bin）最新的 .cs      = 08:19  （新增未跟踪的最新一份也是 08:19:04）
change-review.md（第 1 轮）             = 08:27
remediation-round-1 … 5                 = 08:45 … 09:17
08:27 之后被改动的非 .rks-agent 文件     = 恰好那四份文档（08:36 / 09:04 / 09:15 / 09:15）
```

（`GameProtocol.cs` 09:27 与 `DungeonClearSlice.cs` 09:29 是**我自己的变异**留下的，
两者 sha256 已还原校验 `OK`。）**结论：`Server/` 下没有任何一行被返工改动。**

### 「不改清单」七项 —— 逐条判断

**结论：七项都不该改，但返工方给的统一理由「本次交付没有造成它们」对其中三项是错的。**

| 项 | 该不该改 | 我的理由 |
|---|---|---|
| `a21-tutorial-ending-20260826.md:113-114/150/157` | 不改 | 「没造成」**字面为假**——正是本次交付让「翻牌生成器没有」变假。但首行即「**2026-08-26。**」，通篇是那一天的实机归档，**带日期的快照按其性质不回改**。这才是成立的理由，也正是第 5 轮写进 `NOTI-0x0023.md` 指针段的那条 |
| `a21-room-rules-and-leaving-20260826.md:106/133/137` | 不改 | 同上，首行「**2026-08-26,同日第二次实机。**」 |
| `world-entry.md:700/704` | 不改，**但理由最弱，且应补日期标记** | 我核了结构：`### 还没做的` 在第 698 行，位于 `## 打不到怪：房间的 actor 表（**2026-08-26**）`（561 行）之下，同层前七个 `###`（605/616/625/637/652/659/676）**一律带「（同日）」类标记**，只有它没有。它的两条子弹（翻牌一帧都没做、`NOTI 0x0023` 全零）**今天为假**。不升为阻塞的分界是 B2 定的：B2 阻塞是因为 9 处代码引它当依据；本页对翻牌行为**没有任何代码或协议记录以它为依据**（仓内两处 `.cs` 引用讲的是角色名册与 `NOTI 0x000E` 上提）。**返工方自评「全轮最没把握」，并给出正确改法（补日期标记而非改内容）——我同意，建议后续任务执行** |
| `NOTI-0x0023.md:85-86` | 不改 | 真·先于本次交付：模板句与 `## A21` 一节是各自独立生成/追加的 |
| `NOTI-0x0023.md:89`「第三帧」 | 不改 | 我核了 `git diff`：本次只改了 `ClearDungeonRewardNotification` 的构造器参数，**帧序列与 `AvailableQuestListMessage` 那一支未动**。确属既有近似说法 |
| `change-review.md` | 不改 | 评审产物。全程未被修改，我已核 |
| `TaskContract.md`「史诗与传说」 | 已由控制器改 | 现为「史诗与远古」两处 |

### 第 1 轮非阻塞观察的现状

| | 现状 |
|---|---|
| **N1** 金币常量的分层 | **未变，第二半仍开着**。`AGENTS.md` 未被修改，其例外清单仍只列 `ExperienceDiagnostics` 与 `SelectCharacterDeliveryOptions`。按本 Spec 判据（SC-9/RQ-014 说的是**档案读取点**，这个 1 不是）仍不阻塞——我同意第 1 轮的判断。后续任务的正确形状仍是把金币搬到牌位上 |
| **N2** RQ-011 | **已消解**。`spec.md` RQ-011 已就地记入「前半句在 A21 上为空真…故本 Spec 未实现它」，AC-014 同样改写 |
| **N3** 版本号 | **已消解**（`spec.md` 为 `revision: 4`）。`TASK-08` 仍写「Contract revision: 1」/「根契约（revision 2）」——流程产物，不阻塞 |
| **N4** SC-3 重连一支 | **未变**。我复核 `End` 恰三个调用点（`DungeonClearSlice.cs:159`、`LeaveDungeonSlice.cs:119`、`DungeonSelectionSlice.cs:202`），**无一在断线路径**。结论仍对，仍是推论 |
| **N5** `ResultScreenTests` 帧数断言 | **仍开着**。第 195-196 / 215-216 行仍是 `sender.Sent[0]` / `[1]`，无 `Assert.Equal(2, sender.Sent.Count)`；第三帧仍可悄悄加入。返工为纯文字改动、未碰判据是对的。**不升为阻塞**：`Assert.Collection` 的同类用例已在 `CardRewardTests` 覆盖同一形状，且这是既有用例的质量项而非本次交付的正确性缺口。建议随后续任务补一行 |
| **N6** 那个 1 未被字面钉住 | **推翻——第 1 轮此条为假**。`DungeonClearCodecTests.cs:442` 有 `Assert.Equal(1, GameProtocol.ClearRewardCardGold);`，用例名 `The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision`。**M-A 实测红 4**，「把常量改成 0 全量仍绿」不成立。无需处置 |
| **N7** `MapCountRate` 死 API | **未变**。仍无生产调用者。第 1 轮低估了引用数：实为 3 个判据文件共 13 行（`ClearRewardArchiveTests` 7 行、`PvfClearRewardRateSourceTests` 6 行），非「只剩 :102-103」。不影响结论 |
| **N8** 结算帧提前公布免费牌 | **未变，且已改善**：这条已批准的设计后果现被写进 `NOTI-0x0023.md` 的「本仓写什么」一节，不再只活在代码注释里 |

**没有一条该升为阻塞。**

### 本轮新发现（均不阻塞）

1. **`DropRates.md:62` 与 `新功能接入规范.md:222-223` 的一处精度落差。** 两处都说五个副本声明
   `[gold card use] 0` 并「已实现 / 读作不提供付费牌」。**「五个都声明 0」为真**
   （`DungeonDefinitionContent.cs:59-60`「every one of the five declares it as `0`」），
   但**只有四个被落实**：3503 无 `[map specification]`，内容源答 null，它的开关静默失效。
   代码**主动记下了**这个不完整（`DungeonPaidCardSwitchRuleTests`
   `A_dungeon_with_no_definition_is_not_treated_as_switched_off`，remarks 明写
   「3503's own switch is not honoured, and that is recorded rather than hidden」）。
   **这一处是文档比代码含糊**，不是文档与代码相反，故不阻塞；但形状与本 Spec 栽过的那类相近，
   建议后续补一句「其中 3503 不可达，开关未落实」。
2. **返工记录自身的一处账目不一致（记录文件按指示未改）。** 第 4 轮把 S3 的一行判为日期快照
   并单列「3 个文件 8 行」，第 5 轮的收尾却把它算进「151 真」。
   **「151 命中 / 151 真」不应被读成「151 条现在时为真的陈述」**——其中至少一行是带日期的归档。
   不影响任何处置结论。
3. `M-C` 那次变异触发 `error xUnit1025` 而非变红，已按「编译失败不算变红」丢弃并换 `M-C2` 重做。
   记此一笔，因为它顺带说明 `NoPaidFlipCost = 0` 与 `UnpricedPaidFlipCost` 的取值
   被判据的 `InlineData` 结构隐式约束着。

### 交付可以接受的理由

四条阻塞都只需改文字，四条都改了，**改后的每一句我都自己去代码或档案判据里验过**，
没有出现「把假话换成另一句假话」。返工新增的十二处修正逐处属实，
本 Spec 栽过四次的那个形状（一句话里一半真一半假、同一条陈述在别处还有拷贝）
这一轮由「按陈述扫」收敛，我抽验的四条扫描全部复现。
生产与判据代码 mtime 证明一行未动，全量 1963 / 0 / 2，三个有效变异各自变红。

**`approved`。** 余下的建议（`world-entry.md` 补日期标记、`ResultScreenTests` 补帧数断言、
金币常量的例外登记或搬到牌位上、3503 那句补精度）属后续任务，均不构成本次交付的阻塞。
