# TASK-10 — 出现概率按 revision 4 的式子重算

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

把一个牌位是否有东西的算法，改成契约 revision 4 第 1–5 条定下的式子：

```
刷图基础(按房间数查表，超出声明范围取最大一档 6000)
  + 难度增益(加法，照抄归档，含难度 4 低于难度 3 的倒挂)
  + 骰子(1..10000) × 组队增益
  > 档位阈值([drop prob]，今天只取 default = 10000)
```

并撤掉 revision 3 遗留的探索度实现（`ExplorationRate` 及其调用）——它已被取代。

- Referenced spec requirements: RQ-008（另 RQ-013 就其中的自定项）
- Referenced plan sections: `plan.md` 的 T-010；边界 BD-022
- Owned boundary: BD-022
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`
- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
- 上述各处对应的判据文件

- Dependencies: TASK-08
- Required dependency outputs: 前八个任务的全部产出
- Acceptance conditions:

1. 合成方式与上式一致；四个自定项（加法合成、房间上限 6000、骰子乘组队、阈值读法）
   各自在其所在之处具名标明为本服务器的决定，并写明归档没有说这件事。
2. **房间数超出归档声明范围的真副本，牌面仍可能有内容**；该判据必须先在现实现下变红。
3. 探索度实现（`ExplorationRate`）连同其调用一并撤除，不留死代码。
4. 难度最低一档不再清空整面——原「最低难度全空」的判据与期望值须按新式子更新，
   **且更新后的期望值要用归档已知值手算核对过**。
5. 全量判据不低于交付前基线，失败为零，跳过仍为 2。

- Task-specific authorization: 根契约 revision 4 覆盖本任务。合并、推送、建 PR、删分支或清理 worktree 不在其内。
- Required methods:

- `test-driven-development`：先拿到红，再让它绿。
- 归档读法在 XML doc remarks 里写明段落出处；本服务器自定的值具名标明。
- `System.*` using 在前；`TreatWarningsAsErrors`。
- 变异检查：施加变异时让 mtime **前进**；还原用 **sha256 校验内容**；
  **编译失败不是变红**；至少先观察到一个变异真的变红，才能信其余。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-024 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` --filter ClearReward | 全绿；含房间数超范围的真副本用例 |
| CK-025 | `Non-command:` 用归档已知值手算一组期望几率，与实现逐值比对 | 逐值相符 |
| CK-030 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于基线，失败零，跳过 2 |

- Required evidence: CK-024/CK-030 的运行输出；CK-025 的手算对照表；先红后绿的那条红的原始输出。

## Execution Result — round 1

### 做了什么

把一个牌位是否有东西的判定，从 revision 3 的「四数相乘」改成 revision 4 的式子：

```
刷图基础([reward item rate per map max count]，按房间数查表，无行则取声明的最大一档)
  + 难度增益([dungeon difficulty drop bonusrate]，加法)
  + 骰子(1..10000) × 组队增益([party member drop bonusrate])
  > 档位阈值([drop prob] 的 default 档，本档案每个等级段都是 10000)
```

`ClearRewardChance.Rate` 的含义随之从「乘出来的分子」变为「10000 个骰子值里有多少个能过阈值」；
`DropRandom.Hits(rate, basis)` 掷 1..basis 且 `<= rate` 为中，与「高值获胜」互为镜像、分布相同，
这一点写在 `ClearRewardChance` 的 remarks 里。

四个自定项各自在其所在之处具名，并写明归档没有说这件事：

| 自定项 | 具名位置 |
|---|---|
| 加法合成 | `ClearRewardRules.ChanceOf` remarks 第一条 bullet（另在 `ClearRewardRules` 类级 remarks 的四条清单里列出） |
| 房间数超范围取最大一档 | `ClearRewardRules.MapBaseRate` remarks |
| 组队增益乘在骰子上 | `ClearRewardRules.ChanceOf` remarks 第二条 bullet |
| `[drop prob]` 作阈值、且只取 `default` | `ClearRewardRules.ChanceOf` remarks（读作阈值）与 `ClearRewardRuleService.ThresholdProfile`（取哪一档） |

分母 10000 按实情另写：它有**归档内部旁证**而非本服务器凭空定的——`[drop kind prob]`
两段相加为 10000、`[drop item type prob]` 四段相加为 10000、`default` 档本身每段即 10000。
这三条写在 `ClearRewardRateOptions` 的 remarks 里，并明说「档案哪里都没有声明分母，旁证不是声明」。
前两条相加为 10000 的事实在 `ClearRewardArchiveTests` 里有断言钉住。

撤除的东西：

- `ClearRewardRules.ExplorationRate` 及其唯一调用点 `ClearRewardRuleService.ScaleFor`，两者一并删除。
- `ClearRewardRuleService.ProfileFor(kind)`：revision 4 第 2、7 条决定下，两种牌同阈值同概率，
  按牌型选 `event` 档的读法随之撤销。`[gold card create rate]` 因此不再参与任何计算
  （仍被内容源读入并断言，见「未查明与遗留」第 3 条）。

### 判据

**先红的那一条（TDD 的红）** —— 新增
`ClearRewardArchiveTests.A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row`，
用真档案副本 144（五个迷宫 10/6/4/6/4，其中 10/6/6 三个超出归档声明的 1–5 房）产生牌面。
在改动前的实现下原始输出：

```
[xUnit.net 00:00:00.57]     Dfo.Server.Application.Tests.World.ClearRewardArchiveTests.A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row [FAIL]
  失败 Dfo.Server.Application.Tests.World.ClearRewardArchiveTests.A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row [376 ms]
  错误消息:
   Assert.Equal() Failure: Values differ
Expected: 7400
Actual:   1400
  堆栈跟踪:
     at Dfo.Server.Application.Tests.World.ClearRewardArchiveTests.A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row() in ...\ClearRewardArchiveTests.cs:line 530

失败!  - 失败:     1，通过:     0，已跳过:     0，总计:     1，持续时间: 385 ms - Dfo.Server.Application.Tests.dll (net10.0)
```

**CK-024**（`--filter ClearReward`，全解决方案）：

```
已通过! - 失败: 0，通过: 21，已跳过: 0，总计: 21 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败: 0，通过: 92，已跳过: 0，总计: 92 - Dfo.Server.Application.Tests.dll (net10.0)
```

共 113 通过、0 失败、0 跳过；含上述房间数超范围的真副本用例。

**CK-030**（全量）：

```
Dfo.Server.Protocol.Tests        479 通过 / 0 失败 / 0 跳过
Dfo.Server.Networking.Tests       51 通过 / 0 失败 / 0 跳过
Dfo.Pvf.Tests                     60 通过 / 0 失败 / 0 跳过
Dfo.Server.Persistence.Tests     248 通过 / 0 失败 / 0 跳过
Dfo.Server.Host.Tests              4 通过 / 0 失败 / 0 跳过
Dfo.Server.Infrastructure.Tests  215 通过 / 0 失败 / 2 跳过
Dfo.Server.Application.Tests     898 通过 / 0 失败 / 0 跳过
```

合计 **1955 通过 / 0 失败 / 2 跳过**，基线为 1952 / 0 / 2。+3 的来源可逐条对上：
新增 1 个 Fact，`ClearRewardArchiveTests` 的概率 Theory 由 14 行增至 16 行（+2）；
`ClearRewardRulesTests` 与 `ClearRewardRuleServiceTests` 的用例数净持平（各自增删相抵）。

**CK-025 手算对照表** —— 通过的骰子值个数按下式手算，未从实现反推：

```
缺口   = 阈值 − 刷图基础 − 难度增益
落败数 = floor(缺口 × 10000 / round(组队倍率 × 10000))
通过数 = 10000 − 落败数
```

| 房间 | 刷图基础 | 难度档 | 难度增益 | 队伍 | 倍率(取整到万分) | 缺口 | 落败 | 通过 | 概率 | 实现 |
|---|---|---|---|---|---|---|---|---|---|---|
| ≥5 | 6000 | 2 | 1000 | 1 | 10000 | 3000 | 3000 | 7000 | 70.00% | 7000 ✓ |
| 1 | 2000 | 0 | 0 | 1 | 10000 | 8000 | 8000 | 2000 | 20.00% | 2000 ✓ |
| 5 | 6000 | 3 | 1400 | 4 | 16000 | 2600 | 1625 | 8375 | 83.75% | 8375 ✓ |
| 5 | 6000 | 3 | 1400 | 1 | 10000 | 2600 | 2600 | 7400 | 74.00% | 7400 ✓ |
| 10 | 6000 | 3 | 1400 | 1 | 10000 | 2600 | 2600 | 7400 | 74.00% | 7400 ✓ |
| 6 | 6000 | 3 | 1400 | 1 | 10000 | 2600 | 2600 | 7400 | 74.00% | 7400 ✓ |
| 4 | 5000 | 3 | 1400 | 1 | 10000 | 3600 | 3600 | 6400 | 64.00% | 6400 ✓ |
| 5 | 6000 | 3 | 1400 | 3 | 14000 | 2600 | 1857 | 8143 | 81.43% | 8143 ✓ |
| 5 | 6000 | 3 | 1400 | 2 | 12000 | 2600 | 2166 | 7834 | 78.34% | 7834 ✓ |
| 1 | 2000 | 1 | 600 | 3 | 14000 | 7400 | 5285 | 4715 | 47.15% | 4715 ✓ |
| 5 | 6000 | 0 | 0 | 1 | 10000 | 4000 | 4000 | 6000 | 60.00% | 6000 ✓ |
| 5 | 6000 | 4 | 1200 | 1 | 10000 | 2800 | 2800 | 7200 | 72.00% | 7200 ✓ |

所用归档值：`[reward item rate per map max count]` 1→2000 2→3000 3→4000 4→5000 5→6000；
`[dungeon difficulty drop bonusrate]` 0 600 1000 1400 1200；`[party member drop bonusrate]`
1 1.2 1.4 1.6；`[drop prob]` 的 `default` 档每段 10000。表中每一格都由这些数与上式手算，
再与实现逐值比对。控制器给的三个数（70% / 20% / 83.75%）独立重算后一致。

难度 3（7400）高于难度 4（7200），倒挂照归档原样透传。

### 被改红后更新的既有判据

**「最低难度全空」不再成立**，因为加法下 0 是「不加」而不是「清零」。相关三处：

1. `ClearRewardArchiveTests.The_archives_lowest_difficulty_empties_every_position`
   → `The_archives_lowest_difficulty_no_longer_empties_a_face`，期望值 **6000/10000**：
   5 房刷图基础 6000 + 难度增益 0 = 6000，缺口 4000，落败 4000，通过 6000（对照表第 11 行）。
   同时断言同一局难度升一档为 6600（缺口 3400）——两个数都由归档已知值手算，未反推。
2. `ClearRewardRuleServiceTests.The_lowest_difficulty_leaves_every_position_empty`
   → `The_lowest_difficulty_adds_nothing_and_empties_nothing`：fixture 的刷图基础设为 6000、
   阈值 10000，期望 6000/10000，同一手算。
3. `ClearRewardRulesTests.The_lowest_difficulty_makes_a_position_impossible`
   → `The_lowest_difficulty_adds_nothing_and_leaves_the_map_base_standing`，同上。

其余被改的既有判据：概率 Theory 全表期望值重算（见对照表）；
`A_paid_face_is_drawn_at_the_event_profile_and_its_own_rate` → `Both_faces_of_one_clear_carry_the_same_odds`
（revision 4 第 7 条）；`The_kind_of_a_face_decides_the_profile_and_the_scale_it_is_drawn_at`
→ `Neither_the_kind_nor_the_share_walked_reaches_the_odds`；
`One_seed_does_not_make_the_two_kinds_one_face` → `One_seed_answers_the_same_face_for_both_kinds`
（同概率后，一个种子确实给出同一张牌面；调用方靠不同种子区分，`CardRewardDomainService.SeedFor` 早已如此）；
`No_combination_the_archive_can_reach_passes_the_basis` → `No_occasion_the_archive_describes_is_certain_or_impossible`
（扫描全部刷图基础 × 难度 × 队伍，最陡 8375、最缓 2000，两端均手算）。

## Executor Self-check — round 1

### 判据数字（如实）

- 交付前基线：1952 通过 / 0 失败 / 2 跳过（控制器给出，我未独立重跑改动前的全量）。
- 交付后全量：**1955 通过 / 0 失败 / 2 跳过**。+3 的来源已在上文逐条对上。
- CK-024（`--filter ClearReward`）：113 通过 / 0 失败 / 0 跳过。
- 先红→后绿：红的原始输出见上（Expected 7400 / Actual 1400）。

### 变异检查的真实结果

做法：改动前记 sha256；施加变异后把 mtime 推进至少 2 秒再跑；跑完还原并再次推进 mtime，
用 sha256 与改动前比对确认字节一致。判据命令为
`PVF_ARCHIVE_PATH=... dotnet test Server/tests/Dfo.Server.Application.Tests/... --filter ClearReward`（92 条）。
**先单独跑 M1 确认能观察到真红，再跑其余六条**；每条都检查了是「测试失败」而非「编译失败」
（输出无 `error CS`，且有 `失败! - 失败: N，通过: M` 的汇总行）。

| 变异 | 内容 | 真实结果 | 还原校验 |
|---|---|---|---|
| M1 | 缺口不减难度增益（`shortfall = threshold - mapBaseRate`） | **红** 49 失败 / 43 通过 | sha256 一致 |
| M2 | `MapBaseRate` 查不到时返回 null（回到 revision 3 的「查不到即零概率」） | **红** 9 失败 / 83 通过 | sha256 一致 |
| M3 | `MapBaseRate` 取最大**费率**而非最大**房间数** | **红** 1 失败 / 91 通过 | sha256 一致 |
| M4 | 去掉组队倍率与分母（`losing = shortfall`） | **红** 10 失败 / 82 通过 | sha256 一致 |
| M5 | 阈值档由 `default` 改 `event` | **红** 25 失败 / 67 通过 | sha256 一致 |
| M6 | 组队倍率取整由四舍五入改为截断（1.4 → 13999） | **红** 2 失败 / 90 通过 | sha256 一致 |
| M7 | 落败骰值数 +1（边界偏一） | **红** 45 失败 / 47 通过 | sha256 一致 |

M6 起初能存活：写完实现后我发现判据里没有任何一行能区分 14000 与 13999。
补了两行才钉住——`ClearRewardRulesTests` 的 `[InlineData(SmallestMapRate, 600, PartyOfThree, 4715)]`
与 `ClearRewardArchiveTests` 的 `[InlineData(EndgameLevel, 1, 1, 1, 3, 4715)]`；
4715 由 7400 / 1.4 = 5285.71… 手算得出（截断实现会给 4714）。M6 恰好只打红这两行，与预期相符。

### 改了哪些文件

只在契约 Permitted files 之内，共六个：

- `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`
- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
- `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRulesTests.cs`
- `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs`
- `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs`

六个文件均无 BOM、`\r` 字节计数为 0（按字节数核，非 `grep -c`）、末尾有换行。

### 未查明、越界与不确定的地方

1. **越界项（未自行处理，请控制器裁定）**：
   `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs:150` 的 XML 注释里仍有一处
   `<see cref="ClearRewardRules.ExplorationRate"/>`，指向我删掉的成员。该文件不在 TASK-10 的
   Permitted files 里（它在 TASK-12 的清单里），所以我没有动它，也没有为此停下——因为它不阻塞交付。
   **编译不受影响**：本仓未开 `GenerateDocumentationFile`，CS1574 因此不产生，
   实测 `dotnet build` 为 0 警告 0 错误。但这是一条指向不存在成员的文档引用，需要有人修。
2. **`ClearRewardOccasion.VisitedMapCount` 保留但已无人读取。** 新式子只看房间总数。
   删掉它会改到 `CardRewardDomainService.cs`（在 TASK-11 的 Permitted files 里）与
   `CardRewardDomainServiceTests.cs`，两者都在 TASK-10 边界之外，故保留，并在其 param 注释里
   写明「合成不读它，只是随局携带的事实」。若控制器认为这也算「死代码」，需由 TASK-11 一并撤。
3. **`ClearRewardTable.GoldCardCreateRate` 现在没有任何规则读它。** revision 4 第 7 条（两牌同概率）
   把它挤出了合成。内容源仍解析它、判据仍断言它为 1.0，fixture 里也保留了一个非中性的值，
   使得「有人重新开始读它」会被判据抓到。若这不是本意，请回退第 7 条。
4. **83.75% 依赖于把归档的单精度 1.6 当作十进制 1.6。** 单精度存的是 1.60000002384…，
   若直接以实数比较，骰值 1625 会以第八位小数之差挤进去，答案变成 **83.76%**。
   我的取法是「把组队倍率一次性取整到万分之一（1.6 → 16000）后全程整数比较」，
   结果与十进制手算逐值一致。这处判断写在 `ChanceOf` 的 remarks 里，并由
   `A_party_multiplier_stored_as_a_single_does_not_decide_a_dice_value` 与 M6 变异钉住。
   若控制器要的是「照浮点原样比较」，那 83.75% 应改成 83.76%，请示下。
5. **阈值档的等级键仍是角色等级。** revision 4 第 6 条（改用副本 `[basis level]`）我理解为只覆盖
   品级带与付费价两处（TASK-11 的措辞如此），故 `[drop prob]` 的等级段仍按 `occasion.CharacterLevel` 查。
   本档案 `default` 档四个等级段都是 10000，所以**今天这个键取哪个都得同一个数**，行为上无差别；
   但若第 6 条本意也管这一处，需由 TASK-11 一并改。
6. **「最大一档」按最大房间数取，不是按最大费率取。** A21 两者恰好重合（1→2000 单调升到 5→6000），
   在真档案上分不出来；我用一个乱序、且最大费率不落在最大房间数上的字典把这个区别钉成判据
   （M3 变异只打红这一条）。若「最大一档」本意是最大费率，需要改一行。
7. **`default` 之外的五个 profile（`event`/`event2`/三个 `pcroom`）仍无调用方。**
   归档没有给出选档规则，按约束不编。
8. **一处统计性断言不是手算硬值**：`A_face_drawn_from_the_real_pool_stays_inside_the_declared_band`
   里的 `Assert.InRange(drawn.Count, 900, 1300)`。锚是合成概率 7400/10000 × 1600 个牌位 ≈ 1184，
   区间是围绕它取的宽容带（实际抽中数还会因稀有度桶为空而少一些），不是从实现读出来的精确值。
   同一测试里另有一条 `Assert.Equal(7400, service.ChanceOf(occasion).Rate)` 把概率本身钉死。
9. **未做实机验证。** 概率的观感（例如「难度 0 也有六成」是否符合原版体感）没有任何外部证据，
   全部依据是归档数值加本服务器的四条决定。

## Controller Disposition — 三处停手报告的裁定

执行者三处都**没有自行扩界**，报了上来。三处都做对了，尤其第一处。

### 裁定一：整数比较，保留 83.75%

归档把组队倍率存为单精度，`1.6f` 实为 `1.600000023841858`。控制器独立复算：
浮点比较与整数比较**只在骰值 1625 这一个点上分歧**（8376 对 8375），即 83.76% 对 83.75%。

**判整数。** 理由不是哪个数更接近归档——归档没有说过怎么比——而是**可复现**：
浮点收缩、FMA、不同 JIT 都可能让同一个种子在两台机器上给出不同的牌，
而本服务器的牌面是按种子定死、事后必须能重放的。整数比较把这一整类不确定性去掉。

这一项本身也是自定项，须与另四项同样具名。

### 裁定二：那处注释不只是悬空引用，是说反了 —— 见 Contract Amendment 1

### 裁定三：`VisitedMapCount` 交 TASK-11

字段还在 `IClearRewardRuleService.cs:110`，**全仓无人读**。删它要动
`CardRewardDomainService.cs`，那是 TASK-11 的文件，执行者停手正确。转 TASK-11。

### 裁定四：`[drop prob]` 档位的等级键也交 TASK-11

`ClearRewardRuleService.cs:93` 选档位等级段用的仍是 `occasion.CharacterLevel`。
契约 revision 4 第 6 条点名的是品级带与付费价，**没点到这一处**——但它是同一个问题的第三处。
今天不影响结果（`default` 四个等级段全是 10000），**正因为不影响，才特别容易一直错下去**。
交 TASK-11 一并裁定：要么随另两处改用副本基准等级，要么明确记为「刻意仍用角色等级」并写明理由。

## Contract Amendment 1 — 为修一处说反了的注释扩界（本条写在改动之前）

`Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs:147-151` 的散文写着
该费率「**scales how much of the maze was walked rather than standing as a flat rate for a
maze of that size**」——那是 revision 3 的探索度读法，**revision 4 定的正好相反**。
它还 `<see cref>` 到已删除的 `ClearRewardRules.ExplorationRate`。

所以这不是"悬空引用"这么轻：**读到它的人会得到与实现相反的规则**，而且编译器不会说话
（本仓未开 `GenerateDocumentationFile`）。留着它穿过 TASK-11、TASK-12 两轮更糟。

**Permitted files 增补**：`Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`，
**仅限该 `<param name="MapCountRates">` 一段的散文**，不得改任何签名、逻辑或其他段落。

**本次改动由控制器直接执行**（一段注释，无逻辑），并如实记录于此，
由随后的独立评审一并审。

## Independent Quick Review — round 1

- Verdict: **changes_required**
- 评审依据：Task Unit Contract revision 1 + `## Controller Disposition`（四条裁定）+ `## Contract Amendment 1`
- Owned diff identity：无 git 基线可比（本主题全部 `ClearReward*` / `CardReward*` 文件在
  `git status` 里都是 `??` 未跟踪）。改动集按 mtime 认定，2026-09-01 20:00 之后
  `Server/`、`protocol/`、`Docs/` 下被动过的文件共 7 个，与契约 Permitted files（6 个）
  加 Amendment 1 允许的一个完全重合，无第八个：

  | 文件 | mtime |
  |---|---|
  | `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRulesTests.cs` | 21:05:06 |
  | `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs` | 21:05:12 |
  | `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` | 21:07:22 |
  | `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` | 21:09:06 |
  | `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs` | 21:11:28 |
  | `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs` | 21:11:33 |
  | `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs` | 21:19:49（Amendment 1，控制器直改） |

- 本轮亲自跑过的命令：
  - `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter ClearReward`
    —— 基线 **92 通过 / 0 失败 / 0 跳过**，还原后复跑同样 92/0/0。
  - 三次变异（见第六节），每次施加后 `touch` 推进 mtime，跑完用 sha256 校验还原。
  - 全量 CK-030 与 `dotnet build` 按指示未重做（控制器已独立复核）。

### 一、式子实现：独立手算逐值核对，相符

`ChanceOf`（`Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs:238-257`）的整数式子
等价于 `mapBase + diff + d × party > threshold, d ∈ 1..basis`：
`shortfall = threshold − mapBase − diff`；`scaledParty = round(party × basis)`；
`losing = floor(shortfall × basis / scaledParty)`；`Rate = basis − losing`。
两端边界也对：`shortfall < 0` 直接满值；`shortfall == 0` 时 `losing = 0`，
即「恰好等于阈值不算过，但任何骰值都补得上」，与 remarks 一致。

不看执行者对照表，自选四组重算：

| 房间 / 难度 / 队伍 | 手算 | 实现 |
|---|---|---|
| 5 房 6000 + 难度 2（1000），单人 → 缺口 3000 | 7000（70.00%） | 7000 |
| 1 房 2000 + 难度 0，单人 → 缺口 8000 | 2000（20.00%） | 2000 |
| 5 房 6000 + 难度 3（1400），四人 → 缺口 2600，`round(1.6f×10000)=16000`，`26000000/16000=1625` | 8375（83.75%） | 8375 |
| 1 房 2000 + 难度 1（600），三人 → 缺口 7400，`round(1.4f×10000)=14000`，`74000000/14000=5285.71→5285` | 4715 | 4715 |

`DropRandom.Hits`（`Server/src/Dfo.Server.Abstractions/World/DropRandom.cs:71`）确为
`Next(basis) <= rate`，`Next`（同文件 `:62-68`）确为 1..bound，
所以 `ClearRewardChance.Rate` 的「镜像」说法（`ClearRewardRules.cs:12-18`）属实。

### 二、AC-011 被真档案钉住，且在「查不到即零」的旧读法下确实变红（自行验证）

`A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row`
（`Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs:537`）
用的是**真档案**：`ContentArchive.CreateContentProvider()` + `IDungeonDefinitionSource.GetAsync(144)`，
并先断言真副本的房间数为 `[10,6,4,6,4]`、其中 `[10,6,6]` 是表里查不到的，不是造的 fixture。

我用变异 R-B（删掉 `MapBaseRate` 的最大一档兜底、查不到即 `null`，就是旧的「查不到即概率为零」）
复现：**该判据变红**；同时 `Every_maze_of_an_ordinary_dungeon_carries_a_chance`
（`ClearRewardArchiveTests.cs:465`，它才是断言「牌面仍可能有内容」的那条——真档案 + `FaceFor` +
`Assert.Contains(slot => !slot.IsEmpty)`）也变红。**AC-011 在旧实现下真的会红，成立。**

非阻塞观察：plan T-010 步骤 1 的措辞是「用真副本**产生牌面**的判据」，
而执行者新写的那条只调 `ChanceOf` 不调 `FaceFor`；「产生牌面且有内容」的断言落在既有的
`Every_maze_of_an_ordinary_dungeon_carries_a_chance` 上。两条合起来覆盖 AC-011，
但不是同一条判据同时满足「先红」与「产生牌面」。

### 三、【阻塞】第五个自定项（整数比较）未按 Controller Disposition 裁定一具名

裁定一明写：「这一项本身也是自定项，**须与另四项同样具名**」。现状是**没有做**：
全仓仍一律写「四项」，第五项只被解释、未被认领。

- `ClearRewardRules.cs:79` —— `<b>Four decisions here are this server's own and the archive states none of them.</b>`，清单里只有四条，无整数比较。
- `ClearRewardRules.cs:48` —— `...more than the four decisions named on ClearRewardRules`。
- `ClearRewardRules.cs:176` —— `Two of the four decisions ... are made here`。
- `ClearRewardRules.cs:205` —— `the third of the four ... the fourth`。
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs:52-53` ——
  `The fourth of the four decisions this server makes about this file, and the archive states it no more than the other three`。
- `ClearRewardRules.cs:213-219` 确实有一整段讲整数比较，但它**只说了「为什么会有浮点伪影」**，
  既没有「这是本服务器的决定」，也没有「归档没有说这件事」这两句标记语——
  而另四项每一处都有（如 `:114` 的
  `<b>This server's decision, and the archive says nothing about it: ...</b>`）。
- 且该段给出的理由是「一万分之一的牌位不该由第八位小数决定」，
  **不是**裁定一真正的理由（**可复现**：浮点收缩 / FMA / 不同 JIT 会让同一种子在两台机器上
  给出不同的牌，而本服务器的牌面必须能按种子重放）。裁定的依据没有落到代码里。

需要的补救全部落在 TASK-10 的 Permitted files 之内，属可局部修复。

（顺带：这条决定本身**判据是钉住的**。变异 R-C 把整数比较换成实数比较，红 4 条，
含 8375 两行与 `A_party_multiplier_stored_as_a_single_does_not_decide_a_dice_value`。
所以问题只在「没具名」，不在「没钉住」。）

### 四、【阻塞，需控制器裁边界】同一文件里还留着第二处说反了的散文

Amendment 1 修的是 `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs:145-154`
的 `MapCountRates` 段——**这一段改后与 revision 4 一致，我核过**
（「stands as the base value for a maze of that size, which the difficulty bonus and the dice are
then added to」，并把「超范围取最大一档」指给 `MapBaseRate`）。

但同一文件往下十二行，`<param name="GoldCardCreateRate">`（`ClearRewardContent.cs:166-178`）
仍写着：

> `GeneratePaidItemCard` passes this value in the same argument position that
> `GenerateFreeItemCard` passes **the exploration rate**. **It is the paid face's scale, and the
> paid face reads it instead of reading the map-count table at all.**

这与 revision 4 第 7 条（两牌同概率）**正好相反**：本服务器的付费牌并不读
`[gold card create rate]`，恰恰**是**读地图房间数表的。该句没有「这是参照服务端的读法」这一限定语，
也没有紧邻其上的 `ItemTypeRates` 段（`:127-135`）那句
「Carried verbatim all the same: nothing on this path applies it」的免责说明——
而 `[gold card create rate]` 现在**真的一个读者都没有**（执行者自查第 3 条已如实报告）。
这正是 Amendment 1 说的那种「读到它的人会得到与实现相反的规则」，只是控制器只修了上面那段。

Amendment 1 的授权仅限 `<param name="MapCountRates">` 一段，故此处**不得由执行者自行扩界**，
请控制器决定：扩 Amendment，或明确交 TASK-11 / TASK-12 并记账。

### 五、其余各条：核过，通过

1. **分母 10000 被如实区别对待。** `ClearRewardRateOptions` remarks（`ClearRewardRules.cs:40-50`）
   明说「归档没有声明，但档案内部有旁证，而合成里只有这一项有」，列出三条旁证，并补一句
   「None of that is a declaration of a basis」。三条旁证在
   `ClearRewardArchiveTests.The_archive_declares_the_numbers_the_composition_is_built_from`
   里都有断言（`DropKindRates.Sum()`、`ItemTypeRates.Sum()`、`default` 四段皆 10000）。
   与四（五）个自定项确实不同级，处理得当。
2. **既有判据的期望值是手算的，不是从实现反推的。** 三处「最低难度」判据的新期望值都能由
   同文件断言过的归档值直接推出：`ClearRewardArchiveTests.cs:354` 的 6000 =
   `MapCountRate(5)=6000 + DifficultyRates[0]=0`，缺口 4000 → 6000；同处 6600 = 加难度 1 的 600；
   `ClearRewardRulesTests.cs:138` 的 6000 同式；`ClearRewardRuleServiceTests.cs:61` 走 fixture 的
   `MapBase=6000 / threshold=Basis=10000`。fixture 的 `CertainDifficulty` 用
   `DifficultyRates = [0, Basis - MapBase]`，即恰好顶到阈值 → 必中，也是可手算的。无反推痕迹。
3. **死代码撤干净。** 全仓 `--include=*.cs` 已无 `ExplorationRate` / `ScaleFor` / `ProfileFor`，
   剩余命中只在 `plan.md` 与 TASK-09 / TASK-10 的历史记述里。
   `[gold card create rate]` 无读者一事**在判据侧如实交代**（`ClearRewardArchiveTests` 的
   「Nothing on this path reads it」注释 + `Assert.Equal(1.0, table.GoldCardCreateRate)`；
   fixture `PaidFaceRate` 的「Declared and never read」），**但在内容层没有**——见上第四节。
4. **越界**：无。见开头 mtime 表。
5. **格式**：七个文件均无 BOM、`\r` 字节为 0、末尾有换行（按字节核）。
6. **分层**：纯规则在 `Abstractions/World` 的静态类、规则服务在 `Application/World` 读内容源，
   与 AGENTS.md「CMD handler → 业务服务 → 规则服务 → 纯规则」一致；规则服务仍只拿 facts，
   未新增世界状态读取。

### 六、我自己重做的变异（三条，均已还原并 sha256 校验）

基线 `ClearRewardRules.cs` sha256 = `57d8a10fa9840bb043ee91dc3e76f877bb67e1df770a5a6eef4be8fdb452075d`；
`ClearRewardRuleService.cs` 全程未动，sha256 = `b0dbf5e7022269033aa075e02e0c95e3ca34c33ab8f3606a0ddfd4c8488453e6`。
每次施加变异后 `touch` 推进 mtime；三次输出里均无 `error CS`，且都有 `失败! - 失败: N，通过: M`
汇总行（确认是判据变红而非编译失败）。

| 变异 | 内容 | 我跑出的结果 | 与执行者对照 | 还原 |
|---|---|---|---|---|
| R-A（M6 那一类：倍率截断） | `(long)Math.Round(partyRate*basis, AwayFromZero)` → `(long)(partyRate*basis)` | **红 2 失败 / 90 通过**，正是 `ClearRewardRulesTests.cs:113` 与 `ClearRewardArchiveTests.cs:257` 两行 4715 | 与其 M6 逐数相符 | sha256 回到 `57d8a10f…` |
| R-B（M2 那一类：查不到即零） | 删掉 `MapBaseRate` 的最大一档兜底，返回 `null` | **红 9 失败 / 83 通过**，含 `A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row`、`Every_maze_of_an_ordinary_dungeon_carries_a_chance`、`Both_faces_of_one_clear_carry_the_same_odds`、`No_occasion_the_archive_describes_is_certain_or_impossible`、超范围三行 Theory | 与其 M2 逐数相符 | sha256 回到 `57d8a10f…` |
| R-C（额外：验裁定一） | `losing = shortfall*basis/scaledParty` → `(long)Math.Floor(shortfall/partyRate)`（实数比较） | **红 4 失败 / 88 通过**，含 8375 两行与 `A_party_multiplier_stored_as_a_single_does_not_decide_a_dice_value` | 执行者未做此条；结论一致（83.75 与 83.76 分得开） | sha256 回到 `57d8a10f…` |

还原后复跑 `--filter ClearReward`：**92 通过 / 0 失败 / 0 跳过**，与基线一致。
本轮未修改任何生产或判据文件。

### 七、非阻塞观察（不要求本轮处理）

1. `ClearRewardContent.cs:150-151` 控制器新写的两处 `<see cref="ClearRewardRules.ChanceOf"/>` 与
   `<see cref="ClearRewardRules.MapBaseRate"/>` 在 `Dfo.Server.Abstractions.Content` 命名空间下
   **仍解析不到**（该文件没有 `using Dfo.Server.Abstractions.World;`；同文件 `:160` 的
   `RarityLadder` 能解析是因为它就在 Content 命名空间里）。今天无害——本仓未开
   `GenerateDocumentationFile`，CS1574 不产生——但被删的 `ExplorationRate` cref 之所以「悬空」，
   一半原因也是这个，修完仍悬空。
2. `ClearRewardContent.cs:148-149` 的「the difficulty bonus and **the dice** are then added to」
   略去了骰子先乘组队增益这一步。方向不错，只是不如 `ChanceOf` 自己那句准确。
3. 「先红」的原始输出无法从仓库独立复现：本主题所有 `ClearReward*` 文件在 git 里都未跟踪，
   没有改动前的版本可回退比对，该证据只能采信执行者贴出的输出。R-B 变异从另一侧给了同等力度的
   佐证（同一条判据在「查不到即零」下确实红）。
4. `A_face_drawn_from_the_real_pool_stays_inside_the_declared_band` 的
   `Assert.InRange(drawn.Count, 900, 1300)` 是统计带而非硬值（执行者自查第 8 条已声明），
   同一测试里有 `Assert.Equal(7400, …)` 兜底，可接受。

### 八、要求的补救

1. （第三节）把整数比较作为第五个自定项具名：改 `ClearRewardRules.cs:48/79/176/205` 与
   `ClearRewardRuleService.cs:52-53` 的「四」，并在 `ClearRewardRules.cs:213-219` 补
   「本服务器自定、归档未声明」以及裁定一的**可复现**理由。全部落在 Permitted files 内。
2. （第四节）`ClearRewardContent.cs:166-178` 的 `GoldCardCreateRate` 散文与 revision 4 第 7 条相反，
   且该字段现已无读者。**需控制器先裁边界**（扩 Amendment 1 或转 TASK-11 / TASK-12），
   执行者不得自行动手。

## Controller Disposition — 评审 `changes_required` 成立，两条全接，其中一条是控制器的错

两条阻塞发现控制器都独立复核过，**都成立**。

### 发现一成立：第五个自定项确实没具名

裁定一白纸黑字写着「整数比较这一项本身也是自定项，须与另四项同样具名」，
执行者没做，文档四处仍写「四项」。**这是控制器提的要求没有被执行，评审替控制器查了出来。**

### 发现二成立，而且这是控制器自己的错

`ClearRewardContent.cs:166-178` 的 `GoldCardCreateRate` 一段仍写着
「It is the paid face's scale, and the paid face reads it instead of reading the map-count
table at all」，并仍带 `the exploration rate` 字样——与 revision 4 第 7 条（两牌同概率）
正好相反，而该字段如今一个读者都没有。

**控制器在 Amendment 1 里改了同一个文件的另一段，然后宣布那个文件修好了，
没有回头看同一个文件里还有没有别处说反了。** 这正是控制器一路在警告执行者的那个形状：
**修一处错误陈述时，只核自己改的那半句。** 派工时特意写了「别因为是控制器写的就跳过」，
接住了——这条提示存在的理由就是这个。

### 另核实两条非阻塞，同样是控制器的错

- **控制器新写的两个 `<see cref="ClearRewardRules.*"/>` 解析不了。**
  该文件命名空间为 `Dfo.Server.Abstractions.Content` 且**一个 using 都没有**，
  而 `ClearRewardRules` 在 `...World`。控制器修的是一处悬空引用，**引进了两处**。
  （原来那个 `ExplorationRate` 的 cref 同样解析不了，此病早于控制器；但不该被沿用。）
- **控制器写的 `:148-149` 不精确**：说「difficulty bonus and the dice are then added to」，
  漏了骰子要先乘组队增益。

## Contract Amendment 2 — 为修上述四处扩界（本条写在改动之前）

**Permitted files 增补**：`Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs`
的 **`<param name="GoldCardCreateRate">` 一段**（Amendment 1 只授权了 `MapCountRates` 一段），
以及控制器自己写坏的那两处 cref 与那一句。不得改任何签名、逻辑或其他段落。

**须完成的四项**：

1. **给第五个自定项具名**：整数比较。四处「四项」改为「五项」，并按另四项同样的写法
   标明「这是本服务器的决定，归档没有说这件事」。
   **理由要写真正的那一条**：可复现——浮点收缩、FMA、不同 JIT 会让同一个种子
   在两台机器上给出不同的牌，而本服务器的牌面按种子定死、事后必须能重放。
   现有那段只解释了浮点伪影，不是这个理由。
2. **改写 `GoldCardCreateRate` 一段**：按 revision 4，付费面与免费面同概率、
   **不**读这个值；该字段今日无读者，如实写明。参照服务端那个用法要保留为
   **「另一个服务器的读法」而非本仓的规则**——本 Spec 已确立参照服务端不具权威性。
   并像紧邻的 `ItemTypeRates` 那样带一句免责。
3. **两个 cref 写全限定**（`Dfo.Server.Abstractions.World.ClearRewardRules.…`）或另想办法使其可解析。
4. **补上骰子先乘组队增益**那半句。

**不在本条授权内**：该文件的其余任何段落、任何签名或逻辑。
若发现同文件**还有第三处**说反了的散文——**先停下来报，不要顺手改**。
控制器已经因为「顺手以为改完了」错过一次。

## Execution Result — round 2

返工只做 Contract Amendment 2 的四项，一行逻辑未动，四项全部完成。
其中**三项是修控制器在 Amendment 1 里写坏的东西**，一项是修 round 1 我自己没做的事。

### 一、第五个自定项具名：整数比较（这一项是我 round 1 漏做的）

`ClearRewardRules.cs` 与 `ClearRewardRuleService.cs` 里一律写「四项」的五处全部改为「五项」，
并把整数比较按另四项同样的写法认领下来。

顺带把**序数打通了**。改之前两处编号互相打架：类级清单列的四项是
「加法 / 骰子乘组队 / 最大一档 / 阈值读法」，而 `ChanceOf:205` 与 `ClearRewardRuleService:52`
说的「第三 / 第四」却是「阈值读法 / 选哪一档」——把最大一档漏在编号外，且把阈值读法拆成两项。
现在统一为契约那份清单的顺序（第 1 加法、第 2 骰子乘组队、第 3 最大一档、第 4 阈值读法、
第 5 整数比较），选哪一档是第 4 项的另一半而不是独立一项。这没有新增或删除任何一项决定，
只是让「第几」在三个文件里指同一个东西。

| 位置 | 改了什么 |
|---|---|
| `ClearRewardRules.cs:48` | `the four decisions named on` → `the five decisions named on` |
| `ClearRewardRules.cs:79-90` | `Four decisions here are` → `Five decisions here are`；清单按 first/second/third/fourth/fifth 显式编号，末尾加入第五项「comparison is carried out in whole numbers rather than in reals」；`nothing about any of the four` → `any of the five` |
| `ClearRewardRules.cs:179-180` | `Two of the four decisions` → `The first two of the five decisions` |
| `ClearRewardRules.cs:207-213` | 阈值那句 `the third of the four; which profile's bar is taken is the fourth` → `the fourth of the five; which profile's bar is taken is the other half of it`；同段后半按 100 列重排（内容一字未改） |
| `ClearRewardRules.cs:215-232` | 原来只解释浮点伪影的那一段（旧 `:212-219`）整段改写，见下 |
| `ClearRewardRuleService.cs:52-56` | `The fourth of the four decisions ... no more than the other three` → `The other half of the fourth of the five decisions ... no more than the other four`；`for the list` → `for the list of five` |

**理由换成了裁定一真正的那一条。** 旧段落的理由是「一万分之一的牌位不该由第八位小数决定」，
那是在讲浮点伪影难看；现在写的是**可复现**：

> ...the reason for it is reproducibility rather than accuracy -- the file states no way of
> comparing at all, so neither reading is the archive's and neither can be the more faithful one.
> A face on this server is fixed by its seed and has to be replayable from that seed afterwards, and
> a comparison carried out in reals is not replayable in that sense: floating-point contraction, a
> fused multiply-add and one JIT differing from another can each move the last bits of the same
> product, so two machines replaying one seed could deal different cards.

并且带上了与另四项同形的两句标记语——`This server's decision, and the archive says nothing
about it: ...`（原文加了 `<b>`，与 `:114` 那一处同形）。浮点伪影那件事没有丢，降为**第二段**：
它不再是理由，而是「两种读法在哪个值上分得开」的证据（1625 这个骰值，8376 对 8375）。

### 二、`GoldCardCreateRate` 一段改写（这是控制器 Amendment 1 漏改的）

`ClearRewardContent.cs:168-195`。原文说「It is the paid face's scale, and the paid face reads it
instead of reading the map-count table at all」——与 revision 4 第 7 条正好相反。改后分两段：

- **第一段说本仓的事实**：`Nothing on this path reads it, and that is a rule decision rather
  than an omission.` 两面同概率、都由房间数表 + 难度增益 + 骰子 + `default` 阈值合成，
  **两面都不读这个值**；付费牌的专属之处是 `[gold card cost table]` / 这一段 /
  `[gold card blank item]` 三个段落，即「要花钱、有自己的保底」，而不是「按别的概率抽」。
  并按 Amendment 2 的要求带上与紧邻 `ItemTypeRates` 同形的免责句：
  `Carried verbatim all the same, as <see cref="ItemTypeRates"/> is: a declaration dropped from
  the reading is harder to notice later than one read and left unapplied.`
- **第二段把参照服务端降级为「另一个服务器的读法」**：
  `What another server does with it is recorded here as another server's reading, not as this
  file's rule.` 事实照留（`GeneratePaidItemCard` 把它放在 `GenerateFreeItemCard` 填探索度的
  那个实参位上，该实参乘在 profile 费率上，所以在那台服务器上它是付费面的 scale、付费面根本不读
  房间数表），但明写它 `carries no authority over this one`，是「what the number can be for」的
  证据而非本仓的做法。`the exploration rate` 这个说法也随之限定为**那台服务器自己的**探索度
  （`an exploration rate of its own`）——本仓已无此物。

我另去核了参照服务端源码 `E:/Project/DNF/ServerS4A21/Server/DfoServer/Game/Dungeon/ClearRewardGenerator.cs`
才写这一段：`GenerateConfiguredItemCard(context, profile, mapRate, lcg)` 里
`threshold = StandardDropRate/100 * (baseProbability * max(0, mapRate) + difficultyBonus) * (1 - deathPenalty)`，
确实是乘法；`GeneratePaidItemCard` 传的是 `Event` 档而非 `Default` 档。原注释只说了实参位置，
没说它是乘上去的，也没说那边付费牌换了档——现在两点都写进去了，且都挂在「那是另一台服务器」名下。

### 三、两个 cref 改为全限定，并且**验证过它们现在真的解析得到**

`ClearRewardContent.cs:151` 与 `:153`：`<see cref="ClearRewardRules.ChanceOf"/>` /
`<see cref="ClearRewardRules.MapBaseRate"/>` →
`<see cref="Dfo.Server.Abstractions.World.ClearRewardRules.ChanceOf"/>` /
`...MapBaseRate`。没有加 `using`，因为该文件一个 using 都没有，加一个只为注释不值当。

**验证办法**（本仓未开 `GenerateDocumentationFile`，CS1574 不产生，所以肉眼看不出来）：
临时用命令行属性打开文档生成、不改任何文件——

```
dotnet build Server/src/Dfo.Server.Abstractions/Dfo.Server.Abstractions.csproj --nologo
  --no-incremental -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=false
```

再读生成的 `bin/Debug/net10.0/Dfo.Server.Abstractions.xml`：**解析不到的 cref 在 XML 里写成
`cref="!:..."`**，这是可判定的证据而不是看着像。

| 时点 | `ClearRewardTable` 一族的 cref |
|---|---|
| 改前 | `cref="!:ClearRewardRules.ChanceOf"`、`cref="!:ClearRewardRules.MapBaseRate"` |
| 改后 | `cref="M:Dfo.Server.Abstractions.World.ClearRewardRules.ChanceOf(System.Int32,System.Int32,System.Double,System.Int32,System.Int32)"`、`cref="M:...ClearRewardRules.MapBaseRate(System.Collections.Generic.IReadOnlyDictionary{System.Int32,System.Int32},System.Int32)"` |

我新写的 `<see cref="ItemTypeRates"/>` 也一并核了，解析为
`P:Dfo.Server.Abstractions.Content.ClearRewardTable.ItemTypeRates`。
`Dfo.Server.Application` 同法核过，**无任何 `!:` cref**。

### 四、补上骰子先乘组队增益

`ClearRewardContent.cs:148-149`：`which the difficulty bonus and the dice are then added to`
→ `which the difficulty bonus and the dice are then added to, the dice having been multiplied by
the party's bonus first`。

### 判据

**CK-030（全量，判据命令原样）**：

```
Dfo.Pvf.Tests                     60 通过 / 0 失败 / 0 跳过
Dfo.Server.Networking.Tests       51 通过 / 0 失败 / 0 跳过
Dfo.Server.Protocol.Tests        479 通过 / 0 失败 / 0 跳过
Dfo.Server.Persistence.Tests     248 通过 / 0 失败 / 0 跳过
Dfo.Server.Host.Tests              4 通过 / 0 失败 / 0 跳过
Dfo.Server.Infrastructure.Tests  215 通过 / 0 失败 / 2 跳过
Dfo.Server.Application.Tests     898 通过 / 0 失败 / 0 跳过
```

合计 **1955 通过 / 0 失败 / 2 跳过**，与 round 1 交付数字一字不差。
`dotnet build Server/86JP.Server.slnx --nologo`：**0 警告 0 错误**。
本轮是纯文档改动，判据数字本就不该变，也确实没变。

## Executor Self-check — round 2

### 改了哪些文件哪些行

三个文件，全部只动 XML 注释散文，**签名、逻辑、判据文件一律未动**：

| 文件 | 动过的行（改后行号） | 授权依据 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs` | 48、79-90、179-180、207-213、215-232 | Amendment 2 第 1 项（原本就在 Permitted files 内） |
| `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` | 52-56 | 同上 |
| `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs` | 148-155（第 3、4 项）、168-195（第 2 项） | Amendment 2 增补的那两处 |

`ClearRewardRules.cs:209-213` 的重排要单独交代：改「第三 → 第四」那句时把 `:209` 顶到了 109 列，
所以把该段后半按 100 列重新折行了，**文字一字未改**，只是换行位置变了。

三个文件：`\r` 字节计数为 **0**（`tr -dc '\r' | wc -c`，按字节数核，非 `grep -c`）、无 BOM、
末尾有换行、无行尾空白。新写的行最长 100 列，与同目录既有行（该目录既有 90 行超过 99 列，
最长 110 列）同量级。

### 判据数字（如实）

- 全量：**1955 通过 / 0 失败 / 2 跳过**——与 round 1 完全一致。
- `dotnet build`：0 警告 0 错误。
- 未跑变异：本轮没有改任何逻辑，可变异的东西一个都没变，round 1 的七条变异结论原样有效。
- 本轮**没有先红后绿**，因为没有行为改动可红。这不是省略，是这一轮的性质。
- 三个文件改前改后的 sha256 都变了（本来就要变）；未改的其他四个文件我一个字节都没碰。

### 通读整个 `ClearRewardContent.cs` 的结论（本轮重点，明确回答）

**核过了：没有第三处「说反了」的散文。** 291 行逐段核对 revision 4 九条，逐条结论：

| 段落 | 与 revision 4 | 说明 |
|---|---|---|
| `ClearRewardBand` 类级 `:12-16` | 一致 | 「基数与各加成表怎么合成，在做合成的地方具名」——正是现在的分工 |
| `ClearRewardProfile` 类级 `:27-34` | 一致 | 「哪个档适用于哪种场合是调用方的事实，档案只给了名字」 |
| `ClearRewardProfiles` `:50-59`、`:63` | 一致（第 2、7 条） | 「只有 `default` 有调用方」——revision 4 之后这句才真正成立 |
| `ClearRewardBlankItem` `:85-88` | 一致（第 8 条） | 「是否真发出去是规则决定，不是本类型的事」 |
| `ClearRewardTable` 类级 `:108-112` | 一致 | 「这里不做任何 scale/combine/interpret」 |
| `DropKindRates` `:119-126` | 一致 | 与合成无关 |
| `ItemTypeRates` `:127-135` | 一致 | 免责句在，且指向 `ClearRewardRuleService` |
| `PartyMemberRates` `:141-144` | 一致 | 只说「档案存的是每个队伍规模一个倍率」，**没说它乘在哪** |
| `MapCountRates` `:145-156` | 一致（本轮第 4 项修完） | |
| `Rarity` / `GoldCardCosts` / `GoldCardBlankItems` / `PcRoomCardBlankItems` / `GradeBands` | 一致 | 都只说档案段落形状，未涉合成 |
| `GoldCardCreateRate` `:168-195` | 一致（本轮第 2 项修完） | |
| `ProfileNamed` / `PartyMemberRate` / `MapCountRate` / `GoldCardCost` / `GradeBandFor` | 一致 | |
| `IClearRewardRateSource` `:272-291` | 一致 | |

**两处 revision 3 留下的措辞，我没动，报上来请裁定。** 两处都**不是**在陈述合成方式，
所以我判它们不构成「第三处说反了」，但都带着乘法时代的词：

1. **`ClearRewardContent.cs:238`（`DifficultyRate` 的 remarks）**：
   `answering an unlisted index with a neutral multiplier would be indistinguishable from the
   archive's own zero`。这段的**论证在加法下完全成立**（加法的中性元就是 0，正是档案最低难度那一档，
   所以「查不到」与「中性」确实分不开，null 是对的），但 `multiplier` 这个词点名的是乘法。
   一个词。这是最接近「第三处」的一处。
2. **`ClearRewardContent.cs:136-140`（`DifficultyRates` 的 param）**：
   `The first is zero and the fifth is smaller than the fourth, so it is neither a neutral baseline
   nor a monotonic ladder.` 我读它是「别把这张表当成有中性基线的倍率梯子」——在加法下 0 恰恰**是**
   中性的，所以这句在 revision 4 下读起来别扭。它陈述的是原始数字的性质而非合成方式，
   且「倒挂不排序」的意思与第 4 条一致。

两处都在 Amendment 2 **未授权**的段落里（授权只到 `MapCountRates`、`GoldCardCreateRate`
与那两处 cref、那一句），所以**没有顺手改**。请控制器裁：要么再扩一次界（两处都是一两个词），
要么明确记为「知情保留」。

### 越界项与其他发现（未自行处理）

1. **`ClearRewardRules.cs:76` 的 `<see cref="RarityLadder"/>` 同样解析不到。**
   与控制器那两处是同一个病的镜像：`ClearRewardRules` 在 `...World`，`RarityLadder` 在
   `...Content`，该文件也没有 `using Dfo.Server.Abstractions.Content;`。
   本轮的文档生成核查把它抓了出来：XML 里是 `cref="!:RarityLadder"`，挂在
   `T:Dfo.Server.Abstractions.World.ClearRewardRules` 上。
   该行在 TASK-10 的 Permitted files 内，但**不在 Amendment 2 的四项内**，故未动。
   一字之改（写全限定名）即可，请裁定归本轮补、还是交下一轮。
2. **两个判据文件里还写着「四」，本轮按授权范围未改。**
   - `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs:177`
     `none of the four decisions the composition makes has even that much`
   - `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRulesTests.cs:16`
     `at each of the four places a decision is made`

   这两处在契约原始 Permitted files 内（判据文件），但 Amendment 2 第 1 项点名的是
   `ClearRewardRules.cs:48/79/176/205` 与 `ClearRewardRuleService.cs:52-53` 五处，没有它们。
   派工又明写「不得扩大」，所以我停在这里报。**这是本轮已知的一处不自洽：生产代码说五项、
   判据注释说四项。** 若控制器要，两行都是把 `four` 改成 `five`（第二处严格说是「三处地方」
   而非「四处地方」，因为第五项与第一、二项同在 `ChanceOf`）。
   （另：`ClearRewardRulesTests.cs:89` 的 `multiplying the four instead of adding them` 指的是
   参与合成的四个**数**，不是四个决定，不受影响，无须改。）
3. **`Dfo.Server.Abstractions.Quests.IQuestStore` 有三个解析不到的 cref**
   （`!:Carried`、`!:Cleared`、`!:Tracked`）。与本主题无关，是本轮文档生成核查的副产品，
   记一笔备查，未动。

### 我不确定的地方

1. **序数的重排是不是「改了未授权的段落」。** Amendment 2 只说「四处『四项』改为『五项』」，
   我额外把 `ChanceOf:207-208` 的「第三 / 第四」改成了「第四 / 另一半」、并给类级清单加了显式序数。
   我这么做是因为不改序数就会当场自相矛盾：类级清单五项按顺序数下来阈值是第四，
   而 `ChanceOf` 说它是第三。但这**确实超出了「把四改成五」的字面**。如果控制器认为
   应当只改数词、保留原有的序数打架，请说，我改回去。
2. **`GoldCardCreateRate` 那段我写了「付费面也由房间数表合成」这一正面陈述。**
   它是 revision 4 第 7 条的直接推论，且 `Both_faces_of_one_clear_carry_the_same_odds` 判据钉着，
   但严格说这是内容层文件在陈述规则层的事。我保留了它，因为不说这一句，读者仍无从知道
   「那这个值到底该谁读」——原注释的毛病正是把这句说反了而不是没说。
3. **round 1 自查里第 4、5、7 条的不确定仍然成立**（浮点/整数已由裁定一定案并在本轮具名；
   `[drop prob]` 的等级键、五个无调用方的 profile 已转 TASK-11 或记为按约束不编）。
   本轮未触及任何一条。
4. **仍未做实机验证。** 与 round 1 相同，概率的观感没有任何外部证据。

## Controller Disposition — round 2 的四处报告：三处准修，一处准留

控制器独立复核：全量判据 **1955 通过 / 0 失败 / 2 跳过**（与 round 1 一字不差，
符合纯文档轮次的预期）；`dotnet build` 0 警告 0 错误。

### 先记一件方法上的事：执行者把「cref 解析得到吗」从说法变成了量

它用 `-p:GenerateDocumentationFile=true -p:NoWarn=1591 -p:TreatWarningsAsErrors=false`
构建（**不改仓里任何文件**），读生成的 XML——解析不到的 cref 在里面写作 `cref="!:..."`。

**控制器自己跑了一遍，确认可用**，并顺手测了全仓的账：
整个 `Dfo.Server.Abstractions` 程序集只有 **4 个**解析不到的 cref
（`!:RarityLadder`、`!:Tracked`、`!:Cleared`、`!:Carried`），
而控制器那两个已解析为完整签名
`M:Dfo.Server.Abstractions.World.ClearRewardRules.ChanceOf(...)` 与 `...MapBaseRate(...)`。

**这值得单独记**：控制器上一轮引进两个坏 cref 而毫不知情，正是因为**没有任何东西会报错**
——本仓未开文档生成，编译器对此一言不发。执行者做的不是"更仔细地看"，
而是**造了一把尺**。本 Spec 二十余处缺陷的共同形状是「说得顺口但没人核」，
而这一类里最难办的就是"根本没有核的手段"。现在有了。

### 裁定一：序数打通，**准留**

执行者说这超出了「把四改成五」的字面，问是否算越界。**不算，准留。**
改之前类级清单与 `ChanceOf`/`RuleService` 说的「第三/第四」互相打架、把最大一档漏在编号外
——那是一处真的内部矛盾，改回去等于把矛盾放回去。往「一致」的方向收敛不是扩界。

### 裁定二、三、四：三处准修，见 Contract Amendment 3

## Contract Amendment 3 — 收尾三处（本条写在改动之前）

**Permitted files 增补**：无新增文件——下述三处全部落在 TASK-10 契约原有的
Permitted files（含「上述各处对应的判据文件」）之内。Amendment 2 当时把范围钉到五处具体位置，
执行者据此停手是对的；本条把范围放到这三处，**仅此三处**。

1. **两个判据文件仍写「四」**：`ClearRewardArchiveTests.cs:177`、`ClearRewardRulesTests.cs:16`。
   控制器已核实原文属实。生产代码说五项、判据注释说四项，是本轮已知的不自洽，改掉。
2. **`ClearRewardContent.cs:238` 的 `a neutral multiplier`**：论证在加法下仍成立，
   但 `multiplier` 一词点名的是乘法。加法下的中性元是 0 不是 1，措辞改准。
3. **`ClearRewardContent.cs:136-140` 的 `neither a neutral baseline`**：加法下首项的 0
   恰恰就是中性的，这句现在读起来与实现相左。改准；它要陈述的「非单调」那一半属实，保留。
4. **`ClearRewardRules.cs:76` 的 `<see cref="RarityLadder"/>` 解析不到**（World 文件引 Content
   类型、无 using），与控制器上一轮犯的是同一个病的镜像。写成全限定名。

**收尾要求**：改完再跑一次那把尺，**证明 `ClearReward*` 相关的 cref 全部解析得到**，
并报出 Abstractions 程序集剩余 `!:` 的清单。`!:Tracked` / `!:Cleared` / `!:Carried`
与本主题无关，**不要动**，报出来即可。

**不在本条授权内**：其余任何段落、任何签名或逻辑。仍是纯文档轮次，判据数字应保持 1955/0/2。

## Execution Result — round 3

Contract Amendment 3 的四处全部改完，仍是纯文档轮次，判据 **1955 / 0 / 2** 未变。
收尾那把尺跑过了，`ClearReward*` 相关的 cref **零个解析不到**。
另有**一处新发现，未动，见收尾第三节**——它是那把尺自己找出来的，不是我看出来的。

### 一、两个判据文件里的「四」（Amendment 3 第 1 项）

| 位置 | 改前 | 改后 |
|---|---|---|
| `ClearRewardArchiveTests.cs:177` | `none of the four decisions the composition makes has even that much` | `none of the five decisions ...` |
| `ClearRewardRulesTests.cs:16-18` | `set out on ClearRewardRules and at each of the four places a decision is made` | `set out on ClearRewardRules -- five decisions -- and again at each of the three places one of them is made` |

第二处**两个数都改了**，不是只把 four 改成 five：五项决定分布在**三个**地方
（`ChanceOf` 占第 1、2、5 项并说出第 4 项的前半，`MapBaseRate` 占第 3 项，
`ClearRewardRuleService.ThresholdProfile` 说第 4 项的后半）。原句把「几项决定」和「几个地方」
混成一个数，正是 round 2 序数打架的同一个根。现在两个数分开写，各自属实。

改完全仓复核了一遍剩下的「four」，四处命中全部**在语境里是对的**，不需要改：
`ClearRewardRuleService.cs:52`（我 round 2 写的 `the fourth of the five`）、
`ClearRewardRuleService.cs:84`（`any of the four` 指四次**查表**：档位/难度/组队/房间数）、
`ClearRewardRuleServiceTests.cs:504`（四张**加成表**）、
`ClearRewardRulesTests.cs:91`（Theory 的**第四行**）。

### 二、两处乘法时代的中性元措辞（Amendment 3 第 2、3 项）

**`ClearRewardContent.cs:136-139`（`DifficultyRates` 的 param）**：

- 改前：`The first is zero and the fifth is smaller than the fourth, so it is neither a neutral
  baseline nor a monotonic ladder.`
- 改后：`The first is zero, which where these bonuses are added is the neutral entry rather than an
  emptying one; and the fifth is smaller than the fourth, so this is not a monotonic ladder.`

按裁定：「非单调」那一半属实，原样保留；「不是中性基线」那一半反过来写——加法下首项的 0
**恰恰就是**中性的。

**`ClearRewardContent.cs:235-240`（`DifficultyRate` 的 remarks）**：

- 改前：`... answering an unlisted index with a neutral multiplier would be indistinguishable from
  the archive's own zero having been read one slot over.`
- 改后：`The table's lowest difficulty declares zero, and zero is exactly what a neutral entry is
  worth where these bonuses are added rather than multiplied, so "absent" and "neutral" are not
  interchangeable here: answering an unlisted index with a neutral value would be indistinguishable
  from the archive's own first entry having been read one slot over.`

`multiplier` → `value`，并把「为什么 0 是中性的」补成一句（`added rather than multiplied`）——
不补这一句，「档案自己声明的 0」与「中性值」为何撞在一起就说不通，而这个撞车正是 null 而非
中性值的全部理由。论证本身一字未变，仍然成立。

两处都只说「这些加成是被加起来的」，没有把合成式子搬进内容层——
`ClearRewardTable` 类级那句 `Nothing here is scaled, combined or interpreted` 依然作数。

### 三、`RarityLadder` 的 cref（Amendment 3 第 4 项）

`ClearRewardRules.cs:76-77`：`<see cref="RarityLadder"/>` →
`<see cref="Dfo.Server.Abstractions.Content.RarityLadder"/>`。
该行因此超过 100 列，把它与下一行 `/// costs.` 一并重排为两行，**文字一字未改**。

与控制器上一轮那两处是同一个病的镜像方向：那两处是 Content 文件引 World 类型，
这一处是 World 文件引 Content 类型；两边都没有 using，两边编译器都不说话。

### 四、收尾：再跑一次那把尺

命令（**不改仓里任何文件**，只加命令行属性）：

```
dotnet build <csproj> --nologo --no-incremental \
  -p:GenerateDocumentationFile=true -p:TreatWarningsAsErrors=false \
  -p:NoWarn=CS1591%3BCS1573%3BCS1712
```

再读生成的 `bin/Debug/net10.0/*.xml`，`cref="!:..."` 即解析不到。
这一轮把尺子从两个程序集扩到三个，**把两个判据文件也量进来了**
（`Dfo.Server.Application.Tests`，因为本轮改的两处「四→五」就在那里）：

| 程序集 | `ClearReward*` / `RarityLadder` 的 cref | 其中解析不到 | 该程序集剩余全部 `!:` |
|---|---|---|---|
| `Dfo.Server.Abstractions` | 23 | **0** | `!:Carried`、`!:Cleared`、`!:Tracked`（全在 `Quests.IQuestStore`） |
| `Dfo.Server.Application` | 9 | **0** | 无 |
| `Dfo.Server.Application.Tests` | 14 | **1** | 同左一条，见下 |

**`ClearReward*` 相关的 cref 在生产两个程序集里全部解析得到**，
`!:RarityLadder` 已随第 4 项消失，Abstractions 的 `!:` 从 4 条降到 3 条。
剩下的 `!:Carried` / `!:Cleared` / `!:Tracked` 与本主题无关，按指示**未动**，
三条全部挂在 `T:Dfo.Server.Abstractions.Quests.IQuestStore` 上。

### 五、尺子量出来的一处新发现——**未动，先报**

`Dfo.Server.Application.Tests` 里剩的那一条 `!:`：

```
M:...CardRewardDomainServiceTests.A_clear_fixes_two_faces_...
  -> !:ClearRewardRuleServiceTests.The_kind_of_a_face_decides_the_profile_and_the_scale_it_is_drawn_at
```

出处 `Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs:160-166`：

```
/// <remarks>
/// The seed is what separates them here. The rules do read the kind -- see
/// <see cref="ClearRewardRuleServiceTests.The_kind_of_a_face_decides_the_profile_and_the_scale_it_is_drawn_at"/>
/// for what they do with it -- but a caller may not lean on that: the two faces share a
/// ladder, a grade band and a pool, so one seed for both would draw them alike wherever their
/// two rates agreed.
/// </remarks>
```

**这不只是悬空引用，是又一处说反了的散文，而且是我 round 1 造成的。**

- 它指的那个判据方法**是我 round 1 改名删掉的**：
  `The_kind_of_a_face_decides_the_profile_and_the_scale_it_is_drawn_at`
  → `Neither_the_kind_nor_the_share_walked_reaches_the_odds`。旧名字本身就是旧规则。
- 更要紧的是它旁边那句散文：**`The rules do read the kind`** ——
  revision 4 第 7 条（两牌同概率）之下，规则**不再读牌型**，这句与实现正好相反。
- 连带那半句 `wherever their two rates agreed` 也已过时且偏弱：两面现在**恒等**，
  所以一个种子会让两面**永远**一样，不是「在费率碰巧相同时」。
  它想说的结论（两面必须用不同种子）仍然成立，只是理由比原文更强。

**为什么我没动**：`CardRewardDomainServiceTests.cs` 不在 TASK-10 的 Permitted files 里
（round 1 自查第 2 条已记它属 TASK-11 的清单），Amendment 3 也明写「范围仅此四处」。
故只报不改。

**值得记的一点**：round 1 改了规则、改了判据、写了自查，评审也过了一轮，
这处反话谁都没看见——因为它藏在一个**没被改动的文件**里，靠读 diff 是看不到的。
把尺子从生产程序集扩到判据程序集，它才掉出来。这与控制器上一轮的裁定同形：
问题不在谁不够仔细，在于**有没有核的手段**。

### 判据

**CK-030（全量，判据命令原样）**：

```
Dfo.Pvf.Tests                     60 通过 / 0 失败 / 0 跳过
Dfo.Server.Networking.Tests       51 通过 / 0 失败 / 0 跳过
Dfo.Server.Protocol.Tests        479 通过 / 0 失败 / 0 跳过
Dfo.Server.Persistence.Tests     248 通过 / 0 失败 / 0 跳过
Dfo.Server.Host.Tests              4 通过 / 0 失败 / 0 跳过
Dfo.Server.Infrastructure.Tests  215 通过 / 0 失败 / 2 跳过
Dfo.Server.Application.Tests     898 通过 / 0 失败 / 0 跳过
```

合计 **1955 通过 / 0 失败 / 2 跳过**，与 round 1、round 2 一字不差。
`dotnet build Server/86JP.Server.slnx --nologo`：**0 警告 0 错误**
（尺子那三次构建用的是命令行属性，未落盘到任何项目文件；普通构建复跑确认仍是 0/0）。

## Executor Self-check — round 3

### 改了哪些文件哪些行

四个文件，全部只动 XML 注释散文，**签名、逻辑、任何断言一律未动**：

| 文件 | 动过的行（改后行号） | Amendment 3 第几项 |
|---|---|---|
| `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs` | 177 | 1 |
| `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRulesTests.cs` | 16-18 | 1 |
| `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs` | 137-139、235-240 | 3、2 |
| `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs` | 76-77 | 4 |

四处即 Amendment 3 的四项，一处不多。`ClearRewardRules.cs:76-77` 的重排是全限定名顶破 100 列
所致，文字未改。

四个文件：`\r` 字节计数为 **0**（`tr -dc '\r' | wc -c` 与 python `b.count(b'\r')` 两法各核一遍，
按字节数核，非 `grep -c`）、无 BOM、末尾有换行、无行尾空白。
我新写的行**全部不超过 100 列**；四个文件里现存 6 行超过 100 列（`ClearRewardContent.cs`
63/259/268、`ClearRewardArchiveTests.cs` 71/582、`ClearRewardRulesTests.cs` 324），
逐行核过**都是既有行，不在我改动的段落里**。

### 判据数字（如实）

- 全量：**1955 通过 / 0 失败 / 2 跳过**——与 round 1、round 2 完全一致。
- `dotnet build Server/86JP.Server.slnx --nologo`：0 警告 0 错误。
- 未跑变异、无先红后绿：本轮无任何行为改动，与 round 2 同理。
- 尺子的三次构建全部 0 错误；警告数（3/3/6）都是 CS1570 之类的文档格式提醒，
  与 `NoWarn` 未列全有关，**不是 cref 解析失败**——cref 的判据是 XML 里的 `!:`，不是警告数。

### 通读与复核

1. **本轮改的两处 Content 措辞，我把整个 `DifficultyRate` / `DifficultyRates` 段落重读了一遍**，
   确认改后论证自洽：档案首项声明 0 → 加法下 0 就是中性 → 所以「查不到」若答中性值，
   就与「首项被读串了一格」无法区分 → 故答 null。四步全部成立。
2. **「四」的全仓复核**已在上文第一节列出，四处剩余命中都是对的。
3. **`ClearRewardContent.cs` 与 revision 4 的一致性**：round 2 已逐段核过一遍并列表在案；
   本轮改掉的正是那次报上来的两处，其余各段未动，结论不变。

### 我不确定的地方

1. **`ClearRewardRulesTests.cs:16-18` 我把「四个地方」改成了「三个地方」**，这是个我自己数出来的
   数，不是 Amendment 3 给的。数法写在上文（五项决定分布在 `ChanceOf` / `MapBaseRate` /
   `ThresholdProfile` 三处）。若控制器认为「地方」应当按别的粒度数（比如把 `ChanceOf` 里
   bullet 列表与后两段算作不同地方），这个数要改。
2. **`DifficultyRates` 的 param 里我写了 `where these bonuses are added`**，
   等于让内容层提了一句合成方式。我判断这是必要的——不提，「0 是中性的」就没有依据，
   而这句正是 Amendment 3 第 3 项要求改准的东西。但它确实让该文件比改前多说了一点规则层的事，
   与 `ClearRewardTable` 那句「这里不 interpret」在措辞上贴得很近。若判为越界，
   可退成不给理由的版本（只删掉「不是中性基线」这半句，不解释为什么）。
3. **第五节那处发现我只报不改**，但它属于「说反了」而非单纯悬空，
   留着穿过 TASK-11 的风险与 Amendment 1 当初判断的那种一样。请控制器裁定归谁、何时改。
4. **仍未做实机验证**，与前两轮相同。

## Controller Disposition — round 3 全部接受，新发现转 TASK-11

控制器独立复核：`CardRewardDomainServiceTests.cs:158-166` 原文属实——那句
`The rules do read the kind` 在 revision 4 之下**正好说反**（规则已不读牌型），
被引的 `The_kind_of_a_face_decides_the_profile_and_the_scale_it_is_drawn_at`
**全仓只存在于这一个 cref 里**，确为悬空。`wherever their two rates agreed` 同样偏弱：
两面现在恒等，一个种子会让它们**永远**一样，结论（须用不同种子）反而更强。

### 裁定一：两处自认的不确定，都准

- **「三个地方」这个数**：执行者自己数的，不是 Amendment 3 给的。准。它比原来那个把
  「几项决定」和「几个地方」混成一个数的写法准确，而那个混淆正是 round 2 序数打架的同一个根。
- **`DifficultyRates` 那句提了「这些加成是被加起来的」**：准。不提这句，「0 是中性」就没有依据；
  内容层为解释自己的取值而引一句合成方式，与「这里不 interpret」并不冲突——
  它没有在内容层做判断，只是说明为什么这个 0 需要被特别对待。

### 裁定二：新发现转 TASK-11，不在本 Task 修

`CardRewardDomainServiceTests.cs` 属 TASK-11 的 Permitted files，
执行者停手报上来是对的（Amendment 3 明写「范围仅此四处」）。转 TASK-11。

### 裁定三：那把尺子升为 TASK-09 的常设检查

**这一处反话挺过了：改规则、改判据、写自检、以及一整轮独立评审。**
原因不是谁不够仔细——它藏在一个**没被改动的文件**里，**读 diff 结构上就看不见它**。
它是把尺子从生产程序集扩到判据程序集之后才掉出来的。

尺子只能量「cref 解析不到」，量不了「散文说反了」。但**悬空 cref 是后者的有效代理**：
一处 cref 指向已不存在的东西，往往正说明它旁边那段话谈的是已经变了的事。

**记入 TASK-09 的收尾要求**：全部程序集（含判据程序集）跑一遍尺子，
**每一条 `!:` 都要去看它旁边那段散文还成不成立**，而不是只把 cref 修好。

## Independent Quick Review — round 2

- Verdict: **passed**
- 评审对象：round 2 与 round 3 返工后的**最终状态**。不继承执行者、控制器或 round 1 评审员的任何结论。
- 评审依据：Task Unit Contract revision 1 + `Controller Disposition`（四条裁定）+
  `Contract Amendment 1 / 2 / 3` + 各条 round 2 / round 3 裁定。
- 按派工指示**未重做**的两项：全量 1955/0/2、`dotnet build` 0/0（控制器已独立复核）；
  `CardRewardDomainServiceTests.cs:158-166` 的反话（已转 TASK-11）。
- 本轮亲自跑过的命令：
  - `PVF_ARCHIVE_PATH=... dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --nologo --filter ClearReward`
    —— 基线 **92 通过 / 0 失败 / 0 跳过**；两次变异后各跑一次；还原后复跑仍 **92 / 0 / 0**。
  - 三次 `dotnet build <csproj> --nologo --no-incremental -p:GenerateDocumentationFile=true -p:NoWarn=1591 -p:TreatWarningsAsErrors=false`
    （Abstractions / Application / Application.Tests），**未改仓里任何文件**，读生成的 XML。
  - 全仓 mtime 扫描（排除 `.git`、`obj`、`bin`、`.rks-agent`）认定改动集。
- 本轮**未修改任何生产或判据文件**：七个文件的 sha256 与评审开始时逐字节一致（下第七节）。

### 一、round 1 的两条阻塞发现：都真的消解了

**发现一（第五个自定项「整数比较」未具名）—— 已消解。**

- 具名同形：`ClearRewardRules.cs:216-217` 写作
  `<b>This server's decision, and the archive says nothing about it: the comparison is carried
  out in whole numbers rather than in reals.</b>`
  ——与 `:117` 的 `MapBaseRate` 那一处**逐字同形**（两句标记语齐全）。
- **理由写的正是「可复现」那一条**，不是浮点伪影。`ClearRewardRules.cs:217-226` 原文：
  `the reason for it is <b>reproducibility</b> rather than accuracy ... A face on this server is
  fixed by its seed and has to be replayable from that seed afterwards ... floating-point
  contraction, a fused multiply-add and one JIT differing from another can each move the last
  bits of the same product, so two machines replaying one seed could deal different cards.`
  —— contraction / FMA / JIT 三样都在，「同一种子两台机器给出不同的牌」也在，与裁定一的措辞逐项对上。
- 浮点伪影没有被丢掉，而是**降为第二段**（`:227-233`），作为「两种读法在哪个值上分得开」的证据
  （骰值 1625、8376 对 8375），不再充当理由。这正是裁定一要的分工。
- 五处「四」全部改净：`:48`、`:79`、`:89`、`:179`、`:208`、`:217` 与
  `ClearRewardRuleService.cs:52-54`。两个判据文件的「四」也已改
  （`ClearRewardArchiveTests.cs:177`、`ClearRewardRulesTests.cs:16-17`）。
  全仓复核剩余的 `four`：`ClearRewardRuleService.cs:53`（`the other four`，指另四项决定，属实）、
  `:80/:84`（四次查表）、`:111`（四张加成表）、`:200-201`（四个牌位）、
  `ClearRewardRules.cs:99`（四个等级段）、`ClearRewardRulesTests.cs:89`（参与合成的四个**数**）、
  `ClearRewardArchiveTests.cs:60/112/184/208`（四个等级段 / 四个物品类别位 / 四张表四次查）
  —— 逐条看过，**没有一处是在数「几项决定」**，不需要改。

**发现二（`GoldCardCreateRate` 一段说反了）—— 已消解。**

`ClearRewardContent.cs:168-195` 改后与 revision 4 第 7 条一致，Amendment 2 第 2 项的四个要求逐条对上：

| Amendment 2 第 2 项要求 | 落点 | 核对 |
|---|---|---|
| 两面同概率 | `:173-176` `The two faces of one clear carry the same odds on this server, so the paid face is composed from the map-count table, the difficulty bonus, the dice and the <c>default</c> threshold exactly as the free face is` | 成立 |
| 都不读这个值 | `:176` `neither of them reads this number` | 成立 |
| 该字段今日无读者 | `:173` `<b>Nothing on this path reads it, and that is a rule decision rather than an omission.</b>` | 成立；我全仓核过 `GoldCardCreateRate` 的读者，只有内容源解析与判据断言（`ClearRewardArchiveTests.cs:97`），无规则读者 |
| 参照服务端降级为「另一个服务器的读法」 | `:185-193` `<b>What another server does with it is recorded here as another server's reading, not as this file's rule.</b>` + `carries no authority over this one` + `an exploration rate of its own` | 成立；`the exploration rate` 已限定为那台服务器自己的，本仓无此物 |
| 带 `ItemTypeRates` 同形的免责句 | `:180-182` `Carried verbatim all the same, as <see cref="ItemTypeRates"/> is: ...` | 成立，与 `:133` 同形 |

### 二、序数：我自己数了一遍，两个数都对

**「五项决定」—— 我数出 5。** `ClearRewardRules.cs:79-91` 的类级清单逐项点数：
first 加法 / second 骰子乘组队（两项均 `on ChanceOf`）/ third 最大一档（`on MapBaseRate`）/
fourth `[drop prob]` 作阈值，其另一半为选哪一档（`on ChanceOf`，及选档处）/ fifth 整数比较（`on ChanceOf`）。
**五项，无重复、无遗漏。**

**「三个地方」—— 我数出 3。** 五项决定的**执行处**去重后是：
`ClearRewardRules.ChanceOf`（第 1、2、5 项与第 4 项前半）、`ClearRewardRules.MapBaseRate`（第 3 项）、
`ClearRewardRuleService.ThresholdProfile`（第 4 项后半）。**三处。**
`ClearRewardRulesTests.cs:16-17` 的「五项决定 / 三个地方」两个数**都与我独立数出的一致**。

**各处序数互不打架**（逐处核对）：

| 位置 | 自称 | 与类级清单一致？ |
|---|---|---|
| `ClearRewardRules.cs:179-180` | `The first two of the five` | 是（第 1、2 项） |
| `ClearRewardRules.cs:117` | 无序数，只有具名标记语 | 是（清单说它是第 3；见非阻塞观察 2） |
| `ClearRewardRules.cs:207-208` | `the fourth of the five; ... the other half of it` | 是 |
| `ClearRewardRuleService.cs:52-54` | `The other half of the fourth of the five ... the other four` | 是 |
| `ClearRewardRules.cs:217` | `the fifth of the five` | 是 |

round 1 评审指出的那处编号打架（清单第 4 vs `ChanceOf` 说第 3、且最大一档漏在编号外）**已消除**。

### 三、两处措辞：在加法语义下成立，论证四步完整

**`ClearRewardContent.cs:136-139`（`DifficultyRates`）**：
`The first is zero, which where these bonuses are added is the neutral entry rather than an
emptying one; and the fifth is smaller than the fourth, so this is not a monotonic ladder.`

- 加法下中性元是 0 —— 首项 0 **确实是**中性项，不是「清空项」（那是乘法下的读法）。成立。
- 归档 `[0, 600, 1000, 1400, 1200]`，第五项 1200 < 第四项 1400 —— 非单调，属实
  （`ClearRewardArchiveTests.cs:83-85` 有断言钉住）。成立。
- 原句「不是中性基线」那半句已按加法反过来写，「非单调」那半句原样保留 —— 与 Amendment 3 第 3 项一致。

**`ClearRewardContent.cs:235-241`（`DifficultyRate`）**：论证四步我逐步核过，**全部成立**：

1. `The table's lowest difficulty declares zero` —— 属实（`ClearRewardArchiveTests.cs:84`
   的 `Assert.Equal(0, table.DifficultyRate(0)!.Value)`）。
2. `zero is exactly what a neutral entry is worth where these bonuses are added rather than
   multiplied` —— 加法中性元为 0，成立。
3. `so "absent" and "neutral" are not interchangeable here: answering an unlisted index with a
   neutral value would be indistinguishable from the archive's own first entry having been read
   one slot over` —— 两者取值相撞，确实分不开。成立。
4. 结论 `Null rather than a neutral value, and that is deliberate`（`:236`）+ 实现 `:243-246`
   越界即 null —— 与论证一致。成立。

`multiplier` → `value` 已改；补的那句 `added rather than multiplied` 是第 2 步的依据，**不补则第 3 步
无从谈起**，属必要而非扩界。两处都只提「这些加成是被加起来的」，未把合成式子搬进内容层，
`ClearRewardTable` 类级 `:108` 的 `Nothing here is scaled, combined or interpreted` 仍然作数。

### 四、那把尺子：我自己跑的，`ClearReward*` / `RarityLadder` 全部解析得到

命令（三个 csproj 各一次，**未改仓里任何文件**）：

```
dotnet build <csproj> --nologo --no-incremental \
  -p:GenerateDocumentationFile=true -p:NoWarn=1591 -p:TreatWarningsAsErrors=false
```

三次均 `已成功生成`（0 错误）。读 `bin/Debug/net10.0/*.xml`，`cref="!:..."` 即解析不到：

| 程序集 | `ClearReward*`/`RarityLadder` cref（去重 / 出现次数） | 其中解析不到 | 该程序集剩余全部 `!:` |
|---|---|---|---|
| `Dfo.Server.Abstractions` | 23 / 85 | **0** | `!:Carried`、`!:Cleared`、`!:Tracked` |
| `Dfo.Server.Application` | 9 / 15 | **0** | 无 |
| `Dfo.Server.Application.Tests` | 14 / 21 | **1**（见下） | 同左一条 |

- **`ClearReward*` / `RarityLadder` 的 cref 在两个生产程序集里零个解析不到**，与 round 3 的报告一致。
  去重后的三个数 23 / 9 / 14 与执行者表格**逐数相符**。
- 剩余 `!:` 清单（我自己读出的，共 4 条）：
  - `!:Carried`、`!:Cleared`、`!:Tracked` —— 全部挂在
    `Server/src/Dfo.Server.Abstractions/Quests/IQuestStore.cs:31-32`。
    我另用 `CS1574` 交叉验了一次：该程序集的 `CS1574` 恰为这三条，行列号
    `IQuestStore.cs(31,16)` / `(31,42)` / `(32,16)`。与本主题无关，按指示未动。
  - `!:ClearRewardRuleServiceTests.The_kind_of_a_face_decides_the_profile_and_the_scale_it_is_drawn_at`
    —— 出处 `Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs:160-166`，
    即控制器已裁定转 TASK-11 的那一处，**不在本次评审范围**。我的尺子独立复现了它，
    确认它仍是判据程序集里唯一一条 `!:`。

### 五、式子仍然正确：三轮文档改动之后逐值重算，无一处被碰坏

不看执行者与 round 1 评审的对照表，按 `shortfall = threshold − mapBase − diff`、
`scaledParty = round(partyRate × basis)`、`losing = ⌊shortfall × basis / scaledParty⌋`、
`Rate = basis − losing` 自行手算，与 `ClearRewardRules.cs:252-271` 的实现逐句对照：

| 房间 / 难度 / 队伍 | 手算过程 | 手算 | 判据期望值 |
|---|---|---|---|
| 5 / 3 / 4 | 缺口 2600；`1.6f`=1.600000023841858，`round(16000.000238…)`=16000；`26000000/16000`=1625 | **8375** | `ClearRewardRulesTests.cs:113`、`ClearRewardArchiveTests.cs:250` 均 8375 ✓ |
| 1 / 1 / 3 | 缺口 7400；`1.4f`=1.399999976158142，`round(13999.99976…)`=14000；`74000000/14000`=5285.71→5285 | **4715** | `ClearRewardRulesTests.cs:113`、`ClearRewardArchiveTests.cs:257` 均 4715 ✓ |
| 5 / 4 / 1 | 缺口 `10000−6000−1200`=2800；落败 2800 | **7200** | `ClearRewardRulesTests.cs:99`、`ClearRewardArchiveTests.cs:253` 均 7200 ✓ |
| 5 / 3 / 1 | 缺口 2600；落败 2600 | **7400** | 同上，7400 ✓（且 7400 > 7200，倒挂原样透传） |
| 10 / 3 / 1 | 无 10 房行 → `MapBaseRate` 取最大**房间数**行 5→6000；缺口 2600 | **7400** | `ClearRewardArchiveTests.cs:260-262` 三行均 7400 ✓ |
| 5 / 0 / 1 | 缺口 4000；落败 4000 | **6000** | `ClearRewardRulesTests.cs:138`、`ClearRewardArchiveTests.cs:254` 均 6000 ✓ |

控制器给的三个数 **70.00% / 20.00% / 83.75%** 我独立重算后一致
（5 房难度 2 单人 → 7000；1 房难度 0 单人 → 2000；5 房难度 3 四人 → 8375）。
`ClearRewardArchiveTests.cs:247-266` 的 17 行 Theory 我**逐行手算过，无一行与实现不符**。
边界两端也核过：`shortfall < 0` 直接满值（`:258-259`）；`shortfall == 0` 时 `losing = 0`，
即「恰好等于阈值不算过、但任何骰值都补得上」，与 `:235-238` 的散文一致。
1625 这个分歧点在整数下**判负**（`1625×16000 = 26,000,000` 不大于 `2600×10000 = 26,000,000`），
与 `> threshold` 的严格不等号一致。

归档已知值本身有断言钉住：`ClearRewardArchiveTests.cs:70-107`
（六档 profile、`default` 每段 10000、`[0,600,1000,1400,1200]`、房间数 1→2000 / 5→6000 / 6→null、
`1.4f` / `1.6f` 存的是单精度值）。

### 六、越界：没有第八个文件

无 git 基线可比（本主题全部 `ClearReward*` / `CardReward*` 文件在 `git status` 里仍是 `??`，
我用 `git ls-files --error-unmatch` 逐个确认过 `IClearRewardRuleService.cs` 同样未跟踪）。
改动集按 mtime 认定，**全仓**扫描 2026-09-01 20:00 之后被动过的文件
（排除 `.git`、`obj`、`bin`、`.rks-agent`，不限扩展名）：

| 文件 | mtime | 授权 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` | 21:09:06 | 契约 Permitted files |
| `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs` | 21:11:33 | 契约（对应判据文件） |
| `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` | 21:42:55 | 契约 |
| `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs` | 21:54:29 | 契约（对应判据文件） |
| `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs` | 21:54:29 | Amendment 1 + 2 |
| `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs` | 21:54:29 | 契约 |
| `Server/tests/Dfo.Server.Application.Tests/World/ClearRewardRulesTests.cs` | 21:54:29 | 契约（对应判据文件） |

**恰好七个，与契约 Permitted files（6）加三条 Amendment 授权的一个完全重合，无第八个。**
（20:00 后的其余命中全部是 `obj/`、`bin/` 下我自己那三次文档生成构建的产物。）
三轮合计未动 `CardRewardDomainService.cs`、`CardRewardDomainServiceTests.cs` 等
TASK-11 / TASK-12 名下的文件，与各轮停手报告一致。

格式（按字节核）：七个文件 `\r` 计数均为 **0**、无 BOM、末尾有换行、无行尾空白
（用 `awk '/[ \t]$/'` 计数为 0；注意 `grep -c '[ \t]$'` 在本环境会把结尾字母 `t` 误判，不可用）。

### 七、我自己跑的两条变异（均已还原，sha256 校验）

基线 `ClearRewardRules.cs` sha256 = `1223650d524d3c23ab2e93569cb5c51259f8be936b622a6b92d0bf0d1bb52bd4`。
两条均施加后 `touch` 把 mtime **前进**（22:30 / 22:40，基线 mtime 为 21:54），
跑完从 scratchpad 的原始副本还原并再次前进 mtime。
两次输出**都无 `error CS`**、构建成功，且都有 `失败! - 失败: N，通过: M` 汇总行
—— 确认是判据变红而非编译失败。

| 变异 | 内容（`ClearRewardRules.cs`） | 我跑出的真实结果 | 还原 |
|---|---|---|---|
| **MUT-X**（验第五项决定：整数比较） | `var losing = shortfall * basis / scaledParty;` → `var losing = (long)Math.Floor(shortfall / partyRate);`（改为实数比较） | **红 4 失败 / 88 通过**：`ClearRewardRulesTests.The_numbers_add_and_the_dice_makes_up_the_shortfall(6000, 1400, 1.6000000238418579, 8375)`、`ClearRewardRulesTests.A_party_multiplier_stored_as_a_single_does_not_decide_a_dice_value`、`ClearRewardArchiveTests.The_chance_a_position_carries_is_the_archives_numbers_combined(86, 3, 5, 5, 4, 8375)`、`ClearRewardArchiveTests.No_occasion_the_archive_describes_is_certain_or_impossible` | sha256 回到 `1223650d…` |
| **MUT-Y**（验第三项决定与 AC-011：最大一档兜底） | `MapBaseRate` 的整个兜底循环（`:154-165`）→ `return null;` | **红 9 失败 / 83 通过**：`A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row`、`Every_maze_of_an_ordinary_dungeon_carries_a_chance`、`Both_faces_of_one_clear_carry_the_same_odds`、`No_occasion_the_archive_describes_is_certain_or_impossible`、`ClearRewardRulesTests.A_maze_the_table_prices_nothing_for_takes_the_largest_row`、`ClearRewardRuleServiceTests.A_maze_the_table_prices_nothing_for_still_carries_a_chance`、超范围 Theory 三行（10/10、10/5、6/6） | sha256 回到 `1223650d…` |

**结论：三轮文档改动之后，逻辑仍被判据钉死。** 两条的失败数（4 / 9）与 round 1 评审的 R-C / R-B、
以及执行者的 M2 **逐数相符**。MUT-Y 同时打红 `Every_maze_of_an_ordinary_dungeon_carries_a_chance`
—— AC-011 在「查不到即零」的旧读法下确实会红，本轮独立复现。

**还原证明**：七个主题文件在变异前后的 sha256 逐一比对，**全部一致**：

```
1223650d524d3c23ab2e93569cb5c51259f8be936b622a6b92d0bf0d1bb52bd4  ClearRewardRules.cs
56abc3f58d29cf341b0a231622a33e588e3e568f0737c2ad0d34f26836946296  ClearRewardRuleService.cs
47bbc53f0dc3ededbd4764d88e70662dc5d2e60441deda04a9c3badbabba41c9  ClearRewardContent.cs
967a06388d617e56f084d696491291618d3e475661f50b746caa36cfd780dcb0  IClearRewardRuleService.cs
69d3a23789ad04a1e55750d2cda8047e6f0d88991c9dd547dae18c9d85e13bf3  ClearRewardRulesTests.cs
d5ec6d6d1718f7e6ed2d717647e5e75297771fd3e3843bcf7e2b4ccbe923fb14  ClearRewardArchiveTests.cs
d42ddbc4810e7260f6185fa33f1a2615f7d76b989f6234d2a579c1aedcf44719  ClearRewardRuleServiceTests.cs
```

还原后复跑 `--filter ClearReward`：**92 通过 / 0 失败 / 0 跳过**，与评审开始时的基线一字不差。

### 八、非阻塞观察（不要求本轮处理）

1. **`Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs:24` 长 158 列**，
   是一行没有重新折行的注释：
   `/// the archive's numbers into one chance is new and lives in <see cref="ClearRewardRules"/>. Rolling a quality, taking the level's grade band and drawing one`。
   同目录其余 22 个文件的最长行是 **73–109 列**，158 是唯一的离群值。
   它是 TASK-10 round 1 从这句里删掉 `ExplorationRate` 相关措辞后没有回折造成的。
   仓里无 `.editorconfig` 行宽规则，编译与判据都不受影响，纯观感问题；
   但它与 round 2 自查「新写的行最长 100 列 / 该目录既有最长 110 列」的描述对不上
   —— **该文件本身就有一行 158 列，自查没有量到它**。一行重折即可，落在 Permitted files 内。
2. **`ClearRewardRules.cs:117` 是五个决定落点里唯一不自报序数的一处**。
   它的两句标记语齐全（与 `:216` 同形），契约「各自在其所在之处具名」的要求**已满足**；
   但另四处都写了「第几之五」，读者站在 `MapBaseRate` 上只能回类级清单才知道它是第三项。
3. **`ClearRewardRules.cs:207-208`（第 4 项前半）只有序数、没有「归档没有说这件事」那半句标记语。**
   该半句由类级清单 `:88-89`（`The archive says nothing about any of the five`）与
   `ClearRewardRuleService.cs:53`（`the archive states it no more than the other four`）兜住，
   所以并不缺失，只是与另四处的写法不完全同形。
4. **`ClearRewardRulesTests.cs:16-17` 的「三个地方」有一处在被测类之外。**
   `set out on ClearRewardRules -- five decisions -- and again at each of the three places one of
   them is made`：三处中的 `ClearRewardRuleService.ThresholdProfile` 既不在 `ClearRewardRules` 上，
   也不在该判据类的被测范围内。句子本身没有断言相反的事，数也是对的（我独立数出同样是 3），
   只是读者要离开被测文件才数得全。
5. **「先红」的原始输出仍无法从仓库独立复现**（`ClearReward*` 全部未跟踪，无改动前版本可回退）。
   MUT-Y 从另一侧给了同等力度的佐证：同一条判据在「查不到即零」的旧读法下确实红。
6. **`Dfo.Server.Application.Tests` 那条 `!:` 我的尺子独立复现了**，确认它仍是判据程序集里唯一一条，
   出处 `CardRewardDomainServiceTests.cs:160-166`，已转 TASK-11，本轮按派工未纳入范围。
7. **仍无实机验证**（与前三轮相同）。概率的观感没有任何外部证据。

### 九、结论

round 1 的两条阻塞发现**都真的消解了**，且消解的方式与裁定 / Amendment 的字面要求逐项对上
（尤其「理由必须是可复现那一条」—— contraction / FMA / JIT 三样都写进去了）。
序数在类级清单、`ChanceOf`、`MapBaseRate`、`ThresholdProfile` 与两个判据文件之间**自洽**，
「五项决定」「三个地方」两个数由我独立数出，与文中说法一致。
两处措辞在加法语义下成立，`DifficultyRate` 的四步论证完整。
尺子由我自己跑过，`ClearReward*` / `RarityLadder` 相关的 cref 在两个生产程序集里零个解析不到。
式子在三轮文档改动之后**逐值仍然正确**，并由我自己跑的两条变异确认仍被判据钉住。
改动集恰为七个授权文件，无越界。

**Verdict: passed.**

## Controller Disposition — round 2 评审 `passed`，本 Task 置 `done`

控制器独立复核后接受：评审员自己数出「五项决定」「三个地方」两个数并与实现逐一对上，
自己跑了两条变异（整数比较改实数 → 红 4；`MapBaseRate` 兜底改 null → 红 9，
独立复现了 AC-011），自己跑了三次 doc-gen 构建量 cref——
**生产两个程序集的 `ClearReward*` cref 零个解析不到**。
全量判据 1955 / 0 / 2 由控制器亲自跑过三次，三轮文档改动前后一字不差。

执行结果、自检、评审轮次与 owned diff 指向同一轮。**TASK-10 置 `done`。**

### 唯一一条要接着办的观察，转 TASK-11

`ClearRewardRuleService.cs:24` **158 列**。控制器复核属实，且是唯一离群值：
同目录其余九个文件最长 101–109 列（`ClearRewardContent.cs` 那三处 105/109 与邻居齐平，不算）。

值得记的不是这一行，是**评审员发现 round 2 的自查说「该目录最长 110 列」却没量到自己这个文件**。
自查用的尺子把被测对象漏在外面——与本 Spec 一路在抓的形状同类：
**量了，但没量到该量的那个**。

该文件在 TASK-11 的 Permitted files 内，转 TASK-11 顺手回折。

### 另两条非阻塞观察，不办

`ClearRewardRules.cs:117` 不自报序数（标记语齐全，契约要求已满足）、
`:207-208` 只有序数无「归档没说」半句（由类级清单与 `RuleService.cs:53` 兜住）。
两条都不构成缺陷，记录备查。
