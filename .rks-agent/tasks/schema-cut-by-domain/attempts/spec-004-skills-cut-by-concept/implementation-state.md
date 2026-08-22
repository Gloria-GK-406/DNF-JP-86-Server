# Implementation state — spec-004

六个实施任务已全部完成，全量套件全绿。库形状到达裁定给出的目标：**43 张表**。

基线：`1a77ef8a1de23ebb0d6ba283f44ca178b2496953`。未产生任何任务级提交。

## 已完成

| 任务 | 结果 | 证据 |
|---|---|---|
| T-001 四条保证的守卫先行 | 完成 | 新增 `Schema/SkillCarrierShapeTests.cs`（13 条：七列、旧表消失与两张职业专属页仍在、五条约束逐条、六列 `NOT NULL`）与 `Characters/SkillCarrierTests.cs` 的行为层守卫 |
| T-002 证明收紧无害 | 完成 | 见下「T-002 的结论」 |
| T-003 库形状改为目标形状 | 完成 | 两张表的声明换成一张七列表，四条保证逐条落位，`mode` 带 `CHECK (mode IN ('pve','pvp'))`。表数 44 → **43** |
| T-004 按表接口与读写路径 | 完成 | `ICharacterPvpSkillTable` 与 `CharacterPvpSkillRow` 退役，两者的方法并入 `ICharacterSkillTable` 并各自指名 mode；一张表一个类。全解决方案构建零错误零警告 |
| T-005 领域组装与播种序列 | 完成 | `PvpSkillProfileService`、`CharacterRosterService`、`CharacterEntryAssembly` 改到新接口；领域记录 `git diff` 为空；表征期望值一字未改 |
| T-006 行为保持证据 | 完成 | 见下 |

## T-002 的结论

`UNIQUE (character_id, page_index, skill_id)` 一直只在 PvP 那张表上声明，合并后按 `mode` 限定，因此**也开始约束 PvE 行**。裁定讨论了 `NOT NULL` 与级联外键的收紧，没有讨论这一条。今天写 PvE 技能行的唯一路径是建角色时的初始技能，它们全部落在第零页，若某个职业的初始主动技能里出现两次同一个 `skill_id`，改动后建那个职业的角色会失败——而且只在实机建那个职业时才显形。

用真实档案证明掉了：新增
`PvfInitialSkillDefinitionSourceTests.No_job_declares_the_same_active_skill_twice`，对职业 0..12 逐个取初始技能、只看主动的，断言无重复。跑通。这条断言从此**是那个约束的前置条件的守卫**，不是一次性的调查。

另补 `SkillCarrierTests.ACharacterCreatedHoldingSkillsKeepsOneRowEach`，在**改动库形状之前**先跑绿，确认这条写入路径本来就好使。

## 计划内的有界调整

- **T-004 第 3 步的空值语义统一带来一处收紧。** 两边读 `extra_values` 都把 NULL 读成空数组，这一点保持不变；但合并成一个读取器之后，PvE 那边原有的 1024 字节上限也开始作用于决斗页的行。这是「一列就是一列」的直接后果，与目标形状「取最严」的规则同向；今天没有任何写入接近这个上限。已在代码里写明理由。
- **`character_skills` 整张表退出可补列清单。** `mode` 是 `NOT NULL` 且无默认值，而补列机制无法给既有表加这样的列——这一列的全部意义正是「一行的 mode 无法从这行推出来」。旧形状的库不再被补成新形状，而是被替换；这与本阶段「擦库不迁移」的既定做法一致。已写进库形状声明的类注释。
- 两张职业专属页表的「非本职业持有即拒绝」此前**全仓无人守**（`inapplicable` 这个词在测试里一次都没出现过）。根契约的 SC-5 点名要这条证据，已补 `AJobSpecificPageHeldByAnotherJobIsRefused`（两例）。
- 两个 mode 的读取互不串行此前也无人守，且是这次合并最容易悄悄坏掉的一处。已补 `EachReadAnswersWithItsOwnModeAndNoneOfTheOther`——它是下面第一条可证伪性演示的对象。

## 一处记录而未改的观察

**级联外键在运行时是不生效的。** 全仓没有任何地方执行 `PRAGMA foreign_keys=ON`，而 SQLite 默认关闭外键。所以 `character_skills`（以及 spec-002 给 `character_pvp` 留下的那条）的 `ON DELETE CASCADE` 今天只是一条声明。本成果照裁定把声明搬了过来——搬运不能丢东西——并让 `ACharacterTakenAwayTakesBothSetsOfItsSkillsWithIt` 自己打开那个 pragma，因为那是分辨「声明还在」与「声明没了」的唯一办法。这是既有状况，不是本次引入的，也不属本契约范围。

## 行为保持证据

- **全量 1130 通过 / 2 跳过 / 0 失败。** 基线 1100/2/0。用例数 1102 → 1132，Δ+30 全部可解释：+1 档案重复守卫、+13 形状守卫、+16 行为守卫（6 条 `NOT NULL` + 6 条约束事实 + 1 条建角色 + 2 条职业专属页 + 1 条 mode 互不串行）。**无删除、无取代。**
- **整记录表征用例通过**，期望输出一字未改；其 diff 只有一行播种语句加上 `'pve'`。
- **领域记录未被触碰**：`CharacterModels.cs` 与 `ICharacterEntryStateServices.cs` 的 diff 为空。
- **可证伪性两次**：把 `ReadEntryAsync` 的 `mode='pve'` 去掉 → `EachReadAnswersWithItsOwnModeAndNoneOfTheOther` 立刻红；把 `CHECK (mode <> 'pvp' OR ...)` 从声明里去掉 → 形状守卫与行为守卫各红一条。两次还原后全绿，还原文件与备份逐字节相同。
- **一表一类**：清点 68 个类，零个指名超过一张表；43 张表声明。
- **构建零警告**。
- **逐条回读了本 plan 的每一个 Step**（T-006 第 4 步），六个任务二十余步全部有对应产物或证据；上一块的唯一 A 级缺陷正是漏做了计划里点名的一步，这一步是为它加的。

## 评审第一轮的整改

第一轮判 `changes_required`，一条 A 级，成立，已修。

**A-1：写入侧的 `mode` 一个字面量都没人守。** 合并把「哪一组页」从表名降级成一条语句里的字符串字面量，读的那一半我立了守卫，写的那两条没有。评审做了真改真跑：把普通插入的 `'pve'` 改成 `'pvp'`、把决斗插入的 `'pvp'` 改成 `'pve'`，全量两次都是 1130/2/0 全绿。更糟的是，我为写入路径新写的那条 `ACharacterCreatedHoldingSkillsKeepsOneRowEach` 声称守的正是这件事，而它的回读查询没带 `mode` 条件——所以它对写进哪一组页毫无判断力。同一空洞下删掉 `MarkSkillsInitializedAsync` 也全绿，SC-6 的证据实质为零。

**修法三处**：

1. 回读查询加上 `mode`，并让那条用例同时断言另一组页是空的。
2. 新增 `SeedingTheDuelPagesWritesThemOnceOnTheDuelSide`：用一个**非空**的 profile 走一次播种，断言两行落在决斗侧、普通侧为空、标记被置上；再用另一个不同的 profile 走第二次，断言什么都没变——这一条同时守住了决斗插入的字面量与那个标记。此前所有相关用例都用空 profile，空 profile 一行都不写，所以它们不可能抓到任何一条。
3. 顺带接受评审的一条 B 级：另外三条 `mode='pve'` 的语句（读等级、改等级、删除）同样无人守，而一个同页同槽的决斗行现在就在同一张表里。新增 `LevellingAnOrdinarySkillLeavesTheDuelRowAtTheSamePlaceAlone`，三条一并守住。

三处整改都验过可证伪：普通插入写错 mode → 红；决斗插入写错 mode → 红；标记不写 → 红。三次还原后全绿，还原文件与备份逐字节相同。

另接受评审的一条 B 级并改了裁定：`character_skills` 那一节原本只说四条保证「carried over」，没有写明其中三条现在也开始约束普通行，也没有写明 `UNIQUE` 这一条依赖的是档案里的一个事实。已补写，并指明那个事实钉在哪条断言上。

全量重跑：**1132 通过 / 2 跳过 / 0 失败**（较整改前 +2，正是新增的两条）。

## 当前状态

工作区 13 个已跟踪文件改动加两个新测试文件（`SkillCarrierShapeTests.cs`、`SkillCarrierTests.cs`）与本 attempt 的过程工件，全量全绿，未产生任务级提交。第一轮的一条 A 级已修，待第二轮确认。
