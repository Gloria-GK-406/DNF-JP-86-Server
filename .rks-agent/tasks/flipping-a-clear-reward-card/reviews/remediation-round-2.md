---
kind: rks-remediation
round: 2
scope: 第 1 轮通读报出的 5 处 + 小节标题；两份 Docs 与两份协议 md 的全文通读
verdict: 六项处置完毕；**通读全文，有第六处**，已列出未改
---

# Remediation Round 2

前置：`reviews/remediation-round-1.md`。本轮授权来自控制器，含三条裁定：
B2 的「服务端实现」表**保留**、B3 小节标题**授权改**、
两处非本次造成的（`NOTI-0x0023.md:85-86`、`:89`）**不改**。

**仍然零逻辑改动，没碰任何 `.cs`。**

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

退出码 0。两条跳过仍是那两条「缺档案时应报错」的用例，**因为档案可达而跳过**。

行尾（按字节数核 `\r`，不用 `grep -c '\r'`）：

| 文件 | `\r` |
|---|---:|
| `Docs/新功能接入规范.md` | 0 |
| `Docs/DropRates.md` | 0 |
| `protocol/game/client-to-server/0x0047.md` | 0 |
| `protocol/game/server-to-client/NOTI-0x0023.md` | 0 |
| `reviews/remediation-round-2.md` | 0 |

**生产与判据代码一个字节没动**，这一条有比 `git status` 更硬的核法：

```text
git diff --stat -- Server/src    → 20 files changed, 768 insertions(+), 199 deletions(-)
git diff --stat -- Server/tests  → 10 files changed, 875 insertions(+),  14 deletions(-)
```

`Server/src` 那三个数**与 `change-review.md` 做完九个变异、还原校验之后记下的
「20 文件 / 768 插入 / 199 删除」逐字相同**——两轮返工加起来对 `Server/` 的净影响为零。

（订正：`remediation-round-1.md` 里我把 `Server/` 下的受控修改文件数写成了 32，实为 **30**
（20 + 10）。数错了，不影响那一轮的任何结论，在此更正。）

---

## 本轮改的六处

### 1. `Docs/新功能接入规范.md` 的引文（原 188-194 行）

原文引 `DropRateTables.ClearReward` 的 XML doc 说「唯一的差别」是 `[drop prob]` 形状。
该 XML doc 已被本次交付改写（`DropRateContent.cs`）。改法：
**保留为真的两半**——常量确已声明、确是重构时刻意留的钩子——
再写明「它钩住的不是『换一张表』」，附改写前后的对照与新版中译，
并具名钉住段数的判据
`PvfClearRewardRateSourceTests.Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else`。
标注了「上面是中译，原文是英文 XML doc」——原引文也是中译，这一点没变。

### 2. `Docs/DropRates.md:65`「前五张表**同构**，可以用同一个读取器」

改为「**同构的是三张**」（怪物表 1、怪物表 2、可破坏物表），
并补上可破坏物表少一段 `[party member drop bonusrate]`、按「缺段不等于中性值」记为未列出；
另起一段写明深渊表与翻牌表都不同构、喂给兄弟读取器**会抛**。

两条 refusal 都是判据钉住的，不是我推的：

| 文件 | 判据 |
|---|---|
| 深渊表 | `PvfClearRewardRateSourceTests.Three_siblings_declare_drop_prob_and_the_abyss_table_a_difficulty_keyed_one`（`:573` 的 `Assert.Throws<InvalidDataException>`） |
| 翻牌表 | 同文件 `The_sibling_reader_refuses_this_file_over_a_section_it_read_correctly`（`:836`，并断言错误消息里出现 `[dungeon difficulty drop bonusrate]` 与 `16 rates`） |

「三张」的出处是本次交付改正的 `DropRateContent.cs:115-124`：
「Three of the family's seven files are read through this one: monster, monster-extra and
breakable object」。

### 3. `Docs/DropRates.md:62` —— 半真半假的那一行，按 B1 的教训拆成两行

拆行而不是整删，两半各自写准：

```markdown
| 副本 `.dgn` 的 `[gold card use]` | **确是翻牌的按副本覆盖**：…5 个声明 `0`…已实现（`DungeonDefinition.OffersPaidCard`） |
| 副本 `.dgn` 的 `[result card]` | **不是**翻牌的按副本覆盖：4 个副本声明，属大会玩法…本仓不实现。见第十节 |
```

`[gold card use]` 那半的「已实现」核过：`PvfDungeonDefinitionSource.cs:206`、`:330` 读它，
`ClearRewardRuleService.cs:176` 用它，`DungeonPaidCardSwitchTests` / `DungeonPaidCardSwitchRuleTests`
阴阳两向钉住。

### 4. `Docs/新功能接入规范.md:300`（现 330 行）基线

`1690 通过 / 2 跳过 / 0 失败` → `1963 通过 / 2 跳过 / 0 失败`，
并注明日期与「此前是 1690 通过」，好让读到的人知道这个数是随哪次交付动的。
同节对「跳过数是档案确实加载了的证明」的解释与那两条用例名**核过仍为真**，未动。

### 5. `Docs/DropRates.md:390` 分层草图里的服务名

`ICardRewardService 二期` → `ICardRewardDomainService 二期（已落地；规则那半是 IClearRewardRuleService）`，
并对齐了代码块里的缩进。落地的确是两个：业务服务 `ICardRewardDomainService`
（`Application/World/CardRewardDomainService.cs`）持有本局牌面，
规则服务 `IClearRewardRuleService`（同目录 `ClearRewardRuleService.cs`）产出牌面。

### 6. 小节标题（控制器裁定授权）

`### 还没定的，属于设计决定不属于接入`
→ `### 原先没定、现已定的（属于设计决定，不属于接入）`。
保留了原本的分类意图（设计决定 vs 接入），与该节现在唯一一条已定的内容一致。

### 顺带收紧的一处措辞（不是新发现，是我上一轮写得有歧义）

`NOTI-0x0023.md` 牌尾表里那一行原写「七个牌位(标记恒 1)…后四个本仓没有对应牌位,写零」，
「写零」可能被读成连标记字节一起写零。核过 `EncodeClearDungeonReward` 的循环：
`writer.Write((byte)1)` 对七个 seat **一律**执行，写零的只是 (物品, 数量) 两个 i32。
改成「标记**七个都写 1**;(物品, 数量) 前三个取牌位 1、2、3，后四个…两个字段都写 0」。

---

## 通读结论：**有第六处**

四份文件全部从头到尾读过（不是只看改动附近）：
`Docs/新功能接入规范.md` 1–351、`Docs/DropRates.md` 1–500、
`protocol/game/client-to-server/0x0047.md` 1–140、
`protocol/game/server-to-client/NOTI-0x0023.md` 1–176（含机器生成区）。

### 第六处（本次交付造成，**未改**）

**`Docs/新功能接入规范.md:211-212`**（原 201-202 行）：

> 其余段落（稀有度阶梯、`[item drop ref table]`、难度/组队加成）与怪物表同构，
> `PvfDropRateTableSource` 已经会读，不要重写。

**这是第 2 处（`DropRates.md:65`）的同一条陈述，长在另一份文档里，而且落在我上一轮
自己改过的那一段的下一行。** 我上一轮核过它、判定它为假，却没把它写进报出的五处清单——
这正是本 Spec 反复栽的那个形状，这次栽在我自己身上。

两半都假：

1. **`PvfDropRateTableSource` 读不了这个文件。** 把这个路径交给它会抛
   `InvalidDataException`——`The_sibling_reader_refuses_this_file_over_a_section_it_read_correctly`
   （`PvfClearRewardRateSourceTests.cs:836`）钉住。
2. **「同构的那些段落」也不是「已经会读、不要重写」。**
   `PvfClearRewardRateSource` 自己声明并读了全部 16 段，包括这里点名的稀有度阶梯
   （`RaritySection`）、`[item drop ref table]`（`GradeBandSection`）、难度与组队加成
   （`DifficultySection` / `PartySection`）。而且**其中一段借不得**：
   `[item drop ref table]` 借怪物表的行会在 80–89 级**多开一个品级且不报任何错**，
   由 `The_grade_bands_share_the_monster_tables_below_column_and_differ_above_at_80_to_89` 钉住
   （该判据自己的 remarks 写明「a reader that borrowed the sibling's rows would open one
   extra item grade to every endgame flip and produce no error at all while doing it」）。

**危害与 B3 同级**：这份文档是契约 Constraints 点名的分层依据，
照它做下一个功能的人会去把翻牌表交给 `PvfDropRateTableSource`，
而 80–89 级正是 A21 的毕业段。
**且它现在与同文件 193-198 行的引文直接矛盾**——引文说「不是同一套段落…由
`IClearRewardRateSource` 读」，下一段说「其余段落同构，`PvfDropRateTableSource` 已经会读」。

建议改法（未执行）：保留为真的那半——抽取那几步（掷稀有度 → 品级带 → 桶内加权抽一件）
确实复用 `DropRules`，不要重写——把「读」那半改成「16 段全由
`IClearRewardRateSource` 自己读，`[item drop ref table]` 尤其借不得」。

### 除第六处外，通读中还看到的（**都不是本次交付造成的**，按第 1 轮的规矩一并记录，未改）

| 位置 | 事 | 为什么判为非本次造成 |
|---|---|---|
| `DropRates.md:291` | 翻牌稀有度那一行把第六档写成「**传说** 0.0001%」 | 同文件 §四（138-144）与 §十四「**推翻二：稀有度第六档是远古，不是传说**」（471-473）早在 2026-08-30 就已改正过这个叫法，291 行是当时漏改的。本次交付把它**在代码里定了案**：`ClearRewardRarityLadderTests` 枚举的六档是 Common / Uncommon / Rare / Unique / Epic / **Chronicle**，`ladder.BandOf(1000002)` 断言为 `DropRarity.Chronicle`。数值（50.00 / 29.99 / 20.00 / 0.01 / 0.0001 / 0.0001）与判据里的权重逐项对得上，**错的只是第六档的名字** |
| `新功能接入规范.md:89` | 「于是史诗和**传说**永远摇不到」 | 同一个叫法问题，同样先于本次交付；连 `TaskContract.md` 与 `change-review.md` 都沿用「史诗与传说两档」。真正摇不到的那两档是 Epic 与 Chronicle |
| `NOTI-0x0023.md:85-86` | 模板句「本协议尚无观察记录」与紧随其后的 `## A21` 一节自相矛盾 | 控制器已裁定不改；两段各自独立生成/追加，先于本次交付 |
| `NOTI-0x0023.md:89` | 「是 `SET_PLAY_RESULT` 的第三帧回答」，升级支路上是第四帧 | 控制器已裁定不改；核过 `git diff`，帧序列本次**未改动**，`AvailableQuestListMessage` 那一支是既有的 |

### 通读中核过、判为**不成立**的疑点（列出来，免得下一轮重查）

- `DropRates.md:234`「深渊表结构与其余四张都不同」——字面为真（它确实与另外四张每一张都不同），
  并未声称那四张彼此同构，不与 65 行的新说法冲突。
- `DropRates.md:264`「`etc/itemdropinfo_object.etc` 与怪物表同构」——为真，
  它正是走 `PvfDropRateTableSource` 的三张之一。
- `DropRates.md:108`「最终率 = 基础率 × 档次 × 难度 × 组队」——这一节讲的是怪物表，
  与翻牌的加法合成不冲突，翻牌那套写在第十节。
- `DropRates.md:475-478`「每张费率表 200 行 × 3 数」——为真；
  翻牌表也有这一段、也是 200 行，只是**值**与怪物表在 80–89 级不同（见第六处）。
- `新功能接入规范.md:36-53` 的两条自检与那两条 grep——我重新跑了两条命令：
  `IChannelEntryPolicy` 仍是 3 处，内容源命中的文件个个是 `*RuleService.cs`
  （加上文中自己点名的例外 `PlayingCharacter.cs`）。**仍然为真**，本次新增的
  `ClearRewardRuleService` 也在其中。
- `新功能接入规范.md:131-133` 上提到 endpoint 层的举例只列了三个文件，
  本次新增的 `Game/GameInventoryDelivery.cs` 不在其中——但那是举例不是清单，
  **不构成假陈述**，没算作发现。
- `新功能接入规范.md:253-254`「`ProtocolModule` 加四类注册行」——核过 diff，四类各有新增行，为真。
- `0x0047.md` 机器生成区（1-53）与「请求：两个构包点」一节讲的是客户端与上游，
  不含对本仓行为的陈述，不受本次交付影响。

## 我不确定的地方

1. **第六处我按指示没改，但它现在与同文件 193-198 行的引文直接打架。**
   一份自相矛盾的分层依据可能比一处旧错更容易误导人。如果控制器授权，
   这是一行字的事（改法已写在上面）。
2. **「传说 / 远古」那个叫法**横跨 `DropRates.md`、`新功能接入规范.md`、`TaskContract.md`
   与 `change-review.md`，本次交付的代码已经用 `DropRarity.Chronicle` 定了案。
   我判它为「非本次造成」因而没改，但它现在是**文档与代码之间**的不一致，
   不只是文档内部的。要不要统一叫法是个跨文件的决定，超出本轮。
3. 第 1 处的引文我做了中译。原引文也是中译，所以风格一致；
   但中译本身不是判据，读的人若要核实得去看 `DropRateContent.cs` 的英文原文——
   我把文件路径写进去了。
