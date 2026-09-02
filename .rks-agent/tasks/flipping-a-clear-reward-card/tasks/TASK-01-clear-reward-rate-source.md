# TASK-01 — 翻牌费率表可被读出，且每一段都有出处

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

新增一个只读 `etc/itemdropinfo_clearreward.etc` 的内容源，产出：具名档案下的基础率
（每档案若干等级段、每段单一率）、类别概率、物品类别概率、难度加成、地图数加成、
稀有度阶梯、队伍加成、付费成本表、付费兜底加权表。

- Referenced spec requirements: RQ-008
- Referenced plan sections: `plan.md` 的 T-001；边界 BD-001, BD-002, BD-003, BD-021
- Owned boundary: BD-001, BD-002, BD-003, BD-021
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`（新建）
- `Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`（新建）
- `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`（增注册行）
- `Server/tests/Dfo.Server.Infrastructure.Tests/`（新增判据）

- Dependencies: 无
- Required dependency outputs: 无
- Acceptance conditions:

1. 读出的基础率、难度加成、地图数加成、付费成本表与实测归档值逐值相同。
2. 稀有度阶梯复用既有 `RarityLadder`，不重新建模。
3. Spec 已表态不使用的三段（`[item drop rarity control]`、`[gold card create rate]`、
   `[chronicle set reward rate]`）在代码里被具名列出并写明为何不读。
4. 每个被读段落都有对应的标签常量与出处注释。
5. 全量判据 不低于 1690 通过、0 失败、2 跳过。

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
| CK-001 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --nologo` | 新增内容源判据通过；读出值与实测逐值相同 |
| CK-002 | `Non-command:` 逐段核对读取器与归档段落标签 | 每个被读段落有标签常量与出处注释；不使用的三段被具名列出并说明理由 |
| CK-S | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于 1690 通过、0 失败、2 跳过 |

- Required evidence: CK-001 的判据输出；CK-002 的逐段对照；全量判据输出。

## Execution Result — round 1

### 改动的文件（四处，无其他）

`git status --porcelain`（未提交，HEAD 仍在基线 c0cb6c99）：

```
 M Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs
?? Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs
?? Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs
?? Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs
```

1. **`Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`（新建）**
   契约与记录类型：`ClearRewardBand`（两个等级界 + 单一率）、`ClearRewardProfile`
   （具名档案 + 其若干段，带 `BandFor(level)`）、`ClearRewardProfiles`（归档实际拼写的
   六个档案名常量）、`ClearRewardBlankItem`（权重/物品/数量三元组）、`ClearRewardTable`
   （十二个成员，每个成员的 XML doc 写明它来自哪个段落）、`IClearRewardRateSource`。
   稀有度阶梯复用既有 `RarityLadder`，成员名为 `Rarity`（避免成员与类型同名）。
   查表方法在「表里没有这一项」时一律返回 `null` 而不是中性值：难度表首档就是 0，
   成本表的 0 是「免费」而不是「未定价」，两处都不能让缺席伪装成合法值。

2. **`Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs`（新建）**
   实现，照 `PvfDropRateTableSource` 的写法：`ConcurrentDictionary` 按路径缓存、
   `ReadSections` 一遍收段、`Integers`/`Reals`/`Single` 三个取值助手。差异有三处：
   - `ReadSections` 保留 `QuotedString` token（怪物表的版本只留数字），因为在这份档案里
     它们就是档案名，丢掉就只剩一串匿名整数。
   - `ReadProfiles`：字符串 token 开启一个新档案，其后的整数是它的段；再遇字符串就
     收束上一个。每个档案的整数数必须整除 `[drop prob count]`，且段宽减两个等级界必须
     恰好剩 1 个率，否则按名报错。**没有名字就先来数字的档案一律拒读**——那正是四张同族
     表的形状，宽容读它会得到一组「除得尽但没有意义」的段。
   - `ReadLadder`：这份档案只声明一组七个数（同族四表各声明四组），校验「末位 = 前一位 + 1」
     后建成单条 `RarityLadder`。
   段落标签共 12 个 `private const`，另有嵌套的 `internal static class UnreadSections`
   具名列出 4 个**不读**的段落及理由（见下 CK-002）。

3. **`Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`**（增 3 行：1 行注册 +
   2 行注释）：`services.TryAddSingleton<IClearRewardRateSource, PvfClearRewardRateSource>();`
   插在 `IDropRateTableSource` 那组之后、`IDropItemPoolSource` 之前。

4. **`Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs`
   （新建）** 12 条 `SkippableFact`，全部 `Skip.IfNot(ContentArchive.Available, …)`。
   既有用例的断言与期望值一个未动。

### 判据的真实输出

**CK-001** — `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test
Server/tests/Dfo.Server.Infrastructure.Tests --nologo`：

```
已通过! - 失败:     0，通过:   196，已跳过:     2，总计:   198，持续时间: 14 s
```

只跑新类（`--filter "FullyQualifiedName~PvfClearRewardRateSourceTests"`）：

```
已通过! - 失败:     0，通过:    12，已跳过:     0，总计:    12，持续时间: 3 s
```

该项目基线为 184 通过 + 2 跳过；196 = 184 + 12，新增 12 条全部真跑（未被 Skip 吞掉）。

**CK-S** — `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`，逐程序集：

| 程序集 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 51 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 376 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 196 | 2 | 0 |
| Dfo.Server.Application.Tests | 771 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 244 | 0 | 0 |
| Dfo.Server.Host.Tests | 4 | 0 | 0 |
| **合计** | **1702** | **2** | **0** |

1702 = 基线 1690 + 新增 12，失败 0，跳过仍是那 2 条（`PvfQuestDefinitionSourceTests` 与
`PvfTownDefinitionSourceTests` 的「缺档案时应报错」用例，因为档案在而跳过）。
一次跑成，没有出现已知的假红形状（进程/socket 超时、SQLite 30 秒）。

### 判据里记下的实测值（与 `PvfProbe` 直读逐值相同）

- **六个 profile × 4 段的基础率**：`default` 与 `pcroom default` 四段皆 10000；
  `event`、`event2`、`pcroom bonus` 四段皆 9000；`pcroom event` 为 9000 / 8000 / 8000 / 8000
  （唯一四段不同值的档案，用来证明段是逐档案取的而不是复制第一个）。
  四个等级界在每个档案里都是 1-15、16-30、31-40、41-200。
- **难度加成** `[dungeon difficulty drop bonusrate]`：`0 600 1000 1400 1200`，
  首档为 0（RQ-008 所依赖），且第五档小于第四档（非单调）。
- **地图数加成** `[reward item rate per map max count]`：1→2000、2→3000、3→4000、
  4→5000、5→6000；6 无值（返回 `null`）。
- **付费成本表** `[gold card cost table]`：1→100、5→580、86→10300、200→23980，共 200 条；
  判据另外逐级校验 `100 + (level-1)*120` 全部 200 级；0 与 201 返回 `null`。
- **队伍加成**：1 / 1.2 / 1.4 / 1.6（Single，比到 4 位小数）。
- **稀有度阶梯**：`500000 799900 999900 1000000 1000001 1000002 1000003`，
  基数 1000003、摇点上界 1000002、6 档，逐档 `WeightOf > 0`（最高两档可达）。
- **类别概率 / 物品类别概率**：`[10000, 0]` 与 `[0, 9500, 0, 500]`，只作原样转录断言。
- **兜底表**：`[gold card blank item]` 六行皆 10002120，权重 3000/3000/2000/1000/500/500，
  数量 1/2/3/4/5/10；`[pcroom card blank item]` 一行 50 / 7454 / 1。
- **品级带** `[item drop ref table]`：1→(1,4)、5→(2,8)、86→(79,89)，86 至 200 下界恒为 79；
  0 与 201 无带。注意 86 级是 **(79,89)**，怪物表同级是 (79,90)——这份档案自己的表。

### 意外与处理

1. **档案名不是「多个 token」，是一个 `QuotedString`。** 任务简报提示 `pcroom default`
   等由两个 token 组成、要「读到下一个整数之前都算名字」。用 `PvfProbe tokens` 实测：
   token 43 是单个 `QuotedString`，文本就是 `pcroom default`（含空格）。
   处理：仍按简报要求实现「连续字符串 token 用空格拼成一个名字」的规则——在 A21 上它是
   恒等操作，但它是不依赖归档恰好怎么引号化的那条规则。代码注释写明了这一点。
2. **`[item drop ref table]`（品级带）不在契约的产出清单里，但我读了它。** 理由：
   `plan.md` 的 T-002 声明其输入是「T-001 的内容源」且步骤 2 要「按品级带取范围」，
   而这份档案的品级带不能从既有 `IDropRateTableSource` 拿——用它加载 `ClearReward` 会因
   `[drop prob]` 形状不同而抛 `InvalidDataException`（我在判据 `A_table_whose_bands_name_no_profile_is_refused`
   的反方向确认了两者互不兼容）。且 86 级 (79,89) 与怪物表的 (79,90) 不同，不能借用。
   **这是我在契约产出清单之外多读的一段，请控制器确认是否接受。**
3. **多出第四个「不读」的段落。** 契约点名的是三段；档案里还有
   `[dungeon difficulty gold drop bonusrate]`（1.02/1.38/1.6/1.9/2）既不在产出清单、
   也不在 Spec 的不使用清单里。我把它一并具名列进 `UnreadSections`，理由与前三段不同：
   它不是「用途未查明」，而是**不适用**——Spec 的具名假设已写明「每张翻到的牌固定附带
   1 金币是本服务器的决定，归档没有把金币命中折算成金额的段落」，所以这张乘数表没有
   可乘的东西。不默默跳过，但也不读进表里冒充归档依据。
4. **TDD 的红是编译红。** 先写 12 条判据、跑出 `CS0246 未能找到类型 ClearRewardTable`，
   再写契约与实现。实现写完第一次跑就全绿，没有出现断言红——因为期望值是先用
   `PvfProbe dump/tokens` 直读归档抄下来的，不是从实现里反推的。这一点如实记录：
   本任务的红只证明了「判据先于实现存在」，没有证明「判据能抓住一个错误的读法」。

### 拿不准的地方（如实列出，未在代码里粉饰）

- **`[drop kind prob]` 两个位置各是什么，未查明。** 与 Spec 的 Assumptions 一致。
  我没有把它命名成「物品率/金币率」，而是原样作 `IReadOnlyList<int> DropKindRates` 转录，
  XML doc 明写「两个位置的含义未查明，同族其余各表都没有这一段，无从横向对照」。
  Spec 的「物品满值、金币为零」读法属于合成层，不在内容源里落地。
- **`[drop item type prob]` 四个位置与四个物品槽的对应，只有「数目相同」这一条支撑。**
  我同样只作四元列表转录，未提供按槽查表的方法，XML doc 写明这一点。
- **这份档案为什么只声明一组稀有度阶梯（同族四表各四组），未查明。** 代码只校验
  归档自己的不变式（末位 = 前一位 + 1）并按单组读出，没有替它编一个理由。
- **各段数值的标度（10000 / 600 / 9500 分别是多少分之几），归档没有声明。**
  内容源一律不缩放、不合成，注释写明合成与其假设属于上一层（T-002）。
- `[gold card create rate]` 存的是 `Single` 1.0 而非整数，与相邻各率的存法不同；
  这可能是它的用途线索，但我没有据此推断，仍记为未查明。

## Executor Self-check — round 1

### Acceptance conditions

| # | 条款 | 是否满足 | 依据 |
|---|---|---|---|
| 1 | 读出的基础率、难度加成、地图数加成、付费成本表与实测归档值逐值相同 | **满足** | 期望值先由 `PvfProbe dump/tokens` 直读归档抄出（见 Execution Result 的实测值清单），再写进判据；`The_six_named_profiles_pay_the_rates_the_archive_declares`（六档案 × 四段全部率 + 四个等级界）、`The_bonus_tables_are_the_numbers_the_archive_declares`（难度 5 值、队伍 4 值、地图数 5 对）、`The_paid_flip_cost_table_is_what_the_archive_declares`（1→100 / 5→580 / 86→10300 / 200→23980，另逐级校验全部 200 级）四条判据通过 |
| 2 | 稀有度阶梯复用既有 `RarityLadder`，不重新建模 | **满足** | `ClearRewardTable.Rarity` 的类型就是 `Dfo.Server.Abstractions.Content.RarityLadder`；新文件里没有第二个阶梯类型。`ReadLadder` 只校验归档不变式后 `new RarityLadder(values)`。判据 `The_rarity_ladder_is_the_group_the_archive_declares` 用它的 `Basis` / `HighestRoll` / `BandCount` / `WeightOf` 断言最高两档可达 |
| 3 | Spec 表态不使用的三段在代码里被具名列出并写明为何不读 | **满足** | `PvfClearRewardRateSource.UnreadSections` 三个 `internal const`：`RarityControl`、`GoldCardCreateRate`、`ChronicleSetRewardRate`，各带 XML doc 写明归档实值与「用途未查明，故不读」。另加第四个 `DifficultyGoldBonusRate` 并写明它是**不适用**而非未查明（见 Execution Result 意外 3）。判据 `The_sections_this_reading_skips_are_named_and_still_in_the_file` 拿这四个常量去比对档案自己的 identifier token 集合，防止清单里写了一个拼错的段落名 |
| 4 | 每个被读段落都有对应的标签常量与出处注释 | **满足** | 12 个被读段落各有一个 `private const string`，见下 CK-002 逐段对照表；`ClearRewardTable` 的每个 `<param>` 也写明它来自哪个段落标签 |
| 5 | 全量判据 不低于 1690 通过、0 失败、2 跳过 | **满足** | 1702 通过 / 0 失败 / 2 跳过（逐程序集分解见上表）。1702 = 1690 + 12 |

### Required checks

**CK-001** — 已跑，输出见上：`失败 0，通过 196，已跳过 2，总计 198`；新增类单独跑 12/12 通过。

**CK-002（`Non-command:` 逐段核对读取器与归档段落标签）** — 档案实有 16 个开启标签
（`[/…]` 关闭标签不携带值，不计），12 读 + 4 具名不读，无遗漏、无静默跳过：

| # | 归档段落 | 代码常量 | 去向 | 备注 |
|---|---|---|---|---|
| 1 | `[drop prob count]` | `BandCountSection` | 每档案的段数（4），用于校验段宽 | 不落入 record，只做校验 |
| 2 | `[drop prob]` | `ProfileSection` | `Profiles` | 本任务的核心形状差异 |
| 3 | `[drop kind prob]` | `DropKindSection` | `DropKindRates` | **位置含义未查明**，原样转录 |
| 4 | `[drop item type prob]` | `ItemTypeSection` | `ItemTypeRates` | 与槽位的对应只有「数目相同」支撑 |
| 5 | `[dungeon difficulty drop bonusrate]` | `DifficultySection` | `DifficultyRates` | 首档为 0 |
| 6 | `[basis of rarity dicision]` | `RaritySection` | `Rarity`（`RarityLadder`） | 单组七值 |
| 7 | `[party member drop bonusrate]` | `PartySection` | `PartyMemberRates` | Single |
| 8 | `[dungeon difficulty gold drop bonusrate]` | `UnreadSections.DifficultyGoldBonusRate` | **不读** | 不适用：牌的金币是本服务器的具名决定，无可乘之物 |
| 9 | `[reward item rate per map max count]` | `MapCountSection` | `MapCountRates` | 成对 |
| 10 | `[gold card cost table]` | `CostSection` | `GoldCardCosts` | 成对，200 级 |
| 11 | `[gold card create rate]` | `UnreadSections.GoldCardCreateRate` | **不读** | 用途未查明（存法是 Single，与邻居不同） |
| 12 | `[gold card blank item]` | `GoldBlankItemSection` | `GoldCardBlankItems` | 三元组 |
| 13 | `[pcroom card blank item]` | `PcRoomBlankItemSection` | `PcRoomCardBlankItems` | 三元组 |
| 14 | `[item drop ref table]` | `GradeBandSection` | `GradeBands`（`ItemGradeBand`） | 契约产出清单外，见意外 2 |
| 15 | `[item drop rarity control]` | `UnreadSections.RarityControl` | **不读** | 用途未查明 |
| 16 | `[chronicle set reward rate]` | `UnreadSections.ChronicleSetRewardRate` | **不读** | 用途未查明 |

**CK-S** — 已跑，1702 通过 / 0 失败 / 2 跳过，见上表。

### Required methods

- **`test-driven-development`**：判据先写并跑出编译红（`CS0246 ClearRewardTable`），
  再写契约与实现。诚实补一句：实现完成后第一次跑即全绿，没有经历断言红，
  所以红只证明了判据先于实现存在（见意外 4）。
- **分层**：内容源在 `Infrastructure`、契约在 `Abstractions`，命名走 `*Source` 后缀，
  在 `InfrastructureModule` 注册为单例。本任务不写规则服务、不写 handler、不写命令，
  故「只被规则服务注入」「禁止嵌套 dispatch」「持久写一律 Transactional」三条在本轮
  无可违反之处；契约形状（无状态、只按路径取表、返回不可变 record）允许 T-002 按该约束注入。
- **XML doc remarks 写明出处、未查明处不填数**：见 CK-002 表与「拿不准的地方」一节。
  `[drop kind prob]`、`[drop item type prob]` 两处坚持原样转录，没有替它们命名含义。
- **`System.*` using 在前、不做字母序、最小插入**：
  `PvfClearRewardRateSource.cs` 为 `System.Collections.Concurrent` → `Dfo.Pvf` →
  `Dfo.Pvf.Tokens` → `Dfo.Server.Abstractions.Content`（新文件，照同目录样板的顺序）；
  测试文件只在原有两行前插入 `using Dfo.Pvf.Tokens;`（与同目录样板一致），未重排；
  `InfrastructureModule.cs` 的 using 一行未动。
- **`TreatWarningsAsErrors`**：全量构建 0 警告 0 错误通过（任何警告都会让上述判据无法跑到绿）。
- **归档 GBK**：本任务只读整数与 ASCII 标签；唯一读字符串的地方是档案名，
  经 `archive.ResolveString` 走既有解码路径，六个名字断言逐字通过。

### 边界与授权

- 只改了 Permitted files 列出的四处，`git status` 无第五处改动。
- 未建任何 git 提交，HEAD 仍在基线 `c0cb6c99`；未合并、未推送、未建 PR、未删分支。
- 既有判据的断言与期望值一个未改（BD-021 的约束）。

## Contract Amendment 1 — 控制器批准的有界扩界

**事件**：执行者在 round 1 中多读了一段 `[item drop ref table]`（品级带），它不在本 Task
密封契约的产出清单里。

**控制器裁定：批准，并且这是本轮最有价值的一处。** 理由经独立复核成立：

在同一份 A21 归档上逐级比对两张表的 `[item drop ref table]`：

逐行比对两张表全部 200 级（下方范围于 QR-2 后订正，原文误作「86 级以上」）：

- **下探列 200 行完全相同。**
- **上探列只在 80–89 这十级不同**：本表 3，怪物表 4。其余 1–79 与 90–200 全同。

若按原契约不读这一段、而让 T-002 借用既有
`IDropRateTableSource` 的怪物表品级带，80–89 级的翻牌会悄悄多开一个品级——
一个没有任何报错、没有任何判据会发现的错值。这正是本 Spec 的约束
（数值依据必须来自归档、不得照抄）与 SC-6 所防的那一类失败。

执行者另报该表经既有内容源加载会抛错，故亦无法借用。

**契约相应修订**：Goal and deliverables 的产出清单增补「品级带（`[item drop ref table]`）」。
Acceptance conditions 增补一条：品级带须读自翻牌表自身，不得借用同族其他表。
Spec 的验收边界（RQ-008/009/010）未变，故按有界修订就地记账，不新建替代 Task。

**第二处扩界一并批准**：具名的不读段落由三段增至四段，新增
`[dungeon difficulty gold drop bonusrate]`，且其理由记为「不适用」而非「未查明」——
归档没有把牌的金币命中折算成金额的段落，金额是 Spec 里具名的服务器决定，
乘数无物可乘。这个区分是对的：把它读进来会让一个归档数字挨着一个非归档数字，
正是这张清单要避免的混淆。

**一处简报勘误**：派工简报称 profile 名是「两个 token」，实测为**单个含空格的
QuotedString**。执行者按实测实现并如实报告。错在简报，不在执行。

## Independent Quick Review — round QR-1

- Reviewer: 独立 quick 评审者（未参与执行）
- Reviewed contract revision: 1 + Contract Amendment 1
- Verdict: **changes_required**

### 复核的 diff 身份

工作树未提交，HEAD 仍在基线 `c0cb6c99`。本轮 `git status --short` 实见：

```
 M Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs
?? .rks-agent/tasks/flipping-a-clear-reward-card/
?? Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs
?? Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs
?? Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs
```

产品代码只此四处，与 Permitted files（BD-001/002/003/021）逐条对上，无第五处。第五个条目是
本流程自己的 `.rks-agent/` 制品目录（未被 `.gitignore` 覆盖），不属产品代码，不计越界。

### 我自己跑的证据

| 命令 | 我实测的输出 |
|---|---|
| `PVF_ARCHIVE_PATH=… dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_clearreward.etc 4000` | 16 个开启标签，全段原文 |
| `PVF_ARCHIVE_PATH=… dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_monseter.etc 4000` | 用于比对品级带与 `[drop prob]` 形状 |
| `PVF_ARCHIVE_PATH=… dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --nologo` | `失败: 0，通过: 196，已跳过: 2，总计: 198` |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 376+51+60+244+771+4+196 = **1702 通过 / 2 跳过 / 0 失败** |
| scratchpad 内一次性 console（引用 `Dfo.Pvf`，未写入仓库） | 逐 token 打印 kind，用于核对档案名与 Single/Int32 的存法 |

跳过的两条即 `PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的「缺档案时应报错」
用例，跳过数未暴涨，`PVF_ARCHIVE_PATH` 确已生效。未遇已知假红形状。

### 逐值核对（不采信执行者列的数字，全部比着我自己的 dump）

- `[drop prob count]` = 4；`[drop prob]` 六个档案 × 四段，我 dump 出的率与判据期望**逐值相同**：
  `default` / `pcroom default` 四段皆 10000；`event` / `event2` / `pcroom bonus` 四段皆 9000；
  `pcroom event` 为 9000 / 8000 / 8000 / 8000。四个等级界 1-15 / 16-30 / 31-40 / 41-200 亦相同。
- **档案名的 token 形状**：我的逐 token 打印证实 `default`(#3)、`event`(#16)、`event2`(#29)、
  `pcroom default`(#42)、`pcroom bonus`(#55)、`pcroom event`(#68) 各是**单个 `QuotedString`**，
  空格在引号内。执行者的「简报勘误」属实；`ReadProfiles` 用连续字符串拼名的写法在 A21 上是恒等
  操作且不依赖引号化方式，读法正确。
- `[gold card cost table]`：抽样点 1→100、5→580、86→10300、200→23980 全部对上；200 条整表
  确为 `100 + (level-1)*120` 的等差，判据逐级校验的 200 次断言有效。
- `[item drop ref table]` 高等级段：我解析出 200 行，`1 0 3`→(1,4)、`5 3 3`→(2,8)、
  `86 7 3`→**(79,89)**，且 86–200 的下界恒为 79（我逐级验过）。
  同一归档的怪物表 `etc/itemdropinfo_monseter.etc` 在 86 为 `7 4`→(79,**90**)、87 为 `8 4`→(79,91)。
  **Contract Amendment 1 关于两表上探差 1 的裁定，经我独立复核成立**，读自身表是必需的。
- `[dungeon difficulty drop bonusrate]` `0 600 1000 1400 1200`（首档 0、第五档小于第四档）、
  `[reward item rate per map max count]` 五对、`[drop kind prob]` `10000 0`、
  `[drop item type prob]` `0 9500 0 500`、`[gold card blank item]` 六行皆 10002120、
  `[pcroom card blank item]` `50 7454 1`、`[basis of rarity dicision]` 七值，全部与判据期望相同。
- `[party member drop bonusrate]` 四个值**全部是 `Single`**（含首位的 `1`），故 `Reals` 而非
  `Integers` 是必需的；`[gold card create rate]` 确为 `Single` 的 `1`，
  `[dungeon difficulty gold drop bonusrate]` 五个值确为 `Single` —— 三处 XML doc 的存法描述属实。

### 稀有度阶梯（点 2）

`ClearRewardTable.Rarity` 的类型就是既有 `Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs:60`
的 `RarityLadder`，新 diff 里没有第二个阶梯类型。`ReadLadder`
（`PvfClearRewardRateSource.cs:282-297`）只校验「末位 = 前一位 + 1」后直接 `new RarityLadder(values)`。
我按 `RarityLadder` 的定义手算：Basis 1000003、HighestRoll 1000002、BandCount 6，
`WeightOf(4) = 1`、`WeightOf(5) = 1` —— 最高两档确实可达。**没有重蹈写死一百万的覆辙。**

### 分层与判据价值（点 4、点 7）

- `Dfo.Server.Abstractions.csproj` 不引用 `Dfo.Pvf`，`ClearRewardContent.cs` 亦未用任何 Pvf 类型；
  实现 `internal sealed`，落在 `Dfo.Server.Infrastructure`，经 `InternalsVisibleTo` 供判据构造。
  `InfrastructureModule.cs` 只增 1 行注册 + 2 行注释，未引入新依赖。**无分层问题。**
- 判据确实钉的是归档实值而非实现常量：把实现换成「借怪物表品级带」会被
  `The_grade_bands_are_this_own_files_rows`（期望 (79,89)）当场抓住；把成本表读成
  「先价后级」会被 200 级等差循环抓住；把摇点写死一百万会被
  `The_rarity_ladder_is_the_group_the_archive_declares` 抓住。
  `A_table_whose_bands_name_no_profile_is_refused` 是真断言：我确认怪物表 `[drop prob]` 是
  10 段裸整数、无字符串 token，宽容读法确会得到「除得尽但无意义」的段。
- 弱处两条（不构成阻塞）：
  `PvfClearRewardRateSourceTests.cs:97-98` 的 `Assert.Null(…ProfileNamed(Default)?.BandFor(201))`
  与 `Assert.Null(table.ProfileNamed("pcroom"))` 在 `ProfileNamed` 整体返回 null 时也会绿
  （同一用例前面的断言已另行钉住 `Default`，故未被掩盖）；
  `PvfClearRewardRateSourceTests.cs:369-371` 的「确未读进来」只查了 `-5` 是否落在三个列表里，
  对另外三段的泄漏无话可说（`PartyMemberRates` 的泄漏由第 129-133 行另行钉住）。

### 要求整改（changes_required）

1. **`Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs:93`** ——
   `ClearRewardTable` 的 remarks 写「**Three** sections of the file are deliberately not read at
   all」，而 `PvfClearRewardRateSource.UnreadSections`（`PvfClearRewardRateSource.cs:106-138`）
   具名列出的是**四**段。Contract Amendment 1 已把不读清单批准为四段，契约层这句话未同步。
   这是本 diff 自身引入的内部矛盾，且恰好落在验收条款 3/4 所管的那份清单上。改一个词即可。

2. **`Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs:326-327`** ——
   `ReadGradeBands` 的 remarks 写「level one reaches **three below** itself, and a band starting
   under grade zero would be a span no item can be in」，为 `Math.Max(0, …)` 的钳制作辩护。
   我 dump 的归档里该表第一行是 `1 0 3`：**下探 0、上探 3**。我逐行算过 200 行，
   `level - below` 的最小值是 **1**，从不为负，钳制在这份档案上是死代码，
   其理由陈述的是一条归档没说的「事实」。
   （同样一句在既有 `PvfDropRateTableSource.cs:209-211` 里也有，属仓库既存问题、不在本任务范围；
   但本任务的立身之本是「每一处数值都能指回归档中声明它的位置」，新文件不宜复制一条错的归档陈述。）
   要么按归档改写这句注释，要么如实写明该钳制是防御性的、A21 上不触发。

3. **执行记录的两处引证不实，请就地更正**（不涉及代码）：
   - Execution Result 引的 `git status --porcelain` 只列了四条，实跑另有
     `?? .rks-agent/tasks/flipping-a-clear-reward-card/`。结论（产品代码只四处）不受影响，
     但引的输出不是该命令实际打印的。
   - Execution Result 意外 1 与 Contract Amendment 1 均称以 `PvfProbe tokens` 实测 token 43。
     `Tool/PvfProbe` **没有 `tokens` 子命令**（其用法只有 dump / lst / tags / values / where /
     name / files / pool）。所述事实我已用自己的探针独立证实为真，但请把出处改成实际使用的手段。

### 未构成阻塞的判断

读数正确性、`RarityLadder` 复用、不读段落的具名与「未查明 / 不适用」之分、分层、改动范围、
两条判据的实跑结果（196/2/0 与 1702/2/0，满足验收条款 5 的「不低于 1690 通过、0 失败、2 跳过」）
均已核实成立。三条整改都是文档与记账层面的一行修正，不改行为、不会动摇现有判据。

## Execution Result — round 2（控制器就地整改 QR-1）

QR-1 的三条发现逐条处理。**没有一处改动触及行为**：两处是文档，一处是记录订正。

### 发现 1 — 采纳并已改

`ClearRewardContent.cs` 的类型注释仍写「Three sections … not read」，而 Contract Amendment 1
已批准具名四段。现改为四段，并顺手把「三段因未查明、一段因不适用」这个区分写进注释——
该区分本来只存在于读取器一侧，契约一侧看不到。

### 发现 2 — 采纳并已改，这是三条里唯一有分量的

`PvfClearRewardRateSource.cs` 的 `ReadGradeBands` 注释原文为：

> The lower edge is clamped at zero for the same reason it is there: level one reaches three below
> itself, and a band starting under grade zero would be a span no item can be in.

**归档不是这么说的。** 该表首行是 `1 0 3`，即 1 级下探 **0**、上探 3；全部 200 行里
`level - below` 的最小值是 1。所以「1 级下探三级」是错的，钳制在这份归档里从不触发，
而这条注释把一个归档没说的「事实」当成钳制存在的理由写了进去。

这正是本 Spec 的约束所禁止的形状：一个没有归档出处的说法混在有出处的读数中间。
评审抓得对。

改后的注释做两件事：如实说明钳制是防御性的、在这份归档里从不触发；
并把 Amendment 1 里那条真正的理由（86 级起本表上探 3、怪物表上探 4，借用会静默多开一档）
写到代码里——它此前只活在流程产物里，读代码的人看不见。

### 发现 3 — 前半采纳，后半有异议

**3a（采纳）**：Execution Result 引的 `git status --porcelain` 只列了四条产品文件，
漏了 `?? .rks-agent/tasks/flipping-a-clear-reward-card/`。引证应完整，此处订正：
实际未跟踪项还包括该流程产物目录，它不属产品改动但确实在状态里。

**3b（异议，附反证）**：评审称「`Tool/PvfProbe` 没有 `tokens` 子命令」，据以判定执行记录
两处引证不实。**该判定不成立。**

- `Tool/PvfProbe/Program.cs:42` 有 `case "tokens":` 分支。
- 实跑可用：
  `PVF_ARCHIVE_PATH=… dotnet run --project Tool/PvfProbe -- tokens etc/itemdropinfo_clearreward.etc 8`
  输出前八个 token，其中第 4 个正是
  `4  QuotedString  string=True  value=13878758  default`
  ——顺带再次印证档案名是单个 `QuotedString` 而非两个 token。

评审的依据应是 `Usage()` 的用法文本，它确实**没有列出** `tokens`。所以真实情况是
**该子命令存在但未在用法里登记**，不是不存在。执行者的引证有效。

（`Tool/PvfProbe` 的用法文本漏列 `tokens` 是那个工具自己的小缺陷，不在本 Task 边界内，
不在此处修。记下来供日后。）

### 判据（round 2 重跑）

见下方自检。改动仅注释，但仍整跑，因为本仓 `TreatWarningsAsErrors`，
XML doc 写坏会当场变成编译错误。

## Executor Self-check — round 2

- QR-1 发现 1：已改，`ClearRewardContent.cs` 现称四段并记下「未查明/不适用」的区分。
- QR-1 发现 2：已改，钳制注释不再声称归档没说的事，并把真正的理由写进了代码。
- QR-1 发现 3a：已在本轮记录中订正引证。
- QR-1 发现 3b：不采纳，附可复跑的反证（`Program.cs:42` 与实跑输出）。按规程，
  争议不由执行侧自行了结——本轮连同反证一并交由新的独立评审者裁定。
- 行为未变：本轮只动注释与记录，无一行可执行代码改动。
- 契约验收条款 1–5 的成立依据与 round 1 相同，未受本轮影响。

## Independent Quick Review — round QR-2

- Reviewer: 独立 quick 评审者（第二轮，未参与执行，亦非 QR-1 的评审者）
- Reviewed contract revision: 1 + Contract Amendment 1
- Reviewed rounds: round 1 + QR-1 + round 2（整改）
- Verdict: **changes_required**

### 复核的 diff 身份

工作树未提交，HEAD 仍在基线 `c0cb6c99`。本轮 `git status --short` 实见五条，产品代码四处：

```
 M Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs
?? .rks-agent/tasks/flipping-a-clear-reward-card/
?? Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs
?? Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs
?? Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs
```

与 Permitted files（BD-001/002/003/021）逐条对上，无第五处产品改动。第五条是本流程自己的
`.rks-agent/` 制品目录，不计越界。

### 我自己跑的证据

| 命令 | 我实测的输出 |
|---|---|
| `PVF_ARCHIVE_PATH=… dotnet run --project Tool/PvfProbe -- tokens etc/itemdropinfo_clearreward.etc 75` | **命令存在且成功**，逐 token 打印 kind/IsString/value/text |
| `PVF_ARCHIVE_PATH=… dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_clearreward.etc 4000` | 40 行，16 个开启标签，`[item drop ref table]` 完整 600 数（未截断） |
| `PVF_ARCHIVE_PATH=… dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_monseter.etc 4000` | 26 行，`[item drop ref table]` 完整 600 数 |
| 对两份 600 数逐行比对（scratchpad 内一次性脚本，未写入仓库） | 见下「裁定三」 |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 376+51+60+771+244+4+196 = **1702 通过 / 2 跳过 / 0 失败**，退出码 0，一次跑成 |
| `PVF_ARCHIVE_PATH=… dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --nologo -v n` | 跳过的两条实名为 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog` |

未遇已知假红形状（进程/socket 超时、SQLite 30 秒），无需隔离复跑。`TreatWarningsAsErrors`
下全绿即 0 警告。跳过数仍是 2 而非更多，说明新增 12 条判据全部真跑、未被 `Skip` 吞掉
（196 = 基线 184 + 12）。验收条款 5（不低于 1690 通过 / 0 失败 / 2 跳过）**成立**。

### 裁定一：QR-1 三条发现的整改是否到位

| QR-1 发现 | 我的裁定 | 依据 |
|---|---|---|
| 1（`ClearRewardContent.cs` 写「三段不读」而读取器列四段） | **已真正解决** | `ClearRewardContent.cs:93-96` 现为「Four sections … three because their purpose is not ascertained, and one because it is inapplicable rather than unknown」，与 `UnreadSections`（`PvfClearRewardRateSource.cs:106-138`）的四个常量一致，且把「未查明/不适用」的区分补进了契约一侧 |
| 2（`ReadGradeBands` 注释为钳制编造归档理由） | **部分解决，且整改本身引入了新的同类错误** | 见「裁定三」。钳制那一段（`PvfClearRewardRateSource.cs:333-336`）现在**属实**；但同一 remarks 新写的上一段（`:327-330`）又说了一条归档不支持的话 |
| 3a（round 1 引的 `git status --porcelain` 漏了 `.rks-agent/`） | **已解决** | round 2 以追加订正而非改写 round 1 的方式处理，符合「不重写既往轮次」的规程；round 1 的原引证块保持原样可核 |
| 3b（称 `PvfProbe` 无 `tokens` 子命令） | **QR-1 判定错误，见裁定二** | — |

### 裁定二：`tokens` 子命令之争，**round 2 对，QR-1 错**

我不采信任何一方转述，自己看源码、自己跑：

- **源码**：`Tool/PvfProbe/Program.cs:42` 确有 `case "tokens":`，实现体在 42-69 行，
  签名为 `tokens <path> [limit] [skip]`。
- **实跑**：`PVF_ARCHIVE_PATH=… dotnet run --project Tool/PvfProbe -- tokens etc/itemdropinfo_clearreward.etc 75`
  成功返回，退出码 0。
- **round 1 引的那一条也属实**：该命令打印的 **token 43** 正是

```
43	QuotedString	string=True	value=53499064	pcroom default
```

  即单个含空格的 `QuotedString`，与 round 1「意外 1」及 Contract Amendment 1「简报勘误」
  所述**逐字相符**，token 序号也对得上。
- **QR-1 说对了的那半句**：`Usage()`（`Program.cs:272-290`）的用法文本确实只列
  dump / lst / tags / values / where / name / files / pool，**没有** `tokens`。

**结论**：真实情况是「该子命令存在、可用、但未在用法文本里登记」。QR-1 由用法文本反推
「没有这个子命令」，前提错误，据此得出的「执行记录两处引证不实」这一发现**不成立，应予撤销**。
执行者 round 1 的引证有效，round 2 的异议成立。

附带一个旁证：QR-1 自己列的 token 序号（`default` #3、`pcroom default` #42）与 `PvfProbe tokens`
的 1-based 序号（#4、#43）整体差 1，可见 QR-1 用的是自建 0-based 探针而非本工具——它没有
跑过这个命令就断言了它不存在。这正是本 Task 反复要求的那件事：断言要有实跑出处。

`Usage()` 漏列 `tokens` 是 `Tool/PvfProbe` 自身的小缺陷，不在本 Task 的 Permitted files 内，
同意 round 2 的处置（记下，不在此修）。

### 裁定三：整改引入的新问题（本轮唯一的 changes_required）

round 2 改写后的 `ReadGradeBands` remarks 有两段断言，我按审别人断言的标准逐条查证：

**（B）钳制那一段 —— 属实，通过。**
`PvfClearRewardRateSource.cs:333-336`：「across all two hundred rows the smallest
`level - below` is one (level one declares zero below, not three), so the clamp never fires here」。
我从 dump 解析出 200 行三元组逐行计算：`min(level - below) = 1`，在 level 1/2/3/4 上取到；
无一行为负；首行确为 `1 0 3`（下探 0）。**三项全部属实**，钳制在 A21 上确为死代码，
现在的表述（防御性、此档案不触发）是诚实的。QR-1 发现 2 的本体已被正确修复。

**（A）与怪物表对比那一段 —— 不属实，构成新缺陷。**
`PvfClearRewardRateSource.cs:327-330` 现写：

> **This file's rows are not the sibling family's rows.** They agree up to level 85 and
> part company above it: at level 86 this file says three above and the monster table says
> four, so a reader that borrowed the sibling's band would quietly open one extra grade to
> every high-level flip, with nothing anywhere to say it had.

我把两张表 200 行的 `above` 列逐级比对，实测结果是：

| 等级区间 | 本表 above | 怪物表 above | 是否相同 |
|---|---|---|---|
| 1 – 79 | 3 | 3 | 相同 |
| **80 – 89** | **3** | **4** | **不同（仅此 10 级）** |
| 90 – 200 | 3 | 3 | 相同 |

`below` 列 200 行**逐级完全相同**，无一处差异；本表 `above` 全 200 行恒为 3，
怪物表 `above` 取值为 4 的等级**恰好是 80..89 这十级**，此外全为 3。抽样对照：

```
L79: cr (79,7,3)->(72,82)   | mo (79,7,3)->(72,82)    same
L80: cr (80,7,3)->(73,83)   | mo (80,7,4)->(73,84)    DIFF
L85: cr (85,7,3)->(78,88)   | mo (85,7,4)->(78,89)    DIFF
L86: cr (86,7,3)->(79,89)   | mo (86,7,4)->(79,90)    DIFF
L89: cr (89,10,3)->(79,92)  | mo (89,10,4)->(79,93)   DIFF
L90: cr (90,11,3)->(79,93)  | mo (90,11,3)->(79,93)   same
L200:cr (200,121,3)->(79,203)| mo (200,121,3)->(79,203) same
```

于是这段注释里：

1. **「They agree up to level 85」是错的**——两表在 **level 80 就已分家**，80-85 这六级
   同样是 3 对 4。85 级本身就是不同的那一档，注释却把它划进「相同」的一侧。
2. **「part company above it」「every high-level flip」是错的**——两表在 **level 90 重新一致**
   并一路相同到 200。借用怪物表并不会影响「每一次高等级翻牌」，只影响 80-89 这十级。
3. 中间那句「at level 86 this file says three above and the monster table says four」
   **是对的**——但它是一个抽样点，被包在两句归档不支持的概括里。

这与 QR-1 发现 2 是**同一类缺陷**：一条归档没说的「事实」被写成注释里的既定陈述。
它恰好是在修复该缺陷的过程中新写进去的。本 Task 的立身之本（验收条款 4「每个被读段落
都有对应的标签常量与出处注释」，以及 Spec 的 RQ-013 / AC-017「每一处规则数值都能指回归档中
声明它的位置」）管的正是这类陈述，故不能作为文风问题放过。

需要说明的是：**Contract Amendment 1 的裁定本身依然成立，且理由更强而非更弱。**
分家区间 80-89 完整覆盖本服务器的 86 级上限，正是终局那一段；借用怪物表会在 80-89 级
静默多开一档。要改的是这句话的**范围陈述**，不是这个决定。

（`ClearRewardContent.cs:149-153` 的 `GradeBands` 参数文档只说「at level 86 a card reaches one
grade less far up than a monster kill does」，是单点陈述，**属实，无需改**。）

### 要求整改（changes_required）

1. **`Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs:327-330`** ——
   按归档实测改写范围陈述。事实是：两表的 `below` 列 200 行完全相同；`above` 列**仅在
   level 80-89 这十级**不同（本表 3、怪物表 4），level 1-79 与 90-200 相同。
   建议保留「借用会静默多开一档」这个结论并点明它落在 80-89、覆盖 86 级上限，
   删去「agree up to level 85」与「every high-level flip」这两处与归档相悖的概括。
   只改注释，不改行为，不动任何判据期望值。

2. **（记账，非代码）** Contract Amendment 1 中「高等级段两张表的上探差 1」及其只列
   1/50/86/87 四级的对照表，同样会让读者以为 87 级以上一直不同。请在控制器一侧以追加
   订正的方式记下实测区间为 80-89，勿改写既有段落。此项不阻塞代码。

3. **（记账，非代码）** QR-1 发现 3b 应记为**不成立并撤销**，理由见裁定二。
   round 2 的异议成立，无需就此再做任何代码或记录改动。

### 我独立复核过、结论与 QR-1 一致因而不再重复要求的部分

- **读数逐值对得上归档**：`[drop prob]` 六档案 × 四段（`default` 与 `pcroom default` 皆 10000；
  `event`、`event2`、`pcroom bonus` 皆 9000；`pcroom event` 为 9000/8000/8000/8000）与四个等级界
  1-15 / 16-30 / 31-40 / 41-200；`[drop kind prob]` 为 `10000 0`；`[drop item type prob]` 为 `0 9500 0 500`；
  `[dungeon difficulty drop bonusrate]` 为 `0 600 1000 1400 1200`（首档 0、第五档小于第四档）；
  `[basis of rarity dicision]` 七值；`[party member drop bonusrate]` 为 `1 1.2 1.4 1.6`；
  `[reward item rate per map max count]` 五对且无 6；`[gold card cost table]` 200 条等差
  `100+(level-1)*120`（1→100、5→580、86→10300、200→23980）；`[gold card blank item]` 六行
  皆 10002120；`[pcroom card blank item]` 为 `50 7454 1`；四个不读段落的 XML doc 所引实值
  （`2 13 17 0 -5`、`1`、`0 50 1 75 2 100`、`1.02 1.38 1.6 1.9 2`）—— **全部与我的 dump 逐值相同**。
- **稀有度阶梯复用既有 `RarityLadder`**：`ClearRewardContent.cs:162` 的 `Rarity` 就是
  `DropRateContent.cs:60` 的 `RarityLadder`；新 diff 中没有第二个阶梯类型，
  `ReadLadder`（`PvfClearRewardRateSource.cs:282-297`）只校验归档不变式后 `new RarityLadder(values)`。
- **改动范围未越界**：四处产品文件；`InfrastructureModule.cs` 只增 3 行（1 行注册 + 2 行注释）、
  未引入新依赖；`Dfo.Server.Abstractions.csproj` 不引用 `Dfo.Pvf`，`ClearRewardContent.cs`
  未出现任何 Pvf 类型，分层成立。
- **判据钉的是归档实值而非实现常量**：`The_grade_bands_are_this_own_files_rows`
  （`PvfClearRewardRateSourceTests.cs:275-283`）钉 (1,4) / (2,8) / 86→(79,89) 及 86-200 下界恒 79
  —— 我逐级验过 86-200 的 `level-below` 集合确为 `{79}`；把实现换成借怪物表会在 86 级
  当场变红。成本表 200 级等差循环、稀有度阶梯的 `Basis`/`HighestRoll`/`BandCount`/`WeightOf`
  亦同。**未发现「实现错了也会绿」的判据。**

### 未构成阻塞、留给最终评审的观察

- `PvfClearRewardRateSourceTests.cs:366-371` 的注释说「the table carries eleven numeric members,
  none of which holds the five numbers of `[item drop rarity control]` or the three pairs of
  `[chronicle set reward rate]`」，而其下只对 3 个成员断言了 `DoesNotContain(-5)`，
  对 `[chronicle set reward rate]` 的泄漏一句未查。这是注释比断言说得多，与本轮缺陷同源
  但轻得多（QR-1 亦已记为非阻塞）。不在本轮要求整改之列。
- round 1「意外 4」如实记录「本任务的红只证明判据先于实现存在，未证明判据能抓住错误读法」。
  这个坦白是对的，且我上一节的反向验证部分补上了它：至少品级带、成本表、稀有度阶梯三处，
  错误读法会被现有判据抓住。

### 备注

本轮只读、只核、只追加，未修改任何产品代码、判据或既往轮次。

## Execution Result — round 3（控制器就地整改 QR-2）

QR-2 只提一条阻塞项，而它指向的是 **round 2 整改自己引入的缺陷**，不是 round 1 的残留。
采纳，已改。

### QR-2 的发现成立，且是本 Task 到目前为止最值得记的一条

round 2 在修「注释声称了归档没说的事」时，写下了新的一句：

> They agree up to level 85 and part company above it: at level 86 this file says three above and
> the monster table says four … every high-level flip

**这句话是从两个采样点（86、87）推出来的全称结论，两头都错。** 控制器逐行比对两张表
全部 200 级后确认：

- **下探列 200 行完全相同**；
- **上探列只在 80–89 这十级不同**（本表 3，怪物表 4），1–79 与 90–200 全同。

即：80–85 也不同（原句说相同），90–200 并不不同（原句说一直不同）。

**这正是 round 2 所修的同一类缺陷，在修它的过程中被重新犯了一次。** 记在此处而不是悄悄改掉，
因为它说明「无归档出处的断言」不是执行者一个人的毛病，而是这个主题上人人都会犯的——
包括做整改的控制器本人。本 Task 的判据能钉住读数，钉不住注释里的断言；
能钉住它的只有像 QR-2 这样有人拿着档案逐行去核。

### 改了什么

1. `PvfClearRewardRateSource.cs` 的 `ReadGradeBands` 注释：把范围改成实测的
   「下探列全同，上探列只在 80–89 这十级不同」，并说明那十级正是 A21 毕业段所在。
2. Contract Amendment 1 的表述同样偏窄，一并订正：删去只列四个采样等级的小表与
   「86 级以上」的说法，改为逐行比对的完整结论，并注明是 QR-2 之后的订正。
   **Amendment 1 的裁定本身不变，且理由更强**——80–89 完整覆盖了 86 级这个边界，
   借用怪物表会在整个毕业段而不是零星几级上出错。

### QR-2 对 QR-1 发现 3b 的裁定

QR-2 独立验证后判定 **round 2 对、QR-1 错**：`Program.cs:42` 确有 `case "tokens":`，
命令实跑成功。QR-1 是从 `Usage()`（该处确实漏列）反推「不存在」。**QR-1 发现 3b 撤销。**
该争议至此由独立评审侧了结，执行侧未自行降级任何发现。

## Executor Self-check — round 3

- QR-2 唯一阻塞项：已改，范围陈述改为逐行实测结果。
- 该订正本身的两句断言（下探列全同、上探列差异恰为 80–89）由控制器以
  `PvfProbe dump` 导出两表全部 200 行、逐行 join 比对得出，可复跑。
- Contract Amendment 1 的裁定未变，仅订正其范围表述。
- 行为未变：round 2 与 round 3 合计只动注释与流程记录，无一行可执行代码改动。
- QR-1 的发现 1、2、3a 在 round 2 已处置，本轮未回退。
- 判据：见下轮重跑结果。

## Independent Quick Review — round QR-3

- Reviewer: 独立 quick 评审者（第三轮，未参与执行，亦非 QR-1 / QR-2 的评审者）
- Reviewed contract revision: 1 + Contract Amendment 1（QR-2 之后的订正版）
- Reviewed rounds: round 3 整改；并回查 QR-2 阻塞项与 QR-1 发现 1 / 2 / 3a 的现状
- Verdict: **changes_required**

先把结论摆正：**QR-2 的那一条已经修对了**，我逐行复核过，一个不多一个不少；
Amendment 1 的范围表述也一并订正到位；round 3 没有再引入新的无出处断言。
本轮唯一的阻塞项是**同类的另一句，它从 round 1 起就在，QR-1 与 QR-2 都没查到**。

### 复核的 diff 身份

工作树未提交，HEAD 仍在基线 `c0cb6c99`。本轮 `git status --porcelain` 实见五条，产品代码四处：

```
 M Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs
?? .rks-agent/tasks/flipping-a-clear-reward-card/
?? Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs
?? Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs
?? Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs
```

与 Permitted files（BD-001/002/003/021）逐条对上，无第五处产品改动；第五条是本流程自己的
`.rks-agent/` 制品目录，不计越界。`git diff` 实见 `InfrastructureModule.cs` 仅 +3 行
（1 行 `TryAddSingleton<IClearRewardRateSource, PvfClearRewardRateSource>()` + 2 行注释），
无新 using、无新依赖。

### 我自己跑的证据

| 命令 | 我实测的输出 |
|---|---|
| `PVF_ARCHIVE_PATH=… dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_clearreward.etc 4000` | 40 行，16 个开启标签；`[item drop ref table]` 在第 33/34 行，**600 个数未截断** |
| `PVF_ARCHIVE_PATH=… dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_monseter.etc 4000` | 26 行；`[item drop ref table]` 在第 14/15 行，**600 个数未截断** |
| scratchpad 内一次性 awk（切三元组 + `paste` 逐行 join，未写入仓库） | 两表各 200 行、等级列均为 1..200 顺序无缺；比对结果见裁定一 |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 51+376+60+771+244+4+196 = **1702 通过 / 2 跳过 / 0 失败**，退出码 0，**一次跑成** |
| `grep -rn "Skip.If(ContentArchive.Available"` | 恰两处：`PvfQuestDefinitionSourceTests.cs:372`、`PvfTownDefinitionSourceTests.cs:227` |

未遇已知假红形状（进程/socket 超时、SQLite 30 秒），无需隔离复跑。两条跳过确是
`Skip.If(Available)` 形状——**因为档案在才跳过**，不是因为档案缺席而静默失效；
Infrastructure 项目 196 = 基线 184 + 新增 12，新判据全部真跑。验收条款 5 **成立**。

### 裁定一：QR-2 的阻塞项——范围断言经我独立逐级复核，**完全属实**

`PvfClearRewardRateSource.cs:327-333` 现在的断言是：`below` 列处处相同、`above` 列
恰在 80 到 89 这十级不同（本表 3、怪物表 4）、其余每一级都相同。我不采信任何转述，
自己从两份 dump 切出 200 个三元组逐行 join：

```
below 列差异行数：0          （200 行逐行相同，无一处例外）
above 列差异行数：10         （L80 L81 L82 L83 L84 L85 L86 L87 L88 L89，全部 cr=3 / mo=4）
本表 above 取值分布：200 行全为 3
怪物表 above 取值分布：190 行为 3，10 行为 4
```

**一个不多，一个不少。** 80 是第一个不同的等级，89 是最后一个，中间十级连续，
90 起到 200 重新全同，1 到 79 也全同。QR-2 抓出的两处过宽/过窄概括
（"agree up to level 85"、"every high-level flip"）都已消失，改后的句子没有再多说一个字。

顺带复核了同一段 remarks 的其余可算部分，全部属实：
- `:336-339` 钳制段：我逐行算 200 行，`min(level - below) = 1`（取在 L1..L4），无一行为负，
  首行确为 `1 0 3`（下探 0 而非 3）——"防御性、这份档案不触发"属实；
- `ClearRewardContent.cs:151-152` 的单点陈述（86 级本表比怪物表少够到一档）属实：
  本表 `86 7 3`→(79,89)、怪物表 `86 7 4`→(79,90)。

### 裁定二：Amendment 1 的范围表述已订正到位

`Contract Amendment 1` 第 266-269 行现为"逐行比对两张表全部 200 级（下方范围于 QR-2 后订正，
原文误作「86 级以上」）：下探列 200 行完全相同；上探列只在 80–89 这十级不同（本表 3、怪物表 4），
其余 1–79 与 90–200 全同"。与我实测**逐字相符**，原来只列 1/50/86/87 四个采样等级的小表已删。

两点非阻塞的记账观察：
- QR-2 要求的是"以追加订正的方式记下，勿改写既有段落"，round 3 是**就地改写并加括注**。
  形式与要求不同，但原措辞（「86 级以上」）在括注里被明确保留、可追溯，实质未损失，不阻塞。
- 就地改写留下一句孤立的旧引导语（第 264 行"在同一份 A21 归档上逐级比对两张表的
  `[item drop ref table]`："紧接着第 266 行同义的新引导语），纯排版重复，不影响任何断言。

### 裁定三：round 3 新写的其余说法，有无出处

- **"which is where A21's endgame sits"（`:332`）**：仓内有出处——`CharacterEntryRuleServices.cs:113`
  的 `private const int LastLevel = 86`，`AGENTS.md` 首行"DNF 日服 Act.86 客户端的服务端仿真"，
  86 确实落在 80–89 内；且 QR-2 的整改建议本就要求"点明它落在 80-89、覆盖 86 级上限"。
  唯一的宽松处是窗口上半（87–89）在本服不可达，实际受影响的是 80–86 这七级。
  这是措辞宽松，不是无出处断言，**不构成阻塞**。
- **自检"由 `PvfProbe dump` 导出两表全部 200 行、逐行 join 比对得出，可复跑"**：我按同一手段
  独立复跑，得到同一结论，声明属实。
- **round 3 记述的"80–85 也不同、90–200 并不不同"**：与我的比对逐级相符。
- **"行为未变，只动注释"**：全量判据 1702/0/2 与前两轮逐程序集数字一致，新增 12 条仍全绿，
  未见任何期望值被改动的迹象。

### 裁定四：QR-1 的发现 1、2、3a 在 round 3 之后仍然成立，未被回退

| QR-1 发现 | 现状 | 依据 |
|---|---|---|
| 1（契约侧写"三段不读"） | **仍成立** | `ClearRewardContent.cs:93-96` 为"Four sections … three because their purpose is not ascertained, and one because it is inapplicable"，与 `UnreadSections`（`PvfClearRewardRateSource.cs:106-138`）的四个常量一致 |
| 2（钳制注释编造归档理由） | **仍成立，且 round 3 把 QR-2 指出的连带缺陷也修了** | `:336-339` 的三项数字我逐行验过 |
| 3a（round 1 引证漏 `.rks-agent/`） | **仍成立** | round 2 的追加订正段仍在，round 1 原文未被改写 |

### 要求整改（changes_required）：一条

**`Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs:18-21`** ——
类注释为"另起一个读取器"这个核心决定作辩护时，末句写：

> A reader that accepted both shapes would have to guess which one it was holding, and guessing
> wrong produces a table of plausible numbers rather than an error -- **the monster file's seventy
> integers divide by four perfectly well.**

**这句的算术不成立。** 我从两份 dump 直接数：

```
怪物表 [drop prob]      = 70 个整数、0 个字符串 token；其 [drop prob count] = 10
                          → 70 / 10 = 7 = 2 个等级界 + 5 个率
本表   [drop prob]      = 72 个整数 + 6 个名字 token；其 [drop prob count] = 4
                          → 72 / 4 = 18 … 每档案 12 个数 / 4 段 = 3 = 2 个等级界 + 1 个率
70 除以 4 = 17.5（余 2）
```

数字 **70 来自怪物表**、除数 **4 来自本表自己的 `[drop prob count]`**，两者被拼进同一句，
于是这句话在任何一种读法下都不成立：70 不被 4 整除，"perfectly well"没有依据。

真正支撑这句话的事实就在旁边，而且更有力：**70 能被怪物表自己声明的 10 整除**（得每段 7），
所以宽容读法在"整除"这一关确实过得去——真正卡住它的是另外两道关，
"段宽减两个等级界须恰剩 1 个率"（7-2=5，`CloseProfile` `:256-262`）与"没有名字先来数字一律拒读"
（`CloseProfile` `:241-245`）。改法二选一，均为改一处措辞、不动行为、不动任何判据期望值：

- 把除数改成归档实说的 **10**（"the monster file's seventy integers divide by its own declared ten
  perfectly well"），或
- 把主语换成本表（"this file's seventy-two divide by four perfectly well"，72/4=18 成立）。

**为什么按阻塞记**：本 Task 的立身之本是"注释里每一句关于归档的话都能被复算"，
QR-1（发现 2）与 QR-2（唯一阻塞项）都因同类的一句而判 changes_required，
且都只需改一处措辞。同一标准下这一句不能例外——它还落在整份 diff 里最要紧的那段辩护上，
将来若有人真去写一个兼容两种形状的读取器，这句话会让他以为除法这一关拦不住怪物表，
而实际拦得住的正是它没提到的那两道关。

**同时要说清楚**：这不是 round 3 引入的，它从 round 1 起就在，QR-1、QR-2 均未查到。
round 3 对 QR-2 那一条的整改本身是干净的、经我逐级复核成立的，不因本条打折。

### 未构成阻塞、我已复核成立的部分

- **判据结果**：1702 通过 / 0 失败 / 2 跳过，退出码 0，一次跑成，逐程序集
  51 / 376 / 60 / 771 / 244 / 4 / 196(+2 跳过)；验收条款 5 成立。
- **改动范围**：四处产品文件，无越界；`InfrastructureModule.cs` 只 +3 行。
- **其余归档断言抽查全部属实**：`[item drop rarity control]` 的 `-5` 确为整份档案里**唯一的负数**
  （`:109-110`）；`ReadLadder` 注释（`:276-277`）称"同族四表各声明四组七值"——怪物表
  `[basis of rarity dicision]` 实为 28 个数 = 4 组 × 7，本表实为 7 个数 = 1 组，属实；
  `InfrastructureModule.cs` 注释称本表是"那一族的第五个文件"——归档里该族确为五个
  （`itemdropinfo_monseter` / `_monseter_extra` / `_monster_hell` / `_object` + `_clearreward`），属实。
- **品级带判据仍钉自身表**：`The_grade_bands_are_this_own_files_rows` 期望 86→(79,89)，
  换成借怪物表（(79,90)）会当场变红；`A_table_whose_bands_name_no_profile_is_refused`
  仍是真断言（怪物表 `[drop prob]` 无字符串 token，我数过）。
- **留给最终评审的旧观察**（QR-1、QR-2 已各记一次，本轮不重复要求）：
  `PvfClearRewardRateSourceTests.cs:366-371` 的注释比其下的断言说得多。

### 备注

本轮只读、只核、只追加：未修改任何产品代码、判据、既往轮次或任务控制状态。

## Execution Result — round 4（控制器就地整改 QR-3）

QR-3 确认 round 3 的范围断言一个不多一个不少（下探列 200 行零差异，上探列恰 80–89 十行），
Amendment 1 已订正到同一范围，钳制段属实，QR-1 的三条修法未回退。

唯一阻塞项是**同一类缺陷的第三处**，且它 round 1 起就在，QR-1 与 QR-2 都没查到。

### 发现成立

`PvfClearRewardRateSource.cs` 开头的类型注释原有一句：

> guessing wrong produces a table of plausible numbers rather than an error --
> **the monster file's seventy integers divide by four perfectly well.**

控制器实测两表：

| 文件 | `[drop prob]` 整数个数 | `[drop prob count]` | 相除 |
|---|---|---|---|
| `itemdropinfo_monseter.etc` | 70 | 10 | 7，整除（低、高 + 五个率） |
| `itemdropinfo_clearreward.etc` | 72 | 4 | 18，整除（六档案 × 三个数） |

**70 ÷ 4 = 17 余 2，除不尽。** 那句话取了怪物表的 70 配本表的 4，两个数各来自一张表，
拼在一起不成立。

### 改了什么，以及为什么换了方向

原句想说的道理是对的——猜错形状会产出**看似合理的数**而不是报错，这正是不与
`PvfDropRateTableSource` 合并的理由。错的只是例子。

而真正的例子在**反方向**：拿怪物表的形状读本表，72 个整数除以本表声明的 4 段 = 每段 18 个数，
减去两个等级界剩 16 个，被当成十六个物品槽的率，**整除、不报错、返回一张完整的废表**。
改后的注释用这个例子，它同时更贴切（说的正是本文件被误读的后果）且逐值可核。

### 这是同一类缺陷的第三处，值得留下记录

QR-1 抓到一处（钳制理由），round 2 修它时又写进一处（品级带范围），QR-2 抓到，
round 3 修好，QR-3 又从 round 1 的原文里翻出第三处。三处**形状完全相同**：
一句关于归档的断言，读起来顺理成章，没有人核过数。

本 Task 的判据钉得住读出来的值，钉不住注释里的话。三处全部由独立评审者拿着档案逐行核出来，
无一例外。这一点应当带进后续任务：**注释里凡出现具体数字或"两张表如何如何"的比较，
都要像判据一样有出处。**

## Executor Self-check — round 4

- QR-3 唯一阻塞项：已改，例子换成本文件被误读的真实后果。
- 新写的断言逐项可核：本表 `[drop prob]` 72 个整数、`[drop prob count]` 为 4、
  72/4 = 18、18 − 2 = 16。四项均由 `PvfProbe dump` 实测得出，可复跑。
- QR-3 认定为非阻塞的措辞宽松处（「A21 毕业段」涵盖 87–89 不可达等级）未改动，
  其仓内出处 `LastLevel = 86` 成立。
- 行为未变：round 2、3、4 合计只动注释与流程记录，无一行可执行代码改动。
- 判据：见下轮重跑结果。

## Execution Result — round 5（控制器主动穷举，非评审要求）

QR-3 之后控制器没有直接派第四轮评审，而是先做了一件本该更早做的事：**把两个新增源文件里
每一处带数字或跨表比较的注释断言全部列出来逐条核**。

理由：QR-1、QR-2、QR-3 各抓到一处，三处形状完全相同——关于归档的断言没人核数。
三轮各抓一处正是抽查的必然结果，继续抽查只会继续一轮抓一处。

穷举核出**又六处**，全部属于同一类，全部已改：

| # | 原文断言 | 实测 |
|---|---|---|
| 1 | 「四张同族表」用 `[drop prob]` 且一段一率一物品槽 | **三张**（monseter、monseter_extra、object）。深渊表用的是 `[dungeon difficulty drop prob]`，另一个段名 |
| 2 | 怪物表一段「五个并列率，一率一物品槽」 | 五个率是**一个金币率 + 四个物品率**。与本文件另一处「四个物品率」的说法自相矛盾 |
| 3 | 「另有**五**个只有本表声明的段落」 | **九**个 |
| 4 | 稀有度：「本表一组，四张同族各四组」 | 三张各四组；**深渊表是前缀 2 加两组**；本表一组且无前缀 |
| 5 | 「这正是那四张同族表的样子」 | 同 #1，应为声明该段的三张 |
| 6 | 「一组七个，而四张同族文件各声明四组七个」 | 同 #4 |

核过而**成立**、未改的：成本表「一到二百级」（实测 1–200 共 200 对）、
`event2` 四个率与 `event` 相同、六个兜底行同一物品 id、
难度加成首项为零且第五项小于第四项、三个档案名是两个词。

### 这轮记录本身要说的话

判据钉得住读出来的值，钉不住注释里的话。本 Task 至此共发现同类缺陷 **九处**：
QR-1 一处、round 2 自己新写一处（QR-2 抓）、round 1 遗留一处（QR-3 抓）、本轮穷举六处。
九处无一被判据发现，九处全部只能靠拿着档案核数。

**这条经验应当带进 T-002 及以后**：注释里凡出现具体数字、「所有/每个/从不/只有」这类全称词、
或「本表与某表如何如何」的比较，都要像判据一样有出处；写的时候就核，不要留给评审。

## Executor Self-check — round 5

- 六处已改，每一处的替换文本均由 `PvfProbe dump` 实测得出：
  同族五个文件的 `drop prob` 段名与稀有度整数个数、翻牌表独有段落的集合差、成本表等级范围。
- 改后不再出现「四张同族表」这一未经核实的全称说法；凡涉及同族的陈述均已按
  「声明该段的三张」与「深渊表另一形状」分开表述。
- #2 顺带消除了本文件内部两处自相矛盾的说法（五个率一率一槽 vs 四个物品率）。
- 行为未变：round 2 至 round 5 合计只动注释与流程记录，无一行可执行代码改动。
- 判据：见下方重跑结果。

## Independent Quick Review — round QR-4

- Reviewer: 独立 quick 评审者（第四轮，未参与执行，亦非 QR-1 / QR-2 / QR-3 的评审者）
- Reviewed contract revision: 1 + Contract Amendment 1（QR-2 之后的订正版）
- Reviewed rounds: round 5 的穷举整改；并回查 round 5 自称核过未改的五条、QR-1/QR-2/QR-3 五条修法的现状
- Verdict: **changes_required**

先把好消息说完：**round 5 声称改对的六处，我逐条重算，六处全对**；它声称核过而成立、
因而未改的五条，我也逐条重算，五条全成立；QR-1 的三条、QR-2 的一条、QR-3 的一条修法在
round 5 之后一处未回退。判据 1702 / 0 / 2，一次跑成。

**但穷举有漏。** 我按本轮要求把两个新增源文件**连同判据文件**里每一处带数字、带全称词、
或做跨文件比较的句子全部列出重算，漏掉两条**同类**的（其中一条正落在 round 5 自己改过的
那一句里），另有一条在 owned diff 的第三个产品文件里、且是 QR-3 误判为属实的。详见下。

### 复核的 diff 身份

工作树未提交，HEAD 仍在基线 `c0cb6c99`。本轮 `git status --short` 实见五条，产品代码四处：

```
 M Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs
?? .rks-agent/tasks/flipping-a-clear-reward-card/
?? Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs
?? Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs
?? Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs
```

与 Permitted files（BD-001/002/003/021）逐条对上，无第五处产品改动；第五条是本流程自己的
`.rks-agent/` 制品目录，不计越界。`git diff InfrastructureModule.cs` 实见仅 +3 行
（1 行 `TryAddSingleton<IClearRewardRateSource, PvfClearRewardRateSource>()` + 2 行注释）。

### 我自己跑的证据

| 命令 | 我实测的输出 |
|---|---|
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 376+51+60+244+771+196+4 = **1702 通过 / 2 跳过 / 0 失败**，退出码 0，**一次跑成**，未遇已知假红 |
| `PvfProbe files itemdropinfo` | **7 行**：`(r)itemdropinfo_control` / `clearreward` / `common` / `monseter` / `monseter_extra` / `monster_hell` / `object` |
| `PvfProbe dump etc/itemdropinfo_{monseter,monseter_extra,monster_hell,object,common,clearreward}.etc 4000` 与 `dump "etc/(r)itemdropinfo_control.etc"` | 七份全文，未截断 |
| `PvfProbe tokens etc/itemdropinfo_clearreward.etc 2000` | 逐 token 的 kind，用于核 Int32 / Single / QuotedString 的分布 |
| `PvfProbe lst stackable/stackable.lst 10002120` | `event/chn_event_20130723_GhostSeria/chn_stk_signs_of_seria.stk`，`[name]` 为「幽灵赛丽亚的痕迹」 |
| scratchpad 内一次性 python（切段、切三元组、逐行 join、集合差，未写入仓库） | 下方全部数字 |
| `grep -rn "Skip.If(ContentArchive.Available" Server/tests/` | 恰两处：`PvfQuestDefinitionSourceTests.cs:372`、`PvfTownDefinitionSourceTests.cs:227` |

两条跳过确是 `Skip.If(Available)` 形状——**因为档案在才跳过**；Infrastructure 项目
196 = 基线 184 + 新增 12，新判据全部真跑。验收条款 5 **成立**。

### 裁定一：round 5 声称改对的六处，逐条重算，**六处全对**

我的实测底数（七个 `itemdropinfo` 文件的开启标签集合、各段 token 数与 kind）：

```
[drop prob] 声明者：monseter / monseter_extra / object          （monster_hell 无此段）
monster_hell 声明的是 [dungeon difficulty drop prob]，14 个 Int32，其 [drop prob count] = 2
[drop prob] 宽度：monseter 70/10=7  monseter_extra 49/7=7  object 28/4=7   → 均为 2 界 + 5 率
clearreward [drop prob]：6 个 QuotedString + 72 个 Int32，[drop prob count]=4 → 每段 3 = 2 界 + 1 率
[basis of rarity dicision]：monseter 28  monseter_extra 28  object 28（=4 组 × 7）
                            monster_hell 15（首位 2，其后 14 = 2 组 × 7）
                            clearreward 7（1 组，无前缀）
clearreward 16 个开启标签，其中 9 个是另外六个 itemdropinfo 文件全都不声明的
```

| round 5 # | 新文本要点 | 我的裁定 |
|---|---|---|
| 1 | 只有三张声明 `[drop prob]`，深渊表是 `[dungeon difficulty drop prob]` | **属实** |
| 2 | 那三张一段是两个等级界 + 五个率（一金币率 + 四物品率） | **属实**。宽度 7 三张全同；「一金币 + 四物品」不是新造的说法，是本仓既有 `DropRateSlot`（`DropRateContent.cs:17-24`）的读法，且该处已写明其出处 |
| 3 | 翻牌表另有**九**个段落是同族其余文件都不声明的 | **属实，恰为 9**：`drop kind prob` / `drop item type prob` / `dungeon difficulty gold drop bonusrate` / `reward item rate per map max count` / `gold card cost table` / `gold card create rate` / `gold card blank item` / `pcroom card blank item` / `chronicle set reward rate`。我拿它去比的是**另外六个** `itemdropinfo` 文件（含 `common` 与 `(r)…_control`），结论不变 |
| 4 | 稀有度：三张各四组七个；深渊表前缀 2 + 两组七个；本表一组七个且无前缀 | **属实**（28 / 28 / 28、15 = 1+2×7、7） |
| 5 | 「没有名字先来数字」正是**声明该段的三张**的形状 | **属实**：三张的 `[drop prob]` 字符串 token 数皆为 0 |
| 6 | 一组七个 vs 三张各四组七个 + 深渊表另一形状 | **属实**，同 #4 |

### 裁定二：round 5 声称核过而未改的五条，逐条重算，**五条全成立**

- `[gold card cost table]`：400 个 Int32 = 200 对，等级列 `1..200` 连续无缺，
  价格逐级满足 `100 + (level-1)*120`（我全 200 级验过）。
- `event2` 与 `event`：两者四段皆 `9000 9000 9000 9000`，**相同**。
  （六档案全表：default 与 pcroom default 皆 10000；event / event2 / pcroom bonus 皆 9000；
  pcroom event 为 9000/8000/8000/8000；等级界六档案同为 1-15 / 16-30 / 31-40 / 41-200。）
- `[gold card blank item]`：18 个 Int32 = 六行，物品 id 六行皆 **10002120**，
  权重 3000/3000/2000/1000/500/500，数量 1/2/3/4/5/10。
- `[dungeon difficulty drop bonusrate]`：`0 600 1000 1400 1200` —— 首项为零，第五项 < 第四项。
- 六个档案名中两个词的恰好三个：`pcroom default` / `pcroom bonus` / `pcroom event`。

顺带把 round 5 未列、我自己扫到的其余可算断言也都算了，属实的有（不重复要求整改）：
`[item drop rarity control]` 的 `-5` 确为**整份档案里唯一的负数**（`PvfClearRewardRateSource.cs:112`）；
`[chronicle set reward rate]` = `0 50 1 75 2 100`、`[gold card create rate]` = 单个 Single `1`、
`[dungeon difficulty gold drop bonusrate]` = 5 个 Single 且与 `[dungeon difficulty drop bonusrate]`
同为五档；「本文件混用两种数形，队伍倍率是 Single、难度加成是 Int32」（`:441-444`，实测
party 4×Single、difficulty 5×Int32）；「72 整数 / 4 段 → 四段十八、每段减两界剩十六」
（`:21-24`，且 `PvfDropRateTableSource.cs:109-132` 确实丢字符串并按声明段数整除，
所以那个「不报错、返回一张完整废表」的推演成立）；品级带三列同族四表皆 600 数 = 200×3
（`:330-331`）；两张按对读的表在 A21 均无重复键（`:375-376`）；
`ClearRewardContent.cs:82` 与判据 `:231` 的「一个 2013 年的活动道具」——
`10002120` 经 `stackable.lst` 解析为 `event/chn_event_20130723_GhostSeria/chn_stk_signs_of_seria.stk`，
说明文写着「该道具将于 2013 年 9 月 3 日 …… 删除」，**属实且可指回归档**；
判据 `:127-128` 的「1.2 读回来是 1.20000004」——该 token 的 Single 位模式 `1067030938`
即 `1.20000004768…`，**属实**。

### 裁定三：既往五条修法在 round 5 之后仍成立，未被回退

| 出处 | 现状 | 依据 |
|---|---|---|
| QR-1 发现 1（契约侧「三段不读」） | **仍成立** | `ClearRewardContent.cs:97-100` 为「Four sections … three because their purpose is not ascertained, and one because it is inapplicable」，与 `UnreadSections`（`PvfClearRewardRateSource.cs:109-141`）四个常量一致；12 读 + 4 不读 = 16，正是我数到的 16 个开启标签 |
| QR-1 发现 2（钳制注释编造归档理由） | **仍成立** | `:342-345` 三项我逐行重算：200 行 `min(level - below) = 1`，首行确为 `1 0 3`，无一行为负 |
| QR-1 发现 3a（引证漏 `.rks-agent/`） | **仍成立** | round 2 的追加订正段仍在，round 1 原文未被改写 |
| QR-2 唯一阻塞项（品级带范围） | **仍成立** | `:333-339` 我重算：`below` 列 200 行**零差异**；`above` 列差异**恰 10 行 = L80..L89**（本表 3 / 怪物表 4），本表 above 全 200 行为 3，怪物表 190 行为 3、10 行为 4；86 级本表 (79,89)、怪物表 (79,90)；86–200 下界恒 79 |
| QR-3 唯一阻塞项（`70 ÷ 4`） | **仍成立** | 现文本改用本表自身的 72 / 4 / 18 / 16，四个数我全部实测对上 |

### 要求整改（changes_required）：穷举漏掉的三条

**1（阻塞）—— `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs:92-95`：
「shares **most** of its section tags」与同一句里的「nine」自相矛盾。**

现文本：

> It shares **most** of its section tags with the four sibling rate tables and differs from them in
> one place -- its `[drop prob]` is named profiles rather than bands of parallel rates -- plus
> **nine** sections no other file of the family declares.

我数出来的实情：

```
clearreward 开启标签总数        16
与四张同族表共有的              7   （drop prob count / drop prob / dungeon difficulty drop bonusrate /
                                     basis of rarity dicision / party member drop bonusrate /
                                     item drop ref table / item drop rarity control）
同族其余文件全不声明的           9
7 + 9 = 16                       共有的是 7/16，是少数，不是 most
（连 [/…] 关闭标签一起数则 23 个标签里共有 8 个，比例更低）
```

**这一句正是 round 5 第 3 项亲手改过的那一句**：把「五个独有」订正成「九个独有」，
而这个订正本身就把「shares most」从（原本按 5 算时的 11/16）变成了假。
穷举核了后半句的数，没有回头核前半句的量词——这与本 Task 前三轮抓到的三处、
以及 round 2 在修 QR-1 时新写进一处，是同一个形状。

（附带：同句「differs from them in **one place**」也偏窄——本表还**不**声明
`[monster type drop bonusrate]`，而 monseter / monseter_extra / object 三张都声明；
另外深渊表根本不声明 `[drop prob]`，谈不上「形状不同」。同一句改一次即可一并说准。）

**2（阻塞）—— `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs:182-185`：
round 5 修掉的那句全称说法，在判据文件里原样活着。**

现文本：

> This file declares one group of seven **where the monster family declares four**, and the group
> **closes the same way** -- its last number is one more than the one before it …

这与 round 5 第 4 / 第 6 项判为错误、并已在
`ClearRewardContent.cs:138-141` 与 `PvfClearRewardRateSource.cs:280-283` 改掉的说法**逐字同义**：
「同族各四组」。实测已列于裁定一——四张同族表里只有三张是四组七个，
**深渊表 `monster_hell` 是首位 2 加两组七个**。

后半句「closes the same way」同样过宽：深渊表那两组的收尾是
`… 1000001 1000001` 与 `… 1000002 1000001`，**都不满足「末位 = 前一位 + 1」**；
满足的是三张四组表与本表。

round 5 的穷举范围写的是「两个新增源文件」，判据文件不在其内。但判据文件是 owned diff 的
四分之一，且这条说法在 Task 里已被判定为错误说法，**同一份 diff 里不该一处改、一处留**。
改法与另两处一致，只动注释，不动任何断言与期望值。

**3（阻塞，程度较轻但同类）—— `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs:71`：
「the **fifth** file of that family」，而归档里该族有七个文件。**

`PvfProbe files itemdropinfo` 实见七行：

```
etc/(r)itemdropinfo_control.etc
etc/itemdropinfo_clearreward.etc
etc/itemdropinfo_common.etc
etc/itemdropinfo_monseter.etc
etc/itemdropinfo_monseter_extra.etc
etc/itemdropinfo_monster_hell.etc
etc/itemdropinfo_object.etc
```

`itemdropinfo_common.etc` 不是外人——本仓自己在 `DropRateContent.cs:219-220` 就写着
`[gold drop ref table]` 住在这个文件里，`PvfDropRateTableSource.ReadGold` 正在读它。
所以「that family」的第五个只在把 family 悄悄收窄成「四张费率表」时才成立，
而注释没有把这个收窄说出来。

**这一条 QR-3 曾明文核过并判为属实**（「归档里该族确为五个（`itemdropinfo_monseter` /
`_monseter_extra` / `_monster_hell` / `_object` + `_clearreward`），属实」）——
它漏了 `_common` 与 `(r)_control`。记在这里，是因为它恰好说明这一类缺陷连评审侧也会误判，
不只执行侧。改法：把 family 明确限定成「读费率的那几张」，或直接不用序数。

### 未构成阻塞、留给最终评审的观察（本轮不要求整改）

- `PvfClearRewardRateSource.cs:118`：`[gold card create rate]`「stored as a float **where every
  neighbouring rate is an integer**」。按「文件里紧邻的两段」读是**真的**
  （前 `[gold card cost table]` 400×Int32、后 `[gold card blank item]` 18×Int32）；
  但同一文件另有两段也是 Single（`[party member drop bonusrate]` 4 个、
  `[dungeon difficulty gold drop bonusrate]` 5 个），按「文件里的其他率」读就不真。
  是措辞歧义而非算错，故不阻塞——但它带了个「every」。
- `PvfClearRewardRateSourceTests.cs:233`：「**any other column order** would scatter one repeated id
  across three different positions」。实情是：18 个数无论按哪个行偏移切三元组，
  那个重复 id 都仍然落在**同一列**（偏移 1 时落在第 0 列、偏移 2 时落在第 2 列），
  从不「散到三个位置」。真正支撑三元组读法的是「只有中间那列是常数」，不是这句。
  论证过头，结论仍对。
- `PvfClearRewardRateSource.cs:407-408`：「Only the closing `[/…]` markers repeat in these files」。
  在 clearreward 里其实**没有任何标签重复**（16 个开启标签互不相同，7 个关闭标签也互不相同），
  这句描述的是一件不发生的事。属防御性说明，无害。
- `PvfClearRewardRateSourceTests.cs:365-371`：注释说「eleven numeric members, none of which holds …
  `[chronicle set reward rate]` 的三对」，其下只对 3 个成员断言 `DoesNotContain(-5)`，
  对 `[chronicle set reward rate]` 的泄漏一句未查。「eleven」我数过属实
  （除 `ArchivePath` 外恰 11 个成员）。此条 QR-1 / QR-2 / QR-3 已各记一次，本轮不重复要求。
- 判据结果、改动范围、分层、`RarityLadder` 复用、判据钉的是归档实值而非实现常量，
  我均已独立复核成立，与前三轮结论一致，不再展开。

### 我扫到的断言全集（供最终评审复用，`✓` = 我算过且成立）

`ClearRewardContent.cs`：`:7-11`✓ `:28-34`✓ `:37-38`✓ `:53`✓ `:63-64`✓ `:76-79`✓ `:82`✓
**`:92-95`✗（发现 1）** `:97-100`✓ `:111-112`✓ `:115-118`✓ `:121-123`✓ `:126-128`✓ `:131-132`✓
`:138-143`✓ `:156-158`✓ `:187-190`✓ `:199`✓ `:212-213`✓ `:227-228`✓

`PvfClearRewardRateSource.cs`：`:16-24`✓ `:27-30`✓ `:43`（同发现 1 的宽泛处）`:46`✓ `:49`✓
`:52`✓ `:102-106`✓ `:112-113`✓ `:118`（观察）`:124`✓ `:130-131`✓ `:180-183`✓ `:186-189`✓
`:280-286`✓ `:330-331`✓ `:333-339`✓ `:342-345`✓ `:375-376`✓ `:404-405`✓ `:407-408`（观察）`:441-444`✓

`PvfClearRewardRateSourceTests.cs`：`:11-19`✓ `:24`✓ `:34-38`✓ `:59-76`✓ `:105-108`✓ `:118-128`✓
`:137-144`✓ `:151-153`✓ **`:182-185`✗（发现 2）** `:197-204`✓ `:211-213`✓ `:231-233`（id 与
2013 皆✓，论证句见观察）`:261-265`✓ `:318-319`✓ `:339-343`✓ `:365-371`（观察）

`InfrastructureModule.cs`：**`:71`✗（发现 3）**

### 备注

本轮只读、只核、只追加：未修改任何产品代码、判据、既往轮次或任务控制状态。
三条整改都只改注释文字，不改行为、不动任何判据期望值。

## Execution Result — round 6（控制器整改 QR-4）

QR-4 确认 round 5 的六处全部改对，且 round 5 声称核过未改的五条也全部成立。
但它指出**穷举本身漏了三条**。三条全部核实成立，全部已改。

**两条是穷举方法本身的漏洞，不是运气不好：**

### 漏网 1 — 我改了半句，没回头核另外半句

`ClearRewardContent.cs` 那句原文是「shares **most** of its section tags with the four sibling
rate tables … plus **five** sections that only it declares」。round 5 把「five」改成实测的
「nine」，**却没回头看前半句的「most」**。改完之后 7 个共有 + 9 个独有 = 16 个标签，
共有的是少数——**这一句在我修它的过程中由「有一处错」变成了「有两处错」**。

已改为：十六个标签里七个与同族共有、九个是它独有，所以它声明的东西多数是自己的；
共有的七个里有一个它声明得不一样。

### 漏网 2 — 我把穷举范围划错了

round 5 的范围写的是「两个新增源文件」，**把新增的判据文件排除在外**。
判据文件里 `PvfClearRewardRateSourceTests.cs` 有一句「where the monster family declares four」，
与我已在两个源文件里判为错误并改掉的说法**逐字同义**。

同句的「the group closes the same way」也过宽：深渊表两组的收尾分别是
`… 1000001 1000001` 与 `… 1000002 1000001`，**都不满足「末位 = 前一位 + 1」**，
所以那不是家族不变式。已改为只就本文件与怪物表陈述，并写明深渊表两组都不满足。

**范围划错的教训比错本身要紧**：判据文件里的注释同样是「后来的人据以判断读得对不对」的东西，
穷举的范围应当是**这次新增或改动的全部文件**，不是「源文件」。

### 漏网 3 — 评审侧的误判，由下一轮评审纠正

`InfrastructureModule.cs` 的注释称翻牌表是「该家族的**第五**个文件」。
`PvfProbe files itemdropinfo` 实为**七个**：
`(r)itemdropinfo_control`、`clearreward`、`common`、`monseter`、`monseter_extra`、
`monster_hell`、`object`。

**此条 QR-3 曾明文核过并判其属实。** 这是评审侧的一次误判，由 QR-4 纠正。
记下来是因为它说明：核对不只执行侧会错，评审侧同样会——本 Task 至此评审侧已错两次
（QR-1 的 `PvfProbe tokens`、QR-3 的这一条）。

已改为「该家族的另一个文件」，不再声称序数——序数在这里没有任何用处，
它唯一的作用就是提供一个会错的机会。

## Executor Self-check — round 6

- QR-4 的三条阻塞项全部核实并已改；替换文本均由实测得出
  （标签集合差 16/7/9、家族文件清单七项、深渊表两组的收尾数值）。
- 穷举范围已在记录中更正为「本次新增或改动的全部文件」，判据文件包含在内。
- QR-4 另记的四条非阻塞观察本轮未处理，留作后续任务的输入。
- 行为未变：round 2 至 round 6 合计只动注释与流程记录，无一行可执行代码改动，
  无一处判据期望值改动。
- 判据：见下方重跑结果。

## Contract Amendment 2 — 跨文件断言必须由判据钉住（用户决定）

**触发**：本 Task 至此发现同类缺陷十二处，全部是注释里关于归档的断言，
全部未被任何判据发现，其中两处由评审侧判错、两处在整改过程中新造。
四轮评审各抓一批，说明抽查抓不干净，而穷举也漏了三条。

**根因**：这些注释陈述的是**本代码并不读取的其他文件**的事实。没有任何机制能钉住它们，
只能靠人反复核，而核的人也会错。

**用户决定（2026-09-01）**：「把跨文件断言变成判据」。

**契约相应修订**，Acceptance conditions 增补一条：

> 注释里凡对本读取器所读文件**之外**的文件作出的事实断言，必须有一条判据钉住它；
> 钉不住的断言应当删去而不是留在注释里。

本任务需补的判据至少覆盖以下现存断言（均已核实为真，现在要让它们不可能悄悄变假）：

1. 品级带：与怪物表逐行比较，`below` 列 200 行全同，`above` 列差异恰为 L80–L89。
2. `[drop prob]` 的声明者：同族里只有 monseter、monseter_extra、object 三张声明它；
   monster_hell 声明的是 `[dungeon difficulty drop prob]`。
3. 段落集合：翻牌表十六个标签中七个与同族共有、九个是同族其余文件全不声明的。
4. 稀有度组：那三张各声明四组七个；monster_hell 是前缀 2 加两组七个；本表一组七个且无前缀。
5. 同族一段的宽度：两个等级界加五个率（一个金币率、四个物品率）。
6. 「末位 = 前一位 + 1」不是家族不变式：monster_hell 两组均不满足。

**范围**：只增判据，不改产品代码、不改既有判据的期望值。
Spec 的验收边界（RQ-008/009/010/013）未变。

**这条约束同样适用于 TASK-02 及以后**，届时由各自的密封契约承接。

## Execution Result — round 7

只改了一个文件：`Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs`。
产品代码一行未动（`git diff --stat` 只有 round 1 的 `InfrastructureModule.cs` 3 行，未再变），
既有 12 条判据的断言与期望值一个未改，新增 6 条判据 + 2 个私有读档助手 + 一组档案常量。

### 我自己核出来的实际值（未照抄派工简报的数字）

用 scratchpad 内一次性 console（引用 `Dfo.Pvf`，未写入仓库）逐 token 读七个归档文件，
对每个文件按段收 token 后统计。六条全部核实为真，且与简报所述一致，无一处不符。

**族内七个文件的开启标签数**（`[/…]` 关闭标签不计）：
`clearreward` 16（identifier 共 23，其中 7 个是关闭标签）、`monseter` 12、`monseter_extra` 8、
`monster_hell` 5、`object` 7、`common` 1、`(r)…_control` 5。

1. **品级带**：两表各 600 个整数、200 行，等级集合都是 1..200。
   `below` 列 **200 行逐级全同，无一处差异**；`above` 列差异等级集合 = **{80,81,…,89}**，
   恰好十级，本表 3、怪物表 4。本表 `above` 全 200 行取值集合 = `{3}`，
   怪物表 = `{3,4}`。两表 `min(level - below)` 均为 1（首行都是 `1 0 3`）。
2. **`[drop prob]` 的声明者**：六个同族文件里声明它的是
   `monseter`、`monseter_extra`、`object` 三个；`monster_hell` **不**声明它，
   声明的是 `[dungeon difficulty drop prob]`；`common` 与 `control` 两者都不声明。
   翻牌表声明 `[drop prob]`、不声明 `[dungeon difficulty drop prob]`。
   另实测：`PvfDropRateTableSource.Load("etc/itemdropinfo_monster_hell.etc")` 抛
   `InvalidDataException`（它先取到 `[drop prob count]`=2，再发现 `[drop prob]` 一个数都没有）。
3. **段落集合**：翻牌表 16 个开启标签，其中
   **共有 7 个**：`[drop prob count]`、`[drop prob]`、`[dungeon difficulty drop bonusrate]`、
   `[basis of rarity dicision]`、`[party member drop bonusrate]`、`[item drop ref table]`、
   `[item drop rarity control]`；
   **独有 9 个**（同族其余六个文件全不声明）：`[drop kind prob]`、`[drop item type prob]`、
   `[dungeon difficulty gold drop bonusrate]`、`[reward item rate per map max count]`、
   `[gold card cost table]`、`[gold card create rate]`、`[gold card blank item]`、
   `[pcroom card blank item]`、`[chronicle set reward rate]`。
   （`common` 与 `control` 对这 16 个标签的贡献为 0，所以「与同族共有」按六个文件的并集
   还是按三张同形表来算，结果都是 7。）
4. **稀有度组**：`monseter` / `monseter_extra` / `object` 各 **28** 个整数（4 组 × 7）；
   `monster_hell` **15** 个 = 前缀 `2` + 两组七个，且前缀值 2 恰等于其后组数
   （`(15-1)/7 = 2`）；本表 **7** 个，首值 500000（是阈值不是组数）。
   形状判别的实测差：本表 `7 % 7 = 0`，`monster_hell` `15 % 7 = 1`。
5. **同族一段的宽度**：`monseter` `[drop prob count]`=10、`[drop prob]` 70 个整数 → 宽 7；
   `monseter_extra` 7 / 49 → 7；`object` 4 / 28 → 7。7 − 2 个等级界 = **5 个率**，
   与 `DropRateSlot` 的五个成员（`Gold`=0 加四个物品槽）数目相同。
   本表对照：`[drop prob count]`=4、`[drop prob]` 72 个整数、6 个档案 →
   `72 / (6 × 4) = 3` = 两个等级界加一个率。
6. **「末位 = 前一位 + 1」不是家族不变式**：`monster_hell` 十五值为
   `2 | 0 599000 993000 993000 1000000 1000001 1000001 | 0 800000 1000000 1000000 1000001 1000002 1000001`，
   第一组末位 1000001 而前一位 1000001（差 0），第二组末位 1000001 而前一位 1000002（差 −1），
   **两组都不满足**。本表 `1000003 = 1000002 + 1` 满足；怪物表四组全部满足。

### 新增的六条判据（每条钉的是什么）

| 判据 | 钉住的断言 |
|---|---|
| `The_grade_bands_share_the_monster_tables_below_column_and_differ_above_at_80_to_89` | 断言 1。同时用 `PvfClearRewardRateSource` 与 `PvfDropRateTableSource` 各读一遍，逐级比 200 行：先钉两表等级集合都是 1..200，再钉 `Low` 全同（即 `below` 列全同），再钉 `above` 差异等级集合恰为 `Range(80,10)`、该窗口内本表 3 / 怪物表 4，最后钉本表 `above` 取值集合 `{3}`、怪物表 `{3,4}`（这一条是「窗口之外真的没有差异」的正面证据，而不是抽样） |
| `Three_siblings_declare_drop_prob_and_the_abyss_table_a_difficulty_keyed_one` | 断言 2。直读六个同族文件的开启标签，把「声明 `[drop prob]` 的文件列表」整个比出来（不是逐个 `Contains`），另钉 `monster_hell` 是唯一声明 `[dungeon difficulty drop prob]` 的，并钉同族读取器在它上面抛 `InvalidDataException` |
| `Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else` | 断言 3。钉 16 这个总数，再把 7 个共有与 9 个独有**逐名列出**比对——只钉数目挡不住「一个标签从独有滑到共有」 |
| `A_sibling_band_is_two_bounds_and_five_rates_where_a_band_here_is_two_and_one` | 断言 5。三张同形表各自用它自己的 `[drop prob count]` 除自己的整数数（10/70、7/49、4/28）恢复宽度 7，再经 `PvfDropRateTableSource` 钉每个 band 的 `Rates.Count` 为 5，并钉 `DropRateSlot` 恰有五个成员且 `Gold` 为 0；反面钉本表 72/(6×4)=3 |
| `One_rarity_group_here_four_in_three_siblings_and_a_counted_two_in_the_abyss_table` | 断言 4。三张各 28 且经同族读取器恢复出 4 条七值阶梯；`monster_hell` 15、首值 2 且等于组数、`15 % 7 = 1`；本表 7、`7 % 7 = 0`、首值 500000，并钉读出的 `Rarity.Values` 与档案七值逐值相同（有前缀被误吞会当场红） |
| `The_closing_invariant_holds_here_and_in_the_monster_table_but_in_no_abyss_group` | 断言 6。把 `monster_hell` 十五个整数整体转录，再对两组逐组 `Assert.False(末位 == 前一位 + 1)`；本表与怪物表四组则逐组 `Assert.Equal` 钉它成立 |

新增两个私有助手直读归档（既有内容源拿不到原始段落标签集合，只能经 `PvfArchiveProvider`）：
`OpeningTags`（一个文件的开启标签，按声明序去重，丢掉 `[/…]`）与
`Numbers`（一个文件某一段的全部整数）。同族能拿到的东西仍走既有
`PvfDropRateTableSource`（品级带、band 宽度、稀有度阶梯数），没有绕开它另写一份解析。

### 判据不是空的：一次真做的变异检查

round 1 曾如实记「本任务的红只证明判据先于实现存在，没有证明判据能抓住一个错误的读法」。
这一轮补上了：六条判据写完第一次跑就全绿（期望值先由上面的探针读出、再写进判据），
所以我随后把**每条判据各改坏一处期望值**（`Range(80,10)`→`Range(80,9)`；
声明者列表去掉 `object`；`16`→`15`；`(Monster, 10, 70)`→`71`；`abyss[0]` 期望 `2`→`3`；
abyss 组的 `Assert.False`→`Assert.True`），只跑本类：

```
失败! - 失败:     6，通过:    12，已跳过:     0，总计:    18
```

**六条全红，一条不漏**，随后从备份还原、重跑 18/18 全绿。这证明六条都真的在比档案值，
没有一条是自我循环或永真的。

### 判据的真实输出

**CK-001** — `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test
Server/tests/Dfo.Server.Infrastructure.Tests --nologo`：

```
已通过! - 失败:     0，通过:   202，已跳过:     2，总计:   204，持续时间: 10 s
```

202 = round 1 的 196 + 本轮 6。本类单独跑 18/18 通过、0 跳过。

**CK-S** — `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`，逐程序集：

| 程序集 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 51 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 376 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 202 | 2 | 0 |
| Dfo.Server.Application.Tests | 771 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 244 | 0 | 0 |
| Dfo.Server.Host.Tests | 4 | 0 | 0 |
| **合计** | **1708** | **2** | **0** |

**1708 通过 / 0 失败 / 2 跳过**，1708 = 1702 + 6。跳过仍是那两条「缺档案时应报错」用例，
未暴涨，说明 `PVF_ARCHIVE_PATH` 生效、六条新判据全部真跑。一次跑成，
未出现已知假红形状（进程/socket 超时、SQLite 30 秒）。`TreatWarningsAsErrors` 下能跑到绿即 0 警告。

### 与注释断言不符的发现

**本 Task 新增/改动的三个文件里，没有发现任何一处与实测不符。** 六条断言全部为真，
round 5/6 的整改文本经我独立重测全部成立（16/7/9、七个家族文件、深渊表两组的收尾数值、
`min(level - below)` 为 1、80–89 这十级）。

**但发现一处越界之外的既存问题，不在本 Task 的 Permitted files 内，未改，报给控制器：**

`Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs` 的 `DropRateTable` remarks 写：

> Four of the five files in this family share this shape exactly -- monster, monster-extra,
> breakable object and **clear reward** -- and the abyss table does not.

两处与实测不符：

1. **「five files」**：`itemdropinfo` 家族实有**七个**文件（round 6 已在
   `InfrastructureModule.cs` 一侧纠正过同类说法，但这一处没跟着改）。
2. **「clear reward 与它们 share this shape exactly」**：这正是本 Task 全程在推翻的说法——
   翻牌表的 `[drop prob]` 是具名档案，不是同形。同一个文件下面
   `DropRateTables.ClearReward` 的注释自己写着「its `[drop prob]` is not the same shape」，
   **同一文件内两句互相矛盾**。

该文件是基线里就有的已跟踪文件（`git status` 未列为改动），本轮未触碰。
按 Contract Amendment 2 的口径它属于同一类缺陷（对本读取器不读的文件作断言、无判据钉住），
但改它要么越出本 Task 的 Permitted files，要么改动既有产品代码——两条边界我都不越，
交由控制器决定是就地修订还是转入 TASK-02。

## Executor Self-check — round 7

| # | Contract Amendment 2 的六条 | 是否已被判据钉住 | 依据 |
|---|---|---|---|
| 1 | 品级带：与怪物表逐行比较，`below` 列 200 行全同，`above` 列差异恰为 L80–L89 | **是** | `The_grade_bands_share_the_monster_tables_below_column_and_differ_above_at_80_to_89`。两表分别由两个内容源真读出来再比，不是拿常量比常量。断言链：等级集合都是 `Range(1,200)` → 200 次 `Low` 相等 → `above` 差异集合 `== Range(80,10)` → 窗口内 3 对 4 → 本表 `above` 集合 `{3}`、怪物表 `{3,4}`。若有人让本表借用怪物表品级带，差异集合会变空，第三条断言当场红 |
| 2 | 同族里只有 `monseter`、`monseter_extra`、`object` 声明 `[drop prob]`；`monster_hell` 声明 `[dungeon difficulty drop prob]` | **是** | `Three_siblings_declare_drop_prob_and_the_abyss_table_a_difficulty_keyed_one`。用**整个列表相等**而不是逐个 `Contains`：第四个文件开始声明 `[drop prob]` 也会红。另钉 `monster_hell` 是唯一声明难度键段的，且同族读取器在它上面抛 `InvalidDataException` |
| 3 | 十六个标签中七个共有、九个是同族其余文件全不声明的 | **是** | `Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else`。钉 `own.Count == 16`，再把两个集合**逐名**比对（各按 Ordinal 排序后整表相等）。这一条特意不只钉 7/9 两个数：round 5→6 的教训正是数目对了而归属描述错了 |
| 4 | 那三张各四组七个（28）；`monster_hell` 15 = 前缀 2 加两组七个；本表 7 且无前缀 | **是** | `One_rarity_group_here_four_in_three_siblings_and_a_counted_two_in_the_abyss_table`。「无前缀」用**可判定的形状**钉，不是靠形容词：本表 `count % 7 == 0` 且首值 500000（阈值量级），`monster_hell` `count % 7 == 1` 且首值 2 恰等于 `(count-1)/7`。再钉读出的 `Rarity.Values` 与档案七值逐值相同——多吞或少吞一个数都会红 |
| 5 | 同族一段的宽度是两个等级界加五个率（一个金币率、四个物品率） | **是** | `A_sibling_band_is_two_bounds_and_five_rates_where_a_band_here_is_two_and_one`。宽度是**除出来**的（各文件自己的 `[drop prob count]` 除自己的整数数），三张表都除；再经 `PvfDropRateTableSource` 钉每个 band `Rates.Count == 5`。「一金币 + 四物品」由 `DropRateSlot` 的五个成员与 `Gold == 0` 钉住。反面钉本表 `72/(6×4) = 3` |
| 6 | 「末位 = 前一位 + 1」不是家族不变式：`monster_hell` 两组均不满足 | **是** | `The_closing_invariant_holds_here_and_in_the_monster_table_but_in_no_abyss_group`。先整体转录 `monster_hell` 的十五个整数（任何一个变了都红），再对两组各一次 `Assert.False`；正面钉本表一组与怪物表四组都满足。三个方向都在，所以「不变式失效」与「不变式变成全家族成立」两种漂移都会被抓到 |

### 边界与方法

- **只改了一个文件**：`Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs`
  （外加本节两段追加）。`git status --short` 与 round 1/QR-2 所见一致，无第五处产品改动；
  `git diff --stat` 只有 `InfrastructureModule.cs` 的 3 行（round 1 的，本轮未再动）。
- **未改任何产品代码**，未改任何既有判据的期望值，未改动既往任何一节。
- **未建 git 提交**，HEAD 仍在基线 `c0cb6c99`；未合并、未推送、未建 PR、未删分支，未动主 checkout
  与 `.worktrees/client-debug-lab`。
- `System.*` using 在前、不做字母序、最小插入：本轮**一行 using 都没加**——
  `PvfTokenKind` 走既有的 `using Dfo.Pvf.Tokens;`，`PvfArchive` 只经 `var` 使用，无需引入 `Dfo.Pvf`。
- `TreatWarningsAsErrors` 下构建通过（跑到绿即 0 警告）。
- 判据命名说清它钉的事实：六个方法名分别念作「品级带与怪物表在 below 列相同、在 80–89 的 above 列不同」
  「三个同族声明 drop prob 而深渊表声明难度键的那个」「十六段里七段共有九段独有」
  「同族一段是两界五率而本表是两界一率」「本表一组、三个同族四组、深渊表自带计数的两组」
  「收尾不变式在本表与怪物表成立而在深渊表两组都不成立」。

## Contract Amendment 3 — 边界扩至一处基线注释

**触发**：round 7 的执行者在写跨文件判据时发现一处与实测不符的注释，
它**在基线代码里、在本 Task 的 Permitted files 之外**，因此按指示未改，报给控制器。

**位置**：`Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs` 的
`DropRateTable` remarks：

> Four of the **five** files in this family share this shape exactly -- monster, monster-extra,
> breakable object and **clear reward** -- and the abyss table does not.

**两处错**，均已实测：家族实有**七**个文件（`(r)control`、`clearreward`、`common`、
`monseter`、`monseter_extra`、`monster_hell`、`object`）；而且它把**翻牌表**列为完全同形，
这正是本 Task 全程在推翻的事。它还与同一文件下方 `DropRateTables.ClearReward` 自己的注释
直接矛盾——那里写着 its `[drop prob]` is not the same shape。

**控制器裁定：批准扩界，改这一句。** 理由不是整洁：

这句话会**把读它的人送去踩本 Task 刚防住的那个坑**。照它说的，翻牌表可以用
`PvfDropRateTableSource` 读——而那正是本 Task 从第一天起就在防的事
（Amendment 1 记着借用怪物表品级带会让 80–89 级悄悄多开一个品级）。
留着它，本 Task 的成果就有一半被自己的邻居抵消。

扩界的范围严格限于这一段注释：**一句话，不动任何可执行代码，不动任何判据期望值。**
改后的文本里，「只有三张声明该段」与「翻牌表另一形状」由 round 7 新写的判据钉住；
**「家族七个文件」当时没有任何判据钉住**——判据文件里的同族数组是手抄的六项，
「这六个就是全部」本身是被相信而不是被数出来的。该项直到 round 8 从归档的文件表把
家族数出来（判据 `The_family_is_the_seven_files_the_archives_own_file_table_holds`）
才有出处。（本句于 round 8 就地订正；Amendment 3 的裁定、理由与扩界范围均不变。）

Permitted files or interfaces 相应增补：
`Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs`（仅此一段 remarks）。

**这也是 Amendment 2 那条约束的第一次实际收益**：跨文件断言一旦要求由判据钉住，
邻居代码里同类的旧错误就会在写判据的过程中自己浮出来。

## Independent Quick Review — round QR-5

- 裁决：**changes_required**
- 复核的契约：Contract revision 1 + Contract Amendment 1 / 2 / 3（含 Amendment 2 增补的那条
  「跨文件断言必须由判据钉住」验收条件）
- 评审者：本轮为独立新上下文，未参与 round 1–7 与 QR-1–QR-4。只读、只核、只追加。

### 复核的 diff 身份

`git status --short`（HEAD 仍为基线 `c0cb6c99`，无提交）：

```
 M Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs
 M Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs
?? Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs
?? Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs
?? Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs
?? .rks-agent/tasks/flipping-a-clear-reward-card/
```

`git diff --stat` = `DropRateContent.cs` 10 行（+7/−3，全在 `DropRateTable` 的一段 remarks 内）、
`InfrastructureModule.cs` 3 行（round 1 的注册行与两行注释）。**改动范围与契约 + Amendment 3
完全一致，无越界、无夹带**：两个新增产品文件 + 一个新增判据文件 + 两处已授权修改。
（派工简报所说的「3 个新增产品文件」与契约的 Permitted files 不符，实际是 2 个；以契约为准。）

### 我自己跑的证据

1. `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
   → Pvf 60、Networking 51、Protocol 376、Infrastructure 202（跳过 2）、Application 771、
   Persistence 244、Host 4 = **1708 通过 / 0 失败 / 2 跳过**，一次跑成，无已知假红形状。
   两条跳过是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
   与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，
   即「因为档案在才跳过」的那两条。满足验收条件 5。
2. `PvfProbe files itemdropinfo` → 七个文件，与 round 6/7 所述一致。
3. `PvfProbe dump` 七个族内文件，自行按段切分统计（未采信任何转述数字）。
4. 一个只放在 scratchpad、未写入仓库的反射探针，直接对已构建的
   `Dfo.Server.Infrastructure.dll` 调用 `PvfDropRateTableSource.Load`（见发现 1）。

### 一、六条新判据：逐条核期望值，并判它们钉不钉得住

**(a) 期望值与归档实际逐项相符——我自己 dump 出来比的，六条全对，无一处偏差。**

| 判据 | 我 dump 出来的实测 | 结论 |
|---|---|---|
| 品级带（`:451`） | 两表各 600 整数 / 200 行，等级集合皆 1..200；`below` 列 **200 行零差异**；`above` 差异等级集合 = `{80..89}`，窗口内本表 3 / 怪物表 4；`above` 取值集合 本表 `{3}` 怪物表 `{3,4}`；`min(level−below)` 两表均为 1；L86 本表 `(7,3)` 怪物表 `(7,4)` | ✓ 与判据期望逐项相同 |
| `[drop prob]` 声明者（`:502`） | 声明者恰为 `monseter`/`monseter_extra`/`object`；`monster_hell` 只声明 `[dungeon difficulty drop prob]`；`common`（仅 `[gold drop ref table]`）与 `control`（五个 `[control of …]`）两者皆不声明 | ✓ |
| 段落集合 16/7/9（`:538`） | 开启标签数：clearreward 16、monseter 12、monseter_extra 8、monster_hell 5、object 7、common 1、control 5。`comm` 求交/求差得到的共有 7 名与独有 9 名，**与判据里逐名列出的两张表逐字相同** | ✓ |
| 一段宽度（`:587`） | `[drop prob count]`/整数数：monseter 10/70、monseter_extra 7/49、object 4/28 → 宽 7；clearreward 4/72、6 个档案 → 72/(6×4)=3 | ✓ |
| 稀有度组（`:641`） | monseter/extra/object 各 28；monster_hell 15（首值 2 =（15−1)/7）；clearreward 7、首值 500000 | ✓ |
| 收尾不变式（`:696`） | monster_hell 十五值 `2 / 0 599000 993000 993000 1000000 1000001 1000001 / 0 800000 1000000 1000000 1000001 1000002 1000001`，两组末位分别 = 前一位、= 前一位 − 1，**均不满足**；clearreward `1000003 = 1000002 + 1`；monseter 四组全满足 | ✓ |

**(b) 会不会红：六条都真的依赖归档读数，没有一条是自我循环或永真。**

- `:451` 两表分别经 `PvfClearRewardRateSource` 与 `PvfDropRateTableSource` 真读出来再比；
  最后两句 `Distinct()` 集合断言是「窗口之外确无差异」的正面证据，不是抽样。
- `:502` 用**整个列表相等**而非逐个 `Contains`，且 `Siblings` 含 common/control，
  所以「第四个文件开始声明」与「common/control 也声明」两种漂移都会红。
- `:538` 钉 `own.Count == 16` 后把两个集合按 Ordinal 排序**逐名**比对，标签在两集合间移动会红。
- `:587` 每个文件的宽度由**它自己的 `[drop prob count]` 除自己的整数数**得出（`:607`、`:608`
  两句把 `10/70` 等字面量绑到归档），再经 `PvfDropRateTableSource` 钉 `Rates.Count == 5`。
- `:641` 钉三张 28 且读取器恢复出 4×7、深渊 15 且首值等于组数、本表 7 且
  `ladder.Values` 与档案七值逐值相同（多吞少吞一个数当场红）。
- `:696` 先整体转录十五值（任何一个变都红），再对两组 `Assert.False`，正反两面都在。

唯一两处「空转」：`:610-611` 的 `Assert.Equal(5, 7 - 2)` 与 `:656`/`:676` 的
`Assert.Equal(0, 28 % 7)`，是常量恒等式，钉不住任何东西——但它们旁边的句子把同样的事实
钉住了，属冗余而非缺陷，不要求整改。

**结论：六条判据的期望值全部属实，且六条全部真的在比归档读数。这一部分我判它成立。**

### 二、发现 1（changes_required，本轮最重的一条）——本 Task 最吃重的那句话是假的

`Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs:19-24`：

> A reader that accepted both shapes would have to guess which one it was holding, and guessing
> wrong produces a table of plausible numbers rather than an error: this file's `[drop prob]` is
> 72 integers and declares 4 bands, so the sibling reader would divide it into four bands of
> eighteen, call the sixteen numbers after each pair of bounds sixteen item slots,
> **and hand back a full table without complaining once.**

**最后半句是假的。实测：同族读取器会抛。** 我用只放在 scratchpad 的反射探针，对已构建的
`Dfo.Server.Infrastructure.dll` 直接调用：

```
etc/itemdropinfo_clearreward.etc: THREW InvalidDataException:
  'etc/itemdropinfo_clearreward.etc' declares 5 numbers under
  '[dungeon difficulty drop bonusrate]', which is not a whole number of groups for its 16 rates.
etc/itemdropinfo_monster_hell.etc: THREW InvalidDataException:
  'etc/itemdropinfo_monster_hell.etc' declares 2 rate bands but 0 numbers to build them from.
```

代码路径也对得上：`PvfDropRateTableSource.Read` 确实先把 72 切成 4 × 18、把 16 个数当成
16 个物品槽建出 band（`PvfDropRateTableSource.cs:117-133`），但 band 建完之后立刻用
`rateCount = 16` 去切三张加成表（`:141-143`），而翻牌表的 `[dungeon difficulty drop bonusrate]`
只有 5 个数，`5 % 16 != 0` → `Groups` 抛（`:231-236`）。**表根本没被交出来。**

这一句还与同族读取器自己的 remarks **直接矛盾**：
`PvfDropRateTableSource.cs:14-18` 写「Every one of those is checked, so a file whose shape is not
this one fails here by name instead of arriving as a table of plausible wrong numbers」。
两句必有一假，实测是后者为真。

**为什么这条比前四轮的十五处都要紧**：这是整个 Task「为什么要另写一个读取器」的**唯一论据句**，
也是 Amendment 3 批准扩界时引用的那条理由（「照它说的，翻牌表可以用 `PvfDropRateTableSource` 读」）。
它属于 Amendment 2 所定义的同一类缺陷——对本读取器之外的读取器行为作事实断言、无判据钉住——
而且 QR-4 的断言全集里把 `PvfClearRewardRateSource.cs:16-24` 标成了 `✓`，这是评审侧的第三次误判。

**要求整改**（只改注释 + 加一句判据，不动行为）：

1. 把 `:22-24` 改成实测成立的说法。可用的实情是：`[drop prob]` 那一步**确实**不报错
   （72/4 整除、宽 18 减两个界剩 16 个「物品槽」），报错要等到三段之后的加成表切分才发生，
   而且报的是难度表而不是 `[drop prob]`——即「同族读取器会拒，但拒的理由与真正的错处无关，
   靠的是下游一次巧合的除法」。这比原句更能支撑「两个读取器要分开」，而且是真的。
2. 按 Amendment 2 补一条判据钉住它，一行即可，就放在 `:523` 那句深渊表断言旁边：
   `Assert.Throws<InvalidDataException>(() => new PvfDropRateTableSource(provider).Load(DropRateTables.ClearReward));`
   并断言消息里含 `[dungeon difficulty drop bonusrate]`——这样「哪一步拒的」也被钉住。

### 三、发现 2（changes_required，代价很小）——扩界文本里的「seven」没有判据背书

Amendment 3 写：「改后的文本每一项都由 round 7 新写的判据钉住（**家族七个文件**、
只有三张声明该段、翻牌表另一形状）」。**前一项不成立。**

- `Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs:113`
  「Three of **the family's seven files** share this shape exactly」。
- 六条新判据里没有任何一条去数归档的文件表。`PvfClearRewardRateSourceTests.cs:69-84` 的
  `Siblings` 是一个**手抄的六元数组**，其上的注释还自称「Every file of the `itemdropinfo`
  family except the clear-reward table itself. Six of them, so a claim of the form "no other file
  of the family declares this" is a claim about these six and **can be counted rather than
  believed**」——但「这六个就是全部」这件事本身恰恰是被相信而非被数出来的。
- 后果不只是「seven」一个数：`:538` 那条判据的「nine are declared **nowhere else**」，
  其「nowhere else」的范围也完全由这个手抄数组决定。族里若真有第八个文件，六条判据一条不红。

**这正是 Amendment 2 那条验收条件所指的情形**（对本读取器所读文件之外的文件作事实断言），
所以按契约它必须有判据，或者删去。

**要求整改**：数一次，别抄。`PvfArchive.FileCount` / `GetEntry(int)` / `ResolveString(int)`
都是 public（`Tool/PvfProbe/Program.cs:71-92` 的 `files` 子命令用的就是这三个），
在 `:538` 或 `:502` 里加四行即可把「归档文件表里路径含 `itemdropinfo` 的文件恰有七个，
且恰是 `Siblings` 六项加本表」整个钉住。这一条钉住之后，`Siblings` 才真的是「数出来的」，
`DropRateContent.cs:113` 的「seven」与 `:538` 的「nowhere else」也才同时有了出处。

### 四、Amendment 3 的扩界本身：恰当，一处理由需订正

- **原文确实错，两处都错**：基线原文「Four of the **five** files in this family share this shape
  exactly -- monster, monster-extra, breakable object and **clear reward**」。家族实测七个文件
  （我自己 `PvfProbe files itemdropinfo` 数过）；翻牌表也确实不同形（`[drop prob]` 宽 3 不是 7）。
  它还与同文件 `DropRateTables.ClearReward` 的注释互相矛盾。**改是对的。**
- **改动确实只有这一段**：`git diff` 显示 `DropRateContent.cs` 只有 `DropRateTable` remarks
  一段变化，−4/+7 行，无任何可执行代码、无任何判据期望值改动。✓
- **改后文本的背书情况**：三张同形表的具名列表 ✓（`:502` + `:587` + `:641`）；
  「abyss 声明难度键段」✓（`:502`）；「common 与 control 完全不声明」✓（`Siblings` 含这两个，
  列表相等断言会红）；「翻牌表是具名档案、由 `IClearRewardRateSource` 读」✓。
  **唯「seven」无背书**，见发现 2。
- **一处理由需要订正（不影响裁定）**：Amendment 3 说这句话「会把读者送去用 `PvfDropRateTableSource`
  读翻牌表」。按发现 1 的实测，真那么做会抛而不是拿到一张错表——所以危害是「误导读者以为两者同形」，
  不是「读出一张看似合理的错表」。扩界该批，理由该照实测写。

### 五、漏网断言扫描（四个文件全扫，凡带数字、全称词或跨文件比较的句子）

除发现 1、发现 2 外，四个文件里我逐句算过的断言**均成立**，包括：
`ClearRewardContent.cs` 的 `:7-11`、`:37-38`（六名里三个两词 ✓）、`:63-64`（event2 四率与 event 全同 ✓）、
`:92-95`（16/7/9 与「多数是自己的」，9 > 7 ✓）、`:97-100`（四段不读，`:408` 判据钉了四个名字 ✓）、
`:121-123`、`:138-143`、`:156-158`（L86 少一级 ✓，落在 80–89 窗口内被 `:451` 钉住）、`:212-213`；
`PvfClearRewardRateSource.cs` 的 `:112-113`（`2 13 17 0 -5`，且全文件唯一负数——我 grep 过，
只有一个 `-5` ✓）、`:124`、`:130-131`（`1.02 1.38 1.6 1.9 2` ✓）、`:180-183`、`:280-286`、
`:333-339`、`:342-345`（`min(level−below)=1`、首行 `1 0 3` ✓）、`:441-444`；
判据文件的 `:118-135`（六档案率值逐条 ✓）、`:179`（`0 600 1000 1400 1200` ✓）、
`:188-202`、`:223-234`（`100+(level−1)*120`，L1=100 L5=580 L86=10300 L200=23980 ✓）、
`:259`、`:305-316`（六行同 id `10002120` ✓，pcroom `50 7454 1` ✓）、`:337-341`。

### 未构成阻塞的观察（本轮不要求整改，留给最终评审 / TASK-02）

1. **`DropRateContent.cs:46-47`（基线，不在 Permitted files 内）**：`RarityLadder` remarks 写
   「**every group in this archive** satisfies "basis = last threshold + 1"」。深渊表两组都不满足
   （我已 dump 核实），所以这句是假的——而且它与本轮新增的 `:696` 判据**正面冲突**：
   那条判据存在的全部理由就是「这不是家族不变式」。这是 Amendment 3 所批准的那一段之外、
   同一文件内的**第二处**同类错误。改它需要控制器再批一次扩界，或转 TASK-02。
2. **`DropRateContent.cs:204`（基线）**：`DropRateTables.ClearReward` 的注释写
   「**Same sections**, but its `[drop prob]` is not the same shape」。实测两表段落集合只共有 7 个
   （16 vs 12），并非同一套段落。**而新写的 `:113-118` 恰好用 `see cref` 指向了这一句**，
   等于把读者引向一句不准确的话。同上，需扩界才能改。
3. **`DropRateContent.cs:113` 的「share this shape **exactly**」**：三张里 `object` 并不声明
   `[party member drop bonusrate]`（我 dump 过，它只有 7 个标签），所以它读出来的
   `PartyMemberRates` 是空的。若「this shape」指 `[drop prob]` 的 band 形状则句子为真且已被钉住；
   若指整条 `DropRateTable` 记录则「exactly」偏强。属措辞精度，不阻塞。
4. **`ClearRewardContent.cs:30-31` 的「the fourth, the abyss table」**：这是一个关于家族的序数。
   round 6 刚因为同样的理由把 `InfrastructureModule.cs` 的「第五个文件」改成「另一个文件」，
   并写下「序数在这里没有任何用处，它唯一的作用就是提供一个会错的机会」。此处按「刚列举的四张
   费率表里的第四张」读可通，但它是同一个句式的残留。
5. **`ClearRewardContent.cs:53`「only `Default` has a caller today」**：实测六个常量今天**都没有**
   产品侧调用者（`grep` 全仓只有判据文件引用），本 Task 也不新增规则服务。属前瞻性措辞。
6. QR-4 记的四条观察（`:118` 的「every neighbouring rate」、判据 `:293-296` 的过头论证、
   `:407-408` 描述不发生的事、`:427-429` 的「eleven numeric members」只查了三个成员）
   本轮复核仍然如此，未回退也未处理，与 QR-4 结论一致，不重复要求。

### 六、TASK-01 能否收工——逐条对密封契约

| 验收条件 | 判断 | 依据 |
|---|---|---|
| 1. 基础率 / 难度加成 / 地图数加成 / 付费成本表与实测归档逐值相同 | **满足** | 我自己 dump 后逐值比过，见上表与第五节 |
| 2. 稀有度阶梯复用既有 `RarityLadder` | **满足** | `ClearRewardTable.Rarity` 即 `RarityLadder?`，未重新建模 |
| 3. 不使用的三段被具名列出并写明理由 | **满足** | `UnreadSections`（另含第四段并说明「不是未查明而是不适用」），`:408` 判据钉四个名字确在档案里 |
| 4. 每个被读段落有标签常量与出处注释 | **满足** | 十二个 `private const string` 段落常量 + `ClearRewardTable` 每个 param 注明来源段 |
| 5. 全量判据 ≥ 1690 通过 / 0 失败 / 2 跳过 | **满足** | 我自己跑出 1708 / 0 / 2 |
| Amendment 2 增补：跨文件断言必须有判据钉住，钉不住就删 | **不满足** | 发现 1（`PvfClearRewardRateSource.cs:23-24` 的断言不但无判据，且**为假**）与发现 2（`DropRateContent.cs:113` 的「seven」无判据，`Siblings` 完备性亦无判据） |

**因此本轮不能收工。** 但两条都只需改注释文字 + 加两处判据（合计约十行），不涉及任何行为改动，
也不需要新的扩界（发现 1 改的是本 Task 自己的新增文件；发现 2 加的判据在本 Task 自己的判据文件里，
`DropRateContent.cs:113` 的那句已在 Amendment 3 授权范围内）。整改后重跑 CK-S 即可。

### 备注

本轮只读、只核、只追加：未修改任何产品代码、判据、既往轮次或任务控制状态。
反射探针只写在 scratchpad，未进入仓库；`git status` 在评审前后一致。

## Execution Result — round 8

派工简报里的每个数都当作线索而不是结论：下面每一项都是我自己跑出来的，没有一个数字是从
QR-5 或简报抄的。测量手段有两种，两种都只落在 scratchpad、未写入仓库：

- **反射探针**：一个只在 scratchpad 的 console 工程，`ProjectReference` 到 `Dfo.Pvf` 与
  `Dfo.Server.Infrastructure`，用反射构造 internal 的 `PvfArchiveProvider` 与
  `PvfDropRateTableSource` 并直接调 `Load`，逐个文件打印「返回还是抛、抛的是什么类型、
  消息原文是什么」；同一个探针还枚举 `PvfArchive.FileCount` / `GetEntry` / `ResolveString`
  的整张文件表，并按段收 token 统计每个族内文件的开启标签与各段整数。
- **新增判据本身**：写完先跑绿，再逐条改坏期望值确认会红（见下「变异检查」）。

### 第 1 件 —— 「一声不吭返回废表」是假的，实测是抛

**我实测到的原样输出**（探针对七个族内文件各调一次
`PvfDropRateTableSource.Load`，其中三张真的读成功了，用来说明探针本身没坏）：

```
etc/itemdropinfo_clearreward.etc:   THREW System.IO.InvalidDataException
    message: 'etc/itemdropinfo_clearreward.etc' declares 5 numbers under
             '[dungeon difficulty drop bonusrate]', which is not a whole number of groups
             for its 16 rates.
etc/itemdropinfo_monseter.etc:       OK -> DropRateTable
etc/itemdropinfo_monseter_extra.etc: OK -> DropRateTable
etc/itemdropinfo_object.etc:         OK -> DropRateTable
etc/itemdropinfo_monster_hell.etc:  THREW System.IO.InvalidDataException
    message: 'etc/itemdropinfo_monster_hell.etc' declares 2 rate bands but 0 numbers to
             build them from.
etc/itemdropinfo_common.etc:        THREW System.IO.InvalidDataException
    message: 'etc/itemdropinfo_common.etc' declares no '[drop prob count]'.
etc/(r)itemdropinfo_control.etc:    THREW System.IO.InvalidDataException
    message: 'etc/(r)itemdropinfo_control.etc' declares no '[drop prob count]'.
```

**所以原句错在哪、又对在哪，要分开说：**

原句是「四段十八 → 每段减两界剩十六 → 当成十六个物品槽 → 一声不吭交回一张完整的表」。
前三步**是真的**，而且证据就在异常消息里：消息说 `for its 16 rates`，那个 16 只可能是
`bandWidth - 2 = 18 - 2` 算出来的，也就是说 `[drop prob]` 那一关它一句话都没说就过了。
错的只有最后一步：它没有交回表，它抛了。

**而且它抛得并不体面**：拒读发生在它**读对了**的那一段上——
`[dungeon difficulty drop bonusrate]` 只有 5 个数，`5 % 16 != 0`。
换句话说，救它的是下游一次除法碰巧除不尽，不是有谁看出形状不对；
消息里点名的段落，恰恰不是它读错的那一段。

**改法**：类型注释里那一段拆成两段重写。第一段照实说「会拒，但拒它的不是发现形状不对的人」，
把实测的异常消息原样引进注释；第二段按简报的指点，把「危险性论证」挪到真正会悄悄出错的
地方——`[item drop ref table]`：借怪物表的品级带在 80–89 级多开一个品级，全程零报错，
那一条由既有判据钉着。原句里那个「悄悄给出废表」的例子被彻底删掉，没有换个说法留下。

**新写的这段话本身也是跨文件断言**，按 Contract Amendment 2 必须有判据，故补了一条
（见下「第 1 件的判据」）。

### 第 2 件 —— 家族有几个文件，从归档的文件表数出来

探针枚举整张文件表（`archive.FileCount` = **712010** 个条目），取路径含 `itemdropinfo`
的，**恰好七个**：

```
etc/(r)itemdropinfo_control.etc
etc/itemdropinfo_clearreward.etc
etc/itemdropinfo_common.etc
etc/itemdropinfo_monseter.etc
etc/itemdropinfo_monseter_extra.etc
etc/itemdropinfo_monster_hell.etc
etc/itemdropinfo_object.etc
```

顺带用更松的针 `itemdrop` 数了一遍，得 13 个——多出来的 6 个全是
`common/commoneffect/animation/itemdropeffect/*.ani` 特效文件，与本族无关。
**七这个数与注释一致，所以按简报的规矩：不改注释，补判据。**

新增判据 `The_family_is_the_seven_files_the_archives_own_file_table_holds`
（`PvfClearRewardRateSourceTests.cs`）：走一遍归档文件表把七个路径**逐名**比出来
（不是只比数目——换掉一个文件也要红），再断言手抄的 `Siblings` 六项恰好等于「这七个减去本表」。
这一条钉住之后，`Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else`
里的「nowhere else」才第一次真的有边界，`DropRateContent.cs` 的「the family's seven files」
才第一次有出处。

### 第 1 件的判据 —— 同族读取器到底怎么拒的

新增判据 `The_sibling_reader_refuses_this_file_over_a_section_it_read_correctly`，钉四件事：

1. 真的抛 `InvalidDataException`；
2. 消息里含 `[dungeon difficulty drop bonusrate]` 与 `16 rates`——**抛在哪一段**被钉住；
3. 消息里**不含** `[drop prob]`——「点名的不是它读错的那一段」被钉住；
4. 从归档数出来：`[drop prob count]` = 4、`[drop prob]` 的整数数除以 4 恰为 18、
   18 − 2 = 16，且难度加成段恰有 5 个数——「16 是误读的产物而不是抱怨的内容」被钉住。

### 第 3 件 —— `RarityLadder` 的「every group in this archive」是假的

探针按段读出各文件的 `[basis of rarity dicision]`：

```
clearreward     7 个：500000 799900 999900 1000000 1000001 1000002 1000003
monseter       28 个（4 组 × 7），每组末位 1000003 = 前一位 1000002 + 1
monseter_extra 28 个（4 组 × 7），同上
object         28 个（4 组 × 7），同上
monster_hell   15 个：2 | 0 599000 993000 993000 1000000 1000001 1000001
                        | 0 800000 1000000 1000000 1000001 1000002 1000001
common / (r)control  0 个（不声明这一段）
```

深渊表两组：第一组末位 1000001、前一位 1000001（差 **0**）；第二组末位 1000001、
前一位 1000002（差 **−1**）。**两组都不满足「basis = 末位阈值 + 1」。**
又扫了整份归档所有 `.etc` 文件，声明这一段的**恰好五个**，就是上面非零的那五个。

所以「every group in this archive satisfies …」为假，且与本 Task round 7 新写的判据
`The_closing_invariant_holds_here_and_in_the_monster_table_but_in_no_abyss_group` 正面冲突。
**改成**：这是「经这个类型读出来的那些组」的形状，不是家族的规矩；深渊表两组都不满足，
并把两组的收尾数值原样写进注释；同时点明深渊表是被拒而不是被读的
（这一条由既有判据 `The_abyss_table_is_refused_rather_than_read_as_this_shape` 与
`Three_siblings_declare_drop_prob_and_the_abyss_table_a_difficulty_keyed_one` 钉着，
且我的探针也直接看到它抛）。

### 第 4 件 —— `DropRateTables.ClearReward` 的「Same sections」是假的

探针逐文件收开启标签（`[/…]` 关闭标签不计）：

```
clearreward 16、monseter 12、monseter_extra 8、object 7、monster_hell 5、
common 1、(r)control 5
```

翻牌表 vs 怪物表：**共有 7 个**；**只在翻牌表 9 个**；**只在怪物表 5 个**
（`[condition rate]`、`[first boss/named mob hunting]`、`[gold quantity]`、
`[gold volume]`、`[monster type drop bonusrate]`）。16 ≠ 12，两边各有对方没有的段，
**「Same sections」在任何读法下都不成立。**

**改成**：不是同一套段落——十六个里七个是家族的、另外九个别的文件都不声明；
七个里有一个它声明得不一样（`[drop prob]` 是具名档案而不是五率的段），
所以翻牌由 `IClearRewardRateSource` 读，而不是把这个路径交给 `IDropRateTableSource`。
这句的每一项都由 round 7 的
`Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else` 钉着。
（QR-5 的非阻塞观察 2 说 `DropRateTable` 的 remarks 用 `see cref` 指向这一句、等于把读者
引向一句不准确的话——这一处改完，那个指向就不再有问题。）

### 第 5 件 —— Contract Amendment 3 里那一句

原句：「改后的文本每一项都由 round 7 新写的判据钉住（家族七个文件、只有三张声明该段、
翻牌表另一形状），所以它不是又一句无出处的断言，而是有判据背书的陈述。」

**只改这一句**，改为：后两项由 round 7 判据钉住；「家族七个文件」当时**没有**任何判据钉住
（同族数组是手抄的六项，「这六个就是全部」是被相信而不是被数出来的），该项直到 round 8
补上文件表判据才有出处。**Amendment 3 的裁定、理由与扩界范围一个字未动**，
QR-5 第三节引用该句的那一段（既有评审轮次）也一个字未动。

### 变异检查：新增的两条判据不是空的

两条写完第一次跑就 20/20 全绿（期望值是先由探针读出、再写进判据的），所以逐条改坏期望值确认会红：

| 改坏什么 | 结果 |
|---|---|
| 期望的家族清单里删掉 `etc/itemdropinfo_common.etc`（七项变六项） | **红**：`Collections differ ... ↓ (pos 2)` |
| `Siblings` 比对处改成 `Siblings.Take(5)` | **红**：同一判据 |
| 消息期望 `16 rates` → `15 rates` | **红**：`Sub-string not found: "15 rates"` |
| `Assert.DoesNotContain(DropProbTag, …)` → `Assert.Contains` | **红**：`Not found: "[drop prob]"`（反证消息里确实没有它） |
| `Assert.Equal(18, numbers / 4)` → 期望 19 | **红**：`Expected: 19 / Actual: 18` |

五处全红，随后从备份还原、重跑全绿，还原后与备份逐字节相同（`diff` 无输出）。

### 判据的真实输出

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test
Server/86JP.Server.slnx --nologo`，逐程序集：

| 程序集 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Server.Networking.Tests | 51 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 376 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Application.Tests | 771 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 244 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 204 | 2 | 0 |
| Dfo.Server.Host.Tests | 4 | 0 | 0 |
| **合计** | **1710** | **2** | **0** |

**1710 通过 / 0 失败 / 2 跳过**，1710 = round 7 的 1708 + 本轮 2。跳过仍是那两条
（`grep -rn "Skip.If(ContentArchive.Available" Server/tests/` 实见恰两处：
`PvfQuestDefinitionSourceTests.cs:372`、`PvfTownDefinitionSourceTests.cs:227`），
未暴涨，说明 `PVF_ARCHIVE_PATH` 生效、两条新判据真跑。一次跑成，未出现已知假红形状。
`TreatWarningsAsErrors` 下跑到绿即 0 警告。本类单独跑 20/20 通过、0 跳过（18 + 2）。

### 与简报线索不符之处

**没有。** 简报给的每条线索（同族读取器会抛、抛在
`[dungeon difficulty drop bonusrate]`、家族七个文件、深渊表两组不满足收尾不变式、
翻牌表与同族不是同一套段落）我都独立重测过，**逐项相符，无一处不符**。

有一处需要**补充**而不是纠正：简报把第 1 件概括为「同族读取器会拒绝这个文件」。这没错，
但只说到一半——它拒得晚且拒错了地方：`[drop prob]` 那一关它读错了却毫无怨言地过了，
真正把它拦下来的是它读对了的那一段除不尽。这个区别不是措辞，它决定了注释该怎么写：
「会拒」不足以支撑「所以两个读取器要分开」（一个凑巧的除法不能当作检查），
真正支撑那个决定的是本表与怪物表的形状差本身，以及品级带那个**确实**无声出错的例子。
改后的注释按这个来写。

### 一处顺带的记录（不在本轮五件事内，未改）

探针顺带看到 `PvfDropRateTableSource.Load` 在 `etc/itemdropinfo_common.etc` 与
`etc/(r)itemdropinfo_control.etc` 上抛的是「declares no '[drop prob count]'」。
这与 `PvfDropRateTableSourceTests.cs` 里「its groups do not close the way **the other four** do」
的措辞有关——那是一句家族计数的旧说法（家族实为七个文件），落在**既有判据文件**里、
不在本轮允许改的清单内，未动，报给控制器。

## Executor Self-check — round 8

### 五件事逐条

| # | 要求 | 是否满足 | 依据 |
|---|---|---|---|
| 1 | 修 `PvfClearRewardRateSource.cs` 里「一声不吭返回废表」那句假话，先实测同族读取器的真实行为 | **满足** | 反射探针实调 `PvfDropRateTableSource.Load("etc/itemdropinfo_clearreward.etc")`，**抛 `InvalidDataException`**，消息原文已逐字记入上节与注释。注释拆成两段重写：一段照实说「拒了，但拒它的不是发现形状不对的人，点名的还是它读对的那一段」，一段把危险性论证挪到 `[item drop ref table]`（借怪物表品级带在 80–89 级静默多开一档）。原来那个「悄悄给出废表」的例子已删除，未换说法保留 |
| 2 | 给「家族有几个文件」补判据，从归档文件表数 | **满足** | `The_family_is_the_seven_files_the_archives_own_file_table_holds`：走 `FileCount`/`GetEntry`/`ResolveString` 整张表，逐名比出七个路径，并钉 `Siblings` 六项 = 七个减本表。实测七个，与注释相符，**故按简报规矩改判据不改注释** |
| 3 | 修 `DropRateContent.cs` 的「every group in this archive satisfies basis = last threshold + 1」 | **满足** | 自行 dump 五个声明该段的文件；深渊表两组分别「1000001 接 1000001」「1000001 接 1000002」，均不满足。注释改为「经这个类型读出来的组是这个形状，不是家族的规矩」，并写明深渊表两组的实际收尾与「它是被拒而不是被读的」 |
| 4 | 修 `DropRateTables.ClearReward` 的「Same sections」 | **满足** | 自行比段落集合：翻牌 16 / 怪物 12，共有 7、翻牌独有 9、怪物独有 5。改为「不是同一套段落：十六个里七个是家族的、九个别处不声明，其中一个声明得不一样」，每一项都由 round 7 的 16/7/9 判据钉住 |
| 5 | 订正 Contract Amendment 3 里「改后的文本每一项都由判据钉住」 | **满足** | 就地改那一句：两项由 round 7 判据钉住、「家族七个文件」当时无判据、round 8 补上后才有出处。裁定本身、扩界范围、Permitted files 增补句均未动；QR-5 引用该句的评审段落亦未动 |

### 边界

- **只改了四个文件**：`PvfClearRewardRateSource.cs`（注释）、`DropRateContent.cs`（两处注释）、
  `PvfClearRewardRateSourceTests.cs`（两条新判据 + 一个 `FamilyNeedle` 常量 + 一个
  `FamilyFiles` 助手 + `Siblings` 的 XML 注释加一句指路）、本 Task 文件（Amendment 3 那一句
  + 本轮两节追加）。`git status --short` 与 QR-5 所见一致，无第五处。
- **未改任何可执行代码**：`git diff` 显示 `DropRateContent.cs` 的改动全部落在三段
  XML doc 注释内，`InfrastructureModule.cs` 仍是 round 1 的 3 行、本轮未再动；
  `PvfClearRewardRateSource.cs` 的改动全部在类型 remarks 内。
- **未改任何既有判据的期望值**：判据文件的改动是「新增两个方法 + 新增两个成员 + 一处
  XML 注释补一句」，既有 18 条判据的断言与期望值一个未动。
- **未建 git 提交**，HEAD 仍在基线 `c0cb6c99`；未合并、未推送、未建 PR、未删分支；
  未动主 checkout，未动 `.worktrees/client-debug-lab`。
- 反射探针与判据备份只写在 scratchpad，未进入仓库。
- `System.*` using 在前、不做字母序、最小插入：本轮**一行 using 都没加**——
  `PvfArchiveProvider` 走既有 `using Dfo.Server.Infrastructure.Content;`，
  `PvfArchive` 与其 entry 只经 `var` 使用。
- 新增行未超过本仓的行宽习惯（改动过的三个文件里超过 100 列的行全部是既有行，无一是本轮新写）。

### Contract Amendment 2（跨文件断言必须由判据钉住）在本轮的落实

| 本轮新写/改写的跨文件断言 | 钉住它的判据 |
|---|---|
| 同族读取器读本文件会抛，抛在 `[dungeon difficulty drop bonusrate]`，消息含 `16 rates` 且不含 `[drop prob]` | `The_sibling_reader_refuses_this_file_over_a_section_it_read_correctly`（本轮新增） |
| `[drop prob]` 72 个整数 / 4 段 = 18、18 − 2 = 16、难度段 5 个数 | 同上（从归档数出来，不是常量比常量） |
| 借怪物表品级带会在 80–89 级多开一档且零报错 | `The_grade_bands_share_the_monster_tables_below_column_and_differ_above_at_80_to_89`（round 7） |
| 家族七个文件；`Siblings` 六项就是家族的其余全部 | `The_family_is_the_seven_files_the_archives_own_file_table_holds`（本轮新增） |
| 深渊表两组都不满足收尾不变式；它是被拒而不是被读的 | `The_closing_invariant_holds_here_and_in_the_monster_table_but_in_no_abyss_group`（round 7）、`The_abyss_table_is_refused_rather_than_read_as_this_shape`（既有）、`Three_siblings_declare_drop_prob_and_the_abyss_table_a_difficulty_keyed_one`（round 7） |
| 翻牌表 16 个段落中 7 个共有、9 个别处不声明 | `Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else`（round 7） |

**本轮没有留下任何一句「钉不住的跨文件断言」。**

### 契约验收条款

| # | 条款 | 是否满足 | 依据 |
|---|---|---|---|
| 1 | 基础率 / 难度加成 / 地图数加成 / 付费成本表逐值相同 | **满足** | 既有 12 条判据未动且全绿 |
| 2 | 稀有度阶梯复用既有 `RarityLadder` | **满足** | 本轮未动类型，仅改其 remarks |
| 3 | 不使用的三段具名列出并写明理由 | **满足** | `UnreadSections` 未动，其判据未动 |
| 4 | 每个被读段落有标签常量与出处注释 | **满足** | 段落常量未动 |
| 5 | 全量判据 ≥ 1690 通过 / 0 失败 / 2 跳过 | **满足** | 本轮实测 **1710 / 0 / 2** |
| A2 | 跨文件断言必须由判据钉住，钉不住就删 | **满足** | 见上表；QR-5 的发现 1 与发现 2 均已处理 |

## Controller Disposition — round 8 报的越界项：不扩界

round 8 的执行者报了一处疑似同类缺陷，位于 `Server/tests/Dfo.Server.Infrastructure.Tests/
Content/PvfDropRateTableSourceTests.cs`（既有判据文件，在其边界外），称其中「the other four」
是旧的家族计数说法。它按规矩没有改动，报给控制器——**这个行为是对的**。

**控制器裁定：误报，不扩界。** 两处「the other four」说的都不是家族文件数：

- 第 77 行讲的是 `[monster type drop bonusrate]` **一个段落之内**五组里的另外四组
  （金币与四个物品槽），与家族有几个文件无关。
- 第 279 行讲的是深渊表之外**声明稀有度段的另外四个文件**。实测：全家族七个文件里
  声明 `[basis of rarity dicision]` 的恰有五个——monseter、monseter_extra、
  monster_hell、object、clearreward；common 与 (r)control 不声明。
  深渊表之外正是四个。**原文成立。**

记在此处的理由：本 Task 至此，执行侧与评审侧各自都出现过误判
（评审侧三次、控制器三次、执行侧此一次）。**误报本身不是问题，边界内报出来让人核才是对的做法**；
反过来，若因为「报的人多半是对的」就直接采信并扩界，本 Task 会平白多改一个文件。
凡涉及本主题的断言，无论谁说的，一律先跑一遍。

## Independent Quick Review — round QR-6

- 裁决：**passed**
- 复核的契约：Contract revision 1 + Contract Amendment 1 / 2 / 3 + Controller Disposition（round 8 越界项）
- 评审者：独立新上下文，未参与 round 1–8 与 QR-1–QR-5。只读、只核、只追加；
  未修改任何产品代码、判据、既往轮次或任务控制状态。

### 复核的 diff 身份

`git status --porcelain`（HEAD 仍在基线 `c0cb6c99`，无提交），评审前后一致：

```
 M Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs
 M Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs
?? .rks-agent/tasks/flipping-a-clear-reward-card/
?? Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs
?? Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs
?? Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs
```

`git diff --stat` = `DropRateContent.cs` 27 行（+21/−9）、`InfrastructureModule.cs` 3 行。
**范围恰为 2 个修改的既有产品文件 + 2 个新增产品文件 + 1 个新增判据文件，无越界、无夹带。**
逐行看过 `DropRateContent.cs` 的三段改动：全部落在 XML doc 注释内
（`RarityLadder` remarks、`DropRateTable` remarks、`DropRateTables.ClearReward` summary），
**没有一行可执行代码、没有一个判据期望值被动过**，与 Amendment 3 划的范围相符。

### 我自己跑的证据

1. `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
   → Protocol 376、Networking 51、Pvf 60、Application 771、Host 4、Persistence 244、
   Infrastructure 204（跳过 2）= **1710 通过 / 0 失败 / 2 跳过**。一次跑成，
   未出现已知假红形状（进程/socket 10 秒预算、SQLite 30 秒）。
2. 一个只写在 scratchpad、未进入仓库的反射探针（`ProjectReference` 到 `Dfo.Pvf` 与
   `Dfo.Server.Infrastructure`，反射构造 internal 的 `PvfArchiveProvider` /
   `PvfDropRateTableSource`），用于：枚举整张归档文件表、对七个族内文件逐个真调
   `Load`、按段收 token 统计标签与整数、全库扫 `[basis of rarity dicision]` 的声明者。
   下面每个数字都出自它，**没有一个是从 round 8、QR-5 或派工简报抄的**。

### 一、round 8 的五件整改，逐件自验

#### 第 1 件 —— 同族读取器的真实行为（`PvfClearRewardRateSource.cs:21-31`）

探针对七个族内文件各调一次 `PvfDropRateTableSource.Load`，原样输出：

```
etc/(r)itemdropinfo_control.etc:      THREW InvalidDataException: ... declares no '[drop prob count]'.
etc/itemdropinfo_clearreward.etc:     THREW System.IO.InvalidDataException
    'etc/itemdropinfo_clearreward.etc' declares 5 numbers under
    '[dungeon difficulty drop bonusrate]', which is not a whole number of groups for its 16 rates.
etc/itemdropinfo_common.etc:          THREW InvalidDataException: ... declares no '[drop prob count]'.
etc/itemdropinfo_monseter.etc:        OK -> DropRateTable
etc/itemdropinfo_monseter_extra.etc:  OK -> DropRateTable
etc/itemdropinfo_monster_hell.etc:    THREW InvalidDataException: ... declares 2 rate bands but 0 numbers ...
etc/itemdropinfo_object.etc:          OK -> DropRateTable
```

三张真读成功，说明探针本身没坏。**新文本的每一项都成立**：

- 「会抛 `InvalidDataException`」✓，消息与注释里引的一字不差。
- 「`16` 是误读的产物」✓：探针数出 `[drop prob count]` = 4、`[drop prob]` 下 72 个 Int32
  （另有 6 个 QuotedString，同族读取器的 `Integers` 只收整数），72/4 = 18、18 − 2 = **16**。
- 「拦下它的是它读对了的那一段」✓：`[dungeon difficulty drop bonusrate]` 恰 5 个数
  （`0 600 1000 1400 1200`），`5 % 16 ≠ 0`。代码路径也对得上——`Read` 先无声地把 72 切成
  4 × 18 建出 band，`ReadLadders` 在 7 个数上按宽 7 通过（`1000003 = 1000002 + 1`），
  随后才在 `Groups(DifficultySection, 16)` 上抛。
- 「点名的不是它读错的那一段」✓：消息里确实不含 `[drop prob]`。

原来那句「一声不吭返回完整废表」已彻底删除，未换个说法保留；危险性论证已挪到
`[item drop ref table]`（80–89 级静默多开一档），那一处由既有判据钉着。**第 1 件到位。**

#### 第 2 件 —— 家族文件数（`DropRateContent.cs:113`、判据 `The_family_is_the_seven_files_...`）

探针走整张文件表（`FileCount = 712010`，路径由 `PathOffset` 与 `NameOffset` 拼出），
针 `itemdropinfo` **恰好七个**，与注释的「the family's seven files」一致：

```
etc/(r)itemdropinfo_control.etc   etc/itemdropinfo_clearreward.etc   etc/itemdropinfo_common.etc
etc/itemdropinfo_monseter.etc     etc/itemdropinfo_monseter_extra.etc
etc/itemdropinfo_monster_hell.etc etc/itemdropinfo_object.etc
```

松针 `itemdrop` 得 13 个，多出的 6 个全是 `common/commoneffect/animation/itemdropeffect/*.ani`，
与本族无关。判据 `:751-773` 逐名比这七个、再钉 `Siblings` 六项 = 七个减本表，
写法与我的走法一致。**数与注释相符，按规矩补判据不改注释，做对了。第 2 件到位。**

#### 第 3 件 —— `RarityLadder` remarks（`DropRateContent.cs:45-51`）

探针全库扫 `.etc`，声明 `[basis of rarity dicision]` 的**恰好五个文件**，各段实值：

```
clearreward     7 : 500000 799900 999900 1000000 1000001 1000002 1000003
monseter       28 : 4 组 × 7，每组末位 1000003 = 前一位 1000002 + 1
monseter_extra 28 : 同上          object 28 : 同上
monster_hell   15 : 2 | 0 599000 993000 993000 1000000 1000001 1000001
                      | 0 800000 1000000 1000000 1000001 1000002 1000001
```

深渊表第一组末位 1000001、前一位 **1000001**；第二组末位 1000001、前一位 **1000002**。
**两组都不满足**「basis = 末位阈值 + 1」，改后注释里逐字写的
「closing 1000001 after 1000001 in one and 1000001 after 1000002 in the other」**完全属实**。
「is refused rather than read」亦属实（探针实见 `monster_hell` 抛）。
新句改用「Every group read through this type」限定，这在结构上是恒真的——两个读取器
（`ReadLadders` 靠该不变式反推组宽、`ReadLadder` 显式校验）都只放行满足它的组，
所以旧句那个「every group in this archive」的全称错误被彻底消掉而不是换了个说法。**第 3 件到位。**

#### 第 4 件 —— `DropRateTables.ClearReward`（`DropRateContent.cs:206-211`）

探针逐文件收开启标签（`[/…]` 不计）：clearreward **16**、monseter 12、monseter_extra 8、
object 7、monster_hell 5、common 1、(r)control 5。

- 翻牌表 vs **家族其余六个文件的并集**：共有 **7**、别处不声明 **9**，且七个共有名与九个独有名
  与判据 `:545-579` 逐名列出的两张表**逐字相同**。
- 翻牌表 vs 怪物表单独比：16 vs 12，共有 7、只在翻牌 9、只在怪物 5
  （`[condition rate]`、`[first boss/named mob hunting]`、`[gold quantity]`、`[gold volume]`、
  `[monster type drop bonusrate]`）。**「Same sections」在任何读法下都不成立，删得对。**

改后文本的每一项（16/7/9、其中一个声明得不一样、故由 `IClearRewardRateSource` 读）
都由 round 7 的 `Seven_of_this_files_sixteen_sections_...` 与 `A_sibling_band_...` 钉住。
QR-5 非阻塞观察 2 指出的「`see cref` 把读者引向一句不准确的话」随之消解。**第 4 件到位。**

#### 第 5 件 —— Contract Amendment 3 的就地订正

比对现文与 QR-5 第三节引用的原文：被改的**只有**「改后的文本每一项都由 round 7 新写的判据
钉住（家族七个文件、…）」这一句，现改为「后两项由 round 7 判据钉住；『家族七个文件』当时
没有任何判据钉住……直到 round 8 补上文件表判据才有出处」，并挂了一条
「（本句于 round 8 就地订正；Amendment 3 的裁定、理由与扩界范围均不变。）」的说明。

核过未被改动的部分：Amendment 3 的**触发、位置引文、「两处错」的认定、控制器裁定
（批准扩界）、理由段、扩界范围（严格限于这一段注释，不动可执行代码与判据期望值）、
Permitted files 增补句**——一字未动；**QR-5 第三节仍逐字保留着原句的引用**，
所以旧文本在记录里没有消失，订正可被后来者审计。Amendment 1/2、QR-1–QR-5、round 1–7
各节均未被改写。**第 5 件到位。**

### 二、round 7 + round 8 合计八条判据，钉不钉得住

判断标准不是「有没有跑绿」，而是「归档变了或实现错了会不会红」。逐条：

| 判据 | 断言从哪来 | 归档/实现漂移时会红吗 |
|---|---|---|
| `The_grade_bands_share_the_monster_tables_below_column_and_differ_above_at_80_to_89` | 两表**分别经两个真读取器**读出后逐级比 200 行 | **会**。若本表改借怪物表品级带，`aboveDiffers` 变空，`Assert.Equal(Range(80,10), …)` 当场红；末两句 `Distinct()` 集合断言（本表 `{3}`、怪物表 `{3,4}`）是「窗口外确无差异」的正面证据而非抽样 |
| `Three_siblings_declare_drop_prob_and_the_abyss_table_a_difficulty_keyed_one` | `OpeningTags` 直读六个同族文件的标签 | **会**。用**整列表相等**而非逐个 `Contains`，且 `Siblings` 含 common/control，「第四个文件开始声明」与「common/control 也声明」两种漂移都红 |
| `Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else` | 同上 | **会**。先钉 `own.Count == 16`，再把共有 7 名与独有 9 名**逐名**按 Ordinal 排序整表比对——只钉 7/9 两个数挡不住标签在两集合间移动，这里钉了名字 |
| `A_sibling_band_is_two_bounds_and_five_rates_where_a_band_here_is_two_and_one` | 三张同族**各用自己的 `[drop prob count]` 除自己的整数数**恢复宽度，再经 `PvfDropRateTableSource` 钉 `Rates.Count == 5` | **会**。`Assert.Equal([bands], Numbers(file, BandCountTag))` 与 `Assert.Equal(numbers, Numbers(file, DropProbTag).Count)` 把 10/70、7/49、4/28 三对字面量绑到归档 |
| `One_rarity_group_here_four_in_three_siblings_and_a_counted_two_in_the_abyss_table` | 直读四个文件的稀有度段 | **会**。深渊表「首值 2 恰等于 `(count−1)/7`」是**可判定的形状**而非形容词；本表另钉 `ladder.Values` 与档案七值逐值相同，多吞少吞一个数当场红 |
| `The_closing_invariant_holds_here_and_in_the_monster_table_but_in_no_abyss_group` | 整体转录深渊表十五值 + 两组 `Assert.False` + 本表与怪物表四组 `Assert.Equal` | **会**。正反两面都在：「不变式失效」与「不变式变成全家族成立」两种漂移都被抓 |
| `The_family_is_the_seven_files_the_archives_own_file_table_holds`（round 8 新增） | 走整张文件表 | **会**。逐名比七个路径，多一个、少一个、换一个都红；再钉 `Siblings` 六项 = 七个减本表，手抄数组第一次真的「数出来」了 |
| `The_sibling_reader_refuses_this_file_over_a_section_it_read_correctly`（round 8 新增） | 真调 `Load` + 消息子串 + 从归档数出的算术 | **会**。`Assert.Throws` 在不抛时红；`Contains("16 rates")`、`Contains("[dungeon difficulty drop bonusrate]")`、`DoesNotContain("[drop prob]")` 三面钉住「抛在哪、点名的不是错处」；`Assert.Equal([4], declared)` / `Equal(18, numbers/4)` / `Equal(5, 难度段数)` 把 72、4、18、16、5 全绑到归档 |

**八条全部真的依赖归档读数，没有一条即使归档变了或实现错了也照样绿。**

两处冗余（非缺陷，不要求整改，与 QR-5 结论一致）：判据文件里
`Assert.Equal(SiblingBandRateCount, SiblingBandWidth - ClearRewardBandLevelBoundCount)`
与两处 `Assert.Equal(0, values.Count % RarityGroupWidth)` 是常量恒等式，钉不住东西，
但同一段里紧邻的句子已把同样的事实绑到归档，属重复而非空转。

一处已被作者自己写明的边界：`FamilyNeedle = "itemdropinfo"` 是按**路径含家族名**来数的，
若归档里存在一个不含该子串的族内文件，这条判据数不到它。判据自己的 remarks
已写明「there is no index of the family anywhere in the file to read instead」——
如实交代的限制，不是隐瞒。

### 三、漏网的无出处断言：五个文件全扫，未发现一条站不住的

扫描口径：凡带具体数字、带全称词（every / all / only / never / always / no other /
nowhere / exactly）、或做跨文件比较的句子。逐句自算的结果：

- **`DropRateContent.cs` 本次改动的三段**：七个文件 ✓、三张同形具名 ✓、
  「common 与 control 完全不声明 `[drop prob]`」✓（探针：common 只有
  `[gold drop ref table]`，control 只有五个 `[control of …]`；且由 `Siblings` 的列表相等断言钉着）、
  深渊表两组的收尾数值 ✓、16/7/9 ✓、`[drop prob]` 是具名档案 ✓。
- **`ClearRewardContent.cs`**：`:31`「三 + 第四张深渊表」✓；`:37-38`「六名里三个两词」✓
  （探针：`pcroom default` / `pcroom bonus` / `pcroom event`）；`:63-64`「event2 四率与 event
  全同」✓（两者皆 `9000 9000 9000 9000`）；`:78`「六行同一 item id」✓（六行皆 10002120）；
  `:92-95` 16/7/9 与「9 > 7 所以多数是自己的」✓；`:97-100` 四段不读、三段未查明一段不适用 ✓；
  `:115-116`「`[drop kind prob]` 别的族内文件都不声明」✓（在独有九名之列）；
  `:121-123`「四个位置与四个物品槽只有数目相同这一条支撑」✓（如实标注，未冒充结论）。
- **`PvfClearRewardRateSource.cs`**：类型 remarks 见第一节，全部属实；
  `:112-113` `[item drop rarity control]` = `2 13 17 0 -5` ✓ 且 `-5` 是全文件唯一负数 ✓；
  `:130-131` `[dungeon difficulty gold drop bonusrate]` = `1.02 1.38 1.6 1.9 2` ✓；
  `:285-295` 稀有度组「本表一组七个 / 三张各四组 / 深渊表前缀二」✓ 且「why 未查明」如实标注；
  `:333-344` 品级带「below 列处处相同、above 恰差十级 80–89、本表三怪物表四」✓、
  「200 行里最小 `level − below` 为 1，首行 `1 0 3`，所以钳位从不触发」✓（探针实测两表皆如此）。
- **`PvfClearRewardRateSourceTests.cs`**：期望值全部与我的 dump 逐值相同——
  六档案 × 四段率（`default`/`pcroom default` 皆 10000；`event`/`event2`/`pcroom bonus`
  皆 9000；`pcroom event` 为 9000/8000/8000/8000）与四个等级界（1-15/16-30/31-40/41-200）✓；
  难度 `0 600 1000 1400 1200` ✓；地图数 1→2000…5→6000 且 6 无值 ✓；
  付费成本表 400 个整数、首 `(1,100)`、末 `(200,23980)`、**200 级逐级校验
  `100+(level−1)*120` 零违例** ✓；队伍 `1 1.2 1.4 1.6` ✓；兜底表 ✓；深渊表十五值 ✓。
- **`InfrastructureModule.cs` 本次改动处**（3 行）：注释只说「同族的另一个文件，它的 band 是
  具名档案而不是并行率，所以另给一个读取器」——**不含任何数字、序数或家族计数**，
  正是 round 6 整改后的写法，无可挑之处。

**没有发现第十八处。** 这与前五轮不同，但结论是自算出来的，不是让步。

### 未构成阻塞的观察（不要求整改，留给最终评审 / TASK-02）

1. **`ClearRewardContent.cs:53`「only `Default` has a caller today」**：实测六个常量今天
   **都没有**产品侧调用者（`grep` 全仓仅判据文件引用）。属前瞻性措辞、且是仓内可 grep 的事实
   而非 Amendment 2 所辖的跨文件归档断言。与 QR-5 观察 5 同，本轮仍不阻塞。
2. **`PvfClearRewardRateSource.cs:419-420`「Only the closing `[/…]` markers repeat in these
   files」**：实测七个族内文件里**任何标签都不重复**（clearreward 23 个 identifier =
   16 开启 + 7 关闭，各出现一次）。按「关闭标记是唯一会回声一个已见段名的东西」读，句子成立；
   按「关闭标记会重复出现」读则不成立。属措辞歧义。该句真正承重的后半
   「those carry no values」**七个文件全部实测为真**（关闭标记后无一个 value token），
   `ReadSections` 的写法因而安全。不阻塞。
3. **`DropRateContent.cs:113`「share this shape **exactly**」**：`object` 不声明
   `[party member drop bonusrate]`（探针：它只有 7 个标签），其 `PartyMemberRates` 读出为空。
   若「this shape」指紧随其后那句所界定的 `[drop prob]` 形状则为真且已被钉住；
   若指整条 `DropRateTable` 记录则「exactly」偏强。与 QR-5 观察 3 同，属措辞精度，不阻塞。
4. **Amendment 3 理由段那句「照它说的，翻牌表可以用 `PvfDropRateTableSource` 读」**：
   QR-5 第四节曾建议照实测改写。它是对**基线错误注释所暗示之事**的转述（有「照它说的」限定），
   且紧随其后的括注已把危害落到品级带那个**确实**静默出错的例子上，故不自相矛盾；
   又属任务文件叙述而非代码注释，不在 Amendment 2 的字面辖域内。QR-5 亦未将其列入
   两条 changes_required。本轮不要求整改。
5. QR-4 记的四条观察（`:118` 的「every neighbouring rate」、判据的过头论证、
   描述不发生的事、「eleven numeric members」只查了三个成员）本轮复核仍然如此，
   未回退也未新增，与 QR-4/QR-5 结论一致，不重复要求。

### 四、TASK-01 能否收工——逐条对密封契约与三条 Amendment

| 验收条件 | 判断 | 依据（均为本轮自验） |
|---|---|---|
| 1. 基础率 / 难度加成 / 地图数加成 / 付费成本表与实测归档逐值相同 | **满足** | 探针 dump 后逐值比过，见第三节；成本表 200 级零违例 |
| 2. 稀有度阶梯复用既有 `RarityLadder`，不重新建模 | **满足** | `ClearRewardTable.Rarity` 即 `RarityLadder?`；新增文件里无第二个阶梯类型 |
| 3. 不使用的三段被具名列出并写明为何不读 | **满足** | `UnreadSections` 四个常量（三段未查明 + 一段不适用），判据拿它们比对档案自己的 identifier 集合 |
| 4. 每个被读段落都有标签常量与出处注释 | **满足** | 12 个 `private const string` 段落常量；`ClearRewardTable` 每个 `param` 注明来源段 |
| 5. 全量判据 ≥ 1690 通过 / 0 失败 / 2 跳过 | **满足** | 本轮自跑 **1710 / 0 / 2**，两条跳过正是「因为档案在才跳过」的那两条 |
| A1. 品级带读自翻牌表自身，不借用同族 | **满足** | `ReadGradeBands` 读本文件；`The_grade_bands_...` 判据钉住借用会在 80–89 级多开一档 |
| A2. 跨文件断言必须由判据钉住，钉不住就删 | **满足** | 八条判据覆盖全部现存跨文件断言（见第二节）；QR-5 的发现 1（假断言）已删并改写、发现 2（无出处的「seven」）已由文件表判据补上出处 |
| A3. 扩界严格限于 `DropRateContent.cs` 的注释 | **满足** | `git diff` 实见三段改动全在 XML doc 内，无可执行代码、无判据期望值变动 |
| Controller Disposition（`PvfDropRateTableSourceTests` 的「the other four」为误报） | **成立** | 我另行核过：全家族七个文件里声明 `[basis of rarity dicision]` 的**恰有五个**（monseter、monseter_extra、monster_hell、object、clearreward），深渊表之外正是四个，原文成立，不扩界是对的 |

**全部满足。TASK-01 可以收工。**

### 备注

- 本轮只读、只核、只追加：未修改任何产品代码、判据、既往轮次或任务控制状态。
  反射探针与其构建产物只写在 scratchpad，未进入仓库；`git status` 在评审前后完全一致。
- 关于「第六轮该不该挑出东西」：前五轮各有实质发现，本轮没有，不是因为放松了口径。
  round 8 的五件整改我逐件重测，八条判据我逐条判过会不会红，五个文件的断言我逐句自算。
  **剩下的四条观察全是措辞精度，没有一条是站不住的事实断言。**
  这个 Task 的代码正确性已被多轮独立核对确认，注释与判据现在也对得上归档。干净了。
