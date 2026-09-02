---
kind: rks-verification
decision: pass
---

# Completion Evidence: 通关结算的翻牌奖励

## 被评估的主张

> 本次交付满足 `TaskContract.md`（revision 4）的成功条件 SC-1 … SC-13。

**裁决 `manual_required`。** 十三条里十二条有当前证据，且其中八条我自己施加变异证明了
判据真的在钉着它们。**SC-3 的「重连后再问」那一支没有判据**——契约 Required evidence 一栏
逐字要求「同一局重复请求**与重连后再问**，同一牌位得到同一件东西的判据」，
`spec.md` 把它列为 AC-003 与 EV-002 的一半，而全仓没有任何用例走这条场景。
该支撑我用穷举检索独立核实为**结构上成立**（见 UC-1），但那是代码检视事实，不是判据。
补上它要写一条新用例，超出本次验证的授权（「不要修改任何生产或判据文件」），
因此把处置交回人来定。

其余三条评审留下的非阻塞观察，我逐条判定**不影响任何一条 SC**，理由在下文「非阻塞观察」一节。

## 工作区状态

- 分支 `flipping-a-clear-reward-card`，`HEAD = c0cb6c99126281472d8c35b0643060fd7dfbbeec`，尚未提交。
- `scratchpad/baseline-approved` 的 **69 个文件与工作区逐字节相同**（`cmp` 全数无差异），
  验证前与全部变异还原后各核一次。也就是说：我跑的就是评审 `approved` 的那个交付态。

## 我自己跑过的

| # | 命令 | 结果 |
|---|---|---|
| R1 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 退出码 **0**；Protocol 479 / Networking 51 / Pvf 60 / Host 4 / Persistence 248 / Infrastructure 215（+2 跳过）/ Application 906 = **1963 通过 / 0 失败 / 2 跳过** |
| R2 | 同上加 `--filter`，点名本验证用到的 30 个用例 | 退出码 **0**，展开为 **36 次执行，36 通过，0 跳过**（Protocol 16 + Application 20） |
| R3 | 全部变异还原后重跑 R1 | 退出码 **0**，**1963 / 0 / 2**，与 R1 逐项相同 |

两条跳过是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，
均为 `Skip.If(ContentArchive.Available, …)`——**跳过数停在 2，本身就是档案确实加载了的证明**：
读档案的 `[SkippableFact]`（`ClearRewardArchiveTests` 一族、`One_dungeon_bands_…`、
`Every_maze_…`、`A_maze_larger_…`）若档案不可达会一并跳过，跳过数会暴涨。

## 变异

纪律：施加前记 sha256 → 施加使 mtime 前进 → 跑判据 → **立刻还原并 `sha256sum -c` 校验** →
再做下一条。无一条因编译失败作废（本仓 `TreatWarningsAsErrors`；所有变异都写成运行期改动，
不用 `if (false)` 一类会触发 CS0162 的写法）。

交付态基准（`sha256sum -c` 在每次还原后均 `OK`）：

```text
64f4660b… Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs
93fe5475… Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs
833fd0f7… Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs
76f1ab1b… Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs
63cfa88d… Server/src/Dfo.Server.Protocol/Game/CardReward/CardRewardPayloadCodec.cs
13e8b83a… Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs
8d174538… Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs
```

| # | 变异 | 结果 | 钉住 |
|---|---|---|---|
| **V1** | `ClearRewardRules.cs:319` `largestCount = int.MinValue` → `int.MaxValue`。等价于**「查不到即概率为零」**：`MapBaseRate` 对任何无行的房间数返回 null，`ClearRewardRuleService.cs:113-119` 据此返回 `ClearRewardChance(0, basis)`，即 `IsImpossible` | Application **红 9 / 通过 897**（编译正常）。含 `A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row`（**Expected 7400 / Actual 0**）与 `Every_maze_of_an_ordinary_dungeon_carries_a_chance` | **SC-12 的后半句** |
| **V2** | `ClearRewardRuleService.cs:202` `GradeBandFor(occasion.DungeonBasisLevel)` → `occasion.CharacterLevel` | 红 4，含 `One_dungeon_bands_and_prices_alike_for_every_character_level`、`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons` | SC-11 品级带 |
| **V3** | `CardRewardDomainService.cs:68` `PaidFlipCost(run.Layout.BasisLevel)` → `settlement.CharacterLevel` | 全量红 7（其余六个项目全绿），含 `The_price_is_asked_at_the_dungeons_level_and_not_the_characters` | SC-11 付费价 |
| **V4** | `ClearRewardRules.cs:188` `BlankItemSwitches.GoldCard` 默认 → `true` | 红 3，含 `Blank_item_switches_are_all_off_by_default` | SC-13 默认全关 |
| **V5** | `ClearRewardRuleService.cs:354` `random.Next(ladder.HighestRoll)` → `random.Next(1000000)`。**这正是契约点名的上游那处编造**（档案声明基数 1000003、最高摇点 1000002） | 红 2：`The_service_produces_every_Rarity_band_its_ladder_declares`（Expected `[Common, Uncommon, Rare, Unique, Epic, …]` / Actual `[Chronicle]`）、`The_service_rolls_a_Rarity_by_walking_the_tables_own_ladder` | SC-6 |
| **V6** | `DungeonRunDomainService.cs:180-181` 删掉 `if (claimed.Contains(id)) return null;` | 红 4：`A_position_is_claimed_once`、`A_position_is_handed_over_once`、`A_position_that_holds_nothing_is_still_claimed_once`、`The_two_faces_are_claimed_independently` | SC-4 |
| **V7** | `CardRewardPayloadCodec.cs:264` `ContentOwningPosition(index) => checked((byte)index)` → `checked((byte)Math.Min(index, 0))`（退回上游「只有第 0 张有货」） | Protocol 红 6：`Each_record_names_the_position_it_describes` 四例 + `Every_position_is_declared_including_the_ones_holding_nothing` + `A_face_holding_nothing_is_the_length_the_capture_had` | SC-2；并顺带钉住 SC-1 的长度式 |
| **V8** | `SelectCardCommand.cs:206` 让付费拒绝分支永不触发（`&& refused == SelectCardStatus.Flipped`，运行期恒假，不触发 CS0162） | Protocol 红 2：`An_account_that_cannot_afford_a_paid_flip_keeps_its_money_and_its_card`、`A_paid_flip_at_an_unpriced_level_is_refused_rather_than_free` | SC-8 拒绝路径 |
| **V9** | `DungeonRunDomainService.cs:148-149` 删掉 `if (current.ClearRewardCards is not null) return current;`（牌面幂等闸） | 红 1：`Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing` | SC-3 的「重问」一支 |
| **V10** | `GameProtocol.cs:45` `ClearRewardCardGold = 1` → `0` | 全量红 4：`The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision`、`Gold_behind_the_free_position_never_arrives_without_an_item`（2 例）、`A_paid_flip_costs_the_archives_price_for_the_dungeons_level` | SC-5 金币一支、SC-7 的具名占位值 |

V1 有额外意义：**它证明这条判据当初真的会红**。原实现「查不到就把概率打成 0」正是这个形状，
而当时全绿是因为所有 fixture 的迷宫只有 1–3 房，永远落在档案声明的 1–5 之内；
今天的 `Every_maze_…` / `A_maze_larger_…` 用真副本 144 的真实房间数 `[10,6,4,6,4]` 出题，
V1 一施加就红。

## SC 逐条 → 证据

| ID | 主张 | 证明它的事实 | 我做了什么 | 结论 |
|---|---|---|---|---|
| **SC-1** | 卷轴/版式/翻牌/到货四步走得通，三个应答字节形状与抓包一致 | `CardRewardTests`：`The_layout_is_the_body_the_capture_carried`（逐字节）、`A_face_holding_nothing_is_the_length_the_capture_had`、`The_scroll_acknowledgement_is_followed_by_the_layout`（`Assert.Collection`，**同时约束帧数**）、端到端 `A_flipped_piece_is_in_the_bag_afterwards`、`The_answer_comes_first_and_the_bag_change_after_it_in_one_exchange` | R2 逐个跑过并读到 `已通过`；V7 使长度式判据变红，证明字节断言在生效 | **有** |
| **SC-2** | 四牌位各有各的内容，翻一张时四张全揭晓；空牌与有货牌都覆盖 | `Every_position_is_declared_including_the_ones_holding_nothing`、`Each_record_names_the_position_it_describes(0..3)`、`Turning_one_card_over_reveals_the_whole_face` | R2 跑过；**V7 红 6** | **有** |
| **SC-3** | 牌面不因重问、**重连**或时间推移而变 | 重问：`A_second_settlement_keeps_the_cards_already_fixed`、`Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing`；时间推移：`Both_seeds_are_derived_from_the_run_and_from_nothing_else`（种子只取 `run.Layout.Seed` / `DungeonId`，不取时钟）。**重连：无判据** | R2 跑过前两支；**V9 红 1** 钉住重问。重连一支见 **UC-1** | **部分**（见 UC-1） |
| **SC-4** | 一个牌位翻过不能再翻 | `A_position_is_claimed_once`（领域层）、`A_second_flip_of_the_same_position_is_answered_with_nothing`（协议层）、`The_two_faces_are_claimed_independently` | R2 跑过；**V6 红 4** | **有** |
| **SC-5** | 装备进背包并伴随到货通知；金币计入账号 | `The_bag_change_carries_the_slot_the_piece_and_how_many`、`A_flipped_piece_is_in_the_bag_afterwards`、`A_position_that_held_something_pays_the_cards_gold`、`Gold_behind_the_free_position_never_arrives_without_an_item` | R2 跑过；**V10 红 4**（金币）、**V7 红 6**（牌面记录） | **有** |
| **SC-6** | 档案声明可掷的每一档稀有度都真掷得出，最高两档必须覆盖 | `ClearRewardRarityLadderTests.Every_Rarity_band_the_archive_declares_is_reachable_including_the_two_rarest`、`The_service_produces_every_Rarity_band_its_ladder_declares` | R2 跑过；**V5 红 2**——用契约点名的那处上游编造（分母写死一百万）施压，`The_service_produces_…` 立刻退化为 `[Chronicle]`。**这条判据不是摆设** | **有** |
| **SC-7** | 每处读数指回档案出处；无出处值单独具名并写明是假设 | `ClearRewardArchiveTests.The_reading_of_this_file_gives_no_basis_and_nothing_that_combines`（反射断言 `ClearRewardTable` 无任何含 `Basis` 的成员、四个查表各只一个参数）；`ClearRewardRules` 类注释逐条列出**五项本服务器自定**（加法、组队乘骰、超表取最大行、`[drop prob]` 作阈值、整数比较）；`ClearRewardRateOptions` 注释说明 `RateBasis` 档案未声明但有三处旁证；`GameProtocol.cs:28-45` 把 1 金币标为占位并写明它踩在一条推断的反面 | R2 跑过；我逐处读了这四段注释；**V10 红 4** 证明那个 1 有字面断言钉着 | **有** |
| **SC-8** | 黄金牌按档案等级成本扣币；余额不足拒绝且不发奖 | `A_paid_flip_costs_the_archives_price_for_the_dungeons_level`、`A_paid_flip_costs_the_price_the_settlement_names_and_hands_the_card_over`、`An_account_that_cannot_afford_a_paid_flip_keeps_its_money_and_its_card`、`A_paid_flip_at_an_unpriced_level_is_refused_rather_than_free`、`A_paid_flip_that_lost_its_position_is_given_its_money_back` | R2 跑过；**V8 红 2** | **有** |
| **SC-9** | 新增档案读取点只被规则服务注入；新增规则服务不持世界状态读写方 | `The_rule_service_is_handed_no_reader_of_world_state`（反射枚举构造器恰为 `IClearRewardRateSource` / `IDropItemPoolSource` / `IDungeonDefinitionSource` / `ClearRewardRateOptions`，再排除六个后缀）、`The_settlement_service_reaches_the_archive_only_through_the_rules` | R2 跑过。**我另做独立复扫**：`IClearRewardRateSource` 在 `Server/src` 的注入点只有 `ClearRewardRuleService.cs:52`（其余命中为接口声明、两处 XML doc、Pvf 实现、DI 注册）；`IClearRewardRuleService` 的注入点只有 `CardRewardDomainService.cs:16`（`SelectCardCommand.cs:40` 那处是 `///` 注释里的 `<see cref>`，不是依赖） | **有** |
| **SC-10** | 全量不低于基线 1690 通过 / 0 失败 | R1 与 R3 | 我自己跑了两次，**1963 / 0 / 2，退出码 0**，两次逐项相同 | **有** |
| **SC-11** | 品级带与付费价只由副本决定：同一副本对不同角色等级给同一答案 | `ClearRewardArchiveTests.One_dungeon_bands_and_prices_alike_for_every_character_level`（真副本 144，`[basis level] 5`；先断言 `GoldCardCost(5)=580` 与 `GoldCardCost(86)=10300`、`GoldCardCost(低)=2380` **互不相同**，让后面的相等有东西可失败；再对 8 个种子 × 2 个角色等级各结算一次，比价钱与**逐位牌面指纹**，并断言 `drawn > 0` 排除「两张空脸相等」）、`CardRewardDomainServiceTests.The_price_is_asked_at_the_dungeons_level_and_not_the_characters`、`ClearRewardRuleServiceTests.The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`（反向钉住：`[drop prob]` 刻意仍用角色等级） | R2 跑过；**V2 红 4（品级带）+ V3 红 7（付费价）两向压过** | **有** |
| **SC-12** | 房间数超出档案声明范围时牌面仍可能有内容，**且该判据要能在「查不到即概率为零」下变红** | `Every_maze_of_an_ordinary_dungeon_carries_a_chance`（读真副本 144，断言五个迷宫房间数 `[10,6,4,6,4]`，其中 `[10,6,6]` 三个表里查不到；对每个迷宫 × 免费/付费两面断言 `!IsImpossible` 且 64 个种子里至少有一个非空槽；再断言 `Free(4)=6400` 与 `Free(6)=Free(10)=7400` 两侧确实不同）、`A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row` | R2 跑过。**「能变红」这半我亲自压过：V1** 把实现改回「查不到即 null → 概率 0」，两条判据同时红，其中 `A_maze_larger_…` 报 `Expected 7400 / Actual 0` | **有，两半都有** |
| **SC-13** | 每种牌的保底开关可独立开合、默认全关 | `Blank_item_switches_are_all_off_by_default`（三处：裸对象、`ClearRewardRateOptions` 默认、**经 `ApplicationModule` 实际注册出来的对象**；再逐个只开一个断言 `[true,false,false]` 等，且一律经 `Allows(ClearRewardBlankCard.*)` 读回，映射串了会红）、开态 `Blank_items_are_what_a_switched_on_paid_card_hands_out`（200 个种子，六种数量全须出现）、关态 `Blank_items_are_handed_out_by_nothing_while_the_switches_are_off`、`Blank_items_have_no_free_card_table_to_come_from`、`Blank_items_of_the_pcroom_card_are_read_and_switched_and_reach_no_flip` | R2 跑过五条；**V4 红 3** | **有** |

## 评审留下的四条非阻塞观察：是否影响某条 SC

| 观察 | 影响哪条 SC | 我的理由 |
|---|---|---|
| **N1** `AGENTS.md` 例外清单未登记 `ClearRewardCardGold` | **不影响任何 SC** | 我读了 `AGENTS.md:110-120`：该文确未修改，例外清单仍只列 `ExperienceDiagnostics` 与 `SelectCharacterDeliveryOptions`，按其字面这个常量是违规的。但 **SC-9 的可观察条件与其 Required evidence 说的都是「新增的**档案读取点**」与「新增类型的**构造器依赖枚举**」**，而这个 1 既不是档案读数（档案说翻牌不出金币，这正是它成为占位值的前提），也不是任何构造器参数；契约 Constraints 把分层渲染为「内容源只被规则服务注入」，同样不覆盖常量。**SC-7 要求的「无出处值单独具名并写明是假设」它已满足**，我读了 `GameProtocol.cs:28-45` 并用 V10 证明它有字面断言。因此它是仓库规矩的登记欠账（属契约 Constraints 一侧的后续事项），不是任何一条 SC 的缺口 |
| **N4** SC-3 重连一支是推论 | **影响 SC-3** | 见 **UC-1** |
| **N5** `ResultScreenTests` 两例丢了帧数断言 | **不影响任何 SC** | 我读了 `ResultScreenTests.cs:195-196 / 215-216`，确为 `sender.Sent[0]` / `[1]` 无 `Assert.Equal(2, …)`。但 SC-1 的 Required evidence 是「逐字节断言三个应答 + 一条走完全程的免费牌判据」，其载体是 `CardRewardTests`，其中 `The_scroll_acknowledgement_is_followed_by_the_layout` 用 `Assert.Collection` **本身即约束帧数**（我读了 `CardRewardTests.cs:175-179）。这是既有用例的质量项，不落在任何 SC 的条件或证据上 |
| **N7** `ClearRewardTable.MapCountRate(int)` 无生产调用者 | **不影响任何 SC** | 我复扫 `Server/src`：`MapCountRate` 只剩 `ClearRewardContent.cs:272` 的定义与 `:165` 的一处 doc 引用，生产路径确实走 `MapCountRates` 字典 → `ClearRewardRules.MapBaseRate`。**而这一点我是用实验而非推理确定的**：V1 改的是生产路径上的 `MapBaseRate`，SC-12 的两条判据当场变红——说明它们压的是生产路径，不是那个死 API |

## Unverified Conditions

### UC-1 — SC-3 的「重连后再问」没有判据

**契约要求的是判据。** SC-3 的 Required evidence 一栏：「同一局重复请求**与重连后再问**，
同一牌位得到同一件东西的判据」。`spec.md` 把它拆成 AC-003（「连接断开后同一角色重新读取该局牌面
→ 每个牌位给出的东西与断开之前完全相同」）与 EV-002 的后半。

**当前没有这样的用例。** 我在 `Server/tests` 里按 `Disconnect` / `reconnect` / `disconnected`
检索了 `CardReward` 与 `World` 两族，唯一命中是 `CardRewardDomainServiceTests.cs:221`
的一句**注释**（「…still hand a reconnecting player different cards」），不是场景。
`CardRewardTests` 的 45 个用例里 `A_flip_without_a_published_session_never_reaches_the_run`
说的是「没有会话」，不是「断开后重来再读」。

**这条件本身很可能为真——我自己核过，但核出来的是代码事实，不是判据：**

1. `IDungeonRunDomainService.End` 在 `Server/src` 恰有三个调用点，无一在断线路径：
   `DungeonClearSlice.cs:159`（放弃）、`LeaveDungeonSlice.cs:119`（出门）、
   `DungeonSelectionSlice.cs:202`（开新局）。
2. 断线观察者 `OnlineUserConnectionObserver`（`DisconnectedAsync`）**根本不依赖
   `IDungeonRunDomainService`**——它不在该接口 16 个消费文件之列。
3. 本局状态在内存里按 `CharacterId` 索引，牌面挂在 `DungeonRunState.ClearRewardCards` 上，
   一经 `FixClearRewardCards` 定下就被幂等闸挡住（**V9 红 1** 证明这道闸在生效）。
4. 两个种子只取 `run.Layout.Seed` 与 `DungeonId`，不取时钟或共享发生器
   （`Both_seeds_are_derived_from_the_run_and_from_nothing_else`）。

**为什么这仍是缺口。** 1 与 2 是「今天没人这么写」，不是「谁这么写就会红」。
日后有人给断线加一句 `runs.End`，SC-3 就静默失效而全量仍绿——这正是本 Spec 反复栽过的形状
（判据从没红过，只因为没人出这道题）。把结构推论当成判据，就是把部分证据升级成完整证据。

**要什么才能翻成 `pass`。** 二选一，都需要人来定：

- 补一条用例：同一 `CharacterId` 结算后，模拟连接断开（走真实的断线路径而非直接丢弃对象），
  再读该局牌面，断言四个牌位逐位相同；顺带把「断线不调 `End`」钉住。
  这要改判据文件，超出本次验证「不要修改任何生产或判据文件」的授权。
- 或者由契约所有者明确接受「结构推论 + 上列四项代码事实」作为 SC-3 这一支的证据，
  并把这一裁定就地记进 `TaskContract.md` / `spec.md`（同 N2 对 RQ-011 的处理方式），
  使其可追、不至于下一个读者重新踩。

**其余十二条 SC 不受此影响**，各自的证据与我亲自跑出的结果如上表。

## Re-verification — round 2

我不是第 1 轮那位验证员，也不是 TASK-13 的执行者或评审员。下面每一条都是我自己跑出来的；
第 1 轮的裁决、TASK-13 的执行报告与它的独立评审，我一律当作**待验证的主张**读。

### 工作区状态（我自己核的）

分支 `flipping-a-clear-reward-card`，`HEAD = c0cb6c99126281472d8c35b0643060fd7dfbbeec`，尚未提交。

`scratchpad/baseline-approved` **已经不在了**（`ls: cannot access 'scratchpad'`），所以我用不了
第 1 轮那种 69 文件逐字节比对。改用哈希锚定：我自己算出的五个文件 sha256 与
前几轮各自独立记录下来的值**逐串相同**，因此我跑的确实是同一个交付态：

```text
64f4660bb55c1bfdef319e3656f7760656c166dc2f94d399a0bc289bbcd4b942 *Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs
93fe547557fefb027b70d25087cdb28edd3b9b92ff5641a76ac3c12c7f171ef2 *Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs
be4bab6b39930cc7e52116f86913d946c35d624132e89ef77a1db2594e36fe06 *Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs
8d174538643c023b1287088cc60b28a289229a5cdee3f2dc0a9f689ce889d257 *Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs
deac202a02c9f8427cca3c261daf2ab88058b3bb83e90ff2d1b220d5e4cf69c9 *Server/tests/Dfo.Server.Networking.Tests/Integration/CardRewardReconnectIntegrationTests.cs
```

前三串与第 1 轮验证 / TASK-13 各自记录的相同；`deac202a…` 是 TASK-13 round 2 改完散文后的值
（round 1 是 `27da0d50…`），即当前工作区是 TASK-13 的**最终**态。

### R1 — 全量（我自己跑的第一件事）

```text
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
EXITCODE=0
Protocol 479 / Pvf 60 / Networking 52 / Host 4 / Persistence 248 / Infrastructure 215（+2 跳过）/ Application 906
```

479+60+52+4+248+215+906 = **1964 通过 / 0 失败 / 2 跳过，退出码 0**。与本轮题面给的数字一致。
两条跳过具名为 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
（我从日志里读到 `[SKIP]` 行），**跳过数停在 2 即档案确实加载**。

### SC-1 的抓包比对 —— 我自己去日志里抽验

我不看 TASK-13 的表，自己把 9 份归档日志按 type 抽出来去重
（8 份在 `E:/Project/DNF/ServerS4A21/Server/DfoServer/bin/Debug/`，
第 9 份 `E:/Project/DNF/ServerS4A21/_reference-backup-20260826/packet_log.txt`）：

| 帧 | 我数到 | 去重后取值 |
|---|---:|---|
| `SEND type=0x0045` | **14** | 只有一种：`(1B) [01]` |
| `SEND type=0x0046` | **14** | 只有一种：`(17B) [01 01 00 FF×14]` |
| `SEND type=0x0047` | **18** | `(33B)` ×11 全同；`(49B)` ×7（`a` 取 `00` 四帧 / `FF` 三帧） |
| `RECV type=0x0047` | **14** | `(2B) [00 00]` ×10、`(2B) [01 00]` ×4 |

**与 TASK-13 独立评审员数到的 14/14/18/14 逐项相同**（执行者 round 1 报的 13/13/17 是漏了第 9 份，
round 2 已自行订正）。逐条比判据里的期望值：

| 期望值出处 | 判据写的 | 抓包实测 | 判 |
|---|---|---|---|
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/ResultScreenTests.cs:228` | `new byte[] { 0x01 }` | 14/14 帧 `body=(1B) [01]` | **逐字节相同，语料无第二种取值** |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs:53-57` `CapturedLayout` | `01 01 00` + `FF`×14 | 14/14 帧同串 | **逐字节相同，语料无第二种取值** |
| 同上 `CapturedEmptyAnswerLength` | `33` | 11/11 空脸帧 `(33B)` | **对得上** |
| `CardRewardTests.cs:299-306` `Each_record_names_the_face_it_belongs_to` | Free→`0x00`、Paid→**`0x01`** | 18 帧第 0 条的 `a` **只有 `00` 与 `FF` 两值，`01` 一次未现** | **付费面无抓包支持** |

`(49B)` 帧我自己拆过：
`01 | a=00或FF, b=00, c=02 | (id=0,count=0) (id=0x0000908F,count=1) | 尾 00 | (FF FF 00 00)×3 | FF×16`
= 1+20+12+16 = 49。**带货记录的语法（`a/b/c` + 小端 u32 对 + 尾 u8）被实测帧钉死，与判据一致。**

**结论（我自己判）**：`0x0045` 与 `0x0046` 两个应答**逐字节与抓包相同**，SC-1 这两支现在压的是
一级证据（服务端逐帧发送日志）而非文档转述。`0x0047` 的**字节形状**一致——首字节、空脸 33 字节、
8 条 4 字节记录、后四条 `FF FF FF FF`、变长记录语法全部对得上；三处取值有意不同
（第 1–3 条的取值、付费面 `a=0x01`、一件货只写一对），**都已在
`protocol/game/client-to-server/0x0047.md:103-113`「本仓与上游的差别」一节登记**，
其中付费面那一支在 `:65` 明写「免费那一条有上游读法互证，付费那一条没有」。
**付费面 `a=0x01` 在整个语料里确实一次未现——TASK-13 把这件对自己不利的事如实写了，我复核属实。**
承载这三个期望值的两个判据文件哈希与前几轮记录的相同，**没有任何期望值被往结论上调过**。

### 我自己重做的变异

纪律：先记 sha256（见上）→ `cp` 留 pristine → 施加使 mtime 前进 → 跑判据 → **立刻 `cp` 还原并
`sha256sum -c` 全数 OK** → 再做下一条。**没有一条是编译失败冒充变红**：每次都逐行确认八/九个项目
`-> ….dll` 输出成功、失败发生在测试执行期，且 `grep "error CS"` 无命中。

#### M1 —— SC-12 的「要能变红」（题面点名必做）

`Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs:319`
`var largestCount = int.MinValue;` → `int.MaxValue`。这样任何一行都进不了「取最大行」那一支，
`MapBaseRate` 对**任何未直接声明的房间数返回 null**，即退回缺陷本身的「查不到即概率为零」。
mtime `10:01:31` → `11:01:30`。

```text
dotnet test Server/tests/Dfo.Server.Application.Tests --nologo
EXIT=1   失败: 9，通过: 897，已跳过: 0，总计: 906
```

九条红里点名两条题面要的：

```text
失败 ClearRewardArchiveTests.A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row
  Assert.Equal() Failure: Values differ
  Expected: 7400
  Actual:   0
      at ...\ClearRewardArchiveTests.cs:line 566

失败 ClearRewardArchiveTests.Every_maze_of_an_ordinary_dungeon_carries_a_chance
```

另外 `The_chance_a_position_carries_is_the_archives_numbers_combined(mapMaxCount: 6 / 10, expected: 7400)`
两例也报 `Expected 7400 / Actual 0`，`Both_faces_of_one_clear_carry_the_same_odds` 报
`Expected [7400] / Actual [0]`。**SC-12 的「该判据在『查不到即概率为零』的实现下变红」这半，
我亲自压出来了，红文与第 1 轮所述逐字一致。** 还原后 `sha256sum -c` 五行全 **OK**。

#### M2 —— SC-3 的重连一支（题面点名必做）

`Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs`：末位加
`IDungeonRunDomainService? runs = null`（带默认值，否则另外两处手工构造该观察者的既有判据会
编译失败，那就成了编译失败冒充变红），并在 `DisconnectedAsync` 的 `if (selected is not null)`
**首句**写 `runs?.End(selected.CharacterId);`。mtime `10:53:22` → `11:02:21`。

```text
dotnet test Server/tests/Dfo.Server.Networking.Tests --filter FullyQualifiedName~CardRewardReconnect
EXIT=1   失败: 1，通过: 0（272 / 291 / 300 ms，跑了三次）
  错误消息: Assert.NotNull() Failure: Value is null
     at ...CardRewardReconnectIntegrationTests.Snapshot(...) in ...:line 202
   at ...A_clears_cards_survive_the_connection_that_was_shown_them() in ...:line 164
```

**三件我自己确认的事**：

1. **是判据红，不是编译失败。** 九个项目全部 `-> ….dll` 成功，失败发生在测试执行期。
2. **红在重连之后。** 当前文件第 164 行是 `Assert.Equal(shown, Snapshot(runs));`，即**重连之后**
   那次快照；断线前那次（第 147 行 `var shown = Snapshot(runs);`）是绿的。**3/3 全部红在 164**，
   评审 N-D 担心的「红点落到断线前那次」我三次都没遇到。
3. **它真的走进了断线路径最深的一支。** 变异只写在 `if (selected is not null)` 里面——那要求
   `onlineUsers.TryGetByConnection` 与 `selectedSessions.FindByConnection` 两道闸都过。
   它能让判据红，说明**真 socket 关闭时那个分支确实被框架驱动执行了**，这是实验事实不是推理。

还原后 `sha256sum -c` 五行全 **OK**，单独重跑该用例 **已通过 1 / 0**（281 ms）。

#### M3 —— SC-6（契约点名的那处上游编造）

`Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs:354`
`ladder.BandOf(random.Next(ladder.HighestRoll))` → `random.Next(1000000)`，即上游那处「分母写死
一百万」（档案声明基数 1000003、最高摇点 1000002）。mtime `10:02:52` → `11:03:58`。

```text
dotnet test Server/tests/Dfo.Server.Application.Tests --nologo
EXIT=1   失败: 2，通过: 904

失败 ClearRewardRarityLadderTests.The_service_produces_every_Rarity_band_its_ladder_declares
  Assert.Equal() Failure: Collections differ
  Expected: [Common, Uncommon, Rare, Unique, Epic, ···]
  Actual:   [Chronicle]
      at ...\ClearRewardRarityLadderTests.cs:line 161
失败 ClearRewardRarityLadderTests.The_service_rolls_a_Rarity_by_walking_the_tables_own_ladder
```

还原后 `sha256sum -c` 五行全 **OK**。

#### M4 —— SC-5 的金币一支与 SC-7 的具名占位值

`Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs:45`
`public const int ClearRewardCardGold = 1;` → `= 0;`。mtime `10:07:13` → `11:04:31`。
这一条我跑的是**全量**，好顺便看它是否只动该动的地方：

```text
dotnet test Server/86JP.Server.slnx --nologo
EXIT=1
Protocol 失败 3 / 通过 476；Application 失败 1 / 通过 905；其余五个项目全绿

失败 DungeonClearCodecTests.The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision
失败 DungeonClearCodecTests.Gold_behind_the_free_position_never_arrives_without_an_item(TTTT)
失败 DungeonClearCodecTests.Gold_behind_the_free_position_never_arrives_without_an_item(TFFF)
失败 PaidCardFlipCostTests.A_paid_flip_costs_the_archives_price_for_the_dungeons_level
```

**红 4，与第 1 轮 V10 报的四条逐条相同。** 还原后 `sha256sum -c` 五行全 **OK**。

**我没有重做的变异**：第 1 轮的 V2 / V3（SC-11 两向）、V4（SC-13 默认全关）、V6（SC-4）、
V7（SC-2 与 SC-1 长度式）、V8（SC-8 拒绝路径）、V9（SC-3 重问一支）共七条。
它们**是第 1 轮的主张，不是我自己的实验**——我据以判定的是这些判据在我自己的 R1/R2/R3 里
确实存在且通过，加上第 1 轮那份逐条报告的可信度；本轮题面也只要求我重做三条。
下表凡引用这七条的地方，我都标了「第 1 轮压过（我未重做）」。

### R2 —— 我点名跑本次验证用到的判据

我先用 `grep` 确认**这 37 个用例名在 `Server/tests` 下全部存在**（无一 MISSING），
再按名字过滤跑：

```text
dotnet test Server/86JP.Server.slnx --filter "FullyQualifiedName~<37 个名字用 | 连起来>"
EXIT=0
Protocol 27 / Networking 1 / Application 21 = 49 次执行，49 通过，0 失败，0 跳过
```

37 个名字按项目分布为 Protocol 15 / Application 21 / Networking 1，与执行数
（Protocol 15 个名字展开成 27 次，另两项一对一）**对得平**——**没有一个名字是没匹配上的**。

### R3 —— 四条变异全部还原后重跑全量

```text
EXITCODE=0
Networking 52 / Protocol 479 / Pvf 60 / Host 4 / Persistence 248 / Infrastructure 215（+2 跳过）/ Application 906
```

**1964 / 0 / 2，与 R1 逐项相同**，两条跳过仍是那两条具名用例。
`sha256sum -c` 五行全 OK，`git status --porcelain --untracked-files=all` 仍是 **95** 条
（与 TASK-13 评审数到的 95 相同，没有多出任何文件）。**还原正确，有哈希与全量两重证明。**

### SC-3 —— 我自己判「重连后再问」够不够

契约 Required evidence 逐字：「同一局重复请求**与重连后再问**，同一牌位得到同一件东西的判据」。

**「重问」一支**：`A_second_settlement_keeps_the_cards_already_fixed`、
`Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing`（第 1 轮 V9 压过，我未重做）、
协议层 `A_second_flip_of_the_same_position_is_answered_with_nothing`。R2 全绿。

**「重连」一支**：`CardRewardReconnectIntegrationTests.A_clears_cards_survive_the_connection_that_was_shown_them`。
我自己读了这个用例的全部 301 行，并用 M2 压过。**我的判断是：够。** 四条理由，每条都是我自己核的：

1. **它走的是真断线路径，这一点被实验证住，不是自述。** M2 的变异只写在
   `DisconnectedAsync` 的 `if (selected is not null)` **里面**——要进那一支，
   `onlineUsers.TryGetByConnection` 与 `selectedSessions.FindByConnection` 两道闸都得过。
   判据红了，所以真 socket 关闭时那个分支**确实被框架驱动执行了**。
2. **夹具走的捷径不会漏掉任何观察者。** 用例把会话直接登进 `ISelectedSessionDirectory`
   而不走线上登录选人。我自己复扫 `Server/src`：`INetworkConnectionObserver` 的实现
   **全仓只有 `OnlineUserConnectionObserver` 一个**（`Game/OnlineUserConnectionObserver.cs:24`），
   由 `ProtocolModule.cs:86` 注册为 Singleton；`NetworkPlugins.cs` 三处按 `IEnumerable<>` 注入并逐个
   `await`。因此**真实断线时跑到的观察者集合与线上完全相同**，捷径只替掉了「这条连接属于谁」
   这一个事实，而那个事实本身已被第 1 点证明是真的被读到了。
3. **它断言的正是契约那句话的字面。** 第 164 行按**值**（不是对象引用）比两面的逐位指纹与
   `PaidFlipCost`；第 168–181 行再**逐个牌位真的去领**（`TryClaimClearRewardCard`），
   比 id / 数量 / 稀有度 / 耐久四项。四张牌里有货与空位交错、两面的空位错开、四件货各不相同，
   所以「同一牌位得到同一件东西」不能靠「哪一位都一样」蒙混过去。
4. **红点落在重连之后，我三次都验到。** 当前文件第 164 行是重连后那次 `Assert.Equal(shown, Snapshot(runs))`，
   第 147 行那次断线前快照是绿的。评审 N-D 担心红点可能落到 147，**我 3/3 都落在 164**。

**我另核的一条结构事实（用来定这一支的射程）**：本局在
`DungeonRunDomainService.cs:18` 是 `ConcurrentDictionary<CharacterId, DungeonRunState>`，
**按角色号索引、与连接号无关**，所以「重连丢牌面」在构造上只可能来自断线路径主动拆本局——
而那正是这条判据钉住的东西。

**射程限制（我如实登记，但判为不影响 SC-3）**：重连后的「再问」问在领域缝
（`IDungeonRunDomainService`），不是 `0x0047` 线上帧。契约那句话说的是「同一牌位得到同一件东西」，
不是「经某一帧再问」，且 `0x0047` 帧自身的字节形状由 `CardRewardTests` 另行逐字节钉住
（M4 全量跑时那一族全绿）。它盖不住的是「握手/寻址层在重连后找不到本局」这一类**另一种**故障
——那不是「牌面内容变了」，不在 SC-3 的可观察条件里。**执行者与评审的结论我复核后同意，
但上面四条理由与这条限制是我自己得出的。**

### SC-1 —— 我自己判「与已实证抓包一致」够不够

**够，判 SC-1 有当前证据。** 依据是本节前面那张我自己做的对照表：

- `0x0045` 与 `0x0046` 两个应答**逐字节与抓包相同**，且 14 帧语料里各自只有一种取值、
  没有反例。这两支现在压的是一级证据。
- `0x0047` 的**字节形状**与抓包一致：首字节 `01`、空脸 33 字节、8 条 4 字节记录、
  后四条 `FF FF FF FF`、带货记录的 `a/b/c` + 小端 `(u32 id, u32 count)` 对 + 尾 `u8`
  ——最后这条语法由 7 帧 `c=02` 的实测帧钉死，我自己拆过 49 字节那一帧对上了。
- 三处取值有意不同，**都已如实标注**：`protocol/game/client-to-server/0x0047.md:103-113`
  的「本仓与上游的差别」一节写明本仓给四个牌位各填一条、`b` 填自己的序号、
  「牌型（`a`）**按本仓的读法**写免费 0 / 付费 1」，`:65` 另写明「免费那一条有上游读法互证，
  **付费那一条没有**」。契约 Assumptions 也已把 `b` 的含义与黄金牌形状列为未实证。
- **付费面 `a=0x01` 在整个语料里一次未现**（18 帧第 0 条的 `a` 只有 `00` 与 `FF`）。
  这一条对交付不利，TASK-13 主动写了出来，我复核属实。**它落在契约已具名的假设里，
  不是被隐瞒的差异。**

契约 SC-1 的可观察条件说的是「**字节形状**与已实证的抓包一致」，Required evidence 说的是
「逐字节断言三个应答的判据，以及一条走完全程的免费牌判据」。两样都在，且我亲自比对了抓包原文。
承载期望值的两个判据文件哈希与前几轮记录相同，**没有任何期望值被往结论上调过**。

### SC 逐条 → 本轮结论

「我做了什么」一栏只写**我自己**做的；引用第 1 轮变异的地方一律注明我未重做。

| ID | 证明它的事实 | 我做了什么 | 结论 |
|---|---|---|---|
| **SC-1** | `The_layout_is_the_body_the_capture_carried`（逐字节）、`ResultScreenTests:228` 的 `0x0045` 确认、`A_face_holding_nothing_is_the_length_the_capture_had`、`The_scroll_acknowledgement_is_followed_by_the_layout`（`Assert.Collection` 兼约束帧数）、端到端 `A_flipped_piece_is_in_the_bag_afterwards` | **我自己去 9 份一级抓包日志里逐段比对**（见上）：`0x0045`/`0x0046` 逐字节相同、`0x0047` 形状相同、三处有意不同已登记；R2 这一族全绿 | **有** |
| **SC-2** | `Every_position_is_declared_including_the_ones_holding_nothing`、`Each_record_names_the_position_it_describes(0..3)`、`Turning_one_card_over_reveals_the_whole_face` | R2 跑过全绿；第 1 轮 V7 压过（**我未重做**） | **有** |
| **SC-3** | 重问三条 + **重连 `A_clears_cards_survive_the_connection_that_was_shown_them`** + 时间推移 `Both_seeds_are_derived_from_the_run_and_from_nothing_else` | **M2 我亲自压过，3/3 红且红在重连之后**；我另核了「全仓只有一个连接观察者」与「本局按 CharacterId 索引」两条结构事实。重问一支第 1 轮 V9 压过（**我未重做**） | **有（UC-1 已关闭）** |
| **SC-4** | `A_position_is_claimed_once`、`A_second_flip_of_the_same_position_is_answered_with_nothing`、`The_two_faces_are_claimed_independently` | R2 跑过全绿；第 1 轮 V6 压过（**我未重做**） | **有** |
| **SC-5** | `The_bag_change_carries_the_slot_the_piece_and_how_many`、`A_flipped_piece_is_in_the_bag_afterwards`、`A_position_that_held_something_pays_the_cards_gold`、`Gold_behind_the_free_position_never_arrives_without_an_item` | R2 跑过；**M4 我亲自压过，金币那一支红 4** | **有** |
| **SC-6** | `Every_Rarity_band_the_archive_declares_is_reachable_including_the_two_rarest`（读真档案：基数 1000003、最高摇点 1000002、六档权重全 > 0、**`BandOf(1000001)=Epic`、`BandOf(1000002)=Chronicle`**，并直接断言 `BandOf(1_000_000)=Unique` 把那处编造的后果写死）、`The_service_produces_every_Rarity_band_its_ladder_declares` | **M3 我亲自压过**，服务那条退化为 `[Chronicle]`。我另读了档案那条用例的全部断言，**最高两档确有点名覆盖** | **有** |
| **SC-7** | `The_reading_of_this_file_gives_no_basis_and_nothing_that_combines`；`ClearRewardRules` 与 `ClearRewardRateOptions` 的注释逐条列出本服务器自定项；`GameProtocol.cs:28-45` 把 1 金币标为占位并写明它踩在一条推断的反面 | 我逐段读了 `GameProtocol.cs:28-45` 那段 `<remarks>`；**M4 证明那个 1 有字面断言钉着**（`The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision` 变红） | **有** |
| **SC-8** | `A_paid_flip_costs_the_archives_price_for_the_dungeons_level`、`An_account_that_cannot_afford_a_paid_flip_keeps_its_money_and_its_card`、`A_paid_flip_at_an_unpriced_level_is_refused_rather_than_free` | R2 跑过全绿；拒绝路径第 1 轮 V8 压过（**我未重做**）；扣费一支被我的 M4 顺带压红 | **有** |
| **SC-9** | `The_rule_service_is_handed_no_reader_of_world_state`、`The_settlement_service_reaches_the_archive_only_through_the_rules` | **我自己读了 `ClearRewardRuleServiceTests.cs:490-516`**：它反射取**唯一**构造器并断言参数恰为 `IClearRewardRateSource` / `IDropItemPoolSource` / `IDungeonDefinitionSource` / `ClearRewardRateOptions`，再排除六个世界状态后缀。**这正是契约 Required evidence 说的「构造器依赖枚举」**，不是近似物 | **有** |
| **SC-10** | 全量不低于基线 1690 通过 / 0 失败 | **R1 与 R3 我自己各跑一次全量，两次都 1964 / 0 / 2、退出码 0、逐项相同**；跳过数停在 2 即档案确实加载 | **有** |
| **SC-11** | `One_dungeon_bands_and_prices_alike_for_every_character_level`、`The_price_is_asked_at_the_dungeons_level_and_not_the_characters`、`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons` | R2 跑过全绿；第 1 轮 V2/V3 两向压过（**我未重做**）。我的 M1 顺带让 `One_dungeon_bands_…` 变红，旁证它确实压在这条读数路径上 | **有** |
| **SC-12** | `Every_maze_of_an_ordinary_dungeon_carries_a_chance`（真副本 144，五个迷宫 `[10,6,4,6,4]`）、`A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row` | **M1 我亲自压过**：`MapBaseRate` 退回「查不到即 null」，两条同时红，`A_maze_larger_…` 报 **`Expected 7400 / Actual 0`**。**「要能变红」这半我自己见到了** | **有，两半都有** |
| **SC-13** | `Blank_item_switches_are_all_off_by_default`、`Blank_items_are_what_a_switched_on_paid_card_hands_out`（开态）、`Blank_items_are_handed_out_by_nothing_while_the_switches_are_off`（关态）、`Blank_items_have_no_free_card_table_to_come_from`、`Blank_items_of_the_pcroom_card_are_read_and_switched_and_reach_no_flip` | **我自己读了 `ClearRewardRuleServiceTests.cs:724-755`**：三种牌（Free / Gold / PcRoom）各自单开一个断言 `[true,false,false]` 等，默认全关在**三处**核（裸对象、`ClearRewardRateOptions` 默认、**经 `ApplicationModule` 实际注册出来的对象**），一律经 `Allows(...)` 读回。**「三种牌各自开与关两态」对得上契约字面。** R2 全绿；第 1 轮 V4 压过（**我未重做**） | **有** |

### 关于第 1 轮那一节 `## Unverified Conditions`

按本轮指示，**第 1 轮全文原样保留、一字未动**，因此上面那个 `## Unverified Conditions` 与 UC-1
仍在文中——**它记录的是第 1 轮的裁决，不是本轮的**。UC-1 点名要的东西（「补一条用例：
同一 `CharacterId` 结算后，模拟连接断开（**走真实的断线路径**而非直接丢弃对象），
再读该局牌面，断言四个牌位逐位相同；顺带把『断线不调 `End`』钉住」）**已由 TASK-13 补上，
并经我用 M2 独立复核**：真断线路径（第 1 点）、四个牌位逐位相同（第 3 点）、
「断线不调 `End`」被钉住（变异一加就红）——**三样全中**。

**本轮没有未验证条件**，故不另立 `Unverified Conditions` 一节。

### 本轮登记、但不影响任何 SC 的事

- **N1（第 1 轮已判）**：`AGENTS.md` 例外清单未登记 `ClearRewardCardGold`。我同意第 1 轮的判定
  ——SC-9 说的是「档案读取点」与「构造器依赖枚举」，SC-7 要的「无出处值单独具名并写明是假设」
  它已满足（M4 证明有字面断言）。这是仓库规矩的登记欠账，属契约 Constraints 一侧的后续事项。
- **N5（第 1 轮已判）**：`ResultScreenTests` 两例丢了帧数断言。SC-1 的证据载体是 `CardRewardTests`，
  其 `The_scroll_acknowledgement_is_followed_by_the_layout` 用 `Assert.Collection` 本身即约束帧数。
- **抓包留下的三件未查明**（我复核属实，均已在 TASK-13 与 `0x0047.md` 登记）：`b` 是牌位序号
  还是「付费面是否被选中」抓包分不开；付费面 `a` 该写什么没有证据；上游为什么恒写两对没查明。
  **它们是契约 Assumptions 已具名的假设，不是本次交付新增的缺口。**
- **`scratchpad/baseline-approved` 已不存在**，所以第 1 轮那种 69 文件逐字节比对本轮做不了。
  我改用哈希锚定（五串与前几轮各自独立记录的相同）+ `git status` 条数（95，与评审数到的相同）
  + R1/R3 全量逐项相同三重佐证。**这是方法上的降级，我如实记下**；但它足以证明我跑的是同一个
  交付态、且我的四条变异全部还原正确。
- 仓库根目录仍有三个 0 字节垃圾文件（`上游源码**。`、`档案`、`社区文档`），早于本 Spec，
  与任何 SC 无关。

## Decision — round 2

**`pass`。**

十三条成功条件**全部有当前证据**。第 1 轮判 `manual_required` 的唯一理由 UC-1（SC-3 的
「重连后再问」只有结构推论、没有判据）已经消除：判据存在，**我自己施加变异证明它真的钉着
这一支**，红在重连之后，且证明真实断线路径的最深一支确实被执行。SC-1 的「与已实证的抓包一致」
我自己去一级逐帧日志里比对过：两个应答逐字节相同，第三个应答形状相同、三处取值有意不同且
全部如实标注，其中付费面 `a=0x01` 无抓包支持这件对交付不利的事，交付方主动写明、我复核属实。

判据现状：**1964 通过 / 0 失败 / 2 跳过，退出码 0**，独立跑了两次（变异前 R1、全部还原后 R3），
逐项相同，远高于契约 SC-10 要求的 1690。四条变异全部还原，`sha256sum -c` 五行全 OK。

**我没有修改任何生产或判据文件**：唯一的触碰是四条变异，每条做完立刻还原并经哈希校验；
本次交付我只写了本文件的这一节。
