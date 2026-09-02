# TASK-11 — 品级带与付费价改用副本基准等级

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

把 `[item drop ref table]`（品级带）与 `[gold card cost table]`（付费价）两处取值的键，
由**角色等级**改为**副本自身的 `[basis level]`**。角色等级不再参与这两处。

- Referenced spec requirements: RQ-007, RQ-010
- Referenced plan sections: `plan.md` 的 T-011；边界 BD-023
- Owned boundary: BD-023
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`
- 上述各处对应的判据文件

- Dependencies: TASK-10
- Required dependency outputs: TASK-10 的新合成实现
- Acceptance conditions:

1. 同一副本对不同角色等级给出**同一品级带与同一价钱**；该判据须先在现实现下变红。
2. 副本 144（`[basis level] 5`）的付费价为 **580**，品级带取 5 级那一行——用真档案核。
3. TASK-07 建立的「公布价与实收价一致」仍然成立。按副本取价后该问题自然消失，
   **但那条既有判据不得删除**；若其期望值需改，改动要在文中逐条说明理由。
4. 全量判据不低于交付前基线，失败为零，跳过仍为 2。

- Task-specific authorization: 根契约 revision 4 覆盖本任务。合并、推送、建 PR、删分支或清理 worktree 不在其内。
- Required methods:

- `test-driven-development`：先拿到红，再让它绿。
- 归档读法写明段落出处；`[basis level]` 的取用理由在其所在之处写明。
- 变异检查同 TASK-10 的要求。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-026 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` --filter PaidCard | 全绿 |
| CK-027 | `Non-command:` 副本 144 的品级带与价钱对照归档 `[basis level] 5` | 价钱 580；品级带取 5 级那一行 |
| CK-031 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于基线，失败零，跳过 2 |

- Required evidence: 三条检查的输出；先红后绿的那条红的原始输出；对既有判据改动的逐条理由。

## Contract Amendment 1 — 由 TASK-10 转来的两项

**来源**：TASK-10 的控制器裁定三与裁定四。两项都是「同一个问题的其余处」，
放在本 Task 一并了断，因为本 Task 正是改「用哪个等级去查归档」的那一个。

**Permitted files 增补**：`Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
（已在清单内）与本 Task 已列的其余文件；无需新增文件。

**Acceptance conditions 增补**：

5. **`ClearRewardRuleService.cs` 选 `[drop prob]` 等级段所用的等级要一并裁定。**
   它现在用 `occasion.CharacterLevel`，而契约 revision 4 第 6 条只点名了品级带与付费价。
   要么随另两处改用副本基准等级，要么明确记为「刻意仍用角色等级」并写明理由。
   **今天两种选择的结果相同**（`default` 四个等级段全是 10000）——
   **正因为看不出差别，才特别容易一直错下去**，所以必须显式裁定，不得默默留着。
   若改，须有判据钉住；若不改，理由写在其所在之处。

6. **`ClearRewardOccasion.VisitedMapCount` 已无人读，须处置。**
   TASK-10 撤掉探索度实现后它成了死字段（`IClearRewardRuleService.cs:110`）。
   删除它要同时改 `CardRewardDomainService.cs`，两者都在本 Task 清单内。
   删或留都可以，但**留就要写明为什么留**——本 Spec 已经因「看起来像在用其实没用」的
   字段与脚手架栽过两次。

## Contract Amendment 2 — 由 TASK-10 round 3 转来的一处

**来源**：TASK-10 的控制器裁定二。控制器已独立核实原文。

`Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs:158-166`
（该文件已在本 Task 的 Permitted files 内）：

1. `The rules do read the kind` —— **revision 4 第 7 条之下规则不再读牌型**，这句说反了。
2. 所引的 `ClearRewardRuleServiceTests.The_kind_of_a_face_decides_the_profile_and_the_scale_it_is_drawn_at`
   已在 TASK-10 round 1 被改名删除，**全仓只剩这一个 cref 提到它**，悬空。
3. `wherever their two rates agreed` 偏弱：两面现在恒等，一个种子会让它们**永远**一样。
   **结论（两面须用不同种子）仍成立，且理由比原文更强**——改写时不要把结论一起改掉。

**Acceptance conditions 增补**：

7. 上述三处改准；改完用那把尺子证明 `Application.Tests` 程序集再无与本主题相关的 `!:`。

**尺子的用法**（TASK-10 round 3 造的，控制器已验证可用）：
`dotnet build <csproj> --nologo -p:GenerateDocumentationFile=true -p:NoWarn=1591
-p:TreatWarningsAsErrors=false`，**不改仓里任何文件**，读生成的 XML——
解析不到的 cref 在里面写作 `cref="!:..."`。

## Contract Amendment 3 — 由 TASK-10 转来的一行

`Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs:24` 长 **158 列**，
是同目录唯一离群值（其余九个文件最长 101–109）。该文件已在本 Task 的 Permitted files 内。

**Acceptance conditions 增补**：

8. 该行回折至与邻居齐平（≤110 列），**文字不改**。

**顺带记一句**：这一行是 TASK-10 round 1 删掉 `ExplorationRate` 措辞后没回折留下的，
而 round 2 的自查说「该目录最长 110 列」——**它用的尺子把自己这个文件漏在外面了**。
本 Task 自查若要报同类数字，先确认被测集合真的包含了你改的文件。

## Execution Result — round 1

### 交付概要

`[item drop ref table]`（品级带）与 `[gold card cost table]`（付费价）两处取值的键，
已由角色等级改为副本自身的 `[basis level]`。`ClearRewardOccasion` 现在同时带
`CharacterLevel` 与新增的 `DungeonBasisLevel` 两个事实，各自写明归属；
`IClearRewardRuleService.PaidFlipCost` 的形参改名为 `dungeonBasisLevel`；
`CardRewardDomainService` 从 `run.Layout.BasisLevel` 取这两处的键
（该值本来就随布局走，无需二次读档案）。

第三处 `[drop prob]` 的等级段（Amendment 5）**显式裁定为仍用角色等级**，理由见下。

### 逐条验收条件

**1. 同一副本对不同角色等级给出同一品级带与同一价钱；须先在现实现下变红。**

成立。判据 `ClearRewardArchiveTests.One_dungeon_bands_and_prices_alike_for_every_character_level`：
取真档案的副本 144（`[basis level] 5`），用真 `ClearRewardRuleService` + 真装备池 +
`CardRewardDomainService`，对同一份布局分别以角色等级 20 与 86 结算，跨 8 个迷宫种子比较
——付费价相同且为 580，两面牌逐位指纹相同，且每一件抽出的装备都落在 5 级那一行的品级带
`ItemGradeBand(2, 8)` 内。另断言两个角色等级本身在档案里价钱与品级带都不同
（2380 / 10300、品级带不等），所以上面的「相同」不是两边都没查表。
非空性也断言（八次结算确实发出过装备）。

先红的原始输出（写这条判据、尚未改实现时跑的；当时判据名为
`One_dungeon_gives_one_band_and_one_price_whatever_level_the_character_is`，
后因列宽回折改名为现名，断言未变）：

```
[xUnit.net 00:00:01.34]     Dfo.Server.Application.Tests.World.ClearRewardArchiveTests.
  One_dungeon_gives_one_band_and_one_price_whatever_level_the_character_is [FAIL]
  错误消息:
   Assert.Contains() Failure: Filter not matched in collection
Collection: [DropPoolEntry { ItemId = 114000002, Weight = 375, Durability = 45 },
  DropPoolEntry { ItemId = 106040039, Weight = 375, Durability = 45 },
  DropPoolEntry { ItemId = 10401, Weight = 625, Durability = 33 }, ...]
失败!  - 失败:     1，通过:     0，已跳过:     0，总计:     1，持续时间: 1 s
```

改完实现后，用「把品级带的键改回角色等级」这一变异复核了同一条判据（现名）仍会变红，
输出见自检里的变异一。

**2. 副本 144 的付费价 580，品级带取 5 级那一行——用真档案核。** 成立（CK-027，见下）。

**3. TASK-07 建立的「公布价与实收价一致」保留。** 保留，未删。
`PaidCardFlipCostTests.A_clear_that_levelled_the_character_still_charges_what_it_announced`
名字与结论都没动，期望值改了，逐条理由：

| 原期望 | 新期望 | 理由 |
|---|---|---|
| `announced = Price(85)`（结算时角色等级的价） | `announced = Price(BasisLevel=5)` = 580 | 价钱的键换了。公布价现在是副本那一行，这正是本 Task 的改动；不改这个期望，判据就是在断言旧键。 |
| `Assert.NotEqual(announced, afterTheAward)`（两个角色等级的价不同） | 三条 `Assert.NotEqual`：580、Price(85)=10180、Price(86)=10300 两两不等 | 原来那一条存在的目的是「让重新报价能被抓到」。现在能被抓到的错误报价有两种（结算时的角色等级、翻牌时的角色等级），所以三个数字两两不等才是原来那一条的等价物。 |
| `funded = announced + afterTheAward` | `funded = 580 + 10180 + 10300` | 同上：账户要付得起任意一个错误报价，失败才由「收了多少」而不是由「余额不足」报出来。 |
| `Assert.Equal(announced + afterTheAward - announced + GoldPaid, Purse())` | `Assert.Equal(funded - announced + GoldPaid, Purse())` | 只是随 `funded` 变，等式含义不变。 |

结论未改：公布价 == 实收价 == 结算那一刻定下的那一个数。判据的 remarks 里写明了
「这个缺陷现在已经不可能再犯，判据仍保留」的理由（换成按副本取价后，角色升级不再能移动这个数；
留着它是为了「有人把键改回角色等级」时，除了讲键的判据以外还有一条讲报价的判据会红）。
变异二证实它仍会红。

**4. 全量判据不低于基线，失败零，跳过 2。** 成立：1955 通过 / 0 失败 / 2 跳过，与基线一致。

**5.（Amendment 1）`[drop prob]` 等级段的裁定。**

**裁定：刻意仍用角色等级。** 理由写在 `ClearRewardOccasion.CharacterLevel` 的 doc 与
`ClearRewardRuleService.ChanceOf` 的 remarks 里，四条：

1. `[drop prob]` 声明的是六个**按玩家场合命名**的 profile（`default` / `event` / `event2` /
   三个 `pcroom`）。整段的轴是「谁在玩、在什么活动下」，profile 内部的等级段是同一根轴往下一层。
2. revision 4 第 6 条援引的证据是 `[mob level charac level replace flag]`——一个把**怪物**等级
   换成角色等级的开关，它反证的是「副本发出去的东西按副本定档」。而「一个位置到底出不出东西」
   既不是档次也不是价钱，那个开关对它无话可说。
3. 改的话会在**本 Task 权限之外**制造一个新的死字段：`ClearRewardSettlement.CharacterLevel`
   （`ICardRewardDomainService.cs`）就再没有读者了，而那个文件不在 Permitted files 里。
   Amendment 6 正是为了清掉这类字段，不该顺手造一个新的。
4. 今天两种读法在真档案上给出完全相同的结果（`default` 四段全是 10000）。

因为第 4 点，这个裁定**必须有判据钉住**，所以新增了
`ClearRewardRuleServiceTests.The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`：
它临时造一张两段费率不同（10000 / 5000）、两个等级各有品级带的表，
断言**几率随角色等级变、对副本等级不动**，而**抽出的件随副本等级变、对角色等级不动**。
变异三（把 `[drop prob]` 的键改成副本等级）确实让它变红——见自检。

**6.（Amendment 1）`ClearRewardOccasion.VisitedMapCount` 的处置。**

**删除。** 理由写在 `ClearRewardOccasion` 的 remarks 里：它是「几率按走过房间比例缩放」被撤掉后
留下的字段，全仓无读者，属于本 Spec 已经栽过两次的「看起来像输入其实到不了任何答案」。
跟着删的：`CardRewardDomainService` 不再传 `run.VisitedCells.Count`；
`CardRewardDomainServiceTests.How_much_of_the_maze_was_walked_comes_off_the_run` 整条删除
（它的主语没了）；`ClearRewardFixture.Occasion` 的 `visitedMapCount` 形参删除；
`ClearRewardArchiveTests` 那条 theory 去掉「走过几间」这一列（16 行到 14 行，去掉的两行是
同尺寸迷宫的重复），并删掉「走一半和走到底一样」的两处断言。
`DungeonRunState.VisitedCells` 本身没动，走过哪些房间仍由 run 持有。

**7.（Amendment 2）`CardRewardDomainServiceTests.cs:158-166` 三处。** 三处都改了：

| 原文 | 现状 | 说明 |
|---|---|---|
| `The rules do read the kind` | `The rules do not read the kind` | revision 4 第 7 条之下规则不读牌型，原文说反了。 |
| 悬空 cref `ClearRewardRuleServiceTests.The_kind_of_a_face_decides_the_profile_and_the_scale_it_is_drawn_at` | 改指两条真实存在的判据：`Neither_the_kind_nor_the_dungeons_level_reaches_the_odds` 与 `One_seed_answers_the_same_face_for_both_kinds` | 都能被 cref 尺解析。 |
| `wherever their two rates agreed` | `always ... rather than merely wherever two rates happened to agree` | 两面恒等，一个种子会让它们永远一样——理由比原文更强。 |

**结论没动**：两面必须用不同种子，判据体一行未改。

尺子结果：`Dfo.Server.Application.Tests.xml` 共 54 个 cref，`cref="!:` **零个**。

**8.（Amendment 3）`ClearRewardRuleService.cs:24` 回折。** 已回折，文字一字未改
（只是在 `lives in <see cref="ClearRewardRules"/>.` 之后断行）。该文件最长行由 **158 到 100** 列。

### 三条 Required checks

| ID | 结果 |
|---|---|
| CK-026 | `--filter PaidCard`：Application.Tests 17 通过 / 0 失败；Infrastructure.Tests 8 通过 / 0 失败。全绿。 |
| CK-027 | 副本 144 `[basis level] = 5`；`[gold card cost table]` 第 5 行 = **580**（= 100 + (5-1)×120，与档案等差数列一致，`PvfClearRewardRateSourceTests` 亦断言 580）；`[item drop ref table]` 第 5 行 = `ItemGradeBand(2, 8)`。三者都在 `One_dungeon_bands_and_prices_alike_for_every_character_level` 里对真档案断言，且抽出的每一件装备都在 (2,8) 带内。对照：角色等级 86 那一行是 10300 与 `ItemGradeBand(79, 89)`。 |
| CK-031 | 1955 通过 / 0 失败 / 2 跳过。与交付前基线相同。 |

### 改动文件

生产代码（3 个，全部在 Permitted files 内）：

- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`

判据（5 个，均为上述三处对应的判据文件）：

- `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs`
- `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs`
- `Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs`
- `Server/tests/Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs`
- `Server/tests/Dfo.Server.Application.Tests/World/DungeonPaidCardSwitchRuleTests.cs`

没有越界改动。

## Executor Self-check — round 1

### 判据数字

| 程序集 | 通过 | 失败 | 跳过 |
|---|---|---|---|
| Dfo.Server.Protocol.Tests | 479 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 51 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 248 | 0 | 0 |
| Dfo.Server.Host.Tests | 4 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 215 | 0 | 2 |
| Dfo.Server.Application.Tests | 898 | 0 | 0 |
| **合计** | **1955** | **0** | **2** |

交付前我自己也跑了一次同样的基线，数字完全相同（1955 / 0 / 2）。

Application.Tests 数目未变（898 到 898）是巧合，账对得上：
新增 3 条（`One_dungeon_bands_and_prices_alike_for_every_character_level`、
`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`、
`The_price_is_asked_at_the_dungeons_level_and_not_the_characters`），
删除 1 条（`How_much_of_the_maze_was_walked_comes_off_the_run`），
theory `The_chance_a_position_carries_is_the_archives_numbers_combined` 由 16 行减到 14 行。
+3 − 1 − 2 = 0。

另有三处改名（结论与断言未改）：
`Neither_the_kind_nor_the_share_walked_reaches_the_odds` 到
`Neither_the_kind_nor_the_dungeons_level_reaches_the_odds`；
`A_level_with_odds_but_no_grade_band_draws_nothing` 到
`A_dungeon_level_with_odds_but_no_grade_band_draws_nothing`；
`A_paid_flip_costs_the_archives_price_for_that_level` 到
`A_paid_flip_costs_the_archives_price_for_the_dungeons_level`。

### 变异检查（真实结果）

手法：施加变异后 `touch` 让 mtime 前进；还原用 `sha256sum -c` 核内容。
三个变异全部**编译通过**后才跑判据，没有一个是「编译失败当成变红」。
三个基线 sha256（每次还原后逐个核过，全部 OK）：

```
507b8c62... ClearRewardRuleService.cs
f1000f92... CardRewardDomainService.cs
a0d87e1a... IClearRewardRuleService.cs
```

**变异一** —— 品级带的键改回角色等级
（`ClearRewardRuleService.FaceFor`：`GradeBandFor(occasion.DungeonBasisLevel)` 改成
`GradeBandFor(occasion.CharacterLevel)`）。**真实结果：红，3 条**：

```
ClearRewardRuleServiceTests.A_dungeon_level_with_odds_but_no_grade_band_draws_nothing [FAIL]
ClearRewardRuleServiceTests.The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons [FAIL]
ClearRewardArchiveTests.One_dungeon_bands_and_prices_alike_for_every_character_level [FAIL]
失败!  - 失败: 3，通过: 895，总计: 898
```

这一条我跑了两次：一次在判据改名前（全量），一次在改名后按名过滤重跑，两次都红；
第二次的失败消息是 `Assert.Contains() Failure: Filter not matched in collection`，
落在品级带那一句上，`ClearRewardArchiveTests.cs:711`。

**变异二** —— 付费价的键改回角色等级
（`CardRewardDomainService`：`PaidFlipCost(run.Layout.BasisLevel)` 改成
`PaidFlipCost(settlement.CharacterLevel)`）。**真实结果：红，7 条**：

```
CardRewardDomainServiceTests.The_settlement_hands_back_what_a_paid_flip_costs [FAIL]
CardRewardDomainServiceTests.A_clear_asked_again_after_a_level_up_keeps_the_price_it_settled_at [FAIL]
CardRewardDomainServiceTests.A_second_settlement_keeps_the_cards_already_fixed [FAIL]
CardRewardDomainServiceTests.The_price_is_asked_at_the_dungeons_level_and_not_the_characters [FAIL]
PaidCardFlipCostTests.A_dungeon_that_declares_no_switch_still_charges_for_a_paid_flip [FAIL]
PaidCardFlipCostTests.A_clear_that_levelled_the_character_still_charges_what_it_announced [FAIL]
ClearRewardArchiveTests.One_dungeon_bands_and_prices_alike_for_every_character_level [FAIL]
失败!  - 失败: 7，通过: 891，总计: 898
```

TASK-07 那条判据（倒数第二行）确实还在守着。

**变异三** —— `[drop prob]` 的等级段改成副本等级
（`ClearRewardRuleService.ChanceOf`：`BandFor(occasion.CharacterLevel)` 改成
`BandFor(occasion.DungeonBasisLevel)`）。**真实结果：红，2 条**：

```
ClearRewardRuleServiceTests.Neither_the_kind_nor_the_dungeons_level_reaches_the_odds [FAIL]
ClearRewardRuleServiceTests.The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons [FAIL]
失败!  - 失败: 2，通过: 896，总计: 898
```

也就是说 Amendment 5 那个「今天看不出差别」的选择现在**两个方向都被钉住**：
把它改成副本等级会红（变异三），把品级带改成角色等级也会红（变异一）。

### 两把尺子

**cref 尺**（`dotnet build <csproj> --nologo -p:GenerateDocumentationFile=true -p:NoWarn=1591
-p:TreatWarningsAsErrors=false`，未改仓里任何文件）：
`Dfo.Server.Application.Tests.xml` 共 **54** 个 cref，`cref="!:` **0** 个。
Amendment 2 要求的「本主题相关的 `!:` 归零」成立。

顺带记录（**不是我造成的，也不在本 Task 范围内**）：同一次构建里
`Dfo.Server.Abstractions.xml` 有三个解析不到的 cref——`!:Carried`、`!:Cleared`、`!:Tracked`，
来自 `Server/src/Dfo.Server.Abstractions/Quests/IQuestStore.cs:31-32`，属于任务系统，与本主题无关。

**列宽尺**：先确认被测集合真的包含我改的文件——下面是
`Server/src/Dfo.Server.Application/World/*.cs` 全部 **23** 个文件逐个的最长行，
`ClearRewardRuleService.cs` 与 `CardRewardDomainService.cs` 都在其中：

```
109 EntryPlacementRuleService.cs      100 MonsterDropService.cs           98 ExperienceBonusService.cs
107 DungeonRunDomainService.cs        100 GroundPickupDomainService.cs    95 DungeonSelectionRuleService.cs
107 DungeonLayoutRuleService.cs       100 ExperienceRuleService.cs        94 MonsterKillDomainService.cs
106 ExperienceBonusRules.cs           100 DropRuleService.cs              92 ExperienceValuationDomainService.cs
104 ExperienceTuningSource.cs         100 ClearRewardRuleService.cs       89 TownMovementDomainService.cs
104 DungeonVisitDomainService.cs       99 CharacterExperienceGrant.cs     77 TutorialRewardRuleService.cs
102 DungeonRoomRuleService.cs          99 CardRewardDomainService.cs      73 TutorialRewardDomainService.cs
101 WorldContentEntryRuleService.cs    98 NoDropRateAdjustment.cs
```

目录最长 109 列，`ClearRewardRuleService.cs` 100 列（改前 158）。

我改的 8 个文件逐个最长行（全部不超过 110）：

```
108 Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs
100 Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs
 99 Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs
103 Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs
107 Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs
107 Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs
105 Server/tests/Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs
102 Server/tests/Dfo.Server.Application.Tests/World/DungeonPaidCardSwitchRuleTests.cs
```

中间一度有两行 112 列（我新写的 cref 行），已回折——办法是把判据名从
`One_dungeon_gives_one_band_and_one_price_whatever_level_the_character_is`（70 字符）
改短为 `One_dungeon_bands_and_prices_alike_for_every_character_level`（59 字符），
这样引用它的两行才放得下真 cref 而不是退化成 `<c>`。

**行尾**：八个文件按字节数核，`\r` 计数全部为 **0**，`\n` 分别为
291 / 258 / 138 / 745 / 922 / 531 / 569 / 137。

### 我另外做的一次独立核对

`IClearRewardRuleService` 的 doc 里我要写「`[mob level charac level replace flag]` 由 1047 个副本里的
134 个声明」。这个数字来自控制器的交待，**仓里查不到**，我不想在代码里写没核过的数，
所以在 scratchpad 里写了个一次性小程序（引用 `Dfo.Pvf`，**没有在仓里加任何文件**），
读 `dungeon/dungeon.lst` 再逐个扫 `.dgn`：

```
dungeons listed = 1047
tag resolved = True
declared=134 nonZero=135 zero=0
values: 1x134, 2x1
```

1047 与 134 都对得上；**134 个副本全部声明为非零**（其中有一个文件在该段下写了两个整数，
1 和 2，所以值 token 是 135 个而副本是 134 个）。doc 里写的是「134 个副本声明、且每一个都非零」，
没有把「非零 = 打开」当成档案的声明，只当成按标签名的读法。

### 不确定 / 未查明 / 越界事项

1. **越界（未改，须转出）**：`Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`
   有两处文字因本次改动变成**假话**，但该文件不在本 Task 的 Permitted files 里，我没有动它：
   - `:14` `ClearRewardSettlement.CharacterLevel` 的 doc：
     「Which of the rate table's level bands **and grade bands** this clear is paid at.」
     ——品级带那半句现在是假的（品级带跟副本走）。
   - `:29` `ClearRewardSettlementResult` 的 remarks：
     「It is a fact about the level the clear settled at, and the level a clear settles at is taken
     before the clear's experience is paid; a caller that asked for the price separately would ask
     with whatever level it had to hand, which after the award can be one the run itself bought.」
     ——价钱现在是副本的事实，这一段的因果已不成立（同段后半句 never worked out twice 仍成立）。

   另注：下一个 Task 若把 `[drop prob]` 也改成副本等级，`ClearRewardSettlement.CharacterLevel`
   会彻底失去读者，届时要删的就是它——这也是我这次不改 `[drop prob]` 的第三条理由。

2. **`[drop prob]` 的裁定是一个读法，不是档案的声明。** 档案没说那四个等级段是谁的等级。
   我给的四条理由里第 1 条（profile 按玩家场合命名）最强，但它仍是从命名推的。
   若日后有更硬的证据推翻它，只需改一处（`ChanceOf` 里的一个属性名），
   且变异三证明那一改会被判据抓住。

3. **`One_dungeon_bands_and_prices_alike_for_every_character_level` 用的布局是手搭的**
   （十个房间连成一条线、`StartRoomMapId: 0`），只有 `DungeonId`、`BasisLevel`、
   `ExperienceWeight` 三个事实取自真档案。理由写在判据里：被测的是「查表用哪个等级」，
   迷宫形状与此无关。但这意味着它**不是**一条端到端的真布局判据；
   真布局那一路仍由 `PaidCardFlipCostTests` 与协议层判据覆盖。

4. **`ClearRewardOccasion` 改动后仍是 6 个位置参数**（删了 `VisitedMapCount`、加了
   `DungeonBasisLevel`），所以旧的位置调用**能静默编译成新的含义**。
   我把仓里全部 8 处 `new ClearRewardOccasion(...)` 逐个人工核过并改写，
   其中参数最多的那条 theory 改成了具名实参。这是本次最容易漏的地方，
   复核者若只信编译器会漏掉它——建议重点看这 8 处。

5. **`PaidCardFlipCostTests.Flip` 构造函数的 `funded` 参数一直没有 `<param>`**
   （开 `GenerateDocumentationFile` 时报 CS1573）。**这是既有情况，不是我引入的**，
   正常构建不开文档生成所以看不见。我没改，因为它与本主题无关。

6. **协议层 `CardRewardTests.cs` 里的 `IClearRewardRuleService` 桩仍把形参叫
   `characterLevel`**。C# 不要求实现的形参名与接口一致，编译与判据都没问题，
   但读起来会误导。那个文件不是本 Task 三处源文件对应的判据文件，我没有动它。

## Contract Amendment 4 — 两处因本次改动变成假话的散文（本条写在改动之前）

执行者报了、没自行扩界，对。控制器已逐句核实原文，**两处都确实成了假话**。

**Permitted files 增补**：`Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`，
**仅下述两段散文**，不得改任何签名、逻辑或其他段落。

1. **`:13-15`** —— `<param name="CharacterLevel">` 说它决定
   「the rate table's level bands **and grade bands**」。**品级带现在归副本**，
   角色等级只剩阈值档那一半（且那一半是本 Task 显式裁定「刻意仍用角色等级」的）。

2. **`:27-32`** —— 整段论证的是「价钱是结算时那个等级的事实，
   而结算等级取在发经验之前，否则调用方会拿升级后的等级去问价」。
   **按副本基准等级取价之后，这件事不可能发生**：副本等级不随角色升级而变。

### 值得单独记的一件事

`:27-32` 论证的正是 TASK-07 查出并修掉的那个真缺陷——公布价与实收价在跨段升级时分歧。
**那个缺陷现在不存在了**，不是因为修法失效，而是因为 revision 4 换了取价的键，
**问题的前提被拿掉了**。

所以：**修的东西没错，理由过期了。** 判据（同一局升级后再翻牌，收的价与公布价相同）
按控制器先前的要求保留——它现在钉的是一条更弱但仍为真的性质，而且是免费得到的。
改写这两段时**不要把那条判据一起删掉**。

**收尾**：改完用 cref 尺证明 Abstractions 程序集与本主题相关的 `!:` 仍为零。

## Execution Result — round 2

### 范围

只改 `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs` 的两段散文，
按 Amendment 4 授权。**没有动任何签名、逻辑或其他段落**，全仓其余文件一字未改。

### 第一处 —— `:13-15`，`ClearRewardSettlement.CharacterLevel` 的 param

原文：

```
/// Which of the rate table's level bands and grade bands this clear is paid at. Handed in, because
/// what the caller knows is a character and what the settlement knows is a run.
```

「and grade bands」是假的：品级带现在跟副本的 `[basis level]` 走。改后：

```
/// Which of the threshold profile's level bands this clear is drawn against, and nothing else.
/// Handed in, because what the caller knows is a character and what the settlement knows is a run.
/// <para>
/// <b>Not the grade band, and no longer the price.</b> Both of those are reached by the dungeon's
/// own <c>[basis level]</c>, which the run carries and this record therefore does not: see
/// <see cref="ClearRewardOccasion.DungeonBasisLevel"/> for the reading and for the gear tap the
/// other way round opened. That this one band is still the character's is itself a decision and
/// not a leftover -- it is argued on <see cref="ClearRewardOccasion.CharacterLevel"/>, and it is
/// held down by a judgement that would go red if it were quietly changed either way.
/// </para>
```

三点：删掉假的那半句；说清剩下的那一半是**阈值档**那一个；并且点明「这一半仍归角色」
是本 Task 的显式裁定而不是漏改——后一句「两个方向改都会红」不是修辞，
round 1 的变异一（品级带改回角色等级）与变异三（阈值档改成副本等级）分别红了 3 条与 2 条。
`Handed in, because...` 那一句原样保留。

### 第二处 —— `:27-32`，`ClearRewardSettlementResult` 的 remarks 首段

原文的论证：价钱是「结算时那个等级」的事实，而结算等级取在发经验之前，
否则调用方会拿升级后的等级去问价。**按副本基准等级取价之后这不可能发生**——副本等级不随角色升级而变。

改后拆成三段（第三段 `Which is also why it is never worked out twice...` 是**原有段落，一字未动**）：

1. 首段换成价钱现在是什么的事实：它是**副本**的事实，是 `[gold card cost table]` 里
   该副本 `[basis level]` 够到的那一行；「调用方不该自己去问」的结论保留，
   但理由换成「要问对就得先拿到 run，而拿着 run 是这道缝的活」。
2. 新增一段专门记这件事：**修的东西没错，理由过期了。**
   原文描述的分歧是真的、TASK-07 查出并修掉的也是真的，
   走掉的是**前提**（副本不会升级，所以不再存在第二个数字让两者分歧），不是修法。
3. 新增一段点名那条判据仍在：
   `PaidCardFlipCostTests.A_clear_that_levelled_the_character_still_charges_what_it_announced`，
   并写明它现在钉的是一条更弱的性质、是白得的，
   以及**它还能抓什么**——「有人把这个键改回角色等级」，那是老缺陷唯一的回来路径。
   （round 1 变异二证实：把 `PaidFlipCost` 的键改回 `settlement.CharacterLevel`，这条会红。）

按控制器要求，**判据没有删、没有改**，本轮没有碰任何判据文件。

### 收尾检查

| 项 | 结果 |
|---|---|
| 全量判据 | **1955 通过 / 0 失败 / 2 跳过**，与 round 1 及基线一致 |
| cref 尺（Abstractions） | 共 **186** 个 cref；`cref="!:"` 三个，全部是既有的 `!:Carried` / `!:Cleared` / `!:Tracked`，来自 `Server/src/Dfo.Server.Abstractions/Quests/IQuestStore.cs:31-32`（任务系统，与本主题无关）。**与本主题相关的 `!:` 为零** |
| 我改的两个成员的 cref | 全部解析成 `P:` / `M:`，无一个 `!:`（见自检） |
| 列宽 | 该文件最长行 99 到 **101** 列，仍 ≤110 |
| 行尾 | `\r` 计数 **0**，`\n` 由 112 增至 136 |

## Executor Self-check — round 2

### 控制器点名要的那笔账：1955 为什么没变

先把口径说清：xUnit 的「总计」数的是**用例**，一个 `[InlineData]` 行算一个用例。
所以「判据方法数」和「用例数」是两笔账，两笔都要对平。

**方法一级：+3 − 1 = +2**

| 增减 | 判据 | 所在 |
|---|---|---|
| + | `One_dungeon_bands_and_prices_alike_for_every_character_level` | `ClearRewardArchiveTests` |
| + | `The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons` | `ClearRewardRuleServiceTests` |
| + | `The_price_is_asked_at_the_dungeons_level_and_not_the_characters` | `CardRewardDomainServiceTests` |
| − | `How_much_of_the_maze_was_walked_comes_off_the_run` | `CardRewardDomainServiceTests` |

三条新的都是 `[Fact]` / `[SkippableFact]`，各 1 个用例；删掉的那条是 `[Fact]`，1 个用例。
方法一级净 **+2 个用例**。

现场核过（不是凭记忆）：

```
$ grep -rn "How_much_of_the_maze_was_walked_comes_off_the_run" --include=*.cs Server/ | wc -l
0
$ dotnet test ... --filter "<三条新判据的 FQN 或起来>"
已通过! - 失败: 0，通过: 3，已跳过: 0，总计: 3
```

**用例一级（theory 行）：−2**

`ClearRewardArchiveTests.The_chance_a_position_carries_is_the_archives_numbers_combined`
原来是 `(level, difficulty, rooms, roomsEntered, party, expected)` 六列 **16 行**，
删掉「走过几间」这一列之后，有些行变成了字面重复，必须合并；同时我补了一行。逐条：

| 变动 | 原行 | 现行 | 说明 |
|---|---|---|---|
| −1 | `(86,3,10,10,1)` 与 `(86,3,10,5,1)` | `(86,3,10,1)` | 去掉走过数后两行完全相同，合并 |
| −2 | `(86,3,4,4,1)`、`(86,3,4,2,1)`、`(86,3,4,1,1)` | `(86,3,4,1)` | 三行完全相同，合并 |
| +1 | 无 | `(20,3,5,1)` | 新增：让 profile 的第二个等级段（16-30）也被走到 |
| 0 | 其余 11 行 | 其余 11 行 | 只去掉一列，逐行一一对应 |

−1 − 2 + 1 = **−2**，16 → 14。现场核过：

```
$ grep -c "^    \[InlineData(" ClearRewardArchiveTests.cs
14
$ dotnet test ... --filter "FullyQualifiedName~The_chance_a_position_carries_is_the_archives_numbers_combined"
已通过! - 失败: 0，通过: 14，已跳过: 0，总计: 14
```

**合并：+2 − 2 = 0。** 898 → 898，全量 1955 → 1955。

**账里没有藏东西的三条旁证**：

1. 被合并掉的三行**不是覆盖被删**，是同一组实参重复了。去掉的那一列（走过几间）
   本来就是「变它不改答案」的列——它的存在意义随 `VisitedMapCount` 字段一起没了。
2. 覆盖面净增而不是净减：新增的 `(20,3,5,1)` 让 `[drop prob]` 四个等级段被走到三个
   （15 落第一段、20 落第二段、86 落第四段），原先只有两个（15 与 86）。
3. 三条新判据每一条都被变异证过会红（变异一/二/三），不是凑数。

### 本轮改动的核对

改动文件仅一个：`Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`。

```
改前 sha256: 5b81f13209fd4cf8cd9771d0f507995a2026453804474c7729347b20df42e1cb
改后 sha256: 01b032c307057e276da236cd681f71270b72890436dc36991c22c98c1421338f
行数 112 -> 136（两段散文各多出一个 <para>），最长行 99 -> 101 列，\r 计数 0
```

**只动了授权的两段**：其余段落（`ClearRewardSettlement` 的 `<remarks>`、
`PartyMemberCount` 的 param、`Which is also why it is never worked out twice` 那一段、
`<param name="Cards">`、`<param name="PaidFlipCost">` 以下全部）逐行比对未改；
`public sealed record ClearRewardSettlement(...)` 与
`public sealed record ClearRewardSettlementResult(...)` 两处签名未动。

中途我用 `sed` 单行替换想微调一句措辞，结果把撇号打成了句点、还吞掉了半句
（`the character.s level` / 丢了行首的 `The`）。**没有将错就错**：
从改前备份整份还原、把整段重写好再重新拼接，之后才有上面那个改后 sha256。
记在这里是因为「改一处错话时写出新错话」正是这次要防的事，我确实差点犯了。

### cref 尺（Abstractions）

```
$ dotnet build Server/src/Dfo.Server.Abstractions/Dfo.Server.Abstractions.csproj --nologo \
    -p:GenerateDocumentationFile=true -p:NoWarn=1591 -p:TreatWarningsAsErrors=false
0 errors
total crefs: 186
unresolved: 1 cref="!:Carried"   1 cref="!:Cleared"   1 cref="!:Tracked"
```

三个 `!:` 全部来自 `Server/src/Dfo.Server.Abstractions/Quests/IQuestStore.cs:31-32`（任务系统），
round 1 就已经在报告里记过，与本主题无关，本轮未新增也未减少。

我改的两个成员，XML 里的 cref 逐个都解析到了：

```
T:...ClearRewardSettlement
  P:Dfo.Server.Abstractions.World.ClearRewardOccasion.CharacterLevel
  P:Dfo.Server.Abstractions.World.ClearRewardOccasion.DungeonBasisLevel
  P:Dfo.Server.Abstractions.World.MonsterKill.PartyMemberCount
T:...ClearRewardSettlementResult
  M:...ICardRewardDomainService.SettleAsync(...)
  M:...IClearRewardRuleService.OffersPaidFlipAsync(System.Int32,System.Threading.CancellationToken)
  M:...IClearRewardRuleService.PaidFlipCost(System.Int32)
  P:...ClearRewardCardSet.PaidFlipCost
  T:...ClearRewardSettlementResult
```

新引的两个 `ClearRewardOccasion.CharacterLevel` / `DungeonBasisLevel` 都是 `P:`，没有退化成 `!:`。

判据名 `PaidCardFlipCostTests.A_clear_that_levelled_the_character_still_charges_what_it_announced`
写成 `<c>` 而不是 `<see cref>`——生产程序集引用不到判据程序集，只能如此；
这一处 cref 尺保护不到，是已知的取舍。

### 一处要更正 round 1 自检的记录

round 1 自检里列的三个「基线 sha256」（`507b8c62` / `f1000f92` / `a0d87e1a`）
是**做变异那一刻**的值，`sha256sum -c` 的 OK 也是针对那一刻。
之后我在 round 1 内又改了三处 doc 措辞（`IClearRewardRuleService` 两处、
`ClearRewardRuleService` 一处，都是注释、无逻辑），所以**交付态的哈希与那三个不同**：

```
IClearRewardRuleService.cs   9d99a2e8...
ClearRewardRuleService.cs    78b56d1e...
CardRewardDomainService.cs   973df46a...
```

复核者按 round 1 的那三个去核会对不上，所以在这里讲明。
变异一在那三处措辞改完之后**又单独重跑过一次**（用当时新取的哈希核的还原，OK，判据仍红）；
变异二与变异三跑的是与交付态**只差注释**的代码，未在交付态重跑。
若要求三个变异都在交付态复现，我可以再跑一轮。

### 仍然成立的 round 1 遗留事项

round 1 自检里第 2、3、4、5、6 条（`[drop prob]` 裁定是读法而非声明、
archive 判据用的是手搭布局、`ClearRewardOccasion` 六个 int 位置参数的静默编译风险、
`Flip` 构造函数 `funded` 缺 `<param>` 的既有 CS1573、协议层桩的形参名仍叫 `characterLevel`）
本轮都没有变化。第 1 条（越界项）已由 Amendment 4 授权并在本轮做掉，可以销掉了。

## Independent Quick Review — round 1

**Verdict: passed**

复核者未继承执行者的任何结论。控制器已独立复核过的四项（全量 1955/0/2、8 处
`ClearRewardOccasion` 构造点、`ICardRewardDomainService.cs` 两段散文逐句、归档事实
`[basis level] 5` 与 580）未重做。下面每一条都是本次自己跑、自己数、自己读出来的。

### 一、交付态重跑变异（做了三条，不是两条）

先取交付态基线哈希，与 round 2 自检写的三个**完全相同**，另加 round 2 改的那一个：

```
9d99a2e8924369468846ef09486baf3784cf118394e0f278f12c749abf47d4c0  IClearRewardRuleService.cs
78b56d1e031bf8ad781ac7944e2f0ad93494a44918d0777502c9d5ff9b7e3854  ClearRewardRuleService.cs
973df46a12a0d983100eebee32276e9373af9a20a9c8db03ecf5fcfa583bf622  CardRewardDomainService.cs
01b032c307057e276da236cd681f71270b72890436dc36991c22c98c1421338f  ICardRewardDomainService.cs
```

交付态基线：`Dfo.Server.Application.Tests` **898 通过 / 0 失败 / 0 跳过**。
三条变异一律 `touch` 让 mtime 前进，还原一律 `sha256sum -c` 核内容；
每一次都先确认九个程序集全部产出了 dll、输出里没有 `error CS`，再看失败数——
**没有一条是把编译失败当成变红**。

| 变异 | 施加处 | 真实结果（交付态） | 与执行者所报 |
|---|---|---|---|
| **二** 付费价的键改回角色等级 | `CardRewardDomainService.cs:68`，`PaidFlipCost(run.Layout.BasisLevel)` 改成 `PaidFlipCost(settlement.CharacterLevel)` | **红，7 条** | 完全一致，逐条同名 |
| **三** `[drop prob]` 阈值档的键改成副本等级 | `ClearRewardRuleService.cs:105`，`BandFor(occasion.CharacterLevel)` 改成 `BandFor(occasion.DungeonBasisLevel)` | **红，2 条** | 完全一致，逐条同名 |
| **一**（额外补做） 品级带的键改回角色等级 | `ClearRewardRuleService.cs:196`，`GradeBandFor(occasion.DungeonBasisLevel)` 改成 `GradeBandFor(occasion.CharacterLevel)` | **红，3 条** | 完全一致，逐条同名 |

变异二的 7 条：`CardRewardDomainServiceTests` 的
`The_settlement_hands_back_what_a_paid_flip_costs`、
`The_price_is_asked_at_the_dungeons_level_and_not_the_characters`、
`A_second_settlement_keeps_the_cards_already_fixed`、
`A_clear_asked_again_after_a_level_up_keeps_the_price_it_settled_at`；
`ClearRewardArchiveTests.One_dungeon_bands_and_prices_alike_for_every_character_level`；
`PaidCardFlipCostTests` 的 `A_dungeon_that_declares_no_switch_still_charges_for_a_paid_flip`
与 **`A_clear_that_levelled_the_character_still_charges_what_it_announced`**（TASK-07 那条）。

变异三的 2 条：`ClearRewardRuleServiceTests` 的
`Neither_the_kind_nor_the_dungeons_level_reaches_the_odds` 与
`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`。

变异一的 3 条：`A_dungeon_level_with_odds_but_no_grade_band_draws_nothing`、
`The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`、
`One_dungeon_bands_and_prices_alike_for_every_character_level`。

**补做变异一的理由**：执行者 round 1 重跑变异一是在 round 1 的措辞改动之后，
但 round 2 又改了 `ICardRewardDomainService.cs`，所以它同样不是在最终交付态跑的。
既然是为堵变异证据的口子而来，三条一并在同一个交付态跑完才算堵住。

**还原证明**：每条变异后 `sha256sum -c` 八个文件全部 OK；最后一次还原后
`Dfo.Server.Application.Tests` 回到 **898/0/0**，全量回到
**1955 通过 / 0 失败 / 2 跳过**（479 + 60 + 51 + 248 + 4 + 898 + 215），退出码 0。

### 二、判据增减自己对平

全部在交付态实测，不采信报告里的数字：

| 项 | 我的测法 | 结果 |
|---|---|---|
| 三条新判据 | `--filter` 三个 FQN 或起来 | 通过 3，总计 **3**（各 1 个用例） |
| 删掉的那条 | `grep -rn` 同时搜 `How_much_of_the_maze_was_walked`、`VisitedMapCount`、`visitedMapCount`、`roomsEntered` | **0 命中**（连词汇都不剩） |
| theory 行 | `--filter FullyQualifiedName~The_chance_a_position_carries_is_the_archives_numbers_combined` | 通过 14，总计 **14** |
| 程序集总数 | 整个 csproj | **898** |

+3 − 1 − 2 = 0，898 到 898。账对得平。

**「被合并掉的行是重复还是覆盖被删」——三条独立佐证：**

1. **去掉的那一列在生产代码里根本到不了任何答案。** `ClearRewardOccasion` 没有走过数成员，
   `ClearRewardRuleService.ChanceOf`（:100-116）只读 `CharacterLevel`、`Difficulty`、
   `PartyMemberCount`、`MapMaxCount` 四项。所以删这一列**不可能**删掉合成读得到的任何覆盖。
2. **合并不是选择，是被逼的。** `Server/Directory.Build.props:6` 是
   `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`，而 `xunit` 包带 `xunit.analyzers`，
   xUnit1025（同一 Theory 内 InlineData 必须唯一）是默认 warning——去掉那一列后
   **留着字面重复的行会直接编不过**。
3. **剩下五列的覆盖没有净减，反而净增。** 现存 14 行覆盖 rooms {1,4,5,6,10}、
   difficulty {0,1,3,4}、party {1,2,3,4}、level {15,20,86}。新增的 `(20,3,5,1)` 那一行
   我按归档独立核过：`PvfClearRewardRateSourceTests.cs:126-129` 断言六个 profile 的四段
   一律是 `(1,15) (16,30) (31,40) (41,200)`，所以 15 / 20 / 86 分落第 1 / 2 / 4 段——
   原先只走到第 1、4 两段，现在走到三段。执行者的说法成立。

**一处非阻塞的证据缺口（不是本 Task 造成的）**：本 Spec 的**全部**源文件在 git 里都是未跟踪
（`git status` 里是 `??`），也没有 stash，所以**任何复核者都无法 diff 出改前的文件**。
「原本 16 行、合并掉的是那三行」这一句，是 TASK-11 里唯一一处只能靠执行者交代的事实——
它周围能核的全核过了，行的身份本身核不了。建议后续 Task 落一个可 diff 的基线。

### 三、`[drop prob]` 那条裁定，两个方向都钉住了

`ClearRewardRuleServiceTests.The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons`
（`ClearRewardRuleServiceTests.cs:531-598`）**四个象限全有断言**：

| | 随角色动？ | 随副本动？ |
|---|---|---|
| 几率 | 动：`Rate(10,4)` = 6000，`Rate(90,4)` = 10000 | 不动：`Rate(10,4)==Rate(10,86)`、`Rate(90,4)==Rate(90,86)` |
| 抽出的件 | 不动：`Drawn(10,4)==Drawn(90,4)==11`、`Drawn(10,86)==Drawn(90,86)==8686` | 动：`Drawn(*,4)` = 11，`Drawn(*,86)` = 8686 |

判据不空转：`ClearRewardFixture.LowestDifficultyRate` = **6000**，`Basis` = **10000**，
两个期望值确实是两个不同的数（表里两段费率 10000 / 5000 是差别的来源）。

**「改成副本等级会不会红」——实测会**（变异三，本条在那 2 条里）：
`Assert.Equal(Basis, Rate(HighBandLevel=90, LowGradeLevel=4))` 变成拿副本等级 4 去查段，
落回第一段的 10000 门槛，答 6000，不等于 10000。
**反方向也红**（变异一，本条在那 3 条里）：品级带改用角色等级后，
`Drawn(10, 4)` 要去查 10 级的品级带（表里只声明了 4 与 86），查不到就整面空白，`null` 不等于 11。

同被变异三红掉的 `Neither_the_kind_nor_the_dungeons_level_reaches_the_odds`（:140-169）
也是真断言而非巧合：它把副本等级推到 **201**，越过 fixture profile 的上界，
改键之后查不到段、几率塌成不可能。

**值得记一句**：这条裁定只能钉在手搭的表上，永远钉不到归档上（A21 的 `default` 四段全是 10000）。
这正是 Amendment 5 要求的做法，但复核者应知道：**没有任何归档判据能反驳它**。

### 四、主交付：品级带与付费价确实只由副本决定

`ClearRewardArchiveTests.One_dungeon_bands_and_prices_alike_for_every_character_level`
（`ClearRewardArchiveTests.cs:655-724`）我逐行读过并单独跑绿。它做的正是控制器要的那件事：

- 先钉归档自己的事实：副本 144 `BasisLevel == 5`、`GoldCardCost(5) == 580`、
  `GradeBandFor(5) == ItemGradeBand(2, 8)`；
- **再钉「那两个角色等级本身确实不同」**：`GoldCardCost(86) == 10300`、`GoldCardCost(20) == 2380`，
  且两者的品级带都 `NotEqual` 5 级那一行——所以后面的「相同」不是两边都没查表；
- 然后 8 个种子乘 2 个角色等级：价钱全相同且等于 580，两面牌逐位指纹相同，
  每一件抽出的装备都断言落在 `(2, 8)` 带内，末尾 `drawn > 0` 防空转。

**下一层独立复核**（不经 Application 层）：`PvfClearRewardRateSourceTests.cs:230-235` 断言
5 级 580、86 级 10300，并对**每一级**断言 `100 + (level-1)*120`（故 20 级 = 2380）；
`:343-344` 断言 `GradeBandFor(5) == (2,8)`、`GradeBandFor(86) == (79,89)`。
价钱与品级带两个事实各有两层判据。

**再加一条不靠判据的结构性确认**（自己走的调用图）：

- `Server/src/` 里 `PaidFlipCost(` 只有三处：声明（`IClearRewardRuleService.cs:256`）、
  实现（`ClearRewardRuleService.cs:148`）、**唯一调用方** `CardRewardDomainService.cs:68`，
  传的是 `run.Layout.BasisLevel`。没有漏网的第二处取价。
- 翻牌费率表的 `GradeBandFor(` 只有一处调用：`ClearRewardRuleService.cs:196`，
  传 `occasion.DungeonBasisLevel`。（`DropRuleService.cs:102` 是怪物掉落表的另一条路，无关。）
- `settlement.CharacterLevel` 全仓只有**一个**读者：`CardRewardDomainService.cs:114`，
  喂给 occasion 的阈值档，此外无处可去。所以 `ClearRewardSettlement.CharacterLevel`
  **不是死字段**，而改后 `:13-23` 那句 “and nothing else” 是字面为真的。
- `Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs:276-279`：
  翻牌收的是 `settled.PaidFlipCost`，**没有任何一处在翻牌时重新算价**——
  「公布价等于实收价」在结构上就成立，不只是判据上成立。

### 五、越界：没有

`find Server protocol Docs .rks-agent -type f -newermt "2026-09-01 22:00"`（排除 bin/obj）
在 TASK-11 的时间窗内**只有这些**：

```
22:36 PaidCardFlipCostTests.cs           22:45 IClearRewardRuleService.cs
22:38 DungeonPaidCardSwitchRuleTests.cs  22:46 ClearRewardArchiveTests.cs
22:43 ClearRewardRuleServiceTests.cs     22:58 ICardRewardDomainService.cs（round 2）
22:43 CardRewardDomainServiceTests.cs    另加两个 Application 生产文件（mtime 现为我还原变异所致）
```

窗口之外的 `ClearRewardRules.cs`（22:13）早于 TASK-11 起手，属 TASK-10（其任务文件 22:15 写就）。
八个文件全部在授权内：契约点名的 3 个生产文件、与之对应的 5 个判据文件、
Amendment 4 的 `ICardRewardDomainService.cs`。

**特别核过一处**：round 1 报告提到改了 `ClearRewardFixture.Occasion` 的形参，
而「改动文件」清单里没有 fixture 文件——查证 `ClearRewardFixture` 就定义在
`ClearRewardRuleServiceTests.cs` 内（`:783-796`），**不是第九个文件**，不算越界。

Amendment 4 的「仅两段散文」：两个 record 的签名（`:29-30`、`:87`）完好且正是调用方与判据在用的，
`Which is also why it is never worked out twice` 那一段在 `:57-61` 原样健在，
`PartyMemberCount` / `Cards` / `PaidFlipCost` 三个 param 块读起来未动，文件 136 行与所报
112 到 136 相符。**限度**：该文件同样未跟踪，「其余句子一字未改」是读出来的、不是 diff 出来的。

### 六、cref 尺（自己跑，未改仓里任何文件）

`dotnet build <csproj> --nologo -p:GenerateDocumentationFile=true -p:NoWarn=1591
-p:TreatWarningsAsErrors=false`，三个程序集：

| 程序集 | cref 总数 | `cref="!:` |
|---|---|---|
| `Dfo.Server.Application.Tests` | 55 | **0** |
| `Dfo.Server.Application` | 39 | **0** |
| `Dfo.Server.Abstractions` | 189 | **3** |

**剩余清单（全部三个，均与本主题无关）**：`!:Carried`、`!:Cleared`、`!:Tracked`，
来自 `Server/src/Dfo.Server.Abstractions/Quests/IQuestStore.cs:31-32`
（编译器原话 CS1574，位置 31,16 / 31,42 / 32,16），属任务系统。
**与本主题相关的 `!:` 为零**，Amendment 2 第 7 条与 Amendment 4 的收尾都成立。

记账小差异（非阻塞）：我数到 55 / 189，执行者报的是 54 / 186。189 减 186 恰好等于那 3 个未解析的，
所以执行者多半只数了解析成功的那些；55 与 54 是同类的计数口径差。结论数字（本主题 `!:` 为 0）一致。

### 七、TASK-07 那条判据：还在，还绿，还咬得动

`PaidCardFlipCostTests.A_clear_that_levelled_the_character_still_charges_what_it_announced`
在 `PaidCardFlipCostTests.cs:197`，**原名未改**，单独过滤跑 **1 通过 / 0 失败**。
它仍然钉着契约要求的那件事：`:219` `Assert.Equal(announced, world.Announced)`（结算时公布的价），
`:225` `Turn(Paid, 0, standingAt: 86)`（升级之后再翻），`:228`
`Assert.Equal(announced, result.GoldCharged)`（实收）——跨升级，公布价等于实收价。
`:208-210` 三条 `Assert.NotEqual` 先把三个价钱两两钉开，让上面的相等有东西可失败。
**变异二实测它会红**，不是摆设。

### 非阻塞观察（不影响 verdict）

1. **本 Spec 全部文件未跟踪，无任何可 diff 的基线**（见第二节末）。这是全 Spec 级的证据缺口，
   不是 TASK-11 引入的，但 TASK-11 的「16 到 14」正好撞上它。
2. `Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs:1173`
   的桩仍写作 `public int? PaidFlipCost(int characterLevel)`。编译与判据都没问题，
   但它现在是**全仓唯一**把这个实参叫作角色等级的地方。不在本 Task 权限内，应转给该文件的归属 Task。
3. `ClearRewardRuleServiceTests.What_a_paid_flip_costs_is_the_tables_own_price` 钉住了
   `PaidFlipCost` 只有一个形参，但**没有钉住形参名**。而形参名正是接口这一层区分两种读法的唯一线索，
   第 2 条说明它已经在别处漂了。
4. `PaidCardFlipCostTests.Flip` 构造函数的 `funded` 仍无 `<param>`（开文档生成时 CS1573）——
   既有情况，与本主题无关，执行者已记。
5. `[drop prob]` 的裁定只能钉在手搭表上（见第三节末）。做法正确且已写明，属需长期知晓的事实。

## Controller Disposition — 评审 `passed`，本 Task 置 `done`

评审在**交付态**重跑了三条变异（红 7 / 红 2 / 红 3），逐条与所报同名。
它自己补做了第三条：理由是变异一虽在 round 1 内重跑过，但 round 2 之后又改了
`ICardRewardDomainService.cs`，**三条要在同一个交付态跑完才算真堵住**。这个判断是对的。

判据账它自己对平了，并给了执行者没给的一条硬证据：**那两组 theory 行的合并不是选择而是被逼的**
——`Directory.Build.props:6` 是 `TreatWarningsAsErrors=true`，xunit 的 xUnit1025
会让字面重复的 InlineData 直接编不过。这比「我判断它们是重复」强一个量级。

主交付它也没停在判据上：`PaidFlipCost` 全仓唯一调用方传 `run.Layout.BasisLevel`、
翻牌费率表的 `GradeBandFor` 唯一调用传 `DungeonBasisLevel`、
`settlement.CharacterLevel` 唯一读者只喂阈值档——**结构上确认，不只是判据上确认**。

**TASK-11 置 `done`。**

### 观察一（谁都 diff 不出改前文件）——控制器已办

评审指出：本 Spec 全部文件在 git 里未跟踪、无 stash，**任何复核者都无法 diff 出改前状态**，
「原本 16 行、合并的是那三行」是本 Task 唯一只能靠执行者交代的事实。

**这是流程上的真缺口，不是本 Task 的问题**：Task Unit 一律不提交，于是新增文件全程未跟踪。
控制器已在验收本 Task 的同时，把 63 个受影响文件快照到
`scratchpad/baseline-after-TASK-11`。**TASK-12 与 TASK-09 的评审员可据此 diff。**
此后每个 Task 验收时照此办理。

### 观察二、三转 TASK-12

- `CardRewardTests.cs:1173` 的桩仍写 `PaidFlipCost(int characterLevel)`
  ——**全仓唯一还把这个实参叫角色等级的地方**。
- `What_a_paid_flip_costs_is_the_tables_own_price` 钉了形参个数却没钉形参**名**，
  而形参名正是接口层区分两种读法的唯一线索；上一条恰好说明它已经在别处漂了。

### 观察四、五不办，记录备查

cref 计数口径差（189−186 恰是那三个未解析的，结论一致）；
`[drop prob]` 的裁定只能钉在手搭表上、永远钉不到归档——做法正确且已写明。
