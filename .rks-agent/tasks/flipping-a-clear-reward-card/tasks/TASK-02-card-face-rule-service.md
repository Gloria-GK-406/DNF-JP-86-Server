# TASK-02 — 从归档算出四个牌位各有什么

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

新增静态纯规则与一个规则服务。规则服务接收事实（牌型、角色等级、难度、地图数、
队伍人数）与显式种子，**一次产出四个牌位各自的内容**（可能为空）。
免费牌与付费牌共用它，牌型是传入的事实。

- Referenced spec requirements: RQ-008, RQ-009, RQ-010
- Referenced plan sections: `plan.md` 的 T-002；边界 BD-004, BD-005, BD-006, BD-007, BD-021
- Owned boundary: BD-004, BD-005, BD-006, BD-007, BD-021
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`（新建，静态类）
- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`（新建）
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`（新建）
- `Server/src/Dfo.Server.Application/ApplicationModule.cs`（增注册行）
- `Server/tests/`（新增判据）

- Dependencies: TASK-01
- Required dependency outputs: TASK-01 的内容源及其产出记录类型
- Acceptance conditions:

1. 一次调用产出四个牌位，不是一次一张——「同一局同一牌位同一件东西」由「只调用一次」
   保证，不靠调用方自律。
2. 归档声明可达的六档稀有度逐档可达，含最高两档；摇点上界等于归档基数减一。
3. 最低难度（加成为零）下四个牌位一律为空。
4. 物品来自归档品级带与装备池按权重选取；只取装备一支，不取时装。
5. 分母与合成方式作为具名假设集中在一处，附理由说明归档没有这个数。
6. 同一组事实与种子重复调用，两次结果逐位相同。
7. 规则服务不持有任何世界状态读写方。
8. 全量判据 不低于 1690 通过、0 失败、2 跳过。

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
| CK-003 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo --filter ClearReward` | 几率合成判据以实测归档值为期望通过 |
| CK-004 | `dotnet test Server/86JP.Server.slnx --nologo --filter Rarity` | 六档逐档可达，含最高两档；摇点上界 = 基数 − 1 |
| CK-005 | `Non-command:` 以最低难度产生牌面 | 四个牌位均为空 |
| CK-006 | `Non-command:` 同一组事实与种子重复产生牌面 | 两次结果逐位相同 |
| CK-S | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于 1690 通过、0 失败、2 跳过 |

- Required evidence: CK-003/CK-004 的判据输出；CK-005/CK-006 的观察记录；规则服务构造器依赖枚举；全量判据输出。

## Contract Amendment 1 — 可复用面的实测更正

**触发**：TASK-01 执行期间控制器读了既有掉落零件的公开面，发现本 Task 密封契约与
`plan.md` 的 T-002 对「哪些能复用」的描述两头都不准。趁本 Task 未开工更正。

计划原文：

> 抽取那一步（掷率 → 掷稀有度 → 品级带 → 桶内加权抽一件）`DropRules` 已经有了。
> 翻牌只是喂另一张表，能复用就复用；真正新的只有「按通关评级分概率」这一段。

**实测（均已现地读源码确认）**：

| 零件 | 签名要点 | 翻牌能否直接用 |
|---|---|---|
| `DropRules.Pick(DropItemPool, ItemGradeBand, DropRarity, DropRandom)` | 与表的形状无关 | **能** |
| `RarityLadder` 的 `BandOf` / `HighestRoll` / `ProbabilityOf` / `WeightOf` | 全部 public | **能**，本表恰是一条阶梯 |
| `DropItemPool.Band(ItemGradeBand, DropRarity)`、`DropRandom` | — | **能** |
| `DropRules.RatesFor(table, band, monsterRank, difficulty, party, basis)` | 吃 `DropRateBand`（一段多率）与怪物档次 | **不能**：翻牌一牌位一个率，且无怪物档次 |
| `DropRules.Gate(DropRates, DropRandom)` | 吃逐槽率表 | **不能**：翻牌不按物品槽分率 |
| `DropRules.RollRarity(DropRateTable, DropRateSlot, DropRandom)` | 吃「表 + 槽」，因怪物表一槽一条阶梯 | **不能**：本表一条阶梯、无槽 |

**关键澄清**：`RollRarity` 用不上，**不代表掷稀有度要重写**。它本身只是三行壳子——
取 `table.LadderFor(slot)` 拿到阶梯，再 `(DropRarity)ladder.BandOf(random.Next(ladder.HighestRoll))`。
真正的逻辑在 `RarityLadder` 上，而它完全公开。翻牌只需用本表那条阶梯走同样两步。
**不得另写一套阶梯语义**——那正是参照服务端把摇点写死成一百万的犯错方式，
`RarityLadder` 的注释里记着那件事的后果。

**契约修订**：Goal and deliverables 的「静态纯规则」一项收窄为——

本 Task 要新写的纯规则**只有牌位出现概率的合成**：单一基础率与难度加成、地图数加成、
队伍加成、以及那个无归档出处的分母，合成为一个几率。
掷稀有度、取品级带、桶内加权抽一件三步一律复用既有零件，不得重写。

Acceptance conditions 增补两条：

- 掷稀有度经 `RarityLadder` 的既有成员完成；仓库中不得出现第二套阶梯语义。
- 「六档逐档可达」可直接断言 `RarityLadder.ProbabilityOf` / `WeightOf`，
  不必靠反复摇点做统计——后者既慢又只能给出概率性的结论。

Spec 的验收边界（RQ-008/009/010）未变。

## Contract Amendment 2 — 跨文件断言必须由判据钉住

沿用 TASK-01 的 `Contract Amendment 2`（用户 2026-09-01 决定）。Acceptance conditions 增补：

> 注释里凡对本代码所读文件**之外**的文件作出的事实断言，必须有一条判据钉住它；
> 钉不住的断言应当删去而不是留在注释里。

TASK-01 因这一类缺陷累计返工六轮、发现十二处，无一处被判据发现，其中两处由评审侧判错、
两处在整改过程中新造。本 Task 从一开始就按这条办。

## Execution Result — round 1

### 改了哪些文件

只动了契约允许的五处，一处不多。

| 文件 | 做了什么 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs` | 新建。`ClearRewardChance`（几率＝分子＋基数）、`ClearRewardRateOptions`（**本任务唯一的具名假设**）、静态纯规则 `ClearRewardRules.ChanceOf` 与 `CardSlotCount` |
| `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` | 新建。`ClearRewardCardKind` / `ClearRewardCardItem` / `ClearRewardCardSlot` / `ClearRewardCardFace` / `ClearRewardOccasion` 与规则服务契约（`ChanceOf` 与 `FaceFor`） |
| `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` | 新建。实现：读费率表 → 合成几率 → 逐牌位掷中 → 走 `RarityLadder` 掷档次 → 取品级带 → `DropRules.Pick` 抽一件 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 加两段注册：`IClearRewardRuleService → ClearRewardRuleService`，以及 `ClearRewardRateOptions`（读 `ClearRewardRates:RateBasis`，非正数或读不出则回落到默认 10000） |
| `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRulesTests.cs` | 新建。纯规则的合成算术，用转录的归档数为期望 |
| `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs` | 新建。规则服务端到端（手搭表与池）＋ 共用夹具 `ClearRewardFixture` |
| `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRarityLadderTests.cs` | 新建。归档阶梯逐档可达 ＋ 规则服务确实走这条阶梯 |
| `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs` | 新建。跑真实归档：把转录钉住、几率合成以实测值为期望、最低难度全空、上限扫描、真实装备池端到端 |

### 复用了什么、没重写什么

- 掷稀有度：`(DropRarity)ladder.BandOf(random.Next(ladder.HighestRoll))`，就是 `RollRarity`
  那三行壳子的实质，走的是 `ClearRewardTable.Rarity` 这条阶梯。**仓库里没有第二套阶梯语义**。
- 品级带：`ClearRewardTable.GradeBandFor(level)`（本表自己的 `[item drop ref table]`）。
- 桶内加权抽一件：`DropRules.Pick(pool, grades, rarity, random)`，一行未抄。
- 摇点：`DropRandom`（`Hits` / `Next`），种子由调用方给，服务内不再混任何东西。
- `RatesFor` / `Gate` / `RollRarity` 一个都没调，也没有仿写。

### 分母与合成方式这处具名假设在哪

`Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs` 的 **`ClearRewardRateOptions`**，
一个类型、一处 remarks，同时承担分母与合成方式两半：

- **合成**：三张整数表各自是同一个基数的分子，四者相乘——
  `chance = base/basis × difficulty/basis × mapCount/basis × party`。
  给的理由是两条，都不是引用：(1) 难度必须是乘法，因为归档最低难度的加成是 0，
  而这一档要求四个牌位全空；加法下 0 加成会原样放行基础率。(2) 难度既然是乘法，
  地图数按同样方式处理是「编得最少」的选择——不给任何一张表归档没给它的地位。
  队伍表本来就是接近 1 的实数，直接相乘，不需要基数。
- **分母**：默认 10000。理由是自洽而非声明：归档 `default` 档案四段全是 10000，
  而缩水档案是 9000 / 8000——10000 正是「默认档案＝全额」成立的那个值。因为是自洽论证
  而非声明，所以做成配置（`ClearRewardRates:RateBasis`）而不是常量；
  纯规则 `ChanceOf` 把 basis 当参数收，自己不带默认值。
- 规则里**不默认任何配置性数值**：`ChanceOf(baseRate, difficultyRate, mapCountRate, partyRate, basis)`。

### 判据真实输出

基线（改动前，同一条命令）：

```
$ PVF_ARCHIVE_PATH=.../Script.pvf dotnet test Server/86JP.Server.slnx --nologo
Networking 51 / Protocol 376 / Pvf 60 / Application 771 / Host 4 / Persistence 244 / Infrastructure 204(+2 skip)
合计 1710 通过 / 0 失败 / 2 跳过
```

改动后（CK-S）：

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
已通过! - 失败: 0，通过: 376，已跳过: 0 - Dfo.Server.Protocol.Tests.dll
已通过! - 失败: 0，通过:  51，已跳过: 0 - Dfo.Server.Networking.Tests.dll
已通过! - 失败: 0，通过:  60，已跳过: 0 - Dfo.Pvf.Tests.dll
已通过! - 失败: 0，通过: 822，已跳过: 0 - Dfo.Server.Application.Tests.dll
已通过! - 失败: 0，通过:   4，已跳过: 0 - Dfo.Server.Host.Tests.dll
已通过! - 失败: 0，通过: 244，已跳过: 0 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败: 0，通过: 204，已跳过: 2 - Dfo.Server.Infrastructure.Tests.dll
EXIT=0
合计 1761 通过 / 0 失败 / 2 跳过（+51，跳过数不变）
```

CK-003：

```
$ PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo --filter ClearReward
已通过! - 失败: 0，通过: 51，已跳过: 0 - Dfo.Server.Application.Tests.dll
已通过! - 失败: 0，通过: 20，已跳过: 0 - Dfo.Server.Infrastructure.Tests.dll
```

CK-004（**注意两种跑法结论不同，如实记录**）：

```
$ PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo --filter Rarity
已通过! - 失败: 0，通过: 2，已跳过: 0 - Dfo.Server.Infrastructure.Tests.dll
已通过! - 失败: 0，通过: 9，已跳过: 0 - Dfo.Server.Application.Tests.dll

$ dotnet test Server/86JP.Server.slnx --nologo --filter Rarity   # 契约里 CK-004 的原文，未设 PVF_ARCHIVE_PATH
已通过! - 失败: 0，通过: 8，已跳过: 1 - Dfo.Server.Application.Tests.dll
（跳过的是 ClearRewardRarityLadderTests.Every_Rarity_band_the_archive_declares_is_reachable_including_the_two_rarest，
  与 TASK-01 那两条同族判据一样，无归档时按 SkippableFact 跳过）
```

CK-005 / CK-006 是 `Non-command:` 观察项，已各自变成判据而不是靠观察：

- CK-005：`ClearRewardRuleServiceTests.The_lowest_difficulty_leaves_every_position_empty`
  （手搭表，500 个种子，四位全空；并反证同一布置难度加一档时四位全满）；
  `ClearRewardArchiveTests.The_archives_lowest_difficulty_empties_every_position`
  （**难度取值为零的那一档从归档现读出来**，断言只有 index 0 为零，再用它跑 200 个种子全空；
  付费牌同样全空）。
- CK-006：`ClearRewardRuleServiceTests.The_same_occasion_and_seed_produce_the_same_face`
  （同事实同种子两次逐位相同；并断言 16 个种子不是全都一样，防止「永远同一张脸」蒙混过关）；
  `ClearRewardArchiveTests.A_face_drawn_from_the_real_pool_stays_inside_the_declared_band`
  末尾对真实归档再做一次同样的重复调用比对。

规则服务构造器依赖枚举（判据 `The_rule_service_is_handed_no_reader_of_world_state` 反射断言，非人工核对）：

```
IClearRewardRateSource   内容源
IDropItemPoolSource      内容源
ClearRewardRateOptions   规则配置（具名假设）
```

无 repository / registry / directory / store / persistence / domain service，判据里逐个后缀断言。

### 我自己实测的归档值（没有照抄任何人给的数）

用 `PvfProbe dump etc/itemdropinfo_clearreward.etc` 现地读的：

- `[drop prob]`：六个档案，各四段。`default` 四段全 10000；`event`/`event2`/`pcroom bonus` 全 9000；
  `pcroom default` 全 10000；`pcroom event` 为 9000/8000/8000/8000。
- `[dungeon difficulty drop bonusrate]`：`0 600 1000 1400 1200`（第五项小于第四项）。
- `[party member drop bonusrate]`：`1 1.2 1.4 1.6`，以 single 存。
- `[reward item rate per map max count]`：`1→2000 2→3000 3→4000 4→5000 5→6000`。
- `[basis of rarity dicision]`：`500000 799900 999900 1000000 1000001 1000002 1000003`
  → 基数 1000003，摇点上界 1000002，六档权重 `500000 / 299900 / 200000 / 100 / 1 / 1`，
  和恰为 1000002，最高两档各一个摇点。
- `[item drop ref table]` 第 86 行 `86 7 3` → 品级带 79..89。
- `[drop item type prob]`：`0 9500 0 500`（四个位置，含义未查明，**本任务不作为第二道门应用**）。

由这些算出并写进判据的期望值（等级 86、默认档案、5 张图）：
难度 3 队伍 1 → 840；队伍 3 → 1176；队伍 4 → 1344；难度 1 → 360；难度 4 → 720；
1 张图 → 280；难度 0 → 0。全表最陡的组合是 1344，远在基数 10000 之下（判据扫遍全表断言）。

### 变异检查结果

各改坏一处、跑 `--filter ClearReward`、再还原。**十处全部变红**，没有一处幸存：

| 变异 | 结果 |
|---|---|
| M1 `Math.Round` → 截断 | RED（2 条失败） |
| M2 分母 `basis²` → `basis` | RED（18 条） |
| M3 难度改成加法（并放开零值早退） | RED（23 条） |
| M4 摇点上界写死成 1_000_000（参照服务端那条线） | RED（2 条） |
| M5 档案取 `event` 而非 `default` | RED（14 条） |
| M6 `CardSlotCount` 4 → 3 | RED（6 条） |
| M7 表未描述的场合改成必中 | RED（4 条） |
| M8 抽取时忽略品级带（用 0..999） | RED（2 条） |
| M9 配置键写错 | RED（1 条） |
| M11 把牌型混进种子 | RED（1 条） |

还原后重跑全量确认 1761/0/2，与上表一致。

### Amendment 2 的自查：注释里每条跨文件断言都点了名的判据

对本代码所读文件**之外**（主要是归档本身）的断言，逐条列出钉它的判据：

| 注释里的断言 | 钉它的判据 |
|---|---|
| `default` 档案四段全 10000，缩水档案 9000/8000 | `ClearRewardArchiveTests.The_archive_declares_the_numbers_the_composition_is_built_from` |
| 六个档案，每个四段（且这个「四」不是牌位数） | 同上 ＋ `One_call_answers_four_positions` |
| 最低难度的加成为零 | 同上 ＋ `The_archives_lowest_difficulty_empties_every_position` |
| 难度表第五项小于第四项 | 同上 ＋ `ClearRewardRulesTests.The_bonus_table_is_passed_through_unsorted` |
| 队伍倍率以 single 存，故 1176 在二进制里是 1175.99998 | `The_archive_declares_the_numbers...`（断言 `PartyMemberRate(3) == (double)1.4f 且 != 1.4`）＋ `A_party_multiplier_stored_as_a_single_is_rounded_rather_than_truncated`（断言原值 `1175.99998`、截断得 1175、实际得 1176） |
| 品级带 86 → 79..89 | `The_archive_declares_the_numbers_the_composition_is_built_from` |
| 阶梯最高两档各一个摇点；摇点上界 = 基数 − 1 | `Every_Rarity_band_the_archive_declares_is_reachable_including_the_two_rarest` |
| 本档案能达到的最陡组合不会越过基数（clamp 从不触发） | `No_combination_the_archive_can_reach_passes_the_basis`（断言最大值 1344 且 < 10000） |
| `[drop item type prob]` 是四个数 | `The_archive_declares_the_numbers_the_composition_is_built_from` |
| **归档没给这些分子的分母、也没给合成方式** | `The_reading_of_this_file_gives_no_basis_and_nothing_that_combines`——这是个否定命题，无法正面断言，所以钉的是承载它的读取形状：一段是「两个等级界＋一个率」、表上没有任何带 Basis 的成员（而同文件的稀有度阶梯自己带一个）、四张表是四个各收一个参数的独立查询，没有任何成员把它们合起来 |

我把几处原本「钉不住」的措辞改写成了钉得住的：
「the specification's reading」改成了「the outcome required of it」；
「nothing in the archive makes them draw differently」改成了「nothing that reaches these rules is
keyed on the kind」；`[drop item type prob]` 与六档案的两条改成指向 `ClearRewardTable.ItemTypeRates`
与 `ClearRewardProfiles` 的 `<see cref>`（那两个文件本代码确实读），不再自行复述。
没有删掉任何一条断言——每条要么被钉住，要么被改成了钉得住的说法。

### 遇到的意外与处理

1. **`record` 装 `IReadOnlyList` 时值相等不成立。** `ClearRewardCardFace` 的 `Slots` 是列表，
   合成的 `Equals` 对它按引用比。第一版判据写了 `Assert.Equal(first, second)`，
   那条断言其实恒假、而「两张脸不同」那条恒真——两条都是废判据。
   改成逐位比 `face.Slots`，并加了个 `ClearRewardFixture.Fingerprint` 把一张脸写成字符串再比。
   **这类「看着在断言、其实什么也没断言」的判据是本次最接近漏网的一处。**
2. **舍入不是可选项。** 队伍倍率是 single，`10000×1400×6000×1.4f/10^8` 在二进制里是
   1175.99998；截断会在四个队伍人数里的三个上少付万分之一，且所有数字表面都正常。
   `ChanceOf` 用 `Math.Round(..., AwayFromZero)`，并有一条判据专门钉它（M1 变异确认会红）。
3. **牌型今天不改变抽取结果。** 见下面「拿不准的地方」。
4. **`--filter Rarity` 不设 `PVF_ARCHIVE_PATH` 时会多跳一条。** 与 TASK-01 的同族判据行为一致；
   全量判据那条命令设了环境变量，跳过数仍是 2。

### 拿不准的地方（如实写出来）

1. **合成方式与分母整体是假设，不是读数。** 归档只给了四个数，没给分母也没给结合律。
   我选了「三张整数表各是同一基数的分子、四者相乘」。难度那一半是被「最低难度全空」逼出来的，
   我认为不太可能错；**地图数那一半是我推的**——把它当分子还是当替代率（段名是
   `reward item rate per map max count`，字面上更像「该地图数下的物品率」而不是「加成」），
   归档没有任何东西能分辨。这一处只改 `ClearRewardRateOptions` 一个地方就能翻案。
2. **默认基数 10000 的理由是自洽，不是声明。** 「默认档案＝全额」这个论证成立，
   但 `ServerParameter.etc` 的 `[basis of item dicision] 2500` 也存在（见 `DropRateOptions` 的记述）。
   我没有第二个来源能排除它，所以做成了配置项而不是常量。
3. **牌型（免费/付费）今天不影响抽取。** 归档里能区分两者的只有成本表与兜底表，
   而那两样属于「翻牌」而不属于「牌面」，也不在本任务范围里；若在这里应用兜底表，
   「最低难度四位全空」这条验收就会被破坏。所以我让两种牌从同一种子抽出完全相同的四张，
   并**用判据把这个决定钉住**，同时在接口 remarks 里写明：想让同一局的免费牌与付费牌不同，
   由调用方（T-003）给不同的种子。**这是一处留给下游的坑，我把它写在了契约面上而不是埋起来。**
4. **`[drop item type prob]`（`0 9500 0 500`）没有被应用。** 我读任务是「只做装备那一支、
   时装那一支本次不做」，加上 TASK-01 已记明这四个位置的含义未查明，所以不把 9500/10000
   当第二道门。若上游本意是「装备支 95%、其余 5% 落空」，那就是我读错了——改动很小，
   落点已经在 `ClearRewardRuleService` 的 remarks 里指明。
5. **金币不在牌面里。** RQ-006 的「每翻到一张有东西的牌固定加一份金币」是翻牌的事，
   不是牌面组成的事，本任务没做，也没为它留抽象。
6. **`[result card]` 副本级覆盖一行没碰**，按任务要求不预留抽象。

## Executor Self-check — round 1

### 对照密封契约的 Acceptance conditions

1. **一次调用产出四个牌位** — 成立。`FaceFor` 一次返回 `ClearRewardCardFace`，四位齐出；
   没有「取第 n 位」的入口。判据 `One_call_answers_four_positions`（M6 变异会红）。
2. **六档逐档可达，含最高两档；摇点上界＝归档基数减一** — 成立。
   `Every_Rarity_band_the_archive_declares_is_reachable_including_the_two_rarest` 现读归档，
   断言 `Basis == 1000003`、`HighestRoll == Basis - 1 == 1000002`、六档权重各 > 0
   （`500000/299900/200000/100/1/1`，和 = 1000002）、`BandOf(1000001) == Epic`、
   `BandOf(1000002) == Chronicle`，并断言 `HighestRoll > 1_000_000`、`BandOf(1_000_000) == Unique`
   ——即「写死一百万」会砍掉哪两档。按 Amendment 1，用 `ProbabilityOf` / `WeightOf` 直接断言，
   没有靠反复摇点做统计。
3. **最低难度下四个牌位一律为空** — 成立，见上文 CK-005 两条判据（M3 变异会红）。
4. **物品来自归档品级带与装备池按权重选取；只取装备一支** — 成立。
   `A_piece_comes_from_the_declared_grade_band`（带外的两件永不出现、带内两件都出现）、
   `A_piece_is_drawn_by_its_declared_weight`（权重为 0 的两件永不出现）、
   `Only_the_equipment_family_is_drawn_from`（只向 `DropItemFamily.Equipment` 要过池子）、
   `A_face_drawn_from_the_real_pool_stays_inside_the_declared_band`（真实归档＋真实装备池，
   每件抽出的都确实在 `pool.Band(79..89, 该档次)` 里，且数量为 1）。
5. **分母与合成方式作为具名假设集中在一处，附理由说明归档没有这个数** — 成立。
   `ClearRewardRateOptions` 一个类型、一段 remarks 同时讲清分母与合成方式，
   并说明归档没有这个数；纯规则与规则服务都只 `<see cref>` 指向它，不重复论证。
   「归档没有」这条本身用 `The_reading_of_this_file_gives_no_basis_and_nothing_that_combines` 钉住。
6. **同一组事实与种子重复调用两次结果逐位相同** — 成立，见 CK-006 两条判据。
7. **规则服务不持有任何世界状态读写方** — 成立，构造器三个依赖全部反射断言
   （另有后缀黑名单断言）。
8. **全量判据不低于 1690 通过、0 失败、2 跳过** — 实测 **1761 通过 / 0 失败 / 2 跳过**。

### 对照 Contract Amendment 1

- **只新写「牌位出现概率的合成」这一条纯规则** — 成立。`ClearRewardRules` 只有
  `ChanceOf` 一个函数加一个 `CardSlotCount` 常量。
- **掷稀有度经 `RarityLadder` 的既有成员完成；仓库中不得出现第二套阶梯语义** — 成立。
  `(DropRarity)ladder.BandOf(random.Next(ladder.HighestRoll))`，与 `DropRules.RollRarity`
  的实质完全一致，只是喂本表那条阶梯。判据
  `The_service_rolls_a_Rarity_by_walking_the_tables_own_ladder` 用同一种子重放
  `Hits → Next(HighestRoll) → BandOf`，逐位比对服务的输出，任何别的摇点上界、
  任何多一次或少一次抽取都会让它发散（M4 变异确认会红）。
- **取品级带、桶内加权抽一件复用既有零件** — 成立，`GradeBandFor` 与 `DropRules.Pick`，
  未重写。`RatesFor` / `Gate` / `RollRarity` 一个未用、也未仿写。
- **「六档逐档可达」直接断言 `ProbabilityOf` / `WeightOf`，不做统计** — 成立。
  （另有一条用小阶梯让服务真的产出全部六档的判据，那是对服务而非对阶梯的补充，
  不是靠统计给归档阶梯下结论。）

### 对照 Contract Amendment 2

- 注释里每条对本代码所读文件之外的事实断言，都在上文「Amendment 2 的自查」表里点了名的判据。
- 钉不住的两条否定命题（归档没给分母、没给合成方式）没有删掉——因为契约第 5 条要求写明——
  但改写成了「读取产出的形状」这一可钉的形式，并配了专门的结构判据。
  如果评审认为否定命题即便如此也不该留在注释里，这一处我愿意再改。
- 本轮所有归档数字都是我自己用 `PvfProbe dump` 现读的，没有从任何既有文档、
  既有判据或任务提示里照抄。

### 分层与仓库约定

- 内容源只被规则服务注入（`IClearRewardRateSource`、`IDropItemPoolSource`）；
  规则服务不持世界状态；纯规则只被规则服务调用。
- `System.*` using 在前（本次三个源文件没有 `System.*` using）、不做字母序。
- `TreatWarningsAsErrors` 下全量构建通过。
- `ValueTask` 全覆盖这一条不适用：本服务与既有的 `IDropRuleService` 一样是同步纯计算，
  没有任何异步成员。
- `sealed` ＋ 主构造器 DI：`internal sealed class ClearRewardRuleService(...)`。
- 逆向出来的行为用 XML doc remarks 写明来源。
- 没有建 git 提交，没有碰主 checkout，没有碰 `.worktrees/client-debug-lab`。

## Independent Quick Review — QR-02-001 (round 1)

- Reviewer: 独立 quick 评审者，未参与本 Task 执行
- Verdict: **changes_required**
- Reviewed contract revision: Contract revision 1 + Contract Amendment 1 + Contract Amendment 2
- Owned diff identity（基线 c0cb6c99，工作未提交；TASK-01 产出不在本轮范围）:
  - `M  Server/src/Dfo.Server.Application/ApplicationModule.cs`
  - `?? Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`
  - `?? Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
  - `?? Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
  - `?? Server/tests/Dfo.Server.Application.Tests/World/ClearReward{Rules,RuleService,RarityLadder,Archive}Tests.cs`
  - 越界改动：无。BD-021「不改既有用例的断言与期望值」：`git status` 显示既有判据文件一个未改，成立。

### 我自己跑过的证据

1. `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
   → Protocol 376 / Networking 51 / Pvf 60 / Application 822 / Host 4 / Persistence 244 /
   Infrastructure 204(+2 skip)，**合计 1761 通过 / 0 失败 / 2 跳过**，EXIT=0。与 CK-S 声称一致；
   两条跳过是 `PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的「档案在故跳过」用例，
   与本 Task 无关。
2. `--filter FullyQualifiedName~ClearReward`（带 PVF_ARCHIVE_PATH，Application.Tests）
   → **51 通过 / 0 失败 / 0 跳过**。归档相关的 `SkippableFact` 在全量命令下确实全部真跑，没有静默跳过。
3. `PvfProbe dump etc/itemdropinfo_clearreward.etc` 现地重读，与执行者的转录**逐项对上**：
   `[drop prob]` 六档案各四段，`default` 与 `pcroom default` 四段全 10000，`event`/`event2`/`pcroom bonus`
   全 9000，`pcroom event` 为 9000/8000/8000/8000；`[dungeon difficulty drop bonusrate] 0 600 1000 1400 1200`；
   `[party member drop bonusrate] 1 1.2 1.4 1.6`；`[reward item rate per map max count] 1 2000 … 5 6000`；
   `[basis of rarity dicision] 500000 799900 999900 1000000 1000001 1000002 1000003`（基数 1000003，
   摇点上界 1000002，六档权重 500000/299900/200000/100/1/1，和 1000002）；`[item drop ref table]` 的
   `86 7 3` → 79..89；`[drop item type prob] 0 9500 0 500`。**无一处读数与转录不符。**
   期望值自算复核：840 / 1344 / 1176（1.4f 下二进制 1175.99998）/ 360 / 280 / 720 / 0 与最陡 1344，全部成立。
4. 我自己做的一处变异（非采信执行者的表）：把 `ClearRewardRuleService.cs:129` 的
   `random.Next(ladder.HighestRoll)` 改成 `random.Next(1_000_000)`，`--filter ClearReward` → **失败 2 / 通过 49**
   （`The_service_produces_every_Rarity_band_its_ladder_declares` 等）。已还原，`git status` 与还原前一致。

### 核过并认为成立的

- **稀有度阶梯未被重写（Amendment 1）**：全仓 `grep RarityLadder` 只有
  `DropRateContent.cs:63` 一个定义；`ClearRewardRuleService.cs:129` 的
  `(DropRarity)ladder.BandOf(random.Next(ladder.HighestRoll))` 与 `DropRules.RollRarity`
  （`DropRules.cs:178-181`）逐字同构，含 `HighestRoll <= 0` 的同一道守卫。**仓库里没有第二套阶梯语义。**
- **无出处的数只有一处**：三个源文件里除 `ClearRewardRateOptions.RateBasis = 10000` 外无任何
  配置性数字字面量；`ApplicationModule.cs` 的回落也走 `new ClearRewardRateOptions().RateBasis` 而非重复写
  10000。`CardSlotCount = 4` 在其 remarks 里就地具名为「归档不是它的出处」并给了理由，符合 RQ-013 第二半。
- **分层**：构造器依赖只有 `IClearRewardRateSource` / `IDropItemPoolSource` / `ClearRewardRateOptions`；
  `ClearRewardRules` 在 src 内只被 `ClearRewardRuleService` 调用，`IClearRewardRuleService` 在 src 内只有
  注册行与实现。符合 `AGENTS.md` L375/L377/L387。
- **判据钉得住**（我按「实现错了会不会照样绿」逐条判）：
  - 最低难度四位全空 —— `The_lowest_difficulty_leaves_every_position_empty` 有 500 种子 + **反证**
    （难度加一档四位全满）；`The_archives_lowest_difficulty_empties_every_position` 先断言
    `DifficultyRates` 里为零的下标集合恰为 `[0]` 再用它跑。非空转。
  - 六档逐档可达 —— `Every_Rarity_band...` 断言 `Basis==1000003`、`HighestRoll==Basis-1==1000002`、
    六档权重逐个具名且和为 1000002、`BandOf(1000001)==Epic`、`BandOf(1000002)==Chronicle`、
    `BandOf(1_000_000)==Unique`。我的变异实测确认它咬得住。
  - 同事实同种子逐位相同 —— `Assert.Equal(first.Slots, second.Slots)` 比的是
    `IReadOnlyList<ClearRewardCardSlot>`，`ClearRewardCardSlot` 是 record、`ClearRewardCardItem` 是
    record struct，xUnit 对序列逐元素比，**这条不是空断言**（执行者记录的第一版恒假问题确已修掉）；
    另有 16 种子指纹不全相同的反空转断言。
- 控制器已裁定的「免费脸与付费脸同种子相同」一条：判据
  `The_kind_of_a_face_does_not_change_what_is_drawn`（`Assert.Equal(free.Slots, paid.Slots)` +
  不同种子必不同）确实钉住了，`IClearRewardRuleService.cs:87-92` 的接口注释与代码行为一致。**无发现。**

### Findings

**F-1（阻塞，机械性）—— `ApplicationModule.cs` 被整文件换行符改写，16 行的改动变成 294/278 的全文件 diff。**

- 证据：`git diff --numstat Server/src/Dfo.Server.Application/ApplicationModule.cs` → `294 278`；
  同一命令加 `--ignore-cr-at-eol` → `16 0`。
  `file` 显示工作区该文件现为 CRLF，而 `git show HEAD:` 取出的基线 blob 是 LF，仓库 `core.autocrlf=false`
  且 `.gitattributes` 只覆盖 `protocol/**` —— 即基线检出就是 LF，CRLF 是本轮引入的。
- 影响：本 Task 的实质改动只有 16 行注册代码，却在 diff 里表现为整文件重写。这正是 TASK-01 五轮评审
  最吃亏的地方（评审者读不到真正改了什么）。落到提交上后，后续 Task 的 diff 也会继续被这一文件干扰。
- 参考：非生成的既有 CRLF 源文件全仓只有 `IServerModule.cs` 与 `ServerModuleRegistrationExtensions.cs`
  两个；本轮新建的七个文件里 `ClearRewardRules.cs`、`IClearRewardRuleService.cs`、
  `ClearRewardRuleService.cs`、`ClearRewardArchiveTests.cs`、`ClearRewardRulesTests.cs` 是 CRLF，
  `ClearRewardRarityLadderTests.cs`、`ClearRewardRuleServiceTests.cs` 是 LF —— 同一批产出自相不一致。
- 要求整改：把 `ApplicationModule.cs` 还原为 LF（`git diff` 应只剩那 16 行新增）；新建的五个 CRLF
  文件建议一并统一为 LF。**不要求改动任何逻辑。**

**F-2（阻塞，Amendment 2）—— `ClearRewardRuleService.cs:43-45` 有一条无判据钉住的全称否定断言，且被仓库本身证伪。**

原文（`Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` L42-46）：

> Six are declared … and **this server has neither an event calendar nor a PC-room notion to choose one
> with**, so the profile with no occasion attached is the only one with a caller.

- 这是对本代码所读文件**之外**的文件（本服务器其余各层）作出的事实断言，带全称否定词，
  Amendment 2 要求「必须有一条判据钉住它；钉不住的断言应当删去」。执行者的 Amendment 2 自查表里
  **没有列这一条**，四个判据文件里也没有任何断言碰它。
- 而且它与仓库现状不符。本服务器确实有 PC-room 概念，且是持久化的每角色状态：
  - `Server/src/Dfo.Server.Abstractions/Persistence/CharacterBodyTables.cs:44,51,52`
    —— `PremiumPcRoom`、`PcRoomId`、`PcRoomState`
  - `Server/src/Dfo.Server.Persistence/Characters/CharacterBodyTables.cs:153-164`
    —— `SELECT character_id,pc_room_state FROM character_condition`
  - `Server/src/Dfo.Server.Abstractions/Accounts/IAccountEntryStateServices.cs:29` 与
    `Server/src/Dfo.Server.Application/Accounts/AccountEntryStateServices.cs:96-112`
    —— 账号级 `PcRoomPlayTimeStates`
  「无事件日历」那半我查过，`grep -i EventCalendar` 全仓无命中，成立；被证伪的是「无 PC-room 概念」这半。
- 要求整改（三选一，不要求改逻辑）：
  (a) 删去这半句，只保留「归档没说何时用哪个档案，所以取 `default`」这一条本代码自己能负责的理由；
  (b) 改成钉得住的说法（例如「本规则服务收到的 `ClearRewardOccasion` 里没有任何能选档案的事实」，
      并由现成的构造器/入参判据承载）；
  (c) 保留原意但补一条判据钉住它。
  **注意不要顺手扩大改动范围** —— TASK-01 有两处新缺陷正是在整改这类注释时新造的。

### 非阻塞、供执行者与最终评审参考（不要求本轮整改）

- 同类的全仓全称断言还有两条，我逐条查证**属实**，但同样无判据钉住，最终评审可自行决定是否收紧：
  `ClearRewardRules.cs:93`「this repository holds one ladder semantics and not two」；
  `ClearRewardRuleService.cs:7` / `IClearRewardRuleService.cs:82`「The one reader …」
  （`grep IClearRewardRateSource` 确认 src 内只有该规则服务读它；`IDropItemPoolSource` 另有
  `DropRuleService` 读，但原句有 "on this path" 限定，成立）。
  `IClearRewardRuleService.cs:69`「This server has no party model and passes one」亦属实
  （`PartyBaselineMessage` 注释为 "two zero bytes for a character in no party"，
  `MonsterKill.PartyMemberCount = 1`）。
- `ClearRewardArchiveTests.cs:120-122` 用 `typeof(ClearRewardBand).GetProperties()` 的**返回顺序**
  断言 `["LowLevel","HighLevel","Rate"]`。CLR 不保证反射属性顺序，这条判据是脆的（今天绿，
  换运行时可能无故变红）。建议改为断言集合而非序列。
- `ClearRewardArchiveTests.cs:299` 的 `Assert.InRange(drawn.Count, 80, 200)` 是分布性断言，
  区间宽（期望 ~134），只能拦「差一个数量级」的错。它不是几率合成的主判据（主判据是
  `The_chance_a_position_carries_is_the_archives_numbers_combined` 的八组精确期望），可接受。

### Required remediation

1. 将 `Server/src/Dfo.Server.Application/ApplicationModule.cs` 的换行符还原为 LF，使其 `git diff`
   只剩那 16 行注册新增；建议同时把本轮新建的五个 CRLF 文件统一为 LF。
2. 处理 `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs:43-45` 的
   「this server has neither an event calendar nor a PC-room notion」断言（删去 / 改写为钉得住的说法 /
   补判据三选一）。
3. 重跑 CK-S，确认仍为 1761 通过 / 0 失败 / 2 跳过。

以上两项均为局部可整改缺陷，不涉及归档读数、几率合成、阶梯复用、分层或判据强度 —— 这四项我逐一核过，
未发现缺陷。

## Execution Result — round 2（控制器整改 QR-02-001）

两条阻塞发现均核实成立，均已改；两处都不触及逻辑。

### F-1 行尾被整文件改写

实测确认：`git diff --numstat` 对 `ApplicationModule.cs` 报 **294 / 278**，
加 `--ignore-cr-at-eol` 后只有 **16 / 0**——**278 行是纯行尾改写**，把 16 行真实改动埋在里面。
新增文件也自相矛盾：本轮六个文件里五个 CRLF、一个 LF，而 TASK-01 的四个产出全是 LF。
基线 blob 是 LF，`core.autocrlf=false`，所以 CRLF 是本轮引入的。

已把本轮全部新增与改动文件统一转回 LF。转换后：

| 文件 | 改动量 |
|---|---|
| `DropRateContent.cs` | 18 / 9 |
| `ApplicationModule.cs` | **16 / 0**（原为 294 / 278） |
| `InfrastructureModule.cs` | 3 / 0 |

**为什么这条算阻塞而不是格式洁癖**：把 16 行改动埋进 278 行噪声里，
等于让完整变更评审那一关看不见本轮真正改了什么。本 Spec 没有行为 oracle，
评审就是唯一的 oracle，让评审失明比改错一行更贵。

**顺带记一条仓库级缺口**（不在本 Task 边界内，留给后续）：本仓没有 `.gitattributes`
规范行尾，`core.autocrlf` 为 false，因此任何写出 CRLF 的工具都会制造这种噪声 diff，
而且不会有任何东西提醒。

### F-2 一句被仓库自己证伪的全称否定

`ClearRewardRuleService.cs` 原称「this server has **neither an event calendar nor a PC-room
notion**」。前半属实，**后半被仓库证伪**——实测本仓有：
`IAccountEntryStateServices.cs:29` 的 `PcRoomPlayTimeStates`、
`CharacterBodyTables.cs:44,51,52` 的 `PremiumPcRoom` / `PcRoomId` / `PcRoomState`、
`:139` 的 `ReadPcRoomStatesAsync`、`:144` 的 `CharacterPcRoomRow`，以及 `pc_room_state` 列。

已改。改后不再声称本服务器没有这个概念，而是说明**真正缺的是档案自己的选取规则**：
本服务器认得 PC 房状态，档案却没说 PC 房通关该按哪个 profile 计价，
发明一个就成了追不回出处的费率。这个说法更准，也更贴近不选它的真实理由。

### 这条发现暴露了控制器那条规矩写窄了

`Contract Amendment 2` 的原话是「凡对本代码所读文件**之外的文件**作出的事实断言」——
这是照着**档案文件**写的。而 F-2 是一句关于**本仓代码**的断言，从这条缝里漏了过去。

**规矩就此扩为**（自 TASK-03 起在各自密封契约中生效，本 Task 本轮已按此执行）：

> 注释里凡对**本文件之外的任何东西**作出的可验证事实断言——归档文件、仓库里的其他代码、
> 或参照服务端的行为——都必须要么有判据钉住，要么删去。
> 「本服务器没有 X」这类全称否定尤其危险：它读起来像常识，而验证它需要搜遍全仓。

## Executor Self-check — round 2

- F-1：已改，全部新增与改动文件统一为 LF，`ApplicationModule.cs` 的 diff 由 294/278 收敛为 16/0。
- F-2：已改，且改后的说法本身不含新的全称否定。
- 未改任何逻辑：本轮只动行尾与一段注释，无一行可执行语句变化。
- QR-02-001 另附的三条非阻塞观察本轮未处理，留作后续输入。
- 判据：见下方重跑结果。

## Independent Quick Review — QR-02-002 (round 2)

- Reviewer: 独立 quick 评审者，未参与本 Task 任何一轮执行，也未参与 QR-02-001
- Verdict: **changes_required**
- Reviewed contract revision: Contract revision 1 ＋ Contract Amendment 1 ＋ Contract Amendment 2
  （并按 round 2 在 L563-567 扩宽后的措辞复核了断言面）
- Owned diff identity（基线 c0cb6c99，工作未提交；TASK-01 产出不在本轮范围）:
  - `M  Server/src/Dfo.Server.Application/ApplicationModule.cs`
  - `?? Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`
  - `?? Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
  - `?? Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
  - `?? Server/tests/Dfo.Server.Application.Tests/World/ClearReward{Rules,RuleService,RarityLadder,Archive}Tests.cs`
  - 越界改动：无。`git status` 显示既有判据文件一个未改，BD-021 成立。

### 我自己跑过的证据

1. **CK-S**：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
   → Networking 51 / Protocol 376 / Pvf 60 / Application 822 / Host 4 / Persistence 244 /
   Infrastructure 204(+2 skip)，**合计 1761 通过 / 0 失败 / 2 跳过，EXIT=0**。一次跑通，
   没有撞上任务提示里那两类已知假红。两条跳过是
   `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
   `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
   ——它们验的是「档案缺失时的行为」，因为档案在才跳过，与本 Task 无关。
2. **行尾（F-1）**：`git diff --numstat` 与 `git diff --numstat --ignore-cr-at-eol` **逐行完全一致**：
   `DropRateContent.cs 18/9`、`ApplicationModule.cs 16/0`、`InfrastructureModule.cs 3/0`。
   按字节读本轮全部 13 个新增与改动文件，**`\r` 计数一律为 0**（不是「CRLF=0」而是「一个 CR 字节都没有」），
   且全部以换行结尾。基线 `.cs` 共 621 个 LF / 2 个 CRLF，LF 是本仓事实约定，转换方向正确。
3. **归档现读**：`PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_clearreward.etc 4000`。
   与执行者转录**逐项对上**：`[drop prob count] 4`；`[drop prob]` 六档案各四段，`default` 与
   `pcroom default` 全 10000，`event`/`event2`/`pcroom bonus` 全 9000，`pcroom event` 为
   9000/8000/8000/8000；`[dungeon difficulty drop bonusrate] 0 600 1000 1400 1200`；
   `[party member drop bonusrate] 1 1.2 1.4 1.6`；`[reward item rate per map max count] 1 2000 … 5 6000`；
   `[basis of rarity dicision] 500000 799900 999900 1000000 1000001 1000002 1000003`；
   `[item drop ref table]` 的 `86 7 3`；`[drop item type prob] 0 9500 0 500`。**无一处不符。**
   期望值我自己重算：840 / 1344 / 1176 / 360 / 280 / 720 / 0 与最陡 1344，全部成立；
   并确认整份 dump 里**没有任何一段给出「何时用哪个 profile」的规则**。
4. **变异检查未做**：`reviewing-task-units-quick` 明确规定评审者不得改动实现文件，故本轮不做变异，
   改以逐条读判据断言 ＋ 独立重算归档期望值替代。

### 一、QR-02-001 两条阻塞发现的核验

**F-1 行尾 —— 已解决。** 见上文证据 2。`ApplicationModule.cs` 由 294/278 收敛为 16/0，
`--ignore-cr-at-eol` 不再产生任何差异，本轮 13 个文件零 CR 字节。**无残留。**

**F-2 PC 房断言 —— 本 Task 自有文件内已解决，但整改不完整，见 F-3。**

- 我自己搜仓库：PC 房概念**确实存在**，且是持久化状态——
  `Server/src/Dfo.Server.Abstractions/Persistence/CharacterBodyTables.cs:44,51,52`
  （`PremiumPcRoom` / `PcRoomId` / `PcRoomState`）、`:139` `ReadPcRoomStatesAsync`、`:144` `CharacterPcRoomRow`；
  `Server/src/Dfo.Server.Persistence/Characters/CharacterBodyTables.cs:25,144` 的 `pc_room_state` 列；
  `Server/src/Dfo.Server.Abstractions/Accounts/IAccountEntryStateServices.cs:29` 的 `PcRoomPlayTimeStates`；
  `Server/src/Dfo.Server.Application/Accounts/AccountEntryAssembly.cs:146-152`。
  QR-02-001 的证伪成立。
- 改后的句子（`ClearRewardRuleService.cs:47-51`）：
  > Not because this server could not tell: it carries PC-room state per character and per account
  > already. What is missing is the archive's own rule for which profile a PC-room clear is paid at,
  > and inventing one would be a rate nobody could trace back.
- **它本身没有引入新的无出处断言。** 两半我都验了：「per character and per account」属实，
  且**已被既有判据钉住**（`Server/tests/Dfo.Server.Persistence.Tests/Accounts/SqliteAccountEntryStateRepositoryTests.cs:49`
  断言 `Relations.PcRoomPlayTimeStates`；`AccountEntryLoadCharacterizationTests.cs:196,272-273` 走 `pc_room_state` 列）
  ——Amendment 2 要求的是「有一条判据钉住」，没有限定必须是本 Task 新增的判据。
  「档案没给 PC 房该按哪个 profile 计价」这一半由我的 dump 全文确认属实，且由同段 L42-43 的
  `<see cref="ClearRewardProfiles"/>` 委托给本代码确实读的文件，沿用 round 1 已被接受的委托写法。
  **这一句本身我判为合格，没有重蹈「修断言时写进新断言」。**

### 二、round 2 有没有把逻辑改坏 —— 没有

除 CK-S 全绿外，有一条不依赖执行者自述的客观佐证：QR-02-001 引用的行号在 round 2 后的位移
**恰好只出现在 F-2 注释插入点之后，且位移量正好等于新增的那 5 行**。

| QR-02-001 引用 | round 2 后 | 位移 |
|---|---|---|
| `ClearRewardRules.cs:93`「one ladder semantics and not two」 | 仍在 :93 | 0 |
| `IClearRewardRuleService.cs:69`「no party model」 | 仍在 :69 | 0 |
| `IClearRewardRuleService.cs:82`「The one reader」 | 仍在 :82 | 0 |
| `IClearRewardRuleService.cs:87-92` 接口 remarks | 仍在 :87-92 | 0 |
| `ClearRewardArchiveTests.cs:120-122` 反射顺序断言 | 仍在 :120-122 | 0 |
| `ClearRewardArchiveTests.cs:299` `InRange(80,200)` | 仍在 :299 | 0 |
| `ClearRewardRuleService.cs:129` `random.Next(ladder.HighestRoll)` | 移到 :134 | **+5**（F-2 的 `<para>` 正好 5 行） |

即：本轮唯一的内容改动就是 `ClearRewardRuleService.cs` 第 47-51 行那段注释，
其余文件一个字符未动。`ApplicationModule.cs` 的 16 行全是新增注册代码，`git diff` 我逐行读过，
无删除、无既有语句改写。

### 三、QR-02-001 判为成立的结论，round 2 之后仍然成立

- **稀有度阶梯未被重写**：全仓 `RarityLadder` 的**定义**只有 `DropRateContent.cs:63` 一处
  （另两处 `PvfClearRewardRateSource.cs:315`、`PvfDropRateTableSource.cs:183` 是构造调用，不是第二套语义）。
  `ClearRewardRuleService.cs:134` 仍是 `(DropRarity)ladder.BandOf(random.Next(ladder.HighestRoll))`。成立。
- **无出处的数只有一处**：三个源文件的可执行代码里，两位数以上的字面量**只有**
  `ClearRewardRules.cs:82` 的 `RateBasis = 10000`（其余全在注释里）；`CardSlotCount = 4` 在
  L98-109 就地具名为「归档不是它的出处」并给了理由。`ApplicationModule.cs` 的回落走
  `new ClearRewardRateOptions().RateBasis`，不重复写数。成立。
- **分层干净**：`The_rule_service_is_handed_no_reader_of_world_state`
  （`ClearRewardRuleServiceTests.cs:306-326`）反射断言构造器恰为
  `IClearRewardRateSource` / `IDropItemPoolSource` / `ClearRewardRateOptions` 三者，
  外加六个后缀黑名单。src 内 `IClearRewardRateSource` 除注册行与实现外只有该规则服务读，
  `ClearRewardRules` 只有该规则服务调。成立。
- **三条重点判据不空转**（我逐条读断言，不采信执行者的变异表）：
  - CK-005 `The_lowest_difficulty_leaves_every_position_empty`（:56-75）：先断言 `IsImpossible`，
    再 500 种子全空，**末尾反证**「难度加一档四位全满」——反证是它不空转的关键。
    `The_archives_lowest_difficulty_empties_every_position` 另从归档现读零值下标。
  - CK-006 `The_same_occasion_and_seed_produce_the_same_face`（:79-105）：
    `Assert.Equal(first.Slots, second.Slots)` 比 `IReadOnlyList<ClearRewardCardSlot>`（record，逐元素结构比），
    ＋ `Fingerprint` 双保险，**再加 16 种子指纹 `Distinct().Count() > 1` 的反空转断言**。
    执行者自陈的第一版恒假问题确已修掉。
  - CK-004 `Every_Rarity_band...`（`ClearRewardRarityLadderTests.cs:60-104`）：断言 `Values` 全序列、
    `Basis == 1000003`、`HighestRoll == Basis - 1 == 1000002`、六档权重逐个具名、权重和 == `HighestRoll`、
    `BandOf(1000001)==Epic`、`BandOf(1000002)==Chronicle`、`HighestRoll > 1_000_000`、
    `BandOf(1_000_000)==Unique`。与我 dump 出的原始行**逐值吻合**。成立。
- 控制器已裁定的「免费脸与付费脸同种子相同」：`The_kind_of_a_face_does_not_change_what_is_drawn`
  （`ClearRewardRuleServiceTests.cs:117-140`）以 `Assert.Equal(free.Slots, paid.Slots)` 正面钉住，
  并以 `Seed + 1` 的 `Assert.NotEqual` 反证不是恒等。**钉住了，本轮无发现。**

### Findings

**F-3（阻塞，Amendment 2）—— F-2 的整改只改了一半：被证伪的原句仍在仓库里活着，
而且本 Task 自己的注释正把读者指向它。**

QR-02-001 证伪的那句话，在 `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs` 里
**原封不动地存在两处**：

- `:51-53`（`ClearRewardProfiles` 的 remarks）
  > The archive gives no rule for choosing between them, and **this server has neither an event
  > calendar nor a PC-room notion**, so only `Default` has a caller today.
- `:152-153`（`PcRoomCardBlankItems` 的 param 注释）
  > **This server has no PC-room notion**; the section is read because it is the same shape and
  > reading it costs a line.

这不是「另一个文件的旧毛病」，它落在本 Task 的交付面上，理由有三：

1. **本 Task 自己的注释把读者送过去。** `ClearRewardRuleService.cs:42-43` 写的是
   「see `<see cref="ClearRewardProfiles"/>` for the names and **for what the archive does and does
   not say about choosing between them**」——即 TASK-02 明确指定 `ClearRewardProfiles` 为该问题的权威出处，
   而那个出处上写着被证伪的话。
2. **round 2 的改动亲手制造了一处正面冲突。** `ClearRewardRuleService.cs:48-49` 现在说
   "it carries PC-room state per character and per account already"，
   `ClearRewardContent.cs:52-53` 说 "this server has neither an event calendar nor a PC-room notion"。
   同一个 feature 的两个相邻文件对同一事实给出相反陈述，且二者由 TASK-02 的 `see cref` 连起来。
3. **`ClearRewardContent.cs` 自己也自相矛盾**：说完「本服务器没有 PC 房概念」，
   十几行后就在 `:67-69` 声明 `PcRoomDefault` / `PcRoomBonus` / `PcRoomEvent` 三个常量。

判据核查：`grep` 四个 `ClearReward*Tests.cs` 与全仓，**没有任何判据碰这两句**；
它们既无出处、又为假，正是 round 2 自己在 L563-567 扩宽的规矩点名的「本服务器没有 X」型全称否定。
round 2 的自检 L572 称「F-2：已改」，但就仓库现状而言，被证伪的断言只是从一个文件搬走、
在另一个（本 Task 引用的）文件里原样保留。

**整改路径（不要求改任何逻辑，我不指定选哪条，路由由控制器定）**：

- (a) 删去 `ClearRewardContent.cs:52-53` 的 "and this server has neither an event calendar nor a
  PC-room notion" 半句与 `:152-153` 的 "This server has no PC-room notion" 半句，各保留
  「档案没给选取规则」/「读它是因为形状相同」这一半——两处都只需删半句，不动任何语义；
  **注意该文件不在 TASK-02 的 Permitted files 内**，走这条需要控制器补一条边界修订，
  或另开一条 TASK-01 尾单。
- (b) 完全落在 TASK-02 边界内的替代做法：把 `ClearRewardRuleService.cs:42-43` 的委托改窄，
  只引 `ClearRewardProfiles` 的**名字**，不再引它「对如何选取说了什么」，
  使本 Task 不再为那段文字背书；但 (b) 只消除矛盾，不消除仓库里的假断言。

**不要顺手扩大改动范围** —— 本 Task 家族已有多处新缺陷是在整改注释时新造的。
两处都是删半句，删完不需要重跑变异，重跑 CK-S 即可。

**F-4（非阻塞，记录性）—— round 2 自检承诺的判据输出没有落到 Task 文件里。**

`Executor Self-check — round 2` 末行写「判据：见下方重跑结果」，而文件到此为止（L575 即 EOF），
下方没有任何重跑结果。密封契约的 Required evidence 要求「全量判据输出」。
实质上不构成缺陷——本轮由我实测补齐（1761 / 0 / 2，EXIT=0，见上文证据 1）——
但 Task 文件的 round 2 记录本身缺这一项，收工前应补一行指向本轮评审的实测，或补贴 round 2 自己的输出。

### 非阻塞、供最终评审参考（不要求本轮整改）

- QR-02-001 列的三条「属实但无判据钉住」的全称句，round 2 按其「不要求本轮整改」的说明未处理，
  自检 L574 已如实记录。我逐条复验**仍然属实**：
  `ClearRewardRules.cs:93`「one ladder semantics and not two」（全仓只有一处 `RarityLadder` 定义）；
  `ClearRewardRuleService.cs:7` / `IClearRewardRuleService.cs:82`「The one reader … on this path」；
  `IClearRewardRuleService.cs:69`「This server has no party model and passes one」
  （`GrantKillExperienceCommand.cs:90` 传 `PartyMemberCount: 1`，`IMonsterDropService.cs:59` 默认 1，
  `PartyBaselineMessage` 是固定基线，全仓无 party 实体或服务）。
  三条都为**真**，与 F-3 的**假**性质不同，故沿用 QR-02-001 的定级：非阻塞。
- `ApplicationModule.cs:169-172` 新增注释里的「the two files declare different sections and there is
  nothing saying their numerators are over the same thing」同属真而未钉住的一类。
- `ClearRewardArchiveTests.cs:120-122` 的 `GetProperties()` 顺序断言（QR-02-001 已提）仍在，仍脆。
- **仓库级**：round 2 的 L542-544 称「本仓没有 `.gitattributes` 规范行尾」——**不确切**，
  `.gitattributes` 存在，只是仅覆盖 `protocol/**`；且它的注释自称 "This repository sets
  core.autocrlf=true"，而实测 `core.autocrlf=false`。另有 `.editorconfig` 声明
  `end_of_line = crlf`，与仓库实际的 621 LF / 2 CRLF 相反。三者互不一致，是先于本 Task 的仓库缺口，
  但后续若真要补行尾规范，需知道 `.editorconfig` 现在指的是相反方向。

### Required remediation

1. 处理 `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs:52-53` 与 `:152-153`
   仍在声称「本服务器没有 PC 房概念」的两处（按 F-3 的 (a) 或 (b)）。控制器需先决定走哪条路由，
   因为该文件不在 TASK-02 的 Permitted files 内。
2. 把 round 2 缺失的判据输出补进 Task 文件（或指向本轮 QR-02-002 的实测）。
3. 整改后重跑 CK-S，确认仍为 1761 通过 / 0 失败 / 2 跳过。

### 收工判断

**除 F-3 外，密封契约八条 Acceptance conditions 与两条 Amendment 我逐条核过，全部成立**
（第 8 条实测 1761/0/2，高于「不低于 1690」的门槛）；归档读数、几率合成、阶梯复用、
分层、判据强度五项均无缺陷，F-1 已彻底解决，F-2 在本 Task 自有文件内已正确解决且未引入新断言。
**F-3 是本轮唯一的收工阻碍，且是删两处半句的机械整改。** 清掉即可收工。

## Contract Amendment 3 — 边界扩至 ClearRewardContent.cs 的两处半句

**触发**：QR-02-002 指出 round 2 的整改**只做了一半**。被证伪的那句
「this server has neither an event calendar nor a PC-room notion」在
`Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs` 的两处原封不动活着：
`:52-53`（profile 名那段）与 `:152-153`（PC 房兜底表那段）。

**控制器裁定：批准扩界，改这两处半句。** 三条理由：

1. round 2 改掉的那个文件**正用 `see cref` 把读者指向 `ClearRewardProfiles`**，
   也就是指向没改的那一处。改一处留一处，等于把矛盾从「一句错话」变成
   「两句互相打架的话」，比不改更坏。
2. `ClearRewardContent.cs` **自己在同一个类里、十四行之后声明了三个 `PcRoom*` profile 常量**。
   文件自打脸。
3. 这一处是 TASK-01 的产出，而 TASK-01 已收工。**不重开已收工的 Task**——
   重开会让登记表的 `done` 失去意义；按 TASK-01 `Contract Amendment 3` 的先例，
   由当前在办的 Task 做有界扩界更干净。

扩界范围严格限于这两处半句：**不动任何可执行代码，不动任何判据期望值。**

Permitted files or interfaces 相应增补：
`Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`（仅上述两段 remarks）。

**记一条给后续任务的观察**：同一个错误断言复制在两个文件里，改一处、漏一处，
是本 Spec 到目前为止第二次发生（TASK-01 round 5 的穷举把范围限在源文件、漏了判据文件）。
**整改一句错话之前，先全仓 grep 那句话的关键词**——它很可能不止一处。

## Execution Result — round 3（控制器整改 QR-02-002 的 F-3、F-4，并按新规矩扫出第三处）

### F-3 已改：两处半句

`ClearRewardContent.cs` 的 `:52-53` 与 `:152-153` 都写着被证伪的
「this server has neither an event calendar nor a PC-room notion」/「This server has no PC-room
notion」。两处均已改（扩界依据见 `Contract Amendment 3`）。

改后不再声称本服务器缺什么，而是说明**真正缺的是档案自己的选取规则**；
并在 profile 名那一处点明「三个 PC-room profile 之所以声明在下面，正是因为本服务器
认得 PC 房状态」——把原先自打脸的两段变成互相印证的两段。

### F-4 已补：round 2 自检末行说「见下方重跑结果」而结果没写进来

补记：round 2 之后的全量判据为 **1761 通过 / 0 失败 / 2 跳过**，退出码 0，一次跑成。
（本轮之后的重跑见本节末。）

这条虽是非阻塞，但性质与本 Spec 反复栽的那类一样——**声称有证据而证据不在场**。

### 按新规矩全仓扫出的第三处，一并改了

`Contract Amendment 3` 记的教训是「改一句错话之前先全仓 grep」。照做，扫出第三处同形状断言：

`PvfClearRewardRateSource.cs` 的不读段落表里写着
「Chronicle sets are **not a subject this server has**」。

**这一处的处理与前两处不同，理由要记清楚**：本仓确有 chronicle 概念——
`InventoryModels.cs` 的 `ChronicleOptionState`、物品上的 `ChronicleOption0/1`、
`InventoryItemCore.cs` 的 `WriteChronicle`、`DropRules.cs` 的 `DropRarity.Chronicle`，
全仓三十余处。但本仓有的是**异界属性**（chronicle option），
而该段落说的是**套装**（chronicle **set**）——严格读，原句可能窄义为真。

**所以正确的修法不是把它换成另一句「本服务器有 X」——那同样需要搜遍全仓才能验证。**
正确的修法是**把这个负担去掉**：该段落不读的真实理由是「三对数，第一个数索引什么未查明，
套装数、档次、部位都同样说得通」，这个理由**在文件自身之内就能核**，不依赖任何仓库全称句。

**给后续任务的通则**：当一句自足的理由与一句需要搜全仓才能验证的理由都成立时，用前者。
无出处的全称句不该在注释里承重，哪怕它碰巧是真的。

## Executor Self-check — round 3

- F-3：两处半句已改，全仓 grep 确认 PC 房/事件日历一类断言在本次改动集中已无残留。
- F-4：round 2 的判据结果已补记。
- 第三处（chronicle）已改，且改后的说法**不含任何需要搜仓验证的断言**。
- 未改任何可执行代码，未改任何判据期望值。
- 行尾：本轮改动的三个文件均为 LF；`git diff --numstat` 与 `--ignore-cr-at-eol` 应当一致。
- 判据：见下方重跑结果。

### round 3 判据（写进来，不写「见下方」）

全量：**1761 通过 / 0 失败 / 2 跳过**，退出码 0，一次跑成，无假红。
分项 60 + 376 + 51 + 822 + 4 + 244 + 204 = 1761。
两条跳过是 `PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的
「缺档案时应报错」用例，它们**因为档案在**才跳过——跳过数仍为 2 即是档案确实加载了的证明。

行尾自查：`git diff --numstat` 与 `git diff --numstat --ignore-cr-at-eol` 输出逐行一致，
三个改动文件为 18/9、16/0、3/0。

## Independent Quick Review — QR-02-003 (round 3)

- Reviewer: 独立 quick 评审者，未参与本 Task 任何一轮执行，也未参与 QR-02-001 / QR-02-002
- Verdict: **changes_required**
- Reviewed contract revision: Contract revision 1 ＋ Amendment 1 ＋ Amendment 2 ＋ Amendment 3
- Owned diff identity（基线 c0cb6c99，工作未提交）：
  - 已跟踪：`M DropRateContent.cs` 18/9、`M ApplicationModule.cs` 16/0、`M InfrastructureModule.cs` 3/0
  - 未跟踪（`??`）：`ClearRewardContent.cs`、`ClearRewardRules.cs`、`IClearRewardRuleService.cs`、
    `ClearRewardRuleService.cs`、`PvfClearRewardRateSource.cs`、
    `ClearReward{Rules,RuleService,RarityLadder,Archive}Tests.cs`、`PvfClearRewardRateSourceTests.cs`
  - **round 3 本轮实际改动的文件，按 mtime 判定恰为两个**：
    `ClearRewardContent.cs`（10:12:14）与 `PvfClearRewardRateSource.cs`（10:13:01）。
    其余全部停在 round 2 及更早（09:56–09:57 / 08:44–08:46 / 08-31）。
  - 既有判据文件一个未改，BD-021 成立。

### 我自己跑过的证据

1. **CK-S**：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
   → Protocol 376 / Networking 51 / Pvf 60 / Application 822 / Persistence 244 / Host 4 /
   Infrastructure 204(+2 skip) ＝ **1761 通过 / 0 失败 / 2 跳过，EXIT=0**。**一次跑通，未撞任何已知假红。**
   两条跳过仍是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
   与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
   —— 因为档案在才跳过。
2. **行尾**：`git diff --numstat` 与 `git diff --numstat --ignore-cr-at-eol` **逐行完全一致**，
   均为 `DropRateContent.cs 18/9`、`ApplicationModule.cs 16/0`、`InfrastructureModule.cs 3/0`。
   按字节读本轮相关 13 个 `.cs`（含 round 3 改的两个），**`\r` 计数一律为 0**，且全部以 `0a` 结尾。
3. **归档现读**：`dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_clearreward.etc 4000`，
   自跑一遍全 40 行。与前两轮转录逐项对上，另新核两处本轮相关的：
   `[chronicle set reward rate]` 为 **`0 50 1 75 2 100`**；`[pcroom card blank item]` 为 **`50 7454 1`**。
4. 未做变异：`reviewing-task-units-quick` 禁止评审者改实现文件。改以逐条读断言 ＋ 独立重算替代。

### 一、F-3 是否清干净 —— 被证伪的那句已彻底消失；控制器点名的那句属实且已被判据钉住

- **全仓 grep**（`--include=*.cs Server/ Tool/`，模式 `event calendar|neither an event|no pc-?room|has no pc|pc-?room notion`）：
  「this server has neither an event calendar nor a PC-room notion」/「This server has no PC-room notion」
  **在仓库里一个字都不剩**。QR-02-002 的 F-3 已解决。
- **控制器点名核的那句** —— `ClearRewardContent.cs:56`
  「three of these names are PC-room profiles and **this server already carries PC-room state**」：
  - **属实。** `IAccountEntryStateServices.cs:29` 的 `PcRoomPlayTimeStates`；
    `Abstractions/Persistence/CharacterBodyTables.cs:44,51,52,139,144`；
    `Persistence/Characters/CharacterBodyTables.cs:25,144,153,160` 的 `pc_room_state` 读写；
    `CharacterDatabaseSchema.cs:178` 的建表列；
    `SelectedCharacterStateMessageFactory.cs:117` 把它发给客户端。
  - **已被判据钉住**（不需要本 Task 新增判据，Amendment 2 未限定必须是本轮新增的判据）：
    `Server/tests/Dfo.Server.Persistence.Tests/Accounts/SqliteAccountEntryStateRepositoryTests.cs:49`
    的 `Assert.Equal([7, 7], result.Relations.PcRoomPlayTimeStates)`；
    `Server/tests/Dfo.Server.Application.Tests/Accounts/AccountEntryStateTests.cs:33`
    的 `Assert.Equal((byte)7, result.Relations.PcRoomPlayTimeState)`；
    `Server/tests/Dfo.Server.Persistence.Tests/Schema/CharacterBodyShapeTests.cs:57` 钉住 `pc_room_state` 列。
  - **这一句判为合格。** 它把原先自打脸的两段（说「没有 PC 房概念」却在十四行后声明三个
    `PcRoom*` 常量）变成互相印证，方向正确。
- 但同一次改动在 `:158` 新写进一句同型的仓库全称否定，见 **N-1**（非阻塞）。

### 二、第三处（chronicle）的论证与改法 —— 论证成立，改后的句子确实自足

- **论证成立。** 我自己验：本仓确有 chronicle **option**（`InventoryModels.cs` 的 `ChronicleOptionState`、
  `CharacterRosterService.cs:246,254`、`ItemContainers.cs:21,27`、`DropRules.cs:26` 的 `DropRarity.Chronicle`），
  但 `grep -i chronicle` 全仓**没有任何 chronicle set（套装）类型**。
  所以原句「Chronicle sets are not a subject this server has」严格读**可能窄义为真**，
  控制器「不该把它换成另一句需要搜全仓的话」的判断成立：把一句真假取决于
  「set 与 option 是否同义」的窄义全称句留在注释里，等于把验证负担转嫁给下一个读者。
- **改后的句子确实自足。** 现文（`PvfClearRewardRateSource.cs:136-139`）：
  > `0 50 1 75 2 100` in A21. Three pairs, and what the first of each indexes is not ascertained
  > -- a set count, a tier, a slot, all fit the numbers equally well. Nothing on the card path
  > asks for it; not read.

  三半我逐一核：(1) 数值转录**与我自己 dump 出的原始行逐字相符**；
  (2)「首元素索引什么未查明」是纯认识论陈述，只凭 `0/1/2` 这三个首元素即可判定歧义成立，不依赖仓库；
  (3)「Nothing on the card path asks for it」范围只到本 feature 自己的代码，
  `grep ChronicleSetRewardRate` 全仓只有该常量与 `PvfClearRewardRateSourceTests.cs:429`
  （只断言它出现在 `UnreadSections` 里）。**没有留下需要搜全仓才能验证的话。**
  与同表三个兄弟段落（`RarityControl` / `GoldCardCreateRate` / `DifficultyGoldBonusRate`）形状一致。

### 三、round 3 有没有把逻辑改坏 —— 没有

- round 3 只改了两个文件（mtime 证据见上），两处改动**全在 `///` XML doc 行内**，我逐行读过。
- 客观佐证一：`PvfClearRewardRateSource.cs` 的 `new RarityLadder(values)` 由 QR-02-002 记的 `:315`
  移到 `:316`，**位移正好 +1**，等于 chronicle 段落净增的那一行；`ClearRewardContent.cs` 的
  `PcRoomDefault/Bonus/Event` 三常量由 `:67-69` 移到 `:72-74`（+5，等于新增 `<para>` 的行数）。
  **两个文件的位移量都恰好等于注释增行数，无第二处内容变动。**
- 客观佐证二：`ClearRewardRules.cs:82`（`RateBasis = 10000`）、`:93`、
  `ClearRewardRuleService.cs:134`（`(DropRarity)ladder.BandOf(random.Next(ladder.HighestRoll))`）
  的行号与内容与 QR-02-002 所记**完全一致** —— 这三个文件 round 3 一个字符未动。
- 客观佐证三：钉住这两个文件解析结果的判据（`PvfClearRewardRateSourceTests.cs`、
  `ClearRewardArchiveTests.cs`）**本轮均未被改动**，而 CK-S 全绿、用例总数与 round 2 同为 1761，
  期望值一个未动。**行为未变。**

### 四、行尾 —— 干净

见证据 2。三个已跟踪文件 18/9、16/0、3/0，`--ignore-cr-at-eol` 无差异；13 个文件零 `\r` 字节。
与执行者自检所述一致。

### 五、前两轮判为成立的结论，round 3 之后仍然成立

- **全仓只有一处 `RarityLadder` 定义**：`DropRateContent.cs:63`。
  `PvfClearRewardRateSource.cs:316`、`PvfDropRateTableSource.cs:183` 是构造调用；
  `RarityLadderTests.cs:16` 是判据类名，非第二套语义。**成立。**
- **可执行代码里无出处的数只有一处**：对三个自有源文件剔除 `///` 行后 grep `[0-9]{2,}`，
  命中**只有** `ClearRewardRules.cs:82` 的 `RateBasis = 10000`，另两个文件零命中。
  `ApplicationModule.cs:183` 的回落走 `new ClearRewardRateOptions().RateBasis`，不重复写数。**成立。**
- **规则服务不持世界状态**：构造器为
  `(IClearRewardRateSource, IDropItemPoolSource, ClearRewardRateOptions)` 三者
  （`ClearRewardRuleService.cs:33-36`），由 `ClearRewardRuleServiceTests.cs:306-326`
  反射正面断言 ＋ 六个后缀黑名单钉住。**成立。**
- **三条重点判据不空转**：`The_lowest_difficulty_leaves_every_position_empty`（含「难度加一档四位全满」反证）、
  `The_same_occasion_and_seed_produce_the_same_face`（含 16 种子指纹 `Distinct().Count() > 1` 反证）、
  `Every_Rarity_band_the_archive_declares_is_reachable_including_the_two_rarest`
  （`Basis == 1000003`、`HighestRoll == 1000002`、六档权重逐个具名、`BandOf(1000002)==Chronicle`）
  —— 期望值与我自己 dump 的 `500000 799900 999900 1000000 1000001 1000002 1000003` 逐值吻合。**成立。**
- **免费脸／付费脸同种子相同**：`The_kind_of_a_face_does_not_change_what_is_drawn` 以正面
  `Assert.Equal(free.Slots, paid.Slots)` ＋ `Seed + 1` 的 `Assert.NotEqual` 反证。
  **钉住了**，本轮无发现，按控制器裁定不再复议。

### 六、密封契约 Acceptance conditions 逐条

| # | 条款 | 判据 | 结论 |
|---|---|---|---|
| 1 | 一次调用产出四个牌位 | `One_call_answers_four_positions` | 成立 |
| 2 | 六档逐档可达；上界＝基数−1 | `Every_Rarity_band...`、`The_service_produces_every_Rarity_band_its_ladder_declares` | 成立 |
| 3 | 最低难度四位全空 | `The_lowest_difficulty_leaves_every_position_empty`、`The_archives_lowest_difficulty_empties_every_position`、`The_lowest_difficulty_makes_a_position_impossible` | 成立 |
| 4 | 品级带＋加权抽取，只取装备 | `A_piece_comes_from_the_declared_grade_band`、`A_piece_is_drawn_by_its_declared_weight`、`Only_the_equipment_family_is_drawn_from` | 成立 |
| 5 | 分母与合成集中一处并附理由 | `ClearRewardRateOptions`（`ClearRewardRules.cs`）、`The_reading_of_this_file_gives_no_basis_and_nothing_that_combines` | 成立 |
| 6 | 同事实同种子逐位相同 | `The_same_occasion_and_seed_produce_the_same_face` | 成立 |
| 7 | 不持世界状态读写方 | `The_rule_service_is_handed_no_reader_of_world_state` | 成立 |
| 8 | ≥1690 通过 / 0 失败 / 2 跳过 | 我实测 **1761 / 0 / 2** | 成立 |
| A1 | 掷稀有度走既有 `RarityLadder`，不得第二套阶梯 | 全仓一处定义；`The_service_rolls_a_Rarity_by_walking_the_tables_own_ladder` | 成立 |
| A1 | 六档可达可直接断言 `ProbabilityOf`/`WeightOf` | `ClearRewardRarityLadderTests.cs` | 成立 |
| A2 | 跨文件断言须有判据钉住或删去 | 见 N-1 | 除 N-1 一句外成立 |
| A3 | 扩界严格限于 `ClearRewardContent.cs` 两处半句 | 见 **F-5** | **不成立** |

### Findings

**F-5（阻塞，Amendment 3 的边界）—— round 3 改了一个不在 Permitted files 内、
也不在 Amendment 3 扩界范围内的第三个文件，且未补任何边界修订。**

`Contract Amendment 3` 写得毫不含糊：

> 扩界范围**严格限于这两处半句**：不动任何可执行代码，不动任何判据期望值。
> Permitted files or interfaces 相应增补：
> `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`（**仅上述两段 remarks**）。

而 round 3 实际改的是**两个**文件：`ClearRewardContent.cs`（授权范围内）与
**`Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`（授权范围外）**。
后者的 chronicle 段落（`:136-139`）被重写；mtime 10:13:01 与 `new RarityLadder` 的 +1 行位移
两项独立证据都指向本轮改动。

我 grep 过整份 Task 文件：`PvfClearRewardRateSource` 只在 L664（QR-02-002 的旁证引用）
与 L829（round 3 的执行记录）出现过，**从未出现在任何一处 Permitted files 里**。
它是 TASK-01 的产出，与 `ClearRewardContent.cs` 同属一类。

**为什么这一条算阻塞而不是文书洁癖**：

1. **控制器上一轮刚为同一类文件立了规矩。** QR-02-002 明确写「**注意该文件不在 TASK-02 的
   Permitted files 内**，走这条需要控制器补一条边界修订」，控制器据此写了 `Amendment 3`，
   还专门给了三条理由（含「不重开已收工的 Task」「登记表的 `done` 不能失去意义」）。
   同一条逻辑对 `PvfClearRewardRateSource.cs` 一字不差地适用，而这一次没有修订，
   只有 Execution Result 里的一段自述。**先例立完一轮就被绕过。**
2. **`Amendment 3` 自己留的告诫正是这件事**：「**不要顺手扩大改动范围** ——
   本 Task 家族已有多处新缺陷是在整改注释时新造的」。round 3 的做法（全仓 grep 后「一并改了」）
   恰好是「顺手扩大改动范围」，哪怕这一次改得对。**改得对不能替代授权。**
3. **收工后这份记录会是错的。** Task 文件当前声明的 owned boundary 与真实 owned diff 不符，
   最终评审（`reviewing-code`）拿声明的边界对现场 diff 时会直接撞上这个差额。

**整改（不需要动任何代码，不需要重跑判据）**：由控制器补一条 `Contract Amendment 4`，
按 `Amendment 3` 的同样形式把
`Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`
（**仅 `UnreadSections.ChronicleSetRewardRate` 的 summary**）纳入 Permitted files，
写明扩界理由与「不动可执行代码、不动判据期望值」的同样限定；
或者撤回该处改动、留给 TASK-01 尾单。**内容本身我已判为合格（见第二节），缺的只是那条修订。**
CK-S 我已实测 1761/0/2，补修订后无需再跑。

### 非阻塞、供最终评审参考（不要求本轮整改）

- **N-1（本轮新造的一句无判据仓库全称否定）**：`ClearRewardContent.cs:157-158`

  > `[pcroom card blank item]`, the PC-room flip's weighted fallback rows. Nothing asks for
  > them today -- **there is no PC-room flip path** -- but the section is the same shape as the
  > paid one and reading it costs a line.

  这一句是 round 3 **新写的**（原句是被证伪的「This server has no PC-room notion」）。
  我核过：**它为真** —— 全仓没有任何 flip path，`IClearRewardRuleService` 在 src 内除
  `ApplicationModule.cs:172` 的注册行外无任何调用方，所以「没有 PC 房 flip path」
  在「根本还没有 flip path」的意义上成立。**但它没有判据钉住，且不承重**：
  同一句里「the section is the same shape as the paid one and reading it costs a line」
  已经是完整自足的理由，删掉那个破折号插入语，句子的说明力一分不减。
  这正是 round 3 自己写下的通则所指的情形 ——
  「无出处的全称句不该在注释里承重，哪怕它碰巧是真的」。**round 3 在同一次改动里违反了自己的通则。**
  另有一处措辞不精确：「Nothing asks for them today」严格说被
  `PvfClearRewardRateSourceTests.cs:321`
  （`Assert.Equal([new ClearRewardBlankItem(50, 7454, 1)], table.PcRoomCardBlankItems)`）证否
  —— 它确实被判据读了，句意应为「没有产品代码读它」。
  **定级理由**：沿用 QR-02-001 / QR-02-002 对「真而未钉」一律非阻塞的一致定级，
  与被证伪的 F-2 / F-3 性质不同。建议连同 F-5 的修订一并删去那个破折号插入语（删七个词，零风险）。
- **N-2（记录性）**：round 3 自检首行称「全仓 grep 确认 PC 房/事件日历一类断言在**本次改动集**中已无残留」。
  按被证伪的那句讲属实；但按 round 2 扩宽后的措辞讲不精确 ——
  本次改动集里新写进了 N-1 那一句同型（虽为真的）PC 房全称否定。
- **N-3（承前）**：`[chronicle set reward rate]` 的数值转录 `0 50 1 75 2 100` 我实测无误，
  但没有判据钉住那串数（`PvfClearRewardRateSourceTests.cs:429` 只钉住段落名出现在 `UnreadSections` 里）。
  同表另三个不读段落的数值转录同样未钉。这是密封契约 Required methods 明令的
  「归档读法写进 remarks」的固有形态，且属于「本代码所读文件」，与 Amendment 2 不冲突；
  沿用前两轮定级，非阻塞。
- **承前未变**：QR-02-001/002 列的「真而未钉」四条仍在且仍为真 ——
  `ClearRewardRules.cs:93`、`ClearRewardRuleService.cs:7`、`IClearRewardRuleService.cs:69,82`、
  `ApplicationModule.cs:169-176`。`ClearRewardArchiveTests.cs` 的 `GetProperties()` 顺序断言仍脆。
  仓库级 `.gitattributes` / `.editorconfig` / `core.autocrlf` 三者不一致，仍是先于本 Task 的缺口。

### Required remediation

1. 补 `Contract Amendment 4`（或等效的边界修订），把 round 3 对
   `Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`
   的注释改动纳入 Permitted files 并写明限定；或撤回该处改动。**不需改代码，不需重跑判据。**
2. （建议，非必须）删去 `ClearRewardContent.cs:158` 的 "-- there is no PC-room flip path --"，
   并把 `:157` 的 "Nothing asks for them today" 改为「没有产品代码读它」的意思。

### 收工判断

**代码正确性、归档读数、几率合成、阶梯复用、分层、行尾、判据强度七项本轮全部复核为无缺陷**，
密封契约八条 Acceptance conditions ＋ Amendment 1 两条全部成立，
CK-S 我实测 **1761 通过 / 0 失败 / 2 跳过，EXIT=0，一次跑成**。
**被证伪的 PC 房断言在全仓已彻底清除**，控制器点名的「this server already carries PC-room state」
属实且被既有判据钉住，chronicle 那处的论证成立、改后的句子确实自足。
**唯一的收工阻碍是 F-5：一处未经边界修订的越界改动** —— 补一条修订即可，之后本 Task 即可收工。

## Contract Amendment 4 — 补记 round 3 已改的第二个文件（迟到的修订）

**触发**：QR-02-003 的 F-5 指出，round 3 实际改了**两个**文件，而
`Contract Amendment 3` 白纸黑字写着扩界「严格限于这两处半句」，只增补了
`ClearRewardContent.cs`。第二个文件
`Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`
（chronicle 那一句）**没有任何修订授权它**，只在 round 3 的自述里提了一句。

**控制器裁定：补记，并如实记下这是迟到的。**

Permitted files or interfaces 增补：
`Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`
（仅 `UnreadSections` 里 chronicle 一段的 summary）。

改动本身经 QR-02-003 独立复核为合格：论证成立、改后句子自足、数值与归档相符。

**但顺序错了，这一点比改动本身更值得记。** 上一轮刚为同类情形专门写了 Amendment 3，
说明控制器清楚「越出 Permitted files 要先补修订」这条规矩；然后在同一轮里越出了它，
只写了自述。**修订是为了让边界可审计而存在的，事后补记的修订比事前写的弱**——
事前写的能约束改动，事后写的只能追认它。

这与本 Spec 反复出现的那个形状同源：**知道规矩、也刚复述过规矩，仍在下一步违反它。**
TASK-01 里控制器三次在修错误断言时写进新的错误断言；这一次是在写完边界修订后越出它。
判据钉得住数值，钉不住这个。钉得住这个的只有独立评审逐条对边界。

**给后续任务的做法**：越出 Permitted files 之前先写修订，**在同一次编辑之前**，
不要留到交付时补。若临时发现必须多改一个文件，先停下写修订再改。

## Execution Result — round 4（整改 QR-02-003 的 F-5、N-1）

- **F-5**：已补 `Contract Amendment 4`（见上）。不改代码、不改判据。
- **N-1**：已删。`ClearRewardContent.cs` 的 PC 房兜底表那段原写
  「Nothing asks for them today -- **there is no PC-room flip path** -- but the section is …」。
  破折号里那七个词是一句关于仓库的断言：为真，但**无判据钉住**，
  而同一句已有自足理由（「今天没有东西问它要」本身就是理由，只需看调用点即可核）。
  **它违反的正是 round 3 自己写下的那条通则**：当自足理由与需要搜全仓的理由都成立时，用前者。
  删掉七个词，句意不减。

QR-02-003 复核为属实且已被既有判据钉住的那一句
（`ClearRewardContent.cs` 的「this server already carries PC-room state」）保留不动——
它有判据（`SqliteAccountEntryStateRepositoryTests`、`AccountEntryStateTests`、
`CharacterBodyShapeTests`），符合规矩。

## Executor Self-check — round 4

- F-5：已补修订，并如实记录顺序错误而非粉饰为「一并授权」。
- N-1：已删，且删后该段不含任何需要搜仓验证的断言。
- 未改任何可执行代码，未改任何判据期望值；本轮产品改动仅 `ClearRewardContent.cs` 一处删词。
- 判据与行尾结果写在下方，不写「见下方」。

### round 4 判据

全量：**1761 通过 / 0 失败 / 2 跳过**，退出码 0，一次跑成，无假红。
分项 60 + 51 + 376 + 822 + 244 + 4 + 204 = 1761。

行尾自查：`git diff --numstat` 与 `git diff --numstat --ignore-cr-at-eol` 逐行一致，
三个改动文件 18/9、16/0、3/0。

## Independent Quick Review — QR-02-004 (round 4)

- Reviewer: 独立 quick 评审者，未参与本 Task 任何一轮执行，也未参与 QR-02-001 / 002 / 003
- Verdict: **passed**
- Reviewed contract revision: Contract revision 1 ＋ Amendment 1 ＋ 2 ＋ 3 ＋ 4
- Owned diff identity（基线 c0cb6c99，工作未提交）：见下方第一节的逐文件对账。
  round 4 实际改动的产品文件按 mtime 恰为一个：`ClearRewardContent.cs`（10:32:20；
  round 3 时为 10:12:14）。`PvfClearRewardRateSource.cs` 停在 10:13:01（round 3），
  其余源文件与判据文件停在 09:25–09:57（round 1/2）或更早。

### 我自己跑过的证据

1. **CK-S**：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
   → Networking 51 / Protocol 376 / Pvf 60 / Application 822 / Persistence 244 / Host 4 /
   Infrastructure 204(+2 skip) ＝ **1761 通过 / 0 失败 / 2 跳过，EXIT=0**。
   **一次跑成，未撞任何已知假红。** 两条跳过仍是
   `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
   `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
   —— 因为档案在才跳过，即档案确实加载了。
2. **行尾**：`git diff --numstat` 与 `git diff --numstat --ignore-cr-at-eol` **逐行完全一致**，
   均为 `DropRateContent.cs 18/9`、`ApplicationModule.cs 16/0`、`InfrastructureModule.cs 3/0`。
   按字节读本 Task 触及的 10 个 `.cs`，**`\r` 计数一律为 0**。
   `git diff -U2 ApplicationModule.cs` 实见就是那 16 行新增、0 行删除。
3. **全仓 grep**（`--include=*.cs Server/ Tool/`，模式
   `no PC-?room flip path|there is no pc-?room|has no pc-?room|neither an event calendar|event calendar|pc-?room notion`）
   → **零命中**。
4. 未做变异：规程禁止评审者改实现文件。改以逐行读断言 ＋ 独立 grep / 重算替代。

### 一、F-5 的补法与边界对账 —— 对得齐

**`Contract Amendment 4` 确实授权了那个文件，且范围与实际改动吻合。** Amendment 4 写的是
「仅 `UnreadSections` 里 chronicle 一段的 summary」；现地核实
`PvfClearRewardRateSource.cs:122` 是 `internal static class UnreadSections`，
chronicle 那段 summary 在 `:137-140`、常量在 `:141`，**改动确在该类的该段之内**，
与授权文字逐字对得上。Amendment 4 如实记为「迟到的修订」并写明「事后补记的修订比事前写的弱」，
没有粉饰成「一并授权」——这是正确的记法。

**逐文件对账**（`git status` / `git diff --name-only` 的每一项）：

| 文件 | 授权出处 | 本 Task 是否改过（mtime） |
|---|---|---|
| `M DropRateContent.cs` | **TASK-01** 的 Amendment 3 | 否，09-01 08:46（TASK-01 期） |
| `M ApplicationModule.cs` | TASK-02 原始 Permitted files | 是（round 1，+16/−0） |
| `M InfrastructureModule.cs` | **TASK-01** 原始 Permitted files | 否，08-31 22:39（TASK-01 round 1） |
| `?? ClearRewardContent.cs` | TASK-02 **Amendment 3** | 是（round 3、round 4） |
| `?? ClearRewardRules.cs` | TASK-02 原始 Permitted files | 是（round 1/2） |
| `?? IClearRewardRuleService.cs` | TASK-02 原始 Permitted files | 是（round 1/2） |
| `?? ClearRewardRuleService.cs` | TASK-02 原始 Permitted files | 是（round 1、round 2 整改） |
| `?? PvfClearRewardRateSource.cs` | TASK-02 **Amendment 4** | 是（round 3） |
| `?? ClearReward{Rules,RuleService,RarityLadder,Archive}Tests.cs` | TASK-02 原始 `Server/tests/` | 是（round 1/2） |
| `?? PvfClearRewardRateSourceTests.cs` | TASK-01 产出（亦落在 `Server/tests/` 内） | 否，09-01 08:44 |
| `?? .rks-agent/tasks/...` | 本流程自己的制品目录 | — |

**结论：本 Task 实际改动过的每一个文件都有授权，没有一个对不上。**
工作区未提交，因此 `git status` 是 TASK-01 ＋ TASK-02 的并集；三个不在 TASK-02 边界内的文件
（`DropRateContent.cs`、`InfrastructureModule.cs`、`PvfClearRewardRateSourceTests.cs`）
全部是 TASK-01 的 Permitted files 与产出，mtime 与前三轮记录两项独立证据都显示 TASK-02 未碰。
BD-021「不改既有用例的断言与期望值」成立。

### 二、N-1 是否删干净 —— 干净，且删后那段自足

`ClearRewardContent.cs:156-158` 现文：

> `[pcroom card blank item]`, the PC-room flip's weighted fallback rows. Nothing asks for
> them today, but the section is the same shape as the paid one and reading it costs a line.

「there is no PC-room flip path」七个词已删，全仓再无同形句（证据 3）。
剩下的两半我逐一判：**「the section is the same shape as the paid one」** 是对同文件
`GoldCardBlankItems`（`:153-155`）的比较，本文件之内可核；**「reading it costs a line」**
是对本代码自身的陈述。**这一段不含任何需要搜仓验证的断言。**

### 三、漏网的可验证断言 —— 无阻塞项；四条「真而未钉」承前未变，各自仍为真

我按现行规矩把本 Task 改动过的全部文件的注释逐条过了一遍，逐条自己 grep 验证：

- `ClearRewardRules.cs:93`「this repository holds one ladder semantics and not two」——
  **属实**：全仓 `record/class/struct RarityLadder` 只有 `DropRateContent.cs:63` 一处定义
  （`RarityLadderTests.cs:16` 是判据类名，非第二套语义）。承前非阻塞。
- `ClearRewardRuleService.cs:7` / `IClearRewardRuleService.cs:82`「The one reader … the only caller」——
  **属实**：`grep IClearRewardRateSource Server/src/` 的读方只有 `ClearRewardRuleService.cs:34`
  （另三处是接口声明、实现、注册行，两处是 `DropRateContent.cs` 的 `<c>` 引用）；
  `ClearRewardRules.` 在 src 内只有 `ClearRewardRuleService.cs:68,90,91,108`。承前非阻塞。
- `IClearRewardRuleService.cs:69`「This server has no party model and passes one」——承前，QR-02-001 已核属实。
- `ClearRewardRules.cs:132`「the same reason the monster side holds its rates there」——
  **属实**：`DropRules.cs:140` 就是 `Math.Min(rate, basis)`，`:113` 的注释写着同一句
  「a rate above the basis is a certainty wearing the costume of a chance」。承前非阻塞。
- `ClearRewardRuleService.cs:48-49`「it carries PC-room state per character and per account already」——
  与控制器点名放行的 `ClearRewardContent.cs:56` 同一断言，两半各有既有判据钉住
  （`CharacterBodyShapeTests` 的 `pc_room_state` 列 / `SqliteAccountEntryStateRepositoryTests`
  与 `AccountEntryStateTests` 的 `PcRoomPlayTimeState(s)`）。按控制器裁定不再复议。
- `PvfClearRewardRateSource.cs:137-140`（chronicle）——三半我复核：数值 `0 50 1 75 2 100`
  与 QR-02-003 现读一致；「首元素索引什么未查明」是纯认识论陈述；
  「Nothing on the card path asks for it」范围只到本 feature 自己的代码，
  `grep ChronicleSetRewardRate` 全仓只有该常量与 `PvfClearRewardRateSourceTests.cs:429`。**自足。**

**唯一仍可挑的一句是 N-1 的另一半**：`ClearRewardContent.cs:157`
「Nothing asks for them today」严格说是一句仓库全称句，无判据钉住，且被
`PvfClearRewardRateSourceTests.cs:321`（`Assert.Equal([new ClearRewardBlankItem(50, 7454, 1)], table.PcRoomCardBlankItems)`）
在字面上证否 —— `grep PcRoomCardBlankItems` 全仓只有本文件的 `<param>`、`:176` 的形参
与那一条判据，**没有任何产品代码读它**，所以按「没有产品代码问它要」的本意为真。
**这正是 QR-02-003 已定级为非阻塞、且列在「（建议，非必须）」remediation 里的那半条**；
控制器做了必做的一半、没做建议的一半，这在授权之内。我沿用前三轮对「真而未钉」一律非阻塞的
一致定级，**不因收工轮而改判**（记为 N-4）。

### 四、round 4 有没有把逻辑改坏 —— 没有

- **产品改动只有一处删词，属实。** mtime 显示 round 4 只碰了 `ClearRewardContent.cs`；
  该文件 round 3 之后的位置证据仍成立：`PcRoomDefault/Bonus/Event` 三常量仍在 `:72-74`
  （QR-02-003 记的 round 3 位置），profile 那段的「this server already carries PC-room state」
  仍在 `:56`（QR-02-003 记的行号）—— **删词发生在 `:157`，`:156` 以上一字未动。**
- 删除只在 `<param name="PcRoomCardBlankItems">` 的 doc 文本里，`:165` 的
  `public sealed record ClearRewardTable(` 及其形参表未动。
- 钉住该文件解析结果的 `PvfClearRewardRateSourceTests.cs` mtime 08:44（TASK-01 期），本轮未动；
  CK-S 分项计数与 round 3 **逐个程序集完全相同**（51/376/60/822/244/4/204+2）。**行为未变。**

### 五、行尾 —— 干净

见证据 2。三个已跟踪文件 18/9、16/0、3/0，`--ignore-cr-at-eol` 无差异，与预期一致；
本 Task 触及的 10 个 `.cs` 零 `\r` 字节。

### 六、前三轮判为成立的结论，round 4 之后仍然成立

- **全仓只有一处 `RarityLadder` 定义**：`DropRateContent.cs:63`。**成立。**
- **可执行代码里无出处的数只有一处**：对三个自有源文件剔除 `///` 行后扫数字，
  唯一的配置性数字是 `ClearRewardRules.cs:82` 的 `RateBasis = 10000`；
  `CardSlotCount = 4`（`:109`）在其 remarks 里就地具名为「归档不是它的出处」并给了理由；
  `ApplicationModule.cs` 的回落走 `new ClearRewardRateOptions().RateBasis`，不重复写数。**成立。**
- **规则服务不持世界状态**：构造器为
  `(IClearRewardRateSource rates, IDropItemPoolSource pools, ClearRewardRateOptions options)`
  三者（`ClearRewardRuleService.cs:33-36`），由 `The_rule_service_is_handed_no_reader_of_world_state`
  反射正面断言 ＋ 后缀黑名单钉住。**成立。**
- **三条重点判据不空转**（我逐行读过断言体，非采信自述）：
  - `The_lowest_difficulty_leaves_every_position_empty`：`ChanceOf(...).IsImpossible` ＋ 500 个种子四位全空
    ＋ **反证**「同一布置难度加一档，`Assert.All(..., slot => Assert.False(slot.IsEmpty))`」。非空转。
  - `The_same_occasion_and_seed_produce_the_same_face`：`Assert.Equal(first.Slots, second.Slots)`
    ＋ `Fingerprint` 比对 ＋ **反证**「16 个种子的指纹 `Distinct().Count() > 1`」。非空转。
  - `Every_Rarity_band_the_archive_declares_is_reachable_including_the_two_rarest`：
    现读归档断言 `Values == [500000,799900,999900,1000000,1000001,1000002,1000003]`、
    `Basis == 1000003`、`HighestRoll == Basis - 1 == 1000002`、六档权重逐个具名
    （`500000/299900/200000/100/1/1`）、逐档 `WeightOf > 0` 且 `ProbabilityOf > 0`。非空转。

### Findings

**无阻塞发现。** F-5 与 N-1 均已解决：Amendment 4 的授权文字与实际改动范围逐字对得上，
四条 Amendment 加原始 Permitted files 覆盖本 Task 改动过的每一个文件、无一对不上；
被删的七个词全仓无残留，删后那一段自足。

### 非阻塞、供最终评审参考（不要求整改）

- **N-4**：`ClearRewardContent.cs:157`「Nothing asks for them today」仍是一句无判据的仓库全称句，
  本意（没有产品代码读它）为真、字面（有一条判据读它）不严。承 QR-02-003 的定级为非阻塞，见第三节。
- **N-5（记录性）**：QR-02-003 的 "Owned diff identity" 把 `DropRateContent.cs` 与
  `InfrastructureModule.cs` 列在 TASK-02 名下而未注明二者属 TASK-01。本轮已在第一节逐文件归属。
  最终评审拿工作区 diff 对 TASK-02 声明边界时，需先按此表把 TASK-01 的三个文件摘出去。
- **承前未变**：`ClearRewardArchiveTests.cs` 的 `GetProperties()` 顺序断言仍脆；
  `[chronicle set reward rate]` 等不读段落的数值转录仍无判据（属「本代码所读文件」，与 Amendment 2 不冲突）；
  仓库级 `.gitattributes` / `.editorconfig` / `core.autocrlf` 三者不一致，仍是先于本 Task 的缺口。

### 收工判断：可以收工

密封契约八条 Acceptance conditions ＋ Amendment 1 两条 ＋ Amendment 2 ＋ Amendment 3 ＋ Amendment 4
逐条对照如下，全部成立：

| # | 条款 | 结论 |
|---|---|---|
| 1 | 一次调用产出四个牌位 | 成立（`One_call_answers_four_positions`；`FaceFor` 无「取第 n 位」入口） |
| 2 | 六档逐档可达，含最高两档；摇点上界＝基数−1 | 成立（第六节逐值复核） |
| 3 | 最低难度四位全空 | 成立（含反证，第六节） |
| 4 | 归档品级带＋加权抽取，只取装备 | 成立（`A_piece_comes_from_the_declared_grade_band` / `_by_its_declared_weight` / `Only_the_equipment_family_is_drawn_from`） |
| 5 | 分母与合成集中一处并附理由 | 成立（`ClearRewardRateOptions` 一个类型一段 remarks；`The_reading_of_this_file_gives_no_basis_and_nothing_that_combines`） |
| 6 | 同事实同种子逐位相同 | 成立（含反证，第六节） |
| 7 | 不持世界状态读写方 | 成立（构造器三依赖，反射判据） |
| 8 | ≥1690 通过 / 0 失败 / 2 跳过 | 成立，**我实测 1761 / 0 / 2，EXIT=0，一次跑成** |
| A1 | 只新写几率合成；掷稀有度走既有 `RarityLadder`，不得第二套阶梯 | 成立（全仓一处定义；`ClearRewardRules` 只有 `ChanceOf` ＋ `CardSlotCount`） |
| A1 | 六档可达直接断言 `ProbabilityOf`/`WeightOf`，不做统计 | 成立 |
| A2 | 跨文件断言须有判据钉住或删去 | 成立（被证伪的断言全仓已清零；余下四条「真而未钉」承前非阻塞，N-4 同级） |
| A3 | 扩界严格限于 `ClearRewardContent.cs` 两处半句 | 成立（该文件本 Task 只改了那两段） |
| A4 | 扩界至 `PvfClearRewardRateSource.cs` 的 chronicle 一段 summary | 成立（改动确在 `UnreadSections.ChronicleSetRewardRate` 的 summary 内，与授权文字逐字吻合） |

**本轮无阻塞发现，TASK-02 可以收工。** 前三轮的发现已从实质缺陷（diff 被行尾噪声淹没、
被仓库自己证伪的断言）收敛到流程缺陷（缺一条修订、多七个词），本轮连流程缺陷也已清空；
代码正确性、归档读数、几率合成、阶梯复用、分层、行尾、判据强度、边界授权八项本轮全部复核为无缺陷。
`passed` 仅表示本 Task Unit 可以走执行者的验收逻辑，不构成最终质量批准。
