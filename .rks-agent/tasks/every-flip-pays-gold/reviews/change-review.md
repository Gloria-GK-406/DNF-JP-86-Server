---
kind: rks-review
verdict: approved
---

# 独立评审：every-flip-pays-gold（change 范围）

评审对象：`git diff 927e2d0e`（24 个文件，586 插入 / 135 删除）+ 未跟踪的
`.rks-agent/tasks/every-flip-pays-gold/`。契约 revision 1，Spec RQ-001..010 / AC-001..010 /
EV-001..006。全部结论由本评审自行取证，未采信执行者的判断。

## 复核过的基线

- 全量判据（还原工作区之后重跑）：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
  → **1989 通过 / 0 失败 / 2 跳过**，与执行者所述一致。
- 档案出处，用 `PvfProbe` 亲自核对：
  - `etc/itemdropinfo_common.etc` `[gold drop ref table]`：`1 17 13`、`5 44 13`、`9 77 13` ✔
  - `etc/itemdropinfo_clearreward.etc` `[dungeon difficulty gold drop bonusrate]`：
    `1.02 1.38 1.6 1.9 2` ✔；`[dungeon difficulty drop bonusrate]` 为 `0 600 1000 1400 1200`，
    同为五项，`DifficultyGoldRates.Count == DifficultyRates.Count` 的断言成立 ✔
- **没有凭空因子进入公式**：`git diff` 里 `175` 只出现在两段解释「为什么不采用」的散文中
  （`ClearRewardRules.cs:432`、`ClearRewardArchiveTests.cs` 的 remarks），
  `CardGoldOf` 的形参只有基数、难度倍率、波动百分比、生成器；击杀数、评级加成、
  队伍人数除法一个都没进。这一条本任务做对了。
- 波动区间对称：`DropRandom.Next(n)` 返回 **1..n（含）**，故
  `Next(2p+1) - 1 - p ∈ [-p, +p]`，无 off-by-one。
- 分层：`IGoldDropTableSource` 定义在 `Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs:284`，
  `*Source` 后缀即 AGENTS.md 的「内容源」类别，`DropRuleService` 早已以同样方式注入它；
  它只被规则服务读，不读世界状态。`The_rule_service_is_handed_no_reader_of_world_state`
  的反射断言已相应更新为四个内容源。**符合 AGENTS.md**。
- RQ-009：既有判据的改动只有「构造函数多一个内容源」与「`GoldDropTable` 多一个字典参数」，
  出货概率、品级带、副本定价、房间数上限、掉落池选取的断言与期望值一个未动。唯一被删的断言是
  `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfClearRewardRateSourceTests.cs:435`
  的 `Assert.Contains(...DifficultyGoldBonusRate, tags)`——该节现在确实被读了，删除是必然结果，
  不构成违反。
- 探针残留：无。diff 中无新增 `Console.` / `Debug.` / `TODO` / `Sleep`。
  `GameProtocol.ClearRewardCardGold` 已删且全仓无引用。
- 协议记录：`protocol/game/server-to-client/NOTI-0x0023.md` 描述的是线格布局，本次只改值不改结构，
  无需同步。

## 变异复核（自选 6 条，均在备份+sha256 校验下进行，工作区已逐字节还原）

| # | 变异 | 结果 |
|---|---|---|
| A | `DungeonClearSlice.cs:626` `settled?.Cards.Free.Gold ?? 0` → `0` | **全绿 1989/0/2** ❌ |
| B | `PvfDropRateTableSource.cs:90` 第三列 → 写死 `13` | **全绿 1989/0/2** ❌ |
| C | `ClearRewardRules.cs:454` 丢掉难度倍率 | 红，Application.Tests 5 条失败 ✔ |
| D | `ClearRewardRules.cs:457` 波动只往一个方向 | 红，Application.Tests 1 条失败 ✔ |
| E | `SelectCardCommand.cs:232` 只在认领到装备时入账 | 红，Protocol.Tests 1 条失败 ✔ |
| F | `ClearRewardRuleService.cs:345,347` 基数改按 `CharacterLevel` 取 | **全绿 1989/0/2** ❌ |

红的三条都是断言失败，不是编译失败。绿的三条即下面的三条阻塞项。

还原后 sha256 与备份逐一相同，`git status --porcelain` 与评审开始时完全一致。

## Blocking Findings

### BF-1｜结算 → 帧 这一段没有任何判据，RQ-007 / AC-007 / EV-002 空过

- 变异 A：`Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs:626`
  把 `var cardGold = settled?.Cards.Free.Gold ?? 0;` 改成 `var cardGold = 0;`，
  全量判据 **1989 通过 / 0 失败**，一条不红。
- 原因：`Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/DungeonClearCodecTests.cs:203,285`
  把字面量 `273` 直接喂给 `EncodeClearDungeonReward`，证明的只是「编码器写回它被交的那个数」；
  而 `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/SetPlayResultHandlerTests.cs:537`
  定义了 `FaceGold = 37` 并把它放进两个桩牌面（:549、:561），却**从头到尾没有断言过它**
  （`grep -rn FaceGold Server/tests` 在该文件内零个 `Assert`）。
  于是「规则层产出的数进到帧里」这一步在全仓没有任何判据。
- 这正是上一课题的同一形状：真机会显示 0 金币，而 1989 条判据全绿。契约 SC-5 与 Spec EV-002
  要求的「按客户端读法解码后断言」目前只覆盖了编码器，没有覆盖数据来源。
- 要求：在 `SetPlayResultHandlerTests` 里对**处理器实际发出的**
  `ClearDungeonRewardNotification` 走 `CardPanelFrameReader.ReadClearDungeonReward`，
  断言 `FreeRow.Columns[0].Gold == FaceGold`；`FaceGold` 取一个与 `PaidFlipCost`、
  `ClearBase`、`RankBonus` 都不同的值，避免串位也能过。

### BF-2｜基数取错等级（角色等级 vs 副本基准等级）在判据下完全不可见，RQ-002 / AC-002 未被检验

- 变异 F：`Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs:345` 与 `:347`
  的 `occasion.DungeonBasisLevel` 改成 `occasion.CharacterLevel`，全量判据
  **1989 通过 / 0 失败**。
- 原因：`Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs:1269-1282`
  的 `ClearRewardFixture.Occasion()` 默认 `dungeonBasisLevel ?? level`，两者恒等（都是
  `EndgameLevel` = 86）；新增的 5 条规则服务判据（:1032、:1045、:1067、:1096、:1130）
  全部用默认 `Occasion()`，因此无法区分这两个等级。
  `ClearRewardFixture.GoldTable`（:1328）也只声明了一个等级的基数，本身就无从区分。
- 后果不是理论的：档案里等级 5 基数 44、等级 86 基数 1357，而副本 144 的 `[basis level]` 是 5
  ——读错等级会让 86 级角色在该副本拿到约 **30 倍** 的金币，屏幕上仍是个「随局变化的正数」，
  实机观察（SC-9）也看不出来。这与上一个 Spec 刚修掉的「品级带按角色等级取」是同一 bug 家族，
  `ClearRewardRuleService.cs:186-188` 的 remarks 自己记着那次教训。
- Spec 的 AC-002 本来就写了「两个基准等级不同、难度相同的副本」，但没有任何判据实现它。
- 要求：加一条 `Occasion(level: 86, dungeonBasisLevel: 5)` 之类两级分离、且 gold 表两级基数
  不同的用例，使变异 F 必红。

### BF-3｜第三列的档案读法未被钉住，且源码对该列的档案陈述是错的，RQ-010 / AC-010 / EV-005 未满足

- 变异 B：`Server/src/Dfo.Server.Infrastructure/Content/PvfDropRateTableSource.cs:90`
  的 `variances[baseValues[at]] = baseValues[at + 2];` 改成 `= 13;`，全量判据
  **1989 通过 / 0 失败**。
- 原因：`Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs:789-791`
  只断言等级 1 / 5 / 9 的波动百分比，而这三级在档案里**恰好都是 13**；:796 断言等级 0 为 0，
  在常量变异下同样为 0。于是「读第三列」与「写死 13」在判据下不可分辨——三个因子里第三个的
  档案出处实际上没有被证据支持。该用例的 remarks 自称「读错列不可能通过」，读错列确实会红，
  但**返回常量**不会，而常量正是本仓吃过亏的那一族。
- 更要紧的是档案事实本身。我用
  `PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe/PvfProbe.csproj -- dump etc/itemdropinfo_common.etc 1200`
  核过：`[gold drop ref table]` 第三列**不是全档案 13**，它分三段——
  等级 1–64 为 `13`，等级 65–77 为 `11`，等级 78–200 为 `9`
  （`64 966 13`、`65 977 11`、`77 1177 11`、`78 1196 9`、`86 1357 9`、`200 4877 9`）。
  而 `Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs:241` 写的是
  「A21 declares `13` on every one of them」——**一条错误的档案陈述**。
  （TaskContract 的 Current Situation 表格里「第三列全档案都是 13」同错，但契约不在本次可改范围，
  以代码注释为准即可。）
- 实现读的是真值，所以**运行时行为正确**；问题在于本任务唯一的硬约束就是「每个因子指得出档案
  出处，且读出的值与档案一致」，而这处注释是将来读者判断该列含义的唯一依据。
- 要求：（a）改正 `DropRateContent.cs:241` 的陈述为分段事实；
  （b）把 `ClearRewardArchiveTests` 的波动断言换成跨段的等级，例如
  `VarianceFor(64) == 13`、`VarianceFor(65) == 11`、`VarianceFor(86) == 9`，
  使常量读法必红。

### BF-4｜XML doc 块整体错位，把既有成员的文档接到了新成员上

AGENTS.md 明文把「XML doc remarks 说明来源」定为本仓记录逆向/归档出处的方式，本任务又以
「因子出处写在规则所在之处」为交付物之一，所以这不是排版问题。

- `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`：新增的 `CardGoldOf`（:446）
  被插在 `ChanceOf`（:465）的文档块与 `ChanceOf` 本身之间。结果是
  `CardGoldOf` 顶着**两个 `<summary>`**（前面 `ChanceOf` 的那个，加自己在 :417 的那个）
  和**五个不属于它的 `<param>`**（`mapBaseRate` / `difficultyBonus` / `partyRate` /
  `threshold` / `basis`，:405-415）；而 public 的 `ChanceOf` 一句文档都不剩，
  包括那段解释「为什么用整数而非实数比较」的关键论证。
- 同一形状还有三处：
  - `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs:1321`：
    `GoldBase` 顶着原属 `Table`（:1348）的 remarks；:1326-1327 `GoldTable` 顶着两个
    `<summary>`，其中 :1326「Hands out one gold table」本是 `StubGoldTables` 的，
    而 `StubGoldTables`（:1335）自己一句文档没有。
  - `Server/tests/Dfo.Server.Application.Tests/World/DungeonRunClearRewardCardTests.cs:46-48`：
    `Face` 方法的 summary 落到了 `FaceGold` 常量上。
  - `Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs:135-139`：
    `FaceGold` 顶着原属 `OffersPaidFlipAsync` 的那段 remarks。
- 编译不会报：`Server/Directory.Build.props` 虽然开了 `TreatWarningsAsErrors`，
  但全仓没有 `GenerateDocumentationFile`，XML doc 诊断（CS1571/CS1572）根本不产生，
  所以只能靠评审发现。
- 要求：把四处文档块各归各位。

## Non-Blocking Observations

- **付费（黄金）牌的金额被顺带改了。**
  `Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs:232` 的
  `fixedCards.FaceOf(command.Kind).Gold` 对 `Paid` 同样成立，而
  `ClearRewardRuleService.FaceFor` 算金币时不按 `Kind` 分支
  （`ClearRewardRuleService.cs:203` 在 `Kind` 判断之前），于是黄金牌翻牌从
  「有装备给 1」变成了「一律给档案金额」。契约 Non-Goals 是「不实现付费牌与黑砖牌的金币」，
  本次并未新建模型、只是顺带覆盖，但这确实改动了范围外的可观测行为，且没有一条判据或注释
  说明这是有意的。建议要么在该处就地具名，要么把金币限定在 Free 面。
- **AC-003（五个难度单调不减）没有对应判据。**
  `ClearRewardFixture.Table()` 的 `DifficultyGoldRates` 是
  `[DifficultyGoldRate, DifficultyGoldRate]`（`ClearRewardRuleServiceTests.cs:1366`），
  两项同值，规则服务层测不出难度序；档案层只断言了那五个数本身。
  实质覆盖由 `ClearRewardRulesTests` 的逐值 Theory（:490-496）承担，故不阻塞，
  但 Spec 列了这条场景而未实现。
- **注释过期**：`Server/src/Dfo.Server.Infrastructure/Content/PvfDropRateTableSource.cs:38`
  的 `GoldBaseRowWidth` 仍写「Level, amount, and **a third number this reading has not
  identified**」——本次已经识别并读取了第三列。
- **两个生成器同种子**：`ClearRewardRuleService.cs:203` 与 `:217` 都用
  `new DropRandom(seed)`。确定性与「金币不挪动牌面抽取」都成立
  （变异 D 之外，`What_the_gold_spends_of_the_generator_never_reaches_the_positions` 也钉住了），
  但两条序列完全相同，金币值与第 0 格抽取由同一批随机数派生、存在相关性。目前无人依赖独立性，
  记录备考。
- 若干处多出空行（`PvfClearRewardRateSource.cs` 的 `UnreadSections` 结尾、
  `ClearRewardArchiveTests` / `ClearRewardRulesTests` / `ClearRewardRuleServiceTests` 的类尾）。

## RQ 逐条覆盖判定

| RQ | 判定 | 依据 |
|---|---|---|
| RQ-001 恒大于零 | 满足 | `A_settled_face_carries_its_gold_whether_or_not_it_drew_anything` 两分支 + `An_amount_that_would_round_away_still_pays_one`；「档案无此等级基数 / 难度未列」返回 0 是 Spec 具名的例外 |
| RQ-002 以副本基准等级基数为底 | **不满足** | BF-2，变异 F 全绿 |
| RQ-003 按难度倍率缩放 | 满足 | 变异 C 红 5 条；AC-003 的五难度单调未实现（观察项） |
| RQ-004 波动不越界 | 满足 | 变异 D 红；`Next` 语义核对后区间对称 |
| RQ-005 双向可达 | 满足 | `The_variance_moves_the_amount_both_ways_and_stays_inside_the_band` 断言两侧均出现 |
| RQ-006 同局不变 | 满足 | `The_same_clear_settles_the_same_gold_every_time`（含「不同种子给不同值」的反向保护）+ 领域层牌面一次定死 |
| RQ-007 帧条目 0 携带该金额 | **不满足** | BF-1，变异 A 全绿 |
| RQ-008 一律入账 | 满足 | 变异 E 红；`A_flip_pays_the_cards_gold_whether_or_not_it_held_anything` 两分支 |
| RQ-009 其余取值逐值不变 | 满足 | diff 审阅，无断言/期望值改动 |
| RQ-010 每个因子有档案出处 | **部分不满足** | 基数与难度倍率满足（已亲自核档）；第三列见 BF-3 |

## 结论

公式本身是对的、三个因子都真的来自档案、参照服务端那四个无出处因子一个也没抄进来——本任务
最担心的第一个失败模式没有发生。但第二个失败模式发生了：三条关键链路
（结算→帧、基数取哪个等级、第三列怎么读）在 1989 条全绿的判据下都可以被随意改坏而无人报警，
其中两条会直接产生玩家可见的错数。加上四处 XML doc 错位使「因子出处写在规则所在之处」
这件交付物本身受损，且其中一处（第三列全档案 13）是**错误的档案陈述**。

裁决 `changes_required`：BF-1..BF-4 逐条解决后需重新独立评审。

---

# 复审（第二轮）：every-flip-pays-gold（change 范围）

评审对象：整改后的 `git diff 927e2d0e`（24 个文件，**700 插入 / 135 删除**）+ 未跟踪的
`.rks-agent/tasks/every-flip-pays-gold/`。契约 **revision 2**，Spec 已同步到 revision 2。
Profile 为 `sdd-lite`，未配置对抗挑战集，故无 `Challenge Dispositions` 一节。
本轮结论全部由复审者自行取证；上一轮的四条阻塞项与执行者的整改描述均未采信，逐条重跑。

## 本轮复核过的基线

- 全量判据（整改后原样跑）：
  `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
  → **1992 通过 / 0 失败 / 2 跳过**（52+488+60+248+4+215+925）。与执行者所述一致，
  相对上一轮的 1989 恰好 +3，对应三条新增用例
  （`TheGoldTheSettlementFixedReachesTheCard`、`The_golds_base_is_taken_at_the_dungeons_level_not_the_characters`、
  `The_floor_belongs_to_the_free_card_and_not_to_the_paid_one`）。
- 档案事实，本轮自行用 `PvfProbe` 重新核对
  （`PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe/PvfProbe.csproj -- dump etc/itemdropinfo_common.etc 2000`）：
  `[gold drop ref table]` 第三列确实分三段 —— `1 17 13` … `64 966 13`、`65 977 11` … `77 1177 11`、
  `78 1196 9` … `86 1357 9` … `200 4877 9`。**契约 revision 2 与
  `DropRateContent.cs` 新写的「1–64 为 13、65–77 为 11、78–200 为 9」属实。**
  `etc/itemdropinfo_clearreward.etc` 的 `[dungeon difficulty gold drop bonusrate]` = `1.02 1.38 1.6 1.9 2`，
  `[dungeon difficulty drop bonusrate]` = `0 600 1000 1400 1200`，同为五项 ✔
- 既有规则层取值：`git diff 927e2d0e -- Server/tests/` 中被删除的断言只有
  gold 相关的六条与 `PvfClearRewardRateSourceTests.cs` 那条 `UnreadSections.DifficultyGoldBonusRate`
  （该节现已被读，删除是必然结果）。出货概率、品级带、副本定价、房间数、掉落池选取的期望值一个未动。
  **RQ-009 / SC-7 满足。**
- 还原校验：本轮所有变异均在 `sha256` 备份下进行，未使用 `git checkout --`。
  变异结束后六个被改文件的 sha256 与备份逐一相同，`git status --porcelain` 与复审开始时逐字节一致，
  `git diff 927e2d0e --stat` 仍为 `24 files changed, 700 insertions(+), 135 deletions(-)`，
  还原后重跑全量判据仍为 **1992 / 0 / 2**。

## 上一轮四条阻塞项的逐条核实

| 上一轮 | 本轮判定 | 证据 |
|---|---|---|
| BF-1 结算→帧无判据 | **已解决** | 见下表变异 A2 / A |
| BF-2 基数取错等级不可见 | **已解决** | 变异 C1 / C2 |
| BF-3 第三列读法未钉住 + 错误档案陈述 | **判据已解决；陈述只改对一处** | 变异 B 红；但 `PvfDropRateTableSource.cs:38` 的错误陈述仍在，见 BF-7 |
| BF-4 XML doc 块错位（四处） | **只改了一处，三处仍在** | 见 BF-5，逐处行号已核 |

## 本轮变异复核（10 条，全部在备份 + sha256 下进行）

| # | 变异 | 结果 | 谁抓住 |
|---|---|---|---|
| A2 | `DungeonClearSlice.cs:626` `settled?.Cards.Free.Gold ?? 0` → `0`（上一轮 BF-1 原样） | **红 1 条** ✔ | `SetPlayResultHandlerTests.TheGoldTheSettlementFixedReachesTheCard` |
| A | `DungeonClearSlice.cs:354` `writer.Write(cardGold);` → `writer.Write(0);` | **红 5 条** ✔ | 上者 + `DungeonClearCodecTests` 四条 |
| B | `PvfDropRateTableSource.cs:90` `baseValues[at + 2]` → `13` | **红 1 条** ✔ | `ClearRewardArchiveTests.The_archive_declares_the_numbers_a_cards_gold_is_built_from` |
| C1 | `ClearRewardRuleService.cs:352` `occasion.DungeonBasisLevel` → `occasion.CharacterLevel`（基数） | **红 1 条** ✔ | `The_golds_base_is_taken_at_the_dungeons_level_not_the_characters` |
| C2 | `ClearRewardRuleService.cs:354` 同上（波动） | **红 1 条** ✔ | 同上 |
| D1 | `ClearRewardRuleService.cs:344` `is not ClearRewardCardKind.Free` → `is ...Free`（反转） | **红 6 条** ✔ | 规则服务五条 + `The_floor_belongs_to_the_free_card_and_not_to_the_paid_one` |
| D2 | 同处 → 常量 `false`（直接删守卫） | 编译失败，**不计红** | —— |
| D3 | 同处 → `is not (Free or Paid)`（付费牌也拿底薪，D2 的等价可编译写法） | **红 1 条** ✔ | `The_floor_belongs_to_the_free_card_and_not_to_the_paid_one` |
| E | `ClearRewardRules.cs:478` `... ) - 1 - variancePercent` → `... ) - variancePercent`（波动区间整体偏移 1） | **红 2 条** ✔ | `The_variance_moves_the_amount_both_ways_and_stays_inside_the_band`、`The_faces_gold_is_the_levels_base_scaled_by_the_difficulty` |
| **F** | `ClearRewardContent.cs:287` `DifficultyGoldRates[difficulty]` → `DifficultyGoldRates[0]` | **全绿 1992 / 0 / 2** ❌ | 无 —— 见 BF-6 |
| G | （对照）`ClearRewardContent.cs:273` `DifficultyRates[difficulty]` → `DifficultyRates[0]` | 红 30 条 ✔ | 既有难度取值被牢牢钉住 |

红的各条都是断言失败而非编译失败（除 D2 已标注外，各 log 中 `error CS` 计数为 0）。

## Blocking Findings

### BF-5｜上一轮 BF-4 的四处 XML doc 错位只修好一处，三处原样保留

AGENTS.md:400 明文把「逆向出来的 legacy 行为在代码里用 XML doc remarks 说明来源」定为本仓记录
档案出处的方式，本任务的交付物之一又是「三个因子的出处写在规则所在之处」，所以这不是排版问题。
上一轮 BF-4 明确要求「把**四处**文档块各归各位」，实际只动了第一处。

- ✔ 已修：`Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs` ——
  `ChanceOf`（:417）重新拿回自己的 summary / remarks / 五个 `<param>`，
  `CardGoldOf`（:469）移到它之后且只带自己的文档。本处成立。
- ❌ 仍在：`Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs`
  - :1367-1379 是 `Table()` 的 summary + remarks（「A table whose second profile declares the
    full threshold…」），紧随其后的成员却是 **:1386 `public const int GoldBase = 200;`**，
    于是 `GoldBase` 顶着两个 `<summary>`，而真正的 `public static ClearRewardTable Table(`
    （**:1413**）一句文档不剩。
  - :1391 `/// <summary>Hands out one gold table, whatever path it is asked for.</summary>`
    本属 `StubGoldTables`，却与 :1392 自己的 summary 一并挂到了
    **:1393 `public static GoldDropTable GoldTable(`** 上；
    `public sealed class StubGoldTables`（**:1400**）无任何文档。
- ❌ 仍在：`Server/tests/Dfo.Server.Application.Tests/World/DungeonRunClearRewardCardTests.cs:46-50`
  —— :46 `/// <summary>A face written out by hand; null in a position means it holds nothing.</summary>`
  本属 `Face(`（:50），现在挂在 **:48 `private const int FaceGold = 37;`** 上，`Face` 无文档。
- ❌ 仍在：`Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs:128-141`
  —— :129-135 的 summary + remarks（「Every dungeon offers a flip here…」「A constant rather than a
  knob…」）本属 `OffersPaidFlipAsync`（:141），现在挂在 **:139 `private const int FaceGold = 37;`** 上，
  `OffersPaidFlipAsync` 无文档。

编译器抓不到这一族，本轮已实测：
`dotnet build Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo -p:GenerateDocumentationFile=true -p:NoWarn=1591 -p:TreatWarningsAsErrors=false`
只产出 292 条 CS1573 与 3 条 CS1574（绝大多数为基线既有），**零条 CS1571** ——
重复 `<summary>` 在 Roslyn 里没有诊断。所以这三处只能靠评审发现，本轮已逐处核到行号。

要求：把上述三处文档块各归各位（把 `FaceGold` / `GoldBase` / `GoldTable` 移到它们占住的
成员之后，或把被占的文档块移回它自己的成员之前）。

### BF-6｜「按本局难度取倍率」这一步没有任何判据，RQ-003 / AC-003 / SC-2 空过

- 变异 F：`Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs:287`
  的 `? this.DifficultyGoldRates[difficulty]` 改成 `? this.DifficultyGoldRates[0]`，
  全量判据 **1992 通过 / 0 失败 / 2 跳过**，一条不红。
- 对照变异 G：同一个 record 里紧邻的既有姊妹访问器
  `ClearRewardContent.cs:273` 的 `DifficultyRates[difficulty]` 做同样改动，**红 30 条**。
  既有的难度取值被牢牢钉住，本次新增的这一个一条判据都没有。
- 原因（`grep -rn DifficultyGoldRate Server/tests` 只有四处命中）：
  - `ClearRewardArchiveTests.cs:778-779` 只断言 `table.DifficultyGoldRates` 这个**列表**的内容与
    项数，**从不调用访问器 `DifficultyGoldRate(int)`**；
  - `ClearRewardRuleServiceTests.cs:1431` 的 fixture 把两项写成
    `[DifficultyGoldRate, DifficultyGoldRate]`（**两项同值 1.5**），
    而 `CertainDifficulty = 1`（:1242），于是 `[1]` 与 `[0]` 在规则服务层恒等；
  - `ClearRewardRulesTests` 的逐值 Theory 断言的是 `CardGoldOf(base, rate, …)` —— 它检验
    「拿到的倍率被正确地乘进去」，**不检验拿到的是不是本局难度那一项**。
    （上一轮把 AC-003 判为非阻塞，理由是「实质覆盖由这条 Theory 承担」；本轮实测该 Theory
    与这条链路无关，故上一轮那条理由不成立。）
- 后果不是理论的：档案的五个倍率是 `1.02 1.38 1.6 1.9 2`。访问器若恒取第一项，
  **五个难度一律按 1.02 结算**，最高难度少发近一半，而屏幕上仍然是一个随局变化的正数，
  实机观察（SC-9 / EV-006）同样看不出来 —— 与刚修好的 BF-2 是同一个失败形状。
- 契约 SC-2 写的是「按**该难度**的档案倍率」，Spec AC-003 写的是「同一副本的五个难度分别问规则服务，
  五个金额随难度倍率单调不减」。AC-003 至今没有任何判据实现。
- 要求：让变异 F 必红。最省的做法是把 `ClearRewardFixture.Table()`（`ClearRewardRuleServiceTests.cs:1431`）
  的 `DifficultyGoldRates` 改成两项不同值并加一条按难度逐值断言的用例，
  或按 AC-003 原文用档案的五个倍率跑一遍单调不减。

### BF-7｜`PvfDropRateTableSource.cs:38` 仍写着第三列「未被识别」，与它下面几行的代码直接矛盾

- `Server/src/Dfo.Server.Infrastructure/Content/PvfDropRateTableSource.cs:38`：
  `/// <summary>Level, amount, and a third number this reading has not identified.</summary>`
  而同文件 :88-91 现在正是 `variances[baseValues[at]] = baseValues[at + 2];` ——
  这份读法**已经**识别并读取了第三列。
- 上一轮把它列为非阻塞的「注释过期」；但本轮 BF-3 的整改把「改正对该列的档案陈述」当作交付物
  （`DropRateContent.cs:240-256` 已改对），却漏了同一件事在真正读它的那个文件里的另一半。
  留着的结果是全仓对第三列有**两条互相矛盾的陈述**，而离读取代码最近的那条是错的。
  本任务唯一的硬约束就是「每个因子指得出档案出处、且陈述与档案一致」，这一处正违反它。
- 要求：把 :38 的陈述改成事实（等级、基数、按等级分段的波动百分比），
  或直接指向 `GoldDropTable.VariancePercents` 的那段说明。

## Non-Blocking Observations

- **`ClearRewardCardFace` 新增 `<param name="Gold">` 引入两条 CS1573。**
  `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs:84`：该 record 原先没有任何
  `<param>`，只加一个之后 `Kind` 与 `Slots` 触发 CS1573。全仓 292 条同类告警、且未开
  `GenerateDocumentationFile`，属既有惯例，不阻塞；补齐两个 `<param>` 即可。
- **空行残留仍在**（上一轮已列，未处理）：`ClearRewardRules.cs:485-486` 连着两个空行；
  `ClearRewardArchiveTests.cs`、`ClearRewardRulesTests.cs`、`ClearRewardRuleServiceTests.cs`
  的类尾各多一个空行。
- **上一轮那条「付费牌被顺带改了」的观察项已闭合。**
  `SelectCardCommand.cs:232` 的 `fixedCards.FaceOf(command.Kind).Gold` 对 `Paid` 仍然成立，
  但 `ClearRewardRuleService.GoldOf`（:340-345）现在对非 Free 直接返回 0，
  所以黄金牌翻牌从「有装备给 1」变成「一律给 0」。契约 revision 2 已记录并给了理由、
  规则处有就地注释、`The_floor_belongs_to_the_free_card_and_not_to_the_paid_one` 钉住了它，
  变异 D1 / D3 均红。
- **两个生成器同种子**（`ClearRewardRuleService.cs:203` 与 `:217` 都是 `new DropRandom(seed)`）：
  确定性与「金币不挪动牌面抽取」都成立（`What_the_gold_spends_of_the_generator_never_reaches_the_positions`
  钉住），但两条序列完全相同，金币与第 0 格抽取存在相关性。目前无人依赖独立性，记录备考。
- **SC-9 / EV-006（实机屏幕观察）尚未取得。** 本轮只覆盖自动判据与档案核对；
  这一条按契约需要实机运行 + 使用者确认，属验收阶段，不在本次 change 评审的可判范围。

## RQ 逐条覆盖判定（本轮重判）

| RQ | 判定 | 依据 |
|---|---|---|
| RQ-001 恒大于零、付费牌不拿 | 满足 | `A_settled_face_carries_its_gold_whether_or_not_it_drew_anything`（两个 threshold 分支）、`An_amount_that_would_round_away_still_pays_one`、`The_floor_belongs_to_the_free_card_and_not_to_the_paid_one`；变异 D1 红 6 条、D3 红 1 条 |
| RQ-002 以副本基准等级基数为底 | **已由 BF-2 整改满足** | 新增 `The_golds_base_is_taken_at_the_dungeons_level_not_the_characters`：`Occasion(dungeonBasisLevel: 5)` 与角色 86 级分离，两级基数 44 / 1357、波动 0 / 50，50 个种子逐一断言定值；变异 C1、C2 各红 1 条 |
| RQ-003 按本局难度倍率缩放 | **不满足** | BF-6，变异 F 全绿；AC-003 无判据 |
| RQ-004 波动不越界且按等级分段 | 满足 | `ClearRewardArchiveTests` 现钉 `VarianceFor` 的 1 / 64 / 65 / 77 / 78 / 86 / 200 共七点、跨三段两界，并同时钉四个对应基数防串列；变异 B 红、变异 E 红 |
| RQ-005 双向可达 | 满足 | `The_variance_moves_the_amount_both_ways_and_stays_inside_the_band` 扫 200 个种子，两侧都断言出现 |
| RQ-006 同局不变 | 满足 | `The_same_clear_settles_the_same_gold_every_time`（含「不同种子给不同值」的反向保护）+ 领域层牌面一次定死 |
| RQ-007 帧条目 0 携带该金额 | **已由 BF-1 整改满足** | 新增 `TheGoldTheSettlementFixedReachesTheCard`：真编码 → `CardPanelFrameReader.ReadClearDungeonReward` → 断言 `FreeRow.Columns[0].Gold == 37`；`FaceGold=37` 与 `PaidFlipCost=4210`、`ClampedLanding=40` 各不相同；变异 A2 恰好只红这一条 |
| RQ-008 一律入账 | 满足 | `A_flip_pays_the_cards_gold_whether_or_not_it_held_anything` 两分支现无条件断言 `GoldPaid == FaceGold` 与账户总额 |
| RQ-009 其余取值逐值不变 | 满足 | diff 审阅：被删断言全部 gold 相关，其余仅构造函数多一个内容源 / `GoldDropTable` 多一个字典参数 |
| RQ-010 每个因子有档案出处 | **部分不满足** | 基数与波动百分比：已由 `ClearRewardArchiveTests` 钉住且经 PvfProbe 复核 ✔；难度倍率：值已钉住，但「取的是本局难度那一项」无判据（BF-6），且 `PvfDropRateTableSource.cs:38` 仍留一条错误陈述（BF-7） |

## 结论

上一轮的三条硬伤 —— 结算→帧的断链、基数取错等级、第三列读成常数 —— 本轮变异复核确认**都真的补住了**，
新增的三条判据不是空过的：A2 只红新用例，C1 / C2 只红新用例，B 只红被改写的档案用例。
契约 revision 2 对「第三列分段」与「底薪只属免费牌」的两处修正，经 PvfProbe 与判据核对**属实**，
且未使 Spec 或其他判据失配。公式仍只含三个有出处的因子，参照服务端那四个无出处因子一个未进。

但整改并不完整，且本轮新暴露了一条同族的空过判据：

1. BF-4 要求的四处 XML doc 归位只做了一处，三处原样保留 —— 两处让一个测试常量顶着别人的
   `<summary>`，被顶掉文档的成员一句不剩。
2. 「按本局难度取倍率」这一步在 1992 条全绿下可以被改成恒取第一项而无人报警，
   与刚修好的 BF-2 是同一个失败形状；同一个 record 里的姊妹访问器却被 30 条判据钉着，对照鲜明。
3. 第三列的错误档案陈述只改了两处中的一处，另一处就在读取它的代码上方几行。

裁决 `changes_required`：BF-5、BF-6、BF-7 逐条解决后需重新独立评审。

---

# 复审（第三轮）：every-flip-pays-gold（change 范围）

评审对象：整改后的 `git diff 927e2d0e`（24 个文件，**761 插入 / 136 删除**）+ 未跟踪的
`.rks-agent/tasks/every-flip-pays-gold/`。契约 revision 2，Spec RQ-001..010 / AC-001..010 / EV-001..006。
Profile 为 `sdd-lite`，未配置对抗挑战集，故无 `Challenge Dispositions` 一节。
本轮全部结论自行取证：前两轮的裁决、执行者的整改描述、以及「1993 全绿」这一现有证据都先当作**待核**，
逐条重跑。

## 本轮复核过的基线

- 全量判据（整改后原样跑）：
  `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
  → **1993 通过 / 0 失败 / 2 跳过**（52+488+60+248+4+215+926），`error CS` 计数 0。
  与现有证据所述一致，相对第二轮的 1992 净 +1。
- 档案事实，本轮用 `PvfProbe` 第三次独立核对：
  - `PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe/PvfProbe.csproj -- dump etc/itemdropinfo_common.etc 2200`
    → `[gold drop ref table]` 第三列确实分三段：`1 17 13` … `64 966 13`｜`65 977 11` … `77 1177 11`｜
    `78 1196 9` … `86 1357 9` … `200 4877 9`。**契约 revision 2、`DropRateContent.cs:240-256`
    与 `PvfDropRateTableSource.cs:39-42` 新写的分段陈述属实。**
  - `... dump etc/itemdropinfo_clearreward.etc 400` → `[dungeon difficulty gold drop bonusrate]`
    = `1.02 1.38 1.6 1.9 2` ✔
- 分层：`IGoldDropTableSource` 早在基线就由 `InfrastructureModule.cs:69` 注册、被 `DropRuleService`
  以同样方式注入，本次没有新建注册也没有绕过分层；
  `The_rule_service_is_handed_no_reader_of_world_state` 的反射断言已更新为四个内容源。
- 还原校验：**全程 `sha256` 备份，未使用 `git checkout --`。** 11 次变异结束后，
  六个被改文件的 `sha256sum -c` 全部 OK，`git status --porcelain` 与复审开始时逐字节相同，
  `git diff 927e2d0e --stat` 仍为 `24 files changed, 761 insertions(+), 136 deletions(-)`，
  还原后重跑全量判据仍为 **1993 / 0 / 2**。

## 本轮指定整改的逐条核实

| 上一轮 | 本轮判定 | 证据 |
|---|---|---|
| BF-5 三处 XML doc 错位 | **已解决** | 见下 |
| BF-6 难度倍率取哪一档空过 | **已解决** | 变异 M1 红 3 条 |
| BF-7 `PvfDropRateTableSource.cs:38` 错误陈述 | **已解决** | 见下 |

### BF-5

三处文档块全部归位，逐处核到行号：

- `ClearRewardRuleServiceTests.cs`：`GoldBase`（:1414）现在顶着自己的
  「The gold bases and bands the fixture's dungeon level declares」；
  `GoldTable(`（:1420）顶着自己的；`StubGoldTables`（:1428）拿回了
  「Hands out one gold table…」（:1427）；`Table(`（:1470）拿回了
  「A table whose second profile declares the full threshold…」（:1457-1468）。**四个成员各有各的文档。**
- `DungeonRunClearRewardCardTests.cs`：`FaceGold`（:47）有自己的 summary（:46），
  「A face written out by hand…」（:49）回到 `Face(`（:50）头上。
- `CardRewardDomainServiceTests.cs`：「Every dungeon offers a flip here…」（:129-135）回到
  `OffersPaidFlipAsync`（:138）头上，`FaceGold`（:142）有自己的 summary（:141）。

### BF-7

`PvfDropRateTableSource.cs:39-42` 已改为
「Level, the amount one drop of gold is worth there, and how far either way that amount may move
as a percentage. All three are read; the third is banded rather than constant.」
——与 :88-93 的 `variances[baseValues[at]] = baseValues[at + 2];` 一致，与档案一致。全仓已无
「未识别的第三个数」这类陈述。

### BF-6

`ClearRewardFixture.DifficultyGoldRates`（`ClearRewardRuleServiceTests.cs:1455`）现为
`[1.25, 1.5, 1.75, 2.0, 2.25]` 五项互不相同，`DifficultyGoldRate = 1.5` 正是
`CertainDifficulty = 1` 那一项；新增
`Each_difficulty_scales_the_gold_by_its_own_entry`（:1209）关掉波动后逐档断言
`GoldBase × DifficultyGoldRates[difficulty]`，并断言五个结果两两不同。
**不是空过的**：变异 M1 红 3 条，且新的五项取值同时让原有的两条用例也变得可分辨。
新增的 `A_difficulty_the_file_does_not_list_pays_nothing`（:1237）把「难度未列返回 0」也钉住了。

## 本轮变异复核（11 条，全部在备份 + sha256 下进行）

| # | 变异 | 结果 | 谁抓住 |
|---|---|---|---|
| M1 | `ClearRewardContent.cs:286` `DifficultyGoldRates[difficulty]` → `[0]` | **红 3 条** ✔ | `Each_difficulty_scales_the_gold_by_its_own_entry`、`The_faces_gold_is_the_levels_base_scaled_by_the_difficulty`、`The_golds_base_is_taken_at_the_dungeons_level_not_the_characters` |
| M2 | `ClearRewardRuleService.cs:352` `gold.BaseFor(occasion.DungeonBasisLevel)` → `occasion.CharacterLevel` | **红 1 条** ✔ | `The_golds_base_is_taken_at_the_dungeons_level_not_the_characters` |
| M3 | `ClearRewardRuleService.cs:354` `gold.VarianceFor(...)` 同上 | **红 1 条** ✔ | 同上 |
| M4 | `DungeonClearSlice.cs:354` `writer.Write(cardGold);` → `writer.Write(0);` | **红 4 条** ✔ | `TheGoldTheSettlementFixedReachesTheCard` + `DungeonClearCodecTests` 三条 |
| M5 | `PvfDropRateTableSource.cs:92` `baseValues[at + 2]` → `13` | **红 1 条** ✔ | `ClearRewardArchiveTests.The_archive_declares_the_numbers_a_cards_gold_is_built_from` |
| M6 | `ClearRewardRuleService.cs:344` `is not ClearRewardCardKind.Free` → `is ...Free` | **红 6 条** ✔ | 规则服务五条 + `The_floor_belongs_to_the_free_card_and_not_to_the_paid_one` |
| M9 | `ClearRewardRuleService.cs:354` `gold.VarianceFor(...)` → `0`（波动整段关掉） | **红 1 条** ✔ | `The_same_clear_settles_the_same_gold_every_time` |
| M10 | `ClearRewardRules.cs:478` `random.Next((2*p)+1) - 1 - p` → `random.Next(p+1) - 1`（单向浮动） | **红 1 条** ✔ | `The_variance_moves_the_amount_both_ways_and_stays_inside_the_band` |
| M11 | `DungeonClearSlice.cs:768` 有奖励分支的 `cardGold` → `0` | **红 1 条** ✔ | `TheGoldTheSettlementFixedReachesTheCard`（确认帧判据覆盖的是主路径而非兜底路径） |
| **M7** | `DungeonClearSlice.cs:626` `settled?.Cards.Free.Gold` → `settled?.Cards.Paid.Gold` | **全绿 1993 / 0 / 2** ❌ | 无 —— 见 BF-8 |
| **M8** | `SelectCardCommand.cs:232` `fixedCards.FaceOf(command.Kind).Gold` → `FaceOf(ClearRewardCardKind.Free).Gold` | **金币判据全绿** ❌ | 无 —— 见 BF-8（唯一的红是无关的既有 flake，见观察项） |

指定的五条变异（M1、M2、M3、M4、M5、M6）**全部必红**，且各条 log 的 `error CS` 计数均为 0，
红的是断言失败而不是编译失败。第二轮补住的三条链路本轮重跑仍然成立。

## Blocking Findings

### BF-8｜「哪一张牌的金币」这一步在帧与入账两处都空过，RQ-007 / RQ-001 未被完整检验

同一个根因，两个可独立报红的后果。根因是：**本变更集里没有任何一个 fixture 让免费牌和付费牌
带不同的金币。** 全仓六处手写牌面（`SetPlayResultHandlerTests`、`CardRewardTests`、
`CardRewardReconnectIntegrationTests`、`DungeonRunClearRewardCardTests`、
`CardRewardDomainServiceTests`、`PaidCardFlipCostTests`）一律用同一个 `FaceGold = 37`，
连 `SetPlayResultHandlerTests.cs:576` 自己写着「The paid face, **deliberately unlike** the free one」
的那一张也是 37。于是凡是「读了哪一张牌的 Gold」的代码，判据都分辨不出来。

**（a）帧携带：读错牌面，帧上的金币变成 0，1993 条判据一条不红。**

- 变异 M7：`Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs:626`
  的 `var cardGold = settled?.Cards.Free.Gold ?? 0;` 改成 `settled?.Cards.Paid.Gold ?? 0;`，
  全量判据 **1993 通过 / 0 失败 / 2 跳过**。
- 后果不是理论的：`ClearRewardRuleService.GoldOf`（:344）对非 Free 直接返回 0，
  所以生产上 `Cards.Paid.Gold` **恒为 0** —— 结算通知的条目 0 会写 0，
  玩家翻开牌看到的正是本任务要消灭的那个「一点反馈都没有」。
- 第二轮的 BF-1 整改（`TheGoldTheSettlementFixedReachesTheCard`，
  `SetPlayResultHandlerTests.cs:262-263`）钉住的是「处理器交给编码器的那个数原样到达帧」，
  钉不住「那个数是免费牌的」，因为 `FreeFace`（:565-574）与 `PaidFace`（:577-586）都带 37。
  Spec RQ-007 的原文是「结算通知里那张牌的第一条条目携带**这个金额**」，
  「这个金额」按 RQ-001 只可能是免费牌的那一笔。

**（b）入账：读错牌面，付费牌白拿一份底薪，屏幕上完全看不出来。**

- 变异 M8：`Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs:232`
  的 `var paid = fixedCards.FaceOf(command.Kind).Gold;` 改成
  `fixedCards.FaceOf(ClearRewardCardKind.Free).Gold;`，全部金币判据一条不红
  （该次运行唯一的失败是 `FinishQuestCommandHandlerTests.AQuestWhoseObjectiveIsUnmetPaysNothing`
  抛 `ObjectDisposedException: SQLitePCL.sqlite3`，与本变更集无关，见观察项）。
- 后果：付费（黄金）牌翻牌会在已付档案价钱之外**再拿一份免费牌的底薪**。
  这正是**契约 revision 2 专门加进来禁止的那件事**（「玩家已经按档案的价钱买过那张牌，
  再给一次底薪是本服务器凭空多发一份奖励」），也是 RQ-001 的后半句。
  付费行的帧是 `WriteUnturnedRow` 写空的，所以这份多发的金币**在屏幕上不显示**，
  只是账户悄悄多一笔 —— 与 BF-2 / BF-6 完全同一个失败形状，且比它们更隐蔽
  （BF-2 / BF-6 至少还改变屏幕上的数字）。
- `The_floor_belongs_to_the_free_card_and_not_to_the_paid_one` 钉的是**规则层**按 `Kind` 给 0；
  真正搬钱的是命令层，而命令层挑哪一张牌没有任何判据。
  `CardRewardTests.cs:828` 那条付费翻牌用例断言的是
  `StartingPurse - FixturePrice + FaceGold`，因为它的 `Face(kind)`（:1171-1175）
  给付费牌也发了 37，所以它恰好无法区分。

要求（两处都做，否则任一处仍空过）：

1. 让 `SetPlayResultHandlerTests.RecordingCards` 的 `PaidFace`（:577-586）带一个**与
   `FaceGold` 不同**的金币（用 `0` 最贴近生产，也最能说明「底薪只属免费牌」），
   使变异 M7 必红。
2. 让 `CardRewardTests.World.Face(kind)`（:1171）按 `kind` 给出不同的金币
   （免费牌 `FaceGold`、付费牌 `0`），并相应更新 :828 那条付费翻牌的期望值，
   使变异 M8 必红。这一改动落在金币判据范围内，不触碰 RQ-009 保护的其余取值。

## Non-Blocking Observations

- **`ClearRewardRuleServiceTests.cs:659` 留了一处与自己上一句矛盾的陈述。**
  `The_registration_wires_the_service_and_reads_the_basis` 的 remarks 第一句已改成
  「the **four** content sources」（:657），第三句却仍是「**Three**, as
  `The_rule_service_is_handed_no_reader_of_world_state` pins by reflection and the `Build` below
  registers: the dungeon list joined the rate table and the pool…」——数字没跟着改，
  枚举里也没提金币表。同一族但比 BF-7 轻得多（是测试 remarks 里的一个计数，不是档案陈述），
  故不阻塞；顺手改成「Four…: the dungeon list and the gold table joined…」即可。
- **`The_rule_service_is_handed_no_reader_of_world_state` 的注释重排留了个断行**
  （`ClearRewardRuleServiceTests.cs:500-501`，「which is what the suffix / check below is」
  在 `suffix` 后断行）。
- **既有 flake（非本变更集引入）**：变异 M8 那次运行里
  `Dfo.Server.Protocol.Tests.Game.QuestCompletion.FinishQuestCommandHandlerTests.AQuestWhoseObjectiveIsUnmetPaysNothing`
  抛 `System.ObjectDisposedException: 'SQLitePCL.sqlite3'`（栈在
  `FinishQuestCommandHandlerTests.cs:190` 的 fixture 建库处）。该文件不在 `git diff 927e2d0e` 内，
  本轮另外 11 次全量运行（含基线与还原后）该用例均通过。属既有的测试隔离/时序 flake，
  与本任务无关，但会让「全绿」这一证据偶尔不可复现，记录备考。
- **空行残留仍在**（前两轮已列，第三轮未处理）：`ClearRewardRules.cs:485-486` 连着两个空行；
  `ClearRewardArchiveTests.cs`、`ClearRewardRulesTests.cs`、`ClearRewardRuleServiceTests.cs`
  的类尾各多一个空行。全仓未开 StyleCop，编译不报。
- **`ClearRewardCardFace` 的两条 CS1573 仍在**（`IClearRewardRuleService.cs:84`：只给 `Gold` 写了
  `<param>`，`Kind` 与 `Slots` 没有）。全仓 292 条同类、且未开 `GenerateDocumentationFile`，
  属既有惯例，不阻塞。
- **两个生成器同种子**（`ClearRewardRuleService.cs:203` 与 `:217` 都是 `new DropRandom(seed)`）：
  确定性与「金币不挪动牌面抽取」都成立（M9 之外，
  `What_the_gold_spends_of_the_generator_never_reaches_the_positions` 钉住），
  但两条序列完全相同，金币值与第 0 格抽取由同一批随机数派生。目前无人依赖独立性，记录备考。
- **AC-002 的原文形式（两个基准等级的金额之比等于基数之比）未逐字实现**，
  但 RQ-002 由 `The_golds_base_is_taken_at_the_dungeons_level_not_the_characters` 覆盖且
  M2 / M3 必红，实质已达成，不阻塞。
- **SC-9 / EV-006（实机屏幕观察）仍未取得。** 本轮只覆盖自动判据与档案核对；
  按契约需实机运行 + 使用者确认，属验收阶段，不在本次 change 评审的可判范围。
  需要指出：BF-8(b) 那一族缺陷**实机也观察不到**，所以 SC-9 不能替它兜底。

## RQ 逐条覆盖判定（本轮重判）

| RQ | 判定 | 依据 |
|---|---|---|
| RQ-001 恒大于零、付费牌不拿 | **部分不满足** | 规则层满足（M6 红 6 条、`The_floor_belongs_to_the_free_card_and_not_to_the_paid_one`）；但真正搬钱的命令层挑哪张牌无判据，见 BF-8(b)，M8 全绿 |
| RQ-002 以副本基准等级基数为底 | 满足 | M2、M3 各红 1 条，`Occasion(dungeonBasisLevel: 5)` 与角色 86 级分离、两级基数 44/1357、波动 0/50、50 个种子逐一断言定值 |
| RQ-003 按本局难度倍率缩放 | **已由 BF-6 整改满足** | fixture 五项互不相同，`Each_difficulty_scales_the_gold_by_its_own_entry` 逐档断言 + 五值互异；M1 红 3 条 |
| RQ-004 波动不越界且按等级分段 | 满足 | `ClearRewardArchiveTests` 钉 `VarianceFor` 的 1/64/65/77/78/86/200 共七点、跨三段两界，并同时钉四个对应基数防串列；M5 红、M10 红 |
| RQ-005 双向可达 | 满足 | `The_variance_moves_the_amount_both_ways_and_stays_inside_the_band` 扫 200 个种子、两侧都断言出现；M10 红 |
| RQ-006 同局不变 | 满足 | `The_same_clear_settles_the_same_gold_every_time`（含「不同种子给不同值」的反向保护，M9 红）+ 领域层牌面一次定死 |
| RQ-007 帧条目 0 携带该金额 | **部分不满足** | 「数原样到达帧」满足（M4 红 4 条、M11 红 1 条）；「那是免费牌的数」不满足，见 BF-8(a)，M7 全绿 |
| RQ-008 一律入账 | 满足 | `A_flip_pays_the_cards_gold_whether_or_not_it_held_anything` 两分支无条件断言 `GoldPaid == FaceGold` 与账户总额 |
| RQ-009 其余取值逐值不变 | 满足 | `git diff 927e2d0e -- Server/tests/` 中被删的非注释断言**全部**与金币有关（`ClearRewardCardGold`×6、`card.Gold`×3、`UnreadSections.DifficultyGoldBonusRate`×1）；出货概率、品级带、副本定价、房间数、掉落池选取的期望值一个未动。被一并删掉的 `Assert.Null(...Columns[0].Piece)` 的非金币语义由存活的 `Only_the_position_the_flip_claims_reaches_the_wire`（`DungeonClearCodecTests.cs:311`）继续覆盖 |
| RQ-010 每个因子有档案出处 | 满足 | 三个因子本轮均用 `PvfProbe` 独立核档，并由 `ClearRewardArchiveTests.The_archive_declares_the_numbers_a_cards_gold_is_built_from` 逐值钉住（M5 红）；`PvfDropRateTableSource.cs:39-42` 与 `DropRateContent.cs:240-256` 两处陈述现在一致且属实 |

## 结论

本轮指定的三条（BF-5、BF-6、BF-7）**都真的做完了，且新判据不是空过的**：M1 红 3 条，
文档块三处逐一归位，第三列的陈述全仓一致且经 `PvfProbe` 核实。前两轮补住的六条链路重跑仍全部必红。
公式仍只含三个有出处的因子，参照服务端那四个无出处因子一个未进；既有规则层取值逐值未动；
无探针残留、无超范围改动、`GameProtocol.ClearRewardCardGold` 已退役且全仓无引用。

但同一个失败形状在**尚未被检查过的那一半**上再次出现：前两轮盯的是「查表取错**下标/等级**」，
本轮暴露的是「取错**牌面**」。免费牌与付费牌在全仓每一个 fixture 里都带同一个 `37`，
于是帧上写的是哪张牌的金币、账上加的是哪张牌的金币，两处都无人报警——
其中入账那一处会让付费牌凭空多拿一份底薪（契约 revision 2 专门禁止的那件事），
且因为付费行的帧写的是空行，**实机也看不出来**，SC-9 兜不住。

修法很小：两个 fixture 各让付费牌带一个不同的金币（`0` 最贴近生产），
再顺手改掉 `ClearRewardRuleServiceTests.cs:659` 那个没跟着改的「Three」。

裁决 `changes_required`：BF-8 的 (a)(b) 两处解决后需重新独立评审。

---

# 复审（第四轮）：every-flip-pays-gold（change 范围）

评审对象：整改后的 `git diff 927e2d0e`（24 个文件，**787 插入 / 140 删除**）+ 未跟踪的
`.rks-agent/tasks/every-flip-pays-gold/`。契约 revision 2，Spec RQ-001..010 / AC-001..010 / EV-001..006。
Profile 为 `sdd-lite`，未配置对抗挑战集，故无 `Challenge Dispositions` 一节。
本轮全部结论自行取证：前三轮的裁决、执行者对本轮整改的描述、以及「1993 全绿」这一现有证据
都先当作**待核**，逐条重跑。

## 本轮复核过的基线

- 全量判据（整改后原样跑，未做任何改动）：
  `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
  → **1993 通过 / 0 失败 / 2 跳过**（52+488+60+248+4+215+926），`error CS` 计数 0。与现有证据一致。
  变异全部结束、工作区还原后再跑一次，仍为 **1993 / 0 / 2**。
- 还原校验：**全程 `sha256` 备份（评审开始时对 24 个改动文件逐一取值），未使用 `git checkout --`。**
  18 次变异每一次结束都立刻还原并 `sha256sum -c`，18 次全部 `OK`；最终
  `git status --porcelain` 与复审开始时逐字节相同，`git diff 927e2d0e --stat` 仍为
  `24 files changed, 787 insertions(+), 140 deletions(-)`。
- 探针残留：diff 的新增行里 `Console.` / `Debug.` / `Trace.` / `TODO` / `FIXME` / `Thread.Sleep` /
  `Task.Delay` 命中数为 **0**。`GameProtocol.ClearRewardCardGold` 已删，全仓源码与协议文档无引用
  （仅剩历史评审文档里的叙述）。
- 协议记录：`protocol/game/server-to-client/NOTI-0x0023.md` 只描述线格布局与参照服务端的写法，
  全文没有「本仓写 1」这类会过期的陈述，无需同步。

## BF-8 两条整改的逐条核实

两条都是**指定的原样变异**，两条都必红，且红的是断言失败而非编译失败（各 log `error CS` 计数为 0）。

| # | 变异 | 结果 | 谁抓住 |
|---|---|---|---|
| N1 | `DungeonClearSlice.cs:626` `settled?.Cards.Free.Gold ?? 0` → `settled?.Cards.Paid.Gold ?? 0` | **红 1 条** ✔ | `SetPlayResultHandlerTests.TheGoldTheSettlementFixedReachesTheCard`（`Expected: 37 / Actual: 0`，`SetPlayResultHandlerTests.cs:262`） |
| N2 | `SelectCardCommand.cs:232` `fixedCards.FaceOf(command.Kind).Gold` → `FaceOf(ClearRewardCardKind.Free).Gold` | **红 1 条** ✔ | `CardRewardTests.A_paid_flip_costs_the_price_the_settlement_names_and_hands_the_card_over`（`Expected: 750 / Actual: 787`，`CardRewardTests.cs:838`） |

整改的落点也逐处核过：

- `SetPlayResultHandlerTests.cs:585` 新增 `public const int PaidFaceGold = 0;`，:598 的 `PaidFace`
  改用它；`FaceGold = 37` 与 `PaidFaceGold = 0` 与该文件的 `PaidFlipCost` / `ClearBase` /
  `RankBonus` / `ClampedLanding` 均不相同，串位也过不了。
- `CardRewardTests.cs:738` `FaceGold = 37`、:748 `PaidFaceGold = 0`，:1181 的 `Face(kind)` 按
  `kind is Free ? FaceGold : PaidFaceGold` 分发；:838 的期望值由 `+ GameProtocol.ClearRewardCardGold`
  改成 `+ PaidFaceGold`，算出 `1000 - 250 + 0 = 750`。四个常量 `1000 / 250 / 37 / 0` 互不相同。
- 两处新增常量各自带自己的文档块，没有再制造第三轮 BF-5 那种「常量顶着别人 `<summary>`」的错位。
- `ClearRewardRuleServiceTests.cs:657-663` 的「Three」已改成「Four」，并且枚举里补上了金币表
  （「the dungeon list joined … and the gold table joined when a card's gold became an archive
  reading」），与 :657 的「the four content sources」和
  `The_rule_service_is_handed_no_reader_of_world_state` 的四个反射断言三处一致。

**BF-8 的 (a)(b) 两处都真的补住了，不是空过。**

## 同族缺陷的系统性排查

前三轮四条空过判据的形状是同一个：**fixture 把两个本该可区分的东西写成了同一个值**。
本轮不再逐个碰运气，而是把本变更集触及的查表维度与 fixture 常量整理成清册，逐个维度做变异。

### 维度清册与判定

| 维度 | 取错了会怎样 | 判定 | 证据 |
|---|---|---|---|
| 牌种（免费/付费）— 帧上的金币 | 帧写 0 | 已钉 | N1 红 |
| 牌种 — 入账的金币 | 付费牌白拿底薪 | 已钉 | N2 红 |
| 牌种 — 规则层是否给底薪 | 付费牌拿底薪 | 已钉 | 第三轮 M6/D1/D3 红 |
| 牌种 — 帧上的槽位 | 帧报错牌的内容 | 已钉 | N6 红 2 |
| 牌种 — 翻牌应答的槽位 | 付费翻牌报免费牌的内容 | **空过** | N7 全绿 —— 既有缺口，见观察项 |
| 难度下标 | 五个难度一律按第一档结算 | 已钉 | N10 红 3、N16 红 2 |
| 难度表 vs 加数表（读错节） | 倍率读成 `[dungeon difficulty drop bonusrate]` | 已钉 | N11 红 3 |
| 副本基准等级 vs 角色等级（基数） | 86 级角色在 5 级本拿 30 倍 | 已钉 | N3 红 3、第三轮 M2 红 |
| 副本基准等级 vs 角色等级（波动） | 同上 | 已钉 | 第三轮 M3 红 |
| 基数 ↔ 波动百分比（实参对调） | 金额变成「波动值 × 倍率」 | 已钉 | N3 红 3 |
| 等级→基数 / 等级→波动段（两张字典串表） | 波动读成基数 | 已钉 | N4 红 4 |
| `[gold drop ref table]` 列下标（第三列 vs 第二列） | 波动读成基数列 | 已钉 | N5 红 1 |
| 该表的键（等级列 vs 行偏移） | 按行号而非等级建索引 | 已钉 | N13 红 1 |
| 波动分段（1–64 / 65–77 / 78–200） | 全档案读成 13 | 已钉 | 第三轮 M5 红；档案判据钉七点两界 |
| 组 0/1/2 三行（免费行 / 黄金行 / 黑砖行） | 金币写到黄金行上 | 已钉 | N9 红 12 |
| slot 0..7（`RewardColumn`） | 金币写到第 1 列 | 已钉 | N8 红 7 |
| 记录序号 0..7（`RewardPosition`） | 取第 1 格的东西上线 | 已钉 | N12 红 5 |
| 条目内两个 int 字段（标识符 vs 金额） | 金币写进被忽略的标识符 | 已钉 | N8 / N12 各红，含 `The_gold_entrys_identifier_is_not_the_pieces` |
| 难度下标 vs 队伍人数（fixture 里两者同为 1） | 倍率按队伍人数取 | 已钉 | N16 红 2 |
| 无牌可摇路径是否仍付底薪 | 空牌不付 | 已钉 | N14 红 2 |
| 「摇到零也付一」的下限 | 低等级低倍率付 0 | 已钉 | N15 红 1 |
| 结算通知的三个构造点 | 某一条支路不带金币 | **部分空过** | N17 / N18 全绿 —— 均为防御支路，见观察项 |

### 本轮变异复核（18 条，全部在备份 + sha256 下进行）

| # | 变异 | 结果 | 谁抓住 |
|---|---|---|---|
| N1 | `DungeonClearSlice.cs:626` `Cards.Free.Gold` → `Cards.Paid.Gold` | **红 1** ✔ | `TheGoldTheSettlementFixedReachesTheCard` |
| N2 | `SelectCardCommand.cs:232` `FaceOf(command.Kind).Gold` → `FaceOf(Free).Gold` | **红 1** ✔ | `A_paid_flip_costs_the_price_the_settlement_names_and_hands_the_card_over` |
| N3 | `ClearRewardRuleService.cs:351-355` 把 `BaseFor` 与 `VarianceFor` 两个实参对调 | **红 3** ✔ | `Each_difficulty_scales_the_gold_by_its_own_entry`、`The_faces_gold_is_the_levels_base_scaled_by_the_difficulty`、`The_golds_base_is_taken_at_the_dungeons_level_not_the_characters` |
| N4 | `DropRateContent.cs` `VarianceFor` 改读 `BaseAmounts` | **红 4** ✔ | 上三条 + `ClearRewardArchiveTests.The_archive_declares_the_numbers_a_cards_gold_is_built_from` |
| N5 | `PvfDropRateTableSource.cs:92` `baseValues[at + 2]` → `baseValues[at + 1]` | **红 1** ✔ | `ClearRewardArchiveTests` 同上 |
| N6 | `DungeonClearSlice.cs:625` `Cards.Free.Slots` → `Cards.Paid.Slots` | **红 2** ✔ | `AClearThatCannotBePaidStillFixesAndReportsItsCards`、`TheResultScreenReportsTheCardsThisClearFixed` |
| **N7** | `SelectCardCommand.cs:239` `FaceOf(command.Kind).Slots` → `FaceOf(Free).Slots` | **全绿 1993 / 0 / 2** ❌ | 无 —— 观察项，**既有缺口，本变更集未触碰该行** |
| N8 | `DungeonClearSlice.cs:407` `RewardColumn` 0 → 1 | **红 7** ✔ | `DungeonClearCodecTests` 六条 + `TheGoldTheSettlementFixedReachesTheCard` |
| N9 | `DungeonClearSlice.cs:292-294` 免费行与黄金行互换 | **红 12** ✔ | `DungeonClearCodecTests` 十一条 + `TheGoldTheSettlementFixedReachesTheCard` |
| N10 | `ClearRewardContent.cs:286` `DifficultyGoldRates[difficulty]` → `[0]` | **红 3** ✔ | 复核第三轮 M1，在第四轮新 fixture 下仍成立 |
| N11 | `ClearRewardRuleService.cs:347` `DifficultyGoldRate(d)` → `DifficultyRate(d)`（读成加数表） | **红 3** ✔ | 规则服务三条 |
| N12 | `DungeonClearSlice.cs:415` `RewardPosition` 0 → 1 | **红 5** ✔ | `Only_the_position_the_flip_claims_reaches_the_wire` 等五条 |
| N13 | `PvfDropRateTableSource.cs:92` `variances[baseValues[at]]` → `variances[at]`（键改用行偏移） | **红 1** ✔ | `ClearRewardArchiveTests` 同上 |
| N14 | `ClearRewardRuleService.cs:210` `Undrawn(occasion.Kind, gold)` → `Undrawn(occasion.Kind, 0)` | **红 2** ✔ | `Each_difficulty_scales_the_gold_by_its_own_entry`、`The_golds_base_is_taken_at_the_dungeons_level_not_the_characters` |
| N15 | `ClearRewardRules.cs:483` 去掉 `amount <= 0 ? 1 :` 的下限 | **红 1** ✔ | `An_amount_that_would_round_away_still_pays_one` |
| N16 | `ClearRewardRuleService.cs:347` 难度下标改取 `occasion.PartyMemberCount`（默认 fixture 里两者同为 1） | **红 2** ✔ | `A_difficulty_the_file_does_not_list_pays_nothing`、`Each_difficulty_scales_the_gold_by_its_own_entry` |
| **N17** | `DungeonClearSlice.cs:665`（无技能点镜像分支）的 `cardGold` → `0` | **全绿 1993 / 0 / 2** ❌ | 无 —— 观察项，防御支路 |
| **N18** | `DungeonClearSlice.cs:767`（`award is null` 分支）的 `cardGold` → `0` | **全绿 1993 / 0 / 2** ❌ | 无 —— 观察项，防御支路 |

十五条红的每一条，log 里的 `error CS` 计数都是 0，红的是断言失败而不是编译失败。
第三轮那两条空过（M7 / M8）本轮以 N1 / N2 原样重跑，**都已必红**。

### 三条绿变异的判定

- **N7（付费翻牌应答报的是哪张牌的槽位）—— 既有缺口，不在本变更集边界内。**
  `SelectCardCommand.cs:239` 的 `fixedCards.FaceOf(command.Kind).Slots` 是基线原样、
  本次 diff 一字未改（本次只改了它上面那行 `paid`）。之所以空过，是因为
  `CardRewardTests.World.Face(kind)`（:1181）对两种牌**给的是同一组槽位**
  （`[Held(0, FirstItemId, 1), Empty(1), Held(2, SecondItemId, 2), Empty(3)]`，与 kind 无关），
  `PaidCardFlipCostTests` 的桩只有一张脸，而 `CardRewardReconnectIntegrationTests` 虽然两张脸不同，
  走的却是领域层 `TryClaimClearRewardCard` 而不是命令层。这三处在基线就是这样，
  所以本变更集既没有引入这个缺口、也没有加深它。本轮把它记下来，是因为它正是使用者要找的那一族，
  而且修法只是给 `Face(kind)` 再加一个按 kind 分发的槽位 —— 但那属于另一件事的范围，
  不作为本次交付的阻塞项。
- **N17 / N18（结算通知另外两个构造点）—— 防御支路，不阻塞。**
  `ClearDungeonRewardNotification` 在本文件有三个构造点，`cardGold` 实参三处都是本次新加的：
  - :769（`award` 非空，玩家可达的主路径）—— 第三轮 M11 已证必红；
  - :767（`award is null`）—— 按 `ICharacterExperienceGrant.AwardAsync` 的契约，
    「nothing when there is no such character to award to」，即角色行在本局中途消失才会到达；
  - :665（`ProtocolMirror is null`）—— 该处代码自己的注释论证「the branch cannot be reached for a
    published session（`SelectCharacterHandler` 的发布前构建会在缺镜像时抛）」。

  两条绿的都是防御支路，**玩家可达的那条主路径由 N1 与第三轮 M11 双重钉住**，
  且金币的来源变量本身（`var cardGold = settled?.Cards.Free.Gold ?? 0`）由 N1 钉死。
  因此 RQ-007 / SC-5 成立，不构成阻塞。记录备考的理由是：同文件的
  `TheResultScreenSaysWhatAPaidFlipCosts`（:277-297）对**兄弟字段** `PaidFlipCost` 恰恰把三条支路
  都扫了一遍，`freeCards` 也被扫到两条（N6 红 2），只有新加的 `cardGold` 停在一条；
  照着那条既有用例的写法补四行即可对齐。

## RQ 逐条覆盖判定（本轮重判）

| RQ | 判定 | 依据 |
|---|---|---|
| RQ-001 恒大于零、付费牌不拿 | **满足**（第三轮 BF-8(b) 已闭合） | 规则层：`A_settled_face_carries_its_gold_whether_or_not_it_drew_anything`（两个 threshold 分支）、`An_amount_that_would_round_away_still_pays_one`（N15 红）、`The_floor_belongs_to_the_free_card_and_not_to_the_paid_one`（第三轮 M6 红 6）；空牌路径 N14 红 2；**命令层现由 N2 红 1 钉住**「入的是本张牌的金币」 |
| RQ-002 以副本基准等级基数为底 | 满足 | `The_golds_base_is_taken_at_the_dungeons_level_not_the_characters`（两级基数 44 / 1357、波动 0 / 50、50 个种子逐一断言定值）；N3 红 3、第三轮 M2 / M3 各红 1 |
| RQ-003 按本局难度倍率缩放 | 满足 | `Each_difficulty_scales_the_gold_by_its_own_entry` 逐档断言 + 五值互异；N10 红 3、N11 红 3、N16 红 2；`A_difficulty_the_file_does_not_list_pays_nothing` 钉住未列难度 |
| RQ-004 波动不越界且按等级分段 | 满足 | `ClearRewardArchiveTests.The_archive_declares_the_numbers_a_cards_gold_is_built_from` 钉 `VarianceFor` 的 1 / 64 / 65 / 77 / 78 / 86 / 200 七点、跨三段两界，并同时钉四个对应基数防串列；N4 红 4、N5 红 1、N13 红 1；`The_variance_moves_the_amount_both_ways_and_stays_inside_the_band` 扫 200 个种子断言全部落在带内 |
| RQ-005 双向可达 | 满足 | 同上用例两侧都断言出现；第三轮 M10 红 |
| RQ-006 同局不变 | 满足 | `The_same_clear_settles_the_same_gold_every_time`（含「不同种子给不同值」的反向保护，第三轮 M9 红）；「断线重来」这一半在结构上成立：牌面与金币在 `FixClearRewardCards` 时一次定死并留在内存中的 run 上，重连不重摇，`CardRewardReconnectIntegrationTests` 钉住 run 本身的存活 |
| RQ-007 帧条目 0 携带该金额 | **满足**（第三轮 BF-8(a) 已闭合） | `TheGoldTheSettlementFixedReachesTheCard` 走真编码 → `CardPanelFrameReader.ReadClearDungeonReward` → 断言 `FreeRow.Columns[0].Gold == 37`，**且现在能分辨「那是免费牌的数」**（N1 红 1）；写入位置由 N8 红 7、N9 红 12、N12 红 5、`Only_the_card_the_reward_is_on_carries_gold` 共同钉住 |
| RQ-008 一律入账 | 满足 | `A_flip_pays_the_cards_gold_whether_or_not_it_held_anything` 两分支无条件断言 `GoldPaid == FaceGold` 与账户总额；第三轮 E 红 |
| RQ-009 其余取值逐值不变 | 满足 | 见下节的删除清单审计 |
| RQ-010 每个因子有档案出处 | 满足 | 三个因子由 `ClearRewardArchiveTests` 逐值钉住（N4 / N5 / N13 红）；`DropRateContent.cs:240-256` 与 `PvfDropRateTableSource.cs:39-42` 两处分段陈述一致且属实（前三轮已用 `PvfProbe` 三次核档，本轮不重复取证）；公式里没有第四个因子 —— `175` 只出现在两段解释「为什么不采用」的散文中 |

**RQ-001..010 逐条都有能报红的判据**，无一条依赖「全绿」这一空洞证据。

## 既有规则层取值逐值不变（RQ-009 / SC-7）

把 `git diff 927e2d0e` 的**全部删除行**过滤掉注释与空行后逐条看，共 65 条，全部落在三类里：

1. 记录 / 方法签名的结构性改写（`ClearRewardCardFace` 多一个 `Gold`、`Undrawn` 多一个参数、
   `WriteFreeRow` 多一个参数、`ClearDungeonRewardNotification` 多一个字段、
   `GoldDropTable` 多一个字典、四处构造函数多一个内容源）；
2. 金币相关的断言与用例（`GameProtocol.ClearRewardCardGold` × 6、`card.Gold` × 3、
   `Gold_never_arrives_on_a_card_with_nothing_behind_it` 整条、
   `The_cards_gold_is_one_and_is_this_servers_own_decision` 整条）；
3. `PvfClearRewardRateSourceTests.cs:435` 的 `Assert.Contains(...UnreadSections.DifficultyGoldBonusRate, tags)`
   —— 该节现在确实被读了，删除是必然结果。

出货概率、品级带、副本定价、房间数上限、掉落池选取、稀有度阶梯、经验估值的断言与期望值
**一个未动**。被一并删掉的 `Assert.Null(...Columns[0].Piece)` 的非金币语义，
由存活的 `Only_the_position_the_flip_claims_reaches_the_wire` 继续覆盖（N12 会红它）。

## 超范围改动与 Spec 未做项

- 超范围：无。24 个文件里，10 个源文件全部落在契约 Scope / In 之内；14 个测试文件中有 5 个
  （`DropRulesTests`、`MonsterDropServiceTests`、`DungeonPaidCardSwitchRuleTests`、
  `PaidCardFlipCostTests`、`CardRewardDomainServiceTests`）只是跟着新签名补参数。
  付费牌金币从「有装备给 1」变成「一律给 0」这一可观测变化，由契约 revision 2 具名授权、
  规则处有就地注释、`The_floor_belongs_to_the_free_card_and_not_to_the_paid_one` 钉住。
- Spec 未做项：
  - **EV-006 / SC-9（实机屏幕观察）仍未取得**，按契约需实机运行 + 使用者确认，属验收阶段，
    不在本次 change 评审的可判范围。第三轮曾指出 BF-8(b) 那一族实机也看不到；本轮该缺陷已闭合。
  - AC-002 的原文形式（两个基准等级的金额之比等于基数之比）与 AC-003 的原文形式
    （五个难度单调不减）都没有逐字实现，但两者的实质各由
    `The_golds_base_is_taken_at_the_dungeons_level_not_the_characters` 与
    `Each_difficulty_scales_the_gold_by_its_own_entry` 以**更强的逐值断言**覆盖（定值而非比值、
    定值而非序关系），且对应变异必红。不阻塞。

## Non-Blocking Observations

- **N7：付费翻牌应答报的是哪张牌的槽位，全仓无判据。** `SelectCardCommand.cs:239`
  改成恒取免费面，1993 条判据一条不红。该行是基线原样、本次未改，缺口是既有的；
  记录在此是因为它与 BF-8 同族，且 `CardRewardTests.World.Face(kind)`（:1181）本轮刚被改成
  按 kind 分发金币，再顺手让两张脸的槽位也不同即可闭合。**属另一件事的范围。**
- **N17 / N18：`ClearDungeonRewardNotification` 三个构造点里的两个（`DungeonClearSlice.cs:665`、
  `:767`）不带判据。** 两条都是防御支路（前者代码自证不可达，后者要求角色行中途消失），
  玩家可达的主路径由 N1 与第三轮 M11 钉住，故不阻塞。补法有现成样板：同文件
  `TheResultScreenSaysWhatAPaidFlipCosts`（:277-297）对兄弟字段 `PaidFlipCost` 正是三条支路一起扫。
- **`DropRulesTests.cs:189-192` 与 `MonsterDropServiceTests.cs:76-80` 给 `GoldDropTable` 的
  波动字典填了 `[86] = 13`，而档案在 86 级声明的是 `9`。** 这两处是怪物金币路径的 fixture，
  `DropRules.GoldAmount` 读的是 `VolumeBand` 而不是 `VariancePercents`，所以该值**完全不参与运算**，
  没有任何行为后果；`DropRulesTests.cs:188` 那句「The archive's own numbers」枚举的是
  基数 1357、四个 rank 倍数、100–110% 三样，字面上也没把 `13` 包进去。
  但它紧挨着 `1357` 摆着，读者很容易当成档案值 —— 而「全档案都是 13」正是契约 revision 1
  被 BF-3 推翻的那个错误。建议改成 `9`，或换一个明显不像档案值的数字。
- **`ClearRewardCardFace` 的两条 CS1573 仍在**（`IClearRewardRuleService.cs:78-84`：只给 `Gold`
  写了 `<param>`，`Kind` 与 `Slots` 没有）。全仓 292 条同类、且未开 `GenerateDocumentationFile`，
  属既有惯例，不阻塞。
- **空行残留仍在**（前三轮已列，本轮未处理）：`ClearRewardRules.cs:485-486` 连着两个空行；
  `ClearRewardArchiveTests.cs`、`ClearRewardRulesTests.cs`、`ClearRewardRuleServiceTests.cs`
  的类尾各多一个空行；`PvfClearRewardRateSource.cs` 的 `UnreadSections` 结尾多一个空行。
  全仓未开 StyleCop，编译不报。
- **`PaidCardFlipCostTests.cs:124` 的 `Assert.NotEqual(0, result.GoldPaid)` 断的是一次付费翻牌**，
  而生产上付费面的金币恒为 0，这一条只因该文件的桩给两种牌同一个 `FaceGold = 37` 才成立。
  该断言是基线原样、本次未改（RQ-009 因此不受影响），但它现在描述的是生产到不了的状态。
  若日后把那个桩改成贴近生产，这一条要跟着改。
- **两个生成器同种子**（`ClearRewardRuleService.cs:203` 与 `:217` 都是 `new DropRandom(seed)`）：
  确定性与「金币不挪动牌面抽取」都成立（`What_the_gold_spends_of_the_generator_never_reaches_the_positions`
  钉住），但两条序列完全相同，金币值与第 0 格抽取由同一批随机数派生。目前无人依赖独立性，记录备考。
- **既有 flake（非本变更集引入）**：`FinishQuestCommandHandlerTests` 的 SQLite
  `ObjectDisposedException`。本轮 20 次全量运行（基线 1 次 + 变异 18 次 + 还原后 1 次）**一次也没有复现**。

## 结论

第三轮指定的两处整改**都真的做完了，且新判据不是空过的**：N1、N2 两条原样变异各红 1 条，
红的是断言失败、`error CS` 为 0，新增常量 `PaidFaceGold = 0` 与同文件其余常量互不相同。
顺带的「Three → Four」也改对了，并补上了金币表这一项。

本轮把「取错了也看不出来」这一族做成清册后逐维度证实：21 个维度里 18 个已被判据钉死
（18 条变异中 15 条必红，含前三轮六条链路的重跑）。剩下三条绿的，
经查一条是**基线就有、本次未触碰**的既有缺口（N7），两条落在**代码自证不可达或需角色行消失**的
防御支路（N17 / N18），玩家可达的路径均有判据。

公式仍只含三个有出处的因子，参照服务端那四个无出处因子一个未进；三处档案陈述一致且属实；
既有规则层取值逐值未动（65 条删除行逐条审过，全部是结构性改写或金币相关）；
无探针残留、无超范围改动；`GameProtocol.ClearRewardCardGold` 已退役且全仓源码无引用；
全量判据 1993 / 0 / 2，跳过数停在 2，符合 SC-8。

Spec 的 RQ-001..010 **逐条**都有至少一条能报红的判据。契约 SC-1..SC-8 全部满足；
SC-9（实机屏幕观察）按契约属验收阶段，需实机运行与使用者确认，不在本次 change 评审的可判范围。

裁决 `approved`。上面的观察项均不阻塞本次交付；其中 N7 与 `[86] = 13` 两条建议顺手处理或另开事项。
