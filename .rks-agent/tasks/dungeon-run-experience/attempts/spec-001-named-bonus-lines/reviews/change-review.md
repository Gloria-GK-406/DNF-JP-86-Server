---
kind: rks-review
verdict: approved
---

# Change Review: 每一笔加成走到客户端为它画的那一行

审查范围 `change`（第二轮，覆写第一轮制品）。基线 `ff413e0e`，被审对象为工作树相对 HEAD 的未提交
改动（`Server/` 下六个文件，+355 / -87）加上 `.rks-agent/tasks/dungeon-run-experience/` 下的 SDD
制品。未提供挑战集，故省略 `Challenge Dispositions`。

第一轮结论为 `changes_required`，唯一阻断项 BF-1 是「种类到偏移的配对没有任何自动化断言」。本轮
BF-1 已修复且强度超出报告描述，**verdict 改为 `approved`**。

## 本轮相对上一轮的改动面

`diff` 上一轮与本轮的生产文件，改动只有两处，其余逐字未变：

- 新增嵌套 `record ExperienceNoticeLine(Kind, Offset, ClientName)`（`MonsterDeathSlice.cs:153`）。
- `NamedLineOffsets`（`IReadOnlyDictionary<kind,int>`）替换为 `NamedLines`
  （`IReadOnlyList<ExperienceNoticeLine>`，`:171`），客户端叫法进入生产代码。

写入端 `EncodeExperience`（`:208-250`）、`ExperienceBonusLines`（`:49-91`）、
`ExperienceNotification`、`MonsterDeathHandler` 与上一轮**逐字相同**；另外四个生产文件的 diff 统计
（66 / 6 / 6 / 5）也与上一轮完全一致。因此第一轮已独立核验过的以下结论原样成立，本轮不重复论证：
帧长 83 与逐字节偏移推演、`+0x2F`/`+0x33` 在非探针路径写死字面量 `0`、非加成字节与 HEAD 逐字相同、
`KindCount` 先于 `None` 的静态初始化顺序、四个 `ExperienceNotification` 调用点传值属实、
`Collect` 全仓只有击杀路径一个调用者、`EncodeExperience` 是全仓唯一帧生产者。

测试文件成员清单九个测试全在，无覆盖净损失；改动集中在 `Measured` 锚表与两个被强化的测试。

## BF-1 复核：文档 / 生产表 / 测试锚三方字节级一致

用脚本从三处各自抽出（偏移, 客户端叫法）序列后逐字节 `diff`：

- `Docs/ExperienceNoticeSlots.md` §4 中标 `**可用**`（排除 `**不可用**`）的行 —— 9 条。
- `MonsterDeathPayloadCodec.NamedLines`（`MonsterDeathSlice.cs:172-182`）—— 9 条。
- 测试锚 `Measured`（`MonsterDeathCodecTests.cs:194-206`）—— 9 条。

**doc vs prod：BYTE-IDENTICAL（9 行，同序）。doc vs test：BYTE-IDENTICAL（9 行，同序）。**
中文叫法逐字符相同，无同形字或空白差异。这是上一轮拿不到的证据：上一轮生产侧只有偏移，
「哪个种类对应哪个叫法」只存在于注释里，无法机器比对。

## BF-1 复核：变异实验（九种形状，全部由我独立构造并实跑）

每次变异都从备份还原后单独施加，跑 `MonsterDeathCodecTests`（9 个测试），跑完还原并以 md5 校验。

| # | 变异形状 | 结果 | 变红的测试 |
|---|---|---|---|
| M1 | 生产表 **+** 写入端一起把 `0x1A`/`0x1E` 转置 | **3 红** | `The_declared_lines_are_pair_for_pair_the_measured_ones`、`Every_named_line_is_written_at_the_offset_the_client_draws_it_from`、`Two_reasons_on_one_line_are_added_together` |
| M2 | M1 **+** 把测试锚 `Measured` 也改成同样的错误配对 | **1 红** | `Two_reasons_on_one_line_are_added_together` |
| M2' | 同 M2，但换成 `0x05`/`0x15`（无第二处硬编码的一对） | **全绿** | —— |
| M3 | 只改测试锚，生产侧不动 | **2 红** | pair-for-pair、Every_named_line |
| M4 | 只交换生产表里两个 `ClientName`，偏移不动 | **1 红** | pair-for-pair |
| M5 | 生产表把某个种类指向不可用的 `+0x2F` | **1 红** | pair-for-pair |
| M6 | 写入端真的往 `+0x2F` 写入一笔加成 | **2 红** | Every_named_line、`The_slots_that_do_not_balance_are_never_written` |
| M7 | 生产表与写入端一起删掉一行 | **2 红** | pair-for-pair、Every_named_line |
| M8 | 表不动，写入端静默停止申报一行 | **1 红** | Every_named_line |

M1 复现了上一轮 BF-1 的那个绿色通过场景，现在是红的——阻断项确已消除。M4 是我加的形状：只改叫法
不改偏移，上一轮的测试结构对此完全无感（叫法根本不在生产代码里），现在会红，这是把 `ClientName`
放进生产表换来的实际收益，而不只是文档收益。M5/M6 确认「指向或写入两个不自洽槽」在表侧与字节侧
各有一道独立的红线。

**关于 M2 与 M2'，须如实记录，不要过度宣称。** 报告里说 M2 会红三个测试是准确的，但那三个中的
`Two_reasons_on_one_line_are_added_together`（`:129`）之所以能抓住 M2，是因为它为了验加法而恰好把
`SeriaBlessing → 0x1A` 独立硬编码了一次。我用 M2' 验证了这一点：换成没有第二处硬编码的
`PartyReward`/`FatigueBuff`，同样的「三份副本一起转置」**全绿通过**。所以现有防线的真实强度是
「任何只改一处或只改两处（生产表 / 写入端）的转置都会被抓住」，而不是「连测试锚一起改也抓得住」。
这不构成阻断：`Measured` 就是这套测试的判据本身，重写判据不是测试所能防御的对象，测试文件自己也已
写明「Edit it only from a fresh measurement, never to make a test pass」（`:188-192`）。记在这里只是
免得后人误以为存在第三层防护。

## 本轮独立复核的其余项

- **构建**：`dotnet build Server/86JP.Server.slnx` → 0 个警告，0 个错误。
- **全量测试**：`PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --no-build`
  → 51+306+60+4+139+706+240 = **1506 通过 / 2 跳过 / 0 失败**。与报告一致。
- **`The_declared_lines_are_pair_for_pair_the_measured_ones`（`:79-100`）的断言完备性**：
  `Assert.Equal(Measured.Length, NamedLines.Count)` 挡住多余行；`Assert.Single(NamedLines, line => line.Kind == kind)`
  挡住同一种类出现两次；逐条比 `Offset` 与 `ClientName` 挡住转置与错名；`declarable` 键集比对挡住
  「新增种类却没给它一行」；两条 `DoesNotContain` 挡住指向 `0x2F`/`0x33`。两个偏移相同（两种类挤同
  一槽）也会被逐条 `Offset` 比对抓住。
- **`Every_named_line_is_written_at_the_offset_the_client_draws_it_from`（`:108`）现在遍历 `Measured`**
  而非生产表，写入端因此是对着观测转写核的。其「其余十三槽必须为零」的内层断言仍在，M6/M8 的红即由此而来。
- **探针不受影响**：`Line(index, kind)` 的 `index` 仍是探针槽号 0..13，与写入顺序一致；
  `The_label_probe_still_pays_a_distinct_amount_into_every_slot` 仍绿，RQ-009 成立。
- **工作树状态**：所有变异均已还原，生产文件与测试文件 md5 与变异前一致，`git status --short` 仍为
  六个 `M` 加一个未跟踪的 `.rks-agent/` 目录。

## 合同符合性

| 条目 | 结论 |
|---|---|
| SC-1 / RQ-001 / EV-001 | **成立**。九对配对以三元组逐条断言，锚表逐字抄自 §4 并与文档字节级相同；`DoesNotContain(0x2F/0x33)` 在同一测试内。上一轮的缺口已闭合 |
| SC-2 / SC-3 / RQ-003..005 | 成立。逐种类一例 + 合并一例 |
| SC-4 / RQ-006 | 成立（后半句见 NB-3） |
| SC-5 / RQ-007 | 成立。表侧（M5 红）与字节侧（M6 红）双重把守 |
| SC-6 / RQ-008 | 成立。写入端与上一轮逐字相同，全帧零值扫描断言与 HEAD 逐字相同 |
| SC-7 | 成立。0 警告 0 错误，1506 通过 |
| RQ-002 | 成立且强化。偏移与客户端叫法现在同处一行生产代码，出处在同一注释块内指向 `Docs/ExperienceNoticeSlots.md` |
| RQ-009 | 成立 |

## Non-blocking Observations

- **NB-2（协调者点名要我独立判断的一条）——不升级，我同意保持顺序写。** `NamedLines` 确实仍不是
  写入端的运行期真源（全仓只有测试读它），但本轮之后我认为这不再是值得动的风险，理由有三：
  (a) 变异实验已经把「两份副本静默分叉」这个具体危害测量为封闭——M1/M7/M8 覆盖了双改、同删、单侧
  漏写三种分叉形状，全红；(b) 这一帧是定长定序的，`EncodeExperience` 的 24 次写入里只有 9 次是加成
  行，其余 15 次（等级、总数、四个技能点、`+0x11`、`+0x19`、`+0x2E`、荣誉两项，以及五个必须保持
  零的槽）无论如何都得顺序写，改成表驱动只会让同一个方法里出现两种写入风格，整块 83 字节的布局也
  不再能一列读完；(c) 偏移在写入端本就不是唯一被钉住的东西——`Line(index, kind)` 的 `index` 同时钉
  住探针槽号，表驱动反而会让这层对应关系变得隐晦。结论：保持现状，NB-2 从「值得考虑」降为「已解决」。
- **NB-6（本轮新增）** `MonsterDeathSlice.cs:221` 的注释仍写着 “every name in **NamedLineOffsets**
  was learned exactly this way”，而该成员本轮已更名为 `NamedLines`，是一处指向已不存在符号的引用。
  这个仓库把注释当承载物看待，改名时漏掉的引用会随时间烂掉。一词之改，不阻断。
- **NB-1（沿用）** `MonsterDeathSlice.cs:53/60/73/91`：`_amounts[(int)kind]` 依赖 `ExperienceBonusKind`
  底层值从 0 起连续，而容量取自 `Enum.GetValues<>().Length`；`None` 的正确性另依赖 `KindCount` 在
  文本上先于它声明。今天二者都成立，但若将来有人给成员显式赋值（例如图省事把偏移当枚举值）会静默
  越界。值得一行注释说明这个约束，或改用不依赖连续性的容量。
- **NB-3（沿用）** RQ-006 的后半句「其金额仍计入帧报出的总数」在编解码测试里没有断言（编解码器把
  总数当入参，本就断言不了）。该行为由 `CharacterExperienceGrant`（`gained` 累加全部 contributions，
  不看 `Kind`）保证，未被本改动触及，`CharacterExperienceGrantTests` 已有覆盖。可接受。
- **NB-4（沿用）** Spec 的 EV-004（一次实机击杀的通告截图，明细各项之和加基础值等于宣布的总数）
  尚未产出。按 TaskContract 的授权它本就留给用户验收，不在代码审查判据内，但在宣布本 Spec 完成前
  仍是待补证据。
- **NB-5（沿用）** `SeriaBlessing` 的枚举底层值由 1 变为 3；该枚举不参与任何持久化、配置或线上编码
  （全仓仅 `ExperienceBonusRules.cs:115/135` 两处硬编码产出），无迁移问题。

## 结论

BF-1 已实质修复：客户端叫法进入生产表使配对第一次成为可被机器比对的事实，文档 / 生产表 / 测试锚
三方字节级一致，我构造的九种变异中除「连判据一起改写」外全部变红——包括上一轮那个绿色通过的
转置场景。写入端与其余五个文件相对上一轮逐字未变，第一轮的功能面结论原样成立。构建 0 警告 0 错误，
全量 1506 通过 / 2 跳过 / 0 失败。

**verdict: approved。** 六条非阻断观察中 NB-6（一处过时符号引用）最好顺手带上，其余可留待后续成果。
本轮批准不替代完成验证：Spec 的 EV-004 实机截图仍需用户验收后补齐。
